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

    /// <summary>Persistent parent-local binding of one closed-loop strand.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct MerkabaThreadRun
    {
        internal const int ByteSize = 16;

        internal uint FlowerKey;
        internal uint ProgramRef;
        internal uint ResidualBase;
        internal uint ParentEpoch;

        internal readonly bool IsValidFor(uint currentParentEpoch) =>
            currentParentEpoch != 0u && ParentEpoch == currentParentEpoch &&
            MerkabaFlowerDetailKey.TryDecode(FlowerKey, out _);
    }

    /// <summary>
    /// One sparse linear-light RGB interval. The alpha lane remains part of the
    /// closed-loop RGBA ABI; metric V is deliberately absent and lives only in
    /// FlowerDetail.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct MerkabaThreadResidual
    {
        internal const int ByteSize = 32;

        internal uint SegmentKey;
        internal uint ParentEpoch;
        internal half4 LowerLinearRgba;
        internal half4 UpperLinearRgba;
        internal uint Flags;
        internal uint Reserved;

        internal readonly bool IsValidFor(uint currentParentEpoch)
        {
            if (currentParentEpoch == 0u || ParentEpoch != currentParentEpoch ||
                Reserved != 0u) return false;
            float4 lower = LowerLinearRgba;
            float4 upper = UpperLinearRgba;
            return math.all(math.isfinite(lower)) &&
                math.all(math.isfinite(upper)) && math.all(lower <= upper);
        }
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

        internal uint EndpointColorRule;
        internal uint RgbResidualBasis;
        internal uint FibonacciRouteOrigin;
        internal uint Flags;
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
            return math.all(math.isfinite(opticalLower)) &&
                math.all(math.isfinite(opticalUpper)) &&
                math.all(math.isfinite(viewLower)) &&
                math.all(math.isfinite(viewUpper)) &&
                math.all(opticalLower <= opticalUpper) &&
                math.all(viewLower <= viewUpper) &&
                (Flags & ~(uint)MerkabaThreadProgramFlags.OpticalValid) == 0u;
        }
    }
}
