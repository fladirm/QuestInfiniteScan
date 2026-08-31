using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.SigmaPrism
{
    /// <summary>
    /// Exact bounded dirty-page stage executed by the plugin-owned Vulkan queue.
    /// One batch owns three cold dispatches and one terminal fence; no page causes
    /// its own host dispatch, submission, or readback.
    /// </summary>
    internal static class SigmaNativeVulkanColdEncode
    {
        internal const int AbiVersion = 1;
        internal const int MaximumPages = 8;
        internal const int BlocksPerPage = SigmaDecodedPage.BlockCount;
        internal const int ScratchBytesPerBlock = 8192;
        private const int UnityResourceCount = 6;
        private static ColdEncodeJob _activeJob;

        internal static bool HasJobInFlight => _activeJob != null;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeDescriptor
        {
            internal uint StructSize;
            internal uint Abi;
            internal uint PageCapacity;
            internal uint DirtySkip;
            internal IntPtr ExactGate;
            internal IntPtr State;
            internal IntPtr Representation;
            internal IntPtr Metadata;
            internal IntPtr PublicationRoot;
            internal IntPtr DirtyFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeResultDescriptor
        {
            internal uint StructSize;
            internal uint Abi;
            internal IntPtr Info;
            internal uint InfoCapacity;
            internal IntPtr Slots;
            internal uint SlotCapacity;
            internal IntPtr BlockDescriptors;
            internal ulong BlockDescriptorWordCapacity;
            internal IntPtr Payload;
            internal ulong PayloadByteCapacity;
            internal IntPtr Representation;
            internal ulong RepresentationWordCapacity;
            internal IntPtr Metadata;
            internal ulong MetadataWordCapacity;
        }

        internal static ColdEncodeJob Create(SigmaExactBackendGate exactGate,
            SigmaCarrierReadBatch source, uint dirtySkip)
        {
            if (exactGate == null) throw new ArgumentNullException(
                nameof(exactGate));
            if (_activeJob != null ||
                SigmaNativeVulkanExecutor.HasJobInFlight ||
                SigmaNativeVulkanColdUpload.HasJobInFlight)
                throw new InvalidOperationException(
                    "Cold durable encode requires exclusive scanner-bank " +
                    "ownership; XR FRONT remains independent.");
#if !UNITY_EDITOR && UNITY_ANDROID
            SigmaNativeVulkanExecutor.RequireAvailable();
            if (Native.Abi != AbiVersion ||
                Native.MaximumPages != MaximumPages)
                throw new InvalidOperationException(
                    "N5R cold-encode native ABI is unavailable.");
            var descriptor = new NativeDescriptor
            {
                StructSize = checked((uint)Marshal.SizeOf<NativeDescriptor>()),
                Abi = AbiVersion,
                PageCapacity = checked((uint)source.PageCapacity),
                DirtySkip = dirtySkip,
                ExactGate = exactGate.Buffer.GetNativeBufferPtr(),
                State = source.State.GetNativeBufferPtr(),
                Representation = source.Representation.GetNativeBufferPtr(),
                Metadata = source.Metadata.GetNativeBufferPtr(),
                PublicationRoot = source.PublicationRoot.GetNativeBufferPtr(),
                DirtyFlags = source.DirtyFlags.GetNativeBufferPtr(),
            };
            IntPtr handle = Native.CreateJob(ref descriptor);
            if (handle == IntPtr.Zero)
                throw new InvalidOperationException(
                    "Native queue rejected the bounded durable encode batch.");
            _activeJob = new ColdEncodeJob(handle);
            return _activeJob;
#else
            throw new PlatformNotSupportedException(
                "The plugin-owned durable encoder exists only on Android Vulkan.");
#endif
        }

        private static void Release(ColdEncodeJob job)
        {
            if (ReferenceEquals(_activeJob, job))
                _activeJob = null;
        }

        private static class Native
        {
#if !UNITY_EDITOR && UNITY_ANDROID
            private const string Library = "SigmaVulkanTimestamps";
            [DllImport(Library, EntryPoint = "SigmaColdEncode_GetAbiVersion")]
            private static extern uint GetAbiVersion();
            [DllImport(Library, EntryPoint = "SigmaColdEncode_GetMaximumPages")]
            private static extern uint GetMaximumPages();
            [DllImport(Library, EntryPoint = "SigmaColdEncode_CreateJob")]
            internal static extern IntPtr CreateJob(ref NativeDescriptor descriptor);
            [DllImport(Library, EntryPoint = "SigmaColdEncode_CancelJob")]
            internal static extern int CancelJob(IntPtr handle);
            [DllImport(Library,
                EntryPoint = "SigmaColdEncode_GetRenderEventFunc")]
            private static extern IntPtr GetRenderEventFunc();
            [DllImport(Library, EntryPoint = "SigmaColdEncode_GetEventId")]
            private static extern int GetEventId(int offset);
            [DllImport(Library, EntryPoint = "SigmaColdEncode_PollJob")]
            internal static extern int PollJob(IntPtr handle, out int error);
            [DllImport(Library,
                EntryPoint = "SigmaColdEncode_ReadResultInfo")]
            internal static extern int ReadResultInfo(IntPtr handle,
                [Out] uint[] info, uint infoCapacity);
            [DllImport(Library, EntryPoint = "SigmaColdEncode_ReadResult")]
            internal static extern int ReadResult(IntPtr handle,
                ref NativeResultDescriptor descriptor);
            [DllImport(Library, EntryPoint = "SigmaColdEncode_DestroyJob")]
            internal static extern int DestroyJob(IntPtr handle);
            internal static uint Abi => GetAbiVersion();
            internal static uint MaximumPages => GetMaximumPages();
            internal static IntPtr RenderEvent => GetRenderEventFunc();
            internal static int EventId(int offset) => GetEventId(offset);
#else
            internal static uint Abi => 0u;
            internal static uint MaximumPages => 0u;
            internal static IntPtr CreateJob(ref NativeDescriptor descriptor) =>
                IntPtr.Zero;
            internal static int CancelJob(IntPtr handle) => 0;
            internal static int PollJob(IntPtr handle, out int error)
            {
                error = -1;
                return -1;
            }
            internal static int ReadResult(IntPtr handle,
                ref NativeResultDescriptor descriptor) => 0;
            internal static int ReadResultInfo(IntPtr handle, uint[] info,
                uint infoCapacity) => 0;
            internal static int DestroyJob(IntPtr handle) => 0;
            internal static IntPtr RenderEvent => IntPtr.Zero;
            internal static int EventId(int offset) => 0;
#endif
        }

        internal sealed class ColdEncodeJob : IDisposable
        {
            private IntPtr _handle;
            private bool _recorded;
            private bool _acquireRecorded;
            private bool _terminal;
            private SigmaGpuCompletionStatus _terminalStatus;
            private string _terminalError;
            private SigmaColdEncodedBatch _result;

            internal ColdEncodeJob(IntPtr handle) => _handle = handle;

            internal void Record(CommandBuffer command)
            {
                if (command == null) throw new ArgumentNullException(
                    nameof(command));
                if (_handle == IntPtr.Zero || _recorded)
                    throw new InvalidOperationException(
                        "Cold encode recording state is invalid.");
                IntPtr callback = Native.RenderEvent;
                int prepare = Native.EventId(0);
                int submit = Native.EventId(1);
                if (callback == IntPtr.Zero || prepare == 0 || submit == 0)
                    throw new InvalidOperationException(
                        "Cold encode native events are unavailable.");
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
                            "Sigma N5 cold durable acquire");
                        try
                        {
                            IntPtr callback = Native.RenderEvent;
                            int acquire = Native.EventId(2);
                            if (callback == IntPtr.Zero || acquire == 0)
                                throw new InvalidOperationException(
                                    "Cold encode acquire event is unavailable.");
                            command.IssuePluginEventAndData(callback, acquire,
                                _handle);
                            _acquireRecorded = true;
                            Graphics.ExecuteCommandBuffer(command);
                        }
                        catch (Exception exception)
                        {
                            _terminal = true;
                            _terminalStatus = SigmaGpuCompletionStatus.Faulted;
                            _terminalError = "Cold encode acquire became " +
                                "uncertain; durable source pages remain pinned: " +
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
                    _terminalError = "Native durable encode failed: VkResult=" +
                        vkError + ".";
                }
                else
                {
                    try
                    {
                        _result = ReadNativeResult();
                        _terminalStatus = SigmaGpuCompletionStatus.Complete;
                    }
                    catch (Exception exception)
                    {
                        _terminalStatus = SigmaGpuCompletionStatus.Faulted;
                        _terminalError = "Durable stage receipt was invalid: " +
                            exception.Message;
                    }
                }
                error = _terminalError;
                return _terminalStatus;
            }

            internal SigmaColdEncodedBatch TakeResult()
            {
                if (!_terminal || _terminalStatus !=
                    SigmaGpuCompletionStatus.Complete || _result == null)
                    throw new InvalidOperationException(
                        "Cold encode result is not complete.");
                SigmaColdEncodedBatch result = _result;
                _result = null;
                return result;
            }

            private SigmaColdEncodedBatch ReadNativeResult()
            {
                var info = new uint[4];
                if (Native.ReadResultInfo(_handle, info,
                        checked((uint)info.Length)) == 0)
                    throw new InvalidOperationException(
                        "Native result info rejected its exact range.");
                int pageCount = checked((int)info[0]);
                if (pageCount < 0 || pageCount > MaximumPages)
                    throw new InvalidDataException(
                        "Native dirty-page count exceeds the bounded batch.");
                var slots = new uint[pageCount];
                var descriptors = new uint[checked(pageCount *
                    BlocksPerPage * 4)];
                var payload = new byte[checked(pageCount * BlocksPerPage *
                    ScratchBytesPerBlock)];
                var representation = new uint[checked(pageCount *
                    SigmaCarrier.SamplesPerPage *
                    SigmaCarrier.RepresentationWordsPerSample * 4)];
                var metadata = new uint[checked(pageCount * 16)];
                GCHandle infoPin = default;
                GCHandle slotsPin = default;
                GCHandle descriptorsPin = default;
                GCHandle payloadPin = default;
                GCHandle representationPin = default;
                GCHandle metadataPin = default;
                try
                {
                    infoPin = GCHandle.Alloc(info, GCHandleType.Pinned);
                    slotsPin = GCHandle.Alloc(slots, GCHandleType.Pinned);
                    descriptorsPin = GCHandle.Alloc(descriptors,
                        GCHandleType.Pinned);
                    payloadPin = GCHandle.Alloc(payload, GCHandleType.Pinned);
                    representationPin = GCHandle.Alloc(representation,
                        GCHandleType.Pinned);
                    metadataPin = GCHandle.Alloc(metadata,
                        GCHandleType.Pinned);
                    var native = new NativeResultDescriptor
                    {
                        StructSize = checked((uint)Marshal.SizeOf<
                            NativeResultDescriptor>()),
                        Abi = AbiVersion,
                        Info = infoPin.AddrOfPinnedObject(),
                        InfoCapacity = checked((uint)info.Length),
                        Slots = slotsPin.AddrOfPinnedObject(),
                        SlotCapacity = checked((uint)slots.Length),
                        BlockDescriptors = descriptorsPin.AddrOfPinnedObject(),
                        BlockDescriptorWordCapacity = checked(
                            (ulong)descriptors.LongLength),
                        Payload = payloadPin.AddrOfPinnedObject(),
                        PayloadByteCapacity = checked((ulong)payload.LongLength),
                        Representation = representationPin.AddrOfPinnedObject(),
                        RepresentationWordCapacity = checked(
                            (ulong)representation.LongLength),
                        Metadata = metadataPin.AddrOfPinnedObject(),
                        MetadataWordCapacity = checked(
                            (ulong)metadata.LongLength),
                    };
                    if (Native.ReadResult(_handle, ref native) == 0)
                        throw new InvalidOperationException(
                            "Native result copy rejected its exact ranges.");
                }
                finally
                {
                    if (metadataPin.IsAllocated) metadataPin.Free();
                    if (representationPin.IsAllocated) representationPin.Free();
                    if (payloadPin.IsAllocated) payloadPin.Free();
                    if (descriptorsPin.IsAllocated) descriptorsPin.Free();
                    if (slotsPin.IsAllocated) slotsPin.Free();
                    if (infoPin.IsAllocated) infoPin.Free();
                }
                if (info[0] != (uint)pageCount)
                    throw new InvalidDataException(
                        "Native dirty-page count changed after completion.");
                return new SigmaColdEncodedBatch(info[1], info[2], info[3],
                    slots, descriptors, payload, representation, metadata);
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

    internal sealed class SigmaColdEncodedBatch
    {
        private const int BlocksPerPage =
            SigmaNativeVulkanColdEncode.BlocksPerPage;
        internal SigmaColdEncodedBatch(uint totalDirty, uint dirtySkip,
            uint pageCapacity, uint[] slots, uint[] descriptors, byte[] payload,
            uint[] representation, uint[] metadata)
        {
            TotalDirty = totalDirty;
            DirtySkip = dirtySkip;
            PageCapacity = pageCapacity;
            Slots = slots ?? throw new ArgumentNullException(nameof(slots));
            Descriptors = descriptors ?? throw new ArgumentNullException(
                nameof(descriptors));
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
            Representation = representation ?? throw new ArgumentNullException(
                nameof(representation));
            Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            if (slots.Length > SigmaNativeVulkanColdEncode.MaximumPages ||
                descriptors.Length != slots.Length *
                    SigmaNativeVulkanColdEncode.BlocksPerPage * 4 ||
                payload.Length != slots.Length *
                    SigmaNativeVulkanColdEncode.BlocksPerPage *
                    SigmaNativeVulkanColdEncode.ScratchBytesPerBlock ||
                representation.Length != slots.Length *
                    SigmaCarrier.SamplesPerPage *
                    SigmaCarrier.RepresentationWordsPerSample * 4 ||
                metadata.Length != slots.Length * 16)
                throw new ArgumentException(
                    "Cold encoded batch violates the exact staging ABI.");
        }

        internal uint TotalDirty { get; }
        internal uint DirtySkip { get; }
        internal uint PageCapacity { get; }
        internal uint[] Slots { get; }
        internal uint[] Descriptors { get; }
        internal byte[] Payload { get; }
        internal uint[] Representation { get; }
        internal uint[] Metadata { get; }
        internal int Count => Slots.Length;

        internal IReadOnlyList<SigmaStagedDurablePage> BuildPageUpdates(
            int segmentIndex = 0)
        {
            if (segmentIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(segmentIndex));
            var updates = new List<SigmaStagedDurablePage>(Count);
            for (int pageIndex = 0; pageIndex < Count; ++pageIndex)
            {
                SigmaEncodedBlock[] blocks = ReadBlocks(pageIndex);
                SigmaEncodedPageHeader header = ReadPageHeader(pageIndex,
                    out uint pageGeneration);
                int representationBase = checked(pageIndex *
                    SigmaCarrier.SamplesPerPage *
                    SigmaCarrierRepresentationRecord.WordCount);
                SigmaCarrierCodec.ValidateStagedRepresentationAndCoverage(
                    header, Representation, representationBase, blocks);
                byte[] staged = SigmaCarrierCodec.EncodePageFromStagedBlocks(
                    header, Representation, representationBase, blocks);
                ValidateDirectCodecStage(header, staged);
                byte[] supportBytes = BuildConservativeSupportSummary(header,
                    pageGeneration, blocks);
                SigmaDurableHash supportHash = SigmaDurableHash.Compute(
                    supportBytes);
                var support = new SigmaQuerySupportReceipt(pageGeneration,
                    SigmaQuerySupportFlags.Verified |
                        SigmaQuerySupportFlags.MayContribute,
                    supportHash);
                updates.Add(new SigmaStagedDurablePage(
                    segmentIndex, checked((int)Slots[pageIndex]),
                    SigmaDurablePageUpdate.FromDirectCodecVerifiedStage(header,
                        pageGeneration, support, staged, supportBytes)));
            }
            return updates;
        }

        private SigmaEncodedBlock[] ReadBlocks(int pageIndex)
        {
            var blocks = new SigmaEncodedBlock[BlocksPerPage];
            for (int block = 0; block < blocks.Length; ++block)
            {
                int descriptor = (pageIndex * BlocksPerPage + block) * 4;
                uint mode = Descriptors[descriptor];
                uint size = Descriptors[descriptor + 1];
                uint offset = Descriptors[descriptor + 2];
                uint valid = Descriptors[descriptor + 3];
                int expectedOffset = checked((pageIndex * BlocksPerPage +
                    block) * SigmaNativeVulkanColdEncode.ScratchBytesPerBlock);
                if (valid != 1u || mode > (uint)SigmaBlockMode.Raw ||
                    size > SigmaNativeVulkanColdEncode.ScratchBytesPerBlock ||
                    offset != (uint)expectedOffset ||
                    (ulong)offset + size > (ulong)Payload.LongLength)
                    throw new InvalidDataException(
                        "GPU block descriptor failed its exact range contract.");
                var bytes = new byte[checked((int)size)];
                Buffer.BlockCopy(Payload, checked((int)offset), bytes, 0,
                    bytes.Length);
                blocks[block] = new SigmaEncodedBlock((SigmaBlockMode)mode,
                    bytes);
            }
            return blocks;
        }

        private SigmaEncodedPageHeader ReadPageHeader(int pageIndex,
            out uint pageGeneration)
        {
            int metadataBase = pageIndex * 16;
            long pageX = JoinSigned(Metadata[metadataBase],
                Metadata[metadataBase + 1]);
            long pageY = JoinSigned(Metadata[metadataBase + 2],
                Metadata[metadataBase + 3]);
            pageGeneration = Metadata[metadataBase + 4];
            uint revision = Metadata[metadataBase + 5];
            ulong certificateOffset = JoinUnsigned(Metadata[metadataBase + 6],
                Metadata[metadataBase + 7]);
            uint certificateCount = Metadata[metadataBase + 8];
            uint gaugeGeneration = Metadata[metadataBase + 10];
            uint certificateGeneration = Metadata[metadataBase + 11];
            uint representationFlags = Metadata[metadataBase + 12];
            uint activeCount = Metadata[metadataBase + 14];
            uint stateGeneration = Metadata[metadataBase + 15];
            if (stateGeneration == 0u || pageGeneration == 0u ||
                revision == 0u ||
                activeCount > SigmaCarrier.SamplesPerPage)
                throw new InvalidDataException(
                    "Staged page metadata is not a published generation.");
            return new SigmaEncodedPageHeader(
                new SigmaCarrierPageCoordinate(pageX, pageY), stateGeneration,
                revision, certificateOffset, certificateCount,
                gaugeGeneration, certificateGeneration, representationFlags,
                activeCount, activeCount);
        }

        private static byte[] BuildConservativeSupportSummary(
            SigmaEncodedPageHeader header, uint pageGeneration,
            IReadOnlyList<SigmaEncodedBlock> blocks)
        {
            return SigmaQuerySupportSummary.FromStagedBlocks(header,
                pageGeneration,
                SigmaQuerySupportFlags.Verified |
                SigmaQuerySupportFlags.MayContribute, blocks).Encode();
        }

        private static void ValidateDirectCodecStage(
            SigmaEncodedPageHeader expected,
            byte[] staged)
        {
            // ReadBlocks validates every descriptor/range; the staged
            // representation/coverage gate validates the exact chi/kappa and
            // certificate contract. EncodePageFromStagedBlocks is the
            // separately proved bit-exact direct equivalent of EncodePage (all
            // five modes are covered by the CPU/Vulkan parity corpus). This
            // final pass validates complete canonical framing and all pinned
            // page metadata without materializing 4096 CPU S16 samples.
            SigmaEncodedPageHeader header =
                SigmaCarrierCodec.ReadPageHeader(staged);
            if (!header.Coordinate.Equals(expected.Coordinate) ||
                header.Generation != expected.Generation ||
                header.GaugeGeneration != expected.GaugeGeneration ||
                header.CertificateGeneration !=
                    expected.CertificateGeneration ||
                header.Revision != expected.Revision ||
                header.CertificateOffset != expected.CertificateOffset ||
                header.CertificateCount != expected.CertificateCount ||
                header.RepresentationFlags != expected.RepresentationFlags ||
                header.ActiveSampleCount != expected.ActiveSampleCount ||
                header.RepresentationCount != expected.RepresentationCount)
                throw new InvalidDataException(
                    "Direct-codec staged page metadata mismatch.");
        }

        private static ulong JoinUnsigned(uint low, uint high) =>
            low | ((ulong)high << 32);

        private static long JoinSigned(uint low, uint high) => unchecked(
            (long)JoinUnsigned(low, high));

    }

    internal readonly struct SigmaStagedDurablePage
    {
        internal SigmaStagedDurablePage(int visibleSlot,
            SigmaDurablePageUpdate update) : this(0, visibleSlot, update) { }

        internal SigmaStagedDurablePage(int segmentIndex, int visibleSlot,
            SigmaDurablePageUpdate update)
        {
            if (segmentIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(segmentIndex));
            if (visibleSlot < 0)
                throw new ArgumentOutOfRangeException(nameof(visibleSlot));
            SegmentIndex = segmentIndex;
            VisibleSlot = visibleSlot;
            Update = update ?? throw new ArgumentNullException(nameof(update));
        }

        internal int SegmentIndex { get; }
        internal int VisibleSlot { get; }
        internal SigmaDurablePageUpdate Update { get; }
    }
}
