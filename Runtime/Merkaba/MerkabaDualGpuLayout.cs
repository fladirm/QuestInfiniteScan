using System;

namespace Genesis.RoomScan
{
    /// <summary>
    /// Raw GPU residency layout of the canonical dual hierarchy. Logical
    /// addresses and persistent record layouts remain the existing M8 ABI.
    /// The 4 KiB allocation bitmap is transient storage metadata, not volume.
    /// </summary>
    internal static class MerkabaDualGpuLayout
    {
        internal const int BlockCapacity = MerkabaSpatial.BlockCapacity;
        internal const int ChunkCapacity = MerkabaSpatial.ChunkCapacity;
        internal const int LeafCapacity = MerkabaSpatial.PhysicalTileCapacity;
        internal const int LeafReferenceCapacity = LeafCapacity;
        internal const int StoragePacketBytes = 256;
        internal const int StoragePacketRecords = StoragePacketBytes / 16;
        internal const int FineStoragePacketBytes = 4096;
        internal const int FineStoragePacketRecords = FineStoragePacketBytes / 16;
        internal const int FineStorageHeaderRecords = 2;
        internal const int FineStorageBodyWords = (FineStoragePacketBytes - 32) / sizeof(uint);
        internal const int WritebackTileRecords = 512 + 1 + StoragePacketRecords + FineStoragePacketRecords;
        internal const int FineLoadHeaderRecords = 8;
        internal const int FineLoadBodyWords = (FineStoragePacketRecords - FineLoadHeaderRecords) * 4;
        internal const int LoadTileRecords = 512 + StoragePacketRecords + FineStoragePacketRecords;

        internal const int BlockMetaOffset = 0;
        internal const int BlockMasksOffset =
            BlockCapacity * MerkabaDualBlockMeta.ByteSize;
        internal const int DirtyNodeBitsOffset = BlockMasksOffset +
            BlockCapacity * MerkabaDualBlockChildren.ByteSize;
        internal const int DirtyNodeCount = BlockCapacity + ChunkCapacity;
        internal const int DirtyNodeWordCount = DirtyNodeCount / 32;
        internal const int DirtySummaryWordCount = DirtyNodeWordCount / 32;
        internal const int DirtyTopWordCount = (DirtySummaryWordCount + 31) / 32;
        internal const int DirtySummaryOffset = DirtyNodeBitsOffset + DirtyNodeWordCount * 4;
        internal const int DirtyTopOffset = DirtySummaryOffset + DirtySummaryWordCount * 4;
        internal const int DirtyRootOffset = DirtyTopOffset + DirtyTopWordCount * 4;
        internal const int BlockBufferBytes = DirtyRootOffset + 4;
        internal const int ChunkPayloadOffset = 0;
        internal const int ChunkBufferBytes =
            ChunkCapacity * MerkabaDualChunkPayload.ByteSize;
        internal const int LeafPayloadOffset = 0;
        internal const int LeafReferencesOffset =
            LeafCapacity * MerkabaDualLeaf.ByteSize;
        internal const int ReferenceAllocationOffset = LeafReferencesOffset +
            LeafReferenceCapacity * sizeof(uint);
        internal const int ReferenceAllocationWords = LeafReferenceCapacity / 32;
        internal const int LeafBufferBytes = ReferenceAllocationOffset +
            ReferenceAllocationWords * sizeof(uint);

        internal const uint NoReference = uint.MaxValue;
        internal const uint ColdReference = 1u << 30;
        internal const uint HotReference = 2u << 30;
        internal const uint SlotMask = 0x7fffu;
        internal const int SlotGenerationShift = 15;
        internal const int ResidencyShift = 30;

        internal static int BlockMetaAddress(int block) =>
            CheckedAddress(block, BlockCapacity, BlockMetaOffset,
                MerkabaDualBlockMeta.ByteSize);

        internal static int BlockMasksAddress(int block) =>
            CheckedAddress(block, BlockCapacity, BlockMasksOffset,
                MerkabaDualBlockChildren.ByteSize);

        internal static int ChunkAddress(int chunk) =>
            CheckedAddress(chunk, ChunkCapacity, ChunkPayloadOffset,
                MerkabaDualChunkPayload.ByteSize);

        internal static int LeafAddress(int physicalSlot) =>
            CheckedAddress(physicalSlot, LeafCapacity, LeafPayloadOffset,
                MerkabaDualLeaf.ByteSize);

        internal static int LeafReferenceAddress(int index) =>
            CheckedAddress(index, LeafReferenceCapacity,
                LeafReferencesOffset, sizeof(uint));

        internal static uint PackHotReference(int physicalSlot, uint generation)
        {
            if ((uint)physicalSlot >= LeafCapacity || generation == 0u ||
                generation > SlotMask)
                throw new ArgumentOutOfRangeException(nameof(physicalSlot));
            return HotReference | (generation << SlotGenerationShift) |
                (uint)physicalSlot;
        }

        // Exactly popcount(MixedMask) references, without power-of-two padding.
        // A span touches at most three allocator words. Failure retains the
        // old payload and requests another storage quantum/capacity handling.
        internal static int ReferenceAllocationSize(int count)
        {
            if ((uint)count > MerkabaSpatial.TilesPerChunk)
                throw new ArgumentOutOfRangeException(nameof(count));
            return count;
        }

        private static int CheckedAddress(int index, int capacity,
            int offset, int stride)
        {
            if ((uint)index >= capacity)
                throw new ArgumentOutOfRangeException(nameof(index));
            return checked(offset + index * stride);
        }
    }
}
