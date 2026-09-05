using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Genesis.RoomScan.UI;
using UnityEngine;

namespace Genesis.RoomScan
{
    [Serializable]
    public sealed class MerkabaDesignAsset
    {
        public int formatVersion = 1;
        public string id;
        public string displayName;
        public Vector3 boundsCenter;
        public Vector3 boundsSize;
        public string importedUtc;

        internal Bounds Bounds => new(boundsCenter, boundsSize);
    }

    /// <summary>
    /// Content-addressed GLB library for session design objects. Geometry is
    /// decoded only by the existing artifact-viewer decoder and never becomes
    /// canonical M8 state.
    /// </summary>
    internal sealed class MerkabaDesignLibrary
    {
        internal const int FormatVersion = 1;
        private const int CopyBufferBytes = 1024 * 1024;
        private readonly string _root;
        private readonly List<MerkabaDesignAsset> _assets = new();

        internal MerkabaDesignLibrary(string root)
        {
            if (string.IsNullOrWhiteSpace(root))
                throw new ArgumentException("Design library root is required.",
                    nameof(root));
            _root = Path.GetFullPath(root);
            Refresh();
        }

        internal IReadOnlyList<MerkabaDesignAsset> Assets => _assets;
        internal string Root => _root;

        internal void Refresh()
        {
            Directory.CreateDirectory(_root);
            _assets.Clear();
            string[] metadataPaths = Directory.GetFiles(_root, "*.json",
                SearchOption.TopDirectoryOnly);
            Array.Sort(metadataPaths, StringComparer.Ordinal);
            foreach (string metadataPath in metadataPaths)
            {
                try
                {
                    MerkabaDesignAsset asset = ReadMetadata(metadataPath);
                    if (!File.Exists(AssetPath(asset.id)))
                        throw new FileNotFoundException(
                            "Design GLB bytes are missing.", AssetPath(asset.id));
                    _assets.Add(asset);
                }
                catch (Exception exception)
                {
                    Logger.Warning($"Ignoring invalid design asset metadata " +
                        $"'{Path.GetFileName(metadataPath)}': " +
                        exception.Message);
                }
            }
            _assets.Sort((left, right) =>
            {
                int name = string.Compare(left.displayName, right.displayName,
                    StringComparison.OrdinalIgnoreCase);
                return name != 0 ? name : string.CompareOrdinal(left.id,
                    right.id);
            });
        }

        internal MerkabaDesignAsset Import(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) ||
                !File.Exists(sourcePath))
                throw new FileNotFoundException("Imported GLB was not found.",
                    sourcePath);
            Directory.CreateDirectory(_root);
            string staging = Path.Combine(_root, ".import-" +
                Guid.NewGuid().ToString("N") + ".glb.tmp");
            try
            {
                string id = CopyAndHash(sourcePath, staging);
                MerkabaArtifactViewer.ParsedGlb parsed;
                using (var input = new FileStream(staging, FileMode.Open,
                           FileAccess.Read, FileShare.Read, CopyBufferBytes,
                           FileOptions.SequentialScan))
                    parsed = MerkabaArtifactViewer.ParseGlbForPreview(input,
                        input.Length);

                string destination = AssetPath(id);
                if (File.Exists(destination))
                    File.Delete(staging);
                else
                    MerkabaFilePublishing.Publish(staging, destination);

                string metadataPath = MetadataPath(id);
                MerkabaDesignAsset asset = File.Exists(metadataPath)
                    ? ReadMetadata(metadataPath)
                    : CreateMetadata(id, sourcePath, parsed);
                if (!File.Exists(metadataPath)) WriteMetadata(asset);
                Refresh();
                return _assets.Find(value => value.id == id) ?? asset;
            }
            finally
            {
                if (File.Exists(staging)) File.Delete(staging);
            }
        }

        internal MerkabaArtifactViewer.ParsedGlb Decode(string assetId)
        {
            string path = AssetPath(assetId);
            using var input = new FileStream(path, FileMode.Open,
                FileAccess.Read, FileShare.Read, CopyBufferBytes,
                FileOptions.SequentialScan);
            return MerkabaArtifactViewer.ParseGlbForPreview(input,
                input.Length);
        }

        internal MerkabaDesignAsset Find(string assetId) =>
            _assets.Find(asset => string.Equals(asset.id, assetId,
                StringComparison.Ordinal));

        internal string AssetPath(string assetId)
        {
            ValidateId(assetId);
            return Path.Combine(_root, assetId + ".glb");
        }

        private string MetadataPath(string assetId)
        {
            ValidateId(assetId);
            return Path.Combine(_root, assetId + ".json");
        }

        private static string CopyAndHash(string sourcePath, string staging)
        {
            using var hash = SHA256.Create();
            using var input = new FileStream(sourcePath, FileMode.Open,
                FileAccess.Read, FileShare.Read, CopyBufferBytes,
                FileOptions.SequentialScan);
            if (input.Length == 0L)
                throw new InvalidDataException("Imported GLB is empty.");
            using (var output = new FileStream(staging, FileMode.CreateNew,
                       FileAccess.Write, FileShare.None, CopyBufferBytes,
                       FileOptions.SequentialScan))
            {
                byte[] buffer = new byte[CopyBufferBytes];
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    output.Write(buffer, 0, read);
                    hash.TransformBlock(buffer, 0, read, buffer, 0);
                }
                hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                output.Flush(true);
            }
            return Hex(hash.Hash);
        }

        private MerkabaDesignAsset CreateMetadata(string id,
            string sourcePath, MerkabaArtifactViewer.ParsedGlb parsed)
        {
            if (parsed.Positions.Length == 0)
                throw new InvalidDataException(
                    "Imported GLB contains no design geometry.");
            Bounds bounds = new(parsed.Positions[0], Vector3.zero);
            for (int index = 1; index < parsed.Positions.Length; index++)
                bounds.Encapsulate(parsed.Positions[index]);
            string name = Path.GetFileNameWithoutExtension(sourcePath)?.Trim();
            if (string.IsNullOrWhiteSpace(name)) name = "Imported object";
            if (name.Length > 80) name = name.Substring(0, 80);
            return new MerkabaDesignAsset
            {
                formatVersion = FormatVersion,
                id = id,
                displayName = name,
                boundsCenter = bounds.center,
                boundsSize = bounds.size,
                importedUtc = DateTime.UtcNow.ToString("O",
                    CultureInfo.InvariantCulture)
            };
        }

        private void WriteMetadata(MerkabaDesignAsset asset)
        {
            string destination = MetadataPath(asset.id);
            string temporary = destination + ".tmp";
            byte[] bytes = new UTF8Encoding(false).GetBytes(
                JsonUtility.ToJson(asset, true) + "\n");
            using (var stream = new FileStream(temporary, FileMode.Create,
                       FileAccess.Write, FileShare.None, 16 * 1024,
                       FileOptions.SequentialScan))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            MerkabaFilePublishing.Publish(temporary, destination);
        }

        private static MerkabaDesignAsset ReadMetadata(string path)
        {
            MerkabaDesignAsset asset = JsonUtility.FromJson<
                MerkabaDesignAsset>(File.ReadAllText(path, Encoding.UTF8));
            if (asset == null || asset.formatVersion != FormatVersion)
                throw new InvalidDataException(
                    "Design asset metadata has an unsupported format.");
            ValidateId(asset.id);
            if (!string.Equals(Path.GetFileNameWithoutExtension(path),
                    asset.id, StringComparison.Ordinal))
                throw new InvalidDataException(
                    "Design asset metadata does not match its filename.");
            if (string.IsNullOrWhiteSpace(asset.displayName))
                asset.displayName = "Imported object";
            return asset;
        }

        private static void ValidateId(string value)
        {
            if (value == null || value.Length != 64)
                throw new InvalidDataException("Invalid design asset ID.");
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if ((character < '0' || character > '9') &&
                    (character < 'a' || character > 'f'))
                    throw new InvalidDataException("Invalid design asset ID.");
            }
        }

        private static string Hex(byte[] bytes)
        {
            var text = new StringBuilder(bytes.Length * 2);
            foreach (byte value in bytes)
                text.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            return text.ToString();
        }
    }
}
