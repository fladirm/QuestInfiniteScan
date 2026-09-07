using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Unity.Mathematics;

namespace Genesis.RoomScan
{
    /// <summary>
    /// REV-B filesystem transport. Append tails are usable by the live process;
    /// only the six byte ranges named by the atomically published manifest are
    /// durable session authority.
    /// </summary>
    internal sealed class MerkabaSsdStore
    {
        internal const int TilePayloadBytes =
            MerkabaSpatial.KernelsPerTile * KernelState.ByteSize;
        internal const int EncodedM8RecordBytes = MerkabaRecordHeader.ByteSize +
            MerkabaSphereFlowerPersistenceAbi.TileAddressBytes +
            TilePayloadBytes;

        internal const string M8BaseFileName = "merkaba-base.bin";
        internal const string M8LiveFileName = "merkaba-live.m8log";
        internal const string ThroughBaseFileName = "through-base.bin";
        internal const string ThroughLiveFileName = "through-live.tlog";
        internal const string FlowerDetailFileName = "flower-detail.flog";
        internal const string ThreadAtlasFileName = "thread-atlas.tlog";
        internal const string ManifestFileName = "session-manifest.bin";

        private readonly object _gate = new();
        private readonly object _ioGate = new();
        private object _appendAuthority = new();
        private Exception _appendPublicationFailure;
        private readonly Dictionary<MerkabaTileAddress, Location> _index = new();
        private readonly Dictionary<int3, HashSet<MerkabaTileAddress>>
            _indexedTilesByBlock = new();
        private readonly HashSet<MerkabaStorageStream> _dirtyStreams = new();
        private readonly HashSet<MerkabaTileAddress> _dirtyM8Tiles = new();
        private readonly HashSet<int3> _dirtyDualBlocks = new();
        private readonly HashSet<MerkabaOwnerAddress> _dirtyFineOwners = new();
        private readonly MerkabaSphereFlowerReplayIndex _subordinate = new();
        private readonly string _directory;
        private MerkabaSessionManifest _manifest;
        private ulong _pendingGeneration = 1ul;
        private ulong _recordSequence;
        private ulong _indexedOccupiedKernelCount;

        private readonly struct Location
        {
            internal readonly string Path;
            internal readonly long PayloadOffset;
            internal readonly ulong Generation;
            internal readonly ulong Sequence;
            internal readonly uint OccupiedKernelCount;

            internal Location(string path, long payloadOffset,
                ulong generation, ulong sequence, uint occupiedKernelCount)
            {
                Path = path;
                PayloadOffset = payloadOffset;
                Generation = generation;
                Sequence = sequence;
                OccupiedKernelCount = occupiedKernelCount;
            }

            internal bool IsNewerThan(Location other) =>
                Generation > other.Generation ||
                (Generation == other.Generation && Sequence >= other.Sequence);
        }

        private readonly struct PendingM8Location
        {
            internal readonly MerkabaTileSnapshot Tile;
            internal readonly Location Location;

            internal PendingM8Location(MerkabaTileSnapshot tile,
                Location location)
            {
                Tile = tile;
                Location = location;
            }
        }

        internal MerkabaSsdStore(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("Storage directory is required.",
                    nameof(directory));
            _directory = Path.GetFullPath(directory);
        }

        internal string ManifestPath => Path.Combine(_directory,
            ManifestFileName);
        internal string M8BasePath => Path.Combine(_directory,
            M8BaseFileName);
        internal string M8LivePath => Path.Combine(_directory,
            M8LiveFileName);
        internal string ThroughBasePath => Path.Combine(_directory,
            ThroughBaseFileName);
        internal string ThroughLivePath => Path.Combine(_directory,
            ThroughLiveFileName);
        internal string FlowerDetailPath => Path.Combine(_directory,
            FlowerDetailFileName);
        internal string ThreadAtlasPath => Path.Combine(_directory,
            ThreadAtlasFileName);

        internal int IndexedTileCount
        {
            get { lock (_gate) return _index.Count; }
        }

        internal Task<long> StreamDirtAsync(
            Func<int3, int, MerkabaDirtFaceCoverage> directCoverage,
            Action<IReadOnlyList<MerkabaDirtTriangle>> consume)
        {
            if (directCoverage == null)
                throw new ArgumentNullException(nameof(directCoverage));
            if (consume == null) throw new ArgumentNullException(nameof(consume));
            // Export is already quiesced. Hold the exact replay generation while
            // synchronously consuming bounded batches; do not clone the world or
            // let a later append change the FREE/FULL relation mid-file. Coverage
            // is the prepared shared L2 evaluator, never an asynchronous readback.
            return Task.Run(() =>
            {
                lock (_ioGate)
                lock (_gate)
                    return MerkabaDirtExtraction.Stream(_subordinate,
                        directCoverage, consume);
            });
        }

        internal Task<MerkabaSphereFlowerAuthority.SnapshotReader> ReadFlowerContextAsync(
            MerkabaTileAddress tile, MerkabaTileAddress[] capturedIndex,
            MerkabaStorageAppendPosition position) => Task.Run(() =>
        {
            if (capturedIndex == null) throw new ArgumentNullException(nameof(capturedIndex));
            lock (_ioGate)
            lock (_gate)
                return ReadFlowerContext(tile, capturedIndex, position);
        });

        internal MerkabaTileAddress[] CaptureFlowerSource(out MerkabaStorageAppendPosition position)
        {
            lock (_ioGate)
            lock (_gate)
            {
                position = CaptureAppendPosition();
                RequireFlowerPosition(position);
                return SnapshotSortedAddresses();
            }
        }

        private void RequireFlowerPosition(MerkabaStorageAppendPosition position)
        {
            ThrowIfAppendPublicationFailed();
            if (position.Authority == null || !ReferenceEquals(position.Authority, _appendAuthority) ||
                position.Generation != _pendingGeneration || position.RecordSequence != _recordSequence)
                throw new InvalidOperationException("The frozen Flower export source changed during evaluation.");
        }

        private MerkabaSphereFlowerAuthority.SnapshotReader ReadFlowerContext(
            MerkabaTileAddress tile, MerkabaTileAddress[] capturedIndex, MerkabaStorageAppendPosition position)
        {
            // One bounded context from the existing index; no mesh snapshot or
            // second coordinate authority. Callers hold the existing I/O gate.
            RequireFlowerPosition(position);
            List<MerkabaTileAddress> context = MerkabaGrid.FlowerContextAddresses(tile, capturedIndex);
            var snapshots = new MerkabaTileSnapshot[context.Count];
            for (int i = 0; i < snapshots.Length; i++)
            {
                snapshots[i] = ReadOne(context[i]);
                snapshots[i].Sidecars = _subordinate.CaptureTile(context[i]);
            }
            return new MerkabaSphereFlowerAuthority.SnapshotReader(snapshots, capturedIndex,
                cell => ReadFlowerCell(position, cell));
        }

        private MerkabaSphereFlowerAuthority.ExcavationCellState ReadFlowerCell(
            MerkabaStorageAppendPosition position, int3 cell)
        {
            lock (_gate)
            {
                RequireFlowerPosition(position);
                Span<MerkabaDualReadResult> supports = stackalloc MerkabaDualReadResult[8];
                for (int bit = 0; bit < 8; bit++)
                {
                    long x = (long)cell.x + (bit & 1), y = (long)cell.y + ((bit >> 1) & 1),
                        z = (long)cell.z + ((bit >> 2) & 1);
                    if (x > int.MaxValue || y > int.MaxValue || z > int.MaxValue)
                        return MerkabaSphereFlowerAuthority.ExcavationCellState.Ambiguous;
                    MerkabaSpatial.Address address = MerkabaSpatial.Encode(new int3((int)x, (int)y, (int)z));
                    supports[bit] = _subordinate.ReadDual(new MerkabaTileAddress(address.BlockCoord,
                        address.LocalAddress), address.KernelLocal);
                }
                return MerkabaSphereFlowerAuthority.ClassifyFreeCell(supports);
            }
        }

        internal Task<long> StreamFlowerDirtAsync(float2 planeBounds,
            MerkabaTileAddress[] index, MerkabaStorageAppendPosition position,
            Action<IReadOnlyList<MerkabaDirtTriangle>> consume) => Task.Run(() =>
        {
            if (consume == null) throw new ArgumentNullException(nameof(consume));
            lock (_ioGate)
            lock (_gate)
            {
                if (index == null) throw new ArgumentNullException(nameof(index));
                RequireFlowerPosition(position);
                MerkabaTileAddress previous = default;
                MerkabaFlowerPresentation page = null;
                MerkabaDirtFaceCoverage Coverage(int3 cell, int face)
                {
                    MerkabaSpatial.Address address = MerkabaSpatial.Encode(cell);
                    var tile = new MerkabaTileAddress(address.BlockCoord, address.LocalAddress);
                    if (page == null || !tile.Equals(previous))
                    {
                        previous = tile;
                        var reader = ReadFlowerContext(tile, index, position);
                        page = MerkabaFlowerPresentation.Build(reader, tile, planeBounds);
                    }
                    // This is the actual generated direct footprint of the
                    // same FREE-side page used by live compaction. Unknown
                    // direct evidence is not itself coverage or a DIRT veto.
                    return page.DirtCoverage(cell, face);
                }
                return MerkabaDirtExtraction.Stream(_subordinate, Coverage, consume);
            }
        });

        internal bool HasCommittedSession => File.Exists(ManifestPath);
        internal MerkabaSphereFlowerReplayIndex Subordinate => _subordinate;

        internal MerkabaDualStorageNode[] SnapshotDualNodes(out uint maximumGeneration)
        {
            var nodes = new List<MerkabaDualStorageNode>();
            maximumGeneration = 0u;
            lock (_gate)
            {
                maximumGeneration = _subordinate.MaximumDualGeneration();
                foreach (MerkabaAppendRecord record in _subordinate.CanonicalDualRecords())
                {
                    MerkabaTileAddress address;
                    if (record.Kind == MerkabaRecordKind.DualBlock)
                        address = new MerkabaTileAddress(
                            MerkabaSphereFlowerPersistenceAbi.ReadBlockAddress(record.Address), 0u);
                    else if (record.Kind == MerkabaRecordKind.DualChunk)
                    {
                        MerkabaSphereFlowerPersistenceAbi.ReadChunkAddress(record.Address,
                            out int3 block, out int chunk);
                        address = new MerkabaTileAddress(block, (uint)chunk);
                    }
                    else if (record.Kind == MerkabaRecordKind.DualLeaf)
                        address = MerkabaSphereFlowerPersistenceAbi.ReadTileAddress(record.Address);
                    else continue;
                    nodes.Add(new MerkabaDualStorageNode(record.Kind, address));
                }
            }
            nodes.Sort((left, right) =>
            {
                int kind = left.Kind.CompareTo(right.Kind);
                return kind != 0 ? kind : left.Address.CompareTo(right.Address);
            });
            return nodes.ToArray();
        }

        internal Task<MerkabaTileSnapshot[]> ReadDualNodesAsync(
            IReadOnlyList<MerkabaDualStorageNode> nodes)
        {
            if (nodes == null) throw new ArgumentNullException(nameof(nodes));
            if (nodes.Count > MerkabaGrid.StreamBatchCapacity)
                throw new InvalidDataException("Dual residency batch exceeds 32 nodes.");
            var requested = new MerkabaDualStorageNode[nodes.Count];
            for (int index = 0; index < requested.Length; index++) requested[index] = nodes[index];
            return Task.Run(() =>
            {
                var result = new MerkabaTileSnapshot[requested.Length];
                lock (_ioGate)
                lock (_gate)
                    for (int index = 0; index < result.Length; index++)
                        result[index] = new MerkabaTileSnapshot
                        {
                            Address = requested[index].Address,
                            Sidecars = _subordinate.CaptureDualNode(requested[index])
                        };
                return result;
            });
        }

        internal Task<MerkabaSessionOpenState> OpenCommittedAsync(
            IProgress<OperationWorkProgress> progress = null) =>
            Task.Run(() => OpenCommitted(progress));

        internal MerkabaSessionOpenState OpenCommitted(
            IProgress<OperationWorkProgress> progress = null)
        {
            lock (_ioGate)
            {
                DeleteCompactionTemps();
                MerkabaSessionManifest manifest = ReadManifest(ManifestPath);
                ValidateAvailableRanges(manifest);

                var rebuilt = new Dictionary<MerkabaTileAddress, Location>();
                var replay = new MerkabaSphereFlowerReplayIndex();
                ulong sequence = 0ul;
                long totalBytes = 0L;
                for (int i = 0; i < (int)MerkabaStorageStream.Count; i++)
                    totalBytes = checked(totalBytes + manifest.ValidEnds[i]);
                long completed = 0L;
                for (int i = 0; i < (int)MerkabaStorageStream.Count; i++)
                {
                    var stream = (MerkabaStorageStream)i;
                    long validEnd = manifest.ValidEnd(stream);
                    ScanStream(stream, validEnd, manifest, rebuilt, replay,
                        ref sequence, completed, totalBytes, progress);
                    completed = checked(completed + validEnd);
                }
                if (rebuilt.Count != manifest.CanonicalTileCount)
                    throw new InvalidDataException(
                        "Manifest canonical tile count does not match replay.");
                ulong occupiedKernelCount = 0ul;
                foreach (Location location in rebuilt.Values)
                    occupiedKernelCount = checked(occupiedKernelCount +
                        location.OccupiedKernelCount);
                if (occupiedKernelCount != manifest.OccupiedKernelCount)
                    throw new InvalidDataException(
                        "Manifest occupied-kernel count does not match replay.");
                var replayTileCache = new Dictionary<MerkabaTileAddress,
                    KernelState[]>();
                replay.ValidateClosedHierarchy(owner => IsCanonicalR1Owner(
                    owner, rebuilt, replayTileCache));

                TruncateUncommittedTails(manifest);
                lock (_gate)
                {
                    _index.Clear();
                    _indexedTilesByBlock.Clear();
                    foreach (KeyValuePair<MerkabaTileAddress, Location> pair in
                             rebuilt)
                    {
                        _index.Add(pair.Key, pair.Value);
                        IndexTileBlock(pair.Key);
                    }
                    _subordinate.CopyFrom(replay);
                    _manifest = manifest;
                    _pendingGeneration = NextGeneration(
                        manifest.CommitGeneration);
                    _recordSequence = sequence;
                    _indexedOccupiedKernelCount = occupiedKernelCount;
                    _dirtyStreams.Clear();
                    _dirtyM8Tiles.Clear();
                    _dirtyDualBlocks.Clear();
                    _dirtyFineOwners.Clear();
                    _appendAuthority = new object();
                    _appendPublicationFailure = null;
                }
                progress?.Report(new OperationWorkProgress(
                    ScanOperationStage.RebuildingStorageIndex, totalBytes,
                    totalBytes, $"Indexed {rebuilt.Count} committed M8 tiles"));
                return new MerkabaSessionOpenState(
                    manifest.CloneForSession(manifest.SessionUuid),
                    rebuilt.Count);
            }
        }

        internal static MerkabaSessionManifest ReadManifestFromDirectory(
            string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("Session directory is required.",
                    nameof(directory));
            return ReadManifest(Path.Combine(Path.GetFullPath(directory),
                ManifestFileName));
        }

        internal MerkabaSessionOpenState CurrentCommittedState()
        {
            lock (_gate)
            {
                if (_manifest == null)
                    throw new InvalidOperationException(
                        "The storage root has no committed manifest.");
                return new MerkabaSessionOpenState(
                    _manifest.CloneForSession(_manifest.SessionUuid),
                    _index.Count);
            }
        }

        internal Task AppendM8TilesAsync(
            IReadOnlyList<MerkabaTileSnapshot> tiles) =>
            AppendObservationBatchAsync(tiles, Array.Empty<MerkabaAppendRecord>());

        internal void AppendM8Tiles(IReadOnlyList<MerkabaTileSnapshot> tiles) =>
            AppendObservationBatch(tiles, Array.Empty<MerkabaAppendRecord>());

        internal Task AppendSphereFlowerRecordsAsync(
            IReadOnlyList<MerkabaAppendRecord> records) =>
            AppendObservationBatchAsync(Array.Empty<MerkabaTileSnapshot>(), records);

        internal void AppendSphereFlowerRecords(
            IReadOnlyList<MerkabaAppendRecord> records) =>
            AppendObservationBatch(Array.Empty<MerkabaTileSnapshot>(), records);

        internal Task AppendObservationBatchAsync(
            IReadOnlyList<MerkabaTileSnapshot> tiles,
            IReadOnlyList<MerkabaAppendRecord> sidecars,
            bool completeFineImages = false) =>
            Task.Run(() => AppendObservationBatch(tiles, sidecars, completeFineImages));

        // One publication boundary for M8, sparse dual, owner epochs, metric
        // detail and ThreadAtlas. Consumers never observe M8 from this batch
        // without its corresponding structural invalidation / fine records.
        internal void AppendObservationBatch(
            IReadOnlyList<MerkabaTileSnapshot> tiles,
            IReadOnlyList<MerkabaAppendRecord> sidecars,
            bool completeFineImages = false)
        {
            if (tiles == null) throw new ArgumentNullException(nameof(tiles));
            if (sidecars == null) throw new ArgumentNullException(nameof(sidecars));
            if (tiles.Count > MerkabaGrid.StreamBatchCapacity)
                throw new InvalidDataException("M8 writeback batch exceeds 32 tiles.");
            if (tiles.Count == 0 && sidecars.Count == 0) return;
            var tileSet = new HashSet<MerkabaTileAddress>();
            foreach (MerkabaTileSnapshot tile in tiles)
            {
                ValidateTile(tile);
                if (!tileSet.Add(tile.Address))
                    throw new InvalidDataException("Duplicate tile in writeback batch.");
            }
            var capturedEpochs = new Dictionary<MerkabaOwnerAddress, MerkabaAppendRecord>();
            if (completeFineImages)
                foreach (MerkabaTileSnapshot tile in tiles)
                    foreach (MerkabaAppendRecord captured in tile.Sidecars)
                    {
                        if (!captured.OwnerEpochSnapshot) continue;
                        MerkabaSphereFlowerReplayIndex.ValidateRecord(captured);
                        MerkabaTileAddress address =
                            MerkabaSphereFlowerPersistenceAbi.ReadTileAddress(captured.Address);
                        int kernel = checked((int)MerkabaSphereFlowerPersistenceAbi.ReadUInt32(
                            captured.Payload, 0));
                        if (!address.Equals(tile.Address) ||
                            !capturedEpochs.TryAdd(new MerkabaOwnerAddress(address, kernel), captured))
                            throw new InvalidDataException("Complete tile image has a duplicate or foreign owner epoch.");
                    }
            var byStream = new SortedDictionary<MerkabaStorageStream,
                List<MerkabaAppendRecord>>();
            foreach (MerkabaAppendRecord input in sidecars)
            {
                if (input == null) throw new ArgumentException(
                    "Append record is null.", nameof(sidecars));
                if (input.Kind == MerkabaRecordKind.M8Tile)
                    throw new ArgumentException(
                        "M8 state belongs in the tile batch.", nameof(sidecars));
                var record = new MerkabaAppendRecord(input.Kind,
                    (byte[])input.Address.Clone(), (byte[])input.Payload.Clone(),
                    input.OwnerEpochRebased, input.OwnerEpochSnapshot);
                if (record.OwnerEpochRebased && !completeFineImages)
                    throw new InvalidDataException("Epoch rebase requires a complete frozen fine image.");
                if (record.OwnerEpochSnapshot)
                {
                    var owner = new MerkabaOwnerAddress(
                        MerkabaSphereFlowerPersistenceAbi.ReadTileAddress(record.Address),
                        checked((int)MerkabaSphereFlowerPersistenceAbi.ReadUInt32(record.Payload, 0)));
                    if (!completeFineImages || !capturedEpochs.TryGetValue(owner, out var captured) ||
                        record.OwnerEpochRebased != captured.OwnerEpochRebased ||
                        !record.Payload.AsSpan().SequenceEqual(captured.Payload))
                        throw new InvalidDataException(
                            "Owner epoch snapshot requires its completed frozen M8/fine tile image.");
                    capturedEpochs.Remove(owner);
                }
                MerkabaStorageStream stream = StreamFor(record);
                if (!byStream.TryGetValue(stream, out List<MerkabaAppendRecord> list))
                    byStream.Add(stream, list = new List<MerkabaAppendRecord>());
                list.Add(record);
            }
            if (capturedEpochs.Count != 0)
                throw new InvalidDataException(
                    "Complete fine image omitted an owner epoch from its append batch.");
            var orderedSidecars = new List<MerkabaAppendRecord>(sidecars.Count);
            foreach (List<MerkabaAppendRecord> list in byStream.Values)
                orderedSidecars.AddRange(list);

            lock (_ioGate)
            {
                if (completeFineImages)
                {
                    var rebasePrefix = new List<MerkabaAppendRecord>();
                    // Capture is complete only after every bounded GPU fine
                    // packet has retired. Missing same-epoch records become
                    // explicit log deletions, never resurrected replay tails.
                    lock (_gate)
                    {
                        foreach (MerkabaTileSnapshot tile in tiles)
                        {
                            foreach (MerkabaAppendRecord record in tile.Sidecars)
                                if (record.OwnerEpochRebased)
                                    _subordinate.AppendOwnerRebaseRecords(tile.Address,
                                        checked((int)MerkabaSphereFlowerPersistenceAbi.ReadUInt32(
                                            record.Payload, 0)), rebasePrefix);
                            MerkabaFlowerPageStorage.AppendRemovedRecordTombstones(
                                tile.Address, _subordinate.CaptureTile(tile.Address),
                                tile.Sidecars, orderedSidecars);
                        }
                    }
                    orderedSidecars.Sort((left, right) => left.Kind.CompareTo(right.Kind));
                    // Tombstone the complete pre-wrap history before any
                    // re-observed current-epoch values, including Thread data.
                    orderedSidecars.InsertRange(0, rebasePrefix);
                    byStream.Clear();
                    foreach (MerkabaAppendRecord record in orderedSidecars)
                    {
                        MerkabaStorageStream target = StreamFor(record);
                        if (!byStream.TryGetValue(target, out List<MerkabaAppendRecord> list))
                            byStream.Add(target, list = new List<MerkabaAppendRecord>());
                        list.Add(record);
                    }
                    orderedSidecars.Clear();
                    foreach (List<MerkabaAppendRecord> list in byStream.Values)
                        orderedSidecars.AddRange(list);
                }
                Directory.CreateDirectory(_directory);
                ulong finalSequence = checked(_recordSequence +
                    (ulong)tiles.Count + (ulong)orderedSidecars.Count);
                var priorLengths = new Dictionary<MerkabaStorageStream, long>();
                var pending = new List<PendingM8Location>(tiles.Count);
                object publishedAuthority = new object();
                object precedingAuthority;
                lock (_gate)
                {
                    ThrowIfAppendPublicationFailed();
                    if (_appendAuthority == null)
                        throw new InvalidOperationException(
                            "Another append is still being published.");
                    _subordinate.ValidateAppendBatch(orderedSidecars,
                        _pendingGeneration, checked(_recordSequence + (ulong)tiles.Count));
                    precedingAuthority = _appendAuthority;
                    // A capture racing this un-published write is not a usable
                    // append prefix. On failure, only a complete tail rollback
                    // can make the preceding prefix valid again.
                    _appendAuthority = null;
                }
                ulong sequence = _recordSequence;
                try
                {
                    if (tiles.Count != 0)
                    {
                        long original = PrepareAppendEnd(MerkabaStorageStream.M8Live);
                        priorLengths.Add(MerkabaStorageStream.M8Live, original);
                        using var stream = OpenAppendAtExactEnd(M8LivePath, original);
                        foreach (MerkabaTileSnapshot tile in tiles)
                        {
                            byte[] address = new byte[
                                MerkabaSphereFlowerPersistenceAbi.TileAddressBytes];
                            MerkabaSphereFlowerPersistenceAbi.WriteTileAddress(address,
                                tile.Address);
                            var record = new MerkabaAppendRecord(MerkabaRecordKind.M8Tile,
                                address, EncodeStates(tile.States));
                            long payloadOffset = WriteRecord(stream, record,
                                _pendingGeneration);
                            pending.Add(new PendingM8Location(tile, new Location(
                                M8LivePath, payloadOffset, _pendingGeneration,
                                ++sequence, CountOccupied(tile.States))));
                        }
                        stream.Flush();
                    }
                    foreach (KeyValuePair<MerkabaStorageStream,
                                 List<MerkabaAppendRecord>> pair in byStream)
                    {
                        long original = PrepareAppendEnd(pair.Key);
                        priorLengths.Add(pair.Key, original);
                        using var stream = OpenAppendAtExactEnd(PathFor(pair.Key), original);
                        foreach (MerkabaAppendRecord record in pair.Value)
                            WriteRecord(stream, record, _pendingGeneration);
                        stream.Flush();
                    }
                }
                catch (Exception appendFailure)
                {
                    // No index has been published yet. Restore only the exact
                    // touched stream tails; the committed manifest is untouched.
                    Exception rollbackFailure = null;
                    foreach (KeyValuePair<MerkabaStorageStream, long> prior in priorLengths)
                    {
                        try { TruncateFile(PathFor(prior.Key), prior.Value); }
                        catch (Exception failure) { rollbackFailure ??= failure; }
                    }
                    if (rollbackFailure != null)
                    {
                        var failure = new IOException(
                            "Append rollback failed; the store remains closed to publication until OPEN or Clear.",
                            new AggregateException(appendFailure, rollbackFailure));
                        lock (_gate) _appendPublicationFailure = failure;
                        throw failure;
                    }
                    lock (_gate) _appendAuthority = precedingAuthority;
                    throw;
                }

                lock (_gate)
                {
                    try
                    {
                        foreach (MerkabaAppendRecord record in orderedSidecars)
                            _subordinate.Apply(record, new MerkabaRecordVersion(
                                _pendingGeneration, ++sequence));
                        foreach (PendingM8Location update in pending)
                        {
                            if (_index.TryGetValue(update.Tile.Address, out Location previous))
                                _indexedOccupiedKernelCount -= previous.OccupiedKernelCount;
                            _index[update.Tile.Address] = update.Location;
                            _indexedOccupiedKernelCount = checked(_indexedOccupiedKernelCount +
                                update.Location.OccupiedKernelCount);
                            IndexTileBlock(update.Tile.Address);
                            _dirtyM8Tiles.Add(update.Tile.Address);
                            update.Tile.Generation = update.Location.Generation;
                        }
                        foreach (MerkabaStorageStream stream in priorLengths.Keys)
                            _dirtyStreams.Add(stream);
                        MerkabaSphereFlowerReplayIndex.AccumulateTouched(orderedSidecars,
                            _dirtyDualBlocks, _dirtyFineOwners);
                        _recordSequence = finalSequence;
                        _appendAuthority = publishedAuthority;
                    }
                    catch (Exception failure)
                    {
                        _appendPublicationFailure = failure;
                        throw;
                    }
                }
            }
        }

        internal MerkabaStorageAppendPosition CaptureAppendPosition()
        {
            lock (_gate)
            {
                ThrowIfAppendPublicationFailed();
                return new MerkabaStorageAppendPosition(_appendAuthority,
                    _pendingGeneration, _recordSequence);
            }
        }

        internal Task<MerkabaStorageCommitResult> CommitAsync(
            MerkabaStorageAppendPosition expectedPosition,
            Guid sessionUuid, Guid anchorUuid,
            UnityEngine.Matrix4x4 anchorAtSave, int integrationCount,
            uint occupiedKernelCount,
            IProgress<OperationWorkProgress> progress = null,
            Action<MerkabaCommitStage> crashProbe = null) => Task.Run(() =>
                Commit(expectedPosition, sessionUuid, anchorUuid, anchorAtSave,
                    integrationCount, occupiedKernelCount, progress, crashProbe));

        internal MerkabaStorageCommitResult Commit(
            MerkabaStorageAppendPosition expectedPosition,
            Guid sessionUuid, Guid anchorUuid,
            UnityEngine.Matrix4x4 anchorAtSave, int integrationCount,
            uint occupiedKernelCount,
            IProgress<OperationWorkProgress> progress = null,
            Action<MerkabaCommitStage> crashProbe = null)
        {
            // All append/publication paths use this same I/O lock. Check before
            // flushing or writing a manifest, and keep the lock through publish:
            // a later CPU append cannot be accidentally included in this cut.
            // A later GPU quantum is independent and may continue meanwhile.
            lock (_ioGate)
            {
                lock (_gate)
                {
                    ThrowIfAppendPublicationFailed();
                    if (expectedPosition.Authority == null ||
                        !ReferenceEquals(expectedPosition.Authority,
                            _appendAuthority) ||
                        expectedPosition.Generation != _pendingGeneration ||
                        expectedPosition.RecordSequence != _recordSequence)
                        return default;
                }
                return Commit(sessionUuid, anchorUuid, anchorAtSave,
                    integrationCount, occupiedKernelCount, progress, crashProbe);
            }
        }

        internal Task<MerkabaStorageCommitResult> CommitAsync(Guid sessionUuid,
            Guid anchorUuid, UnityEngine.Matrix4x4 anchorAtSave,
            int integrationCount, uint occupiedKernelCount,
            IProgress<OperationWorkProgress> progress = null,
            Action<MerkabaCommitStage> crashProbe = null) => Task.Run(() =>
                Commit(sessionUuid, anchorUuid, anchorAtSave, integrationCount,
                    occupiedKernelCount, progress, crashProbe));

        internal MerkabaStorageCommitResult Commit(Guid sessionUuid,
            Guid anchorUuid, UnityEngine.Matrix4x4 anchorAtSave,
            int integrationCount, uint occupiedKernelCount,
            IProgress<OperationWorkProgress> progress = null,
            Action<MerkabaCommitStage> crashProbe = null)
        {
            if (sessionUuid == Guid.Empty || anchorUuid == Guid.Empty)
                throw new ArgumentException(
                    "Commit requires session and anchor UUIDs.");
            if (integrationCount < 0)
                throw new ArgumentOutOfRangeException(nameof(integrationCount));
            lock (_ioGate)
            {
                lock (_gate)
                {
                    ThrowIfAppendPublicationFailed();
                    if (_appendAuthority == null)
                        throw new InvalidDataException(
                            "An incomplete append cannot be committed; reopen the committed store.");
                    if (_manifest != null &&
                        (_manifest.SessionUuid != sessionUuid ||
                         _manifest.AnchorUuid != anchorUuid))
                        throw new InvalidOperationException(
                            "A committed storage root cannot be relabeled with " +
                            "another session or anchor identity; use an exact " +
                            "session clone for SAVE AS.");
                }
                Directory.CreateDirectory(_directory);
                MerkabaTileAddress[] dirtyM8Tiles;
                int3[] dirtyDualBlocks;
                MerkabaOwnerAddress[] dirtyFineOwners;
                lock (_gate)
                {
                    dirtyM8Tiles = new List<MerkabaTileAddress>(
                        _dirtyM8Tiles).ToArray();
                    dirtyDualBlocks = new List<int3>(
                        _dirtyDualBlocks).ToArray();
                    dirtyFineOwners = new List<MerkabaOwnerAddress>(
                        _dirtyFineOwners).ToArray();
                }
                var replayTileCache = new Dictionary<MerkabaTileAddress,
                    KernelState[]>();
                _subordinate.ValidateDualBlocks(dirtyDualBlocks);
                _subordinate.ValidateFineOwners(dirtyFineOwners,
                    owner => IsCanonicalR1Owner(owner, _index,
                        replayTileCache));
                _subordinate.ValidateFineOwnersInTiles(dirtyM8Tiles,
                    owner => IsCanonicalR1Owner(owner, _index,
                        replayTileCache));
                crashProbe?.Invoke(MerkabaCommitStage.BeforeDataFlush);

                MerkabaStorageStream[] dirty;
                long oldCommittedBytes;
                lock (_gate)
                {
                    if (_indexedOccupiedKernelCount != occupiedKernelCount)
                        throw new InvalidDataException(
                            "GPU occupied-kernel count differs from dirty M8 " +
                            "storage authority.");
                    dirty = new MerkabaStorageStream[_dirtyStreams.Count];
                    _dirtyStreams.CopyTo(dirty);
                    oldCommittedBytes = CommittedByteCount(_manifest);
                }
                Array.Sort(dirty);
                for (int i = 0; i < dirty.Length; i++)
                {
                    FlushFileDurable(PathFor(dirty[i]));
                    progress?.Report(new OperationWorkProgress(
                        ScanOperationStage.WritingFile, i + 1, dirty.Length,
                        $"Flushed {i + 1}/{dirty.Length} dirty streams"));
                }
                if (dirty.Length == 0)
                    progress?.Report(new OperationWorkProgress(
                        ScanOperationStage.WritingFile, 0, 0,
                        "No dirty stream bytes"));
                crashProbe?.Invoke(MerkabaCommitStage.AfterDataFlush);

                var manifest = new MerkabaSessionManifest
                {
                    SessionUuid = sessionUuid,
                    AnchorUuid = anchorUuid,
                    AnchorAtSave = anchorAtSave,
                    CommitGeneration = _pendingGeneration,
                    IntegrationCount = integrationCount,
                    OccupiedKernelCount = occupiedKernelCount,
                    CanonicalTileCount = checked((uint)IndexedTileCount),
                    M8BaseGeneration = _manifest?.M8BaseGeneration ?? 0ul,
                    ThroughBaseGeneration =
                        _manifest?.ThroughBaseGeneration ?? 0ul
                };
                for (int i = 0; i < (int)MerkabaStorageStream.Count; i++)
                    manifest.ValidEnds[i] = FileLength(PathFor(
                        (MerkabaStorageStream)i));
                manifest.Validate();
                ulong followingGeneration = NextGeneration(
                    manifest.CommitGeneration);

                string temporary = ManifestPath + ".tmp";
                using (var stream = new FileStream(temporary, FileMode.Create,
                           FileAccess.Write, FileShare.None, 16 * 1024,
                           FileOptions.SequentialScan))
                {
                    MerkabaSessionManifest.Write(stream, manifest);
                    stream.Flush(true);
                }
                crashProbe?.Invoke(MerkabaCommitStage.AfterManifestFlush);
                progress?.Report(new OperationWorkProgress(
                    ScanOperationStage.PublishingFile, 0, 1,
                    "Publishing generation manifest"));
                try
                {
                    MerkabaFilePublishing.Publish(temporary, ManifestPath);
                }
                catch
                {
                    // rename succeeded but directory fsync may have reported an
                    // error. Keep this process aligned with the manifest that is
                    // now visible; the caller still receives the durability error.
                    if (ManifestMatches(ManifestPath, manifest))
                        AdoptCommittedManifest(manifest, followingGeneration);
                    throw;
                }
                AdoptCommittedManifest(manifest, followingGeneration);
                crashProbe?.Invoke(MerkabaCommitStage.AfterManifestPublish);

                long newCommittedBytes = CommittedByteCount(manifest);
                progress?.Report(new OperationWorkProgress(
                    ScanOperationStage.PublishingFile, 1, 1,
                    "Committed generation published"));
                return new MerkabaStorageCommitResult(
                    manifest.CommitGeneration, IndexedTileCount,
                    Math.Max(0L, newCommittedBytes - oldCommittedBytes));
            }
        }

        /// <summary>
        /// Idle-only physical maintenance. Complete deterministic images are
        /// prepared first. A recovery manifest temporarily makes complete live
        /// images authoritative, so the two fixed base files can then be replaced
        /// without any crash interval in which the published world is unreadable.
        /// The final manifest selects the new bases and excludes both live logs.
        /// SAVE only requests this later maintenance; it never awaits or performs
        /// the complete-world rewrite.
        /// </summary>
        internal Task<bool> CompactCommittedBasesAsync(
            CancellationToken cancellationToken = default,
            Action<MerkabaCompactionStage> crashProbe = null) => Task.Run(() =>
                CompactCommittedBases(crashProbe, cancellationToken),
                cancellationToken);

        internal bool CompactCommittedBases(
            Action<MerkabaCompactionStage> crashProbe = null,
            CancellationToken cancellationToken = default)
        {
            lock (_ioGate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                MerkabaSessionManifest committed;
                MerkabaTileAddress[] addresses;
                long canonicalDualBytes;
                ulong sequenceBefore;
                lock (_gate)
                {
                    ThrowIfAppendPublicationFailed();
                    if (_manifest == null)
                        throw new InvalidOperationException(
                            "Only a committed session can be compacted.");
                    if (_dirtyStreams.Count != 0 || _dirtyM8Tiles.Count != 0 ||
                        _dirtyDualBlocks.Count != 0 ||
                        _dirtyFineOwners.Count != 0)
                        throw new InvalidOperationException(
                            "Base compaction is permitted only for a clean " +
                            "committed generation.");
                    committed = _manifest.CloneForSession(
                        _manifest.SessionUuid);
                    addresses = new List<MerkabaTileAddress>(_index.Keys)
                        .ToArray();
                    canonicalDualBytes = _subordinate.CanonicalDualRecordBytes;
                    sequenceBefore = _recordSequence;
                }
                Array.Sort(addresses);
                ValidateAvailableRanges(committed);
                TruncateUncommittedTails(committed);
                DeleteCompactionTemps();

                long canonicalM8Bytes = checked((long)addresses.Length *
                    EncodedM8RecordBytes);
                long m8LiveBytes = committed.ValidEnd(
                    MerkabaStorageStream.M8Live);
                long dualLiveBytes = committed.ValidEnd(
                    MerkabaStorageStream.ThroughLive);
                bool establishM8Base = committed.ValidEnd(
                    MerkabaStorageStream.M8Base) == 0L && committed.ValidEnd(
                    MerkabaStorageStream.M8Live) != 0L;
                bool establishDualBase = committed.ValidEnd(
                    MerkabaStorageStream.ThroughBase) == 0L && committed.ValidEnd(
                    MerkabaStorageStream.ThroughLive) != 0L;
                bool reduceM8History = m8LiveBytes > canonicalM8Bytes;
                bool reduceDualHistory = dualLiveBytes > canonicalDualBytes;
                if (!establishM8Base && !establishDualBase &&
                    !reduceM8History && !reduceDualHistory)
                    return false;

                string m8Prepared = M8BasePath + ".tmp";
                string dualPrepared = ThroughBasePath + ".tmp";
                long originalM8Live = committed.ValidEnd(
                    MerkabaStorageStream.M8Live);
                long originalDualLive = committed.ValidEnd(
                    MerkabaStorageStream.ThroughLive);
                ulong preparedSequence = sequenceBefore;
                ulong recoverySequence = sequenceBefore;
                Location[] preparedM8Locations = null;
                Location[] recoveryM8Locations = null;
                bool recoveryPublished = false;
                bool finalPublished = false;
                try
                {
                    preparedM8Locations = WriteCanonicalM8File(m8Prepared,
                        M8BasePath, addresses, committed.CommitGeneration,
                        ref preparedSequence, cancellationToken);
                    WriteCanonicalDualFile(dualPrepared,
                        committed.CommitGeneration, ref preparedSequence,
                        cancellationToken);
                    if (FileLength(m8Prepared) != canonicalM8Bytes ||
                        FileLength(dualPrepared) != canonicalDualBytes)
                        throw new InvalidDataException(
                            "Canonical base image byte count is inconsistent.");

                    using (FileStream m8Live = OpenAppendAtExactEnd(M8LivePath,
                               originalM8Live))
                    {
                        recoveryM8Locations = WriteCanonicalM8Image(m8Live,
                            M8LivePath, addresses, committed.CommitGeneration,
                            ref recoverySequence, cancellationToken);
                        m8Live.Flush();
                    }
                    using (FileStream dualLive = OpenAppendAtExactEnd(
                               ThroughLivePath, originalDualLive))
                    {
                        WriteCanonicalDualImage(dualLive,
                            committed.CommitGeneration, ref recoverySequence,
                            cancellationToken);
                        dualLive.Flush();
                    }
                    if (FileLength(M8LivePath) != checked(originalM8Live +
                            canonicalM8Bytes) ||
                        FileLength(ThroughLivePath) != checked(originalDualLive +
                            canonicalDualBytes))
                        throw new InvalidDataException(
                            "Recovery live image byte count is inconsistent.");
                    cancellationToken.ThrowIfCancellationRequested();
                    crashProbe?.Invoke(
                        MerkabaCompactionStage.BeforeRecoveryDataFlush);
                    FlushFileDurable(m8Prepared);
                    cancellationToken.ThrowIfCancellationRequested();
                    FlushFileDurable(dualPrepared);
                    cancellationToken.ThrowIfCancellationRequested();
                    FlushFileDurable(M8LivePath);
                    cancellationToken.ThrowIfCancellationRequested();
                    FlushFileDurable(ThroughLivePath);
                    crashProbe?.Invoke(
                        MerkabaCompactionStage.AfterRecoveryDataFlush);
                    cancellationToken.ThrowIfCancellationRequested();

                    MerkabaSessionManifest recovery = committed.CloneForSession(
                        committed.SessionUuid);
                    recovery.M8BaseGeneration = 0ul;
                    recovery.ThroughBaseGeneration = 0ul;
                    recovery.ValidEnds[(int)MerkabaStorageStream.M8Base] = 0L;
                    recovery.ValidEnds[(int)MerkabaStorageStream.ThroughBase] = 0L;
                    recovery.ValidEnds[(int)MerkabaStorageStream.M8Live] =
                        FileLength(M8LivePath);
                    recovery.ValidEnds[(int)MerkabaStorageStream.ThroughLive] =
                        FileLength(ThroughLivePath);
                    recovery.Validate();
                    WriteManifestTemporary(recovery);
                    crashProbe?.Invoke(
                        MerkabaCompactionStage.AfterRecoveryManifestFlush);
                    try
                    {
                        MerkabaFilePublishing.Publish(ManifestPath + ".tmp",
                            ManifestPath);
                        recoveryPublished = true;
                    }
                    catch
                    {
                        recoveryPublished = ManifestMatches(ManifestPath,
                            recovery);
                        if (recoveryPublished)
                            AdoptCompactionManifest(recovery, addresses,
                                recoveryM8Locations, recoverySequence);
                        throw;
                    }
                    AdoptCompactionManifest(recovery, addresses,
                        recoveryM8Locations, recoverySequence);
                    crashProbe?.Invoke(
                        MerkabaCompactionStage.AfterRecoveryManifestPublish);

                    // From this point the published recovery manifest ignores
                    // both base files. Replacing either file cannot invalidate it.
                    MerkabaFilePublishing.Publish(m8Prepared, M8BasePath);
                    crashProbe?.Invoke(MerkabaCompactionStage.AfterM8BasePublish);
                    MerkabaFilePublishing.Publish(dualPrepared, ThroughBasePath);
                    crashProbe?.Invoke(
                        MerkabaCompactionStage.AfterThroughBasePublish);

                    MerkabaSessionManifest compacted = recovery.CloneForSession(
                        recovery.SessionUuid);
                    compacted.M8BaseGeneration = committed.CommitGeneration;
                    compacted.ThroughBaseGeneration = committed.CommitGeneration;
                    compacted.ValidEnds[(int)MerkabaStorageStream.M8Base] =
                        canonicalM8Bytes;
                    compacted.ValidEnds[(int)MerkabaStorageStream.ThroughBase] =
                        canonicalDualBytes;
                    compacted.ValidEnds[(int)MerkabaStorageStream.M8Live] = 0L;
                    compacted.ValidEnds[(int)MerkabaStorageStream.ThroughLive] = 0L;
                    compacted.Validate();
                    WriteManifestTemporary(compacted);
                    crashProbe?.Invoke(
                        MerkabaCompactionStage.AfterFinalManifestFlush);
                    try
                    {
                        MerkabaFilePublishing.Publish(ManifestPath + ".tmp",
                            ManifestPath);
                        finalPublished = true;
                    }
                    catch
                    {
                        finalPublished = ManifestMatches(ManifestPath, compacted);
                        if (finalPublished)
                            AdoptCompactionManifest(compacted, addresses,
                                preparedM8Locations, preparedSequence);
                        throw;
                    }
                    AdoptCompactionManifest(compacted, addresses,
                        preparedM8Locations, preparedSequence);
                    crashProbe?.Invoke(
                        MerkabaCompactionStage.AfterFinalManifestPublish);

                    // Logical authority already excludes these bytes. Failure to
                    // trim affects space only and OPEN retries the truncation.
                    TryTruncateIgnoredTail(M8LivePath);
                    TryTruncateIgnoredTail(ThroughLivePath);
                    return true;
                }
                catch
                {
                    if (!recoveryPublished && !finalPublished)
                    {
                        TruncateFile(M8LivePath, originalM8Live);
                        TruncateFile(ThroughLivePath, originalDualLive);
                    }
                    DeleteCompactionTemps();
                    throw;
                }
            }
        }

        internal Task CloneCommittedToAsync(string destinationDirectory,
            Guid destinationSessionUuid) => Task.Run(() => CloneCommittedTo(
                destinationDirectory, destinationSessionUuid));

        internal void CloneCommittedTo(string destinationDirectory,
            Guid destinationSessionUuid)
        {
            if (destinationSessionUuid == Guid.Empty)
                throw new ArgumentException("Destination session UUID is required.",
                    nameof(destinationSessionUuid));
            string destination = Path.GetFullPath(destinationDirectory ??
                throw new ArgumentNullException(nameof(destinationDirectory)));
            if (string.Equals(destination, _directory,
                    StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "A session cannot be cloned onto itself.");
            lock (_ioGate)
            {
                MerkabaSessionManifest source;
                lock (_gate) source = _manifest;
                if (source == null)
                    throw new InvalidOperationException(
                        "Only a committed session may be cloned.");
                Directory.CreateDirectory(destination);
                DeleteStorageFiles(destination);
                for (int i = 0; i < (int)MerkabaStorageStream.Count; i++)
                {
                    var stream = (MerkabaStorageStream)i;
                    long bytes = source.ValidEnd(stream);
                    if (bytes == 0L) continue;
                    CopyPrefixDurable(PathFor(stream), Path.Combine(destination,
                        FileName(stream)), bytes);
                }
                MerkabaSessionManifest clone = source.CloneForSession(
                    destinationSessionUuid);
                string manifest = Path.Combine(destination, ManifestFileName);
                string temporary = manifest + ".tmp";
                using (var output = new FileStream(temporary, FileMode.Create,
                           FileAccess.Write, FileShare.None, 16 * 1024,
                           FileOptions.SequentialScan))
                {
                    MerkabaSessionManifest.Write(output, clone);
                    output.Flush(true);
                }
                MerkabaFilePublishing.Publish(temporary, manifest);
            }
        }

        internal Task<MerkabaTileSnapshot[]> ReadAsync(
            IReadOnlyList<MerkabaTileAddress> addresses) => Task.Run(() =>
        {
            if (addresses == null) throw new ArgumentNullException(
                nameof(addresses));
            if (addresses.Count > MerkabaGrid.StreamBatchCapacity)
                throw new InvalidDataException("M8 load batch exceeds 32 tiles.");
            var result = new MerkabaTileSnapshot[addresses.Count];
            lock (_ioGate)
            {
                for (int i = 0; i < addresses.Count; i++)
                {
                    result[i] = ReadOne(addresses[i]);
                    lock (_gate)
                        result[i].Sidecars = _subordinate.CaptureTile(addresses[i]);
                }
            }
            return result;
        });

        internal MerkabaTileAddress[] SnapshotSortedAddresses()
        {
            List<MerkabaTileAddress> addresses;
            lock (_gate) addresses = new List<MerkabaTileAddress>(_index.Keys);
            addresses.Sort();
            return addresses.ToArray();
        }

        internal bool TryFindNearestStoredTile(float3 gridPosition,
            out float3 closestGridPosition, out float distance)
        {
            closestGridPosition = gridPosition;
            distance = 0f;
            float3 bestGridPosition = gridPosition;
            float bestDistanceSquared = float.PositiveInfinity;
            MerkabaTileAddress bestAddress = default;
            bool found = false;
            lock (_gate)
            {
                foreach (KeyValuePair<int3, HashSet<MerkabaTileAddress>> block in
                         _indexedTilesByBlock)
                {
                    if (found && BlockDistanceSquared(gridPosition, block.Key) >
                        bestDistanceSquared) continue;
                    foreach (MerkabaTileAddress address in block.Value)
                    {
                        int3 tileOrigin = MerkabaSpatial.Decode(
                            address.BlockCoord, address.LocalAddress, 0);
                        float3 minimum = (float3)tileOrigin *
                            MerkabaConstants.LatticeStep;
                        float3 maximum = minimum + MerkabaSpatial.TileSize *
                            MerkabaConstants.LatticeStep;
                        float3 candidate = math.clamp(gridPosition, minimum,
                            maximum);
                        float candidateDistance = math.lengthsq(candidate -
                            gridPosition);
                        if (found && (candidateDistance > bestDistanceSquared ||
                            (candidateDistance == bestDistanceSquared &&
                             address.CompareTo(bestAddress) >= 0))) continue;
                        found = true;
                        bestAddress = address;
                        bestDistanceSquared = candidateDistance;
                        bestGridPosition = candidate;
                    }
                }
            }
            if (!found) return false;
            closestGridPosition = bestGridPosition;
            distance = math.sqrt(bestDistanceSquared);
            return true;
        }

        internal void Clear()
        {
            lock (_ioGate)
            {
                DeleteStorageFiles(_directory);
                lock (_gate)
                {
                    _index.Clear();
                    _indexedTilesByBlock.Clear();
                    _subordinate.Clear();
                    _dirtyStreams.Clear();
                    _dirtyM8Tiles.Clear();
                    _dirtyDualBlocks.Clear();
                    _dirtyFineOwners.Clear();
                    _manifest = null;
                    _pendingGeneration = 1ul;
                    _recordSequence = 0ul;
                    _indexedOccupiedKernelCount = 0ul;
                    _appendAuthority = new object();
                    _appendPublicationFailure = null;
                }
            }
        }

        private void ScanStream(MerkabaStorageStream storageStream,
            long validEnd, MerkabaSessionManifest manifest,
            Dictionary<MerkabaTileAddress, Location> rebuilt,
            MerkabaSphereFlowerReplayIndex replay, ref ulong sequence,
            long completedBefore, long totalBytes,
            IProgress<OperationWorkProgress> progress)
        {
            if (validEnd == 0L) return;
            string path = PathFor(storageStream);
            using var stream = new FileStream(path, FileMode.Open,
                FileAccess.Read, FileShare.Read, 1024 * 1024,
                FileOptions.SequentialScan);
            ulong previousGeneration = 0ul;
            int records = 0;
            while (stream.Position < validEnd)
            {
                long recordOffset = stream.Position;
                byte[] headerBytes = ReadExact(stream,
                    MerkabaRecordHeader.ByteSize);
                if (!MerkabaSphereFlowerPersistenceAbi.TryReadHeader(
                        headerBytes, out MerkabaRecordHeader header))
                    throw new InvalidDataException(
                        $"Invalid record header in {FileName(storageStream)}.");
                long recordEnd = checked(stream.Position +
                    header.AddressBytes + header.PayloadBytes);
                if (recordEnd > validEnd ||
                    header.CommitGeneration > manifest.CommitGeneration ||
                    header.CommitGeneration < previousGeneration)
                    throw new InvalidDataException(
                        $"Invalid committed record range in " +
                        FileName(storageStream) + ".");
                ValidateStreamGeneration(storageStream,
                    header.CommitGeneration, manifest);
                previousGeneration = header.CommitGeneration;
                byte[] address = ReadExact(stream, checked((int)
                    header.AddressBytes));
                long payloadOffset = stream.Position;
                byte[] payload = ReadExact(stream, checked((int)
                    header.PayloadBytes));
                if (!MerkabaSphereFlowerPersistenceAbi.ValidateRecord(header,
                        address, payload))
                    throw new InvalidDataException(
                        $"Record CRC/shape mismatch in " +
                        FileName(storageStream) + ".");
                var record = new MerkabaAppendRecord(
                    (MerkabaRecordKind)header.Kind, address, payload,
                    // A durable manifest is published only after provenance
                    // and hierarchy validation. Restore that receipt after
                    // validating this exact committed CRC-bound log prefix.
                    ownerEpochSnapshot: (MerkabaRecordKind)header.Kind ==
                        MerkabaRecordKind.FlowerOwnerEpoch);
                ValidateStreamKind(storageStream, record);
                sequence++;
                if (record.Kind == MerkabaRecordKind.M8Tile)
                {
                    MerkabaTileAddress tile =
                        MerkabaSphereFlowerPersistenceAbi.ReadTileAddress(
                            address);
                    KernelState[] states = DecodeStates(payload);
                    var location = new Location(path, payloadOffset,
                        header.CommitGeneration, sequence,
                        CountOccupied(states));
                    if (!rebuilt.TryGetValue(tile, out Location prior) ||
                        location.IsNewerThan(prior))
                        rebuilt[tile] = location;
                }
                else
                    replay.Apply(record, new MerkabaRecordVersion(
                        header.CommitGeneration, sequence));
                records++;
                if ((records & 31) == 0 || stream.Position == validEnd)
                    progress?.Report(new OperationWorkProgress(
                        ScanOperationStage.RebuildingStorageIndex,
                        completedBefore + stream.Position, totalBytes,
                        $"Replayed {records} records from " +
                        FileName(storageStream)));
                if (stream.Position <= recordOffset)
                    throw new InvalidDataException("Record replay made no progress.");
            }
            if (stream.Position != validEnd)
                throw new InvalidDataException(
                    "Committed stream does not end on a record boundary.");
        }

        private MerkabaTileSnapshot ReadOne(MerkabaTileAddress address)
        {
            Location location;
            lock (_gate)
            {
                if (!_index.TryGetValue(address, out location))
                {
                    if (_subordinate.HasStoredDualLeaf(address))
                        return new MerkabaTileSnapshot
                        {
                            Address = address,
                            States = new KernelState[MerkabaSpatial.KernelsPerTile]
                        };
                    throw new FileNotFoundException(
                        $"M8 tile {address.LocalAddress} at " +
                        $"{address.BlockCoord} is absent from storage.");
                }
            }
            using var stream = new FileStream(location.Path, FileMode.Open,
                FileAccess.Read, FileShare.ReadWrite, 16 * 1024,
                FileOptions.RandomAccess);
            stream.Position = location.PayloadOffset;
            KernelState[] states = DecodeStates(ReadExact(stream,
                TilePayloadBytes));
            return new MerkabaTileSnapshot
            {
                Address = address,
                Generation = location.Generation,
                States = states
            };
        }

        private static bool IsCanonicalR1Owner(MerkabaOwnerAddress owner,
            IReadOnlyDictionary<MerkabaTileAddress, Location> index,
            IDictionary<MerkabaTileAddress, KernelState[]> tileCache)
        {
            if (!index.TryGetValue(owner.Tile, out Location location))
                return false;
            if (!tileCache.TryGetValue(owner.Tile, out KernelState[] states))
            {
                using var stream = new FileStream(location.Path, FileMode.Open,
                    FileAccess.Read, FileShare.ReadWrite, 16 * 1024,
                    FileOptions.RandomAccess);
                stream.Position = location.PayloadOffset;
                states = DecodeStates(ReadExact(stream, TilePayloadBytes));
                tileCache.Add(owner.Tile, states);
            }
            KernelState state = states[owner.KernelLocal];
            return state.IsOccupied && state.HasMeasuredSurfacePlane;
        }

        private void ValidateAvailableRanges(MerkabaSessionManifest manifest)
        {
            for (int i = 0; i < (int)MerkabaStorageStream.Count; i++)
            {
                var stream = (MerkabaStorageStream)i;
                long required = manifest.ValidEnd(stream);
                long actual = FileLength(PathFor(stream));
                if (actual < required)
                    throw new InvalidDataException(
                        $"Committed {FileName(stream)} is truncated.");
            }
        }

        private void TruncateUncommittedTails(MerkabaSessionManifest manifest)
        {
            for (int i = 0; i < (int)MerkabaStorageStream.Count; i++)
            {
                var stream = (MerkabaStorageStream)i;
                string path = PathFor(stream);
                long valid = manifest.ValidEnd(stream);
                if (FileLength(path) > valid) TruncateFile(path, valid);
            }
        }

        private static long WriteRecord(Stream destination,
            MerkabaAppendRecord record, ulong generation)
        {
            MerkabaRecordHeader header = MerkabaRecordHeader.Create(record.Kind,
                generation, record.Address, record.Payload);
            var headerBytes = new byte[MerkabaRecordHeader.ByteSize];
            MerkabaSphereFlowerPersistenceAbi.WriteHeader(headerBytes, header);
            destination.Write(headerBytes, 0, headerBytes.Length);
            destination.Write(record.Address, 0, record.Address.Length);
            long payloadOffset = destination.Position;
            destination.Write(record.Payload, 0, record.Payload.Length);
            return payloadOffset;
        }

        private Location[] WriteCanonicalM8File(string temporaryPath,
            string authorityPath, IReadOnlyList<MerkabaTileAddress> addresses,
            ulong generation, ref ulong sequence,
            CancellationToken cancellationToken)
        {
            using var stream = new FileStream(temporaryPath, FileMode.Create,
                FileAccess.Write, FileShare.None, 256 * 1024,
                FileOptions.SequentialScan);
            Location[] locations = WriteCanonicalM8Image(stream, authorityPath,
                addresses, generation, ref sequence, cancellationToken);
            stream.Flush();
            return locations;
        }

        private Location[] WriteCanonicalM8Image(Stream destination,
            string authorityPath, IReadOnlyList<MerkabaTileAddress> addresses,
            ulong generation, ref ulong sequence,
            CancellationToken cancellationToken)
        {
            var locations = new Location[addresses.Count];
            for (int i = 0; i < addresses.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                MerkabaTileAddress address = addresses[i];
                MerkabaTileSnapshot tile = ReadOne(address);
                byte[] encodedAddress = new byte[
                    MerkabaSphereFlowerPersistenceAbi.TileAddressBytes];
                MerkabaSphereFlowerPersistenceAbi.WriteTileAddress(
                    encodedAddress, address);
                long payloadOffset = WriteRecord(destination,
                    new MerkabaAppendRecord(MerkabaRecordKind.M8Tile,
                        encodedAddress, EncodeStates(tile.States)), generation);
                sequence = checked(sequence + 1ul);
                locations[i] = new Location(authorityPath, payloadOffset,
                    generation, sequence, CountOccupied(tile.States));
            }
            return locations;
        }

        private void WriteCanonicalDualFile(string temporaryPath,
            ulong generation, ref ulong sequence,
            CancellationToken cancellationToken)
        {
            using var stream = new FileStream(temporaryPath, FileMode.Create,
                FileAccess.Write, FileShare.None, 256 * 1024,
                FileOptions.SequentialScan);
            WriteCanonicalDualImage(stream, generation, ref sequence,
                cancellationToken);
            stream.Flush();
        }

        private void WriteCanonicalDualImage(Stream destination,
            ulong generation, ref ulong sequence,
            CancellationToken cancellationToken)
        {
            foreach (MerkabaAppendRecord record in
                     _subordinate.CanonicalDualRecords())
            {
                cancellationToken.ThrowIfCancellationRequested();
                WriteRecord(destination, record, generation);
                sequence = checked(sequence + 1ul);
            }
        }

        private static FileStream OpenAppendAtExactEnd(string path, long validEnd)
        {
            var stream = new FileStream(path, FileMode.OpenOrCreate,
                FileAccess.Write, FileShare.Read, 256 * 1024,
                FileOptions.SequentialScan);
            if (stream.Length != validEnd)
            {
                stream.Dispose();
                throw new InvalidDataException(
                    "Append stream contains an uncommitted physical tail.");
            }
            stream.Position = validEnd;
            return stream;
        }

        private long PrepareAppendEnd(MerkabaStorageStream storageStream)
        {
            string path = PathFor(storageStream);
            long actual = FileLength(path);
            long committedEnd;
            bool hasCommittedBoundary;
            bool alreadyDirty;
            lock (_gate)
            {
                hasCommittedBoundary = _manifest != null;
                committedEnd = _manifest?.ValidEnd(storageStream) ?? actual;
                alreadyDirty = _dirtyStreams.Contains(storageStream);
            }
            if (!hasCommittedBoundary || alreadyDirty) return actual;
            if (actual < committedEnd)
                throw new InvalidDataException(
                    $"Committed {FileName(storageStream)} is truncated.");
            if (actual > committedEnd) TruncateFile(path, committedEnd);
            return committedEnd;
        }

        private void WriteManifestTemporary(MerkabaSessionManifest manifest)
        {
            using var stream = new FileStream(ManifestPath + ".tmp",
                FileMode.Create, FileAccess.Write, FileShare.None, 16 * 1024,
                FileOptions.SequentialScan);
            MerkabaSessionManifest.Write(stream, manifest);
            stream.Flush(true);
        }

        private void AdoptCommittedManifest(MerkabaSessionManifest manifest,
            ulong followingGeneration)
        {
            lock (_gate)
            {
                _manifest = manifest;
                _pendingGeneration = followingGeneration;
                _dirtyStreams.Clear();
                _dirtyM8Tiles.Clear();
                _dirtyDualBlocks.Clear();
                _dirtyFineOwners.Clear();
            }
        }

        private void ThrowIfAppendPublicationFailed()
        {
            if (_appendPublicationFailure != null)
                throw new IOException(
                    "The append index is incomplete; reopen the committed store before retrying publication.",
                    _appendPublicationFailure);
        }

        private void AdoptCompactionManifest(MerkabaSessionManifest manifest,
            IReadOnlyList<MerkabaTileAddress> addresses,
            IReadOnlyList<Location> locations, ulong sequence)
        {
            if (locations == null || locations.Count != addresses.Count)
                throw new InvalidDataException(
                    "Canonical M8 compaction locations are incomplete.");
            lock (_gate)
            {
                _manifest = manifest;
                _appendAuthority = new object();
                for (int i = 0; i < addresses.Count; i++)
                    _index[addresses[i]] = locations[i];
                _recordSequence = sequence;
            }
        }

        private static bool ManifestMatches(string path,
            MerkabaSessionManifest expected)
        {
            try
            {
                using var expectedBytes = new MemoryStream();
                MerkabaSessionManifest.Write(expectedBytes, expected);
                byte[] actual = File.ReadAllBytes(path);
                byte[] canonical = expectedBytes.ToArray();
                if (actual.Length != canonical.Length) return false;
                for (int i = 0; i < actual.Length; i++)
                    if (actual[i] != canonical[i]) return false;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void DeleteCompactionTemps()
        {
            DeleteIfExists(ManifestPath + ".tmp");
            DeleteIfExists(ManifestPath + ".bak");
            DeleteIfExists(M8BasePath + ".tmp");
            DeleteIfExists(M8BasePath + ".bak");
            DeleteIfExists(ThroughBasePath + ".tmp");
            DeleteIfExists(ThroughBasePath + ".bak");
        }

        private static byte[] EncodeStates(KernelState[] states)
        {
            var payload = new byte[TilePayloadBytes];
            for (int i = 0; i < states.Length; i++)
            {
                int offset = i * KernelState.ByteSize;
                MerkabaSphereFlowerPersistenceAbi.WriteInt32(payload, offset,
                    states[i].OccupancyEvidence);
                MerkabaSphereFlowerPersistenceAbi.WriteUInt32(payload,
                    offset + 4, states[i].PackedColor);
                MerkabaSphereFlowerPersistenceAbi.WriteUInt32(payload,
                    offset + 8, states[i].ColorConfidence);
                MerkabaSphereFlowerPersistenceAbi.WriteUInt32(payload,
                    offset + 12, states[i].Flags);
            }
            return payload;
        }

        private static KernelState[] DecodeStates(byte[] payload)
        {
            if (payload.Length != TilePayloadBytes)
                throw new InvalidDataException("M8 tile payload size is invalid.");
            var states = new KernelState[MerkabaSpatial.KernelsPerTile];
            for (int i = 0; i < states.Length; i++)
            {
                int offset = i * KernelState.ByteSize;
                states[i] = new KernelState
                {
                    OccupancyEvidence =
                        MerkabaSphereFlowerPersistenceAbi.ReadInt32(payload,
                            offset),
                    PackedColor =
                        MerkabaSphereFlowerPersistenceAbi.ReadUInt32(payload,
                            offset + 4),
                    ColorConfidence =
                        MerkabaSphereFlowerPersistenceAbi.ReadUInt32(payload,
                            offset + 8),
                    Flags = MerkabaSphereFlowerPersistenceAbi.ReadUInt32(
                        payload, offset + 12)
                };
                Validate(states[i]);
            }
            return states;
        }

        private static void ValidateTile(MerkabaTileSnapshot tile)
        {
            if (tile?.States == null ||
                tile.States.Length != MerkabaSpatial.KernelsPerTile)
                throw new InvalidDataException(
                    "M8 tile payload must be exactly 8192 bytes.");
            foreach (KernelState state in tile.States) Validate(state);
        }

        private static uint CountOccupied(IReadOnlyList<KernelState> states)
        {
            uint count = 0u;
            for (int i = 0; i < states.Count; i++)
                if (states[i].IsOccupied) count++;
            return count;
        }

        private static void Validate(KernelState state)
        {
            bool seed = (state.Flags & MerkabaConstants.R1SeedFlag) != 0u;
            bool plane = state.HasMeasuredSurfacePlane;
            bool validR1 = state.IsOccupied
                ? plane && !seed && state.OccupancyEvidence > MerkabaConstants.OccupiedOffThreshold
                : seed
                    ? plane && state.OccupancyEvidence > 0 &&
                        state.OccupancyEvidence < MerkabaConstants.OccupiedOnThreshold
                    : !plane && state.OccupancyEvidence == 0;
            if (!validR1 || state.OccupancyEvidence < 0 ||
                state.OccupancyEvidence > MerkabaConstants.MaximumEvidence ||
                state.ColorConfidence > MerkabaConstants.MaximumColorConfidence ||
                (state.Flags & ~(MerkabaConstants.OccupiedFlag |
                                 MerkabaConstants.R1SeedFlag |
                                 MerkabaConstants.SurfacePlanePayloadMask)) != 0u)
                throw new InvalidDataException("M8 KernelState is out of range.");
        }

        private static void ValidateStreamKind(MerkabaStorageStream stream,
            MerkabaAppendRecord record)
        {
            bool valid = stream switch
            {
                MerkabaStorageStream.M8Base or MerkabaStorageStream.M8Live =>
                    record.Kind == MerkabaRecordKind.M8Tile,
                MerkabaStorageStream.ThroughBase or
                    MerkabaStorageStream.ThroughLive =>
                    record.Kind >= MerkabaRecordKind.DualBlock &&
                    record.Kind <= MerkabaRecordKind.DualLeaf,
                MerkabaStorageStream.FlowerDetail =>
                    record.Kind == MerkabaRecordKind.FlowerOwnerEpoch ||
                    record.Kind == MerkabaRecordKind.FlowerDetail ||
                    record.Kind == MerkabaRecordKind.FlowerSkinMetricRun ||
                    record.Kind == MerkabaRecordKind.FlowerVGroup ||
                    IsTombstoneFor(record, MerkabaRecordKind.FlowerDetail) ||
                    IsTombstoneFor(record,
                        MerkabaRecordKind.FlowerSkinMetricRun) ||
                    IsTombstoneFor(record, MerkabaRecordKind.FlowerVGroup),
                MerkabaStorageStream.ThreadAtlas =>
                    record.Kind == MerkabaRecordKind.ThreadProgram ||
                    record.Kind == MerkabaRecordKind.ThreadRun ||
                    record.Kind == MerkabaRecordKind.ThreadColorGroup ||
                    IsTombstoneFor(record, MerkabaRecordKind.ThreadRun) ||
                    IsTombstoneFor(record,
                        MerkabaRecordKind.ThreadColorGroup),
                _ => false
            };
            if (!valid)
                throw new InvalidDataException(
                    $"{record.Kind} is in the wrong persistence stream.");
        }

        private static void ValidateStreamGeneration(
            MerkabaStorageStream stream, ulong generation,
            MerkabaSessionManifest manifest)
        {
            bool valid = stream switch
            {
                MerkabaStorageStream.M8Base =>
                    generation <= manifest.M8BaseGeneration,
                MerkabaStorageStream.M8Live =>
                    generation > manifest.M8BaseGeneration,
                MerkabaStorageStream.ThroughBase =>
                    generation <= manifest.ThroughBaseGeneration,
                MerkabaStorageStream.ThroughLive =>
                    generation > manifest.ThroughBaseGeneration,
                _ => true
            };
            if (!valid)
                throw new InvalidDataException(
                    $"{FileName(stream)} record is outside its base/live " +
                    "generation range.");
        }

        private static MerkabaStorageStream StreamFor(
            MerkabaAppendRecord record)
        {
            if (record.Kind >= MerkabaRecordKind.DualBlock &&
                record.Kind <= MerkabaRecordKind.DualLeaf)
                return MerkabaStorageStream.ThroughLive;
            if (record.Kind == MerkabaRecordKind.FlowerOwnerEpoch ||
                record.Kind == MerkabaRecordKind.FlowerDetail ||
                record.Kind == MerkabaRecordKind.FlowerSkinMetricRun ||
                record.Kind == MerkabaRecordKind.FlowerVGroup ||
                IsTombstoneFor(record, MerkabaRecordKind.FlowerDetail) ||
                IsTombstoneFor(record,
                    MerkabaRecordKind.FlowerSkinMetricRun) ||
                IsTombstoneFor(record, MerkabaRecordKind.FlowerVGroup))
                return MerkabaStorageStream.FlowerDetail;
            return MerkabaStorageStream.ThreadAtlas;
        }

        private static bool IsTombstoneFor(MerkabaAppendRecord record,
            MerkabaRecordKind target) => record.Kind ==
            MerkabaRecordKind.Tombstone &&
            (MerkabaRecordKind)MerkabaSphereFlowerPersistenceAbi.ReadUInt32(
                record.Payload, 0) == target;

        private string PathFor(MerkabaStorageStream stream) =>
            Path.Combine(_directory, FileName(stream));

        private static string FileName(MerkabaStorageStream stream) =>
            stream switch
            {
                MerkabaStorageStream.M8Base => M8BaseFileName,
                MerkabaStorageStream.M8Live => M8LiveFileName,
                MerkabaStorageStream.ThroughBase => ThroughBaseFileName,
                MerkabaStorageStream.ThroughLive => ThroughLiveFileName,
                MerkabaStorageStream.FlowerDetail => FlowerDetailFileName,
                MerkabaStorageStream.ThreadAtlas => ThreadAtlasFileName,
                _ => throw new ArgumentOutOfRangeException(nameof(stream))
            };

        private static MerkabaSessionManifest ReadManifest(string path)
        {
            using var stream = new FileStream(path, FileMode.Open,
                FileAccess.Read, FileShare.Read, 16 * 1024,
                FileOptions.SequentialScan);
            return MerkabaSessionManifest.Read(stream);
        }

        private static void CopyPrefixDurable(string source, string destination,
            long bytes)
        {
            string temporary = destination + ".tmp";
            using (var input = new FileStream(source, FileMode.Open,
                       FileAccess.Read, FileShare.Read, 1024 * 1024,
                       FileOptions.SequentialScan))
            using (var output = new FileStream(temporary, FileMode.Create,
                       FileAccess.Write, FileShare.None, 1024 * 1024,
                       FileOptions.SequentialScan))
            {
                var buffer = new byte[1024 * 1024];
                long remaining = bytes;
                while (remaining > 0L)
                {
                    int wanted = (int)Math.Min(buffer.Length, remaining);
                    int read = input.Read(buffer, 0, wanted);
                    if (read == 0) throw new EndOfStreamException(
                        "Committed source stream is truncated.");
                    output.Write(buffer, 0, read);
                    remaining -= read;
                }
                output.Flush(true);
            }
            MerkabaFilePublishing.Publish(temporary, destination);
        }

        private static byte[] ReadExact(Stream source, int count)
        {
            var result = new byte[count];
            int read = 0;
            while (read < count)
            {
                int next = source.Read(result, read, count - read);
                if (next == 0) throw new EndOfStreamException(
                    "Committed record is truncated.");
                read += next;
            }
            return result;
        }

        private static long FileLength(string path) => File.Exists(path)
            ? new FileInfo(path).Length : 0L;

        private static void FlushFileDurable(string path)
        {
            using var stream = new FileStream(path, FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.Read, 16 * 1024,
                FileOptions.SequentialScan);
            stream.Flush(true);
        }

        private static void TruncateFile(string path, long length)
        {
            if (!File.Exists(path) && length == 0L) return;
            using var stream = new FileStream(path, FileMode.OpenOrCreate,
                FileAccess.Write, FileShare.Read);
            stream.SetLength(length);
            stream.Flush(true);
        }

        private static void TryTruncateIgnoredTail(string path)
        {
            try
            {
                TruncateFile(path, 0L);
            }
            catch (Exception exception)
            {
                Logger.Warning("Committed live tail remains physically present " +
                    "but is excluded by the manifest: " + exception.Message);
            }
        }

        private static long CommittedByteCount(MerkabaSessionManifest manifest)
        {
            if (manifest == null) return 0L;
            long total = 0L;
            foreach (long end in manifest.ValidEnds)
                total = checked(total + end);
            return total;
        }

        private static ulong NextGeneration(ulong generation)
        {
            if (generation == ulong.MaxValue)
                throw new InvalidOperationException(
                    "Session commit generation is exhausted.");
            return generation + 1ul;
        }

        private void IndexTileBlock(MerkabaTileAddress address)
        {
            if (!_indexedTilesByBlock.TryGetValue(address.BlockCoord,
                    out HashSet<MerkabaTileAddress> addresses))
            {
                addresses = new HashSet<MerkabaTileAddress>();
                _indexedTilesByBlock.Add(address.BlockCoord, addresses);
            }
            addresses.Add(address);
        }

        private static float BlockDistanceSquared(float3 gridPosition,
            int3 blockCoord)
        {
            float3 minimum = (float3)blockCoord * MerkabaSpatial.BlockWorldSize;
            float3 maximum = minimum + MerkabaSpatial.BlockWorldSize;
            return math.lengthsq(math.clamp(gridPosition, minimum, maximum) -
                gridPosition);
        }

        private static void DeleteStorageFiles(string directory)
        {
            foreach (string name in new[]
                     {
                         M8BaseFileName, M8LiveFileName,
                         ThroughBaseFileName, ThroughLiveFileName,
                         FlowerDetailFileName, ThreadAtlasFileName,
                         ManifestFileName
                     })
            {
                DeleteIfExists(Path.Combine(directory, name));
                DeleteIfExists(Path.Combine(directory, name) + ".tmp");
                DeleteIfExists(Path.Combine(directory, name) + ".bak");
            }
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
