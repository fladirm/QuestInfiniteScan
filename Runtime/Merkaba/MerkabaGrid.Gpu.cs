using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan
{
    internal readonly struct MerkabaResidencyFrame
    {
        public readonly int IntegrationChunkCount;
        public readonly int VisibleChunkCount;

        public MerkabaResidencyFrame(int integrationChunkCount, int visibleChunkCount)
        {
            IntegrationChunkCount = integrationChunkCount;
            VisibleChunkCount = visibleChunkCount;
        }
    }

    public sealed partial class MerkabaGrid
    {
        [Header("GPU Residency")]
        [SerializeField, Range(16, 192)] private int maxResidentChunks = 96;
        [SerializeField, Range(8, 96)] private int maxIntegrationChunks = 48;
        [SerializeField, Range(8, 128)] private int maxVisibleChunks = 64;

        private sealed class ResidentPage
        {
            public readonly MerkabaChunk Chunk;
            public readonly int Slot;
            public int LastTouchedFrame;
            public bool PendingEviction;

            public ResidentPage(MerkabaChunk chunk, int slot, int frame)
            {
                Chunk = chunk;
                Slot = slot;
                LastTouchedFrame = frame;
            }
        }

        private readonly struct ChunkCandidate
        {
            public readonly int3 Coord;
            public readonly float DistanceSquared;

            public ChunkCandidate(int3 coord, float distanceSquared)
            {
                Coord = coord;
                DistanceSquared = distanceSquared;
            }
        }

        private readonly Dictionary<int3, ResidentPage> _resident = new();
        private readonly SortedSet<int> _freeSlots = new();
        private readonly HashSet<int3> _desiredCoords = new();
        private ResidentPage[] _slots;
        private int _gpuGeneration;
        private bool _gpuReady;

        private ComputeBuffer _kernelBuffer;
        private ComputeBuffer _pageCoordsBuffer;
        private ComputeBuffer _pageNeighboursBuffer;
        private ComputeBuffer _kernelDirtyBuffer;
        private ComputeBuffer _topologyMaskBuffer;
        private ComputeBuffer _integrationSlotsBuffer;
        private ComputeBuffer _visibleSlotsBuffer;

        private int4[] _pageCoordsCpu;
        private int[] _pageNeighboursCpu;
        private int[] _integrationSlotsCpu;
        private int[] _visibleSlotsCpu;
        private uint[] _dirtyOnes;
        private uint[] _zeroMasks;

        internal ComputeBuffer KernelBuffer => _kernelBuffer;
        internal ComputeBuffer PageCoordsBuffer => _pageCoordsBuffer;
        internal ComputeBuffer PageNeighboursBuffer => _pageNeighboursBuffer;
        internal ComputeBuffer KernelDirtyBuffer => _kernelDirtyBuffer;
        internal ComputeBuffer TopologyMaskBuffer => _topologyMaskBuffer;
        internal ComputeBuffer IntegrationSlotsBuffer => _integrationSlotsBuffer;
        internal ComputeBuffer VisibleSlotsBuffer => _visibleSlotsBuffer;
        internal int IntegrationChunkCount { get; private set; }
        internal int VisibleChunkCount { get; private set; }
        internal int MaxVisibleChunks => maxVisibleChunks;
        internal Matrix4x4 GridToWorldMatrix => transform.localToWorldMatrix;
        internal bool GpuReady => _gpuReady;

        internal void EnsureGpuResources()
        {
            if (_gpuReady) return;
            maxIntegrationChunks = Mathf.Min(maxIntegrationChunks, maxResidentChunks);
            maxVisibleChunks = Mathf.Min(maxVisibleChunks, maxResidentChunks);
            int totalKernels = checked(maxResidentChunks * MerkabaConstants.KernelsPerChunk);
            int stateStride = Marshal.SizeOf<KernelState>();
            if (stateStride != 16)
                throw new InvalidOperationException(
                    $"KernelState GPU ABI must be 16 bytes, got {stateStride}.");

            _kernelBuffer = new ComputeBuffer(totalKernels, stateStride,
                ComputeBufferType.Structured);
            _pageCoordsBuffer = new ComputeBuffer(maxResidentChunks, sizeof(int) * 4,
                ComputeBufferType.Structured);
            _pageNeighboursBuffer = new ComputeBuffer(maxResidentChunks * 27, sizeof(int),
                ComputeBufferType.Structured);
            _kernelDirtyBuffer = new ComputeBuffer(totalKernels, sizeof(uint),
                ComputeBufferType.Structured);
            _topologyMaskBuffer = new ComputeBuffer(totalKernels, sizeof(uint),
                ComputeBufferType.Structured);
            _integrationSlotsBuffer = new ComputeBuffer(maxIntegrationChunks, sizeof(int),
                ComputeBufferType.Structured);
            _visibleSlotsBuffer = new ComputeBuffer(maxVisibleChunks, sizeof(int),
                ComputeBufferType.Structured);

            _slots = new ResidentPage[maxResidentChunks];
            _pageCoordsCpu = new int4[maxResidentChunks];
            _pageNeighboursCpu = new int[maxResidentChunks * 27];
            _integrationSlotsCpu = new int[maxIntegrationChunks];
            _visibleSlotsCpu = new int[maxVisibleChunks];
            _dirtyOnes = new uint[MerkabaConstants.KernelsPerChunk];
            _zeroMasks = new uint[MerkabaConstants.KernelsPerChunk];
            Array.Fill(_dirtyOnes, 1u);
            Array.Fill(_pageNeighboursCpu, -1);
            for (int slot = 0; slot < maxResidentChunks; slot++)
            {
                _freeSlots.Add(slot);
                _pageCoordsCpu[slot] = new int4(0, 0, 0, -1);
            }
            _pageCoordsBuffer.SetData(_pageCoordsCpu);
            _pageNeighboursBuffer.SetData(_pageNeighboursCpu);
            _gpuReady = true;
        }

        /// <summary>
        /// Builds a bounded current-frustum working set. Candidate work depends only on
        /// the present camera/frustum and resident cap, never on all historical chunks.
        /// </summary>
        internal MerkabaResidencyFrame RefreshResidency(Camera camera, float maxDistance,
            bool allocateForIntegration)
        {
            EnsureGpuResources();
            if (camera == null || maxDistance <= 0f)
            {
                SetFrameSlots(Array.Empty<int>(), Array.Empty<int>());
                return default;
            }

            List<ChunkCandidate> candidates = CollectFrustumCandidates(camera, maxDistance);
            _desiredCoords.Clear();
            int desiredLimit = Mathf.Min(candidates.Count,
                Mathf.Max(maxIntegrationChunks, maxVisibleChunks));
            for (int i = 0; i < desiredLimit; i++)
                _desiredCoords.Add(candidates[i].Coord);

            var changed = new List<int3>();
            var integration = new List<int>(maxIntegrationChunks);
            var visible = new List<int>(maxVisibleChunks);

            for (int i = 0; i < candidates.Count &&
                 (integration.Count < maxIntegrationChunks || visible.Count < maxVisibleChunks); i++)
            {
                int3 coord = candidates[i].Coord;
                bool chunkExists = _chunks.ContainsKey(coord);
                bool mayAllocate = allocateForIntegration && integration.Count < maxIntegrationChunks;
                if (!chunkExists && !mayAllocate) continue;

                ResidentPage page = EnsureResident(coord, mayAllocate, changed);
                if (page == null || page.PendingEviction) continue;
                page.LastTouchedFrame = Time.frameCount;
                if (visible.Count < maxVisibleChunks) visible.Add(page.Slot);
                if (allocateForIntegration && integration.Count < maxIntegrationChunks)
                    integration.Add(page.Slot);
            }

            if (changed.Count > 0) RebuildPageTablesAndDirtyLocal(changed);
            SetFrameSlots(integration, visible);

            int missing = allocateForIntegration
                ? Mathf.Min(candidates.Count, maxIntegrationChunks) - integration.Count
                : 0;
            while (missing-- > 0 && ScheduleOneEviction()) { }

            return new MerkabaResidencyFrame(IntegrationChunkCount, VisibleChunkCount);
        }

        internal void MarkIntegrationPagesGpuCurrent()
        {
            for (int i = 0; i < IntegrationChunkCount; i++)
            {
                ResidentPage page = _slots[_integrationSlotsCpu[i]];
                if (page != null && !page.PendingEviction)
                {
                    page.Chunk.CpuStateCurrent = false;
                    page.Chunk.Persisted = false;
                }
            }
        }

        internal Task SynchronizeResidentStateAsync()
        {
            if (!_gpuReady || _resident.Count == 0) return Task.CompletedTask;
            var completion = new TaskCompletionSource<bool>();
            int generation = _gpuGeneration;
            AsyncGPUReadback.Request(_kernelBuffer, request =>
            {
                if (request.hasError)
                {
                    completion.TrySetException(new IOException(
                        "GPU readback failed while saving the Merkaba grid."));
                    return;
                }
                if (generation != _gpuGeneration)
                {
                    completion.TrySetCanceled();
                    return;
                }

                NativeArray<KernelState> data = request.GetData<KernelState>();
                foreach (ResidentPage page in _resident.Values)
                    CopyPageSnapshot(page, data, page.Slot * MerkabaConstants.KernelsPerChunk);
                completion.TrySetResult(true);
            });
            return completion.Task;
        }

        private ResidentPage EnsureResident(int3 coord, bool createIfMissing,
            List<int3> changed)
        {
            if (_resident.TryGetValue(coord, out ResidentPage existing)) return existing;
            if (!_chunks.TryGetValue(coord, out MerkabaChunk chunk))
            {
                if (!createIfMissing) return null;
                chunk = GetOrCreateChunk(coord);
            }
            if (_freeSlots.Count == 0) return null;

            int slot = _freeSlots.Min;
            _freeSlots.Remove(slot);
            var page = new ResidentPage(chunk, slot, Time.frameCount);
            _resident.Add(coord, page);
            _slots[slot] = page;

            int offset = slot * MerkabaConstants.KernelsPerChunk;
            _kernelBuffer.SetData(chunk.States, 0, offset, chunk.States.Length);
            _kernelDirtyBuffer.SetData(_dirtyOnes, 0, offset, _dirtyOnes.Length);
            _topologyMaskBuffer.SetData(_zeroMasks, 0, offset, _zeroMasks.Length);
            changed.Add(coord);
            return page;
        }

        private bool ScheduleOneEviction()
        {
            ResidentPage victim = null;
            foreach (ResidentPage candidate in _resident.Values)
            {
                if (candidate.PendingEviction || _desiredCoords.Contains(candidate.Chunk.Coord))
                    continue;
                if (victim == null || candidate.LastTouchedFrame < victim.LastTouchedFrame ||
                    (candidate.LastTouchedFrame == victim.LastTouchedFrame &&
                     candidate.Slot < victim.Slot))
                    victim = candidate;
            }
            if (victim == null) return false;

            victim.PendingEviction = true;
            RebuildPageTablesAndDirtyLocal(new List<int3> { victim.Chunk.Coord });
            int generation = _gpuGeneration;
            int byteSize = MerkabaConstants.KernelsPerChunk * Marshal.SizeOf<KernelState>();
            int byteOffset = victim.Slot * byteSize;
            AsyncGPUReadback.Request(_kernelBuffer, byteSize, byteOffset, request =>
            {
                if (generation != _gpuGeneration) return;
                if (request.hasError)
                {
                    victim.PendingEviction = false;
                    Logger.Error($"MerkabaGrid: eviction readback failed for {victim.Chunk.Coord}");
                    return;
                }
                CopyPageSnapshot(victim, request.GetData<KernelState>(), 0);
                _resident.Remove(victim.Chunk.Coord);
                _slots[victim.Slot] = null;
                _freeSlots.Add(victim.Slot);
                RebuildPageTablesAndDirtyLocal(new List<int3> { victim.Chunk.Coord });
            });
            return true;
        }

        private void CopyPageSnapshot(ResidentPage page, NativeArray<KernelState> data,
            int sourceOffset)
        {
            int occupied = 0;
            KernelState[] destination = page.Chunk.States;
            for (int i = 0; i < destination.Length; i++)
            {
                KernelState state = data[sourceOffset + i];
                destination[i] = state;
                if (state.IsOccupied) occupied++;
            }
            OccupiedKernelCount += occupied - page.Chunk.OccupiedCount;
            page.Chunk.OccupiedCount = occupied;
            page.Chunk.CpuStateCurrent = true;
            page.Chunk.Persisted = false;
        }

        private List<ChunkCandidate> CollectFrustumCandidates(Camera camera, float maxDistance)
        {
            Vector3 localCamera = transform.InverseTransformPoint(camera.transform.position);
            float chunkSpan = MerkabaConstants.ChunkSize * MerkabaConstants.LatticeStep;
            int3 cameraChunk = new(
                Mathf.FloorToInt(localCamera.x / chunkSpan),
                Mathf.FloorToInt(localCamera.y / chunkSpan),
                Mathf.FloorToInt(localCamera.z / chunkSpan));
            int radius = Mathf.CeilToInt(maxDistance / chunkSpan) + 1;
            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(camera);
            var result = new List<ChunkCandidate>(512);
            float maxDistanceSq = maxDistance * maxDistance;

            for (int z = -radius; z <= radius; z++)
            for (int y = -radius; y <= radius; y++)
            for (int x = -radius; x <= radius; x++)
            {
                int3 coord = cameraChunk + new int3(x, y, z);
                Bounds worldBounds = ChunkWorldBounds(coord);
                float distanceSq = worldBounds.SqrDistance(camera.transform.position);
                if (distanceSq > maxDistanceSq ||
                    !GeometryUtility.TestPlanesAABB(planes, worldBounds))
                    continue;
                result.Add(new ChunkCandidate(coord, distanceSq));
            }

            result.Sort((left, right) =>
            {
                int distance = left.DistanceSquared.CompareTo(right.DistanceSquared);
                if (distance != 0) return distance;
                if (left.Coord.x != right.Coord.x)
                    return left.Coord.x.CompareTo(right.Coord.x);
                if (left.Coord.y != right.Coord.y)
                    return left.Coord.y.CompareTo(right.Coord.y);
                return left.Coord.z.CompareTo(right.Coord.z);
            });
            return result;
        }

        private Bounds ChunkWorldBounds(int3 coord)
        {
            int3 origin = MerkabaConstants.ChunkOrigin(coord);
            float3 localCenter = ((float3)origin +
                (MerkabaConstants.ChunkSize - 1) * 0.5f) * MerkabaConstants.LatticeStep;
            float localSize = MerkabaConstants.ChunkSize * MerkabaConstants.LatticeStep +
                              MerkabaConstants.HalfSupport;
            Matrix4x4 matrix = transform.localToWorldMatrix;
            Vector3 worldCenter = matrix.MultiplyPoint3x4((Vector3)localCenter);
            Vector3 localExtents = Vector3.one * (localSize * 0.5f);
            Vector3 worldExtents = new(
                Mathf.Abs(matrix.m00) * localExtents.x + Mathf.Abs(matrix.m01) * localExtents.y +
                Mathf.Abs(matrix.m02) * localExtents.z,
                Mathf.Abs(matrix.m10) * localExtents.x + Mathf.Abs(matrix.m11) * localExtents.y +
                Mathf.Abs(matrix.m12) * localExtents.z,
                Mathf.Abs(matrix.m20) * localExtents.x + Mathf.Abs(matrix.m21) * localExtents.y +
                Mathf.Abs(matrix.m22) * localExtents.z);
            return new Bounds(worldCenter, worldExtents * 2f);
        }

        private void SetFrameSlots(IReadOnlyList<int> integration, IReadOnlyList<int> visible)
        {
            IntegrationChunkCount = Mathf.Min(integration.Count, maxIntegrationChunks);
            VisibleChunkCount = Mathf.Min(visible.Count, maxVisibleChunks);
            Array.Fill(_integrationSlotsCpu, 0);
            Array.Fill(_visibleSlotsCpu, 0);
            for (int i = 0; i < IntegrationChunkCount; i++)
                _integrationSlotsCpu[i] = integration[i];
            for (int i = 0; i < VisibleChunkCount; i++)
                _visibleSlotsCpu[i] = visible[i];
            _integrationSlotsBuffer.SetData(_integrationSlotsCpu);
            _visibleSlotsBuffer.SetData(_visibleSlotsCpu);
        }

        private void RebuildPageTablesAndDirtyLocal(IReadOnlyList<int3> changedCoords)
        {
            if (!_gpuReady) return;
            Array.Fill(_pageNeighboursCpu, -1);
            for (int slot = 0; slot < maxResidentChunks; slot++)
                _pageCoordsCpu[slot] = new int4(0, 0, 0, -1);

            foreach (ResidentPage page in _resident.Values)
            {
                if (page.PendingEviction) continue;
                int3 coord = page.Chunk.Coord;
                _pageCoordsCpu[page.Slot] = new int4(coord, page.Slot);
                for (int dz = -1; dz <= 1; dz++)
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int neighbourIndex = (dx + 1) + 3 * (dy + 1) + 9 * (dz + 1);
                    int3 neighbourCoord = coord + new int3(dx, dy, dz);
                    if (_resident.TryGetValue(neighbourCoord, out ResidentPage neighbour) &&
                        !neighbour.PendingEviction)
                        _pageNeighboursCpu[page.Slot * 27 + neighbourIndex] = neighbour.Slot;
                }
            }
            _pageCoordsBuffer.SetData(_pageCoordsCpu);
            _pageNeighboursBuffer.SetData(_pageNeighboursCpu);

            var dirtySlots = new HashSet<int>();
            foreach (int3 changed in changedCoords)
            {
                if (_resident.TryGetValue(changed, out ResidentPage self) && !self.PendingEviction)
                    dirtySlots.Add(self.Slot);
                foreach (int3 offset in MerkabaConstants.Neighbours)
                    if (_resident.TryGetValue(changed + offset, out ResidentPage neighbour) &&
                        !neighbour.PendingEviction)
                        dirtySlots.Add(neighbour.Slot);
            }
            foreach (int slot in dirtySlots)
                _kernelDirtyBuffer.SetData(_dirtyOnes, 0,
                    slot * MerkabaConstants.KernelsPerChunk, _dirtyOnes.Length);
        }

        private void ClearGpuResidencyWithoutReadback()
        {
            if (!_gpuReady) return;
            _gpuGeneration++;
            _resident.Clear();
            _freeSlots.Clear();
            Array.Clear(_slots, 0, _slots.Length);
            Array.Fill(_pageNeighboursCpu, -1);
            for (int slot = 0; slot < maxResidentChunks; slot++)
            {
                _freeSlots.Add(slot);
                _pageCoordsCpu[slot] = new int4(0, 0, 0, -1);
            }
            _pageCoordsBuffer.SetData(_pageCoordsCpu);
            _pageNeighboursBuffer.SetData(_pageNeighboursCpu);
            SetFrameSlots(Array.Empty<int>(), Array.Empty<int>());
        }

        private void ReleaseGpuResources()
        {
            _gpuGeneration++;
            _kernelBuffer?.Release();
            _pageCoordsBuffer?.Release();
            _pageNeighboursBuffer?.Release();
            _kernelDirtyBuffer?.Release();
            _topologyMaskBuffer?.Release();
            _integrationSlotsBuffer?.Release();
            _visibleSlotsBuffer?.Release();
            _kernelBuffer = null;
            _pageCoordsBuffer = null;
            _pageNeighboursBuffer = null;
            _kernelDirtyBuffer = null;
            _topologyMaskBuffer = null;
            _integrationSlotsBuffer = null;
            _visibleSlotsBuffer = null;
            _gpuReady = false;
        }
    }
}
