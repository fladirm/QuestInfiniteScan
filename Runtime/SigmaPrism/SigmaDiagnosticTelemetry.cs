using System;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.SigmaPrism
{
    /// <summary>
    /// Read-only diagnostics for the direct frame DAG. Tiny asynchronous samples
    /// describe exact closure, the atomic revision root, topology and disposable
    /// readout. They never schedule work or decide canonical state.
    /// </summary>
    internal sealed class SigmaDiagnosticTelemetry : IDisposable
    {
        private const float SampleIntervalSeconds = 0.0f;
        private const int GateWords = 1;
        private const int ClosureWords = 16;
        private const int RevisionWords =
            SigmaGeneratedFrame.FrameRevisionStride / sizeof(uint);
        private const int TopologyWords = 8;
        private const int DrawArgumentWords = 4;
        private const int ReadoutVertexWords =
            SigmaRenderer.ReadoutSamplesPerPage * 4;
        private const uint InvalidPageSlot = uint.MaxValue;

        private Batch _active;
        private float _nextSampleTime;
        private uint _sequence;
        private uint _nextReadoutPageSlot = InvalidPageSlot;
        private int _nextReadoutPageOrdinal;
        private bool _disposed;
        private bool _unsupportedReported;

        internal SigmaRuntimeTelemetrySnapshot Snapshot { get; private set; } =
            SigmaRuntimeTelemetrySnapshot.Awaiting;

        internal bool IsDue(float unscaledTime) => !_disposed &&
            _active == null && unscaledTime >= _nextSampleTime;

        internal void Tick(float unscaledTime, long submittedFrames,
            long committedFrames, uint hostRevision, GraphicsBuffer gate,
            GraphicsBuffer closureCounters, GraphicsBuffer revisions,
            GraphicsBuffer revisionRoot, GraphicsBuffer topologyCounters,
            GraphicsBuffer drawArguments, GraphicsBuffer currentPageSlots,
            GraphicsBuffer readoutVertices, int readoutPageCapacity,
            SigmaRuntimeTimingTelemetry timing)
        {
            if (!IsDue(unscaledTime))
                return;
            _nextSampleTime = unscaledTime + SampleIntervalSeconds;
            if (!SystemInfo.supportsAsyncGPUReadback)
            {
                if (!_unsupportedReported)
                {
                    _unsupportedReported = true;
                    Logger.Warning("Sigma direct telemetry is unavailable: " +
                        "AsyncGPUReadback is unsupported; no synchronous " +
                        "fallback is permitted.");
                }
                Snapshot = SigmaRuntimeTelemetrySnapshot.Unsupported;
                return;
            }
            if (gate == null || closureCounters == null || revisions == null ||
                revisionRoot == null || topologyCounters == null)
            {
                Snapshot = SigmaRuntimeTelemetrySnapshot.MissingBuffers;
                return;
            }

            var batch = new Batch(++_sequence, submittedFrames,
                committedFrames, hostRevision, readoutPageCapacity,
                _nextReadoutPageSlot, timing);
            _active = batch;
            try
            {
                Request(batch, BufferKind.Gate, gate, GateWords);
                Request(batch, BufferKind.Closure, closureCounters,
                    ClosureWords);
                Request(batch, BufferKind.Revisions, revisions,
                    checked(revisions.count * RevisionWords));
                Request(batch, BufferKind.RevisionRoot, revisionRoot, 1);
                Request(batch, BufferKind.Topology, topologyCounters,
                    TopologyWords);
                if (drawArguments != null && currentPageSlots != null &&
                    readoutVertices != null && readoutPageCapacity > 0)
                {
                    Request(batch, BufferKind.DrawArguments, drawArguments,
                        DrawArgumentWords);
                    Request(batch, BufferKind.CurrentPageSlots,
                        currentPageSlots, Math.Min(readoutPageCapacity,
                            currentPageSlots.count));
                    if (batch.SampledReadoutPageSlot != InvalidPageSlot)
                    {
                        int byteOffset = checked(
                            (int)batch.SampledReadoutPageSlot *
                            SigmaRenderer.ReadoutSamplesPerPage *
                            sizeof(float) * 4);
                        RequestRange(batch, BufferKind.ReadoutVertices,
                            readoutVertices, ReadoutVertexWords, byteOffset);
                    }
                }
            }
            catch (Exception exception)
            {
                batch.Cancelled = true;
                _active = null;
                Snapshot = SigmaRuntimeTelemetrySnapshot.RequestFailed(
                    exception.Message);
                return;
            }
            if (batch.Remaining == 0)
                FinalizeBatch(batch);
        }

        private void Request(Batch batch, BufferKind kind,
            GraphicsBuffer buffer, int expectedWords)
        {
            batch.Remaining++;
            AsyncGPUReadback.Request(buffer, request =>
                Complete(batch, kind, expectedWords, request));
        }

        private void RequestRange(Batch batch, BufferKind kind,
            GraphicsBuffer buffer, int expectedWords, int byteOffset)
        {
            batch.Remaining++;
            AsyncGPUReadback.Request(buffer,
                checked(expectedWords * sizeof(uint)), byteOffset, request =>
                    Complete(batch, kind, expectedWords, request));
        }

        private void Complete(Batch batch, BufferKind kind,
            int expectedWords, AsyncGPUReadbackRequest request)
        {
            if (_disposed || batch.Cancelled ||
                !ReferenceEquals(_active, batch))
                return;
            uint[] words = null;
            if (request.hasError)
                batch.ErrorMask |= 1u << (int)kind;
            else
            {
                try
                {
                    var source = request.GetData<uint>();
                    if (source.Length < expectedWords)
                        batch.ErrorMask |= 1u << (int)kind;
                    else
                    {
                        words = new uint[expectedWords];
                        for (int index = 0; index < expectedWords; ++index)
                            words[index] = source[index];
                    }
                }
                catch (Exception)
                {
                    batch.ErrorMask |= 1u << (int)kind;
                }
            }
            batch.Set(kind, words);
            batch.Remaining--;
            if (batch.Remaining == 0)
                FinalizeBatch(batch);
        }

        private void FinalizeBatch(Batch batch)
        {
            if (!ReferenceEquals(_active, batch))
                return;
            _active = null;
            SelectNextReadoutPage(batch);
            Snapshot = SigmaRuntimeTelemetrySnapshot.From(batch);
            if (batch.ErrorMask == 0u)
                Logger.Info(Snapshot.FormatLogLine());
            else
                Logger.Warning(Snapshot.FormatLogLine());
        }

        private void SelectNextReadoutPage(Batch batch)
        {
            uint pageCount = SigmaRenderer.VerticesPerCarrierPage == 0
                ? 0u : Word(batch.DrawArguments, 0) /
                    (uint)SigmaRenderer.VerticesPerCarrierPage;
            int available = Math.Min(batch.CurrentPageSlots?.Length ?? 0,
                pageCount > int.MaxValue ? int.MaxValue : (int)pageCount);
            if (available == 0)
            {
                _nextReadoutPageSlot = InvalidPageSlot;
                _nextReadoutPageOrdinal = 0;
                return;
            }
            int ordinal = _nextReadoutPageOrdinal % available;
            uint pageSlot = batch.CurrentPageSlots[ordinal];
            _nextReadoutPageSlot = pageSlot < batch.ReadoutPageCapacity
                ? pageSlot : InvalidPageSlot;
            _nextReadoutPageOrdinal = (ordinal + 1) % available;
        }

        private static uint Word(uint[] words, int index) =>
            words != null && (uint)index < (uint)words.Length
                ? words[index] : 0u;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            if (_active != null)
                _active.Cancelled = true;
            _active = null;
        }

        internal enum BufferKind
        {
            Gate = 0,
            Closure = 1,
            Revisions = 2,
            RevisionRoot = 3,
            Topology = 4,
            DrawArguments = 5,
            CurrentPageSlots = 6,
            ReadoutVertices = 7,
        }

        internal sealed class Batch
        {
            internal Batch(uint sequence, long submittedFrames,
                long committedFrames, uint hostRevision,
                int readoutPageCapacity, uint sampledReadoutPageSlot,
                SigmaRuntimeTimingTelemetry timing)
            {
                Sequence = sequence;
                SubmittedFrames = submittedFrames;
                CommittedFrames = committedFrames;
                HostRevision = hostRevision;
                ReadoutPageCapacity = readoutPageCapacity;
                SampledReadoutPageSlot = sampledReadoutPageSlot;
                Timing = timing;
            }

            internal uint Sequence { get; }
            internal long SubmittedFrames { get; }
            internal long CommittedFrames { get; }
            internal uint HostRevision { get; }
            internal int ReadoutPageCapacity { get; }
            internal uint SampledReadoutPageSlot { get; }
            internal SigmaRuntimeTimingTelemetry Timing { get; }
            internal int Remaining { get; set; }
            internal uint ErrorMask { get; set; }
            internal bool Cancelled { get; set; }
            internal uint[] Gate { get; private set; }
            internal uint[] Closure { get; private set; }
            internal uint[] Revisions { get; private set; }
            internal uint[] RevisionRoot { get; private set; }
            internal uint[] Topology { get; private set; }
            internal uint[] DrawArguments { get; private set; }
            internal uint[] CurrentPageSlots { get; private set; }
            internal uint[] ReadoutVertices { get; private set; }

            internal void Set(BufferKind kind, uint[] words)
            {
                switch (kind)
                {
                    case BufferKind.Gate: Gate = words; break;
                    case BufferKind.Closure: Closure = words; break;
                    case BufferKind.Revisions: Revisions = words; break;
                    case BufferKind.RevisionRoot: RevisionRoot = words; break;
                    case BufferKind.Topology: Topology = words; break;
                    case BufferKind.DrawArguments: DrawArguments = words; break;
                    case BufferKind.CurrentPageSlots:
                        CurrentPageSlots = words;
                        break;
                    case BufferKind.ReadoutVertices:
                        ReadoutVertices = words;
                        break;
                }
            }
        }
    }

    public readonly struct SigmaStageLatencyTelemetry
    {
        internal SigmaStageLatencyTelemetry(long sampleCount, double lastMs,
            double averageMs, double maximumMs)
        {
            SampleCount = sampleCount;
            LastMs = lastMs;
            AverageMs = averageMs;
            MaximumMs = maximumMs;
        }

        public long SampleCount { get; }
        public double LastMs { get; }
        public double AverageMs { get; }
        public double MaximumMs { get; }

        internal void AppendTo(StringBuilder text, string name)
        {
            text.Append(name).Append('=');
            if (SampleCount == 0)
            {
                text.Append("pending");
                return;
            }
            text.Append(LastMs.ToString("F2")).Append('/')
                .Append(AverageMs.ToString("F2")).Append('/')
                .Append(MaximumMs.ToString("F2")).Append("ms#")
                .Append(SampleCount);
        }
    }

    public readonly struct SigmaRuntimeTimingTelemetry
    {
        internal SigmaRuntimeTimingTelemetry(SigmaStageLatencyTelemetry frame,
            double cpuFrameMs, double gpuFrameMs, bool hasFrameTiming)
        {
            Frame = frame;
            CpuFrameMs = cpuFrameMs;
            GpuFrameMs = gpuFrameMs;
            HasFrameTiming = hasFrameTiming;
        }

        public SigmaStageLatencyTelemetry Frame { get; }
        public double CpuFrameMs { get; }
        public double GpuFrameMs { get; }
        public bool HasFrameTiming { get; }

        internal void AppendTo(StringBuilder text)
        {
            text.Append("wall.xr=");
            if (HasFrameTiming)
                text.Append(CpuFrameMs.ToString("F2")).Append('/')
                    .Append(GpuFrameMs.ToString("F2")).Append("ms");
            else
                text.Append("unavailable");
            text.Append(' ');
            Frame.AppendTo(text, "wall.direct");
        }
    }

    public readonly struct SigmaReadoutVertexTelemetry
    {
        internal SigmaReadoutVertexTelemetry(uint pageSlot, uint[] words)
        {
            PageSlot = pageSlot;
            SampleCount = words?.Length / 4 ?? 0;
            SupportedCount = 0;
            InvalidCount = 0;
            MinInformationMass = float.PositiveInfinity;
            MaxInformationMass = float.NegativeInfinity;
            MinPosition = new Vector3(float.PositiveInfinity,
                float.PositiveInfinity, float.PositiveInfinity);
            MaxPosition = new Vector3(float.NegativeInfinity,
                float.NegativeInfinity, float.NegativeInfinity);
            for (int sample = 0; sample < SampleCount; ++sample)
            {
                int offset = sample * 4;
                float x = BitsToFloat(words[offset]);
                float y = BitsToFloat(words[offset + 1]);
                float z = BitsToFloat(words[offset + 2]);
                float mass = BitsToFloat(words[offset + 3]);
                if (!IsFinite(x) || !IsFinite(y) || !IsFinite(z) ||
                    !IsFinite(mass))
                {
                    InvalidCount++;
                    continue;
                }
                if (mass <= 0f)
                    continue;
                SupportedCount++;
                MinInformationMass = Mathf.Min(MinInformationMass, mass);
                MaxInformationMass = Mathf.Max(MaxInformationMass, mass);
                Vector3 position = new(x, y, z);
                MinPosition = Vector3.Min(MinPosition, position);
                MaxPosition = Vector3.Max(MaxPosition, position);
            }
            if (SupportedCount == 0)
            {
                MinInformationMass = 0f;
                MaxInformationMass = 0f;
                MinPosition = Vector3.zero;
                MaxPosition = Vector3.zero;
            }
        }

        public uint PageSlot { get; }
        public int SampleCount { get; }
        public int SupportedCount { get; }
        public int InvalidCount { get; }
        public float MinInformationMass { get; }
        public float MaxInformationMass { get; }
        public Vector3 MinPosition { get; }
        public Vector3 MaxPosition { get; }
        public bool HasSample => PageSlot != InvalidPageSlot && SampleCount != 0;

        internal void AppendTo(StringBuilder text)
        {
            if (!HasSample)
            {
                text.Append("pending");
                return;
            }
            text.Append("page=").Append(PageSlot)
                .Append(" support=").Append(SupportedCount).Append('/')
                .Append(SampleCount).Append(" invalid=").Append(InvalidCount)
                .Append(" info=").Append(MinInformationMass).Append("..")
                .Append(MaxInformationMass).Append(" aabb=")
                .Append(MinPosition).Append("..").Append(MaxPosition);
        }

        private static float BitsToFloat(uint bits) =>
            BitConverter.Int32BitsToSingle(unchecked((int)bits));
        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
        private const uint InvalidPageSlot = uint.MaxValue;
    }

    public sealed class SigmaRuntimeTelemetrySnapshot
    {
        private SigmaRuntimeTelemetrySnapshot(bool hasSample, string status)
        {
            HasSample = hasSample;
            Status = status ?? string.Empty;
            ReadoutVertices = new SigmaReadoutVertexTelemetry(
                uint.MaxValue, Array.Empty<uint>());
        }

        private SigmaRuntimeTelemetrySnapshot(SigmaDiagnosticTelemetry.Batch batch)
        {
            HasSample = true;
            Status = batch.ErrorMask == 0u ? "sampled" :
                $"readback-error-mask=0x{batch.ErrorMask:X}";
            Sequence = batch.Sequence;
            ErrorMask = batch.ErrorMask;
            SubmittedFrames = batch.SubmittedFrames;
            CommittedFrames = batch.CommittedFrames;
            HostRevision = batch.HostRevision;
            GateWord = Word(batch.Gate, 0);
            PendingExtentWidth = Word(batch.Closure, 0);
            PendingPagePairs = Word(batch.Closure, 1);
            AllocationPageSlot = Word(batch.Closure, 6);
            ChangedPages = Word(batch.Closure, 7);
            FaultMask = Word(batch.Closure, 8);
            DirtyEdges = Word(batch.Closure, 9);
            RevisionRoot = Word(batch.RevisionRoot, 0);
            int revisionOffset = RevisionRoot == 0u ? -1 :
                checked(((int)RevisionRoot - 1) *
                    SigmaGeneratedFrame.FrameRevisionStride / sizeof(uint));
            PublishedRevision = Word(batch.Revisions, revisionOffset);
            BaseRevision = Word(batch.Revisions, revisionOffset + 1);
            RevisionState = Word(batch.Revisions, revisionOffset + 2);
            RevisionFrameSlot = Word(batch.Revisions, revisionOffset + 3);
            RevisionChangedPages = Word(batch.Revisions, revisionOffset + 5);
            RevisionSegment = Word(batch.Revisions, revisionOffset + 6);
            RevisionPageCapacity = Word(batch.Revisions, revisionOffset + 7);
            WitnessFrameSlot = Word(batch.Revisions, revisionOffset + 8);
            WitnessFootprints = Word(batch.Revisions, revisionOffset + 9);
            WitnessDirtyEdges = Word(batch.Revisions, revisionOffset + 10);
            TopologyEvaluated = Word(batch.Topology, 0);
            TopologyAnnihilatorEvaluations = Word(batch.Topology, 1);
            TopologyAssociatorEvaluations = Word(batch.Topology, 2);
            TopologySingular = Word(batch.Topology, 3);
            TopologyUnresolved = Word(batch.Topology, 4);
            TopologyReused = Word(batch.Topology, 5);
            TopologyOverflow = Word(batch.Topology, 6);
            DrawVertexCount = Word(batch.DrawArguments, 0);
            DrawInstanceCount = Word(batch.DrawArguments, 1);
            ReadoutVertices = new SigmaReadoutVertexTelemetry(
                batch.SampledReadoutPageSlot, batch.ReadoutVertices);
            Timing = batch.Timing;
        }

        public static SigmaRuntimeTelemetrySnapshot Awaiting { get; } =
            new(false, "awaiting-first-sample");
        internal static SigmaRuntimeTelemetrySnapshot Unsupported { get; } =
            new(false, "async-readback-unsupported");
        internal static SigmaRuntimeTelemetrySnapshot MissingBuffers { get; } =
            new(false, "diagnostic-buffers-missing");
        internal static SigmaRuntimeTelemetrySnapshot RequestFailed(
            string detail) => new(false, "request-failed: " + detail);
        internal static SigmaRuntimeTelemetrySnapshot From(
            SigmaDiagnosticTelemetry.Batch batch) => new(batch);

        public bool HasSample { get; }
        public string Status { get; }
        public uint Sequence { get; }
        public uint ErrorMask { get; }
        public long SubmittedFrames { get; }
        public long CommittedFrames { get; }
        public uint HostRevision { get; }
        public uint GateWord { get; }
        public uint PendingExtentWidth { get; }
        public uint PendingPagePairs { get; }
        public uint AllocationPageSlot { get; }
        public uint ChangedPages { get; }
        public uint FaultMask { get; }
        public uint DirtyEdges { get; }
        public uint RevisionRoot { get; }
        public uint PublishedRevision { get; }
        public uint BaseRevision { get; }
        public uint RevisionState { get; }
        public uint RevisionFrameSlot { get; }
        public uint RevisionChangedPages { get; }
        public uint RevisionSegment { get; }
        public uint RevisionPageCapacity { get; }
        public uint WitnessFrameSlot { get; }
        public uint WitnessFootprints { get; }
        public uint WitnessDirtyEdges { get; }
        public uint TopologyEvaluated { get; }
        public uint TopologyAnnihilatorEvaluations { get; }
        public uint TopologyAssociatorEvaluations { get; }
        public uint TopologySingular { get; }
        public uint TopologyUnresolved { get; }
        public uint TopologyReused { get; }
        public uint TopologyOverflow { get; }
        public uint DrawVertexCount { get; }
        public uint DrawInstanceCount { get; }
        public uint ReadoutPageCount => SigmaRenderer.VerticesPerCarrierPage == 0
            ? 0u : DrawVertexCount /
                (uint)SigmaRenderer.VerticesPerCarrierPage;
        public SigmaReadoutVertexTelemetry ReadoutVertices { get; }
        public SigmaRuntimeTimingTelemetry Timing { get; }

        public string Frontier
        {
            get
            {
                if (!HasSample || ErrorMask != 0u)
                    return Status;
                if (GateWord == 0u)
                    return "exact-backend-gate";
                if (FaultMask != 0u)
                    return "exact-frame-closure-fault";
                if (SubmittedFrames == 0)
                    return "coherent-frame-ingress";
                if (PublishedRevision == 0u)
                    return ChangedPages == 0u
                        ? "exact-inverse-no-change" : "atomic-publication";
                if (DrawVertexCount == 0u)
                    return "world-readout";
                if (ReadoutVertices.HasSample &&
                    ReadoutVertices.SupportedCount == 0)
                    return "geometry-readout-plan";
                return "xr-preview";
            }
        }

        public string FormatLogLine()
        {
            var text = new StringBuilder(900);
            text.Append("Sigma direct #").Append(Sequence)
                .Append(" status=").Append(Status)
                .Append(" frontier=").Append(Frontier)
                .Append(" gate=").Append(GateWord)
                .Append(" host=").Append(CommittedFrames).Append('/')
                .Append(SubmittedFrames).Append(" hostRev=")
                .Append(HostRevision).Append(" root=").Append(RevisionRoot)
                .Append(" revision={id=").Append(PublishedRevision)
                .Append(" base=").Append(BaseRevision)
                .Append(" state=").Append(RevisionState)
                .Append(" changed=").Append(RevisionChangedPages)
                .Append(" segment=").Append(RevisionSegment)
                .Append('/').Append(RevisionPageCapacity)
                .Append(" witness=").Append(WitnessFrameSlot).Append(':')
                .Append(WitnessFootprints).Append(':')
                .Append(WitnessDirtyEdges).Append("} closure={width=")
                .Append(PendingExtentWidth).Append(" pairs=")
                .Append(PendingPagePairs).Append(" slot=")
                .Append(AllocationPageSlot).Append(" changed=")
                .Append(ChangedPages).Append(" edges=").Append(DirtyEdges)
                .Append(" fault=0x").Append(FaultMask.ToString("X"))
                .Append("} topology=").Append(TopologyEvaluated).Append('/')
                .Append(TopologyAnnihilatorEvaluations).Append('/')
                .Append(TopologyAssociatorEvaluations).Append(" singular=")
                .Append(TopologySingular).Append(" unresolved=")
                .Append(TopologyUnresolved).Append(" overflow=")
                .Append(TopologyOverflow).Append(" draw=")
                .Append(DrawVertexCount).Append('v').Append('/')
                .Append(ReadoutPageCount).Append("p vertices={");
            ReadoutVertices.AppendTo(text);
            text.Append("} timing={");
            Timing.AppendTo(text);
            return text.Append("}").ToString();
        }

        private static uint Word(uint[] words, int index) =>
            words != null && index >= 0 && index < words.Length
                ? words[index] : 0u;
    }
}
