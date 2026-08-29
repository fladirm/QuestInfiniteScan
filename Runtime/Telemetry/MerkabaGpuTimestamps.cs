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
        WorldQuery,
        FrameCompile,
        MerkabaDraw,
        Count
    }

    /// <summary>
    /// Periodic Vulkan timestamps recorded in the same command buffers as each
    /// measured compute dispatch and the actual URP Merkaba draw. Normal frames
    /// emit no plugin events or timing readbacks.
    /// </summary>
    internal static class MerkabaGpuTimestamps
    {
        private const int MaximumTimedEntries = 4096;
        private const float InitialSampleDelaySeconds = 2f;
        private const float SampleIntervalSeconds = 5f;
        private const float UnavailableRetrySeconds = 30f;

        private enum CaptureState : byte
        {
            Idle,
            Recording,
            AwaitingResults
        }

        private sealed class TimingEntry
        {
            internal TimingEntry(MerkabaGpuStage stage, string name)
            {
                Stage = stage;
                Name = name;
            }

            internal MerkabaGpuStage Stage { get; }
            internal string Name { get; }
        }

        private sealed class Aggregate
        {
            internal int Invocations;
            internal double TotalNanoseconds;
            internal double MaximumNanoseconds;
        }

        private sealed class SampleMetrics
        {
            internal uint Revision;
            internal int PendingReadbacks;
            internal bool TimingComplete;
            internal bool ReadbackValid = true;
            internal bool Logged;
            internal bool MetricsCaptureRequested;
            internal uint BlockCount;
            internal uint ChunkCount;
            internal uint HotTiles;
            internal uint ColdTiles;
            internal uint HashCollisions;
            internal uint HashProbes;
            internal uint HashMaxProbe;
            internal uint BlockOverflow;
            internal uint ChunkOverflow;
            internal uint TileStarvation;
            internal uint HashFull;
            internal uint ValidSurfaceCandidates;
            internal uint UniqueSurfaceKernels;
            internal uint UnresolvedSurfaceTiles;
            internal uint SurfaceTilesAllocated;
            internal uint ScanColdMisses;
            internal uint CarveQueryBlocks;
            internal uint CarveQueryTiles;
            internal uint CarveActiveKernels;
            internal uint LoadRequests;
            internal uint WritebackTiles;
            internal uint FailedReads;
            internal uint FailedWrites;
            internal uint StorageBackpressure;
            internal uint CandidateBlocks;
            internal uint HashHitBlocks;
            internal uint VisibleChunks;
            internal uint VisibleTiles;
            internal uint OccupiedKernelsConsidered;
            internal uint PrimitivesBeforeFacing;
            internal uint LogicalPrimitives;
            internal uint RejectedPrimitives;
            internal uint LateColdMisses;
            internal uint RenderPrimitiveOverflow;
            internal float LoadBytesPerSecond;
            internal float WriteBytesPerSecond;
            internal float LoadLatencyP50Ms;
            internal float LoadLatencyP95Ms;
            internal float WriteLatencyP50Ms;
            internal float WriteLatencyP95Ms;
        }

        private static readonly string[] StageNames =
        {
            "DEPTH_PREPROCESS",
            "SURFACE_INTEGRATION",
            "CARVE_INTEGRATION",
            "M8_WORLD_QUERY",
            "M8_FRAME_COMPILE",
            "MERKABA_DRAW"
        };
        private static readonly Dictionary<(ulong Shader, int Kernel), TimingEntry>
            EntriesByKernel = new();
        private static readonly List<TimingEntry> EntrySequence =
            new(MaximumTimedEntries);
        private static readonly TimingEntry DrawEntry = new(
            MerkabaGpuStage.MerkabaDraw, "MerkabaGrid.DrawProceduralIndirect");
        private static readonly ulong[] TimestampPairs =
            new ulong[MaximumTimedEntries * 2];

        private static CaptureState _state;
        private static uint _revision;
        private static float _nextSampleTime;
        private static bool _submissionBegan;
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
            EntriesByKernel.Clear();
            EntrySequence.Clear();
            _state = CaptureState.Idle;
            _revision = 0u;
            _nextSampleTime = float.PositiveInfinity;
            _submissionBegan = false;
#if !UNITY_EDITOR
            _unavailableWarningLogged = false;
#endif
            _metrics = null;
#if UNITY_EDITOR
            _testAvailable = false;
#endif
        }

        internal static int FindProfiledKernel(this ComputeShader shader,
            string kernelName, MerkabaGpuStage stage)
        {
            if (shader == null) throw new ArgumentNullException(nameof(shader));
            if (string.IsNullOrWhiteSpace(kernelName))
                throw new ArgumentException("Kernel name is required.",
                    nameof(kernelName));
            if ((uint)stage >= (uint)MerkabaGpuStage.Count)
                throw new ArgumentOutOfRangeException(nameof(stage));
            int kernel = shader.FindKernel(kernelName);
            RegisterKernel(shader, kernel, stage, kernelName);
            return kernel;
        }

        internal static void RegisterKernel(ComputeShader shader, int kernel,
            MerkabaGpuStage stage, string kernelName)
        {
            if (shader == null) throw new ArgumentNullException(nameof(shader));
            if ((uint)stage >= (uint)MerkabaGpuStage.Count)
                throw new ArgumentOutOfRangeException(nameof(stage));
            string name = shader.name + '.' + kernelName;
            var key = (EntityId.ToULong(shader.GetEntityId()), kernel);
            if (EntriesByKernel.TryGetValue(key, out TimingEntry existing))
            {
                if (existing.Name != name || existing.Stage != stage)
                    throw new InvalidOperationException(
                        $"GPU timing registration collision: {existing.Name} " +
                        $"versus {name}.");
                return;
            }
            EntriesByKernel.Add(key, new TimingEntry(stage, name));
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
            if (!Native.TryArm(revision))
            {
                if (!_unavailableWarningLogged)
                {
                    _unavailableWarningLogged = true;
                    Logger.Warning("Merkaba GPU kernel timestamps unavailable; " +
                                   "Vulkan query plugin was not loaded.");
                }
                _nextSampleTime = Time.unscaledTime + UnavailableRetrySeconds;
                return false;
            }
#endif
            EntrySequence.Clear();
            _revision = revision;
            _metrics = new SampleMetrics { Revision = revision };
            _submissionBegan = false;
            _state = CaptureState.Recording;
            return true;
        }

        internal static void RecordProfileBegin(CommandBuffer command)
        {
            if (_state != CaptureState.Recording || _submissionBegan)
                return;
            if (command == null) throw new ArgumentNullException(nameof(command));
#if !UNITY_EDITOR && UNITY_ANDROID
            command.IssuePluginEvent(Native.RenderEvent,
                Native.EventId(Native.SubmissionBegin));
#endif
            _submissionBegan = true;
        }

        internal static void DispatchComputeProfiled(this CommandBuffer command,
            ComputeShader shader, int kernel, int x, int y, int z)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            ValidateDispatchDimensions(x, y, z);
            bool timed = Observe(shader, kernel);
            RecordDispatchEvent(command, timed, true);
            command.DispatchCompute(shader, kernel, x, y, z);
            RecordDispatchEvent(command, timed, false);
        }

        internal static void DrawProceduralIndirectProfiled(
            this RasterCommandBuffer command, Matrix4x4 matrix,
            Material material, int shaderPass, MeshTopology topology,
            ComputeBuffer arguments, int argumentsOffset)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (material == null) throw new ArgumentNullException(nameof(material));
            if (arguments == null) throw new ArgumentNullException(nameof(arguments));
            bool timed = Observe(DrawEntry);
            RecordDrawEvent(command, timed, true);
            command.DrawProceduralIndirect(matrix, material, shaderPass,
                topology, arguments, argumentsOffset);
            RecordDrawEvent(command, timed, false);
        }

        internal static void DispatchComputeProfiled(this CommandBuffer command,
            ComputeShader shader, int kernel, ComputeBuffer arguments,
            uint offset = 0u)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (arguments == null) throw new ArgumentNullException(nameof(arguments));
            bool timed = Observe(shader, kernel);
            RecordDispatchEvent(command, timed, true);
            command.DispatchCompute(shader, kernel, arguments, offset);
            RecordDispatchEvent(command, timed, false);
        }

        internal static void RecordProfileEnd(CommandBuffer command)
        {
            if (_state != CaptureState.Recording)
                return;
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (!_submissionBegan)
            {
                CancelFrame();
                return;
            }
#if !UNITY_EDITOR && UNITY_ANDROID
            command.IssuePluginEvent(Native.RenderEvent,
                Native.EventId(Native.SubmissionEnd));
#endif
        }

        internal static void RecordProfileEnd(RasterCommandBuffer command)
        {
            if (_state != CaptureState.Recording)
                return;
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (!_submissionBegan)
            {
                CancelFrame();
                return;
            }
#if !UNITY_EDITOR && UNITY_ANDROID
            command.IssuePluginEvent(Native.RenderEvent,
                Native.EventId(Native.SubmissionEnd));
#endif
        }

        internal static void CompleteFrameSubmission(bool submitted)
        {
            if (_state != CaptureState.Recording)
                return;
            if (!submitted)
            {
                CancelFrame();
                return;
            }
            _state = CaptureState.AwaitingResults;
#if !UNITY_EDITOR
            _nextSampleTime = Time.unscaledTime + SampleIntervalSeconds;
#endif
        }

        internal static void CloseIncompleteFrame()
        {
            if (_state != CaptureState.Recording)
                return;
            if (!_submissionBegan)
            {
                CancelFrame();
                return;
            }
            CommandBuffer command = CommandBufferPool.Get(
                "Merkaba GPU timestamp close");
            bool submitted = false;
            try
            {
                RecordProfileEnd(command);
                Graphics.ExecuteCommandBuffer(command);
                submitted = true;
            }
            finally
            {
                CompleteFrameSubmission(submitted);
                CommandBufferPool.Release(command);
            }
        }

        internal static void CancelUnsubmittedFrame()
        {
            if (_state == CaptureState.Recording)
                CancelFrame();
        }

        internal static void Poll()
        {
            if (_state != CaptureState.AwaitingResults)
                return;
#if UNITY_EDITOR
            return;
#else
            int status = Native.TryRead(TimestampPairs, out int entryCount,
                out double timestampPeriod, out int validBits,
                out ulong capturedRevision, out bool overflow);
            if (status == 0)
                return;
            bool valid = status > 0 && !overflow &&
                         capturedRevision == _revision &&
                         entryCount == EntrySequence.Count;
            if (valid)
                LogTimings(entryCount, timestampPeriod, validBits);
            else
                Logger.Warning($"Merkaba GPU timestamp sample invalid " +
                               $"revision={_revision} nativeRevision=" +
                               $"{capturedRevision} expectedEntries=" +
                               $"{EntrySequence.Count} actualEntries=" +
                               $"{entryCount} overflow={overflow}");
            if (_metrics != null)
            {
                _metrics.TimingComplete = true;
                _metrics.ReadbackValid &= valid;
                TryLogMetrics(_metrics);
            }
            FinishFrame();
#endif
        }

        internal static void CaptureM8Metrics(MerkabaGrid grid)
        {
            SampleMetrics sample = RecordingMetrics();
            if (sample == null || grid == null || grid.M8Counters == null ||
                sample.MetricsCaptureRequested)
                return;
            sample.MetricsCaptureRequested = true;
            grid.CaptureStorageMetrics(out sample.LoadBytesPerSecond,
                out sample.WriteBytesPerSecond, out sample.LoadLatencyP50Ms,
                out sample.LoadLatencyP95Ms, out sample.WriteLatencyP50Ms,
                out sample.WriteLatencyP95Ms);
            sample.PendingReadbacks++;
            AsyncGPUReadback.Request(grid.M8Counters, request =>
            {
                if (request.hasError)
                    sample.ReadbackValid = false;
                else
                {
                    var values = request.GetData<uint>();
                    sample.BlockCount = values[0];
                    sample.ChunkCount = values[1];
                    sample.HotTiles = values[2];
                    sample.ColdTiles = values[3];
                    sample.HashCollisions = values[4];
                    sample.HashProbes = values[5];
                    sample.HashMaxProbe = values[6];
                    sample.BlockOverflow = values[7];
                    sample.ChunkOverflow = values[8];
                    sample.TileStarvation = values[9];
                    sample.ValidSurfaceCandidates = values[10];
                    sample.UniqueSurfaceKernels = values[11];
                    sample.UnresolvedSurfaceTiles = values[12];
                    sample.SurfaceTilesAllocated = values[13];
                    sample.ScanColdMisses = values[14];
                    sample.CarveQueryTiles = values[17];
                    sample.CarveActiveKernels = values[18];
                    sample.LoadRequests = values[19];
                    sample.VisibleTiles = values[21];
                    sample.LogicalPrimitives = values[22];
                    sample.RenderPrimitiveOverflow = values[23];
                    sample.LateColdMisses = values[24];
                    sample.CandidateBlocks = values[26];
                    sample.HashHitBlocks = values[27];
                    sample.VisibleChunks = values[28];
                    sample.OccupiedKernelsConsidered = values[29];
                    sample.PrimitivesBeforeFacing = values[30];
                    sample.RejectedPrimitives = values[31];
                    sample.HashFull = values[38];
                    sample.FailedReads = values[39];
                    sample.FailedWrites = values[40];
                    sample.StorageBackpressure = values[41];
                    sample.CarveQueryBlocks = values[48];
                    sample.WritebackTiles = values[49];
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

        private static bool Observe(ComputeShader shader, int kernel)
        {
            if (shader == null) throw new ArgumentNullException(nameof(shader));
            if (_state != CaptureState.Recording)
                return false;
            if (!EntriesByKernel.TryGetValue((
                    EntityId.ToULong(shader.GetEntityId()), kernel),
                    out TimingEntry entry))
                throw new InvalidOperationException(
                    $"GPU timing kernel {shader.name}#{kernel} was not registered.");
            return Observe(entry);
        }

        private static bool Observe(TimingEntry entry)
        {
            if (_state != CaptureState.Recording)
                return false;
            if (EntrySequence.Count >= MaximumTimedEntries)
                throw new InvalidOperationException(
                    $"GPU timing capture exceeds {MaximumTimedEntries} entries.");
            EntrySequence.Add(entry);
            return true;
        }

        private static void RecordDispatchEvent(CommandBuffer command,
            bool timed, bool begin)
        {
#if !UNITY_EDITOR && UNITY_ANDROID
            if (timed)
                command.IssuePluginEvent(Native.RenderEvent,
                    Native.EventId(begin
                        ? Native.DispatchBegin : Native.DispatchEnd));
#endif
        }

        private static void RecordDrawEvent(RasterCommandBuffer command,
            bool timed, bool begin)
        {
#if !UNITY_EDITOR && UNITY_ANDROID
            if (timed)
                command.IssuePluginEvent(Native.RenderEvent,
                    Native.EventId(begin ? Native.DrawBegin : Native.DrawEnd));
#endif
        }

        private static void ValidateDispatchDimensions(int x, int y, int z)
        {
            const int maximum = 65535;
            if (x <= 0 || y <= 0 || z <= 0 ||
                x > maximum || y > maximum || z > maximum)
                throw new InvalidOperationException(
                    $"Illegal compute dispatch ({x},{y},{z}); each dimension " +
                    $"must be in [1,{maximum}].");
        }

        private static void LogTimings(int entryCount,
            double timestampPeriod, int validBits)
        {
            var operationTotals = new Dictionary<TimingEntry, Aggregate>();
            var stageTotals = new Aggregate[(int)MerkabaGpuStage.Count];
            for (int stage = 0; stage < stageTotals.Length; stage++)
                stageTotals[stage] = new Aggregate();
            double checksum = 0.0;
            for (int index = 0; index < entryCount; index++)
            {
                double nanoseconds = ElapsedNanoseconds(
                    TimestampPairs[index * 2], TimestampPairs[index * 2 + 1],
                    timestampPeriod, validBits);
                TimingEntry entry = EntrySequence[index];
                if (!operationTotals.TryGetValue(entry, out Aggregate operation))
                {
                    operation = new Aggregate();
                    operationTotals.Add(entry, operation);
                }
                Add(operation, nanoseconds);
                Add(stageTotals[(int)entry.Stage], nanoseconds);
                checksum += nanoseconds;
            }
            var ranked = new List<KeyValuePair<TimingEntry, Aggregate>>(
                operationTotals);
            ranked.Sort((left, right) => right.Value.TotalNanoseconds
                .CompareTo(left.Value.TotalNanoseconds));
            for (int index = 0; index < ranked.Count; index++)
            {
                TimingEntry entry = ranked[index].Key;
                Aggregate value = ranked[index].Value;
                Logger.Info($"Merkaba gpu-operation revision={_revision} " +
                            $"rank={index + 1} stage=" +
                            $"{StageNames[(int)entry.Stage]} " +
                            $"name={entry.Name} invocations={value.Invocations} " +
                            $"total={value.TotalNanoseconds / 1000.0:F1}us " +
                            $"average={value.TotalNanoseconds / value.Invocations / 1000.0:F1}us " +
                            $"maximum={value.MaximumNanoseconds / 1000.0:F1}us");
            }
            for (int stage = 0; stage < stageTotals.Length; stage++)
            {
                Aggregate value = stageTotals[stage];
                Logger.Info($"Merkaba gpu-stage revision={_revision} " +
                            $"stage={StageNames[stage]} " +
                            $"invocations={value.Invocations} " +
                            $"total={value.TotalNanoseconds / 1_000_000.0:F3}ms " +
                            $"maximum={value.MaximumNanoseconds / 1_000_000.0:F3}ms");
            }
            Logger.Info($"Merkaba gpu-sample revision={_revision} " +
                        $"timestamp-checksum={checksum / 1_000_000.0:F3}ms " +
                        $"entries={entryCount} operations={ranked.Count} " +
                        $"timestampPeriod={timestampPeriod:F6}ns " +
                        $"validBits={validBits}");
        }

        private static void Add(Aggregate aggregate, double nanoseconds)
        {
            aggregate.Invocations++;
            aggregate.TotalNanoseconds += nanoseconds;
            aggregate.MaximumNanoseconds = Math.Max(
                aggregate.MaximumNanoseconds, nanoseconds);
        }

        private static SampleMetrics RecordingMetrics() =>
            _state is CaptureState.Recording or CaptureState.AwaitingResults
                ? _metrics : null;

        private static void TryLogMetrics(SampleMetrics sample)
        {
            if (sample == null || sample.Logged || !sample.TimingComplete ||
                sample.PendingReadbacks != 0)
                return;
            sample.Logged = true;
            Logger.Info($"Merkaba GPU metrics revision={sample.Revision} " +
                        $"valid={sample.ReadbackValid} " +
                        $"m8Blocks={sample.BlockCount} " +
                        $"chunks={sample.ChunkCount} " +
                        $"hotTiles={sample.HotTiles} " +
                        $"coldTiles={sample.ColdTiles} " +
                        $"hashLoad={(double)sample.BlockCount / 32768.0:F4} " +
                        $"hashCollisions={sample.HashCollisions} " +
                        $"hashProbes={sample.HashProbes} " +
                        $"hashMaxProbe={sample.HashMaxProbe} " +
                        $"blockOverflow={sample.BlockOverflow} " +
                        $"chunkOverflow={sample.ChunkOverflow} " +
                        $"physicalTileStarvation={sample.TileStarvation} " +
                        $"hashFull={sample.HashFull} " +
                        $"validSurfaceCandidates={sample.ValidSurfaceCandidates} " +
                        $"uniqueSurfaceKernels={sample.UniqueSurfaceKernels} " +
                        $"unresolvedSurfaceTiles={sample.UnresolvedSurfaceTiles} " +
                        $"surfaceTilesAllocated={sample.SurfaceTilesAllocated} " +
                        $"scanColdMisses={sample.ScanColdMisses} " +
                        $"carveQueryBlocks={sample.CarveQueryBlocks} " +
                        $"carveQueryTiles={sample.CarveQueryTiles} " +
                        $"carveActiveKernels={sample.CarveActiveKernels} " +
                        $"loadRequests={sample.LoadRequests} " +
                        $"loadBytesPerSecond={sample.LoadBytesPerSecond:F0} " +
                        $"loadLatencyP50={sample.LoadLatencyP50Ms:F2}ms " +
                        $"loadLatencyP95={sample.LoadLatencyP95Ms:F2}ms " +
                        $"writebackTiles={sample.WritebackTiles} " +
                        $"writeBytesPerSecond={sample.WriteBytesPerSecond:F0} " +
                        $"writeLatencyP50={sample.WriteLatencyP50Ms:F2}ms " +
                        $"writeLatencyP95={sample.WriteLatencyP95Ms:F2}ms " +
                        $"storageBackpressure={sample.StorageBackpressure} " +
                        $"failedReads={sample.FailedReads} " +
                        $"failedWrites={sample.FailedWrites} " +
                        $"candidateM8Blocks={sample.CandidateBlocks} " +
                        $"hashHitM8Blocks={sample.HashHitBlocks} " +
                        $"visibleChunks={sample.VisibleChunks} " +
                        $"visibleTiles={sample.VisibleTiles} " +
                        $"occupiedKernelsConsidered={sample.OccupiedKernelsConsidered} " +
                        $"primitivesBeforeFacing={sample.PrimitivesBeforeFacing} " +
                        $"logicalVisiblePrimitives={sample.LogicalPrimitives} " +
                        $"rawSPIInstances={sample.LogicalPrimitives * 2u} " +
                        $"primitivesRejectedBothEyes={sample.RejectedPrimitives} " +
                        $"lateDrawColdMisses={sample.LateColdMisses} " +
                        $"renderPrimitiveOverflow={sample.RenderPrimitiveOverflow}");
        }

        private static void CancelFrame()
        {
#if !UNITY_EDITOR && UNITY_ANDROID
            Native.Cancel();
#endif
            FinishFrame();
        }

        private static void FinishFrame()
        {
            EntrySequence.Clear();
            _state = CaptureState.Idle;
            _revision = 0u;
            _submissionBegan = false;
        }

#if UNITY_EDITOR
        internal static void SetAvailableForTests(bool available)
        {
            _testAvailable = available;
            if (!available)
            {
                _state = CaptureState.Idle;
                _revision = 0u;
                _submissionBegan = false;
                EntrySequence.Clear();
            }
        }

        internal static MerkabaGpuStage[] RecordedStagesForTests()
        {
            var result = new MerkabaGpuStage[EntrySequence.Count];
            for (int index = 0; index < result.Length; index++)
                result[index] = EntrySequence[index].Stage;
            return result;
        }
#endif

        private static class Native
        {
            internal const int SubmissionBegin = 0;
            internal const int DispatchBegin = 1;
            internal const int DispatchEnd = 2;
            internal const int DrawBegin = 3;
            internal const int DrawEnd = 4;
            internal const int SubmissionEnd = 5;
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
                int timestampCapacity, out int entryCount,
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
                out int entryCount, out double timestampPeriod,
                out int validBits, out ulong revision, out bool overflow)
            {
                try
                {
                    int result = ReadNative(timestamps, timestamps.Length,
                        out entryCount, out timestampPeriod, out validBits,
                        out revision, out int overflowValue);
                    overflow = overflowValue != 0;
                    return result;
                }
                catch (DllNotFoundException)
                {
                    entryCount = validBits = 0;
                    timestampPeriod = 0.0;
                    revision = 0u;
                    overflow = false;
                    return -1;
                }
                catch (EntryPointNotFoundException)
                {
                    entryCount = validBits = 0;
                    timestampPeriod = 0.0;
                    revision = 0u;
                    overflow = false;
                    return -1;
                }
            }
#else
            internal static IntPtr RenderEvent => IntPtr.Zero;
            internal static int EventId(int offset) => 0;
            internal static bool TryArm(uint revision) => false;
            internal static void Cancel() { }
            internal static int TryRead(ulong[] timestamps,
                out int entryCount, out double timestampPeriod,
                out int validBits, out ulong revision, out bool overflow)
            {
                entryCount = validBits = 0;
                timestampPeriod = 0.0;
                revision = 0u;
                overflow = false;
                return -1;
            }
#endif
        }
    }
}
