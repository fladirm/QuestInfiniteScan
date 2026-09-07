using System;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;

namespace Genesis.RoomScan
{
    /// <summary>
    /// The complete persistent state of one lattice kernel. Its position is implicit in
    /// the lattice and all topology, normals, vertices, and indices are derived.
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct KernelState
    {
        public const int ByteSize = MerkabaConstants.KernelStateByteSize;

        public int OccupancyEvidence;
        public uint PackedColor;
        public uint ColorConfidence;
        public uint Flags;

        public readonly bool IsOccupied => (Flags & MerkabaConstants.OccupiedFlag) != 0;
        public readonly bool IsR1Seed => !IsOccupied && HasMeasuredSurfacePlane &&
            (Flags & MerkabaConstants.R1SeedFlag) != 0;
        public readonly bool HasMeasuredSurfacePlane => HasSurfacePlane(Flags);
        public readonly Color32 Color => UnpackColor(PackedColor);

        public readonly int SurfaceFreeSide =>
            MerkabaSphereFlowerAuthority.M8FlowerPlaneFreeSide(Flags);

        public static bool HasSurfacePlane(uint flags) =>
            MerkabaSphereFlowerAuthority.M8FlowerHasPlane(flags);

        public static bool TrySetSurfacePlane(uint flags, float3 normal,
            float signedOffset, out uint packed) =>
            MerkabaSphereFlowerAuthority.M8FlowerTryPackPlane(flags, normal,
                signedOffset, out packed);

        public static uint SetSurfacePlane(uint flags, float3 normal,
            float signedOffset)
        {
            if (!TrySetSurfacePlane(flags, normal, signedOffset, out uint packed))
                throw new ArgumentOutOfRangeException(nameof(signedOffset),
                    "Measured plane is invalid or outside this owner's representable support.");
            return packed;
        }

        public static void DecodeSurfacePlane(uint flags, out float3 normal,
            out float signedOffset)
        {
            if (!HasSurfacePlane(flags))
                throw new InvalidOperationException(
                    "KernelState has no measured surface plane.");
            MerkabaSphereFlowerAuthority.M8FlowerUnpackPlane(flags,
                out normal, out signedOffset);
        }

        public static uint ClearSurfacePlane(uint flags) =>
            MerkabaSphereFlowerAuthority.M8FlowerClearPlane(flags);

        internal void SetOccupiedForFixture(bool occupied, Color32 color)
        {
            OccupancyEvidence = occupied
                ? MerkabaConstants.OccupiedOnThreshold
                : 0;
            Flags &= ~MerkabaConstants.R1SeedFlag;
            Flags = occupied ? Flags | MerkabaConstants.OccupiedFlag
                             : Flags & ~MerkabaConstants.OccupiedFlag;
            if (!occupied) Flags = ClearSurfacePlane(Flags);
            if (occupied)
            {
                PackedColor = PackColor(color);
                ColorConfidence = 1;
            }
        }

        public static uint PackColor(Color32 color) =>
            color.r | ((uint)color.g << 8) | ((uint)color.b << 16) | ((uint)color.a << 24);

        public static Color32 UnpackColor(uint packed) => new(
            (byte)(packed & 0xffu),
            (byte)((packed >> 8) & 0xffu),
            (byte)((packed >> 16) & 0xffu),
            (byte)((packed >> 24) & 0xffu));
    }
}
