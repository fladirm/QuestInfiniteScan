using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan
{
    public sealed partial class MerkabaGrid
    {
        private volatile bool _gpuSubmissionSuspended;
        private Task _gpuRetirementTask = Task.CompletedTask;
        private const uint WorldGenerationLimit = 0x3fffffffu;
        private uint _worldGenerationSequence;
        private uint _worldPublishedGeneration;
        private uint _worldRetiredGeneration;
        private uint _worldDurableGeneration;
        private uint _worldMutationGeneration;
        private bool _worldNativeMutation;
        private bool _worldSubmissionUncertain;
        private GraphicsFence _worldRetirementFence;
        private uint _worldRetirementFenceGeneration;
        private bool _worldRetirementFencePending;
        private static readonly int WorldPublishingGenerationId =
            Shader.PropertyToID("_M8WorldPublishingGeneration");
        private static readonly int WorldRetiredGenerationId =
            Shader.PropertyToID("_M8WorldRetiredGeneration");
        [Header("M8 GPU World")]
        [SerializeField] private ComputeShader worldCompute;

        // Frozen future data strides only. CUT 02 allocates and binds none of
        // these resources; their owning production cuts do so exactly once.
        internal const int FlowerOwnerEpochStride =
            MerkabaSphereFlowerDataAbi.FlowerOwnerEpochStride;
        internal const int FlowerDetailStride =
            MerkabaSphereFlowerDataAbi.FlowerDetailStride;
        internal const int FlowerSkinMetricRunStride =
            MerkabaSphereFlowerDataAbi.FlowerSkinMetricRunStride;
        internal const int FlowerVGroupStride =
            MerkabaSphereFlowerDataAbi.FlowerVGroupStride;
        internal const int ThreadRunStride =
            MerkabaSphereFlowerDataAbi.ThreadRunStride;
        internal const int ThreadColorGroupStride =
            MerkabaSphereFlowerDataAbi.ThreadColorGroupStride;
        internal const int ThreadProgramStride =
            MerkabaSphereFlowerDataAbi.ThreadProgramStride;
        internal const int FlowerSymbolStride =
            MerkabaSphereFlowerDataAbi.FlowerSymbolStride;
        internal const int ObservationRecordStride =
            MerkabaSphereFlowerDataAbi.ObservationRecordStride;
        internal const int LoadRequestCapacity = 262144;
        internal const int LoadRequestMask = LoadRequestCapacity - 1;
        internal const int StreamBatchCapacity = 32;
        internal const int CounterCount = 52;

        internal const int CounterBlockCount = 0;
        internal const int CounterChunkCount = 1;
        internal const int CounterHotTileCount = 2;
        internal const int CounterColdTileCount = 3;
        internal const int CounterHashCollisions = 4;
        internal const int CounterHashProbes = 5;
        internal const int CounterHashMaxProbe = 6;
        internal const int CounterBlockOverflow = 7;
        internal const int CounterChunkOverflow = 8;
        internal const int CounterTileStarvation = 9;
        internal const int CounterUnresolvedSurfaceTiles = 10;
        internal const int CounterSurfaceTilesAllocated = 11;
        internal const int CounterScanColdMisses = 12;
        internal const int CounterTouchedTileCount = 13;
        internal const int CounterLoadRequests = 14;
        internal const int CounterWritebackCount = 15;
        internal const int CounterNewTileQueueCount = 19;
        internal const int CounterPendingNewTileCount = 20;
        internal const int CounterHashFull = 21;
        internal const int CounterFailedReads = 22;
        internal const int CounterFailedWrites = 23;
        internal const int CounterStorageBackpressure = 24;
        internal const int CounterObservationCompleted = 27;
        internal const int CounterObservationToken = 30;
        internal const int CounterOccupiedKernelCount = 25;
        internal const int CounterFineEraseTileCount = 26;
        internal const int CounterWritebackTiles = 31;
        internal const int CounterEvictionNeeded = 33;
        internal const int CounterObservationFailure = 34;
        internal const int CounterFailedObservations = 35;
        internal const int CounterFreeTileCount = 36;
        internal const int CounterLoadsInstalled = 29;
        internal const int CounterObservationChangeMask = 38;
        internal const int CounterDirtyTileCount = 39;
        internal const int CounterUnresolvedObservationTiles = 40;
        internal const int CounterResidencyEpoch = 41;
        internal const int CounterReadoutUnresolved = 32;
        internal const int CounterReadoutEmittedTriangles = 46;
        internal const int CounterReadoutEmittedVertices = 47;
        internal const int CounterRefinementPendingTiles = 48;
        internal const int CounterRefinementWorkProgress = 49;
        internal const int CounterRefinementBackpressure = 50;
        internal const int CounterRefinementUnresolved = 51;

        internal bool GpuSubmissionAllowed =>
            _gpuReady && !_gpuSubmissionSuspended;
        internal bool GpuSubmissionSuspended => _gpuSubmissionSuspended;
        internal bool ObservationMutationSubmissionAllowed =>
            !_storageReplacementPending && !_flushAllDirty && WorldMutationSubmissionAllowed;

        internal bool WorldMutationSubmissionAllowed => GpuSubmissionAllowed &&
            _worldMutationGeneration == 0u &&
            !MerkabaNativeVulkanExecutor.HasJobInFlight;

        private bool _gpuReady;
        private int _gpuGeneration;

        private int _clearUIntKernel;
        private int _clearRawKernel;
        private int _clearInt4Kernel;
        private int _initializeFreeTilesKernel;
        private int _resetCountersKernel;
        private int _prepareAllocatedClearKernel;
        private int _clearAllocatedBlocksKernel;
        private int _clearAllocatedChunksKernel;
        private int _publishNewBlocksKernel;
        private int _publishNewChunksKernel;
        private int _initializeNewTilesKernel;
        private int _resetClaimQueuesKernel;
        private int _prepareNewTileDispatchKernel;
        private int _prepareEvictionSelectionKernel;
        private int _selectEvictionVictimsKernel;
        private int _gatherWritebackBatchKernel;
        private int _transferFlowerStorageKernel;
        private int _acknowledgeWritebackBatchKernel;
        private int _failWritebackBatchKernel;
        private int _prepareLoadedTilesKernel;
        private int _installLoadedTilesKernel;
        private int _failLoadedTilesKernel;
        private int _registerLoadedTileAddressesKernel;
        private int _benchmarkHashKernel;

        private ComputeBuffer _m8HashEntries;
        private ComputeBuffer _m8OwnerRecords;
        private ComputeBuffer _m8BlockChunkRefs;
        private ComputeBuffer _m8BlockPresenceL0;
        private ComputeBuffer _m8BlockPresenceL1;
        private ComputeBuffer _m8BlockPresenceL2;
        private ComputeBuffer _m8ChunkTileRefs;
        private ComputeBuffer _m8ChunkPresence;
        private ComputeBuffer _m8KernelStates0;
        private ComputeBuffer _m8KernelStates1;
        private ComputeBuffer _m8KernelStates2;
        private ComputeBuffer _m8KernelStates3;
        private ComputeBuffer _m8TileBits;
        private ComputeBuffer _m8TileRecords;
        private ComputeBuffer _m8TileHalo;
        private ComputeBuffer _m8FlowerTables;
        private ComputeBuffer _m8FreeTileStack;
        private ComputeBuffer _m8Counters;
        private ComputeBuffer _m8AttemptCompletion;
        private ComputeBuffer _m8ClaimQueue;
        private ComputeBuffer _m8PendingNewTileRefs;
        private ComputeBuffer _m8LoadRequests;
        private ComputeBuffer _m8LoadRequestReadCount;
        private ComputeBuffer _m8ObservationRecords;
        private ComputeBuffer _m8FlowerSignalItems;
        private ComputeBuffer _m8TouchedTileQueue;
        private ComputeBuffer _m8FrameDispatchArgs;
        private ComputeBuffer _m8ObservationDispatchArgs;
        private ComputeBuffer _m8WritebackQueue;
        private ComputeBuffer _m8WritebackStaging;
        private ComputeBuffer _m8LoadStagingAddresses;
        private ComputeBuffer _m8LoadStagingStates;
        private ComputeBuffer _m8HashBenchmarkOutput;

        private readonly List<ComputeBuffer> _allGpuBuffers = new();

        internal ComputeShader WorldCompute => worldCompute;
        internal ComputeBuffer M8FlowerTables => _m8FlowerTables;
        internal static readonly int FlowerTablesId = Shader.PropertyToID("_M8FlowerTables");
        internal ComputeBuffer M8HashEntries => _m8HashEntries;
        internal ComputeBuffer M8OwnerRecords => _m8OwnerRecords;
        internal ComputeBuffer M8BlockChunkRefs => _m8BlockChunkRefs;
        internal ComputeBuffer M8BlockPresenceL0 => _m8BlockPresenceL0;
        internal ComputeBuffer M8BlockPresenceL1 => _m8BlockPresenceL1;
        internal ComputeBuffer M8BlockPresenceL2 => _m8BlockPresenceL2;
        internal ComputeBuffer M8ChunkTileRefs => _m8ChunkTileRefs;
        internal ComputeBuffer M8ChunkPresence => _m8ChunkPresence;
        internal ComputeBuffer M8KernelStates0 => _m8KernelStates0;
        internal ComputeBuffer M8KernelStates1 => _m8KernelStates1;
        internal ComputeBuffer M8KernelStates2 => _m8KernelStates2;
        internal ComputeBuffer M8KernelStates3 => _m8KernelStates3;
        internal ComputeBuffer M8TileBits => _m8TileBits;
        internal ComputeBuffer M8TileRecords => _m8TileRecords;
        internal ComputeBuffer M8TileHalo => _m8TileHalo;
        internal ComputeBuffer M8FreeTileStack => _m8FreeTileStack;
        internal ComputeBuffer M8Counters => _m8Counters;
        internal ComputeBuffer M8AttemptCompletion => _m8AttemptCompletion;
        internal ComputeBuffer M8ClaimQueue => _m8ClaimQueue;
        internal ComputeBuffer M8PendingNewTileRefs => _m8PendingNewTileRefs;
        internal ComputeBuffer M8LoadRequests => _m8LoadRequests;
        internal ComputeBuffer M8LoadRequestReadCount =>
            _m8LoadRequestReadCount;
        internal ComputeBuffer M8ObservationRecords => _m8ObservationRecords;
        internal ComputeBuffer M8FlowerSignalItems => _m8FlowerSignalItems;
        internal ComputeBuffer M8TouchedTileQueue => _m8TouchedTileQueue;
        internal ComputeBuffer M8FrameDispatchArgs => _m8FrameDispatchArgs;
        internal ComputeBuffer M8ObservationDispatchArgs => _m8ObservationDispatchArgs;
        internal ComputeBuffer M8WritebackQueue => _m8WritebackQueue;
        internal ComputeBuffer M8WritebackStaging => _m8WritebackStaging;
        internal ComputeBuffer M8LoadStagingAddresses => _m8LoadStagingAddresses;
        internal ComputeBuffer M8LoadStagingStates => _m8LoadStagingStates;

        internal void FillNativeExecutorWorldResources(IntPtr[] resources)
        {
            if (resources == null || resources.Length !=
                MerkabaNativeVulkanExecutor.ResourceCount)
                throw new ArgumentException(
                    "Native M8 resource table has an invalid size.",
                    nameof(resources));
            void Set(MerkabaNativeVulkanExecutor.Resource index,
                ComputeBuffer buffer) => resources[(int)index] =
                buffer != null ? buffer.GetNativeBufferPtr() : IntPtr.Zero;
            Set(MerkabaNativeVulkanExecutor.Resource.HashEntries,
                _m8HashEntries);
            Set(MerkabaNativeVulkanExecutor.Resource.OwnerRecords,
                _m8OwnerRecords);
            Set(MerkabaNativeVulkanExecutor.Resource.BlockChunkRefs,
                _m8BlockChunkRefs);
            Set(MerkabaNativeVulkanExecutor.Resource.BlockPresenceL0,
                _m8BlockPresenceL0);
            Set(MerkabaNativeVulkanExecutor.Resource.BlockPresenceL1,
                _m8BlockPresenceL1);
            Set(MerkabaNativeVulkanExecutor.Resource.BlockPresenceL2,
                _m8BlockPresenceL2);
            Set(MerkabaNativeVulkanExecutor.Resource.ChunkTileRefs,
                _m8ChunkTileRefs);
            Set(MerkabaNativeVulkanExecutor.Resource.ChunkPresence,
                _m8ChunkPresence);
            Set(MerkabaNativeVulkanExecutor.Resource.KernelStates0,
                _m8KernelStates0);
            Set(MerkabaNativeVulkanExecutor.Resource.KernelStates1,
                _m8KernelStates1);
            Set(MerkabaNativeVulkanExecutor.Resource.KernelStates2,
                _m8KernelStates2);
            Set(MerkabaNativeVulkanExecutor.Resource.KernelStates3,
                _m8KernelStates3);
            Set(MerkabaNativeVulkanExecutor.Resource.TileBits, _m8TileBits);
            Set(MerkabaNativeVulkanExecutor.Resource.TileRecords,
                _m8TileRecords);
            Set(MerkabaNativeVulkanExecutor.Resource.TileHalo, _m8TileHalo);
            Set(MerkabaNativeVulkanExecutor.Resource.FlowerDetailPages, _m8FlowerDetailPages);
            Set(MerkabaNativeVulkanExecutor.Resource.ThreadAtlasPages, _m8ThreadAtlasPages);
            Set(MerkabaNativeVulkanExecutor.Resource.FlowerSymbolArena, _m8FlowerSymbolArena);
            Set(MerkabaNativeVulkanExecutor.Resource.FlowerPageDirectory, _m8FlowerPageDirectory);
            Set(MerkabaNativeVulkanExecutor.Resource.FlowerIndirectCommands, _m8FlowerIndirectCommands);
            Set(MerkabaNativeVulkanExecutor.Resource.FlowerTables, _m8FlowerTables);
            Set(MerkabaNativeVulkanExecutor.Resource.FreeTileStack,
                _m8FreeTileStack);
            Set(MerkabaNativeVulkanExecutor.Resource.Counters, _m8Counters);
            Set(MerkabaNativeVulkanExecutor.Resource.ClaimQueue,
                _m8ClaimQueue);
            Set(MerkabaNativeVulkanExecutor.Resource.PendingNewTileRefs,
                _m8PendingNewTileRefs);
            Set(MerkabaNativeVulkanExecutor.Resource.LoadRequests,
                _m8LoadRequests);
            Set(MerkabaNativeVulkanExecutor.Resource.LoadRequestReadCount,
                _m8LoadRequestReadCount);
            Set(MerkabaNativeVulkanExecutor.Resource.ObservationRecords,
                _m8ObservationRecords);
            Set(MerkabaNativeVulkanExecutor.Resource.FlowerSignalItems,
                _m8FlowerSignalItems);
            Set(MerkabaNativeVulkanExecutor.Resource.TouchedTileQueue,
                _m8TouchedTileQueue);
            Set(MerkabaNativeVulkanExecutor.Resource.ObservationDispatchArgs,
                _m8ObservationDispatchArgs);
            Set(MerkabaNativeVulkanExecutor.Resource.AttemptCompletion,
                _m8AttemptCompletion);
            Set(MerkabaNativeVulkanExecutor.Resource.FrameDispatchArgs,
                _m8FrameDispatchArgs);
        }

        internal bool GpuReady => _gpuReady;
        internal Matrix4x4 GridToWorldMatrix => transform.localToWorldMatrix;
        internal bool FlowerGraphicsReadAllowed => GpuSubmissionAllowed &&
            _worldMutationGeneration == 0u && !_worldSubmissionUncertain &&
            !MerkabaNativeVulkanExecutor.HasJobInFlight;

        // The existing serialized lease waits for every preceding raw reader.
        internal uint FlowerGraphicsRetiredGeneration
        {
            get
            {
                if (_worldMutationGeneration == 0u)
                    throw new InvalidOperationException("Flower publication requires the serialized GPU lease.");
                return _worldPublishedGeneration;
            }
        }
        internal int M8BlockCount { get; private set; }
        internal int M8ChunkCount { get; private set; }
        internal int M8HotTileCount { get; private set; }
        internal int M8ColdTileCount { get; private set; }
        internal int M8OccupiedKernelCount { get; private set; }

        private static readonly int ClearUIntsId = Shader.PropertyToID("_M8ClearUInts");
        private static readonly int ClearInt4sId = Shader.PropertyToID("_M8ClearInt4s");
        private static readonly int ClearCountId = Shader.PropertyToID("_M8ClearCount");
        private static readonly int LinearGroupsXId =
            Shader.PropertyToID("_M8LinearGroupsX");
        private static readonly int StreamBatchCountId =
            Shader.PropertyToID("_M8StreamBatchCount");
        private static readonly int EvictAllDirtyId =
            Shader.PropertyToID("_M8EvictAllDirty");
        private static readonly int SafeEpochId = Shader.PropertyToID("_M8SafeEpoch");
        private static readonly int ClearBlockArgsId =
            Shader.PropertyToID("_M8ClearBlockArgs");
        private static readonly int ClearChunkArgsId =
            Shader.PropertyToID("_M8ClearChunkArgs");

        internal void EnsureGpuResources()
        {
            if (_gpuSubmissionSuspended)
                throw new InvalidOperationException(
                    "M8 GPU resources cannot initialize while quiesced.");
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
                _m8OwnerRecords = Allocate(MerkabaSpatial.OwnerRecordCount, 16);
                _m8BlockChunkRefs = Allocate(checked(MerkabaSpatial.BlockCapacity *
                    MerkabaSpatial.BlockChunkCount), sizeof(uint));
                _m8BlockPresenceL0 = Allocate(MerkabaSpatial.BlockCapacity, sizeof(uint));
                _m8BlockPresenceL1 = Allocate(checked(MerkabaSpatial.BlockCapacity * 8),
                    sizeof(uint));
                _m8BlockPresenceL2 = Allocate(checked(MerkabaSpatial.BlockCapacity * 64),
                    sizeof(uint));
                _m8ChunkTileRefs = Allocate(checked(MerkabaSpatial.ChunkCapacity *
                    MerkabaSpatial.TilesPerChunk), sizeof(uint));
                _m8ChunkPresence = Allocate(
                    MerkabaSpatial.ChunkPresenceWordCount, sizeof(uint));

                int bankStateCount = checked(MerkabaSpatial.PhysicalTileBankCapacity *
                    MerkabaSpatial.KernelsPerTile);
                _m8KernelStates0 = Allocate(bankStateCount, 16);
                _m8KernelStates1 = Allocate(bankStateCount, 16);
                _m8KernelStates2 = Allocate(bankStateCount, 16);
                _m8KernelStates3 = Allocate(bankStateCount, 16);
                _m8TileBits = Allocate(MerkabaSpatial.TileBitRecordCount, 16);
                _m8TileRecords = Allocate(MerkabaSpatial.TileRecordCount, 16);
                _m8TileHalo = Allocate(MerkabaSpatial.PhysicalTileCapacity * 27,
                    sizeof(uint));
                _m8FreeTileStack = Allocate(MerkabaSpatial.PhysicalTileCapacity,
                    sizeof(uint));
                _m8Counters = Allocate(CounterCount, sizeof(uint));
                _m8AttemptCompletion = Allocate(1, sizeof(uint) * 4);
                // Immutable generated lookup, shared by depth, scan and draw.
                // It lives until the same captured GPU retirement as the world
                // buffers and is never cleared by an observation or new scan.
                _m8FlowerTables = CreateFlowerTableBuffer();
                _allGpuBuffers.Add(_m8FlowerTables);
                AllocateFlowerPages();

                _m8ClaimQueue = Allocate(MerkabaSpatial.ClaimRecordCount,
                    sizeof(uint) * 2);
                _m8PendingNewTileRefs = Allocate(MerkabaSpatial.PhysicalTileCapacity,
                    sizeof(uint));
                _m8LoadRequests = Allocate(LoadRequestCapacity, 16);
                _m8LoadRequestReadCount = Allocate(1, sizeof(uint));
                _m8ObservationRecords = Allocate(MerkabaObservationRecord.Capacity, MerkabaObservationRecord.ByteSize);
                _m8FlowerSignalItems = Allocate(MerkabaFlowerGpuLayout.SignalBufferBytes / 4,
                    4, ComputeBufferType.Raw | ComputeBufferType.IndirectArguments);
                _m8FlowerSignalItems.name = "M8 snapshot-local signal items";
                _m8TouchedTileQueue = Allocate(MerkabaSpatial.PhysicalTileCapacity,
                    sizeof(uint));
                _m8FrameDispatchArgs = Allocate(3, sizeof(uint),
                    ComputeBufferType.IndirectArguments);
                _m8ObservationDispatchArgs = Allocate(MerkabaObservationBinsGpu.DispatchArgumentWords, sizeof(uint),
                    ComputeBufferType.IndirectArguments);
                _m8WritebackQueue = Allocate(StreamBatchCapacity,
                    sizeof(uint) * 2);
                _m8WritebackStaging = Allocate(StreamBatchCapacity *
                    MerkabaFlowerGpuStorageLayout.WritebackTileRecords, 16);
                _m8LoadStagingAddresses = Allocate(StreamBatchCapacity, 16);
                _m8LoadStagingStates = Allocate(StreamBatchCapacity *
                    MerkabaFlowerGpuStorageLayout.LoadTileRecords, 16);
                _m8HashBenchmarkOutput = Allocate(MerkabaSpatial.BlockCapacity, 16);

                BindWorldBuffers(worldCompute, _initializeFreeTilesKernel);
                BindWorldBuffers(worldCompute, _resetCountersKernel);
                BindWorldBuffers(worldCompute, _prepareAllocatedClearKernel);
                BindWorldBuffers(worldCompute, _clearAllocatedBlocksKernel);
                BindWorldBuffers(worldCompute, _clearAllocatedChunksKernel);
                BindWorldBuffers(worldCompute, _publishNewBlocksKernel);
                BindWorldBuffers(worldCompute, _publishNewChunksKernel);
                BindWorldBuffers(worldCompute, _initializeNewTilesKernel);
                BindWorldBuffers(worldCompute, _resetClaimQueuesKernel);
                BindWorldBuffers(worldCompute, _prepareNewTileDispatchKernel);
                BindWorldBuffers(worldCompute, _prepareEvictionSelectionKernel);
                BindWorldBuffers(worldCompute, _selectEvictionVictimsKernel);
                BindWorldBuffers(worldCompute, _gatherWritebackBatchKernel);
                BindWorldBuffers(worldCompute, _transferFlowerStorageKernel);
                BindWorldBuffers(worldCompute, _acknowledgeWritebackBatchKernel);
                BindWorldBuffers(worldCompute, _failWritebackBatchKernel);
                BindWorldBuffers(worldCompute, _prepareLoadedTilesKernel);
                BindWorldBuffers(worldCompute, _installLoadedTilesKernel);
                BindWorldBuffers(worldCompute, _failLoadedTilesKernel);
                BindWorldBuffers(worldCompute, _registerLoadedTileAddressesKernel);
                worldCompute.SetBuffer(_benchmarkHashKernel,
                    "_M8HashBenchmarkOutput", _m8HashBenchmarkOutput);
                worldCompute.SetBuffer(_prepareAllocatedClearKernel,
                    ClearBlockArgsId, _m8FrameDispatchArgs);
                worldCompute.SetBuffer(_prepareAllocatedClearKernel,
                    ClearChunkArgsId, _m8ObservationDispatchArgs);
                worldCompute.SetBuffer(_prepareNewTileDispatchKernel,
                    "_M8ObservationDispatchArgs", _m8ObservationDispatchArgs);
                worldCompute.SetBuffer(_resetClaimQueuesKernel,
                    "_M8ObservationDispatchArgs", _m8ObservationDispatchArgs);
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
            _resetClaimQueuesKernel = worldCompute.FindProfiledKernel(
                "ResetClaimQueueCounts", MerkabaGpuStage.SurfaceIntegration);
            _prepareNewTileDispatchKernel = worldCompute.FindProfiledKernel(
                "PrepareNewTileDispatchArgs",
                MerkabaGpuStage.SurfaceIntegration);
            _prepareEvictionSelectionKernel =
                worldCompute.FindProfiledKernel("PrepareEvictionSelection", MerkabaGpuStage.WorldQuery);
            _selectEvictionVictimsKernel =
                worldCompute.FindProfiledKernel("SelectEvictionVictims", MerkabaGpuStage.WorldQuery);
            _gatherWritebackBatchKernel =
                worldCompute.FindProfiledKernel("GatherWritebackBatch", MerkabaGpuStage.WorldQuery);
            _transferFlowerStorageKernel = worldCompute.FindProfiledKernel("TransferFlowerStorage", MerkabaGpuStage.WorldQuery);
            _acknowledgeWritebackBatchKernel =
                worldCompute.FindProfiledKernel("AcknowledgeWritebackBatch", MerkabaGpuStage.WorldQuery);
            _failWritebackBatchKernel =
                worldCompute.FindKernel("FailWritebackBatch");
            _prepareLoadedTilesKernel = worldCompute.FindProfiledKernel("PrepareLoadedTiles", MerkabaGpuStage.WorldQuery);
            _installLoadedTilesKernel = worldCompute.FindProfiledKernel("InstallLoadedTiles", MerkabaGpuStage.WorldQuery);
            _failLoadedTilesKernel = worldCompute.FindProfiledKernel("FailLoadedTiles", MerkabaGpuStage.WorldQuery);
            _registerLoadedTileAddressesKernel =
                worldCompute.FindKernel("RegisterLoadedTileAddresses");
            _benchmarkHashKernel = worldCompute.FindProfiledKernel(
                "BenchmarkM8Pcg3d", MerkabaGpuStage.WorldQuery);
        }

        private ComputeBuffer Allocate(int count, int stride,
            ComputeBufferType type = ComputeBufferType.Structured)
        {
            ValidateGpuBufferAllocation(count, stride);
            var buffer = new ComputeBuffer(count, stride, type);
            _allGpuBuffers.Add(buffer);
            return buffer;
        }

        // Standalone GPU proofs use this same upload, with their own explicit
        // fixture disposal. Production calls it once per grid GPU lifetime.
        internal static ComputeBuffer CreateFlowerTableBuffer()
        {
            const int stride = 4 * sizeof(uint);
            var rows = MerkabaFlowerTableBlob.CreateRows();
            if (rows == null || rows.Length != MerkabaFlowerTableBlob.RowCount ||
                (long)rows.Length * stride != MerkabaFlowerTableBlob.ByteSize ||
                MerkabaFlowerTableBlob.TableHash != MerkabaSphereFlowerAuthority.FrozenTableHash)
                throw new InvalidOperationException("Generated Flower table payload/hash is stale.");
            ValidateGpuBufferAllocation(rows.Length, stride);
            var buffer = new ComputeBuffer(rows.Length, stride, ComputeBufferType.Structured)
                { name = "M8 immutable generated Flower tables" };
            try
            {
                buffer.SetData(rows);
                return buffer;
            }
            catch
            {
                buffer.Release();
                throw;
            }
        }

        internal const long MaximumGpuBufferBytes = 128L * 1024 * 1024;

        internal static void ValidateGpuBufferAllocation(int count, int stride)
        {
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (stride <= 0) throw new ArgumentOutOfRangeException(nameof(stride));
            long bytes = checked((long)count * stride);
            long limit = Math.Min(MaximumGpuBufferBytes, SystemInfo.maxGraphicsBufferSize);
            if (bytes > limit)
                throw new InvalidOperationException(
                    $"M8 buffer allocation {count} × {stride} = {bytes} bytes exceeds " +
                    $"the {limit}-byte device/Quest limit (128 MiB maximum per buffer). " +
                    "Contract capacities cannot be silently reduced.");
        }


        internal void BindWorldBuffers(ComputeShader shader, int kernel)
        {
            shader.SetBuffer(kernel, FlowerTablesId, _m8FlowerTables);
            BindFlowerPages(shader, kernel);
            shader.SetBuffer(kernel, "_M8HashEntries", _m8HashEntries);
            shader.SetBuffer(kernel, "_M8HashEntriesRead", _m8HashEntries);
            shader.SetBuffer(kernel, "_M8OwnerRecords", _m8OwnerRecords);
            shader.SetBuffer(kernel, "_M8OwnerRecordsRead", _m8OwnerRecords);
            shader.SetBuffer(kernel, "_M8BlockChunkRefs", _m8BlockChunkRefs);
            shader.SetBuffer(kernel, "_M8BlockChunkRefsRead", _m8BlockChunkRefs);
            shader.SetBuffer(kernel, "_M8BlockPresenceL0", _m8BlockPresenceL0);
            shader.SetBuffer(kernel, "_M8BlockPresenceL1", _m8BlockPresenceL1);
            shader.SetBuffer(kernel, "_M8BlockPresenceL2", _m8BlockPresenceL2);
            shader.SetBuffer(kernel, "_M8BlockPresenceL0Read", _m8BlockPresenceL0);
            shader.SetBuffer(kernel, "_M8BlockPresenceL1Read", _m8BlockPresenceL1);
            shader.SetBuffer(kernel, "_M8BlockPresenceL2Read", _m8BlockPresenceL2);
            shader.SetBuffer(kernel, "_M8ChunkTileRefs", _m8ChunkTileRefs);
            shader.SetBuffer(kernel, "_M8ChunkTileRefsRead", _m8ChunkTileRefs);
            shader.SetBuffer(kernel, "_M8ChunkPresence", _m8ChunkPresence);
            shader.SetBuffer(kernel, "_M8ChunkPresenceRead", _m8ChunkPresence);
            shader.SetBuffer(kernel, "_M8KernelStates0", _m8KernelStates0);
            shader.SetBuffer(kernel, "_M8KernelStates1", _m8KernelStates1);
            shader.SetBuffer(kernel, "_M8KernelStates2", _m8KernelStates2);
            shader.SetBuffer(kernel, "_M8KernelStates3", _m8KernelStates3);
            shader.SetBuffer(kernel, "_M8KernelStates0Read", _m8KernelStates0);
            shader.SetBuffer(kernel, "_M8KernelStates1Read", _m8KernelStates1);
            shader.SetBuffer(kernel, "_M8KernelStates2Read", _m8KernelStates2);
            shader.SetBuffer(kernel, "_M8KernelStates3Read", _m8KernelStates3);
            shader.SetBuffer(kernel, "_M8TileBits", _m8TileBits);
            shader.SetBuffer(kernel, "_M8TileBitsRead", _m8TileBits);
            shader.SetBuffer(kernel, "_M8TileRecords", _m8TileRecords);
            shader.SetBuffer(kernel, "_M8TileRecordsRead", _m8TileRecords);
            shader.SetBuffer(kernel, "_M8TileHalo", _m8TileHalo);
            shader.SetBuffer(kernel, "_M8TileHaloRead", _m8TileHalo);
            shader.SetBuffer(kernel, "_M8FreeTileStack", _m8FreeTileStack);
            shader.SetBuffer(kernel, "_M8FreeTileStackRead", _m8FreeTileStack);
            shader.SetBuffer(kernel, "_M8Counters", _m8Counters);
            shader.SetBuffer(kernel, "_M8CountersRead", _m8Counters);
            shader.SetBuffer(kernel, "_M8ClaimQueue", _m8ClaimQueue);
            shader.SetBuffer(kernel, "_M8ClaimQueueRead", _m8ClaimQueue);
            shader.SetBuffer(kernel, "_M8PendingNewTileRefs", _m8PendingNewTileRefs);
            shader.SetBuffer(kernel, "_M8PendingNewTileRefsRead",
                _m8PendingNewTileRefs);
            shader.SetBuffer(kernel, "_M8LoadRequests", _m8LoadRequests);
            shader.SetBuffer(kernel, "_M8LoadRequestReadCount",
                _m8LoadRequestReadCount);
            shader.SetBuffer(kernel, "_M8WritebackQueue", _m8WritebackQueue);
            shader.SetBuffer(kernel, "_M8WritebackQueueRead", _m8WritebackQueue);
            shader.SetBuffer(kernel, "_M8WritebackStaging",
                _m8WritebackStaging);
            shader.SetBuffer(kernel, "_M8LoadStagingAddresses",
                _m8LoadStagingAddresses);
            shader.SetBuffer(kernel, "_M8LoadStagingAddressesRead",
                _m8LoadStagingAddresses);
            shader.SetBuffer(kernel, "_M8LoadStagingStates",
                _m8LoadStagingStates);
            shader.SetBuffer(kernel, "_M8LoadStagingStatesWrite",
                _m8LoadStagingStates);
        }

        private uint ReserveWorldMutation()
        {
            if (!GpuSubmissionAllowed || _worldMutationGeneration != 0u ||
                MerkabaNativeVulkanExecutor.HasJobInFlight)
                throw new InvalidOperationException(
                    "World mutation requires the exclusive serialized GPU lease.");
            if (!SystemInfo.supportsGraphicsFence)
                throw new NotSupportedException(
                    "World generation retirement requires GPU fences.");
            PollWorldRetirement();
            if (_worldGenerationSequence == WorldGenerationLimit)
            {
                _gpuSubmissionSuspended = true;
                throw new InvalidOperationException(
                    "World generation capacity exhausted; wrapping is forbidden.");
            }
            _worldMutationGeneration = ++_worldGenerationSequence;
            return _worldMutationGeneration;
        }

        // All canonical world access is on the graphics queue or the one serialized
        // native job. The graphics fence covers *all* preceding readers, not
        // only the preceding writer. Its GPU wait executes before the shader
        // sees the retired bound. CPU retirement is advanced separately.
        internal uint RecordWorldMutation(CommandBuffer command, ComputeShader shader)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (shader == null) throw new ArgumentNullException(nameof(shader));
            uint generation = ReserveWorldMutation();
            try
            {
                GraphicsFence prior = command.CreateGraphicsFence(
                    GraphicsFenceType.AsyncQueueSynchronisation,
                    SynchronisationStageFlags.AllGPUOperations);
                command.WaitOnAsyncGraphicsFence(prior);
                command.SetComputeIntParam(shader, WorldPublishingGenerationId,
                    checked((int)generation));
                command.SetComputeIntParam(shader, WorldRetiredGenerationId,
                    checked((int)_worldPublishedGeneration));
                return generation;
            }
            catch
            {
                CancelWorldMutationBeforeSubmit(generation);
                throw;
            }
        }

        internal void SubmitWorldMutation(CommandBuffer command, uint generation)
        {
            if (generation == 0u || generation != _worldMutationGeneration ||
                _worldNativeMutation)
                throw new InvalidOperationException("Invalid world GPU lease.");
            // CPU synchronisation, because PollWorldRetirement is the only
            // consumer and it reads this on the CPU. An async-queue fence
            // cannot be queried from the CPU unless the platform supports
            // async compute, and Quest does not: GraphicsFence.passed throws
            // NotSupportedException there. The stage flag is unchanged, so the
            // fence still signals after all preceding GPU work.
            GraphicsFence retired = command.CreateGraphicsFence(
                GraphicsFenceType.CPUSynchronisation,
                SynchronisationStageFlags.AllGPUOperations);
            try { Graphics.ExecuteCommandBuffer(command); }
            catch
            {
                // Unity may already have accepted the command. Neither this
                // generation nor its buffers may be recycled speculatively.
                _worldSubmissionUncertain = true;
                _gpuSubmissionSuspended = true;
                throw;
            }
            _worldPublishedGeneration = generation;
            _worldRetirementFence = retired;
            _worldRetirementFenceGeneration = generation;
            _worldRetirementFencePending = true;
            _worldMutationGeneration = 0u;
        }

        internal uint BeginNativeWorldMutation(MerkabaNativeUniformTable uniforms)
        {
            if (uniforms == null) throw new ArgumentNullException(nameof(uniforms));
            uint generation = ReserveWorldMutation();
            _worldNativeMutation = true;
            try
            {
                uniforms.UInt("_M8WorldPublishingGeneration", generation);
                // SubmitExecutorJob waits graphicsReady after all preceding
                // graphics work. No later raw reader can enter during this lease.
                uniforms.UInt("_M8WorldRetiredGeneration", _worldPublishedGeneration);
                return generation;
            }
            catch
            {
                CancelWorldMutationBeforeSubmit(generation);
                throw;
            }
        }

        internal void CompleteNativeWorldMutation(uint generation, bool completed)
        {
            if (generation == 0u || generation != _worldMutationGeneration ||
                !_worldNativeMutation)
                throw new InvalidOperationException("Invalid native world GPU lease.");
            if (!completed)
            {
                _worldSubmissionUncertain = true;
                _gpuSubmissionSuspended = true;
                return;
            }
            // Called only after Poll confirms nativeFence AND acquireFence.
            _worldPublishedGeneration = generation;
            _worldRetiredGeneration = generation;
            _worldRetirementFencePending = false;
            _worldMutationGeneration = 0u;
            _worldNativeMutation = false;
        }

        internal void CancelWorldMutationBeforeSubmit(uint generation)
        {
            if (generation == 0u || _worldMutationGeneration != generation ||
                _worldSubmissionUncertain) return;
            _worldMutationGeneration = 0u;
            _worldNativeMutation = false;
        }

        private void PollWorldRetirement()
        {
            if (!_worldRetirementFencePending || !_worldRetirementFence.passed) return;
            _worldRetiredGeneration = Math.Max(_worldRetiredGeneration,
                _worldRetirementFenceGeneration);
            _worldRetirementFencePending = false;
        }

        internal void AcknowledgeWorldDurableGeneration(uint generation)
        {
            if (generation > _worldPublishedGeneration || generation > WorldGenerationLimit)
                throw new InvalidOperationException("Unpublished world generation cannot be durable.");
            _worldDurableGeneration = Math.Max(_worldDurableGeneration, generation);
        }

        internal bool SelectEvictionVictims(bool allDirty)
        {
            if (!WorldMutationSubmissionAllowed) return false;
            CommandBuffer command = CommandBufferPool.Get("Merkaba world eviction");
            uint generation = 0u;
            bool timed = false, submitted = false;
            try
            {
                generation = RecordWorldMutation(command, worldCompute);
                timed = MerkabaGpuTimestamps.TryAcquireStorage(generation, command);
                command.SetComputeIntParam(worldCompute, EvictAllDirtyId, allDirty ? 1 : 0);
                command.SetComputeIntParam(worldCompute, SafeEpochId, 3);
                command.SetComputeIntParam(worldCompute, "_M8FlowerCaptureContinue", 0);
                command.DispatchComputeProfiled(worldCompute, _prepareEvictionSelectionKernel, 1, 1, 1);
                command.DispatchComputeProfiled(worldCompute, _selectEvictionVictimsKernel,
                    DivideRoundUp(MerkabaSpatial.PhysicalTileCapacity, 256), 1, 1);
                command.DispatchComputeProfiled(worldCompute, _gatherWritebackBatchKernel,
                    StreamBatchCapacity, 1, 1);
                MerkabaGpuTimestamps.End(CaptureOwner.Observation, command, timed);
                SubmitWorldMutation(command, generation);
                submitted = true;
                MerkabaGpuTimestamps.Complete(CaptureOwner.Observation, timed, true);
                return true;
            }
            finally
            {
                if (!submitted) MerkabaGpuTimestamps.Complete(CaptureOwner.Observation, timed, false);
                CancelWorldMutationBeforeSubmit(generation);
                CommandBufferPool.Release(command);
            }
        }

        internal bool AcknowledgeWritebackBatch(int count)
        {
            return ExecuteWorldStorageBatch(_acknowledgeWritebackBatchKernel, count, false);
        }

        private bool ContinueFlowerWritebackBatch(int count)
        {
            if (!WorldMutationSubmissionAllowed) return false;
            if (count < 1 || count > StreamBatchCapacity)
                throw new ArgumentOutOfRangeException(nameof(count));
            CommandBuffer command = CommandBufferPool.Get("Merkaba fine storage continuation");
            uint generation = 0u;
            bool timed = false, submitted = false;
            try
            {
                generation = RecordWorldMutation(command, worldCompute);
                timed = MerkabaGpuTimestamps.TryAcquireStorage(generation, command);
                command.SetComputeIntParam(worldCompute, "_M8FlowerCaptureContinue", 1);
                command.DispatchComputeProfiled(worldCompute, _gatherWritebackBatchKernel, count, 1, 1);
                MerkabaGpuTimestamps.End(CaptureOwner.Observation, command, timed);
                SubmitWorldMutation(command, generation);
                submitted = true;
                MerkabaGpuTimestamps.Complete(CaptureOwner.Observation, timed, true);
                return true;
            }
            finally
            {
                if (!submitted) MerkabaGpuTimestamps.Complete(CaptureOwner.Observation, timed, false);
                CancelWorldMutationBeforeSubmit(generation);
                CommandBufferPool.Release(command);
            }
        }

        internal void FailWritebackBatch(int count)
        {
            if (!GpuSubmissionAllowed) return;
            worldCompute.SetInt(StreamBatchCountId, count);
            worldCompute.Dispatch(_failWritebackBatchKernel, 1, 1, 1);
        }

        internal bool InstallLoadedTiles(int count)
        {
            return ExecuteWorldStorageBatch(_installLoadedTilesKernel, count, true);
        }

        private bool CancelLoadedFlowerTiles(int count) =>
            ExecuteWorldStorageBatch(_failLoadedTilesKernel,count,false);

        private bool ExecuteWorldStorageBatch(int kernel, int count, bool install)
        {
            if (!WorldMutationSubmissionAllowed) return false;
            if (count < 0 || count > StreamBatchCapacity)
                throw new ArgumentOutOfRangeException(nameof(count));
            CommandBuffer command = CommandBufferPool.Get("Merkaba world storage batch");
            uint generation = 0u;
            bool timed = false, submitted = false;
            try
            {
                generation = RecordWorldMutation(command, worldCompute);
                timed = MerkabaGpuTimestamps.TryAcquireStorage(generation, command);
                command.SetComputeIntParam(worldCompute, StreamBatchCountId, count);
                if (install)
                {
                    command.DispatchComputeProfiled(worldCompute, _prepareLoadedTilesKernel, 1, 1, 1);
                    if(count!=0)
                    {
                        command.SetComputeIntParam(worldCompute,"_M8FlowerStorageMode",0);
                        command.DispatchComputeProfiled(worldCompute,_transferFlowerStorageKernel,1,1,1);
                    }
                }
                else if(kernel==_acknowledgeWritebackBatchKernel)
                {
                    command.SetComputeIntParam(worldCompute,"_M8FlowerStorageMode",1);
                    command.DispatchComputeProfiled(worldCompute,_transferFlowerStorageKernel,1,1,1);
                }
                else if(kernel==_failLoadedTilesKernel)
                {
                    command.SetComputeIntParam(worldCompute,"_M8FlowerStorageMode",2);
                    command.DispatchComputeProfiled(worldCompute,_transferFlowerStorageKernel,1,1,1);
                }
                if (!install || count != 0)
                    command.DispatchComputeProfiled(worldCompute, kernel, install ? count : 1, 1, 1);
                MerkabaGpuTimestamps.End(CaptureOwner.Observation, command, timed);
                SubmitWorldMutation(command, generation);
                submitted = true;
                MerkabaGpuTimestamps.Complete(CaptureOwner.Observation, timed, true);
                return true;
            }
            finally
            {
                if (!submitted) MerkabaGpuTimestamps.Complete(CaptureOwner.Observation, timed, false);
                CancelWorldMutationBeforeSubmit(generation);
                CommandBufferPool.Release(command);
            }
        }

        internal void FailLoadedTiles(int count)
        {
            if (!GpuSubmissionAllowed) return;
            worldCompute.SetInt(StreamBatchCountId, count);
            worldCompute.Dispatch(_failLoadedTilesKernel, 1, 1, 1);
        }

        internal void RegisterLoadedTileAddresses(int count)
        {
            if (!GpuSubmissionAllowed) return;
            worldCompute.SetInt(StreamBatchCountId, count);
            // At most 32 unique addresses participate. In the legal worst
            // case CLAIMED/colliding blocks serialize one address per round;
            // two final rounds then publish its chunk and tile path. Explicit
            // Load verifies the final tile count and all capacity flags.
            int boundedConvergenceRounds = LoadRegistrationRoundLimit(count);
            for (int round = 0; round < boundedConvergenceRounds; round++)
            {
                worldCompute.Dispatch(_registerLoadedTileAddressesKernel,
                    DivideRoundUp(count, 64), 1, 1);
                PublishClaimedBlocksAndChunks();
                ResetClaimQueues();
            }
        }

        internal static int LoadRegistrationRoundLimit(int count)
        {
            if (count <= 0 || count > StreamBatchCapacity)
                throw new ArgumentOutOfRangeException(nameof(count));
            return checked(count + 2);
        }

        internal void RecordHashBenchmark(CommandBuffer command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (!GpuSubmissionAllowed) return;
            command.DispatchComputeProfiled(worldCompute, _benchmarkHashKernel,
                MerkabaSpatial.BlockCapacity / 256, 1, 1);
        }

        internal uint AllocateObservationToken()
        {
            if (!GpuSubmissionAllowed) return 0u;
            NextObservationToken();
            return _issuedObservationToken;
        }

        private void NextObservationToken()
        {
            if (_issuedObservationToken == uint.MaxValue)
            {
                _gpuSubmissionSuspended = true;
                throw new InvalidOperationException(
                    "Observation identity capacity exhausted; HOT R1 stamps cannot wrap.");
            }
            ++_issuedObservationToken;
        }

        internal void PublishClaimedBlocks()
        {
            if (!GpuSubmissionAllowed) return;
            worldCompute.Dispatch(_publishNewBlocksKernel, 1, 1, 1);
        }

        internal void RecordPublishClaimedBlocks(CommandBuffer command)
        {
            if (!GpuSubmissionAllowed) return;
            command.DispatchComputeProfiled(worldCompute,
                _publishNewBlocksKernel, _m8ObservationDispatchArgs);
        }

        internal void PublishClaimedChunks()
        {
            if (!GpuSubmissionAllowed) return;
            worldCompute.Dispatch(_publishNewChunksKernel, 1, 1, 1);
        }

        internal void RecordPublishClaimedChunks(CommandBuffer command)
        {
            if (!GpuSubmissionAllowed) return;
            command.DispatchComputeProfiled(worldCompute,
                _publishNewChunksKernel, _m8ObservationDispatchArgs);
        }

        internal void PublishClaimedBlocksAndChunks()
        {
            PublishClaimedBlocks();
            PublishClaimedChunks();
        }

        internal void RecordInitializeClaimedTiles(CommandBuffer command)
        {
            if (!GpuSubmissionAllowed) return;
            command.DispatchComputeProfiled(worldCompute,
                _initializeNewTilesKernel, _m8ObservationDispatchArgs);
        }

        internal void ResetClaimQueues()
        {
            if (!GpuSubmissionAllowed) return;
            worldCompute.Dispatch(_resetClaimQueuesKernel, 1, 1, 1);
        }

        internal void RecordResetClaimQueues(CommandBuffer command)
        {
            if (!GpuSubmissionAllowed) return;
            command.DispatchComputeProfiled(worldCompute,
                _resetClaimQueuesKernel, 1, 1, 1);
        }

        internal void RecordPrepareNewTileDispatch(CommandBuffer command)
        {
            if (!GpuSubmissionAllowed) return;
            command.DispatchComputeProfiled(worldCompute,
                _prepareNewTileDispatchKernel, 1, 1, 1);
        }

        private void InitializeGpuWorld()
        {
            _clearRawKernel = worldCompute.FindKernel("ClearRawBuffer");
            ClearInt4(_m8HashEntries, MerkabaSpatial.HashEntryCount);
            ClearUInt(_m8BlockChunkRefs, _m8BlockChunkRefs.count);
            ClearUInt(_m8BlockPresenceL0, _m8BlockPresenceL0.count);
            ClearUInt(_m8BlockPresenceL1, _m8BlockPresenceL1.count);
            ClearUInt(_m8BlockPresenceL2, _m8BlockPresenceL2.count);
            ClearUInt(_m8ChunkTileRefs, _m8ChunkTileRefs.count);
            ClearUInt(_m8ChunkPresence, _m8ChunkPresence.count);
            ClearUInt(_m8Counters, _m8Counters.count);
            ClearUInt(_m8LoadRequestReadCount, 1);
            ClearUInt(_m8FrameDispatchArgs, _m8FrameDispatchArgs.count);
            ClearUInt(_m8ObservationDispatchArgs, _m8ObservationDispatchArgs.count);
            worldCompute.Dispatch(_resetCountersKernel, 1, 1, 1);
            worldCompute.Dispatch(_initializeFreeTilesKernel,
                DivideRoundUp(MerkabaSpatial.PhysicalTileCapacity, 256), 1, 1);
            M8BlockCount = M8ChunkCount = M8HotTileCount = M8ColdTileCount = 0;
            M8OccupiedKernelCount = 0;
            ResetWorldGpuLifecycle();
            ResetStorageRuntimeState();
        }

        internal void ClearGpuWorldForNewScan()
        {
            if (!GpuSubmissionAllowed) return;
            if (!WorldMutationSubmissionAllowed)
                throw new InvalidOperationException("Cannot clear a leased world GPU world.");
            _gpuGeneration++;
            ResetFlowerPagesAfterRetirement();
            worldCompute.Dispatch(_prepareAllocatedClearKernel, 1, 1, 1);
            worldCompute.DispatchIndirect(_clearAllocatedBlocksKernel,
                _m8FrameDispatchArgs);
            worldCompute.DispatchIndirect(_clearAllocatedChunksKernel,
                _m8ObservationDispatchArgs);
            ClearInt4(_m8HashEntries, MerkabaSpatial.HashEntryCount);
            ClearUInt(_m8Counters, _m8Counters.count);
            ClearUInt(_m8LoadRequestReadCount, 1);
            ClearUInt(_m8FrameDispatchArgs, _m8FrameDispatchArgs.count);
            ClearUInt(_m8ObservationDispatchArgs,
                _m8ObservationDispatchArgs.count);
            worldCompute.Dispatch(_resetCountersKernel, 1, 1, 1);
            worldCompute.Dispatch(_initializeFreeTilesKernel,
                DivideRoundUp(MerkabaSpatial.PhysicalTileCapacity, 256), 1, 1);
            M8BlockCount = M8ChunkCount = M8HotTileCount = M8ColdTileCount = 0;
            M8OccupiedKernelCount = 0;
            ResetWorldGpuLifecycle();
            ResetStorageRuntimeState();
        }

        private void ResetWorldGpuLifecycle()
        {
            _worldGenerationSequence = _worldPublishedGeneration =
                _worldRetiredGeneration = _worldDurableGeneration =
                _worldMutationGeneration = _worldRetirementFenceGeneration = 0u;
            _worldNativeMutation = _worldSubmissionUncertain =
                _worldRetirementFencePending = false;
            _worldRetirementFence = default;
        }

        private void ClearUInt(ComputeBuffer buffer, int count)
        {
            worldCompute.SetBuffer(_clearUIntKernel, ClearUIntsId, buffer);
            DispatchLinear(_clearUIntKernel, count);
        }

        private void ClearRaw(ComputeBuffer buffer)
        {
            worldCompute.SetBuffer(_clearRawKernel, "_M8ClearRaw", buffer);
            DispatchLinear(_clearRawKernel, buffer.count);
        }

        private void ClearInt4(ComputeBuffer buffer, int count)
        {
            worldCompute.SetBuffer(_clearInt4Kernel, ClearInt4sId, buffer);
            DispatchLinear(_clearInt4Kernel, count);
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

        private void Update()
        {
            if (_gpuReady) PumpStorage();
        }

        private static int ToInt(uint value) =>
            value > int.MaxValue ? int.MaxValue : (int)value;

        internal void BeginGpuSubmissionQuiesce() =>
            _gpuSubmissionSuspended = true;

        internal void ResumeGpuSubmission() => _gpuSubmissionSuspended = false;

        internal Task RetireSubmittedGpuWorkAsync()
        {
            _gpuSubmissionSuspended = true;
            if (!_gpuReady || _m8Counters == null) return Task.CompletedTask;
            if (!_gpuRetirementTask.IsCompleted) return _gpuRetirementTask;
            if (!SystemInfo.supportsAsyncGPUReadback)
                return Task.FromException(new NotSupportedException(
                    "Quest GPU teardown requires asynchronous retirement."));
            int generation = _gpuGeneration;
            var completion = new TaskCompletionSource<bool>();
            _gpuRetirementTask = completion.Task;
            AsyncGPUReadback.Request(_m8Counters, sizeof(uint), 0, request =>
            {
                if (generation != _gpuGeneration)
                    completion.TrySetException(new IOException(
                        "M8 GPU generation changed before retirement."));
                else if (request.hasError)
                    completion.TrySetException(new IOException(
                        "M8 GPU retirement marker failed."));
                else
                    completion.TrySetResult(true);
            });
            return _gpuRetirementTask;
        }

        internal Action CaptureOwnedGpuResourceRelease()
        {
            ComputeBuffer[] captured = _allGpuBuffers.ToArray();
            GraphicsBuffer capturedIndices = _m8FlowerIndices;
            bool released = false;
            return () =>
            {
                if (released) return;
                released = true;
                if (this != null)
                {
                    ReleaseOwnedResourcesAfterGpuRetirement();
                    return;
                }
                foreach (ComputeBuffer buffer in captured) buffer?.Release();
                capturedIndices?.Dispose();
            };
        }

        internal void ReleaseOwnedResourcesAfterGpuRetirement() =>
            ReleaseGpuResources();

        private void ReleaseGpuResources()
        {
            _gpuGeneration++;
            foreach (ComputeBuffer buffer in _allGpuBuffers) buffer?.Release();
            _allGpuBuffers.Clear();
            ForgetFlowerPagesAfterRelease();
            _m8FlowerIndices?.Dispose();
            _m8FlowerIndices = null;
            _m8HashEntries = null;
            _m8OwnerRecords = null;
            _m8BlockChunkRefs = null;
            _m8BlockPresenceL0 = null;
            _m8BlockPresenceL1 = null;
            _m8BlockPresenceL2 = null;
            _m8ChunkTileRefs = null;
            _m8ChunkPresence = null;
            _m8KernelStates0 = null;
            _m8KernelStates1 = null;
            _m8KernelStates2 = null;
            _m8KernelStates3 = null;
            _m8TileBits = null;
            _m8TileRecords = null;
            _m8TileHalo = null;
            _m8FlowerTables = null;
            _m8FreeTileStack = null;
            _m8Counters = null;
            _m8AttemptCompletion = null;
            _m8ClaimQueue = null;
            _m8ObservationRecords = null;
            _m8FlowerSignalItems = null;
            _m8TouchedTileQueue = null;
            _m8ObservationDispatchArgs = null;
            _m8LoadRequestReadCount = null;
            _m8HashBenchmarkOutput = null;
            ResetWorldGpuLifecycle();
            ResetStorageRuntimeState();
            _gpuReady = false;
        }

    }
}
