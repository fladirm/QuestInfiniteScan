using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace Genesis.RoomScan.World
{
    public sealed class ChunkSnapshotPublishResult
    {
        public bool Success { get; internal set; }
        public string Error { get; internal set; }
        public int Revision { get; internal set; }
        public ChunkArtifactRecord VolumeArtifact { get; internal set; }
        public ChunkArtifactRecord LiveMeshArtifact { get; internal set; }
        public ChunkArtifactRecord KeyframesArtifact { get; internal set; }
    }

    /// <summary>
    /// Publishes one finalized GPU snapshot as a new chunk revision. Large binary writes and
    /// hashing happen on a worker; the short in-memory manifest mutation and atomic commit run
    /// on the captured Unity context. Every mutation is restored if publication fails.
    /// </summary>
    public static class ChunkSnapshotPublisher
    {
        private sealed class StageResult
        {
            public bool Success;
            public string Error;
            public ChunkArtifactRecord Volume;
            public ChunkArtifactRecord Mesh;
            public ChunkArtifactRecord Keyframes;
        }

        public static async Task<ChunkSnapshotPublishResult> PublishAsync(WorldStore store,
            WorldManifest manifest, ChunkRecord chunk, ChunkGpuSnapshot snapshot,
            long unixMilliseconds, string keyframeDirectory = null,
            ChunkLifecycleState? stateAfterPublish = null)
        {
            string preflightError = ValidatePreflight(store, manifest, chunk, snapshot,
                unixMilliseconds, stateAfterPublish);
            if (preflightError != null)
                return Failure(preflightError);

            int newChunkRevision = chunk.revision + 1;
            if (!store.TryBeginChunkRevision(manifest.worldId, chunk.chunkId,
                    newChunkRevision, out ChunkRevisionTransaction transaction,
                    out string beginError))
                return Failure(beginError);

            using (transaction)
            {
                StageResult staged = await Task.Run(() => StageSnapshot(transaction, snapshot,
                    keyframeDirectory));
                if (!staged.Success)
                    return Failure(staged.Error);

                int previousChunkRevision = chunk.revision;
                long previousChunkUpdated = chunk.updatedUnixMilliseconds;
                ChunkLifecycleState previousChunkState = chunk.state;
                int previousWorldRevision = manifest.revision;
                long previousWorldUpdated = manifest.updatedUnixMilliseconds;
                List<ChunkArtifactRecord> previousArtifacts = chunk.artifacts;

                chunk.revision = newChunkRevision;
                chunk.updatedUnixMilliseconds = unixMilliseconds;
                // A previously-finalizing chunk may have been reactivated while its bytes
                // were being staged. Never let a late background write demote that active
                // chunk; otherwise the world would temporarily have no active vertex.
                if (stateAfterPublish.HasValue &&
                    chunk.state == ChunkLifecycleState.Finalizing)
                    chunk.state = stateAfterPublish.Value;
                chunk.artifacts = ReplaceSnapshotArtifacts(previousArtifacts,
                    staged.Volume, staged.Mesh, staged.Keyframes);
                manifest.revision++;
                manifest.updatedUnixMilliseconds = unixMilliseconds;

                if (!transaction.TryCommit(manifest, out string commitError))
                {
                    chunk.revision = previousChunkRevision;
                    chunk.updatedUnixMilliseconds = previousChunkUpdated;
                    chunk.state = previousChunkState;
                    chunk.artifacts = previousArtifacts;
                    manifest.revision = previousWorldRevision;
                    manifest.updatedUnixMilliseconds = previousWorldUpdated;
                    return Failure(commitError);
                }

                return new ChunkSnapshotPublishResult
                {
                    Success = true,
                    Revision = newChunkRevision,
                    VolumeArtifact = staged.Volume,
                    LiveMeshArtifact = staged.Mesh,
                    KeyframesArtifact = staged.Keyframes
                };
            }
        }

        private static StageResult StageSnapshot(ChunkRevisionTransaction transaction,
            ChunkGpuSnapshot snapshot, string keyframeDirectory)
        {
            string codecError = null;
            if (!transaction.TryStageStream(ChunkArtifactKind.Volume,
                    ChunkSnapshotCodec.VolumeFormatVersion, "volume.qisv", stream =>
                    {
                        if (!ChunkSnapshotCodec.TryWriteVolume(stream, snapshot.Volume,
                                out codecError))
                            throw new InvalidDataException(codecError);
                    }, out ChunkArtifactRecord volumeArtifact, out string volumeError))
                return new StageResult { Error = codecError ?? volumeError };

            ChunkArtifactRecord meshArtifact = null;
            if (snapshot.LiveMesh != null &&
                !transaction.TryStageStream(ChunkArtifactKind.LiveMesh,
                    ChunkSnapshotCodec.LiveMeshFormatVersion, "live_mesh.qism", stream =>
                    {
                        if (!ChunkSnapshotCodec.TryWriteLiveMesh(stream, snapshot.LiveMesh,
                                out codecError))
                            throw new InvalidDataException(codecError);
                    }, out meshArtifact, out string meshError))
                return new StageResult { Error = codecError ?? meshError };

            ChunkArtifactRecord keyframesArtifact = null;
            string keyframeManifest = string.IsNullOrEmpty(keyframeDirectory)
                ? null
                : Path.Combine(keyframeDirectory, "frames.jsonl");
            if (keyframeManifest != null && File.Exists(keyframeManifest) &&
                !transaction.TryStageStream(ChunkArtifactKind.Keyframes,
                    ChunkKeyframeArchive.FormatVersion, "keyframes.qisk", stream =>
                    {
                        if (!ChunkKeyframeArchive.TryWriteDirectory(stream, keyframeDirectory,
                                out codecError))
                            throw new InvalidDataException(codecError);
                    }, out keyframesArtifact, out string keyframesError))
                return new StageResult { Error = codecError ?? keyframesError };

            return new StageResult
            {
                Success = true,
                Volume = volumeArtifact,
                Mesh = meshArtifact,
                Keyframes = keyframesArtifact
            };
        }

        private static string ValidatePreflight(WorldStore store, WorldManifest manifest,
            ChunkRecord chunk, ChunkGpuSnapshot snapshot, long unixMilliseconds,
            ChunkLifecycleState? stateAfterPublish)
        {
            if (store == null || manifest == null || chunk == null || snapshot?.Volume == null)
                return "Store, manifest, chunk, and volume snapshot are required.";
            if (manifest.chunks == null || !manifest.chunks.Contains(chunk))
                return "Chunk is not part of the supplied world manifest.";
            if (chunk.state != ChunkLifecycleState.Active &&
                chunk.state != ChunkLifecycleState.Finalizing)
                return "Only an active/finalizing chunk can publish a mapper snapshot.";
            if (manifest.revision == int.MaxValue || chunk.revision == int.MaxValue)
                return "World or chunk revision is exhausted.";
            if (unixMilliseconds < manifest.updatedUnixMilliseconds ||
                unixMilliseconds < chunk.updatedUnixMilliseconds)
                return "Snapshot publication timestamp is stale.";
            if (stateAfterPublish.HasValue &&
                stateAfterPublish.Value != ChunkLifecycleState.Persisted &&
                stateAfterPublish.Value != ChunkLifecycleState.Cached)
                return "Snapshot publication can only finish as persisted or cached.";
            if (!PoseApproximately(chunk.worldFromChunk, snapshot.Volume.WorldFromVolume))
                return "Snapshot volume frame does not match the source chunk frame.";
            return null;
        }

        private static List<ChunkArtifactRecord> ReplaceSnapshotArtifacts(
            List<ChunkArtifactRecord> existing, ChunkArtifactRecord volume,
            ChunkArtifactRecord liveMesh, ChunkArtifactRecord keyframes)
        {
            var updated = new List<ChunkArtifactRecord>();
            if (existing != null)
            {
                for (int i = 0; i < existing.Count; i++)
                {
                    ChunkArtifactRecord artifact = existing[i];
                    if (artifact != null && artifact.kind != ChunkArtifactKind.Volume &&
                        artifact.kind != ChunkArtifactKind.LiveMesh &&
                        (keyframes == null || artifact.kind != ChunkArtifactKind.Keyframes))
                        updated.Add(artifact);
                }
            }
            updated.Add(volume);
            if (liveMesh != null)
                updated.Add(liveMesh);
            if (keyframes != null)
                updated.Add(keyframes);
            return updated;
        }

        private static bool PoseApproximately(RigidPoseData left, RigidPoseData right)
        {
            return Vector3.Distance(left.position, right.position) <= 0.001f &&
                   Quaternion.Angle(left.rotation, right.rotation) <= 0.05f;
        }

        private static ChunkSnapshotPublishResult Failure(string error)
        {
            return new ChunkSnapshotPublishResult
            {
                Success = false,
                Error = string.IsNullOrEmpty(error) ? "Chunk snapshot publication failed." : error
            };
        }
    }
}
