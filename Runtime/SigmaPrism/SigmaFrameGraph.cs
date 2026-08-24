using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.SigmaPrism
{
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
    /// no transaction, tile, page-closure or token scheduler state. M2 stops at
    /// exact resolved records and never mutates the carrier.
    /// </summary>
    internal sealed class SigmaFrameGraph : IDisposable
    {
        private const string InverseResource = "SigmaPrism/SigmaFrameInverse";
        private const int ProposalsPerFootprint = 4;
        private const int FootprintsPerCoordinateGroup = 16;

        private readonly ComputeShader _inverse;
        private readonly SigmaExactBackendGate _backendGate;
        private readonly GraphicsBuffer _rgbViewOperators;
        private readonly GraphicsBuffer _rgbViewSupportScale;
        private readonly GraphicsBuffer _nullCarrierState;
        private readonly GraphicsBuffer _nullPageMetadata;
        private readonly GraphicsBuffer _nullCurrentFlags;
        private readonly int _clearKernel;
        private readonly int _proposalKernel;
        private readonly int _depthKernel;
        private readonly int _rgbKernel;
        private readonly int _evaluateKernel;
        private readonly int _compactKernel;
        private bool _disposed;

        internal SigmaFrameGraph(Vector2Int resolution,
            SigmaExactBackendGate backendGate,
            SigmaFrameMemoryProfile profile =
                SigmaFrameMemoryProfile.HighThroughput)
        {
            _backendGate = backendGate ?? throw new ArgumentNullException(
                nameof(backendGate));
            _inverse = UnityEngine.Resources.Load<ComputeShader>(InverseResource);
            if (_inverse == null)
                throw new InvalidOperationException(
                    "Required Sigma direct-frame inverse shader is missing.");
            _clearKernel = _inverse.FindProfiledKernel("ClearFrameState");
            _proposalKernel = _inverse.FindProfiledKernel(
                "BuildFrameProposals");
            _depthKernel = _inverse.FindProfiledKernel("BuildDepthSourceCells");
            _rgbKernel = _inverse.FindProfiledKernel("BuildRgbSourceCells");
            _evaluateKernel = _inverse.FindProfiledKernel(
                "EvaluateCandidateMeets");
            _compactKernel = _inverse.FindProfiledKernel(
                "CompactResolvedTargets");

            Resources = new SigmaFrameResources(resolution, profile);
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
                _nullCurrentFlags = CreateBuffer(1, sizeof(uint),
                    "Sigma frame null current binding");
                _nullCarrierState.SetData(new SigmaFrameUInt2Gpu[1]);
                _nullPageMetadata.SetData(new uint[12]);
                _nullCurrentFlags.SetData(new uint[1]);
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
            RequireSingleExecutionWindow(sources);
            long candidateRecords = checked((long)Resources.FootprintCount *
                ProposalsPerFootprint);
            if (!Resources.TryEnsureCandidateCapacity(candidateRecords))
                throw new InvalidOperationException(
                    "Direct-frame candidate window cannot be admitted losslessly.");
            GraphicsBuffer candidates = Single(Resources.Candidates,
                candidateRecords);
            GraphicsBuffer outcomes = Single(Resources.Outcomes,
                candidateRecords);
            GraphicsBuffer candidateStates = Single(Resources.CandidateStates,
                checked(candidateRecords * SigmaGeneratedFrame.LaneCount));
            GraphicsBuffer deltas = Single(Resources.Deltas, candidateRecords);
            int resolvedBlockCount = CeilDiv(Resources.FootprintCount, 256);
            GraphicsBuffer resolvedBlockCounts = Single(
                Resources.ResolvedBlockCounts, resolvedBlockCount);

            BindFrameConstants(command, source, revision, ownedFrame.Slot, input,
                checked((int)candidateRecords), resolvedBlockCount);

            command.SetComputeBufferParam(_inverse, _clearKernel, "_OwnedFrames",
                Resources.OwnedFrames);
            command.SetComputeBufferParam(_inverse, _clearKernel,
                "_FrameOutcomes", outcomes);
            command.SetComputeBufferParam(_inverse, _clearKernel,
                "_FrameDeltas", deltas);
            command.SetComputeBufferParam(_inverse, _clearKernel,
                "_FrameResolvedBlockCounts", resolvedBlockCounts);
            command.DispatchComputeProfiled(_inverse, _clearKernel,
                CeilDiv(checked((int)candidateRecords), 256), 1, 1);

            BindProposal(command, source, input, candidates);
            for (int first = 0; first < Resources.FootprintCount;
                first += footprintWindow)
            {
                int count = Math.Min(footprintWindow,
                    Resources.FootprintCount - first);
                BindFootprintWindow(command, first, count);
                command.DispatchComputeProfiled(_inverse, _proposalKernel,
                    CeilDiv(count, 256), 1, 1);
            }

            RecordDepthSource(command, input, sources,
                SigmaFrameSource.DepthLeft, footprintWindow);
            RecordDepthSource(command, input, sources,
                SigmaFrameSource.DepthRight, footprintWindow);
            RecordRgbSource(command, source, input, sources,
                SigmaFrameSource.RgbLeft, footprintWindow);
            RecordRgbSource(command, source, input, sources,
                SigmaFrameSource.RgbRight, footprintWindow);
            BindFootprintWindow(command, 0, Resources.FootprintCount);

            BindEvaluate(command, input, sources, candidates, outcomes,
                candidateStates);
            IReadOnlyList<SigmaCarrierReadBatch> readable =
                input.CarrierSegments;
            if (readable != null)
            {
                for (int index = 0; index < readable.Count; ++index)
                {
                    SigmaCarrierReadBatch batch = readable[index];
                    BindCarrier(command, batch.State, batch.Metadata,
                        batch.CurrentFlags, batch.SegmentIndex,
                        batch.PageCapacity,
                        (uint)SigmaFrameProposalKind.Current);
                    command.DispatchComputeProfiled(_inverse, _evaluateKernel,
                        CeilDiv(checked((int)candidateRecords),
                            FootprintsPerCoordinateGroup), 1, 1);
                }
            }
            BindCarrier(command, _nullCarrierState, _nullPageMetadata,
                _nullCurrentFlags, -1, 1,
                (uint)SigmaFrameProposalKind.Novel);
            command.DispatchComputeProfiled(_inverse, _evaluateKernel,
                CeilDiv(checked((int)candidateRecords),
                    FootprintsPerCoordinateGroup), 1, 1);

            command.SetComputeBufferParam(_inverse, _compactKernel,
                "_FrameCandidates", candidates);
            command.SetComputeBufferParam(_inverse, _compactKernel,
                "_FrameOutcomes", outcomes);
            command.SetComputeBufferParam(_inverse, _compactKernel,
                "_FrameDeltas", deltas);
            command.SetComputeBufferParam(_inverse, _compactKernel,
                "_FrameResolvedBlockCounts", resolvedBlockCounts);
            command.DispatchComputeProfiled(_inverse, _compactKernel,
                resolvedBlockCount, 1, 1);
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
            _nullCurrentFlags?.Dispose();
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
            SetFrameMatrices(command, source);
        }

        private void BindProposal(CommandBuffer command,
            StereoRigFrameLease source, SigmaFrameInverseInput input,
            GraphicsBuffer candidates)
        {
            command.SetComputeBufferParam(_inverse, _proposalKernel,
                "_FrameCandidates", candidates);
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
            SigmaFrameSource source, int footprintWindow)
        {
            BindSourceOutputs(command, _depthKernel, sources, source);
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
            for (int first = 0; first < Resources.FootprintCount;
                first += footprintWindow)
            {
                int count = Math.Min(footprintWindow,
                    Resources.FootprintCount - first);
                BindFootprintWindow(command, first, count);
                command.DispatchComputeProfiled(_inverse, _depthKernel,
                    CeilDiv(count, FootprintsPerCoordinateGroup), 1, 1);
            }
        }

        private void RecordRgbSource(CommandBuffer command,
            StereoRigFrameLease frame, SigmaFrameInverseInput input,
            SigmaFrameSourceStorage sources, SigmaFrameSource source,
            int footprintWindow)
        {
            BindSourceOutputs(command, _rgbKernel, sources, source);
            BindSourceReads(command, _rgbKernel, sources);
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
            for (int first = 0; first < Resources.FootprintCount;
                first += footprintWindow)
            {
                int count = Math.Min(footprintWindow,
                    Resources.FootprintCount - first);
                BindFootprintWindow(command, first, count);
                command.DispatchComputeProfiled(_inverse, _rgbKernel,
                    CeilDiv(count, FootprintsPerCoordinateGroup), 1, 1);
            }
        }

        private void BindEvaluate(CommandBuffer command,
            SigmaFrameInverseInput input, SigmaFrameSourceStorage sources,
            GraphicsBuffer candidates, GraphicsBuffer outcomes,
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
            BindSourceReads(command, _evaluateKernel, sources);
        }

        private void BindCarrier(CommandBuffer command, GraphicsBuffer state,
            GraphicsBuffer metadata, GraphicsBuffer currentFlags,
            int segmentIndex, int pageCapacity, uint proposalKind)
        {
            command.SetComputeBufferParam(_inverse, _evaluateKernel,
                "_CarrierState", state);
            command.SetComputeBufferParam(_inverse, _evaluateKernel,
                "_PageMetadata", metadata);
            command.SetComputeBufferParam(_inverse, _evaluateKernel,
                "_CurrentFlags", currentFlags);
            command.SetComputeIntParam(_inverse, "_CarrierSegmentIndex",
                segmentIndex);
            command.SetComputeIntParam(_inverse, "_PageCapacity",
                pageCapacity);
            command.SetComputeIntParam(_inverse, "_ResolveProposalKind",
                unchecked((int)proposalKind));
        }

        private void BindSourceOutputs(CommandBuffer command, int kernel,
            SigmaFrameSourceStorage sources, SigmaFrameSource source)
        {
            command.SetComputeBufferParam(_inverse, kernel, "_SourceLowerOut",
                Single(sources.Lower(source),
                    checked((long)Resources.FootprintCount *
                        SigmaGeneratedFrame.LaneCount)));
            command.SetComputeBufferParam(_inverse, kernel, "_SourceUpperOut",
                Single(sources.Upper(source),
                    checked((long)Resources.FootprintCount *
                        SigmaGeneratedFrame.LaneCount)));
            command.SetComputeBufferParam(_inverse, kernel,
                "_SourceValidityOut", Single(sources.Validity(source),
                    checked((long)Resources.FootprintCount *
                        SigmaGeneratedFrame.LaneCount)));
            command.SetComputeBufferParam(_inverse, kernel,
                "_SourceProvenanceOut", Single(sources.Provenance(source),
                    Resources.FootprintCount));
        }

        private void BindSourceReads(CommandBuffer command, int kernel,
            SigmaFrameSourceStorage sources)
        {
            BindSourceRead(command, kernel, sources,
                SigmaFrameSource.DepthLeft, "DepthLeft");
            BindSourceRead(command, kernel, sources,
                SigmaFrameSource.DepthRight, "DepthRight");
            BindSourceRead(command, kernel, sources,
                SigmaFrameSource.RgbLeft, "RgbLeft");
            BindSourceRead(command, kernel, sources,
                SigmaFrameSource.RgbRight, "RgbRight");
        }

        private void BindSourceRead(CommandBuffer command, int kernel,
            SigmaFrameSourceStorage sources, SigmaFrameSource source,
            string prefix)
        {
            long coordinates = checked((long)Resources.FootprintCount *
                SigmaGeneratedFrame.LaneCount);
            command.SetComputeBufferParam(_inverse, kernel,
                $"_{prefix}Lower", Single(sources.Lower(source), coordinates));
            command.SetComputeBufferParam(_inverse, kernel,
                $"_{prefix}Upper", Single(sources.Upper(source), coordinates));
            command.SetComputeBufferParam(_inverse, kernel,
                $"_{prefix}Validity", Single(sources.Validity(source),
                    coordinates));
            command.SetComputeBufferParam(_inverse, kernel,
                $"_{prefix}Provenance", Single(sources.Provenance(source),
                    Resources.FootprintCount));
        }

        private void SetFrameMatrices(CommandBuffer command,
            StereoRigFrameLease source)
        {
            Matrix4x4 leftWorld = PoseMatrix(source.DepthLeft.WorldFromCamera);
            Matrix4x4 rightWorld = PoseMatrix(source.DepthRight.WorldFromCamera);
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
                PoseMatrix(source.RgbLeft.WorldFromCamera).inverse);
            command.SetComputeMatrixParam(_inverse,
                "_RgbOpticalFromWorldRight",
                PoseMatrix(source.RgbRight.WorldFromCamera).inverse);
            command.SetComputeVectorParam(_inverse, "_RgbIntrinsicsLeft",
                Intrinsics(source.RgbLeft.Intrinsics));
            command.SetComputeVectorParam(_inverse, "_RgbIntrinsicsRight",
                Intrinsics(source.RgbRight.Intrinsics));
        }

        private static void RequireSingleExecutionWindow(
            SigmaFrameSourceStorage sources)
        {
            for (int source = 0; source < SigmaGeneratedFrame.SourceCount;
                ++source)
            {
                SigmaFrameSource kind = (SigmaFrameSource)source;
                if (sources.Lower(kind).Segments.Count != 1 ||
                    sources.Upper(kind).Segments.Count != 1 ||
                    sources.Validity(kind).Segments.Count != 1 ||
                    sources.Provenance(kind).Segments.Count != 1)
                    throw new InvalidOperationException(
                        "M2 direct-frame dispatch requires one legal binding " +
                        "window; segmented continuation is closed in M3.");
            }
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

        private static GraphicsBuffer CreateBuffer(int count, int stride,
            string name) => new(GraphicsBuffer.Target.Structured, count, stride)
            { name = name };

        private static Matrix4x4 PoseMatrix(Pose pose) => Matrix4x4.TRS(
            pose.position, pose.rotation, Vector3.one);

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
