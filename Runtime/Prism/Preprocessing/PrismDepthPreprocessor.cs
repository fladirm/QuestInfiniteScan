using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.Prism
{
    public readonly struct DepthPreprocessDiagnosticSnapshot
    {
        internal DepthPreprocessDiagnosticSnapshot(long accepted, long rejected,
            long ringBackpressure, uint calibrationEpoch)
        {
            AcceptedFrames = accepted;
            RejectedFrames = rejected;
            RingBackpressureFrames = ringBackpressure;
            CalibrationEpoch = calibrationEpoch;
        }

        public long AcceptedFrames { get; }
        public long RejectedFrames { get; }
        public long RingBackpressureFrames { get; }
        public uint CalibrationEpoch { get; }
    }

    /// <summary>
    /// Q3-03 GPU front end: immutable cone LUT epochs plus lossless conversion of Meta
    /// projection depth into one canonical metric first-contact convention.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-20)]
    public sealed class PrismDepthPreprocessor : MonoBehaviour
    {
        [SerializeField] private PrismRigCapture rigCapture;
        [SerializeField] private ComputeShader coneLutCompute;
        [SerializeField] private ComputeShader depthNormalizeCompute;
        [SerializeField] private ComputeShader depthConsensusNormalBoundaryCompute;
        [SerializeField, Range(3, 12)] private int outputRingSlots = 5;
        [SerializeField, Min(0.001f)] private float bootstrapSigmaMeters = 0.008f;
        [SerializeField, Min(0f)] private float bootstrapSigmaSlope = 0.003f;
        [SerializeField, Range(0.001f, 1f)] private float sigmaEmaAlpha = 0.05f;
        [Header("Independent contact uncertainty")]
        [SerializeField, Min(0f)] private float poseTranslationSigmaMeters = 0.001f;
        [SerializeField, Min(0f)] private float poseAngularSigmaDegrees = 0.035f;
        [SerializeField, Min(0f)] private float calibrationAngularSigmaDegrees = 0.05f;
        [SerializeField, Min(0f)] private float depthTemporalApertureMilliseconds = 2f;

        private static readonly int RawDepthId = Shader.PropertyToID("_RawDepth");
        private static readonly int RayLeftId = Shader.PropertyToID("_DepthRayCenterLeft");
        private static readonly int RayRightId = Shader.PropertyToID("_DepthRayCenterRight");
        private static readonly int MetricDepthId = Shader.PropertyToID("_MetricDepth");
        private static readonly int FlagsId = Shader.PropertyToID("_DepthFlags");
        private static readonly int ResolutionId = Shader.PropertyToID("_Resolution");
        private static readonly int NearFarId = Shader.PropertyToID("_NearFar");
        private static readonly int ConsensusDepthId = Shader.PropertyToID("_ConsensusDepth");
        private static readonly int LocalNormalId = Shader.PropertyToID("_LocalNormal");
        private static readonly int BoundaryEvidenceId = Shader.PropertyToID("_BoundaryEvidence");
        private static readonly int FlagsRwId = Shader.PropertyToID("_DepthFlagsRW");
        private static readonly int MetricDepthInputId = Shader.PropertyToID("_MetricDepth");
        private static readonly int DepthResolutionId = Shader.PropertyToID("_DepthResolution");
        private static readonly int RgbResolutionId = Shader.PropertyToID("_RgbResolution");
        private static readonly int DepthIntrinsicsId = Shader.PropertyToID("_DepthIntrinsics");
        private static readonly int RgbIntrinsicsId = Shader.PropertyToID("_RgbIntrinsics");
        private static readonly int OtherDepthFromThisId = Shader.PropertyToID("_OtherDepthFromThis");
        private static readonly int RgbFromDepthId = Shader.PropertyToID("_RgbFromDepth");
        private static readonly int RgbLeftId = Shader.PropertyToID("_RgbLeft");
        private static readonly int RgbRightId = Shader.PropertyToID("_RgbRight");
        private static readonly int ResidualHistogramId = Shader.PropertyToID("_ResidualHistogram");
        private static readonly int RangeSigmaId = Shader.PropertyToID("_RangeSigma");
        private static readonly int BootstrapSigmaId = Shader.PropertyToID("_BootstrapSigma");
        private static readonly int BootstrapSigmaSlopeId = Shader.PropertyToID("_BootstrapSigmaSlope");
        private static readonly int SigmaEmaAlphaId = Shader.PropertyToID("_SigmaEmaAlpha");
        private static readonly int PoseTranslationSigmaId =
            Shader.PropertyToID("_PoseTranslationSigma");
        private static readonly int PoseAngularSigmaId =
            Shader.PropertyToID("_PoseAngularSigma");
        private static readonly int CalibrationAngularSigmaId =
            Shader.PropertyToID("_CalibrationAngularSigma");
        private static readonly int MotionTranslationSigmaId =
            Shader.PropertyToID("_MotionTranslationSigma");
        private static readonly int MotionAngularSigmaId =
            Shader.PropertyToID("_MotionAngularSigma");

        private RigConeLutSet _coneLutSet;
        private NormalizedDepthRing _outputRing;
        private NormalizedRigFrameLease _latestFrame;
        private int _normalizeKernel = -1;
        private int _initializeSigmaKernel = -1;
        private int _clearHistogramKernel = -1;
        private int _consensusKernel = -1;
        private int _solveSigmaKernel = -1;
        private int _planeFitKernel = -1;
        private int _boundaryKernel = -1;
        private GraphicsBuffer _residualHistogram;
        private GraphicsBuffer _rangeSigma;
        private bool _rangeSigmaInitialized;
        private readonly Vector4[] _depthIntrinsics = new Vector4[2];
        private readonly Vector4[] _rgbIntrinsics = new Vector4[2];
        private readonly Matrix4x4[] _otherDepthFromThis = new Matrix4x4[2];
        private readonly Matrix4x4[] _rgbFromDepth = new Matrix4x4[2];
        private Vector2Int _cachedRgbResolution;
        private bool _hasPreviousDepthPose;
        private Pose _previousDepthPose;
        private long _previousDepthTimestampNanoseconds;
        private bool _processing;
        private long _accepted;
        private long _rejected;
        private long _ringBackpressure;
        private long _lastConsumedSequence;
        private GraphicsFence _workGraphFence;
        private bool _hasWorkGraphFence;

        /// <summary>
        /// The callback borrows the frame. Retain it in the callback before storing it.
        /// </summary>
        public event Action<NormalizedRigFrameLease> FrameReady;

        public bool IsProcessing => _processing;
        public bool HasFrame => _latestFrame != null && _latestFrame.IsValid;
        public DepthPreprocessDiagnosticSnapshot Diagnostics => new(_accepted, _rejected,
            _ringBackpressure, _coneLutSet?.Calibration.Epoch ?? 0u);

        public bool TryAcquireLatest(out NormalizedRigFrameLease frame)
        {
            if (_latestFrame == null || !_latestFrame.IsValid)
            {
                frame = null;
                return false;
            }
            frame = _latestFrame.Retain();
            return true;
        }

        public void StartProcessing(PrismRigCapture source = null)
        {
            if (_processing)
                return;
            rigCapture = source != null ? source : rigCapture;
            rigCapture ??= GetComponent<PrismRigCapture>();
            rigCapture ??= FindAnyObjectByType<PrismRigCapture>();
            if (rigCapture == null)
            {
                Logger.Error("Cone-PRISM depth preprocessing requires PrismRigCapture.");
                return;
            }

            coneLutCompute ??= Resources.Load<ComputeShader>("Prism/ConeLut");
            depthNormalizeCompute ??= Resources.Load<ComputeShader>("Prism/DepthNormalize");
            depthConsensusNormalBoundaryCompute ??=
                Resources.Load<ComputeShader>("Prism/DepthConsensusNormalBoundary");
            if (coneLutCompute == null || depthNormalizeCompute == null ||
                depthConsensusNormalBoundaryCompute == null)
            {
                Logger.Error("Cone-PRISM calibration/normalization compute resources are missing.");
                return;
            }

            _normalizeKernel = depthNormalizeCompute.FindKernel("NormalizeStereoDepth");
            _initializeSigmaKernel = depthConsensusNormalBoundaryCompute.FindKernel("InitializeRangeSigma");
            _clearHistogramKernel = depthConsensusNormalBoundaryCompute.FindKernel("ClearResidualHistogram");
            _consensusKernel = depthConsensusNormalBoundaryCompute.FindKernel("DepthConsensus");
            _solveSigmaKernel = depthConsensusNormalBoundaryCompute.FindKernel("SolveRangeSigma");
            _planeFitKernel = depthConsensusNormalBoundaryCompute.FindKernel("DepthPlaneFit");
            _boundaryKernel = depthConsensusNormalBoundaryCompute.FindKernel("BoundaryEvidence");
            _outputRing ??= new NormalizedDepthRing(outputRingSlots);
            EnsureStatisticsBuffers();
            _processing = true;
        }

        public void StopProcessing()
        {
            _processing = false;
            _hasWorkGraphFence = false;
            _lastConsumedSequence = 0L;
            _latestFrame?.Dispose();
            _latestFrame = null;
            _coneLutSet?.Retire();
            _coneLutSet = null;
            _outputRing?.Dispose();
            _outputRing = null;
            _residualHistogram?.Dispose();
            _rangeSigma?.Dispose();
            _residualHistogram = null;
            _rangeSigma = null;
            _rangeSigmaInitialized = false;
            _hasPreviousDepthPose = false;
            _previousDepthTimestampNanoseconds = 0L;
        }

        private void OnDestroy() => StopProcessing();

        /// <summary>
        /// Capture is a mailbox, never a reconstruction callback. The native camera
        /// callback only copies external textures into leased GPU rings and returns.
        /// One newest coherent frame is consumed here after the previous complete GPU
        /// work graph has retired, preventing unbounded command-queue growth.
        /// </summary>
        private void Update()
        {
            if (!_processing || !WorkGraphFencePassed() || rigCapture == null ||
                !rigCapture.TryAcquireLatest(out StereoRigFrameLease source))
                return;
            using (source)
            {
                if (source.Sequence <= _lastConsumedSequence)
                    return;
                _lastConsumedSequence = source.Sequence;
                ProcessRigFrame(source);
            }
        }

        private void ProcessRigFrame(StereoRigFrameLease source)
        {
            if (!_processing || source == null || !source.IsValid)
            {
                _rejected++;
                return;
            }

            if (!EnsureCalibration(source))
            {
                _rejected++;
                return;
            }

            Vector2 nearFar = source.DepthNearFar;

            using ConeLutLease currentLuts = _coneLutSet.Acquire();
            if (!_outputRing.TryBegin(source, currentLuts,
                    out NormalizedRigFrameLease normalized))
            {
                _ringBackpressure++;
                _rejected++;
                return;
            }

            try
            {
                Vector2Int resolution = source.DepthResolution;
                depthNormalizeCompute.SetInts(ResolutionId, resolution.x, resolution.y);
                depthNormalizeCompute.SetVector(NearFarId, nearFar);
                depthNormalizeCompute.SetTexture(_normalizeKernel, RawDepthId,
                    source.DepthLeft.Texture);
                depthNormalizeCompute.SetTexture(_normalizeKernel, RayLeftId,
                    currentLuts.DepthLeft.CenterRaySolidAngle);
                depthNormalizeCompute.SetTexture(_normalizeKernel, RayRightId,
                    currentLuts.DepthRight.CenterRaySolidAngle);
                depthNormalizeCompute.SetTexture(_normalizeKernel, MetricDepthId,
                    normalized.MetricDepth);
                depthNormalizeCompute.SetTexture(_normalizeKernel, FlagsId,
                    normalized.Flags);
                depthNormalizeCompute.Dispatch(_normalizeKernel,
                    CeilDiv(resolution.x, 8), CeilDiv(resolution.y, 8), 2);

                DispatchConsensusNormalsAndBoundaries(source, normalized, currentLuts,
                    resolution);
                normalized.CommitGpuWrite();

                NormalizedRigFrameLease previous = _latestFrame;
                _latestFrame = normalized;
                previous?.Dispose();
                _accepted++;
                FrameReady?.Invoke(normalized);
                MarkWorkGraphSubmitted();
            }
            catch (Exception exception)
            {
                normalized.Dispose();
                _rejected++;
                Logger.Error($"Cone-PRISM depth normalization failed: {exception.Message}");
            }
        }

        private bool EnsureCalibration(StereoRigFrameLease source)
        {
            if (_coneLutSet != null && _coneLutSet.Calibration.IsCompatible(source))
                return true;
            if (!RigCalibration.TryCreate(source, out RigCalibration calibration))
                return false;

            RigConeLutSet replacement;
            try
            {
                replacement = RigConeLutSet.Create(coneLutCompute, calibration);
            }
            catch (Exception exception)
            {
                Logger.Error($"Cone-PRISM cone LUT build failed: {exception.Message}");
                return false;
            }

            RigConeLutSet previous = _coneLutSet;
            _coneLutSet = replacement;
            previous?.Retire();
            _rangeSigmaInitialized = false;
            CacheCalibrationUniforms(source);
            _hasPreviousDepthPose = false;
            Logger.Info($"Cone-PRISM cone LUT epoch {calibration.Epoch} ready: " +
                        $"RGB={calibration.RgbLeft.Resolution.x}x{calibration.RgbLeft.Resolution.y}, " +
                        $"depth={calibration.DepthLeft.Resolution.x}x{calibration.DepthLeft.Resolution.y}");
            return true;
        }

        private bool WorkGraphFencePassed()
        {
            if (!_hasWorkGraphFence) return true;
            try
            {
                if (!_workGraphFence.passed) return false;
            }
            catch (Exception) { }
            _hasWorkGraphFence = false;
            return true;
        }

        private void MarkWorkGraphSubmitted()
        {
            try
            {
                _workGraphFence = Graphics.CreateGraphicsFence(
                    GraphicsFenceType.AsyncQueueSynchronisation,
                    SynchronisationStageFlags.AllGPUOperations);
                _hasWorkGraphFence = true;
            }
            catch (Exception)
            {
                _hasWorkGraphFence = false;
            }
        }

        private static int CeilDiv(int value, int divisor) =>
            Math.Max(1, (value + divisor - 1) / divisor);

        private void DispatchConsensusNormalsAndBoundaries(StereoRigFrameLease source,
            NormalizedRigFrameLease output, ConeLutLease luts, Vector2Int depthResolution)
        {
            EnsureStatisticsBuffers();
            ComputeShader compute = depthConsensusNormalBoundaryCompute;
            ComputeMotionUncertainty(source, out float motionTranslationSigma,
                out float motionAngularSigma);

            compute.SetInts(DepthResolutionId, depthResolution.x, depthResolution.y);
            compute.SetInts(RgbResolutionId, _cachedRgbResolution.x,
                _cachedRgbResolution.y);
            compute.SetFloat(BootstrapSigmaId, bootstrapSigmaMeters);
            compute.SetFloat(BootstrapSigmaSlopeId, bootstrapSigmaSlope);
            compute.SetFloat(SigmaEmaAlphaId, sigmaEmaAlpha);
            compute.SetFloat(PoseTranslationSigmaId, poseTranslationSigmaMeters);
            compute.SetFloat(PoseAngularSigmaId,
                poseAngularSigmaDegrees * Mathf.Deg2Rad);
            compute.SetFloat(CalibrationAngularSigmaId,
                calibrationAngularSigmaDegrees * Mathf.Deg2Rad);
            compute.SetFloat(MotionTranslationSigmaId, motionTranslationSigma);
            compute.SetFloat(MotionAngularSigmaId, motionAngularSigma);
            compute.SetBuffer(_initializeSigmaKernel, RangeSigmaId, _rangeSigma);
            compute.SetBuffer(_clearHistogramKernel, ResidualHistogramId,
                _residualHistogram);
            compute.SetBuffer(_consensusKernel, ResidualHistogramId,
                _residualHistogram);
            compute.SetBuffer(_consensusKernel, RangeSigmaId, _rangeSigma);
            compute.SetBuffer(_solveSigmaKernel, ResidualHistogramId,
                _residualHistogram);
            compute.SetBuffer(_solveSigmaKernel, RangeSigmaId, _rangeSigma);

            BindSharedTextures(compute, _consensusKernel, source, output, luts);
            BindSharedTextures(compute, _planeFitKernel, source, output, luts);
            BindSharedTextures(compute, _boundaryKernel, source, output, luts);

            if (!_rangeSigmaInitialized)
            {
                compute.Dispatch(_initializeSigmaKernel, 1, 1, 1);
                _rangeSigmaInitialized = true;
            }
            compute.Dispatch(_clearHistogramKernel, 12, 1, 1);
            compute.Dispatch(_consensusKernel, CeilDiv(depthResolution.x, 8),
                CeilDiv(depthResolution.y, 8), 2);
            compute.Dispatch(_solveSigmaKernel, 6, 1, 1);
            compute.Dispatch(_planeFitKernel, CeilDiv(depthResolution.x, 8),
                CeilDiv(depthResolution.y, 8), 2);
            compute.Dispatch(_boundaryKernel, CeilDiv(depthResolution.x, 8),
                CeilDiv(depthResolution.y, 8), 2);
        }

        private static void BindSharedTextures(ComputeShader compute, int kernel,
            StereoRigFrameLease source, NormalizedRigFrameLease output,
            ConeLutLease luts)
        {
            compute.SetTexture(kernel, MetricDepthInputId, output.MetricDepth);
            compute.SetTexture(kernel, FlagsRwId, output.Flags);
            compute.SetTexture(kernel, ConsensusDepthId, output.ConsensusDepth);
            compute.SetTexture(kernel, LocalNormalId, output.LocalNormal);
            compute.SetTexture(kernel, BoundaryEvidenceId, output.BoundaryEvidence);
            compute.SetTexture(kernel, RayLeftId, luts.DepthLeft.CenterRaySolidAngle);
            compute.SetTexture(kernel, RayRightId, luts.DepthRight.CenterRaySolidAngle);
            compute.SetTexture(kernel, RgbLeftId, source.RgbLeft.Texture);
            compute.SetTexture(kernel, RgbRightId, source.RgbRight.Texture);
        }

        private void EnsureStatisticsBuffers()
        {
            _residualHistogram ??= new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                6 * 128, sizeof(uint));
            _rangeSigma ??= new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                6, sizeof(float));
        }

        private static void FillIntrinsics(Vector4[] destination,
            RigIntrinsics left, RigIntrinsics right)
        {
            destination[0] = new Vector4(left.FocalLength.x, left.FocalLength.y,
                left.PrincipalPoint.x, left.PrincipalPoint.y);
            destination[1] = new Vector4(right.FocalLength.x, right.FocalLength.y,
                right.PrincipalPoint.x, right.PrincipalPoint.y);
        }

        private void CacheCalibrationUniforms(StereoRigFrameLease source)
        {
            _cachedRgbResolution = source.RgbLeft.Resolution;
            FillIntrinsics(_depthIntrinsics, source.DepthLeft.Intrinsics,
                source.DepthRight.Intrinsics);
            FillIntrinsics(_rgbIntrinsics, source.RgbLeft.Intrinsics,
                source.RgbRight.Intrinsics);
            Matrix4x4 leftDepthFromRight = PoseMatrix(
                source.Extrinsics.LeftDepthFromRightDepth);
            _otherDepthFromThis[0] = leftDepthFromRight.inverse;
            _otherDepthFromThis[1] = leftDepthFromRight;
            _rgbFromDepth[0] = PoseMatrix(source.Extrinsics.LeftRgbFromLeftDepth);
            _rgbFromDepth[1] = PoseMatrix(source.Extrinsics.RightRgbFromRightDepth);

            ComputeShader compute = depthConsensusNormalBoundaryCompute;
            compute.SetVectorArray(DepthIntrinsicsId, _depthIntrinsics);
            compute.SetVectorArray(RgbIntrinsicsId, _rgbIntrinsics);
            compute.SetMatrixArray(OtherDepthFromThisId, _otherDepthFromThis);
            compute.SetMatrixArray(RgbFromDepthId, _rgbFromDepth);
        }

        private void ComputeMotionUncertainty(StereoRigFrameLease source,
            out float translationSigma, out float angularSigma)
        {
            translationSigma = 0f;
            angularSigma = 0f;
            Pose pose = source.DepthLeft.WorldFromCamera;
            long timestamp = source.DepthLeft.Timestamp.UnixNanoseconds;
            if (_hasPreviousDepthPose)
            {
                double dt = (timestamp - _previousDepthTimestampNanoseconds) * 1e-9;
                if (dt > 1e-4 && dt < 0.5)
                {
                    float linearSpeed = Vector3.Distance(
                        pose.position, _previousDepthPose.position) / (float)dt;
                    float angularSpeed = Quaternion.Angle(
                        pose.rotation, _previousDepthPose.rotation) *
                        Mathf.Deg2Rad / (float)dt;
                    double clockSigma = Math.Max(0L,
                        source.Health.ClockUncertaintyNanoseconds) * 1e-9;
                    double aperture = Math.Max(0.0,
                        depthTemporalApertureMilliseconds) * 1e-3 / Math.Sqrt(12.0);
                    float temporalSigma = (float)Math.Sqrt(
                        aperture * aperture + clockSigma * clockSigma);
                    translationSigma = Mathf.Min(0.08f,
                        linearSpeed * temporalSigma);
                    angularSigma = Mathf.Min(5f * Mathf.Deg2Rad,
                        angularSpeed * temporalSigma);
                }
            }
            _previousDepthPose = pose;
            _previousDepthTimestampNanoseconds = timestamp;
            _hasPreviousDepthPose = true;
        }

        private static Matrix4x4 PoseMatrix(Pose pose) =>
            Matrix4x4.TRS(pose.position, pose.rotation, Vector3.one);
    }
}
