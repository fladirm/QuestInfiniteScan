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
        internal readonly bool IsTriangle;
        // Shared-vertex triangles carry the canonical normal and colour of
        // each corner; quads keep one normal and colour.
        internal readonly float3 Normal10;
        internal readonly float3 Normal11;
        internal readonly float3 Normal01;
        internal readonly uint PackedColor10;
        internal readonly uint PackedColor11;
        internal readonly uint PackedColor01;

        internal MerkabaExportMembranePatch(int3 coord, float3 normal,
            float3 corner00, float3 corner10, float3 corner11, float3 corner01,
            uint packedColor, bool isInferred, bool isTriangle = false,
            float3? normal10 = null, float3? normal11 = null,
            uint? packedColor10 = null, uint? packedColor11 = null,
            float3? normal01 = null, uint? packedColor01 = null)
        {
            Normal10 = normal10 ?? normal;
            Normal11 = normal11 ?? normal;
            Normal01 = normal01 ?? normal;
            PackedColor10 = packedColor10 ?? packedColor;
            PackedColor11 = packedColor11 ?? packedColor;
            PackedColor01 = packedColor01 ?? packedColor;
            Coord = coord;
            Normal = normal;
            Corner00 = corner00;
            Corner10 = corner10;
            Corner11 = corner11;
            Corner01 = corner01;
            PackedColor = packedColor;
            IsInferred = isInferred;
            IsTriangle = isTriangle;
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

        /// <summary>
        /// Production export path: the SAME frozen 25 mm membrane oracle as
        /// live readout. One measured occupied MAIN owns one zero-thickness
        /// quad whose corners are shared knots (position, colour and normal
        /// solved from the corner neighbourhood); the 50 mm support is
        /// connectivity only. The GLB writer keys identical knots onto one
        /// vertex, across kernels, tiles and chunks.
        /// </summary>
        internal static MerkabaExportMembraneResult BuildSkin(
            MerkabaExportShellResult shell,
            IProgress<OperationWorkProgress> progress = null,
            Func<int3, bool> ownsCoordinate = null)
        {
            if (shell == null) throw new ArgumentNullException(nameof(shell));
            var synthetic = new HashSet<int3>(shell.SyntheticCoordinates);
            var context = new Dictionary<int3, KernelState>(
                shell.EvidenceKernels.Length + shell.Kernels.Count);
            foreach (MerkabaKernelSnapshot kernel in shell.EvidenceKernels)
                context[kernel.Coord] = kernel.State;
            foreach (MerkabaKernelSnapshot kernel in shell.Kernels)
                if (!synthetic.Contains(kernel.Coord))
                    context[kernel.Coord] = kernel.State;

            var canonical = new List<int3>();
            var measured = new List<int3>();
            var owners = new List<MerkabaKernelSnapshot>();
            foreach (MerkabaKernelSnapshot kernel in shell.Kernels)
            {
                if (synthetic.Contains(kernel.Coord) ||
                    !kernel.State.IsOccupied)
                    continue;
                canonical.Add(kernel.Coord);
                if (!kernel.State.HasMeasuredSurfacePlane) continue;
                measured.Add(kernel.Coord);
                if (ownsCoordinate == null || ownsCoordinate(kernel.Coord))
                    owners.Add(kernel);
            }
            canonical.Sort(CompareCoords);
            measured.Sort(CompareCoords);
            owners.Sort((left, right) => CompareCoords(left.Coord,
                right.Coord));

            var patches = new List<MerkabaExportMembranePatch>(
                Math.Max(owners.Count, 16));
            for (int ownerIndex = 0; ownerIndex < owners.Count; ownerIndex++)
            {
                MerkabaKernelSnapshot kernel = owners[ownerIndex];
                if (MerkabaOverlapShell.TryBuildPatch(kernel.Coord, context,
                        out MerkabaOverlapShell.Patch patch))
                    patches.Add(FromMeasured(patch));

                if (ownerIndex + 1 == owners.Count ||
                    (ownerIndex + 1) % 1024 == 0)
                    progress?.Report(new OperationWorkProgress(
                        ScanOperationStage.ExtractingMerkabaShell,
                        ownerIndex + 1, owners.Count,
                        $"Built {ownerIndex + 1}/{owners.Count} membrane owners"));
            }

            if (patches.Count == 0 && ownsCoordinate == null)
                throw new InvalidOperationException(
                    "The M8 membrane has no resolvable measured patches " +
                    $"(occupied={canonical.Count}, measuredPlane={measured.Count}).");

            return new MerkabaExportMembraneResult(patches,
                canonical.ToArray(), measured.ToArray(), patches.Count, 0,
                Array.Empty<int3>(), 0);
        }

        private static uint AverageColor(uint first, uint second, uint third)
        {
            UnityEngine.Color32 a = KernelState.UnpackColor(first);
            UnityEngine.Color32 b = KernelState.UnpackColor(second);
            UnityEngine.Color32 c = KernelState.UnpackColor(third);
            return KernelState.PackColor(new UnityEngine.Color32(
                (byte)((a.r + b.r + c.r + 1) / 3),
                (byte)((a.g + b.g + c.g + 1) / 3),
                (byte)((a.b + b.b + c.b + 1) / 3), 255));
        }

        internal static MerkabaExportMembraneResult Build(
            MerkabaExportShellResult shell,
            IProgress<OperationWorkProgress> progress = null,
            Func<int3, bool> ownsCoordinate = null,
            Func<int3, bool> isUnknownSpace = null)
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

            SparsePartitionResult partition = SolveSparsePartition(shell,
                isUnknownSpace);
            var patchCache = new Dictionary<int3, MerkabaOverlapShell.Patch?>();
            HashSet<int3> partitionCut = partition.Cut;
            var candidateCoords = new HashSet<int3>();
            foreach (MerkabaKernelSnapshot kernel in shell.Kernels)
                if (ownsCoordinate == null || ownsCoordinate(kernel.Coord))
                    candidateCoords.Add(kernel.Coord);
            foreach (int3 coord in partitionCut)
                if (!strongFree.Contains(coord) &&
                    (ownsCoordinate == null || ownsCoordinate(coord)))
                    candidateCoords.Add(coord);
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
                    TryBuildCachedPatch(coord, membraneContext, patchCache,
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
                         TryInferClosure(coord, membraneContext, patchCache,
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
            if (patches.Count == 0 && ownsCoordinate == null)
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

        // Every corner carries its own knot colour and normal so identical
        // knots of neighbouring patches key onto one exported vertex.
        private static MerkabaExportMembranePatch FromMeasured(
            MerkabaOverlapShell.Patch patch) => new(patch.Main,
            patch.Corner00.Normal,
            patch.Corner00.GridPosition, patch.Corner10.GridPosition,
            patch.Corner11.GridPosition, patch.Corner01.GridPosition,
            patch.Corner00.PackedColor, false, false,
            patch.Corner10.Normal, patch.Corner11.Normal,
            patch.Corner10.PackedColor, patch.Corner11.PackedColor,
            patch.Corner01.Normal, patch.Corner01.PackedColor);

        /// <summary>
        /// One membrane oracle evaluation per coordinate for the whole group:
        /// the oracle is deterministic over the immutable context, so a
        /// memoized result is the identical patch.
        /// </summary>
        private static bool TryBuildCachedPatch(int3 coord,
            IReadOnlyDictionary<int3, KernelState> states,
            Dictionary<int3, MerkabaOverlapShell.Patch?> cache,
            out MerkabaOverlapShell.Patch patch)
        {
            if (!cache.TryGetValue(coord, out MerkabaOverlapShell.Patch? cached))
            {
                cached = MerkabaOverlapShell.TryBuildPatch(coord, states,
                    out MerkabaOverlapShell.Patch built)
                    ? built : (MerkabaOverlapShell.Patch?)null;
                cache.Add(coord, cached);
            }
            patch = cached ?? default;
            return cached.HasValue;
        }

        private static bool TryInferClosure(int3 coord,
            IReadOnlyDictionary<int3, KernelState> states,
            Dictionary<int3, MerkabaOverlapShell.Patch?> patchCache,
            out MerkabaExportMembranePatch patch)
        {
            var donors = new List<(int3 Coord, MerkabaOverlapShell.Patch Patch)>();
            foreach (int3 offset in OrderedNeighbours)
            {
                int3 donorCoord = coord + offset;
                if (!states.ContainsKey(donorCoord) ||
                    !TryBuildCachedPatch(donorCoord, states, patchCache,
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
            MerkabaExportShellResult shell, Func<int3, bool> isUnknownSpace)
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
                    // Only never-observed space terminates FREE reachability.
                    // The edge of a finite solve window is not unknown space.
                    else if (isUnknownSpace == null ||
                             isUnknownSpace(coord + offset))
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
            private readonly int[] _queue;
            private readonly int[] _pathNodes;
            private readonly int[] _pathEdges;
            private int[] _to;
            private int[] _next;
            private long[] _capacity;
            private int _edgeCount;

            internal SparseFlowNetwork(int nodeCount)
            {
                _head = new int[nodeCount];
                _level = new int[nodeCount];
                _nextEdge = new int[nodeCount];
                _queue = new int[nodeCount];
                _pathNodes = new int[nodeCount + 1];
                _pathEdges = new int[nodeCount + 1];
                int initialEdges = Math.Max(16, nodeCount * 8);
                _to = new int[initialEdges];
                _next = new int[initialEdges];
                _capacity = new long[initialEdges];
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
                        long pushed = Push(source, sink);
                        if (pushed == 0L) break;
                        total = checked(total + pushed);
                    }
                }
                return total;
            }

            internal bool[] ReachableFrom(int source)
            {
                var reached = new bool[_head.Length];
                int read = 0, write = 0;
                reached[source] = true;
                _queue[write++] = source;
                while (read < write)
                {
                    int node = _queue[read++];
                    for (int edge = _head[node]; edge >= 0; edge = _next[edge])
                    {
                        int target = _to[edge];
                        if (_capacity[edge] <= 0L || reached[target]) continue;
                        reached[target] = true;
                        _queue[write++] = target;
                    }
                }
                return reached;
            }

            private void AddHalf(int from, int to, long capacity)
            {
                if (_edgeCount == _to.Length)
                {
                    int grown = checked(_to.Length * 2);
                    Array.Resize(ref _to, grown);
                    Array.Resize(ref _next, grown);
                    Array.Resize(ref _capacity, grown);
                }
                int edge = _edgeCount++;
                _to[edge] = to;
                _next[edge] = _head[from];
                _capacity[edge] = capacity;
                _head[from] = edge;
            }

            private bool BuildLevels(int source, int sink)
            {
                Array.Fill(_level, -1);
                int read = 0, write = 0;
                _level[source] = 0;
                _queue[write++] = source;
                while (read < write)
                {
                    int node = _queue[read++];
                    for (int edge = _head[node]; edge >= 0; edge = _next[edge])
                    {
                        int target = _to[edge];
                        if (_capacity[edge] <= 0L || _level[target] >= 0) continue;
                        _level[target] = _level[node] + 1;
                        _queue[write++] = target;
                    }
                }
                return _level[sink] >= 0;
            }

            // Iterative blocking-flow augmentation. Level depth can reach the
            // node count on thread-pool stacks, so no recursion is used.
            private long Push(int source, int sink)
            {
                int depth = 0;
                _pathNodes[0] = source;
                while (depth >= 0)
                {
                    int node = _pathNodes[depth];
                    if (node == sink)
                    {
                        long bottleneck = Infinity;
                        for (int step = 0; step < depth; step++)
                            bottleneck = Math.Min(bottleneck,
                                _capacity[_pathEdges[step]]);
                        for (int step = 0; step < depth; step++)
                        {
                            int edge = _pathEdges[step];
                            _capacity[edge] -= bottleneck;
                            _capacity[edge ^ 1] += bottleneck;
                        }
                        return bottleneck;
                    }
                    bool advanced = false;
                    for (int edge = _nextEdge[node]; edge >= 0;
                         edge = _nextEdge[node])
                    {
                        int target = _to[edge];
                        if (_capacity[edge] > 0L &&
                            _level[target] == _level[node] + 1)
                        {
                            _pathEdges[depth] = edge;
                            _pathNodes[++depth] = target;
                            advanced = true;
                            break;
                        }
                        _nextEdge[node] = _next[edge];
                    }
                    if (advanced) continue;
                    // Dead end: retire this node's level and the edge into it.
                    _level[node] = -1;
                    depth--;
                    if (depth >= 0)
                    {
                        int parent = _pathNodes[depth];
                        _nextEdge[parent] = _next[_nextEdge[parent]];
                    }
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
