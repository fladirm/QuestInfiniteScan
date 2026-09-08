using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan
{
    /// <summary>
    /// Thin managed lifetime boundary for the plugin-owned same-family Vulkan
    /// scanner queue. It contains no scanner math or persistent world state.
    /// </summary>
    internal static class MerkabaNativeVulkanExecutor
    {
        private const float TimingLogIntervalSeconds = 5f;
        // ABI 20: one snapshot is one bounded synchronous scan transaction.
        // The drain is four entry points - geometry, carrier resolve, RGB
        // signal, V signal - and the stage barrier between them is its own
        // one-group dispatch, so the pipeline table is 27 entries.
        internal const int AbiVersion = 20;
        internal const int ResourceCount = 43;
        internal const int PipelineCount = 27;
        // One observation also repeats Count for the allocation round trip,
        // dispatches publication Reserve once, advances the stage three times
        // and runs its allocation barriers twice.
        internal const int MaximumDispatchTimingCount = PipelineCount + 8;
        internal const int MaximumTimestampCount = MaximumDispatchTimingCount * 2 + 2;

        internal enum JobKind : uint
        {
            // One snapshot is one bounded synchronous scan transaction, so there
            // is exactly one observation kind. A retry or a continuation would
            // be a second attempt at a frame the world has already moved past.
            Observation = 0,
            FlowerReadout = 1,
            FineErase = 2,
        }

        [Flags]
        internal enum FlowerPasses : uint
        {
            None = 0,
            Classify = 1,
            Compact = 2,
            Publish = 4,
            Cull = 8,
        }

        internal enum Resource : int
        {
            HashEntries = 0,
            OwnerRecords,
            BlockChunkRefs,
            BlockPresenceL0,
            BlockPresenceL1,
            BlockPresenceL2,
            ChunkTileRefs,
            ChunkPresence,
            KernelStates0,
            KernelStates1,
            KernelStates2,
            KernelStates3,
            TileBits,
            TileRecords,
            FreeTileStack,
            Counters,
            ClaimQueue,
            PendingNewTileRefs,
            LoadRequests,
            LoadRequestReadCount,
            TouchedTileQueue,
            ObservationDispatchArgs,
            AttemptCompletion,
            RefineMetrics,
            RawDepth,
            RefinedDepth,
            Normals,
            CameraLeft,
            CameraRight,
            FrameDispatchArgs,
            ObservationRecords,
            ObservationTileBins,
            TileHalo,
            DepthCertificate,
            DualBlockState,
            DualChunkState,
            DualLeaves,
            FlowerDetailPages,
            ThreadAtlasPages,
            FlowerSymbolArena,
            FlowerPageDirectory,
            FlowerIndirectCommands,
            FlowerTables,
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct UniformValue
        {
            internal uint NameHash;
            internal uint Offset;
            internal uint Size;
            internal uint Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeJobDescriptor
        {
            internal uint StructSize;
            internal uint AbiVersion;
            internal uint Kind;
            internal uint Revision;
            internal uint ResourceCount;
            internal IntPtr Resources;
            internal uint UniformValueCount;
            internal IntPtr UniformValues;
            internal IntPtr UniformData;
            internal uint UniformDataSize;
            internal uint DepthGroupsX;
            internal uint DepthGroupsY;
            internal uint QueryGroups;
            internal uint FlowerPassMask;
        }

        private static readonly string[] PipelineNames =
        {
            "StereoFlowerRefine",
            "BuildDepthCertificate",
            "ReduceDepthCertificate",
            "ResetObservationBins",
            "CountObservationBins",
            "ResolveMissingSpatialNodes",
            "ResolveObservationTileRequests",
            "InitializeNewTiles",
            "ReserveObservationBins",
            "EmitObservationBins",
            "UpdateObservationDual",
            "FlowerCommit",
            "DrainFlowerGeometry",
            "AdvanceRefinementStage",
            "ResolveFlowerCarriers",
            "DrainFlowerSkinRgb",
            "DrainFlowerSkinV",
            "FinalizeObservation",
            "ClassifyHotFlowerPages",
            "PrepareDirtyFlowerBatch",
            "CompactDirtyFlowerSymbols",
            "ReserveDirtyFlowerBatch",
            "PublishDirtyFlowerPages",
            "CullFlowerPages",
            "QueryFineEraseTiles",
            "EraseFineTiles",
            "FinalizeFineErase",
        };

        private static MerkabaNativeVulkanJob _activeJob;
        private static readonly float[] NextTimingLogTimes =
            new float[4];
        private static readonly ulong[] TimingScratch = new ulong[MaximumTimestampCount];
        private static readonly uint[] PipelineScratch = new uint[MaximumDispatchTimingCount];
        private static double _heldObservationStartedAt;
        private static double _nextObservationTimingAt;
        private static ObservationTiming _observationTiming;
        private static bool _startupConfigured;
        private static bool _startupConfigurationFailed;
        private static bool _startupFailureLogged;
        private static int _startupLastState;
        private static uint _startupLastPipeline = uint.MaxValue;
        private static int _startupLastError;
        private static string _startupSummary = "Native pipelines: waiting for initialization";

        internal static string StartupSummary
        {
            get { _ = IsAvailable; return _startupSummary; }
        }

        internal static bool StartupFailed => _startupConfigurationFailed || _startupLastState < 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetTimingSamples()
        {
            Array.Clear(NextTimingLogTimes, 0, NextTimingLogTimes.Length);
            _heldObservationStartedAt = 0.0;
            _nextObservationTimingAt = 0.0;
            _observationTiming = null;
            _startupConfigured = false;
            _startupConfigurationFailed = false;
            _startupFailureLogged = false;
            _startupLastState = 0;
            _startupLastPipeline = uint.MaxValue;
            _startupLastError = 0;
            _startupSummary = "Native pipelines: waiting for initialization";
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ConfigureStartup()
        {
#if !UNITY_EDITOR && UNITY_ANDROID
            if (_startupConfigured) return;
            _startupConfigured = true;
            try
            {
                if (Native.GetAbiVersion() != AbiVersion)
                    throw new InvalidOperationException("Native executor ABI does not match this application.");
                string directory = Path.Combine(Application.persistentDataPath, "merkaba-pipeline-cache");
                Directory.CreateDirectory(directory);
                if (Native.ConfigureStartup(directory) != 0)
                    throw new InvalidOperationException("Native pipeline startup configuration failed.");
            }
            catch (Exception error) when (error is DllNotFoundException ||
                error is EntryPointNotFoundException || error is IOException ||
                error is UnauthorizedAccessException || error is InvalidOperationException)
            {
                _startupFailureLogged = true;
                _startupConfigurationFailed = true;
                _startupSummary = "Native startup FAILED: " + error.Message;
                Logger.Error($"Merkaba native startup FAILED: {error.Message}");
            }
#endif
        }

        // One bounded accumulator for the one immutable observation. A retry
        // contributes another quantum, never another acquisition or sample.
        private sealed class ObservationTiming
        {
            internal uint Token;
            internal double StartedAt;
            internal int Quanta;
            internal int TimedQuanta;
            internal int UnresolvedQuanta;
            internal bool Valid = true;
            internal double GpuMilliseconds;
            internal double PublicationMilliseconds;
            internal readonly double[] PipelineMilliseconds = new double[PipelineCount];
            internal readonly int[] PipelineDispatches = new int[PipelineCount];
        }

        internal static void BeginHeldObservationTiming() =>
            _heldObservationStartedAt = Time.realtimeSinceStartupAsDouble;

        internal static void ObserveHeldPublication(uint observation, bool completed,
            double publicationMilliseconds)
        {
            ObservationTiming sample = _observationTiming;
            if (sample == null || sample.Token != observation) return;
            sample.PublicationMilliseconds += Math.Max(0.0, publicationMilliseconds);
            if (!completed) sample.UnresolvedQuanta++;
        }

        internal static void EndHeldObservationTiming(uint observation, bool completed,
            uint failureReason)
        {
            ObservationTiming sample = _observationTiming;
            if (sample == null || sample.Token != observation) return;
            _observationTiming = null;
            for (int pipeline = 0; pipeline < PipelineCount; pipeline++)
                if (sample.PipelineDispatches[pipeline] != 0)
                    Logger.Info($"Merkaba held-observation kernel observation={observation} " +
                        $"pipeline={PipelineNames[pipeline]} dispatches={sample.PipelineDispatches[pipeline]} " +
                        $"gpuSumMs={sample.PipelineMilliseconds[pipeline]:F3}");
            Logger.Info($"Merkaba held-observation pipeline observation={observation} " +
                $"completed={completed} failure=0x{failureReason:x} " +
                $"quanta={sample.Quanta} timedQuanta={sample.TimedQuanta} " +
                $"unresolvedQuanta={sample.UnresolvedQuanta} " +
                $"timingValid={sample.Valid && sample.TimedQuanta == sample.Quanta} " +
                $"gpuSumMs={sample.GpuMilliseconds:F3} " +
                $"heldWallMs={(Time.realtimeSinceStartupAsDouble - sample.StartedAt) * 1000.0:F3} " +
                $"publicationReadbackMs={sample.PublicationMilliseconds:F3} " +
                "scope=complete-frozen-observation queue=single-serialized-native");
        }

        internal static bool HasJobInFlight => _activeJob != null;

        // Records a four-byte GPU transfer before the current-view cull.
        // The command buffer must be outside a render pass; no CPU count data
        // is uploaded and no readout/page rebuild is implied by head rotation.
        internal static bool RecordFlowerCullReset(CommandBuffer command,
            ComputeBuffer arguments)
        {
            if (command == null || arguments == null) return false;
#if !UNITY_EDITOR && UNITY_ANDROID
            try
            {
                if (Native.GetAbiVersion() != AbiVersion ||
                    Native.FlowerDrawAvailable() == 0) return false;
                IntPtr callback = Native.FlowerCullResetEvent();
                int eventId = Native.FlowerCullResetEventId();
                if (callback == IntPtr.Zero || eventId < 0) return false;
                command.IssuePluginEventAndData(callback, eventId,
                    arguments.GetNativeBufferPtr());
                return true;
            }
            catch (DllNotFoundException) { return false; }
            catch (EntryPointNotFoundException) { return false; }
#else
            return false;
#endif
        }

        // Called immediately before the sole indexed URP DrawProceduralIndirect.
        // No command/count/geometry readback and no per-frame allocation.
        internal static bool RecordFlowerIndirectRegistration(RasterCommandBuffer command,
            ComputeBuffer arguments)
        {
            if (arguments == null) return false;
#if !UNITY_EDITOR && UNITY_ANDROID
            try
            {
                if (Native.GetAbiVersion() != AbiVersion ||
                    Native.FlowerDrawAvailable() == 0) return false;
                command.IssuePluginEventAndData(Native.FlowerDrawEvent(),
                    Native.FlowerDrawEventId(), arguments.GetNativeBufferPtr());
                return true;
            }
            catch (DllNotFoundException) { return false; }
            catch (EntryPointNotFoundException) { return false; }
#else
            // An editor oracle must explicitly supply its own fixture draw;
            // never pretend a one-command wrapper emitted the full page list.
            return false;
#endif
        }

        internal static bool IsAvailable
        {
            get
            {
#if !UNITY_EDITOR && UNITY_ANDROID
                try
                {
                    ConfigureStartup();
                    if (_startupConfigurationFailed) return false;
                    int startup = Native.GetStartupStatus(out uint pipeline, out int error);
                    if (startup != _startupLastState || pipeline != _startupLastPipeline || error != _startupLastError)
                    {
                        _startupLastState = startup;
                        _startupLastPipeline = pipeline;
                        _startupLastError = error;
                        string name = pipeline < PipelineNames.Length ? PipelineNames[pipeline] : "device/cache setup";
                        _startupSummary = startup < 0 ? $"Native startup FAILED: {name}, VkResult={error}"
                            : startup == 2 ? "Native pipelines: ready"
                            : startup == 1 ? $"Native pipelines: compiling {name}"
                            : "Native pipelines: waiting for device/cache path";
                    }
                    if (startup < 0)
                    {
                        if (!_startupFailureLogged)
                        {
                            _startupFailureLogged = true;
                            string name = pipeline < PipelineNames.Length ? PipelineNames[pipeline] : "device/setup";
                            Logger.Error($"Merkaba native startup FAILED: pipeline={name} VkResult={error}; scanner disabled.");
                        }
                        return false;
                    }
                    _startupFailureLogged = false;
                    return startup == 2 && Native.IsAvailable() != 0;
                }
                catch (DllNotFoundException) { return false; }
                catch (EntryPointNotFoundException) { return false; }
#else
                return false;
#endif
            }
        }

        internal static bool TryCreateJob(JobKind kind, uint revision,
            IntPtr[] resources, MerkabaNativeUniformTable uniforms,
            int depthGroupsX, int depthGroupsY, int queryGroups,
            int flowerPassMask, out MerkabaNativeVulkanJob job)
        {
            job = null;
            if (_activeJob != null || revision == 0u || resources == null ||
                resources.Length != ResourceCount || uniforms == null)
                return false;
            ValidateDispatch(depthGroupsX);
            ValidateDispatch(depthGroupsY);
            ValidateDispatch(queryGroups);
            if (kind == JobKind.FlowerReadout)
            {
                if (flowerPassMask <= 0 || (flowerPassMask & ~15) != 0)
                    throw new ArgumentOutOfRangeException(nameof(flowerPassMask));
                if (depthGroupsX != 0 || depthGroupsY != 0 || queryGroups != 0)
                    throw new ArgumentException("Flower jobs dispatch only bounded physical-page stages.");
            }
            else if (flowerPassMask != 0)
                throw new ArgumentOutOfRangeException(nameof(flowerPassMask));
#if !UNITY_EDITOR && UNITY_ANDROID
            if (!IsAvailable) return false;
            uniforms.Build(out UniformValue[] values, out byte[] data);
            GCHandle resourcePin = default;
            GCHandle valuePin = default;
            GCHandle dataPin = default;
            try
            {
                resourcePin = GCHandle.Alloc(resources, GCHandleType.Pinned);
                valuePin = GCHandle.Alloc(values, GCHandleType.Pinned);
                dataPin = GCHandle.Alloc(data, GCHandleType.Pinned);
                var descriptor = new NativeJobDescriptor
                {
                    StructSize = checked((uint)Marshal.SizeOf<NativeJobDescriptor>()),
                    AbiVersion = AbiVersion,
                    Kind = (uint)kind,
                    Revision = revision,
                    ResourceCount = ResourceCount,
                    Resources = resourcePin.AddrOfPinnedObject(),
                    UniformValueCount = checked((uint)values.Length),
                    UniformValues = valuePin.AddrOfPinnedObject(),
                    UniformData = dataPin.AddrOfPinnedObject(),
                    UniformDataSize = checked((uint)data.Length),
                    DepthGroupsX = checked((uint)depthGroupsX),
                    DepthGroupsY = checked((uint)depthGroupsY),
                    QueryGroups = checked((uint)queryGroups),
                    FlowerPassMask = checked((uint)flowerPassMask),
                };
                IntPtr handle = Native.CreateJob(ref descriptor);
                if (handle == IntPtr.Zero) return false;
                uniforms.TryReadUInt("_M8ObservationToken", out uint observation);
                job = new MerkabaNativeVulkanJob(handle, kind, revision, observation);
                _activeJob = job;
                return true;
            }
            finally
            {
                if (dataPin.IsAllocated) dataPin.Free();
                if (valuePin.IsAllocated) valuePin.Free();
                if (resourcePin.IsAllocated) resourcePin.Free();
            }
#else
            return false;
#endif
        }

        private static void ValidateDispatch(int value)
        {
            if (value < 0 || value > 65535)
                throw new ArgumentOutOfRangeException(nameof(value));
        }

        private static void ReleaseActive(MerkabaNativeVulkanJob job)
        {
            if (ReferenceEquals(_activeJob, job)) _activeJob = null;
        }

        private static bool TryClaimTimingLog(JobKind kind)
        {
            int index = (int)kind;
            float now = Time.unscaledTime;
            if (now < NextTimingLogTimes[index]) return false;
            NextTimingLogTimes[index] = now + TimingLogIntervalSeconds;
            return true;
        }

        private static bool ValidTimings(int count, double period, int validBits)
        {
            if (count < 4 || count > MaximumTimestampCount || (count & 1) != 0 ||
                period <= 0.0 || double.IsNaN(period) || double.IsInfinity(period) ||
                validBits <= 0 || validBits > 64) return false;
            for (int index = 0; index < (count - 2) / 2; index++)
                if (PipelineScratch[index] >= PipelineCount) return false;
            ulong mask = validBits == 64 ? ulong.MaxValue : (1UL << validBits) - 1UL;
            ulong total = (TimingScratch[count - 1] - TimingScratch[0]) & mask;
            for (int index = 0; index < (count - 2) / 2; index++)
                if (((TimingScratch[2 + index * 2] - TimingScratch[1 + index * 2]) & mask) > total)
                    return false;
            return true;
        }

        private static void LogTimings(JobKind kind, uint revision, uint observation,
            ulong[] timestamps, uint[] dispatchPipelines, int count, double period, int validBits)
        {
            ulong mask = validBits >= 64 ? ulong.MaxValue :
                validBits <= 0 ? 0UL : (1UL << validBits) - 1UL;
            int dispatchCount = (count - 2) / 2;
            for (int index = 0; index < dispatchCount; ++index)
            {
                ulong begin = timestamps[1 + index * 2] & mask;
                ulong end = timestamps[2 + index * 2] & mask;
                double milliseconds = ((end - begin) & mask) * period /
                    1_000_000.0;
                uint pipeline = dispatchPipelines[index];
                string stage = pipeline < PipelineNames.Length ? PipelineNames[pipeline] :
                    $"INVALID_PIPELINE_{pipeline}";
                Logger.Info($"Merkaba native-queue timing revision={revision} " +
                    $"observation={observation} job={kind} dispatch={index} pipeline={stage} " +
                    $"gpu={milliseconds:F3}ms");
            }
            double total = ((timestamps[count - 1] & mask) -
                (timestamps[0] & mask) & mask) * period / 1_000_000.0;
            Logger.Info($"Merkaba native-queue timing revision={revision} " +
                $"observation={observation} job={kind} total={total:F3}ms dispatches={dispatchCount} " +
                $"validBits={validBits} queue=single-serialized-native");
        }

        private static class Native
        {
#if !UNITY_EDITOR && UNITY_ANDROID
            private const string Library = "MerkabaVulkanTimestamps";

            [DllImport(Library, EntryPoint = "MerkabaFlowerDraw_IsAvailable")]
            internal static extern int FlowerDrawAvailable();
            [DllImport(Library, EntryPoint = "MerkabaFlowerDraw_GetRenderEventFunc")]
            internal static extern IntPtr FlowerDrawEvent();
            [DllImport(Library, EntryPoint = "MerkabaFlowerDraw_GetEventId")]
            internal static extern int FlowerDrawEventId();
            [DllImport(Library, EntryPoint = "MerkabaFlowerDraw_GetCullResetEventFunc")]
            internal static extern IntPtr FlowerCullResetEvent();
            [DllImport(Library, EntryPoint = "MerkabaFlowerDraw_GetCullResetEventId")]
            internal static extern int FlowerCullResetEventId();

            [DllImport(Library, EntryPoint = "MerkabaExecutor_IsAvailable")]
            internal static extern int IsAvailable();
            [DllImport(Library, EntryPoint = "MerkabaExecutor_ConfigureStartup")]
            internal static extern int ConfigureStartup([MarshalAs(UnmanagedType.LPUTF8Str)] string directory);
            [DllImport(Library, EntryPoint = "MerkabaExecutor_GetStartupStatus")]
            internal static extern int GetStartupStatus(out uint pipeline, out int error);
            [DllImport(Library, EntryPoint = "MerkabaExecutor_GetAbiVersion")]
            internal static extern uint GetAbiVersion();
            [DllImport(Library, EntryPoint = "MerkabaExecutor_CreateJob")]
            internal static extern IntPtr CreateJob(
                ref NativeJobDescriptor descriptor);
            [DllImport(Library, EntryPoint = "MerkabaExecutor_CancelJob")]
            internal static extern int CancelJob(IntPtr handle);
            [DllImport(Library,
                EntryPoint = "MerkabaExecutor_GetRenderEventFunc")]
            internal static extern IntPtr GetRenderEventFunc();
            [DllImport(Library, EntryPoint = "MerkabaExecutor_GetEventId")]
            internal static extern int GetEventId(int offset);
            [DllImport(Library, EntryPoint = "MerkabaExecutor_PollJob")]
            internal static extern int PollJob(IntPtr handle, out int error);
            [DllImport(Library, EntryPoint = "MerkabaExecutor_ReadTimings")]
            internal static extern int ReadTimings(IntPtr handle,
                [Out] ulong[] timestamps, int timestampCapacity,
                [Out] uint[] dispatchPipelines, int dispatchCapacity,
                out double timestampPeriod, out int validBits);
            [DllImport(Library, EntryPoint = "MerkabaExecutor_DestroyJob")]
            internal static extern int DestroyJob(IntPtr handle);
#endif
        }

        internal sealed class MerkabaNativeVulkanJob : IDisposable
        {
            private IntPtr _handle;
            private readonly JobKind _kind;
            private readonly uint _revision;
            private readonly uint _observation;
            private bool _sampleObservation;
            private bool _recorded;
            private bool _acquireRecorded;
            private bool _terminal;
            private bool _timingsLogged;

            internal MerkabaNativeVulkanJob(IntPtr handle, JobKind kind,
                uint revision, uint observation = 0u)
            {
                _handle = handle;
                _kind = kind;
                _revision = revision;
                _observation = observation;
            }

            internal uint Revision => _revision;

            internal void RecordPrepareAndSubmit(CommandBuffer command)
            {
                if (command == null) throw new ArgumentNullException(nameof(command));
                if (_handle == IntPtr.Zero || _recorded)
                    throw new InvalidOperationException(
                        "Native Vulkan job recording state is invalid.");
#if !UNITY_EDITOR && UNITY_ANDROID
                IntPtr callback = Native.GetRenderEventFunc();
                int prepareEvent = Native.GetEventId(0);
                int submitEvent = Native.GetEventId(1);
                if (callback == IntPtr.Zero || prepareEvent == 0 ||
                    submitEvent == 0)
                    throw new InvalidOperationException(
                        "Native Vulkan executor events are unavailable.");
                command.IssuePluginEventAndData(callback, prepareEvent, _handle);
                command.IssuePluginEventAndData(callback, submitEvent, _handle);
                _recorded = true;
                if (_kind == JobKind.Observation && _observation != 0u &&
                    Time.realtimeSinceStartupAsDouble >= _nextObservationTimingAt)
                {
                    _nextObservationTimingAt = Time.realtimeSinceStartupAsDouble + TimingLogIntervalSeconds;
                    _observationTiming = new ObservationTiming
                    {
                        Token = _observation,
                        StartedAt = _heldObservationStartedAt > 0.0 ? _heldObservationStartedAt :
                            Time.realtimeSinceStartupAsDouble
                    };
                }
                _sampleObservation = _kind == JobKind.Observation &&
                    _observationTiming != null && _observationTiming.Token == _observation;
                if (_sampleObservation) _observationTiming.Quanta++;
#else
                throw new PlatformNotSupportedException();
#endif
            }

            internal bool Poll(out string error)
            {
                if (_handle == IntPtr.Zero || !_recorded)
                {
                    error = "Native Vulkan job was not submitted.";
                    return false;
                }
#if !UNITY_EDITOR && UNITY_ANDROID
                int status = Native.PollJob(_handle, out int vkError);
                if (status == 0)
                {
                    error = null;
                    return false;
                }
                if (status == 2)
                {
                    if (!_acquireRecorded)
                    {
                        CommandBuffer command = CommandBufferPool.Get(
                            "Merkaba native scanner acquire");
                        try
                        {
                            IntPtr callback = Native.GetRenderEventFunc();
                            int acquireEvent = Native.GetEventId(2);
                            if (callback == IntPtr.Zero || acquireEvent == 0)
                                throw new InvalidOperationException(
                                    "Native Vulkan acquire event is unavailable.");
                            command.IssuePluginEventAndData(callback,
                                acquireEvent, _handle);
                            _acquireRecorded = true;
                            Graphics.ExecuteCommandBuffer(command);
                        }
                        catch (Exception exception)
                        {
                            _acquireRecorded = true;
                            Logger.Error("Merkaba native acquire submission " +
                                "became uncertain; job remains quarantined: " +
                                exception.Message);
                        }
                        finally
                        {
                            CommandBufferPool.Release(command);
                        }
                    }
                    error = null;
                    return false;
                }
                _terminal = true;
                if (status < 0)
                {
                    error = $"Native Vulkan queue completion failed: " +
                        $"VkResult={vkError}.";
                    return true;
                }
                error = null;
                TryLogTimings();
                return true;
#else
                error = "Native Vulkan executor is Android-only.";
                return true;
#endif
            }

            internal void CancelBeforeExecution()
            {
                if (_handle == IntPtr.Zero) return;
#if !UNITY_EDITOR && UNITY_ANDROID
                if (Native.CancelJob(_handle) == 0)
                    throw new InvalidOperationException(
                        "Native Vulkan job could not be cancelled before " +
                        "graphics submission.");
                _handle = IntPtr.Zero;
                _terminal = true;
                ReleaseActive(this);
#endif
            }

            private void TryLogTimings()
            {
                if (_timingsLogged) return;
                _timingsLogged = true;
                bool log = TryClaimTimingLog(_kind);
                if (!log && !_sampleObservation) return;
#if !UNITY_EDITOR && UNITY_ANDROID
                int count = Native.ReadTimings(_handle, TimingScratch,
                    TimingScratch.Length, PipelineScratch, PipelineScratch.Length,
                    out double period, out int validBits);
                bool valid = ValidTimings(count, period, validBits);
                ObservationTiming sample = _sampleObservation ? _observationTiming : null;
                if (sample != null && sample.Token == _observation)
                {
                    sample.Valid &= valid;
                    if (valid)
                    {
                        sample.TimedQuanta++;
                        ulong mask = validBits == 64 ? ulong.MaxValue : (1UL << validBits) - 1UL;
                        sample.GpuMilliseconds += ((TimingScratch[count - 1] - TimingScratch[0]) & mask) * period / 1_000_000.0;
                        for (int index = 0; index < (count - 2) / 2; index++)
                        {
                            int pipeline = (int)PipelineScratch[index];
                            sample.PipelineDispatches[pipeline]++;
                            sample.PipelineMilliseconds[pipeline] +=
                                ((TimingScratch[2 + index * 2] - TimingScratch[1 + index * 2]) & mask) * period / 1_000_000.0;
                        }
                    }
                }
                if (valid && log)
                    LogTimings(_kind, _revision, _observation, TimingScratch, PipelineScratch, count, period, validBits);
                else if (!valid)
                    Logger.Warning("Merkaba native-queue timing unavailable " +
                        $"for revision {_revision}; completion remains valid.");
#endif
            }

            public void Dispose()
            {
                IntPtr handle = _handle;
                if (handle == IntPtr.Zero) return;
#if !UNITY_EDITOR && UNITY_ANDROID
                if (!_recorded)
                {
                    if (Native.CancelJob(handle) != 0)
                    {
                        _handle = IntPtr.Zero;
                        ReleaseActive(this);
                    }
                    return;
                }
                if (_terminal && Native.DestroyJob(handle) != 0)
                {
                    _handle = IntPtr.Zero;
                    ReleaseActive(this);
                }
#endif
            }
        }
    }

    internal sealed class MerkabaNativeUniformTable
    {
        private readonly List<MerkabaNativeVulkanExecutor.UniformValue>
            _values = new();
        private readonly List<byte> _data = new();
        private readonly HashSet<uint> _names = new();
        private MerkabaNativeVulkanExecutor.UniformValue[] _builtValues;
        private byte[] _builtData;
        private bool _serializationDirty = true;

        // CreateJob copies this payload before returning. A new page quantum
        // may reuse the table after its previous job retires; a held immutable
        // observation never calls Reset while its quanta are draining.
        internal void Reset()
        {
            _values.Clear();
            _data.Clear();
            _names.Clear();
            _serializationDirty = true;
        }

        internal void Int(string name, int value) =>
            Write32(Reserve(name, 4), value);
        internal void UInt(string name, uint value) =>
            Int(name, unchecked((int)value));
        internal void Float(string name, float value) =>
            Int(name, BitConverter.SingleToInt32Bits(value));
        internal void UInt2(string name, int x, int y)
        {
            int offset = Reserve(name, 8);
            Write32(offset, x);
            Write32(offset + 4, y);
        }
        internal void Int3(string name, int x, int y, int z)
        {
            int offset = Reserve(name, 12);
            Write32(offset, x);
            Write32(offset + 4, y);
            Write32(offset + 8, z);
        }
        internal void Vector2(string name, Vector2 value)
        {
            int offset = Reserve(name, 8);
            WriteFloat(offset, value.x);
            WriteFloat(offset + 4, value.y);
        }
        internal void Vector3(string name, Vector3 value)
        {
            int offset = Reserve(name, 12);
            WriteFloat(offset, value.x);
            WriteFloat(offset + 4, value.y);
            WriteFloat(offset + 8, value.z);
        }
        internal void Vector4(string name, Vector4 value)
        {
            int offset = Reserve(name, 16);
            WriteFloat(offset, value.x);
            WriteFloat(offset + 4, value.y);
            WriteFloat(offset + 8, value.z);
            WriteFloat(offset + 12, value.w);
        }
        internal void Matrix(string name, Matrix4x4 value) =>
            WriteMatrix(Reserve(name, 64), value);
        internal void Matrices(string name, Matrix4x4[] values)
        {
            if (values == null || values.Length == 0)
                throw new ArgumentException(nameof(values));
            int offset = Reserve(name, checked(values.Length * 64));
            for (int index = 0; index < values.Length; ++index)
                WriteMatrix(offset + index * 64, values[index]);
        }
        internal void Vector3Array(string name, Vector4[] values)
        {
            if (values == null || values.Length == 0)
                throw new ArgumentException(nameof(values));
            int offset = Reserve(name, checked(values.Length * 16));
            for (int index = 0; index < values.Length; ++index)
            {
                WriteFloat(offset + index * 16, values[index].x);
                WriteFloat(offset + index * 16 + 4, values[index].y);
                WriteFloat(offset + index * 16 + 8, values[index].z);
            }
        }
        internal void Vector4Array(string name, Vector4[] values)
        {
            if (values == null || values.Length == 0)
                throw new ArgumentException(nameof(values));
            int offset = Reserve(name, checked(values.Length * 16));
            for (int index = 0; index < values.Length; ++index)
            {
                WriteFloat(offset + index * 16, values[index].x);
                WriteFloat(offset + index * 16 + 4, values[index].y);
                WriteFloat(offset + index * 16 + 8, values[index].z);
                WriteFloat(offset + index * 16 + 12, values[index].w);
            }
        }

        internal void Build(out MerkabaNativeVulkanExecutor.UniformValue[] values,
            out byte[] data)
        {
            // The same immutable observation can span many queue quanta.
            // Serialize once; retries must not rebuild its uniform payload.
            if (_values.Count == 0 || _data.Count == 0)
                throw new InvalidOperationException(
                    "Native scanner job has no uniform ABI values.");
            if (_serializationDirty)
            {
                if (_builtValues == null || _builtValues.Length != _values.Count)
                    _builtValues = new MerkabaNativeVulkanExecutor.UniformValue[_values.Count];
                if (_builtData == null || _builtData.Length != _data.Count)
                    _builtData = new byte[_data.Count];
                _values.CopyTo(_builtValues);
                _data.CopyTo(_builtData);
                _serializationDirty = false;
            }
            values = _builtValues;
            data = _builtData;
        }

        internal bool TryReadUInt(string name, out uint value)
        {
            Build(out var values, out var data);
            uint hash = NameHash(name);
            foreach (var entry in values)
                if (entry.NameHash == hash && entry.Size == sizeof(uint))
                {
                    value = BitConverter.ToUInt32(data, checked((int)entry.Offset));
                    return true;
                }
            value = 0u;
            return false;
        }

        private int Reserve(string name, int size)
        {
            if (string.IsNullOrEmpty(name) || size <= 0)
                throw new ArgumentException("Invalid native uniform value.");
            uint hash = NameHash(name);
            if (!_names.Add(hash))
                throw new InvalidOperationException(
                    $"Duplicate native uniform value: {name}");
            _serializationDirty = true;
            int offset = _data.Count;
            int end = checked(offset + size);
            if (_data.Capacity < end) _data.Capacity = Math.Max(end, _data.Capacity * 2);
            for (int index = offset; index < end; ++index) _data.Add(0);
            _values.Add(new MerkabaNativeVulkanExecutor.UniformValue
            {
                NameHash = hash,
                Offset = checked((uint)offset),
                Size = checked((uint)size),
            });
            return offset;
        }

        private void WriteMatrix(int offset, Matrix4x4 value)
        {
            for (int column = 0; column < 4; ++column)
                for (int row = 0; row < 4; ++row)
                    WriteFloat(offset + column * 16 + row * 4, value[row, column]);
        }

        private static uint NameHash(string value)
        {
            uint hash = 2166136261u;
            for (int index = 0; index < value.Length; ++index)
                hash = (hash ^ checked((byte)value[index])) * 16777619u;
            return hash;
        }

        private void WriteFloat(int offset, float value) =>
            Write32(offset, BitConverter.SingleToInt32Bits(value));

        private void Write32(int offset, int value)
        {
            _data[offset] = unchecked((byte)value);
            _data[offset + 1] = unchecked((byte)(value >> 8));
            _data[offset + 2] = unchecked((byte)(value >> 16));
            _data[offset + 3] = unchecked((byte)(value >> 24));
        }
    }
}
