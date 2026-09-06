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

        internal bool HasCommittedSession => File.Exists(ManifestPath);
        internal MerkabaSphereFlowerReplayIndex Subordinate => _subordinate;

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
            Task.Run(() => AppendM8Tiles(tiles));

        internal void AppendM8Tiles(IReadOnlyList<MerkabaTileSnapshot> tiles)
        {
            if (tiles == null) throw new ArgumentNullException(nameof(tiles));
            if (tiles.Count == 0) return;
            if (tiles.Count > MerkabaGrid.StreamBatchCapacity)
                throw new InvalidDataException(
                    "M8 writeback batch exceeds 32 tiles.");
            foreach (MerkabaTileSnapshot tile in tiles) ValidateTile(tile);

            lock (_ioGate)
            {
                Directory.CreateDirectory(_directory);
                long originalLength = PrepareAppendEnd(
                    MerkabaStorageStream.M8Live);
                var pending = new List<PendingM8Location>(tiles.Count);
                ulong nextSequence = _recordSequence;
                try
                {
                    using var stream = OpenAppendAtExactEnd(M8LivePath,
                        originalLength);
                    foreach (MerkabaTileSnapshot tile in tiles)
                    {
                        byte[] address = new byte[
                            MerkabaSphereFlowerPersistenceAbi.TileAddressBytes];
                        MerkabaSphereFlowerPersistenceAbi.WriteTileAddress(
                            address, tile.Address);
                        byte[] payload = EncodeStates(tile.States);
                        var record = new MerkabaAppendRecord(
                            MerkabaRecordKind.M8Tile, address, payload);
                        long payloadOffset = WriteRecord(stream, record,
                            _pendingGeneration);
                        nextSequence++;
                        pending.Add(new PendingM8Location(tile, new Location(
                            M8LivePath, payloadOffset, _pendingGeneration,
                            nextSequence, CountOccupied(tile.States))));
                    }
                    stream.Flush();
                }
                catch
                {
                    TruncateFile(M8LivePath, originalLength);
                    throw;
                }

                lock (_gate)
                {
                    foreach (PendingM8Location update in pending)
                    {
                        if (_index.TryGetValue(update.Tile.Address,
                                out Location previous))
                            _indexedOccupiedKernelCount -=
                                previous.OccupiedKernelCount;
                        _index[update.Tile.Address] = update.Location;
                        _indexedOccupiedKernelCount = checked(
                            _indexedOccupiedKernelCount +
                            update.Location.OccupiedKernelCount);
                        IndexTileBlock(update.Tile.Address);
                        _dirtyM8Tiles.Add(update.Tile.Address);
                        update.Tile.Generation = update.Location.Generation;
                    }
                    _recordSequence = nextSequence;
                    _dirtyStreams.Add(MerkabaStorageStream.M8Live);
                }
            }
        }

        internal Task AppendSphereFlowerRecordsAsync(
            IReadOnlyList<MerkabaAppendRecord> records) =>
            Task.Run(() => AppendSphereFlowerRecords(records));

        internal void AppendSphereFlowerRecords(
            IReadOnlyList<MerkabaAppendRecord> records)
        {
            if (records == null) throw new ArgumentNullException(nameof(records));
            if (records.Count == 0) return;

            var byStream = new Dictionary<MerkabaStorageStream,
                List<MerkabaAppendRecord>>();
            foreach (MerkabaAppendRecord record in records)
            {
                if (record == null) throw new ArgumentException(
                    "Append record is null.", nameof(records));
                if (record.Kind == MerkabaRecordKind.M8Tile)
                    throw new ArgumentException(
                        "M8 tiles use AppendM8Tiles.", nameof(records));
                MerkabaStorageStream target = StreamFor(record);
                if (!byStream.TryGetValue(target,
                        out List<MerkabaAppendRecord> list))
                {
                    list = new List<MerkabaAppendRecord>();
                    byStream.Add(target, list);
                }
                list.Add(record);
            }

            lock (_ioGate)
            {
                Directory.CreateDirectory(_directory);
                var originalLengths = new Dictionary<MerkabaStorageStream, long>();
                ulong nextSequence = _recordSequence;
                var validated = new MerkabaSphereFlowerReplayIndex();
                lock (_gate) validated.CopyFrom(_subordinate);
                var streams = new List<MerkabaStorageStream>(byStream.Keys);
                streams.Sort();
                foreach (MerkabaStorageStream storageStream in streams)
                    foreach (MerkabaAppendRecord record in byStream[storageStream])
                    {
                        nextSequence++;
                        validated.Apply(record, new MerkabaRecordVersion(
                            _pendingGeneration, nextSequence));
                    }
                nextSequence = _recordSequence;
                try
                {
                    foreach (MerkabaStorageStream storageStream in streams)
                    {
                        List<MerkabaAppendRecord> streamRecords =
                            byStream[storageStream];
                        string path = PathFor(storageStream);
                        long originalLength = PrepareAppendEnd(storageStream);
                        originalLengths[storageStream] = originalLength;
                        using var stream = OpenAppendAtExactEnd(path,
                            originalLength);
                        foreach (MerkabaAppendRecord record in streamRecords)
                        {
                            nextSequence++;
                            WriteRecord(stream, record, _pendingGeneration);
                        }
                        stream.Flush();
                    }
                }
                catch
                {
                    foreach (KeyValuePair<MerkabaStorageStream, long> prior in
                             originalLengths)
                        TruncateFile(PathFor(prior.Key), prior.Value);
                    throw;
                }

                lock (_gate)
                {
                    _subordinate.CopyFrom(validated);
                    foreach (MerkabaStorageStream stream in streams)
                        _dirtyStreams.Add(stream);
                    MerkabaSphereFlowerReplayIndex.AccumulateTouched(records,
                        _dirtyDualBlocks, _dirtyFineOwners);
                    _recordSequence = nextSequence;
                }
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
                Directory.CreateDirectory(_directory);
                lock (_gate)
                    if (_manifest != null &&
                        (_manifest.SessionUuid != sessionUuid ||
                         _manifest.AnchorUuid != anchorUuid))
                        throw new InvalidOperationException(
                            "A committed storage root cannot be relabeled with " +
                            "another session or anchor identity; use an exact " +
                            "session clone for SAVE AS.");
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
            for (int i = 0; i < addresses.Count; i++)
                result[i] = ReadOne(addresses[i]);
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
                    (MerkabaRecordKind)header.Kind, address, payload);
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
                    throw new FileNotFoundException(
                        $"M8 tile {address.LocalAddress} at " +
                        $"{address.BlockCoord} is absent from storage.");
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
            if (state.OccupancyEvidence < MerkabaConstants.MinimumEvidence ||
                state.OccupancyEvidence > MerkabaConstants.MaximumEvidence ||
                state.ColorConfidence > MerkabaConstants.MaximumColorConfidence ||
                (state.Flags & ~(MerkabaConstants.OccupiedFlag |
                                 MerkabaConstants.NeedsCarveFlag |
                                 MerkabaConstants.SurfacePlanePayloadMask)) != 0u ||
                (!state.IsOccupied && state.HasMeasuredSurfacePlane))
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
                    IsTombstoneFor(record, MerkabaRecordKind.FlowerDetail),
                MerkabaStorageStream.ThreadAtlas =>
                    record.Kind == MerkabaRecordKind.ThreadProgram ||
                    record.Kind == MerkabaRecordKind.ThreadRun ||
                    record.Kind == MerkabaRecordKind.ThreadResidual ||
                    (record.Kind == MerkabaRecordKind.Tombstone &&
                     !IsTombstoneFor(record, MerkabaRecordKind.FlowerDetail)),
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
                IsTombstoneFor(record, MerkabaRecordKind.FlowerDetail))
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
