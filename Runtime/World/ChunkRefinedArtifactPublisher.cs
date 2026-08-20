using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Genesis.RoomScan.World
{
    public sealed class ChunkRefinedPublishResult
    {
        public bool Success { get; internal set; }
        public string Error { get; internal set; }
        public int Revision { get; internal set; }
        public ChunkArtifactRecord MeshArtifact { get; internal set; }
        public ChunkArtifactRecord AtlasArtifact { get; internal set; }
        public ChunkArtifactRecord NormalArtifact { get; internal set; }
    }

    /// <summary>
    /// Atomically publishes one chunk-local PBR-ready mesh/atlas set as a monotonic chunk
    /// revision. Existing mapper, keyframe, DiffSoup, and export artifacts are preserved.
    /// </summary>
    internal static class ChunkRefinedArtifactPublisher
    {
        private sealed class StageResult
        {
            public bool Success;
            public string Error;
            public ChunkArtifactRecord Mesh;
            public ChunkArtifactRecord Atlas;
            public ChunkArtifactRecord Normal;
        }

        internal static async Task<ChunkRefinedPublishResult> PublishAsync(WorldStore store,
            WorldManifest manifest, ChunkRecord chunk, RefinedTextureResult refined,
            long unixMilliseconds)
        {
            string preflightError = ValidatePreflight(store, manifest, chunk, refined,
                unixMilliseconds);
            if (preflightError != null)
                return Failure(preflightError);

            int newChunkRevision = chunk.revision + 1;
            if (!store.TryBeginChunkRevision(manifest.worldId, chunk.chunkId,
                    newChunkRevision, out ChunkRevisionTransaction transaction,
                    out string beginError))
                return Failure(beginError);

            using (transaction)
            {
                StageResult staged = await Task.Run(() => Stage(transaction, refined));
                if (!staged.Success)
                    return Failure(staged.Error);

                int previousChunkRevision = chunk.revision;
                long previousChunkUpdated = chunk.updatedUnixMilliseconds;
                int previousWorldRevision = manifest.revision;
                long previousWorldUpdated = manifest.updatedUnixMilliseconds;
                List<ChunkArtifactRecord> previousArtifacts = chunk.artifacts;

                chunk.revision = newChunkRevision;
                chunk.updatedUnixMilliseconds = unixMilliseconds;
                chunk.artifacts = ReplaceRefinedArtifacts(previousArtifacts, staged.Mesh,
                    staged.Atlas, staged.Normal);
                manifest.revision++;
                manifest.updatedUnixMilliseconds = unixMilliseconds;

                if (!transaction.TryCommit(manifest, out string commitError))
                {
                    chunk.revision = previousChunkRevision;
                    chunk.updatedUnixMilliseconds = previousChunkUpdated;
                    chunk.artifacts = previousArtifacts;
                    manifest.revision = previousWorldRevision;
                    manifest.updatedUnixMilliseconds = previousWorldUpdated;
                    return Failure(commitError);
                }

                return new ChunkRefinedPublishResult
                {
                    Success = true,
                    Revision = newChunkRevision,
                    MeshArtifact = staged.Mesh,
                    AtlasArtifact = staged.Atlas,
                    NormalArtifact = staged.Normal
                };
            }
        }

        private static StageResult Stage(ChunkRevisionTransaction transaction,
            RefinedTextureResult refined)
        {
            string codecError = null;
            if (!transaction.TryStageStream(ChunkArtifactKind.RefinedMesh,
                    ChunkRefinedArtifactCodec.MeshFormatVersion, "refined_mesh.qirm", stream =>
                    {
                        if (!ChunkRefinedArtifactCodec.TryWriteMesh(stream, refined,
                                out codecError))
                            throw new InvalidDataException(codecError);
                    }, out ChunkArtifactRecord meshArtifact, out string meshError))
                return new StageResult { Error = codecError ?? meshError };

            if (!transaction.TryStageStream(ChunkArtifactKind.RefinedAtlas,
                    ChunkRefinedArtifactCodec.TextureFormatVersion, "refined_atlas.qirt",
                    stream =>
                    {
                        if (!ChunkRefinedArtifactCodec.TryWriteRgbaTexture(stream,
                                refined.AtlasPixels, refined.AtlasWidth, refined.AtlasHeight,
                                out codecError))
                            throw new InvalidDataException(codecError);
                    }, out ChunkArtifactRecord atlasArtifact, out string atlasError))
                return new StageResult { Error = codecError ?? atlasError };

            ChunkArtifactRecord normalArtifact = null;
            if (refined.NormalPixels != null &&
                !transaction.TryStageStream(ChunkArtifactKind.RefinedNormal,
                    ChunkRefinedArtifactCodec.TextureFormatVersion, "refined_normal.qirt",
                    stream =>
                    {
                        if (!ChunkRefinedArtifactCodec.TryWriteRgbaTexture(stream,
                                refined.NormalPixels, refined.AtlasWidth, refined.AtlasHeight,
                                out codecError))
                            throw new InvalidDataException(codecError);
                    }, out normalArtifact, out string normalError))
                return new StageResult { Error = codecError ?? normalError };

            return new StageResult
            {
                Success = true,
                Mesh = meshArtifact,
                Atlas = atlasArtifact,
                Normal = normalArtifact
            };
        }

        private static string ValidatePreflight(WorldStore store, WorldManifest manifest,
            ChunkRecord chunk, RefinedTextureResult refined, long unixMilliseconds)
        {
            if (store == null || manifest == null || chunk == null)
                return "Store, manifest, and chunk are required.";
            if (manifest.chunks == null || !manifest.chunks.Contains(chunk))
                return "Chunk is not part of the supplied world manifest.";
            if (chunk.state == ChunkLifecycleState.New ||
                chunk.state == ChunkLifecycleState.Failed)
                return "An uninitialized or failed chunk cannot publish refined artifacts.";
            if (manifest.revision == int.MaxValue || chunk.revision == int.MaxValue)
                return "World or chunk revision is exhausted.";
            if (unixMilliseconds < manifest.updatedUnixMilliseconds ||
                unixMilliseconds < chunk.updatedUnixMilliseconds)
                return "Refined artifact publication timestamp is stale.";
            if (!ChunkRefinedArtifactCodec.TryValidateMesh(refined, out string meshError))
                return meshError;
            if (!ChunkRefinedArtifactCodec.TryValidateRgbaTexture(
                    refined.AtlasPixels, refined.AtlasWidth, refined.AtlasHeight,
                    out string atlasError))
                return atlasError;
            if (refined.NormalPixels != null)
            {
                if (!ChunkRefinedArtifactCodec.TryValidateRgbaTexture(
                        refined.NormalPixels, refined.AtlasWidth, refined.AtlasHeight,
                        out string normalError))
                    return normalError;
            }
            return null;
        }

        private static List<ChunkArtifactRecord> ReplaceRefinedArtifacts(
            List<ChunkArtifactRecord> existing, ChunkArtifactRecord mesh,
            ChunkArtifactRecord atlas, ChunkArtifactRecord normal)
        {
            var updated = new List<ChunkArtifactRecord>();
            if (existing != null)
            {
                for (int i = 0; i < existing.Count; i++)
                {
                    ChunkArtifactRecord artifact = existing[i];
                    if (artifact != null && artifact.kind != ChunkArtifactKind.RefinedMesh &&
                        artifact.kind != ChunkArtifactKind.RefinedAtlas &&
                        artifact.kind != ChunkArtifactKind.RefinedNormal)
                        updated.Add(artifact);
                }
            }
            updated.Add(mesh);
            updated.Add(atlas);
            if (normal != null)
                updated.Add(normal);
            return updated;
        }

        private static ChunkRefinedPublishResult Failure(string error)
        {
            return new ChunkRefinedPublishResult
            {
                Success = false,
                Error = string.IsNullOrEmpty(error)
                    ? "Chunk refined artifact publication failed."
                    : error
            };
        }
    }
}
