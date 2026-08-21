using System;
using System.Threading;
using System.Threading.Tasks;
using Genesis.RoomScan.Prism;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.World
{
    /// <summary>
    /// GPU-compacts one chunk from the live append-only pools into immutable local-ID
    /// staging arenas. Async readback and disk encoding consume only these arenas, so
    /// capture/fusion may continue mutating the live world without tearing a revision.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PrismChunkSnapshotStager : MonoBehaviour
    {
        [SerializeField] private ComputeShader chunkStageCompute;

        private static readonly int TargetChunkId = Shader.PropertyToID("_TargetChunkId");
        private static readonly int SourceFilmCapacity = Shader.PropertyToID("_SourceFilmCapacity");
        private static readonly int SourceBoundaryCapacity = Shader.PropertyToID("_SourceBoundaryCapacity");
        private static readonly int SourceBasePageCapacity = Shader.PropertyToID("_SourceBasePageCapacity");
        private static readonly int SourceMicroPageCapacity = Shader.PropertyToID("_SourceMicroPageCapacity");
        private static readonly int SourceDescriptorCapacity = Shader.PropertyToID("_SourceDescriptorCapacity");
        private static readonly int SourceLinkCapacity = Shader.PropertyToID("_SourceLinkCapacity");
        private static readonly int SourceFrontierCapacity = Shader.PropertyToID("_SourceFrontierCapacity");
        private static readonly int SourceManifoldCapacity = Shader.PropertyToID("_SourceManifoldCapacity");
        private static readonly int StageFilmCapacity = Shader.PropertyToID("_StageFilmCapacity");
        private static readonly int StageBoundaryCapacity = Shader.PropertyToID("_StageBoundaryCapacity");
        private static readonly int StageBasePageCapacity = Shader.PropertyToID("_StageBasePageCapacity");
        private static readonly int StageMicroPageCapacity = Shader.PropertyToID("_StageMicroPageCapacity");
        private static readonly int StageVertexCapacity = Shader.PropertyToID("_StageVertexCapacity");
        private static readonly int StageIndexCapacity = Shader.PropertyToID("_StageIndexCapacity");
        private static readonly int StageDescriptorCapacity = Shader.PropertyToID("_StageDescriptorCapacity");
        private static readonly int StageManifoldCapacity = Shader.PropertyToID("_StageManifoldCapacity");
        private static readonly int StageLinkCapacity = Shader.PropertyToID("_StageLinkCapacity");
        private static readonly int StageFrontierCapacity = Shader.PropertyToID("_StageFrontierCapacity");

        private static readonly int SourceFilmHeaders = Shader.PropertyToID("_SourceFilmHeaders");
        private static readonly int SourceFilmInformation = Shader.PropertyToID("_SourceFilmInformation");
        private static readonly int SourceFilmAllocator = Shader.PropertyToID("_SourceFilmAllocator");
        private static readonly int SourceBoundaryHeaders = Shader.PropertyToID("_SourceBoundaryHeaders");
        private static readonly int SourceBoundaryInformation = Shader.PropertyToID("_SourceBoundaryInformation");
        private static readonly int SourceBoundaryAllocator = Shader.PropertyToID("_SourceBoundaryAllocator");
        private static readonly int SourceDisplacementPages = Shader.PropertyToID("_SourceDisplacementPages");
        private static readonly int SourceBaseCells = Shader.PropertyToID("_SourceBaseCells");
        private static readonly int SourceMicroCells = Shader.PropertyToID("_SourceMicroCells");
        private static readonly int SourceBaseChildren = Shader.PropertyToID("_SourceBaseChildren");
        private static readonly int SourceMicroChildren = Shader.PropertyToID("_SourceMicroChildren");
        private static readonly int SourceTopologyEvidence = Shader.PropertyToID("_SourceTopologyEvidence");
        private static readonly int SourceDisplacementAllocator = Shader.PropertyToID("_SourceDisplacementAllocator");
        private static readonly int SourceMeshletVertices = Shader.PropertyToID("_SourceMeshletVertices");
        private static readonly int SourceMeshletIndices = Shader.PropertyToID("_SourceMeshletIndices");
        private static readonly int SourceMeshletDescriptors = Shader.PropertyToID("_SourceMeshletDescriptors");
        private static readonly int SourceMeshletCounters = Shader.PropertyToID("_SourceMeshletCounters");
        private static readonly int SourceFilmMemberships = Shader.PropertyToID("_SourceFilmMemberships");
        private static readonly int SourceManifoldHeaders = Shader.PropertyToID("_SourceManifoldHeaders");
        private static readonly int SourceManifoldLinks = Shader.PropertyToID("_SourceManifoldLinks");
        private static readonly int SourceManifoldLinkIncidences = Shader.PropertyToID("_SourceManifoldLinkIncidences");
        private static readonly int SourceManifoldFrontierIncidences =
            Shader.PropertyToID("_SourceManifoldFrontierIncidences");
        private static readonly int SourceLatentFrontiers = Shader.PropertyToID("_SourceLatentFrontiers");
        private static readonly int SourceManifoldAllocator = Shader.PropertyToID("_SourceManifoldAllocator");
        private static readonly int SourceCurrentManifold = Shader.PropertyToID("_SourceCurrentManifold");

        private static readonly int StageFilmHeaders = Shader.PropertyToID("_StageFilmHeaders");
        private static readonly int StageFilmInformation = Shader.PropertyToID("_StageFilmInformation");
        private static readonly int StageFilmAllocator = Shader.PropertyToID("_StageFilmAllocator");
        private static readonly int StageBoundaryHeaders = Shader.PropertyToID("_StageBoundaryHeaders");
        private static readonly int StageBoundaryInformation = Shader.PropertyToID("_StageBoundaryInformation");
        private static readonly int StageBoundaryAllocator = Shader.PropertyToID("_StageBoundaryAllocator");
        private static readonly int StageDisplacementPages = Shader.PropertyToID("_StageDisplacementPages");
        private static readonly int StageBaseCells = Shader.PropertyToID("_StageBaseCells");
        private static readonly int StageMicroCells = Shader.PropertyToID("_StageMicroCells");
        private static readonly int StageBaseChildren = Shader.PropertyToID("_StageBaseChildren");
        private static readonly int StageMicroChildren = Shader.PropertyToID("_StageMicroChildren");
        private static readonly int StageTopologyEvidence = Shader.PropertyToID("_StageTopologyEvidence");
        private static readonly int StageDisplacementAllocator = Shader.PropertyToID("_StageDisplacementAllocator");
        private static readonly int StageMeshletVertices = Shader.PropertyToID("_StageMeshletVertices");
        private static readonly int StageMeshletIndices = Shader.PropertyToID("_StageMeshletIndices");
        private static readonly int StageMeshletDescriptors = Shader.PropertyToID("_StageMeshletDescriptors");
        private static readonly int StageMeshletCounters = Shader.PropertyToID("_StageMeshletCounters");
        private static readonly int StageMeshletDrawArguments = Shader.PropertyToID("_StageMeshletDrawArguments");
        private static readonly int StageMeshletCullArguments = Shader.PropertyToID("_StageMeshletCullArguments");
        private static readonly int StageFilmSlotStates = Shader.PropertyToID("_StageFilmSlotStates");
        private static readonly int StageActiveFilmIndices = Shader.PropertyToID("_StageActiveFilmIndices");
        private static readonly int StageDirtyFilmIndices = Shader.PropertyToID("_StageDirtyFilmIndices");
        private static readonly int StageManifoldHeaders = Shader.PropertyToID("_StageManifoldHeaders");
        private static readonly int StageFilmMemberships = Shader.PropertyToID("_StageFilmMemberships");
        private static readonly int StageManifoldLinks = Shader.PropertyToID("_StageManifoldLinks");
        private static readonly int StageManifoldLinkIncidences = Shader.PropertyToID("_StageManifoldLinkIncidences");
        private static readonly int StageManifoldFrontierIncidences =
            Shader.PropertyToID("_StageManifoldFrontierIncidences");
        private static readonly int StageLatentFrontiers = Shader.PropertyToID("_StageLatentFrontiers");
        private static readonly int StageManifoldAllocator = Shader.PropertyToID("_StageManifoldAllocator");
        private static readonly int StageCurrentManifold = Shader.PropertyToID("_StageCurrentManifold");
        private static readonly int FilmRemap = Shader.PropertyToID("_FilmRemap");
        private static readonly int BasePageRemap = Shader.PropertyToID("_BasePageRemap");
        private static readonly int MicroPageRemap = Shader.PropertyToID("_MicroPageRemap");
        private static readonly int StageDispatchArguments = Shader.PropertyToID("_StageDispatchArguments");

        private ContactFilmPool _stageFilms;
        private ContactBoundaryPool _stageBoundaries;
        private ContactDisplacementPool _stageDisplacement;
        private ContactMeshletGenerationBuffers _stageMeshlets;
        private GraphicsBuffer _filmRemap;
        private GraphicsBuffer _basePageRemap;
        private GraphicsBuffer _microPageRemap;
        private GraphicsBuffer _dispatchArguments;
        private int[] _kernels;
        private bool _busy;

        public bool IsBusy => _busy;

        public async Task<PrismCanonicalChunkSnapshot> StageAsync(uint chunkId,
            ContactFilmPool films, ContactBoundaryPool boundaries,
            ContactDisplacementPool displacement, ContactMeshletBuffers meshlets,
            ulong calibrationEpoch, byte[] appearanceState = null,
            byte[] observationState = null, string[] keyframeReferences = null,
            CancellationToken cancellationToken = default)
        {
            if (_busy) throw new InvalidOperationException(
                "A PRISM chunk snapshot is already being staged.");
            if (films == null || films.IsDisposed || boundaries == null ||
                boundaries.IsDisposed || displacement == null ||
                displacement.IsDisposed || meshlets == null || meshlets.IsDisposed)
                throw new ArgumentException("Live canonical PRISM pools are required.");
            cancellationToken.ThrowIfCancellationRequested();
            chunkStageCompute ??= Resources.Load<ComputeShader>("Prism/ChunkStage");
            if (chunkStageCompute == null)
                throw new InvalidOperationException("ChunkStage.compute is missing.");

            _busy = true;
            try
            {
                EnsureResources(films, boundaries, displacement, meshlets);
                Bind(chunkId, films, boundaries, displacement, meshlets.Published);
                Dispatch();
                // The readback requests are inserted after every stage dispatch on the
                // graphics queue. Staging arenas remain untouched until all requests
                // finish, which makes the detached revision internally coherent.
                _stageMeshlets.Generation = Math.Max(1u,
                    meshlets.PublicationGeneration);
                return await PrismGpuSnapshotCapture.CaptureStagedAsync(
                    _stageFilms, _stageBoundaries, _stageDisplacement,
                    _stageMeshlets, calibrationEpoch, appearanceState,
                    observationState, keyframeReferences, cancellationToken);
            }
            finally
            {
                _busy = false;
            }
        }

        private void EnsureResources(ContactFilmPool films,
            ContactBoundaryPool boundaries, ContactDisplacementPool displacement,
            ContactMeshletBuffers meshlets)
        {
            bool compatible = _stageFilms != null && !_stageFilms.IsDisposed &&
                _stageFilms.Capacity >= films.Capacity &&
                _stageBoundaries != null && !_stageBoundaries.IsDisposed &&
                _stageBoundaries.Capacity >= boundaries.Capacity &&
                _stageDisplacement != null && !_stageDisplacement.IsDisposed &&
                _stageDisplacement.BasePageCapacity >= displacement.BasePageCapacity &&
                _stageDisplacement.MicroPageCapacity >= displacement.MicroPageCapacity &&
                _stageMeshlets != null && !_stageMeshlets.IsDisposed &&
                _stageMeshlets.VertexCapacity >= meshlets.VertexCapacity &&
                _stageMeshlets.IndexCapacity >= meshlets.IndexCapacity &&
                _stageMeshlets.DescriptorCapacity >= meshlets.DescriptorCapacity;
            if (compatible) return;
            DisposeResources();
            _stageFilms = new ContactFilmPool(films.Capacity);
            _stageBoundaries = new ContactBoundaryPool(boundaries.Capacity,
                boundaries.HashCapacity);
            _stageDisplacement = new ContactDisplacementPool(films.Capacity,
                displacement.BasePageCapacity, displacement.MicroPageCapacity);
            _stageMeshlets = new ContactMeshletGenerationBuffers(
                meshlets.VertexCapacity, meshlets.IndexCapacity,
                meshlets.DescriptorCapacity);
            _filmRemap = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                films.Capacity, sizeof(uint));
            _basePageRemap = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                displacement.BasePageCapacity, sizeof(uint));
            _microPageRemap = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                displacement.MicroPageCapacity, sizeof(uint));
            _dispatchArguments = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured |
                GraphicsBuffer.Target.IndirectArguments, 6, sizeof(uint) * 3);
            CacheKernels();
        }

        private void CacheKernels()
        {
            string[] names =
            {
                "PrepareChunkStage", "ClearFilmRemap", "StageFilms",
                "StageBoundaries", "ClearPageRemap", "IndexBasePages",
                "IndexMicroPages", "CopyBasePages", "CopyMicroPages",
                "PatchFilmDisplacement", "StageMeshlets",
                "InitializeStageManifold", "StageFilmMemberships",
                "OrderStageFilmFrontiers", "StageManifoldLinks",
                "CloseMissingFilmFrontiers",
                "FinalizeStageManifold", "FinalizeChunkStage"
            };
            _kernels = new int[names.Length];
            for (int i = 0; i < names.Length; i++)
                _kernels[i] = chunkStageCompute.FindKernel(names[i]);
        }

        private void Bind(uint chunkId, ContactFilmPool films,
            ContactBoundaryPool boundaries, ContactDisplacementPool displacement,
            ContactMeshletGenerationBuffers meshlets)
        {
            chunkStageCompute.SetInt(TargetChunkId, unchecked((int)chunkId));
            chunkStageCompute.SetInt(SourceFilmCapacity, films.Capacity);
            chunkStageCompute.SetInt(SourceBoundaryCapacity, boundaries.Capacity);
            chunkStageCompute.SetInt(SourceBasePageCapacity,
                displacement.BasePageCapacity);
            chunkStageCompute.SetInt(SourceMicroPageCapacity,
                displacement.MicroPageCapacity);
            chunkStageCompute.SetInt(SourceDescriptorCapacity,
                meshlets.DescriptorCapacity);
            chunkStageCompute.SetInt(SourceLinkCapacity,
                films.Manifolds.LinkCapacity);
            chunkStageCompute.SetInt(SourceFrontierCapacity,
                films.Manifolds.FrontierCapacity);
            chunkStageCompute.SetInt(SourceManifoldCapacity,
                films.Manifolds.ManifoldCapacity);
            chunkStageCompute.SetInt(StageFilmCapacity, _stageFilms.Capacity);
            chunkStageCompute.SetInt(StageBoundaryCapacity,
                _stageBoundaries.Capacity);
            chunkStageCompute.SetInt(StageBasePageCapacity,
                _stageDisplacement.BasePageCapacity);
            chunkStageCompute.SetInt(StageMicroPageCapacity,
                _stageDisplacement.MicroPageCapacity);
            chunkStageCompute.SetInt(StageVertexCapacity,
                _stageMeshlets.VertexCapacity);
            chunkStageCompute.SetInt(StageIndexCapacity,
                _stageMeshlets.IndexCapacity);
            chunkStageCompute.SetInt(StageDescriptorCapacity,
                _stageMeshlets.DescriptorCapacity);
            chunkStageCompute.SetInt(StageManifoldCapacity,
                _stageFilms.Manifolds.ManifoldCapacity);
            chunkStageCompute.SetInt(StageLinkCapacity,
                _stageFilms.Manifolds.LinkCapacity);
            chunkStageCompute.SetInt(StageFrontierCapacity,
                _stageFilms.Manifolds.FrontierCapacity);
            foreach (int kernel in _kernels)
            {
                Set(kernel, SourceFilmHeaders, films.Headers);
                Set(kernel, SourceFilmInformation, films.Information);
                Set(kernel, SourceFilmAllocator, films.Allocator);
                Set(kernel, SourceBoundaryHeaders, boundaries.Headers);
                Set(kernel, SourceBoundaryInformation, boundaries.Information);
                Set(kernel, SourceBoundaryAllocator, boundaries.Allocator);
                Set(kernel, SourceDisplacementPages, displacement.PageHeaders);
                Set(kernel, SourceBaseCells, displacement.BaseCells);
                Set(kernel, SourceMicroCells, displacement.MicroCells);
                Set(kernel, SourceBaseChildren, displacement.BaseChildPages);
                Set(kernel, SourceMicroChildren, displacement.MicroChildPages);
                Set(kernel, SourceTopologyEvidence, displacement.TopologyEvidence);
                Set(kernel, SourceDisplacementAllocator, displacement.Allocator);
                Set(kernel, SourceMeshletVertices, meshlets.Vertices);
                Set(kernel, SourceMeshletIndices, meshlets.Indices);
                Set(kernel, SourceMeshletDescriptors, meshlets.Descriptors);
                Set(kernel, SourceMeshletCounters, meshlets.BuildCounters);
                Set(kernel, SourceFilmMemberships,
                    films.Manifolds.Memberships);
                Set(kernel, SourceManifoldHeaders, films.Manifolds.Headers);
                Set(kernel, SourceManifoldLinks, films.Manifolds.Links);
                Set(kernel, SourceManifoldLinkIncidences,
                    films.Manifolds.LinkIncidences);
                Set(kernel, SourceManifoldFrontierIncidences,
                    films.Manifolds.FrontierIncidences);
                Set(kernel, SourceLatentFrontiers, films.Manifolds.Frontiers);
                Set(kernel, SourceManifoldAllocator, films.Manifolds.Allocator);
                Set(kernel, SourceCurrentManifold, films.Manifolds.Current);
                Set(kernel, StageFilmHeaders, _stageFilms.Headers);
                Set(kernel, StageFilmInformation, _stageFilms.Information);
                Set(kernel, StageFilmAllocator, _stageFilms.Allocator);
                Set(kernel, StageBoundaryHeaders, _stageBoundaries.Headers);
                Set(kernel, StageBoundaryInformation, _stageBoundaries.Information);
                Set(kernel, StageBoundaryAllocator, _stageBoundaries.Allocator);
                Set(kernel, StageDisplacementPages,
                    _stageDisplacement.PageHeaders);
                Set(kernel, StageBaseCells, _stageDisplacement.BaseCells);
                Set(kernel, StageMicroCells, _stageDisplacement.MicroCells);
                Set(kernel, StageBaseChildren,
                    _stageDisplacement.BaseChildPages);
                Set(kernel, StageMicroChildren,
                    _stageDisplacement.MicroChildPages);
                Set(kernel, StageTopologyEvidence,
                    _stageDisplacement.TopologyEvidence);
                Set(kernel, StageDisplacementAllocator,
                    _stageDisplacement.Allocator);
                Set(kernel, StageMeshletVertices, _stageMeshlets.Vertices);
                Set(kernel, StageMeshletIndices, _stageMeshlets.Indices);
                Set(kernel, StageMeshletDescriptors, _stageMeshlets.Descriptors);
                Set(kernel, StageMeshletCounters, _stageMeshlets.BuildCounters);
                Set(kernel, StageMeshletDrawArguments,
                    _stageMeshlets.DrawArguments);
                Set(kernel, StageMeshletCullArguments,
                    _stageMeshlets.CullDispatchArguments);
                Set(kernel, StageFilmSlotStates, _stageFilms.SlotStates);
                Set(kernel, StageActiveFilmIndices, _stageFilms.ActiveIndices);
                Set(kernel, StageDirtyFilmIndices, _stageFilms.DirtyIndices);
                Set(kernel, StageManifoldHeaders,
                    _stageFilms.Manifolds.Headers);
                Set(kernel, StageFilmMemberships,
                    _stageFilms.Manifolds.Memberships);
                Set(kernel, StageManifoldLinks, _stageFilms.Manifolds.Links);
                Set(kernel, StageManifoldLinkIncidences,
                    _stageFilms.Manifolds.LinkIncidences);
                Set(kernel, StageManifoldFrontierIncidences,
                    _stageFilms.Manifolds.FrontierIncidences);
                Set(kernel, StageLatentFrontiers,
                    _stageFilms.Manifolds.Frontiers);
                Set(kernel, StageManifoldAllocator,
                    _stageFilms.Manifolds.Allocator);
                Set(kernel, StageCurrentManifold,
                    _stageFilms.Manifolds.Current);
                Set(kernel, FilmRemap, _filmRemap);
                Set(kernel, BasePageRemap, _basePageRemap);
                Set(kernel, MicroPageRemap, _microPageRemap);
                Set(kernel, StageDispatchArguments, _dispatchArguments);
            }
        }

        private void Dispatch()
        {
            chunkStageCompute.Dispatch(_kernels[0], 1, 1, 1);
            DispatchIndirect(1, 0);
            DispatchIndirect(2, 0);
            chunkStageCompute.Dispatch(_kernels[11], 1, 1, 1);
            DispatchIndirect(12, 0);
            DispatchIndirect(14, 5);
            DispatchIndirect(15, 0);
            DispatchIndirect(13, 0);
            chunkStageCompute.Dispatch(_kernels[16], 1, 1, 1);
            DispatchIndirect(3, 1);
            DispatchIndirect(4, 2);
            DispatchIndirect(4, 3);
            DispatchIndirect(5, 2);
            DispatchIndirect(6, 3);
            DispatchIndirect(7, 2);
            DispatchIndirect(8, 3);
            DispatchIndirect(9, 0);
            DispatchIndirect(10, 4);
            chunkStageCompute.Dispatch(_kernels[17], 1, 1, 1);
        }

        private void DispatchIndirect(int kernelIndex, int argumentIndex) =>
            chunkStageCompute.DispatchIndirect(_kernels[kernelIndex],
                _dispatchArguments,
                checked((uint)(argumentIndex * sizeof(uint) * 3)));

        private void Set(int kernel, int property, GraphicsBuffer buffer) =>
            chunkStageCompute.SetBuffer(kernel, property, buffer);

        private void OnDestroy() => DisposeResources();

        private void DisposeResources()
        {
            _stageFilms?.Dispose();
            _stageBoundaries?.Dispose();
            _stageDisplacement?.Dispose();
            _stageMeshlets?.Dispose();
            _filmRemap?.Dispose();
            _basePageRemap?.Dispose();
            _microPageRemap?.Dispose();
            _dispatchArguments?.Dispose();
            _stageFilms = null;
            _stageBoundaries = null;
            _stageDisplacement = null;
            _stageMeshlets = null;
            _filmRemap = null;
            _basePageRemap = null;
            _microPageRemap = null;
            _dispatchArguments = null;
            _kernels = null;
        }
    }
}
