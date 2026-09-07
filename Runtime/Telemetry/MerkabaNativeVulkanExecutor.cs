using System;
using System.Collections.Generic;
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
        internal const int AbiVersion = 10;
        internal const int ResourceCount = 42;
        internal const int PipelineCount = 25;
        // A native observation also dispatches publication Reserve once and
        // its three allocation barriers after DrainObservationRefinement.
        internal const int MaximumDispatchTimingCount = PipelineCount + 4;
        internal const int MaximumTimestampCount = MaximumDispatchTimingCount * 2 + 2;

        internal enum JobKind : uint
        {
            ObservationNew = 0,
            ObservationRetry = 1,
            FlowerReadout = 2,
            FineErase = 3,
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
            "ResetObservationCounters",
            "ResetObservationBins",
            "CountObservationBins",
            "ResolveMissingSpatialNodes",
            "ResolveObservationTileRequests",
            "InitializeNewTiles",
            "ReserveObservationBins",
            "EmitObservationBins",
            "UpdateObservationDual",
            "FlowerCommit",
            "DrainObservationRefinement",
            "FinalizeObservation",
            "RetireObservationBins",
            "ClassifyHotFlowerPages",
            "CompactDirtyFlowerSymbols",
            "PublishDirtyFlowerPages",
            "CullFlowerPages",
            "ResetFineErase",
            "QueryFineEraseTiles",
            "PrepareFineEraseArgs",
            "EraseFineTiles",
            "FinalizeFineErase",
        };

        private static MerkabaNativeVulkanJob _activeJob;
        private static readonly float[] NextTimingLogTimes =
            new float[4];

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
                    return Native.IsAvailable() != 0 &&
                        Native.GetAbiVersion() == AbiVersion;
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
                job = new MerkabaNativeVulkanJob(handle, kind, revision);
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

        private static void LogTimings(JobKind kind, uint revision,
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
                    $"job={kind} dispatch={index} pipeline={stage} " +
                    $"gpu={milliseconds:F3}ms");
            }
            double total = ((timestamps[count - 1] & mask) -
                (timestamps[0] & mask) & mask) * period / 1_000_000.0;
            Logger.Info($"Merkaba native-queue timing revision={revision} " +
                $"job={kind} total={total:F3}ms dispatches={dispatchCount} " +
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
            private bool _recorded;
            private bool _acquireRecorded;
            private bool _terminal;
            private bool _timingsLogged;

            internal MerkabaNativeVulkanJob(IntPtr handle, JobKind kind,
                uint revision)
            {
                _handle = handle;
                _kind = kind;
                _revision = revision;
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
                if (!TryClaimTimingLog(_kind)) return;
#if !UNITY_EDITOR && UNITY_ANDROID
                var timestamps = new ulong[MaximumTimestampCount];
                var dispatchPipelines = new uint[MaximumDispatchTimingCount];
                int count = Native.ReadTimings(_handle, timestamps,
                    timestamps.Length, dispatchPipelines, dispatchPipelines.Length,
                    out double period, out int validBits);
                if (count >= 4 && count <= timestamps.Length && (count & 1) == 0)
                    LogTimings(_kind, _revision, timestamps, dispatchPipelines, count, period,
                        validBits);
                else
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

        internal void Int(string name, int value) => Add(name,
            BitConverter.GetBytes(value));
        internal void UInt(string name, uint value) => Add(name,
            BitConverter.GetBytes(value));
        internal void Float(string name, float value) => Add(name,
            BitConverter.GetBytes(value));
        internal void UInt2(string name, int x, int y)
        {
            byte[] bytes = new byte[8];
            Write32(bytes, 0, x);
            Write32(bytes, 4, y);
            Add(name, bytes);
        }
        internal void Int3(string name, int x, int y, int z)
        {
            byte[] bytes = new byte[12];
            Write32(bytes, 0, x);
            Write32(bytes, 4, y);
            Write32(bytes, 8, z);
            Add(name, bytes);
        }
        internal void Vector2(string name, Vector2 value)
        {
            byte[] bytes = new byte[8];
            WriteFloat(bytes, 0, value.x);
            WriteFloat(bytes, 4, value.y);
            Add(name, bytes);
        }
        internal void Vector3(string name, Vector3 value)
        {
            byte[] bytes = new byte[12];
            WriteFloat(bytes, 0, value.x);
            WriteFloat(bytes, 4, value.y);
            WriteFloat(bytes, 8, value.z);
            Add(name, bytes);
        }
        internal void Vector4(string name, Vector4 value)
        {
            byte[] bytes = new byte[16];
            WriteFloat(bytes, 0, value.x);
            WriteFloat(bytes, 4, value.y);
            WriteFloat(bytes, 8, value.z);
            WriteFloat(bytes, 12, value.w);
            Add(name, bytes);
        }
        internal void Matrix(string name, Matrix4x4 value) => Add(name,
            MatrixBytes(new[] { value }));
        internal void Matrices(string name, Matrix4x4[] values) => Add(name,
            MatrixBytes(values));
        internal void Vector3Array(string name, Vector4[] values)
        {
            byte[] bytes = new byte[values.Length * 16];
            for (int index = 0; index < values.Length; ++index)
            {
                WriteFloat(bytes, index * 16, values[index].x);
                WriteFloat(bytes, index * 16 + 4, values[index].y);
                WriteFloat(bytes, index * 16 + 8, values[index].z);
            }
            Add(name, bytes);
        }
        internal void Vector4Array(string name, Vector4[] values)
        {
            byte[] bytes = new byte[values.Length * 16];
            for (int index = 0; index < values.Length; ++index)
            {
                WriteFloat(bytes, index * 16, values[index].x);
                WriteFloat(bytes, index * 16 + 4, values[index].y);
                WriteFloat(bytes, index * 16 + 8, values[index].z);
                WriteFloat(bytes, index * 16 + 12, values[index].w);
            }
            Add(name, bytes);
        }

        internal void Build(out MerkabaNativeVulkanExecutor.UniformValue[] values,
            out byte[] data)
        {
            // The same immutable observation can span many queue quanta.
            // Serialize once; retries must not rebuild its uniform payload.
            values = _builtValues ??= _values.ToArray();
            data = _builtData ??= _data.ToArray();
            if (values.Length == 0 || data.Length == 0)
                throw new InvalidOperationException(
                    "Native scanner job has no uniform ABI values.");
        }

        private void Add(string name, byte[] bytes)
        {
            if (string.IsNullOrEmpty(name) || bytes == null || bytes.Length == 0)
                throw new ArgumentException("Invalid native uniform value.");
            uint hash = NameHash(name);
            if (!_names.Add(hash))
                throw new InvalidOperationException(
                    $"Duplicate native uniform value: {name}");
            _builtValues = null;
            _builtData = null;
            int offset = _data.Count;
            _data.AddRange(bytes);
            _values.Add(new MerkabaNativeVulkanExecutor.UniformValue
            {
                NameHash = hash,
                Offset = checked((uint)offset),
                Size = checked((uint)bytes.Length),
            });
        }

        private static byte[] MatrixBytes(Matrix4x4[] values)
        {
            if (values == null || values.Length == 0)
                throw new ArgumentException(nameof(values));
            byte[] bytes = new byte[values.Length * 64];
            for (int matrix = 0; matrix < values.Length; ++matrix)
                for (int row = 0; row < 4; ++row)
                    for (int column = 0; column < 4; ++column)
                        WriteFloat(bytes,
                            matrix * 64 + column * 16 + row * 4,
                            values[matrix][row, column]);
            return bytes;
        }

        private static uint NameHash(string value)
        {
            uint hash = 2166136261u;
            for (int index = 0; index < value.Length; ++index)
                hash = (hash ^ checked((byte)value[index])) * 16777619u;
            return hash;
        }

        private static void WriteFloat(byte[] target, int offset, float value) =>
            Write32(target, offset, BitConverter.SingleToInt32Bits(value));

        private static void Write32(byte[] target, int offset, int value)
        {
            byte[] source = BitConverter.GetBytes(value);
            Buffer.BlockCopy(source, 0, target, offset, sizeof(int));
        }
    }
}
