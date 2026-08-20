using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.Prism
{
    /// <summary>
    /// Immediate derived meshlet materialization for canonical ContactFilms. This first
    /// stage emits one analytic quad per film so prediction closes the online loop;
    /// Q3-12 replaces density with boundary/curvature/screen-error adaptive tessellation.
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
        private int _finalizeKernel = -1;
        private bool _running;
        private bool _rebuildPending;
        private uint _publicationGeneration;

        public void StartBuilding(PrismFilmSpawner films = null,
            PrismPredictionRenderer prediction = null,
            PrismBoundaryGraph boundaries = null,
            PrismDisplacementTopology displacement = null)
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
            _finalizeKernel = meshletBuildCompute.FindKernel("FinalizeMeshletDrawArguments");
            ContactFilmPool pool = filmSpawner.FilmPool;
            predictionRenderer.Meshlets.EnsureCapacity(
                vertexBudget, indexBudget, descriptorBudget);
            filmSpawner.FilmsMutated += OnFilmsMutated;
            _running = true;
        }

        public void StopBuilding()
        {
            if (_running && filmSpawner != null)
                filmSpawner.FilmsMutated -= OnFilmsMutated;
            _running = false;
        }

        private void OnDestroy()
        {
            StopBuilding();
        }

        private void LateUpdate()
        {
            if (_running && _rebuildPending && filmSpawner?.FilmPool != null)
                TryBuild(filmSpawner.FilmPool);
        }

        private void OnFilmsMutated(ContactFilmPool pool)
        {
            _rebuildPending = true;
            TryBuild(pool);
        }

        private void TryBuild(ContactFilmPool pool)
        {
            if (!_running || pool == null || pool.IsDisposed ||
                predictionRenderer?.Meshlets == null) return;
            try
            {
                ContactMeshletBuffers meshlets = predictionRenderer.Meshlets;
                if (!meshlets.TryBeginBuild(out ContactMeshletGenerationBuffers target))
                    return;
                Bind(pool, target);
                meshletBuildCompute.Dispatch(_clearKernel, 1, 1, 1);
                meshletBuildCompute.Dispatch(_buildArgsKernel, 1, 1, 1);
                meshletBuildCompute.DispatchIndirect(_buildKernel,
                    target.BuildDispatchArguments, 0);
                meshletBuildCompute.Dispatch(_finalizeKernel, 1, 1, 1);
                _publicationGeneration = _publicationGeneration == uint.MaxValue
                    ? 1u
                    : _publicationGeneration + 1u;
                meshlets.Publish(_publicationGeneration);
                _rebuildPending = false;
            }
            catch (Exception exception)
            {
                Logger.Error($"Cone-PRISM meshlet build failed: {exception.Message}");
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
                _clearKernel, _buildArgsKernel, _buildKernel, _finalizeKernel
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
            meshletBuildCompute.SetBuffer(_buildKernel, FilmHeadersId, pool.Headers);
            meshletBuildCompute.SetBuffer(_buildKernel, FilmAllocatorId, pool.Allocator);
            meshletBuildCompute.SetBuffer(_buildKernel, VerticesId, meshlets.Vertices);
            meshletBuildCompute.SetBuffer(_buildKernel, IndicesId, meshlets.Indices);
            meshletBuildCompute.SetBuffer(_buildKernel, DescriptorsId,
                meshlets.Descriptors);
        }
    }
}
