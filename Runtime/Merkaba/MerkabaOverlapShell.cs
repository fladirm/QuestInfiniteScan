using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Unity.Mathematics;

namespace Genesis.RoomScan
{
    /// <summary>
    /// CPU/codegen authority for the disposable measured M8 membrane.
    /// KernelState remains the only persistent world state.
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
        // A contributor plane must be well conditioned against the knot line
        // of its chart; the connected height branch and the free side decide
        // sheet membership (MERKABA_GEOMETRY_REVIEW FINDING B).
        internal const float MembraneCompatibleAxisCosine = 0.5f;
        // Two heights on one knot line belong to one branch when closer than
        // this; a face neighbour belongs to one local sheet when the planes
        // agree within it (both directions).
        internal const float BranchHeightGap = MembranePatchPitch * 0.6f;
        // Face neighbours whose normals agree at least this much (no abs: the
        // two sides of a thin leaf never mix) shape the canonical chart.
        internal const float ChartCompatibleCosine = 0.5f;
        // No patch edge may exceed this; a longer patch is not emitted.
        internal const float MembraneMaxEdge = 0.045f;
        internal const int CornerCandidateCapacity = 12;

        private static readonly byte[] TriangleOrder = { 0, 1, 2, 0, 2, 3 };

        /// <summary>
        /// Global identity of a shared knot: its line, chart, free side and the
        /// contributor cells that won its four columns. Two knot solves with the
        /// same identity computed the same numbers from the same inputs in the
        /// same order, on any tile of any consumer; this is the exact weld.
        /// </summary>
        internal readonly struct KnotIdentity : IEquatable<KnotIdentity>
        {
            internal readonly int3 LineAddress;
            internal readonly int Chart;
            internal readonly int Side;
            internal readonly int WinnerMask;
            internal readonly int3 Winner0;
            internal readonly int3 Winner1;
            internal readonly int3 Winner2;
            internal readonly int3 Winner3;

            internal KnotIdentity(int3 lineAddress, int chart, int side,
                int winnerMask, int3 winner0, int3 winner1, int3 winner2,
                int3 winner3)
            {
                LineAddress = lineAddress;
                Chart = chart;
                Side = side;
                WinnerMask = winnerMask;
                Winner0 = winner0;
                Winner1 = winner1;
                Winner2 = winner2;
                Winner3 = winner3;
            }

            public bool Equals(KnotIdentity other) =>
                math.all(LineAddress == other.LineAddress) &&
                Chart == other.Chart && Side == other.Side &&
                WinnerMask == other.WinnerMask &&
                math.all(Winner0 == other.Winner0) &&
                math.all(Winner1 == other.Winner1) &&
                math.all(Winner2 == other.Winner2) &&
                math.all(Winner3 == other.Winner3);

            public override bool Equals(object obj) =>
                obj is KnotIdentity other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(
                LineAddress.x, LineAddress.y, LineAddress.z, Chart, Side,
                WinnerMask, Winner0.x ^ Winner1.y ^ Winner2.z,
                Winner3.x ^ Winner0.z ^ Winner1.x);
        }

        internal readonly struct Corner : IEquatable<Corner>
        {
            internal readonly float3 GridPosition;
            internal readonly uint PackedColor;
            // The half-lattice address of the knot line (chart component zero)
            // and the chart axis. Live GPU vertices are shared inside a tile by
            // Identity; across tiles duplicates are bit-identical.
            internal readonly int3 LineAddress;
            internal readonly int Chart;
            // Export attribute only (the live vertex ABI carries no normal):
            // the confidence-weighted unit normal of the same contributors.
            internal readonly float3 Normal;
            internal readonly KnotIdentity Identity;

            internal Corner(float3 gridPosition, uint packedColor,
                int3 lineAddress, int chart, float3 normal,
                KnotIdentity identity)
            {
                GridPosition = gridPosition;
                PackedColor = packedColor;
                LineAddress = lineAddress;
                Chart = chart;
                Normal = normal;
                Identity = identity;
            }

            public bool Equals(Corner other) =>
                math.all(GridPosition == other.GridPosition) &&
                PackedColor == other.PackedColor &&
                math.all(LineAddress == other.LineAddress) &&
                Chart == other.Chart &&
                math.all(Normal == other.Normal) &&
                Identity.Equals(other.Identity);

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

        internal static int DominantAxis(float3 normal)
        {
            float lengthSquared = math.lengthsq(normal);
            if (!(lengthSquared > 0f) || !math.isfinite(lengthSquared))
                throw new ArgumentOutOfRangeException(nameof(normal));
            normal *= math.rsqrt(lengthSquared);
            float3 absolute = math.abs(normal);
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

        internal static bool TryBuildPatch(int3 main,
            IReadOnlyDictionary<int3, KernelState> context, out Patch patch) =>
            TryBuildPatch(main, context, null, out patch);

        /// <summary>
        /// Caller-owned memo for one export: the canonical chart and the
        /// decoded plane of a cell are pure functions of the context, so one
        /// export computes them once instead of once per corner that reads them.
        /// </summary>
        internal sealed class SolveCache
        {
            internal readonly Dictionary<int3, int> Charts = new();
            internal readonly Dictionary<int3, (float3 Normal, float Constant)>
                Planes = new();
        }

        /// <summary>
        /// Same oracle with a caller-owned memo of canonical charts per cell
        /// (the chart of a cell is a pure function of the context, so one
        /// export computes it once instead of once per corner that reads it).
        /// </summary>
        internal static bool TryBuildPatch(int3 main,
            IReadOnlyDictionary<int3, KernelState> context,
            SolveCache chartCache, out Patch patch)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (!context.TryGetValue(main, out KernelState state))
            {
                patch = default;
                return false;
            }
            return TryBuildPatch(main, state, context, chartCache, out patch);
        }

        /// <summary>
        /// Isolated-context convenience used by fixtures. It executes the same
        /// membrane oracle; every coordinate other than MAIN is UNKNOWN.
        /// </summary>
        internal static bool TryBuildPatch(int3 main, KernelState state,
            out Patch patch) => TryBuildPatch(main, state, null, null, out patch);

        private static int ChartOf(int3 coord, KernelState state,
            IReadOnlyDictionary<int3, KernelState> context,
            SolveCache chartCache)
        {
            if (chartCache == null) return CanonicalChart(coord, state, context, null);
            if (chartCache.Charts.TryGetValue(coord, out int chart)) return chart;
            chart = CanonicalChart(coord, state, context, chartCache);
            chartCache.Charts[coord] = chart;
            return chart;
        }

        /// <summary>
        /// Canonical chart of a measured cell: the dominant axis of the sum of
        /// its plane normal and the normals of its 26 neighbours that belong
        /// to the same local sheet (measured, no KNOWN FREE on the axis steps
        /// between, normals agreeing by at least
        /// <see cref="ChartCompatibleCosine"/>, planes agreeing both ways
        /// within <see cref="BranchHeightGap"/>). Radius one, fixed order,
        /// tie X before Y before Z. Derived readout preprocessing, no state.
        /// </summary>
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

        private static bool TryPlaneLineHeight(float3 normal,
            float planeConstant, int dominantAxis, float3 line,
            out float height)
        {
            float denominator = normal[dominantAxis];
            if (math.abs(denominator) <= NumericalEpsilon)
            {
                height = 0f;
                return false;
            }
            float3 basePoint = line;
            basePoint[dominantAxis] = 0f;
            height = (planeConstant - math.dot(basePoint, normal)) /
                denominator;
            return math.isfinite(height);
        }

        internal static int CanonicalChart(int3 coord, KernelState state,
            IReadOnlyDictionary<int3, KernelState> context) =>
            CanonicalChart(coord, state, context, null);

        private static int CanonicalChart(int3 coord, KernelState state,
            IReadOnlyDictionary<int3, KernelState> context, SolveCache cache)
        {
            DecodePlane(coord, state, cache, out float3 normal,
                out float planeConstant);
            // Geometry charting must not depend on RGB confidence. Six face
            // neighbours only, fixed order, equal geometric weight.
            float3 sum = normal;
            for (int axis = 0; axis < 3; axis++)
            for (int direction = -1; direction <= 1; direction += 2)
            {
                int3 neighbour = coord + AxisInt3(axis) * direction;
                if (!TryGetState(neighbour, coord, state, context,
                        out KernelState neighbourState) ||
                    !neighbourState.IsOccupied ||
                    !neighbourState.HasMeasuredSurfacePlane)
                    continue;
                DecodePlane(neighbour, neighbourState, cache,
                    out float3 neighbourNormal, out float neighbourConstant);
                if (math.dot(normal, neighbourNormal) < ChartCompatibleCosine)
                    continue;
                float3 neighbourCentre = (float3)neighbour *
                    MerkabaConstants.LatticeStep;
                float residualHere = math.abs(math.dot(neighbourCentre, normal) -
                    planeConstant);
                float residualThere = math.abs(math.dot((float3)coord *
                    MerkabaConstants.LatticeStep, neighbourNormal) -
                    neighbourConstant);
                if (residualHere > BranchHeightGap ||
                    residualThere > BranchHeightGap)
                    continue;
                sum += neighbourNormal;
            }
            return DominantAxis(sum);
        }

        private static bool TryBuildPatch(int3 main, KernelState state,
            IReadOnlyDictionary<int3, KernelState> context,
            SolveCache chartCache, out Patch patch)
        {
            if (!state.IsOccupied || !state.HasMeasuredSurfacePlane)
            {
                patch = default;
                return false;
            }

            KernelState.DecodeSurfacePlane(state.Flags, out float3 normal,
                out float signedOffset);
            int chart = ChartOf(main, state, context, chartCache);
            TangentAxes(chart, out int tangentAxis0, out int tangentAxis1);
            float3 tangent0 = AxisVector(tangentAxis0);
            float3 tangent1 = AxisVector(tangentAxis1);

            int side = FreeSideSignature(main, chart, context, main, state);
            if (!TrySolveKnot(main, state, normal, chart, tangentAxis0,
                    tangentAxis1, -1, -1, side, context, chartCache,
                    out Corner corner00) ||
                !TrySolveKnot(main, state, normal, chart, tangentAxis0,
                    tangentAxis1, 1, -1, side, context, chartCache,
                    out Corner corner10) ||
                !TrySolveKnot(main, state, normal, chart, tangentAxis0,
                    tangentAxis1, 1, 1, side, context, chartCache,
                    out Corner corner11) ||
                !TrySolveKnot(main, state, normal, chart, tangentAxis0,
                    tangentAxis1, -1, 1, side, context, chartCache,
                    out Corner corner01))
            {
                patch = default;
                return false;
            }

            // Edge guard: a patch whose knots drifted apart is not a 25 mm
            // membrane cell and is not emitted. No fallback geometry.
            if (math.distance(corner00.GridPosition, corner10.GridPosition) >
                MembraneMaxEdge ||
                math.distance(corner10.GridPosition, corner11.GridPosition) >
                MembraneMaxEdge ||
                math.distance(corner11.GridPosition, corner01.GridPosition) >
                MembraneMaxEdge ||
                math.distance(corner01.GridPosition, corner00.GridPosition) >
                MembraneMaxEdge)
            {
                patch = default;
                return false;
            }

            // Knot addresses are canonical. Only index orientation changes.
            if (math.dot(math.cross(corner10.GridPosition - corner00.GridPosition,
                    corner11.GridPosition - corner00.GridPosition), normal) < 0f)
            {
                (corner10, corner01) = (corner01, corner10);
                (tangent0, tangent1) = (tangent1, tangent0);
            }
            patch = new Patch(main, normal, tangent0, tangent1,
                corner00, corner10, corner11, corner01);
            return true;
        }

        /// <summary>
        /// Same-chart patches are already joined by shared knots. A genuine
        /// chart transition uses the binding plan's one extra primitive: the
        /// two facing knot edges only, no new position and no cross-sheet
        /// search. Cross-tile ownership is deliberately left to export/live
        /// duplicate equality; live only emits the same-tile case.
        /// </summary>
        internal static bool TryBuildStitch(int3 main, int tangentAxis,
            IReadOnlyDictionary<int3, KernelState> context,
            SolveCache solveCache, out Patch stitch)
        {
            stitch = default;
            if (context == null ||
                !context.TryGetValue(main, out KernelState firstState) ||
                !firstState.IsOccupied || !firstState.HasMeasuredSurfacePlane ||
                !TryBuildPatch(main, context, solveCache, out Patch first))
                return false;

            int firstChart = first.Corner00.Chart;
            TangentAxes(firstChart, out int firstT0, out int firstT1);
            if (tangentAxis != firstT0 && tangentAxis != firstT1)
                return false;

            int3 neighbour = main + AxisInt3(tangentAxis);
            if (!context.TryGetValue(neighbour, out KernelState secondState) ||
                !secondState.IsOccupied ||
                !secondState.HasMeasuredSurfacePlane ||
                !TryBuildPatch(neighbour, context, solveCache, out Patch second))
                return false;

            int secondChart = second.Corner00.Chart;
            if (secondChart == firstChart || secondChart == tangentAxis)
                return false;

            DecodePlane(main, firstState, solveCache, out float3 firstNormal,
                out float firstConstant);
            DecodePlane(neighbour, secondState, solveCache,
                out float3 secondNormal, out float secondConstant);
            if (math.dot(firstNormal, secondNormal) < ChartCompatibleCosine)
                return false;
            float3 firstCentre = (float3)main * MerkabaConstants.LatticeStep;
            float3 secondCentre =
                (float3)neighbour * MerkabaConstants.LatticeStep;
            if (math.abs(math.dot(secondCentre, firstNormal) - firstConstant) >
                    BranchHeightGap ||
                math.abs(math.dot(firstCentre, secondNormal) - secondConstant) >
                    BranchHeightGap)
                return false;

            if (!TryGetPatchEdge(first, tangentAxis, 1,
                    out Corner firstLow, out Corner firstHigh) ||
                !TryGetPatchEdge(second, tangentAxis, -1,
                    out Corner secondLow, out Corner secondHigh))
                return false;

            // One physical knot may appear only once in the transition quad.
            Corner a = firstLow;
            Corner b = firstHigh;
            Corner c = secondHigh;
            Corner d = secondLow;
            if (a.Identity.Equals(b.Identity) || a.Identity.Equals(c.Identity) ||
                a.Identity.Equals(d.Identity) || b.Identity.Equals(c.Identity) ||
                b.Identity.Equals(d.Identity) || c.Identity.Equals(d.Identity))
                return false;

            float maxEdge = MembraneMaxEdge;
            if (math.distance(a.GridPosition, b.GridPosition) > maxEdge ||
                math.distance(b.GridPosition, c.GridPosition) > maxEdge ||
                math.distance(c.GridPosition, d.GridPosition) > maxEdge ||
                math.distance(d.GridPosition, a.GridPosition) > maxEdge)
                return false;

            float3 stitchNormal = math.normalizesafe(firstNormal + secondNormal,
                firstNormal);
            float area0 = math.dot(math.cross(
                b.GridPosition - a.GridPosition,
                c.GridPosition - a.GridPosition), stitchNormal);
            float area1 = math.dot(math.cross(
                c.GridPosition - a.GridPosition,
                d.GridPosition - a.GridPosition), stitchNormal);
            if (math.abs(area0) <= NumericalEpsilon ||
                math.abs(area1) <= NumericalEpsilon || area0 * area1 <= 0f)
                return false;
            if (area0 < 0f)
                (b, d) = (d, b);

            stitch = new Patch(main, stitchNormal, first.Tangent0,
                first.Tangent1, a, b, c, d);
            return true;
        }

        private static bool TryGetPatchEdge(Patch patch, int axis,
            int direction, out Corner low, out Corner high)
        {
            TangentAxes(patch.Corner00.Chart, out int tangent0,
                out int tangent1);
            if (axis == tangent0)
            {
                if (direction > 0)
                {
                    low = patch.Corner10;
                    high = patch.Corner11;
                }
                else
                {
                    low = patch.Corner00;
                    high = patch.Corner01;
                }
                return true;
            }
            if (axis == tangent1)
            {
                if (direction > 0)
                {
                    low = patch.Corner01;
                    high = patch.Corner11;
                }
                else
                {
                    low = patch.Corner00;
                    high = patch.Corner10;
                }
                return true;
            }
            low = default;
            high = default;
            return false;
        }

        /// <summary>
        /// One shared knot of a MAIN's corner. The knot is a function of its
        /// line, chart, free side and layer only: the uses of the line at that
        /// layer (the up to four cells of the same chart and side around it)
        /// define the reference height, the four immediately sharing columns
        /// at layers -1/0/+1 supply the contributors, one winner per column.
        /// Every MAIN of the same cluster resolves the identical knot; a MAIN of
        /// an adjacent layer with the same winners resolves the identical knot
        /// as well (same inputs, same order). Contract C5 steps 1-9.
        /// </summary>
        internal static bool TrySolveKnot(int3 main, KernelState mainState,
            float3 mainNormal, int chart, int tangentAxis0, int tangentAxis1,
            int cornerSign0, int cornerSign1, int side,
            IReadOnlyDictionary<int3, KernelState> context,
            SolveCache chartCache, out Corner corner)
        {
            corner = default;
            int3 halfAddress = main * 2;
            halfAddress[tangentAxis0] += cornerSign0;
            halfAddress[tangentAxis1] += cornerSign1;
            float3 line = (float3)halfAddress * (MembranePatchPitch * 0.5f);
            int3 lineAddress = halfAddress;
            lineAddress[chart] = 0;
            int layer = main[chart];
            int lower0 = MerkabaConstants.FloorDiv(halfAddress[tangentAxis0], 2);
            int lower1 = MerkabaConstants.FloorDiv(halfAddress[tangentAxis1], 2);

            // Uses: the cells of this chart and side in the four columns at
            // MAIN's layer. Column order first*2+second is lexicographic.
            Span<float> useHeight = stackalloc float[4];
            int useMask = 0;
            for (int column = 0; column < 4; column++)
            {
                int3 coord = main;
                coord[tangentAxis0] = lower0 + (column >> 1);
                coord[tangentAxis1] = lower1 + (column & 1);
                coord[chart] = layer;
                if (!TryGetState(coord, main, mainState, context,
                        out KernelState use) ||
                    !use.IsOccupied || !use.HasMeasuredSurfacePlane)
                    continue;
                if (ChartOf(coord, use, context, chartCache) != chart ||
                    FreeSideSignature(coord, chart, context, main, mainState) !=
                    side)
                    continue;
                DecodePlane(coord, use, chartCache, out float3 useNormal,
                    out float useConstant);
                // The plan's chart is defined only while |N_c| >= 0.5.
                // Never manufacture a huge line intersection by dividing a
                // legitimate MAIN plane by an almost-zero canonical axis.
                if (math.abs(useNormal[chart]) <
                        MembraneCompatibleAxisCosine ||
                    !TryPlaneLineHeight(useNormal, useConstant, chart, line,
                        out float height))
                    continue;
                useHeight[column] = height;
                useMask |= 1 << column;
            }
            int mainColumn = (main[tangentAxis0] - lower0) * 2 +
                (main[tangentAxis1] - lower1);
            if ((useMask & (1 << mainColumn)) == 0) return false;

            // Cluster: the connected height component of the uses that holds
            // MAIN (|dh| <= gap). Its median is the knot's reference height.
            int cluster = 1 << mainColumn;
            for (int pass = 0; pass < 3; pass++)
            for (int column = 0; column < 4; column++)
            {
                if ((useMask & (1 << column)) == 0 ||
                    (cluster & (1 << column)) != 0)
                    continue;
                for (int member = 0; member < 4; member++)
                    if ((cluster & (1 << member)) != 0 &&
                        math.abs(useHeight[column] - useHeight[member]) <=
                        BranchHeightGap)
                    {
                        cluster |= 1 << column;
                        break;
                    }
            }
            Span<float> sorted = stackalloc float[4];
            int members = 0;
            for (int column = 0; column < 4; column++)
            {
                if ((cluster & (1 << column)) == 0) continue;
                float value = useHeight[column];
                int slot = members;
                while (slot > 0 && value < sorted[slot - 1])
                {
                    sorted[slot] = sorted[slot - 1];
                    slot--;
                }
                sorted[slot] = value;
                members++;
            }
            float reference = (members & 1) != 0
                ? sorted[members / 2]
                : (sorted[members / 2 - 1] + sorted[members / 2]) * 0.5f;

            // Candidates: the four columns at layers -1, 0, +1 (index
            // column*3 + offset+1). Contract C5 step 4.
            Span<float> heights = stackalloc float[CornerCandidateCapacity];
            Span<int3> coords = stackalloc int3[CornerCandidateCapacity];
            Span<float3> normals = stackalloc float3[CornerCandidateCapacity];
            Span<uint> colors = stackalloc uint[CornerCandidateCapacity];
            Span<uint> weights = stackalloc uint[CornerCandidateCapacity];
            int validMask = 0;
            for (int column = 0; column < 4; column++)
            for (int normalOffset = -1; normalOffset <= 1; normalOffset++)
            {
                int index = column * 3 + normalOffset + 1;
                int3 coord = main;
                coord[tangentAxis0] = lower0 + (column >> 1);
                coord[tangentAxis1] = lower1 + (column & 1);
                coord[chart] = layer + normalOffset;
                if (!TryGetState(coord, main, mainState, context,
                        out KernelState candidate) ||
                    !candidate.IsOccupied ||
                    !candidate.HasMeasuredSurfacePlane)
                    continue;
                if (normalOffset != 0)
                {
                    int3 between = coord;
                    between[chart] = layer;
                    if (TryGetState(between, main, mainState, context,
                            out KernelState separator) &&
                        IsKnownFree(separator))
                        continue;
                }
                DecodePlane(coord, candidate, chartCache,
                    out float3 candidateNormal, out float candidateConstant);
                if (math.abs(candidateNormal[chart]) <
                    MembraneCompatibleAxisCosine ||
                    FreeSideSignature(coord, chart, context, main, mainState) !=
                    side ||
                    !TryPlaneLineHeight(candidateNormal, candidateConstant,
                        chart, line, out float height))
                    continue;
                heights[index] = height;
                coords[index] = coord;
                normals[index] = candidateNormal;
                colors[index] = candidate.PackedColor;
                weights[index] = math.max(1u, candidate.ColorConfidence);
                validMask |= 1 << index;
            }

            // Admission: the connected height component (|dh| <= gap) of the
            // cluster cells among the candidates. No distance-two bridging:
            // every member is inside the +-1 window.
            int admitted = 0;
            for (int column = 0; column < 4; column++)
                if ((cluster & (1 << column)) != 0 &&
                    (validMask & (1 << (column * 3 + 1))) != 0)
                    admitted |= 1 << (column * 3 + 1);
            for (int pass = 0; pass < CornerCandidateCapacity - 1; pass++)
            {
                int grown = admitted;
                for (int index = 0; index < CornerCandidateCapacity; index++)
                {
                    if ((validMask & (1 << index)) == 0 ||
                        (grown & (1 << index)) != 0)
                        continue;
                    for (int other = 0; other < CornerCandidateCapacity; other++)
                        if ((admitted & (1 << other)) != 0 &&
                            math.abs(heights[index] - heights[other]) <=
                            BranchHeightGap)
                        {
                            grown |= 1 << index;
                            break;
                        }
                }
                if (grown == admitted) break;
                admitted = grown;
            }
            if (admitted == 0) return false;

            // Winner per column: nearest to the reference height, then the
            // nearer layer, then the lexicographically smaller coordinate
            // (contract C5 step 6 against the knot's own reference).
            float heightSum = 0f;
            int accepted = 0;
            int winnerMask = 0;
            Span<int3> winners = stackalloc int3[4];
            uint red = 0u, green = 0u, blue = 0u, weightTotal = 0u;
            float3 normalSum = float3.zero;
            for (int column = 0; column < 4; column++)
            {
                int best = -1;
                float bestDistance = float.PositiveInfinity;
                for (int normalOffset = -1; normalOffset <= 1; normalOffset++)
                {
                    int index = column * 3 + normalOffset + 1;
                    if ((admitted & (1 << index)) == 0) continue;
                    float distance = math.abs(heights[index] - reference);
                    int bestOffset = best < 0 ? 0 : math.abs(best - column * 3 - 1);
                    if (best < 0 || distance < bestDistance - NumericalEpsilon ||
                        (math.abs(distance - bestDistance) <= NumericalEpsilon &&
                         (math.abs(normalOffset) < bestOffset ||
                          (math.abs(normalOffset) == bestOffset &&
                           LexicographicallyLess(coords[index], coords[best])))))
                    {
                        best = index;
                        bestDistance = distance;
                    }
                }
                winners[column] = best < 0 ? int3.zero : coords[best];
                if (best < 0) continue;
                winnerMask |= 1 << column;
                heightSum += heights[best];
                accepted++;
                float weight = weights[best];
                normalSum += normals[best] * weight;
                red += (colors[best] & 255u) * weights[best];
                green += ((colors[best] >> 8) & 255u) * weights[best];
                blue += ((colors[best] >> 16) & 255u) * weights[best];
                weightTotal += weights[best];
            }
            if (accepted == 0) return false;
            line[chart] = heightSum / accepted;
            if (!math.all(math.isfinite(line))) return false;
            float inverse = 1f / math.max(1u, weightTotal);
            uint r = math.min(255u, (uint)math.floor(red * inverse + 0.5f));
            uint g = math.min(255u, (uint)math.floor(green * inverse + 0.5f));
            uint b = math.min(255u, (uint)math.floor(blue * inverse + 0.5f));
            corner = new Corner(line, r | (g << 8) | (b << 16) | 0xff000000u,
                lineAddress, chart, math.normalizesafe(normalSum, mainNormal),
                new KnotIdentity(lineAddress, chart, side, winnerMask,
                    winners[0], winners[1], winners[2], winners[3]));
            return true;
        }

        private static int FreeSideSignature(int3 coord, int dominantAxis,
            IReadOnlyDictionary<int3, KernelState> context, int3 main,
            KernelState mainState)
        {
            int signature = 0;
            int3 axis = AxisInt3(dominantAxis);
            if (TryGetState(coord - axis, main, mainState,
                    context, out KernelState negative) &&
                IsKnownFree(negative))
                signature |= 1;
            if (TryGetState(coord + axis, main, mainState,
                    context, out KernelState positive) &&
                IsKnownFree(positive))
                signature |= 2;
            return signature;
        }

        private static bool IsKnownFree(in KernelState state) =>
            !state.IsOccupied && state.OccupancyEvidence <=
            MerkabaConstants.ExportKnownFreeThreshold;

        private static bool TryGetState(int3 coord, int3 main,
            KernelState mainState,
            IReadOnlyDictionary<int3, KernelState> context,
            out KernelState state)
        {
            if (context != null) return context.TryGetValue(coord, out state);
            if (math.all(coord == main))
            {
                state = mainState;
                return true;
            }
            state = default;
            return false;
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

        private static bool LexicographicallyLess(int3 left, int3 right) =>
            left.x < right.x || (left.x == right.x &&
            (left.y < right.y || (left.y == right.y && left.z < right.z)));

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
// A contributor plane must be well conditioned against the corner line of
// MAIN's chart; the connected height branch and the free side decide sheet
// membership. No quantized dominant-axis or 26-cell equality.
__HASH__define M8_MEMBRANE_COMPATIBLE_AXIS_COSINE 0.5
// Two heights on one knot line belong to one branch when closer than this;
// face neighbours belong to one local sheet when their planes agree both
// ways within it.
__HASH__define M8_MEMBRANE_BRANCH_GAP (M8_MEMBRANE_PATCH_PITCH * 0.6)
// Face neighbours whose normals agree at least this much shape the chart.
__HASH__define M8_MEMBRANE_CHART_COSINE 0.5
// No patch edge may exceed this; a longer patch is not emitted.
__HASH__define M8_MEMBRANE_MAX_EDGE 0.045

struct M8OverlapPatch
{
    float3 corner00;
    float3 corner10;
    float3 corner11;
    float3 corner01;
    // Knot colour and global identity per corner: the half-lattice address
    // of its corner line (chart component zero) plus the chart axis. Two
    // patches share one vertex exactly when line, chart and position agree.
    uint color00;
    uint color10;
    uint color11;
    uint color01;
    int3 line00;
    int3 line10;
    int3 line11;
    int3 line01;
    // Exact knot identity per corner (winner cells): the weld key.
    uint key00;
    uint key10;
    uint key11;
    uint key01;
    int chart;
    uint side;
    float3 normal;
    uint packedColor;
};

int M8MembraneDominantAxis(float3 normal)
{
    float3 magnitude = abs(normalize(normal));
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

int M8MembraneFloorDiv2(int value)
{
    return value >> 1;
}

bool M8MembraneLexLess(int3 left, int3 right)
{
    return left.x < right.x || (left.x == right.x &&
        (left.y < right.y || (left.y == right.y && left.z < right.z)));
}

bool M8MembranePlaneLineHeight(int3 owner, float3 normal,
    float signedOffset, int dominantAxis, float3 linePoint, out float height)
{
    float denominator = normal[dominantAxis];
    if (abs(denominator) <= M8_MEMBRANE_NUMERICAL_EPSILON)
    {
        height = 0.0;
        return false;
    }
    float3 basePoint = linePoint;
    basePoint = M8MembraneSetFloatComponent(basePoint, dominantAxis, 0.0);
    float planeConstant = dot((float3)owner * MERKABA_LATTICE_STEP, normal) +
        signedOffset;
    height = (planeConstant - dot(basePoint, normal)) / denominator;
    return isfinite(height);
}

bool M8MembraneFreeSideSignature(int3 coord, int dominantAxis,
    out uint signature, out bool unresolved)
{
    signature = 0u;
    unresolved = false;
    int3 axis = M8MembraneAxis(dominantAxis);
    bool resolved;
    bool measured;
    bool knownFree;
    bool exists = M8MembraneCell(coord - axis, resolved, measured, knownFree);
    if (!resolved)
    {
        unresolved = true;
        return false;
    }
    if (exists && knownFree) signature |= 1u;
    exists = M8MembraneCell(coord + axis, resolved, measured, knownFree);
    if (!resolved)
    {
        unresolved = true;
        return false;
    }
    if (exists && knownFree) signature |= 2u;
    return true;
}

bool M8MembraneSeparatedByFree(int3 contributor, int normalOffset,
    int dominantAxis, out bool unresolved)
{
    unresolved = false;
    if (normalOffset == 0) return false;
    int3 towardMain = contributor;
    towardMain = M8MembraneSetIntComponent(towardMain, dominantAxis,
        towardMain[dominantAxis] - (normalOffset < 0 ? -1 : 1));
    bool resolved;
    bool measured;
    bool knownFree;
    bool exists = M8MembraneCell(towardMain, resolved, measured, knownFree);
    if (!resolved)
    {
        unresolved = true;
        return false;
    }
    return exists && knownFree;
}

// Register-resident storage for the four uses and twelve contributors of
// one knot: heights in float4 registers, membership in scalar bit masks.
// Nothing is a private array, so Adreno never lowers the solve to scratch.
void M8MembraneStoreHeight(inout float4 h0, inout float4 h1,
    inout float4 h2, uint slot, float value)
{
    uint lane = slot & 3u;
    if (slot < 4u)
    {
        if (lane == 0u) h0.x = value;
        else if (lane == 1u) h0.y = value;
        else if (lane == 2u) h0.z = value;
        else h0.w = value;
    }
    else if (slot < 8u)
    {
        if (lane == 0u) h1.x = value;
        else if (lane == 1u) h1.y = value;
        else if (lane == 2u) h1.z = value;
        else h1.w = value;
    }
    else
    {
        if (lane == 0u) h2.x = value;
        else if (lane == 1u) h2.y = value;
        else if (lane == 2u) h2.z = value;
        else h2.w = value;
    }
}

float M8MembraneLoadHeight(float4 h0, float4 h1, float4 h2, uint slot)
{
    uint lane = slot & 3u;
    float4 values = slot < 4u ? h0 : slot < 8u ? h1 : h2;
    return lane == 0u ? values.x :
        lane == 1u ? values.y :
        lane == 2u ? values.z : values.w;
}

float M8MembraneLoadUse(float4 uses, uint column)
{
    return column == 0u ? uses.x : column == 1u ? uses.y :
        column == 2u ? uses.z : uses.w;
}

// Column of a candidate slot (slot = column * 3 + offset + 1) without an
// integer division: slot < 12.
uint M8MembraneSlotColumn(uint slot)
{
    return slot < 3u ? 0u : slot < 6u ? 1u : slot < 9u ? 2u : 3u;
}

int3 M8MembraneColumnCoord(int3 main, int chart, int tangentAxis0,
    int tangentAxis1, int lower0, int lower1, uint column, int layer)
{
    int3 coord = main;
    coord = M8MembraneSetIntComponent(coord, tangentAxis0,
        lower0 + (int)(column >> 1u));
    coord = M8MembraneSetIntComponent(coord, tangentAxis1,
        lower1 + (int)(column & 1u));
    coord = M8MembraneSetIntComponent(coord, chart, layer);
    return coord;
}

// Canonical chart of a measured cell: dominant axis of the sum of its plane
// normal and the normals of its 26 neighbours of the same local sheet
// (measured, no KNOWN FREE on the axis steps between, normals agreeing by
// the chart cosine, planes agreeing both ways within the branch gap).
// Radius one, fixed order, tie X, Y, Z.
bool M8MembraneCanonicalChart(int3 coord, out int chart, out bool unresolved)
{
    unresolved = false;
    chart = 0;
    float4 plane = M8MembranePlaneOf(coord);
    float3 normal = plane.xyz;
    float3 sum = normal;
    float planeConstant = plane.w;
    // Binding plan section 1: SIX face neighbours only, fixed axis/sign
    // order, no abs(dot), confidence weighted.
    [unroll]
    for (int axis = 0; axis < 3; axis++)
    [unroll]
    for (int signIndex = 0; signIndex < 2; signIndex++)
    {
        int direction = signIndex == 0 ? -1 : 1;
        int3 neighbour = coord + M8MembraneAxis(axis) * direction;
        bool resolved;
        bool measured;
        bool knownFree;
        bool exists = M8MembraneCell(neighbour, resolved, measured, knownFree);
        if (!resolved)
        {
            unresolved = true;
            return false;
        }
        if (!exists || !measured) continue;
        float4 neighbourPlane = M8MembranePlaneOf(neighbour);
        float3 neighbourNormal = neighbourPlane.xyz;
        if (dot(normal, neighbourNormal) < M8_MEMBRANE_CHART_COSINE) continue;
        float3 neighbourCentre = (float3)neighbour * MERKABA_LATTICE_STEP;
        float residualHere = abs(dot(neighbourCentre, normal) - planeConstant);
        float residualThere = abs(dot((float3)coord * MERKABA_LATTICE_STEP,
            neighbourNormal) - neighbourPlane.w);
        if (residualHere > M8_MEMBRANE_BRANCH_GAP ||
            residualThere > M8_MEMBRANE_BRANCH_GAP)
            continue;
        sum += neighbourNormal;
    }
    chart = M8MembraneDominantAxis(sum);
    return true;
}

// The chart-transition predicate is part of the same generated oracle.
// It creates no point: Resolve/Emit may only use already solved knot edges.
bool M8MembraneStitchCompatible(int3 first, int3 second)
{
    float4 firstPlane = M8MembranePlaneOf(first);
    float4 secondPlane = M8MembranePlaneOf(second);
    if (dot(firstPlane.xyz, secondPlane.xyz) < M8_MEMBRANE_CHART_COSINE)
        return false;
    float3 firstCentre = (float3)first * MERKABA_LATTICE_STEP;
    float3 secondCentre = (float3)second * MERKABA_LATTICE_STEP;
    return abs(dot(secondCentre, firstPlane.xyz) - firstPlane.w) <=
               M8_MEMBRANE_BRANCH_GAP &&
           abs(dot(firstCentre, secondPlane.xyz) - secondPlane.w) <=
               M8_MEMBRANE_BRANCH_GAP;
}

// One shared knot of a MAIN's corner (contract C5 steps 1-9). The knot is a
// function of its line, chart, free side and layer only: the uses of the
// line at that layer define the reference height, the four sharing columns
// at layers -1/0/+1 supply the contributors, one winner per column. Every
// MAIN of the same cluster, and every MAIN of an adjacent layer with the
// same winners, resolves the identical knot. winnerKey encodes the winner
// cells (mask and layer per column), the exact identity for welding.
bool M8MembraneSolveKnot(int3 main, float3 mainNormal, int chart,
    int tangentAxis0, int tangentAxis1, int cornerSign0, int cornerSign1,
    uint side, out float3 position, out uint packedColor,
    out int3 lineAddress, out uint winnerKey, out bool unresolved)
{
    unresolved = false;
    position = 0.0;
    packedColor = 0u;
    winnerKey = 0u;
    int3 halfAddress = main * 2;
    halfAddress = M8MembraneSetIntComponent(halfAddress, tangentAxis0,
        halfAddress[tangentAxis0] + cornerSign0);
    halfAddress = M8MembraneSetIntComponent(halfAddress, tangentAxis1,
        halfAddress[tangentAxis1] + cornerSign1);
    lineAddress = M8MembraneSetIntComponent(halfAddress, chart, 0);
    float3 linePoint = (float3)halfAddress * (M8_MEMBRANE_PATCH_PITCH * 0.5);
    int layer = main[chart];
    int lower0 = M8MembraneFloorDiv2(halfAddress[tangentAxis0]);
    int lower1 = M8MembraneFloorDiv2(halfAddress[tangentAxis1]);

    // Uses: cells of this chart and side in the four columns at MAIN's layer.
    float4 useHeights = 0.0;
    uint useMask = 0u;
    [loop]
    for (uint column = 0u; column < 4u; column++)
    {
        int3 coord = M8MembraneColumnCoord(main, chart, tangentAxis0,
            tangentAxis1, lower0, lower1, column, layer);
        bool resolved;
        bool measured;
        bool knownFree;
        bool exists = M8MembraneCell(coord, resolved, measured, knownFree);
        if (!resolved)
        {
            unresolved = true;
            return false;
        }
        if (!exists || !measured) continue;
        int useChart;
        bool chartUnresolved;
        if (!M8MembraneCanonicalChart(coord, useChart, chartUnresolved))
        {
            unresolved = true;
            return false;
        }
        if (useChart != chart) continue;
        uint useSide;
        bool sideUnresolved;
        if (!M8MembraneFreeSideSignature(coord, chart, useSide, sideUnresolved))
        {
            if (sideUnresolved) unresolved = true;
            return false;
        }
        if (useSide != side) continue;
        float4 plane = M8MembranePlaneOf(coord);
        float denominator = plane[chart];
        if (abs(denominator) < M8_MEMBRANE_COMPATIBLE_AXIS_COSINE) continue;
        float3 basePoint = M8MembraneSetFloatComponent(linePoint, chart, 0.0);
        float height = (plane.w - dot(basePoint, plane.xyz)) / denominator;
        if (!isfinite(height)) continue;
        useHeights = column == 0u ? float4(height, useHeights.yzw) :
            column == 1u ? float4(useHeights.x, height, useHeights.zw) :
            column == 2u ? float4(useHeights.xy, height, useHeights.w) :
            float4(useHeights.xyz, height);
        useMask |= 1u << column;
    }
    uint mainColumn = (uint)(main[tangentAxis0] - lower0) * 2u +
        (uint)(main[tangentAxis1] - lower1);
    if ((useMask & (1u << mainColumn)) == 0u) return false;

    // Cluster: connected height component of the uses that holds MAIN.
    uint cluster = 1u << mainColumn;
    [loop]
    for (uint expansion = 0u; expansion < 3u; expansion++)
    [loop]
    for (uint column = 0u; column < 4u; column++)
    {
        if ((useMask & (1u << column)) == 0u ||
            (cluster & (1u << column)) != 0u)
            continue;
        float height = M8MembraneLoadUse(useHeights, column);
        [loop]
        for (uint member = 0u; member < 4u; member++)
        {
            if ((cluster & (1u << member)) != 0u &&
                abs(height - M8MembraneLoadUse(useHeights, member)) <=
                M8_MEMBRANE_BRANCH_GAP)
            {
                cluster |= 1u << column;
                break;
            }
        }
    }
    // Median of the cluster: rank by (height, column) among members.
    // (bit sum instead of countbits: FXC rejects the intrinsic here.)
    uint members = (cluster & 1u) + ((cluster >> 1u) & 1u) +
        ((cluster >> 2u) & 1u) + ((cluster >> 3u) & 1u);
    uint middle = members >> 1u;
    float medianHigh = 0.0;
    float medianLow = 0.0;
    [loop]
    for (uint column = 0u; column < 4u; column++)
    {
        if ((cluster & (1u << column)) == 0u) continue;
        float height = M8MembraneLoadUse(useHeights, column);
        uint rank = 0u;
        [loop]
        for (uint other = 0u; other < 4u; other++)
        {
            if ((cluster & (1u << other)) == 0u || other == column) continue;
            float otherHeight = M8MembraneLoadUse(useHeights, other);
            if (otherHeight < height ||
                (otherHeight == height && other < column))
                rank++;
        }
        if (rank == middle) medianHigh = height;
        if (middle > 0u && rank == middle - 1u) medianLow = height;
    }
    float reference = (members & 1u) != 0u
        ? medianHigh : (medianLow + medianHigh) * 0.5;

    // Candidates: the four columns at layers -1, 0, +1 (slot = column*3 +
    // offset + 1). Contract C5 step 4.
    float4 heights0 = 0.0;
    float4 heights1 = 0.0;
    float4 heights2 = 0.0;
    uint validMask = 0u;
    [loop]
    for (uint slot = 0u; slot < 12u; slot++)
    {
        uint column = M8MembraneSlotColumn(slot);
        int normalOffset = (int)(slot - column * 3u) - 1;
        int3 coord = M8MembraneColumnCoord(main, chart, tangentAxis0,
            tangentAxis1, lower0, lower1, column, layer + normalOffset);
        bool resolved;
        bool measured;
        bool knownFree;
        bool exists = M8MembraneCell(coord, resolved, measured, knownFree);
        if (!resolved)
        {
            unresolved = true;
            return false;
        }
        if (!exists || !measured) continue;
        if (normalOffset != 0)
        {
            int3 between = M8MembraneSetIntComponent(coord, chart, layer);
            bool separatorResolved;
            bool separatorMeasured;
            bool separatorFree;
            bool separatorExists = M8MembraneCell(between, separatorResolved,
                separatorMeasured, separatorFree);
            if (!separatorResolved)
            {
                unresolved = true;
                return false;
            }
            if (separatorExists && separatorFree) continue;
        }
        float4 plane = M8MembranePlaneOf(coord);
        float denominator = plane[chart];
        if (abs(denominator) < M8_MEMBRANE_COMPATIBLE_AXIS_COSINE) continue;
        uint candidateSide;
        bool sideUnresolved;
        if (!M8MembraneFreeSideSignature(coord, chart, candidateSide,
                sideUnresolved))
        {
            if (sideUnresolved) unresolved = true;
            return false;
        }
        if (candidateSide != side) continue;
        float3 basePoint = M8MembraneSetFloatComponent(linePoint, chart, 0.0);
        float height = (plane.w - dot(basePoint, plane.xyz)) / denominator;
        if (!isfinite(height)) continue;
        M8MembraneStoreHeight(heights0, heights1, heights2, slot, height);
        validMask |= 1u << slot;
    }

    // Admission: connected height component of the cluster cells among the
    // candidates. Every member stays inside the +-1 window.
    uint admitted = 0u;
    [unroll]
    for (uint column = 0u; column < 4u; column++)
    {
        uint slot = column * 3u + 1u;
        if ((cluster & (1u << column)) != 0u &&
            (validMask & (1u << slot)) != 0u)
            admitted |= 1u << slot;
    }
    [loop]
    for (uint expansion = 0u; expansion < 11u; expansion++)
    {
        uint grown = admitted;
        [loop]
        for (uint slot = 0u; slot < 12u; slot++)
        {
            if ((validMask & (1u << slot)) == 0u ||
                (grown & (1u << slot)) != 0u)
                continue;
            float height = M8MembraneLoadHeight(heights0, heights1, heights2,
                slot);
            [loop]
            for (uint other = 0u; other < 12u; other++)
            {
                if ((admitted & (1u << other)) == 0u) continue;
                if (abs(height - M8MembraneLoadHeight(heights0, heights1,
                        heights2, other)) <= M8_MEMBRANE_BRANCH_GAP)
                {
                    grown |= 1u << slot;
                    break;
                }
            }
        }
        if (grown == admitted) break;
        admitted = grown;
    }
    if (admitted == 0u) return false;

    // Winner per column: nearest to the reference height, then the nearer
    // layer, then the lexicographically smaller coordinate.
    float heightSum = 0.0;
    uint accepted = 0u;
    uint red = 0u;
    uint green = 0u;
    uint blue = 0u;
    uint weightTotal = 0u;
    float3 normalSum = 0.0;
    [loop]
    for (uint column = 0u; column < 4u; column++)
    {
        bool haveBest = false;
        uint bestSlot = 0u;
        float bestDistance = 0.0;
        int bestOffset = 0;
        int3 bestCoord = int3(0, 0, 0);
        [unroll]
        for (int normalOffset = -1; normalOffset <= 1; normalOffset++)
        {
            uint slot = column * 3u + (uint)(normalOffset + 1);
            if ((admitted & (1u << slot)) == 0u) continue;
            float distance = abs(M8MembraneLoadHeight(heights0, heights1,
                heights2, slot) - reference);
            int3 coord = M8MembraneColumnCoord(main, chart, tangentAxis0,
                tangentAxis1, lower0, lower1, column, layer + normalOffset);
            int layerDistance = normalOffset < 0 ? -normalOffset : normalOffset;
            if (!haveBest ||
                distance < bestDistance - M8_MEMBRANE_NUMERICAL_EPSILON ||
                (abs(distance - bestDistance) <=
                     M8_MEMBRANE_NUMERICAL_EPSILON &&
                 (layerDistance < bestOffset ||
                  (layerDistance == bestOffset &&
                   M8MembraneLexLess(coord, bestCoord)))))
            {
                haveBest = true;
                bestSlot = slot;
                bestDistance = distance;
                bestOffset = layerDistance;
                bestCoord = coord;
            }
        }
        if (!haveBest) continue;
        heightSum += M8MembraneLoadHeight(heights0, heights1, heights2,
            bestSlot);
        accepted++;
        winnerKey |= 1u << column;
        winnerKey |= ((uint)bestCoord[chart] & 15u) << (4u + column * 4u);
        float4 winnerPlane = M8MembranePlaneOf(bestCoord);
        uint ownerColor;
        uint ownerConfidence;
        M8LoadMembraneColor(bestCoord, ownerColor, ownerConfidence);
        uint weight = max(1u, ownerConfidence);
        normalSum += winnerPlane.xyz * (float)weight;
        red += (ownerColor & 255u) * weight;
        green += ((ownerColor >> 8u) & 255u) * weight;
        blue += ((ownerColor >> 16u) & 255u) * weight;
        weightTotal += weight;
    }
    if (accepted == 0u) return false;
    position = M8MembraneSetFloatComponent(linePoint, chart,
        heightSum / (float)accepted);
    if (!all(isfinite(position))) return false;
    float inverse = 1.0 / (float)max(1u, weightTotal);
    uint r = min(255u, (uint)floor((float)red * inverse + 0.5));
    uint g = min(255u, (uint)floor((float)green * inverse + 0.5));
    uint b = min(255u, (uint)floor((float)blue * inverse + 0.5));
    packedColor = r | (g << 8u) | (b << 16u) | 0xff000000u;
    return true;
}

bool M8TryBuildMembranePatch(int3 main, KernelState state,
    out M8OverlapPatch patch, out bool unresolved)
{
    patch = (M8OverlapPatch)0;
    unresolved = false;
    if ((state.flags & MERKABA_OCCUPIED_FLAG) == 0u ||
        !M8HasSurfacePlane(state.flags))
        return false;
    float3 normal;
    float signedOffset;
    M8DecodeSurfacePlane(state.flags, normal, signedOffset);
    int chart;
    if (!M8MembraneCanonicalChart(main, chart, unresolved)) return false;
    int tangentAxis0;
    int tangentAxis1;
    M8MembraneTangentAxes(chart, tangentAxis0, tangentAxis1);
    uint side;
    if (!M8MembraneFreeSideSignature(main, chart, side, unresolved))
        return false;
    // One knot solve, inlined once: corners 00, 10, 11, 01.
    [loop]
    for (uint corner = 0u; corner < 4u; corner++)
    {
        int sign0 = (corner == 1u || corner == 2u) ? 1 : -1;
        int sign1 = corner >= 2u ? 1 : -1;
        float3 position;
        uint color;
        int3 knotLine;
        uint key;
        if (!M8MembraneSolveKnot(main, normal, chart, tangentAxis0,
                tangentAxis1, sign0, sign1, side, position, color, knotLine,
                key, unresolved))
            return false;
        if (corner == 0u) { patch.corner00 = position; patch.color00 = color; patch.line00 = knotLine; patch.key00 = key; }
        else if (corner == 1u) { patch.corner10 = position; patch.color10 = color; patch.line10 = knotLine; patch.key10 = key; }
        else if (corner == 2u) { patch.corner11 = position; patch.color11 = color; patch.line11 = knotLine; patch.key11 = key; }
        else { patch.corner01 = position; patch.color01 = color; patch.line01 = knotLine; patch.key01 = key; }
    }
    // Edge guard: a patch whose knots drifted apart is not emitted.
    float maxEdge2 = M8_MEMBRANE_MAX_EDGE * M8_MEMBRANE_MAX_EDGE;
    float3 e0 = patch.corner10 - patch.corner00;
    float3 e1 = patch.corner11 - patch.corner10;
    float3 e2 = patch.corner01 - patch.corner11;
    float3 e3 = patch.corner00 - patch.corner01;
    if (dot(e0, e0) > maxEdge2 || dot(e1, e1) > maxEdge2 ||
        dot(e2, e2) > maxEdge2 || dot(e3, e3) > maxEdge2)
        return false;
    if (dot(cross(patch.corner10 - patch.corner00,
            patch.corner11 - patch.corner00), normal) < 0.0)
    {
        float3 temporary = patch.corner10;
        patch.corner10 = patch.corner01;
        patch.corner01 = temporary;
        uint temporaryColor = patch.color10;
        patch.color10 = patch.color01;
        patch.color01 = temporaryColor;
        int3 temporaryLine = patch.line10;
        patch.line10 = patch.line01;
        patch.line01 = temporaryLine;
        uint temporaryKey = patch.key10;
        patch.key10 = patch.key01;
        patch.key01 = temporaryKey;
    }
    patch.chart = chart;
    patch.side = side;
    patch.normal = normal;
    patch.packedColor = state.packedColor;
    return true;
}

float3 M8OverlapPatchCorner(M8OverlapPatch patch, uint corner)
{
    if (corner == 0u) return patch.corner00;
    if (corner == 1u) return patch.corner10;
    if (corner == 2u) return patch.corner11;
    return patch.corner01;
}

uint M8OverlapPatchColor(M8OverlapPatch patch, uint corner)
{
    if (corner == 0u) return patch.color00;
    if (corner == 1u) return patch.color10;
    if (corner == 2u) return patch.color11;
    return patch.color01;
}

int3 M8OverlapPatchLine(M8OverlapPatch patch, uint corner)
{
    if (corner == 0u) return patch.line00;
    if (corner == 1u) return patch.line10;
    if (corner == 2u) return patch.line11;
    return patch.line01;
}

uint M8OverlapTriangleCorner(uint vertex)
{
    if (vertex == 0u || vertex == 3u) return 0u;
    if (vertex == 1u) return 1u;
    if (vertex == 2u || vertex == 4u) return 2u;
    return 3u;
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
