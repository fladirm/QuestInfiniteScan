using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.Prism
{
    /// <summary>
    /// Immediate derived meshlet materialization for canonical ContactFilms. One GPU
    /// workgroup per film tessellates the continuous support domain adaptively from
    /// curvature, uncertainty, measured footprint, displacement and boundaries. The
    /// result closes prediction/association without making triangles canonical state.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(20)]
    public sealed class PrismMeshletBuilder : MonoBehaviour
    {
        private const int CountArgumentsOffset = 0;
        private const int ScanArgumentsOffset = sizeof(uint) * 3;
        private const int AddOffsetsArgumentsOffset = sizeof(uint) * 6;
        private const int EmitArgumentsOffset = sizeof(uint) * 9;
        private const int RecoveryArgumentsOffset = sizeof(uint) * 12;
        private const int BuildPlanStride = sizeof(uint) * 16;
        [SerializeField] private PrismFilmSpawner filmSpawner;
        [SerializeField] private PrismPredictionRenderer predictionRenderer;
        [SerializeField] private PrismBoundaryGraph boundaryGraph;
        [SerializeField] private PrismDisplacementTopology displacementTopology;
        [SerializeField] private ComputeShader meshletBuildCompute;
        [SerializeField, Min(65536)] private int vertexBudget = 3000000;
        [SerializeField, Min(196608)] private int indexBudget = 12000000;
        [SerializeField, Min(65536)] private int descriptorBudget = 262144;
        [SerializeField, Range(1, 16)] private int maximumSubdivision = 16;
        [SerializeField, Min(0.00025f)] private float minimumTessellationError = 0.00075f;
        [SerializeField, Range(5f, 30f)] private float maximumPublicationsPerSecond = 15f;

        private static readonly int FilmCapacityId = Shader.PropertyToID("_FilmCapacity");
        private static readonly int FilmHeadersId = Shader.PropertyToID("_FilmHeaders");
        private static readonly int FilmAllocatorId = Shader.PropertyToID("_FilmAllocator");
        private static readonly int ActiveFilmIndicesId =
            Shader.PropertyToID("_ActiveFilmIndices");
        private static readonly int DirtyFilmIndicesId =
            Shader.PropertyToID("_DirtyFilmIndices");
        private static readonly int FilmHeadersWriteId =
            Shader.PropertyToID("_FilmHeadersWrite");
        private static readonly int FilmSlotStatesId =
            Shader.PropertyToID("_FilmSlotStates");
        private static readonly int FilmAllocatorWriteId =
            Shader.PropertyToID("_FilmAllocatorWrite");
        private static readonly int VerticesId = Shader.PropertyToID("_ContactVertices");
        private static readonly int IndicesId = Shader.PropertyToID("_ContactIndices");
        private static readonly int DescriptorsId = Shader.PropertyToID("_MeshletDescriptors");
        private static readonly int FilmMeshletRangesId =
            Shader.PropertyToID("_FilmMeshletRanges");
        private static readonly int DrawArgumentsId = Shader.PropertyToID("_DrawArguments");
        private static readonly int MeshDispatchArgumentsId = Shader.PropertyToID("_MeshDispatchArguments");
        private static readonly int CullDispatchArgumentsId = Shader.PropertyToID("_CullDispatchArguments");
        private static readonly int MeshletAllocatorId = Shader.PropertyToID("_MeshletAllocator");
        private static readonly int VertexCapacityId = Shader.PropertyToID("_VertexCapacity");
        private static readonly int IndexCapacityId = Shader.PropertyToID("_IndexCapacity");
        private static readonly int DescriptorCapacityId = Shader.PropertyToID("_DescriptorCapacity");
        private static readonly int MaximumSubdivisionId = Shader.PropertyToID("_MaximumSubdivision");
        private static readonly int MinimumTessellationErrorId = Shader.PropertyToID("_MinimumTessellationError");
        private static readonly int HasBoundariesId = Shader.PropertyToID("_HasBoundaries");
        private static readonly int BoundaryHashMaskId = Shader.PropertyToID("_BoundaryHashMask");
        private static readonly int BoundaryCellsPerAxisId = Shader.PropertyToID("_BoundaryCellsPerAxis");
        private static readonly int BoundaryHeadersId = Shader.PropertyToID("_BoundaryHeaders");
        private static readonly int BoundaryHashId = Shader.PropertyToID("_BoundaryHash");
        private static readonly int BoundaryCurveCacheId =
            Shader.PropertyToID("_BoundaryCurveCache");
        private static readonly int ElasticChartStatesId =
            Shader.PropertyToID("_ElasticChartStates");
        private static readonly int HasDisplacementId = Shader.PropertyToID("_HasDisplacement");
        private static readonly int BasePageCapacityId = Shader.PropertyToID("_BasePageCapacity");
        private static readonly int MicroPageCapacityId = Shader.PropertyToID("_MicroPageCapacity");
        private static readonly int BaseCellCapacityId = Shader.PropertyToID("_BaseCellCapacity");
        private static readonly int MaximumMicroLevelsId = Shader.PropertyToID("_MaximumMicroLevels");
        private static readonly int DisplacementPagesId = Shader.PropertyToID("_DisplacementPages");
        private static readonly int BaseCellsId = Shader.PropertyToID("_BaseDisplacementCells");
        private static readonly int MicroCellsId = Shader.PropertyToID("_MicroDisplacementCells");
        private static readonly int BaseChildrenId = Shader.PropertyToID("_BaseChildPages");
        private static readonly int MicroChildrenId = Shader.PropertyToID("_MicroChildPages");
        private static readonly int BuildPlansId =
            Shader.PropertyToID("_MeshletBuildPlans");
        private static readonly int BuildGroupSumsId =
            Shader.PropertyToID("_MeshletBuildGroupSums");
        private static readonly int BuildPlanCapacityId =
            Shader.PropertyToID("_BuildPlanCapacity");
        private static readonly int BuildGroupCapacityId =
            Shader.PropertyToID("_BuildGroupCapacity");
        private static readonly int ManifoldDiagnosticsId =
            Shader.PropertyToID("_ManifoldDiagnostics");
        private static readonly int PreviousVerticesId =
            Shader.PropertyToID("_PreviousContactVertices");
        private static readonly int PreviousIndicesId =
            Shader.PropertyToID("_PreviousContactIndices");
        private static readonly int PreviousDescriptorsId =
            Shader.PropertyToID("_PreviousMeshletDescriptors");
        private static readonly int PreviousFilmMeshletRangesId =
            Shader.PropertyToID("_PreviousFilmMeshletRanges");
        private static readonly int PreviousAllocatorId =
            Shader.PropertyToID("_PreviousMeshletAllocator");
        private static readonly int PreviousDrawArgumentsId =
            Shader.PropertyToID("_PreviousDrawArguments");
        private static readonly int PreviousCullArgumentsId =
            Shader.PropertyToID("_PreviousCullDispatchArguments");
        private static readonly int PreviousVertexCapacityId =
            Shader.PropertyToID("_PreviousVertexCapacity");
        private static readonly int PreviousIndexCapacityId =
            Shader.PropertyToID("_PreviousIndexCapacity");
        private static readonly int PreviousDescriptorCapacityId =
            Shader.PropertyToID("_PreviousDescriptorCapacity");
        private static readonly int TransactionStateId =
            Shader.PropertyToID("_TransactionState");
        private static readonly int IncrementalDispatchArgumentsId =
            Shader.PropertyToID("_IncrementalDispatchArguments");
        private static readonly int BuildModeId = Shader.PropertyToID("_BuildMode");
        private static readonly int InPlacePublicationId =
            Shader.PropertyToID("_InPlacePublication");

        private int _clearKernel = -1;
        private int _buildArgsKernel = -1;
        private int _countKernel = -1;
        private int _scanPlansKernel = -1;
        private int _scanGroupsKernel = -1;
        private int _addOffsetsKernel = -1;
        private int _validateKernel = -1;
        private int _buildKernel = -1;
        private int _recoverKernel = -1;
        private int _finalizeKernel = -1;
        private int _prepareDirtyKernel = -1;
        private int _validateDirtyKernel = -1;
        private int _commitDirtyKernel = -1;
        private int _finalizeDirtyKernel = -1;
        private int _prepareFullRepackKernel = -1;
        private int _commitFullRepackKernel = -1;
        private GraphicsBuffer _buildPlans;
        private GraphicsBuffer _buildGroupSums;
        private GraphicsBuffer _transactionState;
        private GraphicsBuffer _incrementalDispatchArguments;
        private bool _running;
        private bool _subscribedToSource;
        private bool _rebuildPending;
        private uint _publicationGeneration;
        private float _nextPublicationTime;

        public void StartBuilding(PrismFilmSpawner films = null,
            PrismPredictionRenderer prediction = null,
            PrismBoundaryGraph boundaries = null,
            PrismDisplacementTopology displacement = null,
            bool subscribeToSource = true)
        {
            if (_running) return;
            filmSpawner = films != null ? films : filmSpawner;
            predictionRenderer = prediction != null ? prediction : predictionRenderer;
            boundaryGraph = boundaries != null ? boundaries : boundaryGraph;
            displacementTopology = displacement != null ? displacement :
                displacementTopology;
            filmSpawner ??= GetComponent<PrismFilmSpawner>();
            predictionRenderer ??= GetComponent<PrismPredictionRenderer>();
            boundaryGraph ??= GetComponent<PrismBoundaryGraph>();
            displacementTopology ??= GetComponent<PrismDisplacementTopology>();
            meshletBuildCompute ??= Resources.Load<ComputeShader>("Prism/MeshletBuild");
            if (filmSpawner?.FilmPool == null || predictionRenderer?.Meshlets == null ||
                meshletBuildCompute == null)
            {
                Logger.Error("Cone-PRISM meshlet builder dependencies are missing.");
                return;
            }
            _clearKernel = meshletBuildCompute.FindKernel("ClearMeshletBuild");
            _buildArgsKernel = meshletBuildCompute.FindKernel("BuildMeshDispatchArguments");
            _countKernel = meshletBuildCompute.FindKernel("CountMeshletBuildPlans");
            _scanPlansKernel = meshletBuildCompute.FindKernel("ScanMeshletBuildPlans");
            _scanGroupsKernel = meshletBuildCompute.FindKernel("ScanMeshletBuildGroups");
            _addOffsetsKernel = meshletBuildCompute.FindKernel(
                "AddMeshletBuildGroupOffsets");
            _validateKernel = meshletBuildCompute.FindKernel("ValidateMeshletBuild");
            _buildKernel = meshletBuildCompute.FindKernel("BuildAdaptiveFilmMeshlets");
            _recoverKernel = meshletBuildCompute.FindKernel(
                "RecoverPreviousMeshletGeneration");
            _finalizeKernel = meshletBuildCompute.FindKernel("FinalizeMeshletDrawArguments");
            _prepareDirtyKernel = meshletBuildCompute.FindKernel(
                "PrepareDirtyMeshletBuild");
            _validateDirtyKernel = meshletBuildCompute.FindKernel(
                "ValidateDirtyMeshletBuild");
            _commitDirtyKernel = meshletBuildCompute.FindKernel(
                "CommitDirtyMeshletBuild");
            _finalizeDirtyKernel = meshletBuildCompute.FindKernel(
                "FinalizeDirtyMeshletFilms");
            _prepareFullRepackKernel = meshletBuildCompute.FindKernel(
                "PrepareFullMeshletRepack");
            _commitFullRepackKernel = meshletBuildCompute.FindKernel(
                "CommitFullMeshletRepack");
            ContactFilmPool pool = filmSpawner.FilmPool;
            EnsurePlanBuffers(pool.Capacity);
            predictionRenderer.Meshlets.EnsureCapacity(
                vertexBudget, indexBudget,
                Math.Max(descriptorBudget, checked(pool.Capacity * 2)),
                pool.Capacity);
            if (subscribeToSource)
            {
                filmSpawner.FilmsMutated += OnFilmsMutated;
                _subscribedToSource = true;
            }
            _nextPublicationTime = 0f;
            _running = true;
        }

        public void StopBuilding()
        {
            if (_subscribedToSource && filmSpawner != null)
                filmSpawner.FilmsMutated -= OnFilmsMutated;
            _subscribedToSource = false;
            _running = false;
        }

        private void OnDestroy()
        {
            StopBuilding();
            _buildPlans?.Dispose();
            _buildGroupSums?.Dispose();
            _transactionState?.Dispose();
            _incrementalDispatchArguments?.Dispose();
            _buildPlans = null;
            _buildGroupSums = null;
            _transactionState = null;
            _incrementalDispatchArguments = null;
        }

        private void LateUpdate()
        {
            if (!_running || !_rebuildPending || filmSpawner?.FilmPool == null ||
                Time.unscaledTime < _nextPublicationTime) return;
            if (TryBuild(filmSpawner.FilmPool))
                _nextPublicationTime = Time.unscaledTime +
                    1f / Mathf.Max(1f, maximumPublicationsPerSecond);
        }

        private void OnFilmsMutated(ContactFilmPool pool)
        {
            // A reconstruction tick mutates the same canonical films in spawn,
            // information, boundary, displacement and topology passes. Rebuilding the
            // complete derived cache on every notification serialized several large
            // GPU publications ahead of the XR compositor. Coalesce them into the one
            // LateUpdate publication consumed by prediction on the next tick.
            _rebuildPending = true;
        }

        /// <summary>
        /// Marks the derived mesh cache dirty without publishing immediately. A
        /// reconstruction tick may touch the same manifold in several GPU passes;
        /// LateUpdate coalesces all of them into at most one inactive-generation
        /// build after sensor ingress has finished.
        /// </summary>
        internal void RequestBuild() => _rebuildPending = true;

        private bool TryBuild(ContactFilmPool pool)
        {
            if (!_running || pool == null || pool.IsDisposed ||
                predictionRenderer?.Meshlets == null) return false;
            try
            {
                ContactMeshletBuffers meshlets = predictionRenderer.Meshlets;
                return meshlets.PublicationGeneration == 0u
                    ? TryInitialPublication(pool, meshlets)
                    : TryIncrementalPublication(pool, meshlets);
            }
            catch (Exception exception)
            {
                Logger.Error($"Cone-PRISM meshlet build failed: {exception.Message}");
                return false;
            }
        }

        private bool TryInitialPublication(ContactFilmPool pool,
            ContactMeshletBuffers meshlets)
        {
                if (!meshlets.TryBeginBuild(out ContactMeshletGenerationBuffers target))
                    return false;
                ContactMeshletGenerationBuffers previous = meshlets.Published;
                Bind(pool, target, previous, false);
                meshletBuildCompute.SetInt(BuildModeId, 0);
                meshletBuildCompute.SetInt(InPlacePublicationId, 0);
                meshletBuildCompute.Dispatch(_clearKernel, 1, 1, 1);
                meshletBuildCompute.Dispatch(_buildArgsKernel, 1, 1, 1);
                meshletBuildCompute.DispatchIndirect(_countKernel,
                    target.BuildDispatchArguments, CountArgumentsOffset);
                meshletBuildCompute.DispatchIndirect(_scanPlansKernel,
                    target.BuildDispatchArguments, ScanArgumentsOffset);
                meshletBuildCompute.Dispatch(_scanGroupsKernel, 1, 1, 1);
                meshletBuildCompute.DispatchIndirect(_addOffsetsKernel,
                    target.BuildDispatchArguments, AddOffsetsArgumentsOffset);
                meshletBuildCompute.Dispatch(_validateKernel, 1, 1, 1);
                meshletBuildCompute.DispatchIndirect(_buildKernel,
                    target.BuildDispatchArguments, EmitArgumentsOffset);
                // ContactBoundary is image/world evidence on one film. Physical
                // connectors are emitted only from the typed ManifoldLink pool; the
                // former screen-neighbour boundary strips were able to bridge air.
                meshletBuildCompute.DispatchIndirect(_recoverKernel,
                    target.BuildDispatchArguments, RecoveryArgumentsOffset);
                meshletBuildCompute.Dispatch(_finalizeKernel, 1, 1, 1);
                _publicationGeneration = _publicationGeneration == uint.MaxValue
                    ? 1u
                    : _publicationGeneration + 1u;
                meshlets.Publish(_publicationGeneration);
                _rebuildPending = false;
                return true;
        }

        private bool TryIncrementalPublication(ContactFilmPool pool,
            ContactMeshletBuffers meshlets)
        {
            if (!meshlets.TryBeginPublishedWrite(
                out ContactMeshletGenerationBuffers target)) return false;
            Bind(pool, target, target, true);
            meshletBuildCompute.SetInt(InPlacePublicationId, 1);
            meshletBuildCompute.SetInt(BuildModeId, 1);
            meshletBuildCompute.Dispatch(_prepareDirtyKernel, 1, 1, 1);
            meshletBuildCompute.DispatchIndirect(_countKernel,
                _incrementalDispatchArguments, CountArgumentsOffset);
            meshletBuildCompute.DispatchIndirect(_validateDirtyKernel,
                _incrementalDispatchArguments, ScanArgumentsOffset);
            meshletBuildCompute.Dispatch(_commitDirtyKernel, 1, 1, 1);
            meshletBuildCompute.DispatchIndirect(_buildKernel,
                _incrementalDispatchArguments, AddOffsetsArgumentsOffset);

            // A topology/range incompatibility requests an entirely GPU-side,
            // capacity-validated repack. No CPU readback and no partial publication.
            meshletBuildCompute.SetInt(BuildModeId, 0);
            meshletBuildCompute.Dispatch(_prepareFullRepackKernel, 1, 1, 1);
            meshletBuildCompute.DispatchIndirect(_countKernel,
                target.BuildDispatchArguments, CountArgumentsOffset);
            meshletBuildCompute.DispatchIndirect(_scanPlansKernel,
                target.BuildDispatchArguments, ScanArgumentsOffset);
            meshletBuildCompute.Dispatch(_scanGroupsKernel, 1, 1, 1);
            meshletBuildCompute.DispatchIndirect(_addOffsetsKernel,
                target.BuildDispatchArguments, AddOffsetsArgumentsOffset);
            meshletBuildCompute.Dispatch(_validateKernel, 1, 1, 1);
            meshletBuildCompute.DispatchIndirect(_buildKernel,
                target.BuildDispatchArguments, EmitArgumentsOffset);
            meshletBuildCompute.Dispatch(_commitFullRepackKernel, 1, 1, 1);

            meshletBuildCompute.DispatchIndirect(_finalizeDirtyKernel,
                _incrementalDispatchArguments, EmitArgumentsOffset);
            meshletBuildCompute.Dispatch(_commitDirtyKernel, 1, 1, 1);
            _publicationGeneration = _publicationGeneration == uint.MaxValue
                ? 1u
                : _publicationGeneration + 1u;
            meshlets.MarkPublishedMutation(_publicationGeneration);
            _rebuildPending = false;
            return true;
        }

        private void Bind(ContactFilmPool pool,
            ContactMeshletGenerationBuffers meshlets,
            ContactMeshletGenerationBuffers previous,
            bool inPlace)
        {
            meshletBuildCompute.SetInt(FilmCapacityId, pool.Capacity);
            meshletBuildCompute.SetInt(VertexCapacityId, meshlets.VertexCapacity);
            meshletBuildCompute.SetInt(IndexCapacityId, meshlets.IndexCapacity);
            meshletBuildCompute.SetInt(DescriptorCapacityId,
                meshlets.DescriptorCapacity);
            meshletBuildCompute.SetInt(MaximumSubdivisionId, maximumSubdivision);
            meshletBuildCompute.SetFloat(MinimumTessellationErrorId,
                minimumTessellationError);
            meshletBuildCompute.SetInt(InPlacePublicationId, inPlace ? 1 : 0);
            PressureManifoldPool manifolds = pool.Manifolds;
            meshletBuildCompute.SetBuffer(_countKernel, ElasticChartStatesId,
                manifolds.ElasticStates);
            meshletBuildCompute.SetBuffer(_buildKernel, ElasticChartStatesId,
                manifolds.ElasticStates);
            ContactBoundaryPool boundaries = boundaryGraph?.BoundaryPool;
            bool hasBoundaries = boundaries != null && !boundaries.IsDisposed;
            meshletBuildCompute.SetInt(HasBoundariesId, hasBoundaries ? 1 : 0);
            if (hasBoundaries)
            {
                meshletBuildCompute.SetInt(BoundaryHashMaskId,
                    boundaries.HashCapacity - 1);
                meshletBuildCompute.SetInt(BoundaryCellsPerAxisId,
                    boundaryGraph.CellsPerAxis);
                meshletBuildCompute.SetBuffer(_buildKernel, BoundaryHeadersId,
                    boundaries.Headers);
                meshletBuildCompute.SetBuffer(_buildKernel, BoundaryHashId,
                    boundaries.HashEntries);
                meshletBuildCompute.SetBuffer(_buildKernel, BoundaryCurveCacheId,
                    boundaries.CurveCache);
            }
            ContactDisplacementPool displacement =
                displacementTopology?.DisplacementPool;
            bool hasDisplacement = displacement != null &&
                !displacement.IsDisposed;
            meshletBuildCompute.SetInt(HasDisplacementId,
                hasDisplacement ? 1 : 0);
            if (hasDisplacement)
            {
                meshletBuildCompute.SetInt(BasePageCapacityId,
                    displacement.BasePageCapacity);
                meshletBuildCompute.SetInt(MicroPageCapacityId,
                    displacement.MicroPageCapacity);
                meshletBuildCompute.SetInt(BaseCellCapacityId,
                    displacement.BaseCellCapacity);
                meshletBuildCompute.SetInt(MaximumMicroLevelsId,
                    displacementTopology.MaximumMicroLevels);
                int[] displacementReaders =
                {
                    _countKernel, _buildKernel
                };
                foreach (int kernel in displacementReaders)
                {
                    meshletBuildCompute.SetBuffer(kernel, DisplacementPagesId,
                        displacement.PageHeaders);
                    meshletBuildCompute.SetBuffer(kernel, BaseCellsId,
                        displacement.BaseCells);
                    meshletBuildCompute.SetBuffer(kernel, MicroCellsId,
                        displacement.MicroCells);
                    meshletBuildCompute.SetBuffer(kernel, BaseChildrenId,
                        displacement.BaseChildPages);
                    meshletBuildCompute.SetBuffer(kernel, MicroChildrenId,
                        displacement.MicroChildPages);
                }
            }
            int[] kernels =
            {
                _clearKernel, _buildArgsKernel, _countKernel, _scanPlansKernel,
                _scanGroupsKernel, _addOffsetsKernel, _validateKernel,
                _buildKernel, _recoverKernel, _finalizeKernel
            };
            foreach (int kernel in kernels)
            {
                meshletBuildCompute.SetBuffer(kernel, MeshletAllocatorId,
                    meshlets.BuildCounters);
                meshletBuildCompute.SetBuffer(kernel, DrawArgumentsId,
                    meshlets.DrawArguments);
                meshletBuildCompute.SetBuffer(kernel, MeshDispatchArgumentsId,
                    meshlets.BuildDispatchArguments);
                meshletBuildCompute.SetBuffer(kernel, CullDispatchArgumentsId,
                    meshlets.CullDispatchArguments);
            }
            meshletBuildCompute.SetBuffer(_clearKernel, ManifoldDiagnosticsId,
                pool.Manifolds.Diagnostics);
            meshletBuildCompute.SetBuffer(_buildArgsKernel, FilmAllocatorId,
                pool.Allocator);
            int[] activeReaders =
            {
                _countKernel, _scanPlansKernel, _scanGroupsKernel,
                _addOffsetsKernel, _validateKernel, _buildKernel
            };
            foreach (int kernel in activeReaders)
                meshletBuildCompute.SetBuffer(kernel, FilmAllocatorId,
                    pool.Allocator);
            meshletBuildCompute.SetBuffer(_countKernel, FilmHeadersId,
                pool.Headers);
            meshletBuildCompute.SetBuffer(_countKernel, ActiveFilmIndicesId,
                pool.ActiveIndices);
            meshletBuildCompute.SetBuffer(_countKernel, DirtyFilmIndicesId,
                pool.DirtyIndices);
            meshletBuildCompute.SetBuffer(_countKernel, FilmMeshletRangesId,
                meshlets.FilmRanges);
            int[] planKernels =
            {
                _countKernel, _scanPlansKernel, _scanGroupsKernel,
                _addOffsetsKernel, _validateKernel, _buildKernel
            };
            foreach (int kernel in planKernels)
            {
                meshletBuildCompute.SetBuffer(kernel, BuildPlansId, _buildPlans);
                meshletBuildCompute.SetBuffer(kernel, BuildGroupSumsId,
                    _buildGroupSums);
            }
            meshletBuildCompute.SetInt(BuildPlanCapacityId, _buildPlans.count);
            meshletBuildCompute.SetInt(BuildGroupCapacityId,
                _buildGroupSums.count);
            meshletBuildCompute.SetBuffer(_validateKernel,
                ManifoldDiagnosticsId, pool.Manifolds.Diagnostics);
            meshletBuildCompute.SetBuffer(_validateKernel, TransactionStateId,
                _transactionState);
            meshletBuildCompute.SetBuffer(_validateKernel, PreviousAllocatorId,
                previous.BuildCounters);
            meshletBuildCompute.SetInt(PreviousVertexCapacityId,
                previous.VertexCapacity);
            meshletBuildCompute.SetInt(PreviousIndexCapacityId,
                previous.IndexCapacity);
            meshletBuildCompute.SetInt(PreviousDescriptorCapacityId,
                previous.DescriptorCapacity);
            // The boundary graph is a mandatory production dependency. Keep a
            // valid zero-count neutral binding for focused builder contracts that
            // intentionally instantiate the mesh stage without it.
            meshletBuildCompute.SetBuffer(_buildKernel, FilmHeadersId, pool.Headers);
            meshletBuildCompute.SetBuffer(_buildKernel, FilmAllocatorId, pool.Allocator);
            meshletBuildCompute.SetBuffer(_buildKernel, ActiveFilmIndicesId,
                pool.ActiveIndices);
            meshletBuildCompute.SetBuffer(_buildKernel, DirtyFilmIndicesId,
                pool.DirtyIndices);
            meshletBuildCompute.SetBuffer(_buildKernel, TransactionStateId,
                _transactionState);
            meshletBuildCompute.SetBuffer(_buildKernel, VerticesId, meshlets.Vertices);
            meshletBuildCompute.SetBuffer(_buildKernel, IndicesId, meshlets.Indices);
            meshletBuildCompute.SetBuffer(_buildKernel, DescriptorsId,
                meshlets.Descriptors);
            meshletBuildCompute.SetBuffer(_buildKernel, FilmMeshletRangesId,
                meshlets.FilmRanges);
            meshletBuildCompute.SetBuffer(_buildKernel, ManifoldDiagnosticsId,
                manifolds.Diagnostics);
            meshletBuildCompute.SetBuffer(_recoverKernel, VerticesId,
                meshlets.Vertices);
            meshletBuildCompute.SetBuffer(_recoverKernel, IndicesId,
                meshlets.Indices);
            meshletBuildCompute.SetBuffer(_recoverKernel, DescriptorsId,
                meshlets.Descriptors);
            meshletBuildCompute.SetBuffer(_recoverKernel, FilmMeshletRangesId,
                meshlets.FilmRanges);
            meshletBuildCompute.SetBuffer(_recoverKernel, PreviousVerticesId,
                previous.Vertices);
            meshletBuildCompute.SetBuffer(_recoverKernel, PreviousIndicesId,
                previous.Indices);
            meshletBuildCompute.SetBuffer(_recoverKernel, PreviousDescriptorsId,
                previous.Descriptors);
            meshletBuildCompute.SetBuffer(_recoverKernel,
                PreviousFilmMeshletRangesId, previous.FilmRanges);
            meshletBuildCompute.SetBuffer(_recoverKernel, PreviousAllocatorId,
                previous.BuildCounters);
            meshletBuildCompute.SetBuffer(_recoverKernel,
                PreviousDrawArgumentsId, previous.DrawArguments);
            meshletBuildCompute.SetBuffer(_recoverKernel,
                PreviousCullArgumentsId, previous.CullDispatchArguments);

            meshletBuildCompute.SetBuffer(_prepareDirtyKernel, FilmAllocatorId,
                pool.Allocator);
            meshletBuildCompute.SetBuffer(_prepareDirtyKernel,
                TransactionStateId, _transactionState);
            meshletBuildCompute.SetBuffer(_prepareDirtyKernel,
                IncrementalDispatchArgumentsId, _incrementalDispatchArguments);

            meshletBuildCompute.SetBuffer(_validateDirtyKernel,
                TransactionStateId, _transactionState);
            meshletBuildCompute.SetBuffer(_validateDirtyKernel,
                DirtyFilmIndicesId, pool.DirtyIndices);
            meshletBuildCompute.SetBuffer(_validateDirtyKernel, BuildPlansId,
                _buildPlans);
            meshletBuildCompute.SetBuffer(_validateDirtyKernel, FilmHeadersId,
                pool.Headers);
            meshletBuildCompute.SetBuffer(_validateDirtyKernel,
                FilmMeshletRangesId, meshlets.FilmRanges);

            meshletBuildCompute.SetBuffer(_commitDirtyKernel,
                TransactionStateId, _transactionState);
            meshletBuildCompute.SetBuffer(_commitDirtyKernel,
                IncrementalDispatchArgumentsId, _incrementalDispatchArguments);
            meshletBuildCompute.SetBuffer(_commitDirtyKernel,
                ManifoldDiagnosticsId, pool.Manifolds.Diagnostics);
            meshletBuildCompute.SetBuffer(_commitDirtyKernel,
                FilmAllocatorWriteId, pool.Allocator);

            meshletBuildCompute.SetBuffer(_prepareFullRepackKernel,
                TransactionStateId, _transactionState);
            meshletBuildCompute.SetBuffer(_prepareFullRepackKernel,
                MeshDispatchArgumentsId, meshlets.BuildDispatchArguments);
            meshletBuildCompute.SetBuffer(_prepareFullRepackKernel,
                FilmAllocatorId, pool.Allocator);

            meshletBuildCompute.SetBuffer(_commitFullRepackKernel,
                TransactionStateId, _transactionState);
            meshletBuildCompute.SetBuffer(_commitFullRepackKernel,
                MeshletAllocatorId, meshlets.BuildCounters);
            meshletBuildCompute.SetBuffer(_commitFullRepackKernel,
                DrawArgumentsId, meshlets.DrawArguments);
            meshletBuildCompute.SetBuffer(_commitFullRepackKernel,
                CullDispatchArgumentsId, meshlets.CullDispatchArguments);
            meshletBuildCompute.SetBuffer(_commitFullRepackKernel,
                FilmAllocatorId, pool.Allocator);
            meshletBuildCompute.SetBuffer(_commitFullRepackKernel,
                IncrementalDispatchArgumentsId, _incrementalDispatchArguments);

            meshletBuildCompute.SetBuffer(_finalizeDirtyKernel,
                TransactionStateId, _transactionState);
            meshletBuildCompute.SetBuffer(_finalizeDirtyKernel,
                DirtyFilmIndicesId, pool.DirtyIndices);
            meshletBuildCompute.SetBuffer(_finalizeDirtyKernel,
                FilmHeadersWriteId, pool.Headers);
            meshletBuildCompute.SetBuffer(_finalizeDirtyKernel,
                FilmSlotStatesId, pool.SlotStates);
        }

        private void EnsurePlanBuffers(int filmCapacity)
        {
            int capacity = Math.Max(1, filmCapacity);
            int groups = Math.Max(1, (capacity + 255) / 256);
            if (_buildPlans != null && _buildPlans.count == capacity &&
                _buildGroupSums != null && _buildGroupSums.count == groups &&
                _transactionState != null &&
                _incrementalDispatchArguments != null) return;
            _buildPlans?.Dispose();
            _buildGroupSums?.Dispose();
            _transactionState?.Dispose();
            _incrementalDispatchArguments?.Dispose();
            _buildPlans = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                capacity, BuildPlanStride);
            _buildGroupSums = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                groups, sizeof(uint) * 4);
            _transactionState = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                8, sizeof(uint));
            _incrementalDispatchArguments = new GraphicsBuffer(
                GraphicsBuffer.Target.IndirectArguments |
                GraphicsBuffer.Target.Structured, 5, sizeof(uint) * 3);
            _transactionState.SetData(new uint[8]);
            _incrementalDispatchArguments.SetData(new uint[15]);
        }
    }
}
