using System;
using Unity.XR.CoreUtils.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace Genesis.RoomScan
{
    /// <summary>
    /// Captures stereo depth from the AR occlusion subsystem, computes world-space normals,
    /// runs optional bilateral filtering guided by the passthrough RGB feed, and produces
    /// dilated depth textures consumed by <see cref="MerkabaIntegrator"/> for reversible
    /// surface/free-space evidence integration.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public class DepthCapture : MonoBehaviour
    {
        public static DepthCapture Instance { get; private set; }

        [SerializeField] private ComputeShader depthNormalCompute;
        [SerializeField] private ComputeShader depthDilationCompute;
        [SerializeField] private ComputeShader bilateralFilterCompute;

        [Header("Bilateral Depth Filter")]
        [Tooltip("Edge-preserving depth denoising guided by passthrough RGB. Smooths flat surfaces while keeping object boundaries sharp.")]
        [SerializeField] private bool enableBilateralFilter = true;
        [SerializeField, Range(1f, 8f)] private float sigmaSpatial = 3.0f;
        [SerializeField, Range(0.01f, 0.5f)] private float sigmaColor = 0.1f;
        [SerializeField, Range(0.001f, 0.1f)] private float sigmaDepth = 0.02f;
        [SerializeField, Range(1, 5)] private int filterRadius = 2;

        [Header("Dilation")]
        [SerializeField, Range(0, 12)] private int dilationSteps = 8;
        [SerializeField] private float voxelDistance = 0.2f;
        [SerializeField] private float voxelSize = 0.05f;

        private readonly Matrix4x4[] _proj = new Matrix4x4[2];
        private readonly Matrix4x4[] _projInv = new Matrix4x4[2];
        private readonly Matrix4x4[] _view = new Matrix4x4[2];
        private readonly Matrix4x4[] _viewInv = new Matrix4x4[2];
        private Vector2 _planes;

        /// <summary>Per-eye projection matrices derived from the depth frame's FOV and near/far planes.</summary>
        public Matrix4x4[] Proj => _proj;
        /// <summary>Inverse projection matrices (per-eye).</summary>
        public Matrix4x4[] ProjInv => _projInv;
        /// <summary>Per-eye view matrices (tracking-space to depth-camera-space).</summary>
        public Matrix4x4[] View => _view;
        /// <summary>Inverse view matrices (per-eye), mapping depth-camera-space back to tracking-space.</summary>
        public Matrix4x4[] ViewInv => _viewInv;
        /// <summary>Near and far clip distances (x = near, y = far) for the current depth frame.</summary>
        public Vector2 Planes => _planes;

        // Shader property IDs
        public static readonly int DepthTexID = Shader.PropertyToID("gsDepthTex");
        public static readonly int DepthTexRWID = Shader.PropertyToID("gsDepthTexRW");
        public static readonly int TexSizeID = Shader.PropertyToID("gsDepthTexSize");
        public static readonly int NormTexID = Shader.PropertyToID("gsDepthNormalTex");
        public static readonly int NormTexRWID = Shader.PropertyToID("gsDepthNormalTexRW");
        public static readonly int ZParamsID = Shader.PropertyToID("gsDepthZParams");
        public static readonly int ProjID = Shader.PropertyToID("gsDepthProj");
        public static readonly int ProjInvID = Shader.PropertyToID("gsDepthProjInv");
        public static readonly int ViewID = Shader.PropertyToID("gsDepthView");
        public static readonly int ViewInvID = Shader.PropertyToID("gsDepthViewInv");
        public static readonly int InputRawMonoDepthID = Shader.PropertyToID("gsInputRawMonoDepth");
        public static readonly int DilateSrcID = Shader.PropertyToID("gsDilateSrc");
        public static readonly int DilateDestID = Shader.PropertyToID("gsDilateDest");
        public static readonly int DilateStepSizeID = Shader.PropertyToID("gsDilateStepSize");
        public static readonly int DilatedDepthTexID = Shader.PropertyToID("gsDilatedDepth");
        public static readonly int VoxDistID = Shader.PropertyToID("gsVoxDist");
        public static readonly int VoxSizeShaderID = Shader.PropertyToID("gsVoxSize");
        private static readonly int InputProjectionDepthID =
            Shader.PropertyToID("gsInputProjectionDepth");

        // Bilateral filter property IDs
        private static readonly int BilSrcDepthID = Shader.PropertyToID("_SrcDepth");
        private static readonly int BilRGBGuideID = Shader.PropertyToID("_RGBGuide");
        private static readonly int BilDstDepthID = Shader.PropertyToID("_DstDepth");
        private static readonly int BilDepthWID = Shader.PropertyToID("_DepthW");
        private static readonly int BilDepthHID = Shader.PropertyToID("_DepthH");
        private static readonly int BilSigmaSpatialID = Shader.PropertyToID("_SigmaSpatial");
        private static readonly int BilSigmaColorID = Shader.PropertyToID("_SigmaColor");
        private static readonly int BilSigmaDepthID = Shader.PropertyToID("_SigmaDepth");
        private static readonly int BilFilterRadiusID = Shader.PropertyToID("_FilterRadius");

        /// <summary>True once a valid depth frame has been received from the AR occlusion subsystem.</summary>
        public static bool DepthAvailable { get; private set; }

        /// <summary>
        /// True after USE_SCENE permission is confirmed and the initial subsystem check passes.
        /// Until this is set, <see cref="StartDepthCapture"/> is a no-op.
        /// </summary>
        private bool _permissionReady;

        /// <summary>
        /// Tracks whether the caller (RoomScanner) wants depth capture active.
        /// Persists across app pause/resume so the subsystem is re-enabled correctly.
        /// </summary>
        private bool _captureActive;

        private ComputeKernelHelper _normKernel;
        private ComputeKernelHelper _projectionDepthCopyKernel;
        private ComputeKernelHelper _monoConvertKernel;
        private ComputeKernelHelper _initDilateKernel;
        private ComputeKernelHelper _dilateStepKernel;
        private ComputeKernelHelper _bilateralKernel;
        private bool _hasBilateralKernel;

        private readonly RenderTexture[] _ownedRawDepth = new RenderTexture[2];
        private readonly Matrix4x4[,] _ownedProj = new Matrix4x4[2, 2];
        private readonly Matrix4x4[,] _ownedProjInv = new Matrix4x4[2, 2];
        private readonly Matrix4x4[,] _ownedView = new Matrix4x4[2, 2];
        private readonly Matrix4x4[,] _ownedViewInv = new Matrix4x4[2, 2];
        private readonly Vector2[] _ownedPlanes = new Vector2[2];
        private readonly int[] _ownedVersions = new int[2];
        private int _requestedDepthSlot = -1;
        private int _readyDepthSlot = -1;
        private int _heldDepthSlot = -1;
        private Texture _depthTex;
        /// <summary>The latest depth frame actually preprocessed for integration.</summary>
        public Texture DepthTex => _depthTex;

        private RenderTexture _normTex;
        /// <summary>World-space normals computed from the depth texture via the DepthNorm compute shader.</summary>
        public RenderTexture NormTex => _normTex;

        private RenderTexture _dilationA, _dilationB;
        private RenderTexture _dilatedDepth;
        /// <summary>Depth texture after jump-flood dilation, used by the integrator to fill holes near voxel boundaries.</summary>
        public RenderTexture DilatedDepthTex => _dilatedDepth;

        private RenderTexture _filteredDepthTex;
        private Texture _rgbGuide;

        private AROcclusionManager _arOcclusionManager;
        private Unity.XR.CoreUtils.XROrigin _xrOrigin;
        private Transform _trackingSpaceTransform;
        private Camera _mainCam;
        private bool _started;
        private int _frameCount;
        private int _preprocessedFrameCount;
        private int _latestRawFrameVersion;
        private int _processedRawFrameVersion;
        private bool _depthFrameRequested;
        private float _lastLogTime;

        internal int LatestRawFrameVersion => _latestRawFrameVersion;
        internal int ProcessedRawFrameVersion => _processedRawFrameVersion;
        internal int PreprocessedFrameCount => _preprocessedFrameCount;
        internal bool DepthFrameRequested => _depthFrameRequested;
        internal bool OwnedDepthSnapshotReady => _readyDepthSlot >= 0;
        internal RenderTexture OwnedRawDepthSnapshot => _readyDepthSlot >= 0
            ? _ownedRawDepth[_readyDepthSlot]
            : _heldDepthSlot >= 0 ? _ownedRawDepth[_heldDepthSlot] : null;
        internal Texture RGBGuide => _rgbGuide;
        public bool HasUnprocessedFrame => DepthAvailable &&
            _readyDepthSlot >= 0 && _heldDepthSlot < 0 &&
            _ownedRawDepth[_readyDepthSlot] != null &&
            ShouldPreprocessFrame(_ownedVersions[_readyDepthSlot],
                _processedRawFrameVersion);

        private const string ScenePermission = "com.oculus.permission.USE_SCENE";

        /// <summary>Raised only after an integration consumer preprocesses the latest frame.</summary>
        public event Action Updated;

        /// <summary>
        /// Provide the scanner-owned RGB observation as edge guide for bilateral depth filtering.
        /// </summary>
        public void SetRGBGuide(Texture tex) => _rgbGuide = tex;

        private static readonly Vector3 ScaleFlipZ = new(1, 1, -1);

        /// <summary>
        /// Convert a pose from XR tracking space to Unity world space.
        /// Required because MRUK's world-lock may offset TrackingSpace from the XROrigin root.
        /// </summary>
        public Pose TrackingToWorld(Pose trackingPose)
        {
            if (_trackingSpaceTransform == null) return trackingPose;
            return new Pose(
                _trackingSpaceTransform.TransformPoint(trackingPose.position),
                _trackingSpaceTransform.rotation * trackingPose.rotation);
        }

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            // Editor + no XR loader = AR subsystems will all be null and any
            // toggle of AROcclusionManager.enabled blows up DestroyTextures.
            // Build/run on Quest (or via Link) to actually scan.
            if (!XRRuntimeGuard.IsXRActive)
            {
                Logger.Warning("DepthCapture: " + XRRuntimeGuard.EditorDisabledMessage);
                enabled = false;
                return;
            }

            EnsureARSession();

            _arOcclusionManager = FindAnyObjectByType<AROcclusionManager>();
            if (!_arOcclusionManager)
                throw new Exception("[RoomScan] AROcclusionManager not found in scene");

            _xrOrigin = FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
            CacheTrackingSpaceTransform();

            _normKernel = new ComputeKernelHelper(depthNormalCompute, "DepthNorm");
            _projectionDepthCopyKernel = new ComputeKernelHelper(depthNormalCompute,
                "CopyProjectionDepthArray");
            _monoConvertKernel = new ComputeKernelHelper(depthNormalCompute, "MonoRawDepthToStereo");
            _initDilateKernel = new ComputeKernelHelper(depthDilationCompute, "InitDepthDilation");
            _dilateStepKernel = new ComputeKernelHelper(depthDilationCompute, "DilateDepthStep");
            MerkabaGpuTimestamps.RegisterKernel(depthNormalCompute,
                _normKernel.KernelIndex, MerkabaGpuStage.DepthPreprocess,
                "DepthNorm");
            MerkabaGpuTimestamps.RegisterKernel(depthDilationCompute,
                _initDilateKernel.KernelIndex, MerkabaGpuStage.DepthPreprocess,
                "InitDepthDilation");
            MerkabaGpuTimestamps.RegisterKernel(depthDilationCompute,
                _dilateStepKernel.KernelIndex, MerkabaGpuStage.DepthPreprocess,
                "DilateDepthStep");
            if (bilateralFilterCompute != null)
            {
                _bilateralKernel = new ComputeKernelHelper(bilateralFilterCompute, "BilateralFilter");
                _hasBilateralKernel = true;
                MerkabaGpuTimestamps.RegisterKernel(bilateralFilterCompute,
                    _bilateralKernel.KernelIndex,
                    MerkabaGpuStage.DepthPreprocess, "BilateralFilter");
            }

            // Disable occlusion manager initially, enable after permission is confirmed
            _arOcclusionManager.enabled = false;
            CheckPermissionAndEnable();

            _started = true;
        }

        /// <summary>
        /// Resolves the TrackingSpace transform — the parent of the XR cameras that
        /// MRUK world-lock can reposition each frame. Using this instead of the XROrigin
        /// root ensures depth-to-world conversion includes the world-lock offset.
        /// </summary>
        private void CacheTrackingSpaceTransform()
        {
            // Prefer OVRCameraRig.trackingSpace (most reliable on Meta devices)
            var ovrRig = FindAnyObjectByType<OVRCameraRig>();
            if (ovrRig != null && ovrRig.trackingSpace != null)
            {
                _trackingSpaceTransform = ovrRig.trackingSpace;
                Logger.Info($"DepthCapture: using OVRCameraRig.trackingSpace '{_trackingSpaceTransform.name}'");
                return;
            }

            // Fallback: XROrigin.CameraFloorOffsetObject
            if (_xrOrigin != null && _xrOrigin.CameraFloorOffsetObject != null)
            {
                _trackingSpaceTransform = _xrOrigin.CameraFloorOffsetObject.transform;
                Logger.Info($"DepthCapture: using XROrigin.CameraFloorOffsetObject '{_trackingSpaceTransform.name}'");
                return;
            }

            // Last resort: XROrigin root (pre-fix behaviour)
            _trackingSpaceTransform = _xrOrigin != null ? _xrOrigin.transform : null;
            Logger.Warning("DepthCapture: no TrackingSpace found, falling back to XROrigin root");
        }

        private void EnsureARSession()
        {
            if (FindAnyObjectByType<ARSession>() == null)
            {
                var go = new GameObject("[AR Session]");
                go.AddComponent<ARSession>();
                Logger.Info("Created ARSession (was missing from scene)");
            }
        }

        private void CheckPermissionAndEnable()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (Permission.HasUserAuthorizedPermission(ScenePermission))
            {
                EnableOcclusion();
            }
            else
            {
                Logger.Info("Requesting USE_SCENE permission...");
                var callbacks = new PermissionCallbacks();
                callbacks.PermissionGranted += _ => EnableOcclusion();
                callbacks.PermissionDenied += _ => Logger.Error("USE_SCENE permission denied — depth will not work");
                Permission.RequestUserPermission(ScenePermission, callbacks);
            }
#else
            EnableOcclusion();
#endif
        }

        private bool _subscribed;

        private async void EnableOcclusion()
        {
            if (_arOcclusionManager == null) return;

            Logger.Info("Verifying AROcclusionManager subsystem...");

            _arOcclusionManager.frameReceived -= OnDepthFrame;
            _arOcclusionManager.enabled = false;

            await Awaitable.NextFrameAsync();
            await Awaitable.NextFrameAsync();

            if (_arOcclusionManager == null) return;

            // Briefly enable to verify the subsystem is functional
            _arOcclusionManager.enabled = true;

            await Awaitable.NextFrameAsync();
            await Awaitable.NextFrameAsync();

            if (_arOcclusionManager == null) return;
            var sub = _arOcclusionManager.subsystem;
            Logger.Info($"Occlusion subsystem: {(sub != null ? sub.GetType().Name : "null")}, running={sub?.running}");

            _permissionReady = true;

            if (_captureActive)
            {
                _arOcclusionManager.frameReceived += OnDepthFrame;
                _subscribed = true;
                Logger.Info("DepthCapture: subsystem left running (scan already active)");
            }
            else
            {
                _arOcclusionManager.enabled = false;
                Logger.Info("DepthCapture: subsystem disabled (no active scan)");
            }
        }

        /// <summary>
        /// Enables the AROcclusionManager and subscribes to depth frames.
        /// Called by RoomScanner when scanning starts.
        /// </summary>
        public void StartDepthCapture()
        {
            _captureActive = true;
            if (!_permissionReady || _arOcclusionManager == null) return;
            if (!_arOcclusionManager.enabled)
                _arOcclusionManager.enabled = true;
            if (!_subscribed)
            {
                _arOcclusionManager.frameReceived += OnDepthFrame;
                _subscribed = true;
            }
            Logger.Info("DepthCapture: subsystem started");
        }

        public bool RequestNextDepthFrame()
        {
            if (!_captureActive || _depthFrameRequested) return false;
            if (_heldDepthSlot < 0 && _readyDepthSlot >= 0) return false;
            _requestedDepthSlot = _heldDepthSlot >= 0
                ? 1 - _heldDepthSlot
                : _readyDepthSlot >= 0 ? 1 - _readyDepthSlot : 0;
            _depthFrameRequested = true;
            return true;
        }

        /// <summary>
        /// Unsubscribes from depth frames and disables the AROcclusionManager,
        /// stopping the depth sensor and neural inference pipeline on Quest.
        /// Called by RoomScanner when scanning stops.
        /// </summary>
        public void StopDepthCapture()
        {
            _captureActive = false;
            if (_arOcclusionManager != null)
            {
                if (_subscribed)
                {
                    _arOcclusionManager.frameReceived -= OnDepthFrame;
                    _subscribed = false;
                }
                _arOcclusionManager.enabled = false;
            }
            DepthAvailable = false;
            _depthFrameRequested = false;
            _requestedDepthSlot = -1;
            _readyDepthSlot = -1;
            _heldDepthSlot = -1;
            _depthTex = null;
            _processedRawFrameVersion = _latestRawFrameVersion;
        }

        private void OnApplicationPause(bool paused)
        {
            if (!_started) return;

            if (paused)
            {
                if (_arOcclusionManager != null)
                {
                    _arOcclusionManager.frameReceived -= OnDepthFrame;
                    _arOcclusionManager.enabled = false;
                    _subscribed = false;
                }
                DepthAvailable = false;
                _depthFrameRequested = false;
                _requestedDepthSlot = -1;
                _readyDepthSlot = -1;
                _heldDepthSlot = -1;
                _depthTex = null;
                _processedRawFrameVersion = _latestRawFrameVersion;
            }
            else if (_captureActive)
            {
                CheckPermissionAndEnable();
            }
        }

        private void OnDisable()
        {
            if (_arOcclusionManager != null && _subscribed)
            {
                _arOcclusionManager.frameReceived -= OnDepthFrame;
                _subscribed = false;
            }
        }

        private void OnDestroy()
        {
            ReleaseResources();
        }

        /// <summary>
        /// Destroys GPU textures (normals, dilation, filtered depth) to free memory.
        /// Textures are lazily recreated when the next depth frame arrives.
        /// </summary>
        public void ReleaseResources()
        {
            if (_normTex) { Destroy(_normTex); _normTex = null; }
            if (_dilationA) { Destroy(_dilationA); _dilationA = null; }
            if (_dilationB) { Destroy(_dilationB); _dilationB = null; }
            for (int slot = 0; slot < _ownedRawDepth.Length; slot++)
            {
                if (_ownedRawDepth[slot]) Destroy(_ownedRawDepth[slot]);
                _ownedRawDepth[slot] = null;
            }
            if (_filteredDepthTex) { Destroy(_filteredDepthTex); _filteredDepthTex = null; }
            _dilatedDepth = null;
            _depthTex = null;
            _depthFrameRequested = false;
            _requestedDepthSlot = -1;
            _readyDepthSlot = -1;
            _heldDepthSlot = -1;
            _processedRawFrameVersion = _latestRawFrameVersion;
            Logger.Info("DepthCapture: GPU resources released");
        }

        private void Update()
        {
            float t = Time.unscaledTime;
            if (t - _lastLogTime >= 5f)
            {
                _lastLogTime = t;
                var sub = _arOcclusionManager != null ? _arOcclusionManager.subsystem : null;
                Logger.Info($"DepthCapture: rawFrames={_frameCount}, preprocessed={_preprocessedFrameCount}, " +
                          $"ready={_readyDepthSlot >= 0}, held={_heldDepthSlot >= 0}, " +
                          $"depthAvail={DepthAvailable}, " +
                          $"occMgr.enabled={_arOcclusionManager?.enabled}, sub={sub?.GetType().Name ?? "null"}, " +
                          $"running={sub?.running}");
            }
        }

        private void OnDepthFrame(AROcclusionFrameEventArgs args)
        {
            _frameCount++;
            if (_frameCount <= 3 || _frameCount % 100 == 0)
                Logger.Info($"OnDepthFrame #{_frameCount}, textures={args.externalTextures.Count}");

            if (!_depthFrameRequested || _requestedDepthSlot < 0) return;
            if (Application.isEditor) HandleEditorSimulation(args);
            else HandleDeviceDepth(args);
        }

        /// <summary>
        /// Preprocesses exactly the latest raw frame for the integration tick that will
        /// consume it. Intermediate sensor frames are intentionally never filtered,
        /// normalised, or dilated.
        /// </summary>
        public bool ConsumeLatestDepthFrame()
        {
            CommandBuffer command = CommandBufferPool.Get(
                "Merkaba depth preprocess");
            try
            {
                bool consumed = ConsumeLatestDepthFrame(command);
                if (consumed) Graphics.ExecuteCommandBuffer(command);
                return consumed;
            }
            finally
            {
                CommandBufferPool.Release(command);
            }
        }

        internal bool ConsumeLatestDepthFrame(CommandBuffer command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (!DepthAvailable || _readyDepthSlot < 0 ||
                _heldDepthSlot >= 0 ||
                _ownedRawDepth[_readyDepthSlot] == null ||
                !ShouldPreprocessFrame(_ownedVersions[_readyDepthSlot],
                    _processedRawFrameVersion))
                return false;

            _heldDepthSlot = _readyDepthSlot;
            _readyDepthSlot = -1;
            for (int eye = 0; eye < 2; eye++)
            {
                _proj[eye] = _ownedProj[_heldDepthSlot, eye];
                _projInv[eye] = _ownedProjInv[_heldDepthSlot, eye];
                _view[eye] = _ownedView[_heldDepthSlot, eye];
                _viewInv[eye] = _ownedViewInv[_heldDepthSlot, eye];
            }
            _planes = _ownedPlanes[_heldDepthSlot];
            _depthTex = _ownedRawDepth[_heldDepthSlot];
            ApplyBilateralFilter(command);
            SetGlobalShaderProperties();
            ComputeNormals(command);
            ComputeDilation(command);
            _processedRawFrameVersion = _ownedVersions[_heldDepthSlot];
            _preprocessedFrameCount++;
            Updated?.Invoke();
            return true;
        }

        public void ReleaseConsumedObservation()
        {
            _heldDepthSlot = -1;
        }

        internal static bool ShouldPreprocessFrame(int latestRawVersion,
            int processedRawVersion) => latestRawVersion != 0 &&
                                        latestRawVersion != processedRawVersion;

        internal static int[] BuildDilationStepSequence(int maximumExponent)
        {
            if (maximumExponent is < 0 or > 30)
                throw new ArgumentOutOfRangeException(nameof(maximumExponent));
            var result = new int[maximumExponent + 1];
            int step = 1 << maximumExponent;
            for (int index = 0; index < result.Length; index++)
            {
                result[index] = step;
                step >>= 1;
            }
            return result;
        }

        private void HandleEditorSimulation(AROcclusionFrameEventArgs args)
        {
            if (args.externalTextures.Count == 0) return;
            Texture rawDepth = args.externalTextures[0].texture;
            if (rawDepth == null) return;

            if (!_mainCam) _mainCam = Camera.main;
            if (!_mainCam) return;

            Matrix4x4 p = _mainCam.projectionMatrix;
            Matrix4x4 pi = p.inverse;
            Transform ct = _mainCam.transform;
            Matrix4x4 vi = Matrix4x4.TRS(ct.position, ct.rotation, ScaleFlipZ);
            Matrix4x4 v = vi.inverse;

            for (int i = 0; i < 2; i++)
            {
                _ownedProj[_requestedDepthSlot, i] = p;
                _ownedProjInv[_requestedDepthSlot, i] = pi;
                _ownedView[_requestedDepthSlot, i] = v;
                _ownedViewInv[_requestedDepthSlot, i] = vi;
            }

            _ownedPlanes[_requestedDepthSlot] = new Vector2(_mainCam.nearClipPlane,
                _mainCam.farClipPlane);

            EnsureOwnedRawDepth(_requestedDepthSlot, rawDepth.width, rawDepth.height);

            depthNormalCompute.SetVector(ZParamsID,
                _ownedPlanes[_requestedDepthSlot]);
            _monoConvertKernel.Set(DepthTexRWID,
                _ownedRawDepth[_requestedDepthSlot]);
            _monoConvertKernel.Set(InputRawMonoDepthID, rawDepth);
            _monoConvertKernel.DispatchFit(rawDepth.width, rawDepth.height);
            MarkOwnedDepthSnapshotReady();
        }

        private void HandleDeviceDepth(AROcclusionFrameEventArgs args)
        {
            if (args.externalTextures.Count == 0) return;
            Texture rawDepth = args.externalTextures[0].texture;

            ReadOnlyList<XRFov> fovs = default;
            ReadOnlyList<Pose> poses = default;
            XRNearFarPlanes depthPlanes = default;

            if (rawDepth == null || rawDepth.dimension != TextureDimension.Tex2DArray ||
                !args.TryGetFovs(out fovs) || !args.TryGetPoses(out poses) ||
                !args.TryGetNearFarPlanes(out depthPlanes) ||
                fovs.Count < 2 || poses.Count < 2)
                return;

            for (int i = 0; i < 2; i++)
            {
                _ownedProj[_requestedDepthSlot, i] =
                    CalculateProjectionMatrix(fovs[i], depthPlanes);
                _ownedProjInv[_requestedDepthSlot, i] = Matrix4x4.Inverse(
                    _ownedProj[_requestedDepthSlot, i]);

                Pose pose = poses[i];
                Matrix4x4 depthFrameMat = Matrix4x4.TRS(pose.position, pose.rotation, ScaleFlipZ);

                Matrix4x4 worldToTracking = _trackingSpaceTransform != null
                    ? _trackingSpaceTransform.worldToLocalMatrix
                    : Matrix4x4.identity;

                _ownedView[_requestedDepthSlot, i] =
                    depthFrameMat.inverse * worldToTracking;
                _ownedViewInv[_requestedDepthSlot, i] = Matrix4x4.Inverse(
                    _ownedView[_requestedDepthSlot, i]);
            }

            _ownedPlanes[_requestedDepthSlot] = new Vector2(
                depthPlanes.nearZ, depthPlanes.farZ);
            TryLatchDepthSnapshot(rawDepth);
        }

        internal bool TryLatchDepthSnapshot(Texture transientDepth)
        {
            if (!_depthFrameRequested || _requestedDepthSlot < 0 ||
                transientDepth == null ||
                transientDepth.dimension != TextureDimension.Tex2DArray)
                return false;

            int slot = _requestedDepthSlot;
            EnsureOwnedRawDepth(slot, transientDepth.width, transientDepth.height);
            _projectionDepthCopyKernel.Set(InputProjectionDepthID, transientDepth);
            _projectionDepthCopyKernel.Set(DepthTexRWID, _ownedRawDepth[slot]);
            _projectionDepthCopyKernel.DispatchFit(transientDepth.width,
                transientDepth.height, 2);
            MarkOwnedDepthSnapshotReady();
            return true;
        }

        private void EnsureOwnedRawDepth(int slot, int width, int height)
        {
            RenderTexture owned = _ownedRawDepth[slot];
            if (owned != null && owned.width == width &&
                owned.height == height &&
                owned.graphicsFormat == GraphicsFormat.R32_SFloat)
                return;

            if (owned) Destroy(owned);
            owned = new RenderTexture(width, height, 0,
                GraphicsFormat.R32_SFloat, 1)
            {
                dimension = TextureDimension.Tex2DArray,
                volumeDepth = 2,
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            owned.Create();
            _ownedRawDepth[slot] = owned;
        }

        private void MarkOwnedDepthSnapshotReady()
        {
            int slot = _requestedDepthSlot;
            _depthFrameRequested = false;
            _requestedDepthSlot = -1;
            _readyDepthSlot = slot;
            DepthAvailable = true;
            unchecked
            {
                _latestRawFrameVersion++;
                if (_latestRawFrameVersion == 0) _latestRawFrameVersion = 1;
            }
            _ownedVersions[slot] = _latestRawFrameVersion;
        }

        private bool _loggedBilateralSkip;
        private void ApplyBilateralFilter(CommandBuffer command)
        {
            if (!enableBilateralFilter || !_hasBilateralKernel || _rgbGuide == null || _depthTex == null)
            {
                if (!_loggedBilateralSkip && enableBilateralFilter && _hasBilateralKernel && _rgbGuide == null)
                {
                    _loggedBilateralSkip = true;
                    Logger.Info("Bilateral depth filter skipped — no RGB guide (camera unavailable). " +
                              "Depth will be noisier at edges.");
                }
                return;
            }

            int w = _depthTex.width;
            int h = _depthTex.height;

            if (_filteredDepthTex == null || _filteredDepthTex.width != w || _filteredDepthTex.height != h)
            {
                if (_filteredDepthTex) Destroy(_filteredDepthTex);
                _filteredDepthTex = new RenderTexture(w, h, 0, GraphicsFormat.R16_UNorm, 1)
                {
                    dimension = TextureDimension.Tex2DArray,
                    volumeDepth = 2,
                    enableRandomWrite = true,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                _filteredDepthTex.Create();
            }

            var cs = bilateralFilterCompute;
            _bilateralKernel.Set(command, BilSrcDepthID, _depthTex);
            _bilateralKernel.Set(command, BilRGBGuideID, _rgbGuide);
            _bilateralKernel.Set(command, BilDstDepthID, _filteredDepthTex);
            command.SetComputeIntParam(cs, BilDepthWID, w);
            command.SetComputeIntParam(cs, BilDepthHID, h);
            command.SetComputeFloatParam(cs, BilSigmaSpatialID, sigmaSpatial);
            command.SetComputeFloatParam(cs, BilSigmaColorID, sigmaColor);
            command.SetComputeFloatParam(cs, BilSigmaDepthID, sigmaDepth);
            command.SetComputeIntParam(cs, BilFilterRadiusID, filterRadius);

            _bilateralKernel.DispatchFit(command, w, h, 2);

            _depthTex = _filteredDepthTex;
        }

        private void SetGlobalShaderProperties()
        {
            Shader.SetGlobalMatrixArray(ProjID, _proj);
            Shader.SetGlobalMatrixArray(ProjInvID, _projInv);
            Shader.SetGlobalMatrixArray(ViewID, _view);
            Shader.SetGlobalMatrixArray(ViewInvID, _viewInv);
            Shader.SetGlobalVector(ZParamsID, _planes);
            Shader.SetGlobalVector(TexSizeID, new Vector2(_depthTex.width, _depthTex.height));
            Shader.SetGlobalTexture(DepthTexID, _depthTex);
        }

        private void ComputeNormals(CommandBuffer command)
        {
            if (_normTex == null || _normTex.width != _depthTex.width || _normTex.height != _depthTex.height)
            {
                if (_normTex) Destroy(_normTex);
                _normTex = new RenderTexture(_depthTex.width, _depthTex.height, 0,
                    GraphicsFormat.R8G8B8A8_SNorm, 1)
                {
                    dimension = TextureDimension.Tex2DArray,
                    volumeDepth = 2,
                    useMipMap = false,
                    enableRandomWrite = true
                };
                _normTex.Create();
            }

            _normKernel.Set(command, DepthTexID, _depthTex);
            _normKernel.Set(command, NormTexRWID, _normTex);
            _normKernel.DispatchFit(command, _normTex);
            Shader.SetGlobalTexture(NormTexID, _normTex);
        }

        private void ComputeDilation(CommandBuffer command)
        {
            if (_dilationA == null || _dilationA.width != _depthTex.width || _dilationA.height != _depthTex.height)
            {
                if (_dilationA) Destroy(_dilationA);
                if (_dilationB) Destroy(_dilationB);

                var desc = new RenderTextureDescriptor
                {
                    width = _depthTex.width,
                    height = _depthTex.height,
                    volumeDepth = 2,
                    dimension = TextureDimension.Tex2DArray,
                    autoGenerateMips = false,
                    enableRandomWrite = true,
                    graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat,
                    msaaSamples = 1
                };

                _dilationA = new RenderTexture(desc);
                _dilationB = new RenderTexture(desc);
                _dilationA.Create();
                _dilationB.Create();
            }

            command.SetComputeFloatParam(depthDilationCompute, VoxDistID,
                voxelDistance);
            command.SetComputeFloatParam(depthDilationCompute, VoxSizeShaderID,
                voxelSize);

            _initDilateKernel.Set(command, DepthTexID, _depthTex);
            _initDilateKernel.Set(command, DilateSrcID, _dilationA);
            _initDilateKernel.Set(command, DilateDestID, _dilationB);
            _initDilateKernel.DispatchFit(command, _dilationA.width,
                _dilationA.height, 2);

            foreach (int stepSize in BuildDilationStepSequence(dilationSteps))
            {
                _dilateStepKernel.Set(command, DilateSrcID, _dilationA);
                _dilateStepKernel.Set(command, DilateDestID, _dilationB);
                command.SetComputeIntParam(depthDilationCompute,
                    DilateStepSizeID, stepSize);
                _dilateStepKernel.DispatchFit(command, _dilationA.width,
                    _dilationA.height, 2);

                (_dilationA, _dilationB) = (_dilationB, _dilationA);
            }

            _dilatedDepth = _dilationA;
            Shader.SetGlobalTexture(DilatedDepthTexID, _dilatedDepth);
        }

        private static Matrix4x4 CalculateProjectionMatrix(XRFov fov, XRNearFarPlanes planes)
        {
            float left = Mathf.Tan(fov.angleLeft);
            float right = Mathf.Tan(fov.angleRight);
            float bottom = Mathf.Tan(fov.angleDown);
            float top = Mathf.Tan(fov.angleUp);

            float near = planes.nearZ;
            float far = planes.farZ;

            float x = 2.0f / (right - left);
            float y = 2.0f / (top - bottom);
            float a = (right + left) / (right - left);
            float b = (top + bottom) / (top - bottom);

            float c, d;
            if (float.IsInfinity(far))
            {
                c = -1.0f;
                d = -2.0f * near;
            }
            else
            {
                c = -(far + near) / (far - near);
                d = -(2.0f * far * near) / (far - near);
            }

            return new Matrix4x4
            {
                m00 = x,  m01 = 0, m02 = a,  m03 = 0,
                m10 = 0,  m11 = y, m12 = b,  m13 = 0,
                m20 = 0,  m21 = 0, m22 = c,  m23 = d,
                m30 = 0,  m31 = 0, m32 = -1, m33 = 0
            };
        }

        /// <summary>
        /// Update lattice parameters used by dilation when the integrator changes them.
        /// </summary>
        public void SetVoxelParams(float voxDist, float voxSize)
        {
            voxelDistance = voxDist;
            voxelSize = voxSize;
        }
    }
}
