using System;
using Unity.Mathematics;
using UnityEngine;

namespace Genesis.RoomScan
{
    /// <summary>
    /// The six orientation domains used by the authoritative directional TSDF.
    /// Numeric values are a persistence/GPU contract; do not reorder them.
    /// </summary>
    internal enum DirectionalTsdfDirection : byte
    {
        PositiveX = 0,
        NegativeX = 1,
        PositiveY = 2,
        NegativeY = 3,
        PositiveZ = 4,
        NegativeZ = 5
    }

    internal static class DirectionalTsdfMath
    {
        internal const int DirectionCount = 6;
        internal const float DefaultAngleThresholdRadians = 1.1f * Mathf.PI / 4f;

        internal static Vector3 Axis(DirectionalTsdfDirection direction)
        {
            return direction switch
            {
                DirectionalTsdfDirection.PositiveX => Vector3.right,
                DirectionalTsdfDirection.NegativeX => Vector3.left,
                DirectionalTsdfDirection.PositiveY => Vector3.up,
                DirectionalTsdfDirection.NegativeY => Vector3.down,
                DirectionalTsdfDirection.PositiveZ => Vector3.forward,
                DirectionalTsdfDirection.NegativeZ => Vector3.back,
                _ => throw new ArgumentOutOfRangeException(nameof(direction))
            };
        }

        /// <summary>
        /// Exact compact-support weighting used by the pinned directional reference.
        /// It has a unit plateau around the selected axis and fades to zero at the
        /// configured threshold, allowing at most three adjacent domains to receive
        /// one surface observation.
        /// </summary>
        internal static float Weight(Vector3 unitNormal,
            DirectionalTsdfDirection direction,
            float angleThresholdRadians = DefaultAngleThresholdRadians)
        {
            if (!IsFinite(unitNormal) || !IsFinite(angleThresholdRadians) ||
                unitNormal.sqrMagnitude < 0.25f || angleThresholdRadians <= 0f)
                return 0f;

            Vector3 normal = unitNormal.normalized;
            float cosine = Mathf.Clamp(Vector3.Dot(normal, Axis(direction)), -1f, 1f);
            float angle = Mathf.Acos(cosine);
            float width = angleThresholdRadians;
            if (width <= Mathf.PI / 4f + 1e-6f)
                return 1f - Mathf.Min(angle / width, 1f);

            width /= Mathf.PI / 2f;
            angle /= Mathf.PI / 2f;
            float rampStart = 1f - width;
            return 1f - Mathf.Min((Mathf.Max(angle, rampStart) - rampStart) /
                                  (2f * width - 1f), 1f);
        }

        internal static int ContributionMask(Vector3 normal,
            float minimumWeight = 1e-5f,
            float angleThresholdRadians = DefaultAngleThresholdRadians)
        {
            if (!IsFinite(normal) || normal.sqrMagnitude < 0.25f)
                return 0;

            int mask = 0;
            for (int i = 0; i < DirectionCount; i++)
            {
                if (Weight(normal, (DirectionalTsdfDirection)i,
                        angleThresholdRadians) > minimumWeight)
                    mask |= 1 << i;
            }
            return mask;
        }

        internal static DirectionalTsdfDirection DominantDirection(Vector3 normal)
        {
            if (!IsFinite(normal) || normal.sqrMagnitude < 0.25f)
                throw new ArgumentException("A finite non-zero normal is required.", nameof(normal));

            Vector3 n = normal.normalized;
            float ax = Mathf.Abs(n.x);
            float ay = Mathf.Abs(n.y);
            float az = Mathf.Abs(n.z);
            if (ax >= ay && ax >= az)
                return n.x >= 0f ? DirectionalTsdfDirection.PositiveX :
                                   DirectionalTsdfDirection.NegativeX;
            if (ay >= az)
                return n.y >= 0f ? DirectionalTsdfDirection.PositiveY :
                                   DirectionalTsdfDirection.NegativeY;
            return n.z >= 0f ? DirectionalTsdfDirection.PositiveZ :
                               DirectionalTsdfDirection.NegativeZ;
        }

        internal static DirectionalTsdfDirection Opposite(DirectionalTsdfDirection direction)
        {
            return (DirectionalTsdfDirection)((int)direction ^ 1);
        }

        internal static int CountContributions(int mask)
        {
            uint bits = (uint)(mask & 0x3f);
            bits -= (bits >> 1) & 0x55555555u;
            bits = (bits & 0x33333333u) + ((bits >> 2) & 0x33333333u);
            return (int)((bits + (bits >> 4)) & 0x0fu);
        }

        private static bool IsFinite(Vector3 value) =>
            IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }

    internal struct DirectionalTsdfVoxel
    {
        internal float Sdf;
        internal float Weight;
        internal float BestQuality;
        internal Color32 Color;
        internal bool Frozen;

        internal bool IsObserved => Weight > 0f;
    }

    internal readonly struct DirectionalTsdfObservation
    {
        internal readonly Vector3 SurfaceNormalWorld;
        internal readonly float SignedDistanceMeters;
        internal readonly float SurfaceDistanceMeters;
        internal readonly float Incidence;
        internal readonly float NormalConfidence;
        internal readonly bool Visible;
        internal readonly Color32 Color;

        internal DirectionalTsdfObservation(Vector3 surfaceNormalWorld,
            float signedDistanceMeters, float surfaceDistanceMeters,
            float incidence, float normalConfidence, bool visible, Color32 color)
        {
            SurfaceNormalWorld = surfaceNormalWorld;
            SignedDistanceMeters = signedDistanceMeters;
            SurfaceDistanceMeters = surfaceDistanceMeters;
            Incidence = incidence;
            NormalConfidence = normalConfidence;
            Visible = visible;
            Color = color;
        }
    }

    internal readonly struct DirectionalTsdfFusionResult
    {
        internal readonly bool Accepted;
        internal readonly float DirectionWeight;
        internal readonly TsdfFusionDecision Decision;

        internal DirectionalTsdfFusionResult(bool accepted, float directionWeight,
            TsdfFusionDecision decision)
        {
            Accepted = accepted;
            DirectionWeight = directionWeight;
            Decision = decision;
        }
    }

    internal static class DirectionalTsdfFusion
    {
        internal static DirectionalTsdfFusionResult Fuse(
            ref DirectionalTsdfVoxel voxel,
            DirectionalTsdfDirection direction,
            in DirectionalTsdfObservation observation,
            float truncationDistanceMeters,
            float maximumUpdateDistanceMeters,
            in TsdfFusionParameters parameters)
        {
            if (!IsFinite(truncationDistanceMeters) || truncationDistanceMeters <= 0f ||
                !IsFinite(observation.SurfaceNormalWorld) ||
                observation.SurfaceNormalWorld.sqrMagnitude < 0.25f)
                return new DirectionalTsdfFusionResult(false, 0f,
                    TsdfFusionDecision.InvalidInput);

            float directionWeight = DirectionalTsdfMath.Weight(
                observation.SurfaceNormalWorld, direction);
            if (directionWeight <= 0f)
                return new DirectionalTsdfFusionResult(false, 0f,
                    TsdfFusionDecision.OppositeSurface);

            float effectiveIncidence = Mathf.Clamp01(observation.Incidence) *
                                       Mathf.Clamp01(observation.NormalConfidence) *
                                       directionWeight;
            float incomingSdf = Mathf.Clamp(observation.SignedDistanceMeters /
                                            truncationDistanceMeters, -1f, 1f);
            float signedWeight = voxel.Frozen ? -Mathf.Max(voxel.Weight, 0.0001f) :
                                                voxel.Weight;
            var input = new TsdfFusionInput(voxel.Sdf, signedWeight,
                voxel.BestQuality, incomingSdf, observation.SurfaceDistanceMeters,
                maximumUpdateDistanceMeters, effectiveIncidence,
                observation.Visible, false, 1f);
            TsdfFusionResult result = TsdfFusionPolicy.Fuse(input, parameters);
            if (!result.Accepted)
                return new DirectionalTsdfFusionResult(false, directionWeight,
                    result.Decision);

            voxel.Sdf = result.Tsdf;
            voxel.Weight = result.Weight;
            voxel.BestQuality = Mathf.Max(voxel.BestQuality,
                result.ObservationQuality);

            // Live color is deliberately owned by the directional hypothesis. This
            // prevents the opposite side of a wall or panel from recoloring it.
            if (Mathf.Abs(incomingSdf) <= parameters.SurfaceBand &&
                result.ObservationQuality + 1e-5f >= voxel.BestQuality)
                voxel.Color = observation.Color;

            return new DirectionalTsdfFusionResult(true, directionWeight,
                result.Decision);
        }

        private static bool IsFinite(Vector3 value) =>
            IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }

    /// <summary>GPU/persistence representation: SNORM16 SDF + U15 weight/freeze, RGBA8.</summary>
    internal readonly struct DirectionalTsdfPackedVoxel
    {
        internal readonly uint DistanceWeight;
        internal readonly uint ColorQuality;

        internal DirectionalTsdfPackedVoxel(uint distanceWeight, uint colorQuality)
        {
            DistanceWeight = distanceWeight;
            ColorQuality = colorQuality;
        }
    }

    internal static class DirectionalTsdfVoxelCodec
    {
        internal const int StrideBytes = 8;
        private const uint FrozenBit = 0x8000u;
        private const uint WeightMask = 0x7fffu;

        internal static DirectionalTsdfPackedVoxel Pack(in DirectionalTsdfVoxel voxel,
            float maximumWeight)
        {
            if (!(maximumWeight > 0f) || float.IsNaN(maximumWeight) ||
                float.IsInfinity(maximumWeight))
                throw new ArgumentOutOfRangeException(nameof(maximumWeight));

            short sdf = (short)Mathf.RoundToInt(Mathf.Clamp(voxel.Sdf, -1f, 1f) * 32767f);
            uint weight = (uint)Mathf.RoundToInt(Mathf.Clamp01(voxel.Weight /
                                                               maximumWeight) * WeightMask);
            if (voxel.Frozen && weight > 0)
                weight |= FrozenBit;
            uint distanceWeight = (uint)(ushort)sdf | (weight << 16);
            byte quality = (byte)Mathf.RoundToInt(Mathf.Clamp01(voxel.BestQuality) * 255f);
            uint colorQuality = voxel.Color.r |
                                ((uint)voxel.Color.g << 8) |
                                ((uint)voxel.Color.b << 16) |
                                ((uint)quality << 24);
            return new DirectionalTsdfPackedVoxel(distanceWeight, colorQuality);
        }

        internal static DirectionalTsdfVoxel Unpack(in DirectionalTsdfPackedVoxel packed,
            float maximumWeight)
        {
            if (!(maximumWeight > 0f) || float.IsNaN(maximumWeight) ||
                float.IsInfinity(maximumWeight))
                throw new ArgumentOutOfRangeException(nameof(maximumWeight));

            short rawSdf = unchecked((short)(packed.DistanceWeight & 0xffffu));
            uint rawWeight = packed.DistanceWeight >> 16;
            bool frozen = (rawWeight & FrozenBit) != 0;
            float weight = (rawWeight & WeightMask) / (float)WeightMask * maximumWeight;
            return new DirectionalTsdfVoxel
            {
                Sdf = rawSdf / 32767f,
                Weight = weight,
                Frozen = frozen,
                BestQuality = ((packed.ColorQuality >> 24) & 0xffu) / 255f,
                Color = new Color32(
                    (byte)(packed.ColorQuality & 0xffu),
                    (byte)((packed.ColorQuality >> 8) & 0xffu),
                    (byte)((packed.ColorQuality >> 16) & 0xffu), 255)
            };
        }
    }

    internal readonly struct DirectionalTsdfMemoryPlan
    {
        internal readonly int3 VoxelCount;
        internal readonly int BlockEdge;
        internal readonly int3 SpatialBlockCount;
        internal readonly int SpatialBlockTotal;
        internal readonly int DirectionalPageEntries;
        internal readonly int MaximumAllocatedBlocks;
        internal readonly int VoxelsPerBlock;
        internal readonly long VoxelPoolBytes;
        internal readonly long PageTableBytes;
        internal readonly long RequestMaskBytes;
        internal readonly long NewBlockListBytes;

        private DirectionalTsdfMemoryPlan(int3 voxelCount, int blockEdge,
            int3 spatialBlockCount, int spatialBlockTotal, int directionalPageEntries,
            int maximumAllocatedBlocks, int voxelsPerBlock, long voxelPoolBytes,
            long pageTableBytes, long requestMaskBytes, long newBlockListBytes)
        {
            VoxelCount = voxelCount;
            BlockEdge = blockEdge;
            SpatialBlockCount = spatialBlockCount;
            SpatialBlockTotal = spatialBlockTotal;
            DirectionalPageEntries = directionalPageEntries;
            MaximumAllocatedBlocks = maximumAllocatedBlocks;
            VoxelsPerBlock = voxelsPerBlock;
            VoxelPoolBytes = voxelPoolBytes;
            PageTableBytes = pageTableBytes;
            RequestMaskBytes = requestMaskBytes;
            NewBlockListBytes = newBlockListBytes;
        }

        internal long LargestStorageBufferBytes => Math.Max(VoxelPoolBytes,
            Math.Max(PageTableBytes, Math.Max(RequestMaskBytes, NewBlockListBytes)));
        internal long TotalStorageBytes => VoxelPoolBytes + PageTableBytes +
                                           RequestMaskBytes + NewBlockListBytes + 32L;

        internal static bool TryCreate(int3 voxelCount, int blockEdge,
            int maximumAllocatedBlocks, long maximumStorageBufferBytes,
            out DirectionalTsdfMemoryPlan plan)
        {
            plan = default;
            if (blockEdge <= 0 || maximumAllocatedBlocks <= 0 ||
                maximumStorageBufferBytes <= 0 || voxelCount.x <= 0 ||
                voxelCount.y <= 0 || voxelCount.z <= 0 ||
                voxelCount.x % blockEdge != 0 || voxelCount.y % blockEdge != 0 ||
                voxelCount.z % blockEdge != 0)
                return false;

            try
            {
                var blocks = new int3(voxelCount.x / blockEdge,
                    voxelCount.y / blockEdge, voxelCount.z / blockEdge);
                long spatialTotal = checked((long)blocks.x * blocks.y * blocks.z);
                long entries = checked(spatialTotal * DirectionalTsdfMath.DirectionCount);
                long voxelsPerBlock = checked((long)blockEdge * blockEdge * blockEdge);
                long voxelPool = checked((long)maximumAllocatedBlocks * voxelsPerBlock *
                                         DirectionalTsdfVoxelCodec.StrideBytes);
                long pageTable = checked(entries * sizeof(uint));
                long masks = checked(spatialTotal * sizeof(uint));
                long newList = checked((long)maximumAllocatedBlocks * sizeof(uint));
                if (spatialTotal > int.MaxValue || entries > int.MaxValue ||
                    voxelsPerBlock > int.MaxValue || voxelPool > maximumStorageBufferBytes ||
                    pageTable > maximumStorageBufferBytes || masks > maximumStorageBufferBytes ||
                    newList > maximumStorageBufferBytes)
                    return false;

                plan = new DirectionalTsdfMemoryPlan(voxelCount, blockEdge, blocks,
                    (int)spatialTotal, (int)entries, maximumAllocatedBlocks,
                    (int)voxelsPerBlock, voxelPool, pageTable, masks, newList);
                return true;
            }
            catch (OverflowException)
            {
                return false;
            }
        }
    }
}
