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
        private const int BehindDispatchOffset =
            (int)ConeEventClass.Behind * sizeof(uint) * 3;
        private const int BaseInitArgumentsOffset = 0;
        private const int CellArgumentsOffset = sizeof(uint) * 3;
        private const int MicroInitArgumentsOffset = sizeof(uint) * 6;
        private const int TopologyArgumentsOffset = sizeof(uint) * 9;
        private const int SplitInitializeArgumentsOffset = 0;
        private const int BoundaryTransferArgumentsOffset = sizeof(uint) * 3;
        private const int BoundaryHashClearArgumentsOffset = sizeof(uint) * 6;
        private const int BoundaryRehashArgumentsOffset = sizeof(uint) * 9;
        private const int SplitActivityArgumentsOffset = sizeof(uint) * 12;
        private const int SplitEvidenceArgumentsOffset = sizeof(uint) * 15;
        private const int SplitCommitArgumentsOffset = sizeof(uint) * 18;
        private const int ManifoldFilmArgumentsOffset = 0;
        private const int ManifoldLinkArgumentsOffset = sizeof(uint) * 3;
        private const int ManifoldSplitArgumentsOffset = sizeof(uint) * 6;
        private const int ManifoldHashArgumentsOffset = sizeof(uint) * 9;
        private const int ManifoldLinkHashArgumentsOffset = sizeof(uint) * 12;
        private const int AccumulatorWordsPerCell =
            ContactDisplacementPool.TransientAccumulatorWordsPerCell;
        private const int AccumulatorWordsPerFilm = 8;

        [SerializeField] private PrismBoundaryGraph boundaryGraph;
        [SerializeField] private PrismFilmSpawner filmSpawner;
        [SerializeField] private ComputeShader displacementCompute;
        [SerializeField] private ComputeShader topologyCompute;
        [SerializeField] private ComputeShader manifoldTopologyCompute;
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
        private static readonly int FilmSlotStatesId =
            Shader.PropertyToID("_FilmSlotStates");
        private static readonly int ActiveFilmIndicesId =
            Shader.PropertyToID("_ActiveFilmIndices");
        private static readonly int DirtyFilmIndicesId =
            Shader.PropertyToID("_DirtyFilmIndices");
        private static readonly int FilmHeadersReadId =
            Shader.PropertyToID("_FilmHeadersRead");
        private static readonly int FilmInformationReadId =
            Shader.PropertyToID("_FilmInformationRead");
        private static readonly int DisplacementPagesId = Shader.PropertyToID("_DisplacementPages");
        private static readonly int DisplacementPagesReadId =
            Shader.PropertyToID("_DisplacementPagesRead");
        private static readonly int BaseCellsId = Shader.PropertyToID("_BaseDisplacementCells");
        private static readonly int MicroCellsId = Shader.PropertyToID("_MicroDisplacementCells");
        private static readonly int BaseCellsReadId =
            Shader.PropertyToID("_BaseDisplacementCellsRead");
        private static readonly int MicroCellsReadId =
            Shader.PropertyToID("_MicroDisplacementCellsRead");
        private static readonly int BaseChildrenId = Shader.PropertyToID("_BaseChildPages");
        private static readonly int MicroChildrenId = Shader.PropertyToID("_MicroChildPages");
        private static readonly int BaseChildrenReadId =
            Shader.PropertyToID("_BaseChildPagesRead");
        private static readonly int MicroChildrenReadId =
            Shader.PropertyToID("_MicroChildPagesRead");
        private static readonly int DisplacementAllocatorId = Shader.PropertyToID("_DisplacementAllocator");
        private static readonly int DisplacementAllocatorWriteId =
            Shader.PropertyToID("_DisplacementAllocatorWrite");
        private static readonly int BaseCellAccumulatorId =
            Shader.PropertyToID("_BaseDisplacementCellAccumulator");
        private static readonly int MicroCellAccumulatorId =
            Shader.PropertyToID("_MicroDisplacementCellAccumulator");
        private static readonly int CellDirtyFlagsId = Shader.PropertyToID("_DisplacementCellDirtyFlags");
        private static readonly int DirtyCellIndicesId = Shader.PropertyToID("_DirtyDisplacementCellIndices");
        private static readonly int DirtyStateId = Shader.PropertyToID("_DisplacementDirtyState");
        private static readonly int NewBasePagesId = Shader.PropertyToID("_NewBasePages");
        private static readonly int NewMicroPagesId = Shader.PropertyToID("_NewMicroPages");
        private static readonly int NewBasePagesReadId =
            Shader.PropertyToID("_NewBasePagesRead");
        private static readonly int NewMicroPagesReadId =
            Shader.PropertyToID("_NewMicroPagesRead");
        private static readonly int PageStateId = Shader.PropertyToID("_DisplacementPageState");
        private static readonly int PageStateReadId =
            Shader.PropertyToID("_DisplacementPageStateRead");
        private static readonly int DirtyCellIndicesReadId =
            Shader.PropertyToID("_DirtyDisplacementCellIndicesRead");
        private static readonly int DirtyStateReadId =
            Shader.PropertyToID("_DisplacementDirtyStateRead");
        private static readonly int DispatchArgumentsId = Shader.PropertyToID("_DisplacementDispatchArguments");
        private static readonly int TopologyEvidenceId = Shader.PropertyToID("_TopologyEvidence");
        private static readonly int TopologyEvidenceReadId =
            Shader.PropertyToID("_TopologyEvidenceRead");
        private static readonly int TopologyAccumulatorId = Shader.PropertyToID("_TopologyAccumulator");
        private static readonly int TopologyDirtyFlagsId = Shader.PropertyToID("_TopologyDirtyFlags");
        private static readonly int DirtyTopologyIndicesId = Shader.PropertyToID("_DirtyTopologyIndices");
        private static readonly int TopologyStateId = Shader.PropertyToID("_TopologyState");
        private static readonly int FilmAllocatorId = Shader.PropertyToID("_FilmAllocator");
        private static readonly int FilmAllocatorWriteId =
            Shader.PropertyToID("_FilmAllocatorWrite");
        private static readonly int BoundaryCapacityId = Shader.PropertyToID("_BoundaryCapacity");
        private static readonly int BoundaryHashMaskId = Shader.PropertyToID("_BoundaryHashMask");
        private static readonly int BoundaryCellsPerAxisId = Shader.PropertyToID("_BoundaryCellsPerAxis");
        private static readonly int BoundaryHeadersId = Shader.PropertyToID("_BoundaryHeaders");
        private static readonly int BoundaryHeadersReadId =
            Shader.PropertyToID("_BoundaryHeadersRead");
        private static readonly int BoundaryInformationId = Shader.PropertyToID("_BoundaryInformation");
        private static readonly int BoundaryInformationReadId =
            Shader.PropertyToID("_BoundaryInformationRead");
        private static readonly int BoundaryHashId = Shader.PropertyToID("_BoundaryHash");
        private static readonly int BoundaryAllocatorId = Shader.PropertyToID("_BoundaryAllocator");
        private static readonly int BoundaryAllocatorWriteId =
            Shader.PropertyToID("_BoundaryAllocatorWrite");
        private static readonly int BoundarySplitPlansReadId =
            Shader.PropertyToID("_BoundarySplitPlansRead");
        private static readonly int BoundarySplitPlansWriteId =
            Shader.PropertyToID("_BoundarySplitPlansWrite");
        private static readonly int SplitRecordsId = Shader.PropertyToID("_SplitRecords");
        private static readonly int SplitRecordsReadId =
            Shader.PropertyToID("_SplitRecordsRead");
        private static readonly int SplitRecordsWriteId =
            Shader.PropertyToID("_SplitRecordsWrite");
        private static readonly int AdaptStateId = Shader.PropertyToID("_AdaptState");
        private static readonly int AdaptStateReadId =
            Shader.PropertyToID("_AdaptStateRead");
        private static readonly int AdaptStateWriteId =
            Shader.PropertyToID("_AdaptStateWrite");
        private static readonly int AdaptArgumentsId = Shader.PropertyToID("_AdaptDispatchArguments");
        private static readonly int MaximumSplitDepthId = Shader.PropertyToID("_MaximumSplitDepth");
        private static readonly int MinimumSplitExtentId = Shader.PropertyToID("_MinimumSplitExtent");
        private static readonly int BimodalSeparationId = Shader.PropertyToID("_BimodalSeparation");
        private static readonly int SplitVarianceId = Shader.PropertyToID("_SplitVariance");
        private static readonly int SplitBoundarySupportId = Shader.PropertyToID("_SplitBoundarySupport");
        private static readonly int ManifoldCapacityId = Shader.PropertyToID("_ManifoldCapacity");
        private static readonly int LinkCapacityId = Shader.PropertyToID("_LinkCapacity");
        private static readonly int FrontierCapacityId = Shader.PropertyToID("_FrontierCapacity");
        private static readonly int LinkHashMaskId = Shader.PropertyToID("_LinkHashMask");
        private static readonly int FilmHashMaskId = Shader.PropertyToID("_FilmHashMask");
        private static readonly int ManifoldHeadersId = Shader.PropertyToID("_ManifoldHeaders");
        private static readonly int ManifoldHeadersReadId =
            Shader.PropertyToID("_ManifoldHeadersRead");
        private static readonly int FilmMembershipsId = Shader.PropertyToID("_FilmMemberships");
        private static readonly int FilmMembershipsReadId =
            Shader.PropertyToID("_FilmMembershipsRead");
        private static readonly int ManifoldLinksId = Shader.PropertyToID("_ManifoldLinks");
        private static readonly int ManifoldLinksReadId =
            Shader.PropertyToID("_ManifoldLinksRead");
        private static readonly int ManifoldLinkIncidencesId =
            Shader.PropertyToID("_ManifoldLinkIncidences");
        private static readonly int ManifoldLinkIncidencesReadId =
            Shader.PropertyToID("_ManifoldLinkIncidencesRead");
        private static readonly int ManifoldFrontierIncidencesId =
            Shader.PropertyToID("_ManifoldFrontierIncidences");
        private static readonly int ManifoldFrontierIncidencesReadId =
            Shader.PropertyToID("_ManifoldFrontierIncidencesRead");
        private static readonly int LatentFrontiersId = Shader.PropertyToID("_LatentFrontiers");
        private static readonly int LatentFrontiersReadId =
            Shader.PropertyToID("_LatentFrontiersRead");
        private static readonly int ManifoldAllocatorId = Shader.PropertyToID("_ManifoldAllocator");
        private static readonly int CurrentManifoldId = Shader.PropertyToID("_CurrentManifold");
        private static readonly int ManifoldDiagnosticsId = Shader.PropertyToID("_ManifoldDiagnostics");
        private static readonly int LinkHashId = Shader.PropertyToID("_LinkHash");
        private static readonly int FilmHashHeadsId = Shader.PropertyToID("_FilmHashHeads");
        private static readonly int FilmHashEntriesId = Shader.PropertyToID("_FilmHashEntries");
        private static readonly int FilmHashHeadsReadId =
            Shader.PropertyToID("_FilmHashHeadsRead");
        private static readonly int FilmHashEntriesReadId =
            Shader.PropertyToID("_FilmHashEntriesRead");
        private static readonly int ManifoldDispatchArgumentsId =
            Shader.PropertyToID("_ManifoldDispatchArguments");

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
        private GraphicsBuffer _boundarySplitPlans;
        private GraphicsBuffer _pageState;
        private GraphicsBuffer _dispatchArguments;
        private GraphicsBuffer _topologyAccumulator;
        private GraphicsBuffer _topologyDirtyFlags;
        private GraphicsBuffer _dirtyTopologyIndices;
        private GraphicsBuffer _topologyState;
        private GraphicsBuffer _splitRecords;
        private GraphicsBuffer _adaptState;
        private GraphicsBuffer _adaptArguments;
        private GraphicsBuffer _manifoldFilmHashHeads;
        private GraphicsBuffer _manifoldFilmHashEntries;
        private GraphicsBuffer _manifoldDispatchArguments;
        private int _clearKernel = -1;
        private int _initializeStateKernel = -1;
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
        private int _solveTopologyKernel = -1;
        private int _clearAdaptKernel = -1;
        private int _splitKernel = -1;
        private int _publishSplitActivityKernel = -1;
        private int _initializeSplitEvidenceKernel = -1;
        private int _buildAdaptArgumentsKernel = -1;
        private int _initializeSplitKernel = -1;
        private int _transferBoundariesKernel = -1;
        private int _clearBoundaryHashKernel = -1;
        private int _rehashBoundariesKernel = -1;
        private int _initializeManifoldKernel = -1;
        private int _planSplitTransactionsKernel = -1;
        private int _buildManifoldArgumentsKernel = -1;
        private int _remapSplitMembershipsKernel = -1;
        private int _clearCanonicalLinkHashKernel = -1;
        private int _buildCanonicalLinkHashKernel = -1;
        private int _linkSplitChildrenKernel = -1;
        private int _releaseSplitParentsKernel = -1;
        private int _clearManifoldFilmHashKernel = -1;
        private int _buildManifoldFilmHashKernel = -1;
        private int _proveContinuationLinksKernel = -1;
        private int _clearManifoldValidationKernel = -1;
        private int _validateMembershipsKernel = -1;
        private int _validateLinksKernel = -1;
        private int _finalizeManifoldValidationKernel = -1;
        private bool _running;
        private bool _subscribedToSource;
        private bool _initialized;
        private bool _manifoldInitialized;
        private long _processedFrames;

        public event Action<ConeEventFrameLease> DisplacementUpdated;
        public ContactDisplacementPool DisplacementPool => _pool;
        public int MaximumMicroLevels => maximumMicroLevels;
        public long ProcessedFrames => _processedFrames;

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
            topologyCompute ??= Resources.Load<ComputeShader>("Prism/TopologyAdapt");
            manifoldTopologyCompute ??=
                Resources.Load<ComputeShader>("Prism/PressureManifoldTopology");
            if (boundaryGraph?.BoundaryPool == null ||
                filmSpawner?.FilmPool == null ||
                displacementCompute == null || topologyCompute == null ||
                manifoldTopologyCompute == null)
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
            _allocateBehindBaseKernel =
                displacementCompute.FindKernel("AllocateBasePagesBehind");
            _allocateOccluderBaseKernel =
                displacementCompute.FindKernel("AllocateBasePagesOccluder");
            _buildArgumentsKernel = displacementCompute.FindKernel("BuildDisplacementArguments");
            _initializeBaseKernel = displacementCompute.FindKernel("InitializeBasePages");
            _accumulateKernel = displacementCompute.FindKernel("AccumulateDisplacement");
            _accumulateTopologyKernel =
                displacementCompute.FindKernel("AccumulateTopologyEvidence");
            _accumulatePressureKernel =
                displacementCompute.FindKernel("AccumulatePreHitPressure");
            _accumulateOccluderPressureKernel = displacementCompute.FindKernel(
                "AccumulateOccluderPreHitPressure");
            _solveKernel = displacementCompute.FindKernel("SolveDirtyDisplacement");
            _allocateMicroKernel = displacementCompute.FindKernel("AllocateMicrotiles");
            _initializeMicroKernel = displacementCompute.FindKernel("InitializeMicroPages");
            _solveTopologyKernel = displacementCompute.FindKernel("SolveTopologyEvidence");
            _clearAdaptKernel = topologyCompute.FindKernel("ClearTopologyAdaptFrame");
            _splitKernel = topologyCompute.FindKernel("SplitContactFilms");
            _publishSplitActivityKernel =
                topologyCompute.FindKernel("PublishSplitActivity");
            _initializeSplitEvidenceKernel =
                topologyCompute.FindKernel("InitializeSplitEvidence");
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
            _initializeManifoldKernel =
                manifoldTopologyCompute.FindKernel("InitializeManifoldTopology");
            _planSplitTransactionsKernel =
                manifoldTopologyCompute.FindKernel("PlanSplitTransactions");
            _buildManifoldArgumentsKernel = manifoldTopologyCompute.FindKernel(
                "BuildManifoldDispatchArguments");
            _remapSplitMembershipsKernel =
                manifoldTopologyCompute.FindKernel("RemapSplitMemberships");
            _clearCanonicalLinkHashKernel =
                manifoldTopologyCompute.FindKernel("ClearCanonicalLinkHash");
            _buildCanonicalLinkHashKernel =
                manifoldTopologyCompute.FindKernel("BuildCanonicalLinkHash");
            _linkSplitChildrenKernel =
                manifoldTopologyCompute.FindKernel("LinkSplitChildren");
            _releaseSplitParentsKernel =
                manifoldTopologyCompute.FindKernel("ReleaseSplitParents");
            _clearManifoldFilmHashKernel =
                manifoldTopologyCompute.FindKernel("ClearFilmContinuationHash");
            _buildManifoldFilmHashKernel =
                manifoldTopologyCompute.FindKernel("BuildFilmContinuationHash");
            _proveContinuationLinksKernel = manifoldTopologyCompute.FindKernel(
                "ProveMeasuredContinuationLinks");
            _clearManifoldValidationKernel =
                manifoldTopologyCompute.FindKernel("ClearManifoldValidation");
            _validateMembershipsKernel =
                manifoldTopologyCompute.FindKernel("ValidateFilmMemberships");
            _validateLinksKernel =
                manifoldTopologyCompute.FindKernel("ValidateManifoldLinks");
            _finalizeManifoldValidationKernel = manifoldTopologyCompute.FindKernel(
                "FinalizeManifoldValidation");
            BindPersistent();
            BindTopology();
            BindManifoldTopology();
            if (!_initialized)
            {
                displacementCompute.Dispatch(_initializeStateKernel,
                    CeilDiv(_pool.FilmCapacity, 64), 1, 1);
                _initialized = true;
            }
            if (!_manifoldInitialized)
            {
                int clearCount = Math.Max(filmSpawner.FilmPool.Manifolds.LinkHashCapacity,
                    Math.Max(_manifoldFilmHashHeads.count,
                        _manifoldFilmHashEntries.count));
                manifoldTopologyCompute.Dispatch(_initializeManifoldKernel,
                    CeilDiv(clearCount, 64), 1, 1);
                _manifoldInitialized = true;
            }
            if (subscribeToSource)
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
                displacementCompute.DispatchIndirect(_solveTopologyKernel,
                    _dispatchArguments, TopologyArgumentsOffset);
                RunTopologyAdaptation();
                _processedFrames++;
                if (DisplacementUpdated != null) DisplacementUpdated.Invoke(frame);
                else filmSpawner.NotifyFilmsMutated();
                return true;
            }
            catch (Exception exception)
            {
                Logger.Error($"Cone-PRISM displacement update failed: {exception.Message}");
                filmSpawner.NotifyFilmsMutated();
                return false;
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
            int[] eventKernels =
            {
                _allocateBaseKernel, _allocateBehindBaseKernel,
                _allocateOccluderBaseKernel, _accumulateKernel,
                _accumulateTopologyKernel,
                _accumulatePressureKernel, _accumulateOccluderPressureKernel
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
                _accumulatePressureKernel,
                _accumulateOccluderPressureKernel
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
            // A combined default arena is 138,412,032 bytes, above Quest's
            // maxStorageBufferRange (128 MiB). Keep the same number of cells and
            // all eleven information words, but expose the already distinct base
            // and micro address spaces as separate bindings.
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
            _boundarySplitPlans = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                boundaryGraph.BoundaryPool.Capacity,
                TopologyBoundarySplitPlanGpu.Stride);
            _adaptState = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                8, sizeof(uint));
            _adaptArguments = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured |
                GraphicsBuffer.Target.IndirectArguments, 8, sizeof(uint) * 3);
            int continuationHashCapacity = NextPowerOfTwo(pool.FilmCapacity * 2);
            _manifoldFilmHashHeads = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, continuationHashCapacity,
                sizeof(uint));
            _manifoldFilmHashEntries = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, pool.FilmCapacity,
                sizeof(uint) * 4);
            _manifoldDispatchArguments = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured |
                GraphicsBuffer.Target.IndirectArguments, 5, sizeof(uint) * 3);
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
                _allocateBehindBaseKernel, _allocateOccluderBaseKernel,
                _buildArgumentsKernel,
                _initializeBaseKernel, _accumulateKernel,
                _accumulateTopologyKernel,
                _accumulatePressureKernel, _accumulateOccluderPressureKernel,
                _solveKernel,
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
            int[] readLeafKernels =
            {
                _accumulateKernel, _accumulateTopologyKernel
            };
            foreach (int kernel in readLeafKernels)
            {
                displacementCompute.SetBuffer(kernel, BaseChildrenReadId,
                    _pool.BaseChildPages);
                displacementCompute.SetBuffer(kernel, MicroChildrenReadId,
                    _pool.MicroChildPages);
            }
            displacementCompute.SetBuffer(_initializeBaseKernel,
                FilmHeadersReadId, films.Headers);
            displacementCompute.SetBuffer(_initializeBaseKernel,
                FilmInformationReadId, films.Information);
            displacementCompute.SetBuffer(_initializeBaseKernel,
                NewBasePagesReadId, _newBasePages);
            displacementCompute.SetBuffer(_initializeBaseKernel,
                PageStateReadId, _pageState);

            displacementCompute.SetBuffer(_accumulateTopologyKernel,
                FilmHeadersReadId, films.Headers);
            displacementCompute.SetBuffer(_allocateMicroKernel,
                FilmHeadersReadId, films.Headers);
            displacementCompute.SetBuffer(_allocateMicroKernel,
                DirtyCellIndicesReadId, _dirtyCellIndices);
            displacementCompute.SetBuffer(_allocateMicroKernel,
                DirtyStateReadId, _dirtyState);

            int[] pressureKernels =
            {
                _accumulatePressureKernel, _accumulateOccluderPressureKernel
            };
            foreach (int kernel in pressureKernels)
                displacementCompute.SetBuffer(kernel, FilmInformationReadId,
                    films.Information);

            displacementCompute.SetBuffer(_initializeMicroKernel,
                FilmHeadersReadId, films.Headers);
            displacementCompute.SetBuffer(_initializeMicroKernel,
                DisplacementPagesReadId, _pool.PageHeaders);
            displacementCompute.SetBuffer(_initializeMicroKernel,
                BaseCellsReadId, _pool.BaseCells);
            displacementCompute.SetBuffer(_initializeMicroKernel,
                NewMicroPagesReadId, _newMicroPages);
            displacementCompute.SetBuffer(_initializeMicroKernel,
                PageStateReadId, _pageState);
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
            int[] kernels =
            {
                _clearAdaptKernel, _splitKernel, _publishSplitActivityKernel,
                _initializeSplitEvidenceKernel, _buildAdaptArgumentsKernel,
                _initializeSplitKernel, _transferBoundariesKernel,
                _clearBoundaryHashKernel, _rehashBoundariesKernel
            };
            foreach (int kernel in kernels)
            {
                topologyCompute.SetBuffer(kernel, FilmHeadersId, films.Headers);
                topologyCompute.SetBuffer(kernel, FilmInformationId,
                    films.Information);
                topologyCompute.SetBuffer(kernel, FilmAllocatorId,
                    films.Allocator);
                topologyCompute.SetBuffer(kernel, FilmSlotStatesId,
                    films.SlotStates);
                topologyCompute.SetBuffer(kernel, ActiveFilmIndicesId,
                    films.ActiveIndices);
                topologyCompute.SetBuffer(kernel, DirtyFilmIndicesId,
                    films.DirtyIndices);
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
                topologyCompute.SetBuffer(kernel, BaseCellAccumulatorId,
                    _baseCellAccumulator);
                topologyCompute.SetBuffer(kernel, CellDirtyFlagsId,
                    _cellDirtyFlags);
                topologyCompute.SetBuffer(kernel, TopologyEvidenceId,
                    _pool.TopologyEvidence);
                topologyCompute.SetBuffer(kernel, TopologyEvidenceReadId,
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
                topologyCompute.SetBuffer(kernel, AdaptStateId, _adaptState);
                topologyCompute.SetBuffer(kernel, AdaptArgumentsId,
                    _adaptArguments);
            }

            topologyCompute.SetBuffer(_splitKernel, BaseCellsReadId,
                _pool.BaseCells);

            int[] adaptStateReaders =
            {
                _buildAdaptArgumentsKernel, _initializeSplitKernel,
                _publishSplitActivityKernel, _initializeSplitEvidenceKernel
            };
            foreach (int kernel in adaptStateReaders)
                topologyCompute.SetBuffer(kernel, AdaptStateReadId, _adaptState);

            topologyCompute.SetBuffer(_initializeSplitKernel, FilmHeadersReadId,
                films.Headers);
            topologyCompute.SetBuffer(_publishSplitActivityKernel,
                FilmHeadersReadId, films.Headers);
            topologyCompute.SetBuffer(_initializeSplitEvidenceKernel,
                FilmHeadersReadId, films.Headers);
            topologyCompute.SetBuffer(_initializeSplitKernel, MicroCellsReadId,
                _pool.MicroCells);
            topologyCompute.SetBuffer(_initializeSplitKernel,
                MicroChildrenReadId, _pool.MicroChildPages);
            topologyCompute.SetBuffer(_initializeSplitKernel, SplitRecordsReadId,
                _splitRecords);
            topologyCompute.SetBuffer(_transferBoundariesKernel,
                SplitRecordsReadId, _splitRecords);
            topologyCompute.SetBuffer(_transferBoundariesKernel,
                BoundarySplitPlansReadId, _boundarySplitPlans);
        }

        private void BindManifoldTopology()
        {
            ContactFilmPool films = filmSpawner.FilmPool;
            PressureManifoldPool manifolds = films.Manifolds;
            manifoldTopologyCompute.SetInt(FilmCapacityId, films.Capacity);
            manifoldTopologyCompute.SetInt(ManifoldCapacityId,
                manifolds.ManifoldCapacity);
            manifoldTopologyCompute.SetInt(LinkCapacityId, manifolds.LinkCapacity);
            manifoldTopologyCompute.SetInt(FrontierCapacityId,
                manifolds.FrontierCapacity);
            manifoldTopologyCompute.SetInt(BoundaryCapacityId,
                boundaryGraph.BoundaryPool.Capacity);
            manifoldTopologyCompute.SetInt(LinkHashMaskId,
                manifolds.LinkHashCapacity - 1);
            manifoldTopologyCompute.SetInt(FilmHashMaskId,
                _manifoldFilmHashHeads.count - 1);
            manifoldTopologyCompute.SetInt(BasePageCapacityId,
                _pool.BasePageCapacity);
            manifoldTopologyCompute.SetInt(MaximumSplitDepthId,
                maximumSplitDepth);
            manifoldTopologyCompute.SetFloat(MinimumSplitExtentId,
                minimumSplitExtent);
            manifoldTopologyCompute.SetFloat(BimodalSeparationId,
                bimodalSeparation);
            manifoldTopologyCompute.SetFloat(SplitVarianceId, splitVariance);
            manifoldTopologyCompute.SetFloat(SplitBoundarySupportId,
                splitBoundarySupport);
            int[] kernels =
            {
                _initializeManifoldKernel, _planSplitTransactionsKernel,
                _buildManifoldArgumentsKernel,
                _remapSplitMembershipsKernel, _clearCanonicalLinkHashKernel,
                _buildCanonicalLinkHashKernel, _linkSplitChildrenKernel,
                _releaseSplitParentsKernel,
                _clearManifoldFilmHashKernel,
                _buildManifoldFilmHashKernel, _proveContinuationLinksKernel,
                _clearManifoldValidationKernel, _validateMembershipsKernel,
                _validateLinksKernel, _finalizeManifoldValidationKernel
            };
            foreach (int kernel in kernels)
            {
                manifoldTopologyCompute.SetBuffer(kernel, FilmHeadersId,
                    films.Headers);
                manifoldTopologyCompute.SetBuffer(kernel, FilmHeadersReadId,
                    films.Headers);
                manifoldTopologyCompute.SetBuffer(kernel, FilmAllocatorId,
                    films.Allocator);
                manifoldTopologyCompute.SetBuffer(kernel, ActiveFilmIndicesId,
                    films.ActiveIndices);
                manifoldTopologyCompute.SetBuffer(kernel, FilmAllocatorWriteId,
                    films.Allocator);
                manifoldTopologyCompute.SetBuffer(kernel, FilmSlotStatesId,
                    films.SlotStates);
                manifoldTopologyCompute.SetBuffer(kernel, ManifoldHeadersId,
                    manifolds.Headers);
                manifoldTopologyCompute.SetBuffer(kernel, ManifoldHeadersReadId,
                    manifolds.Headers);
                manifoldTopologyCompute.SetBuffer(kernel, FilmMembershipsId,
                    manifolds.Memberships);
                manifoldTopologyCompute.SetBuffer(kernel, ManifoldLinksId,
                    manifolds.Links);
                manifoldTopologyCompute.SetBuffer(kernel,
                    ManifoldLinkIncidencesId, manifolds.LinkIncidences);
                manifoldTopologyCompute.SetBuffer(kernel,
                    ManifoldFrontierIncidencesId,
                    manifolds.FrontierIncidences);
                manifoldTopologyCompute.SetBuffer(kernel, LatentFrontiersId,
                    manifolds.Frontiers);
                manifoldTopologyCompute.SetBuffer(kernel, ManifoldAllocatorId,
                    manifolds.Allocator);
                manifoldTopologyCompute.SetBuffer(kernel, CurrentManifoldId,
                    manifolds.Current);
                manifoldTopologyCompute.SetBuffer(kernel, ManifoldDiagnosticsId,
                    manifolds.Diagnostics);
                manifoldTopologyCompute.SetBuffer(kernel, LinkHashId,
                    manifolds.LinkHash);
                manifoldTopologyCompute.SetBuffer(kernel, FilmHashHeadsId,
                    _manifoldFilmHashHeads);
                manifoldTopologyCompute.SetBuffer(kernel, FilmHashEntriesId,
                    _manifoldFilmHashEntries);
                manifoldTopologyCompute.SetBuffer(kernel, FilmHashHeadsReadId,
                    _manifoldFilmHashHeads);
                manifoldTopologyCompute.SetBuffer(kernel, FilmHashEntriesReadId,
                    _manifoldFilmHashEntries);
                manifoldTopologyCompute.SetBuffer(kernel, SplitRecordsId,
                    _splitRecords);
                manifoldTopologyCompute.SetBuffer(kernel, AdaptStateId,
                    _adaptState);
                manifoldTopologyCompute.SetBuffer(kernel,
                    ManifoldDispatchArgumentsId, _manifoldDispatchArguments);
            }

            manifoldTopologyCompute.SetBuffer(_planSplitTransactionsKernel,
                FilmMembershipsReadId, manifolds.Memberships);
            manifoldTopologyCompute.SetBuffer(_planSplitTransactionsKernel,
                ManifoldLinksReadId, manifolds.Links);
            manifoldTopologyCompute.SetBuffer(_planSplitTransactionsKernel,
                ManifoldLinkIncidencesReadId, manifolds.LinkIncidences);
            manifoldTopologyCompute.SetBuffer(_planSplitTransactionsKernel,
                ManifoldFrontierIncidencesReadId,
                manifolds.FrontierIncidences);
            manifoldTopologyCompute.SetBuffer(_planSplitTransactionsKernel,
                LatentFrontiersReadId, manifolds.Frontiers);
            manifoldTopologyCompute.SetBuffer(_planSplitTransactionsKernel,
                SplitRecordsWriteId, _splitRecords);
            manifoldTopologyCompute.SetBuffer(_planSplitTransactionsKernel,
                AdaptStateWriteId, _adaptState);
            manifoldTopologyCompute.SetBuffer(_planSplitTransactionsKernel,
                TopologyEvidenceReadId, _pool.TopologyEvidence);
            manifoldTopologyCompute.SetBuffer(_planSplitTransactionsKernel,
                DirtyTopologyIndicesId, _dirtyTopologyIndices);
            manifoldTopologyCompute.SetBuffer(_planSplitTransactionsKernel,
                TopologyStateId, _topologyState);
            manifoldTopologyCompute.SetBuffer(_planSplitTransactionsKernel,
                DisplacementAllocatorWriteId, _pool.Allocator);
            manifoldTopologyCompute.SetBuffer(_planSplitTransactionsKernel,
                BoundaryHeadersReadId, boundaryGraph.BoundaryPool.Headers);
            manifoldTopologyCompute.SetBuffer(_planSplitTransactionsKernel,
                BoundaryInformationReadId,
                boundaryGraph.BoundaryPool.Information);
            manifoldTopologyCompute.SetBuffer(_planSplitTransactionsKernel,
                BoundaryAllocatorWriteId,
                boundaryGraph.BoundaryPool.Allocator);
            manifoldTopologyCompute.SetBuffer(_planSplitTransactionsKernel,
                BoundarySplitPlansWriteId, _boundarySplitPlans);
        }

        private void RunTopologyAdaptation()
        {
            topologyCompute.Dispatch(_clearAdaptKernel, 1, 1, 1);
            manifoldTopologyCompute.Dispatch(_planSplitTransactionsKernel, 1, 1, 1);
            topologyCompute.Dispatch(_buildAdaptArgumentsKernel, 1, 1, 1);
            topologyCompute.DispatchIndirect(_splitKernel, _adaptArguments,
                SplitCommitArgumentsOffset);
            topologyCompute.DispatchIndirect(_publishSplitActivityKernel,
                _adaptArguments, SplitActivityArgumentsOffset);
            topologyCompute.DispatchIndirect(_initializeSplitEvidenceKernel,
                _adaptArguments, SplitEvidenceArgumentsOffset);
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
            RunPressureManifoldTopology();
        }

        private void RunPressureManifoldTopology()
        {
            manifoldTopologyCompute.Dispatch(_buildManifoldArgumentsKernel, 1, 1, 1);
            manifoldTopologyCompute.DispatchIndirect(_remapSplitMembershipsKernel,
                _manifoldDispatchArguments, ManifoldSplitArgumentsOffset);
            manifoldTopologyCompute.DispatchIndirect(_linkSplitChildrenKernel,
                _manifoldDispatchArguments, ManifoldSplitArgumentsOffset);
            manifoldTopologyCompute.DispatchIndirect(_releaseSplitParentsKernel,
                _manifoldDispatchArguments, ManifoldSplitArgumentsOffset);
            manifoldTopologyCompute.Dispatch(_buildManifoldArgumentsKernel, 1, 1, 1);
            manifoldTopologyCompute.DispatchIndirect(_clearCanonicalLinkHashKernel,
                _manifoldDispatchArguments, ManifoldLinkHashArgumentsOffset);
            manifoldTopologyCompute.DispatchIndirect(_buildCanonicalLinkHashKernel,
                _manifoldDispatchArguments, ManifoldLinkArgumentsOffset);
            manifoldTopologyCompute.DispatchIndirect(_clearManifoldFilmHashKernel,
                _manifoldDispatchArguments, ManifoldHashArgumentsOffset);
            manifoldTopologyCompute.DispatchIndirect(_buildManifoldFilmHashKernel,
                _manifoldDispatchArguments, ManifoldFilmArgumentsOffset);
            manifoldTopologyCompute.DispatchIndirect(_proveContinuationLinksKernel,
                _manifoldDispatchArguments, ManifoldFilmArgumentsOffset);
            // Continuation proof can append links. Rebuild indirect domains before
            // validating so a just-created endpoint cannot escape this generation's
            // publication gate.
            manifoldTopologyCompute.Dispatch(_buildManifoldArgumentsKernel, 1, 1, 1);
            manifoldTopologyCompute.Dispatch(_clearManifoldValidationKernel, 1, 1, 1);
            manifoldTopologyCompute.DispatchIndirect(_validateMembershipsKernel,
                _manifoldDispatchArguments, ManifoldFilmArgumentsOffset);
            manifoldTopologyCompute.DispatchIndirect(_validateLinksKernel,
                _manifoldDispatchArguments, ManifoldLinkArgumentsOffset);
            manifoldTopologyCompute.Dispatch(_finalizeManifoldValidationKernel,
                1, 1, 1);
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
            _splitRecords?.Dispose();
            _boundarySplitPlans?.Dispose();
            _adaptState?.Dispose();
            _adaptArguments?.Dispose();
            _manifoldFilmHashHeads?.Dispose();
            _manifoldFilmHashEntries?.Dispose();
            _manifoldDispatchArguments?.Dispose();
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
            _splitRecords = null;
            _boundarySplitPlans = null;
            _adaptState = null;
            _adaptArguments = null;
            _manifoldFilmHashHeads = null;
            _manifoldFilmHashEntries = null;
            _manifoldDispatchArguments = null;
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
