using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.Prism
{
    /// <summary>
    /// Builds persistent uncertainty-bearing ContactBoundary curves from calibrated
    /// depth, normal, RGB-gradient, visibility, and coverage evidence. All event
    /// association, narrow-band RGB focusing, view-diversity accumulation, curve
    /// solving, and conservative retirement remain GPU/indirect.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(18)]
    public sealed class PrismBoundaryGraph : MonoBehaviour
    {
        private const int AccumulatorWords = 16;
        private const int MatchDispatchOffset =
            (int)ConeEventClass.Match * sizeof(uint) * 3;
        private const int BoundaryDispatchOffset =
            (int)ConeEventClass.Boundary * sizeof(uint) * 3;

        [SerializeField] private PrismFilmUpdater filmUpdater;
        [SerializeField] private PrismFilmSpawner filmSpawner;
        [SerializeField] private ComputeShader boundaryCompute;
        [SerializeField, Min(1024)] private int boundaryCapacity = 131072;
        [SerializeField, Min(2048)] private int hashCapacity = 262144;
        [SerializeField, Range(4, 32)] private int cellsPerAxis = 16;
        [SerializeField, Range(2, 32)] private int persistentViewSupport = 8;
        [SerializeField, Range(2, 8)] private int minimumDistinctViewBins = 2;
        [SerializeField, Range(1, 8)] private int rgbSearchRadiusPixels = 4;
        [SerializeField, Min(2f)] private float retirementEvidence = 16f;

        private static readonly int BoundaryCapacityId = Shader.PropertyToID("_BoundaryCapacity");
        private static readonly int FilmCapacityId = Shader.PropertyToID("_FilmCapacity");
        private static readonly int HashMaskId = Shader.PropertyToID("_HashMask");
        private static readonly int EventCapacityId = Shader.PropertyToID("_EventCapacity");
        private static readonly int CellsPerAxisId = Shader.PropertyToID("_CellsPerAxis");
        private static readonly int PersistentSupportId = Shader.PropertyToID("_PersistentSupport");
        private static readonly int MinimumViewBinsId = Shader.PropertyToID("_MinimumViewBins");
        private static readonly int SearchRadiusId = Shader.PropertyToID("_SearchRadiusPixels");
        private static readonly int RetirementEvidenceId = Shader.PropertyToID("_RetirementEvidence");
        private static readonly int FrameSequenceId = Shader.PropertyToID("_FrameSequence");
        private static readonly int DepthResolutionId = Shader.PropertyToID("_DepthResolution");
        private static readonly int RgbResolutionId = Shader.PropertyToID("_RgbResolution");
        private static readonly int RgbIntrinsicsId = Shader.PropertyToID("_RgbIntrinsics");
        private static readonly int ChunkFromDepthId = Shader.PropertyToID("_ChunkFromDepth");
        private static readonly int ChunkFromRgbId = Shader.PropertyToID("_ChunkFromRgb");
        private static readonly int RgbFromDepthId = Shader.PropertyToID("_RgbFromDepth");
        private static readonly int EventsId = Shader.PropertyToID("_ConeEvents");
        private static readonly int FilmHeadersId = Shader.PropertyToID("_FilmHeaders");
        private static readonly int ClassifiedIndicesId = Shader.PropertyToID("_ClassifiedIndices");
        private static readonly int ClassCountersId = Shader.PropertyToID("_ClassCounters");
        private static readonly int MeasuredConsensusId = Shader.PropertyToID("_MeasuredConsensus");
        private static readonly int BoundaryEvidenceTextureId = Shader.PropertyToID("_BoundaryEvidenceTexture");
        private static readonly int PredictedFilmIdsId = Shader.PropertyToID("_PredictedFilmIdGeneration");
        private static readonly int RgbLeftId = Shader.PropertyToID("_RgbLeft");
        private static readonly int RgbRightId = Shader.PropertyToID("_RgbRight");
        private static readonly int RayLeftId = Shader.PropertyToID("_DepthRayCenterLeft");
        private static readonly int RayRightId = Shader.PropertyToID("_DepthRayCenterRight");
        private static readonly int BoundaryHeadersId = Shader.PropertyToID("_BoundaryHeaders");
        private static readonly int BoundaryInformationId = Shader.PropertyToID("_BoundaryInformation");
        private static readonly int BoundaryHashId = Shader.PropertyToID("_BoundaryHash");
        private static readonly int BoundaryAllocatorId = Shader.PropertyToID("_BoundaryAllocator");
        private static readonly int FrameAccumulatorId = Shader.PropertyToID("_BoundaryFrameAccumulator");
        private static readonly int DirtyFlagsId = Shader.PropertyToID("_BoundaryDirtyFlags");
        private static readonly int DirtyIndicesId = Shader.PropertyToID("_DirtyBoundaryIndices");
        private static readonly int DirtyStateId = Shader.PropertyToID("_BoundaryDirtyState");
        private static readonly int SolveArgumentsId = Shader.PropertyToID("_BoundarySolveArguments");

        private readonly Matrix4x4[] _chunkFromDepth = new Matrix4x4[2];
        private readonly Matrix4x4[] _chunkFromRgb = new Matrix4x4[2];
        private readonly Matrix4x4[] _rgbFromDepth = new Matrix4x4[2];
        private readonly Vector4[] _rgbIntrinsics = new Vector4[2];
        private Matrix4x4 _chunkFromWorld = Matrix4x4.identity;
        private ContactBoundaryPool _pool;
        private GraphicsBuffer _frameAccumulator;
        private GraphicsBuffer _dirtyFlags;
        private GraphicsBuffer _dirtyIndices;
        private GraphicsBuffer _dirtyState;
        private GraphicsBuffer _solveArguments;
        private int _initializeKernel = -1;
        private int _clearLoadedHashKernel = -1;
        private int _rehashLoadedKernel = -1;
        private int _clearKernel = -1;
        private int _accumulateKernel = -1;
        private int _absenceKernel = -1;
        private int _buildArgsKernel = -1;
        private int _solveKernel = -1;
        private bool _running;
        private bool _subscribedToSource;
        private bool _initialized;
        private long _processedFrames;

        public event Action<ConeEventFrameLease> BoundariesUpdated;
        public ContactBoundaryPool BoundaryPool => _pool;
        public int CellsPerAxis => cellsPerAxis;
        public long ProcessedFrames => _processedFrames;

        public void SetChunkFrame(Matrix4x4 worldFromChunk) =>
            _chunkFromWorld = worldFromChunk.inverse;

        public void StartTracking(PrismFilmUpdater updater = null,
            PrismFilmSpawner films = null, bool subscribeToSource = true)
        {
            if (_running) return;
            filmUpdater = updater != null ? updater : filmUpdater;
            filmSpawner = films != null ? films : filmSpawner;
            filmUpdater ??= GetComponent<PrismFilmUpdater>();
            filmSpawner ??= GetComponent<PrismFilmSpawner>();
            boundaryCompute ??= Resources.Load<ComputeShader>("Prism/ContactBoundaryUpdate");
            if (filmUpdater == null || filmSpawner?.FilmPool == null ||
                boundaryCompute == null)
            {
                Logger.Error("Cone-PRISM boundary graph dependencies are missing.");
                return;
            }

            _pool ??= new ContactBoundaryPool(boundaryCapacity, hashCapacity);
            AllocateFrameBuffers(_pool.Capacity);
            _initializeKernel = boundaryCompute.FindKernel("InitializeBoundaryState");
            _clearLoadedHashKernel =
                boundaryCompute.FindKernel("ClearLoadedBoundaryHash");
            _rehashLoadedKernel =
                boundaryCompute.FindKernel("RehashLoadedBoundaries");
            _clearKernel = boundaryCompute.FindKernel("ClearBoundaryFrame");
            _accumulateKernel = boundaryCompute.FindKernel("AccumulateBoundaryEvents");
            _absenceKernel = boundaryCompute.FindKernel("AccumulateBoundaryAbsence");
            _buildArgsKernel = boundaryCompute.FindKernel("BuildBoundarySolveArguments");
            _solveKernel = boundaryCompute.FindKernel("SolveDirtyBoundaries");
            BindPersistent();
            if (!_initialized)
            {
                boundaryCompute.Dispatch(_initializeKernel,
                    CeilDiv(Math.Max(_pool.Capacity, _pool.HashCapacity), 64), 1, 1);
                _initialized = true;
            }
            if (subscribeToSource)
            {
                filmUpdater.UpdateCompleted += OnFilmUpdateCompleted;
                _subscribedToSource = true;
            }
            _running = true;
        }

        /// <summary>Rebuilds only the derived lookup after canonical bulk restore.</summary>
        public void RebuildCanonicalIndex()
        {
            if (_pool == null || _pool.IsDisposed) return;
            boundaryCompute ??=
                Resources.Load<ComputeShader>("Prism/ContactBoundaryUpdate");
            if (boundaryCompute == null) return;
            if (_clearLoadedHashKernel < 0)
                _clearLoadedHashKernel =
                    boundaryCompute.FindKernel("ClearLoadedBoundaryHash");
            if (_rehashLoadedKernel < 0)
                _rehashLoadedKernel =
                    boundaryCompute.FindKernel("RehashLoadedBoundaries");
            if (_initializeKernel < 0)
                _initializeKernel = boundaryCompute.FindKernel(
                    "InitializeBoundaryState");
            AllocateFrameBuffers(_pool.Capacity);
            BindPersistent();
            boundaryCompute.Dispatch(_clearLoadedHashKernel,
                CeilDiv(_pool.HashCapacity, 64), 1, 1);
            boundaryCompute.Dispatch(_rehashLoadedKernel,
                CeilDiv(_pool.Capacity, 64), 1, 1);
            _initialized = true;
        }

        public void StopTracking()
        {
            if (_subscribedToSource && filmUpdater != null)
                filmUpdater.UpdateCompleted -= OnFilmUpdateCompleted;
            _subscribedToSource = false;
            _running = false;
        }

        private void OnDestroy()
        {
            StopTracking();
            DisposeFrameBuffers();
            _pool?.Dispose();
            _pool = null;
            _initialized = false;
        }

        private void OnFilmUpdateCompleted(ConeEventFrameLease frame) =>
            DispatchBoundaries(frame);

        internal bool DispatchBoundaries(ConeEventFrameLease frame)
        {
            if (!_running || frame == null || frame.IsDisposed || _pool == null)
                return false;
            try
            {
                NormalizedRigFrameLease measured = frame.Source.Source;
                StereoRigFrameLease rig = measured.Source;
                ConeLutLease luts = measured.ConeLuts;
                ConfigureFrame(rig);
                BindPersistent();
                BindFrame(frame, measured, luts, rig);

                boundaryCompute.Dispatch(_clearKernel, 1, 1, 1);
                boundaryCompute.DispatchIndirect(_accumulateKernel,
                    frame.ClassDispatchArguments, BoundaryDispatchOffset);
                boundaryCompute.DispatchIndirect(_absenceKernel,
                    frame.ClassDispatchArguments, MatchDispatchOffset);
                boundaryCompute.Dispatch(_buildArgsKernel, 1, 1, 1);
                boundaryCompute.DispatchIndirect(_solveKernel, _solveArguments, 0);
                _processedFrames++;
                if (BoundariesUpdated != null) BoundariesUpdated.Invoke(frame);
                else filmSpawner.NotifyFilmsMutated();
                return true;
            }
            catch (Exception exception)
            {
                Logger.Error($"Cone-PRISM boundary update failed: {exception.Message}");
                filmSpawner.NotifyFilmsMutated();
                return false;
            }
        }

        private void ConfigureFrame(StereoRigFrameLease rig)
        {
            Matrix4x4 worldFromDepthLeft = PoseMatrix(rig.DepthLeft.WorldFromCamera);
            Matrix4x4 worldFromDepthRight = PoseMatrix(rig.DepthRight.WorldFromCamera);
            Matrix4x4 worldFromRgbLeft = PoseMatrix(rig.RgbLeft.WorldFromCamera);
            Matrix4x4 worldFromRgbRight = PoseMatrix(rig.RgbRight.WorldFromCamera);
            _chunkFromDepth[0] = _chunkFromWorld * worldFromDepthLeft;
            _chunkFromDepth[1] = _chunkFromWorld * worldFromDepthRight;
            _chunkFromRgb[0] = _chunkFromWorld * worldFromRgbLeft;
            _chunkFromRgb[1] = _chunkFromWorld * worldFromRgbRight;
            _rgbFromDepth[0] = worldFromRgbLeft.inverse * worldFromDepthLeft;
            _rgbFromDepth[1] = worldFromRgbRight.inverse * worldFromDepthRight;
            _rgbIntrinsics[0] = Intrinsics(rig.RgbLeft.Intrinsics);
            _rgbIntrinsics[1] = Intrinsics(rig.RgbRight.Intrinsics);
        }

        private void BindFrame(ConeEventFrameLease frame,
            NormalizedRigFrameLease measured, ConeLutLease luts,
            StereoRigFrameLease rig)
        {
            Vector2Int depthResolution = rig.DepthLeft.Resolution;
            Vector2Int rgbResolution = rig.RgbLeft.Resolution;
            boundaryCompute.SetInt(EventCapacityId, frame.EventCapacity);
            boundaryCompute.SetInt(CellsPerAxisId, cellsPerAxis);
            boundaryCompute.SetInt(PersistentSupportId, persistentViewSupport);
            boundaryCompute.SetInt(MinimumViewBinsId, minimumDistinctViewBins);
            boundaryCompute.SetInt(SearchRadiusId, rgbSearchRadiusPixels);
            boundaryCompute.SetFloat(RetirementEvidenceId, retirementEvidence);
            boundaryCompute.SetInt(FrameSequenceId,
                unchecked((int)(uint)rig.Sequence));
            boundaryCompute.SetInts(DepthResolutionId,
                depthResolution.x, depthResolution.y);
            boundaryCompute.SetInts(RgbResolutionId,
                rgbResolution.x, rgbResolution.y);
            boundaryCompute.SetVectorArray(RgbIntrinsicsId, _rgbIntrinsics);
            boundaryCompute.SetMatrixArray(ChunkFromDepthId, _chunkFromDepth);
            boundaryCompute.SetMatrixArray(ChunkFromRgbId, _chunkFromRgb);
            boundaryCompute.SetMatrixArray(RgbFromDepthId, _rgbFromDepth);

            int[] eventKernels = { _accumulateKernel, _absenceKernel };
            foreach (int kernel in eventKernels)
            {
                boundaryCompute.SetBuffer(kernel, EventsId, frame.Events);
                boundaryCompute.SetBuffer(kernel, ClassifiedIndicesId,
                    frame.ClassifiedIndices);
                boundaryCompute.SetBuffer(kernel, ClassCountersId,
                    frame.ClassCounters);
                boundaryCompute.SetTexture(kernel, MeasuredConsensusId,
                    measured.ConsensusDepth);
                boundaryCompute.SetTexture(kernel, BoundaryEvidenceTextureId,
                    measured.BoundaryEvidence);
                boundaryCompute.SetTexture(kernel, PredictedFilmIdsId,
                    frame.Source.FilmIdGeneration);
                boundaryCompute.SetTexture(kernel, RgbLeftId, rig.RgbLeft.Texture);
                boundaryCompute.SetTexture(kernel, RgbRightId, rig.RgbRight.Texture);
                boundaryCompute.SetTexture(kernel, RayLeftId,
                    luts.DepthLeft.CenterRaySolidAngle);
                boundaryCompute.SetTexture(kernel, RayRightId,
                    luts.DepthRight.CenterRaySolidAngle);
            }
        }

        private void AllocateFrameBuffers(int capacity)
        {
            if (_frameAccumulator != null) return;
            _frameAccumulator = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                checked(capacity * AccumulatorWords), sizeof(int));
            _dirtyFlags = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                capacity, sizeof(uint));
            _dirtyIndices = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                capacity, sizeof(uint));
            _dirtyState = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                4, sizeof(uint));
            _solveArguments = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments,
                1, sizeof(uint) * 3);
        }

        private void BindPersistent()
        {
            boundaryCompute.SetInt(BoundaryCapacityId, _pool.Capacity);
            boundaryCompute.SetInt(FilmCapacityId, filmSpawner.FilmPool.Capacity);
            boundaryCompute.SetInt(HashMaskId, _pool.HashCapacity - 1);
            int[] kernels =
            {
                _initializeKernel, _clearLoadedHashKernel, _rehashLoadedKernel,
                _clearKernel, _accumulateKernel, _absenceKernel, _buildArgsKernel,
                _solveKernel
            };
            foreach (int kernel in kernels)
            {
                boundaryCompute.SetBuffer(kernel, BoundaryHeadersId, _pool.Headers);
                boundaryCompute.SetBuffer(kernel, BoundaryInformationId,
                    _pool.Information);
                boundaryCompute.SetBuffer(kernel, BoundaryHashId, _pool.HashEntries);
                boundaryCompute.SetBuffer(kernel, BoundaryAllocatorId, _pool.Allocator);
                boundaryCompute.SetBuffer(kernel, FrameAccumulatorId, _frameAccumulator);
                boundaryCompute.SetBuffer(kernel, DirtyFlagsId, _dirtyFlags);
                boundaryCompute.SetBuffer(kernel, DirtyIndicesId, _dirtyIndices);
                boundaryCompute.SetBuffer(kernel, DirtyStateId, _dirtyState);
                boundaryCompute.SetBuffer(kernel, SolveArgumentsId, _solveArguments);
            }
            boundaryCompute.SetBuffer(_accumulateKernel, FilmHeadersId,
                filmSpawner.FilmPool.Headers);
            boundaryCompute.SetBuffer(_absenceKernel, FilmHeadersId,
                filmSpawner.FilmPool.Headers);
            boundaryCompute.SetBuffer(_solveKernel, FilmHeadersId,
                filmSpawner.FilmPool.Headers);
        }

        private void DisposeFrameBuffers()
        {
            _frameAccumulator?.Dispose();
            _dirtyFlags?.Dispose();
            _dirtyIndices?.Dispose();
            _dirtyState?.Dispose();
            _solveArguments?.Dispose();
            _frameAccumulator = null;
            _dirtyFlags = null;
            _dirtyIndices = null;
            _dirtyState = null;
            _solveArguments = null;
        }

        private static Vector4 Intrinsics(RigIntrinsics intrinsics) => new(
            intrinsics.FocalLength.x, intrinsics.FocalLength.y,
            intrinsics.PrincipalPoint.x, intrinsics.PrincipalPoint.y);

        private static Matrix4x4 PoseMatrix(Pose pose) =>
            Matrix4x4.TRS(pose.position, pose.rotation, Vector3.one);

        private static int CeilDiv(int value, int divisor) =>
            Math.Max(1, (value + divisor - 1) / divisor);
    }
}
