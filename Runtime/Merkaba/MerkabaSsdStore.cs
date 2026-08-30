using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine;

namespace Genesis.RoomScan
{
    internal sealed class MerkabaSessionSnapshot
    {
        internal Guid AnchorUuid;
        internal Matrix4x4 AnchorAtSave = Matrix4x4.identity;
        internal int IntegrationCount;
        internal readonly List<MerkabaTileSnapshot> Tiles = new();
    }

    /// <summary>
    /// Filesystem transport for logical M8 tile addresses. The index contains only
    /// file offsets and never participates in scan, query, topology, or visibility.
    /// </summary>
    internal sealed class MerkabaSsdStore
    {
        internal const uint CheckpointMagic = 0x384D4B4Du; // MKM8
        internal const uint OverlayMagic = 0x474C384Du;    // M8LG
        internal const int FormatVersion = 3;
        internal const int TilePayloadBytes =
            MerkabaSpatial.KernelsPerTile * 16;
        internal const int TileRecordHeaderBytes = 28;
        internal const int CheckpointHeaderBytes = 108;

        private readonly object _gate = new();
        private readonly Dictionary<MerkabaTileAddress, Location> _index = new();
        private readonly string _directory;

        private readonly struct Location
        {
            internal readonly string Path;
            internal readonly long PayloadOffset;
            internal readonly uint Generation;

            internal Location(string path, long payloadOffset, uint generation)
            {
                Path = path;
                PayloadOffset = payloadOffset;
                Generation = generation;
            }
        }

        private readonly struct PendingIndexUpdate
        {
            internal readonly MerkabaTileSnapshot Tile;
            internal readonly Location Location;

            internal PendingIndexUpdate(MerkabaTileSnapshot tile,
                Location location)
            {
                Tile = tile;
                Location = location;
            }
        }

        internal MerkabaSsdStore(string directory)
        {
            _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        }

        internal string CheckpointPath => Path.Combine(_directory,
            "merkaba-grid.bin");
        internal string OverlayPath => Path.Combine(_directory,
            "merkaba-live.m8log");

        internal int IndexedTileCount
        {
            get { lock (_gate) return _index.Count; }
        }

        internal Task RebuildIndexAsync(
            IProgress<OperationWorkProgress> progress = null) =>
            Task.Run(() => RebuildIndex(progress));

        internal void RebuildIndex(IProgress<OperationWorkProgress> progress = null)
        {
            var rebuilt = new Dictionary<MerkabaTileAddress, Location>();
            long checkpointBytes = File.Exists(CheckpointPath)
                ? new FileInfo(CheckpointPath).Length : 0L;
            long overlayBytes = File.Exists(OverlayPath)
                ? new FileInfo(OverlayPath).Length : 0L;
            long totalBytes = checkpointBytes + overlayBytes;
            long completedBase = 0L;
            if (File.Exists(CheckpointPath))
            {
                ScanCheckpoint(CheckpointPath, rebuilt, bytes =>
                    ReportBytes(progress, ScanOperationStage.RebuildingStorageIndex,
                        completedBase + bytes, totalBytes,
                        "Rebuilding checkpoint tile index"));
                completedBase += checkpointBytes;
            }
            if (File.Exists(OverlayPath) &&
                new FileInfo(OverlayPath).Length > 0)
                ScanOverlay(OverlayPath, rebuilt, bytes =>
                    ReportBytes(progress, ScanOperationStage.RebuildingStorageIndex,
                        completedBase + bytes, totalBytes,
                        "Rebuilding overlay tile index"));
            lock (_gate)
            {
                _index.Clear();
                foreach (var pair in rebuilt) _index.Add(pair.Key, pair.Value);
            }
            ReportBytes(progress, ScanOperationStage.RebuildingStorageIndex,
                totalBytes, totalBytes, $"Indexed {rebuilt.Count} canonical tiles");
        }

        internal Task AppendAsync(IReadOnlyList<MerkabaTileSnapshot> tiles) =>
            Task.Run(() => Append(tiles));

        internal void Append(IReadOnlyList<MerkabaTileSnapshot> tiles)
        {
            if (tiles == null) throw new ArgumentNullException(nameof(tiles));
            if (tiles.Count == 0) return;
            if (tiles.Count > MerkabaGrid.StreamBatchCapacity)
                throw new InvalidDataException("M8 writeback batch exceeds 32 tiles.");
            Directory.CreateDirectory(_directory);
            bool newFile = !File.Exists(OverlayPath) ||
                           new FileInfo(OverlayPath).Length == 0;
            long originalLength = newFile ? 0L : new FileInfo(OverlayPath).Length;
            var pending = new List<PendingIndexUpdate>(tiles.Count);
            try
            {
                using var stream = new FileStream(OverlayPath, FileMode.Append,
                    FileAccess.Write, FileShare.Read, 256 * 1024,
                    FileOptions.WriteThrough);
                using var writer = new BinaryWriter(stream,
                    new UTF8Encoding(false), true);
                if (newFile)
                {
                    writer.Write(OverlayMagic);
                    writer.Write(FormatVersion);
                }
                foreach (MerkabaTileSnapshot tile in tiles)
                {
                    ValidateTile(tile);
                    uint generation;
                    lock (_gate)
                    {
                        generation = _index.TryGetValue(tile.Address,
                            out Location prior)
                            ? checked(prior.Generation + 1u) : 1u;
                    }
                    WriteAddress(writer, tile.Address);
                    writer.Write(generation);
                    writer.Write(TilePayloadBytes);
                    writer.Write(Crc32(tile.States));
                    long payloadOffset = stream.Position;
                    WriteStates(writer, tile.States);
                    pending.Add(new PendingIndexUpdate(tile,
                        new Location(OverlayPath, payloadOffset, generation)));
                }
                writer.Flush();
                stream.Flush(true);
            }
            catch
            {
                TryTruncateOverlay(originalLength);
                throw;
            }
            lock (_gate)
            {
                foreach (PendingIndexUpdate update in pending)
                {
                    _index[update.Tile.Address] = update.Location;
                    update.Tile.Generation = update.Location.Generation;
                }
            }
        }

        private void TryTruncateOverlay(long length)
        {
            try
            {
                using var stream = new FileStream(OverlayPath, FileMode.Open,
                    FileAccess.Write, FileShare.Read);
                stream.SetLength(length);
                stream.Flush(true);
            }
            catch (Exception exception)
            {
                Logger.Error("Could not roll back failed M8 overlay append: " +
                             exception.Message);
            }
        }

        internal Task<MerkabaTileSnapshot[]> ReadAsync(
            IReadOnlyList<MerkabaTileAddress> addresses) => Task.Run(() =>
        {
            if (addresses == null) throw new ArgumentNullException(nameof(addresses));
            if (addresses.Count > MerkabaGrid.StreamBatchCapacity)
                throw new InvalidDataException("M8 load batch exceeds 32 tiles.");
            var result = new MerkabaTileSnapshot[addresses.Count];
            for (int index = 0; index < addresses.Count; index++)
                result[index] = ReadOne(addresses[index]);
            return result;
        });

        internal Task<MerkabaSessionSnapshot> ReadCanonicalSnapshotAsync(
            Guid anchorUuid, Matrix4x4 anchorAtSave, int integrationCount,
            IProgress<OperationWorkProgress> progress = null) =>
            Task.Run(() =>
            {
                var snapshot = new MerkabaSessionSnapshot
                {
                    AnchorUuid = anchorUuid,
                    AnchorAtSave = anchorAtSave,
                    IntegrationCount = Mathf.Max(0, integrationCount)
                };
                List<MerkabaTileAddress> addresses;
                lock (_gate) addresses = new List<MerkabaTileAddress>(_index.Keys);
                addresses.Sort();
                for (int index = 0; index < addresses.Count; index++)
                {
                    MerkabaTileAddress address = addresses[index];
                    snapshot.Tiles.Add(ReadOne(address));
                    if (ShouldReport(index + 1, addresses.Count, 32))
                        progress?.Report(new OperationWorkProgress(
                            ScanOperationStage.CapturingState, index + 1,
                            addresses.Count,
                            $"Captured {index + 1}/{addresses.Count} canonical tiles"));
                }
                if (addresses.Count == 0)
                    progress?.Report(new OperationWorkProgress(
                        ScanOperationStage.CapturingState, 0, 0,
                        "Canonical snapshot is empty"));
                return snapshot;
            });

        internal Task PublishCheckpointAsync(MerkabaSessionSnapshot snapshot,
            IProgress<OperationWorkProgress> progress = null) =>
            Task.Run(() => PublishCheckpoint(snapshot, progress));

        internal void PublishCheckpoint(MerkabaSessionSnapshot snapshot,
            IProgress<OperationWorkProgress> progress = null)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            Directory.CreateDirectory(_directory);
            string temporary = CheckpointPath + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.Create,
                       FileAccess.Write, FileShare.None, 1024 * 1024,
                       FileOptions.WriteThrough))
            {
                WriteCheckpoint(stream, snapshot, progress);
                stream.Flush(true);
            }
            progress?.Report(new OperationWorkProgress(
                ScanOperationStage.PublishingFile, 0, 1,
                "Publishing durable checkpoint"));
            MerkabaFilePublishing.Publish(temporary, CheckpointPath);
            if (File.Exists(OverlayPath)) File.Delete(OverlayPath);
            RebuildIndex();
            progress?.Report(new OperationWorkProgress(
                ScanOperationStage.PublishingFile, 1, 1,
                "Checkpoint published"));
        }

        internal void Clear()
        {
            lock (_gate) _index.Clear();
            DeleteIfExists(CheckpointPath);
            DeleteIfExists(CheckpointPath + ".tmp");
            DeleteIfExists(OverlayPath);
        }

        internal static void WriteCheckpoint(Stream destination,
            MerkabaSessionSnapshot snapshot,
            IProgress<OperationWorkProgress> progress = null)
        {
            if (destination == null || !destination.CanWrite)
                throw new ArgumentException("Checkpoint destination is not writable.",
                    nameof(destination));
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            snapshot.Tiles.Sort((left, right) =>
                left.Address.CompareTo(right.Address));
            using var writer = new BinaryWriter(destination,
                new UTF8Encoding(false), true);
            writer.Write(CheckpointMagic);
            writer.Write(FormatVersion);
            writer.Write(MerkabaConstants.SupportSize);
            writer.Write(MerkabaConstants.LatticeStep);
            writer.Write(MerkabaConstants.ChunkSize);
            writer.Write(snapshot.AnchorUuid.ToByteArray());
            for (int i = 0; i < 16; i++) writer.Write(snapshot.AnchorAtSave[i]);
            writer.Write(snapshot.IntegrationCount);
            writer.Write(snapshot.Tiles.Count);
            long totalBytes = checked(CheckpointHeaderBytes +
                (long)snapshot.Tiles.Count *
                (TileRecordHeaderBytes + TilePayloadBytes));
            progress?.Report(new OperationWorkProgress(
                ScanOperationStage.WritingFile, CheckpointHeaderBytes,
                totalBytes, "Writing checkpoint header"));
            MerkabaTileAddress previous = default;
            for (int index = 0; index < snapshot.Tiles.Count; index++)
            {
                MerkabaTileSnapshot tile = snapshot.Tiles[index];
                ValidateTile(tile);
                if (index > 0 && previous.CompareTo(tile.Address) >= 0)
                    throw new InvalidDataException("M8 checkpoint tiles are not unique/sorted.");
                previous = tile.Address;
                WriteAddress(writer, tile.Address);
                writer.Write(tile.Generation);
                writer.Write(TilePayloadBytes);
                writer.Write(Crc32(tile.States));
                WriteStates(writer, tile.States);
                if (ShouldReport(index + 1, snapshot.Tiles.Count, 32))
                    progress?.Report(new OperationWorkProgress(
                        ScanOperationStage.WritingFile,
                        CheckpointHeaderBytes + (long)(index + 1) *
                        (TileRecordHeaderBytes + TilePayloadBytes), totalBytes,
                        $"Wrote {index + 1}/{snapshot.Tiles.Count} tile records"));
            }
            writer.Flush();
        }

        internal static MerkabaSessionSnapshot ReadCheckpoint(Stream source,
            IProgress<OperationWorkProgress> progress = null)
        {
            if (source == null || !source.CanRead)
                throw new ArgumentException("Checkpoint source is not readable.",
                    nameof(source));
            using var reader = new BinaryReader(source, Encoding.UTF8, true);
            ReadCheckpointHeader(reader, out MerkabaSessionSnapshot snapshot,
                out int tileCount);
            long totalBytes = source.CanSeek ? source.Length :
                checked(CheckpointHeaderBytes + (long)tileCount *
                    (TileRecordHeaderBytes + TilePayloadBytes));
            progress?.Report(new OperationWorkProgress(
                ScanOperationStage.ReadingFile, CheckpointHeaderBytes,
                totalBytes, "Read checkpoint header"));
            MerkabaTileAddress previous = default;
            for (int index = 0; index < tileCount; index++)
            {
                MerkabaTileAddress address = ReadAddress(reader);
                uint generation = reader.ReadUInt32();
                int bytes = reader.ReadInt32();
                uint crc = reader.ReadUInt32();
                if (bytes != TilePayloadBytes)
                    throw new InvalidDataException("M8 checkpoint payload size is invalid.");
                var states = ReadStates(reader);
                if (Crc32(states) != crc)
                    throw new InvalidDataException("M8 checkpoint tile CRC mismatch.");
                if (index > 0 && previous.CompareTo(address) >= 0)
                    throw new InvalidDataException("M8 checkpoint tiles are duplicated/unsorted.");
                previous = address;
                snapshot.Tiles.Add(new MerkabaTileSnapshot
                {
                    Address = address,
                    Generation = generation,
                    States = states
                });
                if (ShouldReport(index + 1, tileCount, 32))
                    progress?.Report(new OperationWorkProgress(
                        ScanOperationStage.ReadingFile,
                        CheckpointHeaderBytes + (long)(index + 1) *
                        (TileRecordHeaderBytes + TilePayloadBytes), totalBytes,
                        $"Read {index + 1}/{tileCount} tile records"));
            }
            if (source.CanSeek && source.Position != source.Length)
                throw new InvalidDataException("M8 checkpoint has trailing bytes.");
            return snapshot;
        }

        private MerkabaTileSnapshot ReadOne(MerkabaTileAddress address)
        {
            Location location;
            lock (_gate)
            {
                if (!_index.TryGetValue(address, out location))
                    throw new FileNotFoundException($"M8 tile {address.LocalAddress} " +
                        $"at {address.BlockCoord} is absent from storage.");
            }
            using var stream = new FileStream(location.Path, FileMode.Open,
                FileAccess.Read, FileShare.ReadWrite, 16 * 1024,
                FileOptions.RandomAccess);
            stream.Position = location.PayloadOffset;
            using var reader = new BinaryReader(stream, Encoding.UTF8, true);
            KernelState[] states = ReadStates(reader);
            return new MerkabaTileSnapshot
            {
                Address = address,
                Generation = location.Generation,
                States = states
            };
        }

        private static void ScanCheckpoint(string path,
            IDictionary<MerkabaTileAddress, Location> index,
            Action<long> reportBytes = null)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
            using var reader = new BinaryReader(stream, Encoding.UTF8, true);
            ReadCheckpointHeader(reader, out _, out int tileCount);
            reportBytes?.Invoke(stream.Position);
            for (int item = 0; item < tileCount; item++)
            {
                ScanRecord(path, stream, reader, index);
                if (ShouldReport(item + 1, tileCount, 32))
                    reportBytes?.Invoke(stream.Position);
            }
            if (stream.Position != stream.Length)
                throw new InvalidDataException("M8 checkpoint has trailing bytes.");
        }

        private static void ScanOverlay(string path,
            IDictionary<MerkabaTileAddress, Location> index,
            Action<long> reportBytes = null)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite, 1024 * 1024, FileOptions.SequentialScan);
            using var reader = new BinaryReader(stream, Encoding.UTF8, true);
            if (reader.ReadUInt32() != OverlayMagic ||
                reader.ReadInt32() != FormatVersion)
                throw new InvalidDataException("Unsupported M8 overlay log.");
            reportBytes?.Invoke(stream.Position);
            int item = 0;
            while (stream.Position < stream.Length)
            {
                ScanRecord(path, stream, reader, index);
                item++;
                if ((item & 31) == 0 || stream.Position == stream.Length)
                    reportBytes?.Invoke(stream.Position);
            }
        }

        private static void ScanRecord(string path, Stream stream,
            BinaryReader reader, IDictionary<MerkabaTileAddress, Location> index)
        {
            MerkabaTileAddress address = ReadAddress(reader);
            uint generation = reader.ReadUInt32();
            int bytes = reader.ReadInt32();
            uint expectedCrc = reader.ReadUInt32();
            if (bytes != TilePayloadBytes || stream.Length - stream.Position < bytes)
                throw new InvalidDataException("M8 tile record is truncated.");
            long payloadOffset = stream.Position;
            KernelState[] states = ReadStates(reader);
            if (Crc32(states) != expectedCrc)
                throw new InvalidDataException("M8 tile record CRC mismatch.");
            if (!index.TryGetValue(address, out Location prior) ||
                generation >= prior.Generation)
                index[address] = new Location(path, payloadOffset, generation);
        }

        private static void ReadCheckpointHeader(BinaryReader reader,
            out MerkabaSessionSnapshot snapshot, out int tileCount)
        {
            if (reader.ReadUInt32() != CheckpointMagic ||
                reader.ReadInt32() != FormatVersion)
                throw new InvalidDataException("Unsupported M8 checkpoint format.");
            if (reader.ReadSingle() != MerkabaConstants.SupportSize ||
                reader.ReadSingle() != MerkabaConstants.LatticeStep ||
                reader.ReadInt32() != MerkabaConstants.ChunkSize)
                throw new InvalidDataException("M8 geometry constants do not match.");
            snapshot = new MerkabaSessionSnapshot
            {
                AnchorUuid = new Guid(ReadExact(reader, 16))
            };
            Matrix4x4 matrix = default;
            for (int index = 0; index < 16; index++) matrix[index] = reader.ReadSingle();
            snapshot.AnchorAtSave = matrix;
            snapshot.IntegrationCount = reader.ReadInt32();
            tileCount = reader.ReadInt32();
            if (snapshot.IntegrationCount < 0 || tileCount < 0 ||
                tileCount > MerkabaSpatial.ChunkCapacity *
                MerkabaSpatial.TilesPerChunk)
                throw new InvalidDataException("M8 checkpoint counts are invalid.");
        }

        private static void WriteAddress(BinaryWriter writer,
            MerkabaTileAddress address)
        {
            writer.Write(address.BlockCoord.x);
            writer.Write(address.BlockCoord.y);
            writer.Write(address.BlockCoord.z);
            writer.Write(address.LocalAddress);
        }

        private static MerkabaTileAddress ReadAddress(BinaryReader reader) => new(
            new int3(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32()),
            reader.ReadUInt32());

        private static void WriteStates(BinaryWriter writer, KernelState[] states)
        {
            foreach (KernelState state in states)
            {
                writer.Write(state.OccupancyEvidence);
                writer.Write(state.PackedColor);
                writer.Write(state.ColorConfidence);
                writer.Write(state.Flags);
            }
        }

        private static KernelState[] ReadStates(BinaryReader reader)
        {
            var states = new KernelState[MerkabaSpatial.KernelsPerTile];
            for (int index = 0; index < states.Length; index++)
            {
                states[index].OccupancyEvidence = reader.ReadInt32();
                states[index].PackedColor = reader.ReadUInt32();
                states[index].ColorConfidence = reader.ReadUInt32();
                states[index].Flags = reader.ReadUInt32();
                Validate(states[index]);
            }
            return states;
        }

        private static uint Crc32(KernelState[] states)
        {
            uint crc = 0xffffffffu;
            foreach (KernelState state in states)
            {
                UpdateCrc(ref crc, unchecked((uint)state.OccupancyEvidence));
                UpdateCrc(ref crc, state.PackedColor);
                UpdateCrc(ref crc, state.ColorConfidence);
                UpdateCrc(ref crc, state.Flags);
            }
            return ~crc;
        }

        private static void UpdateCrc(ref uint crc, uint value)
        {
            for (int octet = 0; octet < 4; octet++)
            {
                crc ^= (byte)(value >> (octet * 8));
                for (int bit = 0; bit < 8; bit++)
                    crc = (crc >> 1) ^ ((crc & 1u) != 0u
                        ? 0xedb88320u : 0u);
            }
        }

        private static void ReportBytes(IProgress<OperationWorkProgress> progress,
            ScanOperationStage stage, long completed, long total, string text)
        {
            progress?.Report(new OperationWorkProgress(stage, completed, total,
                text));
        }

        private static bool ShouldReport(int completed, int total, int interval) =>
            completed == total || completed % interval == 0;

        private static void ValidateTile(MerkabaTileSnapshot tile)
        {
            if (tile?.States == null ||
                tile.States.Length != MerkabaSpatial.KernelsPerTile)
                throw new InvalidDataException("M8 tile payload must be exactly 8192 bytes.");
            foreach (KernelState state in tile.States) Validate(state);
        }

        private static void Validate(KernelState state)
        {
            if (state.OccupancyEvidence < MerkabaConstants.MinimumEvidence ||
                state.OccupancyEvidence > MerkabaConstants.MaximumEvidence ||
                state.ColorConfidence > MerkabaConstants.MaximumColorConfidence ||
                (state.Flags & ~(MerkabaConstants.OccupiedFlag |
                                 MerkabaConstants.NeedsCarveFlag |
                                 MerkabaConstants.SurfacePlanePayloadMask)) != 0 ||
                (!state.IsOccupied && state.HasMeasuredSurfacePlane))
                throw new InvalidDataException("M8 KernelState is out of range.");
        }

        private static byte[] ReadExact(BinaryReader reader, int count)
        {
            byte[] result = reader.ReadBytes(count);
            if (result.Length != count) throw new EndOfStreamException();
            return result;
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
