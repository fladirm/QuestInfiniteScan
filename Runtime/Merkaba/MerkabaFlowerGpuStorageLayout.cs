namespace Genesis.RoomScan
{
    /// <summary>GPU transfer packets for M8 tiles and their Flower/Thread sidecars.</summary>
    internal static class MerkabaFlowerGpuStorageLayout
    {
        internal const int FineStoragePacketBytes = 4096;
        internal const int FineStoragePacketRecords = FineStoragePacketBytes / 16;
        internal const int FineStorageHeaderRecords = 2;
        internal const int FineStorageBodyWords = (FineStoragePacketBytes - 32) / sizeof(uint);
        internal const int WritebackTileRecords = 512 + 1 + FineStoragePacketRecords;
        internal const int FineLoadHeaderRecords = 8;
        internal const int FineLoadBodyWords = (FineStoragePacketRecords - FineLoadHeaderRecords) * 4;
        internal const int LoadTileRecords = 512 + FineStoragePacketRecords;
    }
}
