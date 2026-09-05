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
        public readonly int3[] SyntheticCoordinates;
        public readonly int3[] StrongFreeCoordinates;
        public readonly MerkabaKernelSnapshot[] EvidenceKernels;
        public readonly int OriginalOccupiedCount;
        public readonly int StrongKnownFreeCount;
        public readonly int SyntheticKernelCount;

        public MerkabaExportShellResult(List<MerkabaKernelSnapshot> kernels,
            int3[] healedCoordinates, int3[] shellCoordinates,
            int3[] syntheticCoordinates, int3[] strongFreeCoordinates,
            MerkabaKernelSnapshot[] evidenceKernels,
            int originalOccupiedCount, int strongKnownFreeCount,
            int syntheticKernelCount)
        {
            Kernels = kernels;
            HealedCoordinates = healedCoordinates;
            ShellCoordinates = shellCoordinates;
            SyntheticCoordinates = syntheticCoordinates;
            StrongFreeCoordinates = strongFreeCoordinates;
            EvidenceKernels = evidenceKernels;
            OriginalOccupiedCount = originalOccupiedCount;
            StrongKnownFreeCount = strongKnownFreeCount;
            SyntheticKernelCount = syntheticKernelCount;
        }
    }

    /// <summary>
    /// Read-only, sparse export cleanup. It performs one radius-1 binary closing,
    /// vetoes synthetic healing at strong FREE evidence, and preserves every real
    /// occupied M8 owner. Its output is input to the disposable measured membrane;
    /// it never becomes another world authority.
    /// </summary>
    internal static class MerkabaExportShell
    {
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
            IProgress<OperationWorkProgress> progress = null)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var occupied = new HashSet<int3>();
            var strongFree = new HashSet<int3>();
            var realStates = new Dictionary<int3, KernelState>();
            var allEvidence = new Dictionary<int3, KernelState>();
            for (int tileIndex = 0; tileIndex < snapshot.Tiles.Count; tileIndex++)
            {
                MerkabaTileSnapshot tile = snapshot.Tiles[tileIndex];
                if (tile?.States == null ||
                    tile.States.Length != MerkabaSpatial.KernelsPerTile)
                    throw new InvalidOperationException(
                        "Export snapshot contains an invalid M8 tile payload.");
                for (int index = 0; index < tile.States.Length; index++)
                {
                    KernelState state = tile.States[index];
                    int3 coord = MerkabaSpatial.Decode(tile.Address.BlockCoord,
                        tile.Address.LocalAddress, index);
                    AddEvidence(coord, state, occupied, strongFree, realStates,
                        allEvidence);
                }
                ReportEvery(progress, ScanOperationStage.BuildingExportEvidence,
                    tileIndex + 1, snapshot.Tiles.Count, 32,
                    $"Read export evidence from {tileIndex + 1}/" +
                    $"{snapshot.Tiles.Count} tiles");
            }
            if (snapshot.Tiles.Count == 0)
                progress?.Report(new OperationWorkProgress(
                    ScanOperationStage.BuildingExportEvidence, 0, 0,
                    "Export evidence is empty"));
            return Build(occupied, strongFree, realStates, allEvidence, progress);
        }

        internal static MerkabaExportShellResult Build(
            IReadOnlyDictionary<int3, KernelState> evidence,
            IProgress<OperationWorkProgress> progress = null)
        {
            if (evidence == null) throw new ArgumentNullException(nameof(evidence));
            var occupied = new HashSet<int3>();
            var strongFree = new HashSet<int3>();
            var realStates = new Dictionary<int3, KernelState>();
            var allEvidence = new Dictionary<int3, KernelState>();
            int evidenceIndex = 0;
            foreach (KeyValuePair<int3, KernelState> pair in evidence)
            {
                AddEvidence(pair.Key, pair.Value, occupied, strongFree, realStates,
                    allEvidence);
                evidenceIndex++;
                ReportEvery(progress, ScanOperationStage.BuildingExportEvidence,
                    evidenceIndex, evidence.Count, 1024,
                    $"Read {evidenceIndex}/{evidence.Count} evidence states");
            }
            return Build(occupied, strongFree, realStates, allEvidence, progress);
        }

        private static MerkabaExportShellResult Build(HashSet<int3> occupied,
            HashSet<int3> strongFree,
            Dictionary<int3, KernelState> realStates,
            Dictionary<int3, KernelState> allEvidence,
            IProgress<OperationWorkProgress> progress)
        {
            if (occupied.Count == 0)
                throw new InvalidOperationException(
                    "The Merkaba grid has no occupied kernels.");

            HashSet<int3> healed = CloseOnce(occupied, strongFree, progress);

            HashSet<int3> shell = healed;
            if (shell.Count == 0)
                throw new InvalidOperationException(
                    "Evidence-aware export shell contains no kernels.");

            var sortedShell = new List<int3>(shell);
            sortedShell.Sort(CompareCoords);
            var kernels = new List<MerkabaKernelSnapshot>(sortedShell.Count);
            var syntheticCoordinates = new List<int3>();
            int syntheticSelected = 0;
            for (int index = 0; index < sortedShell.Count; index++)
            {
                int3 coord = sortedShell[index];
                KernelState state;
                if (realStates.TryGetValue(coord, out KernelState real))
                {
                    state = WithExportFallbackColor(real);
                }
                else
                {
                    state = SyntheticState(coord, realStates);
                    syntheticCoordinates.Add(coord);
                    syntheticSelected++;
                }
                kernels.Add(new MerkabaKernelSnapshot(coord, state));
                ReportEvery(progress, ScanOperationStage.PreparingExportColors,
                    index + 1, sortedShell.Count, 1024,
                    $"Prepared {index + 1}/{sortedShell.Count} shell colors");
            }

            int3[] healedSorted = Sorted(healed);
            return new MerkabaExportShellResult(kernels, healedSorted,
                sortedShell.ToArray(), syntheticCoordinates.ToArray(),
                Sorted(strongFree), SortedEvidence(allEvidence), occupied.Count,
                strongFree.Count,
                syntheticSelected);
        }

        private static HashSet<int3> CloseOnce(HashSet<int3> occupied,
            HashSet<int3> strongFree,
            IProgress<OperationWorkProgress> progress)
        {
            // A radius-one cubic morphology is separable. These six narrow
            // passes are exactly equivalent to visiting all 27 cube offsets,
            // while avoiding the repeated 27-wide work for every owner/chunk.
            HashSet<int3> dilatedX = DilateAxis(occupied, new int3(1, 0, 0));
            HashSet<int3> dilatedY = DilateAxis(dilatedX, new int3(0, 1, 0));
            HashSet<int3> dilated = DilateAxis(dilatedY, new int3(0, 0, 1));
            progress?.Report(new OperationWorkProgress(
                ScanOperationStage.DilatingShell, occupied.Count, occupied.Count,
                $"Dilated {occupied.Count}/{occupied.Count} occupied kernels"));

            HashSet<int3> erodedX = ErodeAxis(dilated, new int3(1, 0, 0));
            HashSet<int3> erodedY = ErodeAxis(erodedX, new int3(0, 1, 0));
            HashSet<int3> closed = ErodeAxis(erodedY, new int3(0, 0, 1));
            progress?.Report(new OperationWorkProgress(
                ScanOperationStage.HealingTinyHoles, dilated.Count, dilated.Count,
                $"Tested {dilated.Count}/{dilated.Count} closing candidates"));

            var healed = new HashSet<int3>(occupied);
            foreach (int3 candidate in closed)
            {
                if (occupied.Contains(candidate) || strongFree.Contains(candidate))
                    continue;
                healed.Add(candidate);
            }
            return healed;
        }

        private static HashSet<int3> DilateAxis(HashSet<int3> source, int3 axis)
        {
            var result = new HashSet<int3>(source.Count * 2);
            foreach (int3 coord in source)
            {
                result.Add(coord - axis);
                result.Add(coord);
                result.Add(coord + axis);
            }
            return result;
        }

        private static HashSet<int3> ErodeAxis(HashSet<int3> source, int3 axis)
        {
            var result = new HashSet<int3>();
            foreach (int3 coord in source)
            {
                if (source.Contains(coord - axis) && source.Contains(coord + axis))
                    result.Add(coord);
            }
            return result;
        }

        private static void AddEvidence(int3 coord, KernelState state,
            HashSet<int3> occupied, HashSet<int3> strongFree,
            Dictionary<int3, KernelState> realStates,
            Dictionary<int3, KernelState> allEvidence)
        {
            if (state.OccupancyEvidence != 0 || state.Flags != 0u ||
                state.ColorConfidence != 0u)
            {
                if (!allEvidence.TryAdd(coord, state))
                    throw new InvalidOperationException(
                        $"Duplicate export evidence coordinate {coord}.");
            }
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

        private static void ReportEvery(
            IProgress<OperationWorkProgress> progress,
            ScanOperationStage stage, int completed, int total, int interval,
            string text)
        {
            if (progress == null ||
                (completed != total && completed % interval != 0)) return;
            progress.Report(new OperationWorkProgress(stage, completed, total,
                text));
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

        private static MerkabaKernelSnapshot[] SortedEvidence(
            Dictionary<int3, KernelState> evidence)
        {
            var coords = new List<int3>(evidence.Keys);
            coords.Sort(CompareCoords);
            var result = new MerkabaKernelSnapshot[coords.Count];
            for (int index = 0; index < coords.Count; index++)
                result[index] = new MerkabaKernelSnapshot(coords[index],
                    evidence[coords[index]]);
            return result;
        }

        private static int CompareCoords(int3 left, int3 right)
        {
            if (left.x != right.x) return left.x.CompareTo(right.x);
            if (left.y != right.y) return left.y.CompareTo(right.y);
            return left.z.CompareTo(right.z);
        }

    }
}
