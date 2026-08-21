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
        [SerializeField] private PrismFilmSpawner filmSpawner;
        [SerializeField] private PrismPredictionRenderer predictionRenderer;
        [SerializeField] private PrismBoundaryGraph boundaryGraph;
        [SerializeField] private PrismDisplacementTopology displacementTopology;
        [SerializeField] private ComputeShader meshletBuildCompute;
        [SerializeField, Min(65536)] private int vertexBudget = 1500000;
        [SerializeField, Min(196608)] private int indexBudget = 6000000;
        [SerializeField, Min(65536)] private int descriptorBudget = 131072;
        [SerializeField, Range(1, 16)] private int maximumSubdivision = 16;
        [SerializeField, Min(0.00025f)] private float minimumTessellationError = 0.00075f;
        [SerializeField, Range(5f, 30f)] private float maximumPublicationsPerSecond = 15f;

        private static readonly int FilmCapacityId = Shader.PropertyToID("_FilmCapacity");
        private static readonly int FilmHeadersId = Shader.PropertyToID("_FilmHeaders");
        private static readonly int FilmAllocatorId = Shader.PropertyToID("_FilmAllocator");
        private static readonly int VerticesId = Shader.PropertyToID("_ContactVertices");
        private static readonly int IndicesId = Shader.PropertyToID("_ContactIndices");
        private static readonly int DescriptorsId = Shader.PropertyToID("_MeshletDescriptors");
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
        private static readonly int BoundaryInformationId =
            Shader.PropertyToID("_BoundaryInformation");
        private static readonly int BoundaryAllocatorId =
            Shader.PropertyToID("_BoundaryAllocator");
        private static readonly int BoundaryHashId = Shader.PropertyToID("_BoundaryHash");
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

        private int _clearKernel = -1;
        private int _buildArgsKernel = -1;
        private int _buildKernel = -1;
        private int _buildConnectorKernel = -1;
        private int _finalizeKernel = -1;
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
            _buildKernel = meshletBuildCompute.FindKernel("BuildAdaptiveFilmMeshlets");
            _buildConnectorKernel =
                meshletBuildCompute.FindKernel("BuildElasticBoundaryMeshlets");
            _finalizeKernel = meshletBuildCompute.FindKernel("FinalizeMeshletDrawArguments");
            ContactFilmPool pool = filmSpawner.FilmPool;
            predictionRenderer.Meshlets.EnsureCapacity(
                vertexBudget, indexBudget, descriptorBudget);
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
                if (!meshlets.TryBeginBuild(out ContactMeshletGenerationBuffers target))
                    return false;
                Bind(pool, target);
                meshletBuildCompute.Dispatch(_clearKernel, 1, 1, 1);
                meshletBuildCompute.Dispatch(_buildArgsKernel, 1, 1, 1);
                meshletBuildCompute.DispatchIndirect(_buildKernel,
                    target.BuildDispatchArguments, 0);
                ContactBoundaryPool boundaries = boundaryGraph?.BoundaryPool;
                if (boundaries != null && !boundaries.IsDisposed)
                    meshletBuildCompute.DispatchIndirect(_buildConnectorKernel,
                        target.BuildDispatchArguments, sizeof(uint) * 3);
                meshletBuildCompute.Dispatch(_finalizeKernel, 1, 1, 1);
                _publicationGeneration = _publicationGeneration == uint.MaxValue
                    ? 1u
                    : _publicationGeneration + 1u;
                meshlets.Publish(_publicationGeneration);
                _rebuildPending = false;
                return true;
            }
            catch (Exception exception)
            {
                Logger.Error($"Cone-PRISM meshlet build failed: {exception.Message}");
                return false;
            }
        }

        private void Bind(ContactFilmPool pool, ContactMeshletGenerationBuffers meshlets)
        {
            meshletBuildCompute.SetInt(FilmCapacityId, pool.Capacity);
            meshletBuildCompute.SetInt(VertexCapacityId, meshlets.VertexCapacity);
            meshletBuildCompute.SetInt(IndexCapacityId, meshlets.IndexCapacity);
            meshletBuildCompute.SetInt(DescriptorCapacityId,
                meshlets.DescriptorCapacity);
            meshletBuildCompute.SetInt(MaximumSubdivisionId, maximumSubdivision);
            meshletBuildCompute.SetFloat(MinimumTessellationErrorId,
                minimumTessellationError);
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
                meshletBuildCompute.SetBuffer(_buildConnectorKernel,
                    BoundaryHeadersId, boundaries.Headers);
                meshletBuildCompute.SetBuffer(_buildConnectorKernel,
                    BoundaryInformationId, boundaries.Information);
                meshletBuildCompute.SetBuffer(_buildConnectorKernel,
                    BoundaryAllocatorId, boundaries.Allocator);
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
                meshletBuildCompute.SetBuffer(_buildKernel, DisplacementPagesId,
                    displacement.PageHeaders);
                meshletBuildCompute.SetBuffer(_buildKernel, BaseCellsId,
                    displacement.BaseCells);
                meshletBuildCompute.SetBuffer(_buildKernel, MicroCellsId,
                    displacement.MicroCells);
                meshletBuildCompute.SetBuffer(_buildKernel, BaseChildrenId,
                    displacement.BaseChildPages);
                meshletBuildCompute.SetBuffer(_buildKernel, MicroChildrenId,
                    displacement.MicroChildPages);
            }
            int[] kernels =
            {
                _clearKernel, _buildArgsKernel, _buildKernel,
                _buildConnectorKernel, _finalizeKernel
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
            meshletBuildCompute.SetBuffer(_buildArgsKernel, FilmAllocatorId,
                pool.Allocator);
            // The boundary graph is a mandatory production dependency. Keep a
            // valid zero-count fallback binding for focused builder contracts that
            // intentionally instantiate the mesh stage without it.
            meshletBuildCompute.SetBuffer(_buildArgsKernel, BoundaryAllocatorId,
                hasBoundaries ? boundaries.Allocator : pool.Allocator);
            meshletBuildCompute.SetBuffer(_buildKernel, FilmHeadersId, pool.Headers);
            meshletBuildCompute.SetBuffer(_buildKernel, FilmAllocatorId, pool.Allocator);
            meshletBuildCompute.SetBuffer(_buildKernel, VerticesId, meshlets.Vertices);
            meshletBuildCompute.SetBuffer(_buildKernel, IndicesId, meshlets.Indices);
            meshletBuildCompute.SetBuffer(_buildKernel, DescriptorsId,
                meshlets.Descriptors);
            meshletBuildCompute.SetBuffer(_buildConnectorKernel, FilmHeadersId,
                pool.Headers);
            meshletBuildCompute.SetBuffer(_buildConnectorKernel, VerticesId,
                meshlets.Vertices);
            meshletBuildCompute.SetBuffer(_buildConnectorKernel, IndicesId,
                meshlets.Indices);
            meshletBuildCompute.SetBuffer(_buildConnectorKernel, DescriptorsId,
                meshlets.Descriptors);
        }
    }
}
