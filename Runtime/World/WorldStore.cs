using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Genesis.RoomScan.World
{
    public enum WorldManifestLoadSource
    {
        Primary = 0,
        Backup = 1
    }

    /// <summary>
    /// Durable, versioned storage for unbounded worlds. Metadata commits never expose a
    /// partially written JSON document. Chunk payloads are promoted before their manifest
    /// references, so an interrupted commit can at worst leave an unreferenced revision.
    /// </summary>
    public sealed class WorldStore
    {
        public const string ManifestFileName = "world.json";
        public const string ManifestBackupFileName = "world.json.bak";
        public const string ChunksDirectoryName = "chunks";
        public const string TransactionsDirectoryName = ".transactions";

        private readonly object _gate = new();
        private readonly string _rootDirectory;

        public WorldStore(string rootDirectory)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
                throw new ArgumentException("World storage root cannot be empty.",
                    nameof(rootDirectory));
            _rootDirectory = Path.GetFullPath(rootDirectory);
        }

        public string RootDirectory => _rootDirectory;

        public bool TryCommitManifest(WorldManifest manifest, out string error)
        {
            error = null;
            WorldValidationResult validation = WorldManifestValidator.Validate(manifest);
            if (!validation.IsValid)
            {
                error = validation.ToString();
                return false;
            }
            if (!WorldManifestJson.TrySerialize(manifest, true, out string json,
                    out validation))
            {
                error = validation.ToString();
                return false;
            }

            lock (_gate)
            {
                try
                {
                    string worldDirectory = GetWorldDirectory(manifest.worldId);
                    Directory.CreateDirectory(worldDirectory);
                    Directory.CreateDirectory(Path.Combine(worldDirectory, ChunksDirectoryName));
                    Directory.CreateDirectory(Path.Combine(worldDirectory,
                        TransactionsDirectoryName));
                    string manifestPath = Path.Combine(worldDirectory, ManifestFileName);
                    bool primaryIsKnownGood = TryReadManifestCandidate(manifestPath,
                        manifest.worldId, out WorldManifest primaryManifest, out _);
                    string backupPath = Path.Combine(worldDirectory, ManifestBackupFileName);
                    WorldManifest backupManifest = null;
                    bool backupIsKnownGood = !primaryIsKnownGood &&
                        TryReadManifestCandidate(backupPath, manifest.worldId,
                            out backupManifest, out _);
                    WorldManifest durableManifest = primaryIsKnownGood
                        ? primaryManifest
                        : backupIsKnownGood ? backupManifest : null;
                    if (durableManifest != null)
                    {
                        if (manifest.revision < durableManifest.revision)
                        {
                            error = $"Stale world revision {manifest.revision}; durable revision is " +
                                    $"{durableManifest.revision}.";
                            return false;
                        }
                        if (manifest.revision == durableManifest.revision)
                        {
                            WorldManifestJson.TrySerialize(durableManifest, true,
                                out string durableJson, out _);
                            if (!string.Equals(json, durableJson, StringComparison.Ordinal))
                            {
                                error = "A changed manifest must increment the world revision.";
                                return false;
                            }
                            if (primaryIsKnownGood)
                                return true;
                        }
                    }
                    return AtomicUtf8File.TryWrite(
                        manifestPath, backupPath, json, primaryIsKnownGood, out error);
                }
                catch (Exception exception)
                {
                    error = $"Manifest commit failed: {exception.Message}";
                    return false;
                }
            }
        }

        public bool TryLoadManifest(string worldId, out WorldManifest manifest,
            out WorldManifestLoadSource source, out string error)
        {
            manifest = null;
            source = WorldManifestLoadSource.Primary;
            error = null;
            if (!StoragePath.IsSafeIdentifier(worldId, 96))
            {
                error = "World identifier is invalid.";
                return false;
            }

            lock (_gate)
            {
                string worldDirectory;
                try
                {
                    worldDirectory = GetWorldDirectory(worldId);
                }
                catch (Exception exception)
                {
                    error = exception.Message;
                    return false;
                }

                string primaryPath = Path.Combine(worldDirectory, ManifestFileName);
                string backupPath = Path.Combine(worldDirectory, ManifestBackupFileName);
                string primaryError = null;
                if (TryReadManifestCandidate(primaryPath, worldId, out manifest,
                        out primaryError))
                    return true;

                string backupError = null;
                if (TryReadManifestCandidate(backupPath, worldId, out manifest,
                        out backupError))
                {
                    source = WorldManifestLoadSource.Backup;
                    error = string.IsNullOrEmpty(primaryError)
                        ? "Primary manifest was unavailable; loaded last known-good backup."
                        : $"Primary manifest rejected ({primaryError}); loaded last known-good backup.";
                    return true;
                }

                error = $"No valid manifest for '{worldId}'. Primary: {primaryError ?? "missing"}; " +
                        $"backup: {backupError ?? "missing"}.";
                return false;
            }
        }

        public bool TryBeginChunkRevision(string worldId, string chunkId, int revision,
            out ChunkRevisionTransaction transaction, out string error)
        {
            transaction = null;
            error = null;
            if (revision < 0)
            {
                error = "Chunk revision cannot be negative.";
                return false;
            }
            if (!StoragePath.IsSafeIdentifier(chunkId, 64))
            {
                error = "Chunk identifier is invalid.";
                return false;
            }
            if (!TryLoadManifest(worldId, out WorldManifest manifest, out _, out error))
                return false;

            ChunkRecord chunk = manifest.chunks.Find(candidate =>
                string.Equals(candidate.chunkId, chunkId, StringComparison.Ordinal));
            if (chunk == null)
            {
                error = $"Chunk '{chunkId}' is not registered in world '{worldId}'.";
                return false;
            }
            if (revision < chunk.revision)
            {
                error = $"Revision {revision} is older than current chunk revision {chunk.revision}.";
                return false;
            }

            try
            {
                transaction = new ChunkRevisionTransaction(this, worldId, chunkId, revision);
                return true;
            }
            catch (Exception exception)
            {
                error = $"Cannot start chunk transaction: {exception.Message}";
                return false;
            }
        }

        /// <summary>
        /// Stages a content-addressed enhancement for an already published chunk revision.
        /// Unlike <see cref="ChunkRevisionTransaction"/>, this does not replace the mapper
        /// revision directory and therefore cannot collide with its volume/mesh payloads.
        /// Call from a worker thread; commit the returned transaction only after rechecking
        /// the current in-memory manifest on Unity's continuation context.
        /// </summary>
        public bool TryBeginChunkArtifactPromotion(string worldId, string chunkId,
            int chunkRevision, ChunkArtifactKind kind, int formatVersion,
            string artifactFileName, string sourcePath, long expectedLength,
            string expectedSha256, out ChunkArtifactPromotion promotion, out string error)
        {
            promotion = null;
            error = null;
            if (!StoragePath.IsSafeIdentifier(worldId, 96) ||
                !StoragePath.IsSafeIdentifier(chunkId, 64) || chunkRevision < 0 ||
                !Enum.IsDefined(typeof(ChunkArtifactKind), kind) ||
                kind == ChunkArtifactKind.Unknown || formatVersion <= 0 ||
                !StoragePath.IsSafeRelativePath(artifactFileName) ||
                string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath) ||
                expectedLength <= 0 || expectedLength > WorldSchema.MaximumArtifactBytes ||
                !Hashing.IsLowerSha256(expectedSha256))
            {
                error = "Chunk artifact promotion arguments are invalid.";
                return false;
            }
            if (!TryLoadManifest(worldId, out WorldManifest durableManifest, out _,
                    out error))
                return false;
            ChunkRecord durableChunk = durableManifest.chunks.Find(candidate =>
                candidate != null && string.Equals(candidate.chunkId, chunkId,
                    StringComparison.Ordinal));
            if (durableChunk == null || durableChunk.revision != chunkRevision)
            {
                error = "Chunk artifact is stale for the durable chunk revision.";
                return false;
            }
            try
            {
                promotion = new ChunkArtifactPromotion(this, worldId, chunkId,
                    chunkRevision, kind, formatVersion, artifactFileName, sourcePath,
                    expectedLength, expectedSha256);
                return true;
            }
            catch (Exception exception)
            {
                error = "Chunk artifact staging failed: " + exception.Message;
                promotion?.Dispose();
                promotion = null;
                return false;
            }
        }

        public string GetWorldDirectory(string worldId)
        {
            if (!StoragePath.IsSafeIdentifier(worldId, 96))
                throw new ArgumentException("Unsafe world identifier.", nameof(worldId));
            return StoragePath.CombineContained(_rootDirectory, worldId);
        }

        /// <summary>
        /// Mutable capture workspace for a registered chunk. Immutable revision artifacts are
        /// published separately; this directory allows JPEGs to stream to disk during scanning.
        /// </summary>
        public string GetChunkWorkingDirectory(string worldId, string chunkId)
        {
            if (!StoragePath.IsSafeIdentifier(chunkId, 64))
                throw new ArgumentException("Unsafe chunk identifier.", nameof(chunkId));
            return StoragePath.CombineContained(GetWorldDirectory(worldId),
                ChunksDirectoryName, chunkId, "working");
        }

        /// <summary>
        /// Resolves a manifest artifact to a path contained by its world directory and
        /// verifies the durable file's length and SHA-256 before exposing it to a loader.
        /// Network and disk artifacts are untrusted input even when their manifest parsed.
        /// </summary>
        public bool TryResolveVerifiedArtifact(string worldId, ChunkArtifactRecord artifact,
            out string artifactPath, out string error)
        {
            artifactPath = null;
            error = null;
            if (!StoragePath.IsSafeIdentifier(worldId, 96))
            {
                error = "World identifier is invalid.";
                return false;
            }
            if (artifact == null || !StoragePath.IsSafeRelativePath(artifact.relativePath))
            {
                error = "Artifact record or relative path is invalid.";
                return false;
            }

            try
            {
                artifactPath = StoragePath.CombineContained(GetWorldDirectory(worldId),
                    artifact.relativePath.Split('/'));
                if (!File.Exists(artifactPath))
                {
                    error = "Artifact file is missing.";
                    artifactPath = null;
                    return false;
                }

                var info = new FileInfo(artifactPath);
                if (info.Length != artifact.byteLength)
                {
                    error = $"Artifact length mismatch: manifest={artifact.byteLength}, " +
                            $"file={info.Length}.";
                    artifactPath = null;
                    return false;
                }
                string digest = Hashing.ComputeSha256(artifactPath);
                if (!string.Equals(digest, artifact.sha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    error = "Artifact SHA-256 mismatch.";
                    artifactPath = null;
                    return false;
                }
                return true;
            }
            catch (Exception exception)
            {
                error = $"Artifact verification failed: {exception.Message}";
                artifactPath = null;
                return false;
            }
        }

        internal string GetChunkRevisionDirectory(string worldId, string chunkId, int revision)
        {
            if (!StoragePath.IsSafeIdentifier(chunkId, 64) || revision < 0)
                throw new ArgumentException("Unsafe chunk revision path.");
            return StoragePath.CombineContained(GetWorldDirectory(worldId),
                ChunksDirectoryName, chunkId, "revisions", revision.ToString("D10"));
        }

        internal string GetTransactionDirectory(string worldId, string chunkId, int revision,
            string transactionId)
        {
            return StoragePath.CombineContained(GetWorldDirectory(worldId),
                TransactionsDirectoryName,
                $"{chunkId}-{revision:D10}-{transactionId}");
        }

        internal bool TryCommitChunkRevision(ChunkRevisionTransaction transaction,
            WorldManifest updatedManifest, out string error)
        {
            error = null;
            if (transaction == null || transaction.Owner != this)
            {
                error = "Chunk transaction belongs to a different world store.";
                return false;
            }
            WorldValidationResult validation = WorldManifestValidator.Validate(updatedManifest);
            if (!validation.IsValid)
            {
                error = validation.ToString();
                return false;
            }
            if (!string.Equals(updatedManifest.worldId, transaction.WorldId,
                    StringComparison.Ordinal))
            {
                error = "Updated manifest belongs to a different world.";
                return false;
            }
            ChunkRecord chunk = updatedManifest.chunks.Find(candidate =>
                string.Equals(candidate.chunkId, transaction.ChunkId,
                    StringComparison.Ordinal));
            if (chunk == null || chunk.revision != transaction.Revision)
            {
                error = "Updated manifest does not publish the staged chunk revision.";
                return false;
            }
            foreach (ChunkArtifactRecord staged in transaction.StagedArtifacts)
            {
                ChunkArtifactRecord published = chunk.artifacts.Find(candidate =>
                    candidate.kind == staged.kind);
                if (!ArtifactEquals(staged, published))
                {
                    error = $"Manifest does not publish staged artifact {staged.kind} exactly.";
                    return false;
                }
            }

            lock (_gate)
            {
                string finalDirectory = transaction.FinalDirectory;
                if (Directory.Exists(finalDirectory))
                {
                    error = $"Chunk revision already exists: {finalDirectory}";
                    return false;
                }

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(finalDirectory));
                    Directory.Move(transaction.StagingDirectory, finalDirectory);
                    transaction.MarkPayloadPromoted();
                    if (TryCommitManifest(updatedManifest, out error))
                    {
                        transaction.MarkCommitted();
                        return true;
                    }

                    transaction.TryRollbackPayloadPromotion();
                    return false;
                }
                catch (Exception exception)
                {
                    transaction.TryRollbackPayloadPromotion();
                    error = $"Chunk revision commit failed: {exception.Message}";
                    return false;
                }
            }
        }

        internal bool TryCommitChunkArtifactPromotion(ChunkArtifactPromotion promotion,
            WorldManifest manifest, ChunkRecord chunk, long unixMilliseconds,
            out string error)
        {
            error = null;
            if (promotion == null || promotion.Owner != this || manifest == null ||
                chunk == null || !ReferenceEquals(manifest.chunks?.Find(candidate =>
                    ReferenceEquals(candidate, chunk)), chunk) ||
                !string.Equals(manifest.worldId, promotion.WorldId,
                    StringComparison.Ordinal) ||
                !string.Equals(chunk.chunkId, promotion.ChunkId,
                    StringComparison.Ordinal) || chunk.revision != promotion.ChunkRevision ||
                unixMilliseconds < manifest.updatedUnixMilliseconds ||
                unixMilliseconds < chunk.updatedUnixMilliseconds ||
                manifest.revision == int.MaxValue)
            {
                error = "Artifact promotion no longer matches the current chunk revision.";
                return false;
            }
            ChunkArtifactRecord previous = chunk.artifacts?.Find(candidate =>
                candidate != null && candidate.kind == promotion.Artifact.kind);
            if (previous != null && previous.chunkRevision == promotion.ChunkRevision &&
                !string.Equals(previous.sha256, promotion.Artifact.sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                error = "A different immutable artifact already exists for this chunk revision.";
                return false;
            }

            lock (_gate)
            {
                if (!promotion.TryPromotePayload(out bool newlyPromoted, out error))
                    return false;
                if (ArtifactEquals(previous, promotion.Artifact))
                {
                    promotion.MarkCommitted();
                    return true;
                }

                List<ChunkArtifactRecord> previousArtifacts = chunk.artifacts;
                long previousChunkUpdated = chunk.updatedUnixMilliseconds;
                int previousManifestRevision = manifest.revision;
                long previousManifestUpdated = manifest.updatedUnixMilliseconds;
                var updated = new List<ChunkArtifactRecord>();
                if (previousArtifacts != null)
                {
                    for (int i = 0; i < previousArtifacts.Count; i++)
                        if (previousArtifacts[i] != null &&
                            previousArtifacts[i].kind != promotion.Artifact.kind)
                            updated.Add(previousArtifacts[i]);
                }
                updated.Add(promotion.Artifact);
                chunk.artifacts = updated;
                chunk.updatedUnixMilliseconds = unixMilliseconds;
                manifest.revision++;
                manifest.updatedUnixMilliseconds = unixMilliseconds;

                if (TryCommitManifest(manifest, out error))
                {
                    promotion.MarkCommitted();
                    return true;
                }
                chunk.artifacts = previousArtifacts;
                chunk.updatedUnixMilliseconds = previousChunkUpdated;
                manifest.revision = previousManifestRevision;
                manifest.updatedUnixMilliseconds = previousManifestUpdated;
                if (newlyPromoted)
                    promotion.TryRollbackPayloadPromotion();
                return false;
            }
        }

        private static bool TryReadManifestCandidate(string path, string expectedWorldId,
            out WorldManifest manifest, out string error)
        {
            manifest = null;
            error = null;
            if (!File.Exists(path))
                return false;
            try
            {
                var info = new FileInfo(path);
                if (info.Length > (long)WorldSchema.MaximumJsonCharacters * 4L)
                {
                    error = "file exceeds the manifest byte limit";
                    return false;
                }
                string json = File.ReadAllText(path, Encoding.UTF8);
                if (!WorldManifestJson.TryDeserialize(json, out manifest,
                        out WorldValidationResult validation))
                {
                    error = validation.ToString();
                    return false;
                }
                if (!string.Equals(manifest.worldId, expectedWorldId, StringComparison.Ordinal))
                {
                    error = "manifest worldId does not match its directory";
                    manifest = null;
                    return false;
                }
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                manifest = null;
                return false;
            }
        }

        private static bool ArtifactEquals(ChunkArtifactRecord left, ChunkArtifactRecord right)
        {
            return left != null && right != null && left.kind == right.kind &&
                   left.formatVersion == right.formatVersion &&
                   left.chunkRevision == right.chunkRevision &&
                   left.byteLength == right.byteLength &&
                   string.Equals(left.relativePath, right.relativePath,
                       StringComparison.Ordinal) &&
                   string.Equals(left.sha256, right.sha256, StringComparison.OrdinalIgnoreCase);
        }
    }

    public sealed class ChunkRevisionTransaction : IDisposable
    {
        private readonly HashSet<ChunkArtifactKind> _kinds = new();
        private readonly List<ChunkArtifactRecord> _artifacts = new();
        private bool _payloadPromoted;
        private bool _committed;
        private bool _disposed;

        internal ChunkRevisionTransaction(WorldStore owner, string worldId, string chunkId,
            int revision)
        {
            Owner = owner;
            WorldId = worldId;
            ChunkId = chunkId;
            Revision = revision;
            string transactionId = Guid.NewGuid().ToString("N");
            StagingDirectory = owner.GetTransactionDirectory(worldId, chunkId, revision,
                transactionId);
            FinalDirectory = owner.GetChunkRevisionDirectory(worldId, chunkId, revision);
            Directory.CreateDirectory(StagingDirectory);
        }

        internal WorldStore Owner { get; }
        public string WorldId { get; }
        public string ChunkId { get; }
        public int Revision { get; }
        public string StagingDirectory { get; }
        public string FinalDirectory { get; }
        public IReadOnlyList<ChunkArtifactRecord> StagedArtifacts => _artifacts;

        public bool TryStageBytes(ChunkArtifactKind kind, int formatVersion,
            string artifactFileName, byte[] bytes, out ChunkArtifactRecord artifact,
            out string error)
        {
            artifact = null;
            error = null;
            if (bytes == null)
            {
                error = "Artifact bytes cannot be null.";
                return false;
            }
            return TryStage(kind, formatVersion, artifactFileName, stream =>
            {
                stream.Write(bytes, 0, bytes.Length);
            }, out artifact, out error);
        }

        /// <summary>
        /// Streams an artifact directly into the transaction staging file. The writer is
        /// synchronous by design: when this returns, bytes have been flushed to durable
        /// storage and the returned digest describes exactly those bytes. Callers can run
        /// the method on a worker thread when their writer does not touch Unity objects.
        /// </summary>
        public bool TryStageStream(ChunkArtifactKind kind, int formatVersion,
            string artifactFileName, Action<Stream> writer,
            out ChunkArtifactRecord artifact, out string error)
        {
            artifact = null;
            error = null;
            if (writer == null)
            {
                error = "Artifact writer cannot be null.";
                return false;
            }
            return TryStage(kind, formatVersion, artifactFileName,
                stream => writer(stream), out artifact, out error);
        }

        public bool TryStageFile(ChunkArtifactKind kind, int formatVersion,
            string artifactFileName, string sourcePath, out ChunkArtifactRecord artifact,
            out string error)
        {
            artifact = null;
            error = null;
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
            {
                error = "Artifact source file does not exist.";
                return false;
            }
            if (new FileInfo(sourcePath).Length > WorldSchema.MaximumArtifactBytes)
            {
                error = "Artifact source file exceeds the configured size limit.";
                return false;
            }
            return TryStage(kind, formatVersion, artifactFileName, destination =>
            {
                using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read,
                    FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
                source.CopyTo(destination, 1024 * 1024);
            }, out artifact, out error);
        }

        public bool TryCommit(WorldManifest updatedManifest, out string error)
        {
            if (_disposed || _committed)
            {
                error = "Chunk transaction is already closed.";
                return false;
            }
            return Owner.TryCommitChunkRevision(this, updatedManifest, out error);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            if (!_committed && Directory.Exists(StagingDirectory))
            {
                try { Directory.Delete(StagingDirectory, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        internal void MarkPayloadPromoted()
        {
            _payloadPromoted = true;
        }

        internal void MarkCommitted()
        {
            _committed = true;
            _payloadPromoted = false;
        }

        internal void TryRollbackPayloadPromotion()
        {
            if (!_payloadPromoted || !Directory.Exists(FinalDirectory) ||
                Directory.Exists(StagingDirectory))
                return;
            try
            {
                Directory.Move(FinalDirectory, StagingDirectory);
                _payloadPromoted = false;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private bool TryStage(ChunkArtifactKind kind, int formatVersion,
            string artifactFileName, Action<FileStream> writer,
            out ChunkArtifactRecord artifact, out string error)
        {
            artifact = null;
            error = null;
            if (_disposed || _committed || _payloadPromoted)
            {
                error = "Chunk transaction is not writable.";
                return false;
            }
            if (!Enum.IsDefined(typeof(ChunkArtifactKind), kind) ||
                kind == ChunkArtifactKind.Unknown)
            {
                error = "Artifact kind is invalid.";
                return false;
            }
            if (formatVersion <= 0)
            {
                error = "Artifact format version must be positive.";
                return false;
            }
            if (!_kinds.Add(kind))
            {
                error = $"Artifact kind {kind} is already staged.";
                return false;
            }
            if (!StoragePath.IsSafeRelativePath(artifactFileName))
            {
                _kinds.Remove(kind);
                error = "Artifact filename is not a safe normalized relative path.";
                return false;
            }

            string stagedPath = StoragePath.CombineContained(StagingDirectory,
                artifactFileName.Split('/'));
            try
            {
                string parent = Path.GetDirectoryName(stagedPath);
                if (!string.IsNullOrEmpty(parent))
                    Directory.CreateDirectory(parent);
                using (var stream = new FileStream(stagedPath, FileMode.CreateNew,
                           FileAccess.Write, FileShare.None, 1024 * 1024,
                           FileOptions.WriteThrough))
                {
                    writer(stream);
                    stream.Flush(true);
                }

                var info = new FileInfo(stagedPath);
                if (info.Length > WorldSchema.MaximumArtifactBytes)
                    throw new InvalidDataException("Artifact exceeds the configured size limit.");
                string relativePath = $"{WorldStore.ChunksDirectoryName}/{ChunkId}/revisions/" +
                                      $"{Revision:D10}/{artifactFileName}";
                artifact = new ChunkArtifactRecord
                {
                    kind = kind,
                    formatVersion = formatVersion,
                    chunkRevision = Revision,
                    relativePath = relativePath,
                    sha256 = Hashing.ComputeSha256(stagedPath),
                    byteLength = info.Length
                };
                _artifacts.Add(artifact);
                return true;
            }
            catch (Exception exception)
            {
                _kinds.Remove(kind);
                try { if (File.Exists(stagedPath)) File.Delete(stagedPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                error = $"Artifact staging failed: {exception.Message}";
                artifact = null;
                return false;
            }
        }
    }

    public sealed class ChunkArtifactPromotion : IDisposable
    {
        private bool _payloadPromoted;
        private bool _committed;
        private bool _disposed;

        internal ChunkArtifactPromotion(WorldStore owner, string worldId, string chunkId,
            int chunkRevision, ChunkArtifactKind kind, int formatVersion,
            string artifactFileName, string sourcePath, long expectedLength,
            string expectedSha256)
        {
            Owner = owner;
            WorldId = worldId;
            ChunkId = chunkId;
            ChunkRevision = chunkRevision;
            string transactionId = "enhancement-" + chunkId + "-" +
                                   chunkRevision.ToString("D10") + "-" +
                                   Guid.NewGuid().ToString("N");
            StagingDirectory = owner.GetTransactionDirectory(worldId, chunkId,
                chunkRevision, transactionId);
            FinalDirectory = StoragePath.CombineContained(owner.GetWorldDirectory(worldId),
                WorldStore.ChunksDirectoryName, chunkId, "enhancements",
                chunkRevision.ToString("D10"), kind.ToString().ToLowerInvariant() + "-" +
                expectedSha256);
            Directory.CreateDirectory(StagingDirectory);
            string stagedPath = StoragePath.CombineContained(StagingDirectory,
                artifactFileName.Split('/'));
            string stagedParent = Path.GetDirectoryName(stagedPath);
            if (!string.IsNullOrEmpty(stagedParent)) Directory.CreateDirectory(stagedParent);
            using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read,
                       FileShare.Read, 1024 * 1024, FileOptions.SequentialScan))
            using (var destination = new FileStream(stagedPath, FileMode.CreateNew,
                       FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.WriteThrough))
            {
                source.CopyTo(destination, 1024 * 1024);
                destination.Flush(true);
            }
            var info = new FileInfo(stagedPath);
            string actualSha256 = Hashing.ComputeSha256(stagedPath);
            if (info.Length != expectedLength || !string.Equals(actualSha256,
                    expectedSha256, StringComparison.Ordinal))
                throw new InvalidDataException(
                    "Staged artifact changed relative to its verified descriptor.");
            string relative = $"{WorldStore.ChunksDirectoryName}/{chunkId}/enhancements/" +
                              $"{chunkRevision:D10}/{kind.ToString().ToLowerInvariant()}-" +
                              $"{expectedSha256}/{artifactFileName}";
            Artifact = new ChunkArtifactRecord
            {
                kind = kind,
                formatVersion = formatVersion,
                chunkRevision = chunkRevision,
                relativePath = relative,
                sha256 = expectedSha256,
                byteLength = expectedLength
            };
            StagedArtifactPath = stagedPath;
            FinalArtifactPath = StoragePath.CombineContained(FinalDirectory,
                artifactFileName.Split('/'));
        }

        internal WorldStore Owner { get; }
        public string WorldId { get; }
        public string ChunkId { get; }
        public int ChunkRevision { get; }
        public string StagingDirectory { get; }
        public string FinalDirectory { get; }
        public string StagedArtifactPath { get; }
        public string FinalArtifactPath { get; }
        public ChunkArtifactRecord Artifact { get; }

        public bool TryCommit(WorldManifest manifest, ChunkRecord chunk,
            long unixMilliseconds, out string error)
        {
            if (_disposed || _committed)
            {
                error = "Chunk artifact promotion is already closed.";
                return false;
            }
            return Owner.TryCommitChunkArtifactPromotion(this, manifest, chunk,
                unixMilliseconds, out error);
        }

        internal bool TryPromotePayload(out bool newlyPromoted, out string error)
        {
            newlyPromoted = false;
            error = null;
            try
            {
                if (Directory.Exists(FinalDirectory))
                {
                    if (!File.Exists(FinalArtifactPath) ||
                        new FileInfo(FinalArtifactPath).Length != Artifact.byteLength ||
                        !string.Equals(Hashing.ComputeSha256(FinalArtifactPath),
                            Artifact.sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        error = "Content-addressed artifact destination is inconsistent.";
                        return false;
                    }
                    return true;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(FinalDirectory));
                Directory.Move(StagingDirectory, FinalDirectory);
                _payloadPromoted = true;
                newlyPromoted = true;
                return true;
            }
            catch (Exception exception)
            {
                error = "Artifact payload promotion failed: " + exception.Message;
                return false;
            }
        }

        internal void MarkCommitted()
        {
            _committed = true;
            _payloadPromoted = false;
            TryDeleteDirectory(StagingDirectory);
        }

        internal void TryRollbackPayloadPromotion()
        {
            if (!_payloadPromoted || !Directory.Exists(FinalDirectory) ||
                Directory.Exists(StagingDirectory))
                return;
            try
            {
                Directory.Move(FinalDirectory, StagingDirectory);
                _payloadPromoted = false;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (!_committed) TryRollbackPayloadPromotion();
            TryDeleteDirectory(StagingDirectory);
        }

        private static void TryDeleteDirectory(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// Read-only compatibility descriptor for QRS v1 package directories. The existing
    /// RoomScanPersistence loader remains the authority for actually restoring the volume;
    /// this reader validates enough of scan.bin to safely expose it to a world migration UI.
    /// </summary>
    public sealed class LegacyScanPackageInfo
    {
        public string PackageId { get; internal set; }
        public string PackageDirectory { get; internal set; }
        public string ScanBinaryPath { get; internal set; }
        public long TimestampUnixSeconds { get; internal set; }
        public Vector3Int VoxelCount { get; internal set; }
        public float VoxelSize { get; internal set; }
        public int IntegrationCount { get; internal set; }
        public int TriplanarResolution { get; internal set; }
        public BoundsData LocalBounds { get; internal set; }
    }

    public static class LegacyScanPackageReader
    {
        private const uint Magic = 0x48534D52;
        private const int FormatVersion = 1;
        private const int MaximumVoxelAxis = 4096;

        public static bool TryOpen(string legacyRoomScansRoot, string packageId,
            out LegacyScanPackageInfo package, out string error)
        {
            package = null;
            error = null;
            if (string.IsNullOrWhiteSpace(legacyRoomScansRoot) ||
                !StoragePath.IsSafeIdentifier(packageId, 96))
            {
                error = "Legacy package path or identifier is invalid.";
                return false;
            }

            try
            {
                string root = Path.GetFullPath(legacyRoomScansRoot);
                string packageDirectory = StoragePath.CombineContained(root, packageId);
                string scanPath = StoragePath.CombineContained(packageDirectory, "scan.bin");
                if (!File.Exists(scanPath))
                {
                    error = "Legacy package has no scan.bin.";
                    return false;
                }

                using var stream = new FileStream(scanPath, FileMode.Open, FileAccess.Read,
                    FileShare.Read, 65536, FileOptions.SequentialScan);
                using var reader = new BinaryReader(stream, Encoding.UTF8, true);
                if (reader.ReadUInt32() != Magic)
                    throw new InvalidDataException("Legacy scan magic is invalid.");
                if (reader.ReadInt32() != FormatVersion)
                    throw new InvalidDataException("Legacy scan version is unsupported.");
                long timestamp = reader.ReadInt64();
                int x = reader.ReadInt32();
                int y = reader.ReadInt32();
                int z = reader.ReadInt32();
                float voxelSize = reader.ReadSingle();
                int integrationCount = reader.ReadInt32();
                int triplanarResolution = reader.ReadInt32();

                if (timestamp < 0 || x <= 0 || y <= 0 || z <= 0 ||
                    x > MaximumVoxelAxis || y > MaximumVoxelAxis || z > MaximumVoxelAxis ||
                    !IsFinite(voxelSize) || voxelSize <= 0f || voxelSize > 100f ||
                    integrationCount < 0 || triplanarResolution < 0)
                    throw new InvalidDataException("Legacy scan header is outside supported limits.");

                for (int i = 0; i < 16; i++)
                {
                    if (!IsFinite(reader.ReadSingle()))
                        throw new InvalidDataException("Legacy anchor matrix is not finite.");
                }
                int tsdfLength = reader.ReadInt32();
                ValidatePayloadLength(stream, tsdfLength, "TSDF");
                stream.Seek(tsdfLength, SeekOrigin.Current);
                int colorLength = reader.ReadInt32();
                ValidatePayloadLength(stream, colorLength, "color");
                if (stream.Position + colorLength != stream.Length)
                    throw new InvalidDataException("Legacy color payload length is inconsistent.");

                package = new LegacyScanPackageInfo
                {
                    PackageId = packageId,
                    PackageDirectory = packageDirectory,
                    ScanBinaryPath = scanPath,
                    TimestampUnixSeconds = timestamp,
                    VoxelCount = new Vector3Int(x, y, z),
                    VoxelSize = voxelSize,
                    IntegrationCount = integrationCount,
                    TriplanarResolution = triplanarResolution,
                    LocalBounds = new BoundsData(Vector3.zero,
                        new Vector3(x * voxelSize, y * voxelSize, z * voxelSize) * 0.5f)
                };
                return true;
            }
            catch (Exception exception)
            {
                error = $"Legacy package rejected: {exception.Message}";
                return false;
            }
        }

        private static void ValidatePayloadLength(Stream stream, int length, string label)
        {
            // Legacy fields are signed 32-bit, already below the world artifact byte cap.
            if (length < 0 || stream.Position + length > stream.Length)
                throw new InvalidDataException($"Legacy {label} payload length is invalid.");
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    internal static class AtomicUtf8File
    {
        public static bool TryWrite(string destinationPath, string backupPath, string contents,
            bool preserveDestinationAsBackup, out string error)
        {
            error = null;
            string directory = Path.GetDirectoryName(destinationPath);
            if (string.IsNullOrEmpty(directory))
            {
                error = "Atomic destination has no parent directory.";
                return false;
            }
            Directory.CreateDirectory(directory);
            string pendingPath = destinationPath + ".pending-" + Guid.NewGuid().ToString("N");
            string backupPendingPath = backupPath + ".pending-" + Guid.NewGuid().ToString("N");
            try
            {
                WriteDurable(pendingPath, Encoding.UTF8.GetBytes(contents));
                if (preserveDestinationAsBackup && File.Exists(destinationPath))
                {
                    CopyDurable(destinationPath, backupPendingPath);
                    ReplaceOrMove(backupPendingPath, backupPath);
                }
                ReplaceOrMove(pendingPath, destinationPath);
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
            finally
            {
                TryDelete(pendingPath);
                TryDelete(backupPendingPath);
            }
        }

        private static void WriteDurable(string path, byte[] bytes)
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 65536, FileOptions.WriteThrough);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(true);
        }

        private static void CopyDurable(string sourcePath, string destinationPath)
        {
            using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read,
                FileShare.Read, 65536, FileOptions.SequentialScan);
            using var destination = new FileStream(destinationPath, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough);
            source.CopyTo(destination, 65536);
            destination.Flush(true);
        }

        private static void ReplaceOrMove(string sourcePath, string destinationPath)
        {
            if (!File.Exists(destinationPath))
            {
                File.Move(sourcePath, destinationPath);
                return;
            }

            try
            {
                File.Replace(sourcePath, destinationPath, null);
                return;
            }
            catch (PlatformNotSupportedException) { }
            catch (IOException) { }

            string displacedPath = destinationPath + ".displaced-" +
                                   Guid.NewGuid().ToString("N");
            File.Move(destinationPath, displacedPath);
            try
            {
                File.Move(sourcePath, destinationPath);
                TryDelete(displacedPath);
            }
            catch
            {
                if (!File.Exists(destinationPath) && File.Exists(displacedPath))
                    File.Move(displacedPath, destinationPath);
                throw;
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal static class StoragePath
    {
        public static bool IsSafeIdentifier(string value, int maximumLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length > maximumLength ||
                !IsAsciiAlphaNumeric(value[0]) ||
                !IsAsciiAlphaNumeric(value[value.Length - 1]) || value.Contains(".."))
                return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!IsAsciiAlphaNumeric(c) && c != '-' && c != '_' && c != '.')
                    return false;
            }
            return true;
        }

        public static bool IsSafeRelativePath(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 512 || value[0] == '/' ||
                value.Contains('\\') || value.Contains(':') || value.Contains("//"))
                return false;
            string[] segments = value.Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                if (segments[i].Length == 0 || segments[i] == "." || segments[i] == "..")
                    return false;
                for (int j = 0; j < segments[i].Length; j++)
                {
                    if (char.IsControl(segments[i][j]))
                        return false;
                }
            }
            return true;
        }

        public static string CombineContained(string root, params string[] segments)
        {
            string rootFull = Path.GetFullPath(root);
            string combined = rootFull;
            for (int i = 0; i < segments.Length; i++)
                combined = Path.Combine(combined, segments[i]);
            string candidateFull = Path.GetFullPath(combined);
            string prefix = rootFull.TrimEnd(Path.DirectorySeparatorChar,
                                Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            StringComparison comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (!candidateFull.StartsWith(prefix, comparison))
                throw new InvalidOperationException("Resolved path escapes its storage root.");
            return candidateFull;
        }

        public static bool IsContained(string root, string candidate)
        {
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(candidate))
                return false;
            string rootFull = Path.GetFullPath(root);
            string candidateFull = Path.GetFullPath(candidate);
            string prefix = rootFull.TrimEnd(Path.DirectorySeparatorChar,
                                Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            StringComparison comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return candidateFull.StartsWith(prefix, comparison);
        }

        private static bool IsAsciiAlphaNumeric(char value)
        {
            return value >= 'a' && value <= 'z' || value >= 'A' && value <= 'Z' ||
                   value >= '0' && value <= '9';
        }
    }

    internal static class Hashing
    {
        public static string ComputeSha256(string path)
        {
            using var algorithm = SHA256.Create();
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
            byte[] hash = algorithm.ComputeHash(stream);
            var builder = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++)
                builder.Append(hash[i].ToString("x2"));
            return builder.ToString();
        }

        public static bool IsLowerSha256(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 64) return false;
            for (int i = 0; i < value.Length; i++)
                if (!(value[i] >= '0' && value[i] <= '9') &&
                    !(value[i] >= 'a' && value[i] <= 'f'))
                    return false;
            return true;
        }
    }
}
