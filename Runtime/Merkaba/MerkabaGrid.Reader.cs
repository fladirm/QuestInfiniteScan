using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Mathematics;

namespace Genesis.RoomScan
{
    public sealed partial class MerkabaGrid
    {
        // The caller already quiesced scan and captured the sorted M8 index.
        // Export reads the same complete tile/sidecar packets as residency,
        // bounded to this tile and its existing 27-tile context. No GPU
        // geometry readback or independently reconstructed surface is used.
        internal Task<MerkabaSphereFlowerAuthority.SnapshotReader> ReadStoredFlowerContextAsync(
            MerkabaTileAddress ownerTile, MerkabaTileAddress[] capturedIndex,
            MerkabaStorageAppendPosition position)
        {
            if (capturedIndex == null) throw new ArgumentNullException(nameof(capturedIndex));
            if (_storageReplacementPending)
                throw new InvalidOperationException("Cannot read Flower geometry while storage authority is changing.");
            EnsureStorage();
            return _ssdStore.ReadFlowerContextAsync(ownerTile, capturedIndex, position);
        }

        internal Task<long> StreamStoredFlowerDirtAsync(float2 planeBounds,
            MerkabaTileAddress[] capturedIndex, MerkabaStorageAppendPosition position,
            Action<IReadOnlyList<MerkabaDirtTriangle>> consume)
        {
            EnsureStorage();
            if (_storageReplacementPending)
                throw new InvalidOperationException("Cannot export Flower DIRT while storage authority is changing.");
            return _ssdStore.StreamFlowerDirtAsync(planeBounds, capturedIndex, position, consume);
        }

        internal MerkabaTileAddress[] CaptureStoredFlowerSource(out MerkabaStorageAppendPosition position)
        {
            EnsureStorage();
            return _ssdStore.CaptureFlowerSource(out position);
        }

        internal static List<MerkabaTileAddress> FlowerContextAddresses(
            MerkabaTileAddress ownerTile, MerkabaTileAddress[] capturedIndex)
        {
            int3 origin = MerkabaSpatial.Decode(ownerTile.BlockCoord, ownerTile.LocalAddress, 0);
            var context = new List<MerkabaTileAddress>(27);
            for (int z = -1; z <= 1; z++)
            for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
            {
                long gx = (long)origin.x + x * MerkabaSpatial.TileSize;
                long gy = (long)origin.y + y * MerkabaSpatial.TileSize;
                long gz = (long)origin.z + z * MerkabaSpatial.TileSize;
                if (gx < int.MinValue || gx > int.MaxValue || gy < int.MinValue || gy > int.MaxValue ||
                    gz < int.MinValue || gz > int.MaxValue) continue;
                MerkabaSpatial.Address address = MerkabaSpatial.Encode(new int3((int)gx, (int)gy, (int)gz));
                var tile = new MerkabaTileAddress(address.BlockCoord,
                    (uint)(address.ChunkLocal | (address.TileLocal << 9)));
                if (Array.BinarySearch(capturedIndex, tile) >= 0) context.Add(tile);
            }
            return context;
        }
    }
}
