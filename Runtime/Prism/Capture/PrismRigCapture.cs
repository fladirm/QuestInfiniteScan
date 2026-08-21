using System;
using Meta.XR;
using UnityEngine;

namespace Genesis.RoomScan.Prism
{
    /// <summary>
    /// Coherent four-stream Quest capture. Native/external images are copied only
    /// GPU-to-GPU into leased rings; metadata pairing fails closed by timestamp,
    /// calibration epoch, pose validity, and eye identity.
    /// </summary>
    [DefaultExecutionOrder(-30)]
    public sealed class PrismRigCapture : MonoBehaviour
    {
        [Header("Sources")]
        [SerializeField] private PassthroughCameraAccess leftCameraAccess;
        [SerializeField] private PassthroughCameraAccess rightCameraAccess;
        [SerializeField] private DepthCapture depthCapture;

        [Header("RGB capture")]
        [SerializeField] private Vector2Int requestedResolution = new(1280, 960);
        [SerializeField, Range(1, 90)] private int maxFramerate = 30;

        [Header("Fail-closed pairing")]
        [SerializeField, Min(1f)] private float maxRgbDeltaMilliseconds = 20f;
        [SerializeField, Min(1f)] private float maxRgbDepthDeltaMilliseconds = 35f;
        [SerializeField, Min(0.1f)] private float maxClockUncertaintyMilliseconds = 5f;
        [SerializeField, Range(5, 24)] private int gpuRingSlots = 8;
        [SerializeField, Range(3, 24)] private int metadataQueueCapacity = 10;

        private readonly RigIntrinsics[] _sessionIntrinsics = new RigIntrinsics[4];
        private readonly bool[] _hasSessionIntrinsics = new bool[4];
        private GpuTextureRing _rgbLeftRing;
        private GpuTextureRing _rgbRightRing;
        private GpuTextureRing _depthRing;
        private RigFrameSynchronizer _synchronizer;
        private RigClockMapper _clockMapper;
        private StereoRigFrameLease _latestFrame;
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
        private float _lastDiagnosticLog;
        private long _localRejections;
        private RigFrameRejectionReason _lastLocalRejection;

        public bool IsCapturing => _captureRequested;
        public bool HasCoherentFrame => _latestFrame != null && _latestFrame.IsValid;
        public uint CalibrationEpoch => _calibrationEpoch;
        public ulong CombinedCalibrationSignature => _combinedCalibrationSignature;
        public RigFrameRejectionReason LastLocalRejection => _lastLocalRejection;
        public long LocalRejectionCount => _localRejections;
        public RigCaptureDiagnosticSnapshot PairingDiagnostics =>
            _synchronizer != null ? _synchronizer.Diagnostics : default;

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

            Logger.Info("Cone-PRISM rig capture requested: RGB-L/R + DEPTH-L/R GPU rings");
        }

        public void StopCapture()
        {
            if (!_captureRequested && _synchronizer == null)
                return;
            _captureRequested = false;
            if (depthCapture != null && _subscribedDepth)
            {
                depthCapture.RawStereoFrameReceived -= OnRawStereoDepthFrame;
                _subscribedDepth = false;
            }
            _synchronizer?.Flush(RigFrameRejectionReason.Stale);
            _latestFrame?.Dispose();
            _latestFrame = null;

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

            CaptureRgb(leftCameraAccess, RigEye.Left, ref _lastRgbLeftTimestampNs,
                ref _rgbLeftSequence, _rgbLeftRing);
            CaptureRgb(rightCameraAccess, RigEye.Right, ref _lastRgbRightTimestampNs,
                ref _rgbRightSequence, _rgbRightRing);
            PublishAvailableFrames();

            if (Time.unscaledTime - _lastDiagnosticLog >= 5f)
            {
                _lastDiagnosticLog = Time.unscaledTime;
                RigCaptureDiagnosticSnapshot diagnostics = PairingDiagnostics;
                Logger.Info($"Cone-PRISM capture: coherent={diagnostics.AcceptedFrames}, " +
                    $"pairReject={diagnostics.RejectedSamples}, localReject={_localRejections}, " +
                    $"last={diagnostics.LastRejection | _lastLocalRejection}, " +
                    $"rgbDeltaMs={diagnostics.LastRgbDeltaNanoseconds / 1_000_000f:F2}, " +
                    $"rgbDepthDeltaMs={diagnostics.LastRgbDepthDeltaNanoseconds / 1_000_000f:F2}, " +
                    $"epoch={_calibrationEpoch}");
            }
        }

        private void OnDestroy()
        {
            StopCapture();
            _synchronizer?.Dispose();
            _rgbLeftRing?.Dispose();
            _rgbRightRing?.Dispose();
            _depthRing?.Dispose();
            _synchronizer = null;
            _rgbLeftRing = null;
            _rgbRightRing = null;
            _depthRing = null;
        }

        private void EnsureRuntimeObjects()
        {
            _rgbLeftRing ??= new GpuTextureRing("Cone-PRISM RGB Left", gpuRingSlots);
            _rgbRightRing ??= new GpuTextureRing("Cone-PRISM RGB Right", gpuRingSlots);
            _depthRing ??= new GpuTextureRing("Cone-PRISM Stereo Depth", gpuRingSlots,
                UnityEngine.Experimental.Rendering.GraphicsFormat.R32_SFloat,
                GpuTextureCopyMode.ProjectionDepthArray);
            int boundedMetadataQueue = Mathf.Max(3,
                Mathf.Min(metadataQueueCapacity, gpuRingSlots - 2));
            _synchronizer ??= new RigFrameSynchronizer(maxRgbDeltaMilliseconds,
                maxRgbDepthDeltaMilliseconds, maxClockUncertaintyMilliseconds,
                boundedMetadataQueue);
            _clockMapper ??= RigClockMapper.CreateRuntime();
        }

        private void CaptureRgb(PassthroughCameraAccess access, RigEye eye,
            ref long lastTimestampNs, ref long sequence, GpuTextureRing ring)
        {
            if (access == null || !access.IsPlaying || !access.IsUpdatedThisFrame)
                return;

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
            if (_calibrationEpoch == 0u)
                return;

            if (!ring.TryCopy(source, out GpuTextureLease lease,
                    out RigFrameRejectionReason rejection))
            {
                ReportLocalRejection(rejection);
                return;
            }

            lastTimestampNs = timestamp.SourceNanoseconds;
            long sampleSequence = ++sequence;
            var view = new GpuImageView(RigStreamKind.Rgb, eye, lease.Texture, 0,
                sampleSequence, timestamp, access.GetCameraPose(), intrinsics,
                lease.GraphicsFormat);
            _synchronizer.AddRgb(new RgbRigSample(lease, view, _calibrationEpoch));
        }

        private void OnRawStereoDepthFrame(RawStereoDepthFrame raw)
        {
            if (!_captureRequested || !raw.IsValid)
                return;
            if (!_clockMapper.IsValid && !_clockMapper.TryCaptureAnchor())
            {
                ReportLocalRejection(RigFrameRejectionReason.ClockMappingUncertain);
                return;
            }
            if (!_clockMapper.TryMapXrTimestamp(raw.TimestampNanoseconds,
                    out RigTimestamp timestamp))
            {
                ReportLocalRejection(RigFrameRejectionReason.MissingTimestamp |
                                     RigFrameRejectionReason.ClockMappingUncertain);
                return;
            }

            Vector2Int resolution = new(raw.StereoTexture.width, raw.StereoTexture.height);
            if (!_hasSessionIntrinsics[2] &&
                !FreezeSessionIntrinsics(2,
                    RigCalibrationMath.FromDepthFov(raw.LeftFov, resolution)))
            {
                ReportLocalRejection(RigFrameRejectionReason.InvalidIntrinsics);
                return;
            }
            if (!_hasSessionIntrinsics[3] &&
                !FreezeSessionIntrinsics(3,
                    RigCalibrationMath.FromDepthFov(raw.RightFov, resolution)))
            {
                ReportLocalRejection(RigFrameRejectionReason.InvalidIntrinsics);
                return;
            }
            RigIntrinsics leftIntrinsics = _sessionIntrinsics[2];
            RigIntrinsics rightIntrinsics = _sessionIntrinsics[3];
            if (resolution != leftIntrinsics.ImageResolution ||
                resolution != rightIntrinsics.ImageResolution)
            {
                ReportLocalRejection(RigFrameRejectionReason.CalibrationMismatch);
                return;
            }
            if (_calibrationEpoch == 0u)
                return;

            if (!_depthRing.TryCopy(raw.StereoTexture, out GpuTextureLease lease,
                    out RigFrameRejectionReason rejection))
            {
                ReportLocalRejection(rejection);
                return;
            }

            var left = new GpuImageView(RigStreamKind.Depth, RigEye.Left, lease.Texture,
                0, raw.Sequence, timestamp, raw.WorldFromLeft, leftIntrinsics,
                lease.GraphicsFormat, RigDepthEncoding.ProjectionDepth01, raw.NearFar);
            var right = new GpuImageView(RigStreamKind.Depth, RigEye.Right, lease.Texture,
                1, raw.Sequence, timestamp, raw.WorldFromRight, rightIntrinsics,
                lease.GraphicsFormat, RigDepthEncoding.ProjectionDepth01, raw.NearFar);
            _synchronizer.AddDepth(new StereoDepthRigSample(lease, left, right,
                _calibrationEpoch, resolution, raw.NearFar));
            PublishAvailableFrames();
        }

        private void PublishAvailableFrames()
        {
            while (_synchronizer != null && _synchronizer.TryDequeue(out StereoRigFrameLease frame))
            {
                StereoRigFrameLease previous = _latestFrame;
                _latestFrame = frame;
                previous?.Dispose();
            }
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

            ulong combined = RigCalibrationMath.CombineSignatures(
                _sessionIntrinsics[0].Signature, _sessionIntrinsics[1].Signature,
                _sessionIntrinsics[2].Signature, _sessionIntrinsics[3].Signature);
            _combinedCalibrationSignature = combined;
            _calibrationEpoch = 1u;
            _synchronizer?.Flush(RigFrameRejectionReason.CalibrationMismatch);
            _latestFrame?.Dispose();
            _latestFrame = null;
            Logger.Info($"Cone-PRISM immutable rig calibration frozen, signature=0x{combined:x16}");
            return true;
        }

        private PassthroughCameraAccess ResolveEye(PassthroughCameraAccess assigned,
            PassthroughCameraAccess.CameraPositionType eye, ref bool owns)
        {
            if (assigned != null && assigned.CameraPosition != eye)
            {
                Logger.Error($"Cone-PRISM {eye} PCA reference points to {assigned.CameraPosition}; ignoring it.");
                assigned = null;
                owns = false;
            }

            if (assigned == null)
            {
                PassthroughCameraAccess[] all = FindObjectsByType<PassthroughCameraAccess>(
                    FindObjectsInactive.Include);
                foreach (PassthroughCameraAccess candidate in all)
                {
                    if (candidate.CameraPosition != eye)
                        continue;
                    if (assigned != null)
                    {
                        Logger.Error($"Cone-PRISM found duplicate {eye} PassthroughCameraAccess instances; " +
                                     "capture fails closed until the scene is fixed.");
                        return null;
                    }
                    assigned = candidate;
                    owns = false;
                }
            }

            if (assigned == null)
            {
                var host = new GameObject($"[Cone-PRISM] RGB {eye}");
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
    }
}
