using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.SigmaPrism
{
    /// <summary>
    /// One-shot Vulkan timestamps for an otherwise unmodified Release submission.
    /// Explicit plugin events place query pairs around each recorded dispatch and
    /// return the complete pool after the submission fence.
    /// </summary>
    internal static class SigmaGpuKernelTelemetry
    {
        internal const int MaximumThreadGroupsPerDimension = 65535;
        private const int MaximumTimedDispatches = 4096;

        private enum State : byte
        {
            Idle,
            Armed,
            Recording,
            Submitted,
            AwaitingResults,
        }

        private sealed class Entry
        {
            internal Entry(string name) => Name = name;
            internal string Name { get; }
        }

        private sealed class Aggregate
        {
            internal int Dispatches;
            internal double TotalNanoseconds;
            internal double MaximumNanoseconds;
        }

        private sealed class SeriesAggregate
        {
            internal readonly List<double> TotalNanoseconds = new();
        }

        private static readonly Dictionary<(ulong Entity, int Kernel), Entry>
            EntriesByKernel = new();
        private static readonly List<Entry> DispatchSequence = new();
        private static readonly ulong[] TimestampPairs =
            new ulong[MaximumTimedDispatches * 2];
        private static State _state;
        private static uint _revision;
        private static bool _warningLogged;
        private static int _seriesRemaining;
        private static int _seriesWarmupRemaining;
        private static int _seriesRequestedSamples;
        private static readonly Dictionary<string, SeriesAggregate>
            SeriesByKernel = new(StringComparer.Ordinal);
        private static readonly List<double> SeriesSubmissionNanoseconds = new();
#if UNITY_EDITOR
        private static bool _testTimingAvailable;
        internal static Action<ulong, int, int, int, int>
            DirectDispatchObservedForTests;
        internal static Action<ulong, int, int, int, int>
            ProfiledDispatchObservedForTests;
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            EntriesByKernel.Clear();
            DispatchSequence.Clear();
            _state = State.Idle;
            _revision = 0u;
            _warningLogged = false;
            ResetSeries();
#if UNITY_EDITOR
            _testTimingAvailable = false;
            DirectDispatchObservedForTests = null;
            ProfiledDispatchObservedForTests = null;
#endif
        }

        internal static int FindProfiledKernel(this ComputeShader shader,
            string kernelName)
        {
            if (shader == null)
                throw new ArgumentNullException(nameof(shader));
            if (string.IsNullOrWhiteSpace(kernelName))
                throw new ArgumentException("Kernel name is required.",
                    nameof(kernelName));
            int kernel = shader.FindKernel(kernelName);
            var key = Key(shader, kernel);
            string name = shader.name + '.' + kernelName;
            if (EntriesByKernel.TryGetValue(key, out Entry existing))
            {
                if (existing.Name != name)
                    throw new InvalidOperationException(
                        $"Compute timing collision: {existing.Name} versus " +
                        $"{name}.");
            }
            else
                EntriesByKernel.Add(key, new Entry(name));
            return kernel;
        }

        internal static bool RequestSingleSubmission()
        {
            return RequestSubmissionSeries(1, 0);
        }

        internal static bool RequestSubmissionSeries(int sampleCount,
            int warmupCount)
        {
            if (_state != State.Idle)
                return false;
            if (sampleCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(sampleCount));
            if (warmupCount < 0)
                throw new ArgumentOutOfRangeException(nameof(warmupCount));
            ResetSeries();
            _seriesRequestedSamples = sampleCount;
            _seriesWarmupRemaining = warmupCount;
            _seriesRemaining = checked(sampleCount + warmupCount);
            _state = State.Armed;
            return true;
        }

        internal static bool BeginProfiledSubmission(uint revision)
        {
            if (_state != State.Armed || revision == 0u)
                return false;
            DispatchSequence.Clear();
#if UNITY_EDITOR
            if (_testTimingAvailable)
            {
                _revision = revision;
                _state = State.Recording;
                return true;
            }
#endif
            if (!Native.TryArm(revision))
            {
                Warn("Release Vulkan timestamp query pool is unavailable");
                _state = State.Idle;
                ResetSeries();
                return false;
            }
            _revision = revision;
            _state = State.Recording;
            return true;
        }

        internal static void RecordProfileBegin(CommandBuffer command)
        {
            if (_state != State.Recording)
                return;
            if (command == null)
                throw new ArgumentNullException(nameof(command));
#if !UNITY_EDITOR && UNITY_ANDROID
            command.IssuePluginEvent(Native.RenderEvent,
                Native.EventId(Native.SubmissionBegin));
#endif
        }

        internal static void RecordProfileEnd(CommandBuffer command)
        {
            if (_state != State.Recording)
                return;
            if (command == null)
                throw new ArgumentNullException(nameof(command));
#if !UNITY_EDITOR && UNITY_ANDROID
            command.IssuePluginEvent(Native.RenderEvent,
                Native.EventId(Native.SubmissionEnd));
#endif
        }

        internal static void EndProfiledSubmission(uint revision,
            bool submitted)
        {
            if (_state != State.Recording || revision != _revision)
                return;
            if (submitted)
            {
                _state = State.Submitted;
                return;
            }
            CancelNative();
            _revision = 0u;
            _state = State.Armed;
        }

        internal static void CompleteProfiledSubmission(uint revision)
        {
            if (_state != State.Submitted || revision != _revision)
                return;
            _state = State.AwaitingResults;
        }

        internal static void CancelSingleSubmission()
        {
            CancelNative();
            DispatchSequence.Clear();
            _state = State.Idle;
            _revision = 0u;
            ResetSeries();
        }

        internal static void DispatchComputeProfiled(this CommandBuffer command,
            ComputeShader shader, int kernel, int x, int y, int z)
        {
            DispatchComputeProfiled(command, shader, kernel, null, x, y, z);
        }

        internal static void DispatchComputeProfiled(this CommandBuffer command,
            ComputeShader shader, int kernel, string profileName,
            int x, int y, int z)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));
            ValidateDirectDispatchDimensions(x, y, z);
            bool timed = Observe(shader, kernel, profileName, x, y, z);
            RecordDispatchEvent(command, timed, true);
            command.DispatchCompute(shader, kernel, x, y, z);
            RecordDispatchEvent(command, timed, false);
        }

        internal static void DispatchComputeProfiled(this CommandBuffer command,
            ComputeShader shader, int kernel, GraphicsBuffer arguments,
            uint offset)
        {
            DispatchComputeProfiled(command, shader, kernel, null, arguments,
                offset);
        }

        internal static void DispatchComputeProfiled(this CommandBuffer command,
            ComputeShader shader, int kernel, string profileName,
            GraphicsBuffer arguments, uint offset)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));
            if (arguments == null)
                throw new ArgumentNullException(nameof(arguments));
            bool timed = Observe(shader, kernel, profileName, -1, -1, -1);
            RecordDispatchEvent(command, timed, true);
            command.DispatchCompute(shader, kernel, arguments, offset);
            RecordDispatchEvent(command, timed, false);
        }

        internal static void ValidateDirectDispatchDimensions(int x, int y,
            int z)
        {
            if (x <= 0 || y <= 0 || z <= 0 ||
                x > MaximumThreadGroupsPerDimension ||
                y > MaximumThreadGroupsPerDimension ||
                z > MaximumThreadGroupsPerDimension)
                throw new InvalidOperationException(
                    $"Illegal direct compute dispatch ({x}, {y}, {z}); " +
                    $"every dimension must be in [1," +
                    $"{MaximumThreadGroupsPerDimension}].");
        }

        internal static Vector2Int ComputeLinearDispatchGrid(int logicalGroups)
        {
            if (logicalGroups <= 0)
                throw new ArgumentOutOfRangeException(nameof(logicalGroups));
            int y = checked((int)(((long)logicalGroups +
                MaximumThreadGroupsPerDimension - 1L) /
                MaximumThreadGroupsPerDimension));
            int x = checked((int)(((long)logicalGroups + y - 1L) / y));
            ValidateDirectDispatchDimensions(x, y, 1);
            return new Vector2Int(x, y);
        }

        internal static void CaptureAndLogFrame()
        {
            if (_state != State.AwaitingResults)
                return;
#if UNITY_EDITOR
            Finish();
#else
            int status = Native.TryRead(TimestampPairs, out int dispatchCount,
                out double timestampPeriod, out int validBits,
                out ulong capturedRevision, out bool overflow);
            if (status == 0)
                return;
            if (status < 0 || capturedRevision != _revision || overflow ||
                dispatchCount != DispatchSequence.Count)
            {
                Warn($"Vulkan timestamp mismatch revision={_revision}, " +
                    $"nativeRevision={capturedRevision}, " +
                    $"expected={DispatchSequence.Count}, actual={dispatchCount}, " +
                    $"overflow={overflow}");
                CancelSingleSubmission();
                return;
            }
            bool retain = _seriesWarmupRemaining == 0;
            LogResults(dispatchCount, timestampPeriod, validBits, retain);
            if (_seriesWarmupRemaining > 0)
                _seriesWarmupRemaining--;
            Finish();
#endif
        }

        private static bool Observe(ComputeShader shader, int kernel,
            string profileName, int x, int y, int z)
        {
            if (shader == null)
                throw new ArgumentNullException(nameof(shader));
            ulong entity = EntityId.ToULong(shader.GetEntityId());
#if UNITY_EDITOR
            DirectDispatchObservedForTests?.Invoke(entity, kernel, x, y, z);
#endif
            if (_state != State.Recording)
                return false;
            if (!EntriesByKernel.TryGetValue((entity, kernel), out Entry entry))
                throw new InvalidOperationException(
                    $"Compute kernel {shader.name}#{kernel} was not registered.");
            if (!string.IsNullOrWhiteSpace(profileName))
                entry = new Entry(shader.name + '.' + profileName);
            if (DispatchSequence.Count >= MaximumTimedDispatches)
                throw new InvalidOperationException(
                    $"Timestamp capture exceeds {MaximumTimedDispatches} " +
                    "dispatches.");
            DispatchSequence.Add(entry);
#if UNITY_EDITOR
            ProfiledDispatchObservedForTests?.Invoke(entity, kernel, x, y, z);
#endif
            return true;
        }

        private static void RecordDispatchEvent(CommandBuffer command,
            bool timed, bool begin)
        {
#if !UNITY_EDITOR && UNITY_ANDROID
            if (timed)
                command.IssuePluginEvent(Native.RenderEvent, begin
                    ? Native.EventId(Native.DispatchBegin)
                    : Native.EventId(Native.DispatchEnd));
#endif
        }

        private static void LogResults(int dispatchCount,
            double timestampPeriod, int validBits, bool retainForSeries)
        {
            ulong mask = validBits >= 64
                ? ulong.MaxValue : (1UL << validBits) - 1UL;
            var totals = new Dictionary<Entry, Aggregate>();
            double checksum = 0.0;
            for (int index = 0; index < dispatchCount; ++index)
            {
                ulong begin = TimestampPairs[index * 2] & mask;
                ulong end = TimestampPairs[index * 2 + 1] & mask;
                ulong ticks = (end - begin) & mask;
                double nanoseconds = ticks * timestampPeriod;
                Entry entry = DispatchSequence[index];
                if (!totals.TryGetValue(entry, out Aggregate aggregate))
                {
                    aggregate = new Aggregate();
                    totals.Add(entry, aggregate);
                }
                aggregate.Dispatches++;
                aggregate.TotalNanoseconds += nanoseconds;
                aggregate.MaximumNanoseconds = Math.Max(
                    aggregate.MaximumNanoseconds, nanoseconds);
                checksum += nanoseconds;
            }
            if (retainForSeries)
            {
                foreach (KeyValuePair<Entry, Aggregate> item in totals)
                {
                    if (!SeriesByKernel.TryGetValue(item.Key.Name,
                        out SeriesAggregate series))
                    {
                        series = new SeriesAggregate();
                        SeriesByKernel.Add(item.Key.Name, series);
                    }
                    series.TotalNanoseconds.Add(item.Value.TotalNanoseconds);
                }
                SeriesSubmissionNanoseconds.Add(checksum);
            }
            var ranked = new List<KeyValuePair<Entry, Aggregate>>(totals);
            ranked.Sort((left, right) => right.Value.TotalNanoseconds
                .CompareTo(left.Value.TotalNanoseconds));
            for (int index = 0; index < ranked.Count; ++index)
            {
                Entry entry = ranked[index].Key;
                Aggregate aggregate = ranked[index].Value;
                double average = aggregate.TotalNanoseconds /
                    aggregate.Dispatches;
                Logger.Info($"Sigma gpu-kernel revision={_revision} " +
                    $"rank={index + 1} kernel={entry.Name} " +
                    $"dispatches={aggregate.Dispatches} " +
                    $"total={aggregate.TotalNanoseconds / 1000.0:F1}us " +
                    $"average={average / 1000.0:F1}us " +
                    $"maximum={aggregate.MaximumNanoseconds / 1000.0:F1}us");
            }
            Logger.Info($"Sigma gpu-kernel revision={_revision} " +
                $"compute-checksum={checksum / 1_000_000.0:F3}ms " +
                $"dispatches={dispatchCount} kernels={ranked.Count} " +
                $"timestampPeriod={timestampPeriod:F6}ns validBits={validBits}");
        }

        private static void Finish()
        {
            DispatchSequence.Clear();
            _revision = 0u;
            if (_seriesRemaining > 0)
                _seriesRemaining--;
            if (_seriesRemaining > 0)
            {
                _state = State.Armed;
                return;
            }
            _state = State.Idle;
            if (SeriesSubmissionNanoseconds.Count != 0)
                LogSeriesResults();
            ResetSeries();
        }

        private static void LogSeriesResults()
        {
            var ranked = new List<KeyValuePair<string, SeriesAggregate>>(
                SeriesByKernel);
            ranked.Sort((left, right) => Percentile(right.Value.TotalNanoseconds,
                0.95).CompareTo(Percentile(left.Value.TotalNanoseconds, 0.95)));
            for (int index = 0; index < ranked.Count; ++index)
            {
                IReadOnlyList<double> samples = ranked[index].Value.TotalNanoseconds;
                Logger.Info($"Sigma gpu-series rank={index + 1} " +
                    $"kernel={ranked[index].Key} samples={samples.Count} " +
                    $"p50={Percentile(samples, 0.50) / 1000.0:F1}us " +
                    $"p95={Percentile(samples, 0.95) / 1000.0:F1}us " +
                    $"maximum={Maximum(samples) / 1000.0:F1}us");
            }
            double totalP50Ms = Percentile(SeriesSubmissionNanoseconds, 0.50) /
                1_000_000.0;
            double totalP95Ms = Percentile(SeriesSubmissionNanoseconds, 0.95) /
                1_000_000.0;
            double totalMaximumMs = Maximum(SeriesSubmissionNanoseconds) /
                1_000_000.0;
            Logger.Info($"Sigma gpu-series submissions=" +
                $"{SeriesSubmissionNanoseconds.Count}/" +
                $"{_seriesRequestedSamples} total-p50=" +
                $"{totalP50Ms:F3}ms total-p95={totalP95Ms:F3}ms " +
                $"total-max={totalMaximumMs:F3}ms");
        }

        private static double Percentile(IReadOnlyList<double> source,
            double percentile)
        {
            var sorted = new List<double>(source);
            sorted.Sort();
            int index = Math.Max(0, Math.Min(sorted.Count - 1,
                (int)Math.Ceiling(percentile * sorted.Count) - 1));
            return sorted[index];
        }

        private static double Maximum(IReadOnlyList<double> source)
        {
            double result = 0.0;
            for (int index = 0; index < source.Count; ++index)
                result = Math.Max(result, source[index]);
            return result;
        }

        private static void ResetSeries()
        {
            _seriesRemaining = 0;
            _seriesWarmupRemaining = 0;
            _seriesRequestedSamples = 0;
            SeriesByKernel.Clear();
            SeriesSubmissionNanoseconds.Clear();
        }

        private static void CancelNative()
        {
#if !UNITY_EDITOR && UNITY_ANDROID
            Native.Cancel();
#endif
        }

        private static (ulong Entity, int Kernel) Key(ComputeShader shader,
            int kernel) => (EntityId.ToULong(shader.GetEntityId()), kernel);

        private static void Warn(string detail)
        {
            if (_warningLogged)
                return;
            _warningLogged = true;
            Logger.Warning("Sigma GPU timing unavailable: " + detail +
                ". Dispatch remains exact and unprofiled.");
        }

        private static class Native
        {
            internal const int SubmissionBegin = 0;
            internal const int DispatchBegin = 1;
            internal const int DispatchEnd = 2;
            internal const int SubmissionEnd = 3;
#if !UNITY_EDITOR && UNITY_ANDROID
            private const string Library = "SigmaVulkanTimestamps";

            [DllImport(Library, EntryPoint = "SigmaTimestamp_IsAvailable")]
            private static extern int IsAvailableNative();
            [DllImport(Library, EntryPoint = "SigmaTimestamp_Arm")]
            private static extern int ArmNative(ulong revision);
            [DllImport(Library, EntryPoint = "SigmaTimestamp_Cancel")]
            private static extern void CancelNative();
            [DllImport(Library, EntryPoint = "SigmaTimestamp_GetRenderEventFunc")]
            private static extern IntPtr GetRenderEventFuncNative();
            [DllImport(Library, EntryPoint = "SigmaTimestamp_GetEventId")]
            private static extern int GetEventIdNative(int offset);
            [DllImport(Library, EntryPoint = "SigmaTimestamp_Read")]
            private static extern int ReadNative([Out] ulong[] timestamps,
                int timestampCapacity, out int dispatchCount,
                out double timestampPeriod, out int validBits,
                out ulong revision, out int overflow);

            internal static IntPtr RenderEvent => GetRenderEventFuncNative();
            internal static int EventId(int offset) => GetEventIdNative(offset);
            internal static bool TryArm(uint revision)
            {
                try
                {
                    return IsAvailableNative() != 0 &&
                        ArmNative(revision) != 0;
                }
                catch (DllNotFoundException) { return false; }
                catch (EntryPointNotFoundException) { return false; }
            }
            internal static void Cancel()
            {
                try { CancelNative(); }
                catch (DllNotFoundException) { }
                catch (EntryPointNotFoundException) { }
            }
            internal static int TryRead(ulong[] timestamps,
                out int dispatchCount, out double timestampPeriod,
                out int validBits, out ulong revision, out bool overflow)
            {
                int result = ReadNative(timestamps, timestamps.Length,
                    out dispatchCount, out timestampPeriod, out validBits,
                    out revision, out int overflowValue);
                overflow = overflowValue != 0;
                return result;
            }
#else
            internal static IntPtr RenderEvent => IntPtr.Zero;
            internal static int EventId(int offset) => 0;
            internal static bool TryArm(uint revision) => false;
            internal static void Cancel() { }
            internal static int TryRead(ulong[] timestamps,
                out int dispatchCount, out double timestampPeriod,
                out int validBits, out ulong revision, out bool overflow)
            {
                dispatchCount = 0;
                timestampPeriod = 0.0;
                validBits = 0;
                revision = 0u;
                overflow = false;
                return -1;
            }
#endif
        }

#if UNITY_EDITOR
        internal static void SetProfilingEnabledForTests(bool? enabled)
        {
            Reset();
            _testTimingAvailable = enabled == true;
            if (_testTimingAvailable)
                RequestSingleSubmission();
        }

        internal static int RegisteredKernelCountForTests =>
            EntriesByKernel.Count;
#endif
    }
}
