using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Genesis.RoomScan.UI;
using UnityEngine;

namespace Genesis.RoomScan
{
    // Versioned transport of the existing committed store, never another world
    // or a serialization of derived resident pages. Capture requires the caller's
    // whole-operation mutation hold; import validates an isolated store first.
    internal static class MerkabaNativePackage
    {
        internal const int Version = 1;
        internal const string ExtensionName = "M8_flower_thread";
        internal const string DirectoryName = "m8-native";
        internal const string ManifestRelativePath = DirectoryName + "/package-manifest.json";
        internal const int MaximumManifestBytes = 8 * 1024 * 1024;
        internal const int MaximumEntries = 16384;
        internal const long MaximumEntryBytes = 16L * 1024 * 1024 * 1024;
        internal const long MaximumPayloadBytes = 64L * 1024 * 1024 * 1024;
        private const int PacketBytes = 64 * 1024;
        private static readonly string[] StoreNames =
        {
            MerkabaSsdStore.M8BaseFileName, MerkabaSsdStore.M8LiveFileName,
            MerkabaSsdStore.ThroughBaseFileName, MerkabaSsdStore.ThroughLiveFileName,
            MerkabaSsdStore.FlowerDetailFileName, MerkabaSsdStore.ThreadAtlasFileName
        };

        internal readonly struct Source
        {
            internal readonly string SessionDirectory, DesignPath, AnnotationsPath, LibraryDirectory, DisplayName;
            internal readonly MerkabaSessionManifest Manifest;
            internal Source(string sessionDirectory, string designPath, string annotationsPath,
                string libraryDirectory, MerkabaSessionManifest manifest, string displayName)
            {
                SessionDirectory = Path.GetFullPath(sessionDirectory);
                DesignPath = designPath; AnnotationsPath = annotationsPath;
                LibraryDirectory = Path.GetFullPath(libraryDirectory);
                Manifest = manifest?.CloneForSession(manifest.SessionUuid) ??
                    throw new ArgumentNullException(nameof(manifest));
                DisplayName = displayName ?? "Imported model";
            }
        }

        [Serializable]
        internal sealed class Entry
        {
            public string path;
            public long offset;
            public long length;
            public string sha256;
        }

        [Serializable]
        internal sealed class PackageManifest
        {
            public int version;
            public uint tableHash;
            public int recordVersion;
            public string sessionUuid;
            public string anchorUuid;
            public string commitGeneration;
            public string displayName;
            public float[] anchorAtSave;
            public long payloadBytes;
            public string[] assetIds;
            public Entry[] entries;
        }

        internal sealed class Snapshot : IDisposable
        {
            private readonly string _directory;
            internal readonly PackageManifest Manifest;
            internal readonly byte[] ManifestBytes;
            internal long PayloadBytes => Manifest.payloadBytes;
            private bool _disposed;
            internal Snapshot(string directory, PackageManifest manifest, byte[] manifestBytes)
            { _directory = directory; Manifest = manifest; ManifestBytes = manifestBytes; }

            internal void WritePayload(Stream destination, CancellationToken cancellationToken)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(Snapshot));
                var packet = new byte[PacketBytes];
                long written = 0;
                foreach (Entry entry in Manifest.entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (written != entry.offset) throw new InvalidDataException("Native payload offset receipt differs.");
                    using var input = File.OpenRead(Resolve(_directory, entry.path));
                    CopyExact(input, destination, entry.length, packet, cancellationToken);
                    written += entry.length;
                    while ((written & 3L) != 0L) { destination.WriteByte(0); written++; }
                }
                if (written != PayloadBytes) throw new InvalidDataException("Native payload length receipt differs.");
            }

            internal void WriteDirectory(string packageDirectory, CancellationToken cancellationToken)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(Snapshot));
                string root = Path.Combine(packageDirectory, DirectoryName);
                if (Directory.Exists(root)) throw new IOException("Native package staging already exists.");
                System.IO.Directory.CreateDirectory(root);
                var packet = new byte[PacketBytes];
                foreach (Entry entry in Manifest.entries)
                {
                    string target = Resolve(root, entry.path);
                    System.IO.Directory.CreateDirectory(Path.GetDirectoryName(target));
                    using var input = File.OpenRead(Resolve(_directory, entry.path));
                    using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write,
                        FileShare.None, PacketBytes);
                    CopyExact(input, output, entry.length, packet, cancellationToken);
                    output.Flush(true);
                }
                cancellationToken.ThrowIfCancellationRequested();
                using var manifest = new FileStream(Path.Combine(root, "package-manifest.json"),
                    FileMode.CreateNew, FileAccess.Write, FileShare.None, PacketBytes);
                manifest.Write(ManifestBytes, 0, ManifestBytes.Length);
                manifest.Flush(true);
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                DeleteOwnedDirectory(_directory);
            }
        }

        internal static Snapshot Capture(Source source, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string directory = NewDirectory();
            try
            {
                byte[] expected = EncodeManifest(source.Manifest);
                byte[] current = ReadManifestBytes(source.SessionDirectory);
                if (!SameBytes(expected, current))
                    throw new InvalidDataException("Native export source is not the captured committed manifest.");
                string session = Path.Combine(directory, "session");
                System.IO.Directory.CreateDirectory(session);
                var entries = new List<Entry>();
                long offset = 0;
                AddFile(entries, directory, "session/" + MerkabaSsdStore.ManifestFileName,
                    Path.Combine(source.SessionDirectory, MerkabaSsdStore.ManifestFileName),
                    MerkabaSessionManifest.ByteSize, ref offset, cancellationToken);
                for (int index = 0; index < StoreNames.Length; index++)
                    AddFile(entries, directory, "session/" + StoreNames[index],
                        Path.Combine(source.SessionDirectory, StoreNames[index]), source.Manifest.ValidEnds[index],
                        ref offset, cancellationToken);

                string design = Path.Combine(session, MerkabaSessionCatalog.DesignFileName);
                CopyDocumentOrEmpty(source.DesignPath, design, true, cancellationToken);
                string annotations = Path.Combine(session, MerkabaSessionCatalog.AnnotationsFileName);
                CopyDocumentOrEmpty(source.AnnotationsPath, annotations, false, cancellationToken);
                string[] assets = ReadAssetIds(design);
                AddExisting(entries, directory, "session/" + MerkabaSessionCatalog.DesignFileName,
                    ref offset, cancellationToken);
                AddExisting(entries, directory, "session/" + MerkabaSessionCatalog.AnnotationsFileName,
                    ref offset, cancellationToken);
                foreach (string id in assets)
                {
                    RequireDocumentBudget(Path.Combine(source.LibraryDirectory, id + ".json"));
                    AddFile(entries, directory, "library/" + id + ".glb",
                        Path.Combine(source.LibraryDirectory, id + ".glb"), -1, ref offset, cancellationToken);
                    if (entries[entries.Count - 1].sha256 != id)
                        throw new InvalidDataException("Referenced asset does not match its content address: " + id);
                    AddFile(entries, directory, "library/" + id + ".json",
                        Path.Combine(source.LibraryDirectory, id + ".json"), -1, ref offset, cancellationToken);
                    MerkabaDesignLibrary.ReadMetadata(Path.Combine(directory, "library", id + ".json"));
                }
                if (!SameBytes(expected, ReadManifestBytes(source.SessionDirectory)))
                    throw new InvalidDataException("Committed store changed during native export.");
                var manifest = new PackageManifest
                {
                    version = Version, tableHash = MerkabaSphereFlowerAuthority.FrozenTableHash,
                    recordVersion = MerkabaRecordHeader.CurrentVersion,
                    sessionUuid = source.Manifest.SessionUuid.ToString("D"),
                    anchorUuid = source.Manifest.AnchorUuid.ToString("D"),
                    commitGeneration = source.Manifest.CommitGeneration.ToString("x16", CultureInfo.InvariantCulture),
                    displayName = source.DisplayName, payloadBytes = offset,
                    anchorAtSave = MatrixValues(source.Manifest.AnchorAtSave), assetIds = assets, entries = entries.ToArray()
                };
                byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(manifest, false));
                if (bytes.Length > MaximumManifestBytes) throw new InvalidDataException("Native package manifest exceeds its budget.");
                ValidateExtracted(directory, manifest, cancellationToken);
                return new Snapshot(directory, manifest, bytes);
            }
            catch { DeleteOwnedDirectory(directory); throw; }
        }

        internal sealed class ValidatedImport : IDisposable
        {
            private readonly string _directory;
            private readonly MerkabaSsdStore _store;
            private readonly PackageManifest _package;
            internal MerkabaSessionManifest Manifest { get; }
            internal string DisplayName => _package.displayName;
            private bool _disposed;
            internal ValidatedImport(string directory, PackageManifest package, MerkabaSsdStore store)
            {
                _directory = directory; _package = package; _store = store;
                Manifest = store.CurrentCommittedState().Manifest;
            }

            internal void Install(string freshSessionDirectory, Guid newSessionId,
                string libraryDirectory, CancellationToken cancellationToken)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(ValidatedImport));
                cancellationToken.ThrowIfCancellationRequested();
                if (newSessionId == Guid.Empty || newSessionId == Manifest.SessionUuid ||
                    !System.IO.Directory.Exists(freshSessionDirectory))
                    throw new InvalidOperationException("Native import needs a new isolated catalog session.");
                foreach (string path in System.IO.Directory.EnumerateFileSystemEntries(freshSessionDirectory))
                    if (Path.GetFileName(path) != MerkabaSessionCatalog.MetadataFileName || !File.Exists(path))
                        throw new InvalidOperationException("Native import destination is not a fresh catalog directory.");
                // Preflight every shared asset collision before publishing any.
                foreach (Entry entry in _package.entries)
                    if (entry.path.StartsWith("library/", StringComparison.Ordinal))
                    {
                        string target = Resolve(libraryDirectory, entry.path.Substring(8));
                        if (File.Exists(target) && HashFile(target, cancellationToken) != entry.sha256)
                            throw new InvalidDataException("Existing design asset/index differs from imported snapshot: " + entry.path);
                    }
                _store.CloneCommittedTo(freshSessionDirectory, newSessionId);
                var packet = new byte[PacketBytes];
                foreach (Entry entry in _package.entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string target;
                    if (entry.path == "session/" + MerkabaSessionCatalog.DesignFileName ||
                        entry.path == "session/" + MerkabaSessionCatalog.AnnotationsFileName)
                        target = Resolve(freshSessionDirectory, entry.path.Substring(8));
                    else if (entry.path.StartsWith("library/", StringComparison.Ordinal))
                        target = Resolve(libraryDirectory, entry.path.Substring(8));
                    else continue;
                    if (File.Exists(target))
                    {
                        if (!entry.path.StartsWith("library/", StringComparison.Ordinal) ||
                            HashFile(target, cancellationToken) != entry.sha256)
                            throw new InvalidDataException("Native import target changed after preflight: " + entry.path);
                        continue;
                    }
                    System.IO.Directory.CreateDirectory(Path.GetDirectoryName(target));
                    string temporary = target + ".native-" + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        using (var input = File.OpenRead(Resolve(_directory, entry.path)))
                        using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                            FileShare.None, PacketBytes))
                        { CopyExact(input, output, entry.length, packet, cancellationToken); output.Flush(true); }
                        cancellationToken.ThrowIfCancellationRequested();
                        File.Move(temporary, target); // Never overwrite a concurrent publication.
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                }
                cancellationToken.ThrowIfCancellationRequested();
                MerkabaSessionManifest installed = MerkabaSsdStore.ReadManifestFromDirectory(freshSessionDirectory);
                if (installed.SessionUuid != newSessionId || installed.AnchorUuid != Manifest.AnchorUuid)
                    throw new InvalidDataException("Native import did not retain the committed anchor identity.");
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                DeleteOwnedDirectory(_directory);
            }
        }

        // Null means preview-only. A declared but invalid native extension is
        // never silently downgraded to a mesh or merged into the active world.
        internal static ValidatedImport TryRead(string filePath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var input = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                FileShare.Read, PacketBytes, FileOptions.RandomAccess);
            byte[] header = new byte[12];
            ReadExact(input, header, 0, 4);
            input.Position = 0;
            uint magic = BitConverter.ToUInt32(header, 0);
            if (magic == 0x46546c67u) return ReadGlb(input, cancellationToken);
            if ((magic & 0xffffu) == 0x4b50u) return ReadZip(input, cancellationToken);
            return null;
        }

        [Serializable] private sealed class NativeReference
        { public int version; public int manifest; public int[] payloads; }
        [Serializable] private sealed class Extensions
        { public NativeReference M8_flower_thread; }
        [Serializable] private sealed class BufferView
        { public int buffer; public long byteOffset; public long byteLength; public int byteStride; }
        [Serializable] private sealed class Glb
        { public Extensions extensions; public BufferView[] bufferViews; }
        [Serializable] private sealed class TilesReference
        { public int version; public string manifest; }
        [Serializable] private sealed class TilesExtras
        { public TilesReference M8_flower_thread; }
        [Serializable] private sealed class Tiles
        { public TilesExtras extras; }

        private static ValidatedImport ReadGlb(FileStream input, CancellationToken token)
        {
            var header = new byte[20]; ReadExact(input, header, 0, header.Length);
            if (BitConverter.ToUInt32(header, 4) != 2u || BitConverter.ToUInt32(header, 8) != input.Length ||
                (BitConverter.ToUInt32(header, 12) & 3u) != 0u ||
                BitConverter.ToUInt32(header, 16) != 0x4e4f534au)
                throw new InvalidDataException("Invalid GLB container.");
            int jsonLength = checked((int)BitConverter.ToUInt32(header, 12));
            string json = ReadJson(input, jsonLength);
            if (!HasNativeProperty(json)) return null;
            Glb glb = JsonUtility.FromJson<Glb>(json);
            NativeReference reference = glb?.extensions?.M8_flower_thread;
            if (reference == null || reference.version != Version || reference.payloads == null ||
                reference.payloads.Length != 1 || glb.bufferViews == null)
                throw new InvalidDataException("Unsupported M8_flower_thread reference.");
            ReadExact(input, header, 0, 8);
            long binaryLength = BitConverter.ToUInt32(header, 0), binaryStart = input.Position;
            if (BitConverter.ToUInt32(header, 4) != 0x004e4942u || binaryStart + binaryLength != input.Length)
                throw new InvalidDataException("Invalid GLB native binary range.");
            BufferView manifestView = ReadView(glb, reference.manifest, binaryLength);
            BufferView payloadView = ReadView(glb, reference.payloads[0], binaryLength);
            if (manifestView.byteOffset + manifestView.byteLength > payloadView.byteOffset)
                throw new InvalidDataException("Native GLB manifest/payload views overlap or are unordered.");
            input.Position = binaryStart + manifestView.byteOffset;
            PackageManifest manifest = DecodeManifest(ReadJson(input, manifestView.byteLength));
            if (manifest.payloadBytes != payloadView.byteLength)
                throw new InvalidDataException("Native payload view length differs from manifest.");
            return Extract(manifest, (entry, destination, packet) =>
            {
                input.Position = checked(binaryStart + payloadView.byteOffset + entry.offset);
                CopyAndHash(input, destination, entry, packet, token);
            }, token);
        }

        private static ValidatedImport ReadZip(FileStream input, CancellationToken token)
        {
            using var archive = new ZipArchive(input, ZipArchiveMode.Read, true);
            if (archive.Entries.Count > 131072) throw new InvalidDataException("ZIP entry count exceeds native import budget.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                token.ThrowIfCancellationRequested();
                ValidateRelativePath(entry.FullName.TrimEnd('/'));
                if (!names.Add(entry.FullName) || ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000)
                    throw new InvalidDataException("Duplicate/symlink ZIP path is not allowed.");
            }
            ZipArchiveEntry tileset = archive.GetEntry("tileset.json");
            if (tileset == null) return null;
            string json;
            using (Stream stream = tileset.Open()) json = ReadJson(stream, tileset.Length);
            if (!HasNativeProperty(json)) return null;
            Tiles tiles = JsonUtility.FromJson<Tiles>(json);
            TilesReference reference = tiles?.extras?.M8_flower_thread;
            if (reference == null || reference.version != Version || reference.manifest != ManifestRelativePath)
                throw new InvalidDataException("Unsupported native tileset manifest reference.");
            ZipArchiveEntry manifestEntry = archive.GetEntry(ManifestRelativePath) ??
                throw new InvalidDataException("Native tileset manifest is missing.");
            PackageManifest manifest;
            using (Stream stream = manifestEntry.Open()) manifest = DecodeManifest(ReadJson(stream, manifestEntry.Length));
            return Extract(manifest, (entry, destination, packet) =>
            {
                ZipArchiveEntry record = archive.GetEntry(DirectoryName + "/" + entry.path) ??
                    throw new InvalidDataException("Native payload entry is missing: " + entry.path);
                if (record.Length != entry.length) throw new InvalidDataException("ZIP/native entry length mismatch.");
                using Stream stream = record.Open();
                CopyAndHash(stream, destination, entry, packet, token);
                if (stream.ReadByte() != -1) throw new InvalidDataException("ZIP entry exceeds its declared native length.");
            }, token);
        }

        private static ValidatedImport Extract(PackageManifest manifest,
            Action<Entry, Stream, byte[]> copy, CancellationToken token)
        {
            string directory = NewDirectory();
            try
            {
                var packet = new byte[PacketBytes];
                foreach (Entry entry in manifest.entries)
                {
                    token.ThrowIfCancellationRequested();
                    string target = Resolve(directory, entry.path);
                    System.IO.Directory.CreateDirectory(Path.GetDirectoryName(target));
                    using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, PacketBytes);
                    copy(entry, output, packet);
                    output.Flush(true);
                }
                MerkabaSsdStore store = ValidateExtracted(directory, manifest, token);
                return new ValidatedImport(directory, manifest, store);
            }
            catch { DeleteOwnedDirectory(directory); throw; }
        }

        private static PackageManifest DecodeManifest(string json)
        {
            PackageManifest manifest = JsonUtility.FromJson<PackageManifest>(json);
            if (manifest == null || manifest.version != Version ||
                manifest.tableHash != MerkabaSphereFlowerAuthority.FrozenTableHash ||
                manifest.recordVersion != MerkabaRecordHeader.CurrentVersion ||
                !Guid.TryParse(manifest.sessionUuid, out Guid session) || session == Guid.Empty ||
                !Guid.TryParse(manifest.anchorUuid, out Guid anchor) || anchor == Guid.Empty ||
                !ulong.TryParse(manifest.commitGeneration, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong generation) ||
                generation == 0 || manifest.anchorAtSave == null || manifest.anchorAtSave.Length != 16 ||
                manifest.assetIds == null || manifest.entries == null || manifest.entries.Length > MaximumEntries ||
                manifest.entries.Length != 9 + 2 * manifest.assetIds.Length)
                throw new InvalidDataException("Invalid native package identity/version/index.");
            var expected = new HashSet<string>(StringComparer.Ordinal)
            { "session/" + MerkabaSsdStore.ManifestFileName, "session/" + MerkabaSessionCatalog.DesignFileName,
                "session/" + MerkabaSessionCatalog.AnnotationsFileName };
            foreach (string name in StoreNames) expected.Add("session/" + name);
            string previous = null;
            foreach (string id in manifest.assetIds)
            {
                MerkabaDesignLibrary.ValidateId(id);
                if (previous != null && string.CompareOrdinal(previous, id) >= 0)
                    throw new InvalidDataException("Native asset index is not unique/canonical.");
                expected.Add("library/" + id + ".glb"); expected.Add("library/" + id + ".json");
                previous = id;
            }
            long end = 0;
            foreach (Entry entry in manifest.entries)
            {
                if (entry == null || !expected.Remove(entry.path))
                    throw new InvalidDataException("Duplicate or unexpected native payload path.");
                ValidateRelativePath(entry.path);
                MerkabaDesignLibrary.ValidateId(entry.sha256);
                if (entry.offset != end || entry.length < 0 || entry.length > MaximumEntryBytes)
                    throw new InvalidDataException("Invalid native payload offset/length.");
                end = Align4(checked(end + entry.length));
                if (end > MaximumPayloadBytes) throw new InvalidDataException("Native package exceeds import byte budget.");
            }
            if (expected.Count != 0 || end != manifest.payloadBytes)
                throw new InvalidDataException("Native package files do not cover its payload.");
            return manifest;
        }

        private static MerkabaSsdStore ValidateExtracted(string directory, PackageManifest package, CancellationToken token)
        {
            // The same version/offset/index predicate also applies to capture.
            DecodeManifest(JsonUtility.ToJson(package, false));
            string session = Path.Combine(directory, "session");
            MerkabaSessionManifest manifest = MerkabaSsdStore.ReadManifestFromDirectory(session);
            if (manifest.SessionUuid.ToString("D") != package.sessionUuid ||
                manifest.AnchorUuid.ToString("D") != package.anchorUuid ||
                manifest.CommitGeneration.ToString("x16", CultureInfo.InvariantCulture) != package.commitGeneration)
                throw new InvalidDataException("Native package identity differs from its committed session.");
            for (int index = 0; index < 16; index++)
                if (BitConverter.SingleToInt32Bits(manifest.AnchorAtSave[index]) !=
                    BitConverter.SingleToInt32Bits(package.anchorAtSave[index]))
                    throw new InvalidDataException("Native anchor transform differs from the committed manifest.");
            for (int index = 0; index < StoreNames.Length; index++)
                if (new FileInfo(Path.Combine(session, StoreNames[index])).Length != manifest.ValidEnds[index])
                    throw new InvalidDataException("Native store is not the exact manifest-selected prefix.");
            string design = Path.Combine(session, MerkabaSessionCatalog.DesignFileName);
            string annotations = Path.Combine(session, MerkabaSessionCatalog.AnnotationsFileName);
            RequireDocumentBudget(design); RequireDocumentBudget(annotations);
            string[] ids = ReadAssetIds(design);
            if (ids.Length != package.assetIds.Length) throw new InvalidDataException("Native design asset references differ.");
            for (int index = 0; index < ids.Length; index++)
            {
                if (ids[index] != package.assetIds[index]) throw new InvalidDataException("Native design asset index differs.");
                string asset = Path.Combine(directory, "library", ids[index] + ".glb");
                if (HashFile(asset, token) != ids[index]) throw new InvalidDataException("Native asset content address differs.");
                string metadata = Path.Combine(directory, "library", ids[index] + ".json");
                RequireDocumentBudget(metadata); MerkabaDesignLibrary.ReadMetadata(metadata);
                using var input = File.OpenRead(asset);
                // The existing preview validator bounds decoded geometry and
                // images. Bound its JSON allocation before entering it too.
                var header = new byte[20]; ReadExact(input, header, 0, header.Length);
                if (BitConverter.ToUInt32(header, 12) > MaximumManifestBytes)
                    throw new InvalidDataException("Native asset GLB JSON exceeds its import budget.");
                input.Position = 0;
                using var parsed = MerkabaArtifactViewer.ParseGlbForPreview(input, input.Length);
            }
            MerkabaArtifactViewer.ValidateNativeAnnotations(annotations);
            token.ThrowIfCancellationRequested();
            var store = new MerkabaSsdStore(session);
            store.OpenCommitted(new CancellationProgress(token)); // All 13 record kinds, CRC/address/epoch closure.
            token.ThrowIfCancellationRequested();
            return store;
        }

        private sealed class CancellationProgress : IProgress<OperationWorkProgress>
        {
            private readonly CancellationToken _token;
            internal CancellationProgress(CancellationToken token) { _token = token; }
            public void Report(OperationWorkProgress value) => _token.ThrowIfCancellationRequested();
        }

        private static string[] ReadAssetIds(string designPath)
        {
            RequireDocumentBudget(designPath);
            MerkabaDesignDocument design = MerkabaDesignDocument.Load(designPath);
            var ids = new SortedSet<string>(StringComparer.Ordinal);
            foreach (MerkabaDesignInstance instance in design.instances)
            {
                if (instance == null) throw new InvalidDataException("Null native design instance.");
                MerkabaDesignLibrary.ValidateId(instance.assetId); ids.Add(instance.assetId);
            }
            if (ids.Count > (MaximumEntries - 9) / 2) throw new InvalidDataException("Native design asset count exceeds package budget.");
            var result = new string[ids.Count]; ids.CopyTo(result); return result;
        }

        private static void CopyDocumentOrEmpty(string source, string target, bool design, CancellationToken token)
        {
            if (!string.IsNullOrWhiteSpace(source) && File.Exists(source))
            {
                RequireDocumentBudget(source);
                using var input = File.OpenRead(source);
                using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, PacketBytes);
                CopyExact(input, output, input.Length, new byte[PacketBytes], token); output.Flush(true);
            }
            else if (design) new MerkabaDesignDocument().Save(target);
            else MerkabaArtifactViewer.ValidateNativeAnnotations(target, true);
            RequireDocumentBudget(target);
        }

        private static void AddFile(List<Entry> entries, string directory, string relative,
            string source, long length, ref long offset, CancellationToken token)
        {
            if (length < 0) length = new FileInfo(source).Length;
            if (length > MaximumEntryBytes) throw new InvalidDataException("Native source entry exceeds package budget.");
            string target = Resolve(directory, relative);
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(target));
            using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, PacketBytes))
            {
                if (length != 0)
                {
                    using var input = File.OpenRead(source);
                    if (input.Length < length) throw new InvalidDataException("Committed native source is truncated.");
                    CopyExact(input, output, length, new byte[PacketBytes], token);
                }
                output.Flush(true);
            }
            AddExisting(entries, directory, relative, ref offset, token);
        }

        private static void AddExisting(List<Entry> entries, string directory, string relative,
            ref long offset, CancellationToken token)
        {
            string path = Resolve(directory, relative);
            long length = new FileInfo(path).Length;
            entries.Add(new Entry { path = relative, offset = offset, length = length, sha256 = HashFile(path, token) });
            offset = Align4(checked(offset + length));
            if (entries.Count > MaximumEntries || offset > MaximumPayloadBytes)
                throw new InvalidDataException("Native package exceeds its entry/byte budget.");
        }

        private static void CopyAndHash(Stream source, Stream destination, Entry entry, byte[] packet, CancellationToken token)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long left = entry.length;
            while (left != 0)
            {
                token.ThrowIfCancellationRequested();
                int count = source.Read(packet, 0, (int)Math.Min(left, packet.Length));
                if (count == 0) throw new EndOfStreamException("Truncated native package entry.");
                hash.AppendData(packet, 0, count); destination.Write(packet, 0, count); left -= count;
            }
            if (Hex(hash.GetHashAndReset()) != entry.sha256)
                throw new InvalidDataException("Native payload SHA256 mismatch: " + entry.path);
        }

        private static string HashFile(string path, CancellationToken token)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using var input = File.OpenRead(path);
            var packet = new byte[PacketBytes]; int count;
            while ((count = input.Read(packet, 0, packet.Length)) != 0)
            { token.ThrowIfCancellationRequested(); hash.AppendData(packet, 0, count); }
            return Hex(hash.GetHashAndReset());
        }

        private static void CopyExact(Stream source, Stream destination, long bytes, byte[] packet, CancellationToken token)
        {
            while (bytes > 0)
            {
                token.ThrowIfCancellationRequested();
                int count = source.Read(packet, 0, (int)Math.Min(bytes, packet.Length));
                if (count == 0) throw new EndOfStreamException("Native source prefix is truncated.");
                destination.Write(packet, 0, count); bytes -= count;
            }
        }

        private static BufferView ReadView(Glb glb, int index, long binaryLength)
        {
            if ((uint)index >= (uint)glb.bufferViews.Length) throw new InvalidDataException("Invalid native bufferView index.");
            BufferView view = glb.bufferViews[index];
            if (view == null || view.buffer != 0 || view.byteStride != 0 || view.byteOffset < 0 ||
                (view.byteOffset & 3L) != 0 || view.byteLength <= 0 || view.byteOffset > binaryLength - view.byteLength)
                throw new InvalidDataException("Invalid native GLB bufferView range.");
            return view;
        }

        private static string ReadJson(Stream source, long length)
        {
            if (length <= 0 || length > MaximumManifestBytes)
                throw new InvalidDataException("Native JSON exceeds its manifest budget.");
            byte[] bytes = new byte[checked((int)length)]; ReadExact(source, bytes, 0, bytes.Length);
            return new UTF8Encoding(false, true).GetString(bytes).TrimEnd(' ', '\0', '\r', '\n', '\t');
        }

        private static bool HasNativeProperty(string json)
        {
            // Only exact writer-produced property names. String values cannot
            // introduce a declaration; duplicate declarations are invalid.
            int found = 0;
            for (int cursor = 0; cursor < json.Length; cursor++)
            {
                if (json[cursor] != '"') continue;
                int first = ++cursor; bool escaped = false;
                while (cursor < json.Length && json[cursor] != '"')
                { if (json[cursor] == '\\') { escaped = true; cursor++; } cursor++; }
                if (cursor >= json.Length) throw new InvalidDataException("Truncated GLB JSON string.");
                int end = cursor, following = cursor + 1;
                while (following < json.Length && char.IsWhiteSpace(json[following])) following++;
                if (following < json.Length && json[following] == ':' &&
                    IsNativePropertyName(json.AsSpan(first, end - first), escaped)) found++;
            }
            if (found > 1) throw new InvalidDataException("Duplicate native package declaration.");
            return found == 1;
        }

        private static bool IsNativePropertyName(ReadOnlySpan<char> raw, bool escaped)
        {
            if (!escaped) return raw.SequenceEqual(ExtensionName.AsSpan());
            // Do not let an escaped reserved declaration masquerade as an
            // ordinary preview. Only this one fixed property is decoded here;
            // native transport otherwise accepts the writer's canonical keys.
            int matched = 0;
            for (int index = 0; index < raw.Length; index++)
            {
                char value = raw[index];
                if (value == '\\')
                {
                    if (++index >= raw.Length) throw new InvalidDataException("Invalid JSON property escape.");
                    value = raw[index];
                    if (value == 'u')
                    {
                        if (index + 4 >= raw.Length || !ushort.TryParse(raw.Slice(index + 1, 4),
                                NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ushort code))
                            throw new InvalidDataException("Invalid JSON property unicode escape.");
                        value = (char)code; index += 4;
                    }
                    else if (value != '"' && value != '\\' && value != '/') return false;
                }
                if (matched >= ExtensionName.Length || value != ExtensionName[matched++]) return false;
            }
            if (matched == ExtensionName.Length)
                throw new InvalidDataException("Native package declaration must use its canonical unescaped key.");
            return false;
        }

        private static void ValidateRelativePath(string path)
        {
            if (string.IsNullOrEmpty(path) || path[0] == '/' || path.Contains("\\") ||
                path.Contains(":") || path.IndexOf('\0') >= 0)
                throw new InvalidDataException("Native package path is not relative.");
            foreach (string part in path.Split('/'))
                if (part.Length == 0 || part == "." || part == "..")
                    throw new InvalidDataException("Native package path escapes its root.");
        }

        private static string Resolve(string root, string path)
        { ValidateRelativePath(path); return Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)); }
        private static void RequireDocumentBudget(string path)
        {
            long bytes = new FileInfo(path).Length;
            if (bytes <= 0 || bytes > MaximumManifestBytes)
                throw new InvalidDataException("Native design/annotations/index document exceeds its byte budget.");
        }
        private static void ReadExact(Stream input, byte[] bytes, int offset, int count)
        {
            while (count != 0)
            { int read = input.Read(bytes, offset, count); if (read == 0) throw new EndOfStreamException(); offset += read; count -= read; }
        }
        private static byte[] EncodeManifest(MerkabaSessionManifest manifest)
        { using var bytes = new MemoryStream(MerkabaSessionManifest.ByteSize); MerkabaSessionManifest.Write(bytes, manifest); return bytes.ToArray(); }
        private static byte[] ReadManifestBytes(string directory)
        {
            using var input = File.OpenRead(Path.Combine(directory, MerkabaSsdStore.ManifestFileName));
            if (input.Length != MerkabaSessionManifest.ByteSize)
                throw new InvalidDataException("Native source manifest has an invalid fixed length.");
            var bytes = new byte[MerkabaSessionManifest.ByteSize];
            ReadExact(input, bytes, 0, bytes.Length); return bytes;
        }
        private static bool SameBytes(byte[] a, byte[] b) => a.AsSpan().SequenceEqual(b);
        private static float[] MatrixValues(Matrix4x4 matrix)
        { var result = new float[16]; for (int index = 0; index < 16; index++) result[index] = matrix[index]; return result; }
        private static long Align4(long value) => checked((value + 3L) & ~3L);
        private static string Hex(byte[] bytes)
        { var result = new StringBuilder(2 * bytes.Length); foreach (byte value in bytes) result.Append(value.ToString("x2", CultureInfo.InvariantCulture)); return result.ToString(); }
        private static string NewDirectory()
        {
            string path = Path.Combine(Path.GetTempPath(), "m8-native-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(path); return path;
        }
        private static void DeleteOwnedDirectory(string path)
        { if (System.IO.Directory.Exists(path)) System.IO.Directory.Delete(path, true); }
    }
}
