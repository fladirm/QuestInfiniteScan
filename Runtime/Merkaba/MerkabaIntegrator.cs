using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Genesis.RoomScan
{
    /// <summary>
    /// Applies QRS depth/normal/dilation/projection observations to reversible evidence.
    /// The Quest path is one coarse GPU pass over current frustum chunks; the static
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
        private int _kernel = -1;
        private Texture _pendingCameraFrame;
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
        public event Action Integrated;

        private static readonly int KernelsId = Shader.PropertyToID("_MerkabaKernels");
        private static readonly int PageCoordsId = Shader.PropertyToID("_MerkabaPageCoords");
        private static readonly int PageNeighboursId = Shader.PropertyToID("_MerkabaPageNeighbours");
        private static readonly int IntegrationSlotsId = Shader.PropertyToID("_MerkabaIntegrationSlots");
        private static readonly int KernelDirtyId = Shader.PropertyToID("_MerkabaKernelDirty");
        private static readonly int IntegrationCountId = Shader.PropertyToID("_MerkabaIntegrationChunkCount");
        private static readonly int GridToWorldId = Shader.PropertyToID("_MerkabaGridToWorld");
        private static readonly int MaxDistanceId = Shader.PropertyToID("_MerkabaMaxUpdateDistance");
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
            if (compute != null) _kernel = compute.FindKernel("IntegrateMerkaba");
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
            _pendingCameraFrame = frame;
            _pendingCameraPosition = position;
            _pendingCameraRotation = rotation;
            _pendingFocalLength = focalLength;
            _pendingPrincipalPoint = principalPoint;
            _pendingSensorResolution = sensorResolution;
            _pendingCurrentResolution = currentResolution;
        }

        public bool Integrate(Camera camera)
        {
            if (_grid == null || _depthCapture == null || compute == null || _kernel < 0 ||
                camera == null || !DepthCapture.DepthAvailable ||
                !_depthCapture.HasUnprocessedFrame)
                return false;

            MerkabaResidencyFrame residency = _grid.RefreshResidency(camera,
                maxUpdateDistance, true);
            if (residency.IntegrationChunkCount == 0) return false;
            if (!_depthCapture.ConsumeLatestDepthFrame() ||
                _depthCapture.DepthTex == null || _depthCapture.NormTex == null ||
                _depthCapture.DilatedDepthTex == null)
                return false;

            compute.SetMatrixArray(DepthCapture.ViewID, _depthCapture.View);
            compute.SetMatrixArray(DepthCapture.ProjID, _depthCapture.Proj);
            compute.SetMatrixArray(DepthCapture.ViewInvID, _depthCapture.ViewInv);
            compute.SetMatrixArray(DepthCapture.ProjInvID, _depthCapture.ProjInv);
            compute.SetVector(DepthCapture.ZParamsID, _depthCapture.Planes);
            compute.SetVector(DepthCapture.TexSizeID,
                new Vector2(_depthCapture.DepthTex.width, _depthCapture.DepthTex.height));

            compute.SetBuffer(_kernel, KernelsId, _grid.KernelBuffer);
            compute.SetBuffer(_kernel, PageCoordsId, _grid.PageCoordsBuffer);
            compute.SetBuffer(_kernel, PageNeighboursId, _grid.PageNeighboursBuffer);
            compute.SetBuffer(_kernel, IntegrationSlotsId, _grid.IntegrationSlotsBuffer);
            compute.SetBuffer(_kernel, KernelDirtyId, _grid.KernelDirtyBuffer);
            compute.SetTexture(_kernel, DepthCapture.DepthTexID, _depthCapture.DepthTex);
            compute.SetTexture(_kernel, DepthCapture.NormTexID, _depthCapture.NormTex);
            compute.SetTexture(_kernel, DepthCapture.DilatedDepthTexID,
                _depthCapture.DilatedDepthTex);
            compute.SetInt(IntegrationCountId, residency.IntegrationChunkCount);
            compute.SetMatrix(GridToWorldId, _grid.GridToWorldMatrix);
            compute.SetFloat(MaxDistanceId, maxUpdateDistance);

            int exclusionCount = Mathf.Min(ExclusionZones.Count, _exclusionPositions.Length);
            for (int i = 0; i < exclusionCount; i++)
                _exclusionPositions[i] = ExclusionZones[i] != null
                    ? ExclusionZones[i].position : Vector3.positiveInfinity;
            compute.SetInt(ExclusionCountId, exclusionCount);
            compute.SetVectorArray(ExclusionHeadsId, _exclusionPositions);

            BindCamera();
            int total = residency.IntegrationChunkCount * MerkabaConstants.KernelsPerChunk;
            compute.Dispatch(_kernel, Mathf.CeilToInt(total / 64f), 1, 1);
            _grid.MarkIntegrationPagesGpuCurrent();
            IntegrationCount++;
            _pendingCameraFrame = null;

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

        private void BindCamera()
        {
            EnsureCameraCopy();
            bool available = _pendingCameraFrame != null && _cameraFrameCopy != null;
            compute.SetTexture(_kernel, CameraRgbId,
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

        private void EnsureCameraCopy()
        {
            if (_pendingCameraFrame == null) return;
            int width = _pendingCameraFrame.width;
            int height = _pendingCameraFrame.height;
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
            Graphics.Blit(_pendingCameraFrame, _cameraFrameCopy);
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
