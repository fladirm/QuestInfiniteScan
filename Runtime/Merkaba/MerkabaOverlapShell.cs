using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Unity.Mathematics;

namespace Genesis.RoomScan
{
    /// <summary>
    /// CPU/codegen authority for the disposable measured M8 membrane
    /// (contract C5 with the C5.1-C5.4 amendment). KernelState remains the
    /// only persistent world state.
    /// </summary>
    internal static class MerkabaOverlapShell
    {
        internal const int CornersPerPatch = 4;
        internal const int TrianglesPerPatch = 2;
        internal const int VerticesPerPatch = 4;
        internal const int IndicesPerPatch = 6;
        internal const float MembranePatchPitch = MerkabaConstants.LatticeStep;
        internal const float MembraneHalfPitch =
            MerkabaConstants.LatticeStep * 0.5f;

        private const float NumericalEpsilon = 1e-6f;
        // A plane is usable on a line of chart c only while |N[c]| >= this.
        internal const float MembraneCompatibleAxisCosine = 0.5f;
        // g: two heights on one line belong to one branch when closer than
        // this; two cells belong to one local sheet when their planes agree
        // both ways within it.
        internal const float BranchHeightGap = MembranePatchPitch * 0.6f;
        // Neighbours whose normals agree at least this much (no abs: the two
        // sides of a thin leaf never mix) shape the canonical chart.
        internal const float ChartCompatibleCosine = 0.5f;
        // No patch or transition edge may exceed this.
        internal const float MembraneMaxEdge = 0.045f;
        // A shared branch spans at most this many normal layers: the
        // intersection of all [L - 1, L + 1] of its uses is non-empty.
        internal const int MaxLayerSpan = 2;
        // Facing edges of a chart transition must be co-directed this much.
        internal const float TransitionEdgeCosine = 0.5f;

        private static readonly byte[] TriangleOrder = { 0, 1, 2, 0, 2, 3 };

        /// <summary>One absolute half-grid line of one chart.</summary>
        internal readonly struct LineKey : IEquatable<LineKey>
        {
            // Half-lattice address with the chart component set to zero.
            internal readonly int3 LineAddress;
            internal readonly int Chart;

            internal LineKey(int3 halfAddress, int chart)
            {
                halfAddress[chart] = 0;
                LineAddress = halfAddress;
                Chart = chart;
            }

            public bool Equals(LineKey other) =>
                math.all(LineAddress == other.LineAddress) &&
                Chart == other.Chart;

            public override bool Equals(object obj) =>
                obj is LineKey other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(
                LineAddress.x, LineAddress.y, LineAddress.z, Chart);
        }

        /// <summary>
        /// Global identity of one line node (C5.2): line, chart, free side and
        /// the smallest member of its component. Never the winner set.
        /// </summary>
        internal readonly struct NodeKey : IEquatable<NodeKey>
        {
            internal readonly int3 LineAddress;
            internal readonly int Chart;
            internal readonly int Side;
            internal readonly int MinLayer;
            internal readonly int MinColumn;

            internal NodeKey(int3 lineAddress, int chart, int side,
                int minLayer, int minColumn)
            {
                LineAddress = lineAddress;
                Chart = chart;
                Side = side;
                MinLayer = minLayer;
                MinColumn = minColumn;
            }

            public bool Equals(NodeKey other) =>
                math.all(LineAddress == other.LineAddress) &&
                Chart == other.Chart && Side == other.Side &&
                MinLayer == other.MinLayer && MinColumn == other.MinColumn;

            public override bool Equals(object obj) =>
                obj is NodeKey other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(
                LineAddress.x, LineAddress.y, LineAddress.z, Chart, Side,
                MinLayer, MinColumn);
        }

        /// <summary>One solved line node.</summary>
        internal readonly struct Node
        {
            internal readonly NodeKey Key;
            internal readonly float3 GridPosition;
            internal readonly uint PackedColor;
            internal readonly float3 Normal;

            internal Node(NodeKey key, float3 gridPosition, uint packedColor,
                float3 normal)
            {
                Key = key;
                GridPosition = gridPosition;
                PackedColor = packedColor;
                Normal = normal;
            }
        }

        internal readonly struct Corner : IEquatable<Corner>
        {
            internal readonly float3 GridPosition;
            internal readonly uint PackedColor;
            // The half-lattice address of the node line (chart component
            // zero) and the chart axis.
            internal readonly int3 LineAddress;
            internal readonly int Chart;
            // Export attribute only (the live vertex ABI carries no normal):
            // the mean unit normal of the node's winners.
            internal readonly float3 Normal;
            internal readonly NodeKey Key;

            internal Corner(Node node)
            {
                GridPosition = node.GridPosition;
                PackedColor = node.PackedColor;
                LineAddress = node.Key.LineAddress;
                Chart = node.Key.Chart;
                Normal = node.Normal;
                Key = node.Key;
            }

            public bool Equals(Corner other) =>
                math.all(GridPosition == other.GridPosition) &&
                PackedColor == other.PackedColor &&
                math.all(LineAddress == other.LineAddress) &&
                Chart == other.Chart &&
                math.all(Normal == other.Normal) &&
                Key.Equals(other.Key);

            public override bool Equals(object obj) =>
                obj is Corner other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(
                GridPosition.x, GridPosition.y, GridPosition.z, PackedColor,
                LineAddress.x, LineAddress.y, LineAddress.z, Chart);
        }

        internal readonly struct Patch : IEquatable<Patch>
        {
            internal readonly int3 Main;
            internal readonly float3 Normal;
            internal readonly float3 Tangent0;
            internal readonly float3 Tangent1;
            internal readonly Corner Corner00;
            internal readonly Corner Corner10;
            internal readonly Corner Corner11;
            internal readonly Corner Corner01;

            internal Patch(int3 main, float3 normal, float3 tangent0,
                float3 tangent1, Corner corner00, Corner corner10,
                Corner corner11, Corner corner01)
            {
                Main = main;
                Normal = normal;
                Tangent0 = tangent0;
                Tangent1 = tangent1;
                Corner00 = corner00;
                Corner10 = corner10;
                Corner11 = corner11;
                Corner01 = corner01;
            }

            internal Corner GetCorner(int index) => index switch
            {
                0 => Corner00,
                1 => Corner10,
                2 => Corner11,
                3 => Corner01,
                _ => throw new ArgumentOutOfRangeException(nameof(index))
            };

            internal Corner GetTriangleVertex(int vertex)
            {
                if ((uint)vertex >= IndicesPerPatch)
                    throw new ArgumentOutOfRangeException(nameof(vertex));
                return GetCorner(TriangleOrder[vertex]);
            }

            public bool Equals(Patch other)
            {
                if (!math.all(Main == other.Main) ||
                    !math.all(Normal == other.Normal) ||
                    !math.all(Tangent0 == other.Tangent0) ||
                    !math.all(Tangent1 == other.Tangent1))
                    return false;
                for (int corner = 0; corner < CornersPerPatch; corner++)
                    if (!GetCorner(corner).Equals(other.GetCorner(corner)))
                        return false;
                return true;
            }

            public override bool Equals(object obj) =>
                obj is Patch other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(
                Main.x, Main.y, Main.z, Normal.x, Normal.y, Normal.z);
        }

        /// <summary>
        /// Caller-owned memo for one export: charts, decoded planes and solved
        /// nodes are pure functions of the context.
        /// </summary>
        internal sealed class SolveCache
        {
            internal readonly Dictionary<int3, int> Charts = new();
            internal readonly Dictionary<int3, (float3 Normal, float Constant)>
                Planes = new();
            // Node of the component that holds the use (line, layer, column);
            // null when that component is invalid or has no contributor.
            internal readonly Dictionary<(LineKey Line, int Layer, int Column),
                Node?> Nodes = new();
        }

        /// <summary>Tie order X before Y before Z, on the raw vector.</summary>
        internal static int DominantAxis(float3 vector)
        {
            float lengthSquared = math.lengthsq(vector);
            if (!(lengthSquared > 0f) || !math.isfinite(lengthSquared))
                throw new ArgumentOutOfRangeException(nameof(vector));
            return DominantAxisOf(vector);
        }

        private static int DominantAxisOf(float3 vector)
        {
            float3 absolute = math.abs(vector);
            return absolute.x >= absolute.y && absolute.x >= absolute.z
                ? 0 : absolute.y >= absolute.z ? 1 : 2;
        }

        internal static void TangentAxes(int dominantAxis,
            out int tangentAxis0, out int tangentAxis1)
        {
            switch (dominantAxis)
            {
                case 0:
                    tangentAxis0 = 1;
                    tangentAxis1 = 2;
                    return;
                case 1:
                    tangentAxis0 = 0;
                    tangentAxis1 = 2;
                    return;
                case 2:
                    tangentAxis0 = 0;
                    tangentAxis1 = 1;
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(dominantAxis));
            }
        }

        // ---- cells ----

        private static bool TryMeasured(int3 coord,
            IReadOnlyDictionary<int3, KernelState> context,
            out KernelState state) =>
            context.TryGetValue(coord, out state) && state.IsOccupied &&
            state.HasMeasuredSurfacePlane;

        private static bool IsKnownFree(in KernelState state) =>
            !state.IsOccupied && state.OccupancyEvidence <=
            MerkabaConstants.ExportKnownFreeThreshold;

        private static bool FreeAt(int3 coord,
            IReadOnlyDictionary<int3, KernelState> context) =>
            context.TryGetValue(coord, out KernelState state) &&
            IsKnownFree(state);

        /// <summary>Unit normal and plane constant dot(C, N) + d of a cell.</summary>
        private static void DecodePlane(int3 coord, KernelState state,
            SolveCache cache, out float3 normal, out float planeConstant)
        {
            if (cache != null && cache.Planes.TryGetValue(coord,
                    out (float3 Normal, float Constant) cached))
            {
                normal = cached.Normal;
                planeConstant = cached.Constant;
                return;
            }
            KernelState.DecodeSurfacePlane(state.Flags, out normal,
                out float signedOffset);
            planeConstant = math.dot((float3)coord *
                MerkabaConstants.LatticeStep, normal) + signedOffset;
            if (cache != null) cache.Planes[coord] = (normal, planeConstant);
        }

        /// <summary>
        /// Canonical sheet side along the chart axis. Exact one-sided FREE
        /// evidence wins. UNKNOWN/UNKNOWN and FREE/FREE are not allowed to
        /// create a third/fourth sheet family: they inherit the orientation
        /// of the measured plane, whose normal is already oriented toward the
        /// observed free side by the RGB-D solve.
        /// </summary>
        private static int Signature(int3 coord, int chart,
            IReadOnlyDictionary<int3, KernelState> context)
        {
            int3 axis = AxisInt3(chart);
            int signature = (FreeAt(coord - axis, context) ? 1 : 0) |
                (FreeAt(coord + axis, context) ? 2 : 0);
            if (signature == 1 || signature == 2)
                return signature;
            if (!TryMeasured(coord, context, out KernelState state))
                return signature;
            KernelState.DecodeSurfacePlane(state.Flags, out float3 normal,
                out _);
            return normal[chart] >= 0f ? 2 : 1;
        }

        // ---- C5.1 canonical chart ----

        internal static int CanonicalChart(int3 coord, KernelState state,
            IReadOnlyDictionary<int3, KernelState> context) =>
            ChartOf(coord, state, context, null);

        private static int ChartOf(int3 coord, KernelState state,
            IReadOnlyDictionary<int3, KernelState> context, SolveCache cache)
        {
            if (cache != null && cache.Charts.TryGetValue(coord, out int known))
                return known;
            DecodePlane(coord, state, cache, out float3 normal,
                out float planeConstant);
            float3 centre = (float3)coord * MerkabaConstants.LatticeStep;
            float3 sum = normal;
            // 26 neighbours, fixed order dx, dy, dz from -1 to +1.
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
            {
                if (dx == 0 && dy == 0 && dz == 0) continue;
                int3 neighbour = coord + new int3(dx, dy, dz);
                if (!TryMeasured(neighbour, context, out KernelState other))
                    continue;
                DecodePlane(neighbour, other, cache, out float3 otherNormal,
                    out float otherConstant);
                if (math.dot(normal, otherNormal) < ChartCompatibleCosine)
                    continue;
                if (math.abs(math.dot((float3)neighbour *
                        MerkabaConstants.LatticeStep, normal) - planeConstant) >
                    BranchHeightGap ||
                    math.abs(math.dot(centre, otherNormal) - otherConstant) >
                    BranchHeightGap)
                    continue;
                int nonZero = (dx != 0 ? 1 : 0) + (dy != 0 ? 1 : 0) +
                    (dz != 0 ? 1 : 0);
                if (nonZero >= 2 &&
                    ((dx != 0 && FreeAt(coord + new int3(dx, 0, 0), context)) ||
                     (dy != 0 && FreeAt(coord + new int3(0, dy, 0), context)) ||
                     (dz != 0 && FreeAt(coord + new int3(0, 0, dz), context))))
                    continue;
                sum += otherNormal;
            }
            int chart = DominantAxisOf(sum);
            if (math.abs(normal[chart]) < MembraneCompatibleAxisCosine)
                chart = DominantAxisOf(normal);
            if (cache != null) cache.Charts[coord] = chart;
            return chart;
        }

        // ---- C5.2 line node ----

        private static int3 ColumnCell(int3 halfAddress, int chart,
            int tangentAxis0, int tangentAxis1, int column, int layer)
        {
            int3 cell = default;
            cell[tangentAxis0] = MerkabaConstants.FloorDiv(
                halfAddress[tangentAxis0], 2) + (column >> 1);
            cell[tangentAxis1] = MerkabaConstants.FloorDiv(
                halfAddress[tangentAxis1], 2) + (column & 1);
            cell[chart] = layer;
            return cell;
        }

        private static int ColumnOf(int3 cell, int3 halfAddress,
            int tangentAxis0, int tangentAxis1) =>
            (cell[tangentAxis0] - MerkabaConstants.FloorDiv(
                halfAddress[tangentAxis0], 2)) * 2 +
            (cell[tangentAxis1] - MerkabaConstants.FloorDiv(
                halfAddress[tangentAxis1], 2));

        private static bool TryLineHeight(float3 normal, float planeConstant,
            int chart, int3 halfAddress, out float height)
        {
            float denominator = normal[chart];
            if (math.abs(denominator) < MembraneCompatibleAxisCosine)
            {
                height = 0f;
                return false;
            }
            float3 point = (float3)halfAddress * MembraneHalfPitch;
            point[chart] = 0f;
            height = (planeConstant - math.dot(point, normal)) / denominator;
            return math.isfinite(height);
        }

        /// <summary>A use of the line: a measured cell of this chart and
        /// side whose plane is usable on the line.</summary>
        private static bool TryUseHeight(int3 cell, int chart, int side,
            int3 halfAddress, IReadOnlyDictionary<int3, KernelState> context,
            SolveCache cache, out float height)
        {
            height = 0f;
            if (!TryMeasured(cell, context, out KernelState state) ||
                ChartOf(cell, state, context, cache) != chart ||
                Signature(cell, chart, context) != side)
                return false;
            DecodePlane(cell, state, cache, out float3 normal,
                out float planeConstant);
            return TryLineHeight(normal, planeConstant, chart, halfAddress,
                out height);
        }

        private static bool FreeInColumn(int3 halfAddress, int chart,
            int tangentAxis0, int tangentAxis1, int column, int layerA,
            int layerB, IReadOnlyDictionary<int3, KernelState> context)
        {
            int low = math.min(layerA, layerB);
            int high = math.max(layerA, layerB);
            for (int layer = low; layer <= high; layer++)
                if (FreeAt(ColumnCell(halfAddress, chart, tangentAxis0,
                        tangentAxis1, column, layer), context))
                    return true;
            return false;
        }

        // (height, layer, column) order used for the median and the seed.
        private static bool OrderBefore(float heightA, int layerA, int columnA,
            float heightB, int layerB, int columnB) =>
            heightA < heightB || (heightA == heightB &&
            (layerA < layerB || (layerA == layerB && columnA < columnB)));

        /// <summary>
        /// The node of the line component that holds the use (layer, column)
        /// (contract C5.2). Returns false when the cell is not a use of the
        /// line, when its component spans more than two layers, or when the
        /// common window holds no contributor.
        /// </summary>
        internal static bool TrySolveNode(LineKey line, int layer, int column,
            IReadOnlyDictionary<int3, KernelState> context, SolveCache cache,
            out Node node)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            var memoKey = (line, layer, column);
            if (cache != null && cache.Nodes.TryGetValue(memoKey, out Node? known))
            {
                node = known ?? default;
                return known.HasValue;
            }
            bool solved = SolveLine(line, layer, column, context, cache,
                out node, out List<(int Layer, int Column)> visited);
            if (cache != null)
                foreach ((int Layer, int Column) use in visited)
                    cache.Nodes[(line, use.Layer, use.Column)] =
                        solved ? node : (Node?)null;
            return solved;
        }

        private static bool SolveLine(LineKey line, int seedLayer,
            int seedColumn, IReadOnlyDictionary<int3, KernelState> context,
            SolveCache cache, out Node node,
            out List<(int Layer, int Column)> visited)
        {
            node = default;
            visited = new List<(int Layer, int Column)>();
            int chart = line.Chart;
            TangentAxes(chart, out int tangentAxis0, out int tangentAxis1);
            int3 halfAddress = line.LineAddress;
            int3 seedCell = ColumnCell(halfAddress, chart, tangentAxis0,
                tangentAxis1, seedColumn, seedLayer);
            if (!TryMeasured(seedCell, context, out KernelState seedState))
                return false;
            if (ChartOf(seedCell, seedState, context, cache) != chart)
                return false;
            int side = Signature(seedCell, chart, context);
            if (!TryUseHeight(seedCell, chart, side, halfAddress, context,
                    cache, out float seedHeight))
                return false;

            // Component: connected set of uses under R (same side,
            // |dL| <= 2, |dh| <= g, no KNOWN FREE in either column between).
            var members = new List<(int Layer, int Column, float Height)>
            {
                (seedLayer, seedColumn, seedHeight)
            };
            visited.Add((seedLayer, seedColumn));
            int minLayer = seedLayer;
            int maxLayer = seedLayer;
            for (int read = 0; read < members.Count; read++)
            {
                (int layerU, int columnU, float heightU) = members[read];
                for (int layerV = layerU - MaxLayerSpan;
                     layerV <= layerU + MaxLayerSpan; layerV++)
                for (int columnV = 0; columnV < 4; columnV++)
                {
                    if (visited.Contains((layerV, columnV))) continue;
                    int3 cell = ColumnCell(halfAddress, chart, tangentAxis0,
                        tangentAxis1, columnV, layerV);
                    if (!TryUseHeight(cell, chart, side, halfAddress, context,
                            cache, out float heightV) ||
                        math.abs(heightU - heightV) > BranchHeightGap ||
                        FreeInColumn(halfAddress, chart, tangentAxis0,
                            tangentAxis1, columnU, layerU, layerV, context) ||
                        FreeInColumn(halfAddress, chart, tangentAxis0,
                            tangentAxis1, columnV, layerU, layerV, context))
                        continue;
                    visited.Add((layerV, columnV));
                    members.Add((layerV, columnV, heightV));
                    minLayer = math.min(minLayer, layerV);
                    maxLayer = math.max(maxLayer, layerV);
                    // Invalid: the uses share no common legal window.
                    if (maxLayer - minLayer > MaxLayerSpan) return false;
                }
            }

            int minColumn = 4;
            foreach ((int Layer, int Column, float Height) member in members)
                if (member.Layer == minLayer)
                    minColumn = math.min(minColumn, member.Column);

            // Reference: median of member heights in (h, L, column) order.
            float reference = 0f;
            int middle = members.Count >> 1;
            float medianLow = 0f;
            float medianHigh = 0f;
            foreach ((int Layer, int Column, float Height) member in members)
            {
                int rank = 0;
                foreach ((int Layer, int Column, float Height) other in members)
                    if (OrderBefore(other.Height, other.Layer, other.Column,
                            member.Height, member.Layer, member.Column))
                        rank++;
                if (rank == middle) medianHigh = member.Height;
                if (middle > 0 && rank == middle - 1) medianLow = member.Height;
            }
            reference = (members.Count & 1) != 0
                ? medianHigh : (medianLow + medianHigh) * 0.5f;

            // Candidates: measured cells of the four columns inside the
            // common window W = [maxL - 1, minL + 1].
            int windowLow = maxLayer - 1;
            int windowHigh = minLayer + 1;
            var candidates = new List<(int Layer, int Column, float Height,
                int MemberDistance, int3 Cell, float3 Normal)>();
            for (int column = 0; column < 4; column++)
            for (int layer = windowLow; layer <= windowHigh; layer++)
            {
                int nearest = NearestMemberLayer(members, layer);
                int3 cell = ColumnCell(halfAddress, chart, tangentAxis0,
                    tangentAxis1, column, layer);
                if (!TryMeasured(cell, context, out KernelState state))
                    continue;
                DecodePlane(cell, state, cache, out float3 normal,
                    out float planeConstant);
                if (!TryLineHeight(normal, planeConstant, chart, halfAddress,
                        out float height) ||
                    Signature(cell, chart, context) != side)
                    continue;
                if (layer != nearest)
                {
                    int step = nearest > layer ? 1 : -1;
                    bool separated = false;
                    for (int between = layer + step; between != nearest + step;
                         between += step)
                        if (FreeAt(ColumnCell(halfAddress, chart, tangentAxis0,
                                tangentAxis1, column, between), context))
                        {
                            separated = true;
                            break;
                        }
                    if (separated) continue;
                }
                candidates.Add((layer, column, height,
                    math.abs(layer - nearest), cell, normal));
            }
            if (candidates.Count == 0) return false;

            // Admission: the connected height component (|dh| <= g) of the
            // candidate nearest the reference ((h, L, column) order on ties).
            int seed = 0;
            for (int index = 1; index < candidates.Count; index++)
            {
                float distance = math.abs(candidates[index].Height - reference);
                float seedDistance = math.abs(candidates[seed].Height -
                    reference);
                if (distance < seedDistance ||
                    (distance == seedDistance && OrderBefore(
                        candidates[index].Height, candidates[index].Layer,
                        candidates[index].Column, candidates[seed].Height,
                        candidates[seed].Layer, candidates[seed].Column)))
                    seed = index;
            }
            var admitted = new bool[candidates.Count];
            admitted[seed] = true;
            for (bool grown = true; grown;)
            {
                grown = false;
                for (int index = 0; index < candidates.Count; index++)
                {
                    if (admitted[index]) continue;
                    for (int other = 0; other < candidates.Count; other++)
                        if (admitted[other] && math.abs(candidates[index].Height -
                                candidates[other].Height) <= BranchHeightGap)
                        {
                            admitted[index] = true;
                            grown = true;
                            break;
                        }
                }
            }

            // Winner per column: nearest the reference, then nearer to a
            // member layer, then the smaller layer. Node = column-order mean.
            float heightSum = 0f;
            int accepted = 0;
            uint red = 0u, green = 0u, blue = 0u, weightTotal = 0u;
            float3 normalSum = float3.zero;
            for (int column = 0; column < 4; column++)
            {
                int best = -1;
                float bestDistance = 0f;
                for (int index = 0; index < candidates.Count; index++)
                {
                    if (!admitted[index] || candidates[index].Column != column)
                        continue;
                    float distance = math.abs(candidates[index].Height -
                        reference);
                    if (best < 0 ||
                        distance < bestDistance - NumericalEpsilon ||
                        (math.abs(distance - bestDistance) <= NumericalEpsilon &&
                         (candidates[index].MemberDistance <
                              candidates[best].MemberDistance ||
                          (candidates[index].MemberDistance ==
                               candidates[best].MemberDistance &&
                           candidates[index].Layer < candidates[best].Layer))))
                    {
                        best = index;
                        bestDistance = distance;
                    }
                }
                if (best < 0) continue;
                heightSum += candidates[best].Height;
                accepted++;
                normalSum += candidates[best].Normal;
                context.TryGetValue(candidates[best].Cell, out KernelState owner);
                uint weight = math.max(1u, owner.ColorConfidence);
                uint packed = owner.PackedColor;
                red += (packed & 255u) * weight;
                green += ((packed >> 8) & 255u) * weight;
                blue += ((packed >> 16) & 255u) * weight;
                weightTotal += weight;
            }
            if (accepted == 0) return false;
            float3 position = (float3)halfAddress * MembraneHalfPitch;
            position[chart] = heightSum / accepted;
            if (!math.all(math.isfinite(position))) return false;
            float inverse = 1f / math.max(1u, weightTotal);
            uint r = math.min(255u, (uint)math.floor(red * inverse + 0.5f));
            uint g = math.min(255u, (uint)math.floor(green * inverse + 0.5f));
            uint b = math.min(255u, (uint)math.floor(blue * inverse + 0.5f));
            node = new Node(new NodeKey(line.LineAddress, chart, side,
                    minLayer, minColumn), position,
                r | (g << 8) | (b << 16) | 0xff000000u,
                math.normalizesafe(normalSum, AxisVector(chart)));
            return true;
        }

        private static int NearestMemberLayer(
            List<(int Layer, int Column, float Height)> members, int layer)
        {
            int nearest = members[0].Layer;
            foreach ((int Layer, int Column, float Height) member in members)
            {
                int distance = math.abs(member.Layer - layer);
                int nearestDistance = math.abs(nearest - layer);
                if (distance < nearestDistance ||
                    (distance == nearestDistance && member.Layer < nearest))
                    nearest = member.Layer;
            }
            return nearest;
        }

        // ---- C5.3 patch ----

        internal static bool TryResolvePatch(int3 main,
            IReadOnlyDictionary<int3, KernelState> context, out Patch patch) =>
            TryResolvePatch(main, context, null, out patch);

        /// <summary>
        /// Isolated-context convenience used by fixtures: every coordinate
        /// other than MAIN is UNKNOWN.
        /// </summary>
        internal static bool TryResolvePatch(int3 main, KernelState state,
            out Patch patch) => TryResolvePatch(main,
            new Dictionary<int3, KernelState> { [main] = state }, null,
            out patch);

        internal static bool TryResolvePatch(int3 main,
            IReadOnlyDictionary<int3, KernelState> context, SolveCache cache,
            out Patch patch)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            patch = default;
            if (!TryMeasured(main, context, out KernelState state)) return false;
            DecodePlane(main, state, cache, out float3 normal,
                out float planeConstant);
            int chart = ChartOf(main, state, context, cache);
            TangentAxes(chart, out int tangentAxis0, out int tangentAxis1);
            float3 tangent0 = AxisVector(tangentAxis0);
            float3 tangent1 = AxisVector(tangentAxis1);
            if (!TryCornerNode(main, chart, tangentAxis0, tangentAxis1, -1, -1,
                    context, cache, out Node node00) ||
                !TryCornerNode(main, chart, tangentAxis0, tangentAxis1, 1, -1,
                    context, cache, out Node node10) ||
                !TryCornerNode(main, chart, tangentAxis0, tangentAxis1, 1, 1,
                    context, cache, out Node node11) ||
                !TryCornerNode(main, chart, tangentAxis0, tangentAxis1, -1, 1,
                    context, cache, out Node node01))
                return false;

            float3 p00 = node00.GridPosition;
            float3 p10 = node10.GridPosition;
            float3 p11 = node11.GridPosition;
            float3 p01 = node01.GridPosition;
            if (!PatchGuards(p00, p10, p11, p01, normal, planeConstant))
                return false;

            // Node addresses are canonical. Only index orientation changes.
            if (math.dot(math.cross(p10 - p00, p11 - p00), normal) < 0f)
            {
                (node10, node01) = (node01, node10);
                (tangent0, tangent1) = (tangent1, tangent0);
            }
            patch = new Patch(main, normal, tangent0, tangent1,
                new Corner(node00), new Corner(node10), new Corner(node11),
                new Corner(node01));
            return true;
        }

        private static bool TryCornerNode(int3 main, int chart,
            int tangentAxis0, int tangentAxis1, int sign0, int sign1,
            IReadOnlyDictionary<int3, KernelState> context, SolveCache cache,
            out Node node)
        {
            int3 halfAddress = main * 2;
            halfAddress[tangentAxis0] += sign0;
            halfAddress[tangentAxis1] += sign1;
            var line = new LineKey(halfAddress, chart);
            int column = ColumnOf(main, line.LineAddress, tangentAxis0,
                tangentAxis1);
            return TrySolveNode(line, main[chart], column, context, cache,
                out node);
        }

        /// <summary>Every edge <= 45 mm and every corner within g of the
        /// MAIN plane (reject, never clamp).</summary>
        private static bool PatchGuards(float3 p00, float3 p10, float3 p11,
            float3 p01, float3 normal, float planeConstant)
        {
            float maxEdge = MembraneMaxEdge * MembraneMaxEdge;
            if (math.distancesq(p00, p10) > maxEdge ||
                math.distancesq(p10, p11) > maxEdge ||
                math.distancesq(p11, p01) > maxEdge ||
                math.distancesq(p01, p00) > maxEdge)
                return false;
            return math.abs(math.dot(p00, normal) - planeConstant) <=
                       BranchHeightGap &&
                   math.abs(math.dot(p10, normal) - planeConstant) <=
                       BranchHeightGap &&
                   math.abs(math.dot(p11, normal) - planeConstant) <=
                       BranchHeightGap &&
                   math.abs(math.dot(p01, normal) - planeConstant) <=
                       BranchHeightGap;
        }

        // ---- C5.4 chart transition ----

        /// <summary>
        /// The quad between MAIN's +t edge nodes and the -t edge nodes of the
        /// measured neighbour n = MAIN + e_t whose chart is the other tangent
        /// (contract C5.4). Owner is MAIN; valid across tile edges.
        /// </summary>
        internal static bool TryBuildTransition(int3 main, int tangentAxis,
            IReadOnlyDictionary<int3, KernelState> context, SolveCache cache,
            out Patch transition)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            transition = default;
            if (!TryMeasured(main, context, out KernelState mainState))
                return false;
            int chart = ChartOf(main, mainState, context, cache);
            TangentAxes(chart, out int tangentAxis0, out int tangentAxis1);
            if (tangentAxis != tangentAxis0 && tangentAxis != tangentAxis1)
                return false;
            int3 neighbour = main + AxisInt3(tangentAxis);
            if (!TryMeasured(neighbour, context, out KernelState otherState))
                return false;
            int otherChart = ChartOf(neighbour, otherState, context, cache);
            if (otherChart == chart || otherChart == tangentAxis) return false;
            DecodePlane(main, mainState, cache, out float3 mainNormal,
                out float mainConstant);
            DecodePlane(neighbour, otherState, cache, out float3 otherNormal,
                out float otherConstant);
            if (!TransitionCompatible(main, mainNormal, mainConstant, neighbour,
                    otherNormal, otherConstant))
                return false;

            if (!TryEdgeNode(main, chart, tangentAxis, 1, otherChart, -1,
                    context, cache, out Node mainLow) ||
                !TryEdgeNode(main, chart, tangentAxis, 1, otherChart, 1,
                    context, cache, out Node mainHigh) ||
                !TryEdgeNode(neighbour, otherChart, tangentAxis, -1, chart, -1,
                    context, cache, out Node otherLow) ||
                !TryEdgeNode(neighbour, otherChart, tangentAxis, -1, chart, 1,
                    context, cache, out Node otherHigh))
                return false;
            if (mainLow.Key.Equals(mainHigh.Key) ||
                mainLow.Key.Equals(otherLow.Key) ||
                mainLow.Key.Equals(otherHigh.Key) ||
                mainHigh.Key.Equals(otherLow.Key) ||
                mainHigh.Key.Equals(otherHigh.Key) ||
                otherLow.Key.Equals(otherHigh.Key))
                return false;

            float3 a = mainLow.GridPosition;
            float3 b = mainHigh.GridPosition;
            if (!TransitionQuad(a, b, otherLow.GridPosition,
                    otherHigh.GridPosition, mainNormal + otherNormal,
                    tangentAxis, out bool matchHigh, out bool flip))
                return false;
            Node c = matchHigh ? otherHigh : otherLow;
            Node d = matchHigh ? otherLow : otherHigh;
            Node second = mainHigh;
            if (flip) (second, d) = (d, second);
            transition = new Patch(main,
                math.normalizesafe(mainNormal + otherNormal, mainNormal),
                AxisVector(tangentAxis), AxisVector(otherChart),
                new Corner(mainLow), new Corner(second), new Corner(c),
                new Corner(d));
            return true;
        }

        private static bool TryEdgeNode(int3 cell, int chart, int edgeAxis,
            int edgeSign, int alongAxis, int alongSign,
            IReadOnlyDictionary<int3, KernelState> context, SolveCache cache,
            out Node node)
        {
            TangentAxes(chart, out int tangentAxis0, out int tangentAxis1);
            int3 halfAddress = cell * 2;
            halfAddress[edgeAxis] += edgeSign;
            halfAddress[alongAxis] += alongSign;
            var line = new LineKey(halfAddress, chart);
            int column = ColumnOf(cell, line.LineAddress, tangentAxis0,
                tangentAxis1);
            return TrySolveNode(line, cell[chart], column, context, cache,
                out node);
        }

        private static bool TransitionCompatible(int3 first, float3 firstNormal,
            float firstConstant, int3 second, float3 secondNormal,
            float secondConstant)
        {
            if (math.dot(firstNormal, secondNormal) < ChartCompatibleCosine)
                return false;
            float3 firstCentre = (float3)first * MerkabaConstants.LatticeStep;
            float3 secondCentre = (float3)second * MerkabaConstants.LatticeStep;
            return math.abs(math.dot(secondCentre, firstNormal) -
                       firstConstant) <= BranchHeightGap &&
                   math.abs(math.dot(firstCentre, secondNormal) -
                       secondConstant) <= BranchHeightGap;
        }

        /// <summary>
        /// Quad a, b (MAIN edge) and the facing edge oriented co-directed with
        /// b - a. Edges <= 45 mm, cos >= 0.5, both triangles non-degenerate and
        /// equally oriented. All four nodes lie in the plane t = +half, so the
        /// winding follows the reference normal when it has a component along
        /// that plane's normal and +t otherwise.
        /// </summary>
        private static bool TransitionQuad(float3 a, float3 b, float3 low,
            float3 high, float3 reference, int tangentAxis, out bool matchHigh,
            out bool flip)
        {
            flip = false;
            float3 edge = b - a;
            float3 facing = high - low;
            matchHigh = math.dot(edge, facing) >= 0f;
            float3 c = matchHigh ? high : low;
            float3 d = matchHigh ? low : high;
            float3 facingDirected = c - d;
            float edgeLength = math.length(edge);
            float facingLength = math.length(facingDirected);
            if (!(edgeLength > NumericalEpsilon) ||
                !(facingLength > NumericalEpsilon) ||
                math.dot(edge, facingDirected) <
                TransitionEdgeCosine * edgeLength * facingLength)
                return false;
            float maxEdge = MembraneMaxEdge * MembraneMaxEdge;
            if (math.distancesq(a, b) > maxEdge ||
                math.distancesq(b, c) > maxEdge ||
                math.distancesq(c, d) > maxEdge ||
                math.distancesq(d, a) > maxEdge)
                return false;
            float3 first = math.cross(b - a, c - a);
            float3 second = math.cross(c - a, d - a);
            if (!(math.lengthsq(first) > NumericalEpsilon * NumericalEpsilon) ||
                !(math.lengthsq(second) > NumericalEpsilon * NumericalEpsilon) ||
                math.dot(first, second) <= 0f)
                return false;
            float3 quadNormal = first + second;
            float orientation = math.dot(quadNormal, reference);
            if (math.abs(orientation) <= NumericalEpsilon)
                orientation = quadNormal[tangentAxis];
            flip = orientation < 0f;
            return true;
        }

        internal static int3 NearestGridNormalStep(float3 gridNormal)
        {
            gridNormal = math.normalize(gridNormal);
            float3 magnitude = math.abs(gridNormal);
            int3 direction = new(gridNormal.x >= 0f ? 1 : -1,
                gridNormal.y >= 0f ? 1 : -1,
                gridNormal.z >= 0f ? 1 : -1);
            float axisScore = math.max(magnitude.x,
                math.max(magnitude.y, magnitude.z));
            float3 faceScores = new float3(magnitude.x + magnitude.y,
                magnitude.x + magnitude.z, magnitude.y + magnitude.z) *
                0.70710678118f;
            float faceScore = math.max(faceScores.x,
                math.max(faceScores.y, faceScores.z));
            float bodyScore = math.csum(magnitude) * 0.57735026919f;
            if (axisScore >= faceScore && axisScore >= bodyScore)
            {
                if (magnitude.x >= magnitude.y && magnitude.x >= magnitude.z)
                    return new int3(direction.x, 0, 0);
                if (magnitude.y >= magnitude.z)
                    return new int3(0, direction.y, 0);
                return new int3(0, 0, direction.z);
            }
            if (faceScore >= bodyScore)
            {
                if (faceScores.x >= faceScores.y &&
                    faceScores.x >= faceScores.z)
                    return new int3(direction.x, direction.y, 0);
                if (faceScores.y >= faceScores.z)
                    return new int3(direction.x, 0, direction.z);
                return new int3(0, direction.y, direction.z);
            }
            return direction;
        }

        private static float3 AxisVector(int axis) => axis switch
        {
            0 => new float3(1f, 0f, 0f),
            1 => new float3(0f, 1f, 0f),
            2 => new float3(0f, 0f, 1f),
            _ => throw new ArgumentOutOfRangeException(nameof(axis))
        };

        private static int3 AxisInt3(int axis) => axis switch
        {
            0 => new int3(1, 0, 0),
            1 => new int3(0, 1, 0),
            2 => new int3(0, 0, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(axis))
        };

#if UNITY_EDITOR
        internal static string BuildSurfaceOrientationHlsl()
        {
            var output = new StringBuilder();
            output.AppendLine("// GENERATED from MerkabaOverlapShell.cs. DO NOT EDIT.");
            output.AppendLine("#ifndef GENESIS_MERKABA_SURFACE_ORIENTATION_INCLUDED");
            output.AppendLine("#define GENESIS_MERKABA_SURFACE_ORIENTATION_INCLUDED");
            output.AppendLine();
            output.AppendLine($"#define MERKABA_SURFACE_PLANE_NORMAL_U_SHIFT {MerkabaConstants.SurfacePlaneNormalUShift}u");
            output.AppendLine($"#define MERKABA_SURFACE_PLANE_NORMAL_V_SHIFT {MerkabaConstants.SurfacePlaneNormalVShift}u");
            output.AppendLine($"#define MERKABA_SURFACE_PLANE_OFFSET_SHIFT {MerkabaConstants.SurfacePlaneOffsetShift}u");
            output.AppendLine($"#define MERKABA_SURFACE_PLANE_NORMAL_MASK 0x{MerkabaConstants.SurfacePlaneNormalMask:x}u");
            output.AppendLine($"#define MERKABA_SURFACE_PLANE_OFFSET_MASK 0x{MerkabaConstants.SurfacePlaneOffsetMask:x}u");
            output.AppendLine($"#define MERKABA_SURFACE_PLANE_VALID_FLAG 0x{MerkabaConstants.SurfacePlaneValidFlag:x}u");
            output.AppendLine($"#define MERKABA_SURFACE_PLANE_STORAGE_MASK 0x{MerkabaConstants.SurfacePlaneStorageMask:x}u");
            output.AppendLine($"#define MERKABA_SURFACE_PLANE_OFFSET_RANGE {MerkabaConstants.SurfacePlaneOffsetRange.ToString("R", CultureInfo.InvariantCulture)}");
            output.AppendLine();
            output.AppendLine("bool M8HasSurfacePlane(uint flags)");
            output.AppendLine("{");
            output.AppendLine("    return (flags & MERKABA_SURFACE_PLANE_VALID_FLAG) != 0u;");
            output.AppendLine("}");
            output.AppendLine();
            output.AppendLine("float2 M8OctSignNotZero(float2 value)");
            output.AppendLine("{");
            output.AppendLine("    return float2(value.x >= 0.0 ? 1.0 : -1.0,");
            output.AppendLine("        value.y >= 0.0 ? 1.0 : -1.0);");
            output.AppendLine("}");
            output.AppendLine();
            output.AppendLine("float M8SurfacePlaneFirstNonZero(float3 value)");
            output.AppendLine("{");
            output.AppendLine("    return value.x != 0.0 ? value.x :");
            output.AppendLine("        value.y != 0.0 ? value.y : value.z;");
            output.AppendLine("}");
            output.AppendLine();
            output.AppendLine("float2 M8OctEncode(float3 normal)");
            output.AppendLine("{");
            output.AppendLine("    normal /= dot(abs(normal), 1.0.xxx);");
            output.AppendLine("    float2 oct = normal.xy;");
            output.AppendLine("    if (normal.z < 0.0)");
            output.AppendLine("        oct = (1.0 - abs(oct.yx)) * M8OctSignNotZero(oct);");
            output.AppendLine("    return oct;");
            output.AppendLine("}");
            output.AppendLine();
            output.AppendLine("float3 M8OctDecode(float2 oct)");
            output.AppendLine("{");
            output.AppendLine("    float3 normal = float3(oct, 1.0 - abs(oct.x) - abs(oct.y));");
            output.AppendLine("    if (normal.z < 0.0)");
            output.AppendLine("        normal.xy = (1.0 - abs(normal.yx)) * M8OctSignNotZero(normal.xy);");
            output.AppendLine("    return normalize(normal);");
            output.AppendLine("}");
            output.AppendLine();
            output.AppendLine("uint M8SetSurfacePlane(uint flags, float3 normal, float signedOffset)");
            output.AppendLine("{");
            output.AppendLine("    normal = normalize(normal);");
            output.AppendLine("    if (M8SurfacePlaneFirstNonZero(normal) < 0.0)");
            output.AppendLine("    {");
            output.AppendLine("        normal = -normal;");
            output.AppendLine("        signedOffset = -signedOffset;");
            output.AppendLine("    }");
            output.AppendLine("    float2 oct = M8OctEncode(normal);");
            output.AppendLine("    uint encodedU = (uint)clamp(round((oct.x * 0.5 + 0.5) * 1023.0), 0.0, 1023.0);");
            output.AppendLine("    uint encodedV = (uint)clamp(round((oct.y * 0.5 + 0.5) * 1023.0), 0.0, 1023.0);");
            output.AppendLine("    int encodedOffset = (int)round(clamp(signedOffset /");
            output.AppendLine("        MERKABA_SURFACE_PLANE_OFFSET_RANGE, -1.0, 1.0) * 127.0);");
            output.AppendLine("    uint payload =");
            output.AppendLine("        (encodedU << MERKABA_SURFACE_PLANE_NORMAL_U_SHIFT) |");
            output.AppendLine("        (encodedV << MERKABA_SURFACE_PLANE_NORMAL_V_SHIFT) |");
            output.AppendLine("        ((uint(encodedOffset) & MERKABA_SURFACE_PLANE_OFFSET_MASK) <<");
            output.AppendLine("            MERKABA_SURFACE_PLANE_OFFSET_SHIFT) |");
            output.AppendLine("        MERKABA_SURFACE_PLANE_VALID_FLAG;");
            output.AppendLine("    return (flags & ~MERKABA_SURFACE_PLANE_STORAGE_MASK) | payload;");
            output.AppendLine("}");
            output.AppendLine();
            output.AppendLine("void M8DecodeSurfacePlane(uint flags, out float3 normal, out float signedOffset)");
            output.AppendLine("{");
            output.AppendLine("    uint encodedU = (flags >> MERKABA_SURFACE_PLANE_NORMAL_U_SHIFT) &");
            output.AppendLine("        MERKABA_SURFACE_PLANE_NORMAL_MASK;");
            output.AppendLine("    uint encodedV = (flags >> MERKABA_SURFACE_PLANE_NORMAL_V_SHIFT) &");
            output.AppendLine("        MERKABA_SURFACE_PLANE_NORMAL_MASK;");
            output.AppendLine("    normal = M8OctDecode(float2(encodedU, encodedV) / 1023.0 * 2.0 - 1.0);");
            output.AppendLine("    uint rawOffset = (flags >> MERKABA_SURFACE_PLANE_OFFSET_SHIFT) &");
            output.AppendLine("        MERKABA_SURFACE_PLANE_OFFSET_MASK;");
            output.AppendLine("    int encodedOffset = rawOffset >= 128u ? int(rawOffset) - 256 : int(rawOffset);");
            output.AppendLine("    signedOffset = (float)encodedOffset / 127.0 *");
            output.AppendLine("        MERKABA_SURFACE_PLANE_OFFSET_RANGE;");
            output.AppendLine("}");
            output.AppendLine();
            output.AppendLine("uint M8ClearSurfacePlane(uint flags)");
            output.AppendLine("{");
            output.AppendLine("    return flags & ~MERKABA_SURFACE_PLANE_STORAGE_MASK;");
            output.AppendLine("}");
            output.AppendLine();
            output.AppendLine("#endif");
            return output.ToString();
        }

        internal static string BuildGeneratedHlsl()
        {
            string source = @"// GENERATED from MerkabaOverlapShell.cs. DO NOT EDIT.
__HASH__ifndef GENESIS_MERKABA_OVERLAP_SHELL_INCLUDED
__HASH__define GENESIS_MERKABA_OVERLAP_SHELL_INCLUDED

__HASH__include ""MerkabaSurfaceOrientation.generated.hlsl""

__HASH__define M8_MEMBRANE_TRIANGLES_PER_PATCH __TRIANGLES__u
__HASH__define M8_MEMBRANE_VERTICES_PER_PATCH __VERTICES__u
__HASH__define M8_MEMBRANE_INDICES_PER_PATCH __INDICES__u
__HASH__define M8_MEMBRANE_PATCH_PITCH __PITCH__
__HASH__define M8_MEMBRANE_HALF_PITCH __HALF_PITCH__
__HASH__define M8_MEMBRANE_NUMERICAL_EPSILON 1.0e-6
// A plane is usable on a line of chart c only while |N[c]| >= this.
__HASH__define M8_MEMBRANE_COMPATIBLE_AXIS_COSINE 0.5
// g: branch height gap and local-sheet plane agreement.
__HASH__define M8_MEMBRANE_BRANCH_GAP (M8_MEMBRANE_PATCH_PITCH * 0.6)
__HASH__define M8_MEMBRANE_CHART_COSINE 0.5
__HASH__define M8_MEMBRANE_MAX_EDGE 0.045
// The uses of one node span at most two normal layers.
__HASH__define M8_MEMBRANE_MAX_LAYER_SPAN 2
__HASH__define M8_MEMBRANE_TRANSITION_EDGE_COSINE 0.5

// The kernel that includes this oracle defines the cell accessors:
//   bool M8MembraneCell(int3, out bool resolved, out bool measured,
//       out bool knownFree)
//   float4 M8MembranePlaneOf(int3)   unit normal, dot(C, N) + d
//   int M8MembraneChartOf(int3)      the C5.1 chart of a measured cell
//   void M8LoadMembraneColor(int3, out uint packed, out uint confidence)

int M8MembraneDominantAxis(float3 value)
{
    float3 magnitude = abs(value);
    return magnitude.x >= magnitude.y && magnitude.x >= magnitude.z ? 0 :
        magnitude.y >= magnitude.z ? 1 : 2;
}

void M8MembraneTangentAxes(int dominantAxis, out int tangentAxis0,
    out int tangentAxis1)
{
    tangentAxis0 = dominantAxis == 0 ? 1 : 0;
    tangentAxis1 = dominantAxis == 2 ? 1 : 2;
}

int3 M8MembraneAxis(int axis)
{
    return axis == 0 ? int3(1, 0, 0) :
        axis == 1 ? int3(0, 1, 0) : int3(0, 0, 1);
}

int3 M8MembraneSetIntComponent(int3 value, int axis, int component)
{
    if (axis == 0) value.x = component;
    else if (axis == 1) value.y = component;
    else value.z = component;
    return value;
}

float3 M8MembraneSetFloatComponent(float3 value, int axis, float component)
{
    if (axis == 0) value.x = component;
    else if (axis == 1) value.y = component;
    else value.z = component;
    return value;
}

// Floor division by two (arithmetic shift).
int M8MembraneFloorDiv2(int value)
{
    return value >> 1;
}

bool M8MembraneMeasuredAt(int3 coord)
{
    bool resolved;
    bool measured;
    bool knownFree;
    bool exists = M8MembraneCell(coord, resolved, measured, knownFree);
    return exists && measured;
}

bool M8MembraneFreeAt(int3 coord)
{
    bool resolved;
    bool measured;
    bool knownFree;
    bool exists = M8MembraneCell(coord, resolved, measured, knownFree);
    return exists && knownFree;
}

// Canonical sheet side along the chart axis. Exact one-sided FREE evidence
// wins. UNKNOWN/UNKNOWN and FREE/FREE fall back to the measured plane
// orientation, so incomplete free-space evidence cannot split one physical
// sheet into disconnected node families.
uint M8MembraneSignature(int3 coord, int chart)
{
    int3 axis = M8MembraneAxis(chart);
    uint signature = M8MembraneFreeAt(coord - axis) ? 1u : 0u;
    if (M8MembraneFreeAt(coord + axis)) signature |= 2u;
    if (signature == 1u || signature == 2u)
        return signature;
    if (!M8MembraneMeasuredAt(coord))
        return signature;
    float4 plane = M8MembranePlaneOf(coord);
    return plane[chart] >= 0.0 ? 2u : 1u;
}

// C5.1 canonical chart of a measured cell: dominant axis of its normal plus
// the normals of its 26 neighbours of the same local sheet (measured,
// dot >= 0.5 without abs, planes agreeing both ways within g, no KNOWN FREE
// on the single-axis steps of an edge or corner neighbour). Fixed order dx,
// dy, dz; equal weights; tie X, Y, Z. Falls back to the raw dominant axis of
// the cell when its own |N[chart]| < 0.5.
int M8MembraneCanonicalChart(int3 coord)
{
    float4 plane = M8MembranePlaneOf(coord);
    float3 normal = plane.xyz;
    float3 centre = (float3)coord * M8_MEMBRANE_PATCH_PITCH;
    float3 sum = normal;
    [loop]
    for (int dx = -1; dx <= 1; dx++)
    [loop]
    for (int dy = -1; dy <= 1; dy++)
    [loop]
    for (int dz = -1; dz <= 1; dz++)
    {
        if (dx == 0 && dy == 0 && dz == 0) continue;
        int3 neighbour = coord + int3(dx, dy, dz);
        if (!M8MembraneMeasuredAt(neighbour)) continue;
        float4 otherPlane = M8MembranePlaneOf(neighbour);
        if (dot(normal, otherPlane.xyz) < M8_MEMBRANE_CHART_COSINE) continue;
        if (abs(dot((float3)neighbour * M8_MEMBRANE_PATCH_PITCH, normal) -
                plane.w) > M8_MEMBRANE_BRANCH_GAP ||
            abs(dot(centre, otherPlane.xyz) - otherPlane.w) >
                M8_MEMBRANE_BRANCH_GAP)
            continue;
        int nonZero = (dx != 0 ? 1 : 0) + (dy != 0 ? 1 : 0) +
            (dz != 0 ? 1 : 0);
        if (nonZero >= 2)
        {
            if (dx != 0 && M8MembraneFreeAt(coord + int3(dx, 0, 0))) continue;
            if (dy != 0 && M8MembraneFreeAt(coord + int3(0, dy, 0))) continue;
            if (dz != 0 && M8MembraneFreeAt(coord + int3(0, 0, dz))) continue;
        }
        sum += otherPlane.xyz;
    }
    int chart = M8MembraneDominantAxis(sum);
    if (abs(normal[chart]) < M8_MEMBRANE_COMPATIBLE_AXIS_COSINE)
        chart = M8MembraneDominantAxis(normal);
    return chart;
}

// ---- C5.2 line node ----

int3 M8MembraneColumnCell(int3 halfAddress, int chart, int tangentAxis0,
    int tangentAxis1, uint column, int layer)
{
    int3 cell = int3(0, 0, 0);
    cell = M8MembraneSetIntComponent(cell, tangentAxis0,
        M8MembraneFloorDiv2(halfAddress[tangentAxis0]) + (int)(column >> 1u));
    cell = M8MembraneSetIntComponent(cell, tangentAxis1,
        M8MembraneFloorDiv2(halfAddress[tangentAxis1]) + (int)(column & 1u));
    return M8MembraneSetIntComponent(cell, chart, layer);
}

bool M8MembraneLineHeight(float4 plane, int chart, int3 halfAddress,
    out float height)
{
    height = 0.0;
    float denominator = plane[chart];
    if (abs(denominator) < M8_MEMBRANE_COMPATIBLE_AXIS_COSINE) return false;
    float3 linePoint = M8MembraneSetFloatComponent(
        (float3)halfAddress * M8_MEMBRANE_HALF_PITCH, chart, 0.0);
    height = (plane.w - dot(linePoint, plane.xyz)) / denominator;
    return isfinite(height);
}

// A use of the line: a measured cell of this chart and side whose plane is
// usable on the line.
bool M8MembraneUseHeight(int3 cell, int chart, uint side, int3 halfAddress,
    out float height)
{
    height = 0.0;
    if (!M8MembraneMeasuredAt(cell)) return false;
    if (M8MembraneChartOf(cell) != chart) return false;
    if (M8MembraneSignature(cell, chart) != side) return false;
    return M8MembraneLineHeight(M8MembranePlaneOf(cell), chart, halfAddress,
        height);
}

bool M8MembraneFreeInColumn(int3 halfAddress, int chart, int tangentAxis0,
    int tangentAxis1, uint column, int layerA, int layerB)
{
    int low = min(layerA, layerB);
    int high = max(layerA, layerB);
    [loop]
    for (int layer = low; layer <= high; layer++)
    {
        if (M8MembraneFreeAt(M8MembraneColumnCell(halfAddress, chart,
                tangentAxis0, tangentAxis1, column, layer)))
            return true;
    }
    return false;
}

// Relation R of two uses of one side: |dL| <= 2, |dh| <= g, no KNOWN FREE in
// either column between their layers.
bool M8MembraneRelated(int3 halfAddress, int chart, int tangentAxis0,
    int tangentAxis1, uint columnU, int layerU, float heightU, uint columnV,
    int layerV, float heightV)
{
    if (abs(layerU - layerV) > M8_MEMBRANE_MAX_LAYER_SPAN) return false;
    if (abs(heightU - heightV) > M8_MEMBRANE_BRANCH_GAP) return false;
    if (M8MembraneFreeInColumn(halfAddress, chart, tangentAxis0,
            tangentAxis1, columnU, layerU, layerV))
        return false;
    return !M8MembraneFreeInColumn(halfAddress, chart, tangentAxis0,
        tangentAxis1, columnV, layerU, layerV);
}

// (height, layer, column) order of the median and the admission seed.
bool M8MembraneOrderBefore(float heightA, int layerA, uint columnA,
    float heightB, int layerB, uint columnB)
{
    return heightA < heightB || (heightA == heightB &&
        (layerA < layerB || (layerA == layerB && columnA < columnB)));
}

// A contributor of the common window W: measured, usable plane, same side,
// no KNOWN FREE between its layer and the nearest member layer (inclusive).
bool M8MembraneCandidateHeight(int3 halfAddress, int chart, int tangentAxis0,
    int tangentAxis1, uint side, uint column, int layer,
    int nearestMemberLayer, out float height, out float3 normal)
{
    height = 0.0;
    normal = 0.0;
    int3 cell = M8MembraneColumnCell(halfAddress, chart, tangentAxis0,
        tangentAxis1, column, layer);
    if (!M8MembraneMeasuredAt(cell)) return false;
    float4 plane = M8MembranePlaneOf(cell);
    normal = plane.xyz;
    if (!M8MembraneLineHeight(plane, chart, halfAddress, height)) return false;
    if (M8MembraneSignature(cell, chart) != side) return false;
    int distance = abs(nearestMemberLayer - layer);
    int step = nearestMemberLayer > layer ? 1 : -1;
    [loop]
    for (int offset = 1; offset <= distance; offset++)
    {
        if (M8MembraneFreeAt(M8MembraneColumnCell(halfAddress, chart,
                tangentAxis0, tangentAxis1, column, layer + step * offset)))
            return false;
    }
    return true;
}

// Winner per column: nearest the reference, then nearer to a member layer,
// then the smaller layer.
bool M8MembraneWinnerBefore(float distance, int memberDistance, int layer,
    bool haveBest, float bestDistance, int bestMemberDistance, int bestLayer)
{
    return !haveBest ||
        distance < bestDistance - M8_MEMBRANE_NUMERICAL_EPSILON ||
        (abs(distance - bestDistance) <= M8_MEMBRANE_NUMERICAL_EPSILON &&
         (memberDistance < bestMemberDistance ||
          (memberDistance == bestMemberDistance && layer < bestLayer)));
}

uint M8MembranePackMeanColor(uint red, uint green, uint blue,
    uint weightTotal)
{
    float inverse = 1.0 / (float)max(1u, weightTotal);
    uint r = min(255u, (uint)floor((float)red * inverse + 0.5));
    uint g = min(255u, (uint)floor((float)green * inverse + 0.5));
    uint b = min(255u, (uint)floor((float)blue * inverse + 0.5));
    return r | (g << 8u) | (b << 16u) | 0xff000000u;
}

float3 M8MembraneNodeNormal(float3 normalSum, int chart)
{
    float lengthSquared = dot(normalSum, normalSum);
    return lengthSquared > 1.17549435e-38
        ? normalSum * rsqrt(lengthSquared)
        : (float3)M8MembraneAxis(chart);
}

float3 M8MembraneNodePosition(int3 halfAddress, int chart, float height)
{
    return M8MembraneSetFloatComponent(
        (float3)halfAddress * M8_MEMBRANE_HALF_PITCH, chart, height);
}

// ---- C5.3 patch ----

// Every edge <= 45 mm and every corner within g of the MAIN plane.
bool M8MembranePatchGuards(float3 p00, float3 p10, float3 p11, float3 p01,
    float4 plane)
{
    float maxEdge = M8_MEMBRANE_MAX_EDGE * M8_MEMBRANE_MAX_EDGE;
    float3 e0 = p00 - p10;
    float3 e1 = p10 - p11;
    float3 e2 = p11 - p01;
    float3 e3 = p01 - p00;
    if (dot(e0, e0) > maxEdge || dot(e1, e1) > maxEdge ||
        dot(e2, e2) > maxEdge || dot(e3, e3) > maxEdge)
        return false;
    return abs(dot(p00, plane.xyz) - plane.w) <= M8_MEMBRANE_BRANCH_GAP &&
        abs(dot(p10, plane.xyz) - plane.w) <= M8_MEMBRANE_BRANCH_GAP &&
        abs(dot(p11, plane.xyz) - plane.w) <= M8_MEMBRANE_BRANCH_GAP &&
        abs(dot(p01, plane.xyz) - plane.w) <= M8_MEMBRANE_BRANCH_GAP;
}

// ---- C5.4 chart transition ----

bool M8MembraneTransitionCompatible(int3 first, float4 firstPlane,
    int3 second, float4 secondPlane)
{
    if (dot(firstPlane.xyz, secondPlane.xyz) < M8_MEMBRANE_CHART_COSINE)
        return false;
    float3 firstCentre = (float3)first * M8_MEMBRANE_PATCH_PITCH;
    float3 secondCentre = (float3)second * M8_MEMBRANE_PATCH_PITCH;
    return abs(dot(secondCentre, firstPlane.xyz) - firstPlane.w) <=
            M8_MEMBRANE_BRANCH_GAP &&
        abs(dot(firstCentre, secondPlane.xyz) - secondPlane.w) <=
            M8_MEMBRANE_BRANCH_GAP;
}

// Quad a, b (MAIN edge) and the facing edge oriented co-directed with b - a:
// edges <= 45 mm, cos >= 0.5, both triangles non-degenerate and equally
// oriented. All four nodes lie in the plane t = +half, so the winding follows
// the reference normal when it has a component along that plane's normal and
// +t otherwise.
bool M8MembraneTransitionQuad(float3 a, float3 b, float3 low, float3 high,
    float3 referenceNormal, int tangentAxis, out bool matchHigh,
    out bool flip)
{
    flip = false;
    float3 edge = b - a;
    matchHigh = dot(edge, high - low) >= 0.0;
    float3 c = matchHigh ? high : low;
    float3 d = matchHigh ? low : high;
    float3 facing = c - d;
    float edgeLength = length(edge);
    float facingLength = length(facing);
    if (!(edgeLength > M8_MEMBRANE_NUMERICAL_EPSILON) ||
        !(facingLength > M8_MEMBRANE_NUMERICAL_EPSILON) ||
        dot(edge, facing) < M8_MEMBRANE_TRANSITION_EDGE_COSINE * edgeLength *
            facingLength)
        return false;
    float maxEdge = M8_MEMBRANE_MAX_EDGE * M8_MEMBRANE_MAX_EDGE;
    float3 ab = a - b;
    float3 bc = b - c;
    float3 cd = c - d;
    float3 da = d - a;
    if (dot(ab, ab) > maxEdge || dot(bc, bc) > maxEdge ||
        dot(cd, cd) > maxEdge || dot(da, da) > maxEdge)
        return false;
    float3 first = cross(b - a, c - a);
    float3 second = cross(c - a, d - a);
    float epsilon2 = M8_MEMBRANE_NUMERICAL_EPSILON *
        M8_MEMBRANE_NUMERICAL_EPSILON;
    if (!(dot(first, first) > epsilon2) || !(dot(second, second) > epsilon2) ||
        dot(first, second) <= 0.0)
        return false;
    float3 quadNormal = first + second;
    float orientation = dot(quadNormal, referenceNormal);
    if (abs(orientation) <= M8_MEMBRANE_NUMERICAL_EPSILON)
        orientation = quadNormal[tangentAxis];
    flip = orientation < 0.0;
    return true;
}

__HASH__endif
";
            return source
                .Replace("__HASH__", "#")
                .Replace("__TRIANGLES__", TrianglesPerPatch.ToString(
                    CultureInfo.InvariantCulture))
                .Replace("__VERTICES__", VerticesPerPatch.ToString(
                    CultureInfo.InvariantCulture))
                .Replace("__INDICES__", IndicesPerPatch.ToString(
                    CultureInfo.InvariantCulture))
                .Replace("__PITCH__", MembranePatchPitch.ToString("R",
                    CultureInfo.InvariantCulture))
                .Replace("__HALF_PITCH__", MembraneHalfPitch.ToString("R",
                    CultureInfo.InvariantCulture));
        }
#endif
    }
}
