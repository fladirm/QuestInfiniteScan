using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Unity.Mathematics;
using UnityEngine;

namespace Genesis.RoomScan
{
    // Export-owned progress, not scan state/MVCC. A source is revalidated only
    // after the ordinary scanner mutation lease and its pending work retire.
    internal sealed class MerkabaExportJournal : IDisposable
    {
        internal const string FileName = "export.journal";
        private const int MaximumRecordBytes = 64 * 1024;
        private const int PacketBytes = 64 * 1024;
        private const string Policy = "M8-realtime-RT3/export-resume-v2/atlas2048-16-32-64-g2/rgb-v-pair/flat-L2-UV/unlit-capture";
        private readonly string _directory;
        private readonly FileStream _log;
        private readonly Dictionary<string, long> _appendEnds = new(StringComparer.Ordinal);
        private bool _writeFailed;
        internal State Current { get; private set; }
        internal bool Resumed { get; }

        [Serializable]
        private sealed class SourceReceipt
        {
            public int version;
            public string sourceGeneration, session, sourceHash, optionsHash, implementation;
            public uint codegenHash;
            public bool tiles;
        }

        [Serializable]
        internal sealed class State
        {
            // 0=original tiles, 1=DIRT packets, 2=final container assembly.
            public int stage, nextTile, nextLeaf;
            public long nextDirtPacket, dirtTriangles;
            public long occupied, measured, completed, unresolved;
            public string status = "pending", reason = "";
            public MerkabaGlbWriter.ResumeState glb;
            public Leaf leaf;
        }

        [Serializable]
        internal sealed class Leaf
        {
            public int index, vertices, triangles;
            public int3 minimum, maximum;
            public float3 origin;
            public Vector3 contentMinimum, contentMaximum;
            public long bytes;
            internal Leaf(MerkabaTilesetLeaf value)
            {
                index = value.Index; vertices = value.VertexCount; triangles = value.TriangleCount;
                minimum = value.MinimumCoord; maximum = value.MaximumCoord; origin = value.LocalOrigin;
                contentMinimum = value.ContentMinimum; contentMaximum = value.ContentMaximum;
                bytes = value.ByteLength;
            }
            internal MerkabaTilesetLeaf Value => new(index, minimum, maximum, origin,
                contentMinimum, contentMaximum, bytes, vertices, triangles);
        }

        [Serializable]
        private sealed class Segment
        {
            public string path, sha256;
            public long offset, length;
            public bool append;
        }

        [Serializable]
        private sealed class Record
        {
            public State state;
            public Segment[] segments;
        }

        internal static bool Exists(string directory) =>
            File.Exists(Path.Combine(directory, "source.json"));

        internal MerkabaExportJournal(string directory, MerkabaNativePackage.Snapshot source,
            bool tiles, float2 planeBounds, MerkabaSpatialBinding? spatialBinding,
            Action<MerkabaTilesetLeaf> restoredLeaf, CancellationToken token)
        {
            _directory = Path.GetFullPath(directory);
            if (Directory.Exists(_directory) &&
                (File.GetAttributes(_directory) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Export staging cannot be a symbolic link.");
            var optionBytes = new byte[8 + 16 * 4];
            Write32(optionBytes, 0, math.asuint(planeBounds.x));
            Write32(optionBytes, 4, math.asuint(planeBounds.y));
            Matrix4x4 transform = spatialBinding?.AnchorFromPackage ?? Matrix4x4.identity;
            for (int i = 0; i < 16; i++) Write32(optionBytes, 8 + 4 * i,
                math.asuint(transform[i % 4, i / 4]));
            var expected = new SourceReceipt
            {
                version = 1, tiles = tiles, sourceGeneration = source.Manifest.commitGeneration,
                session = source.Manifest.sessionUuid, sourceHash = Hash(source.ManifestBytes),
                optionsHash = Hash(Combine(Encoding.UTF8.GetBytes(Policy), optionBytes)),
                codegenHash = MerkabaSphereFlowerAuthority.FrozenTableHash,
                implementation = typeof(MerkabaExporter).Assembly.ManifestModule.ModuleVersionId.ToString("D")
            };
            string sourcePath = Path.Combine(_directory, "source.json");
            Resumed = Directory.Exists(_directory);
            if (Resumed)
            {
                if (!File.Exists(sourcePath) || new FileInfo(sourcePath).Length > MaximumRecordBytes ||
                    (File.GetAttributes(sourcePath) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Unrecognized export staging; refusing to overwrite it.");
                SourceReceipt actual = JsonUtility.FromJson<SourceReceipt>(File.ReadAllText(sourcePath));
                if (actual == null || JsonUtility.ToJson(actual) != JsonUtility.ToJson(expected))
                    throw new InvalidDataException("RESUME_SOURCE_UNAVAILABLE: committed records, cut, documents/assets, codegen or presentation options changed.");
            }
            else
            {
                Directory.CreateDirectory(_directory);
                byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(expected));
                using var file = new FileStream(sourcePath, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None, PacketBytes);
                file.Write(bytes, 0, bytes.Length); file.Flush(true);
            }
            _log = new FileStream(Resolve(FileName), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None, PacketBytes);
            try
            {
                Recover(restoredLeaf, token);
                Current ??= new State();
            }
            catch { _log.Dispose(); throw; }
        }

        internal void Commit(State state, IReadOnlyList<string> appendFiles,
            string completedFile, CancellationToken token)
        {
            if (_writeFailed) throw new IOException("Export journal must recover its failed write before reuse.");
            token.ThrowIfCancellationRequested();
            var segments = new List<Segment>((appendFiles?.Count ?? 0) + (completedFile == null ? 0 : 1));
            if (appendFiles != null)
                foreach (string relative in appendFiles)
                {
                    _appendEnds.TryGetValue(relative, out long begin);
                    segments.Add(Capture(relative, begin, true, token));
                }
            if (completedFile != null) segments.Add(Capture(completedFile, 0L, false, token));
            byte[] payload = Encoding.UTF8.GetBytes(JsonUtility.ToJson(new Record
                { state = state, segments = segments.ToArray() }));
            if (payload.Length > MaximumRecordBytes)
                throw new InvalidDataException("Export journal checkpoint exceeds its bounded record.");
            State detached = JsonUtility.FromJson<Record>(Encoding.UTF8.GetString(payload)).state;
            long recordStart = _log.Position;
            try
            {
                using var writer = new BinaryWriter(_log, Encoding.UTF8, true);
                writer.Write(payload.Length); writer.Write(payload);
                using (SHA256 sha = SHA256.Create()) writer.Write(sha.ComputeHash(payload));
                writer.Flush(); _log.Flush(true);
            }
            catch
            {
                _writeFailed = true;
                // A following failure receipt must never be appended behind a
                // torn record. If truncation itself fails, recovery still sees
                // only the preceding fully checked record after restart.
                _log.SetLength(recordStart); _log.Position = recordStart;
                throw;
            }
            foreach (Segment segment in segments)
                if (segment.append) _appendEnds[segment.path] = checked(segment.offset + segment.length);
            // Detach state so a caller's next cursor cannot mutate this receipt.
            Current = detached;
        }

        internal void NoteFailure(Exception exception)
        {
            if (Current == null) return;
            State state = JsonUtility.FromJson<State>(JsonUtility.ToJson(Current));
            state.leaf = null;
            state.status = exception is OperationCanceledException ? "pending" : "unresolved";
            state.reason = exception.Message ?? exception.GetType().Name;
            if (state.reason.Length > 4096) state.reason = state.reason.Substring(0, 4096);
            Commit(state, null, null, CancellationToken.None);
        }

        private void Recover(Action<MerkabaTilesetLeaf> restoredLeaf, CancellationToken token)
        {
            long verified = 0;
            using var reader = new BinaryReader(_log, Encoding.UTF8, true);
            while (_log.Position < _log.Length)
            {
                token.ThrowIfCancellationRequested();
                if (_log.Length - _log.Position < 4) break;
                int length = reader.ReadInt32();
                if (length <= 0 || length > MaximumRecordBytes)
                    throw new InvalidDataException("Invalid export journal record length.");
                if (_log.Length - _log.Position < length + 32L) break;
                byte[] payload = reader.ReadBytes(length), checksum = reader.ReadBytes(32);
                using (SHA256 sha = SHA256.Create())
                    if (!Same(sha.ComputeHash(payload), checksum))
                        throw new InvalidDataException("Export journal checksum mismatch.");
                Record record = JsonUtility.FromJson<Record>(Encoding.UTF8.GetString(payload));
                if (record?.state == null || record.segments == null || record.segments.Length > 16 ||
                    record.state.stage < 0 || record.state.stage > 2 || record.state.nextTile < 0 ||
                    record.state.nextLeaf < 0 || record.state.nextDirtPacket < 0)
                    throw new InvalidDataException("Invalid export cursor receipt.");
                foreach (Segment segment in record.segments)
                {
                    if (segment == null || segment.offset < 0 || segment.length < 0)
                        throw new InvalidDataException("Invalid export spool range.");
                    if (segment.append)
                    {
                        _appendEnds.TryGetValue(segment.path, out long previous);
                        if (segment.offset != previous) throw new InvalidDataException("Noncontiguous export spool receipt.");
                    }
                    using var file = File.OpenRead(Resolve(segment.path));
                    if (segment.offset > file.Length || segment.length > file.Length - segment.offset ||
                        (!segment.append && file.Length != segment.length))
                        throw new InvalidDataException("Completed export entry is truncated or changed.");
                    file.Position = segment.offset;
                    if (Hash(file, segment.length, token) != segment.sha256)
                        throw new InvalidDataException("Completed export entry checksum mismatch: " + segment.path);
                    if (segment.append) _appendEnds[segment.path] = checked(segment.offset + segment.length);
                }
                if (record.state.leaf != null) restoredLeaf?.Invoke(record.state.leaf.Value);
                Current = record.state;
                verified = _log.Position;
            }
            // A torn trailing record is never a completed cursor. Remove only
            // append tails belonging to this validated export-owned directory.
            foreach (var pair in _appendEnds)
            {
                using var file = new FileStream(Resolve(pair.Key), FileMode.Open,
                    FileAccess.Write, FileShare.None, PacketBytes);
                file.SetLength(pair.Value); file.Flush(true);
            }
            _log.SetLength(verified); _log.Position = verified; _log.Flush(true);
        }

        private Segment Capture(string relative, long offset, bool append, CancellationToken token)
        {
            // The sole writer has already flushed under the export lease; a
            // read handle must nevertheless share its existing write access.
            using var file = new FileStream(Resolve(relative), FileMode.Open,
                FileAccess.Read, FileShare.ReadWrite, PacketBytes);
            if (offset > file.Length) throw new InvalidDataException("Export spool shrank after its checkpoint.");
            file.Position = offset;
            long count = file.Length - offset;
            return new Segment { path = relative, offset = offset, length = count,
                append = append, sha256 = Hash(file, count, token) };
        }

        private string Resolve(string relative)
        {
            if (string.IsNullOrWhiteSpace(relative) || relative.Contains("\\", StringComparison.Ordinal) ||
                Path.IsPathRooted(relative) || relative.Contains(":", StringComparison.Ordinal))
                throw new InvalidDataException("Invalid export journal path.");
            foreach (string part in relative.Split('/'))
                if (part.Length == 0 || part == "." || part == "..")
                    throw new InvalidDataException("Export journal path escapes staging.");
            string path = _directory;
            foreach (string part in relative.Split('/'))
            {
                path = Path.Combine(path, part);
                if ((File.Exists(path) || Directory.Exists(path)) &&
                    (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Export journal cannot follow a symbolic link.");
            }
            return path;
        }

        private static string Hash(Stream input, long bytes, CancellationToken token)
        {
            using SHA256 sha = SHA256.Create();
            var packet = new byte[PacketBytes];
            while (bytes != 0)
            {
                token.ThrowIfCancellationRequested();
                int read = input.Read(packet, 0, (int)Math.Min(packet.Length, bytes));
                if (read <= 0) throw new EndOfStreamException();
                sha.TransformBlock(packet, 0, read, null, 0); bytes -= read;
            }
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return Hex(sha.Hash);
        }

        private static string Hash(byte[] bytes)
        { using SHA256 sha = SHA256.Create(); return Hex(sha.ComputeHash(bytes)); }
        private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        private static bool Same(byte[] left, byte[] right)
        { if (left.Length != right.Length) return false; for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false; return true; }
        private static byte[] Combine(byte[] first, byte[] second)
        { var value = new byte[first.Length + second.Length]; Buffer.BlockCopy(first, 0, value, 0, first.Length); Buffer.BlockCopy(second, 0, value, first.Length, second.Length); return value; }
        private static void Write32(byte[] value, int offset, uint word)
        { for (int i = 0; i < 4; i++) value[offset + i] = (byte)(word >> (8 * i)); }
        public void Dispose() => _log?.Dispose();
    }
}
