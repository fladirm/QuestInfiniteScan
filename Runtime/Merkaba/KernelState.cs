using System;
using System.Runtime.InteropServices;
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
        public readonly Color32 Color => UnpackColor(PackedColor);

        internal bool Apply(MerkabaObservationKind kind, float quality, Color32 observedColor)
        {
            bool occupiedBefore = IsOccupied;
            quality = Mathf.Clamp01(quality);

            switch (kind)
            {
                case MerkabaObservationKind.Surface:
                    if (quality < MerkabaConstants.MinimumSurfaceQuality)
                        return false;
                    AddEvidence(Mathf.Max(1,
                        Mathf.RoundToInt(quality * quality * MerkabaConstants.SurfaceEvidenceScale)));
                    AccumulateColor(observedColor, quality);
                    break;

                case MerkabaObservationKind.Free:
                    AddEvidence(-Mathf.Max(1,
                        Mathf.RoundToInt(quality * quality * MerkabaConstants.FreeEvidenceScale)));
                    break;

                default:
                    return false;
            }

            bool occupiedAfter = occupiedBefore
                ? OccupancyEvidence > MerkabaConstants.OccupiedOffThreshold
                : OccupancyEvidence >= MerkabaConstants.OccupiedOnThreshold;

            if (occupiedAfter) Flags |= MerkabaConstants.OccupiedFlag;
            else Flags &= ~MerkabaConstants.OccupiedFlag;
            if (!occupiedAfter && occupiedBefore)
            {
                // Once a claimed surface is disproved, its colour must not bias a
                // later better observation. Signed free evidence remains canonical.
                PackedColor = 0;
                ColorConfidence = 0;
            }
            return occupiedBefore != occupiedAfter;
        }

        internal void SetOccupiedForFixture(bool occupied, Color32 color)
        {
            OccupancyEvidence = occupied
                ? MerkabaConstants.OccupiedOnThreshold
                : 0;
            Flags = occupied ? Flags | MerkabaConstants.OccupiedFlag
                             : Flags & ~MerkabaConstants.OccupiedFlag;
            if (occupied)
            {
                PackedColor = PackColor(color);
                ColorConfidence = 1;
            }
        }

        private void AddEvidence(int delta)
        {
            long updated = (long)OccupancyEvidence + delta;
            OccupancyEvidence = (int)Math.Max(MerkabaConstants.MinimumEvidence,
                Math.Min(MerkabaConstants.MaximumEvidence, updated));
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
