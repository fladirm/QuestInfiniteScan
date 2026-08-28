using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan
{
    internal enum MerkabaGpuStage : byte
    {
        DepthPreprocess,
        SurfaceIntegration,
        CarveIntegration,
        TopologyUpdate,
        PublicationCompaction,
        MerkabaDraw,
        Count
    }

    /// <summary>
    /// Periodic one-frame Vulkan query timestamps. Normal frames execute no timing
    /// events or diagnostic readbacks; a sampled frame emits fixed stage spans only.
    /// </summary>
    internal static class MerkabaGpuTimestamps
    {
        private const int MaximumSpans = 64;
        private const float InitialSampleDelaySeconds = 2f;
        private const float SampleIntervalSeconds = 5f;
        private const float UnavailableRetrySeconds = 30f;

        private enum CaptureState : byte
        {
            Idle,
            Recording,
            AwaitingResults
        }

        private readonly struct Span
        {
            internal readonly MerkabaGpuStage Stage;
            internal readonly bool Graphics;

            internal Span(MerkabaGpuStage stage, bool graphics)
            {
                Stage = stage;
                Graphics = graphics;
            }
        }

        private sealed class SampleMetrics
        {
            internal uint Revision;
            internal int PendingReadbacks;
            internal bool TimingComplete;
            internal bool ReadbackValid = true;
            internal bool Logged;
            internal int IntegrationChunks;
            internal int DepthSamples;
            internal uint SurfaceCandidates;
            internal uint CarveCandidates;
            internal int ResidentChunks;
            internal int VisibleChunks;
            internal int CpuDirtyChunks;
            internal uint GpuDirtyChunks;
            internal ulong PublishedPrimitives;
        }

        private static readonly string[] StageNames =
        {
            "DEPTH_PREPROCESS",
            "SURFACE_INTEGRATION",
            "CARVE_INTEGRATION",
            "TOPOLOGY_UPDATE",
            "PUBLICATION_COMPACTION",
            "MERKABA_DRAW"
        };
        private static readonly List<Span> Spans = new(MaximumSpans);
        private static readonly ulong[] TimestampPairs =
            new ulong[MaximumSpans * 2];
        private static readonly int[] EventIds = new int[Native.EventCount];

        private static CaptureState _state;
        private static Span? _openSpan;
        private static uint _revision;
        private static float _nextSampleTime;
        private static IntPtr _renderEvent;
#if !UNITY_EDITOR
        private static bool _unavailableWarningLogged;
#endif
        private static SampleMetrics _metrics;
#if UNITY_EDITOR
        private static bool _testAvailable;
#endif

        internal static bool IsRecording => _state == CaptureState.Recording;
        internal static uint CurrentRevision => _revision;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Spans.Clear();
            _state = CaptureState.Idle;
            _openSpan = null;
            _revision = 0u;
            _nextSampleTime = float.PositiveInfinity;
            _renderEvent = IntPtr.Zero;
#if !UNITY_EDITOR
            _unavailableWarningLogged = false;
#endif
            _metrics = null;
#if UNITY_EDITOR
            _testAvailable = false;
#endif
        }

        internal static void NotifyScanStarted()
        {
            if (_state == CaptureState.Idle)
                _nextSampleTime = Time.unscaledTime + InitialSampleDelaySeconds;
        }

        internal static bool TryBeginFrame(uint revision)
        {
            Poll();
            if (_state != CaptureState.Idle || revision == 0u)
                return false;
#if UNITY_EDITOR
            if (!_testAvailable)
                return false;
#else
            if (Time.unscaledTime < _nextSampleTime)
                return false;
            if (!Native.TryArm(revision, out _renderEvent, EventIds))
            {
                if (!_unavailableWarningLogged)
                {
                    _unavailableWarningLogged = true;
                    Logger.Warning("Merkaba GPU timestamps unavailable; Vulkan " +
                                   "query plugin was not loaded.");
                }
                _nextSampleTime = Time.unscaledTime + UnavailableRetrySeconds;
                return false;
            }
#endif
            Spans.Clear();
            _openSpan = null;
            _revision = revision;
            _metrics = new SampleMetrics { Revision = revision };
            _state = CaptureState.Recording;
            Issue(Native.SubmissionBegin);
            return true;
        }

        internal static void BeginCompute(MerkabaGpuStage stage) =>
            Begin(stage, false);

        internal static void EndCompute(MerkabaGpuStage stage) =>
            End(stage, false);

        internal static void BeginGraphics(MerkabaGpuStage stage) =>
            Begin(stage, true);

        internal static void EndGraphics(MerkabaGpuStage stage) =>
            End(stage, true);

        internal static void EndFrame()
        {
            if (_state != CaptureState.Recording)
                return;
            if (_openSpan.HasValue)
            {
                Span open = _openSpan.Value;
                End(open.Stage, open.Graphics);
            }
            Issue(Native.SubmissionEnd);
            _state = CaptureState.AwaitingResults;
#if !UNITY_EDITOR
            _nextSampleTime = Time.unscaledTime + SampleIntervalSeconds;
#endif
        }

        internal static void Poll()
        {
            if (_state != CaptureState.AwaitingResults)
                return;
#if UNITY_EDITOR
            return;
#else
            int status = Native.TryRead(TimestampPairs, out int spanCount,
                out double timestampPeriod, out int validBits,
                out ulong capturedRevision, out bool overflow);
            if (status == 0)
                return;
            bool valid = status > 0 && !overflow &&
                         capturedRevision == _revision &&
                         spanCount == Spans.Count;
            if (valid)
                LogTimings(spanCount, timestampPeriod, validBits);
            else
                Logger.Warning($"Merkaba GPU timestamp sample invalid " +
                               $"revision={_revision} nativeRevision=" +
                               $"{capturedRevision} expectedSpans={Spans.Count} " +
                               $"actualSpans={spanCount} overflow={overflow}");
            if (_metrics != null)
            {
                _metrics.TimingComplete = true;
                _metrics.ReadbackValid &= valid;
                TryLogMetrics(_metrics);
            }
            Spans.Clear();
            _openSpan = null;
            _state = CaptureState.Idle;
            _revision = 0u;
#endif
        }

        internal static void CaptureIntegrationMetrics(ComputeBuffer surfaceCount,
            ComputeBuffer carveCount, int integrationChunks, int depthWidth,
            int depthHeight)
        {
            SampleMetrics sample = RecordingMetrics();
            if (sample == null)
                return;
            sample.IntegrationChunks = integrationChunks;
            sample.DepthSamples = checked(depthWidth * depthHeight * 2);
            RequestCounter(surfaceCount, sample,
                value => sample.SurfaceCandidates = value);
            RequestCounter(carveCount, sample,
                value => sample.CarveCandidates = value);
        }

        internal static void CaptureRenderMetrics(MerkabaGrid grid,
            int cpuDirtyChunks, bool gpuDirtySubmitted)
        {
            SampleMetrics sample = RecordingMetrics();
            if (sample == null || grid == null)
                return;
            sample.ResidentChunks = grid.ResidentPageCount;
            sample.VisibleChunks = grid.VisibleChunkCount;
            sample.CpuDirtyChunks = cpuDirtyChunks;
            if (gpuDirtySubmitted)
                RequestCounter(grid.GpuPublicationDispatchArgsBuffer, sample,
                    value => sample.GpuDirtyChunks = value);

            int[] visibleSlots = new int[grid.VisibleChunkCount];
            for (int index = 0; index < visibleSlots.Length; index++)
                visibleSlots[index] = grid.VisibleSlotAt(index);
            sample.PendingReadbacks++;
            AsyncGPUReadback.Request(grid.PrimitiveCountBuffer, request =>
            {
                if (request.hasError)
                    sample.ReadbackValid = false;
                else
                {
                    var counts = request.GetData<uint>();
                    ulong total = 0;
                    foreach (int slot in visibleSlots)
                        total += counts[slot];
                    sample.PublishedPrimitives = total;
                }
                sample.PendingReadbacks--;
                TryLogMetrics(sample);
            });
        }

        internal static double ElapsedNanoseconds(ulong begin, ulong end,
            double timestampPeriod, int validBits)
        {
            if (timestampPeriod <= 0.0 || validBits <= 0 || validBits > 64)
                throw new ArgumentOutOfRangeException();
            ulong mask = validBits == 64
                ? ulong.MaxValue : (1UL << validBits) - 1UL;
            return ((end & mask) - (begin & mask) & mask) * timestampPeriod;
        }

        private static void Begin(MerkabaGpuStage stage, bool graphics)
        {
            if (_state != CaptureState.Recording)
                return;
            if ((uint)stage >= (uint)MerkabaGpuStage.Count ||
                _openSpan.HasValue || Spans.Count >= MaximumSpans)
                throw new InvalidOperationException(
                    "Merkaba GPU timestamp span sequence is invalid.");
            Issue(graphics ? Native.GraphicsBegin : Native.ComputeBegin);
            _openSpan = new Span(stage, graphics);
        }

        private static void End(MerkabaGpuStage stage, bool graphics)
        {
            if (_state != CaptureState.Recording)
                return;
            if (!_openSpan.HasValue || _openSpan.Value.Stage != stage ||
                _openSpan.Value.Graphics != graphics)
                throw new InvalidOperationException(
                    "Merkaba GPU timestamp span pairing is invalid.");
            Issue(graphics ? Native.GraphicsEnd : Native.ComputeEnd);
            Spans.Add(_openSpan.Value);
            _openSpan = null;
        }

        private static void Issue(int offset)
        {
#if !UNITY_EDITOR && UNITY_ANDROID
            GL.IssuePluginEvent(_renderEvent, EventIds[offset]);
#endif
        }

        private static void LogTimings(int spanCount, double timestampPeriod,
            int validBits)
        {
            int stageCount = (int)MerkabaGpuStage.Count;
            var totals = new double[stageCount];
            var maxima = new double[stageCount];
            var counts = new int[stageCount];
            double checksum = 0.0;
            for (int index = 0; index < spanCount; index++)
            {
                double nanoseconds = ElapsedNanoseconds(
                    TimestampPairs[index * 2], TimestampPairs[index * 2 + 1],
                    timestampPeriod, validBits);
                int stage = (int)Spans[index].Stage;
                totals[stage] += nanoseconds;
                maxima[stage] = Math.Max(maxima[stage], nanoseconds);
                counts[stage]++;
                checksum += nanoseconds;
            }
            for (int stage = 0; stage < stageCount; stage++)
                Logger.Info($"Merkaba GPU timestamp revision={_revision} " +
                            $"stage={StageNames[stage]} spans={counts[stage]} " +
                            $"total={totals[stage] / 1_000_000.0:F3}ms " +
                            $"maximum={maxima[stage] / 1_000_000.0:F3}ms");
            Logger.Info($"Merkaba GPU timestamp revision={_revision} " +
                        $"stage-checksum={checksum / 1_000_000.0:F3}ms " +
                        $"spans={spanCount} timestampPeriod=" +
                        $"{timestampPeriod:F6}ns validBits={validBits}");
        }

        private static SampleMetrics RecordingMetrics() =>
            _state == CaptureState.Recording ? _metrics : null;

        private static void RequestCounter(ComputeBuffer buffer,
            SampleMetrics sample, Action<uint> assign)
        {
            if (buffer == null)
            {
                sample.ReadbackValid = false;
                return;
            }
            sample.PendingReadbacks++;
            AsyncGPUReadback.Request(buffer, sizeof(uint), 0, request =>
            {
                if (request.hasError || request.GetData<uint>().Length == 0)
                    sample.ReadbackValid = false;
                else
                    assign(request.GetData<uint>()[0]);
                sample.PendingReadbacks--;
                TryLogMetrics(sample);
            });
        }

        private static void TryLogMetrics(SampleMetrics sample)
        {
            if (sample == null || sample.Logged || !sample.TimingComplete ||
                sample.PendingReadbacks != 0)
                return;
            sample.Logged = true;
            Logger.Info($"Merkaba GPU metrics revision={sample.Revision} " +
                        $"valid={sample.ReadbackValid} " +
                        $"depthSamples={sample.DepthSamples} " +
                        $"surfaceCandidates={sample.SurfaceCandidates} " +
                        $"carveCandidates={sample.CarveCandidates} " +
                        $"integrationChunks={sample.IntegrationChunks} " +
                        $"residentChunks={sample.ResidentChunks} " +
                        $"visibleChunks={sample.VisibleChunks} " +
                        $"topologyDirtyChunks=" +
                        $"{sample.CpuDirtyChunks + sample.GpuDirtyChunks} " +
                        $"publishedPrimitives={sample.PublishedPrimitives}");
        }

        private static class Native
        {
            internal const int SubmissionBegin = 0;
            internal const int ComputeBegin = 1;
            internal const int ComputeEnd = 2;
            internal const int GraphicsBegin = 3;
            internal const int GraphicsEnd = 4;
            internal const int SubmissionEnd = 5;
            internal const int EventCount = 6;
#if !UNITY_EDITOR && UNITY_ANDROID
            private const string Library = "MerkabaVulkanTimestamps";

            [DllImport(Library, EntryPoint = "MerkabaTimestamp_IsAvailable")]
            private static extern int IsAvailableNative();
            [DllImport(Library, EntryPoint = "MerkabaTimestamp_Arm")]
            private static extern int ArmNative(ulong revision);
            [DllImport(Library, EntryPoint = "MerkabaTimestamp_Cancel")]
            private static extern void CancelNative();
            [DllImport(Library, EntryPoint = "MerkabaTimestamp_GetRenderEventFunc")]
            private static extern IntPtr GetRenderEventFuncNative();
            [DllImport(Library, EntryPoint = "MerkabaTimestamp_GetEventId")]
            private static extern int GetEventIdNative(int offset);
            [DllImport(Library, EntryPoint = "MerkabaTimestamp_Read")]
            private static extern int ReadNative([Out] ulong[] timestamps,
                int timestampCapacity, out int spanCount,
                out double timestampPeriod, out int validBits,
                out ulong revision, out int overflow);

            internal static bool TryArm(uint revision, out IntPtr renderEvent,
                int[] eventIds)
            {
                renderEvent = IntPtr.Zero;
                try
                {
                    if (IsAvailableNative() == 0 || ArmNative(revision) == 0)
                        return false;
                    renderEvent = GetRenderEventFuncNative();
                    if (renderEvent == IntPtr.Zero)
                    {
                        CancelNative();
                        return false;
                    }
                    for (int index = 0; index < EventCount; index++)
                        eventIds[index] = GetEventIdNative(index);
                    return true;
                }
                catch (DllNotFoundException) { return false; }
                catch (EntryPointNotFoundException) { return false; }
            }

            internal static int TryRead(ulong[] timestamps,
                out int spanCount, out double timestampPeriod,
                out int validBits, out ulong revision, out bool overflow)
            {
                try
                {
                    int result = ReadNative(timestamps, timestamps.Length,
                        out spanCount, out timestampPeriod, out validBits,
                        out revision, out int overflowValue);
                    overflow = overflowValue != 0;
                    return result;
                }
                catch (DllNotFoundException)
                {
                    spanCount = validBits = 0;
                    timestampPeriod = 0.0;
                    revision = 0;
                    overflow = false;
                    return -1;
                }
                catch (EntryPointNotFoundException)
                {
                    spanCount = validBits = 0;
                    timestampPeriod = 0.0;
                    revision = 0;
                    overflow = false;
                    return -1;
                }
            }
#else
            internal static bool TryArm(uint revision, out IntPtr renderEvent,
                int[] eventIds)
            {
                renderEvent = IntPtr.Zero;
                return false;
            }

            internal static int TryRead(ulong[] timestamps,
                out int spanCount, out double timestampPeriod,
                out int validBits, out ulong revision, out bool overflow)
            {
                spanCount = validBits = 0;
                timestampPeriod = 0.0;
                revision = 0;
                overflow = false;
                return -1;
            }
#endif
        }

#if UNITY_EDITOR
        internal static void SetAvailableForTests(bool available)
        {
            Reset();
            _testAvailable = available;
        }

        internal static MerkabaGpuStage[] RecordedStagesForTests()
        {
            var result = new MerkabaGpuStage[Spans.Count];
            for (int index = 0; index < result.Length; index++)
                result[index] = Spans[index].Stage;
            return result;
        }
#endif
    }
}
