using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.SigmaPrism
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct SigmaCarrierPageMetadataGpu
    {
        internal uint PageXLo;
        internal uint PageXHi;
        internal uint PageYLo;
        internal uint PageYHi;
        internal uint Generation;
        internal uint Revision;
        internal uint CertificateOffsetLo;
        internal uint CertificateOffsetHi;
        internal uint CertificateCount;
        internal uint Flags;
        internal uint GaugeGeneration;
        internal uint CertificateGeneration;
        internal uint RepresentationFlags;
        internal uint RepresentationFingerprint;
        internal uint ActiveSampleCount;
        internal uint StateGeneration;

        internal static SigmaCarrierPageMetadataGpu FromPage(
            SigmaDecodedPage page, uint pageGeneration)
        {
            if (page == null) throw new ArgumentNullException(nameof(page));
            if (page.Generation == 0u || pageGeneration == 0u)
                throw new ArgumentOutOfRangeException(nameof(pageGeneration));
            ulong certificate = page.CertificateOffset;
            return new SigmaCarrierPageMetadataGpu
            {
                PageXLo = unchecked((uint)page.Coordinate.X),
                PageXHi = unchecked((uint)(page.Coordinate.X >> 32)),
                PageYLo = unchecked((uint)page.Coordinate.Y),
                PageYHi = unchecked((uint)(page.Coordinate.Y >> 32)),
                Generation = pageGeneration,
                Revision = page.Revision,
                CertificateOffsetLo = unchecked((uint)certificate),
                CertificateOffsetHi = unchecked((uint)(certificate >> 32)),
                CertificateCount = page.CertificateCount,
                Flags = 1u | 2u,
                GaugeGeneration = page.GaugeGeneration,
                CertificateGeneration = page.CertificateGeneration,
                RepresentationFlags = page.RepresentationFlags,
                RepresentationFingerprint = FingerprintWord0(
                    SigmaGeneratedFrame.ChiFingerprint) ^ FingerprintWord0(
                    SigmaGeneratedFrame.KappaFingerprint) ^ FingerprintWord0(
                    SigmaGeneratedFrame.CertificateFingerprint),
                ActiveSampleCount = page.ActiveSampleCount,
                StateGeneration = page.Generation,
            };
        }

        private static uint FingerprintWord0(string fingerprint) =>
            Convert.ToUInt32(fingerprint.Substring(0, 8), 16);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SigmaColdUploadPageGpu
    {
        internal uint CurrentSlot;
        internal uint ShadowSlot;
        internal uint CurrentGlobalSlot;
        internal uint ShadowGlobalSlot;
        internal SigmaCarrierPageMetadataGpu Metadata;
        internal SigmaResidentSlotGpu CurrentDenseSlot;
        internal SigmaResidentSlotGpu ShadowDenseSlot;

        internal static SigmaColdUploadPageGpu Create(SigmaDecodedPage page,
            SigmaResidencyKey key, uint residentGeneration, int segment,
            int currentSlot, int currentGlobalSlot, int shadowGlobalSlot)
        {
            int shadowSlot = currentSlot ^ 1;
            SigmaResidentSlotGpu current = Dense(key, residentGeneration,
                segment, currentSlot, SigmaResidencyState.Loading, 0u);
            SigmaResidentSlotGpu shadow = Dense(key, residentGeneration,
                segment, shadowSlot, SigmaResidencyState.Loading, 1u);
            return new SigmaColdUploadPageGpu
            {
                CurrentSlot = checked((uint)currentSlot),
                ShadowSlot = checked((uint)shadowSlot),
                CurrentGlobalSlot = checked((uint)currentGlobalSlot),
                ShadowGlobalSlot = checked((uint)shadowGlobalSlot),
                Metadata = SigmaCarrierPageMetadataGpu.FromPage(page,
                    key.PageGeneration),
                CurrentDenseSlot = current,
                ShadowDenseSlot = shadow,
            };
        }

        private static SigmaResidentSlotGpu Dense(SigmaResidencyKey key,
            uint residentGeneration, int segment, int slot,
            SigmaResidencyState state, uint flags)
        {
            ulong root = key.RootContext;
            return new SigmaResidentSlotGpu
            {
                PageXLo = unchecked((uint)key.Coordinate.X),
                PageXHi = unchecked((uint)(key.Coordinate.X >> 32)),
                PageYLo = unchecked((uint)key.Coordinate.Y),
                PageYHi = unchecked((uint)(key.Coordinate.Y >> 32)),
                RootContextLo = unchecked((uint)root),
                RootContextHi = unchecked((uint)(root >> 32)),
                PageGeneration = key.PageGeneration,
                ResidentGeneration = residentGeneration,
                Segment = checked((uint)segment),
                Slot = checked((uint)slot),
                State = (uint)state,
                LocatorBucket = uint.MaxValue,
                LastReaderTicket = 0u,
                LastWriterTicket = 0u,
                LeaseCount = 1u,
                Flags = flags,
            };
        }
    }

    internal sealed class SigmaColdDecodedBatch
    {
        internal SigmaColdDecodedBatch(long[] state, uint[] representation,
            SigmaColdUploadPageGpu[] pages)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            Representation = representation ?? throw new ArgumentNullException(
                nameof(representation));
            Pages = pages ?? throw new ArgumentNullException(nameof(pages));
            if (pages.Length == 0 || pages.Length >
                SigmaNativeVulkanColdUpload.MaximumPages ||
                state.Length != pages.Length * SigmaCarrier.PageLaneCount ||
                representation.Length != pages.Length *
                    SigmaCarrier.SamplesPerPage *
                    SigmaCarrier.RepresentationWordsPerSample * 4)
                throw new ArgumentException(
                    "Cold decoded batch does not match the page ABI.");
        }

        internal long[] State { get; }
        internal uint[] Representation { get; }
        internal SigmaColdUploadPageGpu[] Pages { get; }
    }

    /// <summary>
    /// One bounded queue-1 transfer batch. Unity records only the short resource
    /// release/acquire events; decoded page bytes never traverse the XR queue.
    /// </summary>
    internal static class SigmaNativeVulkanColdUpload
    {
        internal const int AbiVersion = 1;
        internal const int MaximumPages = 32;
        private const int ResourceCount = 6;
        private static ColdUploadJob _activeJob;

        internal static bool HasJobInFlight => _activeJob != null;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeDescriptor
        {
            internal uint StructSize;
            internal uint Abi;
            internal uint PageCount;
            internal uint PhysicalSlotCapacity;
            internal IntPtr StateResource;
            internal IntPtr RepresentationResource;
            internal IntPtr MetadataResource;
            internal IntPtr DirtyResource;
            internal IntPtr ReadoutDirtyResource;
            internal IntPtr DenseSlotResource;
            internal IntPtr StateBytes;
            internal ulong StateByteCount;
            internal IntPtr RepresentationBytes;
            internal ulong RepresentationByteCount;
            internal IntPtr Pages;
            internal uint PageStride;
            internal uint Reserved;
        }

        internal static ColdUploadJob Create(SigmaCarrierReadBatch target,
            SigmaColdDecodedBatch batch)
        {
            if (_activeJob != null || SigmaNativeVulkanExecutor.HasJobInFlight)
                throw new InvalidOperationException(
                    "Cold upload and NativeClose cannot share the carrier bank.");
            if (batch == null) throw new ArgumentNullException(nameof(batch));
#if !UNITY_EDITOR && UNITY_ANDROID
            SigmaNativeVulkanExecutor.RequireAvailable();
            if (Native.Abi != AbiVersion)
                throw new InvalidOperationException(
                    "N5R cold-upload native ABI is unavailable.");
            GCHandle statePin = default;
            GCHandle representationPin = default;
            GCHandle pagesPin = default;
            try
            {
                statePin = GCHandle.Alloc(batch.State, GCHandleType.Pinned);
                representationPin = GCHandle.Alloc(batch.Representation,
                    GCHandleType.Pinned);
                pagesPin = GCHandle.Alloc(batch.Pages, GCHandleType.Pinned);
                var descriptor = new NativeDescriptor
                {
                    StructSize = checked((uint)Marshal.SizeOf<NativeDescriptor>()),
                    Abi = AbiVersion,
                    PageCount = checked((uint)batch.Pages.Length),
                    PhysicalSlotCapacity = checked(
                        (uint)target.ResidentSlotCapacity),
                    StateResource = target.State.GetNativeBufferPtr(),
                    RepresentationResource =
                        target.Representation.GetNativeBufferPtr(),
                    MetadataResource = target.Metadata.GetNativeBufferPtr(),
                    DirtyResource = target.DirtyFlags.GetNativeBufferPtr(),
                    ReadoutDirtyResource =
                        target.ReadoutDirtyFlags.GetNativeBufferPtr(),
                    DenseSlotResource =
                        target.ResidentSlotTable.GetNativeBufferPtr(),
                    StateBytes = statePin.AddrOfPinnedObject(),
                    StateByteCount = checked((ulong)batch.State.LongLength *
                        sizeof(long)),
                    RepresentationBytes =
                        representationPin.AddrOfPinnedObject(),
                    RepresentationByteCount = checked(
                        (ulong)batch.Representation.LongLength * sizeof(uint)),
                    Pages = pagesPin.AddrOfPinnedObject(),
                    PageStride = checked(
                        (uint)Marshal.SizeOf<SigmaColdUploadPageGpu>()),
                    Reserved = 0u,
                };
                IntPtr handle = Native.CreateJob(ref descriptor);
                if (handle == IntPtr.Zero)
                    throw new InvalidOperationException(
                        "Native queue rejected the bounded cold upload batch.");
                _activeJob = new ColdUploadJob(handle);
                return _activeJob;
            }
            finally
            {
                if (pagesPin.IsAllocated) pagesPin.Free();
                if (representationPin.IsAllocated) representationPin.Free();
                if (statePin.IsAllocated) statePin.Free();
            }
#else
            throw new PlatformNotSupportedException(
                "The plugin-owned cold upload exists only on Android Vulkan.");
#endif
        }

        private static void Release(ColdUploadJob job)
        {
            if (ReferenceEquals(_activeJob, job))
                _activeJob = null;
        }

        private static class Native
        {
#if !UNITY_EDITOR && UNITY_ANDROID
            private const string Library = "SigmaVulkanTimestamps";
            [DllImport(Library, EntryPoint = "SigmaColdUpload_GetAbiVersion")]
            private static extern uint GetAbiVersion();
            [DllImport(Library, EntryPoint = "SigmaColdUpload_CreateJob")]
            internal static extern IntPtr CreateJob(ref NativeDescriptor descriptor);
            [DllImport(Library, EntryPoint = "SigmaColdUpload_CancelJob")]
            internal static extern int CancelJob(IntPtr handle);
            [DllImport(Library,
                EntryPoint = "SigmaColdUpload_GetRenderEventFunc")]
            private static extern IntPtr GetRenderEventFunc();
            [DllImport(Library, EntryPoint = "SigmaColdUpload_GetEventId")]
            private static extern int GetEventId(int offset);
            [DllImport(Library, EntryPoint = "SigmaColdUpload_PollJob")]
            internal static extern int PollJob(IntPtr handle, out int error);
            [DllImport(Library, EntryPoint = "SigmaColdUpload_DestroyJob")]
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

        internal sealed class ColdUploadJob : IDisposable
        {
            private IntPtr _handle;
            private bool _recorded;
            private bool _acquireRecorded;
            private bool _terminal;
            private SigmaGpuCompletionStatus _terminalStatus;
            private string _terminalError;

            internal ColdUploadJob(IntPtr handle) => _handle = handle;

            internal void Record(CommandBuffer command)
            {
                if (command == null) throw new ArgumentNullException(nameof(command));
                if (_handle == IntPtr.Zero || _recorded)
                    throw new InvalidOperationException(
                        "Cold upload recording state is invalid.");
                IntPtr callback = Native.RenderEvent;
                int prepare = Native.EventId(0);
                int submit = Native.EventId(1);
                if (callback == IntPtr.Zero || prepare == 0 || submit == 0)
                    throw new InvalidOperationException(
                        "Cold upload native events are unavailable.");
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
                            "Sigma N5 cold upload acquire");
                        try
                        {
                            IntPtr callback = Native.RenderEvent;
                            int acquire = Native.EventId(2);
                            if (callback == IntPtr.Zero || acquire == 0)
                                throw new InvalidOperationException(
                                    "Cold upload acquire event is unavailable.");
                            command.IssuePluginEventAndData(callback, acquire,
                                _handle);
                            _acquireRecorded = true;
                            Graphics.ExecuteCommandBuffer(command);
                        }
                        catch (Exception exception)
                        {
                            _terminal = true;
                            _terminalStatus = SigmaGpuCompletionStatus.Faulted;
                            _terminalError = "Cold upload acquire became " +
                                "uncertain; reserved slots are quarantined: " +
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
                    _terminalError = "Native cold upload failed: VkResult=" +
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
