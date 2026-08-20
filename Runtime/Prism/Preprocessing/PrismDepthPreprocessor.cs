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
        private bool _processing;
        private long _accepted;
        private long _rejected;
        private long _ringBackpressure;

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
            rigCapture.FrameReady += OnRigFrame;
            _processing = true;
        }

        public void StopProcessing()
        {
            if (rigCapture != null && _processing)
                rigCapture.FrameReady -= OnRigFrame;
            _processing = false;
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
        }

        private void OnDestroy() => StopProcessing();

        private void OnRigFrame(StereoRigFrameLease source)
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

            Vector2 nearFar = source.DepthLeft.DepthNearFar;
            Vector2 rightNearFar = source.DepthRight.DepthNearFar;
            if (!Approximately(nearFar, rightNearFar) ||
                source.DepthLeft.Resolution != source.DepthRight.Resolution)
            {
                _rejected++;
                return;
            }

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
                Vector2Int resolution = source.DepthLeft.Resolution;
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
            Logger.Info($"Cone-PRISM cone LUT epoch {calibration.Epoch} ready: " +
                        $"RGB={calibration.RgbLeft.Resolution.x}x{calibration.RgbLeft.Resolution.y}, " +
                        $"depth={calibration.DepthLeft.Resolution.x}x{calibration.DepthLeft.Resolution.y}");
            return true;
        }

        private static bool Approximately(Vector2 a, Vector2 b) =>
            Mathf.Abs(a.x - b.x) <= 1e-5f && Mathf.Abs(a.y - b.y) <= 1e-4f;

        private static int CeilDiv(int value, int divisor) =>
            Math.Max(1, (value + divisor - 1) / divisor);

        private void DispatchConsensusNormalsAndBoundaries(StereoRigFrameLease source,
            NormalizedRigFrameLease output, ConeLutLease luts, Vector2Int depthResolution)
        {
            EnsureStatisticsBuffers();
            ComputeShader compute = depthConsensusNormalBoundaryCompute;
            Vector2Int rgbResolution = source.RgbLeft.Resolution;

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

            compute.SetInts(DepthResolutionId, depthResolution.x, depthResolution.y);
            compute.SetInts(RgbResolutionId, rgbResolution.x, rgbResolution.y);
            compute.SetVectorArray(DepthIntrinsicsId, _depthIntrinsics);
            compute.SetVectorArray(RgbIntrinsicsId, _rgbIntrinsics);
            compute.SetMatrixArray(OtherDepthFromThisId, _otherDepthFromThis);
            compute.SetMatrixArray(RgbFromDepthId, _rgbFromDepth);
            compute.SetFloat(BootstrapSigmaId, bootstrapSigmaMeters);
            compute.SetFloat(BootstrapSigmaSlopeId, bootstrapSigmaSlope);
            compute.SetFloat(SigmaEmaAlphaId, sigmaEmaAlpha);
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

        private static Matrix4x4 PoseMatrix(Pose pose) =>
            Matrix4x4.TRS(pose.position, pose.rotation, Vector3.one);
    }
}
