using System;
using System.Text;

namespace Genesis.RoomScan.SigmaPrism
{
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
            Frame.AppendTo(text, "wall.native-close");
        }
    }

    /// <summary>
    /// Host-visible terminal receipt for one immutable NativeCloseCommit. This is
    /// diagnostic state only; it neither mirrors nor owns canonical field state.
    /// Per-kernel Vulkan timing and dispatch cardinality remain owned by
    /// SigmaGpuKernelTelemetry.
    /// </summary>
    public sealed class SigmaRuntimeTelemetrySnapshot
    {
        private SigmaRuntimeTelemetrySnapshot(bool hasSample, string status)
        {
            HasSample = hasSample;
            Status = status ?? string.Empty;
        }

        private SigmaRuntimeTelemetrySnapshot(uint revision, uint publishedRoot,
            SigmaFrameCompletionDisposition disposition,
            SigmaNativeFrameGpu frame, SigmaRuntimeTimingTelemetry timing)
        {
            HasSample = true;
            Revision = revision;
            PublishedRoot = publishedRoot;
            Disposition = disposition.ToString();
            Status = disposition switch
            {
                SigmaFrameCompletionDisposition.Published => "published",
                SigmaFrameCompletionDisposition.NoChange => "no-change",
                SigmaFrameCompletionDisposition.Unresolved => "unresolved",
                _ => "faulted"
            };
            GateWord = disposition == SigmaFrameCompletionDisposition.Faulted
                ? 0u : 1u;
            StateDeltaCount = disposition ==
                SigmaFrameCompletionDisposition.Published ? 1u : 0u;
            GaugeDeltaCount = frame.Disposition.Z;
            UnresolvedConstraintCount = disposition ==
                SigmaFrameCompletionDisposition.Unresolved ? 1u : 0u;
            FaultMask = frame.Disposition.W;
            NativeCloseDispatches = SigmaNativeFrameGraph.HotDispatchCount;
            Timing = timing;
        }

        public static SigmaRuntimeTelemetrySnapshot Awaiting { get; } =
            new(false, "awaiting-first-terminal-native-frame");

        internal static SigmaRuntimeTelemetrySnapshot From(uint revision,
            uint publishedRoot, SigmaFrameCompletionDisposition disposition,
            SigmaNativeFrameGpu frame, SigmaRuntimeTimingTelemetry timing) =>
            new(revision, publishedRoot, disposition, frame, timing);

        public bool HasSample { get; }
        public string Status { get; }
        public string Disposition { get; }
        public uint Revision { get; }
        public uint PublishedRoot { get; }
        public uint GateWord { get; }
        public uint StateDeltaCount { get; }
        public uint GaugeDeltaCount { get; }
        public uint UnresolvedConstraintCount { get; }
        public uint FaultMask { get; }
        public int NativeCloseDispatches { get; }
        public SigmaRuntimeTimingTelemetry Timing { get; }

        public string Frontier => !HasSample ? Status :
            FaultMask != 0u ? "native-close-fault" :
            UnresolvedConstraintCount != 0u ? "unresolved-native-constraint" :
            StateDeltaCount != 0u ? "root-last-publication" :
            "exact-native-no-change";

        public string FormatLogLine()
        {
            var text = new StringBuilder(320);
            text.Append("Sigma native receipt revision=").Append(Revision)
                .Append(" status=").Append(Status)
                .Append(" root=").Append(PublishedRoot)
                .Append(" stateDeltas=").Append(StateDeltaCount)
                .Append(" gaugeDeltas=").Append(GaugeDeltaCount)
                .Append(" unresolved=").Append(UnresolvedConstraintCount)
                .Append(" fault=0x").Append(FaultMask.ToString("X8"))
                .Append(" nativeCloseDispatches=")
                .Append(NativeCloseDispatches).Append(' ');
            Timing.AppendTo(text);
            return text.ToString();
        }
    }
}
