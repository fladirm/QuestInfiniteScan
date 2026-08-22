using System;
using Genesis.RoomScan.SigmaPrism;
using Unity.XR.CoreUtils.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace Genesis.RoomScan
{
    /// <summary>
    /// Thin Quest stereo-depth ingress. ARFoundation owns the native texture; this
    /// component publishes its exact timestamp, per-eye poses, FOV and near/far range
    /// synchronously. Sigma-PRISM-16 performs the only retained GPU copy and all subsequent
    /// normalization, consensus, normal and uncertainty work.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public sealed class DepthCapture : MonoBehaviour
    {
        private const string ScenePermission = "com.oculus.permission.USE_SCENE";

        public static DepthCapture Instance { get; private set; }
        public static bool DepthAvailable { get; private set; }

        private AROcclusionManager _occlusion;
        private bool _permissionReady;
        private bool _captureRequested;
        private bool _subscribed;
        private bool _started;
        private long _frameSequence;
        private float _lastDiagnosticTime;
        private Texture _borrowedDepth;

        /// <summary>
        /// Borrowed texture valid only until the provider reuses it. Production consumers
        /// must retain it through <see cref="RawStereoFrameReceived"/> using GPU-to-GPU copy.
        /// </summary>
        public Texture DepthTex => _borrowedDepth;

        public event Action Updated;
        public event Action<RawStereoDepthFrame> RawStereoFrameReceived;

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            if (!XRRuntimeGuard.IsXRActive)
            {
                Logger.Warning("DepthCapture: " + XRRuntimeGuard.EditorDisabledMessage);
                enabled = false;
                return;
            }

            EnsureArSession();
            _occlusion = FindAnyObjectByType<AROcclusionManager>();
            if (_occlusion == null)
                throw new InvalidOperationException(
                    "[RoomScan] AROcclusionManager not found in scene.");

            _occlusion.enabled = false;
            CheckPermissionAndEnable();
            _started = true;
        }

        public void StartDepthCapture()
        {
            _captureRequested = true;
            if (!_permissionReady || _occlusion == null)
                return;
            SubscribeAndRun();
        }

        public void StopDepthCapture()
        {
            _captureRequested = false;
            StopProvider();
        }

        public void ReleaseResources()
        {
            // The provider owns the native texture. Never Destroy it here.
            _borrowedDepth = null;
            DepthAvailable = false;
        }

        private void OnApplicationPause(bool paused)
        {
            if (!_started)
                return;
            if (paused)
                StopProvider();
            else if (_captureRequested)
                CheckPermissionAndEnable();
        }

        private void OnDisable()
        {
            StopProvider();
        }

        private void OnDestroy()
        {
            StopProvider();
            ReleaseResources();
            if (Instance == this)
                Instance = null;
        }

        private void Update()
        {
            if (Time.unscaledTime - _lastDiagnosticTime < 5f)
                return;
            _lastDiagnosticTime = Time.unscaledTime;
            XROcclusionSubsystem subsystem = _occlusion != null
                ? _occlusion.subsystem
                : null;
            Logger.Info($"DepthCapture: frames={_frameSequence}, " +
                $"available={DepthAvailable}, requested={_captureRequested}, " +
                $"running={subsystem?.running}");
        }

        private void OnDepthFrame(AROcclusionFrameEventArgs args)
        {
            long sequence = ++_frameSequence;
            if (args.externalTextures.Count < 1)
            {
                DepthAvailable = false;
                return;
            }

            Texture texture = args.externalTextures[0].texture;
            if (texture == null || !args.TryGetTimestamp(out long timestampNs) ||
                !args.TryGetFovs(out ReadOnlyList<XRFov> fovs) || fovs.Count < 2 ||
                !args.TryGetPoses(out ReadOnlyList<Pose> poses) || poses.Count < 2 ||
                !args.TryGetNearFarPlanes(out XRNearFarPlanes planes))
            {
                DepthAvailable = false;
                return;
            }

            _borrowedDepth = texture;
            DepthAvailable = true;

            // ARFoundation supplies the per-eye poses at the depth-frame timestamp.
            // SigmaRigBridge freezes intrinsics once per calibration epoch and copies
            // the borrowed stereo array into its generation-safe GPU ring immediately.
            RawStereoFrameReceived?.Invoke(new RawStereoDepthFrame(texture, timestampNs,
                poses[0], poses[1], fovs[0], fovs[1],
                new Vector2(planes.nearZ, planes.farZ), sequence));
            Updated?.Invoke();
        }

        private void EnsureArSession()
        {
            if (FindAnyObjectByType<ARSession>() != null)
                return;
            var session = new GameObject("[AR Session]");
            session.AddComponent<ARSession>();
            Logger.Info("DepthCapture: created missing ARSession.");
        }

        private void CheckPermissionAndEnable()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (Permission.HasUserAuthorizedPermission(ScenePermission))
            {
                VerifyProviderAsync();
                return;
            }

            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ => VerifyProviderAsync();
            callbacks.PermissionDenied += _ =>
                Logger.Error("USE_SCENE permission denied; stereo depth is unavailable.");
            Permission.RequestUserPermission(ScenePermission, callbacks);
#else
            VerifyProviderAsync();
#endif
        }

        private async void VerifyProviderAsync()
        {
            if (_occlusion == null)
                return;

            StopProvider();
            await Awaitable.NextFrameAsync();
            if (_occlusion == null)
                return;

            _occlusion.enabled = true;
            await Awaitable.NextFrameAsync();
            await Awaitable.NextFrameAsync();
            if (_occlusion == null)
                return;

            XROcclusionSubsystem subsystem = _occlusion.subsystem;
            _permissionReady = subsystem != null;
            Logger.Info("DepthCapture provider: " +
                $"{subsystem?.GetType().Name ?? "null"}, running={subsystem?.running}");
            if (_captureRequested && _permissionReady)
                SubscribeAndRun();
            else
                _occlusion.enabled = false;
        }

        private void SubscribeAndRun()
        {
            if (_occlusion == null)
                return;
            if (!_occlusion.enabled)
                _occlusion.enabled = true;
            if (!_subscribed)
            {
                _occlusion.frameReceived += OnDepthFrame;
                _subscribed = true;
            }
            Logger.Info("DepthCapture: stereo depth provider started.");
        }

        private void StopProvider()
        {
            if (_occlusion != null)
            {
                if (_subscribed)
                    _occlusion.frameReceived -= OnDepthFrame;
                _occlusion.enabled = false;
            }
            _subscribed = false;
            _borrowedDepth = null;
            DepthAvailable = false;
        }
    }
}
