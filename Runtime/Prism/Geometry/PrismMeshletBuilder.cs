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
        [SerializeField] private ComputeShader meshletBuildCompute;

        private static readonly int FilmCapacityId = Shader.PropertyToID("_FilmCapacity");
        private static readonly int FilmHeadersId = Shader.PropertyToID("_FilmHeaders");
        private static readonly int FilmAllocatorId = Shader.PropertyToID("_FilmAllocator");
        private static readonly int VerticesId = Shader.PropertyToID("_ContactVertices");
        private static readonly int IndicesId = Shader.PropertyToID("_ContactIndices");
        private static readonly int DrawArgumentsId = Shader.PropertyToID("_DrawArguments");
        private static readonly int MeshDispatchArgumentsId = Shader.PropertyToID("_MeshDispatchArguments");

        private int _buildArgsKernel = -1;
        private int _buildKernel = -1;
        private GraphicsBuffer _dispatchArguments;
        private bool _running;
        private uint _publicationGeneration;

        public void StartBuilding(PrismFilmSpawner films = null,
            PrismPredictionRenderer prediction = null)
        {
            if (_running) return;
            filmSpawner = films != null ? films : filmSpawner;
            predictionRenderer = prediction != null ? prediction : predictionRenderer;
            filmSpawner ??= GetComponent<PrismFilmSpawner>();
            predictionRenderer ??= GetComponent<PrismPredictionRenderer>();
            meshletBuildCompute ??= Resources.Load<ComputeShader>("Prism/MeshletBuild");
            if (filmSpawner?.FilmPool == null || predictionRenderer?.Meshlets == null ||
                meshletBuildCompute == null)
            {
                Logger.Error("Cone-PRISM meshlet builder dependencies are missing.");
                return;
            }
            _buildArgsKernel = meshletBuildCompute.FindKernel("BuildMeshDispatchArguments");
            _buildKernel = meshletBuildCompute.FindKernel("BuildFilmQuads");
            _dispatchArguments ??= new GraphicsBuffer(
                GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments,
                1, sizeof(uint) * 3);
            ContactFilmPool pool = filmSpawner.FilmPool;
            predictionRenderer.Meshlets.EnsureCapacity(
                checked(pool.Capacity * 4), checked(pool.Capacity * 6));
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
            _dispatchArguments = null;
        }

        private void OnFilmsMutated(ContactFilmPool pool)
        {
            if (!_running || pool == null || pool.IsDisposed ||
                predictionRenderer?.Meshlets == null) return;
            try
            {
                ContactMeshletBuffers meshlets = predictionRenderer.Meshlets;
                Bind(pool, meshlets);
                meshletBuildCompute.Dispatch(_buildArgsKernel, 1, 1, 1);
                meshletBuildCompute.DispatchIndirect(_buildKernel,
                    _dispatchArguments, 0);
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
            meshletBuildCompute.SetBuffer(_buildArgsKernel, FilmAllocatorId,
                pool.Allocator);
            meshletBuildCompute.SetBuffer(_buildArgsKernel, DrawArgumentsId,
                meshlets.DrawArguments);
            meshletBuildCompute.SetBuffer(_buildArgsKernel, MeshDispatchArgumentsId,
                _dispatchArguments);
            meshletBuildCompute.SetBuffer(_buildKernel, FilmHeadersId, pool.Headers);
            meshletBuildCompute.SetBuffer(_buildKernel, FilmAllocatorId, pool.Allocator);
            meshletBuildCompute.SetBuffer(_buildKernel, VerticesId, meshlets.Vertices);
            meshletBuildCompute.SetBuffer(_buildKernel, IndicesId, meshlets.Indices);
        }
    }
}
