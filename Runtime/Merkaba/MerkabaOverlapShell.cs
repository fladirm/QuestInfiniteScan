using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Unity.Mathematics;

namespace Genesis.RoomScan
{
    /// <summary>
    /// CPU/codegen authority for the disposable oriented M8 overlap patch.
    /// KernelState remains the only persistent world state.
    /// </summary>
    internal static class MerkabaOverlapShell
    {
        internal const int CanonicalNormalCount = 13;
        internal const int CornersPerPatch = 4;
        internal const int TrianglesPerPatch = 2;
        internal const int VerticesPerPatch = 6;
        internal const int MaximumContributorsPerCorner = 12;
        internal const float PatchHalfExtent = MerkabaConstants.HalfSupport;

        private static readonly byte[] TriangleOrder = { 0, 1, 2, 0, 2, 3 };
        private static readonly int3[] NormalDictionary =
        {
            new(1, 0, 0), new(0, 1, 0), new(0, 0, 1),
            new(1, 1, 0), new(1, -1, 0),
            new(1, 0, 1), new(1, 0, -1),
            new(0, 1, 1), new(0, 1, -1),
            new(1, 1, 1), new(1, 1, -1),
            new(1, -1, 1), new(1, -1, -1)
        };
        private static readonly int3[] ImmediateOffsets = BuildImmediateOffsets();

        internal readonly struct Corner : IEquatable<Corner>
        {
            internal readonly float3 GridPosition;
            internal readonly uint PackedColor;
            internal readonly int ContributorCount;

            internal Corner(float3 gridPosition, uint packedColor,
                int contributorCount)
            {
                GridPosition = gridPosition;
                PackedColor = packedColor;
                ContributorCount = contributorCount;
            }

            public bool Equals(Corner other) =>
                math.all(GridPosition == other.GridPosition) &&
                PackedColor == other.PackedColor &&
                ContributorCount == other.ContributorCount;

            public override bool Equals(object obj) =>
                obj is Corner other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(
                GridPosition.x, GridPosition.y, GridPosition.z,
                PackedColor, ContributorCount);
        }

        internal readonly struct Patch : IEquatable<Patch>
        {
            internal readonly int3 Main;
            internal readonly byte NormalIndex;
            internal readonly float3 Normal;
            internal readonly float3 Tangent0;
            internal readonly float3 Tangent1;
            internal readonly Corner Corner00;
            internal readonly Corner Corner10;
            internal readonly Corner Corner11;
            internal readonly Corner Corner01;

            internal Patch(int3 main, int normalIndex, float3 normal,
                float3 tangent0, float3 tangent1, Corner corner00,
                Corner corner10, Corner corner11, Corner corner01)
            {
                Main = main;
                NormalIndex = (byte)normalIndex;
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
                if ((uint)vertex >= VerticesPerPatch)
                    throw new ArgumentOutOfRangeException(nameof(vertex));
                return GetCorner(TriangleOrder[vertex]);
            }

            public bool Equals(Patch other)
            {
                if (!math.all(Main == other.Main) ||
                    NormalIndex != other.NormalIndex ||
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
                Main.x, Main.y, Main.z, NormalIndex);
        }

        internal static ReadOnlySpan<int3> CanonicalNormals => NormalDictionary;
        internal static ReadOnlySpan<int3> CanonicalImmediateOffsets =>
            ImmediateOffsets;

        internal static int SelectCanonicalOrientation(float3 normalGrid)
        {
            float lengthSquared = math.lengthsq(normalGrid);
            if (!(lengthSquared > 0f))
                throw new ArgumentOutOfRangeException(nameof(normalGrid));
            float3 normalized = normalGrid * math.rsqrt(lengthSquared);
            Span<float> alignments = stackalloc float[CanonicalNormalCount];
            for (int index = 0; index < NormalDictionary.Length; index++)
            {
                float3 branch = math.normalize((float3)NormalDictionary[index]);
                alignments[index] = math.abs(math.dot(normalized, branch));
            }
            return SelectCanonicalOrientationFromAlignments(alignments);
        }

        internal static int SelectCanonicalOrientationFromAlignments(
            ReadOnlySpan<float> alignments)
        {
            if (alignments.Length != CanonicalNormalCount)
                throw new ArgumentOutOfRangeException(nameof(alignments));
            int bestIndex = 0;
            float bestAlignment = alignments[0];
            for (int index = 1; index < alignments.Length; index++)
            {
                if (alignments[index] > bestAlignment)
                {
                    bestAlignment = alignments[index];
                    bestIndex = index;
                }
            }
            return bestIndex;
        }

        internal static void TangentBasis(int normalIndex, out float3 normal,
            out float3 tangent0, out float3 tangent1)
        {
            if ((uint)normalIndex >= CanonicalNormalCount)
                throw new ArgumentOutOfRangeException(nameof(normalIndex));
            normal = math.normalize((float3)NormalDictionary[normalIndex]);
            float3 absolute = math.abs(normal);
            int helperIndex = absolute.x <= absolute.y &&
                absolute.x <= absolute.z ? 0 :
                absolute.y <= absolute.z ? 1 : 2;
            float3 helper = helperIndex == 0 ? new float3(1, 0, 0) :
                helperIndex == 1 ? new float3(0, 1, 0) :
                new float3(0, 0, 1);
            tangent0 = math.normalize(math.cross(normal, helper));
            float first = tangent0.x != 0f ? tangent0.x :
                tangent0.y != 0f ? tangent0.y : tangent0.z;
            if (first < 0f) tangent0 = -tangent0;
            tangent1 = math.normalize(math.cross(normal, tangent0));
        }

        internal static bool TryBuildPatch(int3 main,
            Func<int3, KernelState> sample, out Patch patch) =>
            TryBuildPatch(main, sample, ImmediateOffsets, out patch);

        internal static bool TryBuildPatch(int3 main,
            Func<int3, KernelState> sample, IReadOnlyList<int3> donorOrder,
            out Patch patch)
        {
            if (sample == null) throw new ArgumentNullException(nameof(sample));
            if (donorOrder == null)
                throw new ArgumentNullException(nameof(donorOrder));
            KernelState mainState = sample(main);
            uint encodedOrientation = mainState.SurfaceOrientation;
            if (!mainState.IsOccupied || encodedOrientation == 0u ||
                encodedOrientation > CanonicalNormalCount)
            {
                patch = default;
                return false;
            }

            int normalIndex = (int)encodedOrientation - 1;
            TangentBasis(normalIndex, out float3 normal,
                out float3 tangent0, out float3 tangent1);
            Corner corner00 = BuildCorner(main, mainState, sample, donorOrder,
                normalIndex, normal, tangent0, tangent1, -1, -1);
            Corner corner10 = BuildCorner(main, mainState, sample, donorOrder,
                normalIndex, normal, tangent0, tangent1, 1, -1);
            Corner corner11 = BuildCorner(main, mainState, sample, donorOrder,
                normalIndex, normal, tangent0, tangent1, 1, 1);
            Corner corner01 = BuildCorner(main, mainState, sample, donorOrder,
                normalIndex, normal, tangent0, tangent1, -1, 1);
            patch = new Patch(main, normalIndex, normal, tangent0, tangent1,
                corner00, corner10, corner11, corner01);
            return true;
        }

        private static Corner BuildCorner(int3 main, KernelState mainState,
            Func<int3, KernelState> sample, IReadOnlyList<int3> donorOrder,
            int normalIndex, float3 normal, float3 tangent0, float3 tangent1,
            int tangentSign0, int tangentSign1)
        {
            var heights = new List<int>(MaximumContributorsPerCorner) { 0 };
            var colors = new List<uint>(MaximumContributorsPerCorner);
            if (mainState.ColorConfidence > 0u)
                colors.Add(mainState.PackedColor);

            float3 cornerInSteps = tangent0 * tangentSign0 +
                tangent1 * tangentSign1;
            int3 integerNormal = NormalDictionary[normalIndex];
            foreach (int3 offset in donorOrder)
            {
                ValidateImmediateOffset(offset);
                KernelState donor = sample(main + offset);
                if (!donor.IsOccupied ||
                    donor.SurfaceOrientation != (uint)normalIndex + 1u ||
                    !DonorSupportContainsCorner(offset, cornerInSteps,
                        tangent0, tangent1))
                    continue;
                if (heights.Count >= MaximumContributorsPerCorner)
                    throw new InvalidOperationException(
                        "Immediate overlap contributor bound exceeded.");
                heights.Add(math.dot(offset, integerNormal));
                if (donor.ColorConfidence > 0u)
                    colors.Add(donor.PackedColor);
            }

            heights.Sort();
            int lower = heights[(heights.Count - 1) >> 1];
            int upper = heights[heights.Count >> 1];
            float normalLength = math.length((float3)integerNormal);
            float height = (lower + upper) * 0.5f *
                MerkabaConstants.LatticeStep / normalLength;
            float3 center = (float3)main * MerkabaConstants.LatticeStep;
            float3 baseCorner = center +
                (tangent0 * tangentSign0 + tangent1 * tangentSign1) *
                PatchHalfExtent;
            uint packedColor = colors.Count == 0
                ? mainState.PackedColor : MedianColor(colors);
            return new Corner(baseCorner + normal * height, packedColor,
                heights.Count);
        }

        internal static bool DonorSupportContainsCorner(int3 offset,
            float3 cornerInSteps, float3 tangent0, float3 tangent1)
        {
            float3 relative = cornerInSteps - (float3)offset;
            return math.abs(math.dot(relative, tangent0)) <= 1f &&
                   math.abs(math.dot(relative, tangent1)) <= 1f;
        }

        private static uint MedianColor(List<uint> colors)
        {
            byte red = MedianChannel(colors, 0);
            byte green = MedianChannel(colors, 8);
            byte blue = MedianChannel(colors, 16);
            return red | ((uint)green << 8) | ((uint)blue << 16) |
                0xff000000u;
        }

        private static byte MedianChannel(List<uint> colors, int shift)
        {
            var values = new byte[colors.Count];
            for (int index = 0; index < colors.Count; index++)
                values[index] = (byte)((colors[index] >> shift) & 0xffu);
            Array.Sort(values);
            int lower = values[(values.Length - 1) >> 1];
            int upper = values[values.Length >> 1];
            return (byte)((lower + upper) >> 1);
        }

        private static int3[] BuildImmediateOffsets()
        {
            var result = new int3[26];
            int index = 0;
            for (int z = -1; z <= 1; z++)
            for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
            {
                if (x == 0 && y == 0 && z == 0) continue;
                result[index++] = new int3(x, y, z);
            }
            return result;
        }

        private static void ValidateImmediateOffset(int3 offset)
        {
            if (math.all(offset == 0) || math.any(offset < -1) ||
                math.any(offset > 1))
                throw new InvalidOperationException(
                    $"Overlap patch queried non-immediate offset {offset}.");
        }

#if UNITY_EDITOR
        internal static string BuildSurfaceOrientationHlsl()
        {
            var output = new StringBuilder();
            output.AppendLine("// GENERATED from MerkabaOverlapShell.cs. DO NOT EDIT.");
            output.AppendLine("#ifndef GENESIS_MERKABA_SURFACE_ORIENTATION_INCLUDED");
            output.AppendLine("#define GENESIS_MERKABA_SURFACE_ORIENTATION_INCLUDED");
            output.AppendLine();
            output.AppendLine($"#define MERKABA_SURFACE_ORIENTATION_COUNT {CanonicalNormalCount}u");
            output.AppendLine($"#define MERKABA_SURFACE_PLANE_NORMAL_U_SHIFT {MerkabaConstants.SurfacePlaneNormalUShift}u");
            output.AppendLine($"#define MERKABA_SURFACE_PLANE_NORMAL_V_SHIFT {MerkabaConstants.SurfacePlaneNormalVShift}u");
            output.AppendLine($"#define MERKABA_SURFACE_PLANE_OFFSET_SHIFT {MerkabaConstants.SurfacePlaneOffsetShift}u");
            output.AppendLine($"#define MERKABA_SURFACE_PLANE_NORMAL_MASK 0x{MerkabaConstants.SurfacePlaneNormalMask:x}u");
            output.AppendLine($"#define MERKABA_SURFACE_PLANE_OFFSET_MASK 0x{MerkabaConstants.SurfacePlaneOffsetMask:x}u");
            output.AppendLine($"#define MERKABA_SURFACE_PLANE_VALID_FLAG 0x{MerkabaConstants.SurfacePlaneValidFlag:x}u");
            output.AppendLine($"#define MERKABA_SURFACE_PLANE_STORAGE_MASK 0x{MerkabaConstants.SurfacePlaneStorageMask:x}u");
            output.AppendLine($"#define MERKABA_SURFACE_PLANE_OFFSET_RANGE {MerkabaConstants.SurfacePlaneOffsetRange.ToString("R", CultureInfo.InvariantCulture)}");
            output.AppendLine();
            output.AppendLine("int3 M8CanonicalSurfaceOrientationNormal(uint index)");
            output.AppendLine("{");
            int3 fallback = NormalDictionary[^1];
            output.AppendLine($"    int3 value = int3({fallback.x}, {fallback.y}, {fallback.z});");
            for (int index = 0; index < NormalDictionary.Length - 1; index++)
            {
                int3 normal = NormalDictionary[index];
                string keyword = index == 0 ? "if" : "else if";
                output.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "    {0} (index == {1}u) value = int3({2}, {3}, {4});",
                    keyword, index, normal.x, normal.y, normal.z));
            }
            output.AppendLine("    return value;");
            output.AppendLine("}");
            output.AppendLine();
            output.AppendLine("uint M8SelectCanonicalSurfaceOrientation(float3 normalGrid)");
            output.AppendLine("{");
            output.AppendLine("    float3 normalized = normalize(normalGrid);");
            output.AppendLine("    uint bestIndex = 0u;");
            output.AppendLine("    float bestAlignment = -1.0;");
            output.AppendLine("    [loop]");
            output.AppendLine("    for (uint index = 0u; index < MERKABA_SURFACE_ORIENTATION_COUNT; index++)");
            output.AppendLine("    {");
            output.AppendLine("        float3 branch = normalize((float3)M8CanonicalSurfaceOrientationNormal(index));");
            output.AppendLine("        float alignment = abs(dot(normalized, branch));");
            output.AppendLine("        if (alignment > bestAlignment)");
            output.AppendLine("        {");
            output.AppendLine("            bestAlignment = alignment;");
            output.AppendLine("            bestIndex = index;");
            output.AppendLine("        }");
            output.AppendLine("    }");
            output.AppendLine("    return bestIndex;");
            output.AppendLine("}");
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
            output.AppendLine("uint M8GetSurfaceOrientation(uint flags)");
            output.AppendLine("{");
            output.AppendLine("    if (!M8HasSurfacePlane(flags)) return 0u;");
            output.AppendLine("    float3 normal;");
            output.AppendLine("    float ignoredOffset;");
            output.AppendLine("    M8DecodeSurfacePlane(flags, normal, ignoredOffset);");
            output.AppendLine("    return M8SelectCanonicalSurfaceOrientation(normal) + 1u;");
            output.AppendLine("}");
            output.AppendLine();
            output.AppendLine("uint M8SetSurfaceOrientation(uint flags, uint branchIndex)");
            output.AppendLine("{");
            output.AppendLine("    return M8SetSurfacePlane(flags,");
            output.AppendLine("        (float3)M8CanonicalSurfaceOrientationNormal(branchIndex), 0.0);");
            output.AppendLine("}");
            output.AppendLine();
            output.AppendLine("uint M8ClearSurfaceOrientation(uint flags)");
            output.AppendLine("{");
            output.AppendLine("    return M8ClearSurfacePlane(flags);");
            output.AppendLine("}");
            output.AppendLine();
            output.AppendLine("#endif");
            return output.ToString();
        }

        internal static string BuildGeneratedHlsl()
        {
            var output = new StringBuilder(12000);
            output.Append(GeneratedHlslPrefix);
            for (int index = 0; index < ImmediateOffsets.Length; index++)
            {
                int3 offset = ImmediateOffsets[index];
                string keyword = index == 0 ? "if" : "else if";
                output.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "    {0} (index == {1}u) value = int3({2}, {3}, {4});",
                    keyword, index, offset.x, offset.y, offset.z));
            }
            output.Append(GeneratedHlslSuffix);
            return output.ToString();
        }

        private static readonly string GeneratedHlslPrefix = @"// GENERATED from MerkabaOverlapShell.cs. DO NOT EDIT.
__HASH__ifndef GENESIS_MERKABA_OVERLAP_SHELL_INCLUDED
__HASH__define GENESIS_MERKABA_OVERLAP_SHELL_INCLUDED

__HASH__include ""MerkabaSurfaceOrientation.generated.hlsl""

__HASH__define M8_OVERLAP_TRIANGLES_PER_PATCH 2u
__HASH__define M8_OVERLAP_MAX_CONTRIBUTORS 12u
__HASH__define M8_OVERLAP_NEIGHBOUR_COUNT 26u
__HASH__define M8_OVERLAP_PATCH_HALF_EXTENT 0.025

struct M8OverlapCorner
{
    float3 gridPosition;
    uint packedColor;
};

struct M8OverlapPatch
{
    M8OverlapCorner corner00;
    M8OverlapCorner corner10;
    M8OverlapCorner corner11;
    M8OverlapCorner corner01;
};

void M8OverlapTangentBasis(uint normalIndex, out float3 normal,
    out float3 tangent0, out float3 tangent1)
{
    normal = normalize((float3)M8CanonicalSurfaceOrientationNormal(normalIndex));
    float3 absolute = abs(normal);
    uint helperIndex = absolute.x <= absolute.y && absolute.x <= absolute.z
        ? 0u : (absolute.y <= absolute.z ? 1u : 2u);
    float3 helper = helperIndex == 0u ? float3(1, 0, 0) :
        (helperIndex == 1u ? float3(0, 1, 0) : float3(0, 0, 1));
    tangent0 = normalize(cross(normal, helper));
    float first = tangent0.x != 0.0 ? tangent0.x :
        (tangent0.y != 0.0 ? tangent0.y : tangent0.z);
    if (first < 0.0) tangent0 = -tangent0;
    tangent1 = normalize(cross(normal, tangent0));
}

int3 M8OverlapNeighbourOffset(uint index)
{
    int3 value = int3(1, 1, 1);
".Replace("__HASH__", "#");

        private static readonly string GeneratedHlslSuffix = @"    return value;
}

bool M8OverlapDonorContainsCorner(int3 offset, float3 cornerInSteps,
    float3 tangent0, float3 tangent1)
{
    float3 relative = cornerInSteps - (float3)offset;
    return abs(dot(relative, tangent0)) <= 1.0 &&
        abs(dot(relative, tangent1)) <= 1.0;
}

void M8SortOverlapInts(inout int values[M8_OVERLAP_MAX_CONTRIBUTORS],
    uint count)
{
    [loop]
    for (uint index = 1u; index < count; index++)
    {
        int value = values[index];
        uint insert = index;
        [loop]
        while (insert > 0u && value < values[insert - 1u])
        {
            values[insert] = values[insert - 1u];
            insert--;
        }
        values[insert] = value;
    }
}

uint M8MedianOverlapColorChannel(
    uint colors[M8_OVERLAP_MAX_CONTRIBUTORS], uint count, uint shift)
{
    int values[M8_OVERLAP_MAX_CONTRIBUTORS];
    [loop]
    for (uint index = 0u; index < count; index++)
        values[index] = (int)((colors[index] >> shift) & 255u);
    M8SortOverlapInts(values, count);
    int lower = values[(count - 1u) >> 1u];
    int upper = values[count >> 1u];
    return (uint)((lower + upper) >> 1);
}

uint M8MedianOverlapColor(
    uint colors[M8_OVERLAP_MAX_CONTRIBUTORS], uint count,
    uint fallbackColor)
{
    if (count == 0u) return fallbackColor;
    uint red = M8MedianOverlapColorChannel(colors, count, 0u);
    uint green = M8MedianOverlapColorChannel(colors, count, 8u);
    uint blue = M8MedianOverlapColorChannel(colors, count, 16u);
    return red | (green << 8u) | (blue << 16u) | 0xff000000u;
}

M8OverlapCorner M8BuildOrientedOverlapCorner(int3 globalCoord,
    int3 mainHaloCoord, uint orientation, float3 normal, float3 tangent0,
    float3 tangent1, int tangentSign0, int tangentSign1)
{
    KernelState mainState = M8ShellState(mainHaloCoord);
    int heightNumerators[M8_OVERLAP_MAX_CONTRIBUTORS];
    uint colors[M8_OVERLAP_MAX_CONTRIBUTORS];
    uint heightCount = 1u;
    uint colorCount = 0u;
    heightNumerators[0] = 0;
    if (mainState.colorConfidence > 0u)
        colors[colorCount++] = mainState.packedColor;

    float3 cornerInSteps = tangent0 * tangentSign0 +
        tangent1 * tangentSign1;
    int3 integerNormal = M8CanonicalSurfaceOrientationNormal(orientation - 1u);
    [loop]
    for (uint donorIndex = 0u;
         donorIndex < M8_OVERLAP_NEIGHBOUR_COUNT; donorIndex++)
    {
        int3 offset = M8OverlapNeighbourOffset(donorIndex);
        KernelState donor = M8ShellState(mainHaloCoord + offset);
        if ((donor.flags & MERKABA_OCCUPIED_FLAG) == 0u ||
            M8GetSurfaceOrientation(donor.flags) != orientation ||
            !M8OverlapDonorContainsCorner(offset, cornerInSteps,
                tangent0, tangent1))
            continue;
        heightNumerators[heightCount++] = dot(offset, integerNormal);
        if (donor.colorConfidence > 0u)
            colors[colorCount++] = donor.packedColor;
    }

    M8SortOverlapInts(heightNumerators, heightCount);
    int lower = heightNumerators[(heightCount - 1u) >> 1u];
    int upper = heightNumerators[heightCount >> 1u];
    float height = (lower + upper) * 0.5 * MERKABA_LATTICE_STEP /
        length((float3)integerNormal);
    float3 center = (float3)globalCoord * MERKABA_LATTICE_STEP;
    M8OverlapCorner corner;
    corner.gridPosition = center +
        (tangent0 * tangentSign0 + tangent1 * tangentSign1) *
            M8_OVERLAP_PATCH_HALF_EXTENT + normal * height;
    corner.packedColor = M8MedianOverlapColor(colors, colorCount,
        mainState.packedColor);
    return corner;
}

M8OverlapPatch M8BuildOrientedOverlapPatch(int3 globalCoord,
    int3 mainHaloCoord, uint orientation)
{
    float3 normal;
    float3 tangent0;
    float3 tangent1;
    M8OverlapTangentBasis(orientation - 1u, normal, tangent0, tangent1);
    M8OverlapPatch patch;
    patch.corner00 = M8BuildOrientedOverlapCorner(globalCoord,
        mainHaloCoord, orientation, normal, tangent0, tangent1, -1, -1);
    patch.corner10 = M8BuildOrientedOverlapCorner(globalCoord,
        mainHaloCoord, orientation, normal, tangent0, tangent1, 1, -1);
    patch.corner11 = M8BuildOrientedOverlapCorner(globalCoord,
        mainHaloCoord, orientation, normal, tangent0, tangent1, 1, 1);
    patch.corner01 = M8BuildOrientedOverlapCorner(globalCoord,
        mainHaloCoord, orientation, normal, tangent0, tangent1, -1, 1);
    return patch;
}

M8OverlapCorner M8OverlapPatchCorner(M8OverlapPatch patch, uint index)
{
    M8OverlapCorner corner = patch.corner01;
    if (index == 0u) corner = patch.corner00;
    else if (index == 1u) corner = patch.corner10;
    else if (index == 2u) corner = patch.corner11;
    return corner;
}

uint M8OverlapTriangleCorner(uint vertex)
{
    uint index = 3u;
    if (vertex == 0u || vertex == 3u) index = 0u;
    else if (vertex == 1u) index = 1u;
    else if (vertex == 2u || vertex == 4u) index = 2u;
    return index;
}

__HASH__endif
".Replace("__HASH__", "#");
#endif
    }
}
