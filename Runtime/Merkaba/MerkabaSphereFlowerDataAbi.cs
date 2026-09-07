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

    /// <summary>Derived 16-byte draw symbol; never persistent scan truth.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct MerkabaFlowerSymbolRecord
    {
        internal const int ByteSize = 16;
        internal const uint KernelLocalMask = 0x1ffu;
        internal const int CarrierIdShift = 9;
        internal const uint CarrierIdMask = 0x7fu;
        internal const int ShellMaskShift = 16;
        internal const uint DirectFreeSideFlag = 1u << 19;
        internal const uint HingeFlag = 1u << 20;
        internal const uint DirtFlag = 1u << 21;
        internal const int ActiveWedgeShift = 22;
        internal const uint WedgeMask = 0x3fu;
        internal const uint OwnerReservedMask = 0xf0000000u;
        internal const uint RootSignsMask = 0x7fu;
        internal const int HubSectorShift = 7;
        internal const int CompletedWedgeShift = 12;
        internal const int ReverseWedgeShift = 18;
        internal const uint RootsReservedMask = 0xff000000u;
        internal const uint InvalidRef = 0xffffffffu;

        internal uint OwnerAndCarrier;
        internal uint RootsAndWedges;
        internal uint DetailRef;
        internal uint ThreadRef;

        internal readonly bool IsDirt => (OwnerAndCarrier & DirtFlag) != 0u;
        internal readonly int KernelLocal => (int)(OwnerAndCarrier & KernelLocalMask);
        internal readonly int CarrierId => (int)((OwnerAndCarrier >> CarrierIdShift) & CarrierIdMask);
        internal readonly int ShellMask => (int)((OwnerAndCarrier >> ShellMaskShift) & 7u);
        internal readonly bool DirectFreeSide => (OwnerAndCarrier & DirectFreeSideFlag) != 0u;
        internal readonly bool IsHinge => (OwnerAndCarrier & HingeFlag) != 0u;
        internal readonly uint ActiveWedgeMask => (OwnerAndCarrier >> ActiveWedgeShift) & WedgeMask;
        internal readonly uint RootSigns => RootsAndWedges & RootSignsMask;
        internal readonly int HubSector => (int)((RootsAndWedges >> HubSectorShift) & 31u);
        internal readonly uint CompletedWedgeMask => (RootsAndWedges >> CompletedWedgeShift) & WedgeMask;
        internal readonly uint ReverseWedgeMask => (RootsAndWedges >> ReverseWedgeShift) & WedgeMask;
        internal readonly int DirtHalf => (int)(RootsAndWedges & 1u);
        internal readonly bool IsCanonicalCarrier => !IsDirt &&
            (OwnerAndCarrier & OwnerReservedMask) == 0u &&
            (RootsAndWedges & RootsReservedMask) == 0u &&
            ActiveWedgeMask != 0u &&
            ((CompletedWedgeMask | ReverseWedgeMask) & ~ActiveWedgeMask) == 0u &&
            DetailRef != InvalidRef;
        internal readonly bool IsCanonicalDirt => IsDirt &&
            (OwnerAndCarrier & ~(KernelLocalMask | (CarrierIdMask << CarrierIdShift) |
                DirtFlag | (1u << ActiveWedgeShift))) == 0u &&
            CarrierId < MerkabaSphereFlowerAuthority.DirtFaceClassCount && ActiveWedgeMask == 1u &&
            (RootsAndWedges & ~1u) == 0u &&
            DetailRef == InvalidRef && ThreadRef == InvalidRef;

        // Packing validates the ABI only. The classified producer remains
        // responsible for certain roots, shell admission and direct coverage.
        internal static MerkabaFlowerSymbolRecord CreateCarrier(int kernelLocal,
            int carrierId, int shellMask, bool directFreeSide, bool hinge,
            uint activeWedgeMask, uint rootSigns, int hubSector,
            uint completedWedgeMask, uint reverseWedgeMask,
            uint detailRef = 0u, uint threadRef = InvalidRef)
        {
            if ((uint)kernelLocal > KernelLocalMask || (uint)carrierId > CarrierIdMask ||
                (uint)shellMask > 7u || activeWedgeMask == 0u || activeWedgeMask > WedgeMask ||
                rootSigns > RootSignsMask || (uint)hubSector > 31u ||
                completedWedgeMask > WedgeMask || reverseWedgeMask > WedgeMask ||
                ((completedWedgeMask | reverseWedgeMask) & ~activeWedgeMask) != 0u ||
                detailRef == InvalidRef)
                throw new ArgumentOutOfRangeException(nameof(kernelLocal),
                    "Invalid generated L2 carrier draw symbol.");
            return new MerkabaFlowerSymbolRecord
            {
                OwnerAndCarrier = (uint)kernelLocal | ((uint)carrierId << CarrierIdShift) |
                    ((uint)shellMask << ShellMaskShift) |
                    (directFreeSide ? DirectFreeSideFlag : 0u) | (hinge ? HingeFlag : 0u) |
                    (activeWedgeMask << ActiveWedgeShift),
                RootsAndWedges = rootSigns | ((uint)hubSector << HubSectorShift) |
                    (completedWedgeMask << CompletedWedgeShift) |
                    (reverseWedgeMask << ReverseWedgeShift),
                DetailRef = detailRef,
                ThreadRef = threadRef
            };
        }

        internal static MerkabaFlowerSymbolRecord CreateDirt(int kernelLocal, int face, int half)
        {
            if ((uint)kernelLocal > KernelLocalMask ||
                (uint)face >= MerkabaSphereFlowerAuthority.DirtFaceClassCount || (uint)half > 1u)
                throw new ArgumentOutOfRangeException(nameof(kernelLocal));
            return new MerkabaFlowerSymbolRecord
            {
                OwnerAndCarrier = (uint)kernelLocal | ((uint)face << CarrierIdShift) |
                    DirtFlag | (1u << ActiveWedgeShift),
                RootsAndWedges = (uint)half,
                DetailRef = InvalidRef,
                ThreadRef = InvalidRef
            };
        }

        internal readonly int3 DirtCell(int3 logicalTile)
        {
            if (!IsCanonicalDirt) throw new InvalidOperationException("Invalid DIRT symbol.");
            int local = KernelLocal;
            return new int3(checked(logicalTile.x * 8 + (local & 7)),
                checked(logicalTile.y * 8 + ((local >> 3) & 7)),
                checked(logicalTile.z * 8 + (local >> 6)));
        }
    }

    /// <summary>Bounded attempt-local observation record.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct MerkabaObservationRecord
    {
        internal const int ByteSize = 16;
        internal const int MaximumSourcePixels = 512 * 512;
        internal const int OwnersPerMeasurement = 8;
        internal const int Capacity = MaximumSourcePixels * OwnersPerMeasurement;

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
        internal const uint SchemaVersion = 3u;
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
        internal const int FlowerSkinMetricRunStride =
            MerkabaFlowerSkinMetricRun.ByteSize;
        internal const int FlowerVIntervalStride =
            MerkabaFlowerVInterval.ByteSize;
        internal const int FlowerVGroupStride = MerkabaFlowerVGroup.ByteSize;
        internal const int ThreadRunStride = MerkabaThreadRun.ByteSize;
        internal const int ThreadColorIntervalStride =
            MerkabaThreadColorInterval.ByteSize;
        internal const int ThreadColorGroupStride =
            MerkabaThreadColorGroup.ByteSize;
        internal const int ThreadProgramStride =
            MerkabaThreadProgramRecord.ByteSize;
        internal const int FlowerSymbolStride =
            MerkabaFlowerSymbolRecord.ByteSize;
        internal const int ObservationRecordStride =
            MerkabaObservationRecord.ByteSize;
    }
}
