using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.SigmaPrism
{
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

    internal readonly struct SigmaNativeFrameInput
    {
        internal SigmaNativeFrameInput(SigmaPredictionFrameLease prediction,
            RenderTexture metricDepth, RenderTexture depthFlags,
            GraphicsBuffer depthCalibration, GraphicsBuffer rgbCalibration,
            GraphicsBuffer poseResult, ConeLutLease coneLuts,
            uint depthLeftKey, uint depthRightKey, uint rgbLeftKey,
            uint rgbRightKey, SigmaCarrierReadBatch carrier)
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
            ConeLuts = coneLuts ?? throw new ArgumentNullException(nameof(coneLuts));
            DepthLeftKey = depthLeftKey;
            DepthRightKey = depthRightKey;
            RgbLeftKey = rgbLeftKey;
            RgbRightKey = rgbRightKey;
            Carrier = carrier;
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
        internal SigmaCarrierReadBatch Carrier { get; }
    }

    internal sealed class SigmaNativeFrameLease : IDisposable
    {
        private SigmaNativeFrameGraph _owner;

        internal SigmaNativeFrameLease(SigmaNativeFrameGraph owner, int slot,
            SigmaNativeFrameSlotResources resources)
        {
            _owner = owner;
            Slot = slot;
            Resources = resources;
        }

        internal int Slot { get; }
        internal SigmaNativeFrameSlotResources Resources { get; }

        public void Dispose()
        {
            SigmaNativeFrameGraph owner = _owner;
            if (owner == null)
                return;
            _owner = null;
            owner.Release(Slot);
        }
    }

    /// <summary>
    /// Bounded physical lowering of one NativeCloseCommit. Nine submissions are
    /// invariant in branch/relation/page cardinality: those cardinalities map to
    /// workgroups inside the generated collective kernels.
    /// </summary>
    internal sealed class SigmaNativeFrameGraph : IDisposable
    {
        private const string FrameResource = "SigmaPrism/SigmaNativeFrame";
        private const string QueryResource = "SigmaPrism/SigmaNativeQuery";
        private const string ContractResource = "SigmaPrism/SigmaNativeContract";
        internal const int HotDispatchCount = 9;

        private readonly SigmaExactBackendGate _backendGate;
        private readonly ComputeShader _frame;
        private readonly ComputeShader _query;
        private readonly ComputeShader _contract;
        private readonly int _buildObservation;
        private readonly int _prepareRevision;
        private readonly int _preparePage;
        private readonly int _scatterState;
        private readonly int _closePublish;
        private readonly int _contractNative;
        private readonly int _evaluateRelation;
        private bool _disposed;

        internal SigmaNativeFrameGraph(Vector2Int resolution,
            SigmaExactBackendGate backendGate, int frameCapacity)
        {
            _backendGate = backendGate ?? throw new ArgumentNullException(
                nameof(backendGate));
            _frame = Resources.Load<ComputeShader>(FrameResource);
            _query = Resources.Load<ComputeShader>(QueryResource);
            _contract = Resources.Load<ComputeShader>(ContractResource);
            if (_frame == null || _query == null || _contract == null)
                throw new InvalidOperationException(
                    "Generated native close shaders are missing.");
            _buildObservation = _frame.FindProfiledKernel(
                "BuildNativeObservation");
            _prepareRevision = _frame.FindProfiledKernel(
                "PrepareNativeRevision");
            _preparePage = _frame.FindProfiledKernel("PrepareNativePage");
            _scatterState = _frame.FindProfiledKernel("ScatterNativeState");
            _closePublish = _frame.FindProfiledKernel(
                "CloseAndPublishNativeRevision");
            _contractNative = _contract.FindProfiledKernel(
                "ContractNativeQuery");
            _evaluateRelation = _query.FindProfiledKernel(
                "EvaluateNativeRelation");
            FrameResources = new SigmaNativeFrameResources(resolution,
                frameCapacity);
        }

        internal SigmaNativeFrameResources FrameResources { get; }
        internal Vector2Int Resolution => FrameResources.Resolution;
        internal int FrameCapacity => FrameResources.FrameCapacity;
        internal long OwnedBytes => FrameResources.OwnedBytes;

        internal bool TryAcquire(out SigmaNativeFrameLease lease)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SigmaNativeFrameGraph));
            if (!FrameResources.TryLease(out int index,
                    out SigmaNativeFrameSlotResources resources))
            {
                lease = null;
                return false;
            }
            lease = new SigmaNativeFrameLease(this, index, resources);
            return true;
        }

        internal void RecordNativeCloseCommit(CommandBuffer command,
            SigmaNativeFrameLease lease, uint revision, uint calibrationEpoch,
            in SigmaNativeFrameInput input)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (lease == null) throw new ArgumentNullException(nameof(lease));
            if (revision == 0u) throw new ArgumentOutOfRangeException(nameof(revision));
            SigmaNativeFrameSlotResources slot = lease.Resources;
            BindFrameKernels(command, slot, revision, calibrationEpoch, input);
            command.DispatchComputeProfiled(_frame, _buildObservation, 1, 1, 1);

            BindContract(command, slot);
            command.SetComputeIntParam(_contract, "_NativeContractMode", 1);
            command.SetComputeIntParam(_contract, "_NativeFreshBranchCount",
                SigmaNativeFrameSlotResources.LiveFreshBranchCount);
            command.DispatchComputeProfiled(_contract, _contractNative,
                SigmaNativeFrameSlotResources.LiveFreshBranchCount, 1, 1);

            BindRelation(command, slot, 1);
            command.DispatchComputeProfiled(_query, _evaluateRelation, 1, 1, 1);

            command.SetComputeIntParam(_contract, "_NativeContractMode", 2);
            command.DispatchComputeProfiled(_contract, _contractNative, 1, 1, 1);

            // The same hyperdimensional collective now verifies both the fresh
            // ZEmpty boundary and prior-to-selected transport. Two relations are
            // two workgroups in one dispatch, never two relation submissions.
            BindRelation(command, slot,
                SigmaNativeFrameSlotResources.RelationCapacity);
            command.DispatchComputeProfiled(_query, _evaluateRelation,
                SigmaNativeFrameSlotResources.RelationCapacity, 1, 1);

            command.DispatchComputeProfiled(_frame, _prepareRevision, 1, 1, 1);
            command.DispatchComputeProfiled(_frame, _preparePage,
                SigmaCarrier.PageLaneCount / 256, 1, 1);
            command.DispatchComputeProfiled(_frame, _scatterState, 1, 1, 1);
            command.DispatchComputeProfiled(_frame, _closePublish, 1, 1, 1);
        }

        internal void Release(int slot) => FrameResources.Release(slot);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            FrameResources.Dispose();
        }

        private void BindFrameKernels(CommandBuffer command,
            SigmaNativeFrameSlotResources slot, uint revision,
            uint calibrationEpoch, in SigmaNativeFrameInput input)
        {
            int[] kernels = { _buildObservation, _prepareRevision, _preparePage,
                _scatterState, _closePublish };
            StereoRigFrameLease source = input.Prediction.Source;
            foreach (int kernel in kernels)
            {
                command.SetComputeBufferParam(_frame, kernel,
                    "_SigmaExactBackendGate", _backendGate.Buffer);
                command.SetComputeBufferParam(_frame, kernel,
                    "_DepthCalibrationQ48", input.DepthCalibration);
                command.SetComputeBufferParam(_frame, kernel,
                    "_RgbCalibrationQ48", input.RgbCalibration);
                command.SetComputeBufferParam(_frame, kernel,
                    "_PoseResult", input.PoseResult);
                command.SetComputeBufferParam(_frame, kernel,
                    "_NativeFrames", slot.NativeFrame);
                command.SetComputeBufferParam(_frame, kernel,
                    "_NativeObservations", slot.Observation);
                command.SetComputeBufferParam(_frame, kernel,
                    "_NativeFreshObservationHeaders",
                    slot.FreshObservationHeaders);
                command.SetComputeBufferParam(_frame, kernel,
                    "_NativeFreshRoomRays", slot.FreshRoomRays);
                command.SetComputeBufferParam(_frame, kernel,
                    "_NativeFreshCodeLeaves", slot.FreshCodeLeaves);
                command.SetComputeBufferParam(_frame, kernel,
                    "_NativeStates", slot.States);
                command.SetComputeBufferParam(_frame, kernel,
                    "_NativeBranchHeaders", slot.BranchHeaders);
                command.SetComputeBufferParam(_frame, kernel,
                    "_NativeRelationResults", slot.RelationResults);
                command.SetComputeBufferParam(_frame, kernel,
                    "_NativeRelationFactors", slot.RelationFactors);
                command.SetComputeBufferParam(_frame, kernel,
                    "_NativeRelationHashes", slot.RelationHashes);
                command.SetComputeBufferParam(_frame, kernel,
                    "_NativeStateDeltas", slot.StateDelta);
                command.SetComputeBufferParam(_frame, kernel,
                    "_NativeGaugeDeltas", slot.GaugeDelta);
                command.SetComputeBufferParam(_frame, kernel,
                    "_NativeUnresolved", slot.Unresolved);
                command.SetComputeBufferParam(_frame, kernel,
                    "_NativeRevisions", slot.Revisions);
                command.SetComputeBufferParam(_frame, kernel,
                    "_NativeCounters", slot.Counters);
                command.SetComputeBufferParam(_frame, kernel,
                    "_NativeSourceCarrierState", input.Carrier.State);
                command.SetComputeBufferParam(_frame, kernel,
                    "_NativeSourcePageMetadata", input.Carrier.Metadata);
                command.SetComputeBufferParam(_frame, kernel,
                    "_NativeSourcePublicationRoot",
                    input.Carrier.PublicationRoot);
                command.SetComputeBufferParam(_frame, kernel,
                    "_TargetCarrierState", input.Carrier.State);
                command.SetComputeBufferParam(_frame, kernel,
                    "_TargetPageMetadata", input.Carrier.Metadata);
                command.SetComputeBufferParam(_frame, kernel,
                    "_TargetDirtyFlags", input.Carrier.DirtyFlags);
                command.SetComputeBufferParam(_frame, kernel,
                    "_TargetReadoutDirtyFlags", input.Carrier.ReadoutDirtyFlags);
                command.SetComputeBufferParam(_frame, kernel,
                    "_PublishedRevisionRoot", input.Carrier.PublicationRoot);
            }
            command.SetComputeTextureParam(_frame, _buildObservation,
                "_NativeMetricDepth", input.MetricDepth);
            command.SetComputeTextureParam(_frame, _buildObservation,
                "_NativeDepthFlags", input.DepthFlags);
            command.SetComputeTextureParam(_frame, _buildObservation,
                "_NativeRawDepth", source.DepthLeft.Texture);
            command.SetComputeTextureParam(_frame, _buildObservation,
                "_NativeDepthRayCenterLeft",
                input.ConeLuts.DepthLeft.CenterRaySolidAngle);
            command.SetComputeTextureParam(_frame, _buildObservation,
                "_NativeDepthRayCenterRight",
                input.ConeLuts.DepthRight.CenterRaySolidAngle);
            command.SetComputeTextureParam(_frame, _buildObservation,
                "_NativeDepthRayDifferentialXLeft",
                input.ConeLuts.DepthLeft.DifferentialXHalfAngle);
            command.SetComputeTextureParam(_frame, _buildObservation,
                "_NativeDepthRayDifferentialXRight",
                input.ConeLuts.DepthRight.DifferentialXHalfAngle);
            command.SetComputeTextureParam(_frame, _buildObservation,
                "_NativeDepthRayDifferentialYLeft",
                input.ConeLuts.DepthLeft.DifferentialYHalfAngle);
            command.SetComputeTextureParam(_frame, _buildObservation,
                "_NativeDepthRayDifferentialYRight",
                input.ConeLuts.DepthRight.DifferentialYHalfAngle);
            command.SetComputeTextureParam(_frame, _buildObservation,
                "_NativeRgbLeft", source.RgbLeft.Texture);
            command.SetComputeTextureParam(_frame, _buildObservation,
                "_NativeRgbRight", source.RgbRight.Texture);
            command.SetComputeTextureParam(_frame, _buildObservation,
                "_NativePredCarrierPage", input.Prediction.CarrierPage);
            command.SetComputeTextureParam(_frame, _buildObservation,
                "_NativePredCarrierUvNormal",
                input.Prediction.CarrierUvNormal);
            command.SetComputeTextureParam(_frame, _buildObservation,
                "_NativePredStateKey", input.Prediction.StateKey);
            command.SetComputeIntParams(_frame, "_NativeResolution",
                Resolution.x, Resolution.y);
            command.SetComputeIntParams(_frame, "_NativeRgbLeftResolution",
                source.RgbLeft.Resolution.x, source.RgbLeft.Resolution.y);
            command.SetComputeIntParams(_frame, "_NativeRgbRightResolution",
                source.RgbRight.Resolution.x, source.RgbRight.Resolution.y);
            Matrix4x4 worldToRoom = input.Prediction.WorldToRoom;
            Matrix4x4 depthLeftRoom = SigmaRoomFrame.FromCamera(worldToRoom,
                source.DepthLeft.WorldFromCamera);
            Matrix4x4 depthRightRoom = SigmaRoomFrame.FromCamera(worldToRoom,
                source.DepthRight.WorldFromCamera);
            command.SetComputeMatrixParam(_frame,
                "_NativeRoomFromDepthLeft", depthLeftRoom);
            command.SetComputeMatrixParam(_frame,
                "_NativeRoomFromDepthRight", depthRightRoom);
            command.SetComputeMatrixParam(_frame,
                "_NativeRgbFromRoomLeft", SigmaRoomFrame.FromCamera(worldToRoom,
                    source.RgbLeft.WorldFromCamera).inverse);
            command.SetComputeMatrixParam(_frame,
                "_NativeRgbFromRoomRight", SigmaRoomFrame.FromCamera(worldToRoom,
                    source.RgbRight.WorldFromCamera).inverse);
            command.SetComputeVectorParam(_frame, "_NativeRgbIntrinsicsLeft",
                Intrinsics(source.RgbLeft.Intrinsics));
            command.SetComputeVectorParam(_frame, "_NativeRgbIntrinsicsRight",
                Intrinsics(source.RgbRight.Intrinsics));
            command.SetComputeMatrixParam(_frame,
                "_PoseConsumeReferenceFromWorld", depthLeftRoom.inverse);
            command.SetComputeMatrixParam(_frame,
                "_PoseConsumeWorldFromReference", depthLeftRoom);
            command.SetComputeIntParams(_frame, "_NativeOpticalTransfers",
                unchecked((int)OpticalTransfer(source.RgbLeft)),
                unchecked((int)OpticalTransfer(source.RgbRight)));
            command.SetComputeIntParam(_frame, "_NativeRevision",
                unchecked((int)revision));
            command.SetComputeIntParam(_frame, "_NativeCalibrationEpoch",
                unchecked((int)calibrationEpoch));
            command.SetComputeIntParams(_frame, "_NativeIndependenceKeys",
                unchecked((int)input.DepthLeftKey),
                unchecked((int)input.DepthRightKey),
                unchecked((int)input.RgbLeftKey),
                unchecked((int)input.RgbRightKey));
            command.SetComputeIntParam(_frame, "_NativeTargetSegmentIndex",
                input.Carrier.SegmentIndex);
            command.SetComputeIntParam(_frame, "_NativeTargetPageCapacity",
                input.Carrier.PageCapacity);
        }

        private void BindContract(CommandBuffer command,
            SigmaNativeFrameSlotResources slot)
        {
            int kernel = _contractNative;
            command.SetComputeBufferParam(_contract, kernel,
                "_NativeReverseKeys", slot.DummyUInt4);
            command.SetComputeBufferParam(_contract, kernel,
                "_NativeReverseStates", slot.DummyUInt4);
            command.SetComputeBufferParam(_contract, kernel,
                "_NativeReverseGaugeCoordinates", slot.DummyUInt4);
            command.SetComputeBufferParam(_contract, kernel,
                "_NativeReverseGaugeMetadata", slot.DummyUInt4);
            command.SetComputeBufferParam(_contract, kernel,
                "_NativeReverseRelationResults", slot.RelationResults);
            command.SetComputeBufferParam(_contract, kernel,
                "_NativeReverseRelationFactors", slot.RelationFactors);
            command.SetComputeBufferParam(_contract, kernel,
                "_NativeReverseRelationHashes", slot.RelationHashes);
            command.SetComputeBufferParam(_contract, kernel,
                "_NativeReverseDeltaHashes", slot.DummyUInt);
            command.SetComputeBufferParam(_contract, kernel,
                "_NativeStates", slot.States);
            command.SetComputeBufferParam(_contract, kernel,
                "_NativeQueryRows", slot.DummyUInt2);
            command.SetComputeBufferParam(_contract, kernel,
                "_NativeFreshObservationHeaders",
                slot.FreshObservationHeaders);
            command.SetComputeBufferParam(_contract, kernel,
                "_NativeFreshRoomRays", slot.FreshRoomRays);
            command.SetComputeBufferParam(_contract, kernel,
                "_NativeFreshCodeLeaves", slot.FreshCodeLeaves);
            command.SetComputeBufferParam(_contract, kernel,
                "_NativeObservationOrder", slot.DummyUInt2);
            command.SetComputeBufferParam(_contract, kernel,
                "_NativeMeasuredOptical", slot.DummyUInt2);
            command.SetComputeBufferParam(_contract, kernel,
                "_NativeDirection", slot.DummyUInt2);
            command.SetComputeBufferParam(_contract, kernel,
                "_NativePhotometricExposure", slot.DummyUInt2);
            command.SetComputeBufferParam(_contract, kernel,
                "_NativePhotometricChannelParameters", slot.DummyUInt2);
            command.SetComputeBufferParam(_contract, kernel,
                "_NativePhotometricTransferRanges", slot.DummyUInt2);
            command.SetComputeBufferParam(_contract, kernel,
                "_NativePhotometricTransferData", slot.DummyUInt2);
            command.SetComputeBufferParam(_contract, kernel,
                "_NativeBranchHeaders", slot.BranchHeaders);
            command.SetComputeBufferParam(_contract, kernel,
                "_NativeBranchSupports", slot.BranchSupports);
            command.SetComputeBufferParam(_contract, kernel,
                "_NativeBranchActions", slot.BranchActions);
            command.SetComputeBufferParam(_contract, kernel,
                "_NativeBranchPredictions", slot.BranchPredictions);
            command.SetComputeBufferParam(_contract, kernel,
                "_NativeBranchRelationFactors", slot.BranchRelationFactors);
            command.SetComputeBufferParam(_contract, kernel,
                "_NativeBranchRelationHashes", slot.BranchRelationHashes);
            command.SetComputeBufferParam(_contract, kernel,
                "_NativeOverflowArgs", slot.DummyUInt);
            command.SetComputeIntParam(_contract, "_NativeReverseCount", 0);
            command.SetComputeIntParam(_contract, "_NativeHotCapacity", 0);
            command.SetComputeIntParam(_contract, "_NativeObservationFlags", 0);
            command.SetComputeIntParam(_contract, "_NativeEntryPointIndex",
                SigmaGeneratedFrame.SensorLeftEntryPoint);
            command.SetComputeIntParam(_contract,
                "_NativeFreshLeftEntryPointIndex",
                SigmaGeneratedFrame.SensorLeftEntryPoint);
            command.SetComputeIntParam(_contract,
                "_NativeFreshRightEntryPointIndex",
                SigmaGeneratedFrame.SensorRightEntryPoint);
            command.SetComputeIntParam(_contract,
                "_NativeFreshPriorStateOffset",
                SigmaNativeFrameSlotResources.LiveFreshBranchCount *
                    SigmaS16.LaneCount + 2 * SigmaS16.LaneCount);
        }

        private void BindRelation(CommandBuffer command,
            SigmaNativeFrameSlotResources slot, int count)
        {
            command.SetComputeBufferParam(_query, _evaluateRelation,
                "_NativeStates", slot.States);
            command.SetComputeBufferParam(_query, _evaluateRelation,
                "_NativeRelationInputs", slot.RelationInputs);
            command.SetComputeBufferParam(_query, _evaluateRelation,
                "_NativeRelationPlans", slot.RelationPlans);
            command.SetComputeBufferParam(_query, _evaluateRelation,
                "_NativeRelationNearIntervals", slot.RelationNearIntervals);
            command.SetComputeBufferParam(_query, _evaluateRelation,
                "_NativeRelationResults", slot.RelationResults);
            command.SetComputeBufferParam(_query, _evaluateRelation,
                "_NativeRelationFactors", slot.RelationFactors);
            command.SetComputeBufferParam(_query, _evaluateRelation,
                "_NativeRelationHashes", slot.RelationHashes);
            command.SetComputeBufferParam(_query, _evaluateRelation,
                "_NativeRelationNorms", slot.RelationNorms);
            command.SetComputeIntParam(_query, "_NativeEntryPointIndex",
                SigmaGeneratedFrame.IntrinsicRelationEntryPoint);
            command.SetComputeIntParam(_query, "_NativeRelationCount", count);
        }

        private static Vector4 Intrinsics(RigIntrinsics intrinsics) => new(
            intrinsics.FocalLength.x, intrinsics.FocalLength.y,
            intrinsics.PrincipalPoint.x, intrinsics.PrincipalPoint.y);

        private static uint OpticalTransfer(GpuImageView view)
        {
            if (!view.IsValid || view.GraphicsFormat == GraphicsFormat.None)
                return uint.MaxValue;
            return GraphicsFormatUtility.IsSRGBFormat(view.GraphicsFormat)
                ? 1u : 0u;
        }
    }
}
