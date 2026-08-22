using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.Prism
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct PhotometricPressureGpu
    {
        public uint FilmId;
        public uint Generation;
        public Vector2 Uv;
        public float NormalResidual;
        public float Precision;
        public float Confidence;
        public float BestCost;
        public float SecondCost;
        public float FootprintArea;
        public uint SourceMask;
        public uint ValidViews;

        internal const int Stride = 48;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TemporalRgbViewGpu
    {
        public Matrix4x4 RgbFromChunk;
        public Vector4 Intrinsics;
        public Vector4 CameraOriginChunk;
        public uint Generation;
        public uint SourceSequence;
        public uint Eye;
        public uint Active;

        internal const int Stride = 112;
    }

    /// <summary>
    /// Q3-14/Q3-15 shared photometric-pressure producer. It performs a posterior-
    /// centred 1D normal search in current L/R RGB and a robust temporal cone bundle.
    /// Accepted equations remain GPU-resident and are consumed by the canonical film
    /// information solve; no detached depth map, CPU pixel pass, or readback exists.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(12)]
    public sealed class PrismPhotometricRefiner : MonoBehaviour
    {
        private const int MatchClassDispatchOffset =
            (int)ConeEventClass.Match * sizeof(uint) * 3;
        private const int ViewsPerFilm = 8;

        [SerializeField] private PrismFilmSpawner filmSpawner;
        [SerializeField] private ComputeShader focusCompute;
        [SerializeField, Range(8, 24)] private int temporalStereoFrames = 16;
        [SerializeField, Min(0.005f)] private float maximumNormalSearch = 0.045f;
        [SerializeField, Min(0.00025f)] private float minimumRefineSigma = 0.001f;
        [SerializeField, Range(0f, 1f)] private float ambiguityMargin = 0.035f;
        [SerializeField, Range(0f, 1f)] private float minimumTextureEnergy = 0.025f;
        [SerializeField, Range(1, 8)] private int minimumKeyframeSpacing = 2;
        [SerializeField, Range(10, 120)] private int maximumKeyframeInterval = 36;
        [SerializeField, Range(0.01f, 1f)] private float minimumFilmInformationGain = 0.10f;
        [SerializeField, Range(0.01f, 1f)] private float minimumFrameInformationGain = 0.22f;
        [SerializeField, Range(0.01f, 1f)] private float aggregateFrameInformationGain = 0.08f;

        private static readonly int EventCapacityId = Shader.PropertyToID("_EventCapacity");
        private static readonly int FilmCapacityId = Shader.PropertyToID("_FilmCapacity");
        private static readonly int PressureCapacityId = Shader.PropertyToID("_PressureCapacity");
        private static readonly int TemporalViewCapacityId = Shader.PropertyToID("_TemporalViewCapacity");
        private static readonly int MaximumSearchId = Shader.PropertyToID("_MaximumNormalSearch");
        private static readonly int MinimumSigmaId = Shader.PropertyToID("_MinimumRefineSigma");
        private static readonly int AmbiguityMarginId = Shader.PropertyToID("_AmbiguityMargin");
        private static readonly int TextureEnergyId = Shader.PropertyToID("_MinimumTextureEnergy");
        private static readonly int CurrentSequenceId = Shader.PropertyToID("_CurrentSequence");
        private static readonly int RgbResolutionId = Shader.PropertyToID("_RgbResolution");
        private static readonly int DepthResolutionId = Shader.PropertyToID("_DepthResolution");
        private static readonly int RgbIntrinsicsId = Shader.PropertyToID("_RgbIntrinsics");
        private static readonly int DepthIntrinsicsId = Shader.PropertyToID("_DepthIntrinsics");
        private static readonly int ChunkFromDepthId = Shader.PropertyToID("_ChunkFromDepth");
        private static readonly int RgbFromChunkId = Shader.PropertyToID("_RgbFromChunk");
        private static readonly int DepthFromChunkId = Shader.PropertyToID("_DepthFromChunk");
        private static readonly int EventsId = Shader.PropertyToID("_ConeEvents");
        private static readonly int ClassifiedIndicesId = Shader.PropertyToID("_ClassifiedIndices");
        private static readonly int ClassCountersId = Shader.PropertyToID("_ClassCounters");
        private static readonly int FilmHeadersId = Shader.PropertyToID("_FilmHeaders");
        private static readonly int FilmAllocatorId = Shader.PropertyToID("_FilmAllocator");
        private static readonly int ActiveFilmsId =
            Shader.PropertyToID("_CanonicalActiveFilmIndices");
        private static readonly int RayLeftId = Shader.PropertyToID("_DepthRayCenterLeft");
        private static readonly int RayRightId = Shader.PropertyToID("_DepthRayCenterRight");
        private static readonly int ConsensusDepthId = Shader.PropertyToID("_ConsensusDepth");
        private static readonly int RgbLeftId = Shader.PropertyToID("_RgbLeft");
        private static readonly int RgbRightId = Shader.PropertyToID("_RgbRight");
        private static readonly int PressuresId = Shader.PropertyToID("_PhotometricPressures");
        private static readonly int PressureStateId = Shader.PropertyToID("_PhotometricState");
        private static readonly int PressureArgsId = Shader.PropertyToID("_PhotometricDispatchArguments");
        private static readonly int TemporalRgbId = Shader.PropertyToID("_TemporalRgb");
        private static readonly int TemporalViewsId = Shader.PropertyToID("_TemporalViews");
        private static readonly int FilmViewRefsId = Shader.PropertyToID("_FilmViewRefs");
        private static readonly int FilmViewScoresId = Shader.PropertyToID("_FilmViewScores");
        private static readonly int FilmViewOwnerGenerationId =
            Shader.PropertyToID("_FilmViewOwnerGeneration");
        private static readonly int ViewSelectArgsId = Shader.PropertyToID("_ViewSelectDispatchArguments");
        private static readonly int RefinementClaimsId =
            Shader.PropertyToID("_RefinementClaims");
        private static readonly int KeyframeStateId =
            Shader.PropertyToID("_KeyframeIngressState");
        private static readonly int KeyframeFilmGainId =
            Shader.PropertyToID("_KeyframeFilmGain");

        private readonly Matrix4x4[] _chunkFromDepth = new Matrix4x4[2];
        private readonly Matrix4x4[] _rgbFromChunk = new Matrix4x4[2];
        private readonly Matrix4x4[] _depthFromChunk = new Matrix4x4[2];
        private readonly Vector4[] _rgbIntrinsics = new Vector4[2];
        private readonly Vector4[] _depthIntrinsics = new Vector4[2];
        private readonly Vector4[] _cameraOriginChunk = new Vector4[2];
        private readonly GpuResourceRetirementQueue _gpuRetirement = new();

        private GraphicsBuffer _pressures;
        private GraphicsBuffer _pressureState;
        private GraphicsBuffer _pressureArguments;
        private GraphicsBuffer _temporalViews;
        private GraphicsBuffer _filmViewRefs;
        private GraphicsBuffer _filmViewScores;
        private GraphicsBuffer _filmViewOwnerGeneration;
        private GraphicsBuffer _viewSelectArguments;
        private GraphicsBuffer _refinementClaims;
        private RenderTexture _temporalRgb;
        private int _pressureCapacity;
        private int _filmCapacity;
        private int _temporalViewCapacity;
        private PrismInformationGainKeyframeIngress _keyframeIngress;
        private int _clearKernel = -1;
        private int _clearClaimsKernel = -1;
        private int _initializeKernel = -1;
        private int _stereoKernel = -1;
        private int _temporalKernel = -1;
        private int _buildPressureArgsKernel = -1;
        private int _selectViewsKernel = -1;
        private bool _running;
        private bool _historyResetRequested = true;
        private uint _historyCalibrationEpoch;
        private Matrix4x4 _worldFromChunk = Matrix4x4.identity;
        private long _stereoFrames;
        private long _temporalFrames;

        internal GraphicsBuffer Pressures => _pressures;
        internal GraphicsBuffer PressureState => _pressureState;
        internal GraphicsBuffer PressureArguments => _pressureArguments;
        internal int PressureCapacity => _pressureCapacity;
        public long StereoFrames => _stereoFrames;
        public long TemporalFrames => _temporalFrames;

        public void SetChunkFrame(Matrix4x4 worldFromChunk)
        {
            _worldFromChunk = worldFromChunk;
            // Temporal matrices and camera origins are expressed in chunk-local
            // coordinates. A residency/pose-graph frame change invalidates them even
            // when the RGB resolution and film arena capacities stay identical.
            _historyResetRequested = true;
        }

        internal void StartRefining(PrismFilmSpawner films = null)
        {
            if (_running) return;
            filmSpawner = films != null ? films : filmSpawner;
            filmSpawner ??= GetComponent<PrismFilmSpawner>();
            focusCompute ??= Resources.Load<ComputeShader>("Prism/PhotometricFocus");
            if (filmSpawner?.FilmPool == null || focusCompute == null)
            {
                Logger.Error("Cone-PRISM photometric refinement dependencies are missing.");
                return;
            }
            _initializeKernel = focusCompute.FindKernel("InitializePhotometricState");
            _clearKernel = focusCompute.FindKernel("ClearPhotometricFrame");
            _clearClaimsKernel = focusCompute.FindKernel("ClearRefinementClaims");
            _stereoKernel = focusCompute.FindKernel("NarrowStereoPressure");
            _temporalKernel = focusCompute.FindKernel("TemporalFocusPressure");
            _buildPressureArgsKernel = focusCompute.FindKernel("BuildPhotometricArguments");
            _selectViewsKernel = focusCompute.FindKernel("SelectTemporalViews");
            _keyframeIngress ??= new PrismInformationGainKeyframeIngress(
                _gpuRetirement);
            _running = true;
        }

        internal void StopRefining()
        {
            _running = false;
            DisposeBuffers();
            _historyCalibrationEpoch = 0u;
            _historyResetRequested = true;
        }

        private void LateUpdate() => _gpuRetirement.DrainCompleted();

        private void OnDestroy()
        {
            StopRefining();
            _gpuRetirement.DrainAndWait();
        }

        internal bool DispatchPhotometricPressure(ConeEventFrameLease eventFrame)
        {
            ContactFilmPool pool = filmSpawner?.FilmPool;
            if (!_running || eventFrame == null || eventFrame.IsDisposed ||
                pool == null || pool.IsDisposed) return false;
            try
            {
                NormalizedRigFrameLease normalized = eventFrame.Source.Source;
                StereoRigFrameLease rig = normalized.Source;
                if (_historyCalibrationEpoch != rig.CalibrationEpoch)
                {
                    _historyCalibrationEpoch = rig.CalibrationEpoch;
                    _historyResetRequested = true;
                }
                EnsureBuffers(eventFrame.EventCapacity, pool.Capacity,
                    rig.RgbLeft.Resolution);
                if (_historyResetRequested) ResetHistoryState();
                ConfigureFrame(eventFrame, pool, normalized, rig);

                focusCompute.Dispatch(_clearKernel, 1, 1, 1);
                focusCompute.Dispatch(_clearClaimsKernel,
                    CeilDiv(_filmCapacity * 4, 64), 1, 1);
                focusCompute.DispatchIndirect(_stereoKernel,
                    eventFrame.ClassDispatchArguments, MatchClassDispatchOffset);
                _stereoFrames++;
                focusCompute.DispatchIndirect(_temporalKernel,
                    eventFrame.ClassDispatchArguments, MatchClassDispatchOffset);
                _temporalFrames++;
                focusCompute.Dispatch(_buildPressureArgsKernel, 1, 1, 1);

                _keyframeIngress.Dispatch(eventFrame, pool, rig, normalized,
                    _rgbFromChunk, _chunkFromDepth, _rgbIntrinsics,
                    _cameraOriginChunk, _temporalViews, _temporalRgb,
                    _viewSelectArguments, minimumRefineSigma,
                    maximumKeyframeInterval, minimumKeyframeSpacing,
                    minimumFilmInformationGain, minimumFrameInformationGain,
                    aggregateFrameInformationGain);
                focusCompute.DispatchIndirect(_selectViewsKernel,
                    _viewSelectArguments, 0);
                return true;
            }
            catch (Exception exception)
            {
                Logger.Error($"Cone-PRISM photometric pressure failed: {exception.Message}");
                return false;
            }
        }

        private void EnsureBuffers(int eventCapacity, int filmCapacity,
            Vector2Int rgbResolution)
        {
            int stereoFrames = Mathf.Clamp(temporalStereoFrames, 8, 24);
            int viewCapacity = stereoFrames * 2;
            bool buffersCompatible = _pressures != null &&
                _refinementClaims != null &&
                _keyframeIngress?.IsReady == true &&
                _pressureCapacity == eventCapacity && _filmCapacity == filmCapacity &&
                _temporalViewCapacity == viewCapacity;
            bool textureCompatible = _temporalRgb != null &&
                _temporalRgb.width == rgbResolution.x &&
                _temporalRgb.height == rgbResolution.y &&
                _temporalRgb.volumeDepth == viewCapacity;
            if (buffersCompatible && textureCompatible) return;

            DisposeBuffers();
            _pressureCapacity = Math.Max(1, eventCapacity);
            _filmCapacity = Math.Max(1, filmCapacity);
            _temporalViewCapacity = viewCapacity;
            _pressures = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                _pressureCapacity, PhotometricPressureGpu.Stride);
            _pressureState = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                4, sizeof(uint));
            _pressureArguments = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments,
                1, sizeof(uint) * 3);
            _temporalViews = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                _temporalViewCapacity, TemporalRgbViewGpu.Stride);
            _filmViewRefs = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                checked(_filmCapacity * ViewsPerFilm), sizeof(uint));
            _filmViewScores = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                checked(_filmCapacity * ViewsPerFilm), sizeof(float));
            _filmViewOwnerGeneration = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, _filmCapacity, sizeof(uint));
            _viewSelectArguments = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments,
                1, sizeof(uint) * 3);
            // Two 64-cell bitsets (stereo + temporal) per film. They prevent
            // thousands of image pixels on the same surface region from repeating
            // an identical 1D normal search in one tick.
            _refinementClaims = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                checked(_filmCapacity * 4), sizeof(uint));

            var descriptor = new RenderTextureDescriptor(rgbResolution.x,
                rgbResolution.y)
            {
                graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm,
                depthBufferBits = 0,
                msaaSamples = 1,
                dimension = TextureDimension.Tex2DArray,
                volumeDepth = _temporalViewCapacity,
                useMipMap = false,
                autoGenerateMips = false,
                enableRandomWrite = true
            };
            _temporalRgb = new RenderTexture(descriptor)
            {
                name = "[Cone-PRISM] Temporal RGB cone fields",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            if (!_temporalRgb.Create())
                throw new InvalidOperationException("Unable to allocate temporal RGB cone fields.");
            _keyframeIngress ??= new PrismInformationGainKeyframeIngress(
                _gpuRetirement);
            if (!_keyframeIngress.Ensure(_filmCapacity, _temporalViewCapacity))
                throw new InvalidOperationException(
                    "Information-gain keyframe shader is unavailable.");

            BindPersistent(pool: filmSpawner.FilmPool);
            ResetHistoryState();
        }

        private void ResetHistoryState()
        {
            if (_pressures == null || _filmViewRefs == null ||
                _temporalViews == null) return;
            focusCompute.SetInt(FilmCapacityId, _filmCapacity);
            focusCompute.SetInt(PressureCapacityId, _pressureCapacity);
            focusCompute.SetInt(TemporalViewCapacityId, _temporalViewCapacity);
            focusCompute.Dispatch(_initializeKernel,
                CeilDiv(Math.Max(_filmCapacity * ViewsPerFilm,
                    _temporalViewCapacity), 64), 1, 1);
            _keyframeIngress.Reset(filmSpawner.FilmPool, _temporalViews,
                _viewSelectArguments);
            _historyResetRequested = false;
        }

        private void ConfigureFrame(ConeEventFrameLease eventFrame,
            ContactFilmPool pool, NormalizedRigFrameLease normalized,
            StereoRigFrameLease rig)
        {
            Matrix4x4 chunkFromWorld = _worldFromChunk.inverse;
            GpuImageView[] rgb = { rig.RgbLeft, rig.RgbRight };
            GpuImageView[] depth = { rig.DepthLeft, rig.DepthRight };
            for (int eye = 0; eye < 2; eye++)
            {
                Matrix4x4 worldFromRgb = PoseMatrix(rgb[eye].WorldFromCamera);
                Matrix4x4 worldFromDepth = PoseMatrix(depth[eye].WorldFromCamera);
                _chunkFromDepth[eye] = chunkFromWorld * worldFromDepth;
                _rgbFromChunk[eye] = worldFromRgb.inverse * _worldFromChunk;
                _depthFromChunk[eye] = worldFromDepth.inverse * _worldFromChunk;
                _rgbIntrinsics[eye] = Intrinsics(rgb[eye].Intrinsics);
                _depthIntrinsics[eye] = Intrinsics(depth[eye].Intrinsics);
                Vector3 cameraOrigin = chunkFromWorld.MultiplyPoint3x4(
                    rgb[eye].WorldFromCamera.position);
                _cameraOriginChunk[eye] = new Vector4(cameraOrigin.x,
                    cameraOrigin.y, cameraOrigin.z, 1f);
            }

            BindPersistent(pool);
            focusCompute.SetInt(EventCapacityId, eventFrame.EventCapacity);
            focusCompute.SetInt(FilmCapacityId, pool.Capacity);
            focusCompute.SetInt(PressureCapacityId, _pressureCapacity);
            focusCompute.SetInt(TemporalViewCapacityId, _temporalViewCapacity);
            focusCompute.SetInt(CurrentSequenceId, unchecked((int)rig.Sequence));
            focusCompute.SetFloat(MaximumSearchId, maximumNormalSearch);
            focusCompute.SetFloat(MinimumSigmaId, minimumRefineSigma);
            focusCompute.SetFloat(AmbiguityMarginId, ambiguityMargin);
            focusCompute.SetFloat(TextureEnergyId, minimumTextureEnergy);
            focusCompute.SetInts(RgbResolutionId, rig.RgbLeft.Resolution.x,
                rig.RgbLeft.Resolution.y);
            focusCompute.SetInts(DepthResolutionId, rig.DepthResolution.x,
                rig.DepthResolution.y);
            focusCompute.SetVectorArray(RgbIntrinsicsId, _rgbIntrinsics);
            focusCompute.SetVectorArray(DepthIntrinsicsId, _depthIntrinsics);
            focusCompute.SetMatrixArray(ChunkFromDepthId, _chunkFromDepth);
            focusCompute.SetMatrixArray(RgbFromChunkId, _rgbFromChunk);
            focusCompute.SetMatrixArray(DepthFromChunkId, _depthFromChunk);

            int[] frameKernels = { _stereoKernel, _temporalKernel };
            foreach (int kernel in frameKernels)
            {
                focusCompute.SetBuffer(kernel, EventsId, eventFrame.Events);
                focusCompute.SetBuffer(kernel, ClassifiedIndicesId,
                    eventFrame.ClassifiedIndices);
                focusCompute.SetBuffer(kernel, ClassCountersId,
                    eventFrame.ClassCounters);
                focusCompute.SetTexture(kernel, RayLeftId,
                    normalized.ConeLuts.DepthLeft.CenterRaySolidAngle);
                focusCompute.SetTexture(kernel, RayRightId,
                    normalized.ConeLuts.DepthRight.CenterRaySolidAngle);
                focusCompute.SetTexture(kernel, RgbLeftId, rig.RgbLeft.Texture);
                focusCompute.SetTexture(kernel, RgbRightId, rig.RgbRight.Texture);
                focusCompute.SetTexture(kernel, ConsensusDepthId,
                    normalized.ConsensusDepth);
            }
        }

        private void BindPersistent(ContactFilmPool pool)
        {
            int[] allKernels =
            {
                _initializeKernel, _clearKernel, _stereoKernel, _temporalKernel,
                _clearClaimsKernel, _buildPressureArgsKernel,
                _selectViewsKernel
            };
            foreach (int kernel in allKernels)
            {
                if (kernel < 0) continue;
                focusCompute.SetBuffer(kernel, PressuresId, _pressures);
                focusCompute.SetBuffer(kernel, PressureStateId, _pressureState);
                focusCompute.SetBuffer(kernel, RefinementClaimsId,
                    _refinementClaims);
                focusCompute.SetBuffer(kernel, PressureArgsId, _pressureArguments);
                focusCompute.SetBuffer(kernel, TemporalViewsId, _temporalViews);
                focusCompute.SetBuffer(kernel, FilmViewRefsId, _filmViewRefs);
                focusCompute.SetBuffer(kernel, FilmViewScoresId, _filmViewScores);
                focusCompute.SetBuffer(kernel, FilmViewOwnerGenerationId,
                    _filmViewOwnerGeneration);
                focusCompute.SetBuffer(kernel, ViewSelectArgsId,
                    _viewSelectArguments);
            }
            focusCompute.SetBuffer(_stereoKernel, FilmHeadersId, pool.Headers);
            focusCompute.SetBuffer(_temporalKernel, FilmHeadersId, pool.Headers);
            focusCompute.SetBuffer(_temporalKernel, KeyframeStateId,
                _keyframeIngress.State);
            focusCompute.SetBuffer(_selectViewsKernel, FilmAllocatorId,
                pool.Allocator);
            focusCompute.SetBuffer(_selectViewsKernel, FilmHeadersId, pool.Headers);
            focusCompute.SetBuffer(_selectViewsKernel, ActiveFilmsId,
                pool.ActiveIndices);
            focusCompute.SetBuffer(_selectViewsKernel, KeyframeStateId,
                _keyframeIngress.State);
            focusCompute.SetBuffer(_selectViewsKernel, KeyframeFilmGainId,
                _keyframeIngress.FilmGain);
            if (_temporalRgb != null)
            {
                focusCompute.SetTexture(_temporalKernel, TemporalRgbId, _temporalRgb);
                focusCompute.SetTexture(_selectViewsKernel, TemporalRgbId, _temporalRgb);
            }
        }

        private void DisposeBuffers()
        {
            _gpuRetirement.RetireAfterCurrentGpuWork(_pressures);
            _gpuRetirement.RetireAfterCurrentGpuWork(_pressureState);
            _gpuRetirement.RetireAfterCurrentGpuWork(_pressureArguments);
            _gpuRetirement.RetireAfterCurrentGpuWork(_temporalViews);
            _gpuRetirement.RetireAfterCurrentGpuWork(_filmViewRefs);
            _gpuRetirement.RetireAfterCurrentGpuWork(_filmViewScores);
            _gpuRetirement.RetireAfterCurrentGpuWork(_filmViewOwnerGeneration);
            _gpuRetirement.RetireAfterCurrentGpuWork(_viewSelectArguments);
            _gpuRetirement.RetireAfterCurrentGpuWork(_refinementClaims);
            _keyframeIngress?.Dispose();
            _pressures = null;
            _pressureState = null;
            _pressureArguments = null;
            _temporalViews = null;
            _filmViewRefs = null;
            _filmViewScores = null;
            _filmViewOwnerGeneration = null;
            _viewSelectArguments = null;
            _refinementClaims = null;
            if (_temporalRgb != null)
            {
                _gpuRetirement.RetireAfterCurrentGpuWork(_temporalRgb);
                _temporalRgb = null;
            }
            _pressureCapacity = 0;
            _filmCapacity = 0;
            _temporalViewCapacity = 0;
        }

        private static Vector4 Intrinsics(RigIntrinsics value) => new(
            value.FocalLength.x, value.FocalLength.y,
            value.PrincipalPoint.x, value.PrincipalPoint.y);

        private static Matrix4x4 PoseMatrix(Pose pose) =>
            Matrix4x4.TRS(pose.position, pose.rotation, Vector3.one);

        private static int CeilDiv(int value, int divisor) =>
            Math.Max(1, (value + divisor - 1) / divisor);
    }
}
