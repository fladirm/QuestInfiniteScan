using System;
using System.IO;
using System.Threading.Tasks;
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
    /// Captures stereo depth from the AR occlusion subsystem, runs the mandatory
    /// true-stereo RGB-D joint solve, and produces
    /// dilated depth textures consumed by <see cref="MerkabaIntegrator"/> for reversible
    /// surface/free-space evidence integration.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public class DepthCapture : MonoBehaviour
    {
        public static DepthCapture Instance { get; private set; }

        [SerializeField] private ComputeShader depthNormalCompute;
        [SerializeField] private ComputeShader depthDilationCompute;
        [SerializeField] private ComputeShader stereoRgbdRefineCompute;
        [SerializeField] private bool dynamicOcclusionEnabled = true;

        [Header("Dilation")]
        [SerializeField, Range(0, 12)] private int dilationSteps = 8;

        private readonly Matrix4x4[] _proj = new Matrix4x4[2];
        private readonly Matrix4x4[] _projInv = new Matrix4x4[2];
        private readonly Matrix4x4[] _view = new Matrix4x4[2];
        private readonly Matrix4x4[] _viewInv = new Matrix4x4[2];
        private Vector2 _planes;

        /// <summary>Per-eye projection matrices derived from the depth frame's FOV and near/far planes.</summary>
        public Matrix4x4[] Proj => _proj;
        /// <summary>Inverse projection matrices (per-eye).</summary>
        public Matrix4x4[] ProjInv => _projInv;
        /// <summary>Per-eye view matrices (Unity world to depth-camera space).</summary>
        public Matrix4x4[] View => _view;
        /// <summary>Inverse view matrices (per-eye), mapping depth-camera space to Unity world.</summary>
        public Matrix4x4[] ViewInv => _viewInv;
        /// <summary>Near and far clip distances (x = near, y = far) for the current depth frame.</summary>
        public Vector2 Planes => _planes;

        // Shader property IDs
        public static readonly int DepthTexID = Shader.PropertyToID("gsDepthTex");
        public static readonly int DepthTexRWID = Shader.PropertyToID("gsDepthTexRW");
        public static readonly int TexSizeID = Shader.PropertyToID("gsDepthTexSize");
        public static readonly int NormTexID = Shader.PropertyToID("gsDepthNormalTex");
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

        private static readonly int RefineSrcDepthId =
            Shader.PropertyToID("_SrcDepth");
        private static readonly int RefineDstDepthId =
            Shader.PropertyToID("_DstDepth");
        private static readonly int RefineDstNormalId =
            Shader.PropertyToID("_DstNormal");
        private static readonly int RefineDepthWidthId =
            Shader.PropertyToID("_DepthW");
        private static readonly int RefineDepthHeightId =
            Shader.PropertyToID("_DepthH");
        private static readonly int RefineDepthProjId =
            Shader.PropertyToID("_DepthProj");
        private static readonly int RefineDepthProjInvId =
            Shader.PropertyToID("_DepthProjInv");
        private static readonly int RefineDepthViewId =
            Shader.PropertyToID("_DepthView");
        private static readonly int RefineDepthViewInvId =
            Shader.PropertyToID("_DepthViewInv");
        private static readonly int RefineMetricsId =
            Shader.PropertyToID("_RefineMetrics");
        private static readonly int RefineMetricsEnabledId =
            Shader.PropertyToID("_RefineMetricsEnabled");
        private static readonly int RefineMetricGroupsXId =
            Shader.PropertyToID("_RefineMetricGroupsX");
        private static readonly int RefineFineActiveId =
            Shader.PropertyToID("_M8FineRefineActive");
        private static readonly int RefineFineEyeOriginId =
            Shader.PropertyToID("_M8FineEyeOrigin");
        private static readonly int RefineFineBrushAxisId =
            Shader.PropertyToID("_M8FineBrushAxis");
        private static readonly int RefineFineCosHalfAngleSquaredId =
            Shader.PropertyToID("_M8FineCosHalfAngleSquared");
        private static readonly int RefineFineToolDepthSquaredId =
            Shader.PropertyToID("_M8FineToolDepthSquared");
        private static readonly int FineDepthId =
            Shader.PropertyToID("gsFineDepth");
        private static readonly int FineTargetId =
            Shader.PropertyToID("gsFineTarget");
        private static readonly int FineDepthProjId =
            Shader.PropertyToID("gsFineDepthProj");
        private static readonly int FineDepthProjInvId =
            Shader.PropertyToID("gsFineDepthProjInv");
        private static readonly int FineDepthViewId =
            Shader.PropertyToID("gsFineDepthView");
        private static readonly int FineDepthViewInvId =
            Shader.PropertyToID("gsFineDepthViewInv");
        private static readonly int FineRayOriginId =
            Shader.PropertyToID("gsFineRayOrigin");
        private static readonly int FineRayDirectionId =
            Shader.PropertyToID("gsFineRayDirection");
        private static readonly int FineMaxDistanceId =
            Shader.PropertyToID("gsFineMaxDistance");
        private static readonly int FineDepthSizeId =
            Shader.PropertyToID("gsFineDepthSize");
        private static readonly int[] RefineCameraRgbId =
        {
            Shader.PropertyToID("_MerkabaCameraRgbLeft"),
            Shader.PropertyToID("_MerkabaCameraRgbRight")
        };
        private static readonly int[] RefineCameraPositionId =
        {
            Shader.PropertyToID("_MerkabaCameraPositionLeft"),
            Shader.PropertyToID("_MerkabaCameraPositionRight")
        };
        private static readonly int[] RefineCameraInverseRotationId =
        {
            Shader.PropertyToID("_MerkabaCameraInverseRotationLeft"),
            Shader.PropertyToID("_MerkabaCameraInverseRotationRight")
        };
        private static readonly int[] RefineCameraFocalLengthId =
        {
            Shader.PropertyToID("_MerkabaCameraFocalLengthLeft"),
            Shader.PropertyToID("_MerkabaCameraFocalLengthRight")
        };
        private static readonly int[] RefineCameraPrincipalPointId =
        {
            Shader.PropertyToID("_MerkabaCameraPrincipalPointLeft"),
            Shader.PropertyToID("_MerkabaCameraPrincipalPointRight")
        };
        private static readonly int[] RefineCameraSensorResolutionId =
        {
            Shader.PropertyToID("_MerkabaCameraSensorResolutionLeft"),
            Shader.PropertyToID("_MerkabaCameraSensorResolutionRight")
        };
        private static readonly int[] RefineCameraCurrentResolutionId =
        {
            Shader.PropertyToID("_MerkabaCameraCurrentResolutionLeft"),
            Shader.PropertyToID("_MerkabaCameraCurrentResolutionRight")
        };

        /// <summary>True once a valid depth frame has been received from the AR occlusion subsystem.</summary>
        public static bool DepthAvailable { get; private set; }

        /// <summary>
        /// True after USE_SCENE permission is confirmed. Runtime readiness is
        /// established separately before scanning is admitted.
        /// </summary>
        private bool _permissionReady;

        /// <summary>
        /// Tracks whether the caller (RoomScanner) wants depth capture active.
        /// Persists across app pause/resume so the subsystem is re-enabled correctly.
        /// </summary>
        private bool _captureActive;

        private ComputeKernelHelper _projectionDepthCopyKernel;
        private ComputeKernelHelper _monoConvertKernel;
        private ComputeKernelHelper _fineSurfaceTargetKernel;
        private ComputeKernelHelper _initDilateKernel;
        private ComputeKernelHelper _dilateStepKernel;
        private ComputeKernelHelper _stereoRgbdRefineKernel;

        private readonly RenderTexture[] _ownedRawDepth = new RenderTexture[2];
        private readonly Matrix4x4[,] _ownedProj = new Matrix4x4[2, 2];
        private readonly Matrix4x4[,] _ownedProjInv = new Matrix4x4[2, 2];
        private readonly Matrix4x4[,] _ownedView = new Matrix4x4[2, 2];
        private readonly Matrix4x4[,] _ownedViewInv = new Matrix4x4[2, 2];
        private readonly Vector2[] _ownedPlanes = new Vector2[2];
        private readonly int[] _ownedVersions = new int[2];
        private readonly long[] _ownedTimestampNs = new long[2];
        private readonly SensorClockMapper _depthClock = new();
        private int _requestedDepthSlot = -1;
        private int _readyDepthSlot = -1;
        private int _heldDepthSlot = -1;
        private int _latestOwnedDepthSlot = -1;
        private int _readoutDepthLeaseSlot = -1;
        private int _readoutDepthLeaseVersion;
        private Texture _depthTex;
        /// <summary>The latest depth frame actually preprocessed for integration.</summary>
        public Texture DepthTex => _depthTex;

        private RenderTexture _normTex;
        /// <summary>Joint world-space normal from the same four-stream solve as DepthTex.</summary>
        public RenderTexture NormTex => _normTex;

        private RenderTexture _dilationA, _dilationB;
        private RenderTexture _dilatedDepth;
        /// <summary>Depth texture after jump-flood dilation, used by the integrator to fill holes near voxel boundaries.</summary>
        public RenderTexture DilatedDepthTex => _dilatedDepth;

        private RenderTexture _refinedDepthTex;
        private ComputeBuffer _refineMetrics;
        private int _refineMetricValueCount;
        private uint _refineMetricsRevision;
        private ComputeBuffer _fineSurfaceTarget;
        private bool _fineSurfaceTargetReadbackPending;
        private bool _fineSurfaceTargetValid;
        private Vector3 _fineSurfaceTargetWorld;
        private Vector3 _fineSurfaceTargetNormal;
        private float _nextFineSurfaceTarget;
        private uint _fineSurfaceTargetGeneration = 1u;
        private uint _fineSurfaceTargetIssuedSequence;
        private uint _fineSurfaceTargetCompletedSequence;

        private AROcclusionManager _arOcclusionManager;
        private ARShaderOcclusion _shaderOcclusion;
        private Camera _mainCam;
        private int _frameCount;
        private int _preprocessedFrameCount;
        private int _latestRawFrameVersion;
        private int _processedRawFrameVersion;
        private bool _depthFrameRequested;
        private ulong _copySubmittedEpoch;
        private ulong _copyRetiredEpoch;
        private int _lastCopySlot = -1;
        private Task _copyRetirementTask = Task.CompletedTask;
        private float _lastLogTime;
        private readonly TaskCompletionSource<bool> _scenePermissionCompletion =
            new();
        private Task<bool> _environmentDepthReadyTask =
            Task.FromResult(false);
        private bool _environmentDepthReadyTaskRequiresFreshFrame;
        private bool _environmentDepthSuspended;
        private uint _environmentDepthGeneration = 1u;
        private uint _environmentDepthFrameSequence;

        private static readonly int EnvironmentDepthTextureId =
            Shader.PropertyToID("_EnvironmentDepthTexture");
        private static readonly int IsOcclusionOnId =
            Shader.PropertyToID("_IsOcclusionOn");

        public bool DynamicOcclusionEnabled
        {
            get => dynamicOcclusionEnabled;
            set
            {
                if (dynamicOcclusionEnabled == value) return;
                dynamicOcclusionEnabled = value;
                ApplyDynamicOcclusionState();
                if (value)
                    _ = EnsureEnvironmentDepthRunningAsync(false, true);
                Logger.Info("DepthCapture: dynamic passthrough occlusion " +
                    (value ? "enabled" : "disabled"));
            }
        }

        internal int LatestRawFrameVersion => _latestRawFrameVersion;
        internal int ProcessedRawFrameVersion => _processedRawFrameVersion;
        internal int PreprocessedFrameCount => _preprocessedFrameCount;
        internal bool DepthFrameRequested => _depthFrameRequested;
        internal ulong CopySubmittedEpoch => _copySubmittedEpoch;
        internal ulong CopyRetiredEpoch => _copyRetiredEpoch;
        internal bool OwnedDepthSnapshotReady => _readyDepthSlot >= 0;
        internal double TimestampMappingUncertaintySeconds =>
            _depthClock.UncertaintySeconds;
        internal RenderTexture OwnedRawDepthSnapshot => _readyDepthSlot >= 0
            ? _ownedRawDepth[_readyDepthSlot]
            : _heldDepthSlot >= 0 ? _ownedRawDepth[_heldDepthSlot] : null;
        internal ComputeBuffer RefineMetrics => _refineMetrics;
        internal bool FineSurfaceTargetReadbackPending =>
            _fineSurfaceTargetReadbackPending;
        internal uint FineSurfaceTargetIssuedSequence =>
            _fineSurfaceTargetIssuedSequence;
        internal uint FineSurfaceTargetCompletedSequence =>
            _fineSurfaceTargetCompletedSequence;
        public bool HasUnprocessedFrame => DepthAvailable &&
            _readyDepthSlot >= 0 && _heldDepthSlot < 0 &&
            _ownedRawDepth[_readyDepthSlot] != null &&
            ShouldPreprocessFrame(_ownedVersions[_readyDepthSlot],
                _processedRawFrameVersion);

        private const string ScenePermission = "com.oculus.permission.USE_SCENE";

        /// <summary>Raised only after an integration consumer preprocesses the latest frame.</summary>
        public event Action Updated;

        internal readonly struct ReadoutDepthLease
        {
            internal readonly int Slot;
            internal readonly int Version;
            internal readonly RenderTexture Texture;
            internal readonly Matrix4x4 Proj0;
            internal readonly Matrix4x4 Proj1;
            internal readonly Matrix4x4 ProjInv0;
            internal readonly Matrix4x4 ProjInv1;
            internal readonly Matrix4x4 View0;
            internal readonly Matrix4x4 View1;
            internal readonly Matrix4x4 ViewInv0;
            internal readonly Matrix4x4 ViewInv1;

            internal ReadoutDepthLease(int slot, int version,
                RenderTexture texture, Matrix4x4 proj0, Matrix4x4 proj1,
                Matrix4x4 projInv0, Matrix4x4 projInv1,
                Matrix4x4 view0, Matrix4x4 view1,
                Matrix4x4 viewInv0, Matrix4x4 viewInv1)
            {
                Slot = slot;
                Version = version;
                Texture = texture;
                Proj0 = proj0;
                Proj1 = proj1;
                ProjInv0 = projInv0;
                ProjInv1 = projInv1;
                View0 = view0;
                View1 = view1;
                ViewInv0 = viewInv0;
                ViewInv1 = viewInv1;
            }

            internal bool IsValid => Slot >= 0 && Version != 0 &&
                Texture != null;
        }

        private static readonly Vector3 ScaleFlipZ = new(1, 1, -1);

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

            _projectionDepthCopyKernel = new ComputeKernelHelper(depthNormalCompute,
                "CopyProjectionDepthArray");
            _monoConvertKernel = new ComputeKernelHelper(depthNormalCompute, "MonoRawDepthToStereo");
            _fineSurfaceTargetKernel = new ComputeKernelHelper(depthNormalCompute,
                "FineSurfaceTarget");
            _initDilateKernel = new ComputeKernelHelper(depthDilationCompute, "InitDepthDilation");
            _dilateStepKernel = new ComputeKernelHelper(depthDilationCompute, "DilateDepthStep");
            MerkabaGpuTimestamps.RegisterKernel(depthNormalCompute,
                _projectionDepthCopyKernel.KernelIndex,
                MerkabaGpuStage.DepthPreprocess,
                "CopyProjectionDepthArray");
            MerkabaGpuTimestamps.RegisterKernel(depthDilationCompute,
                _initDilateKernel.KernelIndex, MerkabaGpuStage.DepthPreprocess,
                "InitDepthDilation");
            MerkabaGpuTimestamps.RegisterKernel(depthDilationCompute,
                _dilateStepKernel.KernelIndex, MerkabaGpuStage.DepthPreprocess,
                "DilateDepthStep");
            if (stereoRgbdRefineCompute == null)
                throw new Exception("[RoomScan] StereoRgbdRefine compute is required");
            _stereoRgbdRefineKernel = new ComputeKernelHelper(
                stereoRgbdRefineCompute, "StereoRgbdRefine");
            MerkabaGpuTimestamps.RegisterKernel(stereoRgbdRefineCompute,
                _stereoRgbdRefineKernel.KernelIndex,
                MerkabaGpuStage.DepthPreprocess, "StereoRgbdRefine");

            // Disable occlusion manager initially, enable after permission is confirmed
            _arOcclusionManager.enabled = false;
            EnsureDynamicOcclusion();
            CheckPermissionAndEnable();

        }

        private void EnsureDynamicOcclusion()
        {
            ARShaderOcclusion[] components =
                _arOcclusionManager.GetComponents<ARShaderOcclusion>();
            if (components.Length > 1)
                throw new Exception("[RoomScan] Multiple ARShaderOcclusion components found");
            _shaderOcclusion = components.Length == 1
                ? components[0]
                : _arOcclusionManager.gameObject.AddComponent<ARShaderOcclusion>();
            _shaderOcclusion.occlusionShaderMode =
                AROcclusionShaderMode.HardOcclusion;
            _shaderOcclusion.enabled = dynamicOcclusionEnabled;
            Logger.Info("DepthCapture: dynamic passthrough occlusion configured " +
                "(AR Foundation hard mode, shared environment depth)");
        }

        private void ApplyDynamicOcclusionState()
        {
            if (_shaderOcclusion != null)
                _shaderOcclusion.enabled = dynamicOcclusionEnabled &&
                    !_environmentDepthSuspended;
            if (!_permissionReady || _arOcclusionManager == null) return;
            bool depthRequired = !_environmentDepthSuspended &&
                (_captureActive || dynamicOcclusionEnabled);
            UpdateDepthFrameSubscription(depthRequired);
            if (_arOcclusionManager.enabled != depthRequired)
                _arOcclusionManager.enabled = depthRequired;
        }

        private void UpdateDepthFrameSubscription(bool subscribe)
        {
            if (_arOcclusionManager == null) return;
            if (subscribe && !_subscribed)
            {
                _arOcclusionManager.frameReceived += OnDepthFrame;
                _subscribed = true;
            }
            else if (!subscribe && _subscribed)
            {
                _arOcclusionManager.frameReceived -= OnDepthFrame;
                _subscribed = false;
            }
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
                CompleteScenePermission(true);
            }
            else
            {
                Logger.Info("Requesting USE_SCENE permission...");
                var callbacks = new PermissionCallbacks();
                callbacks.PermissionGranted += _ =>
                    CompleteScenePermission(true);
                callbacks.PermissionDenied += _ =>
                    CompleteScenePermission(false);
                Permission.RequestUserPermission(ScenePermission, callbacks);
            }
#else
            CompleteScenePermission(true);
#endif
        }

        private bool _subscribed;

        private void CompleteScenePermission(bool granted)
        {
            if (_scenePermissionCompletion.Task.IsCompleted) return;
            _permissionReady = granted;
            _scenePermissionCompletion.TrySetResult(granted);
            if (!granted)
            {
                Logger.Error("USE_SCENE permission denied — environment " +
                    "depth and scanning are unavailable.");
                return;
            }
            Logger.Info("USE_SCENE permission granted.");
            if (dynamicOcclusionEnabled || _captureActive)
                _ = EnsureEnvironmentDepthRunningAsync(false, true);
        }

        /// <summary>
        /// Starts the scanner's depth consumer and does not complete until a
        /// new owned stereo Environment Depth snapshot is available.
        /// </summary>
        internal async Task<bool> StartDepthCaptureAsync(
            float timeoutSeconds = 10f)
        {
            if (!_captureActive)
            {
                _depthClock.Reset();
                _depthClock.TryCaptureAnchor();
            }
            _captureActive = true;
            _environmentDepthSuspended = false;
            ApplyDynamicOcclusionState();
            if (!await EnsureEnvironmentDepthRunningAsync(false, true))
                return false;

            int baselineVersion = _latestRawFrameVersion;
            float deadline = Time.realtimeSinceStartup +
                Mathf.Max(0.1f, timeoutSeconds);
            bool requested = false;
            while (_captureActive && isActiveAndEnabled &&
                   Time.realtimeSinceStartup < deadline)
            {
                if (!requested)
                    requested = RequestFreshDepthFrame();
                if (HasUnprocessedFrame &&
                    _latestRawFrameVersion != baselineVersion)
                {
                    Logger.Info("DepthCapture: fresh owned Environment Depth " +
                        $"snapshot ready, version={_latestRawFrameVersion}.");
                    return true;
                }
                await Task.Yield();
            }
            Logger.Error("DepthCapture: timed out waiting for a fresh owned " +
                $"Environment Depth snapshot after {timeoutSeconds:F1}s.");
            return false;
        }

        private Task<bool> EnsureEnvironmentDepthRunningAsync(
            bool forceRestart, bool requireFreshFrame,
            float timeoutSeconds = 10f)
        {
            if (_environmentDepthSuspended)
                return Task.FromResult(false);
            if (!forceRestart && EnvironmentDepthIsRunning() &&
                (!requireFreshFrame || _environmentDepthFrameSequence != 0u))
            {
                ApplyDynamicOcclusionState();
                return Task.FromResult(true);
            }
            if (!forceRestart && _environmentDepthReadyTask != null &&
                !_environmentDepthReadyTask.IsCompleted &&
                (!requireFreshFrame ||
                 _environmentDepthReadyTaskRequiresFreshFrame))
                return _environmentDepthReadyTask;

            uint generation = NextEnvironmentDepthGeneration();
            _environmentDepthReadyTaskRequiresFreshFrame = requireFreshFrame;
            _environmentDepthReadyTask =
                EnableEnvironmentDepthCoreAsync(generation, forceRestart,
                    requireFreshFrame, timeoutSeconds);
            return _environmentDepthReadyTask;
        }

        private async Task<bool> EnableEnvironmentDepthCoreAsync(
            uint generation, bool forceRestart, bool requireFreshFrame,
            float timeoutSeconds)
        {
            bool permission = _permissionReady ||
                await _scenePermissionCompletion.Task;
            if (!permission || generation != _environmentDepthGeneration ||
                _arOcclusionManager == null)
                return false;

            uint baselineFrame = _environmentDepthFrameSequence;
            if (forceRestart && _arOcclusionManager.enabled)
            {
                UpdateDepthFrameSubscription(false);
                _arOcclusionManager.enabled = false;
                await Task.Yield();
                if (generation != _environmentDepthGeneration)
                    return false;
            }

            ApplyDynamicOcclusionState();
            float deadline = Time.realtimeSinceStartup +
                Mathf.Max(0.1f, timeoutSeconds);
            while (generation == _environmentDepthGeneration &&
                   !_environmentDepthSuspended &&
                   Time.realtimeSinceStartup < deadline)
            {
                if (EnvironmentDepthIsRunning() &&
                    (!requireFreshFrame ||
                     _environmentDepthFrameSequence != baselineFrame))
                {
                    Logger.Info("DepthCapture: Environment Depth ready " +
                        $"(running=true, frame=" +
                        $"{_environmentDepthFrameSequence}).");
                    return true;
                }
                await Task.Yield();
            }
            if (generation == _environmentDepthGeneration)
                Logger.Error("DepthCapture: Environment Depth readiness " +
                    $"timed out after {timeoutSeconds:F1}s " +
                    $"(manager={_arOcclusionManager.enabled}, " +
                    $"running={_arOcclusionManager.subsystem?.running}, " +
                    $"freshFrame=" +
                    $"{_environmentDepthFrameSequence != baselineFrame}).");
            return false;
        }

        private bool EnvironmentDepthIsRunning() =>
            _arOcclusionManager != null && _arOcclusionManager.enabled &&
            _arOcclusionManager.subsystem != null &&
            _arOcclusionManager.subsystem.running;

        private uint NextEnvironmentDepthGeneration()
        {
            unchecked
            {
                _environmentDepthGeneration++;
                if (_environmentDepthGeneration == 0u)
                    _environmentDepthGeneration = 1u;
            }
            return _environmentDepthGeneration;
        }

        public bool RequestNextDepthFrame()
        {
            if (!_captureActive || _depthFrameRequested) return false;
            if (_heldDepthSlot < 0 && _readyDepthSlot >= 0) return false;
            int preferred = _heldDepthSlot >= 0
                ? 1 - _heldDepthSlot
                : _readyDepthSlot >= 0 ? 1 - _readyDepthSlot :
                _latestOwnedDepthSlot >= 0 ? 1 - _latestOwnedDepthSlot : 0;
            _requestedDepthSlot = IsDepthSlotWritable(preferred)
                ? preferred : IsDepthSlotWritable(1 - preferred)
                    ? 1 - preferred : -1;
            if (_requestedDepthSlot < 0) return false;
            _depthFrameRequested = true;
            return true;
        }

        private bool IsDepthSlotWritable(int slot) => slot is >= 0 and < 2 &&
            slot != _heldDepthSlot && slot != _readyDepthSlot &&
            slot != _readoutDepthLeaseSlot;

        internal bool TryAcquireReadoutDepth(out ReadoutDepthLease lease)
        {
            lease = default;
            int slot = _latestOwnedDepthSlot;
            if (_readoutDepthLeaseSlot >= 0 || slot < 0 ||
                slot == _heldDepthSlot || _ownedRawDepth[slot] == null ||
                _ownedVersions[slot] == 0)
                return false;
            _readoutDepthLeaseSlot = slot;
            _readoutDepthLeaseVersion = _ownedVersions[slot];
            lease = new ReadoutDepthLease(slot, _readoutDepthLeaseVersion,
                _ownedRawDepth[slot], _ownedProj[slot, 0],
                _ownedProj[slot, 1], _ownedProjInv[slot, 0],
                _ownedProjInv[slot, 1], _ownedView[slot, 0],
                _ownedView[slot, 1], _ownedViewInv[slot, 0],
                _ownedViewInv[slot, 1]);
            return true;
        }

        internal void ReleaseReadoutDepth(ReadoutDepthLease lease)
        {
            if (!lease.IsValid || lease.Slot != _readoutDepthLeaseSlot ||
                lease.Version != _readoutDepthLeaseVersion)
                return;
            _readoutDepthLeaseSlot = -1;
            _readoutDepthLeaseVersion = 0;
        }

        internal bool RequestFreshDepthFrame()
        {
            if (!_captureActive || _heldDepthSlot >= 0) return false;
            _readyDepthSlot = -1;
            _depthFrameRequested = false;
            _requestedDepthSlot = -1;
            return RequestNextDepthFrame();
        }

        internal bool TryGetReadyFrameUnixTime(out double unixSeconds,
            out long timestampNs)
        {
            if (_readyDepthSlot < 0)
            {
                unixSeconds = 0.0;
                timestampNs = 0;
                return false;
            }
            timestampNs = _ownedTimestampNs[_readyDepthSlot];
            if (!_depthClock.IsReady)
                _depthClock.TryCaptureAnchor();
            return _depthClock.TryMapXrNanoseconds(timestampNs,
                out unixSeconds);
        }

        internal bool DiscardReadyDepthFrame()
        {
            if (_readyDepthSlot < 0 || _heldDepthSlot >= 0) return false;
            _processedRawFrameVersion = _ownedVersions[_readyDepthSlot];
            _readyDepthSlot = -1;
            return true;
        }

        internal void BeginQuiesceDepthCapture()
        {
            _captureActive = false;
            _depthFrameRequested = false;
            _requestedDepthSlot = -1;
            // A ready B frame was admitted but is not the immutable observation A.
            // Quiesce drops B while retaining the held A until its GPU token retires.
            _readyDepthSlot = -1;
            ApplyDynamicOcclusionState();
        }

        internal void SuspendEnvironmentDepthForApplicationPause()
        {
            _environmentDepthSuspended = true;
            NextEnvironmentDepthGeneration();
            UpdateDepthFrameSubscription(false);
            if (_shaderOcclusion != null)
                _shaderOcclusion.enabled = false;
            if (_arOcclusionManager != null && _arOcclusionManager.enabled)
                _arOcclusionManager.enabled = false;
            Logger.Info("DepthCapture: environment depth subsystem suspended " +
                        "for application pause");
        }

        internal async Task<bool>
            RestoreEnvironmentDepthAfterApplicationResumeAsync()
        {
            _environmentDepthSuspended = false;
            if (!_captureActive && !dynamicOcclusionEnabled)
                return true;
            bool ready = await EnsureEnvironmentDepthRunningAsync(true, true);
            Logger.Info("DepthCapture: environment depth subsystem restore " +
                (ready ? "completed." : "failed."));
            return ready;
        }

        internal Task RetireSubmittedDepthCopiesAsync()
        {
            ulong target = _copySubmittedEpoch;
            if (_copyRetiredEpoch >= target || target == 0u)
                return Task.CompletedTask;
            if (!_copyRetirementTask.IsCompleted)
                return _copyRetirementTask;
            if (!SystemInfo.supportsAsyncGPUReadback)
                return Task.FromException(new NotSupportedException(
                    "Quest depth copy retirement requires asynchronous GPU readback."));
            if (_lastCopySlot < 0 || _ownedRawDepth[_lastCopySlot] == null)
                return Task.FromException(new IOException(
                    "Owned depth copy target disappeared before retirement."));

            RenderTexture owned = _ownedRawDepth[_lastCopySlot];
            var completion = new TaskCompletionSource<bool>();
            _copyRetirementTask = completion.Task;
            AsyncGPUReadback.Request(owned, 0, 0, 1, 0, 1, 0, 1, request =>
            {
                if (request.hasError)
                {
                    completion.TrySetException(new IOException(
                        "Owned depth GPU-copy retirement readback failed."));
                    return;
                }
                _copyRetiredEpoch = Math.Max(_copyRetiredEpoch, target);
                completion.TrySetResult(true);
            });
            return _copyRetirementTask;
        }

        internal void CompleteDepthCaptureStop()
        {
            DepthAvailable = false;
            _depthFrameRequested = false;
            _requestedDepthSlot = -1;
            _readyDepthSlot = -1;
            _heldDepthSlot = -1;
            _depthTex = null;
            _processedRawFrameVersion = _latestRawFrameVersion;
            _depthClock.Reset();
            ApplyDynamicOcclusionState();
            Logger.Info("DepthCapture: scan stopped; dynamic passthrough " +
                "occlusion is " +
                (dynamicOcclusionEnabled ? "active" : "disabled"));
        }

        private void OnDestroy()
        {
            NextEnvironmentDepthGeneration();
            UpdateDepthFrameSubscription(false);
            _scenePermissionCompletion.TrySetResult(false);
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Destroys GPU textures (normals, dilation, filtered depth) to free memory.
        /// Textures are lazily recreated when the next depth frame arrives.
        /// </summary>
        internal void ReleaseOwnedResourcesAfterGpuRetirement()
        {
            if (_normTex) { Destroy(_normTex); _normTex = null; }
            if (_dilationA) { Destroy(_dilationA); _dilationA = null; }
            if (_dilationB) { Destroy(_dilationB); _dilationB = null; }
            for (int slot = 0; slot < _ownedRawDepth.Length; slot++)
            {
                if (_ownedRawDepth[slot]) Destroy(_ownedRawDepth[slot]);
                _ownedRawDepth[slot] = null;
            }
            if (_refinedDepthTex) { Destroy(_refinedDepthTex); _refinedDepthTex = null; }
            if (_refineMetrics != null)
            {
                _refineMetrics.Release();
                _refineMetrics = null;
            }
            if (_fineSurfaceTarget != null)
            {
                _fineSurfaceTarget.Release();
                _fineSurfaceTarget = null;
            }
            unchecked
            {
                _fineSurfaceTargetGeneration++;
                if (_fineSurfaceTargetGeneration == 0u)
                    _fineSurfaceTargetGeneration = 1u;
            }
            _fineSurfaceTargetReadbackPending = false;
            _fineSurfaceTargetValid = false;
            _fineSurfaceTargetIssuedSequence = 0u;
            _fineSurfaceTargetCompletedSequence = 0u;
            _refineMetricValueCount = 0;
            _refineMetricsRevision = 0u;
            _dilatedDepth = null;
            _depthTex = null;
            _depthFrameRequested = false;
            _requestedDepthSlot = -1;
            _readyDepthSlot = -1;
            _heldDepthSlot = -1;
            _latestOwnedDepthSlot = -1;
            _readoutDepthLeaseSlot = -1;
            _readoutDepthLeaseVersion = 0;
            _processedRawFrameVersion = _latestRawFrameVersion;
            Logger.Info("DepthCapture: GPU resources released");
        }

        internal Action CaptureOwnedGpuResourceRelease()
        {
            UnityEngine.Object[] captured =
            {
                _normTex, _dilationA, _dilationB,
                _ownedRawDepth[0], _ownedRawDepth[1], _refinedDepthTex
            };
            ComputeBuffer capturedRefineMetrics = _refineMetrics;
            ComputeBuffer capturedFineTarget = _fineSurfaceTarget;
            bool released = false;
            return () =>
            {
                if (released) return;
                released = true;
                if (this != null)
                {
                    ReleaseOwnedResourcesAfterGpuRetirement();
                    return;
                }
                foreach (UnityEngine.Object resource in captured)
                    if (resource != null) UnityEngine.Object.Destroy(resource);
                capturedRefineMetrics?.Release();
                capturedFineTarget?.Release();
            };
        }

        internal bool TryUpdateFineSurfaceTarget(Vector3 rayOrigin,
            Vector3 rayDirection, float maximumDistance, bool allowSubmit,
            out Vector3 worldTarget, out Vector3 worldNormal)
        {
            worldTarget = _fineSurfaceTargetWorld;
            worldNormal = _fineSurfaceTargetNormal;
            if (!allowSubmit || _fineSurfaceTargetReadbackPending ||
                Time.unscaledTime < _nextFineSurfaceTarget)
                return _fineSurfaceTargetValid;
            if (_readyDepthSlot < 0)
            {
                RequestNextDepthFrame();
                return _fineSurfaceTargetValid;
            }
            int slot = _readyDepthSlot;
            RenderTexture depth = _ownedRawDepth[slot];
            if (depth == null || maximumDistance <= 0f ||
                rayDirection.sqrMagnitude <= 1e-8f)
                return _fineSurfaceTargetValid;

            _fineSurfaceTarget ??= new ComputeBuffer(2, 16,
                ComputeBufferType.Structured)
            {
                name = "Fine controller-depth surface target"
            };
            rayDirection.Normalize();
            CommandBuffer command = CommandBufferPool.Get(
                "Fine controller-depth surface target");
            int kernel = _fineSurfaceTargetKernel.KernelIndex;
            command.SetComputeTextureParam(depthNormalCompute, kernel,
                FineDepthId, depth);
            command.SetComputeBufferParam(depthNormalCompute, kernel,
                FineTargetId, _fineSurfaceTarget);
            command.SetComputeMatrixParam(depthNormalCompute, FineDepthProjId,
                _ownedProj[slot, 0]);
            command.SetComputeMatrixParam(depthNormalCompute,
                FineDepthProjInvId, _ownedProjInv[slot, 0]);
            command.SetComputeMatrixParam(depthNormalCompute, FineDepthViewId,
                _ownedView[slot, 0]);
            command.SetComputeMatrixParam(depthNormalCompute,
                FineDepthViewInvId, _ownedViewInv[slot, 0]);
            command.SetComputeVectorParam(depthNormalCompute, FineRayOriginId,
                rayOrigin);
            command.SetComputeVectorParam(depthNormalCompute,
                FineRayDirectionId, rayDirection);
            command.SetComputeFloatParam(depthNormalCompute,
                FineMaxDistanceId, maximumDistance);
            command.SetComputeVectorParam(depthNormalCompute, FineDepthSizeId,
                new Vector4(depth.width, depth.height, 0f, 0f));
            command.DispatchCompute(depthNormalCompute, kernel, 1, 1, 1);
            Graphics.ExecuteCommandBuffer(command);
            CommandBufferPool.Release(command);

            _fineSurfaceTargetReadbackPending = true;
            _nextFineSurfaceTarget = Time.unscaledTime + 1f / 15f;
            uint querySequence;
            unchecked
            {
                _fineSurfaceTargetIssuedSequence++;
                if (_fineSurfaceTargetIssuedSequence == 0u)
                    _fineSurfaceTargetIssuedSequence = 1u;
                querySequence = _fineSurfaceTargetIssuedSequence;
            }
            int version = _ownedVersions[slot];
            uint generation = _fineSurfaceTargetGeneration;
            AsyncGPUReadback.Request(_fineSurfaceTarget, request =>
            {
                if (generation != _fineSurfaceTargetGeneration) return;
                _fineSurfaceTargetReadbackPending = false;
                _fineSurfaceTargetCompletedSequence = querySequence;
                if (!request.hasError)
                {
                    Unity.Collections.NativeArray<Vector4> values =
                        request.GetData<Vector4>();
                    _fineSurfaceTargetValid = values.Length == 2 &&
                        values[0].w > 0.5f &&
                        ((Vector3)values[1]).sqrMagnitude > 1e-10f;
                    if (_fineSurfaceTargetValid)
                    {
                        _fineSurfaceTargetWorld = values[0];
                        _fineSurfaceTargetNormal = ((Vector3)values[1]).normalized;
                    }
                }
                else
                    _fineSurfaceTargetValid = false;
                if (_readyDepthSlot == slot &&
                    _ownedVersions[slot] == version && _heldDepthSlot < 0)
                {
                    _processedRawFrameVersion = version;
                    _readyDepthSlot = -1;
                }
            });
            return _fineSurfaceTargetValid;
        }

        internal bool TryGetRefineMetrics(uint revision,
            out ComputeBuffer metrics, out int valueCount)
        {
            bool available = revision != 0u && revision ==
                _refineMetricsRevision && _refineMetrics != null &&
                _refineMetricValueCount > 0;
            metrics = available ? _refineMetrics : null;
            valueCount = available ? _refineMetricValueCount : 0;
            return available;
        }

        private void Update()
        {
            float t = Time.unscaledTime;
            if (t - _lastLogTime >= 5f)
            {
                _lastLogTime = t;
                var sub = _arOcclusionManager != null ? _arOcclusionManager.subsystem : null;
                bool hardKeyword = Shader.IsKeywordEnabled(
                    "XR_HARD_OCCLUSION");
                Logger.Info($"DepthCapture: rawFrames={_frameCount}, preprocessed={_preprocessedFrameCount}, " +
                          $"ready={_readyDepthSlot >= 0}, held={_heldDepthSlot >= 0}, " +
                          $"depthAvail={DepthAvailable}, " +
                          $"occMgr.enabled={_arOcclusionManager?.enabled}, sub={sub?.GetType().Name ?? "null"}, " +
                          $"running={sub?.running}, shaderOcc=" +
                          $"{_shaderOcclusion?.enabled}, hardKeyword=" +
                          $"{hardKeyword}, isOcclusionOn=" +
                          $"{Shader.GetGlobalInt(IsOcclusionOnId)}, " +
                          $"globalDepth=" +
                          $"{Shader.GetGlobalTexture(EnvironmentDepthTextureId) != null}");
            }
        }

        private void OnDepthFrame(AROcclusionFrameEventArgs args)
        {
            _frameCount++;
            if (args.externalTextures.Count > 0 &&
                args.externalTextures[0].texture != null)
            {
                unchecked
                {
                    _environmentDepthFrameSequence++;
                    if (_environmentDepthFrameSequence == 0u)
                        _environmentDepthFrameSequence = 1u;
                }
            }
            if (_frameCount <= 3 || _frameCount % 100 == 0)
                Logger.Info($"OnDepthFrame #{_frameCount}, textures={args.externalTextures.Count}");

            double hostSeconds = Time.realtimeSinceStartupAsDouble;
            long timestampNs;
            if (!args.TryGetTimestamp(out timestampNs))
            {
                if (!Application.isEditor) return;
                timestampNs = (long)Math.Round(hostSeconds * 1_000_000_000.0);
            }
            if (!_depthClock.IsReady && !_depthClock.TryCaptureAnchor()) return;
            if (!_depthFrameRequested || _requestedDepthSlot < 0) return;
            if (Application.isEditor)
                HandleEditorSimulation(args, timestampNs);
            else
                HandleDeviceDepth(args, timestampNs);
        }

        /// <summary>
        /// Preprocesses exactly the latest raw frame for the integration tick that will
        /// consume it. Intermediate sensor frames are intentionally never filtered,
        /// normalised, or dilated.
        /// </summary>
        internal bool ConsumeLatestDepthFrame(CommandBuffer command,
            StereoCameraFrame cameraFrame, FineBrushDescriptor fineBrush)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (!cameraFrame.IsValid || !TryHoldLatestDepthFrame())
                return false;
            ApplyStereoRgbdRefinement(command, cameraFrame, fineBrush);
            SetGlobalShaderProperties();
            ComputeDilation(command);
            _processedRawFrameVersion = _ownedVersions[_heldDepthSlot];
            _preprocessedFrameCount++;
            Updated?.Invoke();
            return true;
        }

        internal bool PrepareLatestDepthFrameForNative(
            StereoCameraFrame cameraFrame)
        {
            if (!cameraFrame.IsValid || !TryHoldLatestDepthFrame())
                return false;
            int width = _depthTex.width;
            int height = _depthTex.height;
            EnsureRefinementOutputs(width, height);
            EnsureRefineMetrics(Mathf.CeilToInt(width / 8f) *
                Mathf.CeilToInt(height / 8f));
            EnsureDilationOutputs(width, height);
            _depthTex = _refinedDepthTex;
            _dilatedDepth = _dilationB;
            _processedRawFrameVersion = _ownedVersions[_heldDepthSlot];
            _preprocessedFrameCount++;
            return true;
        }

        internal void CompleteNativeDepthPreprocess()
        {
            (_dilationA, _dilationB) = (_dilationB, _dilationA);
            _dilatedDepth = _dilationA;
            SetGlobalShaderProperties();
            Shader.SetGlobalTexture(NormTexID, _normTex);
            Shader.SetGlobalTexture(DilatedDepthTexID, _dilatedDepth);
            Updated?.Invoke();
        }

        internal void FillNativeExecutorDepthResources(IntPtr[] resources,
            bool includesPreprocess)
        {
            if (resources == null || resources.Length !=
                MerkabaNativeVulkanExecutor.ResourceCount)
                throw new ArgumentException(nameof(resources));
            IntPtr TexturePtr(Texture texture) => texture != null
                ? texture.GetNativeTexturePtr() : IntPtr.Zero;
            resources[(int)MerkabaNativeVulkanExecutor.Resource.RefineMetrics] =
                _refineMetrics != null ? _refineMetrics.GetNativeBufferPtr() :
                IntPtr.Zero;
            resources[(int)MerkabaNativeVulkanExecutor.Resource.RawDepth] =
                _heldDepthSlot >= 0
                    ? TexturePtr(_ownedRawDepth[_heldDepthSlot]) : IntPtr.Zero;
            resources[(int)MerkabaNativeVulkanExecutor.Resource.RefinedDepth] =
                TexturePtr(_refinedDepthTex);
            resources[(int)MerkabaNativeVulkanExecutor.Resource.Normals] =
                TexturePtr(_normTex);
            resources[(int)MerkabaNativeVulkanExecutor.Resource.DilationA] =
                TexturePtr(_dilationA);
            resources[(int)MerkabaNativeVulkanExecutor.Resource.DilationB] =
                TexturePtr(includesPreprocess ? _dilationB : _dilatedDepth);
        }

        private bool TryHoldLatestDepthFrame()
        {
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

        private void HandleEditorSimulation(AROcclusionFrameEventArgs args,
            long timestampNs)
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
            _ownedTimestampNs[_requestedDepthSlot] = timestampNs;

            EnsureOwnedRawDepth(_requestedDepthSlot, rawDepth.width, rawDepth.height);

            depthNormalCompute.SetVector(ZParamsID,
                _ownedPlanes[_requestedDepthSlot]);
            _monoConvertKernel.Set(DepthTexRWID,
                _ownedRawDepth[_requestedDepthSlot]);
            _monoConvertKernel.Set(InputRawMonoDepthID, rawDepth);
            _monoConvertKernel.DispatchFit(rawDepth.width, rawDepth.height);
            MarkOwnedDepthSnapshotReady();
        }

        private void HandleDeviceDepth(AROcclusionFrameEventArgs args,
            long timestampNs)
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

                _ownedView[_requestedDepthSlot, i] =
                    depthFrameMat.inverse;
                _ownedViewInv[_requestedDepthSlot, i] = depthFrameMat;
            }

            _ownedPlanes[_requestedDepthSlot] = new Vector2(
                depthPlanes.nearZ, depthPlanes.farZ);
            _ownedTimestampNs[_requestedDepthSlot] = timestampNs;
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
            CommandBuffer command = CommandBufferPool.Get(
                "Merkaba owned stereo depth copy");
            bool submitted = false;
            bool timedSubmission = false;
            try
            {
                timedSubmission = MerkabaGpuTimestamps.TryAcquire(
                    CaptureOwner.DepthSnapshotCopy,
                    unchecked((uint)Math.Max(1, _latestRawFrameVersion + 1)),
                    command);
                _projectionDepthCopyKernel.Set(command,
                    InputProjectionDepthID, transientDepth);
                _projectionDepthCopyKernel.Set(command, DepthTexRWID,
                    _ownedRawDepth[slot]);
                _projectionDepthCopyKernel.DispatchFit(command,
                    transientDepth.width, transientDepth.height, 2);
                MerkabaGpuTimestamps.End(CaptureOwner.DepthSnapshotCopy,
                    command, timedSubmission);
                Graphics.ExecuteCommandBuffer(command);
                submitted = true;
            }
            finally
            {
                MerkabaGpuTimestamps.Complete(CaptureOwner.DepthSnapshotCopy,
                    timedSubmission, submitted);
                CommandBufferPool.Release(command);
            }
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
            unchecked
            {
                _copySubmittedEpoch++;
                if (_copySubmittedEpoch == 0u) _copySubmittedEpoch = 1u;
            }
            _lastCopySlot = slot;
            _depthFrameRequested = false;
            _requestedDepthSlot = -1;
            _readyDepthSlot = slot;
            _latestOwnedDepthSlot = slot;
            DepthAvailable = true;
            unchecked
            {
                _latestRawFrameVersion++;
                if (_latestRawFrameVersion == 0) _latestRawFrameVersion = 1;
            }
            _ownedVersions[slot] = _latestRawFrameVersion;
        }

        private void ApplyStereoRgbdRefinement(CommandBuffer command,
            StereoCameraFrame cameraFrame, FineBrushDescriptor fineBrush)
        {
            if (!cameraFrame.IsValid || _depthTex == null)
                throw new InvalidOperationException(
                    "Depth refinement requires a complete stereo PCA pair.");

            int w = _depthTex.width;
            int h = _depthTex.height;

            EnsureRefinementOutputs(w, h);

            ComputeShader shader = stereoRgbdRefineCompute;
            int metricGroupsX = Mathf.CeilToInt(w / 8f);
            int metricGroupsY = Mathf.CeilToInt(h / 8f);
            EnsureRefineMetrics(metricGroupsX * metricGroupsY);
            bool captureMetrics = MerkabaGpuTimestamps.IsOwnerRecording(
                CaptureOwner.Observation);
            _stereoRgbdRefineKernel.Set(command, RefineSrcDepthId, _depthTex);
            _stereoRgbdRefineKernel.Set(command, RefineDstDepthId,
                _refinedDepthTex);
            _stereoRgbdRefineKernel.Set(command, RefineDstNormalId,
                _normTex);
            command.SetComputeIntParam(shader, RefineDepthWidthId, w);
            command.SetComputeIntParam(shader, RefineDepthHeightId, h);
            command.SetComputeMatrixArrayParam(shader, RefineDepthProjId, _proj);
            command.SetComputeMatrixArrayParam(shader, RefineDepthProjInvId,
                _projInv);
            command.SetComputeMatrixArrayParam(shader, RefineDepthViewId, _view);
            command.SetComputeMatrixArrayParam(shader, RefineDepthViewInvId,
                _viewInv);
            _stereoRgbdRefineKernel.Set(command, RefineMetricsId,
                _refineMetrics);
            command.SetComputeIntParam(shader, RefineMetricsEnabledId,
                captureMetrics ? 1 : 0);
            command.SetComputeIntParam(shader, RefineMetricGroupsXId,
                metricGroupsX);
            command.SetComputeIntParam(shader, RefineFineActiveId,
                fineBrush.IsRefine ? 1 : 0);
            command.SetComputeVectorParam(shader, RefineFineEyeOriginId,
                fineBrush.EyeOrigin);
            command.SetComputeVectorParam(shader, RefineFineBrushAxisId,
                fineBrush.Axis);
            command.SetComputeFloatParam(shader,
                RefineFineCosHalfAngleSquaredId,
                fineBrush.CosHalfAngleSquared);
            command.SetComputeFloatParam(shader, RefineFineToolDepthSquaredId,
                fineBrush.ToolDepthSquared);
            BindStereoCamera(command, shader, cameraFrame.Left, 0);
            BindStereoCamera(command, shader, cameraFrame.Right, 1);
            _stereoRgbdRefineKernel.DispatchFit(command, w, h, 1);
            if (captureMetrics)
                _refineMetricsRevision = MerkabaGpuTimestamps.CurrentRevision;

            _depthTex = _refinedDepthTex;
            Shader.SetGlobalTexture(NormTexID, _normTex);
        }

        private void EnsureRefineMetrics(int groupCount)
        {
            int valueCount = Math.Max(1, groupCount) *
                MerkabaGpuTimestamps.RefineMetricValueCount;
            if (_refineMetrics != null &&
                _refineMetrics.count == valueCount)
            {
                _refineMetricValueCount = valueCount;
                return;
            }
            _refineMetrics?.Release();
            _refineMetrics = new ComputeBuffer(valueCount, sizeof(uint),
                ComputeBufferType.Structured)
            {
                name = "Merkaba RGB-D radial metrics"
            };
            _refineMetricValueCount = valueCount;
            _refineMetricsRevision = 0u;
        }

        private void BindStereoCamera(CommandBuffer command,
            ComputeShader shader, CameraFrameDescriptor frame, int eye)
        {
            if (!frame.IsValid || frame.Eye != (StereoEye)eye)
                throw new ArgumentException("Stereo PCA eye mismatch.",
                    nameof(frame));
            command.SetComputeTextureParam(shader,
                _stereoRgbdRefineKernel.KernelIndex,
                RefineCameraRgbId[eye], frame.Texture);
            command.SetComputeVectorParam(shader, RefineCameraPositionId[eye],
                frame.WorldPose.position);
            command.SetComputeMatrixParam(shader,
                RefineCameraInverseRotationId[eye],
                Matrix4x4.Rotate(frame.WorldPose.rotation).inverse);
            command.SetComputeVectorParam(shader,
                RefineCameraFocalLengthId[eye], frame.FocalLength);
            command.SetComputeVectorParam(shader,
                RefineCameraPrincipalPointId[eye], frame.PrincipalPoint);
            command.SetComputeVectorParam(shader,
                RefineCameraSensorResolutionId[eye], frame.SensorResolution);
            command.SetComputeVectorParam(shader,
                RefineCameraCurrentResolutionId[eye], frame.CurrentResolution);
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

        private void ComputeDilation(CommandBuffer command)
        {
            EnsureDilationOutputs(_depthTex.width, _depthTex.height);

            command.SetComputeFloatParam(depthDilationCompute, VoxDistID,
                MerkabaConstants.FreeFullClearance);
            command.SetComputeFloatParam(depthDilationCompute, VoxSizeShaderID,
                MerkabaConstants.SupportSize);

            _initDilateKernel.Set(command, DepthTexID, _depthTex);
            _initDilateKernel.Set(command, DilateSrcID, _dilationA);
            _initDilateKernel.DispatchFit(command, _dilationA.width,
                _dilationA.height, 1);

            foreach (int stepSize in BuildDilationStepSequence(dilationSteps))
            {
                _dilateStepKernel.Set(command, DilateSrcID, _dilationA);
                _dilateStepKernel.Set(command, DilateDestID, _dilationB);
                command.SetComputeIntParam(depthDilationCompute,
                    DilateStepSizeID, stepSize);
                _dilateStepKernel.DispatchFit(command, _dilationA.width,
                    _dilationA.height, 1);

                (_dilationA, _dilationB) = (_dilationB, _dilationA);
            }

            _dilatedDepth = _dilationA;
            Shader.SetGlobalTexture(DilatedDepthTexID, _dilatedDepth);
        }

        private void EnsureRefinementOutputs(int width, int height)
        {
            if (_refinedDepthTex == null || _refinedDepthTex.width != width ||
                _refinedDepthTex.height != height ||
                _refinedDepthTex.graphicsFormat != GraphicsFormat.R32_SFloat)
            {
                if (_refinedDepthTex) Destroy(_refinedDepthTex);
                _refinedDepthTex = new RenderTexture(width, height, 0,
                    GraphicsFormat.R32_SFloat, 1)
                {
                    enableRandomWrite = true,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                _refinedDepthTex.Create();
            }
            if (_normTex == null || _normTex.width != width ||
                _normTex.height != height ||
                _normTex.graphicsFormat != GraphicsFormat.R8G8B8A8_SNorm)
            {
                if (_normTex) Destroy(_normTex);
                _normTex = new RenderTexture(width, height, 0,
                    GraphicsFormat.R8G8B8A8_SNorm, 1)
                {
                    enableRandomWrite = true,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                _normTex.Create();
            }
        }

        private void EnsureDilationOutputs(int width, int height)
        {
            if (_dilationA != null && _dilationA.width == width &&
                _dilationA.height == height) return;
            if (_dilationA) Destroy(_dilationA);
            if (_dilationB) Destroy(_dilationB);
            var descriptor = new RenderTextureDescriptor
            {
                width = width,
                height = height,
                volumeDepth = 1,
                dimension = TextureDimension.Tex2D,
                autoGenerateMips = false,
                enableRandomWrite = true,
                graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat,
                msaaSamples = 1
            };
            _dilationA = new RenderTexture(descriptor);
            _dilationB = new RenderTexture(descriptor);
            _dilationA.Create();
            _dilationB.Create();
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

    }
}
