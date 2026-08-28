using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Genesis.RoomScan
{
    internal sealed class MerkabaExportShellResult
    {
        public readonly List<MerkabaKernelSnapshot> Kernels;
        public readonly int3[] HealedCoordinates;
        public readonly int3[] ShellCoordinates;
        public readonly int OriginalOccupiedCount;
        public readonly int StrongKnownFreeCount;
        public readonly int SyntheticKernelCount;

        public MerkabaExportShellResult(List<MerkabaKernelSnapshot> kernels,
            int3[] healedCoordinates, int3[] shellCoordinates,
            int originalOccupiedCount, int strongKnownFreeCount,
            int syntheticKernelCount)
        {
            Kernels = kernels;
            HealedCoordinates = healedCoordinates;
            ShellCoordinates = shellCoordinates;
            OriginalOccupiedCount = originalOccupiedCount;
            StrongKnownFreeCount = strongKnownFreeCount;
            SyntheticKernelCount = syntheticKernelCount;
        }
    }

    /// <summary>
    /// Read-only, sparse export cleanup. It performs one radius-1 binary closing,
    /// vetoes strong FREE evidence, selects the observed-free frontier per component,
    /// and returns only lattice coordinates/colours. Triangle generation remains the
    /// shared canonical Merkaba writer's responsibility.
    /// </summary>
    internal static class MerkabaExportShell
    {
        private static readonly int3[] ClosingOffsets = BuildClosingOffsets();
        private static readonly int3[] AxisOffsets =
        {
            new(-1, 0, 0), new(1, 0, 0),
            new(0, -1, 0), new(0, 1, 0),
            new(0, 0, -1), new(0, 0, 1)
        };

        internal static bool IsStrongKnownFree(in KernelState state) =>
            !state.IsOccupied && state.OccupancyEvidence <=
            MerkabaConstants.ExportKnownFreeThreshold;

        internal static MerkabaExportShellResult Build(
            MerkabaSessionSnapshot snapshot,
            IProgress<ExportShellProgress> progress = null)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            ReportBuildingEvidence(progress);
            var occupied = new HashSet<int3>();
            var strongFree = new HashSet<int3>();
            var realStates = new Dictionary<int3, KernelState>();
            foreach (MerkabaChunkSnapshot chunk in snapshot.Chunks)
            {
                if (chunk?.States == null ||
                    chunk.States.Length != MerkabaConstants.KernelsPerChunk)
                    throw new InvalidOperationException(
                        "Export snapshot contains an invalid chunk payload.");
                int3 origin = MerkabaConstants.ChunkOrigin(chunk.Coord);
                for (int index = 0; index < chunk.States.Length; index++)
                {
                    KernelState state = chunk.States[index];
                    AddEvidence(origin + MerkabaConstants.Unflatten(index), state,
                        occupied, strongFree, realStates);
                }
            }
            return Build(occupied, strongFree, realStates, progress);
        }

        internal static MerkabaExportShellResult Build(
            IReadOnlyDictionary<int3, KernelState> evidence,
            IProgress<ExportShellProgress> progress = null)
        {
            if (evidence == null) throw new ArgumentNullException(nameof(evidence));
            ReportBuildingEvidence(progress);
            var occupied = new HashSet<int3>();
            var strongFree = new HashSet<int3>();
            var realStates = new Dictionary<int3, KernelState>();
            foreach (KeyValuePair<int3, KernelState> pair in evidence)
                AddEvidence(pair.Key, pair.Value, occupied, strongFree, realStates);
            return Build(occupied, strongFree, realStates, progress);
        }

        private static MerkabaExportShellResult Build(HashSet<int3> occupied,
            HashSet<int3> strongFree,
            Dictionary<int3, KernelState> realStates,
            IProgress<ExportShellProgress> progress)
        {
            if (occupied.Count == 0)
                throw new InvalidOperationException(
                    "The Merkaba grid has no occupied kernels.");

            progress?.Report(new ExportShellProgress(
                ScanOperationStage.HealingTinyHoles, 0.38f,
                "Healing tiny unknown holes"));
            HashSet<int3> healed = CloseOnce(occupied, strongFree);

            progress?.Report(new ExportShellProgress(
                ScanOperationStage.ExtractingMerkabaShell, 0.62f,
                "Extracting observed Merkaba shell"));
            HashSet<int3> shell = SelectShell(healed, strongFree);
            if (shell.Count == 0)
                throw new InvalidOperationException(
                    "Evidence-aware export shell contains no kernels.");

            var sortedShell = new List<int3>(shell);
            sortedShell.Sort(CompareCoords);
            var kernels = new List<MerkabaKernelSnapshot>(sortedShell.Count);
            int syntheticSelected = 0;
            foreach (int3 coord in sortedShell)
            {
                KernelState state;
                if (realStates.TryGetValue(coord, out KernelState real))
                {
                    state = WithExportFallbackColor(real);
                }
                else
                {
                    state = SyntheticState(coord, realStates);
                    syntheticSelected++;
                }
                kernels.Add(new MerkabaKernelSnapshot(coord, state));
            }

            int3[] healedSorted = Sorted(healed);
            return new MerkabaExportShellResult(kernels, healedSorted,
                sortedShell.ToArray(), occupied.Count, strongFree.Count,
                syntheticSelected);
        }

        private static HashSet<int3> CloseOnce(HashSet<int3> occupied,
            HashSet<int3> strongFree)
        {
            var dilated = new HashSet<int3>();
            foreach (int3 coord in occupied)
            foreach (int3 offset in ClosingOffsets)
                dilated.Add(coord + offset);

            var closed = new HashSet<int3>();
            foreach (int3 candidate in dilated)
            {
                bool retained = true;
                foreach (int3 offset in ClosingOffsets)
                {
                    if (dilated.Contains(candidate + offset)) continue;
                    retained = false;
                    break;
                }
                if (retained) closed.Add(candidate);
            }

            var healed = new HashSet<int3>(occupied);
            foreach (int3 candidate in closed)
            {
                if (occupied.Contains(candidate) || strongFree.Contains(candidate))
                    continue;
                healed.Add(candidate);
            }
            return healed;
        }

        private static void ReportBuildingEvidence(
            IProgress<ExportShellProgress> progress) =>
            progress?.Report(new ExportShellProgress(
                ScanOperationStage.BuildingExportEvidence, 0.2f,
                "Building export evidence"));

        private static void AddEvidence(int3 coord, KernelState state,
            HashSet<int3> occupied, HashSet<int3> strongFree,
            Dictionary<int3, KernelState> realStates)
        {
            if (state.IsOccupied)
            {
                if (!occupied.Add(coord))
                    throw new InvalidOperationException(
                        $"Duplicate occupied export coordinate {coord}.");
                realStates.Add(coord, state);
                return;
            }
            if (IsStrongKnownFree(state) && !strongFree.Add(coord))
                throw new InvalidOperationException(
                    $"Duplicate FREE export coordinate {coord}.");
        }

        private static HashSet<int3> SelectShell(HashSet<int3> healed,
            HashSet<int3> strongFree)
        {
            var selected = new HashSet<int3>();
            var unvisited = new HashSet<int3>(healed);
            int3[] seeds = Sorted(healed);
            var queue = new Queue<int3>();
            var component = new List<int3>();

            foreach (int3 seed in seeds)
            {
                if (!unvisited.Remove(seed)) continue;
                component.Clear();
                queue.Enqueue(seed);
                while (queue.Count > 0)
                {
                    int3 coord = queue.Dequeue();
                    component.Add(coord);
                    foreach (int3 offset in MerkabaConstants.Neighbours)
                    {
                        int3 neighbour = coord + offset;
                        if (unvisited.Remove(neighbour)) queue.Enqueue(neighbour);
                    }
                }

                bool hasObservedFreeContact = false;
                foreach (int3 coord in component)
                {
                    if (!Touches(coord, strongFree)) continue;
                    hasObservedFreeContact = true;
                    break;
                }

                foreach (int3 coord in component)
                {
                    bool retain = hasObservedFreeContact
                        ? Touches(coord, strongFree)
                        : TouchesOutside(coord, healed);
                    if (retain) selected.Add(coord);
                }
            }
            return selected;
        }

        private static bool Touches(int3 coord, HashSet<int3> set)
        {
            foreach (int3 offset in MerkabaConstants.Neighbours)
                if (set.Contains(coord + offset)) return true;
            return false;
        }

        private static bool TouchesOutside(int3 coord, HashSet<int3> healed)
        {
            foreach (int3 offset in MerkabaConstants.Neighbours)
                if (!healed.Contains(coord + offset)) return true;
            return false;
        }

        private static KernelState SyntheticState(int3 coord,
            IReadOnlyDictionary<int3, KernelState> realStates)
        {
            if (!TryWeightedColor(coord, AxisOffsets, realStates,
                    out Color32 color, out uint confidence) &&
                !TryWeightedColor(coord, MerkabaConstants.Neighbours,
                    realStates, out color, out confidence))
            {
                color = KernelState.UnpackColor(MerkabaConstants.NeutralPackedColor);
                confidence = 1u;
            }
            return new KernelState
            {
                OccupancyEvidence = MerkabaConstants.OccupiedOnThreshold,
                PackedColor = KernelState.PackColor(color),
                ColorConfidence = confidence,
                Flags = MerkabaConstants.OccupiedFlag
            };
        }

        private static bool TryWeightedColor(int3 coord,
            ReadOnlySpan<int3> offsets,
            IReadOnlyDictionary<int3, KernelState> realStates,
            out Color32 color, out uint confidence)
        {
            ulong red = 0, green = 0, blue = 0, weightTotal = 0;
            foreach (int3 offset in offsets)
            {
                if (!realStates.TryGetValue(coord + offset, out KernelState state) ||
                    !state.IsOccupied || state.ColorConfidence == 0u)
                    continue;
                uint weight = math.min(state.ColorConfidence,
                    (uint)MerkabaConstants.MaximumColorConfidence);
                Color32 sample = state.Color;
                red += (ulong)sample.r * weight;
                green += (ulong)sample.g * weight;
                blue += (ulong)sample.b * weight;
                weightTotal += weight;
            }
            if (weightTotal == 0)
            {
                color = default;
                confidence = 0u;
                return false;
            }
            color = new Color32((byte)((red + weightTotal / 2) / weightTotal),
                (byte)((green + weightTotal / 2) / weightTotal),
                (byte)((blue + weightTotal / 2) / weightTotal), 255);
            confidence = (uint)Math.Min(weightTotal,
                (ulong)MerkabaConstants.MaximumColorConfidence);
            return true;
        }

        private static KernelState WithExportFallbackColor(KernelState state)
        {
            if (state.ColorConfidence != 0u) return state;
            state.PackedColor = MerkabaConstants.NeutralPackedColor;
            state.ColorConfidence = 1u;
            return state;
        }

        private static int3[] Sorted(HashSet<int3> values)
        {
            var sorted = new List<int3>(values);
            sorted.Sort(CompareCoords);
            return sorted.ToArray();
        }

        private static int CompareCoords(int3 left, int3 right)
        {
            if (left.x != right.x) return left.x.CompareTo(right.x);
            if (left.y != right.y) return left.y.CompareTo(right.y);
            return left.z.CompareTo(right.z);
        }

        private static int3[] BuildClosingOffsets()
        {
            var offsets = new int3[27];
            int index = 0;
            for (int z = -1; z <= 1; z++)
            for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
                offsets[index++] = new int3(x, y, z);
            return offsets;
        }
    }
}
