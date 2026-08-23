using System;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.SigmaPrism
{
    /// <summary>
    /// Disposable, read-only device instrumentation for the S4-08 device audit.
    /// It samples a few kilobytes asynchronously and never participates in
    /// scheduling, lifetime, proof, publication, or any canonical decision.
    /// </summary>
    internal sealed class SigmaDiagnosticTelemetry : IDisposable
    {
        private const float SampleIntervalSeconds = 1.0f;
        private const int GateWords = 1;
        private const int DiagnosticWords = 32;
        private const int SchedulerWords = 32;
        private const int WorkWords = SigmaGeneratedStreaming.OpcodeCount;
        private const int TransactionWords =
            SigmaGeneratedStreaming.TransactionStride / sizeof(uint);
        private const int TransactionCount =
            SigmaGeneratedStreaming.TransactionCapacity;
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
            GraphicsBuffer diagnostics, GraphicsBuffer scheduler,
            GraphicsBuffer workCounts, GraphicsBuffer transactions,
            GraphicsBuffer topologyCounters, GraphicsBuffer drawArguments,
            GraphicsBuffer currentPageSlots, GraphicsBuffer readoutVertices,
            int readoutPageCapacity, SigmaRuntimeTimingTelemetry timing)
        {
            if (!IsDue(unscaledTime))
                return;
            _nextSampleTime = unscaledTime + SampleIntervalSeconds;

            if (!SystemInfo.supportsAsyncGPUReadback)
            {
                if (!_unsupportedReported)
                {
                    _unsupportedReported = true;
                    Logger.Warning("Sigma telemetry unavailable: this device " +
                        "does not support asynchronous GPU readback. No " +
                        "synchronous fallback will be used.");
                }
                Snapshot = SigmaRuntimeTelemetrySnapshot.Unsupported;
                return;
            }

            if (gate == null || diagnostics == null || scheduler == null ||
                workCounts == null || transactions == null ||
                topologyCounters == null || drawArguments == null ||
                currentPageSlots == null || readoutVertices == null ||
                readoutPageCapacity <= 0)
            {
                Snapshot = SigmaRuntimeTelemetrySnapshot.MissingBuffers;
                Logger.Warning("Sigma telemetry sample skipped: one or more " +
                    "diagnostic GPU buffers are not resident.");
                return;
            }

            var batch = new Batch(++_sequence, submittedFrames,
                committedFrames, hostRevision, readoutPageCapacity,
                _nextReadoutPageSlot, timing);
            _active = batch;
            try
            {
                Request(batch, BufferKind.Gate, gate, GateWords);
                Request(batch, BufferKind.Diagnostics, diagnostics,
                    DiagnosticWords);
                Request(batch, BufferKind.Scheduler, scheduler,
                    SchedulerWords);
                Request(batch, BufferKind.Work, workCounts, WorkWords);
                Request(batch, BufferKind.Transactions, transactions,
                    TransactionWords * TransactionCount);
                Request(batch, BufferKind.Topology, topologyCounters,
                    TopologyWords);
                Request(batch, BufferKind.DrawArguments, drawArguments,
                    DrawArgumentWords);
                Request(batch, BufferKind.CurrentPageSlots, currentPageSlots,
                    Math.Min(readoutPageCapacity, currentPageSlots.count));
                if (batch.SampledReadoutPageSlot != InvalidPageSlot)
                {
                    int byteOffset = checked((int)batch.SampledReadoutPageSlot *
                        SigmaRenderer.ReadoutSamplesPerPage * sizeof(float) * 4);
                    RequestRange(batch, BufferKind.ReadoutVertices,
                        readoutVertices, ReadoutVertexWords, byteOffset);
                }
            }
            catch (Exception exception)
            {
                batch.Cancelled = true;
                if (ReferenceEquals(_active, batch))
                    _active = null;
                Snapshot = SigmaRuntimeTelemetrySnapshot.RequestFailed(
                    exception.Message);
                Logger.Warning("Sigma telemetry request failed without " +
                    "fallback: " + exception.Message);
            }
        }

        private void Request(Batch batch, BufferKind kind,
            GraphicsBuffer buffer, int expectedWords)
        {
            AsyncGPUReadback.Request(buffer, request =>
                Complete(batch, kind, expectedWords, request));
        }

        private void RequestRange(Batch batch, BufferKind kind,
            GraphicsBuffer buffer, int expectedWords, int byteOffset)
        {
            int byteCount = checked(expectedWords * sizeof(uint));
            AsyncGPUReadback.Request(buffer, byteCount, byteOffset, request =>
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
            if (batch.Remaining != 0)
                return;

            _active = null;
            SelectNextReadoutPage(batch);
            Snapshot = SigmaRuntimeTelemetrySnapshot.From(batch);
            if (batch.ErrorMask != 0u)
                Logger.Warning(Snapshot.FormatLogLine());
            else
                Logger.Info(Snapshot.FormatLogLine());
        }

        private void SelectNextReadoutPage(Batch batch)
        {
            uint pageCount = SigmaRenderer.VerticesPerCarrierPage == 0
                ? 0u : Word(batch.DrawArguments, 0) /
                    (uint)SigmaRenderer.VerticesPerCarrierPage;
            int pageCountInt = pageCount > int.MaxValue
                ? int.MaxValue : (int)pageCount;
            int available = Math.Min(batch.CurrentPageSlots?.Length ?? 0,
                pageCountInt);
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
            Diagnostics = 1,
            Scheduler = 2,
            Work = 3,
            Transactions = 4,
            Topology = 5,
            DrawArguments = 6,
            CurrentPageSlots = 7,
            ReadoutVertices = 8
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
                Remaining = sampledReadoutPageSlot == InvalidPageSlot ? 8 : 9;
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
            internal uint[] Diagnostics { get; private set; }
            internal uint[] Scheduler { get; private set; }
            internal uint[] Work { get; private set; }
            internal uint[] Transactions { get; private set; }
            internal uint[] Topology { get; private set; }
            internal uint[] DrawArguments { get; private set; }
            internal uint[] CurrentPageSlots { get; private set; }
            internal uint[] ReadoutVertices { get; private set; }

            internal void Set(BufferKind kind, uint[] words)
            {
                switch (kind)
                {
                    case BufferKind.Gate:
                        Gate = words;
                        break;
                    case BufferKind.Diagnostics:
                        Diagnostics = words;
                        break;
                    case BufferKind.Scheduler:
                        Scheduler = words;
                        break;
                    case BufferKind.Work:
                        Work = words;
                        break;
                    case BufferKind.Transactions:
                        Transactions = words;
                        break;
                    case BufferKind.Topology:
                        Topology = words;
                        break;
                    case BufferKind.DrawArguments:
                        DrawArguments = words;
                        break;
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
            text.Append(LastMs.ToString("F2"))
                .Append('/').Append(AverageMs.ToString("F2"))
                .Append('/').Append(MaximumMs.ToString("F2"))
                .Append("ms#").Append(SampleCount);
        }
    }

    public readonly struct SigmaRuntimeTimingTelemetry
    {
        internal SigmaRuntimeTimingTelemetry(
            SigmaStageLatencyTelemetry ingress,
            SigmaStageLatencyTelemetry canonical,
            SigmaStageLatencyTelemetry derived,
            double cpuFrameMs, double gpuFrameMs, bool hasFrameTiming)
        {
            Ingress = ingress;
            Canonical = canonical;
            Derived = derived;
            CpuFrameMs = cpuFrameMs;
            GpuFrameMs = gpuFrameMs;
            HasFrameTiming = hasFrameTiming;
        }

        public SigmaStageLatencyTelemetry Ingress { get; }
        public SigmaStageLatencyTelemetry Canonical { get; }
        public SigmaStageLatencyTelemetry Derived { get; }
        public double CpuFrameMs { get; }
        public double GpuFrameMs { get; }
        public bool HasFrameTiming { get; }

        internal void AppendTo(StringBuilder text)
        {
            text.Append("wall.frame=");
            if (HasFrameTiming)
                text.Append(CpuFrameMs.ToString("F2")).Append('/')
                    .Append(GpuFrameMs.ToString("F2")).Append("ms");
            else
                text.Append("unavailable");
            text.Append(' ');
            Ingress.AppendTo(text, "wall.ingress");
            text.Append(' ');
            Canonical.AppendTo(text, "wall.canonical");
            text.Append(' ');
            Derived.AppendTo(text, "wall.derived");
        }
    }

    public readonly struct SigmaTransactionTelemetry
    {
        internal SigmaTransactionTelemetry(int slot, uint[] words, int offset)
        {
            Slot = slot;
            State = Word(words, offset);
            Generation = Word(words, offset + 1);
            IdentitySlot = Word(words, offset + 2);
            Flags = Word(words, offset + 3);
            TicketLow = Word(words, offset + 4);
            TicketHigh = Word(words, offset + 5);
            SourceHead = Word(words, offset + 12);
            SourceGeneration = Word(words, offset + 13);
            SourceCount = Word(words, offset + 14);
            SourceFlags = Word(words, offset + 15);
            PublicationRevision = Word(words, offset + 16);
            PublicationState = Word(words, offset + 17);
            PublicationPageCount = Word(words, offset + 18);
            Page0XLo = Word(words, offset + 20);
            Page0XHi = Word(words, offset + 21);
            Page0YLo = Word(words, offset + 22);
            Page0YHi = Word(words, offset + 23);
            Page0Source = Word(words, offset + 24);
            Page0Target = Word(words, offset + 25);
            Page0SourceGeneration = Word(words, offset + 26);
            Page0TargetGeneration = Word(words, offset + 27);
            ProgressSource = Word(words, offset + 68);
            ProgressBlock = Word(words, offset + 69);
            ProgressMicrotile = Word(words, offset + 70);
            ProgressPhase = Word(words, offset + 71);
            ExecutionSource = Word(words, offset + 72);
            ExecutionBlockMicrotile = Word(words, offset + 73);
            ExecutionPhase = Word(words, offset + 74);
            ExecutionFlags = Word(words, offset + 75);
            ScratchSegment = Word(words, offset + 76);
            ScratchGeneration = Word(words, offset + 77);
            ScratchOffset = Word(words, offset + 78);
            ScratchFlags = Word(words, offset + 79);
            TransitionEdge = Word(words, offset + 80);
            TransitionCell = Word(words, offset + 81);
            TransitionPhase = Word(words, offset + 82);
            TransitionFailure = Word(words, offset + 83);
        }

        public int Slot { get; }
        public uint State { get; }
        public uint Generation { get; }
        public uint IdentitySlot { get; }
        public uint Flags { get; }
        public uint TicketLow { get; }
        public uint TicketHigh { get; }
        public uint SourceHead { get; }
        public uint SourceGeneration { get; }
        public uint SourceCount { get; }
        public uint SourceFlags { get; }
        public uint PublicationRevision { get; }
        public uint PublicationState { get; }
        public uint PublicationPageCount { get; }
        public uint Page0XLo { get; }
        public uint Page0XHi { get; }
        public uint Page0YLo { get; }
        public uint Page0YHi { get; }
        public uint Page0Source { get; }
        public uint Page0Target { get; }
        public uint Page0SourceGeneration { get; }
        public uint Page0TargetGeneration { get; }
        public uint ProgressSource { get; }
        public uint ProgressBlock { get; }
        public uint ProgressMicrotile { get; }
        public uint ProgressPhase { get; }
        public uint ExecutionSource { get; }
        public uint ExecutionBlockMicrotile { get; }
        public uint ExecutionPhase { get; }
        public uint ExecutionFlags { get; }
        public uint ExecutionPhaseMask => ExecutionPhase &
            SigmaGeneratedStreaming.ExecutionPhaseAll;
        public uint ExecutionProposalMask => ExecutionFlags &
            SigmaGeneratedStreaming.ExecutionProposalMask;
        public uint ExecutionOutcomeMask => ExecutionFlags &
            SigmaGeneratedStreaming.ExecutionOutcomeMask;
        public bool ExecutionFaulted => (ExecutionFlags &
            SigmaGeneratedStreaming.ExecutionFault) != 0u;
        public uint ScratchSegment { get; }
        public uint ScratchGeneration { get; }
        public uint ScratchOffset { get; }
        public uint ScratchFlags { get; }
        public uint TransitionEdge { get; }
        public uint TransitionCell { get; }
        public uint TransitionPhase { get; }
        public uint TransitionFailure { get; }
        public bool Occupied => State != 0u;
        public string StateName => State switch
        {
            0u => "FREE",
            1u => "WAIT_DEP",
            2u => "EVALUATING",
            3u => "PROOF_PENDING",
            4u => "TRANSITION_PENDING",
            5u => "REVALIDATE_PENDING",
            6u => "PUBLISHABLE",
            7u => "PUBLISHED",
            8u => "DORMANT",
            9u => "FAILED",
            _ => "UNKNOWN_" + State
        };

        internal void AppendTo(StringBuilder builder)
        {
            builder.Append(Slot).Append(':').Append(StateName)
                .Append("/g").Append(Generation)
                .Append("/id").Append(IdentitySlot)
                .Append("/f=0x").Append(Flags.ToString("X"))
                .Append("/ticket=").Append(TicketHigh).Append(':')
                .Append(TicketLow)
                .Append("/src=").Append(SourceHead).Append(':')
                .Append(SourceGeneration).Append('+').Append(SourceCount)
                .Append("/pub=").Append(PublicationRevision).Append(':')
                .Append(PublicationState).Append('x')
                .Append(PublicationPageCount)
                .Append("/page0=").Append(Page0Source).Append(':')
                .Append(Page0SourceGeneration).Append("->")
                .Append(Page0Target).Append(':').Append(Page0TargetGeneration)
                .Append('@').Append(Page0XLo).Append(':').Append(Page0XHi)
                .Append(',').Append(Page0YLo).Append(':').Append(Page0YHi)
                .Append("/p=").Append(ProgressSource).Append(',')
                .Append(ProgressBlock).Append(',').Append(ProgressMicrotile)
                .Append(',').Append(ProgressPhase)
                .Append("/exec=").Append(ExecutionSource).Append(',')
                .Append(ExecutionBlockMicrotile)
                .Append(" phase=0x").Append(ExecutionPhaseMask.ToString("X"))
                .Append(" proposal=0x")
                .Append(ExecutionProposalMask.ToString("X"))
                .Append(" outcome=0x")
                .Append(ExecutionOutcomeMask.ToString("X"))
                .Append(" fault=").Append(ExecutionFaulted ? 1 : 0)
                .Append("/scratch=").Append(ScratchSegment).Append(':')
                .Append(ScratchGeneration).Append('+').Append(ScratchOffset)
                .Append("/tr=").Append(TransitionEdge).Append(',')
                .Append(TransitionCell).Append(',').Append(TransitionPhase)
                .Append(',').Append(TransitionFailure);
        }

        private static uint Word(uint[] words, int index) =>
            words != null && (uint)index < (uint)words.Length
                ? words[index] : 0u;
    }

    public readonly struct SigmaReadoutVertexTelemetry
    {
        internal SigmaReadoutVertexTelemetry(uint pageSlot, uint[] words)
        {
            PageSlot = pageSlot;
            SampleCount = words?.Length / 4 ?? 0;
            SupportedCount = 0;
            FiniteNonzeroCount = 0;
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
                float informationMass = BitsToFloat(words[offset + 3]);
                bool finite = IsFinite(x) && IsFinite(y) && IsFinite(z) &&
                    IsFinite(informationMass);
                if (!finite)
                {
                    InvalidCount++;
                    continue;
                }
                if (x != 0f || y != 0f || z != 0f || informationMass != 0f)
                    FiniteNonzeroCount++;
                if (informationMass <= 0f)
                    continue;

                SupportedCount++;
                MinInformationMass = Mathf.Min(MinInformationMass,
                    informationMass);
                MaxInformationMass = Mathf.Max(MaxInformationMass,
                    informationMass);
                MinPosition = Vector3.Min(MinPosition, new Vector3(x, y, z));
                MaxPosition = Vector3.Max(MaxPosition, new Vector3(x, y, z));
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
        public int FiniteNonzeroCount { get; }
        public int InvalidCount { get; }
        public float MinInformationMass { get; }
        public float MaxInformationMass { get; }
        public Vector3 MinPosition { get; }
        public Vector3 MaxPosition { get; }
        public bool HasSample => PageSlot != uint.MaxValue && SampleCount != 0;

        internal void AppendTo(StringBuilder text)
        {
            if (!HasSample)
            {
                text.Append("pending");
                return;
            }
            text.Append("page=").Append(PageSlot)
                .Append(" supported=").Append(SupportedCount).Append('/')
                .Append(SampleCount)
                .Append(" nonzero=").Append(FiniteNonzeroCount)
                .Append(" invalid=").Append(InvalidCount)
                .Append(" info=").Append(MinInformationMass)
                .Append("..").Append(MaxInformationMass)
                .Append(" aabb=").Append(MinPosition)
                .Append("..").Append(MaxPosition);
        }

        private static float BitsToFloat(uint bits) =>
            BitConverter.Int32BitsToSingle(unchecked((int)bits));

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public sealed class SigmaRuntimeTelemetrySnapshot
    {
        private static readonly string[] OpcodeNames = {
            "NONE", "ADMIT", "EXTRACT", "EVAL", "REDUCE", "PROOF",
            "ANNIHILATOR", "ASSOCIATOR", "REVALIDATE", "PUBLISH",
            "TOPOLOGY", "READOUT", "DORMANT"
        };

        private SigmaRuntimeTelemetrySnapshot(bool hasSample, string status)
        {
            HasSample = hasSample;
            Status = status ?? string.Empty;
            WorkCounts = Array.Empty<uint>();
            Transactions = Array.Empty<SigmaTransactionTelemetry>();
            Diagnostics = Array.Empty<uint>();
            Scheduler = Array.Empty<uint>();
            Topology = Array.Empty<uint>();
            DrawArguments = Array.Empty<uint>();
            CurrentPageSlots = Array.Empty<uint>();
            ReadoutVertices = SigmaReadoutVertexTelemetryPending;
            Timing = default;
        }

        private SigmaRuntimeTelemetrySnapshot(
            SigmaDiagnosticTelemetry.Batch batch)
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
            Diagnostics = Copy(batch.Diagnostics, 32);
            Scheduler = Copy(batch.Scheduler, 32);
            WorkCounts = Copy(batch.Work,
                SigmaGeneratedStreaming.OpcodeCount);
            Topology = Copy(batch.Topology, 8);
            DrawArguments = Copy(batch.DrawArguments, 4);
            CurrentPageSlots = Copy(batch.CurrentPageSlots,
                batch.CurrentPageSlots?.Length ?? 0);
            ReadoutVertices = new SigmaReadoutVertexTelemetry(
                batch.SampledReadoutPageSlot, batch.ReadoutVertices);
            Timing = batch.Timing;
            Transactions = new SigmaTransactionTelemetry[
                SigmaGeneratedStreaming.TransactionCapacity];
            for (int slot = 0; slot < Transactions.Length; ++slot)
                Transactions[slot] = new SigmaTransactionTelemetry(slot,
                    batch.Transactions, slot *
                    (SigmaGeneratedStreaming.TransactionStride / sizeof(uint)));
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
        public uint[] Diagnostics { get; }
        public uint[] Scheduler { get; }
        public uint[] WorkCounts { get; }
        public SigmaTransactionTelemetry[] Transactions { get; }
        public uint[] Topology { get; }
        public uint[] DrawArguments { get; }
        public uint[] CurrentPageSlots { get; }
        public SigmaReadoutVertexTelemetry ReadoutVertices { get; }
        public SigmaRuntimeTimingTelemetry Timing { get; }

        public uint BundlesReady => Word(Diagnostics, 0);
        public uint DiagnosticProbation => Word(Diagnostics, 1);
        public uint DiagnosticActiveTransactions => Word(Diagnostics, 4);
        public uint DiagnosticDormantTransactions => Word(Diagnostics, 5);
        public uint DiagnosticPublishedTransactions => Word(Diagnostics, 6);
        public uint DiagnosticOccupiedMask => Word(Diagnostics, 7);
        public uint ProofBlocksReduced => Word(Diagnostics, 8);
        public uint ProofClosures => Word(Diagnostics, 9);
        public uint TransitionEdges => Word(Diagnostics, 12);
        public uint TransitionAnnihilators => Word(Diagnostics, 13);
        public uint TransitionAssociators => Word(Diagnostics, 14);
        public uint TransitionUnresolved => Word(Diagnostics, 15);
        public uint ManifestPublications => Word(Diagnostics, 16);
        public uint RevalidationsCompleted => Word(Diagnostics, 21);
        public uint DormantEvents => Word(Diagnostics, 22);
        public uint LifetimeFailures => Word(Diagnostics, 23);
        public uint PhaseIncompleteFaults => Word(Diagnostics, 24);
        public uint OwnerMismatchFaults => Word(Diagnostics, 25);
        public uint AcceptedFinals => Word(Diagnostics, 26);
        public uint ZeroAcceptedClosures => Word(Diagnostics, 27);
        public uint PublicationGateRejects => Word(Diagnostics, 28);
        public uint IngressResidentExhaustions => Word(Diagnostics, 29);
        public uint NovelNullPageRejects => Word(Diagnostics, 30);

        public uint IngressCursor => Word(Scheduler, 3);
        public uint IngressCount => Word(Scheduler, 4);
        public uint SkippedAdmissions => Word(Scheduler, 5);
        public uint SchedulerPublications => Word(Scheduler, 6);
        public uint SchedulerDormant => Word(Scheduler, 7);
        public uint SchedulerActive => Word(Scheduler, 8);
        public uint RevalidationOwner => Word(Scheduler, 9);
        public uint SchedulerProbation => Word(Scheduler, 10);
        public uint SchedulerFailures => Word(Scheduler, 11);
        public uint ProofOwner => Word(Scheduler, 22);
        public uint ProofSpillCount => Word(Scheduler, 24);

        public uint TopologyEvaluated => Word(Topology, 0);
        public uint TopologyAnnihilatorEvaluations => Word(Topology, 1);
        public uint TopologyAssociatorEvaluations => Word(Topology, 2);
        public uint TopologySingular => Word(Topology, 3);
        public uint TopologyUnresolved => Word(Topology, 4);
        public uint TopologyReused => Word(Topology, 5);
        public uint TopologyOverflow => Word(Topology, 6);

        public uint DrawVertexCount => Word(DrawArguments, 0);
        public uint DrawInstanceCount => Word(DrawArguments, 1);
        public uint ReadoutPageCount => SigmaRenderer.VerticesPerCarrierPage == 0
            ? 0u : DrawVertexCount / (uint)SigmaRenderer.VerticesPerCarrierPage;

        public ulong ScheduledTokenLoad => WeightedLoad(
            SigmaGeneratedStreaming.KernelTokenCost);
        public ulong ScheduledBytesRead => WeightedLoad(
            SigmaGeneratedStreaming.KernelBytesRead);
        public ulong ScheduledBytesWritten => WeightedLoad(
            SigmaGeneratedStreaming.KernelBytesWritten);
        public ulong ScheduledBarrierLoad => WeightedLoad(
            SigmaGeneratedStreaming.KernelBarrierCount);
        public ulong ScheduledWitnessLoad => WeightedLoad(
            SigmaGeneratedStreaming.KernelWitnessCount);

        public string Frontier
        {
            get
            {
                if (!HasSample)
                    return Status;
                if (ErrorMask != 0u)
                    return Status;
                if (GateWord == 0u)
                    return "exact-backend-gate";
                if (CommittedFrames == 0)
                    return "host-ingress-fence";
                if (BundlesReady == 0u)
                    return "bundle-extraction";
                if (SchedulerProbation == 0u && SchedulerActive == 0u &&
                    SchedulerPublications == 0u)
                    return "admission/probation";
                if (SchedulerActive != 0u && ProofBlocksReduced == 0u)
                    return "inverse/proof-progress";
                if (ProofBlocksReduced != 0u && ProofClosures == 0u)
                    return "proof-closure";
                if (ProofClosures != 0u && SchedulerPublications == 0u)
                    return "transition/publication";
                if (SchedulerPublications != 0u && DrawVertexCount == 0u)
                    return "derived-readout";
                if (DrawVertexCount != 0u && ReadoutVertices.HasSample &&
                    ReadoutVertices.SupportedCount == 0)
                    return "geometry-readout-plan";
                return DrawVertexCount == 0u ? "undetermined-before-readout" :
                    ReadoutVertices.HasSample ? "xr-preview-draw" :
                    "readout-vertex-sample";
            }
        }

        public string FormatLogLine()
        {
            var text = new StringBuilder(1400);
            text.Append("Sigma telemetry #").Append(Sequence)
                .Append(" status=").Append(Status)
                .Append(" frontier=").Append(Frontier)
                .Append(" gate=").Append(GateWord)
                .Append(" host=").Append(CommittedFrames).Append('/')
                .Append(SubmittedFrames).Append(" rev=").Append(HostRevision)
                .Append(" diag.admission=");
            AppendRange(text, Diagnostics, 0, 4);
            text.Append(" diag.tx=");
            AppendRange(text, Diagnostics, 4, 4);
            text.Append(" diag.proof=");
            AppendRange(text, Diagnostics, 8, 4);
            text.Append(" diag.transition=");
            AppendRange(text, Diagnostics, 12, 4);
            text.Append(" diag.publication=");
            AppendRange(text, Diagnostics, 16, 4);
            text.Append(" diag.lifetime=");
            AppendRange(text, Diagnostics, 20, 4);
            text.Append(" diag.memory=");
            AppendRange(text, Diagnostics, 24, 4);
            text.Append(" diag.reserved=");
            AppendRange(text, Diagnostics, 28, 4);
            text.Append(" scheduler=");
            AppendRange(text, Scheduler, 0, Scheduler.Length);
            text.Append(" work={");
            for (int index = 0; index < WorkCounts.Length; ++index)
            {
                if (index != 0)
                    text.Append(',');
                text.Append(index < OpcodeNames.Length ? OpcodeNames[index] :
                    index.ToString()).Append(':').Append(WorkCounts[index]);
            }
            text.Append("} last-round-load={tokens=")
                .Append(ScheduledTokenLoad)
                .Append(" read=").Append(ScheduledBytesRead)
                .Append("B write=").Append(ScheduledBytesWritten)
                .Append("B barriers=").Append(ScheduledBarrierLoad)
                .Append(" witnesses=").Append(ScheduledWitnessLoad)
                .Append("} topology=");
            AppendRange(text, Topology, 0, Topology.Length);
            text.Append(" draw=");
            AppendRange(text, DrawArguments, 0, DrawArguments.Length);
            text.Append(" vertices={");
            ReadoutVertices.AppendTo(text);
            text.Append("} timing={");
            Timing.AppendTo(text);
            text.Append('}');
            text.Append(" slots={");
            bool first = true;
            for (int index = 0; index < Transactions.Length; ++index)
            {
                if (!Transactions[index].Occupied)
                    continue;
                if (!first)
                    text.Append(" | ");
                Transactions[index].AppendTo(text);
                first = false;
            }
            if (first)
                text.Append("none");
            return text.Append('}').ToString();
        }

        private static uint[] Copy(uint[] source, int count)
        {
            var result = new uint[count];
            if (source != null)
                Array.Copy(source, result, Math.Min(source.Length, count));
            return result;
        }

        private ulong WeightedLoad(uint[] unitCost)
        {
            int count = Math.Min(WorkCounts?.Length ?? 0,
                unitCost?.Length ?? 0);
            ulong total = 0;
            for (int index = 0; index < count; ++index)
                total += (ulong)WorkCounts[index] * unitCost[index];
            return total;
        }

        private static uint Word(uint[] words, int index) =>
            words != null && (uint)index < (uint)words.Length
                ? words[index] : 0u;

        private static SigmaReadoutVertexTelemetry
            SigmaReadoutVertexTelemetryPending =>
                new(uint.MaxValue, Array.Empty<uint>());

        private static void AppendRange(StringBuilder text, uint[] values,
            int start, int count)
        {
            text.Append('[');
            int end = Math.Min(values?.Length ?? 0, start + count);
            for (int index = start; index < end; ++index)
            {
                if (index != start)
                    text.Append(',');
                text.Append(values[index]);
            }
            text.Append(']');
        }
    }
}
