using System;
using System.Collections.Generic;
using System.Globalization;
using Genesis.RoomScan.World;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Genesis.RoomScan
{
    /// <summary>
    /// Manages the GPU TSDF + color volume and dispatches compute-shader integration, pruning,
    /// and freeze/unfreeze passes. Voxels are integrated from depth and optional camera color
    /// each frame, with configurable convergence, exclusion zones, and warmup clearing.
    /// </summary>
    public class VolumeIntegrator : MonoBehaviour
    {
        public static VolumeIntegrator Instance { get; private set; }

        [SerializeField] private ComputeShader compute;

        [Header("Volume")]
        [SerializeField] private int3 voxelCount = new(256, 256, 256);
        [SerializeField] private float voxelSize = 0.05f;
        [SerializeField] private float voxelDistance = 0.15f;
        [SerializeField] private float voxelMin = 0.1f;

        [Header("Integration")]
        [SerializeField] private float depthDisparityThreshold = 0.5f;
        [SerializeField] private float maxUpdateDist = 5f;
        [SerializeField] private float minUpdateDist = 0.5f;
        [SerializeField] private int maxFrustumPositions = 1000000;

        [Header("Convergence")]
        [Tooltip("Blend strength. Higher = faster convergence and correction. (default 0.8)")]
        [SerializeField, Range(0.1f, 2f)] private float blendRate = 0.8f;
        [Tooltip("Weight resistance to blending. Lower = faster corrections but less stable. (default 2.5)")]
        [SerializeField, Range(0.5f, 10f)] private float stability = 2.5f;
        [Tooltip("How fast weight accumulates per frame. Lower = bad data builds less confidence. (default 0.025)")]
        [SerializeField, Range(0.005f, 0.1f)] private float weightGrowth = 0.025f;
        [Tooltip("Maximum weight any voxel can reach. Lower = all areas correct equally fast. (default 0.5)")]
        [SerializeField, Range(0.1f, 1f)] private float maxWeight = 0.5f;

        [Header("Surface Protection")]
        [Tooltip("Protect confident surfaces from lower-quality, opposite-facing, and occluded observations.")]
        [SerializeField] private bool protectStableSurfaces = true;
        [Tooltip("Maximum integration distance behind a measured surface, in voxels. The physical voxelMin limit still applies.")]
        [SerializeField, Range(0.5f, 2f)] private float behindSurfaceBandVoxels = 1.25f;
        [Tooltip("Normalized TSDF confidence at which surface-quality arbitration starts.")]
        [SerializeField, Range(0.1f, 0.9f)] private float stableSurfaceConfidence = 0.35f;
        [Tooltip("Quality difference tolerated before a worse observation is rejected.")]
        [SerializeField, Range(0f, 0.2f)] private float surfaceQualityHysteresis = 0.04f;
        [Tooltip("Existing/incoming normal dot below this value identifies the opposite wall face.")]
        [SerializeField, Range(-1f, 0.5f)] private float oppositeSurfaceNormalDot = -0.15f;
        [Tooltip("Maximum normalized TSDF residual accepted from a non-improving stable observation.")]
        [SerializeField, Range(0.02f, 0.5f)] private float stableSurfaceResidualTolerance = 0.12f;

        [Header("Meshing")]
        [Tooltip("Min voxel confidence weight for Surface Nets to generate mesh. Higher = fewer phantom surfaces. (default 0.08)")]
        [SerializeField, Range(0.01f, 0.5f)] private float minMeshWeight = 0.08f;
        public float MinMeshWeight => minMeshWeight;

        [Header("Camera Color")]
        [Tooltip("Exposure boost for camera texture. Quest 3 passthrough cameras produce dim images. (default 3.0)")]
        [SerializeField, Range(1f, 10f)] private float cameraExposure = 3f;

        private RenderTexture _volume;
        private RenderTexture _colorVolume;
        private RigidPoseData _worldFromVolume = RigidPoseData.Identity;
        private Matrix4x4 _worldFromVolumeMatrix = Matrix4x4.identity;
        private Matrix4x4 _volumeFromWorldMatrix = Matrix4x4.identity;

        /// <summary>3D RenderTexture (R8G8_SNorm) storing the truncated signed distance field.</summary>
        public RenderTexture Volume => _volume;
        /// <summary>3D RenderTexture (RGBA8_UNorm) storing per-voxel accumulated color.</summary>
        public RenderTexture ColorVolume => _colorVolume;
        public int3 VoxelCount => voxelCount;
        public float VoxelSize => voxelSize;
        public float VoxelDistance => voxelDistance;
        public bool ProtectStableSurfaces => protectStableSurfaces;
        public float BehindSurfaceBandMeters => TsdfFusionPolicy.BehindSurfaceBandMeters(
            voxelMin, voxelSize, behindSurfaceBandVoxels);
        /// <summary>
        /// Quest 3/3S Adreno 740 reports Vulkan maxStorageBufferRange=134217728.
        /// This applies to storage/structured buffers, not Texture3D allocations.
        /// </summary>
        public const long MaximumStorageBufferRangeBytes = 128L * 1024L * 1024L;
        /// <summary>Rigid transform from the active volume/chunk frame into tracking world.</summary>
        public RigidPoseData WorldFromVolume => _worldFromVolume;
        public Matrix4x4 WorldFromVolumeMatrix => _worldFromVolumeMatrix;
        public Matrix4x4 VolumeFromWorldMatrix => _volumeFromWorldMatrix;

        private static readonly int VolumeRWID = Shader.PropertyToID("gsVolumeRW");
        private static readonly int VolumeID = Shader.PropertyToID("gsVolume");
        private static readonly int ColorVolumeRWID = Shader.PropertyToID("gsColorVolumeRW");
        private static readonly int ColorVolumeID = Shader.PropertyToID("gsColorVolume");
        private static readonly int VoxCountID = Shader.PropertyToID("gsVoxCount");
        private static readonly int VoxSizeID = Shader.PropertyToID("gsVoxSize");
        private static readonly int VoxMinID = Shader.PropertyToID("gsVoxMin");
        private static readonly int VoxDistID = Shader.PropertyToID("gsVoxDist");
        private static readonly int FrustumVolumeID = Shader.PropertyToID("gsFrustumVolume");
        private static readonly int DepthDispThreshID = Shader.PropertyToID("gsDepthDispThresh");
        private static readonly int NumExclusionsID = Shader.PropertyToID("gsNumExclusions");
        private static readonly int ExclusionHeadsID = Shader.PropertyToID("gsExclusionHeads");
        private static readonly int MaxUpdateDistID = Shader.PropertyToID("gsMaxUpdateDist");
        private static readonly int BlendRateID = Shader.PropertyToID("gsBlendRate");
        private static readonly int StabilityID = Shader.PropertyToID("gsStability");
        private static readonly int WeightGrowthID = Shader.PropertyToID("gsWeightGrowth");
        private static readonly int MaxWeightID = Shader.PropertyToID("gsMaxWeight");
        private static readonly int FusionProtectionEnabledID = Shader.PropertyToID("gsFusionProtectionEnabled");
        private static readonly int FusionStableConfidenceID = Shader.PropertyToID("gsFusionStableConfidence");
        private static readonly int FusionExistingQualityFloorID = Shader.PropertyToID("gsFusionExistingQualityFloor");
        private static readonly int FusionQualityHysteresisID = Shader.PropertyToID("gsFusionQualityHysteresis");
        private static readonly int FusionOppositeOrientationDotID = Shader.PropertyToID("gsFusionOppositeOrientationDot");
        private static readonly int FusionSurfaceBandID = Shader.PropertyToID("gsFusionSurfaceBand");
        private static readonly int FusionResidualToleranceID = Shader.PropertyToID("gsFusionResidualTolerance");
        private static readonly int FusionResidualConfidenceSlackID = Shader.PropertyToID("gsFusionResidualConfidenceSlack");
        private static readonly int FusionImprovementResidualScaleID = Shader.PropertyToID("gsFusionImprovementResidualScale");
        private static readonly int FusionStableBlendFloorID = Shader.PropertyToID("gsFusionStableBlendFloor");
        private static readonly int FusionNormalMinWeightID = Shader.PropertyToID("gsFusionNormalMinWeight");
        private static readonly int FusionBackBandVoxelsID = Shader.PropertyToID("gsFusionBackBandVoxels");
        private static readonly int CamRGBID = Shader.PropertyToID("gsCamRGB");
        private static readonly int CamAvailableID = Shader.PropertyToID("gsCamAvailable");
        private static readonly int CamPosID = Shader.PropertyToID("gsCamPos");
        private static readonly int CamInvRotID = Shader.PropertyToID("gsCamInvRot");
        private static readonly int CamFocalLenID = Shader.PropertyToID("gsCamFocalLen");
        private static readonly int CamPrincipalPtID = Shader.PropertyToID("gsCamPrincipalPt");
        private static readonly int CamSensorResID = Shader.PropertyToID("gsCamSensorRes");
        private static readonly int CamCurrentResID = Shader.PropertyToID("gsCamCurrentRes");
        private static readonly int CamExposureID = Shader.PropertyToID("gsCamExposure");
        private static readonly int WorldFromVolumeID = Shader.PropertyToID("gsWorldFromVolume");
        private static readonly int VolumeFromWorldID = Shader.PropertyToID("gsVolumeFromWorld");

        public float CameraExposure => cameraExposure;

        [Header("Warmup")]
        [Tooltip("Clear the volume after this many integrations to discard sensor startup noise. 0 = disabled.")]
        [SerializeField] private int warmupIntegrations = 3;

        [Header("Pruning")]
        [SerializeField] private float pruneIntervalSeconds = 3f;

        private ComputeKernelHelper _clearKernel;
        private ComputeKernelHelper _integrateKernel;
        private ComputeKernelHelper _pruneKernel;
        private ComputeKernelHelper _freezeKernel;
        private ComputeKernelHelper _unfreezeKernel;

        private ComputeBuffer _frustumVolume;
        private bool _frustumReady;
        private float _lastPruneTime;

        // Coverage metrics
        private ComputeKernelHelper _coverageKernel;
        private ComputeBuffer _coverageCounters;
        private int _integrationsSinceCoverage;
        private bool _coverageReadbackPending;
        private static readonly int CoverageCountersID = Shader.PropertyToID("_CoverageCounters");
        private static readonly int ColorVolumeReadID = Shader.PropertyToID("gsColorVolumeRead");
        private static readonly ProfilerMarker IntegrateMarker =
            new("QIS.VolumeIntegrator.Integrate");
        private static readonly ProfilerMarker FusionDispatchMarker =
            new("QIS.VolumeIntegrator.FusionDispatch");

        [Header("Coverage Metrics")]
        [Tooltip("Dispatch coverage count every N integrations (0 = disabled). Higher = less GPU overhead.")]
        [SerializeField] private int coverageUpdateInterval = 30;

        [Header("Development Profiling")]
        [Tooltip("Emit bounded frame/integration timing summaries in development builds.")]
        [SerializeField] private bool enableDevelopmentProfiling = true;
        [SerializeField, Range(60, 1200)] private int profilingSampleInterval = 300;
        private readonly FrameTiming[] _latestFrameTiming = new FrameTiming[1];
        private int _profilingSamples;
        private double _cpuIntegrationTotalMs;
        private double _cpuIntegrationMaxMs;
        private double _cpuFrameTotalMs;
        private double _cpuFrameMaxMs;
        private double _gpuFrameTotalMs;
        private double _gpuFrameMaxMs;

        /// <summary>Number of voxels near the zero-crossing with sufficient weight (surface voxels).</summary>
        public int SurfaceVoxelCount { get; private set; }
        /// <summary>Number of surface voxels that are frozen (user-confirmed done).</summary>
        public int FrozenSurfaceCount { get; private set; }
        /// <summary>Number of surface voxels with camera color data (alpha &gt; 0.1).</summary>
        public int ColoredSurfaceCount { get; private set; }

        /// <summary>
        /// Transforms whose positions define spherical exclusion zones; voxels near these are skipped during integration.
        /// </summary>
        public readonly List<Transform> ExclusionZones = new();
        private readonly Vector4[] _exclusionPositions = new Vector4[64];

        /// <summary>Total number of integration passes dispatched since startup or the last clear.</summary>
        public int IntegrationCount { get; private set; }
        public int WarmupIntegrations => warmupIntegrations;

        /// <summary>Raised after each integration compute dispatch (before pruning).</summary>
        public event Action Integrated;
        /// <summary>Raised after the volume is cleared.</summary>
        public event Action Cleared;
        /// <summary>Raised when the active local volume is placed at a new world pose.</summary>
        public event Action<RigidPoseData> VolumeFrameChanged;

        private Texture _pendingCamFrame;
        private Vector3 _pendingCamPos;
        private Quaternion _pendingCamRot;
        private Vector2 _pendingFocalLen;
        private Vector2 _pendingPrincipalPt;
        private Vector2 _pendingSensorRes;
        private Vector2 _pendingCurrentRes;
        private RenderTexture _camFrameCopy;
        private Texture2D _dummyCamTex;
        private readonly GpuResourceRetirementQueue _gpuRetirement = new();

        private void Awake()
        {
            Instance = this;
            // GPU resources allocate lazily on the first scan / save / full-load
            // path via ReallocateVolumes(). The lightweight LoadRefinedOnlyAsync
            // path (returning-player and editor-sim) never touches them, so a
            // pure replay session avoids the ~150 MB TSDF+color RT footprint.
        }

        private void Start()
        {
            // Intentionally empty — see Awake().
            //
            // Historic note: kernel helpers + 3D RTs used to be constructed
            // here unconditionally. They're now created on demand inside
            // ReallocateVolumes(), which is called by:
            //   * RoomScanner.StartScanning() (every scan begin)
            //   * RoomScanPersistence.SaveToNewPackageAsync (defensive)
            //   * RoomScanPersistence.LoadPackageAsync (full TSDF reload)
        }

        /// <summary>
        /// Build all compute-kernel helpers and bind them to the current
        /// <see cref="_volume"/> / <see cref="_colorVolume"/>. Idempotent —
        /// the first <see cref="ReallocateVolumes"/> call constructs them;
        /// subsequent allocations only need <see cref="RebindKernelTextures"/>.
        /// </summary>
        private void InitKernels()
        {
            _clearKernel = new ComputeKernelHelper(compute, "Clear");
            _clearKernel.Set(VolumeRWID, _volume);
            _clearKernel.Set(ColorVolumeRWID, _colorVolume);

            _integrateKernel = new ComputeKernelHelper(compute, "Integrate");
            _integrateKernel.Set(VolumeRWID, _volume);
            _integrateKernel.Set(ColorVolumeRWID, _colorVolume);

            _pruneKernel = new ComputeKernelHelper(compute, "Prune");
            _pruneKernel.Set(VolumeRWID, _volume);
            _pruneKernel.Set(ColorVolumeRWID, _colorVolume);

            _freezeKernel = new ComputeKernelHelper(compute, "FreezeInFrustum");
            _freezeKernel.Set(VolumeRWID, _volume);

            _unfreezeKernel = new ComputeKernelHelper(compute, "UnfreezeInFrustum");
            _unfreezeKernel.Set(VolumeRWID, _volume);

            _coverageKernel = new ComputeKernelHelper(compute, "CountSurfaceCoverage");
            _coverageKernel.Set(VolumeRWID, _volume);
            _coverageCounters = new ComputeBuffer(3, sizeof(uint));
            _coverageKernel.Set(CoverageCountersID, _coverageCounters);
            compute.SetTexture(_coverageKernel.KernelIndex, ColorVolumeReadID, _colorVolume);

            _dummyCamTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _dummyCamTex.SetPixel(0, 0, Color.black);
            _dummyCamTex.Apply(false, true);
        }

        private void OnDestroy()
        {
            ReleaseVolumes();
            _coverageCounters?.Release();
            _coverageCounters = null;
            if (_camFrameCopy) Destroy(_camFrameCopy);
            if (_dummyCamTex) Destroy(_dummyCamTex);
        }

        private void LateUpdate()
        {
            _gpuRetirement.DrainCompleted();
        }

        /// <summary>
        /// Destroys the TSDF + color volume RenderTextures and the frustum buffer to free GPU memory.
        /// The component stays alive; calling <see cref="CreateVolume"/> + <see cref="SetShaderConstants"/>
        /// re-allocates everything (handled transparently by the integration path).
        /// </summary>
        public void ReleaseVolumes()
        {
            _frustumVolume?.Release();
            _frustumVolume = null;
            _frustumReady = false;
            if (_volume)
            {
                _gpuRetirement.RetireAfterCurrentGpuWork(_volume);
                _volume = null;
            }
            if (_colorVolume)
            {
                _gpuRetirement.RetireAfterCurrentGpuWork(_colorVolume);
                _colorVolume = null;
            }
            IntegrationCount = 0;
            Logger.Info("VolumeIntegrator: GPU volumes released");
        }

        /// <summary>True when volumes have been released and need re-allocation before integration.</summary>
        public bool VolumesReleased => _volume == null;

        /// <summary>
        /// Allocate (or re-allocate) TSDF + color volumes and bring kernels +
        /// shader constants up to date. Idempotent — early-returns if volumes
        /// already exist. Handles three scenarios:
        /// <list type="bullet">
        ///   <item><description><b>First-ever scan</b>: builds compute kernels
        ///   from scratch (deferred from the old eager <c>Awake</c>/<c>Start</c>
        ///   path), allocates RTs, sets globals.</description></item>
        ///   <item><description><b>Resume after <see cref="ReleaseVolumes"/></b>:
        ///   re-allocates RTs and rebinds existing kernels via
        ///   <see cref="RebindKernelTextures"/>.</description></item>
        ///   <item><description><b>Already allocated</b>: no-op.</description></item>
        /// </list>
        /// Called by <see cref="RoomScanner.StartScanningAsync"/> and the heavy
        /// <c>RoomScanPersistence</c> save/full-load paths. The lightweight
        /// <c>LoadRefinedOnlyAsync</c> path intentionally skips this.
        /// </summary>
        public void ReallocateVolumes()
        {
            if (_volume != null) return;

            // ComputeKernelHelper is a struct — use its readonly Shader
            // backing field as the "never initialized" sentinel.
            bool firstAlloc = (_clearKernel.Shader == null);

            if (!CreateVolume())
                return;

            if (firstAlloc) InitKernels();
            else            RebindKernelTextures();

            SetShaderConstants();
            Clear();

            if (DepthCapture.Instance != null)
                DepthCapture.Instance.SetVoxelParams(voxelDistance, voxelSize);

            Logger.Info(firstAlloc
                ? "VolumeIntegrator: GPU resources allocated lazily on first scan/save/full-load."
                : "VolumeIntegrator: GPU volumes re-allocated after release.");
        }

        private void RebindKernelTextures()
        {
            _clearKernel.Set(VolumeRWID, _volume);
            _clearKernel.Set(ColorVolumeRWID, _colorVolume);
            _integrateKernel.Set(VolumeRWID, _volume);
            _integrateKernel.Set(ColorVolumeRWID, _colorVolume);
            _pruneKernel.Set(VolumeRWID, _volume);
            _pruneKernel.Set(ColorVolumeRWID, _colorVolume);
            _freezeKernel.Set(VolumeRWID, _volume);
            _unfreezeKernel.Set(VolumeRWID, _volume);
            _coverageKernel.Set(VolumeRWID, _volume);
            compute.SetTexture(_coverageKernel.KernelIndex, ColorVolumeReadID, _colorVolume);
        }

        private void DispatchCoverageCount()
        {
            if (_volume == null || _coverageCounters == null) return;
            _coverageReadbackPending = true;

            uint[] zeros = { 0, 0, 0 };
            _coverageCounters.SetData(zeros);
            _coverageKernel.DispatchFit(_volume);

            AsyncGPUReadback.Request(_coverageCounters, OnCoverageReadback);
        }

        private void OnCoverageReadback(AsyncGPUReadbackRequest request)
        {
            _coverageReadbackPending = false;
            if (request.hasError) return;
            var data = request.GetData<uint>();
            if (data.Length < 3) return;
            SurfaceVoxelCount = (int)data[0];
            FrozenSurfaceCount = (int)data[1];
            ColoredSurfaceCount = (int)data[2];
        }

        private bool CreateVolume()
        {
            if (!TryCalculateActiveVolumeMemory(voxelCount, maxFrustumPositions,
                    out long tsdfBytes, out long colorBytes, out long frustumBytes) ||
                frustumBytes > MaximumStorageBufferRangeBytes)
            {
                Logger.Error($"VolumeIntegrator rejected unsafe GPU allocation: voxels={voxelCount}, " +
                    $"TSDF={tsdfBytes}, color={colorBytes}, frustum={frustumBytes}; " +
                    $"storage buffer must be <= {MaximumStorageBufferRangeBytes} bytes.");
                return false;
            }

            Logger.Info($"TSDF volume: {voxelCount} RG8_SNorm = {tsdfBytes / (1024 * 1024)}MB");
            Logger.Info($"Color volume: {voxelCount} RGBA8_UNorm = {colorBytes / (1024 * 1024)}MB");

            _volume = new RenderTexture(voxelCount.x, voxelCount.y, 0, GraphicsFormat.R8G8_SNorm, 0)
            {
                dimension = TextureDimension.Tex3D,
                volumeDepth = voxelCount.z,
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            _volume.Create();

            _colorVolume = new RenderTexture(voxelCount.x, voxelCount.y, 0, GraphicsFormat.R8G8B8A8_UNorm, 0)
            {
                dimension = TextureDimension.Tex3D,
                volumeDepth = voxelCount.z,
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            _colorVolume.Create();
            return true;
        }

        internal static bool TryCalculateActiveVolumeMemory(int3 count,
            int maximumFrustumPositions, out long tsdfBytes, out long colorBytes,
            out long frustumBytes)
        {
            tsdfBytes = 0;
            colorBytes = 0;
            frustumBytes = 0;
            if (count.x <= 0 || count.y <= 0 || count.z <= 0 ||
                maximumFrustumPositions <= 0)
                return false;

            try
            {
                long voxels = checked((long)count.x * count.y * count.z);
                tsdfBytes = checked(voxels * 2L);
                colorBytes = checked(voxels * 4L);
                frustumBytes = checked((long)maximumFrustumPositions * 12L);
                return true;
            }
            catch (OverflowException)
            {
                tsdfBytes = colorBytes = frustumBytes = long.MaxValue;
                return false;
            }
        }

        private void SetShaderConstants()
        {
            int3 s = voxelCount;
            compute.SetInts(VoxCountID, s.x, s.y, s.z);
            Shader.SetGlobalVector(VoxCountID, new Vector4(s.x, s.y, s.z, 0));

            compute.SetFloat(VoxSizeID, voxelSize);
            Shader.SetGlobalFloat(VoxSizeID, voxelSize);

            compute.SetFloat(VoxMinID, voxelMin);
            compute.SetFloat(VoxDistID, voxelDistance);
            Shader.SetGlobalFloat(VoxDistID, voxelDistance);

            compute.SetFloat(DepthDispThreshID, depthDisparityThreshold);
            compute.SetFloat(MaxUpdateDistID, maxUpdateDist);
            compute.SetFloat(BlendRateID, blendRate);
            compute.SetFloat(StabilityID, stability);
            compute.SetFloat(WeightGrowthID, weightGrowth);
            compute.SetFloat(MaxWeightID, maxWeight);
            ApplyFusionPolicyConstants();

            Shader.SetGlobalTexture(VolumeID, _volume);
            Shader.SetGlobalTexture(ColorVolumeID, _colorVolume);
            ApplyVolumeFrameShaderConstants();
        }

        private void ApplyFusionPolicyConstants()
        {
            if (compute == null) return;
            compute.SetInt(FusionProtectionEnabledID, protectStableSurfaces ? 1 : 0);
            compute.SetFloat(FusionStableConfidenceID, stableSurfaceConfidence);
            compute.SetFloat(FusionExistingQualityFloorID, 0.55f);
            compute.SetFloat(FusionQualityHysteresisID, surfaceQualityHysteresis);
            compute.SetFloat(FusionOppositeOrientationDotID, oppositeSurfaceNormalDot);
            compute.SetFloat(FusionSurfaceBandID, 0.85f);
            compute.SetFloat(FusionResidualToleranceID, stableSurfaceResidualTolerance);
            compute.SetFloat(FusionResidualConfidenceSlackID, 0.18f);
            compute.SetFloat(FusionImprovementResidualScaleID, 1.5f);
            compute.SetFloat(FusionStableBlendFloorID, 0.15f);
            compute.SetFloat(FusionNormalMinWeightID, minMeshWeight);
            compute.SetFloat(FusionBackBandVoxelsID, behindSurfaceBandVoxels);
        }

        /// <summary>
        /// Places the reusable TSDF volume in world space without resampling its local voxels.
        /// Depth, exclusion, freeze, and camera-color kernels receive both transform directions.
        /// </summary>
        public bool TrySetWorldFromVolume(RigidPoseData worldFromVolume, out string error)
        {
            error = null;
            Vector3 position = worldFromVolume.position;
            Quaternion rotation = worldFromVolume.rotation;
            float norm = rotation.x * rotation.x + rotation.y * rotation.y +
                         rotation.z * rotation.z + rotation.w * rotation.w;
            if (!IsFinite(position.x) || !IsFinite(position.y) || !IsFinite(position.z) ||
                !IsFinite(norm) || Mathf.Abs(norm - 1f) > 0.01f)
            {
                error = "Volume frame must contain a finite position and unit quaternion.";
                return false;
            }

            _worldFromVolume = worldFromVolume;
            _worldFromVolumeMatrix = worldFromVolume.ToMatrix();
            _volumeFromWorldMatrix = worldFromVolume.Inverse().ToMatrix();
            ApplyVolumeFrameShaderConstants();
            VolumeFrameChanged?.Invoke(worldFromVolume);
            return true;
        }

        private void ApplyVolumeFrameShaderConstants()
        {
            if (compute != null)
            {
                compute.SetMatrix(WorldFromVolumeID, _worldFromVolumeMatrix);
                compute.SetMatrix(VolumeFromWorldID, _volumeFromWorldMatrix);
            }
            Shader.SetGlobalMatrix(WorldFromVolumeID, _worldFromVolumeMatrix);
            Shader.SetGlobalMatrix(VolumeFromWorldID, _volumeFromWorldMatrix);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        /// <summary>
        /// Zeros the TSDF and color volumes on the GPU. No-op if volumes
        /// haven't been allocated yet (lazy alloc — see
        /// <see cref="ReallocateVolumes"/>).
        /// </summary>
        public void Clear()
        {
            if (_volume == null || _clearKernel.Shader == null) return;
            _clearKernel.Set(VolumeRWID, _volume);
            _clearKernel.Set(ColorVolumeRWID, _colorVolume);
            _clearKernel.DispatchFit(_volume);
            Cleared?.Invoke();
        }

        /// <summary>
        /// Resample TSDF + color from the current (relocated) grid into a new identity grid.
        /// After this call the volume data lives in the current tracking/world frame.
        /// </summary>
        public void BakeRelocation(Matrix4x4 relocationMatrix)
        {
            if (_volume == null || _colorVolume == null || compute == null)
                return;

            Matrix4x4 invRelocation = relocationMatrix.inverse;
            int3 vc = voxelCount;

            var dstTsdf = new RenderTexture(vc.x, vc.y, 0, _volume.graphicsFormat, 0)
            {
                dimension = TextureDimension.Tex3D,
                volumeDepth = vc.z,
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            dstTsdf.Create();

            var dstColor = new RenderTexture(vc.x, vc.y, 0, _colorVolume.graphicsFormat, 0)
            {
                dimension = TextureDimension.Tex3D,
                volumeDepth = vc.z,
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            dstColor.Create();

            int kernel = compute.FindKernel("BakeRelocation");
            compute.SetInts(Shader.PropertyToID("gsVoxCount"), vc.x, vc.y, vc.z);
            compute.SetFloat(Shader.PropertyToID("gsVoxSize"), voxelSize);
            compute.SetTexture(kernel, Shader.PropertyToID("gsBakeSrcTsdf"), _volume);
            compute.SetTexture(kernel, Shader.PropertyToID("gsBakeSrcColor"), _colorVolume);
            compute.SetTexture(kernel, VolumeRWID, dstTsdf);
            compute.SetTexture(kernel, ColorVolumeRWID, dstColor);
            compute.SetMatrix(Shader.PropertyToID("gsBakeInvRelocation"), invRelocation);

            int tx = Mathf.CeilToInt(vc.x / 4f);
            int ty = Mathf.CeilToInt(vc.y / 4f);
            int tz = Mathf.CeilToInt(vc.z / 4f);
            compute.Dispatch(kernel, tx, ty, tz);
            GL.Flush();

            // Swap volumes: destroy old, adopt baked textures.
            // Avoids Graphics.CopyTexture on 3D RTs which can silently fail on Vulkan/Quest.
            Destroy(_volume);
            Destroy(_colorVolume);
            _volume = dstTsdf;
            _colorVolume = dstColor;

            // Rebind global texture references (used by render shader for freeze tint etc.)
            Shader.SetGlobalTexture(VolumeID, _volume);
            Shader.SetGlobalTexture(ColorVolumeID, _colorVolume);

            // Rebind per-kernel UAV references so subsequent integrations/clears use new textures
            RebindVolumeTextures();

            Logger.Info($"BakeRelocation complete — resampled {vc} voxels, " +
                      $"reloc row0={relocationMatrix.GetRow(0)}, inv row0={invRelocation.GetRow(0)}");
        }

        private void RebindVolumeTextures()
        {
            if (_clearKernel.Shader == null) return;
            RebindKernelTextures();
        }

        /// <summary>
        /// Freeze all voxels currently visible in the camera frustum.
        /// Frozen voxels are encoded as negative weight and skip integration.
        /// Requires camera data to have been provided via SetCameraData.
        /// </summary>
        public void FreezeInView(Vector3 camPos, Quaternion camRot,
            Vector2 focalLen, Vector2 principalPt, Vector2 sensorRes, Vector2 currentRes)
        {
            if (_volume == null || _freezeKernel.Shader == null)
            {
                Logger.Warning("FreezeInView called before GPU resources allocated; ignored.");
                return;
            }
            SetFrustumCameraUniforms(_freezeKernel, camPos, camRot,
                focalLen, principalPt, sensorRes, currentRes);
            _freezeKernel.Set(VolumeRWID, _volume);
            _freezeKernel.DispatchFit(_volume);
            Logger.Info("FreezeInView dispatched");
        }

        /// <summary>
        /// Unfreeze all frozen voxels currently visible in the camera frustum.
        /// </summary>
        public void UnfreezeInView(Vector3 camPos, Quaternion camRot,
            Vector2 focalLen, Vector2 principalPt, Vector2 sensorRes, Vector2 currentRes)
        {
            if (_volume == null || _unfreezeKernel.Shader == null)
            {
                Logger.Warning("UnfreezeInView called before GPU resources allocated; ignored.");
                return;
            }
            SetFrustumCameraUniforms(_unfreezeKernel, camPos, camRot,
                focalLen, principalPt, sensorRes, currentRes);
            _unfreezeKernel.Set(VolumeRWID, _volume);
            _unfreezeKernel.DispatchFit(_volume);
            Logger.Info("UnfreezeInView dispatched");
        }

        private void SetFrustumCameraUniforms(ComputeKernelHelper kernel, Vector3 camPos,
            Quaternion camRot, Vector2 focalLen, Vector2 principalPt,
            Vector2 sensorRes, Vector2 currentRes)
        {
            compute.SetVector(CamPosID, camPos);
            compute.SetMatrix(CamInvRotID, Matrix4x4.Rotate(Quaternion.Inverse(camRot)));
            compute.SetVector(CamFocalLenID, focalLen);
            compute.SetVector(CamPrincipalPtID, principalPt);
            compute.SetVector(CamSensorResID, sensorRes);
            compute.SetVector(CamCurrentResID, currentRes);
        }

        /// <summary>
        /// Provide a camera frame and intrinsics for color integration this tick.
        /// Uses direct pinhole projection (matching Meta PCA samples) instead of VP matrix.
        /// Call before Integrate() each frame. Pass null frame to skip color.
        /// </summary>
        public void SetCameraData(Texture frame, Vector3 camPos, Quaternion camRot,
            Vector2 focalLength, Vector2 principalPoint, Vector2 sensorRes, Vector2 currentRes)
        {
            _pendingCamFrame = frame;
            _pendingCamPos = camPos;
            _pendingCamRot = camRot;
            _pendingFocalLen = focalLength;
            _pendingPrincipalPt = principalPoint;
            _pendingSensorRes = sensorRes;
            _pendingCurrentRes = currentRes;
        }

        /// <summary>
        /// Ensures _camFrameCopy exists and blits the pending frame to it.
        /// Called internally before Integrate() uses it for compute shader color integration.
        /// </summary>
        private void EnsureCamFrameCopy()
        {
            if (_pendingCamFrame == null) return;
            int w = _pendingCamFrame.width;
            int h = _pendingCamFrame.height;
            if (_camFrameCopy == null || _camFrameCopy.width != w || _camFrameCopy.height != h)
            {
                if (_camFrameCopy) Destroy(_camFrameCopy);
                _camFrameCopy = new RenderTexture(w, h, 0, GraphicsFormat.R8G8B8A8_SRGB, 0)
                {
                    enableRandomWrite = false,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                _camFrameCopy.Create();
            }
            Graphics.Blit(_pendingCamFrame, _camFrameCopy);
        }

        /// <summary>
        /// Builds the frustum sample positions buffer used by the Integrate kernel.
        /// Called lazily on first integration or after a volume clear/load.
        /// </summary>
        public void SetupFrustumVolume()
        {
            if (!DepthCapture.DepthAvailable) return;

            Matrix4x4 depthProj = Shader.GetGlobalMatrixArray(DepthCapture.ProjID)[0];
            FrustumPlanes frustum = depthProj.decomposeProjection;
            frustum.zFar = maxUpdateDist;

            var positions = new List<Vector3>(Mathf.Min(maxFrustumPositions, 200000));

            float ls = frustum.left / frustum.zNear;
            float rs = frustum.right / frustum.zNear;
            float ts = frustum.top / frustum.zNear;
            float bs = frustum.bottom / frustum.zNear;

            float step = voxelSize;
            bool capped = false;

            for (float z = frustum.zNear; z < frustum.zFar && !capped; z += step)
            {
                float xMin = ls * z + step;
                float xMax = rs * z - step;
                float yMin = bs * z + step;
                float yMax = ts * z - step;

                for (float x = xMin; x < xMax && !capped; x += step)
                for (float y = yMin; y < yMax; y += step)
                {
                    var v = new Vector3(x, y, -z);
                    float mag = v.magnitude;
                    if (mag > minUpdateDist && mag < maxUpdateDist)
                    {
                        positions.Add(v);
                        if (positions.Count >= maxFrustumPositions)
                        {
                            capped = true;
                            break;
                        }
                    }
                }
            }

            if (positions.Count == 0) return;

            Logger.Info($"Frustum volume: {positions.Count} positions ({positions.Count * 12 / 1024}KB)");

            _frustumVolume?.Release();
            _frustumVolume = new ComputeBuffer(positions.Count, sizeof(float) * 3);
            _frustumVolume.SetData(positions);
            _integrateKernel.Set(FrustumVolumeID, _frustumVolume);
            _frustumReady = true;
        }

        /// <summary>
        /// Dispatches one TSDF + color integration pass from the current depth frame.
        /// Handles frustum setup, exclusion zones, warmup clearing, and periodic pruning.
        /// </summary>
        public void Integrate()
        {
            using ProfilerMarker.AutoScope integrateScope = IntegrateMarker.Auto();
            var dc = DepthCapture.Instance;
            if (dc == null || !DepthCapture.DepthAvailable || dc.DepthTex == null) return;
            // Defensive: with lazy GPU alloc a stray Integrate() before
            // ReallocateVolumes can land here. RoomScanner.StartScanning()
            // always calls ReallocateVolumes first, so this is just a
            // safety net.
            if (_volume == null || _integrateKernel.Shader == null) return;
            if (!_frustumReady) SetupFrustumVolume();
            if (!_frustumReady) return;
            double integrationCpuStart = Time.realtimeSinceStartupAsDouble;

            dc.UpdateDilationIfNeeded();

            compute.SetMatrixArray(DepthCapture.ViewID, dc.View);
            compute.SetMatrixArray(DepthCapture.ProjID, dc.Proj);
            compute.SetMatrixArray(DepthCapture.ViewInvID, dc.ViewInv);
            compute.SetMatrixArray(DepthCapture.ProjInvID, dc.ProjInv);

            int numExclusions = Mathf.Min(ExclusionZones.Count, 64);
            for (int i = 0; i < numExclusions; i++)
            {
                if (ExclusionZones[i] != null)
                    _exclusionPositions[i] = ExclusionZones[i].position;
            }
            compute.SetInt(NumExclusionsID, numExclusions);
            compute.SetVectorArray(ExclusionHeadsID, _exclusionPositions);

            compute.SetFloat(BlendRateID, blendRate);
            compute.SetFloat(StabilityID, stability);
            compute.SetFloat(WeightGrowthID, weightGrowth);
            compute.SetFloat(MaxWeightID, maxWeight);
            ApplyFusionPolicyConstants();

            EnsureCamFrameCopy();
            if (_pendingCamFrame != null && _camFrameCopy != null)
            {
                compute.SetTexture(_integrateKernel.KernelIndex, CamRGBID, _camFrameCopy);
                compute.SetInt(CamAvailableID, 1);
                compute.SetVector(CamPosID, _pendingCamPos);
                compute.SetMatrix(CamInvRotID, Matrix4x4.Rotate(Quaternion.Inverse(_pendingCamRot)));
                compute.SetVector(CamFocalLenID, _pendingFocalLen);
                compute.SetVector(CamPrincipalPtID, _pendingPrincipalPt);
                compute.SetVector(CamSensorResID, _pendingSensorRes);
                compute.SetVector(CamCurrentResID, _pendingCurrentRes);
                compute.SetFloat(CamExposureID, cameraExposure);
            }
            else
            {
                compute.SetTexture(_integrateKernel.KernelIndex, CamRGBID, _dummyCamTex);
                compute.SetInt(CamAvailableID, 0);
            }

            _integrateKernel.Set(DepthCapture.DepthTexID, dc.DepthTex);
            _integrateKernel.Set(DepthCapture.NormTexID, dc.NormTex);
            _integrateKernel.Set(DepthCapture.DilatedDepthTexID, dc.DilatedDepthTex);

            using (FusionDispatchMarker.Auto())
                _integrateKernel.DispatchFit(_frustumVolume.count, 1);

            IntegrationCount++;
            _pendingCamFrame = null;

            if (warmupIntegrations > 0 && IntegrationCount == warmupIntegrations)
            {
                Logger.Info($"Warmup complete ({warmupIntegrations} frames), clearing volume to discard sensor startup noise");
                Clear();
            }

            float t = Time.time;
            if (t - _lastPruneTime >= pruneIntervalSeconds)
            {
                _lastPruneTime = t;
                _pruneKernel.Set(VolumeRWID, _volume);
                _pruneKernel.Set(ColorVolumeRWID, _colorVolume);
                _pruneKernel.DispatchFit(_volume);
            }

            if (coverageUpdateInterval > 0 && !_coverageReadbackPending)
            {
                _integrationsSinceCoverage++;
                if (_integrationsSinceCoverage >= coverageUpdateInterval)
                {
                    _integrationsSinceCoverage = 0;
                    DispatchCoverageCount();
                }
            }

            CaptureDevelopmentTiming(
                (Time.realtimeSinceStartupAsDouble - integrationCpuStart) * 1000.0);
            Integrated?.Invoke();
        }

        private void CaptureDevelopmentTiming(double cpuIntegrationMs)
        {
            if (!enableDevelopmentProfiling || !Debug.isDebugBuild ||
                profilingSampleInterval <= 0)
                return;

            FrameTimingManager.CaptureFrameTimings();
            uint count = FrameTimingManager.GetLatestTimings(1, _latestFrameTiming);
            if (count == 0)
                return;

            FrameTiming timing = _latestFrameTiming[0];
            if (timing.cpuFrameTime <= 0.0 && timing.gpuFrameTime <= 0.0)
                return;

            _profilingSamples++;
            _cpuIntegrationTotalMs += cpuIntegrationMs;
            _cpuIntegrationMaxMs = Math.Max(_cpuIntegrationMaxMs, cpuIntegrationMs);
            _cpuFrameTotalMs += timing.cpuFrameTime;
            _cpuFrameMaxMs = Math.Max(_cpuFrameMaxMs, timing.cpuFrameTime);
            _gpuFrameTotalMs += timing.gpuFrameTime;
            _gpuFrameMaxMs = Math.Max(_gpuFrameMaxMs, timing.gpuFrameTime);
            if (_profilingSamples < profilingSampleInterval)
                return;

            TryCalculateActiveVolumeMemory(voxelCount,
                _frustumVolume != null ? _frustumVolume.count : maxFrustumPositions,
                out long tsdfBytes, out long colorBytes, out long frustumBytes);
            double samples = _profilingSamples;
            Logger.Info(string.Format(CultureInfo.InvariantCulture,
                "QIS_TSDF_PROFILE samples={0} protection={1} " +
                "cpuIntegrationAvgMs={2:F3} cpuIntegrationMaxMs={3:F3} " +
                "cpuFrameAvgMs={4:F3} cpuFrameMaxMs={5:F3} " +
                "gpuFrameAvgMs={6:F3} gpuFrameMaxMs={7:F3} " +
                "tsdfBytes={8} colorBytes={9} frustumBytes={10} integrations={11}",
                _profilingSamples, protectStableSurfaces ? 1 : 0,
                _cpuIntegrationTotalMs / samples, _cpuIntegrationMaxMs,
                _cpuFrameTotalMs / samples, _cpuFrameMaxMs,
                _gpuFrameTotalMs / samples, _gpuFrameMaxMs,
                tsdfBytes, colorBytes, frustumBytes, IntegrationCount));

            _profilingSamples = 0;
            _cpuIntegrationTotalMs = 0.0;
            _cpuIntegrationMaxMs = 0.0;
            _cpuFrameTotalMs = 0.0;
            _cpuFrameMaxMs = 0.0;
            _gpuFrameTotalMs = 0.0;
            _gpuFrameMaxMs = 0.0;
        }

        /// <summary>
        /// Uploads CPU TSDF/color blobs into the 3D RenderTextures.
        /// Uses <see cref="GraphicsFormat"/> matching the volume RTs so
        /// <see cref="Graphics.CopyTexture"/> is valid on Metal/Vulkan (RG16 Texture3D ≠ R8G8_SNorm layout).
        /// </summary>
        public bool LoadVolumes(byte[] tsdfBytes, byte[] colorBytes, int integrationCount)
        {
            if (_volume == null || _colorVolume == null)
            {
                Logger.Error("Cannot load volumes: textures not created");
                return false;
            }

            int3 s = voxelCount;
            int expectedTsdf = s.x * s.y * s.z * 2;
            int expectedColor = s.x * s.y * s.z * 4;

            if (tsdfBytes.Length != expectedTsdf)
            {
                Logger.Error($"TSDF size mismatch: got {tsdfBytes.Length}, expected {expectedTsdf}");
                return false;
            }
            if (colorBytes.Length != expectedColor)
            {
                Logger.Error($"Color volume size mismatch: got {colorBytes.Length}, expected {expectedColor}");
                return false;
            }

            // Must match CreateVolume(): R8G8_SNorm TSDF + RGBA8_UNorm color
            var tsdfTex = new Texture3D(s.x, s.y, s.z, GraphicsFormat.R8G8_SNorm, TextureCreationFlags.None);
            tsdfTex.SetPixelData(tsdfBytes, 0);
            tsdfTex.Apply(false, false);
            Graphics.CopyTexture(tsdfTex, _volume);
            _gpuRetirement.RetireAfterCurrentGpuWork(tsdfTex);

            var colorTex = new Texture3D(s.x, s.y, s.z, GraphicsFormat.R8G8B8A8_UNorm, TextureCreationFlags.None);
            colorTex.SetPixelData(colorBytes, 0);
            colorTex.Apply(false, false);
            Graphics.CopyTexture(colorTex, _colorVolume);
            _gpuRetirement.RetireAfterCurrentGpuWork(colorTex);

            // Submit the uploads, but never destroy their source allocations merely after
            // GL.Flush: flush is not completion. The retirement queue above waits for an
            // actual GPU fence, preventing the Quest Vulkan "premature free" page fault.
            GL.Flush();

            IntegrationCount = integrationCount;
            _frustumReady = false;

            Logger.Info($"Volumes loaded: {s}, integrationCount={integrationCount}");
            return true;
        }
    }
}
