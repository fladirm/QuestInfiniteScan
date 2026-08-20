using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Genesis.RoomScan.World;

namespace Genesis.RoomScan.HeavyCompute
{
    public enum DiffSoupArtifactPublishFailure
    {
        None = 0,
        InvalidArtifact = 1,
        StaleRevision = 2,
        ImmutableConflict = 3,
        TransientStorage = 4,
        Canceled = 5
    }

    public sealed class DiffSoupArtifactPublishResult
    {
        public bool Success => Data != null && Artifact != null && string.IsNullOrEmpty(Error);
        public string Error { get; internal set; }
        public DiffSoupArtifactPublishFailure Failure { get; internal set; }
        public DiffSoupArtifactData Data { get; internal set; }
        public ChunkArtifactRecord Artifact { get; internal set; }
    }

    /// <summary>
    /// Validates an untrusted downloaded artifact completely before content-addressed staging,
    /// then commits its manifest reference only while the originating chunk revision is still
    /// current. No renderer state is touched here: callers may swap renderers only after a
    /// successful result, so malformed, interrupted, and late jobs leave the prior view intact.
    /// </summary>
    public static class DiffSoupArtifactPublisher
    {
        private sealed class StageResult
        {
            public ChunkArtifactPromotion Promotion;
            public string Error;
        }

        public static async Task<DiffSoupArtifactPublishResult> PublishAsync(
            WorldStore store, WorldManifest manifest, ChunkRecord chunk,
            HeavyComputeQueueItem job, string downloadedArtifactPath,
            long unixMilliseconds, DiffSoupImportLimits limits = null,
            CancellationToken cancellationToken = default)
        {
            string error = ValidatePreflight(store, manifest, chunk, job,
                downloadedArtifactPath, unixMilliseconds, out var preflightFailure);
            if (error != null)
                return Failure(error, preflightFailure);

            DiffSoupArtifactImportResult imported;
            try
            {
                imported = await Task.Run(() => DiffSoupArtifactImporter.Import(
                    downloadedArtifactPath, job.submission, job.artifactBundle, limits),
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return Failure("DiffSoup artifact publication was canceled.",
                    DiffSoupArtifactPublishFailure.Canceled);
            }
            if (!imported.Success)
                return Failure(imported.Error,
                    DiffSoupArtifactPublishFailure.InvalidArtifact);

            // Import can be expensive. Recheck identity on the continuation context before
            // copying, and once more immediately before the atomic metadata commit.
            error = ValidatePreflight(store, manifest, chunk, job,
                downloadedArtifactPath, unixMilliseconds, out preflightFailure);
            if (error != null)
                return Failure(error, preflightFailure);

            StageResult staged;
            try
            {
                staged = await Task.Run(() => Stage(store, manifest.worldId, chunk.chunkId,
                    chunk.revision, downloadedArtifactPath, job.artifactBundle),
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return Failure("DiffSoup artifact publication was canceled.",
                    DiffSoupArtifactPublishFailure.Canceled);
            }
            if (staged.Promotion == null)
                return Failure(staged.Error, IsStale(staged.Error)
                    ? DiffSoupArtifactPublishFailure.StaleRevision
                    : DiffSoupArtifactPublishFailure.TransientStorage);

            using (staged.Promotion)
            {
                error = ValidatePreflight(store, manifest, chunk, job,
                    downloadedArtifactPath, unixMilliseconds, out preflightFailure);
                if (error != null)
                    return Failure(error, preflightFailure);
                if (!staged.Promotion.TryCommit(manifest, chunk, unixMilliseconds,
                        out string commitError))
                    return Failure(commitError, IsStale(commitError)
                        ? DiffSoupArtifactPublishFailure.StaleRevision
                        : commitError?.IndexOf("different immutable artifact",
                            StringComparison.OrdinalIgnoreCase) >= 0
                            ? DiffSoupArtifactPublishFailure.ImmutableConflict
                            : DiffSoupArtifactPublishFailure.TransientStorage);
                return new DiffSoupArtifactPublishResult
                {
                    Data = imported.Data,
                    Artifact = staged.Promotion.Artifact
                };
            }
        }

        private static StageResult Stage(WorldStore store, string worldId, string chunkId,
            int revision, string sourcePath, HeavyComputeBlobDescriptor descriptor)
        {
            if (!store.TryBeginChunkArtifactPromotion(worldId, chunkId, revision,
                    ChunkArtifactKind.DiffSoup,
                    HeavyComputeProtocol.DiffSoupArtifactVersion, "diffsoup.zip", sourcePath,
                    descriptor.byteLength, descriptor.sha256,
                    out ChunkArtifactPromotion promotion, out string error))
                return new StageResult { Error = error };
            return new StageResult { Promotion = promotion };
        }

        private static string ValidatePreflight(WorldStore store, WorldManifest manifest,
            ChunkRecord chunk, HeavyComputeQueueItem job, string path,
            long unixMilliseconds, out DiffSoupArtifactPublishFailure failure)
        {
            failure = DiffSoupArtifactPublishFailure.InvalidArtifact;
            if (store == null || manifest == null || chunk == null || job?.submission == null)
                return "Store, manifest, chunk, and completed job are required.";
            if (manifest.chunks == null || !manifest.chunks.Contains(chunk))
                return "Chunk is not part of the supplied world manifest.";
            if (job.localState != HeavyComputeLocalState.Ready)
                return "DiffSoup job has not completed its verified download.";
            if (!HeavyComputeContract.TryValidateSubmission(job.submission, true,
                    out string submissionError))
                return "DiffSoup submission is invalid: " + submissionError;
            if (!HeavyComputeContract.TryValidateBlob(job.artifactBundle,
                    HeavyComputeProtocol.DiffSoupArtifactMediaType,
                    HeavyComputeProtocol.DiffSoupArtifactVersion,
                    HeavyComputeProtocol.MaximumArtifactBytes, out string artifactError))
                return "DiffSoup artifact descriptor is invalid: " + artifactError;
            HeavyComputeJobKey key = job.submission.key;
            if (!string.Equals(key.worldId, manifest.worldId, StringComparison.Ordinal) ||
                !string.Equals(key.chunkId, chunk.chunkId, StringComparison.Ordinal) ||
                key.chunkRevision != chunk.revision)
            {
                failure = DiffSoupArtifactPublishFailure.StaleRevision;
                return "DiffSoup artifact is stale for the current chunk revision.";
            }
            if (unixMilliseconds < manifest.updatedUnixMilliseconds ||
                unixMilliseconds < chunk.updatedUnixMilliseconds)
            {
                failure = DiffSoupArtifactPublishFailure.StaleRevision;
                return "DiffSoup artifact publication timestamp is stale.";
            }
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return "Downloaded DiffSoup artifact is missing.";
            return null;
        }

        private static bool IsStale(string error) =>
            !string.IsNullOrEmpty(error) &&
            (error.IndexOf("stale", StringComparison.OrdinalIgnoreCase) >= 0 ||
             error.IndexOf("current chunk revision", StringComparison.OrdinalIgnoreCase) >= 0 ||
             error.IndexOf("durable chunk revision", StringComparison.OrdinalIgnoreCase) >= 0);

        private static DiffSoupArtifactPublishResult Failure(string error,
            DiffSoupArtifactPublishFailure failure) =>
            new()
            {
                Error = string.IsNullOrEmpty(error)
                    ? "DiffSoup artifact publication failed."
                    : error,
                Failure = failure
            };
    }
}
