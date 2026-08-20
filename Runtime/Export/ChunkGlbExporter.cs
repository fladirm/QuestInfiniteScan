using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Genesis.RoomScan.World;

namespace Genesis.RoomScan.Exporting
{
    public enum ChunkGlbExportFailure
    {
        None = 0,
        InvalidRequest = 1,
        MissingSourceArtifact = 2,
        InvalidSourceArtifact = 3,
        StaleRevision = 4,
        ImmutableConflict = 5,
        Storage = 6,
        Canceled = 7
    }

    public sealed class ChunkGlbExportResult
    {
        public bool Success => Artifact != null && string.IsNullOrEmpty(Error);
        public string Error { get; internal set; }
        public ChunkGlbExportFailure Failure { get; internal set; }
        public ChunkArtifactRecord Artifact { get; internal set; }
        public ChunkGlbWriteResult Write { get; internal set; }
    }

    /// <summary>
    /// Resolves and hash-verifies the current QRS refined mesh/atlas/normal set, encodes
    /// it off the scan frame, then content-addresses and atomically promotes one GLB
    /// reference. A stale/corrupt/interrupted export never replaces a known-good GLB.
    /// </summary>
    public static class ChunkGlbExporter
    {
        private sealed class SourceSet
        {
            internal string MeshPath;
            internal string AtlasPath;
            internal string NormalPath;
        }

        private sealed class GeneratedFile
        {
            internal string Directory;
            internal string Path;
            internal long ByteLength;
            internal string Sha256;
            internal ChunkGlbWriteResult Write;
            internal string Error;
        }

        private sealed class PromotionResult
        {
            internal ChunkArtifactPromotion Promotion;
            internal string Error;
        }

        public static async Task<ChunkGlbExportResult> ExportRefinedAsync(
            WorldStore store, WorldManifest manifest, ChunkRecord chunk,
            ChunkGlbWriteOptions options, long unixMilliseconds,
            CancellationToken cancellationToken = default)
        {
            string preflight = ValidatePreflight(store, manifest, chunk,
                unixMilliseconds, out ChunkGlbExportFailure failure);
            if (preflight != null)
                return Failed(preflight, failure);
            if (!TryResolveSources(store, manifest.worldId, chunk, out SourceSet sources,
                    out string sourceError, out failure))
                return Failed(sourceError, failure);

            string workingRoot;
            try
            {
                workingRoot = store.GetChunkWorkingDirectory(manifest.worldId,
                    chunk.chunkId);
            }
            catch (Exception exception)
            {
                return Failed("GLB working path is invalid: " + exception.Message,
                    ChunkGlbExportFailure.Storage);
            }

            GeneratedFile generated;
            try
            {
                generated = await Task.Run(() => Generate(sources, workingRoot,
                    chunk.chunkId, chunk.revision, options, cancellationToken),
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return Failed("Chunk GLB export was canceled.",
                    ChunkGlbExportFailure.Canceled);
            }
            if (generated == null || generated.Error != null)
            {
                TryDeleteDirectory(generated?.Directory);
                return Failed(generated?.Error ?? "Chunk GLB generation failed.",
                    cancellationToken.IsCancellationRequested
                        ? ChunkGlbExportFailure.Canceled
                        : ChunkGlbExportFailure.InvalidSourceArtifact);
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                preflight = ValidatePreflight(store, manifest, chunk,
                    unixMilliseconds, out failure);
                if (preflight != null)
                    return Failed(preflight, failure);

                PromotionResult staged = await Task.Run(() => Stage(store,
                    manifest.worldId, chunk.chunkId, chunk.revision, generated),
                    cancellationToken);
                if (staged.Promotion == null)
                    return Failed(staged.Error, ClassifyPromotionFailure(staged.Error));

                using (staged.Promotion)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    preflight = ValidatePreflight(store, manifest, chunk,
                        unixMilliseconds, out failure);
                    if (preflight != null)
                        return Failed(preflight, failure);
                    if (!staged.Promotion.TryCommit(manifest, chunk, unixMilliseconds,
                            out string commitError))
                        return Failed(commitError, ClassifyPromotionFailure(commitError));
                    return new ChunkGlbExportResult
                    {
                        Artifact = staged.Promotion.Artifact,
                        Write = generated.Write
                    };
                }
            }
            catch (OperationCanceledException)
            {
                return Failed("Chunk GLB export was canceled.",
                    ChunkGlbExportFailure.Canceled);
            }
            finally
            {
                TryDeleteDirectory(generated.Directory);
            }
        }

        private static GeneratedFile Generate(SourceSet sources, string workingRoot,
            string chunkId, int revision, ChunkGlbWriteOptions options,
            CancellationToken cancellationToken)
        {
            var generated = new GeneratedFile();
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                RefinedTextureResult refined;
                using (var meshStream = new FileStream(sources.MeshPath, FileMode.Open,
                           FileAccess.Read, FileShare.Read, 1024 * 1024,
                           FileOptions.SequentialScan))
                {
                    if (!ChunkRefinedArtifactCodec.TryReadMesh(meshStream, out refined,
                            out string meshError))
                        throw new InvalidDataException(meshError);
                }
                using (var atlasStream = new FileStream(sources.AtlasPath, FileMode.Open,
                           FileAccess.Read, FileShare.Read, 1024 * 1024,
                           FileOptions.SequentialScan))
                {
                    if (!ChunkRefinedArtifactCodec.TryReadRgbaTexture(atlasStream,
                            out byte[] atlas, out int width, out int height,
                            out string atlasError))
                        throw new InvalidDataException(atlasError);
                    if (width != refined.AtlasWidth || height != refined.AtlasHeight)
                        throw new InvalidDataException(
                            "Refined mesh and base-color atlas dimensions differ.");
                    refined.AtlasPixels = atlas;
                }
                using (var normalStream = new FileStream(sources.NormalPath, FileMode.Open,
                           FileAccess.Read, FileShare.Read, 1024 * 1024,
                           FileOptions.SequentialScan))
                {
                    if (!ChunkRefinedArtifactCodec.TryReadRgbaTexture(normalStream,
                            out byte[] normal, out int width, out int height,
                            out string normalError))
                        throw new InvalidDataException(normalError);
                    if (width != refined.AtlasWidth || height != refined.AtlasHeight)
                        throw new InvalidDataException(
                            "Refined mesh and normal atlas dimensions differ.");
                    refined.NormalPixels = normal;
                }

                cancellationToken.ThrowIfCancellationRequested();
                generated.Directory = Path.Combine(workingRoot, "glb-export",
                    Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(generated.Directory);
                generated.Path = Path.Combine(generated.Directory, "chunk.glb");
                var data = new ChunkGlbExportData
                {
                    Name = chunkId + "_r" + revision.ToString("D10"),
                    Positions = refined.Positions,
                    Normals = refined.Normals,
                    TexCoords0 = refined.UVs,
                    Indices = refined.Indices,
                    BaseColorRgba32 = refined.AtlasPixels,
                    NormalRgba32 = refined.NormalPixels,
                    TextureWidth = refined.AtlasWidth,
                    TextureHeight = refined.AtlasHeight
                };
                using (var stream = new FileStream(generated.Path, FileMode.CreateNew,
                           FileAccess.Write, FileShare.None, 1024 * 1024,
                           FileOptions.WriteThrough))
                {
                    if (!ChunkGlbWriter.TryWrite(stream, data, options,
                            out ChunkGlbWriteResult write, out string writeError,
                            cancellationToken))
                        throw cancellationToken.IsCancellationRequested
                            ? new OperationCanceledException(cancellationToken)
                            : new InvalidDataException(writeError);
                    stream.Flush(true);
                    generated.Write = write;
                }
                var file = new FileInfo(generated.Path);
                if (file.Length <= 0 || file.Length > WorldSchema.MaximumArtifactBytes ||
                    file.Length != generated.Write.ByteLength)
                    throw new InvalidDataException("Generated GLB length is invalid.");
                generated.ByteLength = file.Length;
                generated.Sha256 = Hashing.ComputeSha256(generated.Path);
            }
            catch (OperationCanceledException)
            {
                generated.Error = "Chunk GLB export was canceled.";
            }
            catch (Exception exception) when (exception is IOException ||
                                              exception is InvalidDataException ||
                                              exception is OverflowException ||
                                              exception is OutOfMemoryException ||
                                              exception is UnauthorizedAccessException)
            {
                generated.Error = "Chunk GLB generation failed: " + exception.Message;
            }
            return generated;
        }

        private static PromotionResult Stage(WorldStore store, string worldId,
            string chunkId, int revision, GeneratedFile generated)
        {
            if (!store.TryBeginChunkArtifactPromotion(worldId, chunkId, revision,
                    ChunkArtifactKind.Glb, ChunkGlbWriter.ArtifactFormatVersion,
                    "chunk.glb", generated.Path, generated.ByteLength, generated.Sha256,
                    out ChunkArtifactPromotion promotion, out string error))
                return new PromotionResult { Error = error };
            return new PromotionResult { Promotion = promotion };
        }

        private static bool TryResolveSources(WorldStore store, string worldId,
            ChunkRecord chunk, out SourceSet sources, out string error,
            out ChunkGlbExportFailure failure)
        {
            sources = null;
            error = null;
            failure = ChunkGlbExportFailure.MissingSourceArtifact;
            if (!TryFindCurrentArtifact(chunk, ChunkArtifactKind.RefinedMesh,
                    out ChunkArtifactRecord mesh, out error) ||
                !TryFindCurrentArtifact(chunk, ChunkArtifactKind.RefinedAtlas,
                    out ChunkArtifactRecord atlas, out error) ||
                !TryFindCurrentArtifact(chunk, ChunkArtifactKind.RefinedNormal,
                    out ChunkArtifactRecord normal, out error))
                return false;
            if (!store.TryResolveVerifiedArtifact(worldId, mesh, out string meshPath,
                    out error) ||
                !store.TryResolveVerifiedArtifact(worldId, atlas, out string atlasPath,
                    out error) ||
                !store.TryResolveVerifiedArtifact(worldId, normal, out string normalPath,
                    out error))
            {
                failure = ChunkGlbExportFailure.InvalidSourceArtifact;
                return false;
            }
            sources = new SourceSet
            {
                MeshPath = meshPath,
                AtlasPath = atlasPath,
                NormalPath = normalPath
            };
            return true;
        }

        private static bool TryFindCurrentArtifact(ChunkRecord chunk,
            ChunkArtifactKind kind, out ChunkArtifactRecord artifact, out string error)
        {
            artifact = null;
            error = null;
            if (chunk.artifacts != null)
            {
                for (int i = 0; i < chunk.artifacts.Count; i++)
                {
                    ChunkArtifactRecord candidate = chunk.artifacts[i];
                    if (candidate == null || candidate.kind != kind ||
                        candidate.chunkRevision != chunk.revision)
                        continue;
                    if (artifact != null)
                    {
                        error = $"Chunk has duplicate current {kind} artifacts.";
                        return false;
                    }
                    artifact = candidate;
                }
            }
            if (artifact == null)
            {
                error = $"Chunk revision {chunk.revision} has no current {kind} artifact.";
                return false;
            }
            return true;
        }

        private static string ValidatePreflight(WorldStore store, WorldManifest manifest,
            ChunkRecord chunk, long unixMilliseconds,
            out ChunkGlbExportFailure failure)
        {
            failure = ChunkGlbExportFailure.InvalidRequest;
            if (store == null || manifest == null || chunk == null)
                return "Store, manifest, and chunk are required for GLB export.";
            if (manifest.chunks == null || !manifest.chunks.Contains(chunk))
                return "GLB export chunk is not part of the supplied world.";
            if (chunk.state == ChunkLifecycleState.New ||
                chunk.state == ChunkLifecycleState.Failed)
                return "An uninitialized or failed chunk cannot be exported.";
            if (unixMilliseconds < manifest.updatedUnixMilliseconds ||
                unixMilliseconds < chunk.updatedUnixMilliseconds)
            {
                failure = ChunkGlbExportFailure.StaleRevision;
                return "GLB export timestamp is stale for the current chunk revision.";
            }
            return null;
        }

        private static ChunkGlbExportFailure ClassifyPromotionFailure(string error)
        {
            if (string.IsNullOrEmpty(error))
                return ChunkGlbExportFailure.Storage;
            if (error.IndexOf("different immutable", StringComparison.OrdinalIgnoreCase) >= 0)
                return ChunkGlbExportFailure.ImmutableConflict;
            if (error.IndexOf("stale", StringComparison.OrdinalIgnoreCase) >= 0 ||
                error.IndexOf("current chunk revision", StringComparison.OrdinalIgnoreCase) >= 0 ||
                error.IndexOf("durable chunk revision", StringComparison.OrdinalIgnoreCase) >= 0 ||
                error.IndexOf("no longer matches", StringComparison.OrdinalIgnoreCase) >= 0)
                return ChunkGlbExportFailure.StaleRevision;
            return ChunkGlbExportFailure.Storage;
        }

        private static ChunkGlbExportResult Failed(string error,
            ChunkGlbExportFailure failure) => new()
            {
                Error = string.IsNullOrEmpty(error) ? "Chunk GLB export failed." : error,
                Failure = failure
            };

        private static void TryDeleteDirectory(string path)
        {
            if (string.IsNullOrEmpty(path))
                return;
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
                string parent = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent) &&
                    Directory.GetFileSystemEntries(parent).Length == 0)
                    Directory.Delete(parent);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
