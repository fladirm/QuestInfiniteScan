using System;
using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace Genesis.RoomScan
{
    [Flags]
    internal enum MerkabaThreadProgramFlags : uint
    {
        None = 0,
        OpticalValid = 1u << 0
    }

    /// <summary>
    /// Persistent RGB subdivision of one fixed L2 skin thread. Split bits use
    /// the exact same thread-parent order as metric V but remain an independent
    /// captured-radiance authority.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct MerkabaThreadRun
    {
        internal const int ByteSize = 24;
        internal const uint InvalidRef = uint.MaxValue;

        internal uint FlowerKey;
        internal uint ProgramRef;
        internal uint GroupBase;
        internal uint SplitBitsLo;
        internal uint SplitBitsHi;
        internal uint ParentEpoch;

        internal readonly bool IsValidFor(uint currentParentEpoch)
        {
            if (currentParentEpoch == 0u || ParentEpoch != currentParentEpoch ||
                !MerkabaFlowerL2Key.TryDecode(FlowerKey, out _) ||
                !MerkabaFlowerSkinSplitBits.IsCanonical(SplitBitsLo,
                    SplitBitsHi)) return false;
            bool split = MerkabaFlowerSkinSplitBits.SplitL2(SplitBitsLo);
            if (split != (GroupBase != InvalidRef)) return false;
            return (!split || MerkabaFlowerSkinSplitBits.GroupRangeFits(
                    GroupBase, SplitBitsLo, SplitBitsHi)) &&
                (split || ProgramRef != InvalidRef);
        }

        internal readonly int GroupCount =>
            MerkabaFlowerSkinSplitBits.GroupCount(SplitBitsLo, SplitBitsHi);
    }

    /// <summary>
    /// One actual captured linear-light RGBA interval. Metric V is deliberately
    /// absent and lives only in FlowerDetail.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct MerkabaThreadColorInterval
    {
        internal const int ByteSize = 16;

        internal half4 LowerLinearRgba;
        internal half4 UpperLinearRgba;

        // Persist an enclosure, never nearest-half endpoints that can shrink
        // the measured interval. Overflow is unrepresentable, not a clamp.
        internal static bool TryEncode(float4 lower, float4 upper,
            out MerkabaThreadColorInterval value)
        {
            value = default;
            if (!math.all(math.isfinite(lower)) ||
                !math.all(math.isfinite(upper)) || !math.all(lower <= upper) ||
                math.any(lower < -65504f) || math.any(upper > 65504f))
                return false;
            for (int channel = 0; channel < 4; channel++)
            {
                value.LowerLinearRgba[channel] = EncodeEndpoint(lower[channel], false);
                value.UpperLinearRgba[channel] = EncodeEndpoint(upper[channel], true);
            }
            return true;
        }

        private static half EncodeEndpoint(float value, bool upper)
        {
            half rounded = (half)value;
            float decoded = rounded;
            uint bits = rounded.value;
            if (upper ? decoded < value : decoded > value)
            {
                if ((bits & 0x7fffu) == 0u)
                    bits = upper ? 1u : 0x8001u;
                else if (((bits & 0x8000u) == 0u) == upper)
                    bits++;
                else
                    bits--;
            }
            // Sign of zero is not captured-radiance identity.
            if ((bits & 0x7fffu) == 0u) bits = 0u;
            return new half { value = (ushort)bits };
        }

        internal readonly bool IsCanonical
        {
            get
            {
                float4 lower = LowerLinearRgba;
                float4 upper = UpperLinearRgba;
                return math.all(math.isfinite(lower)) &&
                    math.all(math.isfinite(upper)) && math.all(lower <= upper);
            }
        }
    }

    /// <summary>Atomic seven-child actual captured-color group.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct MerkabaThreadColorGroup
    {
        internal const int ByteSize = 7 * MerkabaThreadColorInterval.ByteSize;

        internal MerkabaThreadColorInterval Child0;
        internal MerkabaThreadColorInterval Child1;
        internal MerkabaThreadColorInterval Child2;
        internal MerkabaThreadColorInterval Child3;
        internal MerkabaThreadColorInterval Child4;
        internal MerkabaThreadColorInterval Child5;
        internal MerkabaThreadColorInterval Child6;

        internal readonly MerkabaThreadColorInterval Child(int stitchRank) =>
            stitchRank switch
            {
                0 => Child0, 1 => Child1, 2 => Child2, 3 => Child3,
                4 => Child4, 5 => Child5, 6 => Child6,
                _ => throw new ArgumentOutOfRangeException(nameof(stitchRank))
            };

        internal readonly bool IsCanonical => Child0.IsCanonical &&
            Child1.IsCanonical && Child2.IsCanonical && Child3.IsCanonical &&
            Child4.IsCanonical && Child5.IsCanonical && Child6.IsCanonical;
    }

    /// <summary>
    /// Reusable closed-loop program. The first optical interval stores
    /// (F0.r,F0.g,F0.b,roughnessFloor); the second stores certified
    /// capture/view residual RGBA. Optical fields are ignored unless the one
    /// explicit validity bit is set.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct MerkabaThreadProgramRecord
    {
        internal const int ByteSize = 48;

        internal uint Flags;
        internal uint Reserved0;
        internal uint Reserved1;
        internal uint Reserved2;
        internal half4 OpticalLower;
        internal half4 OpticalUpper;
        internal half4 CaptureViewLower;
        internal half4 CaptureViewUpper;

        internal readonly bool OpticalValid =>
            (Flags & (uint)MerkabaThreadProgramFlags.OpticalValid) != 0u;

        internal readonly bool HasCanonicalIntervals()
        {
            float4 opticalLower = OpticalLower;
            float4 opticalUpper = OpticalUpper;
            float4 viewLower = CaptureViewLower;
            float4 viewUpper = CaptureViewUpper;
            bool finiteOrdered = math.all(math.isfinite(opticalLower)) &&
                math.all(math.isfinite(opticalUpper)) &&
                math.all(math.isfinite(viewLower)) &&
                math.all(math.isfinite(viewUpper)) &&
                math.all(opticalLower <= opticalUpper) &&
                math.all(viewLower <= viewUpper) &&
                (Flags & ~(uint)MerkabaThreadProgramFlags.OpticalValid) == 0u &&
                Reserved0 == 0u && Reserved1 == 0u && Reserved2 == 0u;
            if (!finiteOrdered) return false;
            if (OpticalValid) return true;
            return math.all(opticalLower == 0f) &&
                math.all(opticalUpper == 0f) &&
                math.all(viewLower == 0f) && math.all(viewUpper == 0f);
        }
    }
}
