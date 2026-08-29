using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using Unity.Mathematics;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class MerkabaGpuIntegrationTests
    {
        private const string Package = "Packages/com.genesis.roomscan/";

        [Test]
        public void M8ComputeAssets_ImportAndExposeOnlyBoundedKernels()
        {
            ComputeShader world = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                Package + "Runtime/Shaders/MerkabaWorld.compute");
            ComputeShader integration = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                Package + "Runtime/Shaders/MerkabaIntegration.compute");
            ComputeShader frame = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                Package + "Runtime/Shaders/MerkabaFrameCompiler.compute");
            Assert.That(world, Is.Not.Null);
            Assert.That(integration, Is.Not.Null);
            Assert.That(frame, Is.Not.Null);
            foreach (string kernel in new[]
                     {
                         "PublishNewBlocks", "PublishNewChunks",
                         "PrepareAllocatedClearArgs", "ClearAllocatedBlocks",
                         "ClearAllocatedChunks",
                         "InitializeNewTiles", "SelectEvictionVictims",
                         "PrepareEvictionSelection", "PrepareLoadedTiles",
                         "GatherWritebackBatch", "AcknowledgeWritebackBatch",
                         "FailWritebackBatch",
                         "InstallLoadedTiles", "RegisterLoadedTileAddresses",
                         "BenchmarkM8Pcg3d"
                     })
                Assert.DoesNotThrow(() => world.FindKernel(kernel), kernel);
            foreach (string kernel in new[]
                     {
                         "DiscoverSurfaceCandidates", "ResolveSurfaceBlocks",
                         "ResolveSurfaceChunks", "ResolveSurfaceTiles",
                         "QueueResolvedSurfaceCandidates",
                         "IntegrateSurfaceCandidates", "QueryCarveTiles",
                         "IntegrateCarveTiles", "FinalizeObservation"
                     })
                Assert.DoesNotThrow(() => integration.FindKernel(kernel), kernel);
            foreach (string kernel in new[]
                     {
                         "ResetFrame", "QueryM8Frame",
                         "CompileVisiblePrimitives", "FinalizeDrawArgs"
                     })
                Assert.DoesNotThrow(() => frame.FindKernel(kernel), kernel);
        }

        [Test]
        public void PhysicalBuffers_AreAtMostSixtyFourMiBAndTilesNeverCrossBanks()
        {
            long stateBank = (long)MerkabaSpatial.PhysicalTileBankCapacity *
                MerkabaSpatial.KernelsPerTile * 16;
            long chunkRefs = (long)MerkabaSpatial.ChunkCapacity *
                MerkabaSpatial.TilesPerChunk * sizeof(uint);
            Assert.That(stateBank, Is.EqualTo(64L * 1024 * 1024));
            Assert.That(chunkRefs, Is.EqualTo(64L * 1024 * 1024));
            Assert.That(stateBank, Is.LessThanOrEqualTo(128L * 1024 * 1024));
            Assert.That(chunkRefs, Is.LessThanOrEqualTo(128L * 1024 * 1024));
            string world = Source("Runtime/Shaders/MerkabaWorld.hlsl");
            Assert.That(world, Does.Contain(
                "physicalSlot >> MERKABA_M8_TILE_BANK_SHIFT"));
            Assert.That(world, Does.Contain(
                "physicalSlot & MERKABA_M8_TILE_BANK_MASK"));
            Assert.That(world, Does.Not.Contain(
                "physicalSlot / MERKABA_M8_TILE_BANK_CAPACITY"));

            long[] allBuffers =
            {
                32768L * 16, (8192L + 262144L) * 16,
                8192L * 512 * 4,
                8192L * 4, 8192L * 8 * 4, 8192L * 64 * 4,
                262144L * 64 * 4, 262144L * 9 * 4,
                stateBank, stateBank, stateBank, stateBank,
                32768L * 16 * 16, 32768L * 2 * 16,
                32768L * 4, 64L * 4,
                (8192L + 262144L + 32768L) * 8,
                32768L * 4, 262144L * 16, 4,
                2097152L * 16, 1048576L * 4,
                32768L * 4, 32768L * 4, 12, 32768L * 4,
                1048576L * 16, 12, 16, 12, 32L * 8,
                32L * 513 * 16, 32L * 16, 32L * 512 * 16,
                8192L * 16
            };
            Assert.That(allBuffers, Has.Length.EqualTo(35));
            Assert.That(allBuffers.Max(), Is.EqualTo(64L * 1024 * 1024));
            Assert.That(allBuffers.Sum(), Is.EqualTo(440895032L));

            Assert.That(MerkabaSpatial.OwnerRecordCount,
                Is.EqualTo(MerkabaSpatial.BlockCapacity +
                           MerkabaSpatial.ChunkCapacity));
            Assert.That(MerkabaSpatial.ChunkPresenceStride, Is.EqualTo(9));
            Assert.That(MerkabaSpatial.TileBitRecordCount,
                Is.EqualTo(MerkabaSpatial.PhysicalTileCapacity * 16));
            Assert.That(MerkabaSpatial.TileRecordCount,
                Is.EqualTo(MerkabaSpatial.PhysicalTileCapacity * 2));
            Assert.That(MerkabaSpatial.ClaimRecordCount,
                Is.EqualTo(MerkabaSpatial.BlockCapacity +
                           MerkabaSpatial.ChunkCapacity +
                           MerkabaSpatial.PhysicalTileCapacity));
        }

        [Test]
        public void QuestWorldBuffers_UsePackedSingleAuthoritiesAndReadAliases()
        {
            string world = Source("Runtime/Shaders/MerkabaWorld.hlsl");
            string gpu = Source("Runtime/Merkaba/MerkabaGrid.Gpu.cs");
            Assert.That(world, Does.Contain(
                "RWStructuredBuffer<uint4> _M8OwnerRecords"));
            Assert.That(world, Does.Contain(
                "RWStructuredBuffer<uint4> _M8TileBits"));
            Assert.That(world, Does.Contain(
                "RWStructuredBuffer<uint4> _M8TileRecords"));
            Assert.That(world, Does.Contain(
                "RWStructuredBuffer<uint2> _M8ClaimQueue"));
            foreach (string alias in new[]
                     {
                         "_M8OwnerRecordsRead", "_M8TileBitsRead",
                         "_M8TileRecordsRead", "_M8ClaimQueueRead",
                         "_M8KernelStates0Read", "_M8ChunkTileRefsRead"
                     })
            {
                Assert.That(world, Does.Contain("StructuredBuffer"));
                Assert.That(world, Does.Contain(alias));
                Assert.That(gpu, Does.Contain($"\"{alias}\""), alias);
            }
            foreach (string removed in new[]
                     {
                         "RWStructuredBuffer<int3> _M8BlockCoords",
                         "RWStructuredBuffer<uint2> _M8ChunkOwners",
                         "RWStructuredBuffer<uint> _M8ChunkPresenceL0",
                         "RWStructuredBuffer<uint> _M8ChunkPresenceL1",
                         "RWStructuredBuffer<uint> _M8OccupiedBits",
                         "RWStructuredBuffer<uint> _M8CarveActiveBits",
                         "RWStructuredBuffer<uint> _M8SurfaceCandidateBits",
                         "RWStructuredBuffer<uint4> _M8TileMeta",
                         "RWStructuredBuffer<uint4> _M8TileRuntime",
                         "RWStructuredBuffer<uint2> _M8NewBlockQueue",
                         "RWStructuredBuffer<uint2> _M8NewChunkQueue",
                         "RWStructuredBuffer<uint2> _M8NewTileQueue",
                         "RWStructuredBuffer<uint> _M8FreeTileCount",
                         "RWStructuredBuffer<uint> _M8StreamStatus"
                     })
                Assert.That(world + gpu, Does.Not.Contain(removed), removed);
        }

        [Test]
        public void FrameTopology_HashesOnlyAcrossAnM8Boundary()
        {
            string world = Source("Runtime/Shaders/MerkabaWorld.hlsl");
            string frame = Source("Runtime/Shaders/MerkabaFrameCompiler.compute");
            string neighbour = Slice(world, "bool M8TryOccupiedNeighbour",
                "M8TileAddress M8LogicalAddress");
            Assert.That(frame, Does.Contain("M8TryOccupiedNeighbour"));
            Assert.That(frame, Does.Not.Contain("M8TryOccupiedExact"));
            Assert.That(neighbour, Does.Contain(
                "neighbourAddress.tileLocal == currentAddress.tileLocal"));
            Assert.That(neighbour, Does.Contain("_M8ChunkTileRefsRead"));
            Assert.That(neighbour, Does.Contain("_M8BlockChunkRefsRead"));
            Assert.That(neighbour, Does.Contain(
                "if (!sameBlock && !M8FindBlock(blockCoord, blockIndex))"));
            Assert.That(Regex.Matches(neighbour, @"M8FindBlock\("),
                Has.Count.EqualTo(1));

            AssertNeighbourClass(new int3(1, 1, 1), new int3(1, 1, 1),
                sameBlock: true, sameChunk: true, sameTile: true);
            AssertNeighbourClass(new int3(7, 7, 7), new int3(1, 1, 1),
                sameBlock: true, sameChunk: true, sameTile: false);
            AssertNeighbourClass(new int3(31, 31, 31), new int3(1, 1, 1),
                sameBlock: true, sameChunk: false, sameTile: false);
            AssertNeighbourClass(new int3(255, 255, 255), new int3(1, 1, 1),
                sameBlock: false, sameChunk: false, sameTile: false);
            AssertNeighbourClass(new int3(-256, -256, -256),
                new int3(-1, -1, -1), sameBlock: false,
                sameChunk: false, sameTile: false);
        }

        [Test]
        public void Pcg3dBenchmark_RunsOnlyInsideTimestampSampleFrames()
        {
            string world = Source("Runtime/Shaders/MerkabaWorld.compute");
            string renderer = Source("Runtime/Merkaba/MerkabaGridRenderer.cs");
            Assert.That(world, Does.Contain("void BenchmarkM8Pcg3d"));
            Assert.That(world, Does.Contain("MerkabaPcg3d(int3("));
            Assert.That(renderer, Does.Contain(
                "if (MerkabaGpuTimestamps.IsRecording)"));
            Assert.That(renderer, Does.Contain("RecordHashBenchmark(command)"));
        }

        [Test]
        public void SurfacePath_DirectlyAddressesM8AndFreeNeverAllocates()
        {
            string integration = Source(
                "Runtime/Shaders/MerkabaIntegration.compute");
            Assert.That(integration, Does.Contain(
                "MerkabaAddressOf(_M8SurfaceCandidatesRead[id].xyz)"));
            Assert.That(integration, Does.Contain("M8FindOrClaimBlock"));
            Assert.That(integration, Does.Contain("M8FindOrClaimChunk"));
            Assert.That(integration, Does.Not.Contain("FindIntegrationPage"));
            Assert.That(integration, Does.Not.Contain("IntegrationSlots"));
            Assert.That(integration, Does.Not.Contain("IntegrationEnabled"));
            int discover = integration.IndexOf("void DiscoverSurfaceCandidates",
                StringComparison.Ordinal);
            int resolve = integration.IndexOf("void ResolveSurfaceBlocks",
                StringComparison.Ordinal);
            int carve = integration.IndexOf("void IntegrateCarveTiles",
                StringComparison.Ordinal);
            Assert.That(discover, Is.GreaterThanOrEqualTo(0));
            Assert.That(resolve, Is.GreaterThan(discover));
            Assert.That(carve, Is.GreaterThan(resolve));
            string carvePath = integration.Substring(carve);
            Assert.That(carvePath, Does.Not.Contain("M8FindOrClaimBlock"));
            Assert.That(carvePath, Does.Not.Contain("M8FindOrClaimChunk"));
        }

        [Test]
        public void CandidateGeneration_PreservesRayBandAndBoundaryGuardUnion()
        {
            string integration = Source(
                "Runtime/Shaders/MerkabaIntegration.compute");
            Assert.That(integration, Does.Contain("for (int layer = -1; layer <= 1; layer++)"));
            Assert.That(integration, Does.Contain("AppendValidatedSurfaceCandidate(nearest - stepCoord)"));
            Assert.That(integration, Does.Contain("AppendValidatedSurfaceCandidate(nearest + stepCoord)"));
            Assert.That(integration, Does.Contain("ObserveDepthEye"));
            Assert.That(integration, Does.Contain("MERKABA_MIN_SURFACE_QUALITY"));
        }

        [Test]
        public void ClaimPublication_IsBoundedAndNeverSpinsAcrossWorkgroups()
        {
            string world = Source("Runtime/Shaders/MerkabaWorld.hlsl");
            string compute = Source("Runtime/Shaders/MerkabaWorld.compute");
            Assert.That(world, Does.Contain(
                "Any observed CLAIMED entry defers instead of walking to a later empty slot"));
            Assert.That(world, Does.Contain("InterlockedCompareExchange"));
            Assert.That(world, Does.Not.Contain("while (entry.blockRef"));
            Assert.That(compute, Does.Contain("PublishNewBlocks"));
            Assert.That(compute, Does.Contain("PublishNewChunks"));
        }

        [Test]
        public void TileGroupReturns_AreUniformAroundGroupBarriers()
        {
            string compute = Source("Runtime/Shaders/MerkabaWorld.compute");
            string initialize = Slice(compute, "void InitializeNewTiles",
                "void ResetClaimQueueCounts");
            string install = Slice(compute, "void InstallLoadedTiles",
                "void FailLoadedTiles");
            Assert.That(install, Does.Contain("GroupMemoryBarrierWithGroupSync();"));
            Assert.That(initialize, Does.Contain(
                "DeviceMemoryBarrierWithGroupSync();"));
            Assert.That(install, Does.Contain(
                "DeviceMemoryBarrierWithGroupSync();"));
            Assert.That(install, Does.Contain(
                "AllMemoryBarrierWithGroupSync();"));
            Assert.That(initialize, Does.Not.Contain("return;"));
            Assert.That(install, Does.Not.Contain("return;"));
            Assert.That(Regex.Matches(initialize,
                "DeviceMemoryBarrierWithGroupSync\\(\\)"), Has.Count.EqualTo(2));
            Assert.That(Regex.Matches(install,
                "DeviceMemoryBarrierWithGroupSync\\(\\)"), Has.Count.EqualTo(2));
        }

        [Test]
        public void FrameCompiler_UsesOneAtomicPerKernelAndOneSpiDraw()
        {
            string frame = Source("Runtime/Shaders/MerkabaFrameCompiler.compute");
            string renderer = Source("Runtime/Merkaba/MerkabaGridRenderer.cs");
            string feature = Source("Runtime/Merkaba/MerkabaRenderFeature.cs");
            Assert.That(frame, Does.Contain("uint survivingCount = countbits(survivingMask)"));
            Assert.That(frame, Does.Contain(
                "M8_COUNTER_LOGICAL_VISIBLE_PRIMITIVES"));
            Assert.That(frame, Does.Contain(
                "M8_COUNTER_VISIBLE_SAFE_COUNT"));
            Assert.That(frame, Does.Contain("InterlockedMin"));
            Assert.That(frame, Does.Contain("logical * 2u"));
            Assert.That(renderer, Does.Not.Contain("Graphics.DrawProceduralIndirect("));
            Assert.That(renderer.Split(new[] { "DrawProceduralIndirectProfiled(" },
                StringSplitOptions.None).Length - 1, Is.EqualTo(1));
            Assert.That(feature, Does.Contain("RecordRenderPass(context.cmd)"));
            Assert.That(renderer, Does.Not.Contain("VisibleSlotAt"));
            Assert.That(renderer, Does.Not.Contain("for (int visible"));
        }

        [Test]
        public void BackfaceCompiler_UsesTheExactCanonicalRasterWinding()
        {
            string frame = Source("Runtime/Shaders/MerkabaFrameCompiler.compute");
            string shader = Source("Runtime/Shaders/MerkabaGrid.shader");
            Assert.That(frame, Does.Contain("MerkabaCanonicalPrimitiveFacing"));
            Assert.That(frame, Does.Contain("_M8EyeGridPositions"));
            Assert.That(frame, Does.Contain("_M8GridWindingSign"));
            Assert.That(frame, Does.Not.Contain(
                "cross(worldB - worldA, worldC - worldA)"));
            Assert.That(frame, Does.Not.Contain(
                "MerkabaCanonicalPrimitivePosition(primitiveId, 0u)"));
            Assert.That(shader, Does.Contain("Cull Back"));
            Assert.That(shader, Does.Contain(
                "primitiveId, input.vertexID"));
            for (int primitive = 0;
                 primitive < MerkabaCanonicalGeometry.PrimitiveCount;
                 primitive++)
            {
                MerkabaCanonicalGeometry.PrimitiveVertex(primitive, 0,
                    out float3 a, out float3 normal);
                MerkabaCanonicalGeometry.PrimitiveVertex(primitive, 1,
                    out float3 b, out _);
                MerkabaCanonicalGeometry.PrimitiveVertex(primitive, 2,
                    out float3 c, out _);
                Assert.That(math.dot(math.cross(b - a, c - a), normal),
                    Is.GreaterThan(0f), $"primitive {primitive}");
            }
        }

        [Test]
        public void WarmQuery_ContainsDrawAndAddsOneM8BlockMargin()
        {
            string frame = Source("Runtime/Shaders/MerkabaFrameCompiler.compute");
            string renderer = Source("Runtime/Merkaba/MerkabaGridRenderer.cs");
            Assert.That(renderer, Does.Contain(
                "renderDistance + MerkabaSpatial.BlockWorldSize"));
            Assert.That(frame, Does.Contain("_M8WarmDistance"));
            Assert.That(frame, Does.Contain("_M8RenderDistance"));
            Assert.That(frame, Does.Contain("if (!inDraw ||"));
        }

        [Test]
        public void LiveVertexPath_ReadsOnlyFrameRecordAndCanonicalPosition()
        {
            string shader = Source("Runtime/Shaders/MerkabaGrid.shader");
            Assert.That(shader, Does.Contain("_M8VisiblePrimitives"));
            Assert.That(shader, Does.Contain("MerkabaCanonicalPrimitivePosition"));
            Assert.That(shader, Does.Not.Contain("_M8KernelStates"));
            Assert.That(shader, Does.Not.Contain("ResidentSlot"));
            Assert.That(shader, Does.Not.Contain("PublishedBank"));
        }

        [Test]
        public void StreamingUsesAsyncOwnedBuffersAndNoGraphicsFence()
        {
            string storage = Source("Runtime/Merkaba/MerkabaGrid.Storage.cs");
            string grid = Source("Runtime/Merkaba/MerkabaGrid.Gpu.cs");
            Assert.That(storage, Does.Contain("AsyncGPUReadback.Request"));
            Assert.That(storage, Does.Not.Contain("WaitForCompletion"));
            Assert.That(storage + grid, Does.Not.Contain("GraphicsFence"));
            Assert.That(storage, Does.Contain("StreamBatchCapacity"));
            Assert.That(storage, Does.Contain("AcknowledgeWritebackBatch"));
        }

        [Test]
        public void ColdLoadRequests_UseBoundedRingAndConsumerAcknowledgement()
        {
            string world = Source("Runtime/Shaders/MerkabaWorld.hlsl");
            string storage = Source("Runtime/Merkaba/MerkabaGrid.Storage.cs");
            Assert.That(MerkabaGrid.LoadRequestCapacity,
                Is.EqualTo(1 << 18));
            Assert.That(world, Does.Contain(
                "requestIndex & MERKABA_M8_LOAD_REQUEST_MASK"));
            Assert.That(world, Does.Contain("_M8LoadRequestReadCount[0]"));
            Assert.That(world, Does.Not.Contain(
                "requestIndex < MERKABA_M8_LOAD_REQUEST_CAPACITY"));
            Assert.That(storage, Does.Contain(
                "_loadRequestCursor & LoadRequestMask"));
            Assert.That(storage, Does.Contain("AcknowledgeLoadRequests"));
        }

        [Test]
        public void FailedWriteback_ReturnsCanonicalTileHotDirtyWithoutFreeingIt()
        {
            string world = Source("Runtime/Shaders/MerkabaWorld.compute");
            string failure = Slice(world, "void FailWritebackBatch",
                "groupshared uint gLoadSlot");
            Assert.That(failure, Does.Contain("M8_COUNTER_FAILED_WRITES"));
            Assert.That(failure, Does.Contain("M8_COUNTER_STORAGE_BACKPRESSURE"));
            Assert.That(failure, Does.Contain("MERKABA_REF_EVICTING"));
            Assert.That(failure, Does.Contain("queued.x + 1u"));
            Assert.That(failure, Does.Contain(
                "_M8TileRecords[M8TileRuntimeIndex(queued.x)].y = 1u"));
            Assert.That(failure, Does.Not.Contain("M8PushPhysicalTile"));
            Assert.That(failure, Does.Not.Contain("MERKABA_REF_COLD_ON_SSD"));
            string storage = Source("Runtime/Merkaba/MerkabaGrid.Storage.cs");
            Assert.That(storage, Does.Not.Contain("_storageWriteDisabled"));
            Assert.That(storage, Does.Contain("FailWritebackBatch"));
        }

        [Test]
        public void ScanDrawAndWarmShareTheEightLaneRadixQueryMath()
        {
            string spatial = Source("Runtime/Shaders/MerkabaSpatial.hlsl");
            string scan = Source("Runtime/Shaders/MerkabaIntegration.compute");
            string frame = Source("Runtime/Shaders/MerkabaFrameCompiler.compute");
            Assert.That(spatial, Does.Contain("MerkabaM8PlaneChildMask"));
            Assert.That(spatial, Does.Contain("MerkabaM8DistanceChildMask"));
            Assert.That(scan, Does.Contain("M8ScanChildMask"));
            Assert.That(scan, Does.Contain("MerkabaM8PlaneChildMask"));
            Assert.That(frame, Does.Contain("M8DrawChildMask"));
            Assert.That(frame, Does.Contain("MerkabaM8PlaneChildMask"));
            Assert.That(scan, Does.Not.Contain("TileIntersectsScan"));
        }

        [Test]
        public void Quest3M8Queries_UseOneSixtyFourLaneGroupPerBlock()
        {
            string scan = Source("Runtime/Shaders/MerkabaIntegration.compute");
            string frame = Source("Runtime/Shaders/MerkabaFrameCompiler.compute");
            string scanQuery = Slice(scan, "groupshared uint gScanBlockRef",
                "void PrepareCarveArgs");
            string frameQuery = Slice(frame, "groupshared uint gFrameBlockRef",
                "void PrepareFrameCompilerArgs");

            foreach (string query in new[] { scanQuery, frameQuery })
            {
                Assert.That(query, Does.Contain("[numthreads(64, 1, 1)]"));
                Assert.That(query, Does.Contain("SV_GroupIndex"));
                Assert.That(query, Does.Contain("thread >> 3u"));
                Assert.That(query, Does.Contain("thread & 7u"));
                Assert.That(query, Does.Contain("GroupMemoryBarrierWithGroupSync"));
                int finalBarrier = query.LastIndexOf(
                    "GroupMemoryBarrierWithGroupSync();",
                    StringComparison.Ordinal);
                int firstReturn = query.IndexOf("return;",
                    StringComparison.Ordinal);
                Assert.That(firstReturn, Is.GreaterThan(finalBarrier),
                    "No lane may return before the final group barrier.");
            }
            Assert.That(scan, Does.Not.Contain(
                "[numthreads(1, 1, 1)]\nvoid QueryCarveTiles"));
            Assert.That(frame, Does.Not.Contain(
                "[numthreads(1, 1, 1)]\nvoid QueryM8Frame"));
        }

        [Test]
        public void Quest3KernelCensus_HasNoSerialDomainKernelOrNewDispatchZoo()
        {
            string world = Source("Runtime/Shaders/MerkabaWorld.compute");
            string scan = Source("Runtime/Shaders/MerkabaIntegration.compute");
            string frame = Source("Runtime/Shaders/MerkabaFrameCompiler.compute");
            string all = world + scan + frame;
            Assert.That(Regex.Matches(all, @"^#pragma kernel ",
                RegexOptions.Multiline), Has.Count.EqualTo(42));

            string[] serial = Regex.Matches(all,
                    @"\[numthreads\(1,\s*1,\s*1\)\]\s*void\s+(\w+)")
                .Cast<Match>().Select(match => match.Groups[1].Value)
                .OrderBy(name => name).ToArray();
            Assert.That(serial, Is.EqualTo(new[]
            {
                "FinalizeDrawArgs",
                "FinalizeObservation",
                "PrepareAllocatedClearArgs",
                "PrepareCarveArgs",
                "PrepareEvictionSelection",
                "PrepareFrameCompilerArgs",
                "PrepareIntegrateArgs",
                "PrepareNewTileDispatchArgs",
                "PrepareResolveArgs",
                "ResetClaimQueueCounts",
                "ResetFrame",
                "ResetObservationCounters"
            }));
            foreach (string kernel in serial)
            {
                string source = world.Contains("void " + kernel)
                    ? world : scan.Contains("void " + kernel) ? scan : frame;
                int start = source.IndexOf("void " + kernel,
                    StringComparison.Ordinal);
                int next = source.IndexOf("\n}", start + 5,
                    StringComparison.Ordinal);
                string body = next > start
                    ? source.Substring(start, next + 2 - start)
                    : source.Substring(start);
                Assert.That(body, Does.Not.Contain("for ("), kernel);
                Assert.That(body, Does.Not.Contain("while ("), kernel);
            }
        }

        [Test]
        public void Quest3DepthKernels_UseTwoDimensionalSixtyFourLaneGroups()
        {
            string depth = Source("Runtime/Shaders/DepthNormals.compute") +
                Source("Runtime/Shaders/DepthDilation.compute") +
                Source("Runtime/Shaders/BilateralDepthFilter.compute");
            Assert.That(Regex.Matches(depth, @"^#pragma kernel ",
                RegexOptions.Multiline), Has.Count.EqualTo(6));
            Assert.That(Regex.Matches(depth,
                @"\[numthreads\(8,\s*8,\s*1\)\]"), Has.Count.EqualTo(6));
            Assert.That(depth, Does.Not.Contain("[numthreads(1, 1, 1)]"));
        }

        [Test]
        public void NewTileWork_UsesMeasuredIndirectDomainsInsteadOfCacheCapacity()
        {
            string integrator = Source("Runtime/Merkaba/MerkabaIntegrator.cs");
            string grid = Source("Runtime/Merkaba/MerkabaGrid.Gpu.cs");
            string world = Source("Runtime/Shaders/MerkabaWorld.compute");
            Assert.That(integrator, Does.Contain(
                "_retryPendingTilesKernel, _grid.M8ObservationDispatchArgs"));
            Assert.That(integrator, Does.Not.Contain(
                "MerkabaSpatial.PhysicalTileCapacity / 64"));
            Assert.That(grid, Does.Contain(
                "_initializeNewTilesKernel, _m8ObservationDispatchArgs"));
            Assert.That(grid, Does.Not.Contain(
                "_initializeNewTilesKernel, 512"));
            Assert.That(grid, Does.Not.Contain("ResetResolveCounter"));
            Assert.That(grid, Does.Contain(
                "_publishNewBlocksKernel, _m8ObservationDispatchArgs"));
            Assert.That(grid, Does.Contain(
                "_publishNewChunksKernel, _m8ObservationDispatchArgs"));
            string publishBlocks = Slice(world, "void PublishNewBlocks",
                "void PublishNewChunks");
            string publishChunks = Slice(world, "void PublishNewChunks",
                "void InitializeNewTiles");
            Assert.That(publishBlocks + publishChunks,
                Does.Not.Contain("index += 64u"));
        }

        [Test]
        public void EvictionDomain_RunsOnlyAfterGpuPressureOrExplicitFlush()
        {
            string world = Source("Runtime/Shaders/MerkabaWorld.hlsl");
            string eviction = Source("Runtime/Shaders/MerkabaWorld.compute");
            string storage = Source("Runtime/Merkaba/MerkabaGrid.Storage.cs");
            string pump = Slice(storage, "private void PumpStorage()",
                "private void ApplySampledCounters");
            Assert.That(world, Does.Contain("M8_COUNTER_EVICTION_NEEDED"));
            Assert.That(world, Does.Contain(
                "InterlockedExchange(_M8Counters[M8_COUNTER_EVICTION_NEEDED], 1u"));
            Assert.That(eviction, Does.Contain(
                "_M8Counters[M8_COUNTER_EVICTION_NEEDED] = 0u"));
            Assert.That(pump.IndexOf("SelectEvictionVictims",
                    StringComparison.Ordinal),
                Is.GreaterThan(pump.IndexOf("request.GetData<uint>()",
                    StringComparison.Ordinal)));
            Assert.That(pump, Does.Contain("CounterEvictionNeeded"));
        }

        [Test]
        public void EvictionRefillsOnlyTheMeasuredFreeSlotDeficit()
        {
            string compute = Source("Runtime/Shaders/MerkabaWorld.compute");
            string grid = Source("Runtime/Merkaba/MerkabaGrid.Gpu.cs");
            string prepare = Slice(compute, "void PrepareEvictionSelection",
                "bool M8TryReserveCleanEviction");
            string select = Slice(compute, "void SelectEvictionVictims",
                "void GatherWritebackBatch");
            Assert.That(prepare, Does.Contain(
                "256u - freeCount : 0u"));
            Assert.That(select, Does.Contain("M8TryReserveCleanEviction"));
            Assert.That(select, Does.Contain(
                "M8_COUNTER_EVICTION_CLEAN_BUDGET"));
            Assert.That(select, Does.Contain("if (queueIndex >= 32u)"));
            Assert.That(select, Does.Contain(
                "MERKABA_REF_EVICTING, expected"));
            Assert.That(grid, Does.Contain(
                "_prepareEvictionSelectionKernel, 1, 1, 1"));
        }

        [Test]
        public void QuestSpirvAudit_CompilesEveryKernelAndCapsWritableStorageAtEight()
        {
            string audit = Source(
                "Tools/shaders/audit_merkaba_compute_spirv.sh");
            Assert.That(audit, Does.Contain("spirv-val"));
            Assert.That(audit, Does.Contain("NonWritable"));
            Assert.That(audit, Does.Contain("writable > 8"));
            Assert.That(audit, Does.Contain("RW/read alias pair"));
            Assert.That(audit, Does.Contain("kernel_count != 42"));
        }

        [Test]
        public void CandidateClearAndNewScanClearTouchOnlyAllocatedWork()
        {
            string integration = Source(
                "Runtime/Shaders/MerkabaIntegration.compute");
            string grid = Source("Runtime/Merkaba/MerkabaGrid.Gpu.cs");
            Assert.That(integration, Does.Contain(
                "M8_COUNTER_TOUCHED_TILE_COUNT"));
            Assert.That(grid, Does.Contain(
                "DispatchIndirect(_clearTouchedCandidatesKernel"));
            string clear = Slice(grid, "internal void ClearGpuWorldForNewScan()",
                "private void ClearUInt(");
            Assert.That(clear, Does.Contain("_clearAllocatedBlocksKernel"));
            Assert.That(clear, Does.Contain("_clearAllocatedChunksKernel"));
            Assert.That(clear, Does.Not.Contain("ClearStates("));
            Assert.That(clear, Does.Not.Contain("_m8KernelStates"));
        }

        [Test]
        public void ObservationCapacityFailure_IsPerObservationAndCommitsNoEvidence()
        {
            string integration = Source(
                "Runtime/Shaders/MerkabaIntegration.compute");
            string world = Source("Runtime/Shaders/MerkabaWorld.hlsl");
            string integrator = Source("Runtime/Merkaba/MerkabaIntegrator.cs");
            string prepare = Slice(integration, "void PrepareIntegrateArgs",
                "void IntegrateSurfaceCandidates");
            string carve = Slice(integration, "void PrepareCarveArgs",
                "void IntegrateCarveTiles");

            Assert.That(prepare, Does.Contain("failure == 0u"));
            Assert.That(prepare, Does.Contain("? (count + 63u) / 64u : 0u"));
            Assert.That(carve, Does.Contain(
                "M8_COUNTER_OBSERVATION_FAILURE] == 0u"));
            foreach (string kernel in new[]
                     {
                         "ResolveSurfaceChunks", "ResolveSurfaceTiles",
                         "QueueResolvedSurfaceCandidates",
                         "RetryPendingNewTiles"
                     })
            {
                int start = integration.IndexOf("void " + kernel,
                    StringComparison.Ordinal);
                int next = integration.IndexOf("[numthreads", start + 5,
                    StringComparison.Ordinal);
                string body = next > start
                    ? integration.Substring(start, next - start)
                    : integration.Substring(start);
                Assert.That(body, Does.Contain(
                    "M8_COUNTER_OBSERVATION_FAILURE"), kernel);
            }
            Assert.That(integration, Does.Contain(
                "M8_COUNTER_FAILED_OBSERVATIONS"));
            Assert.That(world, Does.Contain(
                "out uint failureReason"));
            string failureReason = Slice(integration,
                "uint M8ObservationFailureReason()", "float2 ProjectCameraUv");
            Assert.That(failureReason, Does.Not.Contain(
                "M8_COUNTER_BLOCK_OVERFLOW"));
            Assert.That(failureReason, Does.Not.Contain(
                "M8_COUNTER_HASH_FULL"));
            Assert.That(integrator, Does.Contain(
                "FinishObservation(_grid.CompletedObservationFailure)"));
            Assert.That(integrator, Does.Contain(
                "failureReason != 0u"));
            Assert.That(integrator, Does.Contain(
                "HeldObservationTimeoutSeconds"));
        }

        [Test]
        public void SsdTileTransitionsNeverTreatUnresolvedPayloadAsEmpty()
        {
            string world = Source("Runtime/Shaders/MerkabaWorld.compute");
            string address = Source("Runtime/Shaders/MerkabaWorld.hlsl");
            string integration = Source(
                "Runtime/Shaders/MerkabaIntegration.compute");
            Assert.That(world, Does.Contain(
                "MERKABA_REF_EVICTING, MERKABA_REF_COLD_ON_SSD"));
            Assert.That(world, Does.Contain(
                "MERKABA_REF_LOADING, MERKABA_REF_COLD_ON_SSD"));
            Assert.That(world, Does.Contain(
                "_M8ChunkTileRefs[refIndex] = gLoadSlot + 1u"));
            Assert.That(address, Does.Contain(
                "Existing non-HOT payload is unresolved"));
            Assert.That(integration, Does.Contain(
                "M8_COUNTER_UNRESOLVED_SURFACE_TILES] == 0u"));
        }

        private static string Source(string relative) =>
            File.ReadAllText(Path.GetFullPath(Package + relative));

        private static void AssertNeighbourClass(int3 source, int3 step,
            bool sameBlock, bool sameChunk, bool sameTile)
        {
            MerkabaSpatial.Address current = MerkabaSpatial.Encode(source);
            MerkabaSpatial.Address neighbour = MerkabaSpatial.Encode(source + step);
            Assert.That(neighbour.BlockCoord.Equals(current.BlockCoord),
                Is.EqualTo(sameBlock), $"block {source} + {step}");
            Assert.That(sameBlock && neighbour.ChunkLocal == current.ChunkLocal,
                Is.EqualTo(sameChunk), $"chunk {source} + {step}");
            Assert.That(sameChunk && neighbour.TileLocal == current.TileLocal,
                Is.EqualTo(sameTile), $"tile {source} + {step}");
        }

        private static string Slice(string source, string begin, string end)
        {
            int first = source.IndexOf(begin, StringComparison.Ordinal);
            int last = source.IndexOf(end, first, StringComparison.Ordinal);
            Assert.That(first, Is.GreaterThanOrEqualTo(0));
            Assert.That(last, Is.GreaterThan(first));
            return source.Substring(first, last - first);
        }
    }
}
