using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan
{
    public sealed partial class MerkabaGrid
    {
        [Header("M8 GPU World")]
        [SerializeField] private ComputeShader worldCompute;

        internal const int SurfaceCandidateCapacity = 2_097_152;
        internal const int SurfaceQueueCapacity = 1_048_576;
        internal const int LoadRequestCapacity = 262144;
        internal const int LoadRequestMask = LoadRequestCapacity - 1;
        internal const int StreamBatchCapacity = 32;
        internal const int VisiblePrimitiveCapacity = 1_048_576;
        internal const int CounterCount = 64;

        internal const int CounterBlockCount = 0;
        internal const int CounterChunkCount = 1;
        internal const int CounterHotTileCount = 2;
        internal const int CounterColdTileCount = 3;
        internal const int CounterBlockOverflow = 7;
        internal const int CounterChunkOverflow = 8;
        internal const int CounterHashFull = 38;
        internal const int CounterOccupiedKernelCount = 42;

        private bool _gpuReady;
        private int _gpuGeneration;

        private int _clearUIntKernel;
        private int _clearInt4Kernel;
        private int _clearUInt2Kernel;
        private int _clearKernelStatesKernel;
        private int _clearUInt4Kernel;
        private int _initializeFreeTilesKernel;
        private int _resetCountersKernel;
        private int _prepareAllocatedClearKernel;
        private int _clearAllocatedBlocksKernel;
        private int _clearAllocatedChunksKernel;
        private int _publishNewBlocksKernel;
        private int _publishNewChunksKernel;
        private int _initializeNewTilesKernel;
        private int _resetObservationKernel;
        private int _resetClaimQueuesKernel;
        private int _resetResolveCounterKernel;
        private int _clearTouchedCandidatesKernel;
        private int _selectEvictionVictimsKernel;
        private int _gatherWritebackBatchKernel;
        private int _acknowledgeWritebackBatchKernel;
        private int _failWritebackBatchKernel;
        private int _installLoadedTilesKernel;
        private int _failLoadedTilesKernel;
        private int _registerLoadedTileAddressesKernel;
        private int _benchmarkHashKernel;

        private ComputeBuffer _m8HashEntries;
        private ComputeBuffer _m8BlockCoords;
        private ComputeBuffer _m8BlockChunkRefs;
        private ComputeBuffer _m8BlockPresenceL0;
        private ComputeBuffer _m8BlockPresenceL1;
        private ComputeBuffer _m8BlockPresenceL2;
        private ComputeBuffer _m8ChunkOwners;
        private ComputeBuffer _m8ChunkTileRefs;
        private ComputeBuffer _m8ChunkPresenceL0;
        private ComputeBuffer _m8ChunkPresenceL1;
        private ComputeBuffer _m8KernelStates0;
        private ComputeBuffer _m8KernelStates1;
        private ComputeBuffer _m8KernelStates2;
        private ComputeBuffer _m8KernelStates3;
        private ComputeBuffer _m8OccupiedBits;
        private ComputeBuffer _m8CarveActiveBits;
        private ComputeBuffer _m8SurfaceCandidateBits;
        private ComputeBuffer _m8TileMeta;
        private ComputeBuffer _m8TileRuntime;
        private ComputeBuffer _m8FreeTileStack;
        private ComputeBuffer _m8FreeTileCount;
        private ComputeBuffer _m8Counters;
        private ComputeBuffer _m8NewBlockQueue;
        private ComputeBuffer _m8NewChunkQueue;
        private ComputeBuffer _m8NewTileQueue;
        private ComputeBuffer _m8PendingNewTileRefs;
        private ComputeBuffer _m8LoadRequests;
        private ComputeBuffer _m8LoadRequestReadCount;
        private ComputeBuffer _m8SurfaceCandidates;
        private ComputeBuffer _m8SurfaceQueue;
        private ComputeBuffer _m8TouchedTileQueue;
        private ComputeBuffer _m8CarveTiles;
        private ComputeBuffer _m8CarveDispatchArgs;
        private ComputeBuffer _m8VisibleTiles;
        private ComputeBuffer _m8VisiblePrimitives;
        private ComputeBuffer _m8FrameDispatchArgs;
        private ComputeBuffer _m8DrawArgs;
        private ComputeBuffer _m8ObservationDispatchArgs;
        private ComputeBuffer _m8WritebackQueue;
        private ComputeBuffer _m8WritebackStaging;
        private ComputeBuffer _m8LoadStagingAddresses;
        private ComputeBuffer _m8LoadStagingStates;
        private ComputeBuffer _m8StreamStatus;
        private ComputeBuffer _m8HashBenchmarkOutput;

        private readonly List<ComputeBuffer> _allGpuBuffers = new();

        internal ComputeShader WorldCompute => worldCompute;
        internal ComputeBuffer M8HashEntries => _m8HashEntries;
        internal ComputeBuffer M8BlockCoords => _m8BlockCoords;
        internal ComputeBuffer M8BlockChunkRefs => _m8BlockChunkRefs;
        internal ComputeBuffer M8BlockPresenceL0 => _m8BlockPresenceL0;
        internal ComputeBuffer M8BlockPresenceL1 => _m8BlockPresenceL1;
        internal ComputeBuffer M8BlockPresenceL2 => _m8BlockPresenceL2;
        internal ComputeBuffer M8ChunkOwners => _m8ChunkOwners;
        internal ComputeBuffer M8ChunkTileRefs => _m8ChunkTileRefs;
        internal ComputeBuffer M8ChunkPresenceL0 => _m8ChunkPresenceL0;
        internal ComputeBuffer M8ChunkPresenceL1 => _m8ChunkPresenceL1;
        internal ComputeBuffer M8KernelStates0 => _m8KernelStates0;
        internal ComputeBuffer M8KernelStates1 => _m8KernelStates1;
        internal ComputeBuffer M8KernelStates2 => _m8KernelStates2;
        internal ComputeBuffer M8KernelStates3 => _m8KernelStates3;
        internal ComputeBuffer M8OccupiedBits => _m8OccupiedBits;
        internal ComputeBuffer M8CarveActiveBits => _m8CarveActiveBits;
        internal ComputeBuffer M8SurfaceCandidateBits => _m8SurfaceCandidateBits;
        internal ComputeBuffer M8TileMeta => _m8TileMeta;
        internal ComputeBuffer M8TileRuntime => _m8TileRuntime;
        internal ComputeBuffer M8FreeTileStack => _m8FreeTileStack;
        internal ComputeBuffer M8FreeTileCount => _m8FreeTileCount;
        internal ComputeBuffer M8Counters => _m8Counters;
        internal ComputeBuffer M8NewBlockQueue => _m8NewBlockQueue;
        internal ComputeBuffer M8NewChunkQueue => _m8NewChunkQueue;
        internal ComputeBuffer M8NewTileQueue => _m8NewTileQueue;
        internal ComputeBuffer M8PendingNewTileRefs => _m8PendingNewTileRefs;
        internal ComputeBuffer M8LoadRequests => _m8LoadRequests;
        internal ComputeBuffer M8SurfaceCandidates => _m8SurfaceCandidates;
        internal ComputeBuffer M8SurfaceQueue => _m8SurfaceQueue;
        internal ComputeBuffer M8TouchedTileQueue => _m8TouchedTileQueue;
        internal ComputeBuffer M8CarveTiles => _m8CarveTiles;
        internal ComputeBuffer M8CarveDispatchArgs => _m8CarveDispatchArgs;
        internal ComputeBuffer M8VisibleTiles => _m8VisibleTiles;
        internal ComputeBuffer M8VisiblePrimitives => _m8VisiblePrimitives;
        internal ComputeBuffer M8FrameDispatchArgs => _m8FrameDispatchArgs;
        internal ComputeBuffer M8DrawArgs => _m8DrawArgs;
        internal ComputeBuffer M8ObservationDispatchArgs => _m8ObservationDispatchArgs;
        internal ComputeBuffer M8WritebackQueue => _m8WritebackQueue;
        internal ComputeBuffer M8WritebackStaging => _m8WritebackStaging;
        internal ComputeBuffer M8LoadStagingAddresses => _m8LoadStagingAddresses;
        internal ComputeBuffer M8LoadStagingStates => _m8LoadStagingStates;
        internal ComputeBuffer M8StreamStatus => _m8StreamStatus;

        internal bool GpuReady => _gpuReady;
        internal Matrix4x4 GridToWorldMatrix => transform.localToWorldMatrix;
        internal int M8BlockCount { get; private set; }
        internal int M8ChunkCount { get; private set; }
        internal int M8HotTileCount { get; private set; }
        internal int M8ColdTileCount { get; private set; }
        internal int M8OccupiedKernelCount { get; private set; }

        private static readonly int ClearUIntsId = Shader.PropertyToID("_M8ClearUInts");
        private static readonly int ClearInt4sId = Shader.PropertyToID("_M8ClearInt4s");
        private static readonly int ClearUInt2sId = Shader.PropertyToID("_M8ClearUInt2s");
        private static readonly int ClearKernelStatesId =
            Shader.PropertyToID("_M8ClearKernelStates");
        private static readonly int ClearUInt4sId = Shader.PropertyToID("_M8ClearUInt4s");
        private static readonly int ClearCountId = Shader.PropertyToID("_M8ClearCount");
        private static readonly int LinearGroupsXId =
            Shader.PropertyToID("_M8LinearGroupsX");
        private static readonly int StreamBatchCountId =
            Shader.PropertyToID("_M8StreamBatchCount");
        private static readonly int EvictAllDirtyId =
            Shader.PropertyToID("_M8EvictAllDirty");
        private static readonly int SafeEpochId = Shader.PropertyToID("_M8SafeEpoch");
        private static readonly int ObservationTokenId =
            Shader.PropertyToID("_M8ObservationToken");
        private static readonly int ClearBlockArgsId =
            Shader.PropertyToID("_M8ClearBlockArgs");
        private static readonly int ClearChunkArgsId =
            Shader.PropertyToID("_M8ClearChunkArgs");

        internal void EnsureGpuResources()
        {
            if (_gpuReady) return;
            if (worldCompute == null)
                throw new InvalidOperationException(
                    "MerkabaGrid requires MerkabaWorld.compute.");
            if (Marshal.SizeOf<KernelState>() != 16)
                throw new InvalidOperationException("KernelState GPU ABI must be 16 bytes.");

            try
            {
                CacheWorldKernels();
                _m8HashEntries = Allocate(MerkabaSpatial.HashEntryCount, 16);
                _m8BlockCoords = Allocate(MerkabaSpatial.BlockCapacity, 16);
                _m8BlockChunkRefs = Allocate(checked(MerkabaSpatial.BlockCapacity *
                    MerkabaSpatial.BlockChunkCount), sizeof(uint));
                _m8BlockPresenceL0 = Allocate(MerkabaSpatial.BlockCapacity, sizeof(uint));
                _m8BlockPresenceL1 = Allocate(checked(MerkabaSpatial.BlockCapacity * 8),
                    sizeof(uint));
                _m8BlockPresenceL2 = Allocate(checked(MerkabaSpatial.BlockCapacity * 64),
                    sizeof(uint));
                _m8ChunkOwners = Allocate(MerkabaSpatial.ChunkCapacity, sizeof(uint) * 2);
                _m8ChunkTileRefs = Allocate(checked(MerkabaSpatial.ChunkCapacity *
                    MerkabaSpatial.TilesPerChunk), sizeof(uint));
                _m8ChunkPresenceL0 = Allocate(MerkabaSpatial.ChunkCapacity, sizeof(uint));
                _m8ChunkPresenceL1 = Allocate(checked(MerkabaSpatial.ChunkCapacity * 8),
                    sizeof(uint));

                int bankStateCount = checked(MerkabaSpatial.PhysicalTileBankCapacity *
                    MerkabaSpatial.KernelsPerTile);
                int physicalWordCount = checked(MerkabaSpatial.PhysicalTileCapacity * 16);
                _m8KernelStates0 = Allocate(bankStateCount, 16);
                _m8KernelStates1 = Allocate(bankStateCount, 16);
                _m8KernelStates2 = Allocate(bankStateCount, 16);
                _m8KernelStates3 = Allocate(bankStateCount, 16);
                _m8OccupiedBits = Allocate(physicalWordCount, sizeof(uint));
                _m8CarveActiveBits = Allocate(physicalWordCount, sizeof(uint));
                _m8SurfaceCandidateBits = Allocate(physicalWordCount, sizeof(uint));
                _m8TileMeta = Allocate(MerkabaSpatial.PhysicalTileCapacity, 16);
                _m8TileRuntime = Allocate(MerkabaSpatial.PhysicalTileCapacity, 16);
                _m8FreeTileStack = Allocate(MerkabaSpatial.PhysicalTileCapacity,
                    sizeof(uint));
                _m8FreeTileCount = Allocate(1, sizeof(int));
                _m8Counters = Allocate(CounterCount, sizeof(uint));

                _m8NewBlockQueue = Allocate(MerkabaSpatial.BlockCapacity,
                    sizeof(uint) * 2);
                _m8NewChunkQueue = Allocate(MerkabaSpatial.ChunkCapacity,
                    sizeof(uint) * 2);
                _m8NewTileQueue = Allocate(MerkabaSpatial.PhysicalTileCapacity,
                    sizeof(uint) * 2);
                _m8PendingNewTileRefs = Allocate(MerkabaSpatial.PhysicalTileCapacity,
                    sizeof(uint));
                _m8LoadRequests = Allocate(LoadRequestCapacity, 16);
                _m8LoadRequestReadCount = Allocate(1, sizeof(uint));
                _m8SurfaceCandidates = Allocate(SurfaceCandidateCapacity, 16);
                _m8SurfaceQueue = Allocate(SurfaceQueueCapacity, sizeof(uint));
                _m8TouchedTileQueue = Allocate(MerkabaSpatial.PhysicalTileCapacity,
                    sizeof(uint));
                _m8CarveTiles = Allocate(MerkabaSpatial.PhysicalTileCapacity,
                    sizeof(uint));
                _m8CarveDispatchArgs = Allocate(3, sizeof(uint),
                    ComputeBufferType.IndirectArguments);
                _m8VisibleTiles = Allocate(MerkabaSpatial.PhysicalTileCapacity,
                    sizeof(uint));
                _m8VisiblePrimitives = Allocate(VisiblePrimitiveCapacity, 16);
                _m8FrameDispatchArgs = Allocate(3, sizeof(uint),
                    ComputeBufferType.IndirectArguments);
                _m8DrawArgs = Allocate(4, sizeof(uint),
                    ComputeBufferType.IndirectArguments);
                _m8ObservationDispatchArgs = Allocate(3, sizeof(uint),
                    ComputeBufferType.IndirectArguments);
                _m8WritebackQueue = Allocate(MerkabaSpatial.PhysicalTileCapacity,
                    sizeof(uint) * 2);
                _m8WritebackStaging = Allocate(StreamBatchCapacity *
                    (MerkabaSpatial.KernelsPerTile + 1), 16);
                _m8LoadStagingAddresses = Allocate(StreamBatchCapacity, 16);
                _m8LoadStagingStates = Allocate(StreamBatchCapacity *
                    MerkabaSpatial.KernelsPerTile, 16);
                _m8StreamStatus = Allocate(StreamBatchCapacity, sizeof(uint));
                _m8HashBenchmarkOutput = Allocate(MerkabaSpatial.BlockCapacity, 16);

                BindWorldBuffers(worldCompute, _initializeFreeTilesKernel);
                BindWorldBuffers(worldCompute, _resetCountersKernel);
                BindWorldBuffers(worldCompute, _prepareAllocatedClearKernel);
                BindWorldBuffers(worldCompute, _clearAllocatedBlocksKernel);
                BindWorldBuffers(worldCompute, _clearAllocatedChunksKernel);
                BindWorldBuffers(worldCompute, _publishNewBlocksKernel);
                BindWorldBuffers(worldCompute, _publishNewChunksKernel);
                BindWorldBuffers(worldCompute, _initializeNewTilesKernel);
                BindWorldBuffers(worldCompute, _resetObservationKernel);
                BindWorldBuffers(worldCompute, _resetClaimQueuesKernel);
                BindWorldBuffers(worldCompute, _resetResolveCounterKernel);
                BindWorldBuffers(worldCompute, _clearTouchedCandidatesKernel);
                BindWorldBuffers(worldCompute, _selectEvictionVictimsKernel);
                BindWorldBuffers(worldCompute, _gatherWritebackBatchKernel);
                BindWorldBuffers(worldCompute, _acknowledgeWritebackBatchKernel);
                BindWorldBuffers(worldCompute, _failWritebackBatchKernel);
                BindWorldBuffers(worldCompute, _installLoadedTilesKernel);
                BindWorldBuffers(worldCompute, _failLoadedTilesKernel);
                BindWorldBuffers(worldCompute, _registerLoadedTileAddressesKernel);
                worldCompute.SetBuffer(_benchmarkHashKernel,
                    "_M8HashBenchmarkOutput", _m8HashBenchmarkOutput);
                worldCompute.SetBuffer(_prepareAllocatedClearKernel,
                    ClearBlockArgsId, _m8FrameDispatchArgs);
                worldCompute.SetBuffer(_prepareAllocatedClearKernel,
                    ClearChunkArgsId, _m8ObservationDispatchArgs);
                InitializeGpuWorld();
                EnsureStorage();
                _gpuReady = true;
            }
            catch
            {
                ReleaseGpuResources();
                throw;
            }
        }

        private void CacheWorldKernels()
        {
            _clearUIntKernel = worldCompute.FindKernel("ClearUInts");
            _clearInt4Kernel = worldCompute.FindKernel("ClearInt4s");
            _clearUInt2Kernel = worldCompute.FindKernel("ClearUInt2s");
            _clearKernelStatesKernel = worldCompute.FindKernel("ClearKernelStates");
            _clearUInt4Kernel = worldCompute.FindKernel("ClearUInt4s");
            _initializeFreeTilesKernel = worldCompute.FindKernel("InitializeFreeTileStack");
            _resetCountersKernel = worldCompute.FindKernel("ResetWorldCounters");
            _prepareAllocatedClearKernel = worldCompute.FindKernel(
                "PrepareAllocatedClearArgs");
            _clearAllocatedBlocksKernel = worldCompute.FindKernel(
                "ClearAllocatedBlocks");
            _clearAllocatedChunksKernel = worldCompute.FindKernel(
                "ClearAllocatedChunks");
            _publishNewBlocksKernel = worldCompute.FindProfiledKernel(
                "PublishNewBlocks", MerkabaGpuStage.SurfaceIntegration);
            _publishNewChunksKernel = worldCompute.FindProfiledKernel(
                "PublishNewChunks", MerkabaGpuStage.SurfaceIntegration);
            _initializeNewTilesKernel = worldCompute.FindProfiledKernel(
                "InitializeNewTiles", MerkabaGpuStage.SurfaceIntegration);
            _resetObservationKernel = worldCompute.FindProfiledKernel(
                "ResetObservationCounters", MerkabaGpuStage.SurfaceIntegration);
            _resetClaimQueuesKernel = worldCompute.FindProfiledKernel(
                "ResetClaimQueueCounts", MerkabaGpuStage.SurfaceIntegration);
            _resetResolveCounterKernel = worldCompute.FindProfiledKernel(
                "ResetResolveCounter", MerkabaGpuStage.SurfaceIntegration);
            _clearTouchedCandidatesKernel = worldCompute.FindProfiledKernel(
                "ClearTouchedSurfaceCandidates",
                MerkabaGpuStage.SurfaceIntegration);
            _selectEvictionVictimsKernel =
                worldCompute.FindKernel("SelectEvictionVictims");
            _gatherWritebackBatchKernel =
                worldCompute.FindKernel("GatherWritebackBatch");
            _acknowledgeWritebackBatchKernel =
                worldCompute.FindKernel("AcknowledgeWritebackBatch");
            _failWritebackBatchKernel =
                worldCompute.FindKernel("FailWritebackBatch");
            _installLoadedTilesKernel = worldCompute.FindKernel("InstallLoadedTiles");
            _failLoadedTilesKernel = worldCompute.FindKernel("FailLoadedTiles");
            _registerLoadedTileAddressesKernel =
                worldCompute.FindKernel("RegisterLoadedTileAddresses");
            _benchmarkHashKernel = worldCompute.FindProfiledKernel(
                "BenchmarkM8Pcg3d", MerkabaGpuStage.WorldQuery);
        }

        private ComputeBuffer Allocate(int count, int stride,
            ComputeBufferType type = ComputeBufferType.Structured)
        {
            var buffer = new ComputeBuffer(count, stride, type);
            _allGpuBuffers.Add(buffer);
            return buffer;
        }

        internal void BindWorldBuffers(ComputeShader shader, int kernel)
        {
            shader.SetBuffer(kernel, "_M8HashEntries", _m8HashEntries);
            shader.SetBuffer(kernel, "_M8BlockCoords", _m8BlockCoords);
            shader.SetBuffer(kernel, "_M8BlockChunkRefs", _m8BlockChunkRefs);
            shader.SetBuffer(kernel, "_M8BlockPresenceL0", _m8BlockPresenceL0);
            shader.SetBuffer(kernel, "_M8BlockPresenceL1", _m8BlockPresenceL1);
            shader.SetBuffer(kernel, "_M8BlockPresenceL2", _m8BlockPresenceL2);
            shader.SetBuffer(kernel, "_M8ChunkOwners", _m8ChunkOwners);
            shader.SetBuffer(kernel, "_M8ChunkTileRefs", _m8ChunkTileRefs);
            shader.SetBuffer(kernel, "_M8ChunkPresenceL0", _m8ChunkPresenceL0);
            shader.SetBuffer(kernel, "_M8ChunkPresenceL1", _m8ChunkPresenceL1);
            shader.SetBuffer(kernel, "_M8KernelStates0", _m8KernelStates0);
            shader.SetBuffer(kernel, "_M8KernelStates1", _m8KernelStates1);
            shader.SetBuffer(kernel, "_M8KernelStates2", _m8KernelStates2);
            shader.SetBuffer(kernel, "_M8KernelStates3", _m8KernelStates3);
            shader.SetBuffer(kernel, "_M8OccupiedBits", _m8OccupiedBits);
            shader.SetBuffer(kernel, "_M8CarveActiveBits", _m8CarveActiveBits);
            shader.SetBuffer(kernel, "_M8SurfaceCandidateBits",
                _m8SurfaceCandidateBits);
            shader.SetBuffer(kernel, "_M8TileMeta", _m8TileMeta);
            shader.SetBuffer(kernel, "_M8TileRuntime", _m8TileRuntime);
            shader.SetBuffer(kernel, "_M8FreeTileStack", _m8FreeTileStack);
            shader.SetBuffer(kernel, "_M8FreeTileCount", _m8FreeTileCount);
            shader.SetBuffer(kernel, "_M8Counters", _m8Counters);
            shader.SetBuffer(kernel, "_M8NewBlockQueue", _m8NewBlockQueue);
            shader.SetBuffer(kernel, "_M8NewChunkQueue", _m8NewChunkQueue);
            shader.SetBuffer(kernel, "_M8NewTileQueue", _m8NewTileQueue);
            shader.SetBuffer(kernel, "_M8PendingNewTileRefs", _m8PendingNewTileRefs);
            shader.SetBuffer(kernel, "_M8LoadRequests", _m8LoadRequests);
            shader.SetBuffer(kernel, "_M8LoadRequestReadCount",
                _m8LoadRequestReadCount);
            shader.SetBuffer(kernel, "_M8WritebackQueue", _m8WritebackQueue);
            shader.SetBuffer(kernel, "_M8WritebackStaging",
                _m8WritebackStaging);
            shader.SetBuffer(kernel, "_M8LoadStagingAddresses",
                _m8LoadStagingAddresses);
            shader.SetBuffer(kernel, "_M8LoadStagingStates",
                _m8LoadStagingStates);
            shader.SetBuffer(kernel, "_M8StreamStatus", _m8StreamStatus);
        }

        internal void SelectEvictionVictims(bool allDirty)
        {
            worldCompute.SetInt(EvictAllDirtyId, allDirty ? 1 : 0);
            worldCompute.SetInt(SafeEpochId, 3);
            worldCompute.Dispatch(_selectEvictionVictimsKernel,
                DivideRoundUp(MerkabaSpatial.PhysicalTileCapacity, 256), 1, 1);
            worldCompute.Dispatch(_gatherWritebackBatchKernel,
                StreamBatchCapacity, 1, 1);
        }

        internal void AcknowledgeWritebackBatch(int count)
        {
            worldCompute.SetInt(StreamBatchCountId, count);
            worldCompute.Dispatch(_acknowledgeWritebackBatchKernel, 1, 1, 1);
        }

        internal void FailWritebackBatch(int count)
        {
            worldCompute.SetInt(StreamBatchCountId, count);
            worldCompute.Dispatch(_failWritebackBatchKernel, 1, 1, 1);
        }

        internal void InstallLoadedTiles(int count)
        {
            worldCompute.SetInt(StreamBatchCountId, count);
            worldCompute.Dispatch(_installLoadedTilesKernel, count, 1, 1);
        }

        internal void FailLoadedTiles(int count)
        {
            worldCompute.SetInt(StreamBatchCountId, count);
            worldCompute.Dispatch(_failLoadedTilesKernel, 1, 1, 1);
        }

        internal void RegisterLoadedTileAddresses(int count)
        {
            worldCompute.SetInt(StreamBatchCountId, count);
            const int hierarchyPublicationRounds = 2;
            for (int round = 0; round <
                 MerkabaSpatial.HashSlotsPerBucket * 2 +
                 hierarchyPublicationRounds; round++)
            {
                worldCompute.Dispatch(_registerLoadedTileAddressesKernel,
                    DivideRoundUp(count, 64), 1, 1);
                PublishClaimedBlocksAndChunks();
                ResetClaimQueues();
            }
        }

        internal void RecordHashBenchmark(CommandBuffer command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            command.DispatchComputeProfiled(worldCompute, _benchmarkHashKernel,
                MerkabaSpatial.BlockCapacity / 256, 1, 1);
        }

        internal uint ResetObservationGpuCounters()
        {
            unchecked
            {
                _issuedObservationToken++;
                if (_issuedObservationToken == 0u) _issuedObservationToken = 1u;
            }
            worldCompute.SetInt(ObservationTokenId,
                unchecked((int)_issuedObservationToken));
            worldCompute.Dispatch(_resetObservationKernel, 1, 1, 1);
            return _issuedObservationToken;
        }

        internal uint RecordResetObservationGpuCounters(CommandBuffer command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            unchecked
            {
                _issuedObservationToken++;
                if (_issuedObservationToken == 0u) _issuedObservationToken = 1u;
            }
            command.SetComputeIntParam(worldCompute, ObservationTokenId,
                unchecked((int)_issuedObservationToken));
            command.DispatchComputeProfiled(worldCompute,
                _resetObservationKernel, 1, 1, 1);
            return _issuedObservationToken;
        }

        internal void PublishClaimedBlocks() =>
            worldCompute.Dispatch(_publishNewBlocksKernel, 1, 1, 1);

        internal void RecordPublishClaimedBlocks(CommandBuffer command) =>
            command.DispatchComputeProfiled(worldCompute,
                _publishNewBlocksKernel, 1, 1, 1);

        internal void PublishClaimedChunks() =>
            worldCompute.Dispatch(_publishNewChunksKernel, 1, 1, 1);

        internal void RecordPublishClaimedChunks(CommandBuffer command) =>
            command.DispatchComputeProfiled(worldCompute,
                _publishNewChunksKernel, 1, 1, 1);

        internal void PublishClaimedBlocksAndChunks()
        {
            PublishClaimedBlocks();
            PublishClaimedChunks();
        }

        internal void InitializeClaimedTiles() =>
            worldCompute.Dispatch(_initializeNewTilesKernel,
                512, 1, 1);

        internal void RecordInitializeClaimedTiles(CommandBuffer command) =>
            command.DispatchComputeProfiled(worldCompute,
                _initializeNewTilesKernel, 512, 1, 1);

        internal void ResetClaimQueues() =>
            worldCompute.Dispatch(_resetClaimQueuesKernel, 1, 1, 1);

        internal void RecordResetClaimQueues(CommandBuffer command) =>
            command.DispatchComputeProfiled(worldCompute,
                _resetClaimQueuesKernel, 1, 1, 1);

        internal void ResetResolveCounter() =>
            worldCompute.Dispatch(_resetResolveCounterKernel, 1, 1, 1);

        internal void RecordResetResolveCounter(CommandBuffer command) =>
            command.DispatchComputeProfiled(worldCompute,
                _resetResolveCounterKernel, 1, 1, 1);

        internal void ClearTouchedSurfaceCandidates()
        {
            worldCompute.SetBuffer(_clearTouchedCandidatesKernel,
                "_M8TouchedTileQueue", _m8TouchedTileQueue);
            worldCompute.DispatchIndirect(_clearTouchedCandidatesKernel,
                _m8ObservationDispatchArgs);
        }

        internal void RecordClearTouchedSurfaceCandidates(CommandBuffer command)
        {
            command.SetComputeBufferParam(worldCompute,
                _clearTouchedCandidatesKernel, "_M8TouchedTileQueue",
                _m8TouchedTileQueue);
            command.DispatchComputeProfiled(worldCompute,
                _clearTouchedCandidatesKernel, _m8ObservationDispatchArgs);
        }

        private void InitializeGpuWorld()
        {
            ClearInt4(_m8HashEntries, MerkabaSpatial.HashEntryCount);
            ClearInt4(_m8BlockCoords, MerkabaSpatial.BlockCapacity);
            ClearUInt(_m8BlockChunkRefs, _m8BlockChunkRefs.count);
            ClearUInt(_m8BlockPresenceL0, _m8BlockPresenceL0.count);
            ClearUInt(_m8BlockPresenceL1, _m8BlockPresenceL1.count);
            ClearUInt(_m8BlockPresenceL2, _m8BlockPresenceL2.count);
            ClearUInt2(_m8ChunkOwners, _m8ChunkOwners.count);
            ClearUInt(_m8ChunkTileRefs, _m8ChunkTileRefs.count);
            ClearUInt(_m8ChunkPresenceL0, _m8ChunkPresenceL0.count);
            ClearUInt(_m8ChunkPresenceL1, _m8ChunkPresenceL1.count);
            ClearStates(_m8KernelStates0, _m8KernelStates0.count);
            ClearStates(_m8KernelStates1, _m8KernelStates1.count);
            ClearStates(_m8KernelStates2, _m8KernelStates2.count);
            ClearStates(_m8KernelStates3, _m8KernelStates3.count);
            ClearUInt(_m8OccupiedBits, _m8OccupiedBits.count);
            ClearUInt(_m8CarveActiveBits, _m8CarveActiveBits.count);
            ClearUInt(_m8SurfaceCandidateBits, _m8SurfaceCandidateBits.count);
            ClearUInt4(_m8TileMeta, _m8TileMeta.count);
            ClearUInt4(_m8TileRuntime, _m8TileRuntime.count);
            ClearUInt(_m8Counters, _m8Counters.count);
            ClearUInt(_m8LoadRequestReadCount, 1);
            ClearUInt(_m8FrameDispatchArgs, _m8FrameDispatchArgs.count);
            ClearUInt(_m8DrawArgs, _m8DrawArgs.count);
            ClearUInt(_m8ObservationDispatchArgs, _m8ObservationDispatchArgs.count);
            ClearUInt(_m8CarveDispatchArgs, _m8CarveDispatchArgs.count);
            worldCompute.Dispatch(_resetCountersKernel, 1, 1, 1);
            worldCompute.Dispatch(_initializeFreeTilesKernel,
                DivideRoundUp(MerkabaSpatial.PhysicalTileCapacity, 256), 1, 1);
            M8BlockCount = M8ChunkCount = M8HotTileCount = M8ColdTileCount = 0;
            M8OccupiedKernelCount = 0;
            ResetStorageRuntimeState();
        }

        internal void ClearGpuWorldForNewScan()
        {
            if (!_gpuReady) return;
            _gpuGeneration++;
            worldCompute.Dispatch(_prepareAllocatedClearKernel, 1, 1, 1);
            worldCompute.DispatchIndirect(_clearAllocatedBlocksKernel,
                _m8FrameDispatchArgs);
            worldCompute.DispatchIndirect(_clearAllocatedChunksKernel,
                _m8ObservationDispatchArgs);
            ClearInt4(_m8HashEntries, MerkabaSpatial.HashEntryCount);
            ClearUInt(_m8Counters, _m8Counters.count);
            ClearUInt(_m8LoadRequestReadCount, 1);
            ClearUInt(_m8FrameDispatchArgs, _m8FrameDispatchArgs.count);
            ClearUInt(_m8DrawArgs, _m8DrawArgs.count);
            ClearUInt(_m8ObservationDispatchArgs,
                _m8ObservationDispatchArgs.count);
            ClearUInt(_m8CarveDispatchArgs, _m8CarveDispatchArgs.count);
            worldCompute.Dispatch(_resetCountersKernel, 1, 1, 1);
            worldCompute.Dispatch(_initializeFreeTilesKernel,
                DivideRoundUp(MerkabaSpatial.PhysicalTileCapacity, 256), 1, 1);
            M8BlockCount = M8ChunkCount = M8HotTileCount = M8ColdTileCount = 0;
            M8OccupiedKernelCount = 0;
            ResetStorageRuntimeState();
        }

        private void ClearUInt(ComputeBuffer buffer, int count)
        {
            worldCompute.SetBuffer(_clearUIntKernel, ClearUIntsId, buffer);
            DispatchLinear(_clearUIntKernel, count);
        }

        private void ClearInt4(ComputeBuffer buffer, int count)
        {
            worldCompute.SetBuffer(_clearInt4Kernel, ClearInt4sId, buffer);
            DispatchLinear(_clearInt4Kernel, count);
        }

        private void ClearUInt2(ComputeBuffer buffer, int count)
        {
            worldCompute.SetBuffer(_clearUInt2Kernel, ClearUInt2sId, buffer);
            DispatchLinear(_clearUInt2Kernel, count);
        }

        private void ClearUInt4(ComputeBuffer buffer, int count)
        {
            worldCompute.SetBuffer(_clearUInt4Kernel, ClearUInt4sId, buffer);
            DispatchLinear(_clearUInt4Kernel, count);
        }

        private void ClearStates(ComputeBuffer buffer, int count)
        {
            worldCompute.SetBuffer(_clearKernelStatesKernel,
                ClearKernelStatesId, buffer);
            DispatchLinear(_clearKernelStatesKernel, count);
        }

        private void DispatchLinear(int kernel, int count)
        {
            int groups = DivideRoundUp(count, 256);
            int groupsX = Mathf.Min(65535, Mathf.Max(1, groups));
            int groupsY = DivideRoundUp(groups, groupsX);
            worldCompute.SetInt(ClearCountId, count);
            worldCompute.SetInt(LinearGroupsXId, groupsX);
            worldCompute.Dispatch(kernel, groupsX, Mathf.Max(1, groupsY), 1);
        }

        private static int DivideRoundUp(int value, int divisor) =>
            (value + divisor - 1) / divisor;

        private void Update() => PumpStorage();

        private static int ToInt(uint value) =>
            value > int.MaxValue ? int.MaxValue : (int)value;

        private void ReleaseGpuResources()
        {
            _gpuGeneration++;
            foreach (ComputeBuffer buffer in _allGpuBuffers) buffer?.Release();
            _allGpuBuffers.Clear();
            _m8HashEntries = null;
            _m8BlockCoords = null;
            _m8BlockChunkRefs = null;
            _m8BlockPresenceL0 = null;
            _m8BlockPresenceL1 = null;
            _m8BlockPresenceL2 = null;
            _m8ChunkOwners = null;
            _m8ChunkTileRefs = null;
            _m8ChunkPresenceL0 = null;
            _m8ChunkPresenceL1 = null;
            _m8KernelStates0 = null;
            _m8KernelStates1 = null;
            _m8KernelStates2 = null;
            _m8KernelStates3 = null;
            _m8OccupiedBits = null;
            _m8CarveActiveBits = null;
            _m8SurfaceCandidateBits = null;
            _m8TileMeta = null;
            _m8TileRuntime = null;
            _m8FreeTileStack = null;
            _m8FreeTileCount = null;
            _m8Counters = null;
            _m8LoadRequestReadCount = null;
            _m8HashBenchmarkOutput = null;
            ResetStorageRuntimeState();
            _gpuReady = false;
        }
    }
}
