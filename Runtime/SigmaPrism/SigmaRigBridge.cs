using System;
using Meta.XR;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Genesis.RoomScan.SigmaPrism
{
    /// <summary>
    /// Coherent four-stream Quest capture. PCA images stay borrowed metadata until
    /// one requested owned depth snapshot has a valid timestamp match. Exactly that
    /// RGB pair is then copied GPU-to-GPU and the coherent frame remains held until
    /// the renderer accepts it. The four sensor leaves remain independent query input.
    /// </summary>
    [DefaultExecutionOrder(-30)]
    public sealed class SigmaRigBridge : MonoBehaviour
    {
        [Header("Sources")]
        [SerializeField] private PassthroughCameraAccess leftCameraAccess;
        [SerializeField] private PassthroughCameraAccess rightCameraAccess;
        [SerializeField] private DepthCapture depthCapture;

        [Header("RGB capture")]
        [SerializeField] private Vector2Int requestedResolution = new(640, 480);
        [SerializeField, Range(1, 90)] private int maxFramerate = 30;

        [Header("Fail-closed pairing")]
        [SerializeField, Min(1f)] private float maxRgbDeltaMilliseconds = 20f;
        [SerializeField, Min(1f)] private float maxRgbDepthDeltaMilliseconds = 35f;
        [SerializeField, Min(0.1f)] private float maxClockUncertaintyMilliseconds = 5f;
        [SerializeField, Range(5, 24)] private int gpuRingSlots = 8;

        private readonly RigIntrinsics[] _sessionIntrinsics = new RigIntrinsics[4];
        private readonly bool[] _hasSessionIntrinsics = new bool[4];
        private GpuTextureRing _rgbLeftRing;
        private GpuTextureRing _rgbRightRing;
        private GpuTextureRing _depthRing;
        private RigClockMapper _clockMapper;
        private StereoRigFrameLease _latestFrame;
        private GpuTextureLease _pendingDepthOwner;
        private GpuImageView _pendingDepthLeft;
        private GpuImageView _pendingDepthRight;
        private Vector2Int _pendingDepthResolution;
        private Vector2 _pendingDepthNearFar;
        private BorrowedRgbDescriptor _latestRgbLeft;
        private BorrowedRgbDescriptor _latestRgbRight;
        private bool _captureRequested;
        private bool _ownsLeft;
        private bool _ownsRight;
        private bool _subscribedDepth;
        private uint _calibrationEpoch;
        private ulong _combinedCalibrationSignature;
        private long _lastRgbLeftTimestampNs;
        private long _lastRgbRightTimestampNs;
        private long _rgbLeftSequence;
        private long _rgbRightSequence;
        private long _coherentSequence;
        private long _acceptedFrames;
        private long _rejectedSamples;
        private long _lastRgbDeltaNanoseconds;
        private long _lastRgbDepthDeltaNanoseconds;
        private RigFrameRejectionReason _lastPairingRejection;
        private float _lastDiagnosticLog;
        private long _localRejections;
        private RigFrameRejectionReason _lastLocalRejection;

        public bool IsCapturing => _captureRequested;
        public bool HasCoherentFrame => _latestFrame != null && _latestFrame.IsValid;
        public uint CalibrationEpoch => _calibrationEpoch;
        public ulong CombinedCalibrationSignature => _combinedCalibrationSignature;
        public RigFrameRejectionReason LastLocalRejection => _lastLocalRejection;
        public long LocalRejectionCount => _localRejections;
        public RigCaptureDiagnosticSnapshot PairingDiagnostics => new(
            _acceptedFrames, _rejectedSamples, _lastPairingRejection,
            _lastRgbDeltaNanoseconds, _lastRgbDepthDeltaNanoseconds);

        public bool TryAcquireLatest(out StereoRigFrameLease frame)
        {
            if (_latestFrame == null || !_latestFrame.IsValid)
            {
                frame = null;
                return false;
            }
            frame = _latestFrame.Retain();
            return true;
        }

        /// <summary>
        /// Releases only the bridge's ready/held owner after the renderer has created
        /// and published a retained prediction lease for this exact source sequence.
        /// </summary>
        internal bool AcknowledgeConsumed(long sequence)
        {
            if (_latestFrame == null || _latestFrame.Sequence != sequence)
                return false;
            StereoRigFrameLease consumed = _latestFrame;
            _latestFrame = null;
            consumed.Dispose();
            ArmNextObservation();
            return true;
        }

        public void StartCapture()
        {
            if (_captureRequested)
                return;
            _captureRequested = true;
            EnsureRuntimeObjects();

            if (!_clockMapper.TryCaptureAnchor())
                ReportLocalRejection(RigFrameRejectionReason.ClockMappingUncertain);

            leftCameraAccess = ResolveEye(leftCameraAccess,
                PassthroughCameraAccess.CameraPositionType.Left, ref _ownsLeft);
            rightCameraAccess = ResolveEye(rightCameraAccess,
                PassthroughCameraAccess.CameraPositionType.Right, ref _ownsRight);
            depthCapture ??= GetComponent<DepthCapture>();
            depthCapture ??= FindAnyObjectByType<DepthCapture>();
            if (depthCapture != null && !_subscribedDepth)
            {
                depthCapture.RawStereoFrameReceived += OnRawStereoDepthFrame;
                _subscribedDepth = true;
            }
            else if (depthCapture == null)
            {
                ReportLocalRejection(RigFrameRejectionReason.MissingTexture);
            }

            ArmNextObservation();
            Logger.Info("Sigma-PRISM-16 rig capture requested: demand-driven " +
                        "RGB-L/R + DEPTH-L/R owned snapshots");
        }

        public void StopCapture()
        {
            _captureRequested = false;
            if (depthCapture != null && _subscribedDepth)
            {
                depthCapture.RawStereoFrameReceived -= OnRawStereoDepthFrame;
                _subscribedDepth = false;
            }
            DiscardPendingDepth(RigFrameRejectionReason.None);
            _latestFrame?.Dispose();
            _latestFrame = null;
            _latestRgbLeft = default;
            _latestRgbRight = default;

            // Adopted Building-Block/provider cameras are shared infrastructure.
            if (_ownsLeft && leftCameraAccess != null)
                leftCameraAccess.enabled = false;
            if (_ownsRight && rightCameraAccess != null)
                rightCameraAccess.enabled = false;
        }

        private void LateUpdate()
        {
            if (!_captureRequested)
                return;

            CaptureRgbMetadata(leftCameraAccess, RigEye.Left,
                ref _lastRgbLeftTimestampNs, ref _rgbLeftSequence,
                ref _latestRgbLeft);
            CaptureRgbMetadata(rightCameraAccess, RigEye.Right,
                ref _lastRgbRightTimestampNs, ref _rgbRightSequence,
                ref _latestRgbRight);
            TryPublishPendingDepth();
            ArmNextObservation();

            if (Time.unscaledTime - _lastDiagnosticLog >= 5f)
            {
                _lastDiagnosticLog = Time.unscaledTime;
                RigCaptureDiagnosticSnapshot diagnostics = PairingDiagnostics;
                Logger.Info($"Sigma-PRISM-16 capture: coherent={diagnostics.AcceptedFrames}, " +
                    $"pairReject={diagnostics.RejectedSamples}, localReject={_localRejections}, " +
                    $"held={_latestFrame != null}, depthReady={_pendingDepthOwner != null}, " +
                    $"last={diagnostics.LastRejection | _lastLocalRejection}, " +
                    $"rgbDeltaMs={diagnostics.LastRgbDeltaNanoseconds / 1_000_000f:F2}, " +
                    $"rgbDepthDeltaMs={diagnostics.LastRgbDepthDeltaNanoseconds / 1_000_000f:F2}, " +
                    $"epoch={_calibrationEpoch}");
            }
        }

        private void OnDestroy()
        {
            StopCapture();
            _rgbLeftRing?.Dispose();
            _rgbRightRing?.Dispose();
            _depthRing?.Dispose();
            _rgbLeftRing = null;
            _rgbRightRing = null;
            _depthRing = null;
        }

        private void EnsureRuntimeObjects()
        {
            _rgbLeftRing ??= new GpuTextureRing("Sigma-PRISM-16 RGB Left",
                gpuRingSlots);
            _rgbRightRing ??= new GpuTextureRing("Sigma-PRISM-16 RGB Right",
                gpuRingSlots);
            _depthRing ??= new GpuTextureRing("Sigma-PRISM-16 Stereo Depth",
                gpuRingSlots, GraphicsFormat.R32_SFloat,
                GpuTextureCopyMode.ProjectionDepthArray);
            _clockMapper ??= RigClockMapper.CreateRuntime();
        }

        private void CaptureRgbMetadata(PassthroughCameraAccess access,
            RigEye eye, ref long lastTimestampNs, ref long sequence,
            ref BorrowedRgbDescriptor destination)
        {
            if (access == null || !access.IsPlaying || !access.IsUpdatedThisFrame)
                return;
            if (access.CurrentResolution != requestedResolution ||
                access.MaxFramerate != maxFramerate)
            {
                ReportLocalRejection(
                    RigFrameRejectionReason.CalibrationMismatch);
                return;
            }

            RigTimestamp timestamp;
            try
            {
                timestamp = RigTimestamp.FromUnixDateTime(access.Timestamp);
            }
            catch (Exception)
            {
                ReportLocalRejection(RigFrameRejectionReason.MissingTimestamp);
                return;
            }
            if (!timestamp.IsValid || timestamp.SourceNanoseconds == lastTimestampNs)
                return;

            Texture source = access.GetTexture();
            int signatureIndex = eye == RigEye.Left ? 0 : 1;
            if (!_hasSessionIntrinsics[signatureIndex] &&
                !FreezeSessionIntrinsics(signatureIndex,
                    RigCalibrationMath.FromPassthrough(access)))
            {
                ReportLocalRejection(RigFrameRejectionReason.InvalidIntrinsics);
                return;
            }
            RigIntrinsics intrinsics = _sessionIntrinsics[signatureIndex];
            if (source == null || source.width != intrinsics.ImageResolution.x ||
                source.height != intrinsics.ImageResolution.y)
            {
                ReportLocalRejection(RigFrameRejectionReason.CalibrationMismatch);
                return;
            }

            long sampleSequence = ++sequence;
            var descriptor = new BorrowedRgbDescriptor(source, eye,
                sampleSequence, timestamp, access.GetCameraPose(), intrinsics);
            if (!descriptor.IsValid)
            {
                ReportLocalRejection(RigFrameRejectionReason.MissingPose |
                                     RigFrameRejectionReason.MissingTexture);
                return;
            }
            destination = descriptor;
            lastTimestampNs = timestamp.SourceNanoseconds;
        }

        private void OnRawStereoDepthFrame(RawStereoDepthFrame raw)
        {
            if (!_captureRequested)
                return;
            if (!raw.IsValid)
            {
                ReportLocalRejection(RigFrameRejectionReason.MissingTexture |
                                     RigFrameRejectionReason.MissingTimestamp);
                ArmNextObservation();
                return;
            }
            if (_pendingDepthOwner != null || _latestFrame != null)
            {
                ReportLocalRejection(RigFrameRejectionReason.Stale);
                return;
            }
            if (!_clockMapper.IsValid && !_clockMapper.TryCaptureAnchor())
            {
                ReportLocalRejection(RigFrameRejectionReason.ClockMappingUncertain);
                ArmNextObservation();
                return;
            }
            if (!_clockMapper.TryMapXrTimestamp(raw.TimestampNanoseconds,
                    out RigTimestamp timestamp))
            {
                ReportLocalRejection(RigFrameRejectionReason.MissingTimestamp |
                                     RigFrameRejectionReason.ClockMappingUncertain);
                ArmNextObservation();
                return;
            }

            Vector2Int resolution = new(raw.StereoTexture.width,
                raw.StereoTexture.height);
            if (!_hasSessionIntrinsics[2] &&
                !FreezeSessionIntrinsics(2,
                    RigCalibrationMath.FromDepthFov(raw.LeftFov, resolution)))
            {
                ReportLocalRejection(RigFrameRejectionReason.InvalidIntrinsics);
                ArmNextObservation();
                return;
            }
            if (!_hasSessionIntrinsics[3] &&
                !FreezeSessionIntrinsics(3,
                    RigCalibrationMath.FromDepthFov(raw.RightFov, resolution)))
            {
                ReportLocalRejection(RigFrameRejectionReason.InvalidIntrinsics);
                ArmNextObservation();
                return;
            }
            RigIntrinsics leftIntrinsics = _sessionIntrinsics[2];
            RigIntrinsics rightIntrinsics = _sessionIntrinsics[3];
            if (resolution != leftIntrinsics.ImageResolution ||
                resolution != rightIntrinsics.ImageResolution)
            {
                ReportLocalRejection(RigFrameRejectionReason.CalibrationMismatch);
                ArmNextObservation();
                return;
            }
            if (_calibrationEpoch == 0u)
            {
                ArmNextObservation();
                return;
            }

            if (!_depthRing.TryCopy(raw.StereoTexture,
                    out GpuTextureLease lease,
                    out RigFrameRejectionReason rejection))
            {
                ReportLocalRejection(rejection);
                ArmNextObservation();
                return;
            }

            var left = new GpuImageView(RigStreamKind.Depth, RigEye.Left,
                lease.Texture, 0, raw.Sequence, timestamp, raw.WorldFromLeft,
                leftIntrinsics, lease.GraphicsFormat,
                RigDepthEncoding.ProjectionDepth01, raw.NearFar);
            var right = new GpuImageView(RigStreamKind.Depth, RigEye.Right,
                lease.Texture, 1, raw.Sequence, timestamp, raw.WorldFromRight,
                rightIntrinsics, lease.GraphicsFormat,
                RigDepthEncoding.ProjectionDepth01, raw.NearFar);
            if (!left.IsValid || !right.IsValid)
            {
                lease.Dispose();
                ReportLocalRejection(
                    RigFrameRejectionReason.StereoDepthContractMismatch);
                ArmNextObservation();
                return;
            }

            _pendingDepthOwner = lease;
            _pendingDepthLeft = left;
            _pendingDepthRight = right;
            _pendingDepthResolution = resolution;
            _pendingDepthNearFar = raw.NearFar;
        }

        private void TryPublishPendingDepth()
        {
            if (_pendingDepthOwner == null || _latestFrame != null ||
                !_latestRgbLeft.IsValid || !_latestRgbRight.IsValid)
                return;

            RigLatestSnapshotMatchResult match = RigLatestSnapshotMatcher.Match(
                _latestRgbLeft.Timestamp, _latestRgbRight.Timestamp,
                _pendingDepthLeft.Timestamp,
                MillisecondsToNanoseconds(maxRgbDeltaMilliseconds),
                MillisecondsToNanoseconds(maxRgbDepthDeltaMilliseconds),
                MillisecondsToNanoseconds(maxClockUncertaintyMilliseconds));
            _lastRgbDeltaNanoseconds = match.RgbDeltaNanoseconds;
            _lastRgbDepthDeltaNanoseconds = match.RgbDepthDeltaNanoseconds;
            _lastPairingRejection = match.Rejection;
            if (match.Disposition == RigLatestSnapshotMatch.Waiting)
                return;
            if (match.Disposition == RigLatestSnapshotMatch.DiscardDepth)
            {
                DiscardPendingDepth(match.Rejection);
                ArmNextObservation();
                return;
            }

            if (!GpuTextureRing.TryCopyPair(_rgbLeftRing,
                    _latestRgbLeft.Texture, _rgbRightRing,
                    _latestRgbRight.Texture, out GpuTextureLease leftLease,
                    out GpuTextureLease rightLease,
                    out RigFrameRejectionReason rejection))
            {
                ReportLocalRejection(rejection);
                if ((rejection & RigFrameRejectionReason.RingExhausted) == 0)
                {
                    DiscardPendingDepth(rejection);
                    ArmNextObservation();
                }
                return;
            }

            GpuTextureLease depthLease = _pendingDepthOwner;
            try
            {
                GpuImageView rgbLeft = _latestRgbLeft.ToOwned(leftLease);
                GpuImageView rgbRight = _latestRgbRight.ToOwned(rightLease);
                var health = new RigPairingHealth(match.RgbDeltaNanoseconds,
                    match.RgbDepthDeltaNanoseconds,
                    match.ClockUncertaintyNanoseconds);
                _latestFrame = new StereoRigFrameLease(++_coherentSequence,
                    _calibrationEpoch, leftLease, rgbLeft, rightLease, rgbRight,
                    depthLease, _pendingDepthLeft, _pendingDepthRight,
                    _pendingDepthResolution, _pendingDepthNearFar, health);
                leftLease = null;
                rightLease = null;
                depthLease = null;
                _pendingDepthOwner = null;
                ClearPendingDepthMetadata();
                _acceptedFrames++;
                _lastPairingRejection = RigFrameRejectionReason.None;
            }
            finally
            {
                leftLease?.Dispose();
                rightLease?.Dispose();
                depthLease?.Dispose();
            }
        }

        private void ArmNextObservation()
        {
            if (!_captureRequested || _latestFrame != null ||
                _pendingDepthOwner != null || depthCapture == null)
                return;
            depthCapture.RequestNextDepthFrame();
        }

        private void DiscardPendingDepth(RigFrameRejectionReason reason)
        {
            _pendingDepthOwner?.Dispose();
            _pendingDepthOwner = null;
            ClearPendingDepthMetadata();
            if (reason == RigFrameRejectionReason.None)
                return;
            _rejectedSamples++;
            _lastPairingRejection = reason;
        }

        private void ClearPendingDepthMetadata()
        {
            _pendingDepthLeft = default;
            _pendingDepthRight = default;
            _pendingDepthResolution = default;
            _pendingDepthNearFar = default;
        }

        private bool FreezeSessionIntrinsics(int index, RigIntrinsics intrinsics)
        {
            if ((uint)index >= (uint)_sessionIntrinsics.Length || !intrinsics.IsValid)
                return false;
            if (_hasSessionIntrinsics[index])
                return _sessionIntrinsics[index].Signature == intrinsics.Signature;
            _sessionIntrinsics[index] = intrinsics;
            _hasSessionIntrinsics[index] = true;
            for (int i = 0; i < _hasSessionIntrinsics.Length; i++)
            {
                if (!_hasSessionIntrinsics[i])
                    return true;
            }

            _combinedCalibrationSignature = RigCalibrationMath.CombineSignatures(
                _sessionIntrinsics[0].Signature, _sessionIntrinsics[1].Signature,
                _sessionIntrinsics[2].Signature, _sessionIntrinsics[3].Signature);
            _calibrationEpoch = 1u;
            Logger.Info("Sigma-PRISM-16 immutable rig calibration frozen, " +
                        $"signature=0x{_combinedCalibrationSignature:x16}, " +
                        $"rgbLeft={_sessionIntrinsics[0].ImageResolution.x}x" +
                        $"{_sessionIntrinsics[0].ImageResolution.y}@" +
                        $"{maxFramerate}, rgbRight=" +
                        $"{_sessionIntrinsics[1].ImageResolution.x}x" +
                        $"{_sessionIntrinsics[1].ImageResolution.y}@" +
                        $"{maxFramerate}, depthLeft=" +
                        $"{_sessionIntrinsics[2].ImageResolution.x}x" +
                        $"{_sessionIntrinsics[2].ImageResolution.y}, " +
                        $"depthRight={_sessionIntrinsics[3].ImageResolution.x}x" +
                        $"{_sessionIntrinsics[3].ImageResolution.y}");
            return true;
        }

        private PassthroughCameraAccess ResolveEye(PassthroughCameraAccess assigned,
            PassthroughCameraAccess.CameraPositionType eye, ref bool owns)
        {
            if (assigned != null && assigned.CameraPosition != eye)
            {
                Logger.Error($"Sigma-PRISM-16 {eye} PCA reference points to " +
                             $"{assigned.CameraPosition}; ignoring it.");
                assigned = null;
                owns = false;
            }

            if (assigned == null)
            {
                PassthroughCameraAccess[] all =
                    FindObjectsByType<PassthroughCameraAccess>(
                        FindObjectsInactive.Include);
                foreach (PassthroughCameraAccess candidate in all)
                {
                    if (candidate.CameraPosition != eye)
                        continue;
                    if (assigned != null)
                    {
                        Logger.Error($"Sigma-PRISM-16 found duplicate {eye} " +
                                     "PassthroughCameraAccess instances; capture " +
                                     "fails closed until the scene is fixed.");
                        return null;
                    }
                    assigned = candidate;
                    owns = false;
                }
            }

            if (assigned == null)
            {
                var host = new GameObject($"[Sigma-PRISM-16] RGB {eye}");
                host.transform.SetParent(transform, false);
                host.SetActive(false);
                assigned = host.AddComponent<PassthroughCameraAccess>();
                assigned.enabled = false;
                assigned.CameraPosition = eye;
                assigned.RequestedResolution = requestedResolution;
                assigned.MaxFramerate = maxFramerate;
                host.SetActive(true);
                assigned.enabled = true;
                owns = true;
                return assigned;
            }

            if (!assigned.isActiveAndEnabled)
            {
                assigned.enabled = false;
                assigned.CameraPosition = eye;
                assigned.RequestedResolution = requestedResolution;
                assigned.MaxFramerate = maxFramerate;
                assigned.enabled = true;
            }
            return assigned;
        }

        private void ReportLocalRejection(RigFrameRejectionReason reason)
        {
            _localRejections++;
            _lastLocalRejection = reason;
        }

        private static long MillisecondsToNanoseconds(float milliseconds) =>
            (long)Math.Round(milliseconds * 1_000_000.0);

        private readonly struct BorrowedRgbDescriptor
        {
            internal BorrowedRgbDescriptor(Texture texture, RigEye eye,
                long sourceSequence, RigTimestamp timestamp,
                Pose worldFromCamera, RigIntrinsics intrinsics)
            {
                Texture = texture;
                Eye = eye;
                SourceSequence = sourceSequence;
                Timestamp = timestamp;
                WorldFromCamera = worldFromCamera;
                Intrinsics = intrinsics;
            }

            internal Texture Texture { get; }
            internal RigEye Eye { get; }
            internal long SourceSequence { get; }
            internal RigTimestamp Timestamp { get; }
            internal Pose WorldFromCamera { get; }
            internal RigIntrinsics Intrinsics { get; }
            internal bool IsValid => Texture != null && Timestamp.IsValid &&
                Intrinsics.IsValid && IsFinite(WorldFromCamera);

            internal GpuImageView ToOwned(GpuTextureLease lease) => new(
                RigStreamKind.Rgb, Eye, lease.Texture, 0, SourceSequence,
                Timestamp, WorldFromCamera, Intrinsics, lease.GraphicsFormat);

            private static bool IsFinite(Pose pose) =>
                float.IsFinite(pose.position.x) &&
                float.IsFinite(pose.position.y) &&
                float.IsFinite(pose.position.z) &&
                float.IsFinite(pose.rotation.x) &&
                float.IsFinite(pose.rotation.y) &&
                float.IsFinite(pose.rotation.z) &&
                float.IsFinite(pose.rotation.w) &&
                pose.rotation.x * pose.rotation.x +
                pose.rotation.y * pose.rotation.y +
                pose.rotation.z * pose.rotation.z +
                pose.rotation.w * pose.rotation.w > 0.5f;
        }
    }
}
