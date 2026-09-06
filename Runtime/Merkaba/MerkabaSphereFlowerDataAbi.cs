using System;
using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace Genesis.RoomScan
{
    internal enum MerkabaFlowerSymbolStatus : byte
    {
        Confirmed = 0,
        Veto = 1,
        HoleCandidate = 2,
        Completed = 3,
        Unresolved = 4,
        Refine = 5
    }

    /// <summary>
    /// Packed transient symbol tag. The junction itself is stored separately;
    /// this type is never a persistent PortKey or adjacency graph.
    /// </summary>
    internal readonly struct MerkabaFlowerSymbolTag :
        IEquatable<MerkabaFlowerSymbolTag>
    {
        internal const int LevelShift = 0;
        internal const int LineClassShift = 3;
        internal const int RootSignShift = 7;
        internal const int SectorShift = 8;
        internal const int EndpointOrientationShift = 13;
        internal const int ShellValidityShift = 14;
        internal const int StatusShift = 17;
        internal const int ScratchShift = 20;

        internal const uint LevelMask = 0x7u;
        internal const uint LineClassMask = 0xfu;
        internal const uint SectorMask = 0x1fu;
        internal const uint ShellValidityMask = 0x7u;
        internal const uint StatusMask = 0x7u;
        internal const uint ScratchMask = 0xfffu;

        internal readonly uint Value;

        private MerkabaFlowerSymbolTag(uint value) => Value = value;

        internal static MerkabaFlowerSymbolTag Create(int level,
            int lineClass, bool rootSign, int sector,
            bool endpointOrientation, uint shellValidity,
            MerkabaFlowerSymbolStatus status, uint scratch = 0u)
        {
            if ((uint)level >= MerkabaSphereFlowerAuthority.LevelCount)
                throw new ArgumentOutOfRangeException(nameof(level));
            if ((uint)lineClass >=
                MerkabaSphereFlowerAuthority.LineClassCount)
                throw new ArgumentOutOfRangeException(nameof(lineClass));
            int sectorCount = MerkabaSphereFlowerAuthority.Lines[lineClass]
                .SectorCount;
            if (sector < 0 || sector >= sectorCount)
                throw new ArgumentOutOfRangeException(nameof(sector));
            if (shellValidity > ShellValidityMask)
                throw new ArgumentOutOfRangeException(nameof(shellValidity));
            if ((uint)status > (uint)MerkabaFlowerSymbolStatus.Refine)
                throw new ArgumentOutOfRangeException(nameof(status));
            if (scratch > ScratchMask)
                throw new ArgumentOutOfRangeException(nameof(scratch));

            uint value = (uint)level |
                ((uint)lineClass << LineClassShift) |
                (rootSign ? 1u << RootSignShift : 0u) |
                ((uint)sector << SectorShift) |
                (endpointOrientation ? 1u << EndpointOrientationShift : 0u) |
                (shellValidity << ShellValidityShift) |
                ((uint)status << StatusShift) |
                (scratch << ScratchShift);
            return new MerkabaFlowerSymbolTag(value);
        }

        internal static bool TryDecode(uint value,
            out MerkabaFlowerSymbolTag tag)
        {
            try
            {
                tag = Create(
                    (int)(value & LevelMask),
                    (int)((value >> LineClassShift) & LineClassMask),
                    ((value >> RootSignShift) & 1u) != 0u,
                    (int)((value >> SectorShift) & SectorMask),
                    ((value >> EndpointOrientationShift) & 1u) != 0u,
                    (value >> ShellValidityShift) & ShellValidityMask,
                    (MerkabaFlowerSymbolStatus)((value >> StatusShift) &
                        StatusMask),
                    (value >> ScratchShift) & ScratchMask);
                return tag.Value == value;
            }
            catch (ArgumentOutOfRangeException)
            {
                tag = default;
                return false;
            }
        }

        internal int Level => (int)(Value & LevelMask);
        internal int LineClass =>
            (int)((Value >> LineClassShift) & LineClassMask);
        internal bool RootSign => ((Value >> RootSignShift) & 1u) != 0u;
        internal int Sector => (int)((Value >> SectorShift) & SectorMask);
        internal bool EndpointOrientation =>
            ((Value >> EndpointOrientationShift) & 1u) != 0u;
        internal uint ShellValidity =>
            (Value >> ShellValidityShift) & ShellValidityMask;
        internal MerkabaFlowerSymbolStatus Status =>
            (MerkabaFlowerSymbolStatus)((Value >> StatusShift) & StatusMask);
        internal uint Scratch => (Value >> ScratchShift) & ScratchMask;

        public bool Equals(MerkabaFlowerSymbolTag other) => Value == other.Value;
        public override bool Equals(object obj) =>
            obj is MerkabaFlowerSymbolTag other && Equals(other);
        public override int GetHashCode() => unchecked((int)Value);
    }

    /// <summary>Sixteen-byte transient canonical Flower symbol.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct MerkabaFlowerSymbolKey
    {
        internal const int ByteSize = 16;

        internal int JunctionX;
        internal int JunctionY;
        internal int JunctionZ;
        internal uint Tag;

        internal MerkabaFlowerSymbolKey(int3 junction,
            MerkabaFlowerSymbolTag tag)
        {
            JunctionX = junction.x;
            JunctionY = junction.y;
            JunctionZ = junction.z;
            Tag = tag.Value;
        }

        internal readonly int3 Junction =>
            new(JunctionX, JunctionY, JunctionZ);

        internal readonly bool IsCanonical =>
            MerkabaFlowerSymbolTag.TryDecode(Tag, out _);
    }

    /// <summary>Bounded attempt-local observation record.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct MerkabaObservationRecord
    {
        internal const int ByteSize = 16;

        internal uint TileAndKernel;
        internal uint SourcePixel;
        internal uint SymbolTag;
        internal uint PrecisionKey;
    }

    /// <summary>
    /// ABI sizes shared by C#, generated HLSL and the native static assertions.
    /// No resource using these strides is allocated until its owning cut.
    /// </summary>
    internal static class MerkabaSphereFlowerDataAbi
    {
        internal const uint SchemaVersion = 2u;
        // Frozen CUT 05 flag layout. No CUT 02 consumer reads this bit.
        internal const uint R1SeedFlag = 1u << 1;
        internal const int KernelStateStride = 16;
        internal const int DualBlockMetaStride =
            MerkabaDualBlockMeta.ByteSize;
        internal const int DualBlockChildrenStride =
            MerkabaDualBlockChildren.ByteSize;
        internal const int DualChunkStride =
            MerkabaDualChunkPayload.ByteSize;
        internal const int DualLeafStride = MerkabaDualLeaf.ByteSize;
        internal const int FlowerOwnerEpochStride =
            MerkabaFlowerOwnerEpoch.ByteSize;
        internal const int FlowerDetailStride =
            MerkabaFlowerDetailRecord.ByteSize;
        internal const int ThreadRunStride = MerkabaThreadRun.ByteSize;
        internal const int ThreadResidualStride =
            MerkabaThreadResidual.ByteSize;
        internal const int ThreadProgramStride =
            MerkabaThreadProgramRecord.ByteSize;
        internal const int FlowerSymbolStride =
            MerkabaFlowerSymbolKey.ByteSize;
        internal const int ObservationRecordStride =
            MerkabaObservationRecord.ByteSize;
    }
}
