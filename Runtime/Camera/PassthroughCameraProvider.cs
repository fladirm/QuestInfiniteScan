using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Meta.XR;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
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
        private const int HistoryCapacity = 2;

        public const string CameraPermissionId =
            "horizonos.permission.HEADSET_CAMERA";

        [SerializeField] private Vector2Int requestedResolution =
            new(1280, 960);
        [SerializeField] private int maxFramerate = 30;

        private readonly PassthroughCameraAccess[] _pca =
            new PassthroughCameraAccess[2];
        private readonly bool[] _ownsPca = new bool[2];
        private readonly CameraFrameDescriptor[][] _history =
        {
            new CameraFrameDescriptor[HistoryCapacity],
            new CameraFrameDescriptor[HistoryCapacity]
        };
        private readonly RenderTexture[][] _ownedHistory =
        {
            new RenderTexture[HistoryCapacity],
            new RenderTexture[HistoryCapacity]
        };
        private readonly int[] _nextHistorySlot = new int[2];
        private readonly long[] _latestTimestampTicks = new long[2];
        private readonly uint[] _sequence = new uint[2];
        private ulong _copySubmittedEpoch;
        private ulong _copyRetiredEpoch;
        private RenderTexture _lastCopyTarget;
        private Task _copyRetirementTask = Task.CompletedTask;
        private bool _captureRequested;
        private bool _renderCallbackRegistered;

        private readonly struct PendingSample
        {
            internal readonly Texture Texture;
            internal readonly Pose WorldPose;
            internal readonly Vector2 FocalLength;
            internal readonly Vector2 PrincipalPoint;
            internal readonly Vector2 SensorResolution;
            internal readonly Vector2 CurrentResolution;
            internal readonly double TimestampUnixSeconds;
            internal readonly long TimestampTicks;

            internal PendingSample(Texture texture, Pose worldPose,
                Vector2 focalLength, Vector2 principalPoint,
                Vector2 sensorResolution, Vector2 currentResolution,
                double timestampUnixSeconds, long timestampTicks)
            {
                Texture = texture;
                WorldPose = worldPose;
                FocalLength = focalLength;
                PrincipalPoint = principalPoint;
                SensorResolution = sensorResolution;
                CurrentResolution = currentResolution;
                TimestampUnixSeconds = timestampUnixSeconds;
                TimestampTicks = timestampTicks;
            }
        }

        internal PassthroughCameraAccess CameraAccess(StereoEye eye) =>
            _pca[(int)eye];
        internal bool OwnsCameraAccess(StereoEye eye) =>
            _ownsPca[(int)eye];

        public bool IsReady => _captureRequested &&
                               HasValidHistory(0) && HasValidHistory(1);

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
            RegisterRenderCallback();
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

        internal void BeginSnapshotQuiesce()
        {
            _captureRequested = false;
        }

        internal Task RetireSubmittedSnapshotCopiesAsync()
        {
            ulong target = _copySubmittedEpoch;
            if (_copyRetiredEpoch >= target || target == 0u)
                return Task.CompletedTask;
            if (!_copyRetirementTask.IsCompleted)
                return _copyRetirementTask;
            if (!SystemInfo.supportsAsyncGPUReadback)
                return Task.FromException(new NotSupportedException(
                    "Quest PCA history retirement requires asynchronous GPU readback."));
            if (_lastCopyTarget == null)
                return Task.FromException(new IOException(
                    "Owned PCA history disappeared before GPU retirement."));

            RenderTexture targetTexture = _lastCopyTarget;
            var completion = new TaskCompletionSource<bool>();
            _copyRetirementTask = completion.Task;
            AsyncGPUReadback.Request(targetTexture, 0, 0, 1, 0, 1, 0, 1,
                request =>
                {
                    if (request.hasError)
                    {
                        completion.TrySetException(new IOException(
                            "Owned PCA history retirement readback failed."));
                        return;
                    }
                    _copyRetiredEpoch = Math.Max(_copyRetiredEpoch, target);
                    completion.TrySetResult(true);
                });
            return _copyRetirementTask;
        }

        internal Action CaptureOwnedGpuResourceRelease()
        {
            var captured = new UnityEngine.Object[2 * HistoryCapacity];
            int index = 0;
            for (int eye = 0; eye < 2; eye++)
            for (int slot = 0; slot < HistoryCapacity; slot++)
                captured[index++] = _ownedHistory[eye][slot];
            bool released = false;
            return () =>
            {
                if (released) return;
                released = true;
                if (this != null)
                {
                    ReleaseOwnedHistory();
                    return;
                }
                foreach (UnityEngine.Object resource in captured)
                    if (resource != null) Destroy(resource);
            };
        }

        public StereoFrameMatch TryGetSynchronizedFrame(
            double depthUnixSeconds, double maximumSkewSeconds,
            out StereoCameraFrame frame)
        {
            frame = default;
            if (!IsReady || !double.IsFinite(depthUnixSeconds) ||
                maximumSkewSeconds <= 0.0)
                return StereoFrameMatch.Waiting;

            return MatchFrameHistory(_history[0], _history[1],
                depthUnixSeconds, maximumSkewSeconds, out frame);
        }

        internal static StereoFrameMatch MatchFrameHistory(
            IReadOnlyList<CameraFrameDescriptor> leftSources,
            IReadOnlyList<CameraFrameDescriptor> rightSources,
            double depthUnixSeconds, double maximumSkewSeconds,
            out StereoCameraFrame frame)
        {
            frame = default;
            if (leftSources == null || rightSources == null ||
                !double.IsFinite(depthUnixSeconds) ||
                maximumSkewSeconds <= 0.0)
                return StereoFrameMatch.Waiting;

            double bestSpread = double.PositiveInfinity;
            double bestDistance = double.PositiveInfinity;
            CameraFrameDescriptor bestLeft = default;
            CameraFrameDescriptor bestRight = default;
            double latestLeft = double.NegativeInfinity;
            double latestRight = double.NegativeInfinity;
            bool hasLeft = false;
            bool hasRight = false;

            for (int leftIndex = 0; leftIndex < leftSources.Count; leftIndex++)
            {
                CameraFrameDescriptor left = leftSources[leftIndex];
                if (!left.HasCoherentTime || left.Eye != StereoEye.Left)
                    continue;
                hasLeft = true;
                latestLeft = Math.Max(latestLeft, left.TimestampUnixSeconds);
                for (int rightIndex = 0; rightIndex < rightSources.Count;
                     rightIndex++)
                {
                    CameraFrameDescriptor right = rightSources[rightIndex];
                    if (!right.HasCoherentTime ||
                        right.Eye != StereoEye.Right)
                        continue;
                    hasRight = true;
                    latestRight = Math.Max(latestRight,
                        right.TimestampUnixSeconds);
                    double minimum = Math.Min(depthUnixSeconds,
                        Math.Min(left.TimestampUnixSeconds,
                            right.TimestampUnixSeconds));
                    double maximum = Math.Max(depthUnixSeconds,
                        Math.Max(left.TimestampUnixSeconds,
                            right.TimestampUnixSeconds));
                    double spread = maximum - minimum;
                    double distance = Math.Abs(left.TimestampUnixSeconds -
                                               depthUnixSeconds) +
                                      Math.Abs(right.TimestampUnixSeconds -
                                               depthUnixSeconds);
                    if (spread > maximumSkewSeconds ||
                        (spread > bestSpread &&
                         !NearlyEqual(spread, bestSpread)) ||
                        (NearlyEqual(spread, bestSpread) &&
                         distance >= bestDistance))
                        continue;
                    bestSpread = spread;
                    bestDistance = distance;
                    bestLeft = left;
                    bestRight = right;
                }
            }

            // A right history can be valid even when no left sample reached
            // the nested loop, so obtain its newest timestamp independently.
            for (int rightIndex = 0; rightIndex < rightSources.Count;
                 rightIndex++)
            {
                CameraFrameDescriptor right = rightSources[rightIndex];
                if (!right.HasCoherentTime || right.Eye != StereoEye.Right)
                    continue;
                hasRight = true;
                latestRight = Math.Max(latestRight,
                    right.TimestampUnixSeconds);
            }

            if (bestLeft.IsValid && bestRight.IsValid)
            {
                frame = new StereoCameraFrame(bestLeft, bestRight,
                    bestSpread);
                return StereoFrameMatch.Ready;
            }

            double windowEnd = depthUnixSeconds + maximumSkewSeconds;
            return hasLeft && hasRight && latestLeft > windowEnd &&
                   latestRight > windowEnd
                ? StereoFrameMatch.DepthExpired
                : StereoFrameMatch.Waiting;
        }

        private void OnEndContextRendering(ScriptableRenderContext context,
            List<Camera> cameras)
        {
            if (!_captureRequested) return;
            // PCA owns a live render-thread texture. Latch its descriptor in
            // the same render callback that records the copy, so image, pose,
            // intrinsics, and timestamp describe one producer update.
            bool hasLeft = TryCaptureMetadata(0, out PendingSample left);
            bool hasRight = TryCaptureMetadata(1, out PendingSample right);
            if (!hasLeft && !hasRight) return;

            CommandBuffer command = CommandBufferPool.Get(
                "Merkaba render-thread PCA history");
            int leftSlot = -1;
            int rightSlot = -1;
            try
            {
                if (hasLeft) leftSlot = StageOwnedSample(command, 0, left);
                if (hasRight) rightSlot = StageOwnedSample(command, 1, right);
                Graphics.ExecuteCommandBuffer(command);
            }
            finally
            {
                CommandBufferPool.Release(command);
            }

            if (hasLeft)
                PublishOwnedSample(0, leftSlot, left);
            if (hasRight)
                PublishOwnedSample(1, rightSlot, right);
            unchecked
            {
                _copySubmittedEpoch++;
                if (_copySubmittedEpoch == 0u) _copySubmittedEpoch = 1u;
            }
        }

        private bool TryCaptureMetadata(int eye, out PendingSample sample)
        {
            sample = default;
            PassthroughCameraAccess camera = _pca[eye];
            if (camera == null || !camera.IsPlaying ||
                !camera.IsUpdatedThisFrame || camera.Timestamp == default)
                return false;

            long ticks = camera.Timestamp.Ticks - DateTime.UnixEpoch.Ticks;
            if (ticks <= 0 || ticks == _latestTimestampTicks[eye]) return false;
            Texture texture = camera.GetTexture();
            if (texture == null) return false;

            double sensorSeconds = ticks / (double)TimeSpan.TicksPerSecond;
            sample = new PendingSample(texture, camera.GetCameraPose(),
                camera.Intrinsics.FocalLength,
                camera.Intrinsics.PrincipalPoint,
                camera.Intrinsics.SensorResolution,
                new Vector2(camera.CurrentResolution.x,
                    camera.CurrentResolution.y), sensorSeconds, ticks);
            return true;
        }

        private int StageOwnedSample(CommandBuffer command, int eye,
            PendingSample sample)
        {
            int slot = _nextHistorySlot[eye];
            RenderTexture owned = _ownedHistory[eye][slot];
            int width = Mathf.Max(1, sample.Texture.width);
            int height = Mathf.Max(1, sample.Texture.height);
            if (owned == null || owned.width != width || owned.height != height)
            {
                if (owned != null) Destroy(owned);
                owned = new RenderTexture(width, height, 0,
                    GraphicsFormat.R8G8B8A8_UNorm)
                {
                    name = $"Merkaba PCA history {(StereoEye)eye} {slot}",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                owned.Create();
                _ownedHistory[eye][slot] = owned;
            }
            command.BlitPcaHistoryProfiled(sample.Texture, owned);
            _lastCopyTarget = owned;
            return slot;
        }

        private void PublishOwnedSample(int eye, int slot,
            PendingSample sample)
        {
            unchecked
            {
                _sequence[eye]++;
                if (_sequence[eye] == 0u) _sequence[eye] = 1u;
            }
            StereoEye stereoEye = (StereoEye)eye;
            _history[eye][slot] = new CameraFrameDescriptor(
                _ownedHistory[eye][slot], sample.WorldPose,
                sample.FocalLength, sample.PrincipalPoint,
                sample.SensorResolution, sample.CurrentResolution,
                sample.TimestampUnixSeconds,
                _sequence[eye], stereoEye);
            _latestTimestampTicks[eye] = sample.TimestampTicks;
            _nextHistorySlot[eye] = (slot + 1) % HistoryCapacity;
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

        private bool HasValidHistory(int eye)
        {
            for (int slot = 0; slot < HistoryCapacity; slot++)
                if (_history[eye][slot].IsValid) return true;
            return false;
        }

        private void ResetSamples()
        {
            for (int eye = 0; eye < 2; eye++)
            {
                for (int slot = 0; slot < HistoryCapacity; slot++)
                    _history[eye][slot] = default;
                _nextHistorySlot[eye] = 0;
                _latestTimestampTicks[eye] = 0;
                _sequence[eye] = 0u;
            }
        }

        private void OnEnable() => RegisterRenderCallback();

        private void OnDisable() => UnregisterRenderCallback();

        private void RegisterRenderCallback()
        {
            if (_renderCallbackRegistered) return;
            RenderPipelineManager.endContextRendering += OnEndContextRendering;
            _renderCallbackRegistered = true;
        }

        private void UnregisterRenderCallback()
        {
            if (!_renderCallbackRegistered) return;
            RenderPipelineManager.endContextRendering -= OnEndContextRendering;
            _renderCallbackRegistered = false;
        }

        private void ReleaseOwnedHistory()
        {
            for (int eye = 0; eye < 2; eye++)
            for (int slot = 0; slot < HistoryCapacity; slot++)
            {
                if (_ownedHistory[eye][slot] != null)
                    Destroy(_ownedHistory[eye][slot]);
                _ownedHistory[eye][slot] = null;
            }
            _lastCopyTarget = null;
            ResetSamples();
        }

        private static bool NearlyEqual(double left, double right) =>
            Math.Abs(left - right) <= 1e-12;

    }
}
