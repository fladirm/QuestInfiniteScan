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
    /// Camera provider backed by Meta's PassthroughCameraAccess (Quest 3+).
    /// Provides intrinsics, extrinsics, and RGB frames from the headset cameras.
    ///
    /// <para>
    /// PCA discovery is <b>scene-wide</b> (not GameObject-local): if the Meta XR
    /// Building Block already dropped a <see cref="PassthroughCameraAccess"/>
    /// onto an OVRCameraRig, the provider re-uses it. Without scene-wide find
    /// we ended up with two PCA components fighting over the single native
    /// camera handle — first session worked, subsequent sessions stuck because
    /// PCA self-disables when <c>Play()</c> fails.
    /// </para>
    /// </summary>
    public class PassthroughCameraProvider : MonoBehaviour, ICameraProvider
    {
        /// <summary>The Horizon OS permission required by PCA on Quest 3+.</summary>
        public const string CameraPermissionId = "horizonos.permission.HEADSET_CAMERA";

        [SerializeField] private PassthroughCameraAccess.CameraPositionType cameraPosition =
            PassthroughCameraAccess.CameraPositionType.Left;
        [SerializeField] private Vector2Int requestedResolution = new(1280, 960);
        [SerializeField] private int maxFramerate = 30;

        private PassthroughCameraAccess _pca;
        private bool _ownsPca;
        private bool _captureRequested;

        /// <inheritdoc />
        public bool IsReady => _captureRequested && _pca != null &&
                               _pca.IsPlaying && _pca.IsUpdatedThisFrame;

        /// <inheritdoc />
        public bool IsPlaying => _captureRequested && _pca != null && _pca.IsPlaying;

        /// <inheritdoc />
        public Texture CurrentFrame => IsPlaying ? _pca.GetTexture() : null;

        /// <inheritdoc />
        public Pose CameraPose =>
            IsPlaying ? _pca.GetCameraPose() : Pose.identity;

        /// <inheritdoc />
        public Vector2 FocalLength =>
            IsPlaying ? _pca.Intrinsics.FocalLength : Vector2.one;

        /// <inheritdoc />
        public Vector2 PrincipalPoint =>
            IsPlaying ? _pca.Intrinsics.PrincipalPoint : Vector2.zero;

        /// <inheritdoc />
        public Vector2 SensorResolution =>
            IsPlaying ? _pca.Intrinsics.SensorResolution : new Vector2(1280, 960);

        /// <inheritdoc />
        public Vector2 CurrentResolution =>
            IsPlaying
                ? new Vector2(_pca.CurrentResolution.x, _pca.CurrentResolution.y)
                : new Vector2(1280, 960);

        /// <summary>
        /// True when the user has granted the Horizon OS HEADSET_CAMERA
        /// permission. Always true outside Android device builds.
        /// </summary>
        public static bool HasCameraPermission
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            get => Permission.HasUserAuthorizedPermission(CameraPermissionId);
#else
            get => true;
#endif
        }

        /// <summary>
        /// Requests the HEADSET_CAMERA permission and resolves once the user
        /// accepts, denies, or dismisses the system dialog. Always resolves
        /// <c>true</c> outside Android device builds (no permission to request).
        /// Resolves <c>true</c> immediately if already granted.
        /// </summary>
        public static Task<bool> RequestCameraPermissionAsync()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (Permission.HasUserAuthorizedPermission(CameraPermissionId))
                return Task.FromResult(true);

            var tcs = new TaskCompletionSource<bool>();
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ => tcs.TrySetResult(true);
            callbacks.PermissionDenied  += _ => tcs.TrySetResult(false);
            callbacks.PermissionDeniedAndDontAskAgain += _ => tcs.TrySetResult(false);
            try
            {
                Permission.RequestUserPermission(CameraPermissionId, callbacks);
            }
            catch (Exception ex)
            {
                Logger.Error($"PassthroughCameraProvider: permission request failed: {ex.Message}");
                tcs.TrySetResult(false);
            }
            return tcs.Task;
#else
            return Task.FromResult(true);
#endif
        }

        /// <inheritdoc />
        public void StartCapture()
        {
            _captureRequested = true;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(CameraPermissionId))
            {
                // Defensive: if the caller hasn't already gone through
                // RequestCameraPermissionAsync, kick the dialog now. PCA's
                // own OnEnable also coroutine-polls until granted, so this
                // is layered safety, not a single source of truth.
                Logger.Info("Requesting HEADSET_CAMERA permission");
                Permission.RequestUserPermission(CameraPermissionId);
            }
#endif

            // Scene-wide find: re-use the PCA from the Meta XR Building Block
            // (typically attached to OVRCameraRig). Falling back to a
            // GameObject-local AddComponent here was the bug that caused two
            // PCAs to race for the camera handle — PCA's native side allows
            // exactly one instance per camera position, so the second one
            // self-disables and from then on neither one plays.
            if (_pca == null)
            {
                _pca = FindAnyObjectByType<PassthroughCameraAccess>(FindObjectsInactive.Include);
                if (_pca == null)
                {
                    Logger.Warning("PassthroughCameraProvider: no PassthroughCameraAccess in scene — " +
                                   "creating a provider-owned instance. Prefer letting Meta's Building Block place it.");
                    _pca = CreateOwnedPca();
                    _ownsPca = true;
                }
                else
                {
                    Logger.Info($"PassthroughCameraProvider: adopted existing PassthroughCameraAccess on '{_pca.gameObject.name}'.");
                    _ownsPca = false;
                }
            }

            // A Building-Block PCA starts during scene initialization. Never
            // bounce an already-active native camera merely to apply provider
            // preferences: PCA.OnDisable calls CameraStop + IssuePluginEvent,
            // which can block the XR render fence for tens of seconds when the
            // mapper's Vulkan resources have just been created. The setup
            // wizard serializes matching settings before build. For legacy or
            // hand-authored scenes, keep the already-running stream and report
            // the mismatch instead of disrupting the native camera lifecycle.
            if (_pca.isActiveAndEnabled)
            {
                if (!ConfigurationMatches(_pca))
                {
                    Logger.Warning("PassthroughCameraProvider: adopted PCA is already active with " +
                                   $"camera={_pca.CameraPosition}, resolution={_pca.RequestedResolution}, " +
                                   $"maxFps={_pca.MaxFramerate}; requested camera={cameraPosition}, " +
                                   $"resolution={requestedResolution}, maxFps={maxFramerate}. " +
                                   "Keeping the active stream; run the setup wizard to serialize the desired settings.");
                }
                return;
            }

            // MaxFramerate may only be assigned while Behaviour.enabled is
            // false. An inactive GameObject can still contain an enabled
            // Behaviour, so normalize both cases before configuring it.
            _pca.enabled = false;
            ApplyConfiguration(_pca);
            _pca.enabled = true;
        }

        /// <inheritdoc />
        public void StopCapture()
        {
            _captureRequested = false;

            // A scene/building-block PCA is shared infrastructure and its
            // native lifetime is not owned by this adapter. Only stop an
            // instance that this provider created itself.
            if (_ownsPca && _pca != null)
                _pca.enabled = false;
        }

        private void OnDestroy()
        {
            StopCapture();
        }

        private PassthroughCameraAccess CreateOwnedPca()
        {
            var host = new GameObject("[RoomScan] Passthrough Camera Access");
            host.transform.SetParent(transform, false);
            host.SetActive(false);

            var pca = host.AddComponent<PassthroughCameraAccess>();
            pca.enabled = false;
            ApplyConfiguration(pca);

            host.SetActive(true);
            pca.enabled = true;
            return pca;
        }

        private void ApplyConfiguration(PassthroughCameraAccess pca)
        {
            pca.CameraPosition = cameraPosition;
            pca.RequestedResolution = requestedResolution;
            pca.MaxFramerate = maxFramerate;
        }

        private bool ConfigurationMatches(PassthroughCameraAccess pca)
        {
            return pca.CameraPosition == cameraPosition &&
                   pca.RequestedResolution == requestedResolution &&
                   pca.MaxFramerate == maxFramerate;
        }
    }
}
