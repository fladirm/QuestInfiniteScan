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
        [SerializeField] private ComputeShader meshletBuildCompute;
        [SerializeField, Min(65536)] private int vertexBudget = 1500000;
        [SerializeField, Min(196608)] private int indexBudget = 6000000;
        [SerializeField, Range(1, 16)] private int maximumSubdivision = 16;
        [SerializeField, Min(0.00025f)] private float minimumTessellationError = 0.00075f;

        private static readonly int FilmCapacityId = Shader.PropertyToID("_FilmCapacity");
        private static readonly int FilmHeadersId = Shader.PropertyToID("_FilmHeaders");
        private static readonly int FilmAllocatorId = Shader.PropertyToID("_FilmAllocator");
        private static readonly int VerticesId = Shader.PropertyToID("_ContactVertices");
        private static readonly int IndicesId = Shader.PropertyToID("_ContactIndices");
        private static readonly int DrawArgumentsId = Shader.PropertyToID("_DrawArguments");
        private static readonly int MeshDispatchArgumentsId = Shader.PropertyToID("_MeshDispatchArguments");
        private static readonly int MeshletAllocatorId = Shader.PropertyToID("_MeshletAllocator");
        private static readonly int VertexCapacityId = Shader.PropertyToID("_VertexCapacity");
        private static readonly int IndexCapacityId = Shader.PropertyToID("_IndexCapacity");
        private static readonly int MaximumSubdivisionId = Shader.PropertyToID("_MaximumSubdivision");
        private static readonly int MinimumTessellationErrorId = Shader.PropertyToID("_MinimumTessellationError");
        private static readonly int HasBoundariesId = Shader.PropertyToID("_HasBoundaries");
        private static readonly int BoundaryHashMaskId = Shader.PropertyToID("_BoundaryHashMask");
        private static readonly int BoundaryCellsPerAxisId = Shader.PropertyToID("_BoundaryCellsPerAxis");
        private static readonly int BoundaryHeadersId = Shader.PropertyToID("_BoundaryHeaders");
        private static readonly int BoundaryHashId = Shader.PropertyToID("_BoundaryHash");

        private int _clearKernel = -1;
        private int _buildArgsKernel = -1;
        private int _buildKernel = -1;
        private int _finalizeKernel = -1;
        private GraphicsBuffer _dispatchArguments;
        private GraphicsBuffer _meshletAllocator;
        private bool _running;
        private uint _publicationGeneration;

        public void StartBuilding(PrismFilmSpawner films = null,
            PrismPredictionRenderer prediction = null,
            PrismBoundaryGraph boundaries = null)
        {
            if (_running) return;
            filmSpawner = films != null ? films : filmSpawner;
            predictionRenderer = prediction != null ? prediction : predictionRenderer;
            boundaryGraph = boundaries != null ? boundaries : boundaryGraph;
            filmSpawner ??= GetComponent<PrismFilmSpawner>();
            predictionRenderer ??= GetComponent<PrismPredictionRenderer>();
            boundaryGraph ??= GetComponent<PrismBoundaryGraph>();
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
            _dispatchArguments ??= new GraphicsBuffer(
                GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments,
                1, sizeof(uint) * 3);
            _meshletAllocator ??= new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                4, sizeof(uint));
            ContactFilmPool pool = filmSpawner.FilmPool;
            predictionRenderer.Meshlets.EnsureCapacity(
                vertexBudget, indexBudget);
            Bind(pool, predictionRenderer.Meshlets);
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
            _dispatchArguments?.Dispose();
            _meshletAllocator?.Dispose();
            _dispatchArguments = null;
            _meshletAllocator = null;
        }

        private void OnFilmsMutated(ContactFilmPool pool)
        {
            if (!_running || pool == null || pool.IsDisposed ||
                predictionRenderer?.Meshlets == null) return;
            try
            {
                ContactMeshletBuffers meshlets = predictionRenderer.Meshlets;
                Bind(pool, meshlets);
                meshletBuildCompute.Dispatch(_clearKernel, 1, 1, 1);
                meshletBuildCompute.Dispatch(_buildArgsKernel, 1, 1, 1);
                meshletBuildCompute.DispatchIndirect(_buildKernel,
                    _dispatchArguments, 0);
                meshletBuildCompute.Dispatch(_finalizeKernel, 1, 1, 1);
                _publicationGeneration = _publicationGeneration == uint.MaxValue
                    ? 1u
                    : _publicationGeneration + 1u;
                meshlets.MarkPublished(_publicationGeneration);
            }
            catch (Exception exception)
            {
                Logger.Error($"Cone-PRISM meshlet build failed: {exception.Message}");
            }
        }

        private void Bind(ContactFilmPool pool, ContactMeshletBuffers meshlets)
        {
            meshletBuildCompute.SetInt(FilmCapacityId, pool.Capacity);
            meshletBuildCompute.SetInt(VertexCapacityId, meshlets.VertexCapacity);
            meshletBuildCompute.SetInt(IndexCapacityId, meshlets.IndexCapacity);
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
            int[] kernels =
            {
                _clearKernel, _buildArgsKernel, _buildKernel, _finalizeKernel
            };
            foreach (int kernel in kernels)
            {
                meshletBuildCompute.SetBuffer(kernel, MeshletAllocatorId,
                    _meshletAllocator);
                meshletBuildCompute.SetBuffer(kernel, DrawArgumentsId,
                    meshlets.DrawArguments);
                meshletBuildCompute.SetBuffer(kernel, MeshDispatchArgumentsId,
                    _dispatchArguments);
            }
            meshletBuildCompute.SetBuffer(_buildArgsKernel, FilmAllocatorId,
                pool.Allocator);
            meshletBuildCompute.SetBuffer(_buildKernel, FilmHeadersId, pool.Headers);
            meshletBuildCompute.SetBuffer(_buildKernel, FilmAllocatorId, pool.Allocator);
            meshletBuildCompute.SetBuffer(_buildKernel, VerticesId, meshlets.Vertices);
            meshletBuildCompute.SetBuffer(_buildKernel, IndicesId, meshlets.Indices);
        }
    }
}
