using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.SigmaPrism
{
    /// <summary>
    /// One-shot Vulkan timestamps for an otherwise unmodified Release submission.
    /// Two plugin events bracket the command buffer; the native Vulkan hook writes
    /// query pairs around dispatches and returns the complete pool after its fence.
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

        private static readonly Dictionary<(ulong Entity, int Kernel), Entry>
            EntriesByKernel = new();
        private static readonly List<Entry> DispatchSequence = new();
        private static readonly ulong[] TimestampPairs =
            new ulong[MaximumTimedDispatches * 2];
        private static State _state;
        private static uint _revision;
        private static bool _warningLogged;
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
            if (_state != State.Idle)
                return false;
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
                Native.BeginEventId);
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
                Native.EndEventId);
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
        }

        internal static void DispatchComputeProfiled(this CommandBuffer command,
            ComputeShader shader, int kernel, int x, int y, int z)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));
            ValidateDirectDispatchDimensions(x, y, z);
            Observe(shader, kernel, x, y, z);
            command.DispatchCompute(shader, kernel, x, y, z);
        }

        internal static void DispatchComputeProfiled(this CommandBuffer command,
            ComputeShader shader, int kernel, GraphicsBuffer arguments,
            uint offset)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));
            if (arguments == null)
                throw new ArgumentNullException(nameof(arguments));
            Observe(shader, kernel, -1, -1, -1);
            command.DispatchCompute(shader, kernel, arguments, offset);
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
                Finish();
                return;
            }
            LogResults(dispatchCount, timestampPeriod, validBits);
            Finish();
#endif
        }

        private static void Observe(ComputeShader shader, int kernel,
            int x, int y, int z)
        {
            if (shader == null)
                throw new ArgumentNullException(nameof(shader));
            ulong entity = EntityId.ToULong(shader.GetEntityId());
#if UNITY_EDITOR
            DirectDispatchObservedForTests?.Invoke(entity, kernel, x, y, z);
#endif
            if (_state != State.Recording)
                return;
            if (!EntriesByKernel.TryGetValue((entity, kernel), out Entry entry))
                throw new InvalidOperationException(
                    $"Compute kernel {shader.name}#{kernel} was not registered.");
            if (DispatchSequence.Count >= MaximumTimedDispatches)
                throw new InvalidOperationException(
                    $"Timestamp capture exceeds {MaximumTimedDispatches} " +
                    "dispatches.");
            DispatchSequence.Add(entry);
#if UNITY_EDITOR
            ProfiledDispatchObservedForTests?.Invoke(entity, kernel, x, y, z);
#endif
        }

        private static void LogResults(int dispatchCount,
            double timestampPeriod, int validBits)
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
            _state = State.Idle;
            _revision = 0u;
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
            [DllImport(Library, EntryPoint = "SigmaTimestamp_GetBeginEventId")]
            private static extern int GetBeginEventIdNative();
            [DllImport(Library, EntryPoint = "SigmaTimestamp_GetEndEventId")]
            private static extern int GetEndEventIdNative();
            [DllImport(Library, EntryPoint = "SigmaTimestamp_Read")]
            private static extern int ReadNative([Out] ulong[] timestamps,
                int timestampCapacity, out int dispatchCount,
                out double timestampPeriod, out int validBits,
                out ulong revision, out int overflow);

            internal static IntPtr RenderEvent => GetRenderEventFuncNative();
            internal static int BeginEventId => GetBeginEventIdNative();
            internal static int EndEventId => GetEndEventIdNative();
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
            internal static int BeginEventId => 0;
            internal static int EndEventId => 0;
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
