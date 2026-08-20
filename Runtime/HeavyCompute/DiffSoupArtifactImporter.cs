using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Genesis.RoomScan.World;
using UnityEngine;

namespace Genesis.RoomScan.HeavyCompute
{
    [Serializable]
    public sealed class DiffSoupImportLimits
    {
        public long maximumBundleBytes = 2L * 1024 * 1024 * 1024;
        public int maximumVertices = 4_000_000;
        public int maximumFaces = 4_000_000;
        public int maximumLutDimension = 4096;
        public long maximumTextureFileBytes = 256L * 1024 * 1024;
        public int maximumJsonBytes = 4 * 1024 * 1024;
        public int maximumCompressionRatio = 200;
    }

    [Serializable]
    public sealed class DiffSoupArtifactFile
    {
        public string role;
        public string path;
        public string mediaType;
        public int formatVersion;
        public long byteLength;
        public string sha256;
    }

    [Serializable]
    public sealed class DiffSoupProducer
    {
        public string name;
        public string sourceCommit;
        public string compatibilityTag;
    }

    [Serializable]
    public sealed class DiffSoupModelDescription
    {
        public string meshSpace;
        public string coordinateSystem;
        public string units;
        public string frontFace;
        public string featureEncoding;
        public int level;
        public int numVertices;
        public int numFaces;
        public int lutWidth;
        public int lutHeight;
    }

    [Serializable]
    public sealed class DiffSoupArtifactManifest
    {
        public int schemaVersion;
        public int artifactFormatVersion;
        public string jobId;
        public string requestFingerprint;
        public HeavyComputeJobKey key;
        public DiffSoupProducer producer;
        public DiffSoupModelDescription model;
        public DiffSoupArtifactFile[] files;

        public DiffSoupArtifactFile FindRole(string role) =>
            Array.Find(files ?? Array.Empty<DiffSoupArtifactFile>(), candidate =>
                candidate != null && string.Equals(candidate.role, role,
                    StringComparison.Ordinal));
    }

    [Serializable]
    public sealed class DiffSoupMlpWeights
    {
        public float[] W1;
        public float[] b1;
        public float[] W2;
        public float[] b2;
        public float[] W3;
        public float[] b3;
    }

    [Serializable]
    public sealed class DiffSoupMetadata
    {
        public float[] up;
        public int level;
        public float[] background;
        public int num_faces;
        public int num_verts;
    }

    public sealed class DiffSoupArtifactData
    {
        public DiffSoupArtifactManifest Manifest { get; internal set; }
        public Vector3[] Positions { get; internal set; }
        public int[] Indices { get; internal set; }
        public byte[] Lut0Png { get; internal set; }
        public byte[] Lut1Png { get; internal set; }
        public DiffSoupMlpWeights Mlp { get; internal set; }
        public DiffSoupMetadata Metadata { get; internal set; }
    }

    public sealed class DiffSoupArtifactImportResult
    {
        public bool Success => Data != null && string.IsNullOrEmpty(Error);
        public string Error { get; internal set; }
        public DiffSoupArtifactData Data { get; internal set; }
    }

    /// <summary>
    /// Bounded, fail-closed parser for the untrusted ZIP returned over LAN. It hashes every
    /// declared entry, rejects undeclared/case-colliding/link-like paths, streams PLY, and
    /// only retains renderer payloads after their shape and dimensions are proven.
    /// </summary>
    public static class DiffSoupArtifactImporter
    {
        private const int MaximumManifestBytes = 2 * 1024 * 1024;
        private const int MaximumArtifactFiles = 16;
        private const int MaximumHeaderBytes = 16 * 1024;
        private static readonly byte[] PngSignature =
            { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        public static DiffSoupArtifactImportResult Import(string path,
            HeavyComputeSubmission expectedSubmission,
            HeavyComputeBlobDescriptor expectedBundle,
            DiffSoupImportLimits limits = null)
        {
            limits ??= new DiffSoupImportLimits();
            try
            {
                ValidateLimits(limits);
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    throw new InvalidDataException("Artifact bundle does not exist.");
                if (expectedSubmission == null)
                    throw new InvalidDataException("Expected submission is missing.");
                if (!HeavyComputeContract.TryValidateSubmission(expectedSubmission, true,
                        out string submissionError))
                    throw new InvalidDataException(submissionError ??
                                                   "Expected submission is invalid.");
                if (!HeavyComputeContract.TryValidateBlob(expectedBundle,
                        HeavyComputeProtocol.DiffSoupArtifactMediaType,
                        HeavyComputeProtocol.DiffSoupArtifactVersion,
                        Math.Min(limits.maximumBundleBytes,
                            HeavyComputeProtocol.MaximumArtifactBytes), out string bundleError))
                    throw new InvalidDataException(bundleError ??
                                                   "Expected artifact descriptor is invalid.");
                var info = new FileInfo(path);
                if (info.Length != expectedBundle.byteLength || info.Length <= 0 ||
                    info.Length > limits.maximumBundleBytes ||
                    !string.Equals(Hashing.ComputeSha256(info.FullName), expectedBundle.sha256,
                        StringComparison.Ordinal))
                    throw new InvalidDataException(
                        "Artifact bundle changed relative to its downloaded descriptor.");

                using var archive = ZipFile.OpenRead(info.FullName);
                Dictionary<string, ZipArchiveEntry> entries = ValidateEntries(archive, limits);
                if (!entries.TryGetValue("artifact.json", out ZipArchiveEntry manifestEntry) ||
                    manifestEntry.Length > MaximumManifestBytes)
                    throw new InvalidDataException("artifact.json is missing or too large.");
                byte[] manifestBytes = ReadBounded(manifestEntry, MaximumManifestBytes);
                DiffSoupArtifactManifest manifest = ParseManifest(manifestBytes,
                    expectedSubmission, limits);
                ValidateMembership(entries, manifest);

                byte[] lut0 = null;
                byte[] lut1 = null;
                byte[] mlpJson = null;
                byte[] metaJson = null;
                for (int i = 0; i < manifest.files.Length; i++)
                {
                    DiffSoupArtifactFile descriptor = manifest.files[i];
                    ZipArchiveEntry entry = entries[descriptor.path];
                    if (entry.Length != descriptor.byteLength)
                        throw new InvalidDataException(
                            $"Artifact size mismatch for {descriptor.path}.");
                    byte[] retained = descriptor.role switch
                    {
                        "lut0" or "lut1" => ReadBounded(entry,
                            limits.maximumTextureFileBytes),
                        "mlp" or "meta" => ReadBounded(entry, limits.maximumJsonBytes),
                        _ => null
                    };
                    string digest = retained != null
                        ? Sha256(retained)
                        : HashEntry(entry);
                    if (!string.Equals(digest, descriptor.sha256,
                            StringComparison.Ordinal))
                        throw new InvalidDataException(
                            $"Artifact hash mismatch for {descriptor.path}.");
                    if (descriptor.role == "lut0") lut0 = retained;
                    else if (descriptor.role == "lut1") lut1 = retained;
                    else if (descriptor.role == "mlp") mlpJson = retained;
                    else if (descriptor.role == "meta") metaJson = retained;
                }

                DiffSoupArtifactFile mesh = manifest.FindRole("mesh");
                ParsePly(entries[mesh.path], manifest.model.numVertices,
                    manifest.model.numFaces, out Vector3[] positions, out int[] indices);
                ValidatePng(lut0, manifest.model.lutWidth, manifest.model.lutHeight, "lut0");
                ValidatePng(lut1, manifest.model.lutWidth, manifest.model.lutHeight, "lut1");
                DiffSoupMlpWeights weights = ParseMlp(mlpJson);
                DiffSoupMetadata metadata = ParseMetadata(metaJson, manifest.model);
                return new DiffSoupArtifactImportResult
                {
                    Data = new DiffSoupArtifactData
                    {
                        Manifest = manifest,
                        Positions = positions,
                        Indices = indices,
                        Lut0Png = lut0,
                        Lut1Png = lut1,
                        Mlp = weights,
                        Metadata = metadata
                    }
                };
            }
            catch (Exception exception) when (exception is IOException ||
                                              exception is InvalidDataException ||
                                              exception is UnauthorizedAccessException ||
                                              exception is ArgumentException ||
                                              exception is NotSupportedException ||
                                              exception is OverflowException ||
                                              exception is CryptographicException)
            {
                return new DiffSoupArtifactImportResult
                {
                    Error = "DiffSoup artifact rejected: " + exception.Message
                };
            }
        }

        private static Dictionary<string, ZipArchiveEntry> ValidateEntries(
            ZipArchive archive, DiffSoupImportLimits limits)
        {
            if (archive.Entries.Count < 2 || archive.Entries.Count > MaximumArtifactFiles + 1)
                throw new InvalidDataException("Artifact entry count is outside limits.");
            var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
            var folded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long aggregate = 0;
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string name = entry.FullName;
                if (!StoragePath.IsSafeRelativePath(name) || name.EndsWith("/",
                        StringComparison.Ordinal) || !entries.TryAdd(name, entry) ||
                    !folded.Add(name))
                    throw new InvalidDataException("Artifact contains an unsafe or duplicate path.");
                int unixMode = (entry.ExternalAttributes >> 16) & 0xFFFF;
                int fileType = unixMode & 0xF000;
                if (fileType != 0 && fileType != 0x8000)
                    throw new InvalidDataException("Artifact contains a link or special entry.");
                aggregate = checked(aggregate + entry.Length);
                if (aggregate > limits.maximumBundleBytes)
                    throw new InvalidDataException("Artifact expands beyond its aggregate limit.");
                if (entry.Length > 1024 * 1024 && entry.Length >
                    Math.Max(1, entry.CompressedLength) * limits.maximumCompressionRatio)
                    throw new InvalidDataException("Artifact compression ratio is unsafe.");
            }
            return entries;
        }

        private static DiffSoupArtifactManifest ParseManifest(byte[] bytes,
            HeavyComputeSubmission expected, DiffSoupImportLimits limits)
        {
            string json = DecodeUtf8Object(bytes, "artifact manifest");
            DiffSoupArtifactManifest manifest;
            try { manifest = JsonUtility.FromJson<DiffSoupArtifactManifest>(json); }
            catch (Exception exception)
            {
                throw new InvalidDataException("Artifact manifest JSON is malformed.", exception);
            }
            if (manifest == null || manifest.schemaVersion != HeavyComputeProtocol.Version ||
                manifest.artifactFormatVersion != HeavyComputeProtocol.DiffSoupArtifactVersion ||
                !HeavyComputeContract.TryValidateKey(manifest.key, out _) ||
                !string.Equals(manifest.jobId, manifest.key.JobId, StringComparison.Ordinal) ||
                !string.Equals(manifest.jobId, expected.jobId, StringComparison.Ordinal) ||
                !string.Equals(manifest.requestFingerprint, expected.requestFingerprint,
                    StringComparison.Ordinal) || manifest.producer == null ||
                !string.Equals(manifest.producer.name, "diffsoup", StringComparison.Ordinal) ||
                !IsGitCommit(manifest.producer.sourceCommit) ||
                !HeavyComputeContract.IsLowerHexDigest(manifest.producer.compatibilityTag) ||
                manifest.model == null ||
                manifest.model.meshSpace != "chunk-local" ||
                manifest.model.coordinateSystem != "unity-lh-y-up-z-forward" ||
                manifest.model.units != "meter" || manifest.model.frontFace != "clockwise" ||
                manifest.model.featureEncoding != "diffsoup-sh2-mlp16-v1" ||
                manifest.model.level < 0 || manifest.model.level > 8 ||
                manifest.model.numVertices < 3 ||
                manifest.model.numVertices > limits.maximumVertices ||
                manifest.model.numFaces < 1 ||
                manifest.model.numFaces > limits.maximumFaces ||
                manifest.model.lutWidth < 1 ||
                manifest.model.lutWidth > limits.maximumLutDimension ||
                manifest.model.lutHeight < 1 ||
                manifest.model.lutHeight > limits.maximumLutDimension ||
                manifest.files == null || manifest.files.Length < 5 ||
                manifest.files.Length > MaximumArtifactFiles)
                throw new InvalidDataException("Artifact manifest violates protocol v2.");
            ValidateManifestFiles(manifest.files, limits);
            return manifest;
        }

        private static void ValidateManifestFiles(DiffSoupArtifactFile[] files,
            DiffSoupImportLimits limits)
        {
            var roles = new HashSet<string>(StringComparer.Ordinal);
            var paths = new HashSet<string>(StringComparer.Ordinal);
            long aggregate = 0;
            for (int i = 0; i < files.Length; i++)
            {
                DiffSoupArtifactFile file = files[i];
                if (file == null || !roles.Add(file.role) || !paths.Add(file.path) ||
                    !TryExpectedFile(file.role, out string path, out string mediaType) ||
                    file.path != path || file.mediaType != mediaType ||
                    file.formatVersion != 1 || file.byteLength <= 0 ||
                    file.byteLength > limits.maximumBundleBytes ||
                    !HeavyComputeContract.IsLowerHexDigest(file.sha256))
                    throw new InvalidDataException("Artifact file declaration is invalid.");
                aggregate = checked(aggregate + file.byteLength);
            }
            if (aggregate > limits.maximumBundleBytes || !roles.Contains("mesh") ||
                !roles.Contains("lut0") || !roles.Contains("lut1") ||
                !roles.Contains("mlp") || !roles.Contains("meta"))
                throw new InvalidDataException("Artifact is missing a required renderer role.");
        }

        private static void ValidateMembership(Dictionary<string, ZipArchiveEntry> entries,
            DiffSoupArtifactManifest manifest)
        {
            if (entries.Count != manifest.files.Length + 1)
                throw new InvalidDataException("ZIP membership differs from artifact.json.");
            for (int i = 0; i < manifest.files.Length; i++)
                if (!entries.ContainsKey(manifest.files[i].path))
                    throw new InvalidDataException("A declared artifact payload is missing.");
        }

        private static void ParsePly(ZipArchiveEntry entry, int vertexCount, int faceCount,
            out Vector3[] positions, out int[] indices)
        {
            string expectedHeader = "ply\nformat binary_little_endian 1.0\n" +
                                    $"element vertex {vertexCount}\n" +
                                    "property float x\nproperty float y\nproperty float z\n" +
                                    $"element face {faceCount}\n" +
                                    "property list uchar int vertex_indices\nend_header\n";
            byte[] expectedHeaderBytes = Encoding.ASCII.GetBytes(expectedHeader);
            long expectedLength = checked(expectedHeaderBytes.Length +
                (long)vertexCount * 12L + (long)faceCount * 13L);
            if (entry.Length != expectedLength || expectedHeaderBytes.Length > MaximumHeaderBytes)
                throw new InvalidDataException("DiffSoup PLY length is inconsistent.");
            using Stream stream = entry.Open();
            byte[] actualHeader = ReadExact(stream, expectedHeaderBytes.Length);
            if (!ByteArraysEqual(actualHeader, expectedHeaderBytes))
                throw new InvalidDataException("DiffSoup PLY header/schema is unsupported.");
            using var reader = new BinaryReader(stream, Encoding.UTF8, true);
            positions = new Vector3[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                float x = reader.ReadSingle();
                float y = reader.ReadSingle();
                float z = reader.ReadSingle();
                if (!IsFinite(x) || !IsFinite(y) || !IsFinite(z) ||
                    Math.Abs(x) > 100_000f || Math.Abs(y) > 100_000f ||
                    Math.Abs(z) > 100_000f)
                    throw new InvalidDataException($"DiffSoup vertex {i} is invalid.");
                positions[i] = new Vector3(x, y, z);
            }
            indices = new int[checked(faceCount * 3)];
            for (int face = 0; face < faceCount; face++)
            {
                if (reader.ReadByte() != 3)
                    throw new InvalidDataException($"DiffSoup face {face} is not triangular.");
                for (int corner = 0; corner < 3; corner++)
                {
                    int index = reader.ReadInt32();
                    if (index < 0 || index >= vertexCount)
                        throw new InvalidDataException(
                            $"DiffSoup face {face} contains an invalid index.");
                    indices[face * 3 + corner] = index;
                }
            }
            if (stream.ReadByte() != -1)
                throw new InvalidDataException("DiffSoup PLY contains trailing bytes.");
        }

        private static DiffSoupMlpWeights ParseMlp(byte[] bytes)
        {
            string json = DecodeUtf8Object(bytes, "MLP");
            foreach (string name in new[] { "W1", "b1", "W2", "b2", "W3", "b3" })
                if (!HasJsonProperty(json, name))
                    throw new InvalidDataException("DiffSoup MLP is missing " + name + ".");
            DiffSoupMlpWeights weights;
            try { weights = JsonUtility.FromJson<DiffSoupMlpWeights>(json); }
            catch (Exception exception)
            {
                throw new InvalidDataException("DiffSoup MLP JSON is malformed.", exception);
            }
            ValidateFloatArray(weights?.W1, 256, "W1");
            ValidateFloatArray(weights?.b1, 16, "b1");
            ValidateFloatArray(weights?.W2, 256, "W2");
            ValidateFloatArray(weights?.b2, 16, "b2");
            ValidateFloatArray(weights?.W3, 48, "W3");
            ValidateFloatArray(weights?.b3, 3, "b3");
            return weights;
        }

        private static DiffSoupMetadata ParseMetadata(byte[] bytes,
            DiffSoupModelDescription model)
        {
            string json = DecodeUtf8Object(bytes, "metadata");
            foreach (string name in new[] { "up", "level", "background", "num_faces",
                         "num_verts" })
                if (!HasJsonProperty(json, name))
                    throw new InvalidDataException("DiffSoup metadata is missing " + name + ".");
            DiffSoupMetadata metadata;
            try { metadata = JsonUtility.FromJson<DiffSoupMetadata>(json); }
            catch (Exception exception)
            {
                throw new InvalidDataException("DiffSoup metadata JSON is malformed.", exception);
            }
            ValidateFloatArray(metadata?.up, 3, "metadata up");
            ValidateFloatArray(metadata?.background, 3, "metadata background");
            if (metadata.level != model.level || metadata.num_faces != model.numFaces ||
                metadata.num_verts != model.numVertices)
                throw new InvalidDataException("DiffSoup metadata disagrees with manifest.");
            return metadata;
        }

        private static void ValidatePng(byte[] bytes, int expectedWidth,
            int expectedHeight, string label)
        {
            if (bytes == null || bytes.Length < 33)
                throw new InvalidDataException($"DiffSoup {label} PNG is truncated.");
            for (int i = 0; i < PngSignature.Length; i++)
                if (bytes[i] != PngSignature[i])
                    throw new InvalidDataException($"DiffSoup {label} is not PNG.");
            if (ReadUInt32BigEndian(bytes, 8) != 13 || bytes[12] != 'I' ||
                bytes[13] != 'H' || bytes[14] != 'D' || bytes[15] != 'R' ||
                ReadUInt32BigEndian(bytes, 16) != (uint)expectedWidth ||
                ReadUInt32BigEndian(bytes, 20) != (uint)expectedHeight ||
                bytes[24] != 8 || bytes[25] != 6 || bytes[26] != 0 ||
                bytes[27] != 0 || bytes[28] != 0)
                throw new InvalidDataException(
                    $"DiffSoup {label} PNG dimensions or RGBA8 format are invalid.");
        }

        private static byte[] ReadBounded(ZipArchiveEntry entry, long maximumBytes)
        {
            if (entry.Length < 0 || entry.Length > maximumBytes || entry.Length > int.MaxValue)
                throw new InvalidDataException($"Artifact entry {entry.FullName} is too large.");
            using Stream stream = entry.Open();
            byte[] bytes = ReadExact(stream, (int)entry.Length);
            if (stream.ReadByte() != -1)
                throw new InvalidDataException("Artifact entry exceeds its declared length.");
            return bytes;
        }

        private static byte[] ReadExact(Stream stream, int count)
        {
            var result = new byte[count];
            int offset = 0;
            while (offset < result.Length)
            {
                int read = stream.Read(result, offset, result.Length - offset);
                if (read <= 0) throw new EndOfStreamException("Artifact entry is truncated.");
                offset += read;
            }
            return result;
        }

        private static string HashEntry(ZipArchiveEntry entry)
        {
            using Stream stream = entry.Open();
            using var algorithm = SHA256.Create();
            return Hex(algorithm.ComputeHash(stream));
        }

        private static string Sha256(byte[] bytes)
        {
            using var algorithm = SHA256.Create();
            return Hex(algorithm.ComputeHash(bytes));
        }

        private static string Hex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++)
                builder.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
            return builder.ToString();
        }

        private static string DecodeUtf8Object(byte[] bytes, string label)
        {
            try
            {
                string value = new UTF8Encoding(false, true).GetString(bytes);
                string trimmed = value.Trim();
                if (!trimmed.StartsWith("{", StringComparison.Ordinal) ||
                    !trimmed.EndsWith("}", StringComparison.Ordinal))
                    throw new InvalidDataException($"DiffSoup {label} must be a JSON object.");
                return trimmed;
            }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidDataException($"DiffSoup {label} is not UTF-8.", exception);
            }
        }

        private static bool TryExpectedFile(string role, out string path,
            out string mediaType)
        {
            (path, mediaType) = role switch
            {
                "mesh" => ("model/mesh.ply",
                    "application/vnd.questinfinitescan.diffsoup-mesh"),
                "lut0" => ("model/lut0.png", "image/png"),
                "lut1" => ("model/lut1.png", "image/png"),
                "mlp" => ("model/mlp_weights.json", "application/json"),
                "meta" => ("model/meta.json", "application/json"),
                "checkpoint" => ("checkpoint/resume.pt",
                    "application/vnd.questinfinitescan.diffsoup-checkpoint"),
                _ => (null, null)
            };
            return path != null;
        }

        private static void ValidateFloatArray(float[] values, int expected, string label)
        {
            if (values == null || values.Length != expected)
                throw new InvalidDataException($"DiffSoup {label} has an invalid shape.");
            for (int i = 0; i < values.Length; i++)
                if (!IsFinite(values[i]) || Math.Abs(values[i]) > 1_000_000f)
                    throw new InvalidDataException($"DiffSoup {label} has an invalid value.");
        }

        private static bool HasJsonProperty(string json, string name) =>
            json.IndexOf("\"" + name + "\"", StringComparison.Ordinal) >= 0;

        private static bool IsGitCommit(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 40 && value.Length != 64)
                return false;
            for (int i = 0; i < value.Length; i++)
                if (!(value[i] >= '0' && value[i] <= '9') &&
                    !(value[i] >= 'a' && value[i] <= 'f'))
                    return false;
            return true;
        }

        private static void ValidateLimits(DiffSoupImportLimits limits)
        {
            if (limits.maximumBundleBytes <= 0 ||
                limits.maximumBundleBytes > HeavyComputeProtocol.MaximumArtifactBytes ||
                limits.maximumVertices < 3 || limits.maximumVertices > 8_000_000 ||
                limits.maximumFaces < 1 || limits.maximumFaces > 8_000_000 ||
                limits.maximumLutDimension < 1 || limits.maximumLutDimension > 8192 ||
                limits.maximumTextureFileBytes <= 0 ||
                limits.maximumTextureFileBytes > int.MaxValue ||
                limits.maximumJsonBytes <= 0 || limits.maximumJsonBytes > 16 * 1024 * 1024 ||
                limits.maximumCompressionRatio < 1 || limits.maximumCompressionRatio > 1000)
                throw new ArgumentException("DiffSoup import limits are invalid.");
        }

        private static uint ReadUInt32BigEndian(byte[] bytes, int offset) =>
            (uint)(bytes[offset] << 24 | bytes[offset + 1] << 16 |
                   bytes[offset + 2] << 8 | bytes[offset + 3]);

        private static bool ByteArraysEqual(byte[] left, byte[] right)
        {
            if (left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
            return true;
        }

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
