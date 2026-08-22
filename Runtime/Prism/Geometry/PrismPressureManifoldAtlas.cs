using System;
using UnityEngine;

namespace Genesis.RoomScan.Prism
{
    /// <summary>
    /// Q3-15.6 canonical topology owner. ContactFilm rectangles are numerical chart
    /// domains only; this stage extracts measured support contours, welds proven
    /// continuation half-edges and orders the remaining arcs into manifold-level
    /// FrontierLoops. No CPU readback participates in live topology.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(20)]
    public sealed class PrismPressureManifoldAtlas : MonoBehaviour
    {
        [SerializeField] private PrismFilmSpawner filmSpawner;
        [SerializeField] private PrismDisplacementTopology displacementTopology;
        [SerializeField] private PrismBoundaryGraph boundaryGraph;
        [SerializeField] private PrismEvidenceAlignedSplitter evidenceSplitter;
        [SerializeField] private ComputeShader supportContourCompute;
        [SerializeField] private ComputeShader halfEdgeCompute;
        [SerializeField] private ComputeShader boundaryCurveCompute;
        [SerializeField] private ComputeShader elasticIslandCompute;
        [SerializeField] private ComputeShader crossChunkPortalCompute;
        [SerializeField, Range(0.1f, 0.9f)] private float coverageThreshold = 0.5f;
        [SerializeField, Min(0.0001f)] private float weldPositionFloor = 0.001f;
        [SerializeField, Range(0f, 25f)] private float smoothNormalDegrees = 5f;
        [SerializeField, Range(1f, 60f)] private float creaseNormalDegrees = 25f;
        [SerializeField, Range(1, 8)] private int elasticIterations = 4;
        [SerializeField, Range(1, 4)] private int topologyBatchFrames = 2;
        [SerializeField, Min(0.00001f)] private float elasticStiffnessScale = 0.025f;
        [SerializeField, Min(0.00001f)] private float maximumElasticStep = 0.0025f;
        [SerializeField, Min(0.000001f)] private float elasticConvergenceFloor =
            0.00001f;

        private static readonly int FilmCapacityId = Shader.PropertyToID("_FilmCapacity");
        private static readonly int BasePageCapacityId =
            Shader.PropertyToID("_BasePageCapacity");
        private static readonly int ContourPageCapacityId =
            Shader.PropertyToID("_ContourPageCapacity");
        private static readonly int ContourSegmentCapacityId =
            Shader.PropertyToID("_ContourSegmentCapacity");
        private static readonly int HalfEdgeCapacityId =
            Shader.PropertyToID("_HalfEdgeCapacity");
        private static readonly int HalfEdgeHashCapacityId =
            Shader.PropertyToID("_HalfEdgeHashCapacity");
        private static readonly int ManifoldCapacityId =
            Shader.PropertyToID("_ManifoldCapacity");
        private static readonly int CoverageThresholdId =
            Shader.PropertyToID("_CoverageThreshold");
        private static readonly int WeldPositionFloorId =
            Shader.PropertyToID("_WeldPositionFloor");
        private static readonly int SmoothNormalCosineId =
            Shader.PropertyToID("_SmoothNormalCosine");
        private static readonly int CreaseNormalCosineId =
            Shader.PropertyToID("_CreaseNormalCosine");
        private static readonly int FilmHeadersId = Shader.PropertyToID("_FilmHeaders");
        private static readonly int FilmInformationId =
            Shader.PropertyToID("_FilmInformation");
        private static readonly int FilmAllocatorId =
            Shader.PropertyToID("_FilmAllocator");
        private static readonly int DirtyFilmIndicesId =
            Shader.PropertyToID("_DirtyFilmIndices");
        private static readonly int DirtyTopologyFilmsId =
            Shader.PropertyToID("_DirtyTopologyFilms");
        private static readonly int TopologyDirtyFlagsId =
            Shader.PropertyToID("_TopologyDirtyFlags");
        private static readonly int ActiveFilmIndicesId =
            Shader.PropertyToID("_ActiveFilmIndices");
        private static readonly int FilmSlotStatesId =
            Shader.PropertyToID("_FilmSlotStates");
        private static readonly int DisplacementPagesId =
            Shader.PropertyToID("_DisplacementPages");
        private static readonly int BaseDisplacementCellsId =
            Shader.PropertyToID("_BaseDisplacementCells");
        private static readonly int MicroDisplacementCellsId =
            Shader.PropertyToID("_MicroDisplacementCells");
        private static readonly int BaseChildPagesId =
            Shader.PropertyToID("_BaseChildPages");
        private static readonly int MicroChildPagesId =
            Shader.PropertyToID("_MicroChildPages");
        private static readonly int MicroPageCapacityId =
            Shader.PropertyToID("_MicroPageCapacity");
        private static readonly int BaseCellCapacityId =
            Shader.PropertyToID("_BaseCellCapacity");
        private static readonly int MaximumMicroLevelsId =
            Shader.PropertyToID("_MaximumMicroLevels");
        private static readonly int HasDisplacementId =
            Shader.PropertyToID("_HasDisplacement");
        private static readonly int ManifoldHeadersId =
            Shader.PropertyToID("_ManifoldHeaders");
        private static readonly int FilmMembershipsId =
            Shader.PropertyToID("_FilmMemberships");
        private static readonly int SupportContourPagesId =
            Shader.PropertyToID("_SupportContourPages");
        private static readonly int SupportContoursId =
            Shader.PropertyToID("_SupportContours");
        private static readonly int FilmTopologyRangesId =
            Shader.PropertyToID("_FilmTopologyRanges");
        private static readonly int ContourPlansId = Shader.PropertyToID("_ContourPlans");
        private static readonly int HalfEdgesId = Shader.PropertyToID("_HalfEdges");
        private static readonly int ContinuationEvidenceId =
            Shader.PropertyToID("_ContinuationEvidence");
        private static readonly int HalfEdgeHashHeadsId =
            Shader.PropertyToID("_HalfEdgeHashHeads");
        private static readonly int HalfEdgeHashNextId =
            Shader.PropertyToID("_HalfEdgeHashNext");
        private static readonly int HalfEdgeHashKeysId =
            Shader.PropertyToID("_HalfEdgeHashKeys");
        private static readonly int EndpointHashHeadsId =
            Shader.PropertyToID("_EndpointHashHeads");
        private static readonly int EndpointHashEntriesId =
            Shader.PropertyToID("_EndpointHashEntries");
        private static readonly int HalfEdgeLoopParentsId =
            Shader.PropertyToID("_HalfEdgeLoopParents");
        private static readonly int HalfEdgeLoopIdsId =
            Shader.PropertyToID("_HalfEdgeLoopIds");
        private static readonly int FrontierLoopsId =
            Shader.PropertyToID("_FrontierLoops");
        private static readonly int FrontierLoopMomentsId =
            Shader.PropertyToID("_FrontierLoopMoments");
        private static readonly int AtlasAllocatorId =
            Shader.PropertyToID("_AtlasAllocator");
        private static readonly int AtlasDispatchArgumentsId =
            Shader.PropertyToID("_AtlasDispatchArguments");
        private static readonly int ManifoldDiagnosticsId =
            Shader.PropertyToID("_ManifoldDiagnostics");
        private static readonly int BoundaryCapacityId =
            Shader.PropertyToID("_BoundaryCapacity");
        private static readonly int BoundaryHashMaskId =
            Shader.PropertyToID("_BoundaryHashMask");
        private static readonly int BoundaryCellsPerAxisId =
            Shader.PropertyToID("_BoundaryCellsPerAxis");
        private static readonly int BoundaryAttachFloorId =
            Shader.PropertyToID("_BoundaryAttachFloor");
        private static readonly int FilmHeadersReadId =
            Shader.PropertyToID("_FilmHeadersRead");
        private static readonly int BoundaryHeadersId =
            Shader.PropertyToID("_BoundaryHeaders");
        private static readonly int BoundaryInformationId =
            Shader.PropertyToID("_BoundaryInformation");
        private static readonly int BoundaryAllocatorId =
            Shader.PropertyToID("_BoundaryAllocator");
        private static readonly int HalfEdgeBoundaryClaimsId =
            Shader.PropertyToID("_HalfEdgeBoundaryClaims");
        private static readonly int HalfEdgeBoundaryClaimsReadId =
            Shader.PropertyToID("_HalfEdgeBoundaryClaimsRead");
        private static readonly int BoundaryCurveTopologyId =
            Shader.PropertyToID("_BoundaryCurveTopology");
        private static readonly int BoundaryCurveCacheId =
            Shader.PropertyToID("_BoundaryCurveCache");
        private static readonly int BoundaryHashId =
            Shader.PropertyToID("_BoundaryHash");
        private static readonly int ElasticChartStatesId =
            Shader.PropertyToID("_ElasticChartStates");
        private static readonly int ElasticGradientsId =
            Shader.PropertyToID("_ElasticGradients");
        private static readonly int ElasticDiagonalsId =
            Shader.PropertyToID("_ElasticDiagonals");
        private static readonly int ElasticDispatchArgumentsId =
            Shader.PropertyToID("_ElasticDispatchArguments");
        private static readonly int ElasticStiffnessScaleId =
            Shader.PropertyToID("_ElasticStiffnessScale");
        private static readonly int MaximumElasticStepId =
            Shader.PropertyToID("_MaximumElasticStep");
        private static readonly int ElasticConvergenceFloorId =
            Shader.PropertyToID("_ElasticConvergenceFloor");
        private static readonly int CrossChunkPortalsId =
            Shader.PropertyToID("_CrossChunkPortals");
        private static readonly int PortalCapacityId =
            Shader.PropertyToID("_PortalCapacity");
        private static readonly int CurrentChunkId =
            Shader.PropertyToID("_CurrentChunkId");
        private static readonly int PortalDispatchArgumentsId =
            Shader.PropertyToID("_PortalDispatchArguments");

        private int _initializeContours = -1;
        private int _buildFilmDirtyImportArguments = -1;
        private int _importFilmDirtyTopology = -1;
        private int _buildFilmDirtyTailArguments = -1;
        private int _importFilmDirtyTopologyTail = -1;
        private int _buildContourArguments = -1;
        private int _countContours = -1;
        private int _preflightContourPages = -1;
        private int _commitContourPages = -1;
        private int _writeContours = -1;
        private int _finalizeTopologyDirtyFlags = -1;
        private int _resetTopologyDirtyQueue = -1;
        private int _queueElasticTopologyChanges = -1;
        private int _resetRestoredTopologyTransient = -1;
        private int _buildHalfEdgeArguments = -1;
        private int _clearHalfEdgeHashState = -1;
        private int _clearHalfEdgeState = -1;
        private int _materializeHalfEdges = -1;
        private int _buildHalfEdgeHash = -1;
        private int _proveTwins = -1;
        private int _buildEndpointHash = -1;
        private int _orderOuterEdges = -1;
        private int _initializeFrontierComponents = -1;
        private int _hookFrontierComponents = -1;
        private int _shortcutFrontierComponents = -1;
        private int _createFrontierLoops = -1;
        private int _assignFrontierLoops = -1;
        private int _finalizeFrontierLoops = -1;
        private int _clearBoundaryClaims = -1;
        private int _claimBoundaryHalfEdges = -1;
        private int _commitBoundaryCurves = -1;
        private int _buildElasticArguments = -1;
        private int _initializeElasticStates = -1;
        private int _clearElasticAccumulators = -1;
        private int _accumulateElasticConstraints = -1;
        private int _accumulatePortalConstraints = -1;
        private int _solveElasticCorrections = -1;
        private int _buildPortalReconcileArguments = -1;
        private int _reconcilePortalGhosts = -1;
        private bool _running;
        private bool _initialized;
        private int _framesUntilTopologyBatch;
        private bool _dispatchedThisTick;
        private long _revisions;
        private uint _chunkId;

        public bool IsRunning => _running;
        public bool DispatchedThisTick => _dispatchedThisTick;
        public long Revisions => _revisions;
        public PressureManifoldPool Pool => filmSpawner?.PressureManifolds;

        public void StartAtlas(PrismFilmSpawner films = null,
            PrismDisplacementTopology displacement = null,
            PrismBoundaryGraph boundaries = null)
        {
            if (_running) return;
            filmSpawner = films != null ? films : filmSpawner;
            displacementTopology = displacement != null ? displacement :
                displacementTopology;
            boundaryGraph = boundaries != null ? boundaries : boundaryGraph;
            filmSpawner ??= GetComponent<PrismFilmSpawner>();
            displacementTopology ??= GetComponent<PrismDisplacementTopology>();
            boundaryGraph ??= GetComponent<PrismBoundaryGraph>();
            evidenceSplitter ??= GetComponent<PrismEvidenceAlignedSplitter>();
            supportContourCompute ??=
                Resources.Load<ComputeShader>("Prism/SupportContourExtract");
            halfEdgeCompute ??=
                Resources.Load<ComputeShader>("Prism/ManifoldHalfEdgeUpdate");
            boundaryCurveCompute ??=
                Resources.Load<ComputeShader>("Prism/BoundaryCurveUpdate");
            elasticIslandCompute ??=
                Resources.Load<ComputeShader>("Prism/ElasticIslandSolve");
            crossChunkPortalCompute ??=
                Resources.Load<ComputeShader>("Prism/CrossChunkPortalUpdate");
            if (filmSpawner?.FilmPool == null ||
                displacementTopology?.DisplacementPool == null ||
                boundaryGraph?.BoundaryPool == null ||
                supportContourCompute == null || halfEdgeCompute == null ||
                boundaryCurveCompute == null || elasticIslandCompute == null ||
                crossChunkPortalCompute == null)
            {
                Logger.Error("Cone-PRISM PressureManifold atlas dependencies are missing.");
                return;
            }
            FindKernels();
            BindContourResources();
            BindHalfEdgeResources();
            BindBoundaryCurveResources();
            BindElasticResources();
            BindPortalResources();
            evidenceSplitter?.StartSplitting(filmSpawner,
                displacementTopology, boundaryGraph);
            if (!_initialized)
            {
                PressureManifoldPool atlas = filmSpawner.PressureManifolds;
                supportContourCompute.Dispatch(_initializeContours,
                    CeilDiv(Math.Max(atlas.FilmCapacity, 16), 64), 1, 1);
                _initialized = true;
            }
            _framesUntilTopologyBatch = 0;
            _dispatchedThisTick = false;
            _running = true;
        }

        public void StopAtlas()
        {
            evidenceSplitter?.StopSplitting();
            _dispatchedThisTick = false;
            _running = false;
        }

        public void SetChunkFrame(uint chunkId)
        {
            _chunkId = chunkId;
        }

        private void OnDestroy() => StopAtlas();

        internal void ResetEmptyResidency()
        {
            if (!_running || filmSpawner?.PressureManifolds == null) return;
            BindContourResources();
            PressureManifoldPool atlas = filmSpawner.PressureManifolds;
            supportContourCompute.Dispatch(_initializeContours,
                CeilDiv(Math.Max(atlas.FilmCapacity, 16), 64), 1, 1);
            _initialized = true;
            _framesUntilTopologyBatch = 0;
        }

        internal void PrepareRestoredResidency()
        {
            if (!_running || filmSpawner?.PressureManifolds == null) return;
            BindContourResources();
            PressureManifoldPool atlas = filmSpawner.PressureManifolds;
            supportContourCompute.Dispatch(_resetRestoredTopologyTransient,
                CeilDiv(Math.Max(atlas.FilmCapacity, 16), 64), 1, 1);
            _framesUntilTopologyBatch = 0;
        }

        internal bool DispatchAtlas()
        {
            _dispatchedThisTick = false;
            if (!_running || filmSpawner?.FilmPool == null ||
                displacementTopology?.DisplacementPool == null) return false;

            // Geometry information, competing hypotheses and displacement are still
            // integrated for every finite-cone frame. Topology and its derived mesh
            // publication are transactional caches, so coalescing two ingress frames
            // loses no measurement and prevents dozens of zero/duplicate dispatches
            // from serializing XR presentation. The first frame and every restore run
            // immediately; the previous immutable mesh remains valid between batches.
            if (_framesUntilTopologyBatch > 0)
            {
                _framesUntilTopologyBatch--;
                return true;
            }
            _framesUntilTopologyBatch = Math.Max(0, topologyBatchFrames - 1);
            try
            {
                BindContourResources();
                BindHalfEdgeResources();
                BindBoundaryCurveResources();
                BindElasticResources();
                BindPortalResources();
                PressureManifoldPool atlas = filmSpawner.PressureManifolds;

                supportContourCompute.Dispatch(_buildFilmDirtyImportArguments,
                    1, 1, 1);
                supportContourCompute.DispatchIndirect(_importFilmDirtyTopology,
                    atlas.AtlasDispatchArguments, sizeof(uint) * 21);
                supportContourCompute.Dispatch(_buildContourArguments, 1, 1, 1);
                supportContourCompute.DispatchIndirect(_countContours,
                    atlas.AtlasDispatchArguments, 0);
                supportContourCompute.Dispatch(_preflightContourPages, 1, 1, 1);
                supportContourCompute.DispatchIndirect(_commitContourPages,
                    atlas.AtlasDispatchArguments, sizeof(uint) * 3);
                supportContourCompute.DispatchIndirect(_writeContours,
                    atlas.AtlasDispatchArguments, sizeof(uint) * 6);

                halfEdgeCompute.Dispatch(_buildHalfEdgeArguments, 1, 1, 1);
                halfEdgeCompute.DispatchIndirect(_clearHalfEdgeHashState,
                    atlas.AtlasDispatchArguments, sizeof(uint) * 9);
                halfEdgeCompute.DispatchIndirect(_clearHalfEdgeState,
                    atlas.AtlasDispatchArguments, sizeof(uint) * 9);
                halfEdgeCompute.DispatchIndirect(_materializeHalfEdges,
                    atlas.AtlasDispatchArguments, sizeof(uint) * 12);
                halfEdgeCompute.DispatchIndirect(_buildHalfEdgeHash,
                    atlas.AtlasDispatchArguments, sizeof(uint) * 12);
                crossChunkPortalCompute.Dispatch(
                    _buildPortalReconcileArguments, 1, 1, 1);
                crossChunkPortalCompute.DispatchIndirect(
                    _reconcilePortalGhosts, atlas.PortalDispatchArguments,
                    sizeof(uint) * 9);
                boundaryCurveCompute.DispatchIndirect(_clearBoundaryClaims,
                    atlas.AtlasDispatchArguments, sizeof(uint) * 12);
                boundaryCurveCompute.DispatchIndirect(_claimBoundaryHalfEdges,
                    boundaryGraph.BoundaryDispatchArguments, sizeof(uint) * 3);
                boundaryCurveCompute.DispatchIndirect(_commitBoundaryCurves,
                    boundaryGraph.BoundaryDispatchArguments, sizeof(uint) * 3);
                halfEdgeCompute.DispatchIndirect(_proveTwins,
                    atlas.AtlasDispatchArguments, sizeof(uint) * 12);
                halfEdgeCompute.DispatchIndirect(_buildEndpointHash,
                    atlas.AtlasDispatchArguments, sizeof(uint) * 15);
                halfEdgeCompute.DispatchIndirect(_orderOuterEdges,
                    atlas.AtlasDispatchArguments, sizeof(uint) * 12);
                halfEdgeCompute.DispatchIndirect(_initializeFrontierComponents,
                    atlas.AtlasDispatchArguments, sizeof(uint) * 12);
                int convergenceWaves = CeilLog2(atlas.HalfEdgeCapacity) + 2;
                for (int wave = 0; wave < convergenceWaves; wave++)
                {
                    halfEdgeCompute.DispatchIndirect(_hookFrontierComponents,
                        atlas.AtlasDispatchArguments, sizeof(uint) * 12);
                    halfEdgeCompute.DispatchIndirect(_shortcutFrontierComponents,
                        atlas.AtlasDispatchArguments, sizeof(uint) * 12);
                }
                halfEdgeCompute.DispatchIndirect(_createFrontierLoops,
                    atlas.AtlasDispatchArguments, sizeof(uint) * 12);
                halfEdgeCompute.DispatchIndirect(_assignFrontierLoops,
                    atlas.AtlasDispatchArguments, sizeof(uint) * 12);
                halfEdgeCompute.DispatchIndirect(_finalizeFrontierLoops,
                    atlas.AtlasDispatchArguments, sizeof(uint) * 12);
                supportContourCompute.DispatchIndirect(_finalizeTopologyDirtyFlags,
                    atlas.AtlasDispatchArguments, sizeof(uint) * 3);
                supportContourCompute.Dispatch(_resetTopologyDirtyQueue, 1, 1, 1);
                elasticIslandCompute.Dispatch(_buildElasticArguments, 1, 1, 1);
                elasticIslandCompute.DispatchIndirect(_initializeElasticStates,
                    atlas.AtlasDispatchArguments, 0);
                for (int iteration = 0; iteration < elasticIterations; iteration++)
                {
                    elasticIslandCompute.DispatchIndirect(
                        _clearElasticAccumulators, atlas.AtlasDispatchArguments, 0);
                    elasticIslandCompute.DispatchIndirect(
                        _accumulateElasticConstraints,
                        atlas.AtlasDispatchArguments, sizeof(uint) * 3);
                    elasticIslandCompute.DispatchIndirect(
                        _accumulatePortalConstraints,
                        atlas.PortalDispatchArguments, sizeof(uint) * 9);
                    elasticIslandCompute.DispatchIndirect(
                        _solveElasticCorrections, atlas.AtlasDispatchArguments, 0);
                }
                supportContourCompute.DispatchIndirect(
                    _queueElasticTopologyChanges,
                    atlas.AtlasDispatchArguments, 0);
                if (evidenceSplitter == null ||
                    !evidenceSplitter.DispatchSplits()) return false;
                supportContourCompute.Dispatch(_buildFilmDirtyTailArguments,
                    1, 1, 1);
                supportContourCompute.DispatchIndirect(
                    _importFilmDirtyTopologyTail,
                    atlas.AtlasDispatchArguments, sizeof(uint) * 21);
                _dispatchedThisTick = true;
                _revisions++;
                return true;
            }
            catch (Exception exception)
            {
                _framesUntilTopologyBatch = 0;
                Logger.Error("Cone-PRISM PressureManifold atlas update failed: " +
                    exception.Message);
                return false;
            }
        }

        private void FindKernels()
        {
            _initializeContours = supportContourCompute.FindKernel(
                "InitializeSupportContourAtlas");
            _buildFilmDirtyImportArguments = supportContourCompute.FindKernel(
                "BuildFilmDirtyImportArguments");
            _importFilmDirtyTopology = supportContourCompute.FindKernel(
                "ImportFilmDirtyTopology");
            _buildFilmDirtyTailArguments = supportContourCompute.FindKernel(
                "BuildFilmDirtyTailArguments");
            _importFilmDirtyTopologyTail = supportContourCompute.FindKernel(
                "ImportFilmDirtyTopologyTail");
            _buildContourArguments = supportContourCompute.FindKernel(
                "BuildSupportContourArguments");
            _countContours = supportContourCompute.FindKernel(
                "CountSupportContourSegments");
            _preflightContourPages = supportContourCompute.FindKernel(
                "PreflightSupportContourPages");
            _commitContourPages = supportContourCompute.FindKernel(
                "CommitSupportContourPages");
            _writeContours = supportContourCompute.FindKernel(
                "WriteSupportContourSegments");
            _finalizeTopologyDirtyFlags = supportContourCompute.FindKernel(
                "FinalizeTopologyDirtyFlags");
            _resetTopologyDirtyQueue = supportContourCompute.FindKernel(
                "ResetTopologyDirtyQueue");
            _queueElasticTopologyChanges = supportContourCompute.FindKernel(
                "QueueElasticTopologyChanges");
            _resetRestoredTopologyTransient = supportContourCompute.FindKernel(
                "ResetRestoredTopologyTransient");
            _buildHalfEdgeArguments = halfEdgeCompute.FindKernel(
                "BuildHalfEdgeArguments");
            _clearHalfEdgeHashState = halfEdgeCompute.FindKernel(
                "ClearHalfEdgeHashState");
            _clearHalfEdgeState = halfEdgeCompute.FindKernel(
                "ClearHalfEdgeDerivedState");
            _materializeHalfEdges = halfEdgeCompute.FindKernel(
                "MaterializeMeasuredHalfEdges");
            _buildHalfEdgeHash = halfEdgeCompute.FindKernel(
                "BuildHalfEdgeSpatialHash");
            _proveTwins = halfEdgeCompute.FindKernel("ProveHalfEdgeTwins");
            _buildEndpointHash = halfEdgeCompute.FindKernel(
                "BuildFrontierEndpointHash");
            _orderOuterEdges = halfEdgeCompute.FindKernel("OrderOuterHalfEdges");
            _initializeFrontierComponents = halfEdgeCompute.FindKernel(
                "InitializeFrontierComponents");
            _hookFrontierComponents = halfEdgeCompute.FindKernel(
                "HookFrontierComponents");
            _shortcutFrontierComponents = halfEdgeCompute.FindKernel(
                "ShortcutFrontierComponents");
            _createFrontierLoops = halfEdgeCompute.FindKernel("CreateFrontierLoops");
            _assignFrontierLoops = halfEdgeCompute.FindKernel("AssignFrontierLoops");
            _finalizeFrontierLoops = halfEdgeCompute.FindKernel(
                "FinalizeFrontierLoops");
            _clearBoundaryClaims = boundaryCurveCompute.FindKernel(
                "ClearHalfEdgeBoundaryClaims");
            _claimBoundaryHalfEdges = boundaryCurveCompute.FindKernel(
                "ClaimBoundaryHalfEdges");
            _commitBoundaryCurves = boundaryCurveCompute.FindKernel(
                "CommitBoundaryCurves");
            _buildElasticArguments = elasticIslandCompute.FindKernel(
                "BuildElasticDispatchArguments");
            _initializeElasticStates = elasticIslandCompute.FindKernel(
                "InitializeElasticStates");
            _clearElasticAccumulators = elasticIslandCompute.FindKernel(
                "ClearElasticAccumulators");
            _accumulateElasticConstraints = elasticIslandCompute.FindKernel(
                "AccumulateElasticConstraints");
            _accumulatePortalConstraints = elasticIslandCompute.FindKernel(
                "AccumulatePortalConstraints");
            _solveElasticCorrections = elasticIslandCompute.FindKernel(
                "SolveElasticChartCorrections");
            _buildPortalReconcileArguments = crossChunkPortalCompute.FindKernel(
                "BuildPortalReconcileArguments");
            _reconcilePortalGhosts = crossChunkPortalCompute.FindKernel(
                "ReconcilePortalGhosts");
        }

        private void BindContourResources()
        {
            ContactFilmPool films = filmSpawner.FilmPool;
            ContactDisplacementPool displacement = displacementTopology.DisplacementPool;
            PressureManifoldPool atlas = films.Manifolds;
            supportContourCompute.SetInt(FilmCapacityId, films.Capacity);
            supportContourCompute.SetInt(BasePageCapacityId,
                displacement.BasePageCapacity);
            supportContourCompute.SetInt(ContourPageCapacityId,
                atlas.ContourPageCapacity);
            supportContourCompute.SetInt(ContourSegmentCapacityId,
                atlas.ContourSegmentCapacity);
            supportContourCompute.SetFloat(CoverageThresholdId, coverageThreshold);
            int[] kernels =
            {
                _initializeContours,
                _buildFilmDirtyImportArguments, _importFilmDirtyTopology,
                _buildFilmDirtyTailArguments, _importFilmDirtyTopologyTail,
                _buildContourArguments, _countContours,
                _preflightContourPages, _commitContourPages, _writeContours,
                _finalizeTopologyDirtyFlags, _resetTopologyDirtyQueue,
                _queueElasticTopologyChanges, _resetRestoredTopologyTransient
            };
            foreach (int kernel in kernels)
            {
                supportContourCompute.SetBuffer(kernel, FilmHeadersId, films.Headers);
                supportContourCompute.SetBuffer(kernel, FilmAllocatorId,
                    films.Allocator);
                supportContourCompute.SetBuffer(kernel, DirtyFilmIndicesId,
                    films.DirtyIndices);
                supportContourCompute.SetBuffer(kernel, ActiveFilmIndicesId,
                    films.ActiveIndices);
                supportContourCompute.SetBuffer(kernel, DisplacementPagesId,
                    displacement.PageHeaders);
                supportContourCompute.SetBuffer(kernel, BaseDisplacementCellsId,
                    displacement.BaseCells);
                supportContourCompute.SetBuffer(kernel, FilmMembershipsId,
                    atlas.Memberships);
                supportContourCompute.SetBuffer(kernel, SupportContourPagesId,
                    atlas.SupportContourPages);
                supportContourCompute.SetBuffer(kernel, SupportContoursId,
                    atlas.SupportContours);
                supportContourCompute.SetBuffer(kernel, FilmTopologyRangesId,
                    atlas.FilmTopologyRanges);
                supportContourCompute.SetBuffer(kernel, ContourPlansId,
                    atlas.ContourPlans);
                supportContourCompute.SetBuffer(kernel, AtlasAllocatorId,
                    atlas.AtlasAllocator);
                supportContourCompute.SetBuffer(kernel, DirtyTopologyFilmsId,
                    atlas.DirtyTopologyFilms);
                supportContourCompute.SetBuffer(kernel, TopologyDirtyFlagsId,
                    atlas.TopologyDirtyFlags);
                supportContourCompute.SetBuffer(kernel, AtlasDispatchArgumentsId,
                    atlas.AtlasDispatchArguments);
                supportContourCompute.SetBuffer(kernel, ManifoldDiagnosticsId,
                    atlas.Diagnostics);
                supportContourCompute.SetBuffer(kernel, ElasticChartStatesId,
                    atlas.ElasticStates);
            }
        }

        private void BindHalfEdgeResources()
        {
            ContactFilmPool films = filmSpawner.FilmPool;
            ContactDisplacementPool displacement = displacementTopology.DisplacementPool;
            PressureManifoldPool atlas = films.Manifolds;
            halfEdgeCompute.SetInt(FilmCapacityId, films.Capacity);
            halfEdgeCompute.SetInt(ContourPageCapacityId, atlas.ContourPageCapacity);
            halfEdgeCompute.SetInt(ContourSegmentCapacityId,
                atlas.ContourSegmentCapacity);
            halfEdgeCompute.SetInt(HalfEdgeCapacityId, atlas.HalfEdgeCapacity);
            halfEdgeCompute.SetInt(HalfEdgeHashCapacityId,
                atlas.HalfEdgeHashCapacity);
            halfEdgeCompute.SetInt(ManifoldCapacityId, atlas.ManifoldCapacity);
            halfEdgeCompute.SetInt(HasDisplacementId, 1);
            halfEdgeCompute.SetInt(BasePageCapacityId,
                displacement.BasePageCapacity);
            halfEdgeCompute.SetInt(MicroPageCapacityId,
                displacement.MicroPageCapacity);
            halfEdgeCompute.SetInt(BaseCellCapacityId,
                displacement.BaseCellCapacity);
            halfEdgeCompute.SetInt(MaximumMicroLevelsId,
                displacementTopology.MaximumMicroLevels);
            halfEdgeCompute.SetFloat(WeldPositionFloorId, weldPositionFloor);
            halfEdgeCompute.SetFloat(SmoothNormalCosineId,
                Mathf.Cos(smoothNormalDegrees * Mathf.Deg2Rad));
            halfEdgeCompute.SetFloat(CreaseNormalCosineId,
                Mathf.Cos(creaseNormalDegrees * Mathf.Deg2Rad));
            int[] kernels =
            {
                _buildHalfEdgeArguments, _clearHalfEdgeHashState,
                _clearHalfEdgeState,
                _materializeHalfEdges, _buildHalfEdgeHash, _proveTwins,
                _buildEndpointHash, _orderOuterEdges,
                _initializeFrontierComponents, _hookFrontierComponents,
                _shortcutFrontierComponents, _createFrontierLoops,
                _assignFrontierLoops, _finalizeFrontierLoops
            };
            foreach (int kernel in kernels)
            {
                halfEdgeCompute.SetBuffer(kernel, FilmHeadersId, films.Headers);
                halfEdgeCompute.SetBuffer(kernel, FilmInformationId,
                    films.Information);
                halfEdgeCompute.SetBuffer(kernel, FilmMembershipsId,
                    atlas.Memberships);
                halfEdgeCompute.SetBuffer(kernel, ManifoldHeadersId, atlas.Headers);
                halfEdgeCompute.SetBuffer(kernel, SupportContourPagesId,
                    atlas.SupportContourPages);
                halfEdgeCompute.SetBuffer(kernel, SupportContoursId,
                    atlas.SupportContours);
                halfEdgeCompute.SetBuffer(kernel, HalfEdgesId, atlas.HalfEdges);
                halfEdgeCompute.SetBuffer(kernel, ContinuationEvidenceId,
                    atlas.ContinuationEvidence);
                halfEdgeCompute.SetBuffer(kernel, HalfEdgeHashHeadsId,
                    atlas.HalfEdgeHashHeads);
                halfEdgeCompute.SetBuffer(kernel, HalfEdgeHashNextId,
                    atlas.HalfEdgeHashNext);
                halfEdgeCompute.SetBuffer(kernel, HalfEdgeHashKeysId,
                    atlas.HalfEdgeHashKeys);
                halfEdgeCompute.SetBuffer(kernel, EndpointHashHeadsId,
                    atlas.EndpointHashHeads);
                halfEdgeCompute.SetBuffer(kernel, EndpointHashEntriesId,
                    atlas.EndpointHashEntries);
                halfEdgeCompute.SetBuffer(kernel, HalfEdgeLoopParentsId,
                    atlas.HalfEdgeLoopParents);
                halfEdgeCompute.SetBuffer(kernel, HalfEdgeLoopIdsId,
                    atlas.HalfEdgeLoopIds);
                halfEdgeCompute.SetBuffer(kernel, FrontierLoopsId,
                    atlas.FrontierLoops);
                halfEdgeCompute.SetBuffer(kernel, FrontierLoopMomentsId,
                    atlas.FrontierLoopMoments);
                halfEdgeCompute.SetBuffer(kernel, AtlasAllocatorId,
                    atlas.AtlasAllocator);
                halfEdgeCompute.SetBuffer(kernel, AtlasDispatchArgumentsId,
                    atlas.AtlasDispatchArguments);
                halfEdgeCompute.SetBuffer(kernel, ManifoldDiagnosticsId,
                    atlas.Diagnostics);
                halfEdgeCompute.SetBuffer(kernel, DisplacementPagesId,
                    displacement.PageHeaders);
                halfEdgeCompute.SetBuffer(kernel, BaseDisplacementCellsId,
                    displacement.BaseCells);
                halfEdgeCompute.SetBuffer(kernel, MicroDisplacementCellsId,
                    displacement.MicroCells);
                halfEdgeCompute.SetBuffer(kernel, BaseChildPagesId,
                    displacement.BaseChildPages);
                halfEdgeCompute.SetBuffer(kernel, MicroChildPagesId,
                    displacement.MicroChildPages);
                halfEdgeCompute.SetBuffer(kernel, ElasticChartStatesId,
                    atlas.ElasticStates);
            }
        }

        private void BindBoundaryCurveResources()
        {
            ContactFilmPool films = filmSpawner.FilmPool;
            ContactDisplacementPool displacement = displacementTopology.DisplacementPool;
            PressureManifoldPool atlas = films.Manifolds;
            ContactBoundaryPool boundaries = boundaryGraph.BoundaryPool;
            boundaryCurveCompute.SetInt(FilmCapacityId, films.Capacity);
            boundaryCurveCompute.SetInt(BoundaryCapacityId, boundaries.Capacity);
            boundaryCurveCompute.SetInt(BoundaryHashMaskId,
                boundaries.HashCapacity - 1);
            boundaryCurveCompute.SetInt(BoundaryCellsPerAxisId,
                boundaryGraph.CellsPerAxis);
            boundaryCurveCompute.SetInt(HalfEdgeCapacityId,
                atlas.HalfEdgeCapacity);
            boundaryCurveCompute.SetInt(HalfEdgeHashCapacityId,
                atlas.HalfEdgeHashCapacity);
            boundaryCurveCompute.SetInt(ContourSegmentCapacityId,
                atlas.ContourSegmentCapacity);
            boundaryCurveCompute.SetFloat(BoundaryAttachFloorId,
                weldPositionFloor);
            boundaryCurveCompute.SetInt(HasDisplacementId, 1);
            boundaryCurveCompute.SetInt(BasePageCapacityId,
                displacement.BasePageCapacity);
            boundaryCurveCompute.SetInt(MicroPageCapacityId,
                displacement.MicroPageCapacity);
            boundaryCurveCompute.SetInt(BaseCellCapacityId,
                displacement.BaseCellCapacity);
            boundaryCurveCompute.SetInt(MaximumMicroLevelsId,
                displacementTopology.MaximumMicroLevels);
            int[] kernels =
            {
                _clearBoundaryClaims, _claimBoundaryHalfEdges,
                _commitBoundaryCurves
            };
            foreach (int kernel in kernels)
            {
                boundaryCurveCompute.SetBuffer(kernel, FilmHeadersReadId,
                    films.Headers);
                boundaryCurveCompute.SetBuffer(kernel, FilmHeadersId,
                    films.Headers);
                boundaryCurveCompute.SetBuffer(kernel, BoundaryHeadersId,
                    boundaries.Headers);
                boundaryCurveCompute.SetBuffer(kernel, BoundaryInformationId,
                    boundaries.Information);
                boundaryCurveCompute.SetBuffer(kernel, BoundaryAllocatorId,
                    boundaries.Allocator);
                boundaryCurveCompute.SetBuffer(kernel, FilmMembershipsId,
                    atlas.Memberships);
                boundaryCurveCompute.SetBuffer(kernel, SupportContoursId,
                    atlas.SupportContours);
                boundaryCurveCompute.SetBuffer(kernel, HalfEdgesId,
                    atlas.HalfEdges);
                boundaryCurveCompute.SetBuffer(kernel, ContinuationEvidenceId,
                    atlas.ContinuationEvidence);
                boundaryCurveCompute.SetBuffer(kernel, HalfEdgeHashHeadsId,
                    atlas.HalfEdgeHashHeads);
                boundaryCurveCompute.SetBuffer(kernel, HalfEdgeHashNextId,
                    atlas.HalfEdgeHashNext);
                boundaryCurveCompute.SetBuffer(kernel, HalfEdgeBoundaryClaimsId,
                    atlas.HalfEdgeBoundaryClaims);
                boundaryCurveCompute.SetBuffer(kernel,
                    HalfEdgeBoundaryClaimsReadId, atlas.HalfEdgeBoundaryClaims);
                boundaryCurveCompute.SetBuffer(kernel, BoundaryCurveTopologyId,
                    boundaries.Topology);
                boundaryCurveCompute.SetBuffer(kernel, BoundaryCurveCacheId,
                    boundaries.CurveCache);
                boundaryCurveCompute.SetBuffer(kernel, BoundaryHashId,
                    boundaries.HashEntries);
                boundaryCurveCompute.SetBuffer(kernel, ManifoldDiagnosticsId,
                    atlas.Diagnostics);
                boundaryCurveCompute.SetBuffer(kernel, DisplacementPagesId,
                    displacement.PageHeaders);
                boundaryCurveCompute.SetBuffer(kernel, BaseDisplacementCellsId,
                    displacement.BaseCells);
                boundaryCurveCompute.SetBuffer(kernel, MicroDisplacementCellsId,
                    displacement.MicroCells);
                boundaryCurveCompute.SetBuffer(kernel, BaseChildPagesId,
                    displacement.BaseChildPages);
                boundaryCurveCompute.SetBuffer(kernel, MicroChildPagesId,
                    displacement.MicroChildPages);
                boundaryCurveCompute.SetBuffer(kernel, ElasticChartStatesId,
                    atlas.ElasticStates);
            }
        }

        private void BindElasticResources()
        {
            ContactFilmPool films = filmSpawner.FilmPool;
            ContactDisplacementPool displacement = displacementTopology.DisplacementPool;
            PressureManifoldPool atlas = films.Manifolds;
            elasticIslandCompute.SetInt(FilmCapacityId, films.Capacity);
            elasticIslandCompute.SetInt(HalfEdgeCapacityId,
                atlas.HalfEdgeCapacity);
            elasticIslandCompute.SetInt(ContourSegmentCapacityId,
                atlas.ContourSegmentCapacity);
            elasticIslandCompute.SetInt(PortalCapacityId, atlas.PortalCapacity);
            elasticIslandCompute.SetInt(CurrentChunkId, unchecked((int)_chunkId));
            elasticIslandCompute.SetInt(HasDisplacementId, 1);
            elasticIslandCompute.SetInt(BasePageCapacityId,
                displacement.BasePageCapacity);
            elasticIslandCompute.SetInt(MicroPageCapacityId,
                displacement.MicroPageCapacity);
            elasticIslandCompute.SetInt(BaseCellCapacityId,
                displacement.BaseCellCapacity);
            elasticIslandCompute.SetInt(MaximumMicroLevelsId,
                displacementTopology.MaximumMicroLevels);
            elasticIslandCompute.SetFloat(ElasticStiffnessScaleId,
                elasticStiffnessScale);
            elasticIslandCompute.SetFloat(MaximumElasticStepId,
                maximumElasticStep);
            elasticIslandCompute.SetFloat(ElasticConvergenceFloorId,
                elasticConvergenceFloor);
            int[] kernels =
            {
                _buildElasticArguments, _initializeElasticStates,
                _clearElasticAccumulators, _accumulateElasticConstraints,
                _accumulatePortalConstraints, _solveElasticCorrections
            };
            foreach (int kernel in kernels)
            {
                elasticIslandCompute.SetBuffer(kernel, FilmHeadersId,
                    films.Headers);
                elasticIslandCompute.SetBuffer(kernel, FilmInformationId,
                    films.Information);
                elasticIslandCompute.SetBuffer(kernel, FilmAllocatorId,
                    films.Allocator);
                elasticIslandCompute.SetBuffer(kernel, ActiveFilmIndicesId,
                    films.ActiveIndices);
                elasticIslandCompute.SetBuffer(kernel, DirtyFilmIndicesId,
                    films.DirtyIndices);
                elasticIslandCompute.SetBuffer(kernel, FilmSlotStatesId,
                    films.SlotStates);
                elasticIslandCompute.SetBuffer(kernel, SupportContoursId,
                    atlas.SupportContours);
                elasticIslandCompute.SetBuffer(kernel, HalfEdgesId,
                    atlas.HalfEdges);
                elasticIslandCompute.SetBuffer(kernel, ContinuationEvidenceId,
                    atlas.ContinuationEvidence);
                elasticIslandCompute.SetBuffer(kernel, CrossChunkPortalsId,
                    atlas.CrossChunkPortals);
                elasticIslandCompute.SetBuffer(kernel, AtlasAllocatorId,
                    atlas.AtlasAllocator);
                elasticIslandCompute.SetBuffer(kernel, ElasticChartStatesId,
                    atlas.ElasticStates);
                elasticIslandCompute.SetBuffer(kernel, ElasticGradientsId,
                    atlas.ElasticGradients);
                elasticIslandCompute.SetBuffer(kernel, ElasticDiagonalsId,
                    atlas.ElasticDiagonals);
                elasticIslandCompute.SetBuffer(kernel, ElasticDispatchArgumentsId,
                    atlas.AtlasDispatchArguments);
                elasticIslandCompute.SetBuffer(kernel, DisplacementPagesId,
                    displacement.PageHeaders);
                elasticIslandCompute.SetBuffer(kernel, BaseDisplacementCellsId,
                    displacement.BaseCells);
                elasticIslandCompute.SetBuffer(kernel, MicroDisplacementCellsId,
                    displacement.MicroCells);
                elasticIslandCompute.SetBuffer(kernel, BaseChildPagesId,
                    displacement.BaseChildPages);
                elasticIslandCompute.SetBuffer(kernel, MicroChildPagesId,
                    displacement.MicroChildPages);
            }
        }

        private void BindPortalResources()
        {
            ContactFilmPool films = filmSpawner.FilmPool;
            ContactDisplacementPool displacement =
                displacementTopology.DisplacementPool;
            PressureManifoldPool atlas = films.Manifolds;
            crossChunkPortalCompute.SetInt(FilmCapacityId, films.Capacity);
            crossChunkPortalCompute.SetInt(ContourSegmentCapacityId,
                atlas.ContourSegmentCapacity);
            crossChunkPortalCompute.SetInt(HalfEdgeCapacityId,
                atlas.HalfEdgeCapacity);
            crossChunkPortalCompute.SetInt(HalfEdgeHashCapacityId,
                atlas.HalfEdgeHashCapacity);
            crossChunkPortalCompute.SetInt(PortalCapacityId, atlas.PortalCapacity);
            crossChunkPortalCompute.SetInt(CurrentChunkId,
                unchecked((int)_chunkId));
            crossChunkPortalCompute.SetFloat(WeldPositionFloorId,
                weldPositionFloor);
            crossChunkPortalCompute.SetFloat(CreaseNormalCosineId,
                Mathf.Cos(creaseNormalDegrees * Mathf.Deg2Rad));
            crossChunkPortalCompute.SetInt(HasDisplacementId, 1);
            crossChunkPortalCompute.SetInt(BasePageCapacityId,
                displacement.BasePageCapacity);
            crossChunkPortalCompute.SetInt(MicroPageCapacityId,
                displacement.MicroPageCapacity);
            crossChunkPortalCompute.SetInt(BaseCellCapacityId,
                displacement.BaseCellCapacity);
            crossChunkPortalCompute.SetInt(MaximumMicroLevelsId,
                displacementTopology.MaximumMicroLevels);
            int[] kernels =
            {
                _buildPortalReconcileArguments, _reconcilePortalGhosts
            };
            foreach (int kernel in kernels)
            {
                crossChunkPortalCompute.SetBuffer(kernel, FilmHeadersId,
                    films.Headers);
                crossChunkPortalCompute.SetBuffer(kernel, FilmInformationId,
                    films.Information);
                crossChunkPortalCompute.SetBuffer(kernel, SupportContoursId,
                    atlas.SupportContours);
                crossChunkPortalCompute.SetBuffer(kernel, HalfEdgesId,
                    atlas.HalfEdges);
                crossChunkPortalCompute.SetBuffer(kernel, HalfEdgeHashHeadsId,
                    atlas.HalfEdgeHashHeads);
                crossChunkPortalCompute.SetBuffer(kernel, HalfEdgeHashNextId,
                    atlas.HalfEdgeHashNext);
                crossChunkPortalCompute.SetBuffer(kernel, CrossChunkPortalsId,
                    atlas.CrossChunkPortals);
                crossChunkPortalCompute.SetBuffer(kernel, AtlasAllocatorId,
                    atlas.AtlasAllocator);
                crossChunkPortalCompute.SetBuffer(kernel,
                    PortalDispatchArgumentsId, atlas.PortalDispatchArguments);
                // ReconcilePortalGhosts evaluates exact chart geometry through
                // SurfaceChartGeometry.hlsl. Bind the complete posterior hierarchy;
                // relying on incidental bindings from staging is invalid because a
                // ComputeShader owns a separate binding table per kernel.
                crossChunkPortalCompute.SetBuffer(kernel, DisplacementPagesId,
                    displacement.PageHeaders);
                crossChunkPortalCompute.SetBuffer(kernel,
                    BaseDisplacementCellsId, displacement.BaseCells);
                crossChunkPortalCompute.SetBuffer(kernel,
                    MicroDisplacementCellsId, displacement.MicroCells);
                crossChunkPortalCompute.SetBuffer(kernel, BaseChildPagesId,
                    displacement.BaseChildPages);
                crossChunkPortalCompute.SetBuffer(kernel, MicroChildPagesId,
                    displacement.MicroChildPages);
                crossChunkPortalCompute.SetBuffer(kernel, ElasticChartStatesId,
                    atlas.ElasticStates);
            }
        }

        private static int CeilDiv(int value, int divisor) =>
            Math.Max(1, (value + divisor - 1) / divisor);

        private static int CeilLog2(int value)
        {
            int result = 0;
            for (int covered = 1; covered < Math.Max(1, value); covered <<= 1)
                result++;
            return result;
        }
    }
}
