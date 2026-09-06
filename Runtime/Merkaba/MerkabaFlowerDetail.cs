using System;
using System.Runtime.InteropServices;

namespace Genesis.RoomScan
{
    internal enum MerkabaFlowerDetailKind : byte
    {
        R2Phase = 0,
        R3Phase = 1,
        VAmplitude = 2,
        KnotMetric = 3,
        Tombstone = 4
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
        internal const int LevelShift = 0;
        internal const int ChildPathShift = 3;
        internal const int PetalClassShift = 13;
        internal const int ChannelShift = 19;
        internal const int KindShift = 23;
        internal const int RootSignShift = 26;
        internal const int SectorShift = 27;

        internal const uint LevelMask = 0x7u;
        internal const uint ChildPathMask = 0x3ffu;
        internal const uint PetalClassMask = 0x3fu;
        internal const uint ChannelMask = 0xfu;
        internal const uint KindMask = 0x7u;
        internal const uint SectorMask = 0x1fu;

        internal readonly uint Value;

        private MerkabaFlowerDetailKey(uint value) => Value = value;

        internal static MerkabaFlowerDetailKey Create(int level,
            int childPath, int petalClass, int channel,
            MerkabaFlowerDetailKind kind, bool rootSign, int sector)
        {
            if ((uint)level >= MerkabaSphereFlowerAuthority.LevelCount)
                throw new ArgumentOutOfRangeException(nameof(level));
            int childPathLimit = 1 << (level * 2);
            if (childPath < 0 || childPath >= childPathLimit)
                throw new ArgumentOutOfRangeException(nameof(childPath));
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

            uint value = (uint)level |
                ((uint)childPath << ChildPathShift) |
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
            int level = (int)(value & LevelMask);
            int childPath = (int)((value >> ChildPathShift) & ChildPathMask);
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

        internal int Level => (int)(Value & LevelMask);
        internal int ChildPath =>
            (int)((Value >> ChildPathShift) & ChildPathMask);
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
        internal const int MetricFractionBits = 26;

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

        internal readonly int DrawMidpoint => checked((int)(Lower +
            ((long)Upper - Lower) / 2L));

        internal static int EncodePhaseLower(double value) =>
            EncodeLower(value, PhaseFractionBits);
        internal static int EncodePhaseUpper(double value) =>
            EncodeUpper(value, PhaseFractionBits);
        internal static double DecodePhase(int value) =>
            value / (double)(1L << PhaseFractionBits);

        internal static int EncodeMetricLower(double metres) =>
            EncodeLower(metres, MetricFractionBits);
        internal static int EncodeMetricUpper(double metres) =>
            EncodeUpper(metres, MetricFractionBits);
        internal static double DecodeMetric(int value) =>
            value / (double)(1L << MetricFractionBits);

        private static int EncodeLower(double value, int fractionBits) =>
            EncodeOutward(value, fractionBits, false);

        private static int EncodeUpper(double value, int fractionBits) =>
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
