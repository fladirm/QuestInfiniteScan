using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Genesis.RoomScan.World
{
    /// <summary>
    /// Deterministic, dependency-free container for frames.jsonl and JPEG observations.
    /// It deliberately supports only the two expected path shapes and extracts through a
    /// staging directory, preventing traversal and partially visible restored captures.
    /// </summary>
    public static class ChunkKeyframeArchive
    {
        public const int FormatVersion = 1;
        private const uint Magic = 0x4B534951; // "QISK"
        private const int MaximumEntries = 20_001;
        private const int MaximumNameBytes = 96;
        private const long MaximumEntryBytes = 128L * 1024 * 1024;

        public static bool TryWriteDirectory(Stream destination, string keyframeDirectory,
            out string error)
        {
            error = null;
            if (destination == null || !destination.CanWrite)
            {
                error = "Keyframe archive destination is not writable.";
                return false;
            }
            if (string.IsNullOrEmpty(keyframeDirectory) ||
                !Directory.Exists(keyframeDirectory))
            {
                error = "Keyframe directory does not exist.";
                return false;
            }

            try
            {
                string root = Path.GetFullPath(keyframeDirectory);
                string manifest = Path.Combine(root, "frames.jsonl");
                if (!File.Exists(manifest))
                {
                    error = "Keyframe directory has no frames.jsonl.";
                    return false;
                }

                var entries = new List<(string Name, string Path)>
                {
                    ("frames.jsonl", manifest)
                };
                string images = Path.Combine(root, "images");
                if (Directory.Exists(images))
                {
                    if ((File.GetAttributes(images) & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException("Keyframe images directory cannot be a link.");
                    foreach (string image in Directory.GetFiles(images, "*.jpg",
                                 SearchOption.TopDirectoryOnly))
                    {
                        string name = "images/" + Path.GetFileName(image);
                        if (!IsAllowedEntryName(name))
                            throw new InvalidDataException("Unexpected keyframe filename: " + name);
                        entries.Add((name, image));
                    }
                }
                entries.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));
                if (entries.Count > MaximumEntries)
                    throw new InvalidDataException("Keyframe count exceeds the archive limit.");

                using var writer = new BinaryWriter(destination, Encoding.UTF8, true);
                writer.Write(Magic);
                writer.Write(FormatVersion);
                writer.Write(entries.Count);
                var copyBuffer = new byte[1024 * 1024];
                long aggregateLength = 0;
                for (int i = 0; i < entries.Count; i++)
                {
                    string fullPath = Path.GetFullPath(entries[i].Path);
                    if (!StoragePath.IsContained(root, fullPath) ||
                        (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException("Keyframe entry escapes its capture directory.");
                    byte[] nameBytes = Encoding.UTF8.GetBytes(entries[i].Name);
                    if (nameBytes.Length == 0 || nameBytes.Length > MaximumNameBytes)
                        throw new InvalidDataException("Keyframe entry name length is invalid.");
                    long length = new FileInfo(fullPath).Length;
                    if (length < 0 || length > MaximumEntryBytes)
                        throw new InvalidDataException("Keyframe entry exceeds the size limit.");
                    aggregateLength = checked(aggregateLength + length);
                    if (aggregateLength > WorldSchema.MaximumArtifactBytes)
                        throw new InvalidDataException("Keyframe archive exceeds the artifact limit.");
                    writer.Write(nameBytes.Length);
                    writer.Write(nameBytes);
                    writer.Write(length);
                    using var source = new FileStream(fullPath, FileMode.Open, FileAccess.Read,
                        FileShare.Read, copyBuffer.Length, FileOptions.SequentialScan);
                    CopyExact(source, destination, length, copyBuffer);
                }
                writer.Flush();
                return true;
            }
            catch (Exception exception)
            {
                error = "Keyframe archive write failed: " + exception.Message;
                return false;
            }
        }

        public static bool TryExtract(Stream source, string destinationDirectory,
            out string error)
        {
            error = null;
            if (source == null || !source.CanRead || string.IsNullOrEmpty(destinationDirectory))
            {
                error = "Readable archive and destination directory are required.";
                return false;
            }

            string finalDirectory = Path.GetFullPath(destinationDirectory);
            string parent = Path.GetDirectoryName(finalDirectory);
            string staging = finalDirectory + ".restore-" + Guid.NewGuid().ToString("N");
            if (string.IsNullOrEmpty(parent))
            {
                error = "Keyframe destination has no parent directory.";
                return false;
            }
            try
            {
                if (Directory.Exists(finalDirectory))
                {
                    error = "Keyframe destination already exists.";
                    return false;
                }
                Directory.CreateDirectory(parent);
                Directory.CreateDirectory(staging);
                using var reader = new BinaryReader(source, Encoding.UTF8, true);
                if (reader.ReadUInt32() != Magic)
                    throw new InvalidDataException("Keyframe archive magic is invalid.");
                int version = reader.ReadInt32();
                if (version != FormatVersion)
                    throw new InvalidDataException($"Unsupported keyframe archive {version}.");
                int count = reader.ReadInt32();
                if (count <= 0 || count > MaximumEntries)
                    throw new InvalidDataException("Keyframe archive entry count is invalid.");

                var names = new HashSet<string>(StringComparer.Ordinal);
                bool hasManifest = false;
                var copyBuffer = new byte[1024 * 1024];
                long aggregateLength = 0;
                for (int i = 0; i < count; i++)
                {
                    int nameLength = reader.ReadInt32();
                    if (nameLength <= 0 || nameLength > MaximumNameBytes)
                        throw new InvalidDataException("Keyframe entry name length is invalid.");
                    byte[] nameBytes = reader.ReadBytes(nameLength);
                    if (nameBytes.Length != nameLength)
                        throw new EndOfStreamException("Keyframe entry name is truncated.");
                    string name = new UTF8Encoding(false, true).GetString(nameBytes);
                    if (!IsAllowedEntryName(name) || !names.Add(name))
                        throw new InvalidDataException("Keyframe entry name is unsafe or duplicated.");
                    long length = reader.ReadInt64();
                    if (length < 0 || length > MaximumEntryBytes)
                        throw new InvalidDataException("Keyframe entry length is invalid.");
                    aggregateLength = checked(aggregateLength + length);
                    if (aggregateLength > WorldSchema.MaximumArtifactBytes)
                        throw new InvalidDataException("Keyframe archive exceeds the artifact limit.");
                    string output = StoragePath.CombineContained(staging, name.Split('/'));
                    string outputParent = Path.GetDirectoryName(output);
                    if (!string.IsNullOrEmpty(outputParent))
                        Directory.CreateDirectory(outputParent);
                    using (var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write,
                               FileShare.None, copyBuffer.Length, FileOptions.WriteThrough))
                    {
                        CopyExact(source, file, length, copyBuffer);
                        file.Flush(true);
                    }
                    hasManifest |= string.Equals(name, "frames.jsonl",
                        StringComparison.Ordinal);
                }
                if (!hasManifest)
                    throw new InvalidDataException("Keyframe archive has no frames.jsonl.");
                if (source.CanSeek && source.Position != source.Length)
                    throw new InvalidDataException("Keyframe archive has trailing bytes.");
                Directory.Move(staging, finalDirectory);
                return true;
            }
            catch (Exception exception)
            {
                error = "Keyframe archive rejected: " + exception.Message;
                try
                {
                    if (Directory.Exists(staging))
                        Directory.Delete(staging, true);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                return false;
            }
        }

        private static bool IsAllowedEntryName(string name)
        {
            if (string.Equals(name, "frames.jsonl", StringComparison.Ordinal))
                return true;
            if (!name.StartsWith("images/", StringComparison.Ordinal) ||
                !name.EndsWith(".jpg", StringComparison.Ordinal) ||
                !StoragePath.IsSafeRelativePath(name))
                return false;
            string stem = name.Substring(7, name.Length - 11);
            if (stem.Length != 6)
                return false;
            for (int i = 0; i < stem.Length; i++)
                if (stem[i] < '0' || stem[i] > '9')
                    return false;
            return true;
        }

        private static void CopyExact(Stream source, Stream destination, long length,
            byte[] buffer)
        {
            long remaining = length;
            while (remaining > 0)
            {
                int requested = (int)Math.Min(buffer.Length, remaining);
                int read = source.Read(buffer, 0, requested);
                if (read <= 0)
                    throw new EndOfStreamException("Keyframe entry payload is truncated.");
                destination.Write(buffer, 0, read);
                remaining -= read;
            }
        }
    }
}
