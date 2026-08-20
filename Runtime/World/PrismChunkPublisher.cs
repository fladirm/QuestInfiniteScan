using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Genesis.RoomScan.World
{
    public sealed class PrismChunkPublishResult
    {
        public bool Success { get; internal set; }
        public string Error { get; internal set; }
        public int Revision { get; internal set; }
        public ChunkArtifactRecord CanonicalArtifact { get; internal set; }
    }

    /// <summary>
    /// Thin PRISM payload adapter over the existing proven WorldStore transaction and
    /// manifest revision machinery. Large encoding/hashing stays on a worker; only the
    /// short monotonic manifest mutation runs on the Unity continuation context.
    /// </summary>
    public static class PrismChunkPublisher
    {
        public static async Task<PrismChunkPublishResult> PublishAsync(WorldStore store,
            WorldManifest manifest, ChunkRecord chunk,
            PrismCanonicalChunkSnapshot snapshot, long unixMilliseconds,
            ChunkLifecycleState? stateAfterPublish = null)
        {
            string error = Validate(store, manifest, chunk, snapshot,
                unixMilliseconds, stateAfterPublish);
            if (error != null) return Failure(error);
            int revision = checked(chunk.revision + 1);
            if (!store.TryBeginChunkRevision(manifest.worldId, chunk.chunkId,
                    revision, out ChunkRevisionTransaction transaction, out error))
                return Failure(error);

            using (transaction)
            {
                string codecError = null;
                ChunkArtifactRecord canonical = null;
                bool staged = await Task.Run(() => transaction.TryStageStream(
                    ChunkArtifactKind.PrismCanonical,
                    PrismCanonicalChunkCodec.FormatVersion, "canonical.prism", stream =>
                    {
                        if (!PrismCanonicalChunkCodec.TryWrite(stream, snapshot,
                                out codecError))
                            throw new InvalidDataException(codecError);
                    }, out canonical, out error));
                if (!staged) return Failure(codecError ?? error);

                int previousChunkRevision = chunk.revision;
                long previousChunkUpdated = chunk.updatedUnixMilliseconds;
                ChunkLifecycleState previousState = chunk.state;
                List<ChunkArtifactRecord> previousArtifacts = chunk.artifacts;
                int previousWorldRevision = manifest.revision;
                long previousWorldUpdated = manifest.updatedUnixMilliseconds;
                chunk.revision = revision;
                chunk.updatedUnixMilliseconds = unixMilliseconds;
                if (stateAfterPublish.HasValue &&
                    chunk.state == ChunkLifecycleState.Finalizing)
                    chunk.state = stateAfterPublish.Value;
                chunk.artifacts = ReplaceCanonical(previousArtifacts, canonical);
                manifest.revision = checked(manifest.revision + 1);
                manifest.updatedUnixMilliseconds = unixMilliseconds;
                if (!transaction.TryCommit(manifest, out string commitError))
                {
                    chunk.revision = previousChunkRevision;
                    chunk.updatedUnixMilliseconds = previousChunkUpdated;
                    chunk.state = previousState;
                    chunk.artifacts = previousArtifacts;
                    manifest.revision = previousWorldRevision;
                    manifest.updatedUnixMilliseconds = previousWorldUpdated;
                    return Failure(commitError);
                }
                return new PrismChunkPublishResult
                {
                    Success = true,
                    Revision = revision,
                    CanonicalArtifact = canonical
                };
            }
        }

        private static string Validate(WorldStore store, WorldManifest manifest,
            ChunkRecord chunk, PrismCanonicalChunkSnapshot snapshot,
            long unixMilliseconds, ChunkLifecycleState? stateAfterPublish)
        {
            if (store == null || manifest == null || chunk == null || snapshot == null)
                return "Store, manifest, chunk, and PRISM snapshot are required.";
            if (manifest.chunks == null || !manifest.chunks.Contains(chunk))
                return "Chunk is not part of the supplied world.";
            if (chunk.state != ChunkLifecycleState.Active &&
                chunk.state != ChunkLifecycleState.Finalizing &&
                chunk.state != ChunkLifecycleState.Persisted &&
                chunk.state != ChunkLifecycleState.Cached)
                return "Only live or durable chunks can publish PRISM state.";
            if (chunk.revision == int.MaxValue || manifest.revision == int.MaxValue)
                return "PRISM revision is exhausted.";
            if (unixMilliseconds < chunk.updatedUnixMilliseconds ||
                unixMilliseconds < manifest.updatedUnixMilliseconds)
                return "PRISM publication timestamp is stale.";
            if (stateAfterPublish.HasValue &&
                stateAfterPublish != ChunkLifecycleState.Persisted &&
                stateAfterPublish != ChunkLifecycleState.Cached)
                return "PRISM publication may finish only as persisted or cached.";
            return PrismCanonicalChunkCodec.TryValidate(snapshot,
                out string validationError) ? null : validationError;
        }

        private static List<ChunkArtifactRecord> ReplaceCanonical(
            List<ChunkArtifactRecord> existing, ChunkArtifactRecord canonical)
        {
            var result = new List<ChunkArtifactRecord>();
            if (existing != null)
                foreach (ChunkArtifactRecord artifact in existing)
                    if (artifact != null &&
                        artifact.kind != ChunkArtifactKind.PrismCanonical)
                        result.Add(artifact);
            result.Add(canonical);
            return result;
        }

        private static PrismChunkPublishResult Failure(string error) => new()
        {
            Success = false,
            Error = string.IsNullOrEmpty(error) ? "PRISM publication failed." : error
        };
    }
}
