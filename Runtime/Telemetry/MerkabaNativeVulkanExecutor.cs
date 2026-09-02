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
        internal const int AbiVersion = 1;
        internal const int ResourceCount = 45;
        internal const int PipelineCount = 51;
        internal const int MaximumTimestampCount = PipelineCount * 2 + 2;

        internal enum JobKind : uint
        {
            ObservationNew = 0,
            ObservationRetry = 1,
            Readout = 2,
            MeshReadout = 3,
            FineErase = 4,
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
            SurfaceCandidates,
            SurfaceQueue,
            SurfaceWinnerRanks0,
            SurfaceWinnerRanks1,
            SurfaceWinnerRanks2,
            SurfaceWinnerRanks3,
            TouchedTileQueue,
            CarveTiles,
            ObservationDispatchArgs,
            CarveDispatchArgs,
            AttemptCompletion,
            RefineMetrics,
            RawDepth,
            RefinedDepth,
            Normals,
            DilationA,
            DilationB,
            CameraLeft,
            CameraRight,
            VisibleTiles,
            FrameDispatchArgs,
            ReadoutVertices0,
            ReadoutVertices1,
            ReadoutIndices,
            DrawArgs,
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
            internal uint ReadoutQueryGroups;
        }

        private static readonly string[] PipelineNames =
        {
            "StereoRgbdRefine",
            "InitDepthDilation",
            "DilateDepthStep[8]",
            "DilateDepthStep[7]",
            "DilateDepthStep[6]",
            "DilateDepthStep[5]",
            "DilateDepthStep[4]",
            "DilateDepthStep[3]",
            "DilateDepthStep[2]",
            "DilateDepthStep[1]",
            "DilateDepthStep[0]",
            "ResetObservationCounters",
            "DiscoverSurfaceCandidates",
            "PrepareResolveArgs",
            "ResolveSurfaceBlocks",
            "PublishNewBlocks",
            "ResolveSurfaceChunks",
            "PublishNewChunks",
            "ResolveSurfaceTiles",
            "RetryPendingNewTiles",
            "PrepareNewTileDispatchArgs",
            "InitializeNewTiles",
            "ResetClaimQueueCounts",
            "InitializeSurfaceWinners",
            "SelectSurfaceWinners",
            "QueueResolvedSurfaceCandidates",
            "QueryCarveTiles",
            "PrepareIntegrateArgs",
            "IntegrateSurfaceCandidates",
            "PrepareCarveArgs",
            "IntegrateCarveTiles",
            "FinalizeObservation",
            "ClearTouchedSurfaceCandidates",
            "ResetReadoutBuild",
            "QueryM8Readout",
            "PrepareReadoutBuild",
            "ProjectReadoutFrontDepth",
            "IndexReadoutVertices",
            "BuildReadoutVertices",
            "FinalizeReadout",
            "MeshResetReadoutBuild",
            "MeshQueryM8Readout",
            "MeshPrepareReadoutBuild",
            "ProjectReadoutMeshPins",
            "BuildReadoutMesh",
            "MeshFinalizeReadout",
            "ResetFineErase",
            "QueryFineEraseTiles",
            "PrepareFineEraseArgs",
            "EraseFineTiles",
            "FinalizeFineErase",
        };

        private static MerkabaNativeVulkanJob _activeJob;

        internal static bool HasJobInFlight => _activeJob != null;

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
            int readoutQueryGroups, out MerkabaNativeVulkanJob job)
        {
            job = null;
            if (_activeJob != null || revision == 0u || resources == null ||
                resources.Length != ResourceCount || uniforms == null)
                return false;
            ValidateDispatch(depthGroupsX);
            ValidateDispatch(depthGroupsY);
            ValidateDispatch(queryGroups);
            ValidateDispatch(readoutQueryGroups);
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
                    ReadoutQueryGroups = checked((uint)readoutQueryGroups),
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

        private static void LogTimings(JobKind kind, uint revision,
            ulong[] timestamps, int count, double period, int validBits)
        {
            ulong mask = validBits >= 64 ? ulong.MaxValue :
                validBits <= 0 ? 0UL : (1UL << validBits) - 1UL;
            int first = kind == JobKind.Readout ? 33 :
                kind == JobKind.MeshReadout ? 40 :
                kind == JobKind.FineErase ? 46 :
                kind == JobKind.ObservationRetry ? 13 : 0;
            int dispatchCount = (count - 2) / 2;
            for (int index = 0; index < dispatchCount; ++index)
            {
                ulong begin = timestamps[1 + index * 2] & mask;
                ulong end = timestamps[2 + index * 2] & mask;
                double milliseconds = ((end - begin) & mask) * period /
                    1_000_000.0;
                Logger.Info($"Merkaba native-queue timing revision={revision} " +
                    $"stage={PipelineNames[first + index]} " +
                    $"gpu={milliseconds:F3}ms");
            }
            double total = ((timestamps[count - 1] & mask) -
                (timestamps[0] & mask) & mask) * period / 1_000_000.0;
            Logger.Info($"Merkaba native-queue timing revision={revision} " +
                $"total={total:F3}ms dispatches={dispatchCount} " +
                $"validBits={validBits} queue=plugin-owned-background");
        }

        private static class Native
        {
#if !UNITY_EDITOR && UNITY_ANDROID
            private const string Library = "MerkabaVulkanTimestamps";

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
#if !UNITY_EDITOR && UNITY_ANDROID
                var timestamps = new ulong[MaximumTimestampCount];
                int count = Native.ReadTimings(_handle, timestamps,
                    timestamps.Length, out double period, out int validBits);
                if (count >= 4 && (count & 1) == 0)
                    LogTimings(_kind, _revision, timestamps, count, period,
                        validBits);
                else
                    Logger.Warning("Merkaba native-queue timing unavailable " +
                        $"for revision {_revision}; completion remains valid.");
#endif
                _timingsLogged = true;
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
            values = _values.ToArray();
            data = _data.ToArray();
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
