using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.SigmaPrism
{
    /// <summary>
    /// One disposable pure-eye BACK build on the accepted plugin-owned Vulkan
    /// queue. Queue 0 only records short release/acquire events after terminal
    /// proof; XR keeps drawing the immutable FRONT generation throughout.
    /// </summary>
    internal static class SigmaNativeVulkanReadout
    {
        internal const int AbiVersion = 1;
        internal const int ResourceCount = 14;
        internal const int ConstantBytes = 11 * 4 * sizeof(uint);
        private static ReadoutJob _activeJob;

        internal static bool HasJobInFlight => _activeJob != null;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeDescriptor
        {
            internal uint StructSize;
            internal uint Abi;
            internal uint Revision;
            internal uint ResourcesCount;
            internal IntPtr Resources;
            internal IntPtr Constants;
            internal uint ConstantsSize;
            internal uint PageCapacity0;
            internal uint PageCapacity1;
        }

        internal static ReadoutJob Create(uint revision, IntPtr[] resources,
            byte[] constants, int pageCapacity0, int pageCapacity1)
        {
            if (_activeJob != null ||
                SigmaNativeVulkanExecutor.HasJobInFlight ||
                SigmaNativeVulkanColdEncode.HasJobInFlight ||
                SigmaNativeVulkanColdUpload.HasJobInFlight)
                throw new InvalidOperationException(
                    "N6 readout requires exclusive queue-1 carrier access.");
            if (revision == 0u)
                throw new ArgumentOutOfRangeException(nameof(revision));
            if (resources == null || resources.Length != ResourceCount)
                throw new ArgumentException(
                    $"N6 readout requires {ResourceCount} resources.",
                    nameof(resources));
            if (constants == null || constants.Length != ConstantBytes)
                throw new ArgumentException(
                    "N6 readout constant ABI is not 176 bytes.",
                    nameof(constants));
            if (pageCapacity0 <= 0 || pageCapacity1 <= 0 ||
                pageCapacity0 > 65535 || pageCapacity1 > 65535)
                throw new ArgumentOutOfRangeException(nameof(pageCapacity0));
#if !UNITY_EDITOR && UNITY_ANDROID
            SigmaNativeVulkanExecutor.RequireAvailable();
            if (Native.Abi != AbiVersion)
                throw new InvalidOperationException(
                    "N6 pure readout native ABI is unavailable.");
            GCHandle resourcePin = default;
            GCHandle constantPin = default;
            try
            {
                resourcePin = GCHandle.Alloc(resources, GCHandleType.Pinned);
                constantPin = GCHandle.Alloc(constants, GCHandleType.Pinned);
                var descriptor = new NativeDescriptor
                {
                    StructSize = checked((uint)Marshal.SizeOf<NativeDescriptor>()),
                    Abi = AbiVersion,
                    Revision = revision,
                    ResourcesCount = ResourceCount,
                    Resources = resourcePin.AddrOfPinnedObject(),
                    Constants = constantPin.AddrOfPinnedObject(),
                    ConstantsSize = checked((uint)constants.Length),
                    PageCapacity0 = checked((uint)pageCapacity0),
                    PageCapacity1 = checked((uint)pageCapacity1),
                };
                IntPtr handle = Native.CreateJob(ref descriptor);
                if (handle == IntPtr.Zero)
                    throw new InvalidOperationException(
                        "Native Vulkan queue rejected the N6 readout job.");
                _activeJob = new ReadoutJob(handle, revision);
                return _activeJob;
            }
            finally
            {
                if (constantPin.IsAllocated) constantPin.Free();
                if (resourcePin.IsAllocated) resourcePin.Free();
            }
#else
            throw new PlatformNotSupportedException(
                "Plugin-owned N6 readout exists only on Android Vulkan.");
#endif
        }

        private static void Release(ReadoutJob job)
        {
            if (ReferenceEquals(_activeJob, job))
                _activeJob = null;
        }

        private static class Native
        {
#if !UNITY_EDITOR && UNITY_ANDROID
            private const string Library = "SigmaVulkanTimestamps";
            [DllImport(Library, EntryPoint = "SigmaReadout_GetAbiVersion")]
            private static extern uint GetAbiVersion();
            [DllImport(Library, EntryPoint = "SigmaReadout_CreateJob")]
            internal static extern IntPtr CreateJob(
                ref NativeDescriptor descriptor);
            [DllImport(Library, EntryPoint = "SigmaReadout_CancelJob")]
            internal static extern int CancelJob(IntPtr handle);
            [DllImport(Library,
                EntryPoint = "SigmaReadout_GetRenderEventFunc")]
            private static extern IntPtr GetRenderEventFunc();
            [DllImport(Library, EntryPoint = "SigmaReadout_GetEventId")]
            private static extern int GetEventId(int offset);
            [DllImport(Library, EntryPoint = "SigmaReadout_PollJob")]
            internal static extern int PollJob(IntPtr handle, out int error);
            [DllImport(Library, EntryPoint = "SigmaReadout_DestroyJob")]
            internal static extern int DestroyJob(IntPtr handle);
            internal static uint Abi => GetAbiVersion();
            internal static IntPtr RenderEvent => GetRenderEventFunc();
            internal static int EventId(int offset) => GetEventId(offset);
#else
            internal static uint Abi => 0u;
            internal static IntPtr CreateJob(ref NativeDescriptor descriptor) =>
                IntPtr.Zero;
            internal static int CancelJob(IntPtr handle) => 0;
            internal static int PollJob(IntPtr handle, out int error)
            {
                error = -1;
                return -1;
            }
            internal static int DestroyJob(IntPtr handle) => 0;
            internal static IntPtr RenderEvent => IntPtr.Zero;
            internal static int EventId(int offset) => 0;
#endif
        }

        internal sealed class ReadoutJob : IDisposable
        {
            private IntPtr _handle;
            private readonly uint _revision;
            private bool _recorded;
            private bool _acquireRecorded;
            private bool _terminal;
            private SigmaGpuCompletionStatus _terminalStatus;
            private string _terminalError;

            internal ReadoutJob(IntPtr handle, uint revision)
            {
                _handle = handle;
                _revision = revision;
            }

            internal uint Revision => _revision;

            internal void Record(CommandBuffer command)
            {
                if (command == null) throw new ArgumentNullException(nameof(command));
                if (_handle == IntPtr.Zero || _recorded)
                    throw new InvalidOperationException(
                        "N6 readout recording state is invalid.");
                IntPtr callback = Native.RenderEvent;
                int prepare = Native.EventId(0);
                int submit = Native.EventId(1);
                if (callback == IntPtr.Zero || prepare == 0 || submit == 0)
                    throw new InvalidOperationException(
                        "N6 readout native events are unavailable.");
                command.IssuePluginEventAndData(callback, prepare, _handle);
                command.IssuePluginEventAndData(callback, submit, _handle);
                _recorded = true;
            }

            internal SigmaGpuCompletionStatus Poll(out string error)
            {
                if (_terminal)
                {
                    error = _terminalError;
                    return _terminalStatus;
                }
                int status = Native.PollJob(_handle, out int vkError);
                if (status == 0)
                {
                    error = null;
                    return SigmaGpuCompletionStatus.Pending;
                }
                if (status == 2)
                {
                    if (!_acquireRecorded)
                    {
                        CommandBuffer command = CommandBufferPool.Get(
                            "Sigma N6 readout post-signal acquire");
                        try
                        {
                            IntPtr callback = Native.RenderEvent;
                            int acquire = Native.EventId(2);
                            if (callback == IntPtr.Zero || acquire == 0)
                                throw new InvalidOperationException(
                                    "N6 readout acquire event is unavailable.");
                            command.IssuePluginEventAndData(callback, acquire,
                                _handle);
                            _acquireRecorded = true;
                            Graphics.ExecuteCommandBuffer(command);
                        }
                        catch (Exception exception)
                        {
                            _terminal = true;
                            _terminalStatus = SigmaGpuCompletionStatus.Faulted;
                            _terminalError = "N6 readout acquire became " +
                                "uncertain; BACK is quarantined: " +
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
                _terminal = true;
                if (status < 0)
                {
                    _terminalStatus = SigmaGpuCompletionStatus.Faulted;
                    _terminalError = "Native N6 readout failed: VkResult=" +
                        vkError + ".";
                }
                else
                {
                    _terminalStatus = SigmaGpuCompletionStatus.Complete;
                    _terminalError = null;
                }
                error = _terminalError;
                return _terminalStatus;
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
                        Release(this);
                    }
                    return;
                }
                if (_terminal && Native.DestroyJob(handle) != 0)
                {
                    _handle = IntPtr.Zero;
                    Release(this);
                }
            }
        }
    }
}
