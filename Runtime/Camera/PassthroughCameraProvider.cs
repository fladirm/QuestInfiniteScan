using System;
using System.Threading.Tasks;
using Meta.XR;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace Genesis.RoomScan
{
    /// <summary>
    /// Fixed true-stereo PCA provider. One exact Meta camera instance is owned
    /// or borrowed for each physical eye; a scan observation is exposed only
    /// when both image descriptors match the owned depth timestamp.
    /// </summary>
    [DefaultExecutionOrder(-80)]
    public sealed class PassthroughCameraProvider : MonoBehaviour, ICameraProvider
    {
        public const string CameraPermissionId =
            "horizonos.permission.HEADSET_CAMERA";

        [SerializeField] private Vector2Int requestedResolution =
            new(1280, 960);
        [SerializeField] private int maxFramerate = 30;

        private readonly PassthroughCameraAccess[] _pca =
            new PassthroughCameraAccess[2];
        private readonly bool[] _ownsPca = new bool[2];
        private readonly CameraFrameDescriptor[] _latest =
            new CameraFrameDescriptor[2];
        private readonly long[] _latestTimestampTicks = new long[2];
        private readonly uint[] _sequence = new uint[2];
        private bool _captureRequested;

        internal PassthroughCameraAccess CameraAccess(StereoEye eye) =>
            _pca[(int)eye];
        internal bool OwnsCameraAccess(StereoEye eye) =>
            _ownsPca[(int)eye];

        public bool IsReady => _captureRequested &&
                               _latest[0].IsValid && _latest[1].IsValid;

        public bool IsPlaying => _captureRequested &&
                                 IsEyePlaying(0) && IsEyePlaying(1);

        public static bool HasCameraPermission
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            get => Permission.HasUserAuthorizedPermission(CameraPermissionId);
#else
            get => true;
#endif
        }

        public static Task<bool> RequestCameraPermissionAsync()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (Permission.HasUserAuthorizedPermission(CameraPermissionId))
                return Task.FromResult(true);

            var completion = new TaskCompletionSource<bool>();
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ => completion.TrySetResult(true);
            callbacks.PermissionDenied += _ => completion.TrySetResult(false);
            callbacks.PermissionDeniedAndDontAskAgain += _ =>
                completion.TrySetResult(false);
            try
            {
                Permission.RequestUserPermission(CameraPermissionId, callbacks);
            }
            catch (Exception exception)
            {
                Logger.Error("PassthroughCameraProvider: permission request " +
                             "failed: " + exception.Message);
                completion.TrySetResult(false);
            }
            return completion.Task;
#else
            return Task.FromResult(true);
#endif
        }

        public void StartCapture()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(CameraPermissionId))
            {
                Logger.Info("Requesting HEADSET_CAMERA permission");
                Permission.RequestUserPermission(CameraPermissionId);
            }
#endif
            ResetSamples();
            DiscoverExactCameras();
            for (int eye = 0; eye < 2; eye++) StartEye(eye);
            _captureRequested = true;
        }

        public void StopCapture()
        {
            _captureRequested = false;
            for (int eye = 0; eye < 2; eye++)
            {
                if (_ownsPca[eye] && _pca[eye] != null)
                    _pca[eye].enabled = false;
            }
            ResetSamples();
        }

        public StereoFrameMatch TryGetSynchronizedFrame(
            double depthUnixSeconds, double maximumSkewSeconds,
            out StereoCameraFrame frame)
        {
            frame = default;
            if (!IsReady || !double.IsFinite(depthUnixSeconds) ||
                maximumSkewSeconds <= 0.0)
                return StereoFrameMatch.Waiting;

            return MatchFrames(_latest[0], _latest[1], depthUnixSeconds,
                maximumSkewSeconds, out frame);
        }

        internal static StereoFrameMatch MatchFrames(
            CameraFrameDescriptor leftSource,
            CameraFrameDescriptor rightSource, double depthUnixSeconds,
            double maximumSkewSeconds, out StereoCameraFrame frame)
        {
            frame = default;
            if (!leftSource.IsValid || !rightSource.IsValid ||
                leftSource.Eye != StereoEye.Left ||
                rightSource.Eye != StereoEye.Right ||
                !double.IsFinite(depthUnixSeconds) ||
                maximumSkewSeconds <= 0.0)
                return StereoFrameMatch.Waiting;

            double leftSeconds = leftSource.TimestampUnixSeconds;
            double rightSeconds = rightSource.TimestampUnixSeconds;

            double minimum = Math.Min(depthUnixSeconds,
                Math.Min(leftSeconds, rightSeconds));
            double maximum = Math.Max(depthUnixSeconds,
                Math.Max(leftSeconds, rightSeconds));
            double skew = maximum - minimum;
            if (skew <= maximumSkewSeconds)
            {
                frame = new StereoCameraFrame(leftSource, rightSource, skew);
                return StereoFrameMatch.Ready;
            }

            // The native textures expose only their latest images. Once either
            // stream has advanced past this depth window, no later image can
            // form a valid pair with it, so the depth frame must be dropped.
            if (leftSeconds > depthUnixSeconds + maximumSkewSeconds ||
                rightSeconds > depthUnixSeconds + maximumSkewSeconds)
                return StereoFrameMatch.DepthExpired;

            return StereoFrameMatch.Waiting;
        }

        private void Update()
        {
            if (!_captureRequested) return;
            CaptureMetadata(0);
            CaptureMetadata(1);
        }

        private void CaptureMetadata(int eye)
        {
            PassthroughCameraAccess camera = _pca[eye];
            if (camera == null || !camera.IsPlaying ||
                !camera.IsUpdatedThisFrame || camera.Timestamp == default)
                return;

            long ticks = camera.Timestamp.Ticks - DateTime.UnixEpoch.Ticks;
            if (ticks <= 0 || ticks == _latestTimestampTicks[eye]) return;
            Texture texture = camera.GetTexture();
            if (texture == null) return;

            double sensorSeconds = ticks / (double)TimeSpan.TicksPerSecond;
            unchecked
            {
                _sequence[eye]++;
                if (_sequence[eye] == 0u) _sequence[eye] = 1u;
            }
            StereoEye stereoEye = (StereoEye)eye;
            _latest[eye] = new CameraFrameDescriptor(texture,
                camera.GetCameraPose(), camera.Intrinsics.FocalLength,
                camera.Intrinsics.PrincipalPoint,
                camera.Intrinsics.SensorResolution,
                new Vector2(camera.CurrentResolution.x,
                    camera.CurrentResolution.y), sensorSeconds,
                _sequence[eye], stereoEye);
            _latestTimestampTicks[eye] = ticks;
        }

        private void DiscoverExactCameras()
        {
            PassthroughCameraAccess[] cameras = FindObjectsByType<
                PassthroughCameraAccess>(FindObjectsInactive.Include);
            foreach (PassthroughCameraAccess camera in cameras)
            {
                int eye = camera.CameraPosition ==
                    PassthroughCameraAccess.CameraPositionType.Left ? 0 : 1;
                if (_pca[eye] == null)
                {
                    _pca[eye] = camera;
                    _ownsPca[eye] = false;
                }
                else if (_pca[eye] != camera)
                    throw new InvalidOperationException(
                        "Multiple PCA producers target the same physical " +
                        $"{(StereoEye)eye} eye. True-stereo capture fails closed.");
            }

            for (int eye = 0; eye < 2; eye++)
            {
                if (_pca[eye] != null) continue;
                StereoEye stereoEye = (StereoEye)eye;
                Logger.Info("PassthroughCameraProvider: creating scanner-owned " +
                            $"{stereoEye} PCA instance");
                _pca[eye] = CreateOwnedPca(stereoEye);
                _ownsPca[eye] = true;
            }
        }

        private void StartEye(int eye)
        {
            PassthroughCameraAccess camera = _pca[eye];
            if (camera == null) return;
            if (camera.enabled || camera.IsPlaying)
            {
                Logger.Info("PassthroughCameraProvider: adopted existing " +
                            $"{(StereoEye)eye} PCA on '{camera.gameObject.name}'");
                return;
            }
            ApplyConfiguration(camera, (StereoEye)eye);
            camera.enabled = true;
        }

        private PassthroughCameraAccess CreateOwnedPca(StereoEye eye)
        {
            var host = new GameObject($"[RoomScan] PCA {eye}");
            host.transform.SetParent(transform, false);
            host.SetActive(false);
            var camera = host.AddComponent<PassthroughCameraAccess>();
            camera.enabled = false;
            ApplyConfiguration(camera, eye);
            host.SetActive(true);
            camera.enabled = true;
            return camera;
        }

        private void ApplyConfiguration(PassthroughCameraAccess camera,
            StereoEye eye)
        {
            camera.CameraPosition = eye == StereoEye.Left
                ? PassthroughCameraAccess.CameraPositionType.Left
                : PassthroughCameraAccess.CameraPositionType.Right;
            camera.RequestedResolution = requestedResolution;
            camera.MaxFramerate = maxFramerate;
        }

        private bool IsEyePlaying(int eye) =>
            _pca[eye] != null && _pca[eye].IsPlaying;

        private void ResetSamples()
        {
            for (int eye = 0; eye < 2; eye++)
            {
                _latest[eye] = default;
                _latestTimestampTicks[eye] = 0;
                _sequence[eye] = 0u;
            }
        }

    }
}
