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
        [SerializeField] private ComputeShader chunkTopologyStageCompute;
        [SerializeField] private ComputeShader crossChunkPortalCompute;

        private static readonly int TargetChunkId = Shader.PropertyToID("_TargetChunkId");
        private static readonly int SourceFilmCapacity = Shader.PropertyToID("_SourceFilmCapacity");
        private static readonly int SourceBoundaryCapacity = Shader.PropertyToID("_SourceBoundaryCapacity");
        private static readonly int SourceBasePageCapacity = Shader.PropertyToID("_SourceBasePageCapacity");
        private static readonly int SourceMicroPageCapacity = Shader.PropertyToID("_SourceMicroPageCapacity");
        private static readonly int SourceDescriptorCapacity = Shader.PropertyToID("_SourceDescriptorCapacity");
        private static readonly int StageFilmCapacity = Shader.PropertyToID("_StageFilmCapacity");
        private static readonly int StageBoundaryCapacity = Shader.PropertyToID("_StageBoundaryCapacity");
        private static readonly int StageBasePageCapacity = Shader.PropertyToID("_StageBasePageCapacity");
        private static readonly int StageMicroPageCapacity = Shader.PropertyToID("_StageMicroPageCapacity");
        private static readonly int StageVertexCapacity = Shader.PropertyToID("_StageVertexCapacity");
        private static readonly int StageIndexCapacity = Shader.PropertyToID("_StageIndexCapacity");
        private static readonly int StageDescriptorCapacity = Shader.PropertyToID("_StageDescriptorCapacity");

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
        private GraphicsBuffer _topologyDispatchArguments;
        private readonly GpuResourceRetirementQueue _gpuRetirement = new();
        private int[] _kernels;
        private int[] _topologyKernels;
        private int _preparePortalExport;
        private int _clearPortalPlans;
        private int _planPortalCandidates;
        private int _reservePortalPairs;
        private int _commitPortalPairs;
        private int _preparePortalBootstrap;
        private int _bootstrapManifoldIdentity;
        private int _bootstrapPortalGhosts;
        private uint _lastStagedSourceChunkId;
        private bool _busy;

        public bool IsBusy => _busy;

        internal async Task<PrismCanonicalChunkSnapshot> StageAsync(uint chunkId,
            ContactFilmPool films, ContactBoundaryPool boundaries,
            ContactDisplacementPool displacement, ContactMeshletBuffers meshlets,
            ulong calibrationEpoch, byte[] appearanceState = null,
            byte[] observationState = null, string[] keyframeReferences = null,
            PrismChunkTopologyTransition? topologyTransition = null,
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
            chunkTopologyStageCompute ??=
                Resources.Load<ComputeShader>("Prism/ChunkTopologyStage");
            crossChunkPortalCompute ??=
                Resources.Load<ComputeShader>("Prism/CrossChunkPortalUpdate");
            if (chunkStageCompute == null || chunkTopologyStageCompute == null ||
                crossChunkPortalCompute == null)
                throw new InvalidOperationException(
                    "Canonical PRISM chunk staging resources are missing.");

            _busy = true;
            try
            {
                EnsureResources(films, boundaries, displacement, meshlets);
                if (topologyTransition.HasValue &&
                    topologyTransition.Value.IsValid)
                    ExportCrossChunkPortals(topologyTransition.Value, films);
                Bind(chunkId, films, boundaries, displacement, meshlets.Published);
                BindTopology(chunkId, films, boundaries);
                Dispatch();
                DispatchTopology();
                // The readback requests are inserted after every stage dispatch on the
                // graphics queue. Staging arenas remain untouched until all requests
                // finish, which makes the detached revision internally coherent.
                _stageMeshlets.Generation = Math.Max(1u,
                    meshlets.PublicationGeneration);
                _lastStagedSourceChunkId = chunkId;
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
            _topologyDispatchArguments = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured |
                GraphicsBuffer.Target.IndirectArguments, 7, sizeof(uint) * 3);
            CacheKernels();
        }

        private void CacheKernels()
        {
            string[] names =
            {
                "PrepareChunkStage", "ClearFilmRemap", "StageFilms",
                "StageBoundaries", "ClearPageRemap", "IndexBasePages",
                "IndexMicroPages", "CopyBasePages", "CopyMicroPages",
                "PatchFilmDisplacement", "StageMeshlets", "FinalizeChunkStage"
            };
            _kernels = new int[names.Length];
            for (int i = 0; i < names.Length; i++)
                _kernels[i] = chunkStageCompute.FindKernel(names[i]);
            string[] topologyNames =
            {
                "PrepareTopologyStage", "StageManifoldHeaders",
                "StageFilmTopology", "StageContourPages",
                "StageContourTopology", "StageFrontierLoops",
                "StageBoundaryTopology", "StageCrossChunkPortals"
            };
            _topologyKernels = new int[topologyNames.Length];
            for (int i = 0; i < topologyNames.Length; i++)
                _topologyKernels[i] =
                    chunkTopologyStageCompute.FindKernel(topologyNames[i]);
            _preparePortalExport = crossChunkPortalCompute.FindKernel(
                "PreparePortalExport");
            _clearPortalPlans = crossChunkPortalCompute.FindKernel(
                "ClearPortalPlans");
            _planPortalCandidates = crossChunkPortalCompute.FindKernel(
                "PlanPortalCandidates");
            _reservePortalPairs = crossChunkPortalCompute.FindKernel(
                "ReservePortalPairs");
            _commitPortalPairs = crossChunkPortalCompute.FindKernel(
                "CommitPortalPairs");
            _preparePortalBootstrap = crossChunkPortalCompute.FindKernel(
                "PreparePortalBootstrap");
            _bootstrapManifoldIdentity = crossChunkPortalCompute.FindKernel(
                "BootstrapManifoldIdentity");
            _bootstrapPortalGhosts = crossChunkPortalCompute.FindKernel(
                "BootstrapPortalGhosts");
        }

        private void ExportCrossChunkPortals(
            PrismChunkTopologyTransition transition, ContactFilmPool films)
        {
            PressureManifoldPool atlas = films.Manifolds;
            crossChunkPortalCompute.SetInt("_FilmCapacity", films.Capacity);
            crossChunkPortalCompute.SetInt("_ContourSegmentCapacity",
                atlas.ContourSegmentCapacity);
            crossChunkPortalCompute.SetInt("_HalfEdgeCapacity",
                atlas.HalfEdgeCapacity);
            crossChunkPortalCompute.SetInt("_PortalCapacity", atlas.PortalCapacity);
            crossChunkPortalCompute.SetInt("_SourceChunkId",
                unchecked((int)transition.SourceChunkId));
            crossChunkPortalCompute.SetInt("_TargetChunkId",
                unchecked((int)transition.TargetChunkId));
            crossChunkPortalCompute.SetVector("_OwnershipPlaneInSource",
                transition.OwnershipPlaneInSource);
            crossChunkPortalCompute.SetFloat("_OverlapBandMeters",
                transition.OverlapBandMeters);
            crossChunkPortalCompute.SetMatrix("_TargetFromSource",
                transition.TargetFromSource);
            int[] kernels =
            {
                _preparePortalExport, _clearPortalPlans,
                _planPortalCandidates, _reservePortalPairs, _commitPortalPairs
            };
            foreach (int kernel in kernels)
            {
                crossChunkPortalCompute.SetBuffer(kernel, "_FilmHeaders",
                    films.Headers);
                crossChunkPortalCompute.SetBuffer(kernel, "_FilmInformation",
                    films.Information);
                crossChunkPortalCompute.SetBuffer(kernel, "_SupportContours",
                    atlas.SupportContours);
                crossChunkPortalCompute.SetBuffer(kernel, "_HalfEdges",
                    atlas.HalfEdges);
                crossChunkPortalCompute.SetBuffer(kernel, "_CrossChunkPortals",
                    atlas.CrossChunkPortals);
                crossChunkPortalCompute.SetBuffer(kernel, "_AtlasAllocator",
                    atlas.AtlasAllocator);
                crossChunkPortalCompute.SetBuffer(kernel, "_PortalPlans",
                    atlas.PortalPlans);
                crossChunkPortalCompute.SetBuffer(kernel, "_PortalState",
                    atlas.PortalState);
                crossChunkPortalCompute.SetBuffer(kernel,
                    "_PortalDispatchArguments", atlas.PortalDispatchArguments);
            }
            crossChunkPortalCompute.Dispatch(_preparePortalExport, 1, 1, 1);
            crossChunkPortalCompute.DispatchIndirect(_clearPortalPlans,
                atlas.PortalDispatchArguments, 0);
            crossChunkPortalCompute.DispatchIndirect(_planPortalCandidates,
                atlas.PortalDispatchArguments, 0);
            crossChunkPortalCompute.Dispatch(_reservePortalPairs, 1, 1, 1);
            crossChunkPortalCompute.DispatchIndirect(_commitPortalPairs,
                atlas.PortalDispatchArguments, sizeof(uint) * 3);
        }

        /// <summary>
        /// Carries only global component identity and generation-safe topology ghosts
        /// into the newly resident storage frame. No measured source chart is copied
        /// and no remote endpoint is converted into a latent physical frontier.
        /// </summary>
        internal void BootstrapTargetTopology(
            PrismChunkTopologyTransition transition, ContactFilmPool targetFilms)
        {
            if (!transition.IsValid || _stageFilms == null ||
                _stageFilms.IsDisposed || targetFilms == null ||
                targetFilms.IsDisposed ||
                _lastStagedSourceChunkId != transition.SourceChunkId)
                throw new InvalidOperationException(
                    "The staged source topology does not match this chunk transition.");
            PressureManifoldPool source = _stageFilms.Manifolds;
            PressureManifoldPool target = targetFilms.Manifolds;
            crossChunkPortalCompute.SetInt("_ManifoldCapacity",
                target.ManifoldCapacity);
            crossChunkPortalCompute.SetInt("_PortalCapacity", target.PortalCapacity);
            crossChunkPortalCompute.SetInt("_TargetChunkId",
                unchecked((int)transition.TargetChunkId));
            int[] kernels =
            {
                _preparePortalBootstrap, _bootstrapManifoldIdentity,
                _bootstrapPortalGhosts
            };
            foreach (int kernel in kernels)
            {
                crossChunkPortalCompute.SetBuffer(kernel,
                    "_SourceManifoldHeaders", source.Headers);
                crossChunkPortalCompute.SetBuffer(kernel,
                    "_SourceManifoldAllocator", source.Allocator);
                crossChunkPortalCompute.SetBuffer(kernel,
                    "_SourceCurrentManifold", source.Current);
                crossChunkPortalCompute.SetBuffer(kernel,
                    "_SourceAtlasAllocator", source.AtlasAllocator);
                crossChunkPortalCompute.SetBuffer(kernel,
                    "_SourceCrossChunkPortals", source.CrossChunkPortals);
                crossChunkPortalCompute.SetBuffer(kernel,
                    "_TargetManifoldHeaders", target.Headers);
                crossChunkPortalCompute.SetBuffer(kernel,
                    "_TargetManifoldAllocator", target.Allocator);
                crossChunkPortalCompute.SetBuffer(kernel,
                    "_TargetCurrentManifold", target.Current);
                crossChunkPortalCompute.SetBuffer(kernel,
                    "_TargetAtlasAllocator", target.AtlasAllocator);
                crossChunkPortalCompute.SetBuffer(kernel,
                    "_TargetCrossChunkPortals", target.CrossChunkPortals);
                crossChunkPortalCompute.SetBuffer(kernel,
                    "_PortalDispatchArguments", target.PortalDispatchArguments);
            }
            crossChunkPortalCompute.Dispatch(_preparePortalBootstrap, 1, 1, 1);
            crossChunkPortalCompute.Dispatch(_bootstrapManifoldIdentity, 1, 1, 1);
            crossChunkPortalCompute.DispatchIndirect(_bootstrapPortalGhosts,
                target.PortalDispatchArguments, sizeof(uint) * 6);
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
            DispatchIndirect(3, 1);
            DispatchIndirect(4, 2);
            DispatchIndirect(4, 3);
            DispatchIndirect(5, 2);
            DispatchIndirect(6, 3);
            DispatchIndirect(7, 2);
            DispatchIndirect(8, 3);
            DispatchIndirect(9, 0);
            DispatchIndirect(10, 4);
            chunkStageCompute.Dispatch(_kernels[11], 1, 1, 1);
        }

        private void BindTopology(uint chunkId, ContactFilmPool films,
            ContactBoundaryPool boundaries)
        {
            PressureManifoldPool source = films.Manifolds;
            PressureManifoldPool stage = _stageFilms.Manifolds;
            chunkTopologyStageCompute.SetInt("_TargetChunkId",
                unchecked((int)chunkId));
            chunkTopologyStageCompute.SetInt("_FilmCapacity", films.Capacity);
            chunkTopologyStageCompute.SetInt("_ManifoldCapacity",
                source.ManifoldCapacity);
            chunkTopologyStageCompute.SetInt("_ContourPageCapacity",
                source.ContourPageCapacity);
            chunkTopologyStageCompute.SetInt("_ContourSegmentCapacity",
                source.ContourSegmentCapacity);
            chunkTopologyStageCompute.SetInt("_BoundaryCapacity",
                boundaries.Capacity);
            chunkTopologyStageCompute.SetInt("_PortalCapacity",
                source.PortalCapacity);
            foreach (int kernel in _topologyKernels)
            {
                SetTopology(kernel, "_SourceManifoldHeaders", source.Headers);
                SetTopology(kernel, "_SourceFilmMemberships", source.Memberships);
                SetTopology(kernel, "_SourceManifoldAllocator", source.Allocator);
                SetTopology(kernel, "_SourceCurrentManifold", source.Current);
                SetTopology(kernel, "_SourceFilmAllocator", films.Allocator);
                SetTopology(kernel, "_SourceFilmHeaders", films.Headers);
                SetTopology(kernel, "_SourceSupportContourPages",
                    source.SupportContourPages);
                SetTopology(kernel, "_SourceSupportContours", source.SupportContours);
                SetTopology(kernel, "_SourceHalfEdges", source.HalfEdges);
                SetTopology(kernel, "_SourceFrontierLoops", source.FrontierLoops);
                SetTopology(kernel, "_SourceContinuationEvidence",
                    source.ContinuationEvidence);
                SetTopology(kernel, "_SourceElasticStates", source.ElasticStates);
                SetTopology(kernel, "_SourceFilmTopologyRanges",
                    source.FilmTopologyRanges);
                SetTopology(kernel, "_SourceAtlasAllocator", source.AtlasAllocator);
                SetTopology(kernel, "_SourceCrossChunkPortals",
                    source.CrossChunkPortals);
                SetTopology(kernel, "_SourceBoundaryTopology", boundaries.Topology);
                SetTopology(kernel, "_SourceBoundaryHeaders", boundaries.Headers);
                SetTopology(kernel, "_SourceBoundaryAllocator", boundaries.Allocator);
                SetTopology(kernel, "_StageManifoldHeaders", stage.Headers);
                SetTopology(kernel, "_StageFilmMemberships", stage.Memberships);
                SetTopology(kernel, "_StageManifoldAllocator", stage.Allocator);
                SetTopology(kernel, "_StageCurrentManifold", stage.Current);
                SetTopology(kernel, "_StageSupportContourPages",
                    stage.SupportContourPages);
                SetTopology(kernel, "_StageSupportContours", stage.SupportContours);
                SetTopology(kernel, "_StageHalfEdges", stage.HalfEdges);
                SetTopology(kernel, "_StageFrontierLoops", stage.FrontierLoops);
                SetTopology(kernel, "_StageContinuationEvidence",
                    stage.ContinuationEvidence);
                SetTopology(kernel, "_StageElasticStates", stage.ElasticStates);
                SetTopology(kernel, "_StageFilmTopologyRanges",
                    stage.FilmTopologyRanges);
                SetTopology(kernel, "_StageAtlasAllocator", stage.AtlasAllocator);
                SetTopology(kernel, "_StageCrossChunkPortals",
                    stage.CrossChunkPortals);
                SetTopology(kernel, "_StageBoundaryTopology",
                    _stageBoundaries.Topology);
                SetTopology(kernel, "_StageBoundaryCurveCache",
                    _stageBoundaries.CurveCache);
                SetTopology(kernel, "_StageTopologyDirtyFlags",
                    stage.TopologyDirtyFlags);
                SetTopology(kernel, "_TopologyStageArguments",
                    _topologyDispatchArguments);
            }
        }

        private void DispatchTopology()
        {
            chunkTopologyStageCompute.Dispatch(_topologyKernels[0], 1, 1, 1);
            for (int kernel = 1; kernel < _topologyKernels.Length; kernel++)
                chunkTopologyStageCompute.DispatchIndirect(_topologyKernels[kernel],
                    _topologyDispatchArguments,
                    checked((uint)((kernel - 1) * sizeof(uint) * 3)));
        }

        private void DispatchIndirect(int kernelIndex, int argumentIndex) =>
            chunkStageCompute.DispatchIndirect(_kernels[kernelIndex],
                _dispatchArguments,
                checked((uint)(argumentIndex * sizeof(uint) * 3)));

        private void Set(int kernel, int property, GraphicsBuffer buffer) =>
            chunkStageCompute.SetBuffer(kernel, property, buffer);

        private void SetTopology(int kernel, string property,
            GraphicsBuffer buffer) =>
            chunkTopologyStageCompute.SetBuffer(kernel, property, buffer);

        private void LateUpdate() => _gpuRetirement.DrainCompleted();

        private void OnDestroy()
        {
            DisposeResources();
            _gpuRetirement.DrainAndWait();
        }

        private void DisposeResources()
        {
            _gpuRetirement.RetireAfterCurrentGpuWork(_stageFilms);
            _gpuRetirement.RetireAfterCurrentGpuWork(_stageBoundaries);
            _gpuRetirement.RetireAfterCurrentGpuWork(_stageDisplacement);
            _gpuRetirement.RetireAfterCurrentGpuWork(_stageMeshlets);
            _gpuRetirement.RetireAfterCurrentGpuWork(_filmRemap);
            _gpuRetirement.RetireAfterCurrentGpuWork(_basePageRemap);
            _gpuRetirement.RetireAfterCurrentGpuWork(_microPageRemap);
            _gpuRetirement.RetireAfterCurrentGpuWork(_dispatchArguments);
            _gpuRetirement.RetireAfterCurrentGpuWork(_topologyDispatchArguments);
            _stageFilms = null;
            _stageBoundaries = null;
            _stageDisplacement = null;
            _stageMeshlets = null;
            _filmRemap = null;
            _basePageRemap = null;
            _microPageRemap = null;
            _dispatchArguments = null;
            _topologyDispatchArguments = null;
            _kernels = null;
            _topologyKernels = null;
            _lastStagedSourceChunkId = 0u;
        }
    }
}
