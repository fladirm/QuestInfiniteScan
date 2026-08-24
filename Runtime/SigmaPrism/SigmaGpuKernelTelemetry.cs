using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.SigmaPrism
{
    /// <summary>
    /// Optional read-only GPU timestamp instrumentation for compute dispatches.
    /// Production Release dispatches remain unprofiled unless explicitly enabled;
    /// measurements never participate in scheduling or canonical decisions.
    /// </summary>
    internal static class SigmaGpuKernelTelemetry
    {
        internal const int MaximumThreadGroupsPerDimension = 65535;
        private const int GpuTimestampDelayFrames = 3;
        private const int LogLineLimit = 3000;

        private sealed class Entry
        {
            internal Entry(string name, CustomSampler sampler,
                Recorder recorder)
            {
                Name = name;
                Sampler = sampler;
                Recorder = recorder;
            }

            internal string Name { get; }
            internal CustomSampler Sampler { get; }
            internal Recorder Recorder { get; }
        }

        private static readonly Dictionary<(ulong Entity, int Kernel), Entry>
            EntriesByKernel =
            new();
        private static readonly List<Entry> Entries = new();
        private static int _lastCapturedFrame = -1;
        private static bool _gpuUnavailableReported;
        private static bool _registrationUnavailableReported;
#if UNITY_EDITOR
        private static bool? _profilingOverrideForTests;
        internal static Action<ulong, int, int, int, int>
            DirectDispatchObservedForTests;
#endif

        private static bool ProfilingEnabled
        {
            get
            {
#if UNITY_EDITOR
                if (_profilingOverrideForTests.HasValue)
                    return _profilingOverrideForTests.Value;
#endif
#if UNITY_EDITOR || DEVELOPMENT_BUILD || SIGMA_GPU_KERNEL_PROFILING
                return true;
#else
                return false;
#endif
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            for (int index = 0; index < Entries.Count; ++index)
            {
                Recorder recorder = Entries[index].Recorder;
                if (recorder != null && recorder.isValid)
                    recorder.enabled = false;
            }
            EntriesByKernel.Clear();
            Entries.Clear();
            _lastCapturedFrame = -1;
            _gpuUnavailableReported = false;
            _registrationUnavailableReported = false;
#if UNITY_EDITOR
            DirectDispatchObservedForTests = null;
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
            if (ProfilingEnabled)
                Register(shader, kernel, kernelName);
            return kernel;
        }

        internal static void DispatchComputeProfiled(this CommandBuffer command,
            ComputeShader shader, int kernel, int threadGroupsX,
            int threadGroupsY, int threadGroupsZ)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));
            ValidateDirectDispatchDimensions(threadGroupsX, threadGroupsY,
                threadGroupsZ);
#if UNITY_EDITOR
            DirectDispatchObservedForTests?.Invoke(
                shader.GetEntityId().GetRawData(), kernel, threadGroupsX,
                threadGroupsY, threadGroupsZ);
#endif
            Entry entry = OptionalEntry(shader, kernel);
            if (entry?.Sampler == null)
            {
                command.DispatchCompute(shader, kernel, threadGroupsX,
                    threadGroupsY, threadGroupsZ);
                return;
            }
            command.BeginSample(entry.Sampler);
            command.DispatchCompute(shader, kernel, threadGroupsX,
                threadGroupsY, threadGroupsZ);
            command.EndSample(entry.Sampler);
        }

        internal static void DispatchComputeProfiled(this CommandBuffer command,
            ComputeShader shader, int kernel, GraphicsBuffer indirectArguments,
            uint argumentsOffset)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));
            if (indirectArguments == null)
                throw new ArgumentNullException(nameof(indirectArguments));
            Entry entry = OptionalEntry(shader, kernel);
            if (entry?.Sampler == null)
            {
                command.DispatchCompute(shader, kernel, indirectArguments,
                    argumentsOffset);
                return;
            }
            command.BeginSample(entry.Sampler);
            command.DispatchCompute(shader, kernel, indirectArguments,
                argumentsOffset);
            command.EndSample(entry.Sampler);
        }

        internal static void DispatchProfiled(this ComputeShader shader,
            int kernel, int threadGroupsX, int threadGroupsY,
            int threadGroupsZ)
        {
            ValidateDirectDispatchDimensions(threadGroupsX, threadGroupsY,
                threadGroupsZ);
            Entry entry = OptionalEntry(shader, kernel);
            if (entry?.Sampler == null)
            {
                shader.Dispatch(kernel, threadGroupsX, threadGroupsY,
                    threadGroupsZ);
                return;
            }
            entry.Sampler.Begin();
            shader.Dispatch(kernel, threadGroupsX, threadGroupsY,
                threadGroupsZ);
            entry.Sampler.End();
        }

        internal static void ValidateDirectDispatchDimensions(int threadGroupsX,
            int threadGroupsY, int threadGroupsZ)
        {
            if (threadGroupsX <= 0 || threadGroupsY <= 0 ||
                threadGroupsZ <= 0 ||
                threadGroupsX > MaximumThreadGroupsPerDimension ||
                threadGroupsY > MaximumThreadGroupsPerDimension ||
                threadGroupsZ > MaximumThreadGroupsPerDimension)
                throw new InvalidOperationException(
                    $"Illegal direct compute dispatch ({threadGroupsX}, " +
                    $"{threadGroupsY}, {threadGroupsZ}); every dimension must " +
                    $"be in [1,{MaximumThreadGroupsPerDimension}].");
        }

        internal static Vector2Int ComputeLinearDispatchGrid(
            int logicalGroupCount)
        {
            if (logicalGroupCount <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(logicalGroupCount));
            int groupsY = checked((int)(((long)logicalGroupCount +
                MaximumThreadGroupsPerDimension - 1L) /
                MaximumThreadGroupsPerDimension));
            int groupsX = checked((int)(((long)logicalGroupCount +
                groupsY - 1L) / groupsY));
            ValidateDirectDispatchDimensions(groupsX, groupsY, 1);
            return new Vector2Int(groupsX, groupsY);
        }

        internal static void DispatchIndirectProfiled(this ComputeShader shader,
            int kernel, ComputeBuffer indirectArguments, uint argumentsOffset)
        {
            if (indirectArguments == null)
                throw new ArgumentNullException(nameof(indirectArguments));
            Entry entry = OptionalEntry(shader, kernel);
            if (entry?.Sampler == null)
            {
                shader.DispatchIndirect(kernel, indirectArguments,
                    argumentsOffset);
                return;
            }
            entry.Sampler.Begin();
            shader.DispatchIndirect(kernel, indirectArguments, argumentsOffset);
            entry.Sampler.End();
        }

        internal static void DispatchIndirectProfiled(this ComputeShader shader,
            int kernel, GraphicsBuffer indirectArguments, uint argumentsOffset)
        {
            if (indirectArguments == null)
                throw new ArgumentNullException(nameof(indirectArguments));
            Entry entry = OptionalEntry(shader, kernel);
            if (entry?.Sampler == null)
            {
                shader.DispatchIndirect(kernel, indirectArguments,
                    argumentsOffset);
                return;
            }
            entry.Sampler.Begin();
            shader.DispatchIndirect(kernel, indirectArguments, argumentsOffset);
            entry.Sampler.End();
        }

        internal static void CaptureAndLogFrame()
        {
            if (!ProfilingEnabled)
                return;
            int frame = Time.frameCount;
            if (frame == _lastCapturedFrame)
                return;
            _lastCapturedFrame = frame;

            long totalNanoseconds = 0L;
            int totalBlocks = 0;
            int validRecorders = 0;
            var lines = new List<string>();
            var line = BeginLine();

            for (int index = 0; index < Entries.Count; ++index)
            {
                Entry entry = Entries[index];
                Recorder recorder = entry.Recorder;
                if (recorder == null || !recorder.isValid)
                    continue;
                validRecorders++;
                int blocks = recorder.gpuSampleBlockCount;
                long nanoseconds = recorder.gpuElapsedNanoseconds;
                if (blocks <= 0 && nanoseconds <= 0L)
                    continue;

                if (nanoseconds < 0L)
                    nanoseconds = 0L;
                totalBlocks += Math.Max(0, blocks);
                totalNanoseconds += nanoseconds;
                string sample = FormatSample(entry.Name, blocks, nanoseconds);
                if (line.Length + sample.Length + 1 > LogLineLimit)
                {
                    lines.Add(line.ToString());
                    line = BeginLine();
                }
                if (line[line.Length - 1] != '{')
                    line.Append(';');
                line.Append(sample);
            }

            if (line[line.Length - 1] != '{')
                lines.Add(line.ToString());

            if (totalBlocks == 0)
            {
                if (frame <= GpuTimestampDelayFrames + 2)
                    return;
                if (validRecorders == 0)
                {
                    if (!_gpuUnavailableReported)
                    {
                        _gpuUnavailableReported = true;
                        Logger.Warning("Sigma GPU kernel timestamps are " +
                            "unavailable; compute dispatch remains enabled " +
                            "without profiling markers.");
                    }
                    return;
                }
                if (!_gpuUnavailableReported)
                {
                    _gpuUnavailableReported = true;
                    Logger.Warning("Sigma GPU kernel timestamps contain no " +
                        "executed samples for this delayed GPU frame.");
                }
                return;
            }

            _gpuUnavailableReported = false;
            string summary = $"Sigma gpu-kernels sourceFrame=" +
                $"{frame - GpuTimestampDelayFrames} total=" +
                $"{NanosecondsToMilliseconds(totalNanoseconds):F3}ms " +
                $"blocks={totalBlocks} kernels={CountSamples(lines)}";
            for (int index = 0; index < lines.Count; ++index)
                Logger.Info(summary + " part=" + (index + 1) + "/" +
                    lines.Count + " " + lines[index] + '}');
        }

        private static void Register(ComputeShader shader, int kernel,
            string kernelName)
        {
            (ulong Entity, int Kernel) key = Key(shader, kernel);
            if (EntriesByKernel.TryGetValue(key, out Entry existing))
            {
                string expected = FullName(shader, kernelName);
                if (!string.Equals(existing.Name, expected,
                        StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"Compute kernel telemetry key collision: " +
                        $"{existing.Name} versus {expected}.");
                return;
            }

            string name = FullName(shader, kernelName);
            if (Entries.Count == 0 && !Profiler.enabled)
            {
                try
                {
                    Profiler.enabled = true;
                }
                catch (Exception exception)
                {
                    ReportRegistrationUnavailable(name,
                        "Profiler could not be enabled: " + exception.Message);
                }
            }
            CustomSampler sampler = null;
            Recorder recorder = null;
            try
            {
                sampler = CustomSampler.Create("Sigma.GPU." + name, true);
                if (sampler == null || !sampler.isValid)
                    sampler = null;
                else
                {
                    recorder = sampler.GetRecorder();
                    if (recorder == null || !recorder.isValid)
                    {
                        recorder = null;
                        sampler = null;
                    }
                    else
                    {
                        recorder.CollectFromAllThreads();
                        recorder.enabled = true;
                    }
                }
            }
            catch (Exception exception)
            {
                sampler = null;
                recorder = null;
                ReportRegistrationUnavailable(name, exception.Message);
            }
            if (sampler == null)
                ReportRegistrationUnavailable(name,
                    "GPU sampler or recorder is invalid");
            var entry = new Entry(name, sampler, recorder);
            EntriesByKernel.Add(key, entry);
            Entries.Add(entry);
        }

        private static Entry RequireEntry(ComputeShader shader, int kernel)
        {
            if (shader == null)
                throw new ArgumentNullException(nameof(shader));
            (ulong Entity, int Kernel) key = Key(shader, kernel);
            if (EntriesByKernel.TryGetValue(key, out Entry entry))
                return entry;
            throw new InvalidOperationException(
                $"Compute kernel {shader.name}#{kernel} was dispatched " +
                "without FindProfiledKernel registration.");
        }

        private static Entry OptionalEntry(ComputeShader shader, int kernel)
        {
            if (shader == null)
                throw new ArgumentNullException(nameof(shader));
            return ProfilingEnabled ? RequireEntry(shader, kernel) : null;
        }

        private static (ulong Entity, int Kernel) Key(ComputeShader shader,
            int kernel) => (shader.GetEntityId().GetRawData(), kernel);

        private static string FullName(ComputeShader shader,
            string kernelName) => shader.name + '.' + kernelName;

        private static void ReportRegistrationUnavailable(string name,
            string detail)
        {
            if (_registrationUnavailableReported)
                return;
            _registrationUnavailableReported = true;
            Logger.Warning("Sigma GPU kernel timing unavailable for " + name +
                ": " + detail + ". Dispatch will remain unprofiled.");
        }

        private static StringBuilder BeginLine() =>
            new StringBuilder(1024).Append("samples{");

        private static string FormatSample(string name, int blocks,
            long nanoseconds)
        {
            double totalMicroseconds = nanoseconds / 1000.0;
            double averageMicroseconds = blocks > 0
                ? totalMicroseconds / blocks : 0.0;
            return name + '=' + Math.Max(0, blocks) + '/' +
                totalMicroseconds.ToString("F1") + "us/" +
                averageMicroseconds.ToString("F1") + "us";
        }

        private static int CountSamples(List<string> lines)
        {
            int count = 0;
            for (int index = 0; index < lines.Count; ++index)
            {
                string value = lines[index];
                for (int cursor = 0; cursor < value.Length; ++cursor)
                    if (value[cursor] == '=')
                        count++;
            }
            return count;
        }

        private static double NanosecondsToMilliseconds(long nanoseconds) =>
            nanoseconds / 1_000_000.0;

#if UNITY_EDITOR
        internal static void SetProfilingEnabledForTests(bool? enabled)
        {
            Reset();
            _profilingOverrideForTests = enabled;
        }

        internal static int RegisteredKernelCountForTests => Entries.Count;
#endif
    }
}
