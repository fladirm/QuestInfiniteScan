using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.Prism
{
    /// <summary>
    /// Updates sparse hierarchical ContactFilm displacement from MATCH events. It
    /// preserves the same pressure-quality invariant as the quadratic posterior and
    /// allocates finer pages only when measured footprint and residual information
    /// demonstrate resolvable detail.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(19)]
    public sealed class PrismDisplacementTopology : MonoBehaviour
    {
        private const int MatchDispatchOffset =
            (int)ConeEventClass.Match * sizeof(uint) * 3;
        private const int BaseInitArgumentsOffset = 0;
        private const int CellArgumentsOffset = sizeof(uint) * 3;
        private const int MicroInitArgumentsOffset = sizeof(uint) * 6;
        private const int TopologyArgumentsOffset = sizeof(uint) * 9;
        private const int SplitInitializeArgumentsOffset = 0;
        private const int BoundaryTransferArgumentsOffset = sizeof(uint) * 3;
        private const int BoundaryHashClearArgumentsOffset = sizeof(uint) * 6;
        private const int BoundaryRehashArgumentsOffset = sizeof(uint) * 9;
        private const int FilmAdaptArgumentsOffset = sizeof(uint) * 12;
        private const int FilmHashClearArgumentsOffset = sizeof(uint) * 15;
        private const int MergeInitializeArgumentsOffset = sizeof(uint) * 18;
        private const int AccumulatorWordsPerCell = 8;
        private const int AccumulatorWordsPerFilm = 8;

        [SerializeField] private PrismBoundaryGraph boundaryGraph;
        [SerializeField] private PrismFilmSpawner filmSpawner;
        [SerializeField] private ComputeShader displacementCompute;
        [SerializeField] private ComputeShader topologyCompute;
        [SerializeField, Min(256)] private int basePageCapacity = 8192;
        [SerializeField, Min(1024)] private int microPageCapacity = 16384;
        [SerializeField, Range(1, 4)] private int maximumMicroLevels = 3;
        [SerializeField, Range(0.05f, 0.9f)] private float qualityFloor = 0.25f;
        [SerializeField, Min(0.0001f)] private float minimumSigma = 0.00035f;
        [SerializeField, Min(0.0001f)] private float minimumHuberDelta = 0.001f;
        [SerializeField, Min(0.05f)] private float refinementFootprintRatio = 0.65f;
        [SerializeField, Min(0.000001f)] private float refinementVariance = 0.000004f;
        [Header("Adaptive topology")]
        [SerializeField, Range(1, 8)] private int maximumSplitDepth = 5;
        [SerializeField, Min(0.0025f)] private float minimumSplitExtent = 0.0125f;
        [SerializeField, Min(0.0005f)] private float bimodalSeparation = 0.003f;
        [SerializeField, Min(0.000001f)] private float splitVariance = 0.000009f;
        [SerializeField, Min(1f)] private float splitBoundarySupport = 8f;
        [SerializeField, Min(0.1f)] private float mergeHashCellSize = 0.5f;
        [SerializeField, Range(0.1f, 5f)] private float mergeNormalDegrees = 3f;
        [SerializeField, Min(0.0005f)] private float mergeSurfaceGap = 0.005f;

        private static readonly int FilmCapacityId = Shader.PropertyToID("_FilmCapacity");
        private static readonly int EventCapacityId = Shader.PropertyToID("_EventCapacity");
        private static readonly int BasePageCapacityId = Shader.PropertyToID("_BasePageCapacity");
        private static readonly int MicroPageCapacityId = Shader.PropertyToID("_MicroPageCapacity");
        private static readonly int BaseCellCapacityId = Shader.PropertyToID("_BaseCellCapacity");
        private static readonly int MicroCellCapacityId = Shader.PropertyToID("_MicroCellCapacity");
        private static readonly int MaximumMicroLevelsId = Shader.PropertyToID("_MaximumMicroLevels");
        private static readonly int QualityFloorId = Shader.PropertyToID("_QualityFloor");
        private static readonly int MinimumSigmaId = Shader.PropertyToID("_MinimumSigma");
        private static readonly int MinimumHuberId = Shader.PropertyToID("_MinimumHuber");
        private static readonly int RefinementFootprintRatioId = Shader.PropertyToID("_RefinementFootprintRatio");
        private static readonly int RefinementVarianceId = Shader.PropertyToID("_RefinementVariance");
        private static readonly int ChunkFromDepthId = Shader.PropertyToID("_ChunkFromDepth");
        private static readonly int EventsId = Shader.PropertyToID("_ConeEvents");
        private static readonly int ClassifiedIndicesId = Shader.PropertyToID("_ClassifiedIndices");
        private static readonly int ClassCountersId = Shader.PropertyToID("_ClassCounters");
        private static readonly int RayLeftId = Shader.PropertyToID("_DepthRayCenterLeft");
        private static readonly int RayRightId = Shader.PropertyToID("_DepthRayCenterRight");
        private static readonly int FilmHeadersId = Shader.PropertyToID("_FilmHeaders");
        private static readonly int FilmInformationId = Shader.PropertyToID("_FilmInformation");
        private static readonly int DisplacementPagesId = Shader.PropertyToID("_DisplacementPages");
        private static readonly int BaseCellsId = Shader.PropertyToID("_BaseDisplacementCells");
        private static readonly int MicroCellsId = Shader.PropertyToID("_MicroDisplacementCells");
        private static readonly int BaseChildrenId = Shader.PropertyToID("_BaseChildPages");
        private static readonly int MicroChildrenId = Shader.PropertyToID("_MicroChildPages");
        private static readonly int DisplacementAllocatorId = Shader.PropertyToID("_DisplacementAllocator");
        private static readonly int CellAccumulatorId = Shader.PropertyToID("_DisplacementCellAccumulator");
        private static readonly int CellDirtyFlagsId = Shader.PropertyToID("_DisplacementCellDirtyFlags");
        private static readonly int DirtyCellIndicesId = Shader.PropertyToID("_DirtyDisplacementCellIndices");
        private static readonly int DirtyStateId = Shader.PropertyToID("_DisplacementDirtyState");
        private static readonly int NewBasePagesId = Shader.PropertyToID("_NewBasePages");
        private static readonly int NewMicroPagesId = Shader.PropertyToID("_NewMicroPages");
        private static readonly int PageStateId = Shader.PropertyToID("_DisplacementPageState");
        private static readonly int DispatchArgumentsId = Shader.PropertyToID("_DisplacementDispatchArguments");
        private static readonly int TopologyEvidenceId = Shader.PropertyToID("_TopologyEvidence");
        private static readonly int TopologyAccumulatorId = Shader.PropertyToID("_TopologyAccumulator");
        private static readonly int TopologyDirtyFlagsId = Shader.PropertyToID("_TopologyDirtyFlags");
        private static readonly int DirtyTopologyIndicesId = Shader.PropertyToID("_DirtyTopologyIndices");
        private static readonly int TopologyStateId = Shader.PropertyToID("_TopologyState");
        private static readonly int FilmAllocatorId = Shader.PropertyToID("_FilmAllocator");
        private static readonly int BoundaryCapacityId = Shader.PropertyToID("_BoundaryCapacity");
        private static readonly int BoundaryHashMaskId = Shader.PropertyToID("_BoundaryHashMask");
        private static readonly int BoundaryCellsPerAxisId = Shader.PropertyToID("_BoundaryCellsPerAxis");
        private static readonly int BoundaryHeadersId = Shader.PropertyToID("_BoundaryHeaders");
        private static readonly int BoundaryInformationId = Shader.PropertyToID("_BoundaryInformation");
        private static readonly int BoundaryHashId = Shader.PropertyToID("_BoundaryHash");
        private static readonly int BoundaryAllocatorId = Shader.PropertyToID("_BoundaryAllocator");
        private static readonly int SplitRecordsId = Shader.PropertyToID("_SplitRecords");
        private static readonly int AdaptStateId = Shader.PropertyToID("_AdaptState");
        private static readonly int AdaptArgumentsId = Shader.PropertyToID("_AdaptDispatchArguments");
        private static readonly int MaximumSplitDepthId = Shader.PropertyToID("_MaximumSplitDepth");
        private static readonly int MinimumSplitExtentId = Shader.PropertyToID("_MinimumSplitExtent");
        private static readonly int BimodalSeparationId = Shader.PropertyToID("_BimodalSeparation");
        private static readonly int SplitVarianceId = Shader.PropertyToID("_SplitVariance");
        private static readonly int SplitBoundarySupportId = Shader.PropertyToID("_SplitBoundarySupport");
        private static readonly int MergeRecordsId = Shader.PropertyToID("_MergeRecords");
        private static readonly int FilmMergeHashId = Shader.PropertyToID("_FilmMergeHash");
        private static readonly int FilmMergeHashMaskId = Shader.PropertyToID("_FilmMergeHashMask");
        private static readonly int MergeHashCellSizeId = Shader.PropertyToID("_MergeHashCellSize");
        private static readonly int MergeNormalCosineId = Shader.PropertyToID("_MergeNormalCosine");
        private static readonly int MergeSurfaceGapId = Shader.PropertyToID("_MergeSurfaceGap");

        private readonly Matrix4x4[] _chunkFromDepth = new Matrix4x4[2];
        private Matrix4x4 _chunkFromWorld = Matrix4x4.identity;
        private ContactDisplacementPool _pool;
        private GraphicsBuffer _cellAccumulator;
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
        private GraphicsBuffer _splitRecords;
        private GraphicsBuffer _adaptState;
        private GraphicsBuffer _adaptArguments;
        private GraphicsBuffer _mergeRecords;
        private GraphicsBuffer _filmMergeHash;
        private int _clearKernel = -1;
        private int _initializeStateKernel = -1;
        private int _allocateBaseKernel = -1;
        private int _buildArgumentsKernel = -1;
        private int _initializeBaseKernel = -1;
        private int _accumulateKernel = -1;
        private int _solveKernel = -1;
        private int _allocateMicroKernel = -1;
        private int _initializeMicroKernel = -1;
        private int _solveTopologyKernel = -1;
        private int _clearAdaptKernel = -1;
        private int _splitKernel = -1;
        private int _buildAdaptArgumentsKernel = -1;
        private int _initializeSplitKernel = -1;
        private int _transferBoundariesKernel = -1;
        private int _clearBoundaryHashKernel = -1;
        private int _rehashBoundariesKernel = -1;
        private int _clearFilmMergeHashKernel = -1;
        private int _buildFilmMergeHashKernel = -1;
        private int _mergeFilmsKernel = -1;
        private int _buildMergeArgumentsKernel = -1;
        private int _initializeMergeKernel = -1;
        private bool _running;
        private bool _initialized;
        private long _processedFrames;

        public event Action<ConeEventFrameLease> DisplacementUpdated;
        public ContactDisplacementPool DisplacementPool => _pool;
        public int MaximumMicroLevels => maximumMicroLevels;
        public long ProcessedFrames => _processedFrames;

        public void SetChunkFrame(Matrix4x4 worldFromChunk) =>
            _chunkFromWorld = worldFromChunk.inverse;

        public void StartUpdating(PrismBoundaryGraph boundaries = null,
            PrismFilmSpawner films = null)
        {
            if (_running) return;
            boundaryGraph = boundaries != null ? boundaries : boundaryGraph;
            filmSpawner = films != null ? films : filmSpawner;
            boundaryGraph ??= GetComponent<PrismBoundaryGraph>();
            filmSpawner ??= GetComponent<PrismFilmSpawner>();
            displacementCompute ??=
                Resources.Load<ComputeShader>("Prism/DisplacementTopology");
            topologyCompute ??= Resources.Load<ComputeShader>("Prism/TopologyAdapt");
            if (boundaryGraph?.BoundaryPool == null ||
                filmSpawner?.FilmPool == null ||
                displacementCompute == null || topologyCompute == null)
            {
                Logger.Error("Cone-PRISM displacement dependencies are missing.");
                return;
            }

            _pool ??= new ContactDisplacementPool(filmSpawner.FilmPool.Capacity,
                basePageCapacity, microPageCapacity);
            AllocateTransientBuffers(_pool);
            _initializeStateKernel =
                displacementCompute.FindKernel("InitializeDisplacementState");
            _clearKernel = displacementCompute.FindKernel("ClearDisplacementFrame");
            _allocateBaseKernel = displacementCompute.FindKernel("AllocateBasePages");
            _buildArgumentsKernel = displacementCompute.FindKernel("BuildDisplacementArguments");
            _initializeBaseKernel = displacementCompute.FindKernel("InitializeBasePages");
            _accumulateKernel = displacementCompute.FindKernel("AccumulateDisplacement");
            _solveKernel = displacementCompute.FindKernel("SolveDirtyDisplacement");
            _allocateMicroKernel = displacementCompute.FindKernel("AllocateMicrotiles");
            _initializeMicroKernel = displacementCompute.FindKernel("InitializeMicroPages");
            _solveTopologyKernel = displacementCompute.FindKernel("SolveTopologyEvidence");
            _clearAdaptKernel = topologyCompute.FindKernel("ClearTopologyAdaptFrame");
            _splitKernel = topologyCompute.FindKernel("SplitContactFilms");
            _buildAdaptArgumentsKernel =
                topologyCompute.FindKernel("BuildTopologyAdaptArguments");
            _initializeSplitKernel =
                topologyCompute.FindKernel("InitializeSplitDisplacement");
            _transferBoundariesKernel =
                topologyCompute.FindKernel("TransferSplitBoundaries");
            _clearBoundaryHashKernel =
                topologyCompute.FindKernel("ClearTopologyBoundaryHash");
            _rehashBoundariesKernel =
                topologyCompute.FindKernel("RehashTopologyBoundaries");
            _clearFilmMergeHashKernel =
                topologyCompute.FindKernel("ClearFilmMergeHash");
            _buildFilmMergeHashKernel =
                topologyCompute.FindKernel("BuildFilmMergeHash");
            _mergeFilmsKernel = topologyCompute.FindKernel("MergeCompatibleFilms");
            _buildMergeArgumentsKernel =
                topologyCompute.FindKernel("BuildMergeArguments");
            _initializeMergeKernel =
                topologyCompute.FindKernel("InitializeMergedDisplacement");
            BindPersistent();
            BindTopology();
            if (!_initialized)
            {
                displacementCompute.Dispatch(_initializeStateKernel,
                    CeilDiv(_pool.FilmCapacity, 64), 1, 1);
                _initialized = true;
            }
            boundaryGraph.BoundariesUpdated += OnBoundariesUpdated;
            _running = true;
        }

        public void StopUpdating()
        {
            if (_running && boundaryGraph != null)
                boundaryGraph.BoundariesUpdated -= OnBoundariesUpdated;
            _running = false;
        }

        private void OnDestroy()
        {
            StopUpdating();
            DisposeTransientBuffers();
            _pool?.Dispose();
            _pool = null;
        }

        private void OnBoundariesUpdated(ConeEventFrameLease frame)
        {
            if (!_running || frame == null || frame.IsDisposed ||
                _pool == null || _pool.IsDisposed) return;
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
                displacementCompute.Dispatch(_buildArgumentsKernel, 1, 1, 1);
                displacementCompute.DispatchIndirect(_initializeBaseKernel,
                    _dispatchArguments, BaseInitArgumentsOffset);
                displacementCompute.DispatchIndirect(_accumulateKernel,
                    frame.ClassDispatchArguments, MatchDispatchOffset);
                displacementCompute.Dispatch(_buildArgumentsKernel, 1, 1, 1);
                displacementCompute.DispatchIndirect(_solveKernel,
                    _dispatchArguments, CellArgumentsOffset);
                displacementCompute.DispatchIndirect(_allocateMicroKernel,
                    _dispatchArguments, CellArgumentsOffset);
                displacementCompute.Dispatch(_buildArgumentsKernel, 1, 1, 1);
                displacementCompute.DispatchIndirect(_initializeMicroKernel,
                    _dispatchArguments, MicroInitArgumentsOffset);
                displacementCompute.DispatchIndirect(_solveTopologyKernel,
                    _dispatchArguments, TopologyArgumentsOffset);
                RunTopologyAdaptation();
                _processedFrames++;
                if (DisplacementUpdated != null) DisplacementUpdated.Invoke(frame);
                else filmSpawner.NotifyFilmsMutated();
            }
            catch (Exception exception)
            {
                Logger.Error($"Cone-PRISM displacement update failed: {exception.Message}");
                filmSpawner.NotifyFilmsMutated();
            }
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
            int[] eventKernels = { _allocateBaseKernel, _accumulateKernel };
            foreach (int kernel in eventKernels)
            {
                displacementCompute.SetBuffer(kernel, EventsId, frame.Events);
                displacementCompute.SetBuffer(kernel, ClassifiedIndicesId,
                    frame.ClassifiedIndices);
                displacementCompute.SetBuffer(kernel, ClassCountersId,
                    frame.ClassCounters);
            }
            displacementCompute.SetTexture(_accumulateKernel, RayLeftId,
                luts.DepthLeft.CenterRaySolidAngle);
            displacementCompute.SetTexture(_accumulateKernel, RayRightId,
                luts.DepthRight.CenterRaySolidAngle);
        }

        private void AllocateTransientBuffers(ContactDisplacementPool pool)
        {
            if (_cellAccumulator != null) return;
            int cells = pool.TotalCellCapacity;
            _cellAccumulator = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                checked(cells * AccumulatorWordsPerCell), sizeof(int));
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
                GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments,
                4, sizeof(uint) * 3);
            _topologyAccumulator = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                checked(pool.FilmCapacity * AccumulatorWordsPerFilm), sizeof(int));
            _topologyDirtyFlags = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                pool.FilmCapacity, sizeof(uint));
            _dirtyTopologyIndices = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                pool.FilmCapacity, sizeof(uint));
            _topologyState = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                4, sizeof(uint));
            _splitRecords = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                pool.FilmCapacity, TopologySplitRecordGpu.Stride);
            _adaptState = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                8, sizeof(uint));
            _adaptArguments = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured |
                GraphicsBuffer.Target.IndirectArguments, 8, sizeof(uint) * 3);
            _mergeRecords = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                pool.FilmCapacity, TopologyMergeRecordGpu.Stride);
            int mergeHashCapacity = NextPowerOfTwo(pool.FilmCapacity * 2);
            _filmMergeHash = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                mergeHashCapacity, FilmMergeHashEntryGpu.Stride);
            uint[] zeros = { 0u, 0u, 0u, 0u };
            _dirtyState.SetData(zeros);
            _pageState.SetData(zeros);
            _topologyState.SetData(zeros);
            _adaptState.SetData(new uint[8]);
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
                _buildArgumentsKernel,
                _initializeBaseKernel, _accumulateKernel, _solveKernel,
                _allocateMicroKernel, _initializeMicroKernel,
                _solveTopologyKernel
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
                displacementCompute.SetBuffer(kernel, CellAccumulatorId,
                    _cellAccumulator);
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
        }

        private void BindTopology()
        {
            ContactFilmPool films = filmSpawner.FilmPool;
            ContactBoundaryPool boundaries = boundaryGraph.BoundaryPool;
            topologyCompute.SetInt(FilmCapacityId, films.Capacity);
            topologyCompute.SetInt(BasePageCapacityId, _pool.BasePageCapacity);
            topologyCompute.SetInt(MicroPageCapacityId, _pool.MicroPageCapacity);
            topologyCompute.SetInt(BaseCellCapacityId, _pool.BaseCellCapacity);
            topologyCompute.SetInt(MaximumMicroLevelsId, maximumMicroLevels);
            topologyCompute.SetInt(BoundaryCapacityId, boundaries.Capacity);
            topologyCompute.SetInt(BoundaryHashMaskId,
                boundaries.HashCapacity - 1);
            topologyCompute.SetInt(BoundaryCellsPerAxisId,
                boundaryGraph.CellsPerAxis);
            topologyCompute.SetInt(MaximumSplitDepthId, maximumSplitDepth);
            topologyCompute.SetFloat(MinimumSplitExtentId, minimumSplitExtent);
            topologyCompute.SetFloat(BimodalSeparationId, bimodalSeparation);
            topologyCompute.SetFloat(SplitVarianceId, splitVariance);
            topologyCompute.SetFloat(SplitBoundarySupportId,
                splitBoundarySupport);
            topologyCompute.SetInt(FilmMergeHashMaskId,
                _filmMergeHash.count - 1);
            topologyCompute.SetFloat(MergeHashCellSizeId, mergeHashCellSize);
            topologyCompute.SetFloat(MergeNormalCosineId,
                Mathf.Cos(mergeNormalDegrees * Mathf.Deg2Rad));
            topologyCompute.SetFloat(MergeSurfaceGapId, mergeSurfaceGap);
            int[] kernels =
            {
                _clearAdaptKernel, _splitKernel, _buildAdaptArgumentsKernel,
                _initializeSplitKernel, _transferBoundariesKernel,
                _clearBoundaryHashKernel, _rehashBoundariesKernel,
                _clearFilmMergeHashKernel, _buildFilmMergeHashKernel,
                _mergeFilmsKernel, _buildMergeArgumentsKernel,
                _initializeMergeKernel
            };
            foreach (int kernel in kernels)
            {
                topologyCompute.SetBuffer(kernel, FilmHeadersId, films.Headers);
                topologyCompute.SetBuffer(kernel, FilmInformationId,
                    films.Information);
                topologyCompute.SetBuffer(kernel, FilmAllocatorId,
                    films.Allocator);
                topologyCompute.SetBuffer(kernel, DisplacementPagesId,
                    _pool.PageHeaders);
                topologyCompute.SetBuffer(kernel, BaseCellsId, _pool.BaseCells);
                topologyCompute.SetBuffer(kernel, MicroCellsId, _pool.MicroCells);
                topologyCompute.SetBuffer(kernel, BaseChildrenId,
                    _pool.BaseChildPages);
                topologyCompute.SetBuffer(kernel, MicroChildrenId,
                    _pool.MicroChildPages);
                topologyCompute.SetBuffer(kernel, DisplacementAllocatorId,
                    _pool.Allocator);
                topologyCompute.SetBuffer(kernel, CellAccumulatorId,
                    _cellAccumulator);
                topologyCompute.SetBuffer(kernel, CellDirtyFlagsId,
                    _cellDirtyFlags);
                topologyCompute.SetBuffer(kernel, TopologyEvidenceId,
                    _pool.TopologyEvidence);
                topologyCompute.SetBuffer(kernel, DirtyTopologyIndicesId,
                    _dirtyTopologyIndices);
                topologyCompute.SetBuffer(kernel, TopologyStateId,
                    _topologyState);
                topologyCompute.SetBuffer(kernel, BoundaryHeadersId,
                    boundaries.Headers);
                topologyCompute.SetBuffer(kernel, BoundaryInformationId,
                    boundaries.Information);
                topologyCompute.SetBuffer(kernel, BoundaryHashId,
                    boundaries.HashEntries);
                topologyCompute.SetBuffer(kernel, BoundaryAllocatorId,
                    boundaries.Allocator);
                topologyCompute.SetBuffer(kernel, SplitRecordsId, _splitRecords);
                topologyCompute.SetBuffer(kernel, MergeRecordsId, _mergeRecords);
                topologyCompute.SetBuffer(kernel, FilmMergeHashId,
                    _filmMergeHash);
                topologyCompute.SetBuffer(kernel, AdaptStateId, _adaptState);
                topologyCompute.SetBuffer(kernel, AdaptArgumentsId,
                    _adaptArguments);
            }
        }

        private void RunTopologyAdaptation()
        {
            topologyCompute.Dispatch(_clearAdaptKernel, 1, 1, 1);
            topologyCompute.DispatchIndirect(_splitKernel, _dispatchArguments,
                TopologyArgumentsOffset);
            topologyCompute.Dispatch(_buildAdaptArgumentsKernel, 1, 1, 1);
            topologyCompute.DispatchIndirect(_initializeSplitKernel,
                _adaptArguments, SplitInitializeArgumentsOffset);
            topologyCompute.DispatchIndirect(_transferBoundariesKernel,
                _adaptArguments, BoundaryTransferArgumentsOffset);
            // Boundary curves crossing a split can produce additional canonical
            // segments; refresh indirect counts before rebuilding the hash.
            topologyCompute.Dispatch(_buildAdaptArgumentsKernel, 1, 1, 1);
            topologyCompute.DispatchIndirect(_clearBoundaryHashKernel,
                _adaptArguments, BoundaryHashClearArgumentsOffset);
            topologyCompute.DispatchIndirect(_rehashBoundariesKernel,
                _adaptArguments, BoundaryRehashArgumentsOffset);
            topologyCompute.DispatchIndirect(_clearFilmMergeHashKernel,
                _adaptArguments, FilmHashClearArgumentsOffset);
            topologyCompute.DispatchIndirect(_buildFilmMergeHashKernel,
                _adaptArguments, FilmAdaptArgumentsOffset);
            topologyCompute.DispatchIndirect(_mergeFilmsKernel,
                _adaptArguments, FilmAdaptArgumentsOffset);
            topologyCompute.Dispatch(_buildMergeArgumentsKernel, 1, 1, 1);
            topologyCompute.DispatchIndirect(_initializeMergeKernel,
                _adaptArguments, MergeInitializeArgumentsOffset);
        }

        private void DisposeTransientBuffers()
        {
            _cellAccumulator?.Dispose();
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
            _splitRecords?.Dispose();
            _adaptState?.Dispose();
            _adaptArguments?.Dispose();
            _mergeRecords?.Dispose();
            _filmMergeHash?.Dispose();
            _cellAccumulator = null;
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
            _splitRecords = null;
            _adaptState = null;
            _adaptArguments = null;
            _mergeRecords = null;
            _filmMergeHash = null;
        }

        private static Matrix4x4 PoseMatrix(Pose pose) =>
            Matrix4x4.TRS(pose.position, pose.rotation, Vector3.one);

        private static int CeilDiv(int value, int divisor) =>
            Math.Max(1, (value + divisor - 1) / divisor);

        private static int NextPowerOfTwo(int value)
        {
            int result = 1;
            while (result < value && result < 1 << 30) result <<= 1;
            return result;
        }
    }
}
