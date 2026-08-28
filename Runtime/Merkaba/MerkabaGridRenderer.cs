using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan
{
    /// <summary>
    /// Rebuilds only queued dirty resident chunks into persistent compact triangle
    /// records, then draws exactly three vertices per published canonical primitive.
    /// A clean frame submits zero topology/publication groups.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MerkabaGridRenderer : MonoBehaviour
    {
        [SerializeField] private ComputeShader topologyCompute;
        [SerializeField] private Shader renderShader;
        [SerializeField, Range(2f, 12f)] private float renderDistance = 8f;
        [SerializeField, Range(0f, 1f)] private float scanOpacity = 1f;

        private sealed class RetiredPublication
        {
            public readonly MerkabaPublicationBuffer Buffer;
            public readonly GraphicsFence ProtectingFence;
            public bool ReleaseSupported;

            public RetiredPublication(MerkabaPublicationBuffer buffer,
                GraphicsFence protectingFence, bool releaseSupported)
            {
                Buffer = buffer;
                ProtectingFence = protectingFence;
                ReleaseSupported = releaseSupported;
            }
        }

        private MerkabaGrid _grid;
        private Material _material;
        private MaterialPropertyBlock _drawProperties;
        private int _rebuildKernel = -1;
        private int _finalizeKernel = -1;
        private int _migrateKernel = -1;
        private bool _initialized;
        private bool _statusReadbackPending;
        private bool _statusSampleRequested;
        private int _statusPartsPending;
        private float _nextStatusSampleTime;
        private uint[] _lastPublicationVersions;
        private uint _requestedResizeRequirement;
        private bool _resizeMigrationPending;
        private MerkabaPublicationBuffer _replacementPublication;
        private uint _replacementRequirement;
        private GraphicsFence _migrationFence;
        private GraphicsFence _latestSourceUseFence;
        private bool _migrationFenceValid;
        private bool _latestSourceUseFenceValid;
        private bool _sourceRetirementUnsafe;
        private bool _publicationResizeDisabled;
        private ShadowCastingMode _shadowCastingMode = ShadowCastingMode.On;
        private readonly List<RetiredPublication> _retiredPublications = new();

        private const float StatusSampleIntervalSeconds = 1f;

        public int VisiblePrimitiveCount { get; private set; }
        public int VisibleSurfaceKernelCount => VisiblePrimitiveCount;
        public int PublicationOverflowChunkCount { get; private set; }
        public uint PeakPrimitiveRequirement { get; private set; }
        public int PublicationPrimitiveCapacity =>
            _grid != null && _grid.GpuReady ? _grid.PublicationPrimitiveCapacity : 0;
        public int LastPublicationDirtyChunkCount { get; private set; }
        public ulong TotalPublicationChunkRebuilds { get; private set; }
        public float ScanOpacity
        {
            get => scanOpacity;
            set
            {
                scanOpacity = Mathf.Clamp01(value);
                ApplyOpacityState();
            }
        }

        private static readonly int KernelsId = Shader.PropertyToID("_MerkabaKernels");
        private static readonly int PageCoordsId = Shader.PropertyToID("_MerkabaPageCoords");
        private static readonly int PageNeighboursId =
            Shader.PropertyToID("_MerkabaPageNeighbours");
        private static readonly int BoundarySummaryHashId =
            Shader.PropertyToID("_MerkabaBoundarySummaryHash");
        private static readonly int BoundarySummaryWordsId =
            Shader.PropertyToID("_MerkabaBoundarySummaryWords");
        private static readonly int BoundarySummaryHashCountId =
            Shader.PropertyToID("_MerkabaBoundarySummaryHashCapacity");
        private static readonly int DirtySlotQueueId =
            Shader.PropertyToID("_MerkabaDirtySlotQueue");
        private static readonly int PublicationDirtyId =
            Shader.PropertyToID("_MerkabaPublicationDirtyChunks");
        private static readonly int PublicationVersionsId =
            Shader.PropertyToID("_MerkabaPublicationVersions");
        private static readonly int PrimitiveRecordBanksId =
            Shader.PropertyToID("_MerkabaPrimitiveRecordBanks");
        private static readonly int SourcePrimitiveRecordBanksId =
            Shader.PropertyToID("_MerkabaSourcePrimitiveRecordBanks");
        private static readonly int SourcePublishedBanksId =
            Shader.PropertyToID("_MerkabaSourcePublishedBanks");
        private static readonly int PrimitiveCountsId =
            Shader.PropertyToID("_MerkabaPrimitiveCounts");
        private static readonly int PrimitiveBuildCountsId =
            Shader.PropertyToID("_MerkabaPrimitiveBuildCounts");
        private static readonly int PublicationOverflowCountsId =
            Shader.PropertyToID("_MerkabaPublicationOverflowCounts");
        private static readonly int PublishedBanksId =
            Shader.PropertyToID("_MerkabaPublishedBanks");
        private static readonly int PrimitiveDrawArgsId =
            Shader.PropertyToID("_MerkabaPrimitiveDrawArgs");
        private static readonly int ResidentCapacityId =
            Shader.PropertyToID("_MerkabaResidentSlotCapacity");
        private static readonly int PrimitiveCapacityId =
            Shader.PropertyToID("_MerkabaPrimitiveCapacityPerChunk");
        private static readonly int SourcePrimitiveCapacityId =
            Shader.PropertyToID("_MerkabaSourcePrimitiveCapacityPerChunk");
        private static readonly int ResidentSlotId =
            Shader.PropertyToID("_MerkabaResidentSlot");
        private static readonly int ChunkOriginId = Shader.PropertyToID("_MerkabaChunkOrigin");
        private static readonly int GridToWorldId = Shader.PropertyToID("_MerkabaGridToWorld");
        private static readonly int ScanOpacityId = Shader.PropertyToID("_ScanOpacity");
        private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");

        private void Awake() => _grid = GetComponent<MerkabaGrid>();

        private void OnDestroy()
        {
            _replacementPublication.Release();
            foreach (RetiredPublication retired in _retiredPublications)
                retired.Buffer.Release();
            _retiredPublications.Clear();
            if (_material != null) Destroy(_material);
        }

        private void LateUpdate()
        {
            Camera camera = Camera.main;
            if (camera == null || _grid == null)
            {
                MerkabaGpuTimestamps.EndFrame();
                return;
            }
            if (!_initialized && !Initialize())
            {
                MerkabaGpuTimestamps.EndFrame();
                return;
            }

            TryCommitPublicationReplacement();
            ReleaseRetiredBuffers();
            RoomScanner scanner = RoomScanner.Instance;
            if (scanner == null || !scanner.IsScanning)
                _grid.ClearIntegrationResidencyDemand();
            _grid.RefreshResidency(camera, renderDistance, false);

            int cpuDirtyGroups = 0;
            bool gpuQueueSubmitted = false;
            // During the exceptional migration, current records remain the render
            // authority and dirty state accumulates without modifying them.
            if (!_resizeMigrationPending)
            {
                cpuDirtyGroups = _grid.FlushCpuPublicationDirtySlots();
                if (cpuDirtyGroups > 0)
                    DispatchDirectDirtyQueue(_grid.CpuPublicationDirtySlotsBuffer,
                        cpuDirtyGroups);

                if (_grid.ConsumeGpuPublicationMayBeDirty())
                {
                    DispatchGpuDirtyQueue();
                    gpuQueueSubmitted = true;
                }

                if (cpuDirtyGroups > 0 || gpuQueueSubmitted)
                    _statusSampleRequested = true;
            }

            bool drawsMerkaba = _grid.VisibleChunkCount > 0 &&
                                scanOpacity > 0.001f;
            if (drawsMerkaba)
                MerkabaGpuTimestamps.BeginGraphics(
                    MerkabaGpuStage.MerkabaDraw);
            DrawVisibleChunks(camera);
            if (drawsMerkaba)
                MerkabaGpuTimestamps.EndGraphics(
                    MerkabaGpuStage.MerkabaDraw);
            if (_resizeMigrationPending && drawsMerkaba)
                RecordLatestSourceUseFence();

            MerkabaGpuTimestamps.CaptureRenderMetrics(_grid, cpuDirtyGroups,
                gpuQueueSubmitted);
            MerkabaGpuTimestamps.EndFrame();

            if (_statusSampleRequested && !_statusReadbackPending &&
                Time.unscaledTime >= _nextStatusSampleTime)
            {
                _statusSampleRequested = false;
                _nextStatusSampleTime = Time.unscaledTime +
                                        StatusSampleIntervalSeconds;
                RequestPublicationStatus();
            }

            // Start only after this frame queued every draw using the current buffer.
            if (!_resizeMigrationPending &&
                !_publicationResizeDisabled &&
                _requestedResizeRequirement > _grid.PublicationPrimitiveCapacity)
                BeginExceptionalResizeMigration();
        }

        private bool Initialize()
        {
            if (topologyCompute == null || renderShader == null)
            {
                Logger.Error("MerkabaGridRenderer: shader references are not wired");
                enabled = false;
                return false;
            }
            _grid.EnsureGpuResources();
            _rebuildKernel = topologyCompute.FindKernel("RebuildDirtyChunkRecords");
            _finalizeKernel = topologyCompute.FindKernel("FinalizeDirtyChunkRecords");
            _migrateKernel = topologyCompute.FindKernel("MigratePublishedChunkRecords");
            _material = new Material(renderShader) { name = "MerkabaGrid (Runtime)" };
            _drawProperties = new MaterialPropertyBlock();
            _lastPublicationVersions = new uint[_grid.ResidentSlotCapacity];
            ApplyOpacityState();
            _initialized = true;
            return true;
        }

        private void DispatchDirectDirtyQueue(ComputeBuffer queue, int groupCount)
        {
            BindRebuildKernel(queue);
            MerkabaGpuTimestamps.BeginCompute(
                MerkabaGpuStage.TopologyUpdate);
            topologyCompute.Dispatch(_rebuildKernel, groupCount, 512, 1);
            MerkabaGpuTimestamps.EndCompute(
                MerkabaGpuStage.TopologyUpdate);
            BindFinalizeKernel(queue);
            MerkabaGpuTimestamps.BeginCompute(
                MerkabaGpuStage.PublicationCompaction);
            topologyCompute.Dispatch(_finalizeKernel, groupCount, 1, 1);
            MerkabaGpuTimestamps.EndCompute(
                MerkabaGpuStage.PublicationCompaction);
        }

        private void DispatchGpuDirtyQueue()
        {
            ComputeBuffer.CopyCount(_grid.GpuPublicationDirtyQueueBuffer,
                _grid.GpuPublicationDispatchArgsBuffer, 0);
            ComputeBuffer.CopyCount(_grid.GpuPublicationDirtyQueueBuffer,
                _grid.GpuPublicationFinalizeArgsBuffer, 0);
            BindRebuildKernel(_grid.GpuPublicationDirtyQueueBuffer);
            MerkabaGpuTimestamps.BeginCompute(
                MerkabaGpuStage.TopologyUpdate);
            topologyCompute.DispatchIndirect(_rebuildKernel,
                _grid.GpuPublicationDispatchArgsBuffer);
            MerkabaGpuTimestamps.EndCompute(
                MerkabaGpuStage.TopologyUpdate);
            BindFinalizeKernel(_grid.GpuPublicationDirtyQueueBuffer);
            MerkabaGpuTimestamps.BeginCompute(
                MerkabaGpuStage.PublicationCompaction);
            topologyCompute.DispatchIndirect(_finalizeKernel,
                _grid.GpuPublicationFinalizeArgsBuffer);
            MerkabaGpuTimestamps.EndCompute(
                MerkabaGpuStage.PublicationCompaction);
            _grid.GpuPublicationDirtyQueueBuffer.SetCounterValue(0);
        }

        private void BindRebuildKernel(ComputeBuffer dirtyQueue)
        {
            topologyCompute.SetBuffer(_rebuildKernel, KernelsId, _grid.KernelBuffer);
            topologyCompute.SetBuffer(_rebuildKernel, PageCoordsId,
                _grid.PageCoordsBuffer);
            topologyCompute.SetBuffer(_rebuildKernel, PageNeighboursId,
                _grid.PageNeighboursBuffer);
            topologyCompute.SetBuffer(_rebuildKernel, BoundarySummaryHashId,
                _grid.BoundarySummaryHashBuffer);
            topologyCompute.SetBuffer(_rebuildKernel, BoundarySummaryWordsId,
                _grid.BoundarySummaryWordsBuffer);
            topologyCompute.SetBuffer(_rebuildKernel, DirtySlotQueueId, dirtyQueue);
            topologyCompute.SetBuffer(_rebuildKernel, PublicationDirtyId,
                _grid.PublicationDirtyChunksBuffer);
            topologyCompute.SetBuffer(_rebuildKernel, PrimitiveRecordBanksId,
                _grid.PrimitiveRecordBanksBuffer);
            topologyCompute.SetBuffer(_rebuildKernel, PrimitiveBuildCountsId,
                _grid.PrimitiveBuildCountBuffer);
            topologyCompute.SetBuffer(_rebuildKernel, PublishedBanksId,
                _grid.PublishedBankBuffer);
            topologyCompute.SetInt(ResidentCapacityId, _grid.ResidentSlotCapacity);
            topologyCompute.SetInt(PrimitiveCapacityId,
                _grid.PublicationPrimitiveCapacity);
            topologyCompute.SetInt(BoundarySummaryHashCountId,
                _grid.BoundarySummaryHashEntryCount);
        }

        private void BindFinalizeKernel(ComputeBuffer dirtyQueue)
        {
            topologyCompute.SetBuffer(_finalizeKernel, DirtySlotQueueId, dirtyQueue);
            topologyCompute.SetBuffer(_finalizeKernel, PublicationDirtyId,
                _grid.PublicationDirtyChunksBuffer);
            topologyCompute.SetBuffer(_finalizeKernel, PublicationVersionsId,
                _grid.PublicationVersionBuffer);
            topologyCompute.SetBuffer(_finalizeKernel, PrimitiveCountsId,
                _grid.PrimitiveCountBuffer);
            topologyCompute.SetBuffer(_finalizeKernel, PrimitiveBuildCountsId,
                _grid.PrimitiveBuildCountBuffer);
            topologyCompute.SetBuffer(_finalizeKernel, PublicationOverflowCountsId,
                _grid.PublicationOverflowCountBuffer);
            topologyCompute.SetBuffer(_finalizeKernel, PublishedBanksId,
                _grid.PublishedBankBuffer);
            topologyCompute.SetBuffer(_finalizeKernel, PrimitiveDrawArgsId,
                _grid.PrimitiveDrawArgsBuffer);
            topologyCompute.SetInt(ResidentCapacityId, _grid.ResidentSlotCapacity);
            topologyCompute.SetInt(PrimitiveCapacityId,
                _grid.PublicationPrimitiveCapacity);
        }

        private void DrawVisibleChunks(Camera camera)
        {
            if (_grid.VisibleChunkCount == 0 || scanOpacity <= 0.001f) return;
            _material.SetBuffer(KernelsId, _grid.KernelBuffer);
            _material.SetBuffer(PrimitiveRecordBanksId,
                _grid.PrimitiveRecordBanksBuffer);
            _material.SetBuffer(PublishedBanksId, _grid.PublishedBankBuffer);
            _material.SetInt(ResidentCapacityId, _grid.ResidentSlotCapacity);
            _material.SetInt(PrimitiveCapacityId,
                _grid.PublicationPrimitiveCapacity);
            _material.SetMatrix(GridToWorldId, _grid.GridToWorldMatrix);
            Bounds bounds = new(camera.transform.position,
                Vector3.one * (renderDistance * 2.5f));
            for (int visibleIndex = 0;
                 visibleIndex < _grid.VisibleChunkCount; visibleIndex++)
            {
                int slot = _grid.VisibleSlotAt(visibleIndex);
                int3 chunkOrigin = MerkabaConstants.ChunkOrigin(
                    _grid.ResidentCoordAtSlot(slot));
                _drawProperties.Clear();
                _drawProperties.SetInt(ResidentSlotId, slot);
                _drawProperties.SetVector(ChunkOriginId,
                    new Vector4(chunkOrigin.x, chunkOrigin.y, chunkOrigin.z, 0f));
                Graphics.DrawProceduralIndirect(_material, bounds,
                    MeshTopology.Triangles, _grid.PrimitiveDrawArgsBuffer,
                    slot * 4 * sizeof(uint), null, _drawProperties,
                    _shadowCastingMode, true, gameObject.layer);
            }
        }

        private void ApplyOpacityState()
        {
            if (_material == null) return;
            bool opaque = scanOpacity >= 0.999f;
            _material.SetFloat(ScanOpacityId, scanOpacity);
            _material.SetInt(SrcBlendId, (int)(opaque
                ? BlendMode.One : BlendMode.SrcAlpha));
            _material.SetInt(DstBlendId, (int)(opaque
                ? BlendMode.Zero : BlendMode.OneMinusSrcAlpha));
            _material.SetInt(ZWriteId, 1);
            _material.renderQueue = opaque
                ? (int)RenderQueue.Geometry : (int)RenderQueue.Transparent;
            _shadowCastingMode = opaque
                ? ShadowCastingMode.On : ShadowCastingMode.Off;
        }

        private void RequestPublicationStatus()
        {
            _statusReadbackPending = true;
            _statusPartsPending = 3;
            int visibleCount = _grid.VisibleChunkCount;
            var visibleSlots = new int[visibleCount];
            for (int i = 0; i < visibleCount; i++)
                visibleSlots[i] = _grid.VisibleSlotAt(i);

            AsyncGPUReadback.Request(_grid.PrimitiveCountBuffer, request =>
            {
                if (!request.hasError)
                {
                    var counts = request.GetData<uint>();
                    ulong visible = 0;
                    foreach (int slot in visibleSlots) visible += counts[slot];
                    VisiblePrimitiveCount = (int)Math.Min((ulong)int.MaxValue, visible);
                }
                CompleteStatusPart();
            });
            AsyncGPUReadback.Request(_grid.PublicationOverflowCountBuffer, request =>
            {
                if (!request.hasError)
                {
                    var overflows = request.GetData<uint>();
                    uint maximum = 0;
                    int overflowChunks = 0;
                    for (int slot = 0; slot < overflows.Length; slot++)
                    {
                        uint required = overflows[slot];
                        if (required == 0u) continue;
                        overflowChunks++;
                        maximum = Math.Max(maximum, required);
                    }
                    PublicationOverflowChunkCount = overflowChunks;
                    PeakPrimitiveRequirement = Math.Max(PeakPrimitiveRequirement,
                        maximum);
                    _requestedResizeRequirement = Math.Max(
                        _requestedResizeRequirement, maximum);
                }
                CompleteStatusPart();
            });
            AsyncGPUReadback.Request(_grid.PublicationVersionBuffer, request =>
            {
                if (!request.hasError)
                {
                    var versions = request.GetData<uint>();
                    int rebuilt = 0;
                    int count = Math.Min(versions.Length,
                        _lastPublicationVersions.Length);
                    for (int slot = 0; slot < count; slot++)
                    {
                        uint previous = _lastPublicationVersions[slot];
                        uint current = versions[slot];
                        uint delta = current >= previous
                            ? current - previous : current;
                        rebuilt += (int)Math.Min(delta, (uint)int.MaxValue);
                        _lastPublicationVersions[slot] = current;
                    }
                    LastPublicationDirtyChunkCount = rebuilt;
                    TotalPublicationChunkRebuilds += (ulong)Math.Max(0, rebuilt);
                }
                CompleteStatusPart();
            });
        }

        private void CompleteStatusPart()
        {
            if (--_statusPartsPending > 0) return;
            _statusReadbackPending = false;
        }

        private void BeginExceptionalResizeMigration()
        {
            if (!SystemInfo.supportsGraphicsFence)
            {
                _publicationResizeDisabled = true;
                Logger.Error("Merkaba publication growth requires graphics fences; " +
                             "retaining the last valid publication until teardown.");
                return;
            }

            uint required = _requestedResizeRequirement;
            try
            {
                _replacementPublication =
                    _grid.CreatePublicationReplacement(required);
            }
            catch (Exception exception)
            {
                Logger.Error($"Merkaba publication capacity growth failed for " +
                             $"measured requirement {required}: {exception}");
                _requestedResizeRequirement = 0u;
                return;
            }

            _replacementRequirement = required;
            _resizeMigrationPending = true;
            topologyCompute.SetBuffer(_migrateKernel, SourcePrimitiveRecordBanksId,
                _grid.PrimitiveRecordBanksBuffer);
            topologyCompute.SetBuffer(_migrateKernel, SourcePublishedBanksId,
                _grid.PublishedBankBuffer);
            topologyCompute.SetBuffer(_migrateKernel, PrimitiveRecordBanksId,
                _replacementPublication.Records);
            topologyCompute.SetBuffer(_migrateKernel, PrimitiveCountsId,
                _grid.PrimitiveCountBuffer);
            topologyCompute.SetInt(ResidentCapacityId, _grid.ResidentSlotCapacity);
            topologyCompute.SetInt(SourcePrimitiveCapacityId,
                _grid.PublicationPrimitiveCapacity);
            topologyCompute.SetInt(PrimitiveCapacityId,
                _replacementPublication.CapacityPerChunk);
            topologyCompute.Dispatch(_migrateKernel,
                _grid.ResidentSlotCapacity,
                Mathf.CeilToInt(_grid.PublicationPrimitiveCapacity / 64f), 1);

            try
            {
                _migrationFence = CreatePublicationFence();
                _migrationFenceValid = true;
                _latestSourceUseFence = _migrationFence;
                _latestSourceUseFenceValid = true;
                _sourceRetirementUnsafe = false;
            }
            catch (Exception exception)
            {
                _publicationResizeDisabled = true;
                _sourceRetirementUnsafe = true;
                Logger.Error("Merkaba publication migration fence failed; retaining " +
                             $"source and replacement until teardown: {exception.Message}");
            }
        }

        private static GraphicsFence CreatePublicationFence() =>
            Graphics.CreateGraphicsFence(
                GraphicsFenceType.AsyncQueueSynchronisation,
                SynchronisationStageFlags.AllGPUOperations);

        private void RecordLatestSourceUseFence()
        {
            if (!SystemInfo.supportsGraphicsFence)
            {
                _sourceRetirementUnsafe = true;
                return;
            }
            try
            {
                _latestSourceUseFence = CreatePublicationFence();
                _latestSourceUseFenceValid = true;
            }
            catch (Exception exception)
            {
                _sourceRetirementUnsafe = true;
                Logger.Error("Merkaba source-use fence failed; retaining the source " +
                             $"publication until teardown: {exception.Message}");
            }
        }

        private void TryCommitPublicationReplacement()
        {
            if (!_resizeMigrationPending || !_migrationFenceValid ||
                _publicationResizeDisabled)
                return;

            bool migrationPassed;
            try
            {
                migrationPassed = _migrationFence.passed;
            }
            catch (Exception exception)
            {
                _publicationResizeDisabled = true;
                _sourceRetirementUnsafe = true;
                Logger.Error("Merkaba publication migration fence cannot be polled; " +
                             $"retaining both generations until teardown: {exception.Message}");
                return;
            }
            if (!migrationPassed || !_replacementPublication.IsValid) return;

            MerkabaPublicationBuffer retired =
                _grid.CommitPublicationReplacement(_replacementPublication,
                    _replacementRequirement);
            _replacementPublication = default;
            _resizeMigrationPending = false;
            _requestedResizeRequirement = 0u;
            GraphicsFence protectingFence = _latestSourceUseFenceValid
                ? _latestSourceUseFence : _migrationFence;
            bool releaseSupported = !_sourceRetirementUnsafe &&
                                    (_latestSourceUseFenceValid ||
                                     _migrationFenceValid);
            _retiredPublications.Add(new RetiredPublication(retired,
                protectingFence, releaseSupported));
            _migrationFenceValid = false;
            _latestSourceUseFenceValid = false;
            _sourceRetirementUnsafe = false;
        }

        private void ReleaseRetiredBuffers()
        {
            for (int index = _retiredPublications.Count - 1; index >= 0; index--)
            {
                RetiredPublication retired = _retiredPublications[index];
                if (!retired.ReleaseSupported) continue;
                try
                {
                    if (!retired.ProtectingFence.passed) continue;
                    retired.Buffer.Release();
                    _retiredPublications.RemoveAt(index);
                }
                catch (Exception exception)
                {
                    retired.ReleaseSupported = false;
                    Logger.Error("Merkaba retired-publication fence cannot be polled; " +
                                 $"retaining its buffer until teardown: {exception.Message}");
                }
            }
        }
    }
}
