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
        [Header("M8 GPU World")]
        [SerializeField] private ComputeShader worldCompute;

        internal const int SurfaceCandidateCapacity = 2_097_152;
        internal const int SurfaceQueueCapacity = 1_048_576;
        internal const int LoadRequestCapacity = 262144;
        internal const int LoadRequestMask = LoadRequestCapacity - 1;
        internal const int StreamBatchCapacity = 32;
        internal const int ReadoutTriangleCapacityPerBuffer = 2_097_152;
        internal const int ReadoutTriangleCapacity =
            ReadoutTriangleCapacityPerBuffer * 2;
        internal const int ReadoutVertexCapacityPerBuffer =
            ReadoutTriangleCapacityPerBuffer *
            MerkabaCanonicalGeometry.VerticesPerPrimitive;
        internal const int ReadoutIndexCapacity = ReadoutTriangleCapacity *
            MerkabaCanonicalGeometry.VerticesPerPrimitive;
        internal const int ReadoutIndexMapWordCount =
            MerkabaSpatial.PhysicalTileCapacity *
            MerkabaSpatial.KernelsPerTile / 2;
        internal const int ReadoutIndexStorageCount =
            ReadoutIndexCapacity + ReadoutIndexMapWordCount;
        internal const int ReadoutFrontFaceDimension = 512;
        internal const int ReadoutFrontPixelCount = 6 *
            ReadoutFrontFaceDimension * ReadoutFrontFaceDimension;
        internal const int ReadoutVisibleBufferCount =
            ReadoutFrontPixelCount > MerkabaSpatial.PhysicalTileCapacity
                ? ReadoutFrontPixelCount
                : MerkabaSpatial.PhysicalTileCapacity;
        internal const int ReadoutResetGroupCount =
            (ReadoutFrontPixelCount + 127) / 128;
        internal const int CounterCount = 98;

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
        internal const int CounterValidSurfaceCandidates = 10;
        internal const int CounterUniqueSurfaceKernels = 11;
        internal const int CounterUnresolvedSurfaceTiles = 12;
        internal const int CounterSurfaceTilesAllocated = 13;
        internal const int CounterScanColdMisses = 14;
        internal const int CounterCarveCandidateTiles = 17;
        internal const int CounterCarveActiveKernels = 18;
        internal const int CounterLoadRequests = 19;
        internal const int CounterVisibleTiles = 21;
        internal const int CounterLogicalPrimitives = 22;
        internal const int CounterRenderPrimitiveOverflow = 23;
        internal const int CounterLateDrawColdMisses = 24;
        internal const int CounterCandidateBlocks = 26;
        internal const int CounterHashHitBlocks = 27;
        internal const int CounterVisibleChunks = 28;
        internal const int CounterOccupiedKernelsConsidered = 29;
        internal const int CounterReadoutPlaneValid = 30;
        internal const int CounterReadoutEmittedPatches = 31;
        internal const int CounterHashFull = 38;
        internal const int CounterFailedReads = 39;
        internal const int CounterFailedWrites = 40;
        internal const int CounterStorageBackpressure = 41;
        internal const int CounterOccupiedKernelCount = 42;
        internal const int CounterCarveQueryBlocks = 48;
        internal const int CounterWritebackTiles = 49;
        internal const int CounterEvictionNeeded = 51;
        internal const int CounterObservationFailure = 52;
        internal const int CounterFailedObservations = 53;
        internal const int CounterFreeTileCount = 54;
        internal const int CounterLoadsInstalled = 46;
        internal const int CounterCarveClassifiedFree = 56;
        internal const int CounterCarveClassifiedSurface = 57;
        internal const int CounterCarveClassifiedUnknown = 58;
        internal const int CounterCarveEvidenceDecrements = 59;
        internal const int CounterCarveOccupiedToFree = 60;
        internal const int CounterCarveBitsRetired = 61;
        internal const int CounterColdCarveTilesRequested = 62;
        internal const int CounterUnresolvedCarveTiles = 63;
        internal const int CounterResidencyEpoch = 64;
        internal const int CounterReadoutUnresolved = 50;
        internal const int CounterReadoutBuildStatus = 69;
        internal const int CounterCarveFreeRadialBase = 70;
        internal const int CounterJointAcceptedCenter = 78;
        internal const int CounterJointAcceptedMid = 79;
        internal const int CounterJointAcceptedEdge = 80;
        internal const int CounterAuthorityDiscovery = 81;
        internal const int CounterAuthoritySupport = 82;
        internal const int CounterAuthorityRevision = 83;
        internal const int CounterOffAxisMutationBlocked = 84;
        internal const int CounterSurfaceReplacement = 85;
        internal const int CounterSameObservationConflict = 86;
        internal const int CounterReadoutEmittedTriangles = 87;
        internal const int CounterCarveCheapInvalidProjectionDepth = 88;
        internal const int CounterCarveCheapNotInFront = 89;
        internal const int CounterCarveCheapOutsideRayTube = 90;
        internal const int CounterCarveCheapOutsideOuterAttention = 91;
        internal const int CounterCarveCheapSurfaceEndpoint = 92;
        internal const int CounterCarveKernelsEvaluated = 93;
        internal const int CounterCarveExactIncidenceReject = 94;
        internal const int CounterCarveExactDilationReject = 95;
        internal const int CounterReadoutPlaneLegacyInvalid = 96;
        internal const int CounterReadoutEmittedVertices = 97;

        internal bool GpuSubmissionAllowed =>
            _gpuReady && !_gpuSubmissionSuspended;
        internal bool GpuSubmissionSuspended => _gpuSubmissionSuspended;

        private bool _gpuReady;
        private int _gpuGeneration;

        private int _clearUIntKernel;
        private int _clearInt4Kernel;
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
        private int _prepareNewTileDispatchKernel;
        private int _clearTouchedCandidatesKernel;
        private int _prepareEvictionSelectionKernel;
        private int _selectEvictionVictimsKernel;
        private int _gatherWritebackBatchKernel;
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
        private ComputeBuffer _m8FreeTileStack;
        private ComputeBuffer _m8Counters;
        private ComputeBuffer _m8AttemptCompletion;
        private ComputeBuffer _m8ClaimQueue;
        private ComputeBuffer _m8PendingNewTileRefs;
        private ComputeBuffer _m8LoadRequests;
        private ComputeBuffer _m8LoadRequestReadCount;
        private ComputeBuffer _m8SurfaceCandidates;
        private ComputeBuffer _m8SurfaceQueue;
        private ComputeBuffer _m8SurfaceWinnerRanks0;
        private ComputeBuffer _m8SurfaceWinnerRanks1;
        private ComputeBuffer _m8SurfaceWinnerRanks2;
        private ComputeBuffer _m8SurfaceWinnerRanks3;
        private ComputeBuffer _m8TouchedTileQueue;
        private ComputeBuffer _m8CarveTiles;
        private ComputeBuffer _m8CarveDispatchArgs;
        private ComputeBuffer _m8VisibleTiles;
        private readonly ComputeBuffer[] _m8ReadoutVertices0 =
            new ComputeBuffer[2];
        private readonly ComputeBuffer[] _m8ReadoutVertices1 =
            new ComputeBuffer[2];
        private readonly GraphicsBuffer[] _m8ReadoutIndices =
            new GraphicsBuffer[2];
        private ComputeBuffer _m8FrameDispatchArgs;
        private readonly ComputeBuffer[] _m8DrawArgs = new ComputeBuffer[2];
        private ComputeBuffer _m8ObservationDispatchArgs;
        private ComputeBuffer _m8WritebackQueue;
        private ComputeBuffer _m8WritebackStaging;
        private ComputeBuffer _m8LoadStagingAddresses;
        private ComputeBuffer _m8LoadStagingStates;
        private ComputeBuffer _m8HashBenchmarkOutput;

        private readonly List<ComputeBuffer> _allGpuBuffers = new();
        private readonly List<GraphicsBuffer> _allGraphicsBuffers = new();

        internal ComputeShader WorldCompute => worldCompute;
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
        internal ComputeBuffer M8FreeTileStack => _m8FreeTileStack;
        internal ComputeBuffer M8Counters => _m8Counters;
        internal ComputeBuffer M8AttemptCompletion => _m8AttemptCompletion;
        internal ComputeBuffer M8ClaimQueue => _m8ClaimQueue;
        internal ComputeBuffer M8PendingNewTileRefs => _m8PendingNewTileRefs;
        internal ComputeBuffer M8LoadRequests => _m8LoadRequests;
        internal ComputeBuffer M8LoadRequestReadCount =>
            _m8LoadRequestReadCount;
        internal ComputeBuffer M8SurfaceCandidates => _m8SurfaceCandidates;
        internal ComputeBuffer M8SurfaceQueue => _m8SurfaceQueue;
        internal ComputeBuffer M8SurfaceWinnerRanks0 =>
            _m8SurfaceWinnerRanks0;
        internal ComputeBuffer M8SurfaceWinnerRanks1 =>
            _m8SurfaceWinnerRanks1;
        internal ComputeBuffer M8SurfaceWinnerRanks2 =>
            _m8SurfaceWinnerRanks2;
        internal ComputeBuffer M8SurfaceWinnerRanks3 =>
            _m8SurfaceWinnerRanks3;
        internal ComputeBuffer M8TouchedTileQueue => _m8TouchedTileQueue;
        internal ComputeBuffer M8CarveTiles => _m8CarveTiles;
        internal ComputeBuffer M8CarveDispatchArgs => _m8CarveDispatchArgs;
        internal ComputeBuffer M8VisibleTiles => _m8VisibleTiles;
        internal ComputeBuffer GetM8ReadoutVertices0(int slot) =>
            _m8ReadoutVertices0[ValidateReadoutSlot(slot)];
        internal ComputeBuffer GetM8ReadoutVertices1(int slot) =>
            _m8ReadoutVertices1[ValidateReadoutSlot(slot)];
        internal GraphicsBuffer GetM8ReadoutIndices(int slot) =>
            _m8ReadoutIndices[ValidateReadoutSlot(slot)];
        internal ComputeBuffer M8FrameDispatchArgs => _m8FrameDispatchArgs;
        internal ComputeBuffer GetM8DrawArgs(int slot) =>
            _m8DrawArgs[ValidateReadoutSlot(slot)];
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
            Set(MerkabaNativeVulkanExecutor.Resource.SurfaceCandidates,
                _m8SurfaceCandidates);
            Set(MerkabaNativeVulkanExecutor.Resource.SurfaceQueue,
                _m8SurfaceQueue);
            Set(MerkabaNativeVulkanExecutor.Resource.SurfaceWinnerRanks0,
                _m8SurfaceWinnerRanks0);
            Set(MerkabaNativeVulkanExecutor.Resource.SurfaceWinnerRanks1,
                _m8SurfaceWinnerRanks1);
            Set(MerkabaNativeVulkanExecutor.Resource.SurfaceWinnerRanks2,
                _m8SurfaceWinnerRanks2);
            Set(MerkabaNativeVulkanExecutor.Resource.SurfaceWinnerRanks3,
                _m8SurfaceWinnerRanks3);
            Set(MerkabaNativeVulkanExecutor.Resource.TouchedTileQueue,
                _m8TouchedTileQueue);
            Set(MerkabaNativeVulkanExecutor.Resource.CarveTiles,
                _m8CarveTiles);
            Set(MerkabaNativeVulkanExecutor.Resource.ObservationDispatchArgs,
                _m8ObservationDispatchArgs);
            Set(MerkabaNativeVulkanExecutor.Resource.CarveDispatchArgs,
                _m8CarveDispatchArgs);
            Set(MerkabaNativeVulkanExecutor.Resource.AttemptCompletion,
                _m8AttemptCompletion);
            Set(MerkabaNativeVulkanExecutor.Resource.VisibleTiles,
                _m8VisibleTiles);
            Set(MerkabaNativeVulkanExecutor.Resource.FrameDispatchArgs,
                _m8FrameDispatchArgs);
        }

        internal bool GpuReady => _gpuReady;
        internal Matrix4x4 GridToWorldMatrix => transform.localToWorldMatrix;
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
        private static readonly int ObservationTokenId =
            Shader.PropertyToID("_M8ObservationToken");
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
                _m8FreeTileStack = Allocate(MerkabaSpatial.PhysicalTileCapacity,
                    sizeof(uint));
                _m8Counters = Allocate(CounterCount, sizeof(uint));
                _m8AttemptCompletion = Allocate(1, sizeof(uint) * 4);

                _m8ClaimQueue = Allocate(MerkabaSpatial.ClaimRecordCount,
                    sizeof(uint) * 2);
                _m8PendingNewTileRefs = Allocate(MerkabaSpatial.PhysicalTileCapacity,
                    sizeof(uint));
                _m8LoadRequests = Allocate(LoadRequestCapacity, 16);
                _m8LoadRequestReadCount = Allocate(1, sizeof(uint));
                _m8SurfaceCandidates = Allocate(SurfaceCandidateCapacity, 16);
                _m8SurfaceQueue = Allocate(SurfaceQueueCapacity,
                    sizeof(uint) * 2);
                _m8SurfaceWinnerRanks0 = Allocate(bankStateCount, sizeof(uint));
                _m8SurfaceWinnerRanks1 = Allocate(bankStateCount, sizeof(uint));
                _m8SurfaceWinnerRanks2 = Allocate(bankStateCount, sizeof(uint));
                _m8SurfaceWinnerRanks3 = Allocate(bankStateCount, sizeof(uint));
                _m8TouchedTileQueue = Allocate(MerkabaSpatial.PhysicalTileCapacity,
                    sizeof(uint));
                _m8CarveTiles = Allocate(MerkabaSpatial.PhysicalTileCapacity,
                    sizeof(uint));
                _m8CarveDispatchArgs = Allocate(3, sizeof(uint),
                    ComputeBufferType.IndirectArguments);
                _m8VisibleTiles = Allocate(ReadoutVisibleBufferCount,
                    sizeof(uint) * 2);
                for (int slot = 0; slot < 2; slot++)
                {
                    _m8ReadoutVertices0[slot] = Allocate(
                        ReadoutVertexCapacityPerBuffer, 16);
                    _m8ReadoutVertices1[slot] = Allocate(
                        ReadoutVertexCapacityPerBuffer, 16);
                    _m8ReadoutIndices[slot] = AllocateGraphics(
                        ReadoutIndexStorageCount, sizeof(uint),
                        GraphicsBuffer.Target.Raw |
                        GraphicsBuffer.Target.Index);
                    _m8DrawArgs[slot] = Allocate(5, sizeof(uint),
                        ComputeBufferType.IndirectArguments);
                }
                _m8FrameDispatchArgs = Allocate(3, sizeof(uint),
                    ComputeBufferType.IndirectArguments);
                _m8ObservationDispatchArgs = Allocate(3, sizeof(uint),
                    ComputeBufferType.IndirectArguments);
                _m8WritebackQueue = Allocate(StreamBatchCapacity,
                    sizeof(uint) * 2);
                _m8WritebackStaging = Allocate(StreamBatchCapacity *
                    (MerkabaSpatial.KernelsPerTile + 1), 16);
                _m8LoadStagingAddresses = Allocate(StreamBatchCapacity, 16);
                _m8LoadStagingStates = Allocate(StreamBatchCapacity *
                    MerkabaSpatial.KernelsPerTile, 16);
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
                BindWorldBuffers(worldCompute, _prepareNewTileDispatchKernel);
                BindWorldBuffers(worldCompute, _clearTouchedCandidatesKernel);
                BindWorldBuffers(worldCompute, _prepareEvictionSelectionKernel);
                BindWorldBuffers(worldCompute, _selectEvictionVictimsKernel);
                BindWorldBuffers(worldCompute, _gatherWritebackBatchKernel);
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
            _resetObservationKernel = worldCompute.FindProfiledKernel(
                "ResetObservationCounters", MerkabaGpuStage.SurfaceIntegration);
            _resetClaimQueuesKernel = worldCompute.FindProfiledKernel(
                "ResetClaimQueueCounts", MerkabaGpuStage.SurfaceIntegration);
            _prepareNewTileDispatchKernel = worldCompute.FindProfiledKernel(
                "PrepareNewTileDispatchArgs",
                MerkabaGpuStage.SurfaceIntegration);
            _clearTouchedCandidatesKernel = worldCompute.FindProfiledKernel(
                "ClearTouchedSurfaceCandidates",
                MerkabaGpuStage.SurfaceIntegration);
            _prepareEvictionSelectionKernel =
                worldCompute.FindKernel("PrepareEvictionSelection");
            _selectEvictionVictimsKernel =
                worldCompute.FindKernel("SelectEvictionVictims");
            _gatherWritebackBatchKernel =
                worldCompute.FindKernel("GatherWritebackBatch");
            _acknowledgeWritebackBatchKernel =
                worldCompute.FindKernel("AcknowledgeWritebackBatch");
            _failWritebackBatchKernel =
                worldCompute.FindKernel("FailWritebackBatch");
            _prepareLoadedTilesKernel = worldCompute.FindKernel("PrepareLoadedTiles");
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

        private GraphicsBuffer AllocateGraphics(int count, int stride,
            GraphicsBuffer.Target target)
        {
            var buffer = new GraphicsBuffer(target, count, stride);
            _allGraphicsBuffers.Add(buffer);
            return buffer;
        }

        internal void BindWorldBuffers(ComputeShader shader, int kernel)
        {
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
        }

        internal void SelectEvictionVictims(bool allDirty)
        {
            if (!GpuSubmissionAllowed) return;
            worldCompute.SetInt(EvictAllDirtyId, allDirty ? 1 : 0);
            worldCompute.SetInt(SafeEpochId, 3);
            worldCompute.Dispatch(_prepareEvictionSelectionKernel, 1, 1, 1);
            worldCompute.Dispatch(_selectEvictionVictimsKernel,
                DivideRoundUp(MerkabaSpatial.PhysicalTileCapacity, 256), 1, 1);
            worldCompute.Dispatch(_gatherWritebackBatchKernel,
                StreamBatchCapacity, 1, 1);
        }

        internal void AcknowledgeWritebackBatch(int count)
        {
            if (!GpuSubmissionAllowed) return;
            worldCompute.SetInt(StreamBatchCountId, count);
            worldCompute.Dispatch(_acknowledgeWritebackBatchKernel, 1, 1, 1);
        }

        internal void FailWritebackBatch(int count)
        {
            if (!GpuSubmissionAllowed) return;
            worldCompute.SetInt(StreamBatchCountId, count);
            worldCompute.Dispatch(_failWritebackBatchKernel, 1, 1, 1);
        }

        internal void InstallLoadedTiles(int count)
        {
            if (!GpuSubmissionAllowed) return;
            worldCompute.SetInt(StreamBatchCountId, count);
            worldCompute.Dispatch(_prepareLoadedTilesKernel, 1, 1, 1);
            worldCompute.Dispatch(_installLoadedTilesKernel, count, 1, 1);
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

        internal uint ResetObservationGpuCounters()
        {
            if (!GpuSubmissionAllowed) return 0u;
            NextObservationToken();
            worldCompute.SetInt(ObservationTokenId,
                unchecked((int)_issuedObservationToken));
            worldCompute.Dispatch(_resetObservationKernel, 1, 1, 1);
            return _issuedObservationToken;
        }

        internal uint RecordResetObservationGpuCounters(CommandBuffer command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (!GpuSubmissionAllowed) return 0u;
            NextObservationToken();
            command.SetComputeIntParam(worldCompute, ObservationTokenId,
                unchecked((int)_issuedObservationToken));
            command.DispatchComputeProfiled(worldCompute,
                _resetObservationKernel, 1, 1, 1);
            return _issuedObservationToken;
        }

        internal uint AllocateNativeObservationToken()
        {
            if (!GpuSubmissionAllowed) return 0u;
            NextObservationToken();
            return _issuedObservationToken;
        }

        private void NextObservationToken()
        {
            unchecked
            {
                _issuedObservationToken++;
                if (_issuedObservationToken == 0u) _issuedObservationToken = 1u;
            }
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

        internal void ClearTouchedSurfaceCandidates()
        {
            if (!GpuSubmissionAllowed) return;
            worldCompute.SetBuffer(_clearTouchedCandidatesKernel,
                "_M8TouchedTileQueue", _m8TouchedTileQueue);
            worldCompute.DispatchIndirect(_clearTouchedCandidatesKernel,
                _m8ObservationDispatchArgs);
        }

        internal void RecordClearTouchedSurfaceCandidates(CommandBuffer command)
        {
            if (!GpuSubmissionAllowed) return;
            command.SetComputeBufferParam(worldCompute,
                _clearTouchedCandidatesKernel, "_M8TouchedTileQueue",
                _m8TouchedTileQueue);
            command.DispatchComputeProfiled(worldCompute,
                _clearTouchedCandidatesKernel, _m8ObservationDispatchArgs);
        }

        private void InitializeGpuWorld()
        {
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
            for (int slot = 0; slot < 2; slot++)
                ClearUInt(_m8DrawArgs[slot], _m8DrawArgs[slot].count);
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
            if (!GpuSubmissionAllowed) return;
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
            for (int slot = 0; slot < 2; slot++)
                ClearUInt(_m8DrawArgs[slot], _m8DrawArgs[slot].count);
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
            if (GpuSubmissionAllowed) PumpStorage();
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
            GraphicsBuffer[] capturedGraphics = _allGraphicsBuffers.ToArray();
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
                foreach (GraphicsBuffer buffer in capturedGraphics)
                    buffer?.Release();
            };
        }

        internal void ReleaseOwnedResourcesAfterGpuRetirement() =>
            ReleaseGpuResources();

        private void ReleaseGpuResources()
        {
            _gpuGeneration++;
            foreach (ComputeBuffer buffer in _allGpuBuffers) buffer?.Release();
            _allGpuBuffers.Clear();
            foreach (GraphicsBuffer buffer in _allGraphicsBuffers)
                buffer?.Release();
            _allGraphicsBuffers.Clear();
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
            for (int slot = 0; slot < 2; slot++)
            {
                _m8ReadoutVertices0[slot] = null;
                _m8ReadoutVertices1[slot] = null;
                _m8ReadoutIndices[slot] = null;
                _m8DrawArgs[slot] = null;
            }
            _m8FreeTileStack = null;
            _m8Counters = null;
            _m8AttemptCompletion = null;
            _m8ClaimQueue = null;
            _m8LoadRequestReadCount = null;
            _m8HashBenchmarkOutput = null;
            ResetStorageRuntimeState();
            _gpuReady = false;
        }

        private static int ValidateReadoutSlot(int slot)
        {
            if ((uint)slot >= 2u)
                throw new ArgumentOutOfRangeException(nameof(slot));
            return slot;
        }
    }
}
