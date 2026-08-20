using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Genesis.RoomScan.World;
using UnityEngine;

namespace Genesis.RoomScan.Exporting
{
    public sealed class WorldGlbExportOptions
    {
        public ChunkGlbWriteOptions Material { get; set; } = new();
        public bool WriteMonolithicGlb { get; set; } = true;
        public long MaximumMonolithicByteLength { get; set; } = 2L * 1024 * 1024 * 1024;
        public GlbCompressionRequest Compression { get; set; } = new();
    }

    public sealed class WorldGlbExportResult
    {
        public bool Success => string.IsNullOrEmpty(Error) &&
                               !string.IsNullOrEmpty(BuildingManifestPath);
        public string Error { get; internal set; }
        public string OutputDirectory { get; internal set; }
        public string BuildingManifestPath { get; internal set; }
        public string MonolithicGlbPath { get; internal set; }
        public string MonolithicError { get; internal set; }
        public int ChunkCount { get; internal set; }
        public long ShardedByteLength { get; internal set; }
        public WorldGlbWriteResult MonolithicWrite { get; internal set; }
        public GlbCompressionSelection Compression { get; internal set; }
    }

    /// <summary>
    /// Exports a detached pose-graph snapshot. Each chunk is generated and copied before the
    /// next one is decoded, keeping geometry/texture residency O(one chunk). The sharded form
    /// is mandatory and committed by one directory rename; the bounded monolithic GLB is an
    /// optional convenience artifact and never invalidates a successful sharded export.
    /// </summary>
    public static class WorldGlbExporter
    {
        public const int BuildingManifestVersion = 1;
        private const int CopyBufferBytes = 1024 * 1024;

        private sealed class FrozenChunk
        {
            internal ChunkRecord Source;
            internal string ChunkId;
            internal int Revision;
            internal RigidPoseData WorldFromChunk;
        }

        private sealed class ExportedChunk
        {
            internal FrozenChunk Frozen;
            internal string RelativePath;
            internal string StagedPath;
            internal string Sha256;
            internal long ByteLength;
            internal ChunkGlbWriteResult Layout;
        }

        public static async Task<WorldGlbExportResult> ExportAsync(WorldStore store,
            WorldManifest manifest, string outputDirectory, WorldGlbExportOptions options,
            long unixMilliseconds, CancellationToken cancellationToken = default)
        {
            options ??= new WorldGlbExportOptions();
            string preflight = ValidatePreflight(store, manifest, outputDirectory, options,
                unixMilliseconds, out string finalDirectory, out List<FrozenChunk> frozen,
                out GlbCompressionSelection compression);
            if (preflight != null)
                return Failed(preflight);

            string parent = Path.GetDirectoryName(finalDirectory);
            string staging = Path.Combine(parent,
                "." + Path.GetFileName(finalDirectory) + ".pending-" +
                Guid.NewGuid().ToString("N"));
            var result = new WorldGlbExportResult { Compression = compression };
            try
            {
                Directory.CreateDirectory(parent);
                Directory.CreateDirectory(Path.Combine(staging, "chunks"));
                var exported = new List<ExportedChunk>(frozen.Count);
                var worldInputs = new List<WorldGlbChunkInput>(frozen.Count);
                long shardedBytes = 0;

                for (int i = 0; i < frozen.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    FrozenChunk item = frozen[i];
                    EnsureFrozen(item);
                    ChunkGlbExportResult chunkResult = await
                        ChunkGlbExporter.ExportRefinedAsync(store, manifest, item.Source,
                            options.Material, unixMilliseconds, cancellationToken);
                    if (!chunkResult.Success)
                        throw new InvalidDataException($"Chunk '{item.ChunkId}' GLB failed: " +
                                                       chunkResult.Error);
                    EnsureFrozen(item);
                    if (!store.TryResolveVerifiedArtifact(manifest.worldId,
                            chunkResult.Artifact, out string sourcePath, out string sourceError))
                        throw new InvalidDataException($"Chunk '{item.ChunkId}' GLB failed " +
                                                       "verification: " + sourceError);

                    string fileName = item.ChunkId + "_r" +
                                      item.Revision.ToString("D10", CultureInfo.InvariantCulture) +
                                      ".glb";
                    string relativePath = "chunks/" + fileName;
                    string stagedPath = Path.Combine(staging, "chunks", fileName);
                    (long copied, string digest) = await Task.Run(() =>
                        CopyAndHash(sourcePath, stagedPath, cancellationToken), cancellationToken);
                    if (copied != chunkResult.Artifact.byteLength ||
                        !string.Equals(digest, chunkResult.Artifact.sha256,
                            StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"Chunk '{item.ChunkId}' changed while " +
                                                       "the world export copied it.");

                    var exportedChunk = new ExportedChunk
                    {
                        Frozen = item,
                        RelativePath = relativePath,
                        StagedPath = stagedPath,
                        Sha256 = digest,
                        ByteLength = copied,
                        Layout = chunkResult.Write
                    };
                    exported.Add(exportedChunk);
                    worldInputs.Add(new WorldGlbChunkInput
                    {
                        ChunkId = item.ChunkId,
                        Revision = item.Revision,
                        WorldFromChunk = item.WorldFromChunk,
                        GlbPath = stagedPath,
                        ChunkLayout = chunkResult.Write
                    });
                    shardedBytes = checked(shardedBytes + copied);
                }

                EnsureWorldStillFrozen(manifest, frozen);
                string monolithicRelative = null;
                if (options.WriteMonolithicGlb)
                {
                    string monolithicPath = Path.Combine(staging, "world.glb");
                    WorldGlbWriteResult write = null;
                    string writeError = null;
                    bool wrote = await Task.Run(() =>
                    {
                        using var stream = new FileStream(monolithicPath, FileMode.CreateNew,
                            FileAccess.Write, FileShare.None, CopyBufferBytes,
                            FileOptions.WriteThrough);
                        bool ok = WorldGlbWriter.TryWrite(stream, worldInputs,
                            new WorldGlbWriteOptions
                            {
                                MaximumByteLength = options.MaximumMonolithicByteLength,
                                Material = options.Material
                            }, out write, out writeError, cancellationToken);
                        if (ok) stream.Flush(true);
                        return ok;
                    }, cancellationToken);
                    if (wrote)
                    {
                        monolithicRelative = "world.glb";
                        result.MonolithicWrite = write;
                    }
                    else
                    {
                        TryDeleteFile(monolithicPath);
                        result.MonolithicError = writeError ??
                            "Monolithic GLB was not generated; use building.json.";
                    }
                }

                string buildingJson = BuildManifestJson(manifest.worldId, manifest.revision,
                    exported, monolithicRelative, result.MonolithicError,
                    options.MaximumMonolithicByteLength, compression);
                string stagedManifest = Path.Combine(staging, "building.json");
                WriteDurableUtf8(stagedManifest, buildingJson);
                cancellationToken.ThrowIfCancellationRequested();
                EnsureWorldStillFrozen(manifest, frozen);
                if (Directory.Exists(finalDirectory))
                    throw new IOException("World export destination already exists; choose a " +
                                          "new revision directory: " + finalDirectory);
                Directory.Move(staging, finalDirectory);

                result.OutputDirectory = finalDirectory;
                result.BuildingManifestPath = Path.Combine(finalDirectory, "building.json");
                result.MonolithicGlbPath = monolithicRelative == null
                    ? null
                    : Path.Combine(finalDirectory, monolithicRelative);
                result.ChunkCount = exported.Count;
                result.ShardedByteLength = shardedBytes;
                return result;
            }
            catch (OperationCanceledException)
            {
                result.Error = "World GLB export was canceled.";
                return result;
            }
            catch (Exception exception) when (exception is IOException ||
                                              exception is InvalidDataException ||
                                              exception is OverflowException ||
                                              exception is UnauthorizedAccessException ||
                                              exception is OutOfMemoryException)
            {
                result.Error = "World GLB export failed: " + exception.Message;
                return result;
            }
            finally
            {
                TryDeleteDirectory(staging);
            }
        }

        private static string ValidatePreflight(WorldStore store, WorldManifest manifest,
            string outputDirectory, WorldGlbExportOptions options, long unixMilliseconds,
            out string finalDirectory, out List<FrozenChunk> frozen,
            out GlbCompressionSelection compression)
        {
            finalDirectory = null;
            frozen = null;
            compression = null;
            if (store == null || manifest == null)
                return "World store and manifest are required.";
            WorldValidationResult validation = WorldManifestValidator.Validate(manifest);
            if (!validation.IsValid)
                return "World manifest is invalid: " + validation;
            if (string.IsNullOrWhiteSpace(outputDirectory))
                return "World export destination is required.";
            if (unixMilliseconds < manifest.updatedUnixMilliseconds)
                return "World export timestamp is stale.";
            if (options.Material == null)
                options.Material = new ChunkGlbWriteOptions();
            if (options.MaximumMonolithicByteLength <= 0 ||
                options.MaximumMonolithicByteLength > uint.MaxValue)
                return "Monolithic GLB limit must be in (0, 2^32-1].";
            // V1 ships no encoder binaries. This truthful probe keeps the baseline mandatory;
            // a future verified backend can pass concrete capabilities at this boundary.
            compression = GlbCompressionNegotiator.Negotiate(options.Compression,
                GlbCompressionRuntimeCapabilities.BaselineOnly);
            if (!compression.Success)
                return compression.Error;
            try
            {
                finalDirectory = Path.GetFullPath(outputDirectory);
                string parent = Path.GetDirectoryName(finalDirectory);
                if (string.IsNullOrEmpty(parent) || string.Equals(parent, finalDirectory,
                        StringComparison.Ordinal))
                    return "World export destination cannot be a filesystem root.";
            }
            catch (Exception exception) when (exception is ArgumentException ||
                                              exception is NotSupportedException ||
                                              exception is PathTooLongException)
            {
                return "World export destination is invalid: " + exception.Message;
            }
            if (Directory.Exists(finalDirectory) || File.Exists(finalDirectory))
                return "World export destination already exists; choose a new directory.";

            frozen = new List<FrozenChunk>(manifest.chunks.Count);
            for (int i = 0; i < manifest.chunks.Count; i++)
            {
                ChunkRecord chunk = manifest.chunks[i];
                if (chunk == null || chunk.state == ChunkLifecycleState.New ||
                    chunk.state == ChunkLifecycleState.Failed)
                    return $"World chunk {i} is not exportable.";
                frozen.Add(new FrozenChunk
                {
                    Source = chunk,
                    ChunkId = chunk.chunkId,
                    Revision = chunk.revision,
                    WorldFromChunk = chunk.worldFromChunk
                });
            }
            frozen.Sort((left, right) => string.CompareOrdinal(left.ChunkId, right.ChunkId));
            return frozen.Count == 0 ? "World has no chunks to export." : null;
        }

        private static void EnsureFrozen(FrozenChunk frozen)
        {
            if (frozen.Source == null || frozen.Source.revision != frozen.Revision ||
                !string.Equals(frozen.Source.chunkId, frozen.ChunkId,
                    StringComparison.Ordinal) ||
                !frozen.Source.worldFromChunk.Equals(frozen.WorldFromChunk))
                throw new InvalidDataException($"Chunk '{frozen.ChunkId}' changed during export.");
        }

        private static void EnsureWorldStillFrozen(WorldManifest manifest,
            IReadOnlyList<FrozenChunk> frozen)
        {
            if (manifest?.chunks == null || manifest.chunks.Count != frozen.Count)
                throw new InvalidDataException("World chunk membership changed during export.");
            for (int i = 0; i < frozen.Count; i++)
                EnsureFrozen(frozen[i]);
        }

        private static (long ByteLength, string Sha256) CopyAndHash(string sourcePath,
            string destinationPath, CancellationToken cancellationToken)
        {
            using var hash = SHA256.Create();
            using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read,
                FileShare.Read, CopyBufferBytes, FileOptions.SequentialScan);
            using var destination = new FileStream(destinationPath, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, CopyBufferBytes, FileOptions.WriteThrough);
            var buffer = new byte[CopyBufferBytes];
            long copied = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int read = source.Read(buffer, 0, buffer.Length);
                if (read == 0) break;
                destination.Write(buffer, 0, read);
                hash.TransformBlock(buffer, 0, read, buffer, 0);
                copied = checked(copied + read);
            }
            hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            destination.Flush(true);
            return (copied, ToLowerHex(hash.Hash));
        }

        private static string BuildManifestJson(string worldId, int worldRevision,
            IReadOnlyList<ExportedChunk> chunks, string monolithicRelative,
            string monolithicError, long monolithicLimit,
            GlbCompressionSelection compression)
        {
            var json = new StringBuilder(Math.Max(2048, chunks.Count * 600));
            json.Append('{')
                .Append("\"schemaVersion\":").Append(BuildingManifestVersion).Append(',')
                .Append("\"generator\":\"QuestInfiniteScan\",")
                .Append("\"worldId\":\"").Append(ChunkGlbWriter.EscapeJson(worldId))
                .Append("\",\"worldRevision\":").Append(worldRevision).Append(',')
                .Append("\"coordinateSystem\":\"glTF-2.0-RH-Y-up\",")
                .Append("\"transformConvention\":\"worldFromChunk-applied-once\",")
                .Append("\"compression\":{\"meshopt\":")
                .Append(compression.UseMeshopt ? "true" : "false")
                .Append(",\"ktx2\":")
                .Append(compression.UseKtx2 ? "true" : "false")
                .Append(",\"extensionsUsed\":[");
            bool wroteExtension = false;
            if (compression.UseMeshopt)
            {
                json.Append('"').Append(GlbCompressionNegotiator.MeshoptExtension).Append('"');
                wroteExtension = true;
            }
            if (compression.UseKtx2)
            {
                if (wroteExtension) json.Append(',');
                json.Append('"').Append(GlbCompressionNegotiator.Ktx2Extension).Append('"');
            }
            json.Append(']');
            if (!string.IsNullOrEmpty(compression.FallbackReason))
                json.Append(",\"fallbackReason\":\"")
                    .Append(ChunkGlbWriter.EscapeJson(compression.FallbackReason)).Append('"');
            json.Append("},")
                .Append("\"chunks\":[");
            for (int i = 0; i < chunks.Count; i++)
            {
                if (i > 0) json.Append(',');
                ExportedChunk chunk = chunks[i];
                json.Append("{\"chunkId\":\"")
                    .Append(ChunkGlbWriter.EscapeJson(chunk.Frozen.ChunkId))
                    .Append("\",\"revision\":").Append(chunk.Frozen.Revision)
                    .Append(",\"uri\":\"")
                    .Append(ChunkGlbWriter.EscapeJson(chunk.RelativePath))
                    .Append("\",\"byteLength\":").Append(chunk.ByteLength)
                    .Append(",\"sha256\":\"").Append(chunk.Sha256)
                    .Append("\",\"matrix\":[");
                AppendMatrix(json, WorldGlbWriter.ToGltfMatrix(
                    chunk.Frozen.WorldFromChunk));
                json.Append("]}");
            }
            json.Append("],\"monolithic\":");
            if (monolithicRelative != null)
                json.Append("{\"uri\":\"").Append(monolithicRelative)
                    .Append("\",\"maximumByteLength\":").Append(monolithicLimit).Append('}');
            else
                json.Append("null");
            if (!string.IsNullOrEmpty(monolithicError))
                json.Append(",\"monolithicFallbackReason\":\"")
                    .Append(ChunkGlbWriter.EscapeJson(monolithicError)).Append('"');
            json.Append('}');
            return json.ToString();
        }

        private static void AppendMatrix(StringBuilder json, Matrix4x4 matrix)
        {
            float[] values =
            {
                matrix.m00, matrix.m10, matrix.m20, matrix.m30,
                matrix.m01, matrix.m11, matrix.m21, matrix.m31,
                matrix.m02, matrix.m12, matrix.m22, matrix.m32,
                matrix.m03, matrix.m13, matrix.m23, matrix.m33
            };
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) json.Append(',');
                json.Append(ChunkGlbWriter.JsonFloat(values[i]));
            }
        }

        private static void WriteDurableUtf8(string path, string contents)
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(contents);
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 64 * 1024, FileOptions.WriteThrough);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(true);
        }

        private static string ToLowerHex(byte[] bytes)
        {
            var value = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++)
                value.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
            return value.ToString();
        }

        private static WorldGlbExportResult Failed(string error) => new()
        {
            Error = string.IsNullOrEmpty(error) ? "World GLB export failed." : error
        };

        private static void TryDeleteFile(string path)
        {
            try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                    Directory.Delete(path, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
