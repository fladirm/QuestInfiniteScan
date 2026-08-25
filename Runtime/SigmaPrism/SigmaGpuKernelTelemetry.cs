using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.SigmaPrism
{
    /// <summary>
    /// Explicit one-submission GPU timestamp instrumentation. Normal dispatches
    /// carry no markers. One requested sample aggregates each compute kernel via
    /// Unity's recorder without per-kernel readbacks or canonical authority.
    /// </summary>
    internal static class SigmaGpuKernelTelemetry
    {
        internal const int MaximumThreadGroupsPerDimension = 65535;
        private const int GpuTimestampDelayFrames = 3;

        private enum State : byte
        {
            Idle,
            Armed,
            Recording,
            Submitted,
            AwaitingCounters,
        }

        private sealed class Entry
        {
            internal Entry(string name) => Name = name;
            internal string Name { get; }
            internal CustomSampler Sampler { get; set; }
            internal Recorder Recorder { get; set; }
        }

        private static readonly Dictionary<(ulong Entity, int Kernel), Entry>
            EntriesByKernel = new();
        private static readonly List<Entry> Entries = new();
        private static State _state;
        private static uint _revision;
        private static int _captureFrame;
        private static bool _ownsProfiler;
        private static bool _registrationWarning;
#if UNITY_EDITOR
        internal static Action<ulong, int, int, int, int>
            DirectDispatchObservedForTests;
        internal static Action<ulong, int, int, int, int>
            ProfiledDispatchObservedForTests;
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Finish();
            EntriesByKernel.Clear();
            Entries.Clear();
            _registrationWarning = false;
#if UNITY_EDITOR
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
            Register(shader, kernel, kernelName);
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
            _ownsProfiler = !Profiler.enabled;
            if (_ownsProfiler)
            {
                try { Profiler.enabled = true; }
                catch (Exception exception)
                {
                    _ownsProfiler = false;
                    Warn("one-shot request", exception.Message);
                }
            }
            _revision = revision;
            for (int index = 0; index < Entries.Count; ++index)
            {
                Entry entry = Entries[index];
                EnsureSampler(entry);
                if (entry.Recorder == null || !entry.Recorder.isValid)
                    continue;
                entry.Recorder.enabled = false;
                entry.Recorder.enabled = true;
            }
            _state = State.Recording;
            return true;
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
            DisableRecorders();
            RestoreProfiler();
            _revision = 0u;
            _state = State.Armed;
        }

        internal static void CompleteProfiledSubmission(uint revision)
        {
            if (_state != State.Submitted || revision != _revision)
                return;
            _captureFrame = Time.frameCount + GpuTimestampDelayFrames;
            _state = State.AwaitingCounters;
        }

        internal static void CancelSingleSubmission() => Finish();

        internal static void DispatchComputeProfiled(this CommandBuffer command,
            ComputeShader shader, int kernel, int x, int y, int z)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));
            ValidateDirectDispatchDimensions(x, y, z);
#if UNITY_EDITOR
            ulong entity = EntityId.ToULong(shader.GetEntityId());
            DirectDispatchObservedForTests?.Invoke(entity, kernel, x, y, z);
            if (_state == State.Recording)
                ProfiledDispatchObservedForTests?.Invoke(entity, kernel,
                    x, y, z);
#endif
            Entry entry = ActiveEntry(shader, kernel);
            if (entry?.Sampler == null)
            {
                command.DispatchCompute(shader, kernel, x, y, z);
                return;
            }
            command.BeginSample(entry.Sampler);
            command.DispatchCompute(shader, kernel, x, y, z);
            command.EndSample(entry.Sampler);
        }

        internal static void DispatchComputeProfiled(this CommandBuffer command,
            ComputeShader shader, int kernel, GraphicsBuffer arguments,
            uint offset)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));
            if (arguments == null)
                throw new ArgumentNullException(nameof(arguments));
            Entry entry = ActiveEntry(shader, kernel);
            if (entry?.Sampler == null)
            {
                command.DispatchCompute(shader, kernel, arguments, offset);
                return;
            }
            command.BeginSample(entry.Sampler);
            command.DispatchCompute(shader, kernel, arguments, offset);
            command.EndSample(entry.Sampler);
        }

        internal static void DispatchProfiled(this ComputeShader shader,
            int kernel, int x, int y, int z)
        {
            ValidateDirectDispatchDimensions(x, y, z);
            Entry entry = ActiveEntry(shader, kernel);
            if (entry?.Sampler == null)
            {
                shader.Dispatch(kernel, x, y, z);
                return;
            }
            entry.Sampler.Begin();
            shader.Dispatch(kernel, x, y, z);
            entry.Sampler.End();
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
            if (_state != State.AwaitingCounters ||
                Time.frameCount < _captureFrame)
                return;
            var samples = new List<(Entry Entry, int Blocks, long Nanoseconds)>();
            for (int index = 0; index < Entries.Count; ++index)
            {
                Entry entry = Entries[index];
                Recorder recorder = entry.Recorder;
                if (recorder == null || !recorder.isValid)
                    continue;
                int blocks = Math.Max(0, recorder.gpuSampleBlockCount);
                long nanoseconds = Math.Max(0L,
                    recorder.gpuElapsedNanoseconds);
                if (blocks != 0 || nanoseconds != 0L)
                    samples.Add((entry, blocks, nanoseconds));
            }
            samples.Sort((left, right) =>
                right.Nanoseconds.CompareTo(left.Nanoseconds));
            long total = 0L;
            int totalBlocks = 0;
            for (int index = 0; index < samples.Count; ++index)
            {
                var sample = samples[index];
                total = checked(total + sample.Nanoseconds);
                totalBlocks = checked(totalBlocks + sample.Blocks);
                double totalUs = sample.Nanoseconds / 1000.0;
                double averageUs = sample.Blocks == 0
                    ? 0.0 : totalUs / sample.Blocks;
                Logger.Info($"Sigma gpu-kernel revision={_revision} " +
                    $"rank={index + 1} kernel={sample.Entry.Name} " +
                    $"dispatchBlocks={sample.Blocks} total={totalUs:F1}us " +
                    $"average={averageUs:F1}us");
            }
            if (samples.Count == 0)
                Logger.Warning("Sigma one-shot per-kernel GPU timestamp " +
                    "recorders returned no samples; dispatch stayed exact.");
            else
                Logger.Info($"Sigma gpu-kernel revision={_revision} " +
                    $"compute-checksum={total / 1_000_000.0:F3}ms " +
                    $"dispatchBlocks={totalBlocks} kernels={samples.Count}");
            Finish();
        }

        private static void Register(ComputeShader shader, int kernel,
            string kernelName)
        {
            var key = Key(shader, kernel);
            string name = shader.name + '.' + kernelName;
            if (EntriesByKernel.TryGetValue(key, out Entry existing))
            {
                if (existing.Name != name)
                    throw new InvalidOperationException(
                        $"Compute kernel telemetry collision: " +
                        $"{existing.Name} versus {name}.");
                return;
            }
            var entry = new Entry(name);
            EntriesByKernel.Add(key, entry);
            Entries.Add(entry);
            if (_state == State.Recording)
                EnsureSampler(entry);
        }

        private static Entry ActiveEntry(ComputeShader shader, int kernel)
        {
            if (shader == null)
                throw new ArgumentNullException(nameof(shader));
            if (_state != State.Recording)
                return null;
            if (!EntriesByKernel.TryGetValue(Key(shader, kernel),
                    out Entry entry))
                throw new InvalidOperationException(
                    $"Compute kernel {shader.name}#{kernel} was not registered.");
            EnsureSampler(entry);
            return entry;
        }

        private static void EnsureSampler(Entry entry)
        {
            if (entry.Sampler != null && entry.Sampler.isValid &&
                entry.Recorder != null && entry.Recorder.isValid)
                return;
            try
            {
                CustomSampler sampler = CustomSampler.Create(
                    "Sigma.GPU." + entry.Name, true);
                Recorder recorder = sampler?.GetRecorder();
                if (sampler == null || !sampler.isValid || recorder == null ||
                    !recorder.isValid)
                {
                    Warn(entry.Name, "invalid GPU sampler/recorder");
                    return;
                }
                recorder.CollectFromAllThreads();
                recorder.enabled = true;
                entry.Sampler = sampler;
                entry.Recorder = recorder;
            }
            catch (Exception exception) { Warn(entry.Name, exception.Message); }
        }

        private static void Finish()
        {
            DisableRecorders();
            RestoreProfiler();
            _state = State.Idle;
            _revision = 0u;
            _captureFrame = 0;
        }

        private static void DisableRecorders()
        {
            for (int index = 0; index < Entries.Count; ++index)
            {
                Recorder recorder = Entries[index].Recorder;
                if (recorder != null && recorder.isValid)
                    recorder.enabled = false;
            }
        }

        private static void RestoreProfiler()
        {
            if (!_ownsProfiler)
                return;
            try { Profiler.enabled = false; }
            catch (Exception) { }
            _ownsProfiler = false;
        }

        private static (ulong Entity, int Kernel) Key(ComputeShader shader,
            int kernel) => (EntityId.ToULong(shader.GetEntityId()), kernel);

        private static void Warn(string name, string detail)
        {
            if (_registrationWarning)
                return;
            _registrationWarning = true;
            Logger.Warning("Sigma GPU timing unavailable for " + name + ": " +
                detail + ". Dispatch remains exact and unprofiled.");
        }

#if UNITY_EDITOR
        internal static void SetProfilingEnabledForTests(bool? enabled)
        {
            Reset();
            if (enabled == true)
                RequestSingleSubmission();
        }

        internal static int RegisteredKernelCountForTests => Entries.Count;
        internal static int RegisteredSamplerCountForTests
        {
            get
            {
                int count = 0;
                for (int index = 0; index < Entries.Count; ++index)
                    if (Entries[index].Sampler != null)
                        count++;
                return count;
            }
        }
#endif
    }
}
