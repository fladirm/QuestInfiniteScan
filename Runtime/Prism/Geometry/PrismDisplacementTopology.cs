using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.Prism
{
    /// <summary>
    /// Owns only the measured hierarchical displacement posterior and the evidence
    /// field consumed by the PressureManifold atlas. Canonical chart topology is
    /// deliberately not mutated here; support contours, half-edges and topology
    /// adaptation have one owner in <see cref="PrismPressureManifoldAtlas"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(19)]
    public sealed class PrismDisplacementTopology : MonoBehaviour
    {
        private const int MatchDispatchOffset =
            (int)ConeEventClass.Match * sizeof(uint) * 3;
        private const int BehindDispatchOffset =
            (int)ConeEventClass.Behind * sizeof(uint) * 3;
        private const int BaseInitArgumentsOffset = 0;
        private const int CellArgumentsOffset = sizeof(uint) * 3;
        private const int MicroInitArgumentsOffset = sizeof(uint) * 6;
        private const int TopologyEvidenceArgumentsOffset = sizeof(uint) * 9;
        private const int AccumulatorWordsPerCell =
            ContactDisplacementPool.TransientAccumulatorWordsPerCell;
        private const int AccumulatorWordsPerFilm = 8;

        [SerializeField] private PrismBoundaryGraph boundaryGraph;
        [SerializeField] private PrismFilmSpawner filmSpawner;
        [SerializeField] private ComputeShader displacementCompute;
        [SerializeField, Min(256)] private int basePageCapacity = 8192;
        [SerializeField, Min(1024)] private int microPageCapacity = 16384;
        [SerializeField, Range(1, 4)] private int maximumMicroLevels = 3;
        [SerializeField, Range(0.05f, 0.9f)] private float qualityFloor = 0.25f;
        [SerializeField, Min(0.0001f)] private float minimumSigma = 0.00035f;
        [SerializeField, Min(0.0001f)] private float minimumHuberDelta = 0.001f;
        [SerializeField, Min(0.05f)] private float refinementFootprintRatio = 0.65f;
        [SerializeField, Min(0.000001f)] private float refinementVariance = 0.000004f;

        private static readonly int FilmCapacityId = Shader.PropertyToID("_FilmCapacity");
        private static readonly int EventCapacityId = Shader.PropertyToID("_EventCapacity");
        private static readonly int BasePageCapacityId =
            Shader.PropertyToID("_BasePageCapacity");
        private static readonly int MicroPageCapacityId =
            Shader.PropertyToID("_MicroPageCapacity");
        private static readonly int BaseCellCapacityId =
            Shader.PropertyToID("_BaseCellCapacity");
        private static readonly int MicroCellCapacityId =
            Shader.PropertyToID("_MicroCellCapacity");
        private static readonly int MaximumMicroLevelsId =
            Shader.PropertyToID("_MaximumMicroLevels");
        private static readonly int QualityFloorId = Shader.PropertyToID("_QualityFloor");
        private static readonly int MinimumSigmaId = Shader.PropertyToID("_MinimumSigma");
        private static readonly int MinimumHuberId = Shader.PropertyToID("_MinimumHuber");
        private static readonly int RefinementFootprintRatioId =
            Shader.PropertyToID("_RefinementFootprintRatio");
        private static readonly int RefinementVarianceId =
            Shader.PropertyToID("_RefinementVariance");
        private static readonly int ChunkFromDepthId =
            Shader.PropertyToID("_ChunkFromDepth");
        private static readonly int EventsId = Shader.PropertyToID("_ConeEvents");
        private static readonly int ClassifiedIndicesId =
            Shader.PropertyToID("_ClassifiedIndices");
        private static readonly int ClassCountersId =
            Shader.PropertyToID("_ClassCounters");
        private static readonly int RayLeftId =
            Shader.PropertyToID("_DepthRayCenterLeft");
        private static readonly int RayRightId =
            Shader.PropertyToID("_DepthRayCenterRight");
        private static readonly int FilmHeadersId = Shader.PropertyToID("_FilmHeaders");
        private static readonly int FilmInformationId =
            Shader.PropertyToID("_FilmInformation");
        private static readonly int FilmHeadersReadId =
            Shader.PropertyToID("_FilmHeadersRead");
        private static readonly int FilmInformationReadId =
            Shader.PropertyToID("_FilmInformationRead");
        private static readonly int DisplacementPagesId =
            Shader.PropertyToID("_DisplacementPages");
        private static readonly int DisplacementPagesReadId =
            Shader.PropertyToID("_DisplacementPagesRead");
        private static readonly int BaseCellsId =
            Shader.PropertyToID("_BaseDisplacementCells");
        private static readonly int MicroCellsId =
            Shader.PropertyToID("_MicroDisplacementCells");
        private static readonly int BaseCellsReadId =
            Shader.PropertyToID("_BaseDisplacementCellsRead");
        private static readonly int MicroCellsReadId =
            Shader.PropertyToID("_MicroDisplacementCellsRead");
        private static readonly int BaseChildrenId =
            Shader.PropertyToID("_BaseChildPages");
        private static readonly int MicroChildrenId =
            Shader.PropertyToID("_MicroChildPages");
        private static readonly int BaseChildrenReadId =
            Shader.PropertyToID("_BaseChildPagesRead");
        private static readonly int MicroChildrenReadId =
            Shader.PropertyToID("_MicroChildPagesRead");
        private static readonly int DisplacementAllocatorId =
            Shader.PropertyToID("_DisplacementAllocator");
        private static readonly int BaseCellAccumulatorId =
            Shader.PropertyToID("_BaseDisplacementCellAccumulator");
        private static readonly int MicroCellAccumulatorId =
            Shader.PropertyToID("_MicroDisplacementCellAccumulator");
        private static readonly int CellDirtyFlagsId =
            Shader.PropertyToID("_DisplacementCellDirtyFlags");
        private static readonly int DirtyCellIndicesId =
            Shader.PropertyToID("_DirtyDisplacementCellIndices");
        private static readonly int DirtyStateId =
            Shader.PropertyToID("_DisplacementDirtyState");
        private static readonly int NewBasePagesId = Shader.PropertyToID("_NewBasePages");
        private static readonly int NewMicroPagesId =
            Shader.PropertyToID("_NewMicroPages");
        private static readonly int NewBasePagesReadId =
            Shader.PropertyToID("_NewBasePagesRead");
        private static readonly int NewMicroPagesReadId =
            Shader.PropertyToID("_NewMicroPagesRead");
        private static readonly int PageStateId =
            Shader.PropertyToID("_DisplacementPageState");
        private static readonly int PageStateReadId =
            Shader.PropertyToID("_DisplacementPageStateRead");
        private static readonly int DirtyCellIndicesReadId =
            Shader.PropertyToID("_DirtyDisplacementCellIndicesRead");
        private static readonly int DirtyStateReadId =
            Shader.PropertyToID("_DisplacementDirtyStateRead");
        private static readonly int DispatchArgumentsId =
            Shader.PropertyToID("_DisplacementDispatchArguments");
        private static readonly int TopologyEvidenceId =
            Shader.PropertyToID("_TopologyEvidence");
        private static readonly int TopologyAccumulatorId =
            Shader.PropertyToID("_TopologyAccumulator");
        private static readonly int TopologyDirtyFlagsId =
            Shader.PropertyToID("_TopologyDirtyFlags");
        private static readonly int DirtyTopologyIndicesId =
            Shader.PropertyToID("_DirtyTopologyIndices");
        private static readonly int TopologyStateId =
            Shader.PropertyToID("_TopologyState");

        private readonly Matrix4x4[] _chunkFromDepth = new Matrix4x4[2];
        private Matrix4x4 _chunkFromWorld = Matrix4x4.identity;
        private ContactDisplacementPool _pool;
        private GraphicsBuffer _baseCellAccumulator;
        private GraphicsBuffer _microCellAccumulator;
        private GraphicsBuffer _cellDirtyFlags;
        private GraphicsBuffer _dirtyCellIndices;
        private GraphicsBuffer _dirtyState;
        private GraphicsBuffer _newBasePages;
        private GraphicsBuffer _newMicroPages;
        private GraphicsBuffer _pageState;
        private GraphicsBuffer _dispatchArguments;
        private GraphicsBuffer _topologyAccumulator;
        private GraphicsBuffer _topologyDirtyFlags;
        private GraphicsBuffer _dirtyTopologyIndices;
        private GraphicsBuffer _topologyState;
        private int _initializeStateKernel = -1;
        private int _clearKernel = -1;
        private int _allocateBaseKernel = -1;
        private int _allocateBehindBaseKernel = -1;
        private int _allocateOccluderBaseKernel = -1;
        private int _buildArgumentsKernel = -1;
        private int _initializeBaseKernel = -1;
        private int _accumulateKernel = -1;
        private int _accumulateTopologyKernel = -1;
        private int _accumulatePressureKernel = -1;
        private int _accumulateOccluderPressureKernel = -1;
        private int _solveKernel = -1;
        private int _allocateMicroKernel = -1;
        private int _initializeMicroKernel = -1;
        private int _solveTopologyEvidenceKernel = -1;
        private bool _running;
        private bool _subscribedToSource;
        private bool _initialized;
        private long _processedFrames;

        public event Action<ConeEventFrameLease> DisplacementUpdated;
        public ContactDisplacementPool DisplacementPool => _pool;
        public int MaximumMicroLevels => maximumMicroLevels;
        public long ProcessedFrames => _processedFrames;
        internal GraphicsBuffer BaseCellAccumulator => _baseCellAccumulator;
        internal GraphicsBuffer DisplacementCellDirtyFlags => _cellDirtyFlags;

        public void SetChunkFrame(Matrix4x4 worldFromChunk) =>
            _chunkFromWorld = worldFromChunk.inverse;

        public void StartUpdating(PrismBoundaryGraph boundaries = null,
            PrismFilmSpawner films = null, bool subscribeToSource = true)
        {
            if (_running) return;
            boundaryGraph = boundaries != null ? boundaries : boundaryGraph;
            filmSpawner = films != null ? films : filmSpawner;
            boundaryGraph ??= GetComponent<PrismBoundaryGraph>();
            filmSpawner ??= GetComponent<PrismFilmSpawner>();
            displacementCompute ??=
                Resources.Load<ComputeShader>("Prism/DisplacementTopology");
            if (filmSpawner?.FilmPool == null || displacementCompute == null)
            {
                Logger.Error("Cone-PRISM displacement posterior dependencies are missing.");
                return;
            }

            _pool ??= new ContactDisplacementPool(filmSpawner.FilmPool.Capacity,
                basePageCapacity, microPageCapacity);
            AllocateTransientBuffers(_pool);
            FindKernels();
            BindPersistent();
            if (!_initialized)
            {
                displacementCompute.Dispatch(_initializeStateKernel,
                    CeilDiv(_pool.FilmCapacity, 64), 1, 1);
                _initialized = true;
            }
            if (subscribeToSource && boundaryGraph != null)
            {
                boundaryGraph.BoundariesUpdated += OnBoundariesUpdated;
                _subscribedToSource = true;
            }
            _running = true;
        }

        public void StopUpdating()
        {
            if (_subscribedToSource && boundaryGraph != null)
                boundaryGraph.BoundariesUpdated -= OnBoundariesUpdated;
            _subscribedToSource = false;
            _running = false;
        }

        private void OnDestroy()
        {
            StopUpdating();
            DisposeTransientBuffers();
            _pool?.Dispose();
            _pool = null;
        }

        private void OnBoundariesUpdated(ConeEventFrameLease frame) =>
            DispatchDisplacement(frame);

        internal bool DispatchDisplacement(ConeEventFrameLease frame)
        {
            if (!_running || frame == null || frame.IsDisposed ||
                _pool == null || _pool.IsDisposed) return false;
            try
            {
                NormalizedRigFrameLease measured = frame.Source.Source;
                StereoRigFrameLease rig = measured.Source;
                ConeLutLease luts = measured.ConeLuts;
                _chunkFromDepth[0] = _chunkFromWorld *
                    PoseMatrix(rig.DepthLeft.WorldFromCamera);
                _chunkFromDepth[1] = _chunkFromWorld *
                    PoseMatrix(rig.DepthRight.WorldFromCamera);
                ConfigureFrame(frame, luts);
                BindPersistent();

                displacementCompute.Dispatch(_clearKernel, 1, 1, 1);
                displacementCompute.DispatchIndirect(_allocateBaseKernel,
                    frame.ClassDispatchArguments, MatchDispatchOffset);
                displacementCompute.DispatchIndirect(_allocateBehindBaseKernel,
                    frame.ClassDispatchArguments, BehindDispatchOffset);
                displacementCompute.DispatchIndirect(_allocateOccluderBaseKernel,
                    frame.ClassDispatchArguments, MatchDispatchOffset);
                displacementCompute.Dispatch(_buildArgumentsKernel, 1, 1, 1);
                displacementCompute.DispatchIndirect(_initializeBaseKernel,
                    _dispatchArguments, BaseInitArgumentsOffset);
                displacementCompute.DispatchIndirect(_accumulateKernel,
                    frame.ClassDispatchArguments, MatchDispatchOffset);
                displacementCompute.DispatchIndirect(_accumulateTopologyKernel,
                    frame.ClassDispatchArguments, MatchDispatchOffset);
                displacementCompute.DispatchIndirect(_accumulatePressureKernel,
                    frame.ClassDispatchArguments, BehindDispatchOffset);
                displacementCompute.DispatchIndirect(_accumulateOccluderPressureKernel,
                    frame.ClassDispatchArguments, MatchDispatchOffset);
                displacementCompute.Dispatch(_buildArgumentsKernel, 1, 1, 1);
                displacementCompute.DispatchIndirect(_solveKernel,
                    _dispatchArguments, CellArgumentsOffset);
                displacementCompute.DispatchIndirect(_allocateMicroKernel,
                    _dispatchArguments, CellArgumentsOffset);
                displacementCompute.Dispatch(_buildArgumentsKernel, 1, 1, 1);
                displacementCompute.DispatchIndirect(_initializeMicroKernel,
                    _dispatchArguments, MicroInitArgumentsOffset);
                displacementCompute.DispatchIndirect(_solveTopologyEvidenceKernel,
                    _dispatchArguments, TopologyEvidenceArgumentsOffset);
                _processedFrames++;
                if (DisplacementUpdated != null) DisplacementUpdated.Invoke(frame);
                else filmSpawner.NotifyFilmsMutated();
                return true;
            }
            catch (Exception exception)
            {
                Logger.Error("Cone-PRISM displacement posterior update failed: " +
                    exception.Message);
                filmSpawner.NotifyFilmsMutated();
                return false;
            }
        }

        private void FindKernels()
        {
            _initializeStateKernel =
                displacementCompute.FindKernel("InitializeDisplacementState");
            _clearKernel = displacementCompute.FindKernel("ClearDisplacementFrame");
            _allocateBaseKernel = displacementCompute.FindKernel("AllocateBasePages");
            _allocateBehindBaseKernel =
                displacementCompute.FindKernel("AllocateBasePagesBehind");
            _allocateOccluderBaseKernel =
                displacementCompute.FindKernel("AllocateBasePagesOccluder");
            _buildArgumentsKernel =
                displacementCompute.FindKernel("BuildDisplacementArguments");
            _initializeBaseKernel =
                displacementCompute.FindKernel("InitializeBasePages");
            _accumulateKernel =
                displacementCompute.FindKernel("AccumulateDisplacement");
            _accumulateTopologyKernel =
                displacementCompute.FindKernel("AccumulateTopologyEvidence");
            _accumulatePressureKernel =
                displacementCompute.FindKernel("AccumulatePreHitPressure");
            _accumulateOccluderPressureKernel = displacementCompute.FindKernel(
                "AccumulateOccluderPreHitPressure");
            _solveKernel = displacementCompute.FindKernel("SolveDirtyDisplacement");
            _allocateMicroKernel = displacementCompute.FindKernel("AllocateMicrotiles");
            _initializeMicroKernel =
                displacementCompute.FindKernel("InitializeMicroPages");
            _solveTopologyEvidenceKernel =
                displacementCompute.FindKernel("SolveTopologyEvidence");
        }

        private void ConfigureFrame(ConeEventFrameLease frame, ConeLutLease luts)
        {
            displacementCompute.SetInt(EventCapacityId, frame.EventCapacity);
            displacementCompute.SetInt(MaximumMicroLevelsId, maximumMicroLevels);
            displacementCompute.SetFloat(QualityFloorId, qualityFloor);
            displacementCompute.SetFloat(MinimumSigmaId, minimumSigma);
            displacementCompute.SetFloat(MinimumHuberId, minimumHuberDelta);
            displacementCompute.SetFloat(RefinementFootprintRatioId,
                refinementFootprintRatio);
            displacementCompute.SetFloat(RefinementVarianceId,
                refinementVariance);
            displacementCompute.SetMatrixArray(ChunkFromDepthId, _chunkFromDepth);
            int[] eventKernels =
            {
                _allocateBaseKernel, _allocateBehindBaseKernel,
                _allocateOccluderBaseKernel, _accumulateKernel,
                _accumulateTopologyKernel, _accumulatePressureKernel,
                _accumulateOccluderPressureKernel
            };
            foreach (int kernel in eventKernels)
            {
                displacementCompute.SetBuffer(kernel, EventsId, frame.Events);
                displacementCompute.SetBuffer(kernel, ClassifiedIndicesId,
                    frame.ClassifiedIndices);
                displacementCompute.SetBuffer(kernel, ClassCountersId,
                    frame.ClassCounters);
            }
            int[] rayKernels =
            {
                _accumulateKernel, _accumulateTopologyKernel,
                _accumulatePressureKernel, _accumulateOccluderPressureKernel
            };
            foreach (int kernel in rayKernels)
            {
                displacementCompute.SetTexture(kernel, RayLeftId,
                    luts.DepthLeft.CenterRaySolidAngle);
                displacementCompute.SetTexture(kernel, RayRightId,
                    luts.DepthRight.CenterRaySolidAngle);
            }
        }

        private void AllocateTransientBuffers(ContactDisplacementPool pool)
        {
            if (_baseCellAccumulator != null) return;
            int cells = pool.TotalCellCapacity;
            _baseCellAccumulator = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                checked(pool.BaseCellCapacity * AccumulatorWordsPerCell),
                sizeof(int));
            _microCellAccumulator = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                checked(pool.MicroCellCapacity * AccumulatorWordsPerCell),
                sizeof(int));
            _cellDirtyFlags = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                cells, sizeof(uint));
            _dirtyCellIndices = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                cells, sizeof(uint));
            _dirtyState = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                4, sizeof(uint));
            _newBasePages = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                pool.BasePageCapacity, sizeof(uint));
            _newMicroPages = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                pool.MicroPageCapacity, sizeof(uint));
            _pageState = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                4, sizeof(uint));
            _dispatchArguments = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured |
                GraphicsBuffer.Target.IndirectArguments, 4, sizeof(uint) * 3);
            _topologyAccumulator = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                checked(pool.FilmCapacity * AccumulatorWordsPerFilm), sizeof(int));
            _topologyDirtyFlags = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                pool.FilmCapacity, sizeof(uint));
            _dirtyTopologyIndices = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                pool.FilmCapacity, sizeof(uint));
            _topologyState = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                4, sizeof(uint));
            uint[] zeros = { 0u, 0u, 0u, 0u };
            _dirtyState.SetData(zeros);
            _pageState.SetData(zeros);
            _topologyState.SetData(zeros);
        }

        private void BindPersistent()
        {
            ContactFilmPool films = filmSpawner.FilmPool;
            displacementCompute.SetInt(FilmCapacityId, films.Capacity);
            displacementCompute.SetInt(BasePageCapacityId, _pool.BasePageCapacity);
            displacementCompute.SetInt(MicroPageCapacityId, _pool.MicroPageCapacity);
            displacementCompute.SetInt(BaseCellCapacityId, _pool.BaseCellCapacity);
            displacementCompute.SetInt(MicroCellCapacityId, _pool.MicroCellCapacity);
            int[] kernels =
            {
                _initializeStateKernel, _clearKernel, _allocateBaseKernel,
                _allocateBehindBaseKernel, _allocateOccluderBaseKernel,
                _buildArgumentsKernel, _initializeBaseKernel, _accumulateKernel,
                _accumulateTopologyKernel, _accumulatePressureKernel,
                _accumulateOccluderPressureKernel, _solveKernel,
                _allocateMicroKernel, _initializeMicroKernel,
                _solveTopologyEvidenceKernel
            };
            foreach (int kernel in kernels)
            {
                displacementCompute.SetBuffer(kernel, FilmHeadersId, films.Headers);
                displacementCompute.SetBuffer(kernel, FilmInformationId,
                    films.Information);
                displacementCompute.SetBuffer(kernel, DisplacementPagesId,
                    _pool.PageHeaders);
                displacementCompute.SetBuffer(kernel, BaseCellsId, _pool.BaseCells);
                displacementCompute.SetBuffer(kernel, MicroCellsId, _pool.MicroCells);
                displacementCompute.SetBuffer(kernel, BaseChildrenId,
                    _pool.BaseChildPages);
                displacementCompute.SetBuffer(kernel, MicroChildrenId,
                    _pool.MicroChildPages);
                displacementCompute.SetBuffer(kernel, DisplacementAllocatorId,
                    _pool.Allocator);
                displacementCompute.SetBuffer(kernel, BaseCellAccumulatorId,
                    _baseCellAccumulator);
                displacementCompute.SetBuffer(kernel, MicroCellAccumulatorId,
                    _microCellAccumulator);
                displacementCompute.SetBuffer(kernel, CellDirtyFlagsId,
                    _cellDirtyFlags);
                displacementCompute.SetBuffer(kernel, DirtyCellIndicesId,
                    _dirtyCellIndices);
                displacementCompute.SetBuffer(kernel, DirtyStateId, _dirtyState);
                displacementCompute.SetBuffer(kernel, NewBasePagesId, _newBasePages);
                displacementCompute.SetBuffer(kernel, NewMicroPagesId, _newMicroPages);
                displacementCompute.SetBuffer(kernel, PageStateId, _pageState);
                displacementCompute.SetBuffer(kernel, DispatchArgumentsId,
                    _dispatchArguments);
                displacementCompute.SetBuffer(kernel, TopologyEvidenceId,
                    _pool.TopologyEvidence);
                displacementCompute.SetBuffer(kernel, TopologyAccumulatorId,
                    _topologyAccumulator);
                displacementCompute.SetBuffer(kernel, TopologyDirtyFlagsId,
                    _topologyDirtyFlags);
                displacementCompute.SetBuffer(kernel, DirtyTopologyIndicesId,
                    _dirtyTopologyIndices);
                displacementCompute.SetBuffer(kernel, TopologyStateId,
                    _topologyState);
            }

            int[] readHierarchyKernels =
            {
                _accumulateKernel, _accumulateTopologyKernel,
                _accumulatePressureKernel, _accumulateOccluderPressureKernel,
                _allocateMicroKernel
            };
            foreach (int kernel in readHierarchyKernels)
            {
                displacementCompute.SetBuffer(kernel, DisplacementPagesReadId,
                    _pool.PageHeaders);
                displacementCompute.SetBuffer(kernel, BaseCellsReadId,
                    _pool.BaseCells);
                displacementCompute.SetBuffer(kernel, MicroCellsReadId,
                    _pool.MicroCells);
            }
            foreach (int kernel in new[] { _accumulateKernel, _accumulateTopologyKernel })
            {
                displacementCompute.SetBuffer(kernel, BaseChildrenReadId,
                    _pool.BaseChildPages);
                displacementCompute.SetBuffer(kernel, MicroChildrenReadId,
                    _pool.MicroChildPages);
            }
            displacementCompute.SetBuffer(_initializeBaseKernel, FilmHeadersReadId,
                films.Headers);
            displacementCompute.SetBuffer(_initializeBaseKernel,
                FilmInformationReadId, films.Information);
            displacementCompute.SetBuffer(_initializeBaseKernel, NewBasePagesReadId,
                _newBasePages);
            displacementCompute.SetBuffer(_initializeBaseKernel, PageStateReadId,
                _pageState);
            displacementCompute.SetBuffer(_accumulateTopologyKernel,
                FilmHeadersReadId, films.Headers);
            displacementCompute.SetBuffer(_allocateMicroKernel, FilmHeadersReadId,
                films.Headers);
            displacementCompute.SetBuffer(_allocateMicroKernel,
                DirtyCellIndicesReadId, _dirtyCellIndices);
            displacementCompute.SetBuffer(_allocateMicroKernel, DirtyStateReadId,
                _dirtyState);
            foreach (int kernel in new[]
                     { _accumulatePressureKernel, _accumulateOccluderPressureKernel })
                displacementCompute.SetBuffer(kernel, FilmInformationReadId,
                    films.Information);
            displacementCompute.SetBuffer(_initializeMicroKernel, FilmHeadersReadId,
                films.Headers);
            displacementCompute.SetBuffer(_initializeMicroKernel,
                DisplacementPagesReadId, _pool.PageHeaders);
            displacementCompute.SetBuffer(_initializeMicroKernel, BaseCellsReadId,
                _pool.BaseCells);
            displacementCompute.SetBuffer(_initializeMicroKernel,
                NewMicroPagesReadId, _newMicroPages);
            displacementCompute.SetBuffer(_initializeMicroKernel, PageStateReadId,
                _pageState);
        }

        private void DisposeTransientBuffers()
        {
            _baseCellAccumulator?.Dispose();
            _microCellAccumulator?.Dispose();
            _cellDirtyFlags?.Dispose();
            _dirtyCellIndices?.Dispose();
            _dirtyState?.Dispose();
            _newBasePages?.Dispose();
            _newMicroPages?.Dispose();
            _pageState?.Dispose();
            _dispatchArguments?.Dispose();
            _topologyAccumulator?.Dispose();
            _topologyDirtyFlags?.Dispose();
            _dirtyTopologyIndices?.Dispose();
            _topologyState?.Dispose();
            _baseCellAccumulator = null;
            _microCellAccumulator = null;
            _cellDirtyFlags = null;
            _dirtyCellIndices = null;
            _dirtyState = null;
            _newBasePages = null;
            _newMicroPages = null;
            _pageState = null;
            _dispatchArguments = null;
            _topologyAccumulator = null;
            _topologyDirtyFlags = null;
            _dirtyTopologyIndices = null;
            _topologyState = null;
        }

        private static Matrix4x4 PoseMatrix(Pose pose) =>
            Matrix4x4.TRS(pose.position, pose.rotation, Vector3.one);

        private static int CeilDiv(int value, int divisor) =>
            Math.Max(1, (value + divisor - 1) / divisor);
    }
}
