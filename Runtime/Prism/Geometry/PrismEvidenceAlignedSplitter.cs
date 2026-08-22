using System;
using UnityEngine;

namespace Genesis.RoomScan.Prism
{
    /// <summary>
    /// Evidence-driven canonical chart partitioning.  A persistent internal
    /// BoundaryCurve or a spatially supported bimodal residual proposes an
    /// arbitrary UV separator.  Two generation-safe children are published as one
    /// GPU transaction; no axis-aligned quadrant ontology is used.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(21)]
    public sealed class PrismEvidenceAlignedSplitter : MonoBehaviour
    {
        [SerializeField] private ComputeShader splitPlanCompute;
        [SerializeField] private ComputeShader chartSplitCompute;
        [SerializeField] private ComputeShader displacementSplitCompute;
        [SerializeField] private ComputeShader publishSplitCompute;
        [SerializeField, Range(1, 16)] private int maximumSplitsPerTick = 4;
        [SerializeField, Range(1, 10)] private int maximumSplitDepth = 6;
        [SerializeField, Min(0.005f)] private float minimumSplitExtent = 0.025f;
        [SerializeField, Min(0.0001f)] private float bimodalSeparation = 0.003f;
        [SerializeField, Min(0.0000001f)] private float splitVariance = 0.000006f;
        [SerializeField, Min(0.1f)] private float splitBoundarySupport = 4f;

        private PrismFilmSpawner _films;
        private PrismDisplacementTopology _displacement;
        private PrismBoundaryGraph _boundaries;
        private bool _running;

        private static readonly int FilmCapacityId = Shader.PropertyToID("_FilmCapacity");
        private static readonly int BoundaryCapacityId =
            Shader.PropertyToID("_BoundaryCapacity");
        private static readonly int BasePageCapacityId =
            Shader.PropertyToID("_BasePageCapacity");
        private static readonly int MicroPageCapacityId =
            Shader.PropertyToID("_MicroPageCapacity");
        private static readonly int BaseCellCapacityId =
            Shader.PropertyToID("_BaseCellCapacity");
        private static readonly int MaximumMicroLevelsId =
            Shader.PropertyToID("_MaximumMicroLevels");
        private static readonly int MaximumSplitDepthId =
            Shader.PropertyToID("_MaximumSplitDepth");
        private static readonly int MaximumSplitsPerTickId =
            Shader.PropertyToID("_MaximumSplitsPerTick");
        private static readonly int MinimumSplitExtentId =
            Shader.PropertyToID("_MinimumSplitExtent");
        private static readonly int BimodalSeparationId =
            Shader.PropertyToID("_BimodalSeparation");
        private static readonly int SplitVarianceId =
            Shader.PropertyToID("_SplitVariance");
        private static readonly int SplitBoundarySupportId =
            Shader.PropertyToID("_SplitBoundarySupport");
        private static readonly int FilmHeadersId = Shader.PropertyToID("_FilmHeaders");
        private static readonly int FilmInformationId =
            Shader.PropertyToID("_FilmInformation");
        private static readonly int FilmAllocatorId =
            Shader.PropertyToID("_FilmAllocator");
        private static readonly int FilmSlotStatesId =
            Shader.PropertyToID("_FilmSlotStates");
        private static readonly int ActiveFilmIndicesId =
            Shader.PropertyToID("_ActiveFilmIndices");
        private static readonly int DirtyFilmIndicesId =
            Shader.PropertyToID("_DirtyFilmIndices");
        private static readonly int FilmMembershipsId =
            Shader.PropertyToID("_FilmMemberships");
        private static readonly int TopologyEvidenceId =
            Shader.PropertyToID("_TopologyEvidence");
        private static readonly int DisplacementPagesId =
            Shader.PropertyToID("_DisplacementPages");
        private static readonly int BaseCellsId =
            Shader.PropertyToID("_BaseDisplacementCells");
        private static readonly int MicroCellsId =
            Shader.PropertyToID("_MicroDisplacementCells");
        private static readonly int BaseChildrenId =
            Shader.PropertyToID("_BaseChildPages");
        private static readonly int MicroChildrenId =
            Shader.PropertyToID("_MicroChildPages");
        private static readonly int DisplacementAllocatorId =
            Shader.PropertyToID("_DisplacementAllocator");
        private static readonly int BaseAccumulatorId =
            Shader.PropertyToID("_BaseDisplacementCellAccumulator");
        private static readonly int CellDirtyFlagsId =
            Shader.PropertyToID("_DisplacementCellDirtyFlags");
        private static readonly int BoundaryHeadersId =
            Shader.PropertyToID("_BoundaryHeaders");
        private static readonly int BoundaryTopologyId =
            Shader.PropertyToID("_BoundaryCurveTopology");
        private static readonly int BoundaryCacheId =
            Shader.PropertyToID("_BoundaryCurveCache");
        private static readonly int BoundaryAllocatorId =
            Shader.PropertyToID("_BoundaryAllocator");
        private static readonly int ElasticStatesId =
            Shader.PropertyToID("_ElasticChartStates");
        private static readonly int ProposalKeysId =
            Shader.PropertyToID("_SplitProposalKeys");
        private static readonly int SplitPlansId = Shader.PropertyToID("_SplitPlans");
        private static readonly int SplitRecordIndicesId =
            Shader.PropertyToID("_SplitRecordIndices");
        private static readonly int SplitStateId = Shader.PropertyToID("_SplitState");
        private static readonly int SplitStateReadId =
            Shader.PropertyToID("_SplitStateRead");
        private static readonly int SplitArgumentsId =
            Shader.PropertyToID("_SplitDispatchArguments");
        private int _clear = -1;
        private int _boundaryProposals = -1;
        private int _buildPlans = -1;
        private int _reserve = -1;
        private int _createChildren = -1;
        private int _initializeDisplacement = -1;
        private int _validate = -1;
        private int _commit = -1;

        public void StartSplitting(PrismFilmSpawner films,
            PrismDisplacementTopology displacement, PrismBoundaryGraph boundaries)
        {
            if (_running) return;
            _films = films;
            _displacement = displacement;
            _boundaries = boundaries;
            splitPlanCompute ??=
                Resources.Load<ComputeShader>("Prism/EvidenceAlignedSplitPlan");
            chartSplitCompute ??=
                Resources.Load<ComputeShader>("Prism/ContactChartSplit");
            displacementSplitCompute ??=
                Resources.Load<ComputeShader>("Prism/DisplacementChartSplit");
            publishSplitCompute ??=
                Resources.Load<ComputeShader>("Prism/PublishChartSplit");
            if (_films?.FilmPool == null ||
                _displacement?.DisplacementPool == null ||
                _boundaries?.BoundaryPool == null || splitPlanCompute == null ||
                chartSplitCompute == null || displacementSplitCompute == null ||
                publishSplitCompute == null)
            {
                Logger.Error("Cone-PRISM evidence-aligned split dependencies are missing.");
                return;
            }
            FindKernels();
            BindResources();
            _running = true;
        }

        public void StopSplitting() => _running = false;

        internal bool DispatchSplits()
        {
            if (!_running) return false;
            try
            {
                BindResources();
                ContactFilmPool films = _films.FilmPool;
                PressureManifoldPool atlas = films.Manifolds;
                splitPlanCompute.Dispatch(_clear,
                    CeilDiv(films.Capacity, 64), 1, 1);
                splitPlanCompute.Dispatch(_boundaryProposals,
                    CeilDiv(_boundaries.BoundaryPool.Capacity, 64), 1, 1);
                splitPlanCompute.Dispatch(_buildPlans,
                    CeilDiv(films.Capacity, 64), 1, 1);
                splitPlanCompute.Dispatch(_reserve, 1, 1, 1);
                chartSplitCompute.DispatchIndirect(_createChildren,
                    atlas.SplitDispatchArguments, 0);
                displacementSplitCompute.DispatchIndirect(_initializeDisplacement,
                    atlas.SplitDispatchArguments, sizeof(uint) * 3);
                publishSplitCompute.DispatchIndirect(_validate,
                    atlas.SplitDispatchArguments, sizeof(uint) * 6);
                publishSplitCompute.DispatchIndirect(_commit,
                    atlas.SplitDispatchArguments, sizeof(uint) * 9);
                return true;
            }
            catch (Exception exception)
            {
                Logger.Error("Cone-PRISM evidence-aligned split failed: " +
                    exception.Message);
                return false;
            }
        }

        private void FindKernels()
        {
            _clear = splitPlanCompute.FindKernel("ClearSplitPlanning");
            _boundaryProposals = splitPlanCompute.FindKernel(
                "AccumulateBoundarySplitProposals");
            _buildPlans = splitPlanCompute.FindKernel(
                "BuildEvidenceAlignedSplitPlans");
            _reserve = splitPlanCompute.FindKernel("ReserveSplitTransactions");
            _createChildren = chartSplitCompute.FindKernel(
                "CreateEvidenceAlignedChildren");
            _initializeDisplacement = displacementSplitCompute.FindKernel(
                "InitializeSplitDisplacement");
            _validate = publishSplitCompute.FindKernel(
                "ValidateEvidenceAlignedSplits");
            _commit = publishSplitCompute.FindKernel(
                "CommitEvidenceAlignedSplits");
        }

        private void BindResources()
        {
            ContactFilmPool films = _films.FilmPool;
            ContactDisplacementPool displacement = _displacement.DisplacementPool;
            ContactBoundaryPool boundaries = _boundaries.BoundaryPool;
            PressureManifoldPool atlas = films.Manifolds;

            splitPlanCompute.SetInt(FilmCapacityId, films.Capacity);
            splitPlanCompute.SetInt(BoundaryCapacityId, boundaries.Capacity);
            splitPlanCompute.SetInt(BasePageCapacityId,
                displacement.BasePageCapacity);
            splitPlanCompute.SetInt(MaximumSplitDepthId, maximumSplitDepth);
            splitPlanCompute.SetInt(MaximumSplitsPerTickId,
                maximumSplitsPerTick);
            splitPlanCompute.SetFloat(MinimumSplitExtentId, minimumSplitExtent);
            splitPlanCompute.SetFloat(BimodalSeparationId, bimodalSeparation);
            splitPlanCompute.SetFloat(SplitVarianceId, splitVariance);
            splitPlanCompute.SetFloat(SplitBoundarySupportId,
                splitBoundarySupport);
            int[] planKernels = { _clear, _boundaryProposals, _buildPlans, _reserve };
            foreach (int kernel in planKernels)
            {
                splitPlanCompute.SetBuffer(kernel, FilmHeadersId, films.Headers);
                splitPlanCompute.SetBuffer(kernel, FilmAllocatorId, films.Allocator);
                splitPlanCompute.SetBuffer(kernel, FilmSlotStatesId,
                    films.SlotStates);
                splitPlanCompute.SetBuffer(kernel, ActiveFilmIndicesId,
                    films.ActiveIndices);
                splitPlanCompute.SetBuffer(kernel, TopologyEvidenceId,
                    displacement.TopologyEvidence);
                splitPlanCompute.SetBuffer(kernel, DisplacementPagesId,
                    displacement.PageHeaders);
                splitPlanCompute.SetBuffer(kernel, BaseCellsId,
                    displacement.BaseCells);
                splitPlanCompute.SetBuffer(kernel, BoundaryHeadersId,
                    boundaries.Headers);
                splitPlanCompute.SetBuffer(kernel, BoundaryTopologyId,
                    boundaries.Topology);
                splitPlanCompute.SetBuffer(kernel, BoundaryCacheId,
                    boundaries.CurveCache);
                splitPlanCompute.SetBuffer(kernel, BoundaryAllocatorId,
                    boundaries.Allocator);
                splitPlanCompute.SetBuffer(kernel, ProposalKeysId,
                    atlas.SplitProposalKeys);
                splitPlanCompute.SetBuffer(kernel, SplitPlansId, atlas.SplitPlans);
                splitPlanCompute.SetBuffer(kernel, SplitRecordIndicesId,
                    atlas.SplitRecordIndices);
                splitPlanCompute.SetBuffer(kernel, SplitStateId, atlas.SplitState);
                splitPlanCompute.SetBuffer(kernel, SplitArgumentsId,
                    atlas.SplitDispatchArguments);
                splitPlanCompute.SetBuffer(kernel, DisplacementAllocatorId,
                    displacement.Allocator);
            }

            chartSplitCompute.SetInt(FilmCapacityId, films.Capacity);
            chartSplitCompute.SetBuffer(_createChildren, FilmHeadersId,
                films.Headers);
            chartSplitCompute.SetBuffer(_createChildren, FilmInformationId,
                films.Information);
            chartSplitCompute.SetBuffer(_createChildren, FilmMembershipsId,
                atlas.Memberships);
            chartSplitCompute.SetBuffer(_createChildren, TopologyEvidenceId,
                displacement.TopologyEvidence);
            chartSplitCompute.SetBuffer(_createChildren, ElasticStatesId,
                atlas.ElasticStates);
            chartSplitCompute.SetBuffer(_createChildren, SplitPlansId,
                atlas.SplitPlans);
            chartSplitCompute.SetBuffer(_createChildren, SplitRecordIndicesId,
                atlas.SplitRecordIndices);

            displacementSplitCompute.SetInt(FilmCapacityId, films.Capacity);
            displacementSplitCompute.SetInt(BasePageCapacityId,
                displacement.BasePageCapacity);
            displacementSplitCompute.SetInt(MicroPageCapacityId,
                displacement.MicroPageCapacity);
            displacementSplitCompute.SetInt(BaseCellCapacityId,
                displacement.BaseCellCapacity);
            displacementSplitCompute.SetInt(MaximumMicroLevelsId,
                _displacement.MaximumMicroLevels);
            displacementSplitCompute.SetBuffer(_initializeDisplacement,
                FilmHeadersId, films.Headers);
            displacementSplitCompute.SetBuffer(_initializeDisplacement,
                SplitPlansId, atlas.SplitPlans);
            displacementSplitCompute.SetBuffer(_initializeDisplacement,
                SplitRecordIndicesId, atlas.SplitRecordIndices);
            displacementSplitCompute.SetBuffer(_initializeDisplacement,
                BoundaryCacheId, boundaries.CurveCache);
            displacementSplitCompute.SetBuffer(_initializeDisplacement,
                DisplacementAllocatorId, displacement.Allocator);
            displacementSplitCompute.SetBuffer(_initializeDisplacement,
                DisplacementPagesId, displacement.PageHeaders);
            displacementSplitCompute.SetBuffer(_initializeDisplacement,
                BaseCellsId, displacement.BaseCells);
            displacementSplitCompute.SetBuffer(_initializeDisplacement,
                MicroCellsId, displacement.MicroCells);
            displacementSplitCompute.SetBuffer(_initializeDisplacement,
                BaseChildrenId, displacement.BaseChildPages);
            displacementSplitCompute.SetBuffer(_initializeDisplacement,
                MicroChildrenId, displacement.MicroChildPages);
            displacementSplitCompute.SetBuffer(_initializeDisplacement,
                BaseAccumulatorId, _displacement.BaseCellAccumulator);
            displacementSplitCompute.SetBuffer(_initializeDisplacement,
                CellDirtyFlagsId, _displacement.DisplacementCellDirtyFlags);

            publishSplitCompute.SetInt(FilmCapacityId, films.Capacity);
            publishSplitCompute.SetInt(BasePageCapacityId,
                displacement.BasePageCapacity);
            int[] publishKernels = { _validate, _commit };
            foreach (int kernel in publishKernels)
            {
                publishSplitCompute.SetBuffer(kernel, FilmHeadersId, films.Headers);
                publishSplitCompute.SetBuffer(kernel, FilmMembershipsId,
                    atlas.Memberships);
                publishSplitCompute.SetBuffer(kernel, FilmSlotStatesId,
                    films.SlotStates);
                publishSplitCompute.SetBuffer(kernel, ActiveFilmIndicesId,
                    films.ActiveIndices);
                publishSplitCompute.SetBuffer(kernel, DirtyFilmIndicesId,
                    films.DirtyIndices);
                publishSplitCompute.SetBuffer(kernel, FilmAllocatorId,
                    films.Allocator);
                publishSplitCompute.SetBuffer(kernel, SplitPlansId,
                    atlas.SplitPlans);
                publishSplitCompute.SetBuffer(kernel, SplitRecordIndicesId,
                    atlas.SplitRecordIndices);
                publishSplitCompute.SetBuffer(kernel, DisplacementPagesId,
                    displacement.PageHeaders);
                publishSplitCompute.SetBuffer(kernel, SplitStateId,
                    atlas.SplitState);
                publishSplitCompute.SetBuffer(kernel, SplitStateReadId,
                    atlas.SplitState);
                publishSplitCompute.SetBuffer(kernel, DisplacementAllocatorId,
                    displacement.Allocator);
            }
        }

        private static int CeilDiv(int value, int divisor) =>
            Math.Max(1, (value + divisor - 1) / divisor);
    }
}
