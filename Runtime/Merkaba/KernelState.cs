using System;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;

namespace Genesis.RoomScan
{
    public enum MerkabaObservationKind : byte
    {
        Unknown = 0,
        Free = 1,
        Surface = 2
    }

    /// <summary>
    /// The complete persistent state of one lattice kernel. Its position is implicit in
    /// the lattice and all topology, normals, vertices, and indices are derived.
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct KernelState
    {
        public int OccupancyEvidence;
        public uint PackedColor;
        public uint ColorConfidence;
        public uint Flags;

        public readonly bool IsOccupied => (Flags & MerkabaConstants.OccupiedFlag) != 0;
        public readonly bool NeedsCarve =>
            (Flags & MerkabaConstants.NeedsCarveFlag) != 0;
        public readonly bool HasMeasuredSurfacePlane => HasSurfacePlane(Flags);
        public readonly Color32 Color => UnpackColor(PackedColor);

        public static bool HasSurfacePlane(uint flags) =>
            (flags & MerkabaConstants.SurfacePlaneValidFlag) != 0u;

        public static uint SetSurfacePlane(uint flags, float3 normal,
            float signedOffset)
        {
            float lengthSquared = math.lengthsq(normal);
            if (!(lengthSquared > 0f) || !math.isfinite(lengthSquared))
                throw new ArgumentOutOfRangeException(nameof(normal));
            normal *= math.rsqrt(lengthSquared);
            if (FirstNonZero(normal) < 0f)
            {
                normal = -normal;
                signedOffset = -signedOffset;
            }

            float2 oct = OctEncode(normal);
            uint encodedU = (uint)math.clamp(math.round(
                (oct.x * 0.5f + 0.5f) * 1023f), 0f, 1023f);
            uint encodedV = (uint)math.clamp(math.round(
                (oct.y * 0.5f + 0.5f) * 1023f), 0f, 1023f);
            int encodedOffset = (int)math.round(math.clamp(signedOffset /
                MerkabaConstants.SurfacePlaneOffsetRange, -1f, 1f) * 127f);
            uint payload =
                (encodedU << MerkabaConstants.SurfacePlaneNormalUShift) |
                (encodedV << MerkabaConstants.SurfacePlaneNormalVShift) |
                ((uint)(encodedOffset & 0xff) <<
                    MerkabaConstants.SurfacePlaneOffsetShift) |
                MerkabaConstants.SurfacePlaneValidFlag;
            return (flags & ~MerkabaConstants.SurfacePlaneStorageMask) |
                payload;
        }

        public static void DecodeSurfacePlane(uint flags, out float3 normal,
            out float signedOffset)
        {
            if (!HasSurfacePlane(flags))
                throw new InvalidOperationException(
                    "KernelState has no measured surface plane.");
            uint encodedU = (flags >>
                MerkabaConstants.SurfacePlaneNormalUShift) &
                MerkabaConstants.SurfacePlaneNormalMask;
            uint encodedV = (flags >>
                MerkabaConstants.SurfacePlaneNormalVShift) &
                MerkabaConstants.SurfacePlaneNormalMask;
            float2 oct = new(encodedU / 1023f * 2f - 1f,
                encodedV / 1023f * 2f - 1f);
            normal = OctDecode(oct);
            int encodedOffset = (int)((flags >>
                MerkabaConstants.SurfacePlaneOffsetShift) &
                MerkabaConstants.SurfacePlaneOffsetMask);
            if (encodedOffset >= 128) encodedOffset -= 256;
            signedOffset = encodedOffset / 127f *
                MerkabaConstants.SurfacePlaneOffsetRange;
        }

        public static uint ClearSurfacePlane(uint flags) =>
            flags & ~MerkabaConstants.SurfacePlaneStorageMask;

        private static float2 OctEncode(float3 normal)
        {
            normal /= math.csum(math.abs(normal));
            float2 oct = normal.xy;
            if (normal.z < 0f)
                oct = (1f - math.abs(oct.yx)) * SignNotZero(oct);
            return oct;
        }

        private static float3 OctDecode(float2 oct)
        {
            float3 normal = new(oct.x, oct.y,
                1f - math.abs(oct.x) - math.abs(oct.y));
            if (normal.z < 0f)
                normal.xy = (1f - math.abs(normal.yx)) *
                    SignNotZero(normal.xy);
            return math.normalize(normal);
        }

        private static float2 SignNotZero(float2 value) => new(
            value.x >= 0f ? 1f : -1f,
            value.y >= 0f ? 1f : -1f);

        private static float FirstNonZero(float3 value) =>
            value.x != 0f ? value.x : value.y != 0f ? value.y : value.z;

        internal bool Apply(MerkabaObservationKind kind, float quality, Color32 observedColor)
        {
            quality = Mathf.Clamp01(quality);
            return ApplyWeighted(kind, quality, quality * quality,
                observedColor, true);
        }

        internal bool ApplyWeighted(MerkabaObservationKind kind, float quality,
            float evidenceWeight, Color32 observedColor,
            bool allowOccupiedClear)
        {
            bool occupiedBefore = IsOccupied;
            quality = Mathf.Clamp01(quality);
            evidenceWeight = Mathf.Clamp01(evidenceWeight);

            switch (kind)
            {
                case MerkabaObservationKind.Surface:
                    if (quality < MerkabaConstants.MinimumSurfaceQuality)
                        return false;
                    AddEvidence(Mathf.Max(1,
                        Mathf.RoundToInt(evidenceWeight *
                            MerkabaConstants.SurfaceEvidenceScale)));
                    AccumulateColor(observedColor, quality);
                    Flags |= MerkabaConstants.NeedsCarveFlag;
                    break;

                case MerkabaObservationKind.Free:
                    int decrement = Mathf.Max(1,
                        Mathf.RoundToInt(evidenceWeight *
                            MerkabaConstants.FreeEvidenceScale));
                    int minimumEvidence = occupiedBefore && !allowOccupiedClear
                        ? MerkabaConstants.OccupiedOffThreshold + 1
                        : -MerkabaConstants.EvidenceConfidenceLimit;
                    AddEvidence(-decrement, minimumEvidence);
                    break;

                default:
                    return false;
            }

            bool occupiedAfter = occupiedBefore
                ? OccupancyEvidence > MerkabaConstants.OccupiedOffThreshold
                : OccupancyEvidence >= MerkabaConstants.OccupiedOnThreshold;

            if (occupiedAfter) Flags |= MerkabaConstants.OccupiedFlag;
            else
            {
                Flags &= ~MerkabaConstants.OccupiedFlag;
                Flags = ClearSurfacePlane(Flags);
            }
            if (kind == MerkabaObservationKind.Free && !occupiedAfter &&
                OccupancyEvidence <= MerkabaConstants.ExportKnownFreeThreshold)
                Flags &= ~MerkabaConstants.NeedsCarveFlag;
            return occupiedBefore != occupiedAfter;
        }

        internal void SetOccupiedForFixture(bool occupied, Color32 color)
        {
            OccupancyEvidence = occupied
                ? MerkabaConstants.OccupiedOnThreshold
                : 0;
            Flags = occupied ? Flags | MerkabaConstants.OccupiedFlag
                             : Flags & ~MerkabaConstants.OccupiedFlag;
            if (!occupied) Flags = ClearSurfacePlane(Flags);
            if (occupied)
            {
                PackedColor = PackColor(color);
                ColorConfidence = 1;
            }
        }

        private void AddEvidence(int delta, int minimum =
            -MerkabaConstants.EvidenceConfidenceLimit)
        {
            long updated = (long)OccupancyEvidence + delta;
            OccupancyEvidence = (int)Math.Max(
                minimum,
                Math.Min(MerkabaConstants.EvidenceConfidenceLimit, updated));
        }

        private void AccumulateColor(Color32 observed, float quality)
        {
            uint incomingWeight = (uint)Mathf.Clamp(
                Mathf.RoundToInt(quality * 256f), 1, 256);
            uint oldWeight = Math.Min(ColorConfidence,
                (uint)MerkabaConstants.MaximumColorConfidence);
            uint total = Math.Min((uint)MerkabaConstants.MaximumColorConfidence,
                oldWeight + incomingWeight);
            if (oldWeight == 0)
            {
                PackedColor = PackColor(observed);
                ColorConfidence = incomingWeight;
                return;
            }

            Color32 old = UnpackColor(PackedColor);
            uint divisor = oldWeight + incomingWeight;
            byte r = Weighted(old.r, oldWeight, observed.r, incomingWeight, divisor);
            byte g = Weighted(old.g, oldWeight, observed.g, incomingWeight, divisor);
            byte b = Weighted(old.b, oldWeight, observed.b, incomingWeight, divisor);
            PackedColor = PackColor(new Color32(r, g, b, 255));
            ColorConfidence = total;
        }

        private static byte Weighted(byte oldValue, uint oldWeight, byte value,
            uint weight, uint divisor) => (byte)Math.Min(255u,
                ((uint)oldValue * oldWeight + (uint)value * weight + divisor / 2u) / divisor);

        public static uint PackColor(Color32 color) =>
            color.r | ((uint)color.g << 8) | ((uint)color.b << 16) | ((uint)color.a << 24);

        public static Color32 UnpackColor(uint packed) => new(
            (byte)(packed & 0xffu),
            (byte)((packed >> 8) & 0xffu),
            (byte)((packed >> 16) & 0xffu),
            (byte)((packed >> 24) & 0xffu));
    }
}
