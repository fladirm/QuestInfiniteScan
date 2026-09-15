using System;
using UnityEngine;
using FinalScan.World;

namespace FinalScan.Render
{
    /// <summary>
    /// Managed reference of the FsDrawRecord field encodings consumed by Shaders/FinalScanSurfel.shader.
    /// The shader (HLSL) and this class must stay in lock-step; Tests/Editor/SurfelDrawAbiTests.cs pins the
    /// numeric behaviour. Packed layouts (32 B record, 16 B args) come from the generated WorldAbi.
    ///
    /// Encodings (provisional until gen_abi.py emits them):
    ///   normalOct32      bits 0..15 = oct u (u16), bits 16..31 = oct v (u16); u16 → [-1,1] via v/65535*2-1
    ///   tangentAndRadii  bits 0..15 tangentAngle (u16, [0,2π)), bits 16..23 radiusMajor u8 log,
    ///                    bits 24..31 radiusMinor u8 log; u8 log = high byte of the FsSurfel u16 log code:
    ///                    r = 0.5 mm * 2^(v8 / 16)   (0.5 mm .. ~32 m, 4.4 % steps)
    ///   colorOrHandle    RGBA8, R in bits 0..7; alpha byte = coverage 0..255 when FS_DRAW_FLAG_AGGREGATE is set
    ///   tangent frame    t0 = normalize(cross(n, ref)), ref = (0,1,0) unless |n.y| > 0.99 then (1,0,0);
    ///                    b0 = cross(n, t0); tMajor = cos(a) t0 + sin(a) b0; tMinor = cross(n, tMajor)
    /// </summary>
    public static class SurfelDrawAbi
    {
        public const int DrawRecordStride = FsDrawRecord.SizeBytes;      // 32
        public const int IndirectArgsCount = 4;                          // uint32 x4 (FsIndirectDrawArgs)
        public const uint VerticesPerSurfel = 6;                         // two triangles per record

        public const uint FlagDetail = 1u << 0;
        public const uint FlagSelected = 1u << 1;
        public const uint FlagErasedPreview = 1u << 2;
        public const uint FlagAggregate = (uint)WorldAbi.DrawFlagAggregate;   // 1 << 3
        public const uint FlagTransient = (uint)WorldAbi.DrawFlagTransient;   // 1 << 4

        public const float RadiusBaseM = 0.0005f;        // 0.5 mm
        public const float RadiusLogStepsU16 = 4096f;     // FsSurfel u16: r = base * 2^(v/4096)
        public const float RadiusLogStepsU8 = 16f;        // FsDrawRecord u8: r = base * 2^(v/16)

        /// <summary>u16 log code (FsSurfel.radiusMajor/Minor) → metres.</summary>
        public static float DecodeRadiusU16(ushort v) => RadiusBaseM * Mathf.Pow(2f, v / RadiusLogStepsU16);

        /// <summary>u8 log code (FsDrawRecord.tangentAndRadii bytes) → metres.</summary>
        public static float DecodeRadiusU8(byte v) => RadiusBaseM * Mathf.Pow(2f, v / RadiusLogStepsU8);

        /// <summary>Metres → u8 log code (round to nearest, saturating).</summary>
        public static byte EncodeRadiusU8(float metres)
        {
            if (!(metres > RadiusBaseM)) return 0;
            float v = Mathf.Log(metres / RadiusBaseM, 2f) * RadiusLogStepsU8;
            return (byte)Mathf.Clamp(Mathf.RoundToInt(v), 0, 255);
        }

        /// <summary>Metres → u16 log code (round to nearest, saturating).</summary>
        public static ushort EncodeRadiusU16(float metres)
        {
            if (!(metres > RadiusBaseM)) return 0;
            float v = Mathf.Log(metres / RadiusBaseM, 2f) * RadiusLogStepsU16;
            return (ushort)Mathf.Clamp(Mathf.RoundToInt(v), 0, 65535);
        }

        public static uint PackTangentAndRadii(ushort tangentAngle, byte radiusMajor, byte radiusMinor)
            => tangentAngle | ((uint)radiusMajor << 16) | ((uint)radiusMinor << 24);

        public static void UnpackTangentAndRadii(uint packed, out ushort tangentAngle, out byte radiusMajor, out byte radiusMinor)
        {
            tangentAngle = (ushort)(packed & 0xFFFFu);
            radiusMajor = (byte)((packed >> 16) & 0xFFu);
            radiusMinor = (byte)((packed >> 24) & 0xFFu);
        }

        public static float DecodeTangentAngle(ushort v) => v * (2f * Mathf.PI / 65536f);
        public static ushort EncodeTangentAngle(float radians)
        {
            float t = Mathf.Repeat(radians, 2f * Mathf.PI) / (2f * Mathf.PI);
            return (ushort)(Mathf.RoundToInt(t * 65536f) & 0xFFFF);
        }

        /// <summary>Octahedral encode of a unit normal into 16+16 bits (u in low half, v in high half).</summary>
        public static uint EncodeNormalOct32(Vector3 n)
        {
            n.Normalize();
            float l1 = Mathf.Abs(n.x) + Mathf.Abs(n.y) + Mathf.Abs(n.z);
            float px = n.x / l1, py = n.y / l1;
            if (n.z < 0f)
            {
                float ox = (1f - Mathf.Abs(py)) * (px >= 0f ? 1f : -1f);
                float oy = (1f - Mathf.Abs(px)) * (py >= 0f ? 1f : -1f);
                px = ox; py = oy;
            }
            uint u = (uint)Mathf.Clamp(Mathf.RoundToInt((px * 0.5f + 0.5f) * 65535f), 0, 65535);
            uint v = (uint)Mathf.Clamp(Mathf.RoundToInt((py * 0.5f + 0.5f) * 65535f), 0, 65535);
            return u | (v << 16);
        }

        /// <summary>Octahedral decode; mirrors FsDecodeNormal in the shader exactly.</summary>
        public static Vector3 DecodeNormalOct32(uint packed)
        {
            float fx = (packed & 0xFFFFu) / 65535f * 2f - 1f;
            float fy = ((packed >> 16) & 0xFFFFu) / 65535f * 2f - 1f;
            float fz = 1f - Mathf.Abs(fx) - Mathf.Abs(fy);
            if (fz < 0f)
            {
                float ox = (1f - Mathf.Abs(fy)) * (fx >= 0f ? 1f : -1f);
                float oy = (1f - Mathf.Abs(fx)) * (fy >= 0f ? 1f : -1f);
                fx = ox; fy = oy;
            }
            return new Vector3(fx, fy, fz).normalized;
        }

        /// <summary>Tangent frame convention shared with the shader (FsTangentFrame).</summary>
        public static void TangentFrame(Vector3 n, float tangentAngle, out Vector3 tMajor, out Vector3 tMinor)
        {
            Vector3 reference = Mathf.Abs(n.y) > 0.99f ? new Vector3(1f, 0f, 0f) : new Vector3(0f, 1f, 0f);
            Vector3 t0 = Vector3.Cross(n, reference).normalized;
            Vector3 b0 = Vector3.Cross(n, t0);
            tMajor = Mathf.Cos(tangentAngle) * t0 + Mathf.Sin(tangentAngle) * b0;
            tMinor = Vector3.Cross(n, tMajor);
        }

        public static Color32 DecodeColor(uint rgba8) =>
            new Color32((byte)(rgba8 & 0xFFu), (byte)((rgba8 >> 8) & 0xFFu), (byte)((rgba8 >> 16) & 0xFFu), (byte)((rgba8 >> 24) & 0xFFu));

        public static uint EncodeColor(Color32 c) => c.r | ((uint)c.g << 8) | ((uint)c.b << 16) | ((uint)c.a << 24);

        /// <summary>Coverage of an AGGREGATE record: alpha byte / 255.</summary>
        public static float DecodeCoverage(uint colorOrHandle) => ((colorOrHandle >> 24) & 0xFFu) / 255f;
    }
}
