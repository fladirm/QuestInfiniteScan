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
        // A contributor plane must be well conditioned against the corner
        // line of MAIN's chart; the connected height branch and the free side
        // decide sheet membership. No quantized dominant-axis or 26-cell
        // equality (MERKABA_GEOMETRY_REVIEW FINDING B).
        internal const float MembraneCompatibleAxisCosine = 0.5f;

        private static readonly byte[] TriangleOrder = { 0, 1, 2, 0, 2, 3 };

        internal readonly struct Corner : IEquatable<Corner>
        {
            internal readonly float3 GridPosition;
            internal readonly uint PackedColor;
            // Global identity of the shared knot: the half-lattice address of
            // its corner line (dominant component zero) and the chart axis.
            // Two patches share one vertex exactly when LineAddress, Chart and
            // the resolved position bits agree, on any tile of any consumer.
            internal readonly int3 LineAddress;
            internal readonly int Chart;
            // Export attribute only (the live vertex ABI carries no normal):
            // the mean unit normal of the same contributors, same order.
            internal readonly float3 Normal;

            internal Corner(float3 gridPosition, uint packedColor,
                int3 lineAddress, int chart, float3 normal)
            {
                GridPosition = gridPosition;
                PackedColor = packedColor;
                LineAddress = lineAddress;
                Chart = chart;
                Normal = normal;
            }

            public bool Equals(Corner other) =>
                math.all(GridPosition == other.GridPosition) &&
                PackedColor == other.PackedColor &&
                math.all(LineAddress == other.LineAddress) &&
                Chart == other.Chart &&
                math.all(Normal == other.Normal);

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
            IReadOnlyDictionary<int3, KernelState> context, out Patch patch)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (!context.TryGetValue(main, out KernelState state))
            {
                patch = default;
                return false;
            }
            return TryBuildPatch(main, state, context, out patch);
        }

        /// <summary>
        /// Isolated-context convenience used by fixtures. It executes the same
        /// membrane oracle; every coordinate other than MAIN is UNKNOWN.
        /// </summary>
        internal static bool TryBuildPatch(int3 main, KernelState state,
            out Patch patch) => TryBuildPatch(main, state, null, out patch);

        private static bool TryBuildPatch(int3 main, KernelState state,
            IReadOnlyDictionary<int3, KernelState> context, out Patch patch)
        {
            if (!state.IsOccupied || !state.HasMeasuredSurfacePlane)
            {
                patch = default;
                return false;
            }

            KernelState.DecodeSurfacePlane(state.Flags, out float3 normal,
                out float signedOffset);
            int dominantAxis = DominantAxis(normal);
            TangentAxes(dominantAxis, out int tangentAxis0,
                out int tangentAxis1);
            float3 tangent0 = AxisVector(tangentAxis0);
            float3 tangent1 = AxisVector(tangentAxis1);

            int freeSignature = FreeSideSignature(main, dominantAxis, context);
            if (!TryResolveCorner(main, state, normal, signedOffset,
                    dominantAxis, tangentAxis0, tangentAxis1, -1, -1,
                    freeSignature, context, out Corner corner00) ||
                !TryResolveCorner(main, state, normal, signedOffset,
                    dominantAxis, tangentAxis0, tangentAxis1, 1, -1,
                    freeSignature, context, out Corner corner10) ||
                !TryResolveCorner(main, state, normal, signedOffset,
                    dominantAxis, tangentAxis0, tangentAxis1, 1, 1,
                    freeSignature, context, out Corner corner11) ||
                !TryResolveCorner(main, state, normal, signedOffset,
                    dominantAxis, tangentAxis0, tangentAxis1, -1, 1,
                    freeSignature, context, out Corner corner01))
            {
                patch = default;
                return false;
            }

            // Corner addresses are canonical. Only index orientation changes.
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

        private static bool TryResolveCorner(int3 main, KernelState mainState,
            float3 mainNormal, float mainOffset, int dominantAxis,
            int tangentAxis0, int tangentAxis1, int cornerSign0,
            int cornerSign1, int mainFreeSignature,
            IReadOnlyDictionary<int3, KernelState> context,
            out Corner corner)
        {
            int3 halfAddress = main * 2;
            halfAddress[tangentAxis0] += cornerSign0;
            halfAddress[tangentAxis1] += cornerSign1;
            float3 line = (float3)halfAddress * (MembranePatchPitch * 0.5f);
            int3 lineAddress = halfAddress;
            lineAddress[dominantAxis] = 0;
            corner = default;
            if (!TryPlaneLineHeight(main, mainNormal, mainOffset,
                    dominantAxis, line, out float mainHeight))
                return false;

            int lower0 = MerkabaConstants.FloorDiv(halfAddress[tangentAxis0], 2);
            int lower1 = MerkabaConstants.FloorDiv(halfAddress[tangentAxis1], 2);
            Span<float> heights = stackalloc float[CornerCandidateCapacity];
            Span<int> signatures = stackalloc int[CornerCandidateCapacity];
            Span<int3> coords = stackalloc int3[CornerCandidateCapacity];
            Span<int> columns = stackalloc int[CornerCandidateCapacity];
            // Contract C5 step 6: after the residual, the nearer normal layer
            // wins, then the lexicographically smaller coordinate.
            Span<int> layers = stackalloc int[CornerCandidateCapacity];
            int count = 0;

            // Tangent axes are returned in ascending coordinate order, so this
            // fixed enumeration is lexicographic and MAIN-independent.
            for (int first = 0; first < 2; first++)
            for (int second = 0; second < 2; second++)
            {
                int3 column = main;
                column[tangentAxis0] = lower0 + first;
                column[tangentAxis1] = lower1 + second;
                for (int normalOffset = -1; normalOffset <= 1;
                     normalOffset++)
                {
                    int3 coord = column;
                    coord[dominantAxis] = main[dominantAxis] + normalOffset;
                    if (!TryGetState(coord, main, mainState, context,
                            out KernelState candidate) ||
                        !candidate.IsOccupied ||
                        !candidate.HasMeasuredSurfacePlane ||
                        IsSeparatedByKnownFree(coord, normalOffset,
                            dominantAxis, main, mainState, context))
                        continue;
                    KernelState.DecodeSurfacePlane(candidate.Flags,
                        out float3 candidateNormal, out float candidateOffset);
                    if (math.abs(candidateNormal[dominantAxis]) <
                            MembraneCompatibleAxisCosine ||
                        !TryPlaneLineHeight(coord, candidateNormal,
                            candidateOffset, dominantAxis, line,
                            out float height))
                        continue;
                    heights[count] = height;
                    signatures[count] = FreeSideSignature(coord, dominantAxis,
                        context, main, mainState);
                    coords[count] = coord;
                    columns[count] = first * 2 + second;
                    layers[count] = math.abs(normalOffset);
                    count++;
                }
            }
            if (count == 0) return false;

            // MAIN chooses only WHICH sheet branch this corner belongs to:
            // same free side first, then nearest to its own measured plane.
            int seed = 0;
            for (int index = 1; index < count; index++)
            {
                bool signature = signatures[index] == mainFreeSignature;
                bool seedSignature = signatures[seed] == mainFreeSignature;
                float residual = math.abs(heights[index] - mainHeight);
                float seedResidual = math.abs(heights[seed] - mainHeight);
                if ((signature && !seedSignature) ||
                    (signature == seedSignature &&
                     (residual < seedResidual - NumericalEpsilon ||
                      (math.abs(residual - seedResidual) <= NumericalEpsilon &&
                       (layers[index] < layers[seed] ||
                        (layers[index] == layers[seed] &&
                         LexicographicallyLess(coords[index], coords[seed])))))))
                    seed = index;
            }

            // The branch is the connected height interval of its free side:
            // membership, median and per-column contributors are functions of
            // the corner neighbourhood alone, so every patch of the same
            // branch resolves a bit-identical shared corner.
            Span<int> order = stackalloc int[CornerCandidateCapacity];
            int ordered = 0;
            for (int index = 0; index < count; index++)
                if (signatures[index] == signatures[seed])
                    order[ordered++] = index;
            for (int index = 1; index < ordered; index++)
            {
                int value = order[index];
                int slot = index;
                while (slot > 0 && CornerCandidateBefore(value, order[slot - 1],
                           heights, coords))
                {
                    order[slot] = order[slot - 1];
                    slot--;
                }
                order[slot] = value;
            }
            int seedPosition = 0;
            while (order[seedPosition] != seed) seedPosition++;
            int low = seedPosition;
            int high = seedPosition;
            while (low > 0 && heights[order[low]] - heights[order[low - 1]] <=
                   BranchHeightGap)
                low--;
            while (high + 1 < ordered &&
                   heights[order[high + 1]] - heights[order[high]] <=
                   BranchHeightGap)
                high++;
            int members = high - low + 1;
            float median = (members & 1) != 0
                ? heights[order[low + members / 2]]
                : (heights[order[low + members / 2 - 1]] +
                   heights[order[low + members / 2]]) * 0.5f;

            float heightSum = 0f;
            int accepted = 0;
            uint red = 0u, green = 0u, blue = 0u, weightTotal = 0u;
            float3 normalSum = float3.zero;
            for (int columnIndex = 0; columnIndex < 4; columnIndex++)
            {
                int best = -1;
                float bestDistance = float.PositiveInfinity;
                for (int position2 = low; position2 <= high; position2++)
                {
                    int index = order[position2];
                    if (columns[index] != columnIndex) continue;
                    float distance = math.abs(heights[index] - median);
                    if (best < 0 || distance < bestDistance - NumericalEpsilon ||
                        (math.abs(distance - bestDistance) <= NumericalEpsilon &&
                         (layers[index] < layers[best] ||
                          (layers[index] == layers[best] &&
                           LexicographicallyLess(coords[index], coords[best])))))
                    {
                        best = index;
                        bestDistance = distance;
                    }
                }
                if (best < 0) continue;
                heightSum += heights[best];
                accepted++;
                // The knot's colour is the confidence-weighted mean of the
                // same contributors, in the same column order, so it is one
                // value for every patch that shares the knot.
                TryGetState(coords[best], main, mainState, context,
                    out KernelState owner);
                KernelState.DecodeSurfacePlane(owner.Flags,
                    out float3 ownerNormal, out _);
                normalSum += ownerNormal;
                uint weight = math.max(1u, owner.ColorConfidence);
                uint packed = owner.PackedColor;
                red += (packed & 255u) * weight;
                green += ((packed >> 8) & 255u) * weight;
                blue += ((packed >> 16) & 255u) * weight;
                weightTotal += weight;
            }

            if (accepted == 0) return false;
            line[dominantAxis] = heightSum / accepted;
            if (!math.all(math.isfinite(line))) return false;
            float inverse = 1f / math.max(1u, weightTotal);
            uint r = math.min(255u, (uint)math.floor(red * inverse + 0.5f));
            uint g = math.min(255u, (uint)math.floor(green * inverse + 0.5f));
            uint b = math.min(255u, (uint)math.floor(blue * inverse + 0.5f));
            corner = new Corner(line, r | (g << 8) | (b << 16) | 0xff000000u,
                lineAddress, dominantAxis,
                math.normalizesafe(normalSum, mainNormal));
            return true;
        }

        internal const int CornerCandidateCapacity = 12;
        internal const float BranchHeightGap = MembranePatchPitch * 0.6f;

        private static bool CornerCandidateBefore(int left, int right,
            Span<float> heights, Span<int3> coords) =>
            heights[left] < heights[right] ||
            (heights[left] == heights[right] &&
             LexicographicallyLess(coords[left], coords[right]));

        private static bool TryPlaneLineHeight(int3 owner, float3 normal,
            float signedOffset, int dominantAxis, float3 line,
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
            float planeConstant = math.dot((float3)owner *
                MerkabaConstants.LatticeStep, normal) + signedOffset;
            height = (planeConstant - math.dot(basePoint, normal)) /
                denominator;
            return math.isfinite(height);
        }

        private static bool IsSeparatedByKnownFree(int3 contributor,
            int normalOffset, int dominantAxis, int3 main,
            KernelState mainState, IReadOnlyDictionary<int3, KernelState> context)
        {
            if (normalOffset == 0) return false;
            int3 towardMain = contributor;
            towardMain[dominantAxis] -= math.sign(normalOffset);
            return TryGetState(towardMain, main, mainState, context,
                       out KernelState separator) && IsKnownFree(separator);
        }

        private static int FreeSideSignature(int3 coord, int dominantAxis,
            IReadOnlyDictionary<int3, KernelState> context)
        {
            int signature = 0;
            int3 axis = AxisInt3(dominantAxis);
            if (context != null &&
                context.TryGetValue(coord - axis, out KernelState negative) &&
                IsKnownFree(negative))
                signature |= 1;
            if (context != null &&
                context.TryGetValue(coord + axis, out KernelState positive) &&
                IsKnownFree(positive))
                signature |= 2;
            return signature;
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
    int chart;
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

bool M8MembraneKnownFree(KernelState state)
{
    return (state.flags & MERKABA_OCCUPIED_FLAG) == 0u &&
        state.evidence <= MERKABA_EXPORT_KNOWN_FREE;
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
    KernelState state;
    bool resolved;
    bool exists = M8TryLoadMembraneState(coord - axis, state, resolved);
    if (!resolved)
    {
        unresolved = true;
        return false;
    }
    if (exists && M8MembraneKnownFree(state)) signature |= 1u;
    exists = M8TryLoadMembraneState(coord + axis, state, resolved);
    if (!resolved)
    {
        unresolved = true;
        return false;
    }
    if (exists && M8MembraneKnownFree(state)) signature |= 2u;
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
    KernelState separator;
    bool resolved;
    bool exists = M8TryLoadMembraneState(towardMain, separator, resolved);
    if (!resolved)
    {
        unresolved = true;
        return false;
    }
    return exists && M8MembraneKnownFree(separator);
}

bool M8MembraneResolveCorner(int3 main, KernelState mainState,
    float3 mainNormal, float mainOffset, int dominantAxis,
    int tangentAxis0, int tangentAxis1, int cornerSign0, int cornerSign1,
    uint mainFreeSignature, out float3 position, out uint packedColor,
    out int3 lineAddress, out bool unresolved)
{
    // Same math as the CPU oracle, laid out for Adreno: twelve fixed
    // candidate slots (column x normal layer), every loop unrolled with
    // constant indices, the branch grown as the connected component of the
    // height-gap relation and the median selected by rank. No private array
    // is ever indexed dynamically, so nothing spills to scratch memory.
    unresolved = false;
    packedColor = 0u;
    int3 halfAddress = main * 2;
    halfAddress = M8MembraneSetIntComponent(halfAddress, tangentAxis0,
        halfAddress[tangentAxis0] + cornerSign0);
    halfAddress = M8MembraneSetIntComponent(halfAddress, tangentAxis1,
        halfAddress[tangentAxis1] + cornerSign1);
    lineAddress = M8MembraneSetIntComponent(halfAddress, dominantAxis, 0);
    float3 cornerLine = (float3)halfAddress *
        (M8_MEMBRANE_PATCH_PITCH * 0.5);
    float mainHeight;
    if (!M8MembranePlaneLineHeight(main, mainNormal, mainOffset,
            dominantAxis, cornerLine, mainHeight))
    {
        position = 0.0;
        return false;
    }

    int lower0 = M8MembraneFloorDiv2(halfAddress[tangentAxis0]);
    int lower1 = M8MembraneFloorDiv2(halfAddress[tangentAxis1]);
    bool valid[12];
    float heights[12];
    uint signatures[12];
    int3 coords[12];
    // Contract C5 step 6: after the residual, the nearer normal layer wins,
    // then the lexicographically smaller coordinate.
    uint layers[12];
    uint count = 0u;
    [unroll]
    for (uint column = 0u; column < 4u; column++)
    [unroll]
    for (uint layer = 0u; layer < 3u; layer++)
    {
        uint slot = column * 3u + layer;
        valid[slot] = false;
        heights[slot] = 0.0;
        signatures[slot] = 0u;
        coords[slot] = int3(0, 0, 0);
        layers[slot] = layer == 1u ? 0u : 1u;
        int first = (int)(column >> 1u);
        int second = (int)(column & 1u);
        int normalOffset = (int)layer - 1;
        int3 coord = main;
        coord = M8MembraneSetIntComponent(coord, tangentAxis0,
            lower0 + first);
        coord = M8MembraneSetIntComponent(coord, tangentAxis1,
            lower1 + second);
        coord = M8MembraneSetIntComponent(coord, dominantAxis,
            main[dominantAxis] + normalOffset);
        KernelState candidate;
        bool resolved;
        bool exists = M8TryLoadMembraneState(coord, candidate, resolved);
        if (!resolved)
        {
            unresolved = true;
            position = 0.0;
            return false;
        }
        if (!exists ||
            (candidate.flags & MERKABA_OCCUPIED_FLAG) == 0u ||
            !M8HasSurfacePlane(candidate.flags))
            continue;
        bool separatorUnresolved;
        if (M8MembraneSeparatedByFree(coord, normalOffset,
                dominantAxis, separatorUnresolved))
            continue;
        if (separatorUnresolved)
        {
            unresolved = true;
            position = 0.0;
            return false;
        }
        float3 candidateNormal;
        float candidateOffset;
        M8DecodeSurfacePlane(candidate.flags, candidateNormal,
            candidateOffset);
        if (abs(candidateNormal[dominantAxis]) <
            M8_MEMBRANE_COMPATIBLE_AXIS_COSINE)
            continue;
        float height;
        if (!M8MembranePlaneLineHeight(coord, candidateNormal,
                candidateOffset, dominantAxis, cornerLine, height))
            continue;
        uint freeSignature;
        bool signatureUnresolved;
        if (!M8MembraneFreeSideSignature(coord, dominantAxis,
                freeSignature, signatureUnresolved))
        {
            if (signatureUnresolved)
            {
                unresolved = true;
                position = 0.0;
                return false;
            }
            continue;
        }
        valid[slot] = true;
        heights[slot] = height;
        signatures[slot] = freeSignature;
        coords[slot] = coord;
        count++;
    }
    if (count == 0u)
    {
        position = 0.0;
        return false;
    }

    // MAIN chooses only WHICH sheet branch this corner belongs to.
    bool haveSeed = false;
    float seedHeight = 0.0;
    int3 seedCoord = int3(0, 0, 0);
    uint seedSignature = 0u;
    uint seedLayer = 0u;
    [unroll]
    for (uint slot = 0u; slot < 12u; slot++)
    {
        if (!valid[slot]) continue;
        bool signature = signatures[slot] == mainFreeSignature;
        bool currentSignature = seedSignature == mainFreeSignature;
        float residual = abs(heights[slot] - mainHeight);
        float seedResidual = abs(seedHeight - mainHeight);
        if (!haveSeed ||
            (signature && !currentSignature) ||
            (signature == currentSignature &&
             (residual < seedResidual - M8_MEMBRANE_NUMERICAL_EPSILON ||
              (abs(residual - seedResidual) <=
                   M8_MEMBRANE_NUMERICAL_EPSILON &&
               (layers[slot] < seedLayer ||
                (layers[slot] == seedLayer &&
                 M8MembraneLexLess(coords[slot], seedCoord)))))))
        {
            haveSeed = true;
            seedHeight = heights[slot];
            seedCoord = coords[slot];
            seedSignature = signatures[slot];
            seedLayer = layers[slot];
        }
    }

    // The branch is the connected height interval of the seed's free side:
    // the component of the ""gap <= 0.6 pitch"" relation containing the seed.
    bool inBranch[12];
    [unroll]
    for (uint slot = 0u; slot < 12u; slot++)
        inBranch[slot] = valid[slot] && signatures[slot] == seedSignature &&
            all(coords[slot] == seedCoord);
    [unroll]
    for (uint expansion = 0u; expansion < 11u; expansion++)
    {
        [unroll]
        for (uint slot = 0u; slot < 12u; slot++)
        {
            if (!valid[slot] || inBranch[slot] ||
                signatures[slot] != seedSignature)
                continue;
            bool joined = false;
            [unroll]
            for (uint other = 0u; other < 12u; other++)
                if (inBranch[other] &&
                    abs(heights[slot] - heights[other]) <=
                    M8_MEMBRANE_PATCH_PITCH * 0.6)
                    joined = true;
            inBranch[slot] = joined;
        }
    }

    // Median by rank: members are totally ordered by height then coordinate.
    uint members = 0u;
    uint ranks[12];
    [unroll]
    for (uint slot = 0u; slot < 12u; slot++)
    {
        ranks[slot] = 0u;
        if (!inBranch[slot]) continue;
        members++;
        [unroll]
        for (uint other = 0u; other < 12u; other++)
            if (inBranch[other] && other != slot &&
                (heights[other] < heights[slot] ||
                 (heights[other] == heights[slot] &&
                  M8MembraneLexLess(coords[other], coords[slot]))))
                ranks[slot]++;
    }
    uint middle = members >> 1u;
    float medianHigh = 0.0;
    float medianLow = 0.0;
    [unroll]
    for (uint slot = 0u; slot < 12u; slot++)
    {
        if (!inBranch[slot]) continue;
        if (ranks[slot] == middle) medianHigh = heights[slot];
        if (middle > 0u && ranks[slot] == middle - 1u)
            medianLow = heights[slot];
    }
    float median = (members & 1u) != 0u
        ? medianHigh : (medianLow + medianHigh) * 0.5;

    float heightSum = 0.0;
    uint accepted = 0u;
    uint red = 0u;
    uint green = 0u;
    uint blue = 0u;
    uint weightTotal = 0u;
    [unroll]
    for (uint columnIndex = 0u; columnIndex < 4u; columnIndex++)
    {
        bool haveBest = false;
        float bestHeight = 0.0;
        float bestDistance = 0.0;
        int3 bestCoord = int3(0, 0, 0);
        uint bestLayer = 0u;
        [unroll]
        for (uint layer = 0u; layer < 3u; layer++)
        {
            uint slot = columnIndex * 3u + layer;
            if (!inBranch[slot]) continue;
            float distance = abs(heights[slot] - median);
            if (!haveBest ||
                distance < bestDistance - M8_MEMBRANE_NUMERICAL_EPSILON ||
                (abs(distance - bestDistance) <=
                     M8_MEMBRANE_NUMERICAL_EPSILON &&
                 (layers[slot] < bestLayer ||
                  (layers[slot] == bestLayer &&
                   M8MembraneLexLess(coords[slot], bestCoord)))))
            {
                haveBest = true;
                bestHeight = heights[slot];
                bestDistance = distance;
                bestCoord = coords[slot];
                bestLayer = layers[slot];
            }
        }
        if (!haveBest) continue;
        heightSum += bestHeight;
        accepted++;
        // The knot's colour is the confidence-weighted mean of the same
        // contributors in the same column order: one value per shared knot.
        uint ownerColor;
        uint ownerConfidence;
        M8LoadMembraneColor(bestCoord, ownerColor, ownerConfidence);
        uint weight = max(1u, ownerConfidence);
        red += (ownerColor & 255u) * weight;
        green += ((ownerColor >> 8u) & 255u) * weight;
        blue += ((ownerColor >> 16u) & 255u) * weight;
        weightTotal += weight;
    }
    if (accepted == 0u)
    {
        position = 0.0;
        return false;
    }
    cornerLine = M8MembraneSetFloatComponent(cornerLine, dominantAxis,
        heightSum / (float)accepted);
    position = cornerLine;
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
    int dominantAxis = M8MembraneDominantAxis(normal);
    int tangentAxis0;
    int tangentAxis1;
    M8MembraneTangentAxes(dominantAxis, tangentAxis0, tangentAxis1);
    uint freeSignature;
    if (!M8MembraneFreeSideSignature(main, dominantAxis, freeSignature,
            unresolved))
        return false;
    if (!M8MembraneResolveCorner(main, state, normal, signedOffset,
            dominantAxis, tangentAxis0, tangentAxis1, -1, -1,
            freeSignature, patch.corner00, patch.color00, patch.line00,
            unresolved) ||
        !M8MembraneResolveCorner(main, state, normal, signedOffset,
            dominantAxis, tangentAxis0, tangentAxis1, 1, -1,
            freeSignature, patch.corner10, patch.color10, patch.line10,
            unresolved) ||
        !M8MembraneResolveCorner(main, state, normal, signedOffset,
            dominantAxis, tangentAxis0, tangentAxis1, 1, 1,
            freeSignature, patch.corner11, patch.color11, patch.line11,
            unresolved) ||
        !M8MembraneResolveCorner(main, state, normal, signedOffset,
            dominantAxis, tangentAxis0, tangentAxis1, -1, 1,
            freeSignature, patch.corner01, patch.color01, patch.line01,
            unresolved))
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
    }
    patch.chart = dominantAxis;
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
