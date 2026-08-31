using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.SigmaPrism
{
    /// <summary>
    /// Thin managed ownership surface for the plugin-owned same-family Vulkan
    /// queue. It has no reconstruction authority: the embedded pipelines are
    /// generated from the same 16 production HLSL entry points, and every Unity
    /// resource remains owned by its existing lease until the native fence closes.
    /// </summary>
    internal static class SigmaNativeVulkanExecutor
    {
        internal const int AbiVersion = 3;
        internal const int ResourceCount = 46;
        internal const int DispatchCount = 16;
        internal const int SliceCount = 7;
        internal const int TimestampCount = DispatchCount * 2 + 2;

        internal enum Resource : int
        {
            ExactGate = 0,
            DepthCalibration,
            RgbCalibration,
            PoseResult,
            NativeFrame,
            Observation,
            CloseScratch,
            States,
            StateDelta,
            GaugeDelta,
            LocalityCertificates,
            Revisions,
            Counters,
            CompletionJournal,
            CarrierState,
            CarrierRepresentation,
            CarrierMetadata,
            CarrierPublicationRoot,
            CarrierDirtyFlags,
            CarrierReadoutDirtyFlags,
            RelationInputs,
            RelationPlans,
            RelationNearIntervals,
            RelationResults,
            RelationFactors,
            RelationHashes,
            RelationNorms,
            BranchHeaders,
            BranchSupports,
            BranchPredictions,
            RawDepth,
            MetricDepth,
            DepthFlags,
            DepthRayCenterLeft,
            DepthRayCenterRight,
            DepthRayDifferentialXLeft,
            DepthRayDifferentialXRight,
            DepthRayDifferentialYLeft,
            DepthRayDifferentialYRight,
            DepthSlopeBoundsLeft,
            DepthSlopeBoundsRight,
            RgbLeft,
            RgbRight,
            PredCarrierPage,
            PredCarrierUvNormal,
            PredStateKey,
        }

        private static readonly string[] StageNames =
        {
            "BuildNativeObservation",
            "ContractNativeQuery.FOOTPRINT",
            "EvaluateNativeRelation.BOUNDARY",
            "ContractNativeQuery.TILE_CLOSE",
            "EvaluateNativeRelation.GLOBAL_CLOSE",
            "PrepareNativeCanonicalSeed",
            "PrepareNativeCanonicalRuns",
            "PrepareNativeRefinementPlan",
            "PrepareNativeCanonicalSelect",
            "PrepareNativeRefinementProof",
            "PrepareNativeComponentOrder",
            "PrepareNativeRefinementScan",
            "PrepareNativeRevision",
            "PrepareNativePage",
            "ScatterNativeState",
            "CloseAndPublishNativeRevision",
        };

        private static SigmaNativeVulkanJob _activeJob;

        internal static bool HasJobInFlight => _activeJob != null;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeJobDescriptor
        {
            internal uint StructSize;
            internal uint Abi;
            internal uint Revision;
            internal uint ResourcesCount;
            internal IntPtr Resources;
            internal IntPtr FrameConstants;
            internal IntPtr ContractConstants;
            internal IntPtr QueryBoundaryConstants;
            internal IntPtr QueryGlobalConstants;
            internal uint FrameConstantsSize;
            internal uint ContractConstantsSize;
            internal uint QueryBoundaryConstantsSize;
            internal uint QueryGlobalConstantsSize;
            internal uint ObservationGroups;
            internal uint FootprintGroupsX;
            internal uint FootprintGroupsY;
            internal uint TileGroups;
            internal uint CompletionRecordIndex;
        }

        internal static void RequireAvailable()
        {
            SigmaGpuCompletion.RequireSupported();
            if (!SystemInfo.supportsAsyncGPUReadback)
                throw new InvalidOperationException(
                    "N4.2R Quest requires asynchronous completion readback.");
#if !UNITY_EDITOR && UNITY_ANDROID
            if (!Native.IsAvailable || Native.Abi != AbiVersion)
                throw new InvalidOperationException(
                    "N4.2R same-family Vulkan queue executor is unavailable; " +
                    "graphics-queue fallback is forbidden.");
#endif
        }

        internal static SigmaNativeVulkanJob CreateJob(uint revision,
            IntPtr[] resources, byte[] frameConstants,
            byte[] contractConstants, byte[] queryBoundaryConstants,
            byte[] queryGlobalConstants, int observationGroups,
            Vector2Int footprintGroups, int tileGroups,
            int completionRecordIndex)
        {
            if (_activeJob != null)
                throw new InvalidOperationException(
                    "A Sigma native Vulkan job is already in flight.");
            if (revision == 0u)
                throw new ArgumentOutOfRangeException(nameof(revision));
            if (resources == null || resources.Length != ResourceCount)
                throw new ArgumentException(
                    $"Native executor requires {ResourceCount} resources.",
                    nameof(resources));
            if (frameConstants == null || frameConstants.Length < 736 ||
                contractConstants == null || contractConstants.Length < 120 ||
                queryBoundaryConstants == null ||
                queryBoundaryConstants.Length < 120 ||
                queryGlobalConstants == null ||
                queryGlobalConstants.Length < 120)
                throw new ArgumentException(
                    "Native executor constant blocks do not match shader ABI.");
            SigmaGpuKernelTelemetry.ValidateDirectDispatchDimensions(
                observationGroups, 1, 1);
            SigmaGpuKernelTelemetry.ValidateDirectDispatchDimensions(
                footprintGroups.x, footprintGroups.y, 1);
            SigmaGpuKernelTelemetry.ValidateDirectDispatchDimensions(
                tileGroups, 1, 1);
            if ((uint)completionRecordIndex >=
                    SigmaNativeCompletionTransfer.RecordsPerBatch)
                throw new ArgumentOutOfRangeException(
                    nameof(completionRecordIndex));
#if !UNITY_EDITOR && UNITY_ANDROID
            RequireAvailable();
            GCHandle resourcePin = default;
            GCHandle framePin = default;
            GCHandle contractPin = default;
            GCHandle boundaryPin = default;
            GCHandle globalPin = default;
            try
            {
                resourcePin = GCHandle.Alloc(resources, GCHandleType.Pinned);
                framePin = GCHandle.Alloc(frameConstants, GCHandleType.Pinned);
                contractPin = GCHandle.Alloc(contractConstants,
                    GCHandleType.Pinned);
                boundaryPin = GCHandle.Alloc(queryBoundaryConstants,
                    GCHandleType.Pinned);
                globalPin = GCHandle.Alloc(queryGlobalConstants,
                    GCHandleType.Pinned);
                var descriptor = new NativeJobDescriptor
                {
                    StructSize = checked((uint)Marshal.SizeOf<NativeJobDescriptor>()),
                    Abi = AbiVersion,
                    Revision = revision,
                    ResourcesCount = ResourceCount,
                    Resources = resourcePin.AddrOfPinnedObject(),
                    FrameConstants = framePin.AddrOfPinnedObject(),
                    ContractConstants = contractPin.AddrOfPinnedObject(),
                    QueryBoundaryConstants = boundaryPin.AddrOfPinnedObject(),
                    QueryGlobalConstants = globalPin.AddrOfPinnedObject(),
                    FrameConstantsSize = checked((uint)frameConstants.Length),
                    ContractConstantsSize = checked((uint)contractConstants.Length),
                    QueryBoundaryConstantsSize = checked(
                        (uint)queryBoundaryConstants.Length),
                    QueryGlobalConstantsSize = checked(
                        (uint)queryGlobalConstants.Length),
                    ObservationGroups = checked((uint)observationGroups),
                    FootprintGroupsX = checked((uint)footprintGroups.x),
                    FootprintGroupsY = checked((uint)footprintGroups.y),
                    TileGroups = checked((uint)tileGroups),
                    CompletionRecordIndex = checked(
                        (uint)completionRecordIndex),
                };
                IntPtr handle = Native.CreateJob(ref descriptor);
                if (handle == IntPtr.Zero)
                    throw new InvalidOperationException(
                        "Native Vulkan executor rejected the exact 16-dispatch job.");
                var job = new SigmaNativeVulkanJob(handle, revision);
                _activeJob = job;
                return job;
            }
            finally
            {
                if (globalPin.IsAllocated) globalPin.Free();
                if (boundaryPin.IsAllocated) boundaryPin.Free();
                if (contractPin.IsAllocated) contractPin.Free();
                if (framePin.IsAllocated) framePin.Free();
                if (resourcePin.IsAllocated) resourcePin.Free();
            }
#else
            throw new PlatformNotSupportedException(
                "The plugin-owned Vulkan queue exists only in Android production.");
#endif
        }

        private static void ReleaseActive(SigmaNativeVulkanJob job)
        {
            if (ReferenceEquals(_activeJob, job))
                _activeJob = null;
        }

        internal static void LogTimings(uint revision, ulong[] timestamps,
            double timestampPeriod, int validBits)
        {
            ulong mask = validBits >= 64
                ? ulong.MaxValue
                : validBits <= 0 ? 0UL : (1UL << validBits) - 1UL;
            double activeGpuMilliseconds = 0.0;
            for (int index = 0; index < DispatchCount; ++index)
            {
                ulong begin = timestamps[1 + index * 2] & mask;
                ulong end = timestamps[2 + index * 2] & mask;
                double milliseconds = ((end - begin) & mask) *
                    timestampPeriod / 1_000_000.0;
                activeGpuMilliseconds += milliseconds;
                Logger.Info($"Sigma native-queue timing revision={revision} " +
                    $"stage={StageNames[index]} gpu={milliseconds:F3}ms");
            }
            double total = ((timestamps[TimestampCount - 1] & mask) -
                (timestamps[0] & mask) & mask) * timestampPeriod / 1_000_000.0;
            Logger.Info($"Sigma native-queue timing revision={revision} " +
                $"total={total:F3}ms activeGpu={activeGpuMilliseconds:F3}ms " +
                $"dispatches={DispatchCount} slices={SliceCount} " +
                $"validBits={validBits} queue=plugin-owned-background");
        }

        private static class Native
        {
#if !UNITY_EDITOR && UNITY_ANDROID
            private const string Library = "SigmaVulkanTimestamps";

            [DllImport(Library, EntryPoint = "SigmaExecutor_IsAvailable")]
            private static extern int IsAvailableNative();
            [DllImport(Library, EntryPoint = "SigmaExecutor_GetAbiVersion")]
            private static extern uint GetAbiVersionNative();
            [DllImport(Library, EntryPoint = "SigmaExecutor_CreateJob")]
            internal static extern IntPtr CreateJob(
                ref NativeJobDescriptor descriptor);
            [DllImport(Library, EntryPoint = "SigmaExecutor_CancelJob")]
            internal static extern int CancelJob(IntPtr handle);
            [DllImport(Library, EntryPoint = "SigmaExecutor_GetRenderEventFunc")]
            private static extern IntPtr GetRenderEventFuncNative();
            [DllImport(Library, EntryPoint = "SigmaExecutor_GetEventId")]
            private static extern int GetEventIdNative(int offset);
            [DllImport(Library, EntryPoint = "SigmaExecutor_PollJob")]
            internal static extern int PollJob(IntPtr handle, out int error);
            [DllImport(Library,
                EntryPoint = "SigmaExecutor_SubmitNextSlice")]
            internal static extern int SubmitNextSlice(IntPtr handle);
            [DllImport(Library, EntryPoint = "SigmaExecutor_ReadTimings")]
            internal static extern int ReadTimings(IntPtr handle,
                [Out] ulong[] timestamps, int timestampCapacity,
                out double timestampPeriod, out int validBits);
            [DllImport(Library, EntryPoint = "SigmaExecutor_ReadCompletion")]
            internal static extern int ReadCompletion(IntPtr handle,
                [Out] uint[] words, int wordCapacity);
            [DllImport(Library, EntryPoint = "SigmaExecutor_DestroyJob")]
            internal static extern int DestroyJob(IntPtr handle);

            internal static bool IsAvailable
            {
                get
                {
                    try { return IsAvailableNative() != 0; }
                    catch (DllNotFoundException) { return false; }
                    catch (EntryPointNotFoundException) { return false; }
                }
            }
            internal static uint Abi
            {
                get
                {
                    try { return GetAbiVersionNative(); }
                    catch (DllNotFoundException) { return 0u; }
                    catch (EntryPointNotFoundException) { return 0u; }
                }
            }
            internal static IntPtr RenderEvent => GetRenderEventFuncNative();
            internal static int EventId(int offset) => GetEventIdNative(offset);
#else
            internal static bool IsAvailable => false;
            internal static uint Abi => 0u;
            internal static IntPtr CreateJob(ref NativeJobDescriptor descriptor) =>
                IntPtr.Zero;
            internal static int CancelJob(IntPtr handle) => 0;
            internal static int PollJob(IntPtr handle, out int error)
            {
                error = -1;
                return -1;
            }
            internal static int SubmitNextSlice(IntPtr handle) => 0;
            internal static int ReadTimings(IntPtr handle, ulong[] timestamps,
                int timestampCapacity, out double timestampPeriod,
                out int validBits)
            {
                timestampPeriod = 0.0;
                validBits = 0;
                return 0;
            }
            internal static int ReadCompletion(IntPtr handle, uint[] words,
                int wordCapacity) => 0;
            internal static int DestroyJob(IntPtr handle) => 0;
            internal static IntPtr RenderEvent => IntPtr.Zero;
            internal static int EventId(int offset) => 0;
#endif
        }

        internal sealed class SigmaNativeVulkanJob : IDisposable
        {
            private IntPtr _handle;
            private readonly uint _revision;
            private bool _recorded;
            private bool _prepareRecorded;
            private bool _acquireRecorded;
            private bool _terminal;
            private bool _timingsLogged;
            private bool _sliceContinuationPending;
            private int _continuationFrame;
            private int _submittedSlices;
            private SigmaGpuCompletionStatus _terminalStatus;
            private string _terminalError;

            internal SigmaNativeVulkanJob(IntPtr handle, uint revision)
            {
                _handle = handle;
                _revision = revision;
            }

            internal uint Revision => _revision;

            internal void RecordPrepare(CommandBuffer command)
            {
                if (command == null)
                    throw new ArgumentNullException(nameof(command));
                if (_handle == IntPtr.Zero || _recorded || _prepareRecorded)
                    throw new InvalidOperationException(
                        "Native Vulkan job recording state is invalid.");
                IntPtr callback = Native.RenderEvent;
                int prepareEvent = Native.EventId(0);
                if (callback == IntPtr.Zero || prepareEvent == 0)
                    throw new InvalidOperationException(
                        "Native Vulkan executor events are unavailable.");
                command.IssuePluginEventAndData(callback, prepareEvent, _handle);
                _prepareRecorded = true;
            }

            internal void RecordSubmit(CommandBuffer command)
            {
                if (command == null)
                    throw new ArgumentNullException(nameof(command));
                if (_handle == IntPtr.Zero || _recorded || !_prepareRecorded)
                    throw new InvalidOperationException(
                        "Native Vulkan job submit state is invalid.");
                IntPtr callback = Native.RenderEvent;
                int submitEvent = Native.EventId(1);
                if (callback == IntPtr.Zero || submitEvent == 0)
                    throw new InvalidOperationException(
                        "Native Vulkan executor submit event is unavailable.");
                command.IssuePluginEventAndData(callback, submitEvent, _handle);
                _recorded = true;
                _submittedSlices = 1;
            }

            internal SigmaGpuCompletionStatus Poll(out string error)
            {
                if (_terminal)
                {
                    error = _terminalError;
                    return _terminalStatus;
                }
                if (_handle == IntPtr.Zero || !_recorded)
                {
                    error = "Native Vulkan job was not submitted.";
                    return SigmaGpuCompletionStatus.Faulted;
                }
                int status = Native.PollJob(_handle, out int vkError);
                if (status == 0)
                {
                    error = null;
                    return SigmaGpuCompletionStatus.Pending;
                }
                if (status == 2)
                {
                    if (_submittedSlices != SliceCount)
                    {
                        _terminal = true;
                        _terminalStatus = SigmaGpuCompletionStatus.Faulted;
                        _terminalError =
                            "Sigma native completion arrived before every " +
                            $"fixed slice: {_submittedSlices}/{SliceCount}.";
                        error = _terminalError;
                        return _terminalStatus;
                    }
                    if (!_acquireRecorded)
                    {
                        CommandBuffer command = CommandBufferPool.Get(
                            "Sigma native close acquire");
                        try
                        {
                            IntPtr callback = Native.RenderEvent;
                            int acquireEvent = Native.EventId(2);
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
                            _terminal = true;
                            _terminalStatus = SigmaGpuCompletionStatus.Faulted;
                            _terminalError =
                                "Sigma native acquire submission became " +
                                "uncertain; job remains quarantined: " +
                                exception.Message;
                            error = _terminalError;
                            return _terminalStatus;
                        }
                        finally
                        {
                            CommandBufferPool.Release(command);
                        }
                    }
                    error = null;
                    return SigmaGpuCompletionStatus.Pending;
                }
                if (status == 3)
                {
                    if (_submittedSlices >= SliceCount)
                    {
                        _terminal = true;
                        _terminalStatus = SigmaGpuCompletionStatus.Faulted;
                        _terminalError =
                            "Sigma native executor requested a slice beyond " +
                            $"the fixed {SliceCount}-slice schedule.";
                        error = _terminalError;
                        return _terminalStatus;
                    }
                    if (!_sliceContinuationPending)
                    {
                        _sliceContinuationPending = true;
                        _continuationFrame = Time.frameCount + 1;
                        error = null;
                        return SigmaGpuCompletionStatus.Pending;
                    }
                    if (Time.frameCount < _continuationFrame)
                    {
                        error = null;
                        return SigmaGpuCompletionStatus.Pending;
                    }
                    if (Native.SubmitNextSlice(_handle) == 0)
                    {
                        Native.PollJob(_handle, out int continuationError);
                        _terminal = true;
                        _terminalStatus = SigmaGpuCompletionStatus.Faulted;
                        _terminalError =
                            "Sigma native slice continuation failed: " +
                            $"VkResult={continuationError}.";
                        error = _terminalError;
                        return _terminalStatus;
                    }
                    _sliceContinuationPending = false;
                    _submittedSlices++;
                    error = null;
                    return SigmaGpuCompletionStatus.Pending;
                }
                _terminal = true;
                if (status < 0)
                {
                    _terminalStatus = SigmaGpuCompletionStatus.Faulted;
                    _terminalError =
                        $"Native Vulkan queue completion failed: " +
                        $"VkResult={vkError}.";
                    error = _terminalError;
                    return _terminalStatus;
                }
                TryLogTimings();
                _terminalStatus = SigmaGpuCompletionStatus.Complete;
                _terminalError = null;
                error = _terminalError;
                return _terminalStatus;
            }

            internal bool TryReadCompletion(
                out SigmaFrameUInt2Gpu[] completion)
            {
                completion = null;
                if (_handle == IntPtr.Zero || !_terminal)
                    return false;
                var raw = new uint[
                    SigmaGeneratedFrame.CompletionWordCount * 2];
                int count = Native.ReadCompletion(_handle, raw, raw.Length);
                if (count != raw.Length)
                    return false;
                completion = new SigmaFrameUInt2Gpu[
                    SigmaGeneratedFrame.CompletionWordCount];
                for (int index = 0; index < completion.Length; ++index)
                {
                    completion[index].X = raw[index * 2];
                    completion[index].Y = raw[index * 2 + 1];
                }
                return true;
            }

            internal void CancelBeforeExecution()
            {
                IntPtr handle = _handle;
                if (handle == IntPtr.Zero)
                    return;
                if (Native.CancelJob(handle) == 0)
                    throw new InvalidOperationException(
                        "Native Vulkan job could not be cancelled before " +
                        "graphics submission.");
                _handle = IntPtr.Zero;
                _terminal = true;
                _terminalStatus = SigmaGpuCompletionStatus.Faulted;
                _terminalError = "Native Vulkan job was cancelled before " +
                    "execution.";
                ReleaseActive(this);
            }

            private void TryLogTimings()
            {
                if (_timingsLogged)
                    return;
                var timestamps = new ulong[TimestampCount];
                int count = Native.ReadTimings(_handle, timestamps,
                    timestamps.Length, out double period, out int validBits);
                if (count == TimestampCount)
                    LogTimings(_revision, timestamps, period, validBits);
                else
                    Logger.Warning($"Sigma native-queue timing unavailable " +
                        $"for revision {_revision}; completion remains valid.");
                _timingsLogged = true;
            }

            public void Dispose()
            {
                IntPtr handle = _handle;
                if (handle == IntPtr.Zero)
                    return;
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
            }
        }
    }

    internal sealed class SigmaNativeConstantBlock
    {
        internal SigmaNativeConstantBlock(int size)
        {
            if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
            Bytes = new byte[size];
        }

        internal byte[] Bytes { get; }

        internal void UInt(int offset, uint value) =>
            Write32(offset, unchecked((int)value));

        internal void Int(int offset, int value) => Write32(offset, value);

        internal void UInt2(int offset, int x, int y)
        {
            Int(offset, x);
            Int(offset + 4, y);
        }

        internal void UInt4(int offset, uint x, uint y, uint z, uint w)
        {
            UInt(offset, x);
            UInt(offset + 4, y);
            UInt(offset + 8, z);
            UInt(offset + 12, w);
        }

        internal void Float4(int offset, Vector4 value)
        {
            Float(offset, value.x);
            Float(offset + 4, value.y);
            Float(offset + 8, value.z);
            Float(offset + 12, value.w);
        }

        internal void Matrix(int offset, Matrix4x4 value)
        {
            for (int row = 0; row < 4; ++row)
                for (int column = 0; column < 4; ++column)
                    Float(offset + row * 16 + column * 4,
                        value[row, column]);
        }

        internal void Std140UIntArray(int offset, int[] values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            for (int index = 0; index < values.Length; ++index)
                Int(offset + index * 16, values[index]);
        }

        private void Float(int offset, float value) => Write32(offset,
            BitConverter.SingleToInt32Bits(value));

        private void Write32(int offset, int value)
        {
            if ((uint)offset > (uint)(Bytes.Length - sizeof(int)))
                throw new ArgumentOutOfRangeException(nameof(offset));
            byte[] source = BitConverter.GetBytes(value);
            Buffer.BlockCopy(source, 0, Bytes, offset, sizeof(int));
        }
    }
}
