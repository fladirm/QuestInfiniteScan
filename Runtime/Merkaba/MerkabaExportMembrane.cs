using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Genesis.RoomScan
{
    /// <summary>One export-local, zero-thickness membrane support.</summary>
    internal readonly struct MerkabaExportMembranePatch
    {
        internal readonly int3 Coord;
        internal readonly float3 Normal;
        internal readonly float3 Corner00;
        internal readonly float3 Corner10;
        internal readonly float3 Corner11;
        internal readonly float3 Corner01;
        internal readonly uint PackedColor;
        internal readonly bool IsInferred;

        internal MerkabaExportMembranePatch(int3 coord, float3 normal,
            float3 corner00, float3 corner10, float3 corner11, float3 corner01,
            uint packedColor, bool isInferred)
        {
            Coord = coord;
            Normal = normal;
            Corner00 = corner00;
            Corner10 = corner10;
            Corner11 = corner11;
            Corner01 = corner01;
            PackedColor = packedColor;
            IsInferred = isInferred;
        }

        internal float3 Corner(int index) => index switch
        {
            0 => Corner00,
            1 => Corner10,
            2 => Corner11,
            3 => Corner01,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };
    }

    internal sealed class MerkabaExportMembraneResult
    {
        internal readonly List<MerkabaExportMembranePatch> Patches;
        internal readonly int3[] CanonicalOccupiedCoordinates;
        internal readonly int3[] MeasuredPlaneCoordinates;
        internal readonly int CanonicalOccupiedCount;
        internal readonly int MeasuredPlaneOccupiedCount;
        internal readonly int MeasuredPatchCount;
        internal readonly int InferredPatchCount;
        internal readonly int UnresolvedMeasuredPlaneCount;
        internal readonly int3[] RemovedBehindCoordinates;
        internal readonly int RemovedBehindMembraneCount;
        internal readonly int PartitionCutCount;

        internal MerkabaExportMembraneResult(
            List<MerkabaExportMembranePatch> patches,
            int3[] canonicalOccupiedCoordinates,
            int3[] measuredPlaneCoordinates,
            int measuredPatchCount, int inferredPatchCount,
            int3[] removedBehindCoordinates, int partitionCutCount)
        {
            Patches = patches ?? throw new ArgumentNullException(nameof(patches));
            CanonicalOccupiedCoordinates = canonicalOccupiedCoordinates ??
                Array.Empty<int3>();
            MeasuredPlaneCoordinates = measuredPlaneCoordinates ??
                Array.Empty<int3>();
            CanonicalOccupiedCount = CanonicalOccupiedCoordinates.Length;
            MeasuredPlaneOccupiedCount = MeasuredPlaneCoordinates.Length;
            MeasuredPatchCount = measuredPatchCount;
            InferredPatchCount = inferredPatchCount;
            UnresolvedMeasuredPlaneCount = Math.Max(0,
                CanonicalOccupiedCount - MeasuredPlaneOccupiedCount);
            RemovedBehindCoordinates = removedBehindCoordinates ??
                Array.Empty<int3>();
            RemovedBehindMembraneCount = RemovedBehindCoordinates.Length;
            PartitionCutCount = partitionCutCount;
        }
    }

    /// <summary>
    /// Disposable export membrane derived from the immutable M8 snapshot. Measured
    /// patches use the same plane decoder as live readout; export-only hole closure
    /// is neutral gray and never writes back to M8.
    /// </summary>
    internal static class MerkabaExportMembrane
    {
        private static readonly int3[] OrderedNeighbours = BuildOrderedNeighbours();
        private static readonly int3[] AxisOffsets =
        {
            new(-1, 0, 0), new(1, 0, 0),
            new(0, -1, 0), new(0, 1, 0),
            new(0, 0, -1), new(0, 0, 1)
        };

        private sealed class SparsePartitionResult
        {
            internal readonly HashSet<int3> Cut;
            internal readonly HashSet<int3> FreeReachable;

            internal SparsePartitionResult(HashSet<int3> cut,
                HashSet<int3> freeReachable)
            {
                Cut = cut;
                FreeReachable = freeReachable;
            }
        }
        internal static MerkabaExportMembraneResult Build(
            MerkabaExportShellResult shell,
            IProgress<OperationWorkProgress> progress = null)
        {
            if (shell == null) throw new ArgumentNullException(nameof(shell));
            var states = new Dictionary<int3, KernelState>(shell.Kernels.Count);
            foreach (MerkabaKernelSnapshot kernel in shell.Kernels)
            {
                if (!states.TryAdd(kernel.Coord, kernel.State))
                    throw new InvalidOperationException(
                        $"Duplicate membrane coordinate {kernel.Coord}.");
            }
            var membraneContext = new Dictionary<int3, KernelState>(
                shell.EvidenceKernels.Length + shell.Kernels.Count);
            foreach (MerkabaKernelSnapshot kernel in shell.EvidenceKernels)
                membraneContext.Add(kernel.Coord, kernel.State);
            foreach (MerkabaKernelSnapshot kernel in shell.Kernels)
                membraneContext[kernel.Coord] = kernel.State;

            var synthetic = new HashSet<int3>(shell.SyntheticCoordinates);
            var strongFree = new HashSet<int3>(shell.StrongFreeCoordinates);
            var measured = new List<int3>();
            var canonicalCoords = new List<int3>();
            foreach (MerkabaKernelSnapshot kernel in shell.Kernels)
            {
                if (synthetic.Contains(kernel.Coord)) continue;
                canonicalCoords.Add(kernel.Coord);
                if (kernel.State.HasMeasuredSurfacePlane)
                    measured.Add(kernel.Coord);
            }
            measured.Sort(CompareCoords);
            canonicalCoords.Sort(CompareCoords);

            SparsePartitionResult partition = SolveSparsePartition(shell);
            HashSet<int3> partitionCut = partition.Cut;
            var candidateCoords = new HashSet<int3>();
            foreach (MerkabaKernelSnapshot kernel in shell.Kernels)
                candidateCoords.Add(kernel.Coord);
            foreach (int3 coord in partitionCut)
                if (!strongFree.Contains(coord)) candidateCoords.Add(coord);
            var sortedCandidates = new List<int3>(candidateCoords);
            sortedCandidates.Sort(CompareCoords);
            var patches = new List<MerkabaExportMembranePatch>(
                sortedCandidates.Count);
            int measuredPatches = 0;
            int inferredPatches = 0;
            var removedBehindCoordinates = new List<int3>();
            for (int index = 0; index < sortedCandidates.Count; index++)
            {
                int3 coord = sortedCandidates[index];
                bool hasState = states.TryGetValue(coord, out KernelState state);
                bool isSynthetic = synthetic.Contains(coord);
                if (hasState && !isSynthetic &&
                    MerkabaOverlapShell.TryBuildPatch(coord, membraneContext,
                        out MerkabaOverlapShell.Patch measuredPatch))
                {
                    if (ShouldKeepMeasured(coord, partition, strongFree))
                    {
                        patches.Add(FromMeasured(measuredPatch));
                        measuredPatches++;
                    }
                    else
                    {
                        removedBehindCoordinates.Add(coord);
                    }
                }
                else if (!hasState || isSynthetic)
                {
                    if ((partitionCut.Contains(coord) ||
                         (strongFree.Count == 0 && isSynthetic)) &&
                         TryInferClosure(coord, membraneContext,
                            out MerkabaExportMembranePatch inferred))
                    {
                        patches.Add(inferred);
                        inferredPatches++;
                    }
                }

                if (index + 1 == sortedCandidates.Count ||
                    (index + 1) % 1024 == 0)
                    progress?.Report(new OperationWorkProgress(
                        ScanOperationStage.ExtractingMerkabaShell, index + 1,
                        sortedCandidates.Count,
                        $"Solved {index + 1}/{sortedCandidates.Count} membrane supports"));
            }
            if (patches.Count == 0)
                throw new InvalidOperationException(
                    "The export membrane has no resolvable measured surface " +
                    $"patches (occupied={canonicalCoords.Count}, " +
                    $"measuredPlane={measured.Count}, " +
                    $"unresolvedPlane=" +
                    $"{canonicalCoords.Count - measured.Count}).");

            return new MerkabaExportMembraneResult(patches,
                canonicalCoords.ToArray(), measured.ToArray(), measuredPatches,
                inferredPatches, removedBehindCoordinates.ToArray(),
                partitionCut.Count);
        }

        private static bool ShouldKeepMeasured(int3 coord,
            SparsePartitionResult partition, HashSet<int3> strongFree)
        {
            if (strongFree.Count == 0 || partition.Cut.Contains(coord))
                return true;
            foreach (int3 offset in OrderedNeighbours)
                if (partition.FreeReachable.Contains(coord + offset))
                    return true;
            // Destructive selection requires a locally witnessed separator:
            // the measured support is immediately behind a selected cut and
            // that cut itself touches canonical strong-FREE evidence. Sparse,
            // distant FREE cannot erase an otherwise legitimate measured wall.
            foreach (int3 offset in OrderedNeighbours)
            {
                int3 cutCoord = coord + offset;
                if (!partition.Cut.Contains(cutCoord)) continue;
                foreach (int3 freeOffset in OrderedNeighbours)
                    if (strongFree.Contains(cutCoord + freeOffset))
                        return false;
            }
            return true;
        }

        private static MerkabaExportMembranePatch FromMeasured(
            MerkabaOverlapShell.Patch patch) => new(patch.Main, patch.Normal,
            patch.Corner00.GridPosition, patch.Corner10.GridPosition,
            patch.Corner11.GridPosition, patch.Corner01.GridPosition,
            patch.Corner00.PackedColor, false);

        private static bool TryInferClosure(int3 coord,
            IReadOnlyDictionary<int3, KernelState> states,
            out MerkabaExportMembranePatch patch)
        {
            var donors = new List<(int3 Coord, MerkabaOverlapShell.Patch Patch)>();
            foreach (int3 offset in OrderedNeighbours)
            {
                int3 donorCoord = coord + offset;
                if (!states.TryGetValue(donorCoord, out KernelState state) ||
                    !MerkabaOverlapShell.TryBuildPatch(donorCoord, states,
                        out MerkabaOverlapShell.Patch donor))
                    continue;
                donors.Add((donorCoord, donor));
            }
            if (donors.Count == 0)
            {
                patch = default;
                return false;
            }

            int reference = 0;
            float bestCoherence = float.NegativeInfinity;
            for (int candidate = 0; candidate < donors.Count; candidate++)
            {
                float coherence = 0f;
                for (int other = 0; other < donors.Count; other++)
                    coherence += math.abs(math.dot(donors[candidate].Patch.Normal,
                        donors[other].Patch.Normal));
                if (coherence > bestCoherence)
                {
                    bestCoherence = coherence;
                    reference = candidate;
                }
            }

            float3 normal = donors[reference].Patch.Normal;
            var heights = new List<float>(donors.Count);
            foreach ((int3 _, MerkabaOverlapShell.Patch donor) in donors)
            {
                float3 donorCenter = (donor.Corner00.GridPosition +
                    donor.Corner10.GridPosition + donor.Corner11.GridPosition +
                    donor.Corner01.GridPosition) * 0.25f;
                heights.Add(math.dot(donorCenter, normal));
            }
            heights.Sort();
            float height = heights.Count % 2 != 0
                ? heights[heights.Count / 2]
                : (heights[heights.Count / 2 - 1] +
                   heights[heights.Count / 2]) * 0.5f;
            float3 latticeCenter = (float3)coord * MerkabaConstants.LatticeStep;
            float3 center = latticeCenter + normal *
                (height - math.dot(latticeCenter, normal));
            int dominantAxis = MerkabaOverlapShell.DominantAxis(normal);
            MerkabaOverlapShell.TangentAxes(dominantAxis,
                out int tangentAxis0, out int tangentAxis1);
            float3 tangent0 = tangentAxis0 == 0 ? new float3(1, 0, 0) :
                tangentAxis0 == 1 ? new float3(0, 1, 0) :
                new float3(0, 0, 1);
            float3 tangent1 = tangentAxis1 == 0 ? new float3(1, 0, 0) :
                tangentAxis1 == 1 ? new float3(0, 1, 0) :
                new float3(0, 0, 1);
            float3 extent0 = tangent0 *
                MerkabaOverlapShell.MembraneHalfPitch;
            float3 extent1 = tangent1 *
                MerkabaOverlapShell.MembraneHalfPitch;
            patch = new MerkabaExportMembranePatch(coord, normal,
                center - extent0 - extent1, center + extent0 - extent1,
                center + extent0 + extent1, center - extent0 + extent1,
                MerkabaConstants.NeutralPackedColor, true);
            return true;
        }

        private static SparsePartitionResult SolveSparsePartition(
            MerkabaExportShellResult shell)
        {
            if (shell.StrongFreeCoordinates.Length == 0)
                return new SparsePartitionResult(new HashSet<int3>(),
                    new HashSet<int3>());

            var evidence = new Dictionary<int3, KernelState>(
                shell.EvidenceKernels.Length);
            var domain = new HashSet<int3>();
            foreach (MerkabaKernelSnapshot kernel in shell.EvidenceKernels)
            {
                evidence.Add(kernel.Coord, kernel.State);
                domain.Add(kernel.Coord);
                foreach (int3 offset in AxisOffsets)
                    domain.Add(kernel.Coord + offset);
            }
            foreach (MerkabaKernelSnapshot kernel in shell.Kernels)
            {
                domain.Add(kernel.Coord);
                foreach (int3 offset in AxisOffsets)
                    domain.Add(kernel.Coord + offset);
            }

            var coords = new List<int3>(domain);
            coords.Sort(CompareCoords);
            var indices = new Dictionary<int3, int>(coords.Count);
            for (int index = 0; index < coords.Count; index++)
                indices.Add(coords[index], index);

            int source = checked(coords.Count * 2);
            int sink = source + 1;
            var flow = new SparseFlowNetwork(sink + 1);
            for (int index = 0; index < coords.Count; index++)
            {
                int input = index * 2;
                int output = input + 1;
                int3 coord = coords[index];
                evidence.TryGetValue(coord, out KernelState state);
                flow.AddEdge(input, output, PartitionCost(state));
                if (MerkabaExportShell.IsStrongKnownFree(state))
                    flow.AddEdge(source, input, SparseFlowNetwork.Infinity);

                bool boundary = false;
                foreach (int3 offset in AxisOffsets)
                {
                    if (indices.TryGetValue(coord + offset, out int neighbour))
                        flow.AddEdge(output, neighbour * 2,
                            SparseFlowNetwork.Infinity);
                    else
                        boundary = true;
                }
                if (boundary)
                    flow.AddEdge(output, sink, SparseFlowNetwork.Infinity);
            }

            flow.MaxFlow(source, sink);
            bool[] reachable = flow.ReachableFrom(source);
            var cut = new HashSet<int3>();
            var freeReachable = new HashSet<int3>();
            for (int index = 0; index < coords.Count; index++)
            {
                if (reachable[index * 2] && !reachable[index * 2 + 1])
                    cut.Add(coords[index]);
                if (reachable[index * 2 + 1])
                    freeReachable.Add(coords[index]);
            }
            return new SparsePartitionResult(cut, freeReachable);
        }

        private static long PartitionCost(KernelState state)
        {
            if (MerkabaExportShell.IsStrongKnownFree(state))
                return SparseFlowNetwork.Infinity;
            int limit = MerkabaConstants.EvidenceConfidenceLimit;
            if (state.IsOccupied)
            {
                int evidence = math.clamp(state.OccupancyEvidence,
                    MerkabaConstants.OccupiedOnThreshold, limit);
                return 1L + limit - evidence;
            }
            long unknown = limit + 1L;
            return state.OccupancyEvidence < 0
                ? unknown + Math.Min(-((long)state.OccupancyEvidence), limit)
                : unknown;
        }

        private sealed class SparseFlowNetwork
        {
            internal const long Infinity = 1L << 50;
            private readonly int[] _head;
            private readonly int[] _level;
            private readonly int[] _nextEdge;
            private readonly List<int> _to = new();
            private readonly List<int> _next = new();
            private readonly List<long> _capacity = new();

            internal SparseFlowNetwork(int nodeCount)
            {
                _head = new int[nodeCount];
                _level = new int[nodeCount];
                _nextEdge = new int[nodeCount];
                Array.Fill(_head, -1);
            }

            internal void AddEdge(int from, int to, long capacity)
            {
                AddHalf(from, to, capacity);
                AddHalf(to, from, 0L);
            }

            internal long MaxFlow(int source, int sink)
            {
                long total = 0L;
                while (BuildLevels(source, sink))
                {
                    Array.Copy(_head, _nextEdge, _head.Length);
                    while (true)
                    {
                        long pushed = Push(source, sink, Infinity);
                        if (pushed == 0L) break;
                        total = checked(total + pushed);
                    }
                }
                return total;
            }

            internal bool[] ReachableFrom(int source)
            {
                var reached = new bool[_head.Length];
                var queue = new int[_head.Length];
                int read = 0, write = 0;
                reached[source] = true;
                queue[write++] = source;
                while (read < write)
                {
                    int node = queue[read++];
                    for (int edge = _head[node]; edge >= 0; edge = _next[edge])
                    {
                        int target = _to[edge];
                        if (_capacity[edge] <= 0L || reached[target]) continue;
                        reached[target] = true;
                        queue[write++] = target;
                    }
                }
                return reached;
            }

            private void AddHalf(int from, int to, long capacity)
            {
                int edge = _to.Count;
                _to.Add(to);
                _next.Add(_head[from]);
                _capacity.Add(capacity);
                _head[from] = edge;
            }

            private bool BuildLevels(int source, int sink)
            {
                Array.Fill(_level, -1);
                var queue = new int[_head.Length];
                int read = 0, write = 0;
                _level[source] = 0;
                queue[write++] = source;
                while (read < write)
                {
                    int node = queue[read++];
                    for (int edge = _head[node]; edge >= 0; edge = _next[edge])
                    {
                        int target = _to[edge];
                        if (_capacity[edge] <= 0L || _level[target] >= 0) continue;
                        _level[target] = _level[node] + 1;
                        queue[write++] = target;
                    }
                }
                return _level[sink] >= 0;
            }

            private long Push(int node, int sink, long available)
            {
                if (node == sink) return available;
                for (int edge = _nextEdge[node]; edge >= 0;
                     edge = _nextEdge[node])
                {
                    int target = _to[edge];
                    if (_capacity[edge] > 0L &&
                        _level[target] == _level[node] + 1)
                    {
                        long pushed = Push(target, sink,
                            Math.Min(available, _capacity[edge]));
                        if (pushed > 0L)
                        {
                            _capacity[edge] -= pushed;
                            _capacity[edge ^ 1] += pushed;
                            return pushed;
                        }
                    }
                    _nextEdge[node] = _next[edge];
                }
                return 0L;
            }
        }

        private static int3[] BuildOrderedNeighbours()
        {
            var neighbours = new List<int3>(MerkabaConstants.NeighbourCount);
            foreach (int3 offset in MerkabaConstants.Neighbours)
                neighbours.Add(offset);
            neighbours.Sort(CompareCoords);
            return neighbours.ToArray();
        }

        private static int CompareCoords(int3 left, int3 right)
        {
            if (left.x != right.x) return left.x.CompareTo(right.x);
            if (left.y != right.y) return left.y.CompareTo(right.y);
            return left.z.CompareTo(right.z);
        }
    }
}
