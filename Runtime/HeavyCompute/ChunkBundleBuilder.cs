using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using Genesis.RoomScan.World;

namespace Genesis.RoomScan.HeavyCompute
{
    public sealed class ChunkBundleBuildResult
    {
        public bool Success { get; internal set; }
        public string Error { get; internal set; }
        public string BundlePath { get; internal set; }
        public HeavyComputeBlobDescriptor Descriptor { get; internal set; }
    }

    /// <summary>
    /// Deterministically packages one durable QRS mesh plus its QISK keyframes into the
    /// protocol-v2 ZIP. It is deliberately synchronous; callers run it on a worker task.
    /// </summary>
    public static class ChunkBundleBuilder
    {
        private static readonly DateTimeOffset CanonicalZipTime =
            new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

        private sealed class BundleFile
        {
            internal string Role;
            internal string ArchivePath;
            internal string MediaType;
            internal string SourcePath;
            internal long Length;
            internal string Sha256;
        }

        public static ChunkBundleBuildResult Build(WorldStore store, string worldId,
            string chunkId, int chunkRevision, string destinationPath)
        {
            string temporaryRoot = null;
            string pendingPath = null;
            try
            {
                if (store == null || !HeavyComputeContract.IsSafeIdentifier(worldId, 96) ||
                    !HeavyComputeContract.IsSafeIdentifier(chunkId, 64) || chunkRevision < 0 ||
                    string.IsNullOrEmpty(destinationPath))
                    return Failure("Bundle build arguments are invalid.");
                destinationPath = Path.GetFullPath(destinationPath);
                string destinationParent = Path.GetDirectoryName(destinationPath);
                if (string.IsNullOrEmpty(destinationParent))
                    return Failure("Bundle destination has no parent directory.");
                Directory.CreateDirectory(destinationParent);

                if (File.Exists(destinationPath))
                    return Existing(destinationPath);
                if (!store.TryLoadManifest(worldId, out WorldManifest manifest,
                        out _, out string loadError))
                    return Failure(loadError);
                ChunkRecord chunk = manifest.chunks.Find(candidate => candidate != null &&
                    string.Equals(candidate.chunkId, chunkId, StringComparison.Ordinal));
                if (chunk == null || chunk.revision != chunkRevision)
                    return Failure("Requested chunk revision is no longer current and durable.");

                ChunkArtifactRecord mesh = FindPreferredMesh(chunk);
                ChunkArtifactRecord keyframes = chunk.artifacts?.Find(candidate =>
                    candidate != null && candidate.kind == ChunkArtifactKind.Keyframes &&
                    candidate.chunkRevision <= chunkRevision);
                if (mesh == null || keyframes == null)
                    return Failure("Chunk needs a verified mesh and keyframe artifact.");
                if (!store.TryResolveVerifiedArtifact(worldId, mesh, out string meshPath,
                        out string meshError))
                    return Failure(meshError);
                if (!store.TryResolveVerifiedArtifact(worldId, keyframes,
                        out string keyframePath, out string keyframeError))
                    return Failure(keyframeError);

                temporaryRoot = Path.Combine(destinationParent, ".bundle-" +
                    Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(temporaryRoot);
                string extractedKeyframes = Path.Combine(temporaryRoot, "keyframes");
                using (var source = new FileStream(keyframePath, FileMode.Open, FileAccess.Read,
                           FileShare.Read, 1024 * 1024, FileOptions.SequentialScan))
                {
                    if (!ChunkKeyframeArchive.TryExtract(source, extractedKeyframes,
                            out string extractError))
                        return Failure(extractError);
                }

                var files = CollectFiles(mesh, meshPath, extractedKeyframes);
                if (files == null)
                    return Failure("Keyframe archive has no JPEG observations.");
                string inputJson = BuildInputManifest(worldId, chunkId, chunkRevision, files);
                pendingPath = destinationPath + ".pending-" + Guid.NewGuid().ToString("N");
                WriteDeterministicZip(pendingPath, inputJson, files);
                if (File.Exists(destinationPath))
                    throw new IOException("Bundle destination appeared concurrently.");
                File.Move(pendingPath, destinationPath);
                pendingPath = null;
                return Existing(destinationPath);
            }
            catch (Exception exception)
            {
                return Failure("Chunk bundle build failed: " + exception.Message);
            }
            finally
            {
                TryDeleteFileExact(pendingPath);
                TryDeleteDirectoryExact(temporaryRoot);
            }
        }

        private static List<BundleFile> CollectFiles(ChunkArtifactRecord mesh,
            string meshPath, string keyframeDirectory)
        {
            bool refined = mesh.kind == ChunkArtifactKind.RefinedMesh;
            var files = new List<BundleFile>
            {
                Describe(refined ? "refined_mesh" : "live_mesh",
                    refined ? "mesh/refined_mesh.qirm" : "mesh/live_mesh.qism",
                    refined ? "application/vnd.questinfinitescan.refined-mesh" :
                        "application/vnd.questinfinitescan.live-mesh", meshPath),
                Describe("keyframe_manifest", "keyframes/frames.jsonl",
                    "application/x-ndjson", Path.Combine(keyframeDirectory, "frames.jsonl"))
            };
            string images = Path.Combine(keyframeDirectory, "images");
            if (!Directory.Exists(images))
                return null;
            string[] paths = Directory.GetFiles(images, "*.jpg", SearchOption.TopDirectoryOnly);
            Array.Sort(paths, StringComparer.Ordinal);
            for (int i = 0; i < paths.Length; i++)
            {
                string name = Path.GetFileName(paths[i]);
                if (name.Length != 10 || !name.EndsWith(".jpg", StringComparison.Ordinal))
                    throw new InvalidDataException("Keyframe image name is non-canonical.");
                for (int digit = 0; digit < 6; digit++)
                    if (name[digit] < '0' || name[digit] > '9')
                        throw new InvalidDataException("Keyframe image name is non-canonical.");
                files.Add(Describe("keyframe_image", "keyframes/images/" + name,
                    "image/jpeg", paths[i]));
            }
            return paths.Length == 0 ? null : files;
        }

        private static BundleFile Describe(string role, string archivePath,
            string mediaType, string sourcePath)
        {
            var info = new FileInfo(sourcePath);
            if (!info.Exists || info.Length <= 0 ||
                info.Length > HeavyComputeProtocol.MaximumUploadBytes)
                throw new InvalidDataException("Bundle source file is missing or too large.");
            return new BundleFile
            {
                Role = role,
                ArchivePath = archivePath,
                MediaType = mediaType,
                SourcePath = info.FullName,
                Length = info.Length,
                Sha256 = Hashing.ComputeSha256(info.FullName)
            };
        }

        private static string BuildInputManifest(string worldId, string chunkId,
            int chunkRevision, List<BundleFile> files)
        {
            long aggregate = 0;
            for (int i = 0; i < files.Count; i++)
                aggregate = checked(aggregate + files[i].Length);
            if (files.Count < 3 || files.Count > 4096 ||
                aggregate > HeavyComputeProtocol.MaximumUploadBytes)
                throw new InvalidDataException("Bundle file count or aggregate size is invalid.");
            var builder = new StringBuilder(1024 + files.Count * 240);
            builder.Append("{\"schemaVersion\":2,\"bundleFormatVersion\":1,\"key\":{")
                .Append("\"worldId\":\"").Append(worldId)
                .Append("\",\"chunkId\":\"").Append(chunkId)
                .Append("\",\"chunkRevision\":")
                .Append(chunkRevision.ToString(CultureInfo.InvariantCulture))
                .Append("},\"meshSpace\":\"chunk-local\",")
                .Append("\"coordinateSystem\":\"unity-lh-y-up-z-forward\",")
                .Append("\"units\":\"meter\",\"frontFace\":\"clockwise\",\"files\":[");
            for (int i = 0; i < files.Count; i++)
            {
                if (i > 0) builder.Append(',');
                BundleFile file = files[i];
                builder.Append("{\"role\":\"").Append(file.Role)
                    .Append("\",\"path\":\"").Append(file.ArchivePath)
                    .Append("\",\"mediaType\":\"").Append(file.MediaType)
                    .Append("\",\"byteLength\":")
                    .Append(file.Length.ToString(CultureInfo.InvariantCulture))
                    .Append(",\"sha256\":\"").Append(file.Sha256).Append("\"}");
            }
            return builder.Append("]}").ToString();
        }

        private static void WriteDeterministicZip(string destination, string inputJson,
            List<BundleFile> files)
        {
            using var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 1024 * 1024, FileOptions.WriteThrough);
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true,
                       Encoding.UTF8))
            {
                WriteBytesEntry(archive, "input.json", Encoding.UTF8.GetBytes(inputJson));
                for (int i = 0; i < files.Count; i++)
                    WriteFileEntry(archive, files[i].ArchivePath, files[i].SourcePath);
            }
            stream.Flush(true);
        }

        private static void WriteBytesEntry(ZipArchive archive, string name, byte[] value)
        {
            ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
            entry.LastWriteTime = CanonicalZipTime;
            entry.ExternalAttributes = 0;
            using Stream output = entry.Open();
            output.Write(value, 0, value.Length);
        }

        private static void WriteFileEntry(ZipArchive archive, string name, string sourcePath)
        {
            ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
            entry.LastWriteTime = CanonicalZipTime;
            entry.ExternalAttributes = 0;
            using Stream output = entry.Open();
            using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read,
                FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
            input.CopyTo(output, 1024 * 1024);
        }

        private static ChunkArtifactRecord FindPreferredMesh(ChunkRecord chunk)
        {
            ChunkArtifactRecord refined = chunk.artifacts?.Find(candidate =>
                candidate != null && candidate.kind == ChunkArtifactKind.RefinedMesh &&
                candidate.chunkRevision <= chunk.revision);
            return refined ?? chunk.artifacts?.Find(candidate => candidate != null &&
                candidate.kind == ChunkArtifactKind.LiveMesh &&
                candidate.chunkRevision <= chunk.revision);
        }

        private static ChunkBundleBuildResult Existing(string path)
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length <= 0 ||
                info.Length > HeavyComputeProtocol.MaximumUploadBytes)
                return Failure("Queue bundle is missing or too large.");
            return new ChunkBundleBuildResult
            {
                Success = true,
                BundlePath = info.FullName,
                Descriptor = new HeavyComputeBlobDescriptor
                {
                    mediaType = HeavyComputeProtocol.ChunkBundleMediaType,
                    formatVersion = HeavyComputeProtocol.ChunkBundleVersion,
                    byteLength = info.Length,
                    sha256 = Hashing.ComputeSha256(info.FullName)
                }
            };
        }

        private static ChunkBundleBuildResult Failure(string error) =>
            new() { Error = string.IsNullOrEmpty(error) ? "Bundle build failed." : error };

        private static void TryDeleteFileExact(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static void TryDeleteDirectoryExact(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try { if (Directory.Exists(path)) Directory.Delete(path, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
