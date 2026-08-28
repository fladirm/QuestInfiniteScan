using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Genesis.RoomScan
{
    /// <summary>
    /// Applies QRS depth/normal/dilation/projection observations to reversible evidence.
    /// The Quest path is a bounded surface queue plus a compact existing-state carve
    /// queue; the static
    /// reference entry point is its deterministic semantic oracle for tests/replay.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MerkabaIntegrator : MonoBehaviour
    {
        [SerializeField] private ComputeShader compute;
        [SerializeField, Range(1f, 8f)] private float maxUpdateDistance = 5f;
        [SerializeField, Range(1f, 10f)] private float cameraExposure = 3f;
        [SerializeField, Min(0)] private int warmupIntegrations = 3;

        private MerkabaGrid _grid;
        private DepthCapture _depthCapture;
        private int _generateSurfaceKernel = -1;
        private int _prepareArgsKernel = -1;
        private int _surfaceKernel = -1;
        private int _gatherCarveKernel = -1;
        private int _carveKernel = -1;
        private bool _cameraFrameAvailable;
        private Vector3 _pendingCameraPosition;
        private Quaternion _pendingCameraRotation;
        private Vector2 _pendingFocalLength;
        private Vector2 _pendingPrincipalPoint;
        private Vector2 _pendingSensorResolution;
        private Vector2 _pendingCurrentResolution;
        private RenderTexture _cameraFrameCopy;
        private Texture2D _dummyCameraTexture;
        private readonly Vector4[] _exclusionPositions = new Vector4[64];

        public readonly List<Transform> ExclusionZones = new();
        public int IntegrationCount { get; private set; }
        public float MaxUpdateDistance => maxUpdateDistance;
        internal RenderTexture OwnedCameraFrame => _cameraFrameCopy;
        internal bool CameraFrameAvailable => _cameraFrameAvailable;
        public event Action Integrated;

        private static readonly int KernelsId = Shader.PropertyToID("_MerkabaKernels");
        private static readonly int PageCoordsId = Shader.PropertyToID("_MerkabaPageCoords");
        private static readonly int PageNeighboursId = Shader.PropertyToID("_MerkabaPageNeighbours");
        private static readonly int IntegrationSlotsId = Shader.PropertyToID("_MerkabaIntegrationSlots");
        private static readonly int IntegrationEnabledId = Shader.PropertyToID("_MerkabaIntegrationEnabledSlots");
        private static readonly int PublicationDirtyId =
            Shader.PropertyToID("_MerkabaPublicationDirtyChunks");
        private static readonly int IntegrationCountId = Shader.PropertyToID("_MerkabaIntegrationChunkCount");
        private static readonly int GridToWorldId = Shader.PropertyToID("_MerkabaGridToWorld");
        private static readonly int WorldToGridId = Shader.PropertyToID("_MerkabaWorldToGrid");
        private static readonly int MaxDistanceId = Shader.PropertyToID("_MerkabaMaxUpdateDistance");
        private static readonly int PageHashId = Shader.PropertyToID("_MerkabaPageHash");
        private static readonly int PageHashCapacityId = Shader.PropertyToID("_MerkabaPageHashCapacity");
        private static readonly int WorkCapacityId = Shader.PropertyToID("_MerkabaWorkCapacity");
        private static readonly int WorkCountId = Shader.PropertyToID("_MerkabaWorkCount");
        private static readonly int IndirectArgsId = Shader.PropertyToID("_MerkabaIndirectArgs");
        private static readonly int SurfaceBitsId = Shader.PropertyToID("_MerkabaSurfaceCandidateBits");
        private static readonly int SurfaceQueueId = Shader.PropertyToID("_MerkabaSurfaceQueue");
        private static readonly int SurfaceCountId = Shader.PropertyToID("_MerkabaSurfaceCount");
        private static readonly int SurfaceQueueReadId =
            Shader.PropertyToID("_MerkabaSurfaceQueueRead");
        private static readonly int SurfaceCountReadId =
            Shader.PropertyToID("_MerkabaSurfaceCountRead");
        private static readonly int CarveListedBitsId = Shader.PropertyToID("_MerkabaCarveListedBits");
        private static readonly int CarveLocalIndicesId = Shader.PropertyToID("_MerkabaCarveLocalIndices");
        private static readonly int CarveCountsId = Shader.PropertyToID("_MerkabaCarveCounts");
        private static readonly int CarveQueueId = Shader.PropertyToID("_MerkabaCarveQueue");
        private static readonly int CarveCountId = Shader.PropertyToID("_MerkabaCarveCount");
        private static readonly int CarveQueueReadId =
            Shader.PropertyToID("_MerkabaCarveQueueRead");
        private static readonly int CarveCountReadId =
            Shader.PropertyToID("_MerkabaCarveCountRead");
        private static readonly int GpuPublicationDirtyQueueId =
            Shader.PropertyToID("_MerkabaGpuPublicationDirtyQueue");
        private static readonly int ExclusionCountId = Shader.PropertyToID("_MerkabaExclusionCount");
        private static readonly int ExclusionHeadsId = Shader.PropertyToID("_MerkabaExclusionHeads");
        private static readonly int CameraRgbId = Shader.PropertyToID("_MerkabaCameraRgb");
        private static readonly int CameraAvailableId = Shader.PropertyToID("_MerkabaCameraAvailable");
        private static readonly int CameraPositionId = Shader.PropertyToID("_MerkabaCameraPosition");
        private static readonly int CameraInverseRotationId = Shader.PropertyToID("_MerkabaCameraInverseRotation");
        private static readonly int CameraFocalLengthId = Shader.PropertyToID("_MerkabaCameraFocalLength");
        private static readonly int CameraPrincipalPointId = Shader.PropertyToID("_MerkabaCameraPrincipalPoint");
        private static readonly int CameraSensorResolutionId = Shader.PropertyToID("_MerkabaCameraSensorResolution");
        private static readonly int CameraCurrentResolutionId = Shader.PropertyToID("_MerkabaCameraCurrentResolution");
        private static readonly int CameraExposureId = Shader.PropertyToID("_MerkabaCameraExposure");

        private void Awake()
        {
            _grid = GetComponent<MerkabaGrid>();
            _depthCapture = GetComponent<DepthCapture>();
            if (compute != null)
            {
                _generateSurfaceKernel = compute.FindKernel("GenerateSurfaceCandidates");
                _prepareArgsKernel = compute.FindKernel("PrepareIndirectArgs");
                _surfaceKernel = compute.FindKernel("IntegrateSurfaceCandidates");
                _gatherCarveKernel = compute.FindKernel("GatherCarveCandidates");
                _carveKernel = compute.FindKernel("IntegrateCarveCandidates");
            }
            _dummyCameraTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _dummyCameraTexture.SetPixel(0, 0, Color.black);
            _dummyCameraTexture.Apply(false, true);
            _depthCapture?.SetVoxelParams(
                MerkabaConstants.SupportSize + MerkabaConstants.LatticeStep,
                MerkabaConstants.LatticeStep);
        }

        private void OnDestroy()
        {
            if (_cameraFrameCopy != null) Destroy(_cameraFrameCopy);
            if (_dummyCameraTexture != null) Destroy(_dummyCameraTexture);
        }

        public void SetCameraData(Texture frame, Vector3 position, Quaternion rotation,
            Vector2 focalLength, Vector2 principalPoint, Vector2 sensorResolution,
            Vector2 currentResolution)
        {
            _cameraFrameAvailable = frame != null;
            if (_cameraFrameAvailable) CopyCameraFrame(frame);
            _pendingCameraPosition = position;
            _pendingCameraRotation = rotation;
            _pendingFocalLength = focalLength;
            _pendingPrincipalPoint = principalPoint;
            _pendingSensorResolution = sensorResolution;
            _pendingCurrentResolution = currentResolution;
            _depthCapture?.SetRGBGuide(
                _cameraFrameAvailable ? _cameraFrameCopy : null);
        }

        public bool Integrate(Camera camera)
        {
            if (_grid == null || _depthCapture == null || compute == null ||
                _generateSurfaceKernel < 0 || _prepareArgsKernel < 0 ||
                _surfaceKernel < 0 || _gatherCarveKernel < 0 || _carveKernel < 0 ||
                camera == null || !DepthCapture.DepthAvailable ||
                !_depthCapture.HasUnprocessedFrame)
                return false;

            MerkabaResidencyFrame residency = _grid.RefreshResidency(camera,
                maxUpdateDistance, true);
            if (residency.IntegrationChunkCount == 0) return false;
            MerkabaGpuTimestamps.TryBeginFrame(
                unchecked((uint)Math.Max(1, IntegrationCount + 1)));
            MerkabaGpuTimestamps.BeginCompute(
                MerkabaGpuStage.DepthPreprocess);
            bool consumedDepth = _depthCapture.ConsumeLatestDepthFrame();
            MerkabaGpuTimestamps.EndCompute(
                MerkabaGpuStage.DepthPreprocess);
            if (!consumedDepth ||
                _depthCapture.DepthTex == null || _depthCapture.NormTex == null ||
                _depthCapture.DilatedDepthTex == null)
            {
                MerkabaGpuTimestamps.EndFrame();
                return false;
            }

            compute.SetMatrixArray(DepthCapture.ViewID, _depthCapture.View);
            compute.SetMatrixArray(DepthCapture.ProjID, _depthCapture.Proj);
            compute.SetMatrixArray(DepthCapture.ViewInvID, _depthCapture.ViewInv);
            compute.SetMatrixArray(DepthCapture.ProjInvID, _depthCapture.ProjInv);
            compute.SetVector(DepthCapture.ZParamsID, _depthCapture.Planes);
            compute.SetVector(DepthCapture.TexSizeID,
                new Vector2(_depthCapture.DepthTex.width, _depthCapture.DepthTex.height));

            compute.SetInt(IntegrationCountId, residency.IntegrationChunkCount);
            compute.SetMatrix(GridToWorldId, _grid.GridToWorldMatrix);
            compute.SetMatrix(WorldToGridId, _grid.GridToWorldMatrix.inverse);
            compute.SetFloat(MaxDistanceId, maxUpdateDistance);
            compute.SetInt(PageHashCapacityId, _grid.PageHashEntryCount);
            compute.SetInt(WorkCapacityId, _grid.IntegrationWorkCapacity);

            int exclusionCount = Mathf.Min(ExclusionZones.Count, _exclusionPositions.Length);
            for (int i = 0; i < exclusionCount; i++)
                _exclusionPositions[i] = ExclusionZones[i] != null
                    ? ExclusionZones[i].position : Vector3.positiveInfinity;
            compute.SetInt(ExclusionCountId, exclusionCount);
            compute.SetVectorArray(ExclusionHeadsId, _exclusionPositions);

            MerkabaGpuTimestamps.BeginCompute(
                MerkabaGpuStage.SurfaceIntegration);
            _grid.BeginIntegrationWorkFrame();
            BindSurfaceGeneration();
            compute.Dispatch(_generateSurfaceKernel,
                Mathf.CeilToInt(_depthCapture.DepthTex.width / 8f),
                Mathf.CeilToInt(_depthCapture.DepthTex.height / 8f), 2);

            PrepareIndirect(_grid.SurfaceCountBuffer,
                _grid.SurfaceDispatchArgsBuffer);
            BindSurfaceIntegration();
            compute.DispatchIndirect(_surfaceKernel,
                _grid.SurfaceDispatchArgsBuffer);
            MerkabaGpuTimestamps.EndCompute(
                MerkabaGpuStage.SurfaceIntegration);

            MerkabaGpuTimestamps.BeginCompute(
                MerkabaGpuStage.CarveIntegration);
            BindCarveGather();
            compute.Dispatch(_gatherCarveKernel,
                residency.IntegrationChunkCount, 1, 1);
            PrepareIndirect(_grid.CarveCountBuffer,
                _grid.CarveDispatchArgsBuffer);
            BindCarveIntegration();
            compute.DispatchIndirect(_carveKernel, _grid.CarveDispatchArgsBuffer);
            MerkabaGpuTimestamps.EndCompute(
                MerkabaGpuStage.CarveIntegration);
            MerkabaGpuTimestamps.CaptureIntegrationMetrics(
                _grid.SurfaceCountBuffer, _grid.CarveCountBuffer,
                residency.IntegrationChunkCount, _depthCapture.DepthTex.width,
                _depthCapture.DepthTex.height);
            // The append queue itself remains GPU-owned. The renderer later issues a
            // zero-or-more-group indirect publication dispatch; no count readback is
            // introduced into the integration frame.
            _grid.NotifyGpuPublicationMayBeDirty();
            _grid.MarkIntegrationPagesGpuCurrent();
            IntegrationCount++;
            _cameraFrameAvailable = false;

            if (warmupIntegrations > 0 && IntegrationCount == warmupIntegrations)
            {
                // Preserve QRS startup-noise semantics without resetting the counter and
                // accidentally repeating warmup forever.
                _grid.Clear();
                Logger.Info($"Merkaba warmup complete ({warmupIntegrations}); discarded startup evidence");
            }
            Integrated?.Invoke();
            return true;
        }

        public void Clear()
        {
            _grid?.Clear();
            IntegrationCount = 0;
        }

        internal void RestoreIntegrationCount(int integrationCount)
        {
            IntegrationCount = Mathf.Max(0, integrationCount);
        }

        public Task SynchronizeCanonicalStateAsync() =>
            _grid != null ? _grid.SynchronizeResidentStateAsync() : Task.CompletedTask;

        private void BindSurfaceGeneration()
        {
            compute.SetBuffer(_generateSurfaceKernel, PageHashId, _grid.PageHashBuffer);
            compute.SetBuffer(_generateSurfaceKernel, IntegrationEnabledId,
                _grid.IntegrationEnabledBuffer);
            compute.SetBuffer(_generateSurfaceKernel, SurfaceBitsId,
                _grid.SurfaceCandidateBitsBuffer);
            compute.SetBuffer(_generateSurfaceKernel, SurfaceQueueId,
                _grid.SurfaceQueueBuffer);
            compute.SetBuffer(_generateSurfaceKernel, SurfaceCountId,
                _grid.SurfaceCountBuffer);
            compute.SetTexture(_generateSurfaceKernel, DepthCapture.DepthTexID,
                _depthCapture.DepthTex);
        }

        private void BindSurfaceIntegration()
        {
            BindObservation(_surfaceKernel);
            compute.SetBuffer(_surfaceKernel, SurfaceBitsId,
                _grid.SurfaceCandidateBitsBuffer);
            compute.SetBuffer(_surfaceKernel, SurfaceQueueReadId,
                _grid.SurfaceQueueBuffer);
            compute.SetBuffer(_surfaceKernel, SurfaceCountReadId,
                _grid.SurfaceCountBuffer);
            compute.SetBuffer(_surfaceKernel, CarveListedBitsId,
                _grid.CarveListedBitsBuffer);
            compute.SetBuffer(_surfaceKernel, CarveLocalIndicesId,
                _grid.CarveLocalIndicesBuffer);
            compute.SetBuffer(_surfaceKernel, CarveCountsId,
                _grid.CarveCountsBuffer);
            BindCamera(_surfaceKernel);
        }

        private void BindCarveGather()
        {
            compute.SetBuffer(_gatherCarveKernel, KernelsId, _grid.KernelBuffer);
            compute.SetBuffer(_gatherCarveKernel, IntegrationSlotsId,
                _grid.IntegrationSlotsBuffer);
            compute.SetBuffer(_gatherCarveKernel, CarveLocalIndicesId,
                _grid.CarveLocalIndicesBuffer);
            compute.SetBuffer(_gatherCarveKernel, CarveCountsId,
                _grid.CarveCountsBuffer);
            compute.SetBuffer(_gatherCarveKernel, CarveQueueId,
                _grid.CarveQueueBuffer);
            compute.SetBuffer(_gatherCarveKernel, CarveCountId,
                _grid.CarveCountBuffer);
        }

        private void BindCarveIntegration()
        {
            BindObservation(_carveKernel);
            compute.SetBuffer(_carveKernel, CarveQueueReadId,
                _grid.CarveQueueBuffer);
            compute.SetBuffer(_carveKernel, CarveCountReadId,
                _grid.CarveCountBuffer);
        }

        private void BindObservation(int kernel)
        {
            compute.SetBuffer(kernel, KernelsId, _grid.KernelBuffer);
            compute.SetBuffer(kernel, PageCoordsId, _grid.PageCoordsBuffer);
            compute.SetBuffer(kernel, PageNeighboursId, _grid.PageNeighboursBuffer);
            compute.SetBuffer(kernel, PublicationDirtyId,
                _grid.PublicationDirtyChunksBuffer);
            compute.SetBuffer(kernel, GpuPublicationDirtyQueueId,
                _grid.GpuPublicationDirtyQueueBuffer);
            compute.SetTexture(kernel, DepthCapture.DepthTexID,
                _depthCapture.DepthTex);
            compute.SetTexture(kernel, DepthCapture.NormTexID,
                _depthCapture.NormTex);
            compute.SetTexture(kernel, DepthCapture.DilatedDepthTexID,
                _depthCapture.DilatedDepthTex);
        }

        private void PrepareIndirect(ComputeBuffer count, ComputeBuffer args)
        {
            compute.SetBuffer(_prepareArgsKernel, WorkCountId, count);
            compute.SetBuffer(_prepareArgsKernel, IndirectArgsId, args);
            compute.Dispatch(_prepareArgsKernel, 1, 1, 1);
        }

        private void BindCamera(int kernel)
        {
            bool available = _cameraFrameAvailable && _cameraFrameCopy != null;
            compute.SetTexture(kernel, CameraRgbId,
                available ? _cameraFrameCopy : _dummyCameraTexture);
            compute.SetInt(CameraAvailableId, available ? 1 : 0);
            if (!available) return;

            compute.SetVector(CameraPositionId, _pendingCameraPosition);
            compute.SetMatrix(CameraInverseRotationId,
                Matrix4x4.Rotate(Quaternion.Inverse(_pendingCameraRotation)));
            compute.SetVector(CameraFocalLengthId, _pendingFocalLength);
            compute.SetVector(CameraPrincipalPointId, _pendingPrincipalPoint);
            compute.SetVector(CameraSensorResolutionId, _pendingSensorResolution);
            compute.SetVector(CameraCurrentResolutionId, _pendingCurrentResolution);
            compute.SetFloat(CameraExposureId, cameraExposure);
        }

        private void CopyCameraFrame(Texture frame)
        {
            int width = frame.width;
            int height = frame.height;
            if (_cameraFrameCopy == null || _cameraFrameCopy.width != width ||
                _cameraFrameCopy.height != height)
            {
                if (_cameraFrameCopy != null) Destroy(_cameraFrameCopy);
                _cameraFrameCopy = new RenderTexture(width, height, 0,
                    GraphicsFormat.R8G8B8A8_SRGB, 0)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                _cameraFrameCopy.Create();
            }
            Graphics.Blit(frame, _cameraFrameCopy);
        }

        public static MerkabaObservationResult IntegrateObservation(ref KernelState state,
            in MerkabaObservationInput input, Color32 color)
        {
            MerkabaObservationResult result = MerkabaObservation.Classify(input);
            IntegrateClassified(ref state, result.Kind, result.Quality, color);
            return result;
        }

        public static bool IntegrateClassified(ref KernelState state,
            MerkabaObservationKind kind, float quality, Color32 color) =>
            state.Apply(kind, quality, color);
    }
}
