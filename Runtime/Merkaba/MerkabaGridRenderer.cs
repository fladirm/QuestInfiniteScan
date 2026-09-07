using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
using UnityEngine.XR;

namespace Genesis.RoomScan
{
    /// <summary>The sole procedural consumer of immutable Flower pages.</summary>
    [DisallowMultipleComponent]
    public sealed class MerkabaGridRenderer : MonoBehaviour
    {
        private static MerkabaGridRenderer _active;
        [FormerlySerializedAs("frameCompilerCompute")]
        [SerializeField] private ComputeShader readoutCompute;
        [SerializeField] private Shader renderShader;
        [SerializeField, Range(2f, 24f)] private float renderDistance = 12f;
        [SerializeField, Range(0f, 1f)] private float scanOpacity = 1f;
        [SerializeField] private bool readoutDrawEnabled = true;
        [SerializeField] private bool checkerReadoutEnabled;

        private MerkabaGrid _grid;
        private MerkabaIntegrator _integrator;
        private DepthCapture _depthCapture;
        private Material _material;
        private int _classifyKernel, _compactKernel, _publishKernel, _cullKernel;
        private bool _initialized;
        private volatile bool _gpuSubmissionSuspended;
        private uint _readoutRevision;
        private bool _hasResidencyCell;
        private int3 _residencyCell;
        private Matrix4x4 _classifiedGridToWorld;
        private bool _classificationPending;
        private bool _nativeClassification;
        private int3 _nativeResidencyCell;
        private Matrix4x4 _nativeGridToWorld;
        private MerkabaNativeVulkanExecutor.MerkabaNativeVulkanJob _nativeReadoutJob;
        private uint _nativeGeneration;
#if !UNITY_EDITOR && UNITY_ANDROID
        private readonly MerkabaNativeUniformTable _nativePageUniforms = new();
        private readonly IntPtr[] _nativePageResources =
            new IntPtr[MerkabaNativeVulkanExecutor.ResourceCount];
#endif
        private bool _managedBuildInFlight;
        private GraphicsFence _managedBuildFence;
        private bool _viewReady;
        private int _viewFrame = -1;
        private readonly Vector4[] _cullPlanes = new Vector4[12];
        private readonly Plane[] _eyePlanes = new Plane[6];
        private FineBrushDescriptor _finePreviewDescriptor;
        private Color _finePreviewColor;
        private bool _dynamicOcclusionEnabled = true;
        private bool _reportedDrawUnavailable;

        private static readonly int GridToWorldId = Shader.PropertyToID("_MerkabaGridToWorld");
        private static readonly int WorldToGridId = Shader.PropertyToID("_MerkabaWorldToGrid");
        private static readonly int PlaneBoundsId = Shader.PropertyToID("_M8FlowerPlaneErrorBounds");
        private static readonly int CullPlanesId = Shader.PropertyToID("_M8FlowerCullPlanes");
        private static readonly int ViewInstancesId = Shader.PropertyToID("_M8FlowerViewInstanceCount");
        private static readonly int GraphicsRetiredId = Shader.PropertyToID("_M8FlowerGraphicsRetiredGeneration");
        private static readonly int ScanOpacityId = Shader.PropertyToID("_ScanOpacity");
        private static readonly int FineCursorPositionId = Shader.PropertyToID("_FineCursorPosition");
        private static readonly int FineBrushAxisId = Shader.PropertyToID("_FineBrushAxis");
        private static readonly int FineBrushParamsId = Shader.PropertyToID("_FineBrushParams");
        private static readonly int FinePreviewColorId = Shader.PropertyToID("_FinePreviewColor");

        internal bool HasReadoutBuildInFlight => _nativeReadoutJob != null || _managedBuildInFlight;
        public float ScanOpacity
        {
            get => scanOpacity;
            set { scanOpacity = Mathf.Clamp01(value); ApplyOpacityState(); }
        }
        public bool ReadoutDrawEnabled { get => readoutDrawEnabled; set => readoutDrawEnabled = value; }
        public bool CheckerReadoutEnabled
        {
            get => checkerReadoutEnabled;
            set { checkerReadoutEnabled = value; SetKeyword("M8_CHECKER_READOUT", value); }
        }

        private void Awake()
        {
            _grid = GetComponent<MerkabaGrid>();
            _integrator = GetComponent<MerkabaIntegrator>();
            _depthCapture = GetComponent<DepthCapture>();
            if (_grid != null)
            {
                _grid.RegisterFlowerReadoutConsumer();
                _grid.Cleared += OnWorldCleared;
            }
        }

        private void OnEnable()
        {
            if (!_gpuSubmissionSuspended) _active = this;
            RenderPipelineManager.endContextRendering += OnContextRendered;
        }

        private void OnDisable()
        {
            if (_active == this) _active = null;
            RenderPipelineManager.endContextRendering -= OnContextRendered;
        }

        private void OnDestroy()
        {
            if (_active == this) _active = null;
            RenderPipelineManager.endContextRendering -= OnContextRendered;
            if (_grid != null) _grid.Cleared -= OnWorldCleared;
        }

        private void OnWorldCleared()
        {
            _hasResidencyCell = false;
            _viewReady = false;
        }

        internal void SuspendGpuSubmission()
        {
            _gpuSubmissionSuspended = true;
            _viewReady = false;
            if (_active == this) _active = null;
        }

        internal void ResumeGpuSubmission()
        {
            _gpuSubmissionSuspended = false;
            if (isActiveAndEnabled) _active = this;
        }

        internal async Task FinishCurrentReadoutAsync()
        {
            while (HasReadoutBuildInFlight)
            {
                PollReadoutCompletion();
                if (HasReadoutBuildInFlight) await Task.Yield();
            }
        }

        internal Action CaptureOwnedGpuResourceRelease()
        {
            Material captured = _material;
            bool released = false;
            return () =>
            {
                if (released) return;
                released = true;
                if (this != null) ReleaseOwnedResourcesAfterGpuRetirement();
                else if (captured != null) Destroy(captured);
            };
        }

        internal void ReleaseOwnedResourcesAfterGpuRetirement()
        {
            if (_material != null) Destroy(_material);
            _material = null;
            _initialized = false;
            _viewReady = false;
            _hasResidencyCell = false;
            _managedBuildInFlight = false;
            _readoutRevision = 0u;
        }

        internal static bool TryGetActive(Camera camera, out MerkabaGridRenderer renderer)
        {
            renderer = _active;
            return renderer != null && renderer._initialized && renderer.readoutDrawEnabled &&
                !renderer._gpuSubmissionSuspended && renderer._grid != null &&
                renderer._grid.FlowerGraphicsReadAllowed && renderer.isActiveAndEnabled &&
                camera == Camera.main;
        }

        private bool Initialize()
        {
            if (_initialized) return true;
            if (_grid == null || readoutCompute == null || renderShader == null)
            {
                Logger.Error("Merkaba Flower readout assets are not wired.");
                enabled = false;
                return false;
            }
            _grid.EnsureGpuResources();
            _classifyKernel = readoutCompute.FindProfiledKernel("ClassifyHotFlowerPages", MerkabaGpuStage.FlowerClassify);
            _compactKernel = readoutCompute.FindProfiledKernel("CompactDirtyFlowerSymbols", MerkabaGpuStage.FlowerCompact);
            _publishKernel = readoutCompute.FindProfiledKernel("PublishDirtyFlowerPages", MerkabaGpuStage.FlowerPublish);
            _cullKernel = readoutCompute.FindProfiledKernel("CullFlowerPages", MerkabaGpuStage.FlowerCull);
            foreach (int kernel in new[] { _classifyKernel, _compactKernel, _publishKernel, _cullKernel })
                _grid.BindWorldBuffers(readoutCompute, kernel);
            _material = new Material(renderShader)
            {
                name = "Merkaba Sphere-Flower procedural readout",
                enableInstancing = true
            };
            _grid.BindFlowerRenderResources(_material);
            ApplyOpacityState();
            ApplyFinePreviewState();
            SetKeyword("M8_ENVIRONMENT_OCCLUSION", _dynamicOcclusionEnabled);
            SetKeyword("M8_CHECKER_READOUT", checkerReadoutEnabled);
            _initialized = true;
            return true;
        }

        private void LateUpdate()
        {
            PollReadoutCompletion();
            if (_gpuSubmissionSuspended || _grid == null || _grid.GpuSubmissionSuspended || Camera.main == null)
                return;
            if (!Initialize()) return;
            _material.SetMatrix(GridToWorldId, _grid.GridToWorldMatrix);
            _material.SetMatrix(WorldToGridId, _grid.GridToWorldMatrix.inverse);
            _material.SetVector(PlaneBoundsId, PlaneBounds());
        }

        private Vector4 PlaneBounds()
        {
            if (_depthCapture != null && _depthCapture.TryGetFlowerPlaneBounds(out Vector2 bounds))
                return new Vector4(bounds.x, bounds.y, 0f, 1f);
            // Missing calibration cannot authorize a measured root. DIRT does
            // not use this measured-plane predicate.
            return new Vector4(float.PositiveInfinity, float.PositiveInfinity, 0f, 0f);
        }

        private void OnContextRendered(ScriptableRenderContext context, List<Camera> cameras)
        {
            Camera camera = Camera.main;
            if (camera == null || !cameras.Contains(camera) || !_initialized || _gpuSubmissionSuspended ||
                _grid == null || !_grid.FlowerGraphicsReadAllowed || HasReadoutBuildInFlight)
                return;
            if (_integrator != null && (_integrator.HasAttemptInFlight ||
                _integrator.HasFineEraseAttemptInFlight || _integrator.HasPendingFineErase))
                return;
            // Submit AFTER all views (including XR multipass), not after the
            // first eye: a new native lease must not suppress later readers.
            try { SubmitPageQuantum(camera); }
            catch (Exception exception) { Logger.Error("Flower page scheduling failed: " + exception.Message); }
        }

        private void ConfigureResidency(Camera camera, out Vector3 cameraGrid,
            out Vector3 metricDiagonal, out Vector3 metricCross, out int3 cell)
        {
            Matrix4x4 gridToWorld = _grid.GridToWorldMatrix;
            cameraGrid = gridToWorld.inverse.MultiplyPoint3x4(camera.transform.position);
            cell = (int3)math.floor((float3)cameraGrid / (MerkabaConstants.LatticeStep * 8f));
            MerkabaReadoutCoverage.WriteGridMetric(gridToWorld, out metricDiagonal, out metricCross);
            _classificationPending = !_hasResidencyCell || math.any(cell != _residencyCell) ||
                gridToWorld != _classifiedGridToWorld;
        }

        private float ScanDistance => _integrator != null ? _integrator.MaxUpdateDistance : 0f;
        private float DrawDistance => Mathf.Max(renderDistance, ScanDistance);
        private float WarmDistance => DrawDistance + MerkabaSpatial.BlockWorldSize;

        private void SubmitPageQuantum(Camera camera)
        {
            ConfigureResidency(camera, out Vector3 center, out Vector3 diagonal, out Vector3 cross, out int3 cell);
            uint revision = NextNonZero(ref _readoutRevision);
#if !UNITY_EDITOR && UNITY_ANDROID
            MerkabaNativeUniformTable uniforms = _nativePageUniforms;
            uniforms.Reset();
            uniforms.Vector3("_M8CameraGridMeters", center);
            uniforms.Vector3("_M8GridMetricDiagonal", diagonal);
            uniforms.Vector3("_M8GridMetricCross", cross);
            uniforms.Float("_M8RenderDistance", DrawDistance);
            uniforms.Float("_M8WarmDistance", WarmDistance);
            uniforms.Float("_M8ScanDistance", ScanDistance);
            uniforms.UInt("_M8FlowerClassifySingleSlot", 0u);
            uniforms.UInt("_M8FlowerClassifySlot", 0u);
            Vector4 planeBounds = PlaneBounds();
            uniforms.Vector2("_M8FlowerPlaneErrorBounds", new Vector2(planeBounds.x, planeBounds.y));
            uniforms.Matrix("_MerkabaGridToWorld", _grid.GridToWorldMatrix);
            IntPtr[] resources = _nativePageResources;
            _grid.FillNativeExecutorWorldResources(resources);
            uint generation = _grid.BeginNativeDualMutation(uniforms);
            uniforms.UInt("_M8FlowerGraphicsRetiredGeneration", _grid.FlowerGraphicsRetiredGeneration);
            var passes = MerkabaNativeVulkanExecutor.FlowerPasses.Compact |
                MerkabaNativeVulkanExecutor.FlowerPasses.Publish;
            if (_classificationPending) passes |= MerkabaNativeVulkanExecutor.FlowerPasses.Classify;
            MerkabaNativeVulkanExecutor.MerkabaNativeVulkanJob job = null;
            CommandBuffer command = CommandBufferPool.Get("Merkaba bounded Flower page publication");
            bool recorded = false;
            try
            {
                if (!MerkabaNativeVulkanExecutor.TryCreateJob(MerkabaNativeVulkanExecutor.JobKind.FlowerReadout,
                    revision, resources, uniforms, 0, 0, 0, (int)passes, out job))
                {
                    _grid.CancelDualMutationBeforeSubmit(generation);
                    return;
                }
                job.RecordPrepareAndSubmit(command);
                recorded = true;
                _nativeReadoutJob = job;
                _nativeGeneration = generation;
                _nativeClassification = _classificationPending;
                _nativeResidencyCell = cell;
                _nativeGridToWorld = _grid.GridToWorldMatrix;
                Graphics.ExecuteCommandBuffer(command);
            }
            catch (Exception exception)
            {
                if (!recorded)
                {
                    job?.CancelBeforeExecution();
                    job?.Dispose();
                    _grid.CancelDualMutationBeforeSubmit(generation);
                }
                else _grid.CompleteNativeDualMutation(generation, false);
                Logger.Error("Flower page submission failed: " + exception.Message);
            }
            finally { CommandBufferPool.Release(command); }
#else
            CommandBuffer command = CommandBufferPool.Get("Merkaba bounded Flower page publication");
            uint generation = 0u;
            bool submitted = false;
            bool timed = false;
            try
            {
                generation = _grid.RecordDualMutation(command, readoutCompute);
                timed = MerkabaGpuTimestamps.TryAcquire(CaptureOwner.FlowerPages, revision, command);
                command.SetComputeIntParam(readoutCompute, GraphicsRetiredId,
                    checked((int)_grid.FlowerGraphicsRetiredGeneration));
                command.SetComputeVectorParam(readoutCompute, "_M8CameraGridMeters", center);
                command.SetComputeVectorParam(readoutCompute, "_M8GridMetricDiagonal", diagonal);
                command.SetComputeVectorParam(readoutCompute, "_M8GridMetricCross", cross);
                command.SetComputeFloatParam(readoutCompute, "_M8RenderDistance", DrawDistance);
                command.SetComputeFloatParam(readoutCompute, "_M8WarmDistance", WarmDistance);
                command.SetComputeFloatParam(readoutCompute, "_M8ScanDistance", ScanDistance);
                command.SetComputeIntParam(readoutCompute, "_M8FlowerClassifySingleSlot", 0);
                command.SetComputeIntParam(readoutCompute, "_M8FlowerClassifySlot", 0);
                command.SetComputeVectorParam(readoutCompute, PlaneBoundsId, PlaneBounds());
                command.SetComputeMatrixParam(readoutCompute, GridToWorldId, _grid.GridToWorldMatrix);
                if (_classificationPending)
                    command.DispatchComputeProfiled(readoutCompute, _classifyKernel, 256, 1, 1);
                command.DispatchComputeProfiled(readoutCompute, _compactKernel, 1, 1, 1);
                command.DispatchComputeProfiled(readoutCompute, _publishKernel, 256, 1, 1);
                MerkabaGpuTimestamps.End(CaptureOwner.FlowerPages, command, timed);
                _managedBuildFence = command.CreateGraphicsFence(GraphicsFenceType.AsyncQueueSynchronisation,
                    SynchronisationStageFlags.AllGPUOperations);
                _grid.SubmitDualMutation(command, generation);
                submitted = true;
                MerkabaGpuTimestamps.Complete(CaptureOwner.FlowerPages, timed, true);
                _managedBuildInFlight = true;
                if (_classificationPending) PublishResidencyCell(cell, _grid.GridToWorldMatrix);
            }
            catch (Exception exception)
            {
                if (!submitted)
                {
                    MerkabaGpuTimestamps.Complete(CaptureOwner.FlowerPages, timed, false);
                    _grid.CancelDualMutationBeforeSubmit(generation);
                }
                Logger.Error("Flower page submission failed: " + exception.Message);
            }
            finally { CommandBufferPool.Release(command); }
#endif
        }

        private void PublishResidencyCell(int3 cell, Matrix4x4 gridToWorld)
        {
            _residencyCell = cell;
            _classifiedGridToWorld = gridToWorld;
            _hasResidencyCell = true;
        }

        private void PollReadoutCompletion()
        {
            if (_managedBuildInFlight && _managedBuildFence.passed) _managedBuildInFlight = false;
            if (_nativeReadoutJob == null || !_nativeReadoutJob.Poll(out string error)) return;
            bool succeeded = string.IsNullOrEmpty(error);
            _grid.CompleteNativeDualMutation(_nativeGeneration, succeeded);
            _nativeReadoutJob.Dispose();
            _nativeReadoutJob = null;
            _nativeGeneration = 0u;
            if (!succeeded) { Logger.Error(error); return; }
            if (_nativeClassification) PublishResidencyCell(_nativeResidencyCell, _nativeGridToWorld);
            // FRONT is GPU-atomic. CPU completion never reads a status word to
            // select a buffer or to authorize a draw.
        }

        internal void RecordViewCull(CommandBuffer command, Camera camera)
        {
            _viewReady = false;
            if (command == null || camera == null || !_initialized || !_grid.FlowerGraphicsReadAllowed ||
                _gpuSubmissionSuspended || !readoutDrawEnabled) return;
            uint views = camera.stereoEnabled &&
                XRSettings.stereoRenderingMode == XRSettings.StereoRenderingMode.SinglePassInstanced ? 2u : 1u;
            Matrix4x4 gridToWorld = _grid.GridToWorldMatrix;
            for (int eye = 0; eye < 2; eye++)
            {
                Matrix4x4 view = camera.stereoEnabled
                    ? camera.GetStereoViewMatrix((Camera.StereoscopicEye)eye) : camera.worldToCameraMatrix;
                Matrix4x4 projection = camera.stereoEnabled
                    ? camera.GetStereoProjectionMatrix((Camera.StereoscopicEye)eye) : camera.projectionMatrix;
                GeometryUtility.CalculateFrustumPlanes(projection * view * gridToWorld, _eyePlanes);
                for (int plane = 0; plane < 6; plane++)
                {
                    Plane p = _eyePlanes[plane];
                    if (!float.IsFinite(p.normal.x) || !float.IsFinite(p.normal.y) ||
                        !float.IsFinite(p.normal.z) || !float.IsFinite(p.distance)) return;
                    _cullPlanes[6 * eye + plane] = new Vector4(p.normal.x, p.normal.y, p.normal.z, p.distance);
                }
            }
            if (!MerkabaNativeVulkanExecutor.RecordFlowerCullReset(command, _grid.M8FlowerIndirectCommands))
            {
                ReportDrawUnavailable();
                return;
            }
            command.SetComputeVectorArrayParam(readoutCompute, CullPlanesId, _cullPlanes);
            command.SetComputeIntParam(readoutCompute, ViewInstancesId, checked((int)views));
            bool timed = MerkabaGpuTimestamps.TryAcquire(CaptureOwner.FlowerPages,
                _readoutRevision == 0u ? 1u : _readoutRevision, command);
            command.DispatchComputeProfiled(readoutCompute, _cullKernel, 256, 1, 1);
            MerkabaGpuTimestamps.End(CaptureOwner.FlowerPages, command, timed);
            MerkabaGpuTimestamps.Complete(CaptureOwner.FlowerPages, timed, true);
            _viewFrame = Time.frameCount;
            _viewReady = true;
        }

        internal void RecordRenderPass(RasterCommandBuffer command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (!_viewReady || _viewFrame != Time.frameCount || !_initialized || _gpuSubmissionSuspended ||
                !_grid.FlowerGraphicsReadAllowed || !readoutDrawEnabled || scanOpacity <= 0f) return;
            if (!MerkabaNativeVulkanExecutor.RecordFlowerIndirectRegistration(command, _grid.M8FlowerIndirectCommands))
            {
                ReportDrawUnavailable();
                return;
            }
            bool timed = MerkabaGpuTimestamps.TryAcquire(CaptureOwner.Draw,
                _readoutRevision == 0u ? 1u : _readoutRevision, command);
            command.DrawProceduralIndirectProfiled(_grid.M8FlowerIndices, Matrix4x4.identity,
                _material, 0, MeshTopology.Triangles, _grid.M8FlowerIndirectCommands, 0);
            MerkabaGpuTimestamps.End(CaptureOwner.Draw, command, timed);
            MerkabaGpuTimestamps.Complete(CaptureOwner.Draw, timed, true);
        }

        internal void SetDynamicOcclusionEnabled(bool enabled)
        {
            _dynamicOcclusionEnabled = enabled;
            SetKeyword("M8_ENVIRONMENT_OCCLUSION", enabled);
        }

        private void ReportDrawUnavailable()
        {
            if (_reportedDrawUnavailable) return;
            _reportedDrawUnavailable = true;
            Logger.Error("Flower indexed indirect-count draw is unavailable; no legacy mesh fallback exists.");
        }

        internal void SetFineSurfacePreview(FineBrushDescriptor descriptor, Color color)
        {
            _finePreviewDescriptor = descriptor;
            _finePreviewColor = color;
            ApplyFinePreviewState();
        }

        private void SetKeyword(string keyword, bool enabled)
        {
            if (_material == null) return;
            if (enabled) _material.EnableKeyword(keyword);
            else _material.DisableKeyword(keyword);
        }

        private void ApplyOpacityState()
        {
            if (_material == null) return;
            _material.SetFloat(ScanOpacityId, scanOpacity);
            _material.renderQueue = (int)RenderQueue.Geometry;
            SetKeyword("M8_ALPHA_COVERAGE", scanOpacity < 1f);
        }

        private void ApplyFinePreviewState()
        {
            if (_material == null) return;
            bool active = _finePreviewDescriptor.IsActive;
            Color tint = _finePreviewColor;
            tint.a = 0.25f;
            _material.SetVector(FineCursorPositionId, _finePreviewDescriptor.CursorPosition);
            _material.SetVector(FineBrushAxisId, _finePreviewDescriptor.Axis);
            _material.SetVector(FineBrushParamsId, active ? new Vector4(1f,
                _finePreviewDescriptor.Radius * _finePreviewDescriptor.Radius,
                _finePreviewDescriptor.Length, 0f) : Vector4.zero);
            _material.SetColor(FinePreviewColorId, tint);
            SetKeyword("M8_FINE_PREVIEW", active);
        }

        private static uint NextNonZero(ref uint value)
        {
            unchecked { if (++value == 0u) value = 1u; }
            return value;
        }
    }
}
