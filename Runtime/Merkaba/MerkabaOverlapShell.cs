using System;
using System.Globalization;
using System.Text;
using Unity.Mathematics;

namespace Genesis.RoomScan
{
    /// <summary>
    /// CPU/codegen authority for the disposable measured M8 support patch.
    /// KernelState remains the only persistent world state.
    /// </summary>
    internal static class MerkabaOverlapShell
    {
        internal const int CornersPerPatch = 4;
        internal const int TrianglesPerPatch = 2;
        internal const int VerticesPerPatch = 6;
        internal const float PatchHalfExtent = MerkabaConstants.HalfSupport;

        private static readonly byte[] TriangleOrder = { 0, 1, 2, 0, 2, 3 };

        internal readonly struct Corner : IEquatable<Corner>
        {
            internal readonly float3 GridPosition;
            internal readonly uint PackedColor;

            internal Corner(float3 gridPosition, uint packedColor)
            {
                GridPosition = gridPosition;
                PackedColor = packedColor;
            }

            public bool Equals(Corner other) =>
                math.all(GridPosition == other.GridPosition) &&
                PackedColor == other.PackedColor;

            public override bool Equals(object obj) =>
                obj is Corner other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(
                GridPosition.x, GridPosition.y, GridPosition.z, PackedColor);
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
                if ((uint)vertex >= VerticesPerPatch)
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

        internal static void TangentBasis(float3 normal,
            out float3 tangent0, out float3 tangent1)
        {
            float lengthSquared = math.lengthsq(normal);
            if (!(lengthSquared > 0f))
                throw new ArgumentOutOfRangeException(nameof(normal));
            normal *= math.rsqrt(lengthSquared);
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

        internal static bool TryBuildPatch(int3 main, KernelState state,
            out Patch patch)
        {
            if (!state.IsOccupied || !state.HasMeasuredSurfacePlane)
            {
                patch = default;
                return false;
            }

            KernelState.DecodeSurfacePlane(state.Flags, out float3 normal,
                out float signedOffset);
            TangentBasis(normal, out float3 tangent0, out float3 tangent1);
            float3 center = (float3)main * MerkabaConstants.LatticeStep +
                normal * signedOffset;
            float3 extent0 = tangent0 * PatchHalfExtent;
            float3 extent1 = tangent1 * PatchHalfExtent;
            uint color = state.PackedColor;
            patch = new Patch(main, normal, tangent0, tangent1,
                new Corner(center - extent0 - extent1, color),
                new Corner(center + extent0 - extent1, color),
                new Corner(center + extent0 + extent1, color),
                new Corner(center - extent0 + extent1, color));
            return true;
        }

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
            return string.Format(CultureInfo.InvariantCulture,
@"// GENERATED from MerkabaOverlapShell.cs. DO NOT EDIT.
#ifndef GENESIS_MERKABA_OVERLAP_SHELL_INCLUDED
#define GENESIS_MERKABA_OVERLAP_SHELL_INCLUDED

#include ""MerkabaSurfaceOrientation.generated.hlsl""

#define M8_OVERLAP_TRIANGLES_PER_PATCH {0}u
#define M8_OVERLAP_PATCH_HALF_EXTENT {1}

struct M8OverlapPatch
{{
    float3 corner00;
    float3 corner10;
    float3 corner11;
    float3 corner01;
    uint packedColor;
}};

void M8MeasuredPlaneTangentBasis(float3 normal,
    out float3 tangent0, out float3 tangent1)
{{
    float3 absolute = abs(normal);
    float3 helper = absolute.x <= absolute.y && absolute.x <= absolute.z
        ? float3(1.0, 0.0, 0.0)
        : absolute.y <= absolute.z
            ? float3(0.0, 1.0, 0.0)
            : float3(0.0, 0.0, 1.0);
    tangent0 = normalize(cross(normal, helper));
    float first = tangent0.x != 0.0 ? tangent0.x :
        tangent0.y != 0.0 ? tangent0.y : tangent0.z;
    if (first < 0.0) tangent0 = -tangent0;
    tangent1 = normalize(cross(normal, tangent0));
}}

bool M8TryBuildMeasuredPlanePatch(int3 globalCoord, KernelState state,
    out M8OverlapPatch patch)
{{
    patch = (M8OverlapPatch)0;
    if ((state.flags & MERKABA_OCCUPIED_FLAG) == 0u ||
        !M8HasSurfacePlane(state.flags))
        return false;
    float3 normal;
    float signedOffset;
    M8DecodeSurfacePlane(state.flags, normal, signedOffset);
    float3 tangent0;
    float3 tangent1;
    M8MeasuredPlaneTangentBasis(normal, tangent0, tangent1);
    float3 center = (float3)globalCoord * MERKABA_LATTICE_STEP +
        normal * signedOffset;
    float3 extent0 = tangent0 * M8_OVERLAP_PATCH_HALF_EXTENT;
    float3 extent1 = tangent1 * M8_OVERLAP_PATCH_HALF_EXTENT;
    patch.corner00 = center - extent0 - extent1;
    patch.corner10 = center + extent0 - extent1;
    patch.corner11 = center + extent0 + extent1;
    patch.corner01 = center - extent0 + extent1;
    patch.packedColor = state.packedColor;
    return true;
}}

float3 M8OverlapPatchCorner(M8OverlapPatch patch, uint corner)
{{
    if (corner == 0u) return patch.corner00;
    if (corner == 1u) return patch.corner10;
    if (corner == 2u) return patch.corner11;
    return patch.corner01;
}}

uint M8OverlapTriangleCorner(uint vertex)
{{
    if (vertex == 0u || vertex == 3u) return 0u;
    if (vertex == 1u) return 1u;
    if (vertex == 2u || vertex == 4u) return 2u;
    return 3u;
}}

#endif
", TrianglesPerPatch,
                PatchHalfExtent.ToString("R", CultureInfo.InvariantCulture));
        }
#endif
    }
}
