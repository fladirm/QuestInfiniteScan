using System;
using System.Runtime.InteropServices;

namespace Genesis.RoomScan
{
    internal enum MerkabaFlowerDetailKind : byte
    {
        R2Phase = 0,
        R3Phase = 1,
        KnotMetric = 2,
        Tombstone = 3
    }

    /// <summary>
    /// Sparse epoch for an M8 owner that actually owns persistent fine state.
    /// Epoch zero is invalid and therefore cannot accidentally validate a
    /// zero-initialized child record.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct MerkabaFlowerOwnerEpoch
    {
        internal const int ByteSize = 8;

        internal uint KernelLocal;
        internal uint Epoch;

        internal MerkabaFlowerOwnerEpoch(uint kernelLocal, uint epoch)
        {
            if (kernelLocal >= MerkabaSpatial.KernelsPerTile)
                throw new ArgumentOutOfRangeException(nameof(kernelLocal));
            if (epoch == 0u)
                throw new ArgumentOutOfRangeException(nameof(epoch));
            KernelLocal = kernelLocal;
            Epoch = epoch;
        }

        internal readonly bool IsCanonical =>
            KernelLocal < MerkabaSpatial.KernelsPerTile && Epoch != 0u;

        internal static MerkabaEpochAdvance Advance(uint current)
        {
            if (current == uint.MaxValue)
                return new MerkabaEpochAdvance(1u, true);
            return new MerkabaEpochAdvance(current + 1u, false);
        }
    }

    internal readonly struct MerkabaEpochAdvance
    {
        internal readonly uint Epoch;
        internal readonly bool RequiresTransactionalRebase;

        internal MerkabaEpochAdvance(uint epoch,
            bool requiresTransactionalRebase)
        {
            Epoch = epoch;
            RequiresTransactionalRebase = requiresTransactionalRebase;
        }
    }

    /// <summary>Canonical packed key of one parent-local fine metric record.</summary>
    internal readonly struct MerkabaFlowerDetailKey :
        IEquatable<MerkabaFlowerDetailKey>
    {
        internal const int GeometryLevelShift = 0;
        internal const int GeometryChildPathShift = 2;
        internal const int PetalClassShift = 6;
        internal const int ChannelShift = 12;
        internal const int KindShift = 16;
        internal const int RootSignShift = 19;
        internal const int SectorShift = 20;
        internal const int ReservedShift = 25;

        internal const uint GeometryLevelMask = 0x3u;
        internal const uint GeometryChildPathMask = 0xfu;
        internal const uint PetalClassMask = 0x3fu;
        internal const uint ChannelMask = 0xfu;
        internal const uint KindMask = 0x7u;
        internal const uint SectorMask = 0x1fu;

        internal readonly uint Value;

        private MerkabaFlowerDetailKey(uint value) => Value = value;

        internal static MerkabaFlowerDetailKey Create(int geometryLevel,
            int geometryChildPath, int petalClass, int channel,
            MerkabaFlowerDetailKind kind, bool rootSign, int sector)
        {
            if ((uint)geometryLevel >=
                MerkabaSphereFlowerAuthority.GeometryLevelCount)
                throw new ArgumentOutOfRangeException(nameof(geometryLevel));
            int childPathLimit = 1 << (geometryLevel * 2);
            if (geometryChildPath < 0 ||
                geometryChildPath >= childPathLimit)
                throw new ArgumentOutOfRangeException(
                    nameof(geometryChildPath));
            if ((uint)petalClass >=
                MerkabaSphereFlowerAuthority.PetalClassCount)
                throw new ArgumentOutOfRangeException(nameof(petalClass));
            if ((uint)channel >=
                MerkabaSphereFlowerAuthority.LineClassCount)
                throw new ArgumentOutOfRangeException(nameof(channel));
            if ((uint)kind > (uint)MerkabaFlowerDetailKind.Tombstone)
                throw new ArgumentOutOfRangeException(nameof(kind));
            int sectorCount = MerkabaSphereFlowerAuthority.Lines[channel]
                .SectorCount;
            if (sector < 0 || sector >= sectorCount)
                throw new ArgumentOutOfRangeException(nameof(sector));

            uint value = (uint)geometryLevel |
                ((uint)geometryChildPath << GeometryChildPathShift) |
                ((uint)petalClass << PetalClassShift) |
                ((uint)channel << ChannelShift) |
                ((uint)kind << KindShift) |
                (rootSign ? 1u << RootSignShift : 0u) |
                ((uint)sector << SectorShift);
            return new MerkabaFlowerDetailKey(value);
        }

        internal static bool TryDecode(uint value,
            out MerkabaFlowerDetailKey key)
        {
            if ((value >> ReservedShift) != 0u)
            {
                key = default;
                return false;
            }
            int level = (int)(value & GeometryLevelMask);
            int childPath = (int)((value >> GeometryChildPathShift) &
                GeometryChildPathMask);
            int petal = (int)((value >> PetalClassShift) & PetalClassMask);
            int channel = (int)((value >> ChannelShift) & ChannelMask);
            var kind = (MerkabaFlowerDetailKind)((value >> KindShift) &
                KindMask);
            int sector = (int)((value >> SectorShift) & SectorMask);
            try
            {
                key = Create(level, childPath, petal, channel, kind,
                    ((value >> RootSignShift) & 1u) != 0u, sector);
                return key.Value == value;
            }
            catch (ArgumentOutOfRangeException)
            {
                key = default;
                return false;
            }
        }

        internal int GeometryLevel => (int)(Value & GeometryLevelMask);
        internal int GeometryChildPath =>
            (int)((Value >> GeometryChildPathShift) &
                GeometryChildPathMask);
        internal int PetalClass =>
            (int)((Value >> PetalClassShift) & PetalClassMask);
        internal int Channel =>
            (int)((Value >> ChannelShift) & ChannelMask);
        internal MerkabaFlowerDetailKind Kind =>
            (MerkabaFlowerDetailKind)((Value >> KindShift) & KindMask);
        internal bool RootSign => ((Value >> RootSignShift) & 1u) != 0u;
        internal int Sector => (int)((Value >> SectorShift) & SectorMask);

        public bool Equals(MerkabaFlowerDetailKey other) => Value == other.Value;
        public override bool Equals(object obj) =>
            obj is MerkabaFlowerDetailKey other && Equals(other);
        public override int GetHashCode() => unchecked((int)Value);
    }

    /// <summary>Persistent sixteen-byte interval innovation.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct MerkabaFlowerDetailRecord
    {
        internal const int ByteSize = 16;
        internal const int PhaseFractionBits = 29;

        internal uint Key;
        internal int Lower;
        internal int Upper;
        internal uint ParentEpoch;

        internal static MerkabaFlowerDetailRecord Create(
            MerkabaFlowerDetailKey key, int lower, int upper,
            uint parentEpoch)
        {
            if (lower > upper)
                throw new ArgumentOutOfRangeException(nameof(lower));
            if (parentEpoch == 0u)
                throw new ArgumentOutOfRangeException(nameof(parentEpoch));
            return new MerkabaFlowerDetailRecord
            {
                Key = key.Value,
                Lower = lower,
                Upper = upper,
                ParentEpoch = parentEpoch
            };
        }

        internal readonly bool IsValidFor(uint currentParentEpoch) =>
            currentParentEpoch != 0u && ParentEpoch == currentParentEpoch &&
            Lower <= Upper && MerkabaFlowerDetailKey.TryDecode(Key, out _);

        internal readonly bool TryReadR2Phase(MerkabaFlowerDetailKey expectedKey,
            uint currentParentEpoch, out int lower, out int upper)
        {
            lower = upper = 0;
            if (Key != expectedKey.Value || !IsValidFor(currentParentEpoch) ||
                expectedKey.Kind != MerkabaFlowerDetailKind.R2Phase ||
                (Lower <= 0 && Upper >= 0))
                return false;
            // Storage validates identity and epoch, not geometry routing.
            // The generated channel transport consumes the unchanged Q2.29
            // endpoints and applies its orientation after outward decoding.
            lower = Lower;
            upper = Upper;
            return true;
        }

        internal readonly int DrawMidpoint => checked((int)(Lower +
            ((long)Upper - Lower) / 2L));

        internal static int EncodePhaseLower(double value) =>
            EncodeLower(value, PhaseFractionBits);
        internal static int EncodePhaseUpper(double value) =>
            EncodeUpper(value, PhaseFractionBits);
        internal static double DecodePhase(int value) =>
            value / (double)(1L << PhaseFractionBits);

        internal static int EncodeLower(double value, int fractionBits) =>
            EncodeOutward(value, fractionBits, false);

        internal static int EncodeUpper(double value, int fractionBits) =>
            EncodeOutward(value, fractionBits, true);

        private static int EncodeOutward(double value, int fractionBits,
            bool upper)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            double scaled = value * (1L << fractionBits);
            double rounded = upper ? Math.Ceiling(scaled) : Math.Floor(scaled);
            if (rounded < int.MinValue || rounded > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(value));
            return (int)rounded;
        }
    }

    /// <summary>
    /// Opaque owner-local identity of one terminal geometric L2 carrier. The
    /// containing M8 owner supplies world position; this key contains only the
    /// canonical source representative of the generated L2 carrier. Its six
    /// source wedges share one skin address, not six independent thread runs.
    /// </summary>
    internal readonly struct MerkabaFlowerL2Key :
        IEquatable<MerkabaFlowerL2Key>
    {
        internal const int GeometryChildPathShift = 0;
        internal const int PetalClassShift = 4;
        internal const int RootSignShift = 10;
        internal const int SectorShift = 11;
        internal const int ReservedShift = 16;
        internal const uint GeometryChildPathMask = 0xfu;
        internal const uint PetalClassMask = 0x3fu;
        internal const uint SectorMask = 0x1fu;

        internal readonly uint Value;

        private MerkabaFlowerL2Key(uint value) => Value = value;

        internal static MerkabaFlowerL2Key Create(int geometryChildPath,
            int petalClass, bool rootSign, int sector)
        {
            if ((uint)geometryChildPath > GeometryChildPathMask)
                throw new ArgumentOutOfRangeException(
                    nameof(geometryChildPath));
            if ((uint)petalClass >=
                MerkabaSphereFlowerAuthority.PetalClassCount)
                throw new ArgumentOutOfRangeException(nameof(petalClass));
            if ((uint)sector > SectorMask)
                throw new ArgumentOutOfRangeException(nameof(sector));
            int carrier = MerkabaSphereFlowerAuthority.L2WedgeIndex(
                petalClass, geometryChildPath) /
                MerkabaSphereFlowerAuthority.L2CarrierWedgeCount;
            uint source = MerkabaSphereFlowerAuthority.L2Wedges[
                MerkabaSphereFlowerAuthority.L2CarrierWedgeCount * carrier].Source;
            return new MerkabaFlowerL2Key(source |
                (rootSign ? 1u << RootSignShift : 0u) |
                ((uint)sector << SectorShift));
        }

        internal static bool TryDecode(uint value, out MerkabaFlowerL2Key key)
        {
            if ((value >> ReservedShift) != 0u)
            {
                key = default;
                return false;
            }
            try
            {
                key = Create(
                    (int)((value >> GeometryChildPathShift) &
                        GeometryChildPathMask),
                    (int)((value >> PetalClassShift) & PetalClassMask),
                    ((value >> RootSignShift) & 1u) != 0u,
                    (int)((value >> SectorShift) & SectorMask));
                return key.Value == value;
            }
            catch (ArgumentOutOfRangeException)
            {
                key = default;
                return false;
            }
        }

        internal int GeometryChildPath => (int)(Value &
            GeometryChildPathMask);
        internal int PetalClass => (int)((Value >> PetalClassShift) &
            PetalClassMask);
        internal bool RootSign => ((Value >> RootSignShift) & 1u) != 0u;
        internal int Sector => (int)((Value >> SectorShift) & SectorMask);
        public bool Equals(MerkabaFlowerL2Key other) => Value == other.Value;
        public override bool Equals(object obj) =>
            obj is MerkabaFlowerL2Key other && Equals(other);
        public override int GetHashCode() => unchecked((int)Value);
    }

    /// <summary>Canonical operations over the exact 57 split bits.</summary>
    internal static class MerkabaFlowerSkinSplitBits
    {
        internal const uint HighValidMask = 0x01ffffffu;

        internal static bool SplitL2(uint low) => (low & 1u) != 0u;
        internal static uint SplitL3Thread(uint low) => (low >> 1) & 0x7fu;
        internal static uint SplitL4Low(uint low, uint high) =>
            (low >> 8) | ((high & 0xffu) << 24);
        internal static uint SplitL4High(uint high) =>
            (high >> 8) & 0x1ffffu;

        internal static bool IsCanonical(uint low, uint high) =>
            (high & ~HighValidMask) == 0u &&
            MerkabaSphereFlowerAuthority.ValidateSkinSplitClosure(
                SplitL2(low), SplitL3Thread(low), SplitL4Low(low, high),
                SplitL4High(high));

        internal static int GroupCount(uint low, uint high)
        {
            if (!IsCanonical(low, high)) return -1;
            return Unity.Mathematics.math.countbits(low) +
                Unity.Mathematics.math.countbits(high & HighValidMask);
        }

        internal static bool GroupRangeFits(uint groupBase, uint low,
            uint high)
        {
            int count = GroupCount(low, high);
            return count >= 0 && (count == 0 || groupBase != uint.MaxValue &&
                (ulong)groupBase + (uint)count <= (ulong)uint.MaxValue + 1ul);
        }

        internal static bool TryCompactChildAddress(uint groupBase, uint low,
            uint high, int level, int c3, int c4, int c5,
            out uint groupIndex, out int childStitchRank)
        {
            if ((uint)(level - 3) > 2u)
                throw new ArgumentOutOfRangeException(nameof(level));
            if (!IsCanonical(low, high) ||
                !GroupRangeFits(groupBase, low, high))
                throw new ArgumentException("Noncanonical skin split run.");
            int j3 = MerkabaSphereFlowerAuthority.SkinL3ParentThreadIndex(c3);
            if (!SplitL2(low))
                return Missing(out groupIndex, out childStitchRank);
            if (level == 3)
            {
                groupIndex = groupBase;
                childStitchRank = j3;
                return true;
            }

            uint splitL3 = SplitL3Thread(low);
            if ((splitL3 & (1u << j3)) == 0u)
                return Missing(out groupIndex, out childStitchRank);
            int r4 = MerkabaSphereFlowerAuthority.SkinL4CompactChildRank(c3,
                c4);
            if (level == 4)
            {
                groupIndex = checked(groupBase + 1u +
                    (uint)MerkabaSphereFlowerAuthority.Rank7(splitL3, j3));
                childStitchRank = r4;
                return true;
            }

            int j4 = MerkabaSphereFlowerAuthority.SkinL4ParentThreadIndex(c3,
                c4);
            uint l4Low = SplitL4Low(low, high);
            uint l4High = SplitL4High(high);
            uint l4Word = j4 < 32 ? l4Low : l4High;
            int l4Shift = j4 < 32 ? j4 : j4 - 32;
            if ((l4Word & (1u << l4Shift)) == 0u)
                return Missing(out groupIndex, out childStitchRank);
            if (level != 5) throw new ArgumentOutOfRangeException(nameof(level));
            groupIndex = checked(groupBase + 1u +
                (uint)Unity.Mathematics.math.countbits(splitL3) +
                (uint)MerkabaSphereFlowerAuthority.Rank49(l4Low, l4High, j4));
            childStitchRank = MerkabaSphereFlowerAuthority
                .SkinL5CompactChildRank(c3, c4, c5);
            return true;
        }

        private static bool Missing(out uint groupIndex,
            out int childStitchRank)
        {
            groupIndex = uint.MaxValue;
            childStitchRank = -1;
            return false;
        }
    }

    /// <summary>
    /// One sparse L2 metric-skin run. Its 57 bits are in immutable
    /// thread-parent order and each bit owns one seven-interval group.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct MerkabaFlowerSkinMetricRun
    {
        internal const int ByteSize = 24;
        internal const uint InvalidGroupBase = uint.MaxValue;

        internal uint FlowerKey;
        internal uint GroupBase;
        internal uint SplitBitsLo;
        internal uint SplitBitsHi;
        internal uint ParentEpoch;
        internal uint Reserved;

        internal readonly bool IsValidFor(uint currentParentEpoch) =>
            currentParentEpoch != 0u && ParentEpoch == currentParentEpoch &&
            Reserved == 0u && GroupBase != InvalidGroupBase &&
            MerkabaFlowerL2Key.TryDecode(FlowerKey, out _) &&
            MerkabaFlowerSkinSplitBits.SplitL2(SplitBitsLo) &&
            MerkabaFlowerSkinSplitBits.IsCanonical(SplitBitsLo, SplitBitsHi) &&
            MerkabaFlowerSkinSplitBits.GroupRangeFits(GroupBase, SplitBitsLo,
                SplitBitsHi);

        internal readonly int GroupCount =>
            MerkabaFlowerSkinSplitBits.GroupCount(SplitBitsLo, SplitBitsHi);
    }

    /// <summary>One signed Q5.26 additive metric-V interval.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct MerkabaFlowerVInterval
    {
        internal const int ByteSize = 8;
        internal const int MetricFractionBits = 26;

        internal int Lower;
        internal int Upper;

        internal readonly bool IsCanonical => Lower <= Upper;
        internal readonly int DrawMidpoint => checked((int)(Lower +
            ((long)Upper - Lower) / 2L));

        internal static int EncodeLower(double metres) =>
            MerkabaFlowerDetailRecord.EncodeLower(metres,
                MetricFractionBits);
        internal static int EncodeUpper(double metres) =>
            MerkabaFlowerDetailRecord.EncodeUpper(metres,
                MetricFractionBits);
        internal static double Decode(int value) =>
            value / (double)(1L << MetricFractionBits);
    }

    /// <summary>Atomic seven-child additive metric-V group.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct MerkabaFlowerVGroup
    {
        internal const int ByteSize = 7 * MerkabaFlowerVInterval.ByteSize;

        internal MerkabaFlowerVInterval Child0;
        internal MerkabaFlowerVInterval Child1;
        internal MerkabaFlowerVInterval Child2;
        internal MerkabaFlowerVInterval Child3;
        internal MerkabaFlowerVInterval Child4;
        internal MerkabaFlowerVInterval Child5;
        internal MerkabaFlowerVInterval Child6;

        internal readonly MerkabaFlowerVInterval Child(int stitchRank) =>
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
    /// Transient structural identity used to decide whether the sparse owner
    /// epoch changes. It is derived before replacing an R1 plane and is never a
    /// second persistent surface field.
    /// </summary>
    internal readonly struct MerkabaFlowerAnchorIdentity :
        IEquatable<MerkabaFlowerAnchorIdentity>
    {
        internal readonly uint Value;

        internal MerkabaFlowerAnchorIdentity(int r1LineClass, int sector,
            bool rootSign, int freeSide, int petalClass)
        {
            if ((uint)r1LineClass >= 3u)
                throw new ArgumentOutOfRangeException(nameof(r1LineClass));
            if (sector < 0 || sector >=
                MerkabaSphereFlowerAuthority.Lines[r1LineClass].SectorCount)
                throw new ArgumentOutOfRangeException(nameof(sector));
            if ((uint)freeSide >= 6u)
                throw new ArgumentOutOfRangeException(nameof(freeSide));
            if ((uint)petalClass >=
                MerkabaSphereFlowerAuthority.PetalClassCount)
                throw new ArgumentOutOfRangeException(nameof(petalClass));
            Value = (uint)r1LineClass |
                ((uint)sector << 2) |
                (rootSign ? 1u << 7 : 0u) |
                ((uint)freeSide << 8) |
                ((uint)petalClass << 11);
        }

        public bool Equals(MerkabaFlowerAnchorIdentity other) =>
            Value == other.Value;
        public override bool Equals(object obj) =>
            obj is MerkabaFlowerAnchorIdentity other && Equals(other);
        public override int GetHashCode() => unchecked((int)Value);
    }
}
