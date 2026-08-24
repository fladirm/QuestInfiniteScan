using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.SigmaPrism
{
    /// <summary>
    /// The rigid I/O frame of the canonical carrier. Sensor poses enter room
    /// space before exact inverse work; readout leaves it only at presentation.
    /// </summary>
    internal static class SigmaRoomFrame
    {
        internal static Matrix4x4 FromCamera(Matrix4x4 worldToRoom,
            Pose worldFromCamera) => worldToRoom * Matrix4x4.TRS(
                worldFromCamera.position, worldFromCamera.rotation,
                Vector3.one);

        internal static Pose CameraPose(Matrix4x4 worldToRoom,
            Pose worldFromCamera)
        {
            Matrix4x4 roomFromCamera = FromCamera(worldToRoom,
                worldFromCamera);
            return new Pose(new Vector3(roomFromCamera.m03,
                    roomFromCamera.m13, roomFromCamera.m23),
                roomFromCamera.rotation);
        }

        internal static Matrix4x4 ToUnityWorld => RoomSpaceRoot.RoomToWorld;
    }

    /// <summary>
    /// Immutable inputs consumed while recording one whole coherent frame. The
    /// caller keeps every lease alive until the completion fence closes.
    /// </summary>
    internal readonly struct SigmaFrameInverseInput
    {
        internal SigmaFrameInverseInput(SigmaPredictionFrameLease prediction,
            RenderTexture metricDepth, RenderTexture depthFlags,
            GraphicsBuffer depthCalibration, GraphicsBuffer rgbCalibration,
            GraphicsBuffer poseResult, ConeLutLease coneLuts,
            uint depthLeftKey, uint depthRightKey, uint rgbLeftKey,
            uint rgbRightKey,
            IReadOnlyList<SigmaCarrierReadBatch> carrierSegments)
        {
            Prediction = prediction ?? throw new ArgumentNullException(
                nameof(prediction));
            MetricDepth = metricDepth != null ? metricDepth :
                throw new ArgumentNullException(nameof(metricDepth));
            DepthFlags = depthFlags != null ? depthFlags :
                throw new ArgumentNullException(nameof(depthFlags));
            DepthCalibration = depthCalibration ?? throw new ArgumentNullException(
                nameof(depthCalibration));
            RgbCalibration = rgbCalibration ?? throw new ArgumentNullException(
                nameof(rgbCalibration));
            PoseResult = poseResult ?? throw new ArgumentNullException(
                nameof(poseResult));
            ConeLuts = coneLuts ?? throw new ArgumentNullException(
                nameof(coneLuts));
            DepthLeftKey = depthLeftKey;
            DepthRightKey = depthRightKey;
            RgbLeftKey = rgbLeftKey;
            RgbRightKey = rgbRightKey;
            CarrierSegments = carrierSegments;
        }

        internal SigmaPredictionFrameLease Prediction { get; }
        internal RenderTexture MetricDepth { get; }
        internal RenderTexture DepthFlags { get; }
        internal GraphicsBuffer DepthCalibration { get; }
        internal GraphicsBuffer RgbCalibration { get; }
        internal GraphicsBuffer PoseResult { get; }
        internal ConeLutLease ConeLuts { get; }
        internal uint DepthLeftKey { get; }
        internal uint DepthRightKey { get; }
        internal uint RgbLeftKey { get; }
        internal uint RgbRightKey { get; }
        internal IReadOnlyList<SigmaCarrierReadBatch> CarrierSegments { get; }
    }

    /// <summary>
    /// Fixed direct GPU dataflow for one coherent four-source observation. It has
    /// no transaction, tile, page-closure or token scheduler state. Candidate
    /// closure and shadow publication remain one fixed dataflow.
    /// </summary>
    internal sealed class SigmaFrameGraph : IDisposable
    {
        private const string InverseResource = "SigmaPrism/SigmaFrameInverse";
        private const string ClosureResource = "SigmaPrism/SigmaFrameClosure";
        private const string PublishResource = "SigmaPrism/SigmaFramePublish";
        private const int ProposalsPerFootprint = 4;
        private const int FootprintsPerCoordinateGroup = 16;

        private readonly ComputeShader _inverse;
        private readonly ComputeShader _closure;
        private readonly ComputeShader _publish;
        private readonly SigmaExactBackendGate _backendGate;
        private readonly GraphicsBuffer _rgbViewOperators;
        private readonly GraphicsBuffer _rgbViewSupportScale;
        private readonly GraphicsBuffer _nullCarrierState;
        private readonly GraphicsBuffer _nullPageMetadata;
        private readonly GraphicsBuffer _nullPublicationRoot;
        private readonly int _clearKernel;
        private readonly int _pendingProjectionClearKernel;
        private readonly int _pendingProjectionDepthKernel;
        private readonly int _pendingProjectionResolveKernel;
        private readonly int _proposalKernel;
        private readonly int _depthKernel;
        private readonly int _rgbKernel;
        private readonly int _evaluateKernel;
        private readonly int _compactKernel;
        private readonly int[] _closureKernels;
        private readonly int[] _reductionKernels;
        private readonly int[] _publishKernels;
        private bool _disposed;

        internal SigmaFrameGraph(Vector2Int resolution,
            SigmaExactBackendGate backendGate,
            SigmaFrameMemoryProfile profile =
                SigmaFrameMemoryProfile.HighThroughput)
            : this(resolution, backendGate, profile,
                SystemInfo.maxGraphicsBufferSize)
        {
        }

        internal SigmaFrameGraph(Vector2Int resolution,
            SigmaExactBackendGate backendGate, SigmaFrameMemoryProfile profile,
            long bindingLimit)
        {
            _backendGate = backendGate ?? throw new ArgumentNullException(
                nameof(backendGate));
            _inverse = UnityEngine.Resources.Load<ComputeShader>(InverseResource);
            _closure = UnityEngine.Resources.Load<ComputeShader>(ClosureResource);
            _publish = UnityEngine.Resources.Load<ComputeShader>(PublishResource);
            if (_inverse == null || _closure == null || _publish == null)
                throw new InvalidOperationException(
                    "Required Sigma direct-frame shaders are missing.");
            _clearKernel = _inverse.FindProfiledKernel("ClearFrameState");
            _pendingProjectionClearKernel = _inverse.FindProfiledKernel(
                "ClearPendingProjection");
            _pendingProjectionDepthKernel = _inverse.FindProfiledKernel(
                "ProjectPendingDepth");
            _pendingProjectionResolveKernel = _inverse.FindProfiledKernel(
                "ResolvePendingProjection");
            _proposalKernel = _inverse.FindProfiledKernel(
                "BuildFrameProposals");
            _depthKernel = _inverse.FindProfiledKernel("BuildDepthSourceCells");
            _rgbKernel = _inverse.FindProfiledKernel("BuildRgbSourceCells");
            _evaluateKernel = _inverse.FindProfiledKernel(
                "EvaluateCandidateMeets");
            _compactKernel = _inverse.FindProfiledKernel(
                "CompactResolvedTargets");
            _closureKernels = FindKernels(_closure,
                "ClearExactClosure", "BuildPendingEdges",
                "GatherEdgeEndpoints", "ClosePendingEdges",
                "ApplyPendingEdges", "RelaxPendingLabels",
                "FinalizePendingGauges", "DeferUnresolvedEdges",
                "FinalizeExactClosure", "MarkPendingRetention",
                "AssignPendingRetentionSlots", "PersistPendingTargets",
                "CommitPendingRetention");
            _reductionKernels = FindKernels(_closure,
                "ClearTargetReduction", "SortTargetBlocks",
                "MergeTargetStage", "MergeTargetTails",
                "MarkTargetHeads", "ScanTargetHeads",
                "ScanTargetHeadBlocks", "ScanTargetHeadSupers",
                "CompactTargetHeads", "MapTargetOrdinals",
                "ReduceTargetWindow", "FinalizeReducedTargets");
            _publishKernels = FindKernels(_publish,
                "ClearPublicationState", "AccumulateNovelBounds",
                "ReserveNovelExtent", "MapFrameTargets", "MarkPageHeads",
                "ScanPageValues", "ScanPageBlocks", "ScanPageSupers",
                "CompactPageHeads", "MapPageOrdinals",
                "FindExistingPages", "MarkMissingPages", "CountFreePairs",
                "ScanFreeSegments", "CommitPhysicalAllocation",
                "AssignPagePairs", "PropagatePageMappings", "BeginSegment",
                "MarkSegmentPages", "PrepareSegmentPages",
                "ScatterSegmentTargets", "CloseFrameRevision",
                "PublishFrameRevision");

            Resources = new SigmaFrameResources(resolution, profile,
                bindingLimit);
            try
            {
                SigmaRgbViewCatalog catalog = SigmaRgbViewCatalog.CreateCanonical();
                var operators = new SigmaFrameUInt2Gpu[
                    catalog.OperatorRaw.Count];
                for (int index = 0; index < operators.Length; ++index)
                    operators[index] = Packed(catalog.OperatorRaw[index]);
                _rgbViewOperators = CreateBuffer(operators.Length,
                    SigmaGeneratedFrame.PackedQ48Stride,
                    "Sigma frame RGB view operators");
                _rgbViewOperators.SetData(operators);
                var scales = new uint[catalog.SupportScale.Count];
                for (int index = 0; index < scales.Length; ++index)
                    scales[index] = catalog.SupportScale[index];
                _rgbViewSupportScale = CreateBuffer(scales.Length,
                    sizeof(uint), "Sigma frame RGB view support");
                _rgbViewSupportScale.SetData(scales);
                _nullCarrierState = CreateBuffer(1,
                    SigmaGeneratedFrame.PackedQ48Stride,
                    "Sigma frame null carrier binding");
                _nullPageMetadata = CreateBuffer(1,
                    SigmaCarrier.PageMetadataStride,
                    "Sigma frame null page metadata binding");
                _nullPublicationRoot = CreateBuffer(1, sizeof(uint),
                    "Sigma frame null publication root");
                _nullCarrierState.SetData(new SigmaFrameUInt2Gpu[1]);
                _nullPageMetadata.SetData(new uint[12]);
                _nullPublicationRoot.SetData(new uint[1]);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal SigmaFrameResources Resources { get; }

        internal bool TryAcquire(uint revision, uint calibrationEpoch,
            SigmaFrameInverseInput input, out SigmaOwnedFrameLease lease)
        {
            RequireAlive();
            if (input.CarrierSegments != null &&
                (!ValidatePublicationSegments(input.CarrierSegments,
                    out int totalPhysicalPages, out _) ||
                 !Resources.TryEnsurePublicationCapacity(
                    input.CarrierSegments.Count, totalPhysicalPages)))
            {
                lease = null;
                return false;
            }
            return Resources.TryAcquireFrame(revision, calibrationEpoch,
                input.DepthLeftKey, input.DepthRightKey, input.RgbLeftKey,
                input.RgbRightKey, input.Prediction.PoseGauge.Revision,
                input.Prediction.TargetGeneration, out lease);
        }

        internal void RecordSourceAndResolve(CommandBuffer command,
            SigmaOwnedFrameLease ownedFrame, uint revision,
            SigmaFrameInverseInput input,
            int footprintWindow = int.MaxValue)
        {
            RequireAlive();
            if (command == null)
                throw new ArgumentNullException(nameof(command));
            if (ownedFrame == null)
                throw new ArgumentNullException(nameof(ownedFrame));
            StereoRigFrameLease source = input.Prediction.Source;
            if (!source.IsValid || source.DepthResolution != Resources.Resolution)
                throw new InvalidOperationException(
                    "Direct-frame input does not match its owned frame window.");
            if (revision == 0u || input.ConeLuts.IsDisposed)
                throw new InvalidOperationException(
                    "Direct-frame revision or calibration lease is invalid.");
            if (footprintWindow <= 0)
                throw new ArgumentOutOfRangeException(nameof(footprintWindow));
            footprintWindow = Math.Min(footprintWindow,
                Resources.FootprintCount);

            SigmaFrameSourceStorage sources = ownedFrame.Sources;
            long candidateRecords = checked((long)Resources.FootprintCount *
                ProposalsPerFootprint);
            if (!Resources.TryEnsureCandidateCapacity(candidateRecords))
                throw new InvalidOperationException(
                    "Direct-frame candidate window cannot be admitted losslessly.");
            RecordPendingProjection(command, source, input, revision,
                ownedFrame.Slot);
            GraphicsBuffer resolvedBlockCounts =
                Resources.ResolvedBlockCounts.Segment(0).Buffer;
            for (int windowIndex = 0;
                windowIndex < Resources.ExecutionWindowCount; ++windowIndex)
            {
                SigmaFrameExecutionWindow window =
                    Resources.ExecutionWindow(windowIndex);
                int windowCandidates = checked(window.FootprintCount *
                    ProposalsPerFootprint);
                int resolvedBlockCount = CeilDiv(window.FootprintCount, 256);
                GraphicsBuffer candidates = Window(Resources.Candidates, window,
                    ProposalsPerFootprint);
                GraphicsBuffer outcomes = Window(Resources.Outcomes, window,
                    ProposalsPerFootprint);
                GraphicsBuffer candidateStates = Window(Resources.CandidateStates,
                    window, ProposalsPerFootprint * SigmaGeneratedFrame.LaneCount);
                GraphicsBuffer deltas = Resources.Deltas.Segment(0).Buffer;
                GraphicsBuffer resolvedIndices =
                    Resources.ResolvedIndices.Segment(0).Buffer;

                BindFrameConstants(command, source, revision, ownedFrame.Slot,
                    input, windowCandidates, resolvedBlockCount);
                BindExecutionWindow(command, window);
                command.SetComputeBufferParam(_inverse, _clearKernel,
                    "_OwnedFrames", Resources.OwnedFrames);
                command.SetComputeBufferParam(_inverse, _clearKernel,
                    "_FrameOutcomes", outcomes);
                command.SetComputeBufferParam(_inverse, _clearKernel,
                    "_FrameDeltas", deltas);
                command.SetComputeBufferParam(_inverse, _clearKernel,
                    "_FrameResolvedBlockCounts", resolvedBlockCounts);
                command.SetComputeBufferParam(_inverse, _clearKernel,
                    "_FrameResolvedIndices", resolvedIndices);
                command.DispatchComputeProfiled(_inverse, _clearKernel,
                    CeilDiv(Math.Max(windowCandidates, window.FootprintCount), 256),
                    1, 1);

                BindProposal(command, source, input, candidates, window);
                RecordWindowChunks(command, _proposalKernel, window,
                    footprintWindow, 256);
                RecordDepthSource(command, input, sources,
                    SigmaFrameSource.DepthLeft, window, footprintWindow);
                RecordDepthSource(command, input, sources,
                    SigmaFrameSource.DepthRight, window, footprintWindow);
                RecordRgbSource(command, source, input, sources,
                    SigmaFrameSource.RgbLeft, window, footprintWindow);
                RecordRgbSource(command, source, input, sources,
                    SigmaFrameSource.RgbRight, window, footprintWindow);
                BindFootprintWindow(command, window.FirstFootprint,
                    window.FootprintCount);

                BindEvaluate(command, input, sources, window, candidates,
                    outcomes, candidateStates);
                IReadOnlyList<SigmaCarrierReadBatch> readable =
                    input.CarrierSegments;
                if (readable != null)
                {
                    for (int index = 0; index < readable.Count; ++index)
                    {
                        SigmaCarrierReadBatch batch = readable[index];
                        BindCarrier(command, batch.State, batch.Metadata,
                            batch.PublicationRoot, batch.SegmentIndex,
                            batch.PageCapacity,
                            (uint)SigmaFrameProposalKind.Current);
                        command.DispatchComputeProfiled(_inverse, _evaluateKernel,
                            CeilDiv(windowCandidates,
                                FootprintsPerCoordinateGroup), 1, 1);
                    }
                }
                BindCarrier(command, _nullCarrierState, _nullPageMetadata,
                    _nullPublicationRoot, -1, 1,
                    (uint)SigmaFrameProposalKind.Pending);
                for (int pendingWindowIndex = 0;
                    pendingWindowIndex < Resources.ExecutionWindowCount;
                    ++pendingWindowIndex)
                {
                    BindPendingInverseWindow(command, _evaluateKernel,
                        Resources.ExecutionWindow(pendingWindowIndex));
                    command.DispatchComputeProfiled(_inverse, _evaluateKernel,
                        CeilDiv(windowCandidates,
                            FootprintsPerCoordinateGroup), 1, 1);
                }
                BindCarrier(command, _nullCarrierState, _nullPageMetadata,
                    _nullPublicationRoot, -1, 1,
                    (uint)SigmaFrameProposalKind.Novel);
                command.DispatchComputeProfiled(_inverse, _evaluateKernel,
                    CeilDiv(windowCandidates, FootprintsPerCoordinateGroup), 1, 1);

                RecordCompactWindow(command, window, revision);
            }
        }

        private void RecordPendingProjection(CommandBuffer command,
            StereoRigFrameLease source, SigmaFrameInverseInput input,
            uint revision, int frameSlot)
        {
            BindFrameConstants(command, source, revision, frameSlot, input,
                checked(Resources.FootprintCount * ProposalsPerFootprint),
                CeilDiv(Resources.FootprintCount, 256));
            for (int projectionWindowIndex = 0;
                projectionWindowIndex < Resources.ExecutionWindowCount;
                ++projectionWindowIndex)
            {
                SigmaFrameExecutionWindow projectionWindow =
                    Resources.ExecutionWindow(projectionWindowIndex);
                BindPendingProjectionOutput(command,
                    _pendingProjectionClearKernel, projectionWindow);
                command.DispatchComputeProfiled(_inverse,
                    _pendingProjectionClearKernel,
                    CeilDiv(checked(projectionWindow.FootprintCount * 2), 256),
                    1, 1);

                for (int pendingWindowIndex = 0;
                    pendingWindowIndex < Resources.ExecutionWindowCount;
                    ++pendingWindowIndex)
                {
                    SigmaFrameExecutionWindow pendingWindow =
                        Resources.ExecutionWindow(pendingWindowIndex);
                    BindPendingProjectionInput(command,
                        _pendingProjectionDepthKernel, pendingWindow,
                        projectionWindow, input.PoseResult);
                    command.DispatchComputeProfiled(_inverse,
                        _pendingProjectionDepthKernel,
                        CeilDiv(pendingWindow.FootprintCount, 64), 1, 1);
                }
                for (int pendingWindowIndex = 0;
                    pendingWindowIndex < Resources.ExecutionWindowCount;
                    ++pendingWindowIndex)
                {
                    SigmaFrameExecutionWindow pendingWindow =
                        Resources.ExecutionWindow(pendingWindowIndex);
                    BindPendingProjectionInput(command,
                        _pendingProjectionResolveKernel, pendingWindow,
                        projectionWindow, input.PoseResult);
                    command.DispatchComputeProfiled(_inverse,
                        _pendingProjectionResolveKernel,
                        CeilDiv(pendingWindow.FootprintCount, 64), 1, 1);
                }
            }
        }

        internal void RecordCompactWindow(CommandBuffer command,
            int windowIndex, uint revision)
        {
            RequireAlive();
            if (command == null)
                throw new ArgumentNullException(nameof(command));
            if (revision == 0u)
                throw new ArgumentOutOfRangeException(nameof(revision));
            RecordCompactWindow(command,
                Resources.ExecutionWindow(windowIndex), revision);
        }

        internal void RecordTargetReduction(CommandBuffer command,
            SigmaOwnedFrameLease ownedFrame, SigmaFrameInverseInput input)
        {
            RequireAlive();
            if (command == null)
                throw new ArgumentNullException(nameof(command));
            if (ownedFrame == null)
                throw new ArgumentNullException(nameof(ownedFrame));
            _ = ownedFrame.Sources;

            int footprints = Resources.FootprintCount;
            int sortCapacity = Resources.TargetSortCapacity;
            int headBlocks = CeilDiv(footprints, 256);
            int headSupers = CeilDiv(headBlocks, 256);
            if (headSupers > 256)
                throw new InvalidOperationException(
                    "Direct target-head scan exceeds its exact fixed circuit.");

            for (int index = 0; index < _reductionKernels.Length; ++index)
                BindTargetReductionBase(command, _reductionKernels[index], input,
                    sortCapacity, headBlocks, headSupers);
            command.SetComputeIntParam(_closure, "_ScanOutputMode", 0);

            command.DispatchComputeProfiled(_closure, _reductionKernels[0],
                CeilDiv(sortCapacity, 256), 1, 1);

            // One global fixed bitonic circuit is the R2 correctness lowering.
            // R3's complete-file closure replacement may fuse its local stages;
            // dispatch partition has no authority over the ordered target stream.
            for (int width = 2; ; width <<= 1)
            {
                command.SetComputeIntParam(_closure, "_SortK", width);
                for (int stride = width >> 1; stride != 0; stride >>= 1)
                {
                    command.SetComputeIntParam(_closure, "_SortJ", stride);
                    command.DispatchComputeProfiled(_closure,
                        _reductionKernels[2], CeilDiv(sortCapacity, 256), 1, 1);
                }
                if (width == sortCapacity)
                    break;
            }

            command.DispatchComputeProfiled(_closure, _reductionKernels[4],
                headBlocks, 1, 1);
            command.DispatchComputeProfiled(_closure, _reductionKernels[5],
                headBlocks, 1, 1);
            command.DispatchComputeProfiled(_closure, _reductionKernels[6],
                headSupers, 1, 1);
            command.DispatchComputeProfiled(_closure, _reductionKernels[7],
                1, 1, 1);
            command.DispatchComputeProfiled(_closure, _reductionKernels[8],
                headBlocks, 1, 1);
            command.DispatchComputeProfiled(_closure, _reductionKernels[9],
                headBlocks, 1, 1);

            for (int targetWindowIndex = 0;
                targetWindowIndex < Resources.ExecutionWindowCount;
                ++targetWindowIndex)
            {
                SigmaFrameExecutionWindow targetWindow =
                    Resources.ExecutionWindow(targetWindowIndex);
                BindTargetWindow(command, _reductionKernels[10], targetWindow);
                for (int sourceWindowIndex = 0;
                    sourceWindowIndex < Resources.ExecutionWindowCount;
                    ++sourceWindowIndex)
                {
                    SigmaFrameExecutionWindow sourceWindow =
                        Resources.ExecutionWindow(sourceWindowIndex);
                    BindReductionSourceWindow(command, _reductionKernels[10],
                        sourceWindow);
                    command.SetComputeIntParam(_closure,
                        "_ReductionFirstWindow", sourceWindowIndex == 0 ? 1 : 0);
                    command.DispatchComputeProfiled(_closure,
                        _reductionKernels[10], targetWindow.FootprintCount, 1, 1);
                }
            }

            IReadOnlyList<SigmaCarrierReadBatch> readable =
                input.CarrierSegments;
            for (int targetWindowIndex = 0;
                targetWindowIndex < Resources.ExecutionWindowCount;
                ++targetWindowIndex)
            {
                SigmaFrameExecutionWindow targetWindow =
                    Resources.ExecutionWindow(targetWindowIndex);
                BindTargetWindow(command, _reductionKernels[11], targetWindow);
                // Vulkan requires every statically referenced resource to be
                // bound even when the proposal-kind branch does not consume it.
                // The first pending window is a neutral binding for CURRENT and
                // NOVEL; PENDING dispatches replace it with each logical window.
                BindPendingClosureReadWindow(command, _reductionKernels[11],
                    Resources.ExecutionWindow(0));
                command.SetComputeIntParam(_closure, "_FinalizeProposalKind",
                    (int)SigmaFrameProposalKind.Current);
                if (readable != null)
                {
                    for (int index = 0; index < readable.Count; ++index)
                    {
                        BindReductionCarrier(command, _reductionKernels[11],
                            readable[index]);
                        command.DispatchComputeProfiled(_closure,
                            _reductionKernels[11],
                            CeilDiv(targetWindow.FootprintCount, 16), 1, 1);
                    }
                }
                command.SetComputeIntParam(_closure, "_FinalizeProposalKind",
                    (int)SigmaFrameProposalKind.Pending);
                BindReductionCarrier(command, _reductionKernels[11],
                    _nullCarrierState, _nullPageMetadata, _nullPublicationRoot,
                    -1, 1);
                for (int pendingWindowIndex = 0;
                    pendingWindowIndex < Resources.ExecutionWindowCount;
                    ++pendingWindowIndex)
                {
                    BindPendingClosureReadWindow(command,
                        _reductionKernels[11],
                        Resources.ExecutionWindow(pendingWindowIndex));
                    command.DispatchComputeProfiled(_closure,
                        _reductionKernels[11],
                        CeilDiv(targetWindow.FootprintCount, 16), 1, 1);
                }
                command.SetComputeIntParam(_closure, "_FinalizeProposalKind",
                    (int)SigmaFrameProposalKind.Novel);
                BindReductionCarrier(command, _reductionKernels[11],
                    _nullCarrierState, _nullPageMetadata, _nullPublicationRoot,
                    -1, 1);
                command.DispatchComputeProfiled(_closure,
                    _reductionKernels[11],
                    CeilDiv(targetWindow.FootprintCount, 16), 1, 1);
            }
        }

        internal void RecordExactClosure(CommandBuffer command,
            SigmaOwnedFrameLease ownedFrame, uint revision,
            SigmaFrameInverseInput input)
        {
            RequireAlive();
            if (command == null)
                throw new ArgumentNullException(nameof(command));
            if (ownedFrame == null)
                throw new ArgumentNullException(nameof(ownedFrame));
            if (revision == 0u)
                throw new ArgumentOutOfRangeException(nameof(revision));
            RecordTargetReduction(command, ownedFrame, input);
            RecordExactClosureOnly(command, ownedFrame, revision, input);
        }

        internal void RecordExactClosureOnly(CommandBuffer command,
            SigmaOwnedFrameLease ownedFrame, uint revision,
            SigmaFrameInverseInput input)
        {
            RequireAlive();
            if (command == null)
                throw new ArgumentNullException(nameof(command));
            if (ownedFrame == null)
                throw new ArgumentNullException(nameof(ownedFrame));
            if (revision == 0u)
                throw new ArgumentOutOfRangeException(nameof(revision));
            int footprints = Resources.FootprintCount;
            for (int index = 0; index < _closureKernels.Length; ++index)
                BindExactClosureBase(command, _closureKernels[index],
                    ownedFrame, revision, input);
            command.DispatchComputeProfiled(_closure, _closureKernels[0],
                CeilDiv(footprints, 256), 1, 1);

            for (int edgeWindowIndex = 0;
                edgeWindowIndex < Resources.ExecutionWindowCount;
                ++edgeWindowIndex)
            {
                SigmaFrameExecutionWindow edgeWindow =
                    Resources.ExecutionWindow(edgeWindowIndex);
                BindExactEdgeWindow(command, _closureKernels[1], edgeWindow);
                command.DispatchComputeProfiled(_closure, _closureKernels[1],
                    CeilDiv(edgeWindow.FootprintCount, 256), 1, 1);
            }

            for (int edgeWindowIndex = 0;
                edgeWindowIndex < Resources.ExecutionWindowCount;
                ++edgeWindowIndex)
            {
                SigmaFrameExecutionWindow edgeWindow =
                    Resources.ExecutionWindow(edgeWindowIndex);
                BindExactEdgeWindow(command, _closureKernels[2], edgeWindow);
                for (int targetWindowIndex = 0;
                    targetWindowIndex < Resources.ExecutionWindowCount;
                    ++targetWindowIndex)
                {
                    BindExactTargetWindow(command, _closureKernels[2],
                        Resources.ExecutionWindow(targetWindowIndex));
                    command.DispatchComputeProfiled(_closure,
                        _closureKernels[2],
                        CeilDiv(edgeWindow.FootprintCount, 16), 1, 1);
                }
                BindExactEdgeWindow(command, _closureKernels[3], edgeWindow);
                command.DispatchComputeProfiled(_closure, _closureKernels[3],
                    checked(edgeWindow.FootprintCount * 2), 1, 1);
            }

            // Classification is immutable for the pass. Mark every unresolved
            // claimed endpoint before any component union so dispatch/window
            // order cannot give a deferred mutation connectivity authority.
            for (int edgeWindowIndex = 0;
                edgeWindowIndex < Resources.ExecutionWindowCount;
                ++edgeWindowIndex)
            {
                SigmaFrameExecutionWindow edgeWindow =
                    Resources.ExecutionWindow(edgeWindowIndex);
                BindExactEdgeWindow(command, _closureKernels[7], edgeWindow);
                command.DispatchComputeProfiled(_closure, _closureKernels[7],
                    CeilDiv(checked(edgeWindow.FootprintCount * 2), 256), 1, 1);
            }

            for (int edgeWindowIndex = 0;
                edgeWindowIndex < Resources.ExecutionWindowCount;
                ++edgeWindowIndex)
            {
                SigmaFrameExecutionWindow edgeWindow =
                    Resources.ExecutionWindow(edgeWindowIndex);
                BindExactEdgeWindow(command, _closureKernels[4], edgeWindow);
                command.DispatchComputeProfiled(_closure, _closureKernels[4],
                    CeilDiv(checked(edgeWindow.FootprintCount * 2), 256), 1, 1);
            }

            for (int pass = 0; pass < 18; ++pass)
                command.DispatchComputeProfiled(_closure, _closureKernels[5],
                    CeilDiv(footprints, 256), 1, 1);
            command.DispatchComputeProfiled(_closure, _closureKernels[6],
                CeilDiv(footprints, 256), 1, 1);
            command.DispatchComputeProfiled(_closure, _closureKernels[8],
                CeilDiv(footprints, 256), 1, 1);

            int headBlocks = CeilDiv(footprints, 256);
            int headSupers = CeilDiv(headBlocks, 256);
            command.DispatchComputeProfiled(_closure, _closureKernels[9],
                headBlocks, 1, 1);
            for (int scan = 5; scan <= 7; ++scan)
                BindTargetReductionBase(command, _reductionKernels[scan], input,
                    Resources.TargetSortCapacity, headBlocks, headSupers);
            command.SetComputeIntParam(_closure, "_ScanOutputMode", 1);
            command.DispatchComputeProfiled(_closure, _reductionKernels[5],
                headBlocks, 1, 1);
            command.DispatchComputeProfiled(_closure, _reductionKernels[6],
                headSupers, 1, 1);
            command.DispatchComputeProfiled(_closure, _reductionKernels[7],
                1, 1, 1);
            command.DispatchComputeProfiled(_closure, _closureKernels[10],
                headBlocks, 1, 1);
            for (int targetWindowIndex = 0;
                targetWindowIndex < Resources.ExecutionWindowCount;
                ++targetWindowIndex)
            {
                SigmaFrameExecutionWindow targetWindow =
                    Resources.ExecutionWindow(targetWindowIndex);
                BindExactTargetWindow(command, _closureKernels[11],
                    targetWindow);
                for (int pendingWindowIndex = 0;
                    pendingWindowIndex < Resources.ExecutionWindowCount;
                    ++pendingWindowIndex)
                {
                    BindPendingClosureWriteWindow(command,
                        _closureKernels[11],
                        Resources.ExecutionWindow(pendingWindowIndex));
                    command.DispatchComputeProfiled(_closure,
                        _closureKernels[11], targetWindow.FootprintCount, 1, 1);
                }
            }
            command.DispatchComputeProfiled(_closure, _closureKernels[12],
                1, 1, 1);
            command.SetComputeIntParam(_closure, "_ScanOutputMode", 0);
        }

        internal void RecordPublication(CommandBuffer command,
            SigmaOwnedFrameLease ownedFrame, uint revision,
            SigmaFrameInverseInput input)
        {
            RequireAlive();
            if (command == null)
                throw new ArgumentNullException(nameof(command));
            if (ownedFrame == null)
                throw new ArgumentNullException(nameof(ownedFrame));
            if (revision == 0u ||
                !ValidatePublicationSegments(input.CarrierSegments,
                    out int totalPhysicalPages, out int totalPairCapacity))
                throw new InvalidOperationException(
                    "Direct-frame publication has no complete carrier backing.");
            if (!Resources.TryEnsurePublicationCapacity(
                    input.CarrierSegments.Count, totalPhysicalPages))
                throw new InvalidOperationException(
                    "Direct-frame publication storage cannot retain the revision.");

            int footprintCount = Resources.FootprintCount;
            int sortCapacity = Resources.TargetSortCapacity;
            int footprintGroups = CeilDiv(footprintCount, 256);
            int scanBlocks = CeilDiv(footprintCount, 256);
            int scanSupers = CeilDiv(scanBlocks, 256);
            for (int index = 0; index < _publishKernels.Length; ++index)
                BindPublicationBase(command, _publishKernels[index], ownedFrame,
                    revision, input, 2);

            command.DispatchComputeProfiled(_publish, _publishKernels[0],
                CeilDiv(Math.Max(sortCapacity, footprintCount), 256), 1, 1);
            command.DispatchComputeProfiled(_publish, _publishKernels[1],
                footprintGroups, 1, 1);
            command.DispatchComputeProfiled(_publish, _publishKernels[2],
                1, 1, 1);
            command.DispatchComputeProfiled(_publish, _publishKernels[3],
                footprintGroups, 1, 1);
            RecordMappedTargetSort(command, sortCapacity);
            command.DispatchComputeProfiled(_publish, _publishKernels[4],
                footprintGroups, 1, 1);
            RecordPublicationScan(command, footprintCount, scanBlocks,
                scanSupers, 0);
            command.DispatchComputeProfiled(_publish, _publishKernels[8],
                footprintGroups, 1, 1);
            command.DispatchComputeProfiled(_publish, _publishKernels[9],
                footprintGroups, 1, 1);

            for (int segmentIndex = 0;
                segmentIndex < input.CarrierSegments.Count; ++segmentIndex)
            {
                SigmaCarrierReadBatch segment =
                    input.CarrierSegments[segmentIndex];
                BindPublicationSegment(command, _publishKernels[10], segment);
                command.DispatchComputeProfiled(_publish, _publishKernels[10],
                    Resources.PublicationDispatchArguments, 0u);
            }
            command.DispatchComputeProfiled(_publish, _publishKernels[11],
                footprintGroups, 1, 1);
            RecordPublicationScan(command, footprintCount, scanBlocks,
                scanSupers, 1);

            for (int segmentIndex = 0;
                segmentIndex < input.CarrierSegments.Count; ++segmentIndex)
            {
                SigmaCarrierReadBatch segment =
                    input.CarrierSegments[segmentIndex];
                BindPublicationSegment(command, _publishKernels[12], segment);
                command.DispatchComputeProfiled(_publish, _publishKernels[12],
                    1, 1, 1);
            }
            command.DispatchComputeProfiled(_publish, _publishKernels[13],
                1, 1, 1);
            command.DispatchComputeProfiled(_publish, _publishKernels[14],
                1, 1, 1);

            for (int segmentIndex = 0;
                segmentIndex < input.CarrierSegments.Count; ++segmentIndex)
            {
                SigmaCarrierReadBatch segment =
                    input.CarrierSegments[segmentIndex];
                BindPublicationSegment(command, _publishKernels[15], segment);
                command.DispatchComputeProfiled(_publish, _publishKernels[15],
                    footprintGroups, 1, 1);
            }
            command.DispatchComputeProfiled(_publish, _publishKernels[16],
                footprintGroups, 1, 1);

            for (int segmentIndex = 0;
                segmentIndex < input.CarrierSegments.Count; ++segmentIndex)
            {
                SigmaCarrierReadBatch segment =
                    input.CarrierSegments[segmentIndex];
                BindPublicationSegment(command, _publishKernels[17], segment);
                command.DispatchComputeProfiled(_publish, _publishKernels[17],
                    CeilDiv(segment.PageCapacity, 256), 1, 1);
                BindPublicationSegment(command, _publishKernels[18], segment);
                command.DispatchComputeProfiled(_publish, _publishKernels[18],
                    footprintGroups, 1, 1);
                BindPublicationSegment(command, _publishKernels[19], segment);
                command.DispatchComputeProfiled(_publish, _publishKernels[19],
                    SigmaCarrier.SamplesPerPage / 64,
                    segment.PageCapacity, 1);
                for (int targetWindowIndex = 0;
                    targetWindowIndex < Resources.ExecutionWindowCount;
                    ++targetWindowIndex)
                {
                    SigmaFrameExecutionWindow targetWindow =
                        Resources.ExecutionWindow(targetWindowIndex);
                    BindPublicationSegment(command, _publishKernels[20], segment);
                    BindPublicationTargetWindow(command, _publishKernels[20],
                        targetWindow);
                    command.DispatchComputeProfiled(_publish,
                        _publishKernels[20], CeilDiv(footprintCount, 16), 1, 1);
                }
            }
            command.DispatchComputeProfiled(_publish, _publishKernels[21],
                1, 1, 1);
            command.DispatchComputeProfiled(_publish, _publishKernels[22],
                1, 1, 1);
        }

        private void RecordMappedTargetSort(CommandBuffer command,
            int sortCapacity)
        {
            int kernel = _reductionKernels[2];
            command.SetComputeIntParam(_closure, "_TargetSortCapacity",
                sortCapacity);
            command.SetComputeBufferParam(_closure, kernel, "_FrameDeltas",
                FirstSegment(Resources.Deltas, sortCapacity,
                    "_FrameDeltas"));
            for (int width = 2; ; width <<= 1)
            {
                command.SetComputeIntParam(_closure, "_SortK", width);
                for (int stride = width >> 1; stride != 0; stride >>= 1)
                {
                    command.SetComputeIntParam(_closure, "_SortJ", stride);
                    command.DispatchComputeProfiled(_closure, kernel,
                        CeilDiv(sortCapacity, 256), 1, 1);
                }
                if (width == sortCapacity)
                    break;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            Resources?.Dispose();
            _rgbViewOperators?.Dispose();
            _rgbViewSupportScale?.Dispose();
            _nullCarrierState?.Dispose();
            _nullPageMetadata?.Dispose();
            _nullPublicationRoot?.Dispose();
        }

        private void BindExactClosureBase(CommandBuffer command, int kernel,
            SigmaOwnedFrameLease ownedFrame, uint revision,
            SigmaFrameInverseInput input)
        {
            command.SetComputeIntParams(_closure, "_FrameResolution",
                Resources.Resolution.x, Resources.Resolution.y);
            command.SetComputeIntParam(_closure, "_FrameRevision",
                unchecked((int)revision));
            command.SetComputeIntParam(_closure, "_FrameSlot", ownedFrame.Slot);
            command.SetComputeIntParam(_closure, "_FrameFootprintCount",
                Resources.FootprintCount);
            command.SetComputeIntParam(_closure, "_TargetSortCapacity",
                Resources.TargetSortCapacity);
            command.SetComputeIntParam(_closure, "_SingularShift",
                SigmaIntrinsicTopology.DefaultSingularShift);
            command.SetComputeIntParam(_closure, "_AssociatorShift",
                SigmaIntrinsicTopology.DefaultAssociatorShift);
            command.SetComputeIntParams(_closure, "_FrameSourceKeys",
                unchecked((int)input.DepthLeftKey),
                unchecked((int)input.DepthRightKey),
                unchecked((int)input.RgbLeftKey),
                unchecked((int)input.RgbRightKey));
            command.SetComputeBufferParam(_closure, kernel,
                "_SigmaExactBackendGate", _backendGate.Buffer);
            command.SetComputeBufferParam(_closure, kernel,
                "_FrameTargetScratch", FirstSegment(Resources.TargetScratch,
                    checked(Resources.TargetSortCapacity +
                        Resources.FootprintCount), "_FrameTargetScratch"));
            command.SetComputeBufferParam(_closure, kernel,
                "_FrameResolvedIndices", FirstSegment(Resources.ResolvedIndices,
                    Resources.FootprintCount, "_FrameResolvedIndices"));
            command.SetComputeBufferParam(_closure, kernel, "_PendingLabels",
                Single(Resources.PendingLabels, Resources.FootprintCount));
            command.SetComputeBufferParam(_closure, kernel, "_PendingLinks",
                Single(Resources.PendingLinks, Resources.FootprintCount));
            command.SetComputeBufferParam(_closure, kernel,
                "_PendingLabelsRead", Single(Resources.PendingLabels,
                    Resources.FootprintCount));
            command.SetComputeBufferParam(_closure, kernel,
                "_PendingLinksRead", Single(Resources.PendingLinks,
                    Resources.FootprintCount));
            command.SetComputeBufferParam(_closure, kernel,
                "_FrameDeferredFlags", Single(Resources.DeferredFlags,
                    Resources.FootprintCount));
            command.SetComputeBufferParam(_closure, kernel,
                "_RootLocalOffsets", FirstSegment(Resources.RootLocalOffsets,
                    Resources.FootprintCount, "_RootLocalOffsets"));
            command.SetComputeBufferParam(_closure, kernel,
                "_RootBlockOffsets", FirstSegment(Resources.RootBlockOffsets,
                    CeilDiv(Resources.FootprintCount, 256),
                    "_RootBlockOffsets"));
            command.SetComputeBufferParam(_closure, kernel,
                "_RootSuperOffsets", FirstSegment(Resources.RootSuperOffsets,
                    CeilDiv(CeilDiv(Resources.FootprintCount, 256), 256),
                    "_RootSuperOffsets"));
            command.SetComputeBufferParam(_closure, kernel,
                "_PendingControl", Single(Resources.PendingControl, 1));
            command.SetComputeBufferParam(_closure, kernel,
                "_PendingControlRead", Single(Resources.PendingControl, 1));
            command.SetComputeIntParam(_closure, "_PendingCapacity",
                Resources.FootprintCount);
            command.SetComputeBufferParam(_closure, kernel,
                "_FrameClosureCounters", Single(Resources.ClosureCounters,
                    SigmaFrameResources.ClosureCounterRecords));
            command.SetComputeBufferParam(_closure, kernel,
                "_FrameClosureCountersRead", Single(Resources.ClosureCounters,
                    SigmaFrameResources.ClosureCounterRecords));
        }

        private void BindExactEdgeWindow(CommandBuffer command, int kernel,
            SigmaFrameExecutionWindow window)
        {
            command.SetComputeIntParam(_closure, "_EdgeFirstFootprint",
                window.FirstFootprint);
            command.SetComputeIntParam(_closure, "_EdgeFootprintCount",
                window.FootprintCount);
            command.SetComputeBufferParam(_closure, kernel,
                "_FrameDirtyEdges", Window(Resources.DirtyEdges, window, 2));
            command.SetComputeBufferParam(_closure, kernel,
                "_FrameEdgeLower", Window(Resources.CandidateLower,
                    window, ProposalsPerFootprint *
                        SigmaGeneratedFrame.LaneCount));
            command.SetComputeBufferParam(_closure, kernel,
                "_FrameEdgeUpper", Window(Resources.CandidateUpper,
                    window, ProposalsPerFootprint *
                        SigmaGeneratedFrame.LaneCount));
            command.SetComputeBufferParam(_closure, kernel,
                "_FrameEdgeStates", Window(Resources.CandidateStates,
                    window, ProposalsPerFootprint *
                        SigmaGeneratedFrame.LaneCount));
            command.SetComputeBufferParam(_closure, kernel,
                "_FrameEdgeValidity", Window(Resources.CandidateValidity,
                    window, ProposalsPerFootprint *
                        SigmaGeneratedFrame.LaneCount));
        }

        private void BindPublicationBase(CommandBuffer command, int kernel,
            SigmaOwnedFrameLease ownedFrame, uint revision,
            SigmaFrameInverseInput input, int revisionCapacity)
        {
            int footprintCount = Resources.FootprintCount;
            int sortCapacity = Resources.TargetSortCapacity;
            command.SetComputeIntParams(_publish, "_FrameResolution",
                Resources.Resolution.x, Resources.Resolution.y);
            command.SetComputeIntParams(_publish, "_FrameSourceKeys",
                unchecked((int)input.DepthLeftKey),
                unchecked((int)input.DepthRightKey),
                unchecked((int)input.RgbLeftKey),
                unchecked((int)input.RgbRightKey));
            command.SetComputeIntParam(_publish, "_FrameRevision",
                unchecked((int)revision));
            command.SetComputeIntParam(_publish, "_FrameSlot", ownedFrame.Slot);
            command.SetComputeIntParam(_publish, "_FrameFootprintCount",
                footprintCount);
            command.SetComputeIntParam(_publish, "_CalibrationEpoch",
                unchecked((int)input.Prediction.Source.CalibrationEpoch));
            command.SetComputeIntParam(_publish, "_EvidenceJournal",
                unchecked((int)revision));
            command.SetComputeIntParam(_publish, "_TargetSortCapacity",
                sortCapacity);
            command.SetComputeIntParam(_publish, "_PublicationSegmentCount",
                input.CarrierSegments.Count);
            command.SetComputeIntParam(_publish, "_RevisionCapacity",
                revisionCapacity);
            command.SetComputeBufferParam(_publish, kernel,
                "_SigmaExactBackendGate", _backendGate.Buffer);
            command.SetComputeBufferParam(_publish, kernel, "_OwnedFrames",
                Resources.OwnedFrames);
            command.SetComputeBufferParam(_publish, kernel, "_FrameDeltas",
                FirstSegment(Resources.Deltas, sortCapacity, "_FrameDeltas"));
            command.SetComputeBufferParam(_publish, kernel,
                "_FrameTargetScratch", FirstSegment(Resources.TargetScratch,
                    checked(sortCapacity + footprintCount),
                    "_FrameTargetScratch"));
            command.SetComputeBufferParam(_publish, kernel,
                "_FrameTargetScratchRead", FirstSegment(
                    Resources.TargetScratch,
                    checked(sortCapacity + footprintCount),
                    "_FrameTargetScratchRead"));
            command.SetComputeBufferParam(_publish, kernel,
                "_FrameResolvedIndices", FirstSegment(Resources.ResolvedIndices,
                    footprintCount, "_FrameResolvedIndices"));
            command.SetComputeBufferParam(_publish, kernel,
                "_FrameDeferredFlags", FirstSegment(Resources.DeferredFlags,
                    footprintCount, "_FrameDeferredFlags"));
            command.SetComputeBufferParam(_publish, kernel, "_PendingLabels",
                FirstSegment(Resources.PendingLabels, footprintCount,
                    "_PendingLabels"));
            command.SetComputeBufferParam(_publish, kernel, "_PendingLinks",
                FirstSegment(Resources.PendingLinks, footprintCount,
                    "_PendingLinks"));
            command.SetComputeBufferParam(_publish, kernel,
                "_RootLocalOffsets", FirstSegment(Resources.RootLocalOffsets,
                    footprintCount, "_RootLocalOffsets"));
            command.SetComputeBufferParam(_publish, kernel,
                "_RootBlockOffsets", FirstSegment(Resources.RootBlockOffsets,
                    CeilDiv(footprintCount, 256), "_RootBlockOffsets"));
            command.SetComputeBufferParam(_publish, kernel,
                "_RootSuperOffsets", FirstSegment(Resources.RootSuperOffsets,
                    CeilDiv(CeilDiv(footprintCount, 256), 256),
                    "_RootSuperOffsets"));
            command.SetComputeBufferParam(_publish, kernel,
                "_FrameClosureCounters", FirstSegment(
                    Resources.ClosureCounters,
                    SigmaFrameResources.ClosureCounterRecords,
                    "_FrameClosureCounters"));
            command.SetComputeBufferParam(_publish, kernel,
                "_FrameClosureCountersRead", FirstSegment(
                    Resources.ClosureCounters,
                    SigmaFrameResources.ClosureCounterRecords,
                    "_FrameClosureCountersRead"));
            command.SetComputeBufferParam(_publish, kernel, "_ExtentAllocator",
                FirstSegment(Resources.ExtentAllocator, 1, "_ExtentAllocator"));
            command.SetComputeBufferParam(_publish, kernel,
                "_PublicationSegments", FirstSegment(
                    Resources.PublicationSegments,
                    input.CarrierSegments.Count, "_PublicationSegments"));
            command.SetComputeBufferParam(_publish, kernel,
                "_FramePageMarks", FirstSegment(Resources.PageMarks,
                    SigmaCarrier.MaximumPagesPerSegment,
                    "_FramePageMarks"));
            command.SetComputeBufferParam(_publish, kernel, "_FrameRevisions",
                FirstSegment(Resources.Revisions, revisionCapacity,
                    "_FrameRevisions"));
            command.SetComputeBufferParam(_publish, kernel,
                "_FrameRevisionRoot",
                input.CarrierSegments[0].PublicationRoot);
            command.SetComputeBufferParam(_publish, kernel,
                "_PublicationDispatchArguments",
                Resources.PublicationDispatchArguments);
        }

        private void BindPublicationTargetWindow(CommandBuffer command,
            int kernel, SigmaFrameExecutionWindow window)
        {
            command.SetComputeIntParam(_publish, "_TargetWindowFirst",
                window.FirstFootprint);
            command.SetComputeIntParam(_publish, "_TargetWindowCount",
                window.FootprintCount);
            command.SetComputeBufferParam(_publish, kernel,
                "_ReducedTargetStatesRead", Window(Resources.ReducedStates,
                    window, SigmaGeneratedFrame.LaneCount));
        }

        private void BindPublicationSegment(CommandBuffer command, int kernel,
            SigmaCarrierReadBatch segment)
        {
            command.SetComputeIntParam(_publish, "_TargetSegmentIndex",
                segment.SegmentIndex);
            command.SetComputeIntParam(_publish, "_TargetPairFirst",
                segment.PairFirst);
            command.SetComputeIntParam(_publish, "_TargetPairCount",
                segment.PairCount);
            command.SetComputeIntParam(_publish, "_TargetPageCapacity",
                segment.PageCapacity);
            command.SetComputeBufferParam(_publish, kernel,
                "_TargetCarrierState", segment.State);
            command.SetComputeBufferParam(_publish, kernel,
                "_TargetPageMetadata", segment.Metadata);
            command.SetComputeBufferParam(_publish, kernel,
                "_TargetPageMetadataRead", segment.Metadata);
            command.SetComputeBufferParam(_publish, kernel,
                "_TargetDirtyFlags", segment.DirtyFlags);
            command.SetComputeBufferParam(_publish, kernel,
                "_TargetReadoutDirtyFlags", segment.ReadoutDirtyFlags);
        }

        private void RecordPublicationScan(CommandBuffer command, int count,
            int blocks, int supers, int mode)
        {
            command.SetComputeIntParam(_publish, "_ScanElementCount", count);
            command.SetComputeIntParam(_publish, "_ScanBlockCount", blocks);
            command.SetComputeIntParam(_publish, "_ScanSuperCount", supers);
            command.SetComputeIntParam(_publish, "_ScanMode", mode);
            command.DispatchComputeProfiled(_publish, _publishKernels[5],
                blocks, 1, 1);
            command.DispatchComputeProfiled(_publish, _publishKernels[6],
                supers, 1, 1);
            command.DispatchComputeProfiled(_publish, _publishKernels[7],
                1, 1, 1);
        }

        private static bool ValidatePublicationSegments(
            IReadOnlyList<SigmaCarrierReadBatch> segments,
            out int totalPhysicalPages, out int totalPairCapacity)
        {
            totalPhysicalPages = 0;
            totalPairCapacity = 0;
            if (segments == null || segments.Count == 0 || segments.Count > 256)
                return false;
            for (int index = 0; index < segments.Count; ++index)
            {
                SigmaCarrierReadBatch segment = segments[index];
                if (segment.SegmentIndex != index ||
                    segment.PairFirst != totalPairCapacity ||
                    segment.PageCapacity <= 0 ||
                    (segment.PageCapacity & 1) != 0 ||
                    segment.PageCapacity > SigmaCarrier.MaximumPagesPerSegment ||
                    !segment.HasPublicationStorage || segment.State == null ||
                    segment.Metadata == null || segment.DirtyFlags == null ||
                    segment.ReadoutDirtyFlags == null ||
                    segment.PublicationRoot == null ||
                    (index != 0 && segment.PublicationRoot !=
                        segments[0].PublicationRoot))
                    return false;
                totalPhysicalPages = checked(totalPhysicalPages +
                    segment.PageCapacity);
                totalPairCapacity = checked(totalPairCapacity +
                    segment.PairCount);
            }
            return totalPairCapacity != 0;
        }

        private void BindTargetReductionBase(CommandBuffer command, int kernel,
            SigmaFrameInverseInput input, int sortCapacity, int headBlocks,
            int headSupers)
        {
            command.SetComputeIntParam(_closure, "_FrameFootprintCount",
                Resources.FootprintCount);
            command.SetComputeIntParam(_closure, "_TargetSortCapacity",
                sortCapacity);
            command.SetComputeIntParam(_closure, "_TargetHeadBlockCount",
                headBlocks);
            command.SetComputeIntParam(_closure, "_TargetHeadSuperCount",
                headSupers);
            command.SetComputeIntParams(_closure, "_FrameSourceKeys",
                unchecked((int)input.DepthLeftKey),
                unchecked((int)input.DepthRightKey),
                unchecked((int)input.RgbLeftKey),
                unchecked((int)input.RgbRightKey));
            command.SetComputeBufferParam(_closure, kernel,
                "_SigmaExactBackendGate", _backendGate.Buffer);
            command.SetComputeBufferParam(_closure, kernel,
                "_DepthCalibrationQ48", input.DepthCalibration);
            command.SetComputeBufferParam(_closure, kernel, "_FrameDeltas",
                FirstSegment(Resources.Deltas, sortCapacity,
                    "_FrameDeltas"));
            command.SetComputeBufferParam(_closure, kernel,
                "_FrameTargetScratch",
                FirstSegment(Resources.TargetScratch,
                    checked(sortCapacity + Resources.FootprintCount),
                    "_FrameTargetScratch"));
            command.SetComputeBufferParam(_closure, kernel,
                "_FrameResolvedIndices",
                FirstSegment(Resources.ResolvedIndices,
                    Resources.FootprintCount, "_FrameResolvedIndices"));
            command.SetComputeBufferParam(_closure, kernel,
                "_RootLocalOffsets", FirstSegment(Resources.RootLocalOffsets,
                    Resources.FootprintCount, "_RootLocalOffsets"));
            command.SetComputeBufferParam(_closure, kernel,
                "_RootBlockOffsets", FirstSegment(Resources.RootBlockOffsets,
                    headBlocks, "_RootBlockOffsets"));
            command.SetComputeBufferParam(_closure, kernel,
                "_RootSuperOffsets", FirstSegment(Resources.RootSuperOffsets,
                    headSupers, "_RootSuperOffsets"));
            command.SetComputeBufferParam(_closure, kernel,
                "_FrameClosureCounters",
                FirstSegment(Resources.ClosureCounters,
                    SigmaFrameResources.ClosureCounterRecords,
                    "_FrameClosureCounters"));
            command.SetComputeBufferParam(_closure, kernel,
                "_PendingControl", Single(Resources.PendingControl, 1));
            command.SetComputeBufferParam(_closure, kernel,
                "_PendingControlRead", Single(Resources.PendingControl, 1));
        }

        private void BindReductionSourceWindow(CommandBuffer command, int kernel,
            SigmaFrameExecutionWindow window)
        {
            command.SetComputeIntParam(_closure, "_CandidateWindowFirst",
                window.FirstFootprint);
            command.SetComputeIntParam(_closure, "_CandidateWindowCount",
                window.FootprintCount);
            command.SetComputeBufferParam(_closure, kernel,
                "_FrameCandidateLower", Window(Resources.CandidateLower, window,
                    ProposalsPerFootprint * SigmaGeneratedFrame.LaneCount));
            command.SetComputeBufferParam(_closure, kernel,
                "_FrameCandidateUpper", Window(Resources.CandidateUpper, window,
                    ProposalsPerFootprint * SigmaGeneratedFrame.LaneCount));
            command.SetComputeBufferParam(_closure, kernel,
                "_FrameCandidateValidity", Window(Resources.CandidateValidity,
                    window, ProposalsPerFootprint *
                        SigmaGeneratedFrame.LaneCount));
            command.SetComputeBufferParam(_closure, kernel,
                "_FrameCandidateStates", Window(Resources.CandidateStates, window,
                    ProposalsPerFootprint * SigmaGeneratedFrame.LaneCount));
        }

        private void BindTargetWindow(CommandBuffer command, int kernel,
            SigmaFrameExecutionWindow window)
        {
            command.SetComputeIntParam(_closure, "_TargetWindowFirst",
                window.FirstFootprint);
            command.SetComputeIntParam(_closure, "_TargetWindowCount",
                window.FootprintCount);
            command.SetComputeBufferParam(_closure, kernel,
                "_ReducedTargetLower", Window(Resources.ReducedLower, window,
                    SigmaGeneratedFrame.LaneCount));
            command.SetComputeBufferParam(_closure, kernel,
                "_ReducedTargetUpper", Window(Resources.ReducedUpper, window,
                    SigmaGeneratedFrame.LaneCount));
            command.SetComputeBufferParam(_closure, kernel,
                "_ReducedTargetGap", Window(Resources.ReducedGap, window,
                    SigmaGeneratedFrame.LaneCount));
            command.SetComputeBufferParam(_closure, kernel,
                "_ReducedTargetValidity", Window(Resources.ReducedValidity,
                    window, SigmaGeneratedFrame.LaneCount));
            command.SetComputeBufferParam(_closure, kernel,
                "_ReducedTargetStates", Window(Resources.ReducedStates, window,
                    SigmaGeneratedFrame.LaneCount));
        }

        private void BindExactTargetWindow(CommandBuffer command, int kernel,
            SigmaFrameExecutionWindow window)
        {
            command.SetComputeIntParam(_closure, "_TargetWindowFirst",
                window.FirstFootprint);
            command.SetComputeIntParam(_closure, "_TargetWindowCount",
                window.FootprintCount);
            command.SetComputeBufferParam(_closure, kernel,
                "_ReducedTargetLowerRead", Window(Resources.ReducedLower, window,
                    SigmaGeneratedFrame.LaneCount));
            command.SetComputeBufferParam(_closure, kernel,
                "_ReducedTargetUpperRead", Window(Resources.ReducedUpper, window,
                    SigmaGeneratedFrame.LaneCount));
            command.SetComputeBufferParam(_closure, kernel,
                "_ReducedTargetValidityRead", Window(Resources.ReducedValidity,
                    window, SigmaGeneratedFrame.LaneCount));
            command.SetComputeBufferParam(_closure, kernel,
                "_ReducedTargetStatesRead", Window(Resources.ReducedStates,
                    window, SigmaGeneratedFrame.LaneCount));
        }

        private void BindPendingClosureReadWindow(CommandBuffer command,
            int kernel, SigmaFrameExecutionWindow window)
        {
            command.SetComputeIntParam(_closure, "_PendingFirst",
                window.FirstFootprint);
            command.SetComputeIntParam(_closure, "_PendingCount",
                window.FootprintCount);
            command.SetComputeBufferParam(_closure, kernel,
                "_PendingGaugesRead", Window(Resources.PendingGauges, window,
                    1));
            command.SetComputeBufferParam(_closure, kernel,
                "_PendingStatesRead", Window(Resources.PendingStates, window,
                    SigmaGeneratedFrame.LaneCount));
        }

        private void BindPendingClosureWriteWindow(CommandBuffer command,
            int kernel, SigmaFrameExecutionWindow window)
        {
            BindPendingClosureReadWindow(command, kernel, window);
            command.SetComputeBufferParam(_closure, kernel, "_PendingGauges",
                Window(Resources.PendingGauges, window, 1));
            command.SetComputeBufferParam(_closure, kernel, "_PendingStates",
                Window(Resources.PendingStates, window,
                    SigmaGeneratedFrame.LaneCount));
            command.SetComputeBufferParam(_closure, kernel, "_PendingLower",
                Window(Resources.PendingLower, window,
                    SigmaGeneratedFrame.LaneCount));
            command.SetComputeBufferParam(_closure, kernel, "_PendingUpper",
                Window(Resources.PendingUpper, window,
                    SigmaGeneratedFrame.LaneCount));
            command.SetComputeBufferParam(_closure, kernel, "_PendingValidity",
                Window(Resources.PendingValidity, window,
                    SigmaGeneratedFrame.LaneCount));
        }

        private void BindReductionCarrier(CommandBuffer command, int kernel,
            SigmaCarrierReadBatch batch) => BindReductionCarrier(command, kernel,
            batch.State, batch.Metadata, batch.PublicationRoot,
            batch.SegmentIndex, batch.PageCapacity);

        private void BindReductionCarrier(CommandBuffer command, int kernel,
            GraphicsBuffer state, GraphicsBuffer metadata,
            GraphicsBuffer publicationRoot, int segmentIndex, int pageCapacity)
        {
            command.SetComputeBufferParam(_closure, kernel, "_CarrierState",
                state);
            command.SetComputeBufferParam(_closure, kernel, "_PageMetadata",
                metadata);
            command.SetComputeBufferParam(_closure, kernel,
                "_PublishedRevisionRoot", publicationRoot);
            command.SetComputeIntParam(_closure, "_CarrierSegmentIndex",
                segmentIndex);
            command.SetComputeIntParam(_closure, "_PageCapacity", pageCapacity);
        }

        private void BindFrameConstants(CommandBuffer command,
            StereoRigFrameLease source, uint revision, int frameSlot,
            SigmaFrameInverseInput input, int candidateCount,
            int resolvedBlockCount)
        {
            command.SetComputeIntParams(_inverse, "_FrameResolution",
                Resources.Resolution.x, Resources.Resolution.y);
            command.SetComputeIntParams(_inverse, "_FrameSourceKeys",
                unchecked((int)input.DepthLeftKey),
                unchecked((int)input.DepthRightKey),
                unchecked((int)input.RgbLeftKey),
                unchecked((int)input.RgbRightKey));
            command.SetComputeIntParam(_inverse, "_FrameRevision",
                unchecked((int)revision));
            command.SetComputeIntParam(_inverse, "_FrameSlot", frameSlot);
            command.SetComputeIntParam(_inverse, "_FrameFootprintCount",
                Resources.FootprintCount);
            command.SetComputeIntParam(_inverse, "_FrameCandidateCount",
                candidateCount);
            command.SetComputeIntParam(_inverse, "_FrameResolvedBlockCount",
                resolvedBlockCount);
            command.SetComputeIntParam(_inverse, "_FirstFootprint", 0);
            command.SetComputeIntParam(_inverse, "_BoundFootprintCount",
                Resources.FootprintCount);
            command.SetComputeIntParam(_inverse, "_SourceFootprintBase", 0);
            command.SetComputeIntParams(_inverse, "_RgbResolutionLeft",
                source.RgbLeft.Resolution.x, source.RgbLeft.Resolution.y);
            command.SetComputeIntParams(_inverse, "_RgbResolutionRight",
                source.RgbRight.Resolution.x, source.RgbRight.Resolution.y);
            command.SetComputeIntParam(_inverse, "_RgbLeftIndependenceKey",
                unchecked((int)input.RgbLeftKey));
            command.SetComputeIntParam(_inverse, "_RgbRightIndependenceKey",
                unchecked((int)input.RgbRightKey));
            SetFrameMatrices(command, source, input.Prediction.WorldToRoom);
        }

        private void BindProposal(CommandBuffer command,
            StereoRigFrameLease source, SigmaFrameInverseInput input,
            GraphicsBuffer candidates, SigmaFrameExecutionWindow window)
        {
            command.SetComputeBufferParam(_inverse, _proposalKernel,
                "_FrameCandidates", candidates);
            command.SetComputeBufferParam(_inverse, _proposalKernel,
                "_PendingControl", Single(Resources.PendingControl, 1));
            command.SetComputeBufferParam(_inverse, _proposalKernel,
                "_PendingProjectionHandles", Window(
                    Resources.PendingProjectionHandles, window, 2));
            command.SetComputeBufferParam(_inverse, _proposalKernel,
                "_PoseResult", input.PoseResult);
            command.SetComputeTextureParam(_inverse, _proposalKernel,
                "_MetricDepth", input.MetricDepth);
            command.SetComputeTextureParam(_inverse, _proposalKernel,
                "_DepthFlags", input.DepthFlags);
            command.SetComputeTextureParam(_inverse, _proposalKernel,
                "_PredDepthSupport", input.Prediction.DepthSupport);
            command.SetComputeTextureParam(_inverse, _proposalKernel,
                "_PredCarrierPage", input.Prediction.CarrierPage);
            command.SetComputeTextureParam(_inverse, _proposalKernel,
                "_PredCarrierUvNormal", input.Prediction.CarrierUvNormal);
            command.SetComputeTextureParam(_inverse, _proposalKernel,
                "_PredStateKey", input.Prediction.StateKey);
            command.SetComputeTextureParam(_inverse, _proposalKernel,
                "_DepthRayCenterLeft",
                input.ConeLuts.DepthLeft.CenterRaySolidAngle);
            command.SetComputeTextureParam(_inverse, _proposalKernel,
                "_DepthRayCenterRight",
                input.ConeLuts.DepthRight.CenterRaySolidAngle);
        }

        private void RecordDepthSource(CommandBuffer command,
            SigmaFrameInverseInput input, SigmaFrameSourceStorage sources,
            SigmaFrameSource source, SigmaFrameExecutionWindow window,
            int footprintWindow)
        {
            BindSourceOutputs(command, _depthKernel, sources, source, window);
            command.SetComputeIntParam(_inverse, "_FrameSourceOrdinal",
                (int)source);
            command.SetComputeBufferParam(_inverse, _depthKernel,
                "_DepthCalibrationQ48", input.DepthCalibration);
            command.SetComputeBufferParam(_inverse, _depthKernel,
                "_PoseResult", input.PoseResult);
            command.SetComputeTextureParam(_inverse, _depthKernel,
                "_MetricDepth", input.MetricDepth);
            command.SetComputeTextureParam(_inverse, _depthKernel,
                "_DepthFlags", input.DepthFlags);
            command.SetComputeTextureParam(_inverse, _depthKernel,
                "_DepthRayCenterLeft",
                input.ConeLuts.DepthLeft.CenterRaySolidAngle);
            command.SetComputeTextureParam(_inverse, _depthKernel,
                "_DepthRayCenterRight",
                input.ConeLuts.DepthRight.CenterRaySolidAngle);
            command.SetComputeTextureParam(_inverse, _depthKernel,
                "_DepthSlopeBoundsLeft",
                input.ConeLuts.DepthLeft.SlopeBounds);
            command.SetComputeTextureParam(_inverse, _depthKernel,
                "_DepthSlopeBoundsRight",
                input.ConeLuts.DepthRight.SlopeBounds);
            RecordWindowChunks(command, _depthKernel, window, footprintWindow,
                FootprintsPerCoordinateGroup);
        }

        private void RecordRgbSource(CommandBuffer command,
            StereoRigFrameLease frame, SigmaFrameInverseInput input,
            SigmaFrameSourceStorage sources, SigmaFrameSource source,
            SigmaFrameExecutionWindow window, int footprintWindow)
        {
            BindSourceOutputs(command, _rgbKernel, sources, source, window);
            BindSourceReads(command, _rgbKernel, sources, window);
            command.SetComputeIntParam(_inverse, "_FrameSourceOrdinal",
                (int)source);
            command.SetComputeBufferParam(_inverse, _rgbKernel,
                "_SigmaExactBackendGate", _backendGate.Buffer);
            command.SetComputeBufferParam(_inverse, _rgbKernel,
                "_DepthCalibrationQ48", input.DepthCalibration);
            command.SetComputeBufferParam(_inverse, _rgbKernel,
                "_RgbCalibrationQ48", input.RgbCalibration);
            command.SetComputeBufferParam(_inverse, _rgbKernel,
                "_RgbViewOperators", _rgbViewOperators);
            command.SetComputeBufferParam(_inverse, _rgbKernel,
                "_RgbViewSupportScale", _rgbViewSupportScale);
            command.SetComputeBufferParam(_inverse, _rgbKernel,
                "_PoseResult", input.PoseResult);
            command.SetComputeTextureParam(_inverse, _rgbKernel, "_RgbLeft",
                frame.RgbLeft.Texture);
            command.SetComputeTextureParam(_inverse, _rgbKernel, "_RgbRight",
                frame.RgbRight.Texture);
            RecordWindowChunks(command, _rgbKernel, window, footprintWindow,
                FootprintsPerCoordinateGroup);
        }

        private void BindEvaluate(CommandBuffer command,
            SigmaFrameInverseInput input, SigmaFrameSourceStorage sources,
            SigmaFrameExecutionWindow window, GraphicsBuffer candidates,
            GraphicsBuffer outcomes,
            GraphicsBuffer candidateStates)
        {
            command.SetComputeBufferParam(_inverse, _evaluateKernel,
                "_SigmaExactBackendGate", _backendGate.Buffer);
            command.SetComputeBufferParam(_inverse, _evaluateKernel,
                "_DepthCalibrationQ48", input.DepthCalibration);
            command.SetComputeBufferParam(_inverse, _evaluateKernel,
                "_FrameCandidates", candidates);
            command.SetComputeBufferParam(_inverse, _evaluateKernel,
                "_FrameOutcomes", outcomes);
            command.SetComputeBufferParam(_inverse, _evaluateKernel,
                "_FrameCandidateStates", candidateStates);
            command.SetComputeBufferParam(_inverse, _evaluateKernel,
                "_FrameCandidateLower", Window(Resources.CandidateLower, window,
                    ProposalsPerFootprint * SigmaGeneratedFrame.LaneCount));
            command.SetComputeBufferParam(_inverse, _evaluateKernel,
                "_FrameCandidateUpper", Window(Resources.CandidateUpper, window,
                    ProposalsPerFootprint * SigmaGeneratedFrame.LaneCount));
            command.SetComputeBufferParam(_inverse, _evaluateKernel,
                "_FrameCandidateValidity", Window(Resources.CandidateValidity,
                    window, ProposalsPerFootprint *
                        SigmaGeneratedFrame.LaneCount));
            command.SetComputeBufferParam(_inverse, _evaluateKernel,
                "_PendingControl", Single(Resources.PendingControl, 1));
            BindPendingInverseWindow(command, _evaluateKernel,
                Resources.ExecutionWindow(0));
            BindSourceReads(command, _evaluateKernel, sources, window);
        }

        private void BindPendingProjectionOutput(CommandBuffer command,
            int kernel, SigmaFrameExecutionWindow window)
        {
            command.SetComputeIntParam(_inverse, "_ProjectionFirstFootprint",
                window.FirstFootprint);
            command.SetComputeIntParam(_inverse, "_ProjectionFootprintCount",
                window.FootprintCount);
            command.SetComputeBufferParam(_inverse, kernel,
                "_PendingProjectionDepth", Window(
                    Resources.PendingProjectionDepth, window, 2));
            command.SetComputeBufferParam(_inverse, kernel,
                "_PendingProjectionHandles", Window(
                    Resources.PendingProjectionHandles, window, 2));
        }

        private void BindPendingProjectionInput(CommandBuffer command,
            int kernel, SigmaFrameExecutionWindow pendingWindow,
            SigmaFrameExecutionWindow projectionWindow,
            GraphicsBuffer poseResult)
        {
            BindPendingProjectionOutput(command, kernel, projectionWindow);
            command.SetComputeIntParam(_inverse, "_PendingFirst",
                pendingWindow.FirstFootprint);
            command.SetComputeIntParam(_inverse, "_PendingCount",
                pendingWindow.FootprintCount);
            command.SetComputeBufferParam(_inverse, kernel, "_PendingGauges",
                Window(Resources.PendingGauges, pendingWindow, 1));
            command.SetComputeBufferParam(_inverse, kernel, "_PendingStates",
                Window(Resources.PendingStates, pendingWindow,
                    SigmaGeneratedFrame.LaneCount));
            command.SetComputeBufferParam(_inverse, kernel, "_PendingControl",
                Single(Resources.PendingControl, 1));
            command.SetComputeBufferParam(_inverse, kernel, "_PoseResult",
                poseResult);
        }

        private void BindPendingInverseWindow(CommandBuffer command, int kernel,
            SigmaFrameExecutionWindow window)
        {
            command.SetComputeIntParam(_inverse, "_PendingFirst",
                window.FirstFootprint);
            command.SetComputeIntParam(_inverse, "_PendingCount",
                window.FootprintCount);
            command.SetComputeBufferParam(_inverse, kernel, "_PendingGauges",
                Window(Resources.PendingGauges, window, 1));
            command.SetComputeBufferParam(_inverse, kernel, "_PendingStates",
                Window(Resources.PendingStates, window,
                    SigmaGeneratedFrame.LaneCount));
            command.SetComputeBufferParam(_inverse, kernel, "_PendingLower",
                Window(Resources.PendingLower, window,
                    SigmaGeneratedFrame.LaneCount));
            command.SetComputeBufferParam(_inverse, kernel, "_PendingUpper",
                Window(Resources.PendingUpper, window,
                    SigmaGeneratedFrame.LaneCount));
            command.SetComputeBufferParam(_inverse, kernel, "_PendingValidity",
                Window(Resources.PendingValidity, window,
                    SigmaGeneratedFrame.LaneCount));
        }

        private void BindCarrier(CommandBuffer command, GraphicsBuffer state,
            GraphicsBuffer metadata, GraphicsBuffer publicationRoot,
            int segmentIndex, int pageCapacity, uint proposalKind)
        {
            command.SetComputeBufferParam(_inverse, _evaluateKernel,
                "_CarrierState", state);
            command.SetComputeBufferParam(_inverse, _evaluateKernel,
                "_PageMetadata", metadata);
            command.SetComputeBufferParam(_inverse, _evaluateKernel,
                "_PublishedRevisionRoot", publicationRoot);
            command.SetComputeIntParam(_inverse, "_CarrierSegmentIndex",
                segmentIndex);
            command.SetComputeIntParam(_inverse, "_PageCapacity",
                pageCapacity);
            command.SetComputeIntParam(_inverse, "_ResolveProposalKind",
                unchecked((int)proposalKind));
        }

        private void BindSourceOutputs(CommandBuffer command, int kernel,
            SigmaFrameSourceStorage sources, SigmaFrameSource source,
            SigmaFrameExecutionWindow window)
        {
            command.SetComputeBufferParam(_inverse, kernel, "_SourceLowerOut",
                Window(sources.Lower(source), window,
                    SigmaGeneratedFrame.LaneCount));
            command.SetComputeBufferParam(_inverse, kernel, "_SourceUpperOut",
                Window(sources.Upper(source), window,
                    SigmaGeneratedFrame.LaneCount));
            command.SetComputeBufferParam(_inverse, kernel,
                "_SourceValidityOut", Window(sources.Validity(source), window,
                    SigmaGeneratedFrame.LaneCount));
            command.SetComputeBufferParam(_inverse, kernel,
                "_SourceProvenanceOut", Window(sources.Provenance(source), window,
                    1));
        }

        private void BindSourceReads(CommandBuffer command, int kernel,
            SigmaFrameSourceStorage sources, SigmaFrameExecutionWindow window)
        {
            BindSourceRead(command, kernel, sources,
                SigmaFrameSource.DepthLeft, "DepthLeft", window);
            BindSourceRead(command, kernel, sources,
                SigmaFrameSource.DepthRight, "DepthRight", window);
            BindSourceRead(command, kernel, sources,
                SigmaFrameSource.RgbLeft, "RgbLeft", window);
            BindSourceRead(command, kernel, sources,
                SigmaFrameSource.RgbRight, "RgbRight", window);
        }

        private void BindSourceRead(CommandBuffer command, int kernel,
            SigmaFrameSourceStorage sources, SigmaFrameSource source,
            string prefix, SigmaFrameExecutionWindow window)
        {
            command.SetComputeBufferParam(_inverse, kernel,
                $"_{prefix}Lower", Window(sources.Lower(source), window,
                    SigmaGeneratedFrame.LaneCount));
            command.SetComputeBufferParam(_inverse, kernel,
                $"_{prefix}Upper", Window(sources.Upper(source), window,
                    SigmaGeneratedFrame.LaneCount));
            command.SetComputeBufferParam(_inverse, kernel,
                $"_{prefix}Validity", Window(sources.Validity(source), window,
                    SigmaGeneratedFrame.LaneCount));
            command.SetComputeBufferParam(_inverse, kernel,
                $"_{prefix}Provenance", Window(sources.Provenance(source), window,
                    1));
        }

        private void SetFrameMatrices(CommandBuffer command,
            StereoRigFrameLease source, Matrix4x4 worldToRoom)
        {
            Matrix4x4 leftWorld = SigmaRoomFrame.FromCamera(worldToRoom,
                source.DepthLeft.WorldFromCamera);
            Matrix4x4 rightWorld = SigmaRoomFrame.FromCamera(worldToRoom,
                source.DepthRight.WorldFromCamera);
            command.SetComputeMatrixParam(_inverse, "_WorldFromOpticalLeft",
                leftWorld);
            command.SetComputeMatrixParam(_inverse, "_WorldFromOpticalRight",
                rightWorld);
            command.SetComputeMatrixParam(_inverse, "_OpticalFromWorldLeft",
                leftWorld.inverse);
            command.SetComputeMatrixParam(_inverse, "_OpticalFromWorldRight",
                rightWorld.inverse);
            command.SetComputeMatrixParam(_inverse,
                "_PoseConsumeReferenceFromWorld", leftWorld.inverse);
            command.SetComputeMatrixParam(_inverse,
                "_PoseConsumeWorldFromReference", leftWorld);
            command.SetComputeVectorParam(_inverse, "_DepthIntrinsicsLeft",
                Intrinsics(source.DepthLeft.Intrinsics));
            command.SetComputeVectorParam(_inverse, "_DepthIntrinsicsRight",
                Intrinsics(source.DepthRight.Intrinsics));
            command.SetComputeMatrixParam(_inverse, "_RgbOpticalFromWorldLeft",
                SigmaRoomFrame.FromCamera(worldToRoom,
                    source.RgbLeft.WorldFromCamera).inverse);
            command.SetComputeMatrixParam(_inverse,
                "_RgbOpticalFromWorldRight",
                SigmaRoomFrame.FromCamera(worldToRoom,
                    source.RgbRight.WorldFromCamera).inverse);
            command.SetComputeVectorParam(_inverse, "_RgbIntrinsicsLeft",
                Intrinsics(source.RgbLeft.Intrinsics));
            command.SetComputeVectorParam(_inverse, "_RgbIntrinsicsRight",
                Intrinsics(source.RgbRight.Intrinsics));
        }

        private static GraphicsBuffer Single(SigmaFrameSegmentedBuffer buffer,
            long requiredRecords)
        {
            if (buffer == null || buffer.RecordCapacity < requiredRecords ||
                buffer.Segments.Count != 1)
                throw new InvalidOperationException(
                    "Direct-frame execution window is incomplete or segmented.");
            return buffer.Segments[0].Buffer;
        }

        private static GraphicsBuffer FirstSegment(
            SigmaFrameSegmentedBuffer buffer, int requiredRecords,
            string propertyName)
        {
            if (buffer == null || requiredRecords <= 0 ||
                buffer.Segments.Count == 0 ||
                buffer.Segment(0).RecordCount < requiredRecords)
                throw new InvalidOperationException(
                    $"Direct target circuit binding {propertyName} has " +
                    $"{(buffer?.Segments.Count > 0 ? buffer.Segment(0).RecordCount : 0)} " +
                    $"records, requires {requiredRecords}.");
            return buffer.Segment(0).Buffer;
        }

        private static GraphicsBuffer CreateBuffer(int count, int stride,
            string name) => new(GraphicsBuffer.Target.Structured, count, stride)
            { name = name };

        private static int[] FindKernels(ComputeShader shader,
            params string[] names)
        {
            var kernels = new int[names.Length];
            for (int index = 0; index < names.Length; ++index)
                kernels[index] = shader.FindProfiledKernel(names[index]);
            return kernels;
        }

        private static Vector4 Intrinsics(RigIntrinsics intrinsics) => new(
            intrinsics.FocalLength.x, intrinsics.FocalLength.y,
            intrinsics.PrincipalPoint.x, intrinsics.PrincipalPoint.y);

        private static SigmaFrameUInt2Gpu Packed(long raw)
        {
            ulong bits = unchecked((ulong)raw);
            return new SigmaFrameUInt2Gpu
            {
                X = unchecked((uint)bits),
                Y = unchecked((uint)(bits >> 32)),
            };
        }

        private static int CeilDiv(int value, int divisor) => checked(
            (value + divisor - 1) / divisor);

        private static GraphicsBuffer Window(SigmaFrameSegmentedBuffer buffer,
            SigmaFrameExecutionWindow window, int recordsPerFootprint) =>
            SigmaFrameResources.WindowBuffer(buffer, window,
                recordsPerFootprint);

        private void RecordCompactWindow(CommandBuffer command,
            SigmaFrameExecutionWindow window, uint revision)
        {
            int resolvedBlockCount = CeilDiv(window.FootprintCount, 256);
            command.SetComputeIntParam(_inverse, "_FrameFootprintCount",
                Resources.FootprintCount);
            command.SetComputeIntParam(_inverse, "_FrameRevision",
                unchecked((int)revision));
            BindExecutionWindow(command, window);
            BindFootprintWindow(command, window.FirstFootprint,
                window.FootprintCount);
            command.SetComputeBufferParam(_inverse, _compactKernel,
                "_FrameCandidates", Window(Resources.Candidates, window,
                    ProposalsPerFootprint));
            command.SetComputeBufferParam(_inverse, _compactKernel,
                "_FrameOutcomes", Window(Resources.Outcomes, window,
                    ProposalsPerFootprint));
            command.SetComputeBufferParam(_inverse, _compactKernel,
                "_FrameDeltas", Resources.Deltas.Segment(0).Buffer);
            command.SetComputeBufferParam(_inverse, _compactKernel,
                "_FrameResolvedBlockCounts",
                Resources.ResolvedBlockCounts.Segment(0).Buffer);
            command.SetComputeBufferParam(_inverse, _compactKernel,
                "_FrameResolvedIndices",
                Resources.ResolvedIndices.Segment(0).Buffer);
            command.DispatchComputeProfiled(_inverse, _compactKernel,
                resolvedBlockCount, 1, 1);
        }

        private void BindExecutionWindow(CommandBuffer command,
            SigmaFrameExecutionWindow window)
        {
            command.SetComputeIntParam(_inverse, "_FrameBufferFootprintBase",
                window.FirstFootprint);
            command.SetComputeIntParam(_inverse, "_SourceFootprintBase",
                window.FirstFootprint);
        }

        private void RecordWindowChunks(CommandBuffer command, int kernel,
            SigmaFrameExecutionWindow window, int requestedWindow,
            int footprintsPerGroup)
        {
            int chunkSize = Math.Min(Math.Max(1, requestedWindow),
                window.FootprintCount);
            int end = checked(window.FirstFootprint + window.FootprintCount);
            for (int first = window.FirstFootprint; first < end;
                first += chunkSize)
            {
                int count = Math.Min(chunkSize, end - first);
                BindFootprintWindow(command, first, count);
                command.DispatchComputeProfiled(_inverse, kernel,
                    CeilDiv(count, footprintsPerGroup), 1, 1);
            }
        }

        private void BindFootprintWindow(CommandBuffer command, int first,
            int count)
        {
            command.SetComputeIntParam(_inverse, "_FirstFootprint", first);
            command.SetComputeIntParam(_inverse, "_BoundFootprintCount", count);
        }

        private void RequireAlive()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SigmaFrameGraph));
        }
    }
}
