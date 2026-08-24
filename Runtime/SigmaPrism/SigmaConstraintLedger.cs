using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.SigmaPrism
{
    [Flags]
    internal enum SigmaProofMutationFlags : uint
    {
        None = 0,
        StateChanged = 1u << 0,
        ProofSetChanged = 1u << 1,
        RawChanged = 1u << 2,
        Failed = 1u << 31,
    }

    internal readonly struct SigmaProofPageStatus
    {
        internal SigmaProofPageStatus(uint completedBlocks,
            SigmaProofMutationFlags flags, ulong rawUsedMask,
            uint certificateCount, uint rawReasons)
        {
            CompletedBlocks = completedBlocks;
            Flags = flags;
            RawUsedMask = rawUsedMask;
            CertificateCount = certificateCount;
            RawReasons = rawReasons;
        }

        internal uint CompletedBlocks { get; }
        internal SigmaProofMutationFlags Flags { get; }
        internal ulong RawUsedMask { get; }
        internal uint CertificateCount { get; }
        internal uint RawReasons { get; }
        internal bool IsValid => CompletedBlocks ==
            SigmaConstraintLedger.BlocksPerPage &&
            (Flags & SigmaProofMutationFlags.Failed) == 0;
        internal bool HasMutation => (Flags & (SigmaProofMutationFlags.StateChanged |
            SigmaProofMutationFlags.ProofSetChanged |
            SigmaProofMutationFlags.RawChanged)) != 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct SigmaGaugeRawClonePlan
    {
        internal SigmaGaugeRawClonePlan(int sourceRaw, int targetRaw,
            int targetBlock, int sourceBlock)
        {
            SourceRaw = unchecked((uint)sourceRaw);
            TargetRaw = unchecked((uint)targetRaw);
            TargetBlock = unchecked((uint)targetBlock);
            SourceBlock = unchecked((uint)sourceBlock);
        }

        internal readonly uint SourceRaw;
        internal readonly uint TargetRaw;
        internal readonly uint TargetBlock;
        internal readonly uint SourceBlock;
    }

    /// <summary>
    /// Durable proof metadata for immutable Sigma carrier generations. The ledger
    /// stores only exact block certificates plus unresolved raw sensor tiles; it is
    /// interpretation/provenance metadata and cannot render or become physical
    /// geometry. Pixel work and proof minimization remain GPU-local.
    /// </summary>
    internal sealed class SigmaConstraintLedger : IDisposable
    {
        internal const int CertificatesPerBlock = 4;
        internal const int BoundsPerBlock = 48;
        internal const int BlocksPerPage = SigmaCarrier.BlocksPerPage;
        internal const int CertificatesPerPage =
            CertificatesPerBlock * BlocksPerPage;
        internal const int BoundsPerPage = BoundsPerBlock * BlocksPerPage;
        internal const int StatusStride = 8;
        internal const int CertificateStride = 48;
        internal const int BoundStride = 16;
        internal const int BlockStride = 32;
        internal const int RawHeaderStride = 32;
        internal const int RawWord4PerTile = 384;
        internal const int RawFrameRecordStride = 592;
        internal const int RawRequestStride = 16;
        internal const int ProofSampleStride = 784;
        internal const int ProofSourceCount = 4;
        internal const int StreamingProofSourceWindowCapacity =
            SigmaGeneratedStreaming.ProofCandidateWindowCapacity;
        internal const int ProofSourceMetaRecords = 3;
        internal const uint InvalidSlot = uint.MaxValue;
        private const string ResourceName =
            "SigmaPrism/SigmaConstraintLedger";
        private const string GaugeResourceName =
            "SigmaPrism/SigmaGaugeDemand";

        private readonly ComputeShader _shader;
        private readonly ComputeShader _gaugeShader;
        private readonly Stack<int> _freeProofSlots;
        private readonly SigmaExactBackendGate _backendGate;
        private readonly Stack<int> _freeRawTiles;
        private readonly Stack<int> _freeFrameSlots;
        private readonly bool[] _proofActive;
        private readonly int[] _proofRawHeads;
        private readonly int[] _rawNext;
        private readonly int[] _rawFrame;
        private readonly int[] _frameRefCount;
        private readonly bool[] _frameOpen;
        private readonly uint[] _reservationUpload = new uint[BlocksPerPage];

        private GraphicsBuffer _certificates;
        private GraphicsBuffer _bounds;
        private GraphicsBuffer _blocks;
        private GraphicsBuffer _rawHeaders;
        private GraphicsBuffer _rawWords;
        private GraphicsBuffer _rawReservations;
        private GraphicsBuffer _rawAllocator;
        private GraphicsBuffer _rawLiveWords;
        private GraphicsBuffer _frameRecords;
        private GraphicsBuffer _rawRequests;
        private GraphicsBuffer _proofSamples;
        private GraphicsBuffer _proofSourceScratchMeta;
        private GraphicsBuffer _proofSourceScratchBounds;
        private GraphicsBuffer _gaugeCoordinateScratch;
        private GraphicsBuffer _pageStatus;
        private GraphicsBuffer _gaugeDemand;
        private int _clearKernel;
        private int _reduceSourcesKernel;
        private int _finalizeKernel;
        private int _gaugeCoordinateKernel;
        private int _gaugeFinalizeKernel;
        private bool _disposed;

        internal SigmaConstraintLedger(int proofPageCapacity,
            int rawTileCapacity, SigmaExactBackendGate backendGate,
            int gpuWorkCapacity = 1)
        {
            if (proofPageCapacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(proofPageCapacity));
            if (rawTileCapacity < BlocksPerPage)
                throw new ArgumentOutOfRangeException(nameof(rawTileCapacity));
            if (gpuWorkCapacity <= 0 || gpuWorkCapacity > proofPageCapacity)
                throw new ArgumentOutOfRangeException(nameof(gpuWorkCapacity));
            if (Marshal.SizeOf<SigmaRawFrameRecordGpu>() !=
                RawFrameRecordStride)
                throw new InvalidOperationException(
                    "Sigma raw-frame ABI stride does not match the GPU contract.");
            _backendGate = backendGate ?? throw new ArgumentNullException(
                nameof(backendGate));
            ProofPageCapacity = proofPageCapacity;
            RawTileCapacity = rawTileCapacity;
            GpuWorkCapacity = gpuWorkCapacity;
            _shader = Resources.Load<ComputeShader>(ResourceName);
            if (_shader == null)
                throw new InvalidOperationException(
                    "Sigma constraint-ledger compute resource is missing.");
            _gaugeShader = Resources.Load<ComputeShader>(GaugeResourceName);
            if (_gaugeShader == null)
                throw new InvalidOperationException(
                    "Sigma gauge-demand compute resource is missing.");
            _clearKernel = _shader.FindProfiledKernel("ClearProofTransaction");
            _reduceSourcesKernel = _shader.FindProfiledKernel("ReduceProofSources");
            _finalizeKernel = _shader.FindProfiledKernel("FinalizeProofPage");
            _gaugeCoordinateKernel = _gaugeShader.FindProfiledKernel(
                "BuildGaugeCoordinateCandidates");
            _gaugeFinalizeKernel = _gaugeShader.FindProfiledKernel(
                "FinalizeGaugeDemand");

            _certificates = CreateBuffer(checked(proofPageCapacity *
                CertificatesPerPage), CertificateStride,
                "Sigma minimal constraint certificates");
            _bounds = CreateBuffer(checked(proofPageCapacity * BoundsPerPage),
                BoundStride, "Sigma sparse certificate Q48 bounds");
            _blocks = CreateBuffer(checked(proofPageCapacity * BlocksPerPage),
                BlockStride, "Sigma certificate block metadata");
            _rawHeaders = CreateBuffer(rawTileCapacity, RawHeaderStride,
                "Sigma unresolved raw observation headers");
            _rawWords = CreateBuffer(checked(rawTileCapacity * RawWord4PerTile),
                sizeof(uint) * 4, "Sigma unresolved raw observation payloads");
            _rawReservations = CreateBuffer(checked(proofPageCapacity *
                BlocksPerPage), sizeof(uint),
                "Sigma raw observation transaction reservations");
            _rawAllocator = CreateBuffer(1, sizeof(uint),
                "Sigma GPU raw observation active count");
            int rawLiveWordCount = checked((rawTileCapacity + 31) / 32);
            _rawLiveWords = CreateBuffer(rawLiveWordCount, sizeof(uint),
                "Sigma unresolved raw observation residency bitmap");
            _frameRecords = CreateBuffer(rawTileCapacity,
                RawFrameRecordStride,
                "Sigma unresolved observation frame records");
            _rawRequests = CreateBuffer(checked(gpuWorkCapacity * BlocksPerPage),
                RawRequestStride, "Sigma raw-retention transaction requests");
            _proofSamples = CreateBuffer(checked(gpuWorkCapacity *
                SigmaCarrier.SamplesPerPage), ProofSampleStride,
                "Sigma compact inverse proof scratch");
            int sourceCandidates = checked(gpuWorkCapacity * BlocksPerPage *
                StreamingProofSourceWindowCapacity);
            _proofSourceScratchMeta = CreateBuffer(checked(sourceCandidates *
                ProofSourceMetaRecords), sizeof(uint) * 4,
                "Sigma proof source reduction metadata");
            _proofSourceScratchBounds = CreateBuffer(checked(sourceCandidates *
                SigmaS16.LaneCount), BoundStride,
                "Sigma proof source reduction bounds");
            _gaugeCoordinateScratch = CreateBuffer(checked(gpuWorkCapacity *
                BlocksPerPage * SigmaS16.LaneCount), sizeof(uint) * 12,
                "Sigma coordinate-major gauge candidates");
            _pageStatus = CreateBuffer(checked(proofPageCapacity * StatusStride),
                sizeof(uint), "Sigma proof transaction status");
            _gaugeDemand = CreateBuffer(checked(proofPageCapacity * BlocksPerPage),
                sizeof(uint) * 12, "Sigma exact carrier-gauge demand proof");

            _proofActive = new bool[proofPageCapacity];
            _proofRawHeads = new int[checked(proofPageCapacity * BlocksPerPage)];
            Array.Fill(_proofRawHeads, -1);
            _rawNext = new int[rawTileCapacity];
            _rawFrame = new int[rawTileCapacity];
            Array.Fill(_rawNext, -1);
            Array.Fill(_rawFrame, -1);
            _frameRefCount = new int[rawTileCapacity];
            _frameOpen = new bool[rawTileCapacity];
            _freeProofSlots = DescendingStack(proofPageCapacity);
            _freeRawTiles = DescendingStack(rawTileCapacity);
            _freeFrameSlots = DescendingStack(rawTileCapacity);
            _pageStatus.SetData(new uint[checked(proofPageCapacity *
                StatusStride)]);
            _rawAllocator.SetData(new uint[1]);
            _rawLiveWords.SetData(new uint[(rawTileCapacity + 31) / 32]);
        }

        internal int ProofPageCapacity { get; }
        internal int RawTileCapacity { get; }
        internal int GpuWorkCapacity { get; }
        internal GraphicsBuffer StatusBuffer => _pageStatus;
        internal GraphicsBuffer GaugeDemandBuffer => _gaugeDemand;
        internal GraphicsBuffer CertificateBuffer => _certificates;
        internal GraphicsBuffer CertificateBoundsBuffer => _bounds;
        internal GraphicsBuffer ConstraintBlockBuffer => _blocks;
        internal GraphicsBuffer RawHeaderBuffer => _rawHeaders;
        internal GraphicsBuffer RawWordsBuffer => _rawWords;
        internal GraphicsBuffer RawReservationBuffer => _rawReservations;
        internal GraphicsBuffer RawAllocatorBuffer => _rawAllocator;
        internal GraphicsBuffer RawLiveBitmapBuffer => _rawLiveWords;
        internal GraphicsBuffer FrameRecordBuffer => _frameRecords;
        internal GraphicsBuffer RawRequestBuffer => _rawRequests;
        internal GraphicsBuffer ProofSampleBuffer => _proofSamples;
        internal GraphicsBuffer ProofSourceScratchMetaBuffer =>
            _proofSourceScratchMeta;
        internal GraphicsBuffer ProofSourceScratchBoundsBuffer =>
            _proofSourceScratchBounds;
        internal GraphicsBuffer GaugeCoordinateScratchBuffer =>
            _gaugeCoordinateScratch;
        internal long CertificateBytes =>
            (long)ProofPageCapacity * CertificatesPerPage * CertificateStride +
            (long)ProofPageCapacity * BoundsPerPage * BoundStride +
            (long)ProofPageCapacity * BlocksPerPage * BlockStride;
        internal long RawObservationBytes =>
            (long)RawTileCapacity * RawHeaderStride +
            (long)RawTileCapacity * RawWord4PerTile * sizeof(uint) * 4 +
            (long)RawTileCapacity * RawFrameRecordStride +
            (long)((RawTileCapacity + 31) / 32) * sizeof(uint);

        /// <summary>
        /// Stages immutable capture provenance for the current GPU transaction.
        /// The reducer copies it into durable storage only for raw evidence that
        /// survives proof minimization, so ordinary frames consume no record slot
        /// and no readback-driven lifetime scheduler is required.
        /// </summary>
        internal GraphicsBuffer CreateStreamingFrameStagingBuffer(string name)
        {
            RequireAlive();
            return CreateBuffer(1, RawFrameRecordStride,
                string.IsNullOrWhiteSpace(name)
                    ? "Sigma immutable ingress provenance staging"
                    : name);
        }

        internal void StageGpuFrame(GraphicsBuffer staging,
            StereoRigFrameLease frame, uint revision,
            uint depthLeftKey, uint depthRightKey, uint rgbLeftKey,
            uint rgbRightKey)
        {
            RequireAlive();
            if (staging == null || staging.count != 1 ||
                staging.stride != RawFrameRecordStride)
                throw new ArgumentException(
                    "A one-record Sigma raw-frame staging buffer is required.",
                    nameof(staging));
            if (frame == null || !frame.IsValid)
                throw new ArgumentException("A coherent rig frame is required.",
                    nameof(frame));
            SigmaRawFrameRecordGpu record = SigmaRawFrameRecordGpu.From(frame,
                revision, depthLeftKey, depthRightKey, rgbLeftKey, rgbRightKey);
            staging.SetData(new[] { record });
        }

        internal SigmaProofFrameLease BeginFrame(StereoRigFrameLease frame,
            uint revision, uint depthLeftKey, uint depthRightKey,
            uint rgbLeftKey, uint rgbRightKey)
        {
            RequireAlive();
            if (frame == null || !frame.IsValid)
                throw new ArgumentException("A coherent rig frame is required.",
                    nameof(frame));
            if (_freeFrameSlots.Count == 0)
                throw new InvalidOperationException(
                    "Unresolved observation frame-record pool is exhausted.");
            int slot = _freeFrameSlots.Pop();
            _frameOpen[slot] = true;
            _frameRefCount[slot] = 0;
            SigmaRawFrameRecordGpu record = SigmaRawFrameRecordGpu.From(frame,
                revision, depthLeftKey, depthRightKey, rgbLeftKey, rgbRightKey);
            _frameRecords.SetData(new[] { record }, 0, slot, 1);
            return new SigmaProofFrameLease(this, slot, frame.CalibrationEpoch,
                revision);
        }

        internal SigmaProofPageLease BeginPage(
            SigmaCarrierPageHandle source, SigmaProofFrameLease frame)
        {
            RequireAlive();
            if (frame == null || !frame.IsActive || frame.Owner != this)
                throw new ArgumentException("Active proof frame is required.",
                    nameof(frame));
            if (_freeProofSlots.Count == 0)
                throw new InvalidOperationException(
                    "Constraint-certificate page budget is exhausted.");
            if (_freeRawTiles.Count < BlocksPerPage)
                throw new InvalidOperationException(
                    "Raw observation transaction reserve is exhausted; stage " +
                    "unresolved tiles before accepting another proof page.");

            int targetSlot = _freeProofSlots.Pop();
            int sourceSlot = DecodeSourceSlot(source);
            var reservations = new int[BlocksPerPage];
            for (int block = 0; block < BlocksPerPage; ++block)
            {
                int raw = _freeRawTiles.Pop();
                reservations[block] = raw;
                _reservationUpload[block] = unchecked((uint)raw);
            }
            _rawReservations.SetData(_reservationUpload, 0,
                targetSlot * BlocksPerPage, BlocksPerPage);
            return new SigmaProofPageLease(this, targetSlot, sourceSlot,
                reservations, frame);
        }

        internal void Prepare(SigmaProofPageLease page)
        {
            RequirePage(page);
            BindCommon(page, _clearKernel);
            _shader.SetBuffer(_clearKernel, "_ProofSamples", _proofSamples);
            _shader.SetBuffer(_clearKernel, "_ProofPageStatus", _pageStatus);
            _shader.SetBuffer(_clearKernel, "_RawRequests", _rawRequests);
            _shader.DispatchProfiled(_clearKernel,
                (SigmaCarrier.SamplesPerPage + 63) / 64, 1, 1);
        }

        internal void BindInverse(ComputeShader inverse, int kernel,
            SigmaProofPageLease page)
        {
            RequirePage(page);
            if (inverse == null)
                throw new ArgumentNullException(nameof(inverse));
            inverse.SetBuffer(kernel, "_ProofSamples", _proofSamples);
            inverse.SetBuffer(kernel, "_ProofPageStatus", _pageStatus);
            inverse.SetInt("_ProofTargetSlot", page.TargetSlot);
            inverse.SetInt("_ProofFrameSlot", page.Frame.Slot);
            inverse.SetInt("_ProofCalibrationEpoch",
                unchecked((int)page.Frame.CalibrationEpoch));
        }

        internal void BindReadOnly(CommandBuffer command, ComputeShader inverse,
            int kernel)
        {
            RequireAlive();
            if (command == null)
                throw new ArgumentNullException(nameof(command));
            if (inverse == null)
                throw new ArgumentNullException(nameof(inverse));
            command.SetComputeBufferParam(inverse, kernel,
                "_ConstraintCertificates", _certificates);
            command.SetComputeBufferParam(inverse, kernel,
                "_ConstraintCertificateBounds", _bounds);
            command.SetComputeBufferParam(inverse, kernel,
                "_ConstraintBlocks", _blocks);
            command.SetComputeIntParam(inverse, "_ConstraintProofCapacity",
                ProofPageCapacity);
        }

        internal void BindReadOnly(ComputeShader inverse, int kernel)
        {
            RequireAlive();
            if (inverse == null)
                throw new ArgumentNullException(nameof(inverse));
            inverse.SetBuffer(kernel, "_ConstraintCertificates", _certificates);
            inverse.SetBuffer(kernel, "_ConstraintCertificateBounds", _bounds);
            inverse.SetBuffer(kernel, "_ConstraintBlocks", _blocks);
            inverse.SetInt("_ConstraintProofCapacity", ProofPageCapacity);
        }

        internal void Reduce(SigmaProofPageLease page,
            SigmaCarrierWriteLease carrierPage)
        {
            RequirePage(page);
            if (carrierPage == null)
                throw new ArgumentNullException(nameof(carrierPage));

            _shader.SetInt("_UseInverseWorkList", 0);
            BindCommon(page, _reduceSourcesKernel);
            _shader.SetBuffer(_reduceSourcesKernel, "_ProofSamples",
                _proofSamples);
            _shader.SetBuffer(_reduceSourcesKernel, "_ProofSourceScratchMeta",
                _proofSourceScratchMeta);
            _shader.SetBuffer(_reduceSourcesKernel, "_ProofSourceScratchBounds",
                _proofSourceScratchBounds);
            _backendGate.Bind(_shader, _reduceSourcesKernel);
            _shader.DispatchProfiled(_reduceSourcesKernel, BlocksPerPage, 1,
                ProofSourceCount);

            BindCommon(page, _finalizeKernel);
            _shader.SetBuffer(_finalizeKernel, "_ProofSamples", _proofSamples);
            _shader.SetBuffer(_finalizeKernel, "_ProofSourceScratchMetaRead",
                _proofSourceScratchMeta);
            _shader.SetBuffer(_finalizeKernel, "_ProofSourceScratchBoundsRead",
                _proofSourceScratchBounds);
            _shader.SetBuffer(_finalizeKernel, "_Certificates", _certificates);
            _shader.SetBuffer(_finalizeKernel, "_CertificateBounds", _bounds);
            _shader.SetBuffer(_finalizeKernel, "_ConstraintBlocks", _blocks);
            _shader.SetBuffer(_finalizeKernel, "_RawTiles", _rawHeaders);
            _shader.SetBuffer(_finalizeKernel, "_RawTileWords", _rawWords);
            _shader.SetBuffer(_finalizeKernel, "_RawReservations",
                _rawReservations);
            _shader.SetBuffer(_finalizeKernel, "_RawRequests", _rawRequests);
            _shader.SetBuffer(_finalizeKernel, "_ProofPageStatus", _pageStatus);
            _backendGate.Bind(_shader, _finalizeKernel);
            _shader.DispatchProfiled(_finalizeKernel, BlocksPerPage, 1, 1);

            _gaugeShader.SetInt("_UseInverseWorkList", 0);
            _gaugeShader.SetInt("_TargetProofSlot", page.TargetSlot);
            _gaugeShader.SetInt("_ProofRevision",
                unchecked((int)page.Frame.Revision));
            carrierPage.BindWritable(_gaugeShader, _gaugeCoordinateKernel,
                "_ProofCarrierState", "_ProofCarrierPageSlot",
                "_ProofCarrierPageCapacity");
            _gaugeShader.SetBuffer(_gaugeCoordinateKernel, "_ProofSamples",
                _proofSamples);
            _gaugeShader.SetBuffer(_gaugeCoordinateKernel,
                "_GaugeCoordinateCandidates", _gaugeCoordinateScratch);
            _backendGate.Bind(_gaugeShader, _gaugeCoordinateKernel);
            _gaugeShader.DispatchProfiled(_gaugeCoordinateKernel, BlocksPerPage, 1,
                SigmaS16.LaneCount);

            _gaugeShader.SetInt("_UseInverseWorkList", 0);
            _gaugeShader.SetInt("_TargetProofSlot", page.TargetSlot);
            _gaugeShader.SetBuffer(_gaugeFinalizeKernel,
                "_GaugeCoordinateCandidatesRead", _gaugeCoordinateScratch);
            _gaugeShader.SetBuffer(_gaugeFinalizeKernel, "_GaugeDemand",
                _gaugeDemand);
            _backendGate.Bind(_gaugeShader, _gaugeFinalizeKernel);
            _gaugeShader.DispatchProfiled(_gaugeFinalizeKernel, BlocksPerPage, 1, 1);
        }

        internal void BindGaugeReadOnly(ComputeShader shader, int kernel)
        {
            BindReadOnly(shader, kernel);
            shader.SetBuffer(kernel, "_GaugeDemand", _gaugeDemand);
        }

        internal void BindGaugeSourceReadOnly(ComputeShader shader, int kernel,
            int sourceSlot)
        {
            RequireAlive();
            if (shader == null)
                throw new ArgumentNullException(nameof(shader));
            if ((uint)sourceSlot >= (uint)ProofPageCapacity ||
                !_proofActive[sourceSlot])
                throw new InvalidOperationException(
                    "Gauge source proof slot is not active.");
            shader.SetBuffer(kernel, "_GaugeSourceCertificates", _certificates);
            shader.SetBuffer(kernel, "_GaugeSourceBounds", _bounds);
            shader.SetBuffer(kernel, "_GaugeSourceBlocks", _blocks);
            shader.SetBuffer(kernel, "_GaugeDemand", _gaugeDemand);
            shader.SetInt("_GaugeSourceProofSlot", sourceSlot);
            shader.SetInt("_GaugeProofCapacity", ProofPageCapacity);
        }

        internal SigmaGaugeProofLease BeginGaugePage(
            SigmaCarrierPageHandle source, SigmaGaugeMap map)
        {
            RequireAlive();
            int sourceSlot = DecodeSourceSlot(source);
            if (sourceSlot < 0)
                throw new InvalidOperationException(
                    "Gauge refinement requires an active source proof page.");
            if (_freeProofSlots.Count == 0)
                throw new InvalidOperationException(
                    "Constraint-certificate page budget is exhausted.");

            var sourceRecords = new List<(int Raw, int SourceBlock,
                int TargetBlock)>();
            int sourceBase = sourceSlot * BlocksPerPage;
            for (int sourceBlock = 0; sourceBlock < BlocksPerPage;
                ++sourceBlock)
            {
                int[] targetBlocks = SigmaGaugeRefinement.
                    TargetBlocksForSourceBlock(sourceBlock, map);
                int raw = _proofRawHeads[sourceBase + sourceBlock];
                int guard = 0;
                while (raw >= 0 && guard++ < RawTileCapacity)
                {
                    for (int target = 0; target < targetBlocks.Length; ++target)
                        sourceRecords.Add((raw, sourceBlock,
                            targetBlocks[target]));
                    raw = _rawNext[raw];
                }
                if (raw >= 0)
                    throw new InvalidOperationException(
                        "Gauge source raw chain is cyclic or corrupt.");
            }
            if (_freeRawTiles.Count < sourceRecords.Count)
                throw new InvalidOperationException(
                    "Gauge proof transport raw reserve is exhausted.");

            int targetSlot = _freeProofSlots.Pop();
            var plan = new SigmaGaugeRawClonePlan[sourceRecords.Count];
            int allocated = 0;
            try
            {
                for (int index = 0; index < sourceRecords.Count; ++index)
                {
                    int targetRaw = _freeRawTiles.Pop();
                    (int sourceRaw, int sourceBlock, int targetBlock) =
                        sourceRecords[index];
                    plan[index] = new SigmaGaugeRawClonePlan(sourceRaw,
                        targetRaw, targetBlock, sourceBlock);
                    allocated++;
                }
                return new SigmaGaugeProofLease(this, targetSlot, sourceSlot,
                    map, plan);
            }
            catch
            {
                for (int index = 0; index < allocated; ++index)
                    ReleaseUnusedReservation(
                        unchecked((int)plan[index].TargetRaw));
                ReleaseProofSlot(targetSlot, false);
                throw;
            }
        }

        internal void BindGaugeSource(ComputeShader shader, int kernel,
            SigmaGaugeProofLease page)
        {
            RequireGaugePage(page);
            shader.SetBuffer(kernel, "_GaugeSourceCertificates", _certificates);
            shader.SetBuffer(kernel, "_GaugeSourceBounds", _bounds);
            shader.SetBuffer(kernel, "_GaugeSourceBlocks", _blocks);
            shader.SetBuffer(kernel, "_GaugeDemand", _gaugeDemand);
            shader.SetInt("_GaugeSourceProofSlot", page.SourceSlot);
            shader.SetInt("_GaugeProofCapacity", ProofPageCapacity);
        }

        internal void BindGaugeTarget(ComputeShader shader, int kernel,
            SigmaGaugeProofLease page)
        {
            RequireGaugePage(page);
            shader.SetBuffer(kernel, "_GaugeTargetCertificates", _certificates);
            shader.SetBuffer(kernel, "_GaugeTargetBounds", _bounds);
            shader.SetBuffer(kernel, "_GaugeTargetBlocks", _blocks);
            shader.SetBuffer(kernel, "_GaugeTargetDemand", _gaugeDemand);
            shader.SetBuffer(kernel, "_GaugeRawTiles", _rawHeaders);
            shader.SetBuffer(kernel, "_GaugeRawWords", _rawWords);
            shader.SetInt("_GaugeTargetProofSlot", page.TargetSlot);
            shader.SetInt("_GaugeRawTileCapacity", RawTileCapacity);
            shader.SetInt("_GaugeProofCapacity", ProofPageCapacity);
        }

        internal void ValidateGaugeForPublication(SigmaGaugeProofLease page,
            NativeArray<uint> cloneStatus)
        {
            RequireGaugePage(page);
            if (cloneStatus.Length < page.ClonePlan.Length * 4)
                throw new InvalidOperationException(
                    "Gauge raw-clone status is incomplete.");
            for (int index = 0; index < page.ClonePlan.Length; ++index)
            {
                SigmaGaugeRawClonePlan plan = page.ClonePlan[index];
                int status = index * 4;
                bool used = cloneStatus[status] != 0u;
                if (cloneStatus[status + 1] != plan.TargetRaw ||
                    cloneStatus[status + 2] != plan.TargetBlock)
                    throw new InvalidOperationException(
                        "Gauge raw-clone status does not match its reservation.");
                if (!used)
                    continue;
                int sourceRaw = unchecked((int)plan.SourceRaw);
                int frame = _rawFrame[sourceRaw];
                if ((uint)frame >= (uint)_frameRefCount.Length)
                    throw new InvalidOperationException(
                        "Gauge raw proof lost its immutable frame record.");
                if (_frameRefCount[frame] == int.MaxValue)
                    throw new OverflowException(
                        "Gauge raw proof frame reference count overflowed.");
            }
        }

        internal void PublishGauge(SigmaGaugeProofLease page,
            NativeArray<uint> cloneStatus)
        {
            ValidateGaugeForPublication(page, cloneStatus);
            int targetBase = page.TargetSlot * BlocksPerPage;
            var heads = new int[BlocksPerPage];
            Array.Fill(heads, -1);
            for (int index = page.ClonePlan.Length - 1; index >= 0; --index)
            {
                SigmaGaugeRawClonePlan plan = page.ClonePlan[index];
                int status = index * 4;
                bool used = cloneStatus[status] != 0u;
                int targetRaw = unchecked((int)plan.TargetRaw);
                if (!used)
                {
                    ReleaseUnusedReservation(targetRaw);
                    continue;
                }
                int sourceRaw = unchecked((int)plan.SourceRaw);
                int targetBlock = unchecked((int)plan.TargetBlock);
                int frame = _rawFrame[sourceRaw];
                _rawNext[targetRaw] = heads[targetBlock];
                _rawFrame[targetRaw] = frame;
                checked { _frameRefCount[frame]++; }
                heads[targetBlock] = targetRaw;
            }
            for (int block = 0; block < BlocksPerPage; ++block)
                _proofRawHeads[targetBase + block] = heads[block];
            _proofActive[page.TargetSlot] = true;
            ReleaseProofSlot(page.SourceSlot, true);
            page.MarkPublished();
        }

        internal void AbortGauge(SigmaGaugeProofLease page)
        {
            if (_disposed || page == null || page.Owner != this ||
                page.IsDisposed || page.IsPublished)
                return;
            for (int index = 0; index < page.ClonePlan.Length; ++index)
                ReleaseUnusedReservation(unchecked((int)
                    page.ClonePlan[index].TargetRaw));
            ReleaseProofSlot(page.TargetSlot, false);
            page.MarkDisposed();
        }

        internal SigmaProofPageStatus ReadStatus(NativeArray<uint> status,
            SigmaProofPageLease page)
        {
            RequirePage(page);
            int offset = checked(page.TargetSlot * StatusStride);
            if (status.Length < offset + StatusStride)
                return new SigmaProofPageStatus(0,
                    SigmaProofMutationFlags.Failed, 0, 0, 0);
            ulong rawMask = status[offset + 2] |
                ((ulong)status[offset + 3] << 32);
            return new SigmaProofPageStatus(status[offset],
                (SigmaProofMutationFlags)status[offset + 1], rawMask,
                status[offset + 6], status[offset + 7]);
        }

        internal void ValidateForPublication(SigmaProofPageLease page,
            SigmaProofPageStatus status)
        {
            RequirePage(page);
            if (!status.IsValid)
                throw new InvalidOperationException(
                    $"Proof page {page.TargetSlot} failed closed: " +
                    $"blocks={status.CompletedBlocks}, flags={status.Flags}.");
        }

        internal void Publish(SigmaProofPageLease page,
            SigmaProofPageStatus status)
        {
            ValidateForPublication(page, status);
            int targetBase = page.TargetSlot * BlocksPerPage;
            int sourceBase = page.SourceSlot >= 0
                ? page.SourceSlot * BlocksPerPage : 0;
            for (int block = 0; block < BlocksPerPage; ++block)
            {
                bool used = (status.RawUsedMask & (1UL << block)) != 0;
                int oldHead = page.SourceSlot >= 0
                    ? _proofRawHeads[sourceBase + block] : -1;
                if (used)
                {
                    int raw = page.RawReservations[block];
                    _rawNext[raw] = oldHead;
                    _rawFrame[raw] = page.Frame.Slot;
                    checked { _frameRefCount[page.Frame.Slot]++; }
                    _proofRawHeads[targetBase + block] = raw;
                }
                else
                {
                    ReleaseUnusedReservation(page.RawReservations[block]);
                    _proofRawHeads[targetBase + block] = oldHead;
                }
                if (page.SourceSlot >= 0)
                    _proofRawHeads[sourceBase + block] = -1;
            }
            _proofActive[page.TargetSlot] = true;
            if (page.SourceSlot >= 0)
                ReleaseProofSlot(page.SourceSlot, false);
            page.MarkPublished();
        }

        internal void Abort(SigmaProofPageLease page)
        {
            if (_disposed || page == null || page.Owner != this ||
                page.IsPublished || page.IsDisposed)
                return;
            for (int index = 0; index < page.RawReservations.Length; ++index)
                ReleaseUnusedReservation(page.RawReservations[index]);
            ReleaseProofSlot(page.TargetSlot, false);
            page.MarkDisposed();
        }

        internal void CloseFrame(SigmaProofFrameLease frame)
        {
            if (_disposed || frame == null || frame.Owner != this ||
                !frame.IsActive)
                return;
            _frameOpen[frame.Slot] = false;
            frame.MarkDisposed();
            TryReleaseFrame(frame.Slot);
        }

        internal static ulong CertificateOffsetForSlot(int slot) =>
            checked((ulong)slot * CertificatesPerPage);

        internal static int DecodeCertificateSlot(ulong offset, uint count,
            int capacity)
        {
            if (count != CertificatesPerPage ||
                offset % CertificatesPerPage != 0)
                return -1;
            ulong slot = offset / CertificatesPerPage;
            return slot < (ulong)capacity ? checked((int)slot) : -1;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _certificates?.Dispose();
            _bounds?.Dispose();
            _blocks?.Dispose();
            _rawHeaders?.Dispose();
            _rawWords?.Dispose();
            _rawReservations?.Dispose();
            _rawAllocator?.Dispose();
            _rawLiveWords?.Dispose();
            _frameRecords?.Dispose();
            _rawRequests?.Dispose();
            _proofSamples?.Dispose();
            _proofSourceScratchMeta?.Dispose();
            _proofSourceScratchBounds?.Dispose();
            _gaugeCoordinateScratch?.Dispose();
            _pageStatus?.Dispose();
            _gaugeDemand?.Dispose();
            _certificates = null;
            _bounds = null;
            _blocks = null;
            _rawHeaders = null;
            _rawWords = null;
            _rawReservations = null;
            _rawAllocator = null;
            _rawLiveWords = null;
            _frameRecords = null;
            _rawRequests = null;
            _proofSamples = null;
            _proofSourceScratchMeta = null;
            _proofSourceScratchBounds = null;
            _gaugeCoordinateScratch = null;
            _pageStatus = null;
            _gaugeDemand = null;
        }

        private int DecodeSourceSlot(SigmaCarrierPageHandle source)
        {
            if (!source.IsValid || source.CertificateCount == 0)
                return -1;
            int slot = DecodeCertificateSlot(source.CertificateOffset,
                source.CertificateCount, ProofPageCapacity);
            if (slot < 0 || !_proofActive[slot])
                throw new InvalidOperationException(
                    "Carrier generation references an unavailable proof slot.");
            return slot;
        }

        private void BindCommon(SigmaProofPageLease page, int kernel)
        {
            _shader.SetInt("_SourceProofSlot", page.SourceSlot >= 0
                ? page.SourceSlot : unchecked((int)InvalidSlot));
            _shader.SetInt("_TargetProofSlot", page.TargetSlot);
            _shader.SetInt("_ProofFrameSlot", page.Frame.Slot);
            _shader.SetInt("_ProofCalibrationEpoch",
                unchecked((int)page.Frame.CalibrationEpoch));
            _shader.SetInt("_ProofRevision",
                unchecked((int)page.Frame.Revision));
            _shader.SetInt("_RawTileCapacity", RawTileCapacity);
        }

        private void ReleaseProofSlot(int slot, bool releaseRaw)
        {
            if ((uint)slot >= (uint)ProofPageCapacity)
                return;
            if (releaseRaw)
            {
                int baseIndex = slot * BlocksPerPage;
                for (int block = 0; block < BlocksPerPage; ++block)
                {
                    ReleaseRawChain(_proofRawHeads[baseIndex + block]);
                    _proofRawHeads[baseIndex + block] = -1;
                }
            }
            _proofActive[slot] = false;
            _freeProofSlots.Push(slot);
        }

        private void ReleaseRawChain(int head)
        {
            int guard = 0;
            while (head >= 0 && guard++ < RawTileCapacity)
            {
                int next = _rawNext[head];
                int frame = _rawFrame[head];
                _rawNext[head] = -1;
                _rawFrame[head] = -1;
                _freeRawTiles.Push(head);
                if (frame >= 0)
                {
                    _frameRefCount[frame]--;
                    TryReleaseFrame(frame);
                }
                head = next;
            }
        }

        private void ReleaseUnusedReservation(int raw)
        {
            if ((uint)raw >= (uint)RawTileCapacity)
                return;
            _rawNext[raw] = -1;
            _rawFrame[raw] = -1;
            _freeRawTiles.Push(raw);
        }

        private void TryReleaseFrame(int slot)
        {
            if ((uint)slot >= (uint)_frameRefCount.Length ||
                _frameOpen[slot] || _frameRefCount[slot] != 0)
                return;
            _freeFrameSlots.Push(slot);
        }

        private void RequirePage(SigmaProofPageLease page)
        {
            RequireAlive();
            if (page == null || page.Owner != this || page.IsDisposed ||
                page.IsPublished)
                throw new InvalidOperationException(
                    "Proof page transaction is not writable.");
        }

        private void RequireGaugePage(SigmaGaugeProofLease page)
        {
            RequireAlive();
            if (page == null || page.Owner != this || page.IsDisposed ||
                page.IsPublished)
                throw new InvalidOperationException(
                    "Gauge proof transaction is not writable.");
        }

        private void RequireAlive()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SigmaConstraintLedger));
        }

        private static GraphicsBuffer CreateBuffer(int count, int stride,
            string name) => new(GraphicsBuffer.Target.Structured,
                Math.Max(1, count), stride) { name = name };

        private static Stack<int> DescendingStack(int capacity)
        {
            var result = new Stack<int>(capacity);
            for (int index = capacity - 1; index >= 0; --index)
                result.Push(index);
            return result;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SigmaRawTimestampGpu
        {
            public uint SourceDomain;
            public uint Reserved;
            public uint SourceLo;
            public uint SourceHi;
            public uint UnixLo;
            public uint UnixHi;
            public uint UncertaintyLo;
            public uint UncertaintyHi;

            internal static SigmaRawTimestampGpu From(RigTimestamp value) => new()
            {
                SourceDomain = (uint)value.SourceDomain,
                SourceLo = Low(value.SourceNanoseconds),
                SourceHi = High(value.SourceNanoseconds),
                UnixLo = Low(value.UnixNanoseconds),
                UnixHi = High(value.UnixNanoseconds),
                UncertaintyLo = Low(value.MappingUncertaintyNanoseconds),
                UncertaintyHi = High(value.MappingUncertaintyNanoseconds),
            };
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SigmaRawPairingHealthGpu
        {
            public uint RgbDeltaLo;
            public uint RgbDeltaHi;
            public uint RgbDepthDeltaLo;
            public uint RgbDepthDeltaHi;
            public uint ClockUncertaintyLo;
            public uint ClockUncertaintyHi;
            public uint Reserved0;
            public uint Reserved1;

            internal static SigmaRawPairingHealthGpu From(
                RigPairingHealth value) => new()
                {
                    RgbDeltaLo = Low(value.RgbDeltaNanoseconds),
                    RgbDeltaHi = High(value.RgbDeltaNanoseconds),
                    RgbDepthDeltaLo = Low(value.RgbDepthDeltaNanoseconds),
                    RgbDepthDeltaHi = High(value.RgbDepthDeltaNanoseconds),
                    ClockUncertaintyLo = Low(value.ClockUncertaintyNanoseconds),
                    ClockUncertaintyHi = High(value.ClockUncertaintyNanoseconds),
                };
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SigmaRawFrameRecordGpu
        {
            public Matrix4x4 DepthWorldLeft;
            public Matrix4x4 DepthWorldRight;
            public Matrix4x4 RgbWorldLeft;
            public Matrix4x4 RgbWorldRight;
            public Vector4 DepthIntrinsicsLeft;
            public Vector4 DepthIntrinsicsRight;
            public Vector4 RgbIntrinsicsLeft;
            public Vector4 RgbIntrinsicsRight;
            public Vector4 DepthNearFarAndEpoch;
            public Vector4 Keys;
            public Vector4 SequenceAndRevision;
            public SigmaRawTimestampGpu RgbLeftTimestamp;
            public SigmaRawTimestampGpu RgbRightTimestamp;
            public SigmaRawTimestampGpu DepthLeftTimestamp;
            public SigmaRawTimestampGpu DepthRightTimestamp;
            public SigmaRawPairingHealthGpu PairingHealth;
            public Vector4 PoseGaugeMeta;
            public Vector4 PoseGaugeTranslation01;
            public Vector4 PoseGaugeTranslation2Rotation0;
            public Vector4 PoseGaugeRotation12;

            internal static SigmaRawFrameRecordGpu From(StereoRigFrameLease frame,
                uint revision, uint depthLeftKey, uint depthRightKey,
                uint rgbLeftKey, uint rgbRightKey) => new()
                {
                    DepthWorldLeft = World(frame.DepthLeft),
                    DepthWorldRight = World(frame.DepthRight),
                    RgbWorldLeft = World(frame.RgbLeft),
                    RgbWorldRight = World(frame.RgbRight),
                    DepthIntrinsicsLeft = Intrinsics(frame.DepthLeft),
                    DepthIntrinsicsRight = Intrinsics(frame.DepthRight),
                    RgbIntrinsicsLeft = Intrinsics(frame.RgbLeft),
                    RgbIntrinsicsRight = Intrinsics(frame.RgbRight),
                    DepthNearFarAndEpoch = new Vector4(frame.DepthNearFar.x,
                        frame.DepthNearFar.y, BitConverter.Int32BitsToSingle(
                            unchecked((int)frame.CalibrationEpoch)), 0f),
                    Keys = new Vector4(BitConverter.Int32BitsToSingle(
                            unchecked((int)depthLeftKey)),
                        BitConverter.Int32BitsToSingle(unchecked((int)depthRightKey)),
                        BitConverter.Int32BitsToSingle(unchecked((int)rgbLeftKey)),
                        BitConverter.Int32BitsToSingle(unchecked((int)rgbRightKey))),
                    SequenceAndRevision = new Vector4(
                        BitConverter.Int32BitsToSingle(unchecked((int)frame.Sequence)),
                        BitConverter.Int32BitsToSingle(unchecked((int)(frame.Sequence >> 32))),
                        BitConverter.Int32BitsToSingle(unchecked((int)revision)), 0f),
                    RgbLeftTimestamp = SigmaRawTimestampGpu.From(
                        frame.RgbLeft.Timestamp),
                    RgbRightTimestamp = SigmaRawTimestampGpu.From(
                        frame.RgbRight.Timestamp),
                    DepthLeftTimestamp = SigmaRawTimestampGpu.From(
                        frame.DepthLeft.Timestamp),
                    DepthRightTimestamp = SigmaRawTimestampGpu.From(
                        frame.DepthRight.Timestamp),
                    PairingHealth = SigmaRawPairingHealthGpu.From(frame.Health),
                };

            private static Matrix4x4 World(GpuImageView view) => Matrix4x4.TRS(
                view.WorldFromCamera.position, view.WorldFromCamera.rotation,
                Vector3.one);
            private static Vector4 Intrinsics(GpuImageView view) => new(
                view.Intrinsics.FocalLength.x, view.Intrinsics.FocalLength.y,
                view.Intrinsics.PrincipalPoint.x,
                view.Intrinsics.PrincipalPoint.y);
        }

        private static uint Low(long value) => unchecked((uint)value);
        private static uint High(long value) =>
            unchecked((uint)((ulong)value >> 32));
    }

    internal sealed class SigmaProofFrameLease : IDisposable
    {
        private bool _disposed;
        internal SigmaProofFrameLease(SigmaConstraintLedger owner, int slot,
            uint calibrationEpoch, uint revision)
        {
            Owner = owner;
            Slot = slot;
            CalibrationEpoch = calibrationEpoch;
            Revision = revision;
        }
        internal SigmaConstraintLedger Owner { get; }
        internal int Slot { get; }
        internal uint CalibrationEpoch { get; }
        internal uint Revision { get; }
        internal bool IsActive => !_disposed;
        internal void MarkDisposed() => _disposed = true;
        public void Dispose() => Owner?.CloseFrame(this);
    }

    internal sealed class SigmaProofPageLease : IDisposable
    {
        private bool _disposed;
        private bool _published;
        internal SigmaProofPageLease(SigmaConstraintLedger owner, int targetSlot,
            int sourceSlot, int[] rawReservations, SigmaProofFrameLease frame)
        {
            Owner = owner;
            TargetSlot = targetSlot;
            SourceSlot = sourceSlot;
            RawReservations = rawReservations;
            Frame = frame;
        }
        internal SigmaConstraintLedger Owner { get; }
        internal int TargetSlot { get; }
        internal int SourceSlot { get; }
        internal int[] RawReservations { get; }
        internal SigmaProofFrameLease Frame { get; }
        internal ulong CertificateOffset =>
            SigmaConstraintLedger.CertificateOffsetForSlot(TargetSlot);
        internal uint CertificateCount =>
            SigmaConstraintLedger.CertificatesPerPage;
        internal bool IsDisposed => _disposed;
        internal bool IsPublished => _published;
        internal void MarkPublished()
        {
            _published = true;
            _disposed = true;
        }
        internal void MarkDisposed() => _disposed = true;
        public void Dispose()
        {
            if (_disposed)
                return;
            Owner?.Abort(this);
        }
    }


    internal sealed class SigmaGaugeProofLease : IDisposable
    {
        private bool _disposed;
        private bool _published;

        internal SigmaGaugeProofLease(SigmaConstraintLedger owner,
            int targetSlot, int sourceSlot, SigmaGaugeMap map,
            SigmaGaugeRawClonePlan[] clonePlan)
        {
            Owner = owner;
            TargetSlot = targetSlot;
            SourceSlot = sourceSlot;
            Map = map;
            ClonePlan = clonePlan ?? Array.Empty<SigmaGaugeRawClonePlan>();
        }

        internal SigmaConstraintLedger Owner { get; }
        internal int TargetSlot { get; }
        internal int SourceSlot { get; }
        internal SigmaGaugeMap Map { get; }
        internal SigmaGaugeRawClonePlan[] ClonePlan { get; }
        internal ulong CertificateOffset =>
            SigmaConstraintLedger.CertificateOffsetForSlot(TargetSlot);
        internal uint CertificateCount =>
            SigmaConstraintLedger.CertificatesPerPage;
        internal bool IsDisposed => _disposed;
        internal bool IsPublished => _published;
        internal void MarkPublished()
        {
            _published = true;
            _disposed = true;
        }
        internal void MarkDisposed() => _disposed = true;
        public void Dispose()
        {
            if (_disposed)
                return;
            Owner?.AbortGauge(this);
        }
    }
}
