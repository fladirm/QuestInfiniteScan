using System;
using System.Collections.Generic;
using System.IO;
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
    }

    /// <summary>
    /// Coalesced durable owner for the minimized unresolved-factor journal.
    /// Immutable snapshots are written off the frame thread and atomically
    /// replace the prior generation. It owns no physical field state.
    /// </summary>
    internal sealed class SigmaExactConstraintStore : IDisposable
    {
        private readonly object _gate = new();
        private readonly string _path;
        private byte[] _pending;
        private Task _writer;
        private string _fault;
        private bool _accepting = true;

        internal SigmaExactConstraintStore(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A durable journal path is required.",
                    nameof(path));
            _path = path;
        }

        internal SigmaExactConstraintJournal Load()
        {
            lock (_gate)
            {
                if (!_accepting || _writer != null)
                    throw new InvalidOperationException(
                        "Journal load is legal only before persistence begins.");
            }
            return File.Exists(_path)
                ? SigmaExactConstraintJournal.DecodeCanonical(
                    File.ReadAllBytes(_path))
                : new SigmaExactConstraintJournal();
        }

        internal void Stage(SigmaExactConstraintJournal journal)
        {
            if (journal == null) throw new ArgumentNullException(nameof(journal));
            byte[] snapshot = journal.EncodeCanonical();
            lock (_gate)
            {
                if (!_accepting)
                    throw new ObjectDisposedException(
                        nameof(SigmaExactConstraintStore));
                if (_fault != null)
                    throw new IOException(_fault);
                _pending = snapshot;
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
                byte[] snapshot;
                lock (_gate)
                {
                    snapshot = _pending;
                    _pending = null;
                    if (snapshot == null)
                    {
                        _writer = null;
                        return;
                    }
                }
                try
                {
                    WriteAtomic(snapshot);
                }
                catch (Exception exception)
                {
                    lock (_gate)
                    {
                        _fault = "Exact constraint persistence failed: " +
                            exception.Message;
                        _pending = null;
                        _writer = null;
                    }
                    return;
                }
            }
        }

        private void WriteAtomic(byte[] snapshot)
        {
            string directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            string temporary = _path + ".next";
            using (var stream = new FileStream(temporary, FileMode.Create,
                       FileAccess.Write, FileShare.None, 4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(snapshot, 0, snapshot.Length);
                stream.Flush(true);
            }
            if (File.Exists(_path))
                File.Replace(temporary, _path, null);
            else
                File.Move(temporary, _path);
        }
    }

    /// <summary>
    /// Exact observation-side factor retained only while its full-field support
    /// remains unresolved.  It has no carrier address or branch/object identity.
    /// </summary>
    internal sealed class SigmaExactConstraintRecord
    {
        internal SigmaExactConstraintRecord(
            SigmaUnresolvedConstraintGpu constraint,
            SigmaFrameUInt4Gpu[] observationHeaders,
            SigmaFrameUInt2Gpu[] roomRays,
            SigmaFrameUInt2Gpu[] codeLeaves)
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
        }

        internal SigmaUnresolvedConstraintGpu Constraint { get; }
        internal SigmaFrameUInt4Gpu[] ObservationHeaders { get; }
        internal SigmaFrameUInt2Gpu[] RoomRays { get; }
        internal SigmaFrameUInt2Gpu[] CodeLeaves { get; }

        internal bool SameContextAndIndependence(SigmaExactConstraintRecord other)
        {
            if (other == null ||
                Constraint.Observation.Y != other.Constraint.Observation.Y ||
                Constraint.Observation.W != other.Constraint.Observation.W ||
                !Equal(Constraint.Relation, other.Constraint.Relation) ||
                !Equal(Constraint.Evidence, other.Constraint.Evidence) ||
                Constraint.Provenance.X != other.Constraint.Provenance.X ||
                Constraint.Provenance.Y != other.Constraint.Provenance.Y ||
                Constraint.Provenance.W != other.Constraint.Provenance.W)
                return false;
            // Revision is intentionally excluded; epoch, roles, query transfer,
            // provenance, calibrated rows and independence class are retained.
            SigmaFrameUInt4Gpu leftHeader = ObservationHeaders[0];
            SigmaFrameUInt4Gpu rightHeader = other.ObservationHeaders[0];
            if (leftHeader.X != rightHeader.X ||
                leftHeader.Z != rightHeader.Z ||
                leftHeader.W != rightHeader.W ||
                !Equal(ObservationHeaders[1], other.ObservationHeaders[1]))
                return false;
            for (int index = 0; index < RoomRays.Length; ++index)
                if (!Equal(RoomRays[index], other.RoomRays[index]))
                    return false;
            return true;
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

        internal byte[] CanonicalBytes()
        {
            using var stream = new MemoryStream(272);
            using var writer = new BinaryWriter(stream);
            Write(writer, Constraint.Observation);
            Write(writer, Constraint.Relation);
            Write(writer, Constraint.Evidence);
            Write(writer, Constraint.Provenance);
            for (int index = 0; index < ObservationHeaders.Length; ++index)
                Write(writer, ObservationHeaders[index]);
            for (int index = 0; index < RoomRays.Length; ++index)
                Write(writer, RoomRays[index]);
            for (int index = 0; index < CodeLeaves.Length; ++index)
                Write(writer, CodeLeaves[index]);
            writer.Flush();
            return stream.ToArray();
        }

        internal string FormatLogLine(uint revision) =>
            $"Sigma unresolved exact-factor revision={revision} " +
            $"relation={Constraint.Relation.X}/{Constraint.Relation.Y}/" +
            $"{Constraint.Relation.Z}/{Constraint.Relation.W} " +
            $"epoch={Constraint.Provenance.W}";

        internal static SigmaExactConstraintRecord Read(BinaryReader reader)
        {
            var constraint = new SigmaUnresolvedConstraintGpu
            {
                Observation = Read4(reader),
                Relation = Read4(reader),
                Evidence = Read4(reader),
                Provenance = Read4(reader),
            };
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
        private static bool Equal(SigmaFrameUInt2Gpu left,
            SigmaFrameUInt2Gpu right) => left.X == right.X && left.Y == right.Y;
        private static bool Equal(SigmaFrameUInt4Gpu left,
            SigmaFrameUInt4Gpu right) => left.X == right.X && left.Y == right.Y &&
            left.Z == right.Z && left.W == right.W;
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

    /// <summary>
    /// Canonically ordered, reclaimable exact-factor journal.  Exact duplicates
    /// retain one factor plus bounded multiplicity; a broader factor may be
    /// removed only inside the identical context and independence class.
    /// </summary>
    internal sealed class SigmaExactConstraintJournal
    {
        private const uint Magic = 0x34434a53u; // SJC4
        private const uint Version = 1u;
        private readonly List<Entry> _entries = new();

        internal int Count => _entries.Count;

        internal SigmaConstraintAdmission Add(SigmaExactConstraintRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            bool removedWeaker = false;
            for (int index = _entries.Count - 1; index >= 0; --index)
            {
                SigmaExactConstraintRecord existing = _entries[index].Record;
                if (existing.NoBroaderThan(record))
                {
                    if (record.NoBroaderThan(existing))
                    {
                        SigmaExactConstraintRecord canonical =
                            CompareRecords(existing, record) <= 0
                                ? existing : record;
                        // Repetition inside one independence class is not new
                        // information. Keep durable bytes independent of the
                        // number and order of exact duplicate revisits.
                        _entries[index] = new Entry(canonical, 1u);
                        _entries.Sort(CompareEntries);
                    }
                    return SigmaConstraintAdmission.DuplicateOrWeaker;
                }
                if (record.NoBroaderThan(existing))
                {
                    _entries.RemoveAt(index);
                    removedWeaker = true;
                }
            }
            _entries.Add(new Entry(record, 1u));
            _entries.Sort(CompareEntries);
            return removedWeaker ? SigmaConstraintAdmission.ReplacedWeaker :
                SigmaConstraintAdmission.Added;
        }

        internal byte[] EncodeCanonical()
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(Magic);
            writer.Write(Version);
            writer.Write((uint)_entries.Count);
            for (int index = 0; index < _entries.Count; ++index)
            {
                byte[] bytes = _entries[index].Record.CanonicalBytes();
                writer.Write(_entries[index].Multiplicity);
                writer.Write((uint)bytes.Length);
                writer.Write(bytes);
            }
            writer.Flush();
            return stream.ToArray();
        }

        internal static SigmaExactConstraintJournal DecodeCanonical(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            using var stream = new MemoryStream(bytes, false);
            using var reader = new BinaryReader(stream);
            Require(reader.ReadUInt32() == Magic, "Invalid constraint journal magic.");
            Require(reader.ReadUInt32() == Version,
                "Unsupported constraint journal version.");
            uint count = reader.ReadUInt32();
            Require(count <= int.MaxValue, "Constraint count is invalid.");
            var result = new SigmaExactConstraintJournal();
            for (uint index = 0; index < count; ++index)
            {
                uint multiplicity = reader.ReadUInt32();
                uint length = reader.ReadUInt32();
                Require(multiplicity != 0u && length == 272u,
                    "Invalid exact constraint record header.");
                long end = checked(stream.Position + length);
                SigmaExactConstraintRecord record =
                    SigmaExactConstraintRecord.Read(reader);
                Require(stream.Position == end,
                    "Invalid exact constraint record length.");
                result._entries.Add(new Entry(record, multiplicity));
            }
            result._entries.Sort(CompareEntries);
            Require(stream.Position == stream.Length,
                "Trailing exact constraint journal bytes.");
            return result;
        }

        internal void Clear() => _entries.Clear();

        private static int CompareEntries(Entry left, Entry right)
        {
            int recordOrder = CompareRecords(left.Record, right.Record);
            return recordOrder != 0 ? recordOrder :
                left.Multiplicity.CompareTo(right.Multiplicity);
        }

        private static int CompareRecords(SigmaExactConstraintRecord left,
            SigmaExactConstraintRecord right)
        {
            byte[] a = left.CanonicalBytes();
            byte[] b = right.CanonicalBytes();
            for (int index = 0; index < a.Length; ++index)
            {
                int order = a[index].CompareTo(b[index]);
                if (order != 0) return order;
            }
            return 0;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidDataException(message);
        }

        private readonly struct Entry
        {
            internal Entry(SigmaExactConstraintRecord record, uint multiplicity)
            {
                Record = record;
                Multiplicity = multiplicity;
            }
            internal SigmaExactConstraintRecord Record { get; }
            internal uint Multiplicity { get; }
        }
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
            return new SigmaNativeCompletionRecord(frame, root.X,
                new SigmaExactConstraintRecord(constraint, headers, rays,
                    leaves));
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
