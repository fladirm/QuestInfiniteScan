using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

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
                Package + "Runtime/Shaders/MerkabaReadout.compute");
            ComputeShader stereoRgbd = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                Package + "Runtime/Shaders/StereoRgbdRefine.compute");
            ComputeShader bins = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                Package + "Runtime/Shaders/MerkabaObservationBins.compute");
            ComputeShader certificate = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                Package + "Runtime/Shaders/MerkabaDepthCertificate.compute");
            Assert.That(world, Is.Not.Null);
            Assert.That(integration, Is.Not.Null);
            Assert.That(frame, Is.Not.Null);
            Assert.That(stereoRgbd, Is.Not.Null);
            Assert.That(bins, Is.Not.Null);
            Assert.That(certificate, Is.Not.Null);
            Assert.DoesNotThrow(() => stereoRgbd.FindKernel(
                "StereoFlowerRefine"));
            Assert.DoesNotThrow(() => certificate.FindKernel("BuildDepthCertificate"));
            Assert.DoesNotThrow(() => certificate.FindKernel("ReduceDepthCertificate"));
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
                         "FlowerCommit", "UpdateObservationDual", "FinalizeObservation",
                         "DrainFlowerGeometry", "ResolveFlowerCarriers",
                         "DrainFlowerSkinRgb", "DrainFlowerSkinV", "QueryFineEraseTiles",
                         "EraseFineTiles", "FinalizeFineErase"
                     })
                Assert.DoesNotThrow(() => integration.FindKernel(kernel), kernel);
            foreach (string kernel in new[]
                     {
                         "CountObservationBins", "ReserveObservationBins", "EmitObservationBins",
                         "ResolveMissingSpatialNodes", "ResolveObservationTileRequests",
                         "ResetObservationBins"
                     })
                Assert.DoesNotThrow(() => bins.FindKernel(kernel), kernel);
            foreach (string kernel in new[]
                     {
                         "ClassifyHotFlowerPages", "CompactDirtyFlowerSymbols",
                         "PublishDirtyFlowerPages", "CullFlowerPages"
                     })
                Assert.DoesNotThrow(() => frame.FindKernel(kernel), kernel);
        }

        [Test]
        public void SetupWizard_WiresTheCurrentReadoutAsset()
        {
            string wizard = Source("Editor/RoomScanSetupWizard.cs");
            Assert.That(wizard, Does.Contain(
                "AssignAsset(renderer, \"readoutCompute\""));
            Assert.That(wizard, Does.Contain(
                "Runtime/Shaders/MerkabaReadout.compute"));
            Assert.That(wizard, Does.Not.Contain("MerkabaFrameCompiler"));
            Assert.That(wizard, Does.Contain(
                "AssignAsset(depth, \"stereoRgbdRefineCompute\""));
            Assert.That(wizard, Does.Contain(
                "Runtime/Shaders/StereoRgbdRefine.compute"));
            Assert.That(wizard, Does.Not.Contain("BilateralDepthFilter"));
        }

        [Test]
        public void PhysicalBuffers_RespectQuestLimitAndTilesNeverCrossBanks()
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

            // Budget the current ABI payloads, not a hand-copied inventory
            // containing retired winner banks and the old SurfaceQueue.
            long[] buffers =
            {
                stateBank, chunkRefs,
                MerkabaDualGpuLayout.BlockBufferBytes,
                MerkabaDualGpuLayout.ChunkBufferBytes,
                MerkabaDualGpuLayout.LeafBufferBytes,
                (long)MerkabaObservationRecord.Capacity * MerkabaObservationRecord.ByteSize,
                (long)MerkabaSpatial.PhysicalTileCapacity * 27 * sizeof(uint)
            };
            Assert.That(buffers.Max(), Is.LessThanOrEqualTo(128L * 1024 * 1024));
            Assert.That((long)MerkabaObservationRecord.Capacity * MerkabaObservationRecord.ByteSize,
                Is.EqualTo(32L * 1024 * 1024));
            Assert.That((long)MerkabaDualGpuLayout.LeafCapacity * MerkabaDualLeaf.ByteSize,
                Is.EqualTo(2L * 1024 * 1024));

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
        public void M8CounterAbi_UsesEverySlotExactlyOnce()
        {
            string world = Source("Runtime/Shaders/MerkabaWorld.hlsl");
            MatchCollection matches = Regex.Matches(world,
                @"^#define M8_COUNTER_(?!COUNT)[A-Z0-9_]+ (\d+)u$",
                RegexOptions.Multiline);
            int[] slots = matches.Cast<Match>()
                .Select(match => int.Parse(match.Groups[1].Value))
                .OrderBy(value => value).ToArray();
            Assert.That(slots, Has.Length.EqualTo(MerkabaGrid.CounterCount));
            Assert.That(slots, Is.EqualTo(
                Enumerable.Range(0, MerkabaGrid.CounterCount).ToArray()));

            var shaderSlots = Regex.Matches(world,
                    @"^#define M8_COUNTER_([A-Z0-9_]+) (\d+)u$",
                    RegexOptions.Multiline).Cast<Match>()
                .ToDictionary(match => match.Groups[1].Value,
                    match => int.Parse(match.Groups[2].Value));
            foreach (var field in typeof(MerkabaGrid).GetFields(
                         System.Reflection.BindingFlags.Static |
                         System.Reflection.BindingFlags.NonPublic))
            {
                if (!field.IsLiteral || field.FieldType != typeof(int) ||
                    !field.Name.StartsWith("Counter", StringComparison.Ordinal))
                    continue;
                string name = field.Name == "CounterLoadRequests"
                    ? "LOAD_REQUEST_COUNT"
                    : Regex.Replace(field.Name.Substring("Counter".Length),
                        "([a-z0-9])([A-Z])", "$1_$2").ToUpperInvariant();
                Assert.That(shaderSlots.TryGetValue(name, out int slot), Is.True,
                    field.Name + " must name a current shader counter");
                Assert.That((int)field.GetRawConstantValue(), Is.EqualTo(slot),
                    field.Name + " must read the same C#/HLSL slot");
            }
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
        public void Pcg3dBenchmark_IsNeverInjectedIntoProductionReadoutTiming()
        {
            string world = Source("Runtime/Shaders/MerkabaWorld.compute");
            string renderer = Source("Runtime/Merkaba/MerkabaGridRenderer.cs");
            Assert.That(world, Does.Contain("void BenchmarkM8Pcg3d"));
            Assert.That(world, Does.Contain("MerkabaPcg3d(int3("));
            Assert.That(renderer, Does.Not.Contain(
                "RecordHashBenchmark(command)"));
            Assert.That(renderer, Does.Not.Contain("timingBuildRequested"));
        }

        [Test]
        public void MeasuredPlane_QuantizationPreservesTheBoundedEndpointPlane()
        {
            // Admission now intersects interval evidence in FlowerCommit. The
            // chosen representative must still use the original 16-byte M8 ABI.
            float3 owner = new(0.025f, -0.05f, 0.075f);
            foreach (var node in MerkabaSphereFlowerAuthority.Nodes)
            foreach (float offset in new[] { -0.024f, -0.0113f, 0f, 0.0113f, 0.024f })
            {
                int3 direction = node.Direction;
                float3 normal = math.normalize((float3)direction);
                float3 endpoint = owner + normal * offset;
                uint flags = KernelState.SetSurfacePlane(0u, normal,
                    math.dot(endpoint - owner, normal));
                KernelState.DecodeSurfacePlane(flags, out float3 decodedNormal,
                    out float decodedOffset);
                double error = math.abs(math.dot(endpoint - owner, decodedNormal) - decodedOffset);
                double bound = 0.025 / (2 * 127) +
                    2 * Math.Sqrt(18) / 1023 * math.length(endpoint - owner);
                Assert.That(error, Is.LessThanOrEqualTo(bound), direction + ":" + offset);
            }
        }

        [Test]
        public void ReplacedScannerAuthorities_AreAbsentFromProductionConsumers()
        {
            string[] paths =
            {
                "Runtime/Shaders/MerkabaIntegration.compute",
                "Runtime/Shaders/MerkabaWorld.compute",
                "Runtime/Shaders/MerkabaWorld.hlsl",
                "Runtime/Merkaba/MerkabaIntegrator.cs",
                "Runtime/Merkaba/MerkabaGrid.Gpu.cs",
                "Runtime/Telemetry/MerkabaNativeVulkanExecutor.cs",
                "Runtime/Telemetry/Native/MerkabaVulkanTimestamps.cpp"
            };
            foreach (string path in paths)
            foreach (string removed in new[] {
                "SurfaceWinnerRanks", "SurfaceQueue", "IntegrateSurfaceCandidates",
                "IntegrateCarveTiles", "CarveDispatchArgs", "NeedsCarveFlag",
                "M8IsCurrentFrameSheetSupport", "M8HasCurrentFramePlanarSupport",
                "MerkabaNearestNormalStep", "sameM8Sheet", "gsDilatedDepth" })
                Assert.That(Source(path), Does.Not.Contain(removed), path + ":" + removed);
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
        public void RadialWarmQueryContainsCoverageAndOneBlockMargin()
        {
            string frame = Source("Runtime/Shaders/MerkabaReadout.compute");
            string renderer = Source("Runtime/Merkaba/MerkabaGridRenderer.cs");
            Assert.That(renderer, Does.Contain(
                "MerkabaSpatial.BlockWorldSize"));
            Assert.That(renderer, Does.Contain("renderDistance = 12f"));
            Assert.That(frame, Does.Contain("_M8WarmDistance"));
            Assert.That(frame, Does.Contain("_M8RenderDistance"));
        }








        [Test]
        public void StreamingUsesAsyncOwnedBuffersWithoutCpuWaitForCompletion()
        {
            string storage = Source("Runtime/Merkaba/MerkabaGrid.Storage.cs");
            string grid = Source("Runtime/Merkaba/MerkabaGrid.Gpu.cs");
            Assert.That(storage, Does.Contain("AsyncGPUReadback.Request"));
            Assert.That(storage, Does.Not.Contain("WaitForCompletion"));
            // Generation-retirement fences are permitted. Synchronous CPU
            // waits in the streaming pump are not.
            Assert.That(storage, Does.Not.Contain("WaitOnAsyncGraphicsFence"));
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
                "#define M8_LOAD_LOCAL_MASK");
            Assert.That(failure, Does.Contain("M8_COUNTER_FAILED_WRITES"));
            Assert.That(failure, Does.Contain("M8_COUNTER_STORAGE_BACKPRESSURE"));
            Assert.That(failure, Does.Contain("MERKABA_REF_EVICTING"));
            Assert.That(failure, Does.Contain("queued.x + 1u"));
            Assert.That(failure, Does.Contain(
                "M8MarkTileDirty(queued.x)"));
            Assert.That(failure, Does.Not.Contain("M8PushPhysicalTile"));
            Assert.That(failure, Does.Not.Contain("MERKABA_REF_COLD_ON_SSD"));
            string storage = Source("Runtime/Merkaba/MerkabaGrid.Storage.cs");
            Assert.That(storage, Does.Not.Contain("_storageWriteDisabled"));
            Assert.That(storage, Does.Contain("FailWritebackBatch"));
        }

        [Test]
        public void ScanBroadPhaseUsesConservativeFrozenMutationCoverage()
        {
            string spatial = Source("Runtime/Shaders/MerkabaSpatial.hlsl");
            string scan = Source("Runtime/Shaders/MerkabaIntegration.compute");
            string frame = Source("Runtime/Shaders/MerkabaReadout.compute");
            string integrator = Source(
                "Runtime/Merkaba/MerkabaIntegrator.cs");
            Assert.That(spatial, Does.Contain("MerkabaM8DistanceChildMask"));
            Assert.That(scan, Does.Contain("M8ScanChildMask"));
            Assert.That(scan, Does.Contain("MerkabaM8DistanceChildMask"));
            Assert.That(scan, Does.Contain("MerkabaM8KernelPlaneChildMask"));
            Assert.That(scan, Does.Contain("_M8ScanCoveragePlanes"));
            Assert.That(scan, Does.Not.Contain("M8ScanEyeChildMask"));
            Assert.That(integrator, Does.Contain(
                "MerkabaMutationCoverage.WriteGridPlanes"));
            Assert.That(integrator, Does.Not.Contain(
                "GeometryUtility.CalculateFrustumPlanes"));
            Assert.That(frame, Does.Not.Contain(
                "MerkabaM8KernelPlaneChildMask"));
            Assert.That(frame, Does.Not.Contain("MerkabaM8PlaneChildMask"));
            Assert.That(Source("Runtime/Merkaba/MerkabaGridRenderer.cs"),
                Does.Not.Contain("MerkabaReadoutCoverage.WorldToKernelPlane"));
            Assert.That(scan, Does.Not.Contain("TileIntersectsScan"));
        }







        [TestCase("MerkabaIntegration.compute", "UpdateObservationDual", 64u)]
        [TestCase("MerkabaIntegration.compute", "FlowerCommit", 128u)]
        public void TileAndBlockKernels_UseBoundedCooperativeGroups(string asset,
            string entry, uint expectedX)
        {
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                Package + "Runtime/Shaders/" + asset);
            Assert.That(shader, Is.Not.Null, asset);
            shader.GetKernelThreadGroupSizes(shader.FindKernel(entry),
                out uint x, out uint y, out uint z);
            Assert.That(x, Is.EqualTo(expectedX));
            Assert.That(y, Is.EqualTo(1u));
            Assert.That(z, Is.EqualTo(1u));
        }

        [TestCase("DepthNormals.compute", "CopyProjectionDepthArray", 8u, 8u)]
        [TestCase("DepthNormals.compute", "MonoRawDepthToStereo", 8u, 8u)]
        [TestCase("DepthNormals.compute", "FineSurfaceTarget", 128u, 1u)]
        [TestCase("MerkabaDepthCertificate.compute", "BuildDepthCertificate", 16u, 16u)]
        [TestCase("MerkabaDepthCertificate.compute", "ReduceDepthCertificate", 16u, 16u)]
        [TestCase("StereoRgbdRefine.compute", "StereoFlowerRefine", 8u, 8u)]
        public void Quest3DepthKernels_UseBoundedWorkgroups(string asset,
            string entry, uint expectedX, uint expectedY)
        {
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                Package + "Runtime/Shaders/" + asset);
            Assert.That(shader, Is.Not.Null, asset);
            shader.GetKernelThreadGroupSizes(shader.FindKernel(entry),
                out uint x, out uint y, out uint z);
            Assert.That(x, Is.EqualTo(expectedX), entry);
            Assert.That(y, Is.EqualTo(expectedY), entry);
            Assert.That(z, Is.EqualTo(1u), entry);
            Assert.That(x * y * z, Is.LessThanOrEqualTo(256u), entry);
        }

        [Test]
        public void NewTileWork_UsesMeasuredIndirectDomainsInsteadOfCacheCapacity()
        {
            string integrator = Source("Runtime/Merkaba/MerkabaIntegrator.cs");
            string grid = Source("Runtime/Merkaba/MerkabaGrid.Gpu.cs");
            string world = Source("Runtime/Shaders/MerkabaWorld.compute");
            Assert.That(integrator, Does.Contain("_bins.Record(command, reset: false)"));
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
            Assert.That(eviction, Does.Contain(
                "_M8Counters[M8_COUNTER_EVICTION_NEEDED] = 1u"));
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
                "void SelectEvictionVictims");
            string select = Slice(compute, "void SelectEvictionVictims",
                "void GatherWritebackBatch");
            Assert.That(prepare, Does.Contain(
                "256u - freeCount : 0u"));
            Assert.That(prepare, Does.Contain(
                "M8_COUNTER_EVICTION_CLEAN_TICKET] = 0u"));
            Assert.That(select, Does.Contain(
                "M8_COUNTER_EVICTION_CLEAN_TICKET"));
            Assert.That(select, Does.Contain(
                "M8_COUNTER_EVICTION_CLEAN_BUDGET"));
            Assert.That(compute, Does.Not.Contain(
                "M8TryReserveCleanEviction"));
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
            Assert.That(audit, Does.Contain("kernel_count != expected_kernel_count"));
            Assert.That(audit, Does.Contain("DepthNormals.compute"));
            Assert.That(audit, Does.Contain("MerkabaDepthCertificate.compute"));
            Assert.That(audit, Does.Contain("MerkabaObservationBins.compute"));
            Assert.That(audit, Does.Not.Contain("DepthDilation.compute"));
            Assert.That(audit, Does.Contain("StereoRgbdRefine.compute"));
            Assert.That(audit, Does.Contain("MerkabaSphereFlowerOracle.compute"));
            Assert.That(audit, Does.Contain("NoContraction"));
        }

        [Test]
        public void ObservationRetirementAndNewScanClearTouchOnlyAllocatedWork()
        {
            string integration = Source(
                "Runtime/Shaders/MerkabaIntegration.compute");
            string grid = Source("Runtime/Merkaba/MerkabaGrid.Gpu.cs");
            Assert.That(integration, Does.Contain(
                "M8_COUNTER_TOUCHED_TILE_COUNT"));
            string bins = Source("Runtime/Shaders/MerkabaObservationBins.hlsl");
            Assert.That(bins, Does.Contain("void M8FlowerRetireObservation"));
            Assert.That(integration, Does.Contain("M8FlowerRetireObservation(lane,128u)"));
            Assert.That(bins, Does.Contain("M8_COUNTER_TOUCHED_TILE_COUNT"));
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
                "keepHot ? physicalSlot + 1u : MERKABA_REF_COLD_ON_SSD"));
            Assert.That(world, Does.Contain(
                "MERKABA_REF_LOADING, MERKABA_REF_COLD_ON_SSD"));
            Assert.That(world, Does.Contain(
                "_M8ChunkTileRefs[refIndex] = gLoadSlot + 1u"));
            Assert.That(address, Does.Contain("bool M8IsHotRef"));

            // The current readers require both a HOT dual reference and the
            // published M8 slot/generation. A staged LOADING leaf, COLD ref or
            // reused slot therefore stays AMBIGUOUS, never empty/free.
            string dual = Source("Runtime/Shaders/MerkabaDualHierarchy.hlsl");
            string resident = Slice(dual, "bool M8DualLeafResident", "uint M8DualLeafReference");
            Assert.That(resident, Does.Contain("(packed >> 30u) != 2u"));
            Assert.That(resident, Does.Contain("generation != 0u && slotGeneration == generation"));
            Assert.That(resident, Does.Contain("meta.x == chunk && meta.y == tile"));
            Assert.That(resident, Does.Contain("_M8ChunkTileRefsRead[chunk*64u+tile] == slot+1u"));
            string tileRead = Slice(dual, "uint M8DualReadTile", "uint M8DualReadKernelAt");
            Assert.That(tileRead, Does.Contain("M8DualLeafResident("));
            Assert.That(tileRead, Does.Contain("? M8_DUAL_MIXED : M8_DUAL_AMBIGUOUS"));
            string supportSource = Source("Runtime/Shaders/MerkabaFlowerSupport.hlsl");
            string support = Slice(supportSource,
                "uint4 M8FlowerSupportResolveTile", "void M8FlowerSupportCacheTile");
            Assert.That(support, Does.Contain("if (!M8FlowerSupportLeafResident("));
            Assert.That(support, Does.Contain("context.x = M8_DUAL_AMBIGUOUS"));
            string supportResident = Slice(supportSource,
                "bool M8FlowerSupportLeafResident", "uint4 M8FlowerSupportResolveTile");
            Assert.That(supportResident, Does.Contain("return M8DualLeafResident(packed,chunk,tile,slot,true)"));
            Assert.That(supportResident, Does.Contain("return M8DualLeafResident(packed,chunk,tile,slot,false)"));

            string front = Slice(Source("Runtime/Shaders/MerkabaFlowerPages.hlsl"),
                "bool M8FlowerFrontPage", "M8FlowerSymbolRecord M8FlowerLoadSymbol");
            Assert.That(front, Does.Contain("source.y&M8_FLOWER_PAGE_SOURCE_INVALID"));
            Assert.That(front, Does.Contain("source.x!=directory.y"));
            Assert.That(front, Does.Contain("header.Generation==directory.y"));
            Assert.That(integration, Does.Contain(
                "M8_COUNTER_UNRESOLVED_SURFACE_TILES] == 0u"));
        }




        [Test]
        public void PhysicalTileAllocation_UsesOneBatchReservationAndOneLedger()
        {
            string bins = Source("Runtime/Shaders/MerkabaObservationBins.compute");
            string world = Source("Runtime/Shaders/MerkabaWorld.compute");
            string requests = Slice(bins, "void ResolveObservationTileRequests",
                "#define M8_OBSERVATION_RESERVE_LANES");
            string prepare = Slice(world, "void PrepareLoadedTiles", "groupshared uint gLoadSlot");
            string install = Slice(world, "void InstallLoadedTiles", "void FailLoadedTiles");
            Assert.That(requests, Does.Contain("_M8PendingNewTileRefs"));
            Assert.That(requests, Does.Contain("uint installCount = min(freeCount, m8ObservationInstallCount)"));
            Assert.That(requests, Does.Contain("uint reservationBase = freeCount - installCount"));
            Assert.That(prepare, Does.Contain("gLoadReservationCount = min(gLoadNeedCount, freeCount)"));
            Assert.That(prepare, Does.Contain("M8RestoreDualLeaf"));
            Assert.That(prepare, Does.Contain("M8PushPhysicalTile"));
            Assert.That(install, Does.Not.Contain("M8RestoreDualLeaf"));
            Assert.That(install, Does.Not.Contain("M8PushPhysicalTile"));
            Assert.That(install, Does.Contain("_M8ChunkTileRefs[refIndex] = gLoadSlot + 1u"));
            Assert.That(Source("Runtime/Merkaba/MerkabaGrid.Gpu.cs"),
                Does.Contain("ExecuteDualWorldBatch(_installLoadedTilesKernel, count, true)"));
        }

        [Test]
        public void ResidencyRetryEpoch_IsCapturedAtAttemptSubmitAndGpuOwned()
        {
            string address = Source("Runtime/Shaders/MerkabaWorld.hlsl");
            string world = Source("Runtime/Shaders/MerkabaWorld.compute");
            string storage = Source(
                "Runtime/Merkaba/MerkabaGrid.Storage.cs");
            string integrator = Source(
                "Runtime/Merkaba/MerkabaIntegrator.cs");
            string apply = Slice(storage, "private void ApplySampledCounters",
                "private void BeginLoadAddressReadback");

            Assert.That(address, Does.Contain(
                "#define M8_COUNTER_RESIDENCY_EPOCH 44u"));
            Assert.That(MerkabaGrid.CounterResidencyEpoch, Is.EqualTo(44));
            Assert.That(world, Does.Contain("M8SignalResidencyChange"));
            // The epoch is still the GPU-owned signal that a reference span was
            // reclaimed. It is no longer a reason to resubmit a frame: a
            // snapshot is released at its fence, so the next snapshot simply
            // reads the newer residency.
            Assert.That(integrator, Does.Not.Contain("_attemptResidencyEpoch"),
                "residency movement is not a reason to re-attempt a snapshot");
            Assert.That(apply, Does.Contain(
                "PublishResidencyEpoch(values[CounterResidencyEpoch])"));
            Assert.That(storage, Does.Not.Contain(
                "_dependencySampleInitialized"));
        }

        [Test]
        public void AttemptCompletion_UsesOneExactCpuOnlyRecord()
        {
            string integration = Source(
                "Runtime/Shaders/MerkabaIntegration.compute");
            string finalize = Slice(integration, "void FinalizeObservation",
                "\n}") + "\n}";
            string completion = Slice(integration, "void M8FinalizeObservationCompletion()",
                "\n}") + "\n}";
            string integrator = Source(
                "Runtime/Merkaba/MerkabaIntegrator.cs");
            string submit = Slice(integrator,
                "internal bool TrySubmitObservationAttempt()",
                "private bool TrySubmitNativeObservationAttempt()");
            string storage = Source(
                "Runtime/Merkaba/MerkabaGrid.Storage.cs");
            string pump = Slice(storage, "private void PumpStorage()",
                "internal void PumpStorageForLifecycleRetirement()");
            string exact = Slice(storage,
                "internal void RequestAttemptCompletion(",
                "private void PublishResidencyEpoch(");

            Assert.That(finalize, Does.Contain("if (lane == 0u)"));
            Assert.That(finalize, Does.Contain("M8FinalizeObservationCompletion();"));
            Assert.That(finalize.IndexOf("M8FinalizeObservationCompletion();", StringComparison.Ordinal),
                Is.LessThan(finalize.IndexOf("GroupMemoryBarrierWithGroupSync();", StringComparison.Ordinal)));
            Assert.That(completion, Does.Contain(
                "_M8AttemptCompletion[0] = uint4(_M8AttemptToken"));
            Assert.That(completion, Does.Contain(
                "M8_COUNTER_OBSERVATION_TOKEN"));
            Assert.That(completion, Does.Contain(
                "M8_COUNTER_RESIDENCY_EPOCH"));
            Assert.That(completion, Does.Contain("M8_COUNTER_OBSERVATION_CHANGE_MASK"));
            Assert.That(completion, Does.Contain(
                "M8_ATTEMPT_COMPLETION_READOUT_CHANGED"));
            Assert.That(integration, Does.Not.Contain(
                "M8_COUNTER_ATTEMPT_COMPLETED_TOKEN"));
            Assert.That(submit, Does.Contain(
                "_grid.RequestAttemptCompletion(_attemptToken)"));
            Assert.That(pump, Does.Not.Contain(
                "_completedAttemptToken ="));
            Assert.That(pump, Does.Not.Contain(
                "_completedObservationToken ="));
            Assert.That(exact, Does.Contain(
                "expectedAttemptToken != _attemptCompletionExpectedToken"));
            Assert.That(exact, Does.Contain(
                "generation != _gpuGeneration"));
            Assert.That(exact, Does.Contain(
                "_completedAttemptToken = completion.X"));
            Assert.That(exact, Does.Contain(
                "_completedObservationChangedReadout"));
            Assert.That(exact, Does.Not.Contain("SelectEvictionVictims"));
            Assert.That(exact, Does.Not.Contain("InstallLoadedTiles"));
            Assert.That(exact, Does.Not.Contain("Dispatch"));
        }

        [Test]
        public void FailedObservation_RollsBackOnlyStillClaimedNewTiles()
        {
            string integration = Source(
                "Runtime/Shaders/MerkabaIntegration.compute");
            string finalize = Slice(integration, "void FinalizeObservation",
                "\n}") + "\n}";
            string completion = Slice(integration, "void M8FinalizeObservationCompletion()",
                "\n}") + "\n}";
            string cleanup = Slice(Source("Runtime/Shaders/MerkabaObservationBins.hlsl"),
                "void M8FlowerRetireObservation", "\n}");

            Assert.That(finalize, Does.Contain("M8FinalizeObservationCompletion();"));
            Assert.That(completion, Does.Contain(
                "M8_COUNTER_CLEANUP_PENDING_COUNT"));
            Assert.That(completion, Does.Contain("completed != 0u && failure != 0u"));
            Assert.That(completion, Does.Contain("M8_COUNTER_PENDING_NEW_TILE_COUNT"));
            Assert.That(cleanup, Does.Contain(
                "MERKABA_REF_CLAIMED_NEW,MERKABA_REF_EMPTY"));
            Assert.That(cleanup, Does.Not.Contain("M8StoreKernelState"));
            Assert.That(cleanup, Does.Not.Contain("M8PushPhysicalTile"));
            Assert.That(cleanup, Does.Not.Contain("MERKABA_REF_COLD_ON_SSD"));
        }


        [Test]
        public void NativeScannerQueue_IsPreloadedSerialAndPublicationSafe()
        {
            string native = Source(
                "Runtime/Telemetry/Native/MerkabaVulkanTimestamps.cpp");
            string managed = Source(
                "Runtime/Telemetry/MerkabaNativeVulkanExecutor.cs");
            string integrator = Source(
                "Runtime/Merkaba/MerkabaIntegrator.cs");
            string renderer = Source(
                "Runtime/Merkaba/MerkabaGridRenderer.cs");
            string pluginMeta = Source(
                "Tools/unity/MerkabaVulkanTimestamps.pluginmeta");
            string generator = Source(
                "Tools/unity/generate_merkaba_native_executor_shaders.py");

            Assert.That(pluginMeta, Does.Contain("isPreloaded: 1"));
            Assert.That(native, Does.Contain("AddInterceptInitialization"));
            Assert.That(native, Does.Contain("info.queueCount == 1u"));
            Assert.That(native, Does.Contain("physicalCount >= 2u"));
            Assert.That(native, Does.Contain("priorities[selected].push_back(0.1f)"));
            Assert.That(native, Does.Contain("vkGetDeviceQueue"));
            Assert.That(native, Does.Contain("g_injectedQueueIndex"));
            Assert.That(native, Does.Contain("graphicsReady"));
            Assert.That(native, Does.Contain("nativeDone"));
            Assert.That(native, Does.Contain("acquireFence"));
            Assert.That(native, Does.Contain(
                "PipelineRangeForKind(descriptor->kind, &firstPipeline,"));
            Assert.That(native, Does.Contain(
                "job->firstPipeline = firstPipeline;"));
            Assert.That(native, Does.Contain(
                "job->lastPipeline = lastPipeline;"));
            Assert.That(managed, Does.Contain(
                "private static MerkabaNativeVulkanJob _activeJob"));
            Assert.That(managed, Does.Not.Contain("System.Threading.Thread"));
            Assert.That(managed, Does.Not.Contain("ExecuteCommandBufferAsync"));
            Assert.That(managed, Does.Contain(
                "TimingLogIntervalSeconds = 5f"));
            Assert.That(managed, Does.Contain(
                "bool log = TryClaimTimingLog(_kind)"));
            Assert.That(managed, Does.Contain(
                "if (!log && !_sampleObservation) return"));
            Assert.That(managed, Does.Contain("sample.PipelineDispatches[pipeline]++"));
            Assert.That(integrator, Does.Contain("JobKind.Observation;"));
            Assert.That(integrator, Does.Contain("JobKind.FineErase"));
            Assert.That(generator, Does.Contain("StereoRgbdRefine"));
            Assert.That(generator, Does.Contain("FinalizeObservation"));
            Assert.That(generator, Does.Contain("FinalizeFineErase"));
        }

        // REV-C 18 and closure 4.3 used to say the drain gets "the remaining
        // work of the SAME immutable observation", which turned a realtime
        // scanner into a grinding buffer: on device one snapshot was re-entered
        // past 200 attempts with obs=1 stage=1 pendingTiles=1 frozen for 50 s
        // and zero triangles, while ~3000 newer depth frames went unread. The
        // corrected contract: one snapshot is one bounded synchronous scan
        // transaction, the persistent M8/dual/FlowerDetail/ThreadAtlas world is
        // the refinement memory, and a camera observation is evidence, not a
        // work queue.
        [Test]
        public void OneSnapshotIsOneBoundedScanTransaction()
        {
            string integrator = Source("Runtime/Merkaba/MerkabaIntegrator.cs");
            string generator = Source(
                "Tools/unity/generate_merkaba_native_executor_shaders.py");
            string executor = Source(
                "Runtime/Telemetry/MerkabaNativeVulkanExecutor.cs");
            string native = Source(
                "Runtime/Telemetry/Native/MerkabaVulkanTimestamps.cpp");
            string drain = Source("Runtime/Shaders/MerkabaFlowerDrainBody.hlsl");
            string completion = Slice(
                Source("Runtime/Shaders/MerkabaIntegration.compute"),
                "void M8FinalizeObservationCompletion()", "\n}") + "\n}";

            // Nothing resumes a prepared snapshot.
            foreach (string resumed in new[]
            {
                "CanRetryPreparedObservation", "_resumeNeedsAcquisition",
                "_waitingForDependency", "_retryAfterPublishedChange",
                "RememberObservationAttemptDependencies", "newObservation",
                "JobKind.ObservationRetry", "JobKind.ObservationContinue"
            })
                Assert.That(integrator, Does.Not.Contain(resumed),
                    resumed + " is a second attempt at one frame");
            Assert.That(executor, Does.Not.Contain("ObservationRetry"));
            Assert.That(executor, Does.Not.Contain("ObservationContinue"));
            Assert.That(native, Does.Not.Contain("kJobObservationRetry"));
            Assert.That(native, Does.Not.Contain("kJobObservationContinue"));
            Assert.That(native, Does.Contain("kJobKindCount = 3,"));

            // Finalize publishes and releases; it schedules nothing.
            Assert.That(completion, Does.Contain(
                "_M8Counters[M8_COUNTER_OBSERVATION_COMPLETED] = 1u;"));
            Assert.That(completion, Does.Not.Contain(
                "M8_COUNTER_REFINEMENT_PENDING_TILES"),
                "a tile that did not finish its budget may not hold a snapshot");
            Assert.That(completion, Does.Not.Contain("M8_COUNTER_REFINEMENT_STAGE"),
                "the stage is a barrier index inside the graph, not a life stage");

            // The stage barriers are dispatches of this one graph, and the
            // allocation round trip that used to need a retry is a barrier too.
            int observation = generator.IndexOf("observation = [",
                StringComparison.Ordinal);
            int observationEnd = generator.IndexOf("\"FinalizeObservation\",",
                observation, StringComparison.Ordinal);
            Assert.That(observation, Is.GreaterThanOrEqualTo(0));
            Assert.That(observationEnd, Is.GreaterThan(observation));
            string schedule = generator.Substring(observation,
                observationEnd - observation);
            Assert.That(Regex.Matches(schedule, "\"DrainFlowerGeometry\"").Count,
                Is.EqualTo(3), "root, L1 and L2 are three barriers of ONE graph");
            Assert.That(Regex.Matches(schedule, "\"AdvanceRefinementStage\"").Count,
                Is.EqualTo(3), "a workgroup may not publish its own dispatch stage");
            Assert.That(Regex.Matches(schedule, "\"CountObservationBins\"").Count,
                Is.EqualTo(2), "count, allocate, count again - inside one graph");
            Assert.That(generator, Does.Contain("(\"Observation\", observation)"));
            Assert.That(generator, Does.Not.Contain("continuation = ["));
            Assert.That(generator, Does.Not.Contain("retry = "));
            Assert.That(generator, Does.Not.Contain("(\"ObservationRetry\","));
            Assert.That(generator, Does.Not.Contain("(\"ObservationContinue\","));

            // The tile cursor is the world's refinement memory, not a workset
            // the observation owns. Keyed by the observation it restarted at
            // zero every snapshot, which is what forced the retention.
            Assert.That(drain, Does.Contain(
                "M8FlowerTileRefinementCursor(slot,runtime.w)"));
            Assert.That(drain, Does.Not.Contain("M8FlowerTilePendingCursor"));
            // A COLD dual chunk or a kernel the dual has not certified FULL is
            // the normal state of a world whose default is UNKNOWN. Vetoing all
            // refinement on it meant no tile ever refined at all.
            Assert.That(drain, Does.Not.Contain(
                "_M8Counters[M8_COUNTER_UNRESOLVED_OBSERVATION_TILES]!=0u"));
            Assert.That(drain, Does.Contain(
                "_M8Counters[M8_COUNTER_FINE_LEASE_BUSY]!=0u"),
                "the R1 read lease is the one genuine cross-tile veto");
        }

        // The managed and native ABI constants live in two files and are
        // compared at runtime, so a one-sided bump does not fail the build or
        // the suite - it ships and the scanner refuses to start with "Native
        // executor ABI does not match this application", which reads on device
        // as a scan that claims to run and produces no chunks. That is exactly
        // what a schedule change did here.
        [Test]
        public void ExecutorAbiVersion_MatchesBetweenManagedAndNative()
        {
            string managed = Source(
                "Runtime/Telemetry/MerkabaNativeVulkanExecutor.cs");
            string native = Source(
                "Runtime/Telemetry/Native/MerkabaVulkanTimestamps.cpp");
            Match csharp = Regex.Match(managed,
                @"internal const int AbiVersion = (\d+);");
            Match cpp = Regex.Match(native,
                @"constexpr uint32_t kExecutorAbiVersion = (\d+);");
            Assert.That(csharp.Success, Is.True, "managed ABI constant");
            Assert.That(cpp.Success, Is.True, "native ABI constant");
            Assert.That(cpp.Groups[1].Value, Is.EqualTo(csharp.Groups[1].Value),
                "a schedule, resource or pipeline change must bump BOTH sides");
        }

        [Test]
        public void NativeScannerUniforms_PackMatricesForSpirvRowMajorLayout()
        {
            Matrix4x4 matrix = new Matrix4x4
            {
                m00 = 1f, m01 = 2f, m02 = 3f, m03 = 4f,
                m10 = 5f, m11 = 6f, m12 = 7f, m13 = 8f,
                m20 = 9f, m21 = 10f, m22 = 11f, m23 = 12f,
                m30 = 13f, m31 = 14f, m32 = 15f, m33 = 16f,
            };
            var uniforms = new MerkabaNativeUniformTable();
            uniforms.Matrix("_AsymmetricMatrix", matrix);

            uniforms.Build(out MerkabaNativeVulkanExecutor.UniformValue[] values,
                out byte[] data);

            Assert.That(values, Has.Length.EqualTo(1));
            Assert.That(values[0].Offset, Is.Zero);
            Assert.That(values[0].Size, Is.EqualTo(64u));
            float[] packed = new float[16];
            Buffer.BlockCopy(data, 0, packed, 0, data.Length);
            Assert.That(packed, Is.EqualTo(new[]
            {
                1f, 5f, 9f, 13f,
                2f, 6f, 10f, 14f,
                3f, 7f, 11f, 15f,
                4f, 8f, 12f, 16f,
            }));
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
            Assert.That(first, Is.GreaterThanOrEqualTo(0), begin);
            int last = source.IndexOf(end, first, StringComparison.Ordinal);
            Assert.That(last, Is.GreaterThan(first), end);
            return source.Substring(first, last - first);
        }
    }
}
