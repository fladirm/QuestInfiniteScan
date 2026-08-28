using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Genesis.RoomScan
{
    public readonly struct MerkabaKernelSnapshot
    {
        public readonly int3 Coord;
        public readonly KernelState State;

        public MerkabaKernelSnapshot(int3 coord, KernelState state)
        {
            Coord = coord;
            State = state;
        }
    }

    /// <summary>
    /// The sole reconstruction authority: signed infinite lattice coordinates backed by
    /// dense allocated chunks of minimal canonical kernel state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed partial class MerkabaGrid : MonoBehaviour
    {
        public static MerkabaGrid Instance { get; private set; }

        private readonly Dictionary<int3, MerkabaChunk> _chunks = new();

        public int ActiveChunkCount => _chunks.Count;
        public int OccupiedKernelCount { get; private set; }
        public IReadOnlyDictionary<int3, MerkabaChunk> Chunks => _chunks;

        public event Action<int3, bool> OccupancyChanged;
        public event Action Cleared;

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            ReleaseGpuResources();
            if (Instance == this) Instance = null;
        }

        public MerkabaChunk GetOrCreateChunk(int3 chunkCoord)
        {
            if (_chunks.TryGetValue(chunkCoord, out MerkabaChunk chunk)) return chunk;
            chunk = new MerkabaChunk(chunkCoord);
            _chunks.Add(chunkCoord, chunk);
            return chunk;
        }

        public bool TryGetChunk(int3 chunkCoord, out MerkabaChunk chunk) =>
            _chunks.TryGetValue(chunkCoord, out chunk);

        public bool TryGetState(int3 globalCoord, out KernelState state)
        {
            int3 chunkCoord = MerkabaConstants.ChunkCoord(globalCoord);
            if (!_chunks.TryGetValue(chunkCoord, out MerkabaChunk chunk))
            {
                state = default;
                return false;
            }
            state = chunk.States[MerkabaConstants.Flatten(
                MerkabaConstants.LocalCoord(globalCoord))];
            return true;
        }

        public bool IsOccupied(int3 globalCoord) =>
            TryGetState(globalCoord, out KernelState state) && state.IsOccupied;

        public void SetState(int3 globalCoord, KernelState state)
        {
            int3 chunkCoord = MerkabaConstants.ChunkCoord(globalCoord);
            MerkabaChunk chunk = GetOrCreateChunk(chunkCoord);
            int index = MerkabaConstants.Flatten(MerkabaConstants.LocalCoord(globalCoord));
            bool before = chunk.States[index].IsOccupied;
            bool after = state.IsOccupied;
            chunk.States[index] = state;
            chunk.CpuStateCurrent = true;
            chunk.Persisted = false;
            if (before == after) return;
            int delta = after ? 1 : -1;
            chunk.OccupiedCount += delta;
            OccupiedKernelCount += delta;
            OccupancyChanged?.Invoke(globalCoord, after);
        }

        public MerkabaObservationResult ApplyObservation(int3 globalCoord,
            in MerkabaObservationInput input, Color32 color)
        {
            MerkabaObservationResult result = MerkabaObservation.Classify(input);
            if (result.Kind == MerkabaObservationKind.Unknown) return result;

            int3 chunkCoord = MerkabaConstants.ChunkCoord(globalCoord);
            if (!_chunks.TryGetValue(chunkCoord, out MerkabaChunk chunk))
            {
                // Observed free untouched space carries no canonical allocation.
                if (result.Kind == MerkabaObservationKind.Free) return result;
                chunk = GetOrCreateChunk(chunkCoord);
            }

            int index = MerkabaConstants.Flatten(MerkabaConstants.LocalCoord(globalCoord));
            ref KernelState state = ref chunk.States[index];
            bool transition = MerkabaIntegrator.IntegrateClassified(
                ref state, result.Kind, result.Quality, color);
            chunk.CpuStateCurrent = true;
            chunk.Persisted = false;
            if (transition)
            {
                int delta = state.IsOccupied ? 1 : -1;
                chunk.OccupiedCount += delta;
                OccupiedKernelCount += delta;
                OccupancyChanged?.Invoke(globalCoord, state.IsOccupied);
            }
            return result;
        }

        public IEnumerable<MerkabaChunk> ChunksSorted()
        {
            var coords = new List<int3>(_chunks.Keys);
            coords.Sort(CompareCoords);
            foreach (int3 coord in coords) yield return _chunks[coord];
        }

        public IEnumerable<MerkabaKernelSnapshot> OccupiedKernelsSorted()
        {
            foreach (MerkabaChunk chunk in ChunksSorted())
            {
                int3 origin = MerkabaConstants.ChunkOrigin(chunk.Coord);
                for (int index = 0; index < chunk.States.Length; index++)
                {
                    KernelState state = chunk.States[index];
                    if (state.IsOccupied)
                        yield return new MerkabaKernelSnapshot(
                            origin + MerkabaConstants.Unflatten(index), state);
                }
            }
        }

        public void RecountOccupied()
        {
            OccupiedKernelCount = 0;
            foreach (MerkabaChunk chunk in _chunks.Values)
            {
                int count = 0;
                foreach (KernelState state in chunk.States)
                    if (state.IsOccupied) count++;
                chunk.OccupiedCount = count;
                OccupiedKernelCount += count;
            }
        }

        public void Clear()
        {
            ClearGpuResidencyWithoutReadback();
            _chunks.Clear();
            OccupiedKernelCount = 0;
            Cleared?.Invoke();
        }

        private static int CompareCoords(int3 left, int3 right)
        {
            if (left.x != right.x) return left.x.CompareTo(right.x);
            if (left.y != right.y) return left.y.CompareTo(right.y);
            return left.z.CompareTo(right.z);
        }
    }
}
