using System;
using System.IO;
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
                "MerkabaAddressOf(_M8SurfaceCandidates[id].xyz)"));
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
            string install = Slice(compute, "void InstallLoadedTiles",
                "void FailLoadedTiles");
            Assert.That(install, Does.Contain("if (item >= _M8StreamBatchCount) return;"));
            Assert.That(install, Does.Contain("GroupMemoryBarrierWithGroupSync();"));
            Assert.That(install, Does.Contain("if (gLoadReady != 1u) return;"));
            Assert.That(install.IndexOf("if (gLoadReady != 1u) return;",
                StringComparison.Ordinal), Is.GreaterThan(install.IndexOf(
                "GroupMemoryBarrierWithGroupSync();", StringComparison.Ordinal)));
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
            Assert.That(frame, Does.Contain(
                "MerkabaCanonicalPrimitivePosition(primitiveId, 0u)"));
            Assert.That(frame, Does.Contain(
                "MerkabaCanonicalPrimitivePosition(primitiveId, 1u)"));
            Assert.That(frame, Does.Contain(
                "MerkabaCanonicalPrimitivePosition(primitiveId, 2u)"));
            Assert.That(frame, Does.Contain(
                "cross(worldB - worldA, worldC - worldA)"));
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
        public void FailedWriteback_IsCountedWithoutFreeingEvictingTiles()
        {
            string world = Source("Runtime/Shaders/MerkabaWorld.compute");
            string failure = Slice(world, "void FailWritebackBatch",
                "groupshared uint gLoadSlot");
            Assert.That(failure, Does.Contain("M8_COUNTER_FAILED_WRITES"));
            Assert.That(failure, Does.Contain("M8_COUNTER_STORAGE_BACKPRESSURE"));
            Assert.That(failure, Does.Not.Contain("M8PushPhysicalTile"));
            Assert.That(failure, Does.Not.Contain("MERKABA_REF_COLD_ON_SSD"));
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
