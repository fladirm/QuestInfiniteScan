using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.SigmaPrism
{
    internal enum SigmaConstraintAdmission
    {
        Added,
        ReplacedWeaker,
        DuplicateOrWeaker,
        IncompatibleRetained,
    }

    internal enum SigmaConstraintEntryKind : uint
    {
        Raw = 0u,
        Certificate = 1u,
    }

    /// <summary>
    /// Durable owner for minimized unresolved factors. The frame thread hands
    /// off bounded immutable deltas; a worker atomically replaces only affected
    /// exact-key shards. It owns no physical field state.
    /// </summary>
    internal sealed class SigmaExactConstraintStore : IDisposable
    {
        private const uint MarkerMagic = 0x34534453u; // SDS4
        private const uint MarkerVersion = 1u;
        private const uint BucketMagic = 0x34424353u; // SCB4
        private const uint BucketVersion = 1u;
        private readonly object _gate = new();
        private readonly string _path;
        private readonly string _shardDirectory;
        private readonly List<SigmaConstraintJournalDelta> _pending = new();
        private SigmaExactConstraintJournal.Entry[] _migrationEntries;
        private Task _writer;
        private string _fault;
        private bool _accepting = true;
        private bool _sharded;

        internal SigmaExactConstraintStore(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A durable journal path is required.",
                    nameof(path));
            _path = path;
            _shardDirectory = path + ".entries";
        }

        internal SigmaExactConstraintJournal Load()
        {
            lock (_gate)
            {
                if (!_accepting || _writer != null)
                    throw new InvalidOperationException(
                        "Journal load is legal only before persistence begins.");
            }
            if (File.Exists(_path) && IsShardMarker(File.ReadAllBytes(_path)))
            {
                if (!Directory.Exists(_shardDirectory))
                    throw new InvalidDataException(
                        "Constraint shard directory is missing.");
                _sharded = true;
                return LoadShards();
            }
            if (!File.Exists(_path))
                return new SigmaExactConstraintJournal();
            SigmaExactConstraintJournal legacy =
                SigmaExactConstraintJournal.DecodeCanonical(
                    File.ReadAllBytes(_path));
            _migrationEntries = legacy.SnapshotEntries();
            return legacy;
        }

        internal void Stage(SigmaExactConstraintJournal journal)
        {
            if (journal == null) throw new ArgumentNullException(nameof(journal));
            SigmaConstraintJournalDelta[] deltas = journal.TakePendingDeltas();
            if (deltas.Length == 0)
                return;
            lock (_gate)
            {
                if (!_accepting)
                    throw new ObjectDisposedException(
                        nameof(SigmaExactConstraintStore));
                if (_fault != null)
                    throw new IOException(_fault);
                _pending.AddRange(deltas);
                if (_writer == null)
                    _writer = Task.Run(WritePending);
            }
        }

        internal bool TryGetFault(out string error)
        {
            lock (_gate)
            {
                error = _fault;
                return error != null;
            }
        }

        public void Dispose()
        {
            Task writer;
            lock (_gate)
            {
                if (!_accepting) return;
                _accepting = false;
                writer = _writer;
            }
            writer?.GetAwaiter().GetResult();
        }

        private void WritePending()
        {
            while (true)
            {
                SigmaConstraintJournalDelta[] deltas;
                lock (_gate)
                {
                    if (_pending.Count == 0)
                    {
                        _writer = null;
                        return;
                    }
                    deltas = _pending.ToArray();
                    _pending.Clear();
                }
                try
                {
                    EnsureShardStore();
                    deltas = Coalesce(deltas);
                    // Phase one persists each exact certificate together with
                    // every source it still owns. Only a completed atomic shard
                    // replacement authorizes the certificate-only phase.
                    for (int index = 0; index < deltas.Length; ++index)
                        ApplyShardDelta(deltas[index].Key,
                            deltas[index].Entry);
                    for (int index = 0; index < deltas.Length; ++index)
                    {
                        SigmaConstraintJournalDelta delta = deltas[index];
                        if (delta.Entry != null &&
                            delta.Entry.Kind == SigmaConstraintEntryKind.Certificate &&
                            delta.Entry.CanReleaseRaw)
                        {
                            ApplyShardDelta(delta.Key,
                                delta.Entry.WithoutRaw());
                            delta.Owner.AcknowledgeDurable(delta.Entry.Key,
                                delta.Entry.Version);
                        }
                    }
                }
                catch (Exception exception)
                {
                    lock (_gate)
                    {
                        _fault = "Exact constraint persistence failed: " +
                            exception.Message;
                        _pending.Clear();
                        _writer = null;
                    }
                    return;
                }
            }
        }

        private SigmaExactConstraintJournal LoadShards()
        {
            var result = new SigmaExactConstraintJournal();
            string[] files = Directory.GetFiles(_shardDirectory, "*.scb",
                SearchOption.TopDirectoryOnly);
            for (int file = 0; file < files.Length; ++file)
            {
                List<SigmaExactConstraintJournal.Entry> entries =
                    ReadBucket(files[file]);
                for (int index = 0; index < entries.Count; ++index)
                    result.AddPersistedEntry(entries[index]);
            }
            return result;
        }

        private void EnsureShardStore()
        {
            if (_sharded) return;
            if (_migrationEntries == null)
            {
                if (Directory.Exists(_shardDirectory))
                    Directory.Delete(_shardDirectory, true);
                Directory.CreateDirectory(_shardDirectory);
            }
            else
            {
                string staging = _shardDirectory + ".next";
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
                Directory.CreateDirectory(staging);
                for (int index = 0; index < _migrationEntries.Length; ++index)
                    ApplyShardDelta(_migrationEntries[index].Key,
                        _migrationEntries[index], staging);
                if (Directory.Exists(_shardDirectory))
                    Directory.Delete(_shardDirectory, true);
                Directory.Move(staging, _shardDirectory);
                _migrationEntries = null;
            }
            WriteAtomic(_path, MarkerBytes());
            _sharded = true;
        }

        private void ApplyShardDelta(SigmaConstraintByteKey key,
            SigmaExactConstraintJournal.Entry replacement,
            string directory = null)
        {
            string path = Path.Combine(directory ?? _shardDirectory,
                BucketName(key));
            List<SigmaExactConstraintJournal.Entry> entries = File.Exists(path)
                ? ReadBucket(path)
                : new List<SigmaExactConstraintJournal.Entry>();
            int found = entries.FindIndex(entry => entry.Key.Equals(key));
            if (replacement == null)
            {
                if (found >= 0) entries.RemoveAt(found);
            }
            else if (found >= 0)
                entries[found] = replacement;
            else
                entries.Add(replacement);
            if (entries.Count == 0)
            {
                if (File.Exists(path)) File.Delete(path);
                return;
            }
            entries.Sort((left, right) => left.Key.CompareTo(right.Key));
            WriteAtomic(path, EncodeBucket(entries));
        }

        private static SigmaConstraintJournalDelta[] Coalesce(
            SigmaConstraintJournalDelta[] deltas)
        {
            var latest = new Dictionary<SigmaConstraintByteKey,
                SigmaConstraintJournalDelta>();
            for (int index = 0; index < deltas.Length; ++index)
                if (!latest.TryGetValue(deltas[index].Key, out var prior) ||
                    deltas[index].Version > prior.Version)
                    latest[deltas[index].Key] = deltas[index];
            var result = new List<SigmaConstraintJournalDelta>(latest.Values);
            result.Sort((left, right) => left.Key.CompareTo(right.Key));
            return result.ToArray();
        }

        private static byte[] EncodeBucket(
            List<SigmaExactConstraintJournal.Entry> entries)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(BucketMagic); writer.Write(BucketVersion);
            writer.Write((uint)entries.Count);
            for (int index = 0; index < entries.Count; ++index)
                entries[index].Write(writer);
            writer.Flush();
            return stream.ToArray();
        }

        private static List<SigmaExactConstraintJournal.Entry> ReadBucket(
            string path)
        {
            using var stream = new MemoryStream(File.ReadAllBytes(path), false);
            using var reader = new BinaryReader(stream);
            Require(reader.ReadUInt32() == BucketMagic &&
                reader.ReadUInt32() == BucketVersion,
                "Invalid constraint shard header.");
            uint count = reader.ReadUInt32();
            Require(count <= 4096u, "Invalid constraint shard count.");
            var result = new List<SigmaExactConstraintJournal.Entry>((int)count);
            for (uint index = 0; index < count; ++index)
                result.Add(SigmaExactConstraintJournal.Entry.Read(reader));
            Require(stream.Position == stream.Length,
                "Trailing constraint shard bytes.");
            return result;
        }

        private static string BucketName(SigmaConstraintByteKey key)
        {
            byte[] digest;
            using (SHA256 sha = SHA256.Create())
                digest = sha.ComputeHash(key.Bytes);
            const string hex = "0123456789abcdef";
            var name = new char[digest.Length * 2 + 4];
            for (int index = 0; index < digest.Length; ++index)
            {
                name[index * 2] = hex[digest[index] >> 4];
                name[index * 2 + 1] = hex[digest[index] & 15];
            }
            name[name.Length - 4] = '.'; name[name.Length - 3] = 's';
            name[name.Length - 2] = 'c'; name[name.Length - 1] = 'b';
            return new string(name);
        }

        private static byte[] MarkerBytes()
        {
            using var stream = new MemoryStream(8);
            using var writer = new BinaryWriter(stream);
            writer.Write(MarkerMagic); writer.Write(MarkerVersion);
            return stream.ToArray();
        }

        private static bool IsShardMarker(byte[] bytes)
        {
            if (bytes == null || bytes.Length != 8) return false;
            using var reader = new BinaryReader(new MemoryStream(bytes, false));
            return reader.ReadUInt32() == MarkerMagic &&
                reader.ReadUInt32() == MarkerVersion;
        }

        private static void WriteAtomic(string path, byte[] snapshot)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            string temporary = path + ".next";
            using (var stream = new FileStream(temporary, FileMode.Create,
                       FileAccess.Write, FileShare.None, 4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(snapshot, 0, snapshot.Length);
                stream.Flush(true);
            }
            if (File.Exists(path))
                File.Replace(temporary, path, null);
            else
                File.Move(temporary, path);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidDataException(message);
        }
    }

    /// <summary>
    /// Exact observation-side factor retained only while its full-field support
    /// remains unresolved.  It has no carrier address or branch/object identity.
    /// </summary>
    internal sealed class SigmaExactConstraintRecord
    {
        private readonly byte[] _canonicalBytes;
        private readonly SigmaConstraintByteKey _rawContextKey;

        internal SigmaExactConstraintRecord(
            SigmaUnresolvedConstraintGpu constraint,
            SigmaFrameUInt4Gpu[] observationHeaders,
            SigmaFrameUInt2Gpu[] roomRays,
            SigmaFrameUInt2Gpu[] codeLeaves,
            SigmaFrameUInt4Gpu[] certificateWords = null)
        {
            if (observationHeaders == null || observationHeaders.Length != 2)
                throw new ArgumentException("Two coherent-eye headers are required.",
                    nameof(observationHeaders));
            if (roomRays == null || roomRays.Length != 6)
                throw new ArgumentException("Six calibrated ray rows are required.",
                    nameof(roomRays));
            if (codeLeaves == null || codeLeaves.Length != 16)
                throw new ArgumentException("Eight interval leaves are required.",
                    nameof(codeLeaves));
            Constraint = constraint;
            ObservationHeaders = (SigmaFrameUInt4Gpu[])observationHeaders.Clone();
            RoomRays = (SigmaFrameUInt2Gpu[])roomRays.Clone();
            CodeLeaves = (SigmaFrameUInt2Gpu[])codeLeaves.Clone();
            if (certificateWords != null && certificateWords.Length != 16)
                throw new ArgumentException(
                    "Sixteen locality-certificate words are required.",
                    nameof(certificateWords));
            CertificateWords = certificateWords == null
                ? new SigmaFrameUInt4Gpu[16]
                : (SigmaFrameUInt4Gpu[])certificateWords.Clone();
            _canonicalBytes = BuildCanonicalBytes();
            _rawContextKey = new SigmaConstraintByteKey(BuildRawContextBytes());
        }

        internal SigmaUnresolvedConstraintGpu Constraint { get; }
        internal SigmaFrameUInt4Gpu[] ObservationHeaders { get; }
        internal SigmaFrameUInt2Gpu[] RoomRays { get; }
        internal SigmaFrameUInt2Gpu[] CodeLeaves { get; }
        internal SigmaFrameUInt4Gpu[] CertificateWords { get; }
        internal SigmaConstraintByteKey RawContextKey => _rawContextKey;

        internal bool SameContextAndIndependence(SigmaExactConstraintRecord other)
        {
            return other != null && _rawContextKey.Equals(other._rawContextKey);
        }

        internal bool NoBroaderThan(SigmaExactConstraintRecord other)
        {
            if (!SameContextAndIndependence(other))
                return false;
            for (int leaf = 0; leaf < 8; ++leaf)
            {
                long lower = Raw(CodeLeaves[leaf * 2]);
                long upper = Raw(CodeLeaves[leaf * 2 + 1]);
                long otherLower = Raw(other.CodeLeaves[leaf * 2]);
                long otherUpper = Raw(other.CodeLeaves[leaf * 2 + 1]);
                if (lower > upper || otherLower > otherUpper ||
                    lower < otherLower || upper > otherUpper)
                    return false;
            }
            return true;
        }

        internal byte[] CanonicalBytes() => (byte[])_canonicalBytes.Clone();

        internal bool TryCreateCertificate(
            out SigmaExactConstraintCertificate certificate)
        {
            uint required = (uint)(
                SigmaNativeConstraintProofFlags.BoundLocality |
                SigmaNativeConstraintProofFlags.LosslessPullback);
            uint forbidden = (uint)(SigmaNativeConstraintProofFlags.Coupled |
                SigmaNativeConstraintProofFlags.Disjunctive |
                SigmaNativeConstraintProofFlags.RawRequired);
            uint proof = Constraint.Program.W;
            uint certificateFlags = CertificateWords[0].X;
            uint requiredCertificate = (uint)(SigmaNativeCertificateFlags.Valid |
                SigmaNativeCertificateFlags.Directional |
                SigmaNativeCertificateFlags.Minimized);
            if ((proof & required) != required || (proof & forbidden) != 0u ||
                (certificateFlags & requiredCertificate) != requiredCertificate ||
                Constraint.Program.X == 0u || Constraint.Program.Y == 0u)
            {
                certificate = null;
                return false;
            }
            certificate = SigmaExactConstraintCertificate.From(this);
            return true;
        }

        internal static SigmaExactConstraintRecord FromCanonicalBytes(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (bytes.Length != 272 && bytes.Length != 304)
                throw new InvalidDataException("Invalid exact raw factor size.");
            using var stream = new MemoryStream(bytes, false);
            using var reader = new BinaryReader(stream);
            return Read(reader, bytes.Length == 304);
        }

        private byte[] BuildCanonicalBytes()
        {
            using var stream = new MemoryStream(304);
            using var writer = new BinaryWriter(stream);
            Write(writer, Constraint.Observation);
            Write(writer, Constraint.Relation);
            Write(writer, Constraint.Evidence);
            Write(writer, Constraint.Provenance);
            Write(writer, Constraint.Frontier);
            Write(writer, Constraint.Program);
            for (int index = 0; index < ObservationHeaders.Length; ++index)
                Write(writer, ObservationHeaders[index]);
            for (int index = 0; index < RoomRays.Length; ++index)
                Write(writer, RoomRays[index]);
            for (int index = 0; index < CodeLeaves.Length; ++index)
                Write(writer, CodeLeaves[index]);
            writer.Flush();
            return stream.ToArray();
        }

        private byte[] BuildRawContextBytes()
        {
            using var stream = new MemoryStream(240);
            using var writer = new BinaryWriter(stream);
            // Revision/order are excluded.  Every input still read by the exact
            // reverse program—including concrete raw-ray context and source
            // independence—is retained for raw-only factors.
            writer.Write(Constraint.Observation.Y);
            writer.Write(Constraint.Observation.W);
            Write(writer, Constraint.Relation);
            Write(writer, Constraint.Evidence);
            writer.Write(Constraint.Provenance.X);
            writer.Write(Constraint.Provenance.Y);
            writer.Write(Constraint.Provenance.W);
            Write(writer, Constraint.Frontier);
            Write(writer, Constraint.Program);
            writer.Write(ObservationHeaders[0].X);
            writer.Write(ObservationHeaders[0].Z);
            writer.Write(ObservationHeaders[0].W);
            Write(writer, ObservationHeaders[1]);
            for (int index = 0; index < RoomRays.Length; ++index)
                Write(writer, RoomRays[index]);
            writer.Flush();
            return stream.ToArray();
        }

        internal string FormatLogLine(uint revision) =>
            $"Sigma unresolved exact-factor revision={revision} " +
            $"relation={Constraint.Relation.X}/{Constraint.Relation.Y}/" +
            $"{Constraint.Relation.Z}/{Constraint.Relation.W} " +
            $"epoch={Constraint.Provenance.W}";

        internal static SigmaExactConstraintRecord Read(BinaryReader reader,
            bool extended = true)
        {
            var constraint = new SigmaUnresolvedConstraintGpu
            {
                Observation = Read4(reader),
                Relation = Read4(reader),
                Evidence = Read4(reader),
                Provenance = Read4(reader),
            };
            if (extended)
            {
                constraint.Frontier = Read4(reader);
                constraint.Program = Read4(reader);
            }
            var headers = new SigmaFrameUInt4Gpu[2];
            var rays = new SigmaFrameUInt2Gpu[6];
            var leaves = new SigmaFrameUInt2Gpu[16];
            for (int index = 0; index < headers.Length; ++index)
                headers[index] = Read4(reader);
            for (int index = 0; index < rays.Length; ++index)
                rays[index] = Read2(reader);
            for (int index = 0; index < leaves.Length; ++index)
                leaves[index] = Read2(reader);
            return new SigmaExactConstraintRecord(constraint, headers, rays,
                leaves);
        }

        private static long Raw(SigmaFrameUInt2Gpu value) => unchecked(
            (long)((ulong)value.Y << 32 | value.X));
        private static void Write(BinaryWriter writer, SigmaFrameUInt2Gpu value)
        {
            writer.Write(value.X);
            writer.Write(value.Y);
        }
        private static void Write(BinaryWriter writer, SigmaFrameUInt4Gpu value)
        {
            writer.Write(value.X);
            writer.Write(value.Y);
            writer.Write(value.Z);
            writer.Write(value.W);
        }
        private static SigmaFrameUInt2Gpu Read2(BinaryReader reader) => new()
        {
            X = reader.ReadUInt32(), Y = reader.ReadUInt32()
        };
        private static SigmaFrameUInt4Gpu Read4(BinaryReader reader) => new()
        {
            X = reader.ReadUInt32(), Y = reader.ReadUInt32(),
            Z = reader.ReadUInt32(), W = reader.ReadUInt32()
        };
    }

    internal sealed class SigmaExactConstraintCertificate
    {
        private SigmaExactConstraintCertificate(SigmaConstraintByteKey key,
            SigmaFrameUInt4Gpu[] words)
        {
            Key = key;
            Words = words;
        }

        internal SigmaConstraintByteKey Key { get; }
        internal SigmaFrameUInt4Gpu[] Words { get; }

        internal static SigmaExactConstraintCertificate From(
            SigmaExactConstraintRecord source)
        {
            var words = (SigmaFrameUInt4Gpu[])source.CertificateWords.Clone();
            // Generation/multiplicity is a receipt, not feasible-set identity.
            words[0].Z = 1u;
            words[0].W = 0u;
            // Concrete per-frame independence ids are reconstructed by the
            // finite directional-mode receipt (word 14); retaining them here
            // would make a duplicate observation mutate the certificate.
            words[2] = default;
            return new SigmaExactConstraintCertificate(
                new SigmaConstraintByteKey(BuildKey(source, words)), words);
        }

        internal static SigmaExactConstraintCertificate FromPersisted(
            SigmaConstraintByteKey key, SigmaFrameUInt4Gpu[] words)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (words == null || words.Length != 16)
                throw new ArgumentException(
                    "Sixteen locality-certificate words are required.",
                    nameof(words));
            return new SigmaExactConstraintCertificate(key,
                (SigmaFrameUInt4Gpu[])words.Clone());
        }

        internal SigmaExactConstraintCertificate Snapshot() =>
            FromPersisted(Key, Words);

        internal bool TryMeet(SigmaExactConstraintCertificate other,
            out SigmaExactConstraintCertificate result)
        {
            result = null;
            if (other == null || !Key.Equals(other.Key) ||
                !SigmaGeneratedFrame.TryMeetLocalityCertificates(
                    Words, other.Words, out SigmaFrameUInt4Gpu[] words))
                return false;
            result = new SigmaExactConstraintCertificate(Key, words);
            return true;
        }

        internal bool SameWords(SigmaExactConstraintCertificate other)
        {
            if (other == null) return false;
            for (int index = 0; index < Words.Length; ++index)
                if (!Equal(Words[index], other.Words[index]))
                    return false;
            return true;
        }

        private static byte[] BuildKey(SigmaExactConstraintRecord source,
            SigmaFrameUInt4Gpu[] words)
        {
            using var stream = new MemoryStream(128);
            using var writer = new BinaryWriter(stream);
            Write(writer, source.Constraint.Frontier);
            Write(writer, source.Constraint.Program);
            Write(writer, source.Constraint.Relation);
            writer.Write(source.Constraint.Provenance.X);
            writer.Write(source.Constraint.Provenance.Y);
            writer.Write(source.Constraint.Provenance.W);
            Write(writer, words[0]);
            Write(writer, words[1]);
            Write(writer, words[3]);
            Write(writer, words[12]);
            Write(writer, words[13]);
            Write(writer, words[15]);
            writer.Flush();
            return stream.ToArray();
        }

        private static bool Equal(SigmaFrameUInt4Gpu left,
            SigmaFrameUInt4Gpu right) => left.X == right.X &&
            left.Y == right.Y && left.Z == right.Z && left.W == right.W;
        private static void Write(BinaryWriter writer, SigmaFrameUInt4Gpu value)
        {
            writer.Write(value.X); writer.Write(value.Y);
            writer.Write(value.Z); writer.Write(value.W);
        }
    }

    internal sealed class SigmaConstraintByteKey : IEquatable<SigmaConstraintByteKey>,
        IComparable<SigmaConstraintByteKey>
    {
        private readonly int _hash;

        internal SigmaConstraintByteKey(byte[] bytes)
        {
            Bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
            unchecked
            {
                int hash = (int)2166136261u;
                for (int index = 0; index < bytes.Length; ++index)
                    hash = (hash ^ bytes[index]) * 16777619;
                _hash = hash;
            }
        }

        internal byte[] Bytes { get; }
        public bool Equals(SigmaConstraintByteKey other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other == null || _hash != other._hash ||
                Bytes.Length != other.Bytes.Length) return false;
            for (int index = 0; index < Bytes.Length; ++index)
                if (Bytes[index] != other.Bytes[index]) return false;
            return true;
        }
        public override bool Equals(object obj) =>
            Equals(obj as SigmaConstraintByteKey);
        public override int GetHashCode() => _hash;
        public int CompareTo(SigmaConstraintByteKey other)
        {
            if (other == null) return 1;
            int length = Math.Min(Bytes.Length, other.Bytes.Length);
            for (int index = 0; index < length; ++index)
            {
                int order = Bytes[index].CompareTo(other.Bytes[index]);
                if (order != 0) return order;
            }
            return Bytes.Length.CompareTo(other.Bytes.Length);
        }
    }

    /// <summary>
    /// Canonically ordered, reclaimable exact-factor journal.  Exact duplicates
    /// retain one factor plus bounded multiplicity; a broader factor may be
    /// removed only inside the identical context and independence class.
    /// </summary>
    internal sealed class SigmaExactConstraintJournal
    {
        private const uint Magic = 0x34434a53u; // SJC4
        private const uint Version = 2u;
        private readonly object _gate = new();
        private readonly Dictionary<SigmaConstraintByteKey, Entry> _entries = new();
        private readonly Dictionary<SigmaConstraintByteKey,
            List<SigmaConstraintByteKey>> _rawContexts = new();
        private readonly Dictionary<SigmaConstraintByteKey, long> _dirty = new();
        private long _version;

        internal int Count { get { lock (_gate) return _entries.Count; } }
        internal int CertificateCount { get { lock (_gate) return
            CountKind(SigmaConstraintEntryKind.Certificate); } }
        internal int RawEvidenceCount { get { lock (_gate) return
            CountRawEvidence(); } }

        internal SigmaConstraintAdmission Add(SigmaExactConstraintRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            lock (_gate)
            {
                if (record.TryCreateCertificate(out
                    SigmaExactConstraintCertificate certificate))
                {
                    if (_entries.TryGetValue(certificate.Key, out Entry existing))
                    {
                        if (!existing.Certificate.TryMeet(certificate,
                            out SigmaExactConstraintCertificate met))
                            return AddRaw(record, true);
                        if (existing.Certificate.SameWords(met))
                            return SigmaConstraintAdmission.DuplicateOrWeaker;
                        long version = ++_version;
                        _entries[certificate.Key] = existing.WithCertificate(met,
                            record, version);
                        _dirty[certificate.Key] = version;
                        return SigmaConstraintAdmission.ReplacedWeaker;
                    }
                    long addedVersion = ++_version;
                    _entries.Add(certificate.Key, Entry.CertificateEntry(
                        certificate, record, addedVersion));
                    _dirty[certificate.Key] = addedVersion;
                    return SigmaConstraintAdmission.Added;
                }
                return AddRaw(record, false);
            }
        }

        internal byte[] EncodeCanonical()
        {
            lock (_gate)
            {
                Entry[] entries = new List<Entry>(_entries.Values).ToArray();
                Array.Sort(entries, (left, right) => left.Key.CompareTo(right.Key));
                using var stream = new MemoryStream();
                using var writer = new BinaryWriter(stream);
                writer.Write(Magic);
                writer.Write(Version);
                writer.Write((uint)entries.Length);
                for (int index = 0; index < entries.Length; ++index)
                    entries[index].Write(writer);
                writer.Flush();
                return stream.ToArray();
            }
        }

        internal static SigmaExactConstraintJournal DecodeCanonical(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            using var stream = new MemoryStream(bytes, false);
            using var reader = new BinaryReader(stream);
            Require(reader.ReadUInt32() == Magic, "Invalid constraint journal magic.");
            uint version = reader.ReadUInt32();
            Require(version == 1u || version == Version,
                "Unsupported constraint journal version.");
            uint count = reader.ReadUInt32();
            Require(count <= int.MaxValue, "Constraint count is invalid.");
            var result = new SigmaExactConstraintJournal();
            for (uint index = 0; index < count; ++index)
            {
                if (version == 1u)
                {
                    uint multiplicity = reader.ReadUInt32();
                    uint length = reader.ReadUInt32();
                    Require(multiplicity != 0u && length == 272u,
                        "Invalid legacy exact constraint record header.");
                    long end = checked(stream.Position + length);
                    SigmaExactConstraintRecord record =
                        SigmaExactConstraintRecord.Read(reader, false);
                    Require(stream.Position == end,
                        "Invalid legacy exact constraint record length.");
                    result.AddDecodedRaw(record);
                }
                else
                    result.AddDecodedEntry(Entry.Read(reader));
            }
            Require(stream.Position == stream.Length,
                "Trailing exact constraint journal bytes.");
            return result;
        }

        internal void Clear()
        {
            lock (_gate)
            {
                _entries.Clear(); _rawContexts.Clear(); _dirty.Clear();
            }
        }

        internal SigmaConstraintJournalDelta[] TakePendingDeltas()
        {
            lock (_gate)
            {
                var result = new List<SigmaConstraintJournalDelta>(_dirty.Count);
                foreach (KeyValuePair<SigmaConstraintByteKey, long> dirty in _dirty)
                    result.Add(new SigmaConstraintJournalDelta(this, dirty.Key,
                        _entries.TryGetValue(dirty.Key, out Entry entry)
                            ? entry.Snapshot() : null, dirty.Value));
                _dirty.Clear();
                return result.ToArray();
            }
        }

        internal Entry[] SnapshotEntries()
        {
            lock (_gate)
            {
                var result = new Entry[_entries.Count];
                int index = 0;
                foreach (Entry entry in _entries.Values)
                    result[index++] = entry.Snapshot();
                return result;
            }
        }

        internal void AddPersistedEntry(Entry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            lock (_gate)
            {
                Require(!_entries.ContainsKey(entry.Key),
                    "Duplicate persisted constraint key.");
                AddDecodedEntry(entry);
            }
        }

        internal void AcknowledgeDurable(SigmaConstraintByteKey key, long version)
        {
            lock (_gate)
            {
                if (_entries.TryGetValue(key, out Entry entry) &&
                    entry.Version == version && entry.CanReleaseRaw &&
                    entry.RawRecords.Length != 0)
                    _entries[key] = entry.WithoutRaw();
            }
        }

        private SigmaConstraintAdmission AddRaw(SigmaExactConstraintRecord record,
            bool incompatible)
        {
            SigmaConstraintByteKey context = record.RawContextKey;
            if (!_rawContexts.TryGetValue(context,
                out List<SigmaConstraintByteKey> bucket))
            {
                bucket = new List<SigmaConstraintByteKey>();
                _rawContexts.Add(context, bucket);
            }
            bool removedWeaker = false;
            for (int index = bucket.Count - 1; index >= 0; --index)
            {
                SigmaConstraintByteKey key = bucket[index];
                if (!_entries.TryGetValue(key, out Entry entry))
                    continue;
                SigmaExactConstraintRecord existing = entry.RawRecords[0];
                if (existing.NoBroaderThan(record))
                    return SigmaConstraintAdmission.DuplicateOrWeaker;
                if (record.NoBroaderThan(existing))
                {
                    _entries.Remove(key);
                    bucket.RemoveAt(index);
                    _dirty[key] = ++_version;
                    removedWeaker = true;
                }
            }
            var rawKey = new SigmaConstraintByteKey(record.CanonicalBytes());
            long version = ++_version;
            _entries[rawKey] = Entry.RawEntry(rawKey, record, version);
            bucket.Add(rawKey);
            _dirty[rawKey] = version;
            if (incompatible)
                return SigmaConstraintAdmission.IncompatibleRetained;
            return removedWeaker ? SigmaConstraintAdmission.ReplacedWeaker :
                SigmaConstraintAdmission.Added;
        }

        private void AddDecodedRaw(SigmaExactConstraintRecord record)
        {
            var key = new SigmaConstraintByteKey(record.CanonicalBytes());
            _entries[key] = Entry.RawEntry(key, record, 0L);
            if (!_rawContexts.TryGetValue(record.RawContextKey,
                out List<SigmaConstraintByteKey> bucket))
            {
                bucket = new List<SigmaConstraintByteKey>();
                _rawContexts.Add(record.RawContextKey, bucket);
            }
            bucket.Add(key);
        }

        private void AddDecodedEntry(Entry entry)
        {
            _entries[entry.Key] = entry;
            if (entry.Kind == SigmaConstraintEntryKind.Raw)
            {
                SigmaExactConstraintRecord record = entry.RawRecords[0];
                if (!_rawContexts.TryGetValue(record.RawContextKey,
                    out List<SigmaConstraintByteKey> bucket))
                {
                    bucket = new List<SigmaConstraintByteKey>();
                    _rawContexts.Add(record.RawContextKey, bucket);
                }
                bucket.Add(entry.Key);
            }
        }

        private int CountKind(SigmaConstraintEntryKind kind)
        {
            int count = 0;
            foreach (Entry entry in _entries.Values)
                count += entry.Kind == kind ? 1 : 0;
            return count;
        }

        private int CountRawEvidence()
        {
            int count = 0;
            foreach (Entry entry in _entries.Values)
                count += entry.RawRecords.Length;
            return count;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidDataException(message);
        }

        internal sealed class Entry
        {
            private Entry(SigmaConstraintEntryKind kind,
                SigmaConstraintByteKey key,
                SigmaExactConstraintCertificate certificate,
                SigmaExactConstraintRecord[] rawRecords, long version)
            {
                Kind = kind; Key = key; Certificate = certificate;
                RawRecords = rawRecords; Version = version;
            }
            internal SigmaConstraintEntryKind Kind { get; }
            internal SigmaConstraintByteKey Key { get; }
            internal SigmaExactConstraintCertificate Certificate { get; }
            internal SigmaExactConstraintRecord[] RawRecords { get; }
            internal long Version { get; }
            internal bool CanReleaseRaw => Kind ==
                SigmaConstraintEntryKind.Certificate;

            internal static Entry RawEntry(SigmaConstraintByteKey key,
                SigmaExactConstraintRecord record, long version) => new(
                    SigmaConstraintEntryKind.Raw, key, null,
                    new[] { record }, version);
            internal static Entry CertificateEntry(
                SigmaExactConstraintCertificate certificate,
                SigmaExactConstraintRecord record, long version) => new(
                    SigmaConstraintEntryKind.Certificate, certificate.Key,
                    certificate, new[] { record }, version);
            internal Entry WithCertificate(
                SigmaExactConstraintCertificate certificate,
                SigmaExactConstraintRecord record, long version)
            {
                var raw = new SigmaExactConstraintRecord[RawRecords.Length + 1];
                Array.Copy(RawRecords, raw, RawRecords.Length);
                raw[raw.Length - 1] = record;
                return new Entry(Kind, Key, certificate, raw, version);
            }
            internal Entry WithoutRaw() => new(Kind, Key, Certificate,
                Array.Empty<SigmaExactConstraintRecord>(), Version);
            internal Entry Snapshot() => new(Kind, Key, Certificate == null ? null :
                Certificate.Snapshot(),
                (SigmaExactConstraintRecord[])RawRecords.Clone(), Version);

            internal void Write(BinaryWriter writer)
            {
                writer.Write((uint)Kind);
                writer.Write((uint)Key.Bytes.Length);
                writer.Write((uint)(Certificate?.Words.Length ?? 0));
                writer.Write((uint)RawRecords.Length);
                // Admission order/version is an in-memory durability fence, not
                // canonical evidence. Persisting it would make A/B and B/A
                // byte-distinct despite the same exact feasible set.
                writer.Write(0L);
                writer.Write(Key.Bytes);
                if (Certificate != null)
                    for (int index = 0; index < Certificate.Words.Length; ++index)
                        WriteWord(writer, Certificate.Words[index]);
                for (int index = 0; index < RawRecords.Length; ++index)
                {
                    byte[] raw = RawRecords[index].CanonicalBytes();
                    writer.Write((uint)raw.Length);
                    writer.Write(raw);
                }
            }

            internal static Entry Read(BinaryReader reader)
            {
                var kind = (SigmaConstraintEntryKind)reader.ReadUInt32();
                uint keyLength = reader.ReadUInt32();
                uint wordCount = reader.ReadUInt32();
                uint rawCount = reader.ReadUInt32();
                long version = reader.ReadInt64();
                Require(keyLength != 0u && keyLength <= 4096u &&
                    (wordCount == 0u || wordCount == 16u) && rawCount <= 4096u,
                    "Invalid exact constraint entry header.");
                var key = new SigmaConstraintByteKey(
                    reader.ReadBytes(checked((int)keyLength)));
                SigmaExactConstraintCertificate certificate = null;
                if (wordCount != 0u)
                {
                    var words = new SigmaFrameUInt4Gpu[wordCount];
                    for (int index = 0; index < words.Length; ++index)
                        words[index] = ReadWord(reader);
                    certificate = SigmaExactConstraintCertificate.FromPersisted(
                        key, words);
                }
                var raw = new SigmaExactConstraintRecord[rawCount];
                for (int index = 0; index < raw.Length; ++index)
                {
                    uint length = reader.ReadUInt32();
                    Require(length == 272u || length == 304u,
                        "Invalid persisted raw factor size.");
                    raw[index] = SigmaExactConstraintRecord.FromCanonicalBytes(
                        reader.ReadBytes(checked((int)length)));
                }
                Require((kind == SigmaConstraintEntryKind.Raw &&
                        certificate == null && raw.Length == 1) ||
                    (kind == SigmaConstraintEntryKind.Certificate &&
                        certificate != null),
                    "Invalid exact constraint entry payload.");
                return new Entry(kind, key, certificate, raw, version);
            }

            private static void WriteWord(BinaryWriter writer,
                SigmaFrameUInt4Gpu value)
            {
                writer.Write(value.X); writer.Write(value.Y);
                writer.Write(value.Z); writer.Write(value.W);
            }
            private static SigmaFrameUInt4Gpu ReadWord(BinaryReader reader) =>
                new()
                {
                    X = reader.ReadUInt32(), Y = reader.ReadUInt32(),
                    Z = reader.ReadUInt32(), W = reader.ReadUInt32(),
                };
        }
    }

    internal readonly struct SigmaConstraintJournalDelta
    {
        internal SigmaConstraintJournalDelta(SigmaExactConstraintJournal owner,
            SigmaConstraintByteKey key, SigmaExactConstraintJournal.Entry entry,
            long version)
        { Owner = owner; Key = key; Entry = entry; Version = version; }
        internal SigmaExactConstraintJournal Owner { get; }
        internal SigmaConstraintByteKey Key { get; }
        internal SigmaExactConstraintJournal.Entry Entry { get; }
        internal long Version { get; }
    }

    /// <summary>
    /// Terminal GPU receipt plus the exact observation factor needed by the
    /// persistence journal. It is a host persistence envelope, never mutation
    /// authority and never a carrier/support identity.
    /// </summary>
    internal sealed class SigmaNativeCompletionRecord
    {
        private SigmaNativeCompletionRecord(SigmaNativeFrameGpu frame,
            uint publishedRoot, SigmaExactConstraintRecord evidence)
        {
            Frame = frame;
            PublishedRoot = publishedRoot;
            Evidence = evidence;
        }

        internal SigmaNativeFrameGpu Frame { get; }
        internal uint PublishedRoot { get; }
        internal SigmaExactConstraintRecord Evidence { get; }
        internal uint Revision => Frame.Identity.X;

        internal static SigmaNativeCompletionRecord Decode(
            NativeArray<SigmaFrameUInt2Gpu> batch, int recordIndex)
        {
            int recordBase = checked(recordIndex *
                SigmaGeneratedFrame.CompletionWordCount);
            Require(recordBase >= 0 && recordBase +
                SigmaGeneratedFrame.CompletionWordCount <= batch.Length,
                "Completion record lies outside its transfer batch.");
            SigmaFrameUInt4Gpu Read4(int offset) => new()
            {
                X = batch[recordBase + offset].X,
                Y = batch[recordBase + offset].Y,
                Z = batch[recordBase + offset + 1].X,
                W = batch[recordBase + offset + 1].Y,
            };
            var frame = new SigmaNativeFrameGpu
            {
                Identity = Read4(SigmaGeneratedFrame.CompletionFrame),
                Disposition = Read4(SigmaGeneratedFrame.CompletionFrame + 2),
                Evidence = Read4(SigmaGeneratedFrame.CompletionFrame + 4),
                Publication = Read4(SigmaGeneratedFrame.CompletionFrame + 6),
            };
            SigmaFrameUInt4Gpu root = Read4(
                SigmaGeneratedFrame.CompletionRoot);
            Require(root.Y == frame.Identity.X,
                "Completion root revision does not match its frame.");
            var constraint = new SigmaUnresolvedConstraintGpu
            {
                Observation = Read4(
                    SigmaGeneratedFrame.CompletionUnresolved),
                Relation = Read4(
                    SigmaGeneratedFrame.CompletionUnresolved + 2),
                Evidence = Read4(
                    SigmaGeneratedFrame.CompletionUnresolved + 4),
                Provenance = Read4(
                    SigmaGeneratedFrame.CompletionUnresolved + 6),
                Frontier = Read4(
                    SigmaGeneratedFrame.CompletionUnresolved + 8),
                Program = Read4(
                    SigmaGeneratedFrame.CompletionUnresolved + 10),
            };
            var headers = new SigmaFrameUInt4Gpu[2];
            for (int index = 0; index < headers.Length; ++index)
                headers[index] = Read4(
                    SigmaGeneratedFrame.CompletionObservationHeaders +
                    index * 2);
            var rays = new SigmaFrameUInt2Gpu[6];
            for (int index = 0; index < rays.Length; ++index)
                rays[index] = batch[recordBase +
                    SigmaGeneratedFrame.CompletionRoomRays + index];
            var leaves = new SigmaFrameUInt2Gpu[16];
            for (int index = 0; index < leaves.Length; ++index)
                leaves[index] = batch[recordBase +
                    SigmaGeneratedFrame.CompletionCodeLeaves + index];
            var certificate = new SigmaFrameUInt4Gpu[16];
            for (int index = 0; index < certificate.Length; ++index)
                certificate[index] = Read4(
                    SigmaGeneratedFrame.CompletionCertificate + index * 2);
            return new SigmaNativeCompletionRecord(frame, root.X,
                new SigmaExactConstraintRecord(constraint, headers, rays,
                    leaves, certificate));
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidDataException(message);
        }
    }

    /// <summary>
    /// Dynamically segmented, batched GPU-to-host persistence handoff. A batch
    /// readback is recorded only on the frame that seals it; ingress and root
    /// publication complete solely on their GPU fence and never wait for this
    /// transfer. Segment count follows undrained persistence work, not session
    /// length, and completed segments are recycled.
    /// </summary>
    internal sealed class SigmaNativeCompletionTransfer : IDisposable
    {
        internal const int RecordsPerBatch = 16;
        private readonly object _gate = new();
        private readonly Queue<TransferResult> _completed = new();
        private readonly Stack<Segment> _free = new();
        private readonly HashSet<Segment> _segments = new();
        private Segment _open;
        private int _nextSegment;
        private bool _disposed;

        internal readonly struct Reservation
        {
            internal readonly Segment _segment;

            internal Reservation(Segment segment, int recordIndex,
                bool sealsBatch)
            {
                _segment = segment;
                RecordIndex = recordIndex;
                SealsBatch = sealsBatch;
            }

            internal GraphicsBuffer Buffer => _segment?.Buffer;
            internal int RecordIndex { get; }
            internal bool SealsBatch { get; }
            internal bool IsValid => _segment != null;
        }

        internal Reservation Reserve()
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                if (_open == null)
                    _open = AcquireSegment();
                int index = _open.RecordCount++;
                bool seals = _open.RecordCount == RecordsPerBatch;
                Segment segment = _open;
                if (seals)
                {
                    segment.Sealed = true;
                    _open = null;
                }
                return new Reservation(segment, index, seals);
            }
        }

        internal void RecordSealedReadback(CommandBuffer command,
            Reservation reservation)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (!reservation.IsValid || !reservation.SealsBatch)
                return;
            Segment segment = reservation._segment;
            lock (_gate)
            {
                ThrowIfDisposed();
                if (!segment.Sealed || segment.ReadbackIssued)
                    throw new InvalidOperationException(
                        "Completion batch has an invalid seal state.");
                segment.ReadbackIssued = true;
            }
            int bytes = checked(segment.RecordCount *
                SigmaGeneratedFrame.CompletionWordCount * sizeof(uint) * 2);
            command.RequestAsyncReadback(segment.Buffer, bytes, 0,
                request => CompleteReadback(segment, request));
        }

        internal void Cancel(Reservation reservation)
        {
            if (!reservation.IsValid)
                return;
            lock (_gate)
            {
                Segment segment = reservation._segment;
                if (reservation.RecordIndex != segment.RecordCount - 1)
                    throw new InvalidOperationException(
                        "Only the latest completion reservation can be cancelled.");
                // The caller invokes Cancel only when its command buffer was
                // never submitted, so a recorded readback command was discarded
                // together with that command buffer and has no callback.
                segment.ReadbackIssued = false;
                segment.RecordCount--;
                segment.Sealed = false;
                if (_open == null)
                    _open = segment;
            }
        }

        internal void FlushOpenAfterGpuIdle()
        {
            Segment segment;
            lock (_gate)
            {
                if (_disposed || _open == null || _open.RecordCount == 0)
                    return;
                segment = _open;
                _open = null;
                segment.Sealed = true;
                segment.ReadbackIssued = true;
            }
            int bytes = checked(segment.RecordCount *
                SigmaGeneratedFrame.CompletionWordCount * sizeof(uint) * 2);
            AsyncGPUReadback.Request(segment.Buffer, bytes, 0,
                request => CompleteReadback(segment, request));
        }

        internal bool TryDequeue(out SigmaNativeCompletionRecord record,
            out string error)
        {
            lock (_gate)
            {
                if (_completed.Count == 0)
                {
                    record = null;
                    error = null;
                    return false;
                }
                TransferResult result = _completed.Dequeue();
                record = result.Record;
                error = result.Error;
                return true;
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                foreach (Segment segment in _segments)
                    if (!segment.ReadbackIssued)
                        segment.Buffer.Dispose();
                _free.Clear();
                _open = null;
                _completed.Clear();
            }
        }

        private Segment AcquireSegment()
        {
            if (_free.Count != 0)
            {
                Segment reused = _free.Pop();
                reused.Reset();
                return reused;
            }
            var buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                RecordsPerBatch * SigmaGeneratedFrame.CompletionWordCount,
                sizeof(uint) * 2)
            {
                name = $"Sigma exact completion batch {_nextSegment++}",
            };
            var segment = new Segment(buffer);
            _segments.Add(segment);
            return segment;
        }

        private void CompleteReadback(Segment segment,
            AsyncGPUReadbackRequest request)
        {
            var decoded = new List<TransferResult>(segment.RecordCount);
            if (request.hasError)
            {
                decoded.Add(new TransferResult(null,
                    "Batched exact completion readback failed."));
            }
            else
            {
                try
                {
                    NativeArray<SigmaFrameUInt2Gpu> words =
                        request.GetData<SigmaFrameUInt2Gpu>();
                    int expected = checked(segment.RecordCount *
                        SigmaGeneratedFrame.CompletionWordCount);
                    if (words.Length != expected)
                        throw new InvalidDataException(
                            "Batched exact completion size mismatch.");
                    for (int index = 0; index < segment.RecordCount; ++index)
                        decoded.Add(new TransferResult(
                            SigmaNativeCompletionRecord.Decode(words, index),
                            null));
                }
                catch (Exception exception)
                {
                    decoded.Clear();
                    decoded.Add(new TransferResult(null,
                        "Exact completion decode failed: " +
                        exception.Message));
                }
            }
            lock (_gate)
            {
                if (!_disposed)
                    for (int index = 0; index < decoded.Count; ++index)
                        _completed.Enqueue(decoded[index]);
                segment.ReadbackIssued = false;
                segment.Sealed = false;
                segment.RecordCount = 0;
                if (_disposed)
                    segment.Buffer.Dispose();
                else
                    _free.Push(segment);
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(
                    nameof(SigmaNativeCompletionTransfer));
        }

        internal sealed class Segment
        {
            internal Segment(GraphicsBuffer buffer) => Buffer = buffer;
            internal GraphicsBuffer Buffer { get; }
            internal int RecordCount { get; set; }
            internal bool Sealed { get; set; }
            internal bool ReadbackIssued { get; set; }
            internal void Reset()
            {
                RecordCount = 0;
                Sealed = false;
                ReadbackIssued = false;
            }
        }

        private readonly struct TransferResult
        {
            internal TransferResult(SigmaNativeCompletionRecord record,
                string error)
            {
                Record = record;
                Error = error;
            }
            internal SigmaNativeCompletionRecord Record { get; }
            internal string Error { get; }
        }
    }
}
