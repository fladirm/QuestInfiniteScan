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
                Package + "Runtime/Shaders/MerkabaReadout.compute");
            ComputeShader stereoRgbd = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                Package + "Runtime/Shaders/StereoRgbdRefine.compute");
            Assert.That(world, Is.Not.Null);
            Assert.That(integration, Is.Not.Null);
            Assert.That(frame, Is.Not.Null);
            Assert.That(stereoRgbd, Is.Not.Null);
            Assert.DoesNotThrow(() => stereoRgbd.FindKernel(
                "StereoRgbdRefine"));
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
                         "InitializeSurfaceWinners", "SelectSurfaceWinners",
                         "QueueResolvedSurfaceCandidates",
                         "IntegrateSurfaceCandidates", "QueryCarveTiles",
                         "IntegrateCarveTiles", "FinalizeObservation"
                     })
                Assert.DoesNotThrow(() => integration.FindKernel(kernel), kernel);
            foreach (string kernel in new[]
                     {
                         "ResetReadoutBuild", "QueryM8Readout",
                         "PrepareReadoutBuild", "PreflightReadout",
                         "PrepareReadoutEmit", "EmitReadoutVertices",
                         "FinalizeReadout"
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

            long[] allBuffers =
            {
                32768L * 16, (8192L + 262144L) * 16,
                8192L * 512 * 4,
                8192L * 4, 8192L * 8 * 4, 8192L * 64 * 4,
                262144L * 64 * 4, 262144L * 9 * 4,
                stateBank, stateBank, stateBank, stateBank,
                32768L * 16 * 16, 32768L * 2 * 16,
                32768L * 4, (long)MerkabaGrid.CounterCount * 4, 16,
                (8192L + 262144L + 32768L) * 8,
                32768L * 4, 262144L * 16, 4,
                2097152L * 16, 1048576L * 8,
                4194304L * 4, 4194304L * 4,
                4194304L * 4, 4194304L * 4,
                32768L * 4, 32768L * 4, 12, 32768L * 8,
                (long)MerkabaGrid.ReadoutVertexCapacityPerBuffer * 16,
                (long)MerkabaGrid.ReadoutVertexCapacityPerBuffer * 16,
                12, 16, 12,
                32L * 8,
                32L * 513 * 16, 32L * 16, 32L * 512 * 16,
                8192L * 16
            };
            Assert.That(allBuffers, Has.Length.EqualTo(41));
            Assert.That(allBuffers.Max(), Is.EqualTo(96L * 1024 * 1024));
            Assert.That(allBuffers.Max(), Is.LessThan(128L * 1024 * 1024));
            Assert.That(allBuffers.Sum(), Is.EqualTo(696878796L));

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
        public void ReadoutPatchNeverQueriesNeighbourGeometry()
        {
            string frame = Source("Runtime/Shaders/MerkabaReadout.compute");
            string generated = Source(
                "Runtime/Shaders/MerkabaOverlapShell.generated.hlsl");
            Assert.That(frame, Does.Not.Contain("M8LoadReadoutNeighbourState"));
            Assert.That(frame, Does.Not.Contain("M8_SHELL_HALO"));
            Assert.That(frame, Does.Contain(
                "M8LoadKernelStateRead(gM8ShellPhysicalSlot"));
            Assert.That(generated, Does.Not.Contain("neighbour"));
            Assert.That(generated, Does.Not.Contain("donor"));
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
        public void SurfaceIntegration_PersistsExactWinningMeasuredPlane()
        {
            string integration = Source(
                "Runtime/Shaders/MerkabaIntegration.compute");
            string surface = Slice(integration,
                "void IntegrateSurfaceCandidates", "uint M8ScanChildMask");
            Assert.That(surface, Does.Contain(
                "TrySurfaceMeasurement(sourcePixel"));
            Assert.That(surface, Does.Contain(
                "float signedOffset = dot(worldSurface - kernelWorld"));
            Assert.That(surface, Does.Contain(
                "M8SetSurfacePlane(state.flags, normalGrid"));
            Assert.That(surface, Does.Not.Contain(
                "M8SelectCanonicalSurfaceOrientation(normalGrid)"));
        }

        [Test]
        public void SurfacePath_DirectlyAddressesM8AndFreeNeverAllocates()
        {
            string integration = Source(
                "Runtime/Shaders/MerkabaIntegration.compute");
            Assert.That(integration, Does.Contain(
                "MerkabaAddressOf(candidate.xyz)"));
            Assert.That(integration, Does.Contain("RouteSurfaceCandidate"));
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
        public void CandidateGeneration_OwnsExactlyOneCanonicalSurfaceKernel()
        {
            string integration = Source(
                "Runtime/Shaders/MerkabaIntegration.compute");
            string discover = Slice(integration,
                "void DiscoverSurfaceCandidates", "void PrepareResolveArgs");
            Assert.That(discover, Does.Contain("TrySurfaceMeasurement"));
            Assert.That(discover, Does.Contain(
                "AppendSurfaceCandidate(surfaceKernel, id.xy)"));
            Assert.That(discover, Does.Not.Contain("for (int layer"));
            Assert.That(discover, Does.Not.Contain("nearest - stepCoord"));
            Assert.That(discover, Does.Not.Contain("nearest + stepCoord"));
            Assert.That(integration, Does.Contain("MerkabaNearestKernel"));
            Assert.That(integration, Does.Contain(
                "all(globalCoord == surfaceKernel)"));
            Assert.That(integration, Does.Contain(
                "RWStructuredBuffer<uint2> _M8SurfaceQueue"));
            Assert.That(Source("Runtime/Merkaba/MerkabaGrid.Gpu.cs"),
                Does.Contain("_m8SurfaceQueue = Allocate(SurfaceQueueCapacity,\n" +
                    "                    sizeof(uint) * 2);"));
            Assert.That(integration, Does.Contain(
                "MerkabaPackSurfaceMetadata(sourcePixel"));
            Assert.That(integration, Does.Contain(
                "MerkabaSurfacePixel(candidate.w)"));
        }

        [Test]
        public void SurfaceMeasurementAbi_PreservesPixelAndRanksDeterministically()
        {
            string integration = Source(
                "Runtime/Shaders/MerkabaIntegration.compute");
            Assert.That(integration, Does.Contain(
                "#define MERKABA_SURFACE_ROUTE_SHIFT 24u"));
            Assert.That(integration, Does.Contain(
                "#define MERKABA_SURFACE_AUTHORITY_SHIFT 26u"));
            Assert.That(integration, Does.Contain(
                "#define MERKABA_SURFACE_FLAG_OFF_AXIS_BLOCKED 0x10000000u"));
            Assert.That(integration, Does.Contain(
                "#define MERKABA_SURFACE_FLAG_REPLACEMENT 0x20000000u"));
            int metadata = MerkabaSurfaceMeasurement.PackMetadata(4095, 3071,
                route: 1, MerkabaSurfaceMeasurement.AuthorityRevision,
                offAxisBlocked: true, replacement: true);
            Assert.That(MerkabaSurfaceMeasurement.PixelX(metadata),
                Is.EqualTo(4095));
            Assert.That(MerkabaSurfaceMeasurement.PixelY(metadata),
                Is.EqualTo(3071));
            uint raw = unchecked((uint)metadata);
            Assert.That((raw >> MerkabaSurfaceMeasurement.RouteShift) & 3u,
                Is.EqualTo(1u));
            Assert.That((raw >> MerkabaSurfaceMeasurement.AuthorityShift) & 3u,
                Is.EqualTo((uint)MerkabaSurfaceMeasurement.AuthorityRevision));
            Assert.That(raw & MerkabaSurfaceMeasurement.OffAxisBlockedFlag,
                Is.Not.Zero);
            Assert.That(raw & MerkabaSurfaceMeasurement.ReplacementFlag,
                Is.Not.Zero);

            uint revision = MerkabaSurfaceMeasurement.WinnerRank(
                MerkabaSurfaceMeasurement.AuthorityRevision, 0.01f, 0.8f,
                20, 30);
            uint support = MerkabaSurfaceMeasurement.WinnerRank(
                MerkabaSurfaceMeasurement.AuthoritySupport, 0f, 1f, 0, 0);
            uint discovery = MerkabaSurfaceMeasurement.WinnerRank(
                MerkabaSurfaceMeasurement.AuthorityDiscovery, 0f, 1f, 0, 0);
            Assert.That(revision, Is.LessThan(support));
            Assert.That(support, Is.LessThan(discovery));

            uint residualNear = MerkabaSurfaceMeasurement.WinnerRank(
                MerkabaSurfaceMeasurement.AuthoritySupport, 0.001f, 0.5f,
                100, 100);
            uint residualFar = MerkabaSurfaceMeasurement.WinnerRank(
                MerkabaSurfaceMeasurement.AuthoritySupport, 0.02f, 0.5f,
                100, 100);
            uint facing = MerkabaSurfaceMeasurement.WinnerRank(
                MerkabaSurfaceMeasurement.AuthoritySupport, 0.001f, 0.9f,
                100, 100);
            Assert.That(residualNear, Is.LessThan(residualFar));
            Assert.That(facing, Is.LessThan(residualNear));

            uint[] candidates =
            {
                discovery, residualFar, support, residualNear, revision
            };
            uint expected = candidates.Min();
            foreach (uint[] order in new[]
                     {
                         candidates,
                         candidates.Reverse().ToArray(),
                         new[] { residualNear, discovery, revision, support,
                             residualFar }
                     })
                Assert.That(order.Aggregate(uint.MaxValue, Math.Min),
                    Is.EqualTo(expected));
        }

        [Test]
        public void SurfaceWinner_UsesThreeOrderedPassesAndAttemptLocalBanks()
        {
            string integration = Source(
                "Runtime/Shaders/MerkabaIntegration.compute");
            string integrator = Slice(
                Source("Runtime/Merkaba/MerkabaIntegrator.cs"),
                "internal bool TrySubmitObservationAttempt()",
                "private bool CanRetryPreparedObservation()");
            string gpu = Source("Runtime/Merkaba/MerkabaGrid.Gpu.cs");
            Assert.That(integration, Does.Contain(
                "RWStructuredBuffer<uint> _M8SurfaceWinnerRanks0"));
            Assert.That(integration, Does.Contain(
                "InterlockedMin(_M8SurfaceWinnerRanks"));
            Assert.That(integration, Does.Contain(
                "asuint(packedMetadata) & MERKABA_MEASUREMENT_PACKED_MASK"));
            Assert.That(gpu, Does.Contain(
                "_m8SurfaceWinnerRanks0 = Allocate(bankStateCount, " +
                "sizeof(uint))"));

            int initialize = integrator.IndexOf(
                "_initializeSurfaceWinnersKernel,", StringComparison.Ordinal);
            int select = integrator.IndexOf(
                "_selectSurfaceWinnersKernel,", initialize + 1,
                StringComparison.Ordinal);
            int queue = integrator.IndexOf("_queueResolvedKernel,",
                select + 1, StringComparison.Ordinal);
            Assert.That(initialize, Is.GreaterThanOrEqualTo(0));
            Assert.That(select, Is.GreaterThan(initialize));
            Assert.That(queue, Is.GreaterThan(select));
        }

        [Test]
        public void SurfaceOwnership_RoutesOnlyNormalLayersAndColdIsUnresolved()
        {
            string integration = Source(
                "Runtime/Shaders/MerkabaIntegration.compute");
            string route = Slice(integration, "int RouteSurfaceCandidate",
                "float MerkabaFreeDistanceWeight");
            string compatibility = Slice(integration,
                "bool MerkabaOwnerCompatible", "int RouteSurfaceCandidate");
            Assert.That(route, Does.Contain(
                "nearestKernel + normalStep"));
            Assert.That(route, Does.Contain(
                "nearestKernel - normalStep"));
            Assert.That(route, Does.Not.Contain("26"));
            Assert.That(compatibility, Does.Contain(
                "perpendicularError <= MERKABA_HALF_SUPPORT"));
            Assert.That(compatibility, Does.Contain(
                "alongError <= MERKABA_SUPPORT_SIZE"));
            Assert.That(route, Does.Contain(
                "MERKABA_SURFACE_ROUTE_UNRESOLVED"));
            Assert.That(route, Does.Contain("MerkabaMutationAttention"));
            string attention = Slice(integration,
                "float MerkabaMutationAttention",
                "uint MerkabaPackMeasurementPixel");
            Assert.That(attention, Does.Contain(
                "incidence < MERKABA_REVISION_MIN_INCIDENCE"));
            Assert.That(route, Does.Contain(
                "targetCoord = revision ? nearestKernel : bestOwner"));

            string resolve = Slice(integration, "void ResolveSurfaceBlocks",
                "void ResolveSurfaceChunks");
            Assert.That(resolve, Does.Contain("RouteSurfaceCandidate"));
            Assert.That(resolve, Does.Contain(
                "_M8SurfaceCandidates[id] = int4(targetCoord, metadata)"));
            string initialize = Slice(integration,
                "void InitializeSurfaceWinners",
                "void SelectSurfaceWinners");
            Assert.That(initialize, Does.Contain("RequestSurfaceOwnerLoad"));
            Assert.That(initialize, Does.Contain(
                "M8_COUNTER_UNRESOLVED_SURFACE_TILES"));
            string queue = Slice(integration,
                "void QueueResolvedSurfaceCandidates",
                "void RetryPendingNewTiles");
            Assert.That(queue, Does.Contain(
                "_M8SurfaceQueue[queueIndex] = uint2(key, metadata)"));
            Assert.That(queue, Does.Contain("M8LoadSurfaceWinner"));

            string integrate = Slice(integration,
                "void IntegrateSurfaceCandidates", "uint M8ScanChildMask");
            Assert.That(integrate, Does.Contain(
                "TrySurfaceMeasurement(sourcePixel"));
            Assert.That(integrate, Does.Contain(
                "MerkabaSurfacePixel(asint(queued.y))"));
            Assert.That(integration, Does.Not.Contain(
                "TrySurfaceMeasurementAtKernel"));
            Assert.That(integrate, Does.Not.Contain("sourceEye"));
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
        public void ReadoutBuild_EmitsDrawReadyVerticesAndOneSpiDraw()
        {
            string frame = Source("Runtime/Shaders/MerkabaReadout.compute");
            string generated = Source(
                "Runtime/Shaders/MerkabaOverlapShell.generated.hlsl");
            string renderer = Source("Runtime/Merkaba/MerkabaGridRenderer.cs");
            string feature = Source("Runtime/Merkaba/MerkabaRenderFeature.cs");
            Assert.That(frame, Does.Contain(
                "MerkabaOverlapShell.generated.hlsl"));
            Assert.That(frame, Does.Not.Contain("gM8ShellFlags"));
            Assert.That(frame, Does.Not.Contain("gM8ShellPackedColors"));
            Assert.That(frame, Does.Not.Contain("gM8ShellColorConfidence"));
            Assert.That(frame, Does.Contain("[numthreads(8, 8, 2)]"));
            Assert.That(frame, Does.Not.Contain("M8LoadReadoutNeighbourState"));
            Assert.That(frame, Does.Contain(
                "M8TryBuildMeasuredPlanePatch"));
            Assert.That(generated, Does.Contain(
                "bool M8TryBuildMeasuredPlanePatch"));
            Assert.That(frame, Does.Contain(
                "M8_OVERLAP_TRIANGLES_PER_PATCH"));
            Assert.That(frame, Does.Contain("M8StoreReadoutVertex"));
            Assert.That(frame, Does.Not.Contain("MerkabaReadoutCubeTriangle"));
            Assert.That(frame, Does.Not.Contain("triangleIndex < 12u"));
            Assert.That(frame, Does.Not.Contain("M8ReadoutEvenFace"));
            Assert.That(frame, Does.Not.Contain("M8ReadoutOddFace"));
            Assert.That(frame, Does.Not.Contain("MerkabaCanonicalPrimitive"));
            Assert.That(frame, Does.Contain(
                "M8_COUNTER_LOGICAL_VISIBLE_PRIMITIVES"));
            Assert.That(frame, Does.Contain(
                "MERKABA_M8_READOUT_TRIANGLE_CAPACITY"));
            Assert.That(frame, Does.Contain(
                "logical * M8_READOUT_VERTICES_PER_TRIANGLE"));
            Assert.That(frame, Does.Contain("_M8DrawArgs[1] = 2u"));
            Assert.That(frame, Does.Contain("MerkabaReadoutVertex"));
            Assert.That(frame, Does.Contain("vertex.gridPosition"));
            Assert.That(renderer, Does.Not.Contain("Graphics.DrawProceduralIndirect("));
            Assert.That(renderer.Split(new[] { "DrawProceduralIndirectProfiled(" },
                StringSplitOptions.None).Length - 1, Is.EqualTo(1));
            Assert.That(feature, Does.Contain("RecordRenderPass(context.cmd)"));
            Assert.That(renderer, Does.Not.Contain("VisibleSlotAt"));
            Assert.That(renderer, Does.Not.Contain("for (int visible"));
        }

        [Test]
        public void ReadoutSkinWinding_IsOrientationCanonicalAndViewIndependent()
        {
            string frame = Source("Runtime/Shaders/MerkabaReadout.compute");
            string generated = Source(
                "Runtime/Shaders/MerkabaOverlapShell.generated.hlsl");
            string shader = Source("Runtime/Shaders/MerkabaGrid.shader");
            Assert.That(generated, Does.Contain(
                "M8MeasuredPlaneTangentBasis("));
            Assert.That(generated, Does.Contain(
                "M8OverlapTriangleCorner(uint vertex)"));
            Assert.That(generated, Does.Not.Contain("freeSign"));
            Assert.That(frame + generated,
                Does.Not.Contain("_M8EyeGridPositions"));
            Assert.That(frame + generated,
                Does.Not.Contain("_M8GridWindingSign"));
            Assert.That(frame + generated, Does.Not.Contain("opposite0"));
            Assert.That(frame + generated, Does.Not.Contain("swapWinding"));
            Assert.That(shader, Does.Contain("Cull Off"));
            Assert.That(shader, Does.Not.Contain(
                "MerkabaCanonicalPrimitivePosition"));
        }

        [Test]
        public void ReadoutSkin_UsesOneTileGroupAndOnlyMeasuredMainPlane()
        {
            string frame = Source("Runtime/Shaders/MerkabaReadout.compute");
            string generated = Source(
                "Runtime/Shaders/MerkabaOverlapShell.generated.hlsl");
            string preflight = Slice(frame, "void PreflightReadout",
                "void PrepareReadoutEmit");
            string compile = Slice(frame, "void EmitReadoutVertices",
                "void FinalizeReadout");
            Assert.That(frame, Does.Not.Contain("M8_SHELL_HALO_COUNT"));
            Assert.That(frame, Does.Not.Contain("M8LoadReadoutNeighbourState"));
            Assert.That(frame + generated, Does.Not.Contain("donor"));
            Assert.That(frame + generated, Does.Not.Contain("neighbour"));
            Assert.That(frame, Does.Contain("M8HasSurfacePlane(state.flags)"));
            Assert.That(frame, Does.Contain(
                "M8EmitOneOverlapPatch(thread + M8_SHELL_GROUP_THREADS * 3u"));
            Assert.That(frame, Does.Contain(
                "_M8FrameDispatchArgs[1] = 1u"));
            Assert.That(frame, Does.Contain("M8PatchPrefix"));
            Assert.That(frame, Does.Contain("gM8ShellPatchValidWords"));
            Assert.That(frame, Does.Contain("M8ResolveOverlapPatchMask"));
            Assert.That(frame, Does.Contain(
                "M8_COUNTER_READOUT_PLANE_LEGACY_INVALID"));
            Assert.That(preflight, Does.Contain(
                "M8ResolveOverlapPatchMask(thread)"));
            Assert.That(compile, Does.Contain(
                "M8ResolveOverlapPatchMask(thread)"));
            Assert.That(frame, Does.Contain(
                "GroupMemoryBarrierWithGroupSync()"));
            Assert.That(frame, Does.Not.Contain("evenVertex"));
            Assert.That(frame, Does.Not.Contain("candidateMask"));
            Assert.That(frame, Does.Not.Contain("surfaceMask"));
            Assert.That(frame, Does.Not.Contain("MerkabaReadoutCubeTriangle"));
            Assert.That(frame, Does.Not.Contain(
                "MerkabaCanonicalPrimitivePosition"));
            Assert.That(generated, Does.Contain(
                "M8TryBuildMeasuredPlanePatch"));
            Assert.That(generated, Does.Contain(
                "normal * signedOffset"));

            int mainLoop = compile.IndexOf("M8EmitOneOverlapPatch(thread",
                StringComparison.Ordinal);
            Assert.That(mainLoop, Is.GreaterThan(0));
            string mainPath = compile.Substring(mainLoop);
            Assert.That(mainPath, Does.Not.Contain("M8FindBlock"));
            Assert.That(mainPath, Does.Not.Contain(
                "M8LoadReadoutNeighbourState"));
            Assert.That(mainPath, Does.Not.Contain("InterlockedAdd"));
            Assert.That(Regex.Matches(compile,
                "M8LoadMainTile\\("), Has.Count.EqualTo(1));
            Assert.That(frame, Does.Contain("[numthreads(8, 8, 2)]"));
        }

        [Test]
        public void RadialWarmQueryContainsCoverageAndOneBlockMargin()
        {
            string frame = Source("Runtime/Shaders/MerkabaReadout.compute");
            string renderer = Source("Runtime/Merkaba/MerkabaGridRenderer.cs");
            Assert.That(renderer, Does.Contain(
                "coverageDistance +"));
            Assert.That(renderer, Does.Contain(
                "MerkabaSpatial.BlockWorldSize"));
            Assert.That(renderer, Does.Contain("renderDistance = 12f"));
            Assert.That(renderer, Does.Contain(
                "readoutTranslationGuard = 1f"));
            Assert.That(frame, Does.Contain("_M8WarmDistance"));
            Assert.That(frame, Does.Contain("_M8RenderDistance"));
            Assert.That(frame, Does.Contain("if (!inDraw ||"));
            Assert.That(frame, Does.Contain("M8_COUNTER_READOUT_UNRESOLVED"));
        }

        [Test]
        public void LiveVertexPath_ReadsOnlyDrawReadyReadoutVertex()
        {
            string shader = Source("Runtime/Shaders/MerkabaGrid.shader");
            Assert.That(shader, Does.Contain(
                "_M8ReadoutVertices0[input.vertexID]"));
            Assert.That(shader, Does.Contain(
                "_M8ReadoutVertices1["));
            Assert.That(shader, Does.Contain(
                "input.vertexID - 6291456u"));
            Assert.That(shader, Does.Not.Contain(
                "MerkabaCanonicalPrimitivePosition"));
            Assert.That(shader, Does.Not.Contain("primitiveId"));
            Assert.That(shader, Does.Not.Contain("_M8KernelStates"));
            Assert.That(shader, Does.Not.Contain("ResidentSlot"));
            Assert.That(shader, Does.Not.Contain("PublishedBank"));
        }

        [Test]
        public void ReadoutCache_IsDisposableCadencedAndSingleBuffered()
        {
            string readout = Source("Runtime/Shaders/MerkabaReadout.compute");
            string renderer = Source("Runtime/Merkaba/MerkabaGridRenderer.cs");
            string scanner = Source("Runtime/Core/RoomScanner.cs");
            string gpu = Source("Runtime/Merkaba/MerkabaGrid.Gpu.cs");

            Assert.That(renderer, Does.Contain("readoutBuildHz = 15f"));
            Assert.That(renderer, Does.Contain(
                "_canonicalDirty || coverageDirty || residencyChanged"));
            Assert.That(renderer, Does.Not.Contain("Vector3.Angle"));
            Assert.That(renderer, Does.Not.Contain("publishedGridForward"));
            Assert.That(renderer, Does.Not.Contain("publishedGridUp"));
            Assert.That(renderer, Does.Contain("MarkCanonicalReadoutDirty"));
            Assert.That(scanner, Does.Contain(
                "_renderer?.MarkCanonicalReadoutDirty()"));
            Assert.That(renderer, Does.Contain(
                "Graphics.ExecuteCommandBuffer(command)"));
            Assert.That(renderer, Does.Not.Contain(
                "Graphics.ExecuteCommandBufferAsync"));
            Assert.That(renderer, Does.Not.Contain("GraphicsFence"));
            Assert.That(renderer + gpu, Does.Not.Contain("ReadoutBank"));
            Assert.That(renderer + gpu, Does.Not.Contain("ReadoutChunk"));
            Assert.That(gpu, Does.Contain(
                "_m8ReadoutVertices0 = Allocate("));
            Assert.That(gpu, Does.Contain(
                "_m8ReadoutVertices1 = Allocate("));
            Assert.That(MerkabaGrid.CounterReadoutUnresolved, Is.EqualTo(50));
            Assert.That(MerkabaGrid.CounterReadoutBuildStatus, Is.EqualTo(69));

            string reset = Slice(readout, "void ResetReadoutBuild",
                "groupshared uint gFrameBlockRef");
            string prepare = Slice(readout, "void PrepareReadoutBuild",
                "#define M8_SHELL_GROUP_THREADS");
            Assert.That(reset, Does.Not.Contain("_M8DrawArgs"));
            Assert.That(prepare, Does.Contain("MERKABA_READOUT_SKIPPED"));
            Assert.That(prepare, Does.Contain("_M8FrameDispatchArgs[0] = 0u"));
        }

        [Test]
        public void ReadoutPublication_PreflightsBeforeTouchingLastGoodStream()
        {
            string readout = Source("Runtime/Shaders/MerkabaReadout.compute");
            string renderer = Source("Runtime/Merkaba/MerkabaGridRenderer.cs");
            string preflight = Slice(readout, "void PreflightReadout",
                "void PrepareReadoutEmit");
            string prepareEmit = Slice(readout, "void PrepareReadoutEmit",
                "void EmitReadoutVertices");
            string emitHelper = Slice(readout, "void M8EmitOneOverlapPatch",
                "void EmitReadoutVertices");
            string emit = Slice(readout, "void EmitReadoutVertices",
                "void FinalizeReadout");
            string finalize = readout.Substring(readout.IndexOf(
                "void FinalizeReadout", StringComparison.Ordinal));

            Assert.That(preflight, Does.Not.Contain("M8StoreReadoutVertex"));
            Assert.That(preflight, Does.Not.Contain("_M8ReadoutVertices0"));
            Assert.That(preflight, Does.Not.Contain("_M8ReadoutVertices1"));
            Assert.That(preflight, Does.Contain(
                "_M8VisibleTiles[groupId.x].y = baseTriangle"));
            Assert.That(prepareEmit, Does.Not.Contain("_M8DrawArgs"));
            Assert.That(prepareEmit, Does.Contain(
                "_M8FrameDispatchArgs[0] = 0u"));
            Assert.That(emitHelper, Does.Contain("M8StoreReadoutVertex"));
            Assert.That(emit, Does.Contain("M8EmitOneOverlapPatch"));
            Assert.That(emit, Does.Contain(
                "M8_COUNTER_READOUT_EMITTED_TRIANGLES"));
            Assert.That(finalize, Does.Contain("emitted != logical"));
            Assert.That(finalize, Does.Not.Contain("_M8DrawArgs[0] = 0u"));
            Assert.That(finalize.IndexOf("status == MERKABA_READOUT_FAILED",
                StringComparison.Ordinal), Is.LessThan(finalize.IndexOf(
                "_M8DrawArgs[0]", StringComparison.Ordinal)));

            int preflightDispatch = renderer.IndexOf("_preflightKernel",
                renderer.IndexOf("void SubmitReadoutBuild",
                    StringComparison.Ordinal), StringComparison.Ordinal);
            int prepareEmitDispatch = renderer.IndexOf("_prepareEmitKernel",
                preflightDispatch, StringComparison.Ordinal);
            int emitDispatch = renderer.IndexOf("_emitKernel",
                prepareEmitDispatch, StringComparison.Ordinal);
            int finalizeDispatch = renderer.IndexOf("_finalizeKernel",
                emitDispatch, StringComparison.Ordinal);
            Assert.That(preflightDispatch, Is.GreaterThan(0));
            Assert.That(prepareEmitDispatch, Is.GreaterThan(preflightDispatch));
            Assert.That(emitDispatch, Is.GreaterThan(prepareEmitDispatch));
            Assert.That(finalizeDispatch, Is.GreaterThan(emitDispatch));
        }

        [Test]
        public void ReadoutVertexAbi_MaterializesOrientedOverlapShell()
        {
            string readout = Source("Runtime/Shaders/MerkabaReadout.compute");
            string generated = Source(
                "Runtime/Shaders/MerkabaOverlapShell.generated.hlsl");
            string shader = Source("Runtime/Shaders/MerkabaGrid.shader");
            Assert.That(MerkabaGrid.ReadoutTriangleCapacity,
                Is.EqualTo(4_194_304));
            Assert.That(MerkabaGrid.ReadoutTriangleCapacityPerBuffer,
                Is.EqualTo(2_097_152));
            Assert.That(MerkabaGrid.ReadoutVertexCapacityPerBuffer,
                Is.EqualTo(6_291_456));
            Assert.That((long)MerkabaGrid.ReadoutVertexCapacityPerBuffer * 16,
                Is.EqualTo(96L * 1024 * 1024));
            uint boundaryTriangle = (uint)
                MerkabaGrid.ReadoutTriangleCapacityPerBuffer;
            uint boundaryVertex = boundaryTriangle * 3u - (uint)
                MerkabaGrid.ReadoutVertexCapacityPerBuffer;
            Assert.That(boundaryVertex, Is.Zero,
                "buffer 1 is the consecutive half of one logical stream");
            Assert.That(readout, Does.Contain("struct MerkabaReadoutVertex"));
            Assert.That(readout, Does.Contain(
                "vertex.gridPosition = gridPosition"));
            Assert.That(readout, Does.Contain(
                "_M8ReadoutVertices0[outputVertex + corner] = vertex"));
            Assert.That(readout, Does.Contain(
                "_M8ReadoutVertices1[outputVertex + corner] = vertex"));
            Assert.That(readout + generated, Does.Contain(
                "M8OverlapPatchCorner"));
            Assert.That(generated, Does.Contain(
                "M8_OVERLAP_PATCH_HALF_EXTENT 0.025"));
            Assert.That(generated, Does.Contain(
                "patch.packedColor = state.packedColor"));
            Assert.That(readout, Does.Not.Contain("MerkabaReadoutCubeTriangle"));
            Assert.That(readout, Does.Not.Contain("half3(0.55h"));
            Assert.That(shader, Does.Contain("half3(0.55h, 0.16h, 0.42h)"));
            Assert.That(shader, Does.Contain("packedColor >> 24u"));
        }

        [Test]
        public void ReadoutCounters_DescribeMeasuredPlanePublication()
        {
            string world = Source("Runtime/Shaders/MerkabaWorld.hlsl");
            string readout = Source("Runtime/Shaders/MerkabaReadout.compute");
            Assert.That(world, Does.Contain(
                "M8_COUNTER_READOUT_PLANE_VALID 30u"));
            Assert.That(world, Does.Contain(
                "M8_COUNTER_READOUT_EMITTED_PATCHES 31u"));
            Assert.That(world, Does.Contain(
                "M8_COUNTER_READOUT_PLANE_LEGACY_INVALID 96u"));
            Assert.That(readout, Does.Contain(
                "gM8ShellValidPatchCount * M8_OVERLAP_TRIANGLES_PER_PATCH"));
            Assert.That(readout, Does.Contain(
                "M8_COUNTER_READOUT_EMITTED_TRIANGLES"));
            Assert.That(MerkabaGrid.CounterReadoutPlaneLegacyInvalid,
                Is.EqualTo(96));
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
            Assert.That(frame, Does.Contain("M8DrawChildMask"));
            Assert.That(frame, Does.Not.Contain(
                "MerkabaM8KernelPlaneChildMask"));
            Assert.That(frame, Does.Contain(
                "MerkabaM8GridDistanceChildMask"));
            Assert.That(frame, Does.Not.Contain("_MerkabaGridToWorld"));
            Assert.That(frame, Does.Not.Contain("_MerkabaWorldToGrid"));
            Assert.That(frame, Does.Not.Contain("MerkabaM8PlaneChildMask"));
            Assert.That(Source("Runtime/Merkaba/MerkabaGridRenderer.cs"),
                Does.Not.Contain("MerkabaReadoutCoverage.WorldToKernelPlane"));
            Assert.That(scan, Does.Not.Contain("TileIntersectsScan"));
        }

        [Test]
        public void CarveMembership_IsCanonicalPersistentAndLoadDerivedOnlyFromFlag()
        {
            string world = Source("Runtime/Shaders/MerkabaWorld.hlsl");
            string compute = Source("Runtime/Shaders/MerkabaWorld.compute");
            string integration = Source(
                "Runtime/Shaders/MerkabaIntegration.compute");
            Assert.That(world, Does.Contain(
                "#define MERKABA_NEEDS_CARVE_FLAG 2u"));

            string surface = Slice(integration,
                "void IntegrateSurfaceCandidates", "uint M8ScanChildMask");
            Assert.That(surface, Does.Contain(
                "state.flags |= MERKABA_NEEDS_CARVE_FLAG"));

            string carveKernel = Slice(integration, "void IntegrateCarveKernel",
                "void IntegrateCarveTiles");
            string carve = Slice(integration, "void IntegrateCarveTiles",
                "void FinalizeObservation");
            Assert.That(carveKernel, Does.Contain("UpdateOccupancy"));
            Assert.That(carveKernel, Does.Contain(
                "state.flags &= ~MERKABA_NEEDS_CARVE_FLAG"));
            Assert.That(carveKernel, Does.Contain(
                "state.evidence <= MERKABA_EXPORT_KNOWN_FREE"));
            Assert.That(carveKernel, Does.Not.Contain("M8FindOrClaimBlock"));
            Assert.That(carveKernel, Does.Not.Contain("M8FindOrClaimChunk"));
            Assert.That(Regex.Matches(carve, @"\breturn;"), Has.Count.Zero,
                "Indirect dispatch owns the exact domain; no lane may return " +
                "before either group barrier.");

            string install = Slice(compute, "void InstallLoadedTiles",
                "void FailLoadedTiles");
            Assert.That(install, Does.Contain(
                "state.flags & MERKABA_NEEDS_CARVE_FLAG"));
            Assert.That(install, Does.Not.Contain(
                "state.evidence > MERKABA_EXPORT_KNOWN_FREE"));
        }

        [Test]
        public void JointSurfaceClassification_PrecedesFreeAndCarveStatsAreReduced()
        {
            string integration = Source(
                "Runtime/Shaders/MerkabaIntegration.compute");
            Assert.That(integration, Does.Contain(
                "#define MERKABA_SURFACE_SCALE 640.0"));
            Assert.That(integration, Does.Contain(
                "#define MERKABA_FREE_SCALE 256.0"));
            Assert.That(integration, Does.Contain(
                "#define MERKABA_EVIDENCE_CONFIDENCE_LIMIT 2560"));
            Assert.That(integration, Does.Contain(
                "#define MERKABA_FREE_FULL_CLEARANCE 0.150"));
            string cheap = Slice(integration,
                "uint CheapFrozenRayGate",
                "bool ExactFrozenRayClassify");
            string observation = Slice(integration,
                "bool ExactFrozenRayClassify",
                "bool M8TryOccupiedExactForCarve");
            Assert.That(cheap, Does.Contain("gsDepthTex.Load"));
            Assert.That(cheap, Does.Contain("gsDepthNDCtoWorld"));
            Assert.That(cheap, Does.Not.Contain("gsDepthNormalTex.Load"));
            Assert.That(cheap, Does.Not.Contain("gsDilatedDepth.Load"));
            Assert.That(observation, Does.Contain("gsDepthNormalTex.Load"));
            Assert.That(observation, Does.Contain(
                "float clearance = measuredDistance - kernelDistance"));
            Assert.That(observation, Does.Contain(
                "perpendicularDistance > MERKABA_HALF_SUPPORT"));
            Assert.That(observation, Does.Contain(
                "kernelDistance < measuredDistance - MERKABA_HALF_SUPPORT"));
            Assert.That(observation, Does.Contain(
                "float3 rayOrigin = MerkabaDepthEyePosition(0u)"));
            Assert.That(observation, Does.Contain(
                "MerkabaFreeDistanceWeight(clearance)"));
            Assert.That(observation, Does.Contain("gsDilatedDepth.Load"));
            Assert.That(integration, Does.Not.Contain("void FuseDepth"));
            Assert.That(integration, Does.Not.Contain("ObserveJointDepth"));

            string carve = Slice(integration, "groupshared uint gCarveStats",
                "void FinalizeObservation");
            Assert.That(carve, Does.Contain(
                "M8TryOccupiedExactForCarve"));
            Assert.That(carve, Does.Contain(
                "MERKABA_OCCUPIED_OFF + 1"));
            Assert.That(carve, Does.Contain("replacementResolved"));
            Assert.That(carve, Does.Contain(
                "evidenceWeight *\n            MERKABA_FREE_SCALE"));

            Assert.That(carve, Does.Contain("FlushCarveStat"));
            Assert.That(carve, Does.Contain(
                "M8_COUNTER_CARVE_CLASSIFIED_FREE"));
            Assert.That(carve, Does.Contain(
                "M8_COUNTER_CARVE_CLASSIFIED_SURFACE"));
            Assert.That(carve, Does.Contain(
                "M8_COUNTER_CARVE_CLASSIFIED_UNKNOWN"));
            Assert.That(carve, Does.Not.Contain(
                "M8CounterIncrement(M8_COUNTER_CARVE_ACTIVE_KERNELS)"));

            string reset = Slice(
                Source("Runtime/Shaders/MerkabaWorld.compute"),
                "void ResetObservationCounters", "void ClearTouchedSurfaceCandidates");
            foreach (string counter in new[]
                     {
                         "M8_COUNTER_CARVE_CLASSIFIED_FREE",
                         "M8_COUNTER_CARVE_CLASSIFIED_SURFACE",
                         "M8_COUNTER_CARVE_CLASSIFIED_UNKNOWN",
                         "M8_COUNTER_CARVE_EVIDENCE_DECREMENTS",
                         "M8_COUNTER_CARVE_OCCUPIED_TO_FREE",
                         "M8_COUNTER_CARVE_BITS_RETIRED",
                         "M8_COUNTER_COLD_CARVE_TILES_REQUESTED",
                         "M8_COUNTER_CARVE_CHEAP_INVALID_PROJECTION_DEPTH",
                         "M8_COUNTER_CARVE_CHEAP_NOT_IN_FRONT",
                         "M8_COUNTER_CARVE_CHEAP_OUTSIDE_RAY_TUBE",
                         "M8_COUNTER_CARVE_CHEAP_OUTSIDE_OUTER_ATTENTION",
                         "M8_COUNTER_CARVE_CHEAP_SURFACE_ENDPOINT",
                         "M8_COUNTER_CARVE_EXACT_EVALUATIONS",
                         "M8_COUNTER_CARVE_EXACT_INCIDENCE_REJECT",
                         "M8_COUNTER_CARVE_EXACT_DILATION_REJECT"
                     })
                Assert.That(reset, Does.Contain(counter), counter);
        }

        [Test]
        public void CarveCheapGate_PrecedesExactWorkAndTileDispatchIsCooperative()
        {
            string integration = Source(
                "Runtime/Shaders/MerkabaIntegration.compute");
            string cheap = Slice(integration, "uint CheapFrozenRayGate",
                "bool ExactFrozenRayClassify");
            string exact = Slice(integration, "bool ExactFrozenRayClassify",
                "bool M8TryOccupiedExactForCarve");
            string prepare = Slice(integration, "void PrepareCarveArgs",
                "groupshared uint gCarveWords");
            string carve = Slice(integration, "void IntegrateCarveTiles",
                "void FinalizeObservation");

            Assert.That(cheap, Does.Contain("gsDepthTex.Load"));
            Assert.That(cheap, Does.Contain("gsDepthNDCtoWorld"));
            Assert.That(cheap, Does.Contain(
                "perpendicularDistance > MERKABA_HALF_SUPPORT"));
            Assert.That(cheap, Does.Contain(
                "kernelDistance >= rayDistance - MERKABA_HALF_SUPPORT"));
            Assert.That(cheap, Does.Contain(
                "TryMerkabaDepthRadialPosition"));
            Assert.That(cheap, Does.Not.Contain("gsDepthNormalTex.Load"));
            Assert.That(cheap, Does.Not.Contain("gsDilatedDepth.Load"));
            Assert.That(exact, Does.Contain("gsDepthNormalTex.Load"));
            Assert.That(exact, Does.Contain("gsDilatedDepth.Load"));
            Assert.That(prepare, Does.Contain("_M8CarveDispatchArgs[1] = 1u"));
            Assert.That(integration, Does.Contain(
                "groupshared uint gCarveWords[16]"));
            Assert.That(integration, Does.Contain(
                "[numthreads(128, 1, 1)]\nvoid IntegrateCarveTiles"));
            Assert.That(carve, Does.Contain(
                "for (uint batch = 0u; batch < 4u; batch++)"));
            Assert.That(carve, Does.Contain(
                "IntegrateCarveKernel(physicalSlot, kernelLocal)"));
            Assert.That(carve, Does.Not.Contain("groupId.y"));
            Assert.That(Regex.Matches(carve, @"\breturn;"), Has.Count.Zero,
                "No lane may return across either tile-group barrier.");
            foreach (string counter in new[]
                     {
                         "M8_COUNTER_CARVE_CHEAP_INVALID_PROJECTION_DEPTH",
                         "M8_COUNTER_CARVE_CHEAP_NOT_IN_FRONT",
                         "M8_COUNTER_CARVE_CHEAP_OUTSIDE_RAY_TUBE",
                         "M8_COUNTER_CARVE_CHEAP_OUTSIDE_OUTER_ATTENTION",
                         "M8_COUNTER_CARVE_CHEAP_SURFACE_ENDPOINT",
                         "M8_COUNTER_CARVE_EXACT_EVALUATIONS",
                         "M8_COUNTER_CARVE_EXACT_INCIDENCE_REJECT",
                         "M8_COUNTER_CARVE_EXACT_DILATION_REJECT"
                     })
                Assert.That(carve, Does.Contain(counter), counter);
        }

        [Test]
        public void DepthAttention_GatesRevisionAndFreeButNeverDiscovery()
        {
            string integration = Source(
                "Runtime/Shaders/MerkabaIntegration.compute");
            string constants = Source(
                "Runtime/Merkaba/MerkabaConstants.cs");
            Assert.That(integration, Does.Contain(
                "#define MERKABA_MUTATION_INNER_RADIUS (1.0 / 3.0)"));
            Assert.That(constants, Does.Contain(
                "MutationOuterRadius = 2f / 3f"));
            Assert.That(integration, Does.Contain(
                "radialPosition >= _MerkabaMutationOuterRadius"));
            string radial = Slice(integration,
                "bool TryMerkabaDepthRadialPosition",
                "float3 MerkabaDepthEyePosition");
            Assert.That(radial, Does.Contain(
                "for (uint eye = 0u; eye < 2u; eye++)"));
            Assert.That(radial, Does.Contain("gsDepthProj[eye]"));
            Assert.That(radial, Does.Contain("gsDepthView[eye]"));
            Assert.That(radial, Does.Not.Contain("Camera"));

            string route = Slice(integration, "int RouteSurfaceCandidate",
                "float MerkabaFreeDistanceWeight");
            int discovery = route.IndexOf(
                "MERKABA_SURFACE_AUTHORITY_DISCOVERY",
                StringComparison.Ordinal);
            int attention = route.IndexOf("MerkabaMutationAttention",
                StringComparison.Ordinal);
            Assert.That(discovery, Is.GreaterThanOrEqualTo(0));
            Assert.That(attention, Is.GreaterThan(discovery),
                "Unknown-space discovery must not be gated by mutation attention.");
            Assert.That(route, Does.Contain(
                "revision ? MERKABA_SURFACE_AUTHORITY_REVISION"));
            Assert.That(route, Does.Contain(
                "MERKABA_SURFACE_AUTHORITY_SUPPORT"));

            string observation = Slice(integration,
                "bool ExactFrozenRayClassify",
                "bool M8TryOccupiedExactForCarve");
            Assert.That(observation, Does.Contain(
                "attention <= 0.0"));
            Assert.That(observation, Does.Contain(
                "MerkabaFreeDistanceWeight(clearance) * attention"));
            Assert.That(observation, Does.Contain(
                "float kernelDistance = dot(originToKernel, rayDirection)"));
            Assert.That(observation.IndexOf("attention <= 0.0",
                    StringComparison.Ordinal),
                Is.LessThan(observation.IndexOf("kind = 1",
                    StringComparison.Ordinal)),
                "Outside the depth cone must be UNKNOWN before FREE exists.");

            string carve = Slice(integration, "groupshared uint gCarveStats",
                "void FinalizeObservation");
            Assert.That(carve, Does.Contain(
                "_M8TileBits[wordIndex].z & bit"));
            Assert.That(carve, Does.Contain(
                "ObserveFrozenSurfaceWinner(physicalSlot, kernelLocal"));
            Assert.That(Source("Runtime/Shaders/MerkabaWorld.hlsl"),
                Does.Contain("M8_COUNTER_SAME_OBSERVATION_CONFLICT"));
            int surfaceOverride = carve.IndexOf(
                "ObserveFrozenSurfaceWinner(physicalSlot, kernelLocal",
                StringComparison.Ordinal);
            int projectedRay = carve.IndexOf("CheapFrozenRayGate(",
                surfaceOverride + 1, StringComparison.Ordinal);
            int freeMutation = carve.IndexOf("if (observationKind == 1)",
                surfaceOverride + 1, StringComparison.Ordinal);
            Assert.That(surfaceOverride, Is.GreaterThanOrEqualTo(0));
            Assert.That(projectedRay, Is.GreaterThan(surfaceOverride),
                "A current SURFACE must consume its S4 winner before the " +
                "K-center projection path is considered.");
            Assert.That(freeMutation, Is.GreaterThan(surfaceOverride),
                "Same-observation SURFACE must override FREE before mutation.");

            string winner = Slice(integration,
                "bool ObserveFrozenSurfaceWinner", "bool M8TrySurfaceWinnerRank");
            Assert.That(winner, Does.Contain("M8LoadSurfaceWinner"));
            Assert.That(winner, Does.Contain(
                "winnerRank & MERKABA_MEASUREMENT_PACKED_MASK"));
            Assert.That(winner, Does.Contain("TrySurfaceMeasurement(sourcePixel"));
            Assert.That(winner, Does.Not.Contain("gsDepthWorldToNDC"));
            Assert.That(carve, Does.Not.Contain(
                "M8_COUNTER_SAME_OBSERVATION_CONFLICT"));

            string managed = Source(
                "Runtime/Merkaba/MerkabaIntegrator.cs");
            string configure = Slice(managed, "private void ConfigureObservation",
                "private void ConfigureAttempt");
            Assert.That(configure, Does.Contain(
                "BindDepth(_resolveBlocksKernel)"),
                "Owner routing reads the immutable joint depth and normal field.");
        }

        [Test]
        public void MutationAuthorityTelemetry_IsAggregateAndAttemptOwned()
        {
            string world = Source("Runtime/Shaders/MerkabaWorld.hlsl");
            string reset = Slice(
                Source("Runtime/Shaders/MerkabaWorld.compute"),
                "void ResetObservationCounters", "void ClearTouchedSurfaceCandidates");
            string telemetry = Source(
                "Runtime/Telemetry/MerkabaGpuTimestamps.cs");
            foreach (string counter in new[]
                     {
                         "M8_COUNTER_JOINT_ACCEPTED_CENTER",
                         "M8_COUNTER_JOINT_ACCEPTED_MID",
                         "M8_COUNTER_JOINT_ACCEPTED_EDGE",
                         "M8_COUNTER_AUTHORITY_DISCOVERY",
                         "M8_COUNTER_AUTHORITY_SUPPORT",
                         "M8_COUNTER_AUTHORITY_REVISION",
                         "M8_COUNTER_OFF_AXIS_MUTATION_BLOCKED",
                         "M8_COUNTER_SURFACE_REPLACEMENT",
                         "M8_COUNTER_SAME_OBSERVATION_CONFLICT"
                     })
            {
                Assert.That(world, Does.Contain(counter), counter);
                Assert.That(reset, Does.Contain(counter), counter);
            }
            Assert.That(telemetry, Does.Contain("carveFreeRadial=["));
            Assert.That(telemetry, Does.Contain("sameObservationConflict="));
            Assert.That(telemetry, Does.Contain("Merkaba metrics-rgbd"));
            Assert.That(telemetry, Does.Contain("FormatRefineBin"));
            Assert.That(telemetry, Does.Not.Contain(
                "AsyncGPUReadback.Request(_mutation"));
        }

        [Test]
        public void Quest3M8Queries_UseOneSixtyFourLaneGroupPerBlock()
        {
            string scan = Source("Runtime/Shaders/MerkabaIntegration.compute");
            string frame = Source("Runtime/Shaders/MerkabaReadout.compute");
            string scanQuery = Slice(scan, "groupshared uint gScanBlockRef",
                "void PrepareCarveArgs");
            string frameQuery = Slice(frame, "groupshared uint gFrameBlockRef",
                "void PrepareReadoutBuild");

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
                "[numthreads(1, 1, 1)]\nvoid QueryM8Readout"));
        }

        [Test]
        public void Quest3KernelCensus_HasNoSerialDomainKernelOrNewDispatchZoo()
        {
            string world = Source("Runtime/Shaders/MerkabaWorld.compute");
            string scan = Source("Runtime/Shaders/MerkabaIntegration.compute");
            string frame = Source("Runtime/Shaders/MerkabaReadout.compute");
            string all = world + scan + frame;
            Assert.That(Regex.Matches(all, @"^#pragma kernel ",
                RegexOptions.Multiline), Has.Count.EqualTo(46));

            string[] serial = Regex.Matches(all,
                    @"\[numthreads\(1,\s*1,\s*1\)\]\s*void\s+(\w+)")
                .Cast<Match>().Select(match => match.Groups[1].Value)
                .OrderBy(name => name).ToArray();
            Assert.That(serial, Is.EqualTo(new[]
            {
                "FinalizeObservation",
                "FinalizeReadout",
                "PrepareAllocatedClearArgs",
                "PrepareCarveArgs",
                "PrepareEvictionSelection",
                "PrepareIntegrateArgs",
                "PrepareNewTileDispatchArgs",
                "PrepareReadoutBuild",
                "PrepareReadoutEmit",
                "PrepareResolveArgs",
                "ResetClaimQueueCounts",
                "ResetObservationCounters",
                "ResetReadoutBuild"
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
                Source("Runtime/Shaders/StereoRgbdRefine.compute");
            Assert.That(Regex.Matches(depth, @"^#pragma kernel ",
                RegexOptions.Multiline), Has.Count.EqualTo(5));
            Assert.That(Regex.Matches(depth,
                @"\[numthreads\(8,\s*8,\s*1\)\]"), Has.Count.EqualTo(5));
            Assert.That(depth, Does.Not.Contain("[numthreads(1, 1, 1)]"));
            string refine = Source(
                "Runtime/Shaders/StereoRgbdRefine.compute");
            Assert.That(refine, Does.Contain(
                "groupshared uint gRefineMetrics[RGBD_METRIC_VALUE_COUNT]"));
            Assert.That(refine, Does.Contain(
                "GroupMemoryBarrierWithGroupSync();"));
            Assert.That(refine, Does.Not.Contain(
                "InterlockedAdd(_RefineMetrics"));
            string entry = refine.Substring(refine.IndexOf(
                "void StereoRgbdRefine", StringComparison.Ordinal));
            Assert.That(entry, Does.Not.Contain("return;"),
                "No lane may return before the optional group reduction barrier.");
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
            Assert.That(audit, Does.Contain("kernel_count != 51"));
            Assert.That(audit, Does.Contain("DepthNormals.compute"));
            Assert.That(audit, Does.Contain("DepthDilation.compute"));
            Assert.That(audit, Does.Contain("StereoRgbdRefine.compute"));
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
                         "InitializeSurfaceWinners", "SelectSurfaceWinners",
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
                "uint M8ObservationFailureReason()",
                "uint CheapFrozenRayGate");
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
            string readout = Source("Runtime/Shaders/MerkabaReadout.compute");
            Assert.That(address, Does.Contain("bool M8IsHotRef"));
            Assert.That(readout, Does.Contain(
                "if (tileRef == MERKABA_REF_COLD_ON_SSD)"));
            Assert.That(readout, Does.Contain(
                "M8_COUNTER_READOUT_UNRESOLVED"));
            Assert.That(integration, Does.Contain(
                "M8_COUNTER_UNRESOLVED_SURFACE_TILES] == 0u"));
        }

        [Test]
        public void ColdCarveDependency_GatesAllCanonicalMutationAndCompletion()
        {
            string integration = Source(
                "Runtime/Shaders/MerkabaIntegration.compute");
            string integrator = Source(
                "Runtime/Merkaba/MerkabaIntegrator.cs");
            string world = Source("Runtime/Shaders/MerkabaWorld.hlsl");
            string prepareResolve = Slice(integration,
                "void PrepareResolveArgs", "bool RequestColdTile");
            string query = Slice(integration, "void QueryCarveTiles",
                "void PrepareCarveArgs");
            string prepareSurface = Slice(integration,
                "void PrepareIntegrateArgs", "void IntegrateSurfaceCandidates");
            string prepareCarve = Slice(integration,
                "void PrepareCarveArgs", "groupshared uint gCarveStats");
            string finalize = Slice(integration, "void FinalizeObservation",
                "\n}") + "\n}";

            Assert.That(world, Does.Contain(
                "#define M8_COUNTER_UNRESOLVED_CARVE_TILES 63u"));
            Assert.That(MerkabaGrid.CounterUnresolvedCarveTiles, Is.EqualTo(63));
            Assert.That(prepareResolve, Does.Contain(
                "M8_COUNTER_UNRESOLVED_CARVE_TILES] = 0u"));
            Assert.That(query, Does.Contain(
                "tileRef == MERKABA_REF_COLD_ON_SSD"));
            Assert.That(query, Does.Contain("RequestColdTile"));
            Assert.That(Regex.Matches(query,
                @"M8CounterIncrement\(\s*M8_COUNTER_UNRESOLVED_CARVE_TILES\)"),
                Has.Count.EqualTo(2));
            Assert.That(query, Does.Contain("if (!M8IsHotRef(tileRef))"));
            Assert.That(prepareSurface, Does.Contain(
                "M8_COUNTER_UNRESOLVED_CARVE_TILES] == 0u"));
            Assert.That(prepareCarve, Does.Contain(
                "M8_COUNTER_UNRESOLVED_SURFACE_TILES] == 0u"));
            Assert.That(prepareCarve, Does.Contain(
                "M8_COUNTER_UNRESOLVED_CARVE_TILES] == 0u"));
            Assert.That(finalize, Does.Contain(
                "M8_COUNTER_UNRESOLVED_CARVE_TILES] == 0u"));

            int queryDispatch = integrator.IndexOf("DispatchCarveQuery(command)",
                StringComparison.Ordinal);
            int surfaceGate = integrator.IndexOf("_prepareIntegrateKernel",
                integrator.IndexOf("internal bool TrySubmitObservationAttempt()",
                    StringComparison.Ordinal), StringComparison.Ordinal);
            int surfaceMutation = integrator.IndexOf("_integrateSurfaceKernel",
                surfaceGate, StringComparison.Ordinal);
            Assert.That(queryDispatch, Is.LessThan(surfaceGate));
            Assert.That(queryDispatch, Is.LessThan(surfaceMutation));
        }

        [Test]
        public void CarveQueueIsAttemptLocalWhileSurfaceDedupSurvivesRetry()
        {
            string integration = Source(
                "Runtime/Shaders/MerkabaIntegration.compute");
            string prepare = Slice(integration, "void PrepareResolveArgs",
                "bool RequestColdTile");
            string finalize = Slice(integration, "void FinalizeObservation",
                "\n}") + "\n}";

            Assert.That(prepare, Does.Contain(
                "M8_COUNTER_CARVE_TILE_COUNT] = 0u"));
            Assert.That(prepare, Does.Contain(
                "M8_COUNTER_UNRESOLVED_CARVE_TILES] = 0u"));
            Assert.That(prepare, Does.Not.Contain(
                "M8_COUNTER_SURFACE_QUEUE_COUNT] = 0u"));
            Assert.That(prepare, Does.Not.Contain(
                "M8_COUNTER_TOUCHED_TILE_COUNT] = 0u"));
            Assert.That(finalize, Does.Contain(
                "uint completed = _M8Counters[M8_COUNTER_OBSERVATION_COMPLETED]"));
            Assert.That(finalize, Does.Contain(": 0u;"));
        }

        [Test]
        public void LoadedNeedsCarveTile_RetriesTheSameImmutableObservationOnce()
        {
            string integration = Source(
                "Runtime/Shaders/MerkabaIntegration.compute");
            string integrator = Source(
                "Runtime/Merkaba/MerkabaIntegrator.cs");
            string world = Source("Runtime/Shaders/MerkabaWorld.compute");
            string install = Slice(world, "void InstallLoadedTiles",
                "void FailLoadedTiles");
            string retry = Slice(integrator,
                "private bool CanRetryPreparedObservation()",
                "private bool ObservationTimedOut()");
            string submit = Slice(integrator,
                "internal bool TrySubmitObservationAttempt()",
                "private bool CanRetryPreparedObservation()");

            Assert.That(install, Does.Contain(
                "state.flags & MERKABA_NEEDS_CARVE_FLAG"));
            Assert.That(retry, Does.Contain("_waitingForDependency"));
            Assert.That(retry, Does.Contain("ResidencyEpoch"));
            Assert.That(retry, Does.Contain("_attemptResidencyEpoch"));
            Assert.That(submit, Does.Contain("bool newObservation ="));
            Assert.That(submit, Does.Contain("else\n                    ConfigureAttempt();"));
            Assert.That(Regex.Matches(submit, @"_observationToken\s*=").Count,
                Is.EqualTo(1));
            Assert.That(submit.IndexOf("DispatchCarveQuery(command)",
                    StringComparison.Ordinal),
                Is.LessThan(submit.IndexOf("_integrateSurfaceKernel",
                    StringComparison.Ordinal)));
            Assert.That(integration, Does.Contain(
                "M8_COUNTER_UNRESOLVED_CARVE_TILES"));

            const uint observationToken = 91u;
            uint tokenAfterRetry = observationToken;
            var loaded = new KernelState();
            loaded.Apply(MerkabaObservationKind.Surface, 1f,
                new Color32(4, 8, 12, 255));
            int evidenceBefore = loaded.OccupancyEvidence;
            int mutationCount = 0;
            foreach (uint unresolvedCarveTiles in new[] { 1u, 0u })
            {
                bool mutationAllowed = unresolvedCarveTiles == 0u;
                if (!mutationAllowed) continue;
                loaded.Apply(MerkabaObservationKind.Free, 1f, default);
                mutationCount++;
            }
            Assert.That(tokenAfterRetry, Is.EqualTo(observationToken));
            Assert.That(mutationCount, Is.EqualTo(1));
            Assert.That(loaded.OccupancyEvidence,
                Is.EqualTo(evidenceBefore - MerkabaConstants.FreeEvidenceScale));
        }

        [Test]
        public void PhysicalTileAllocation_UsesOneBatchReservationAndOneLedger()
        {
            string address = Source("Runtime/Shaders/MerkabaWorld.hlsl");
            string integration = Source(
                "Runtime/Shaders/MerkabaIntegration.compute");
            string world = Source("Runtime/Shaders/MerkabaWorld.compute");
            string resolve = Slice(integration, "void ResolveSurfaceTiles",
                "bool M8TrySurfaceTargetHot");
            string retry = Slice(integration, "void RetryPendingNewTiles",
                "void PrepareIntegrateArgs");
            string prepare = Slice(world, "void PrepareNewTileDispatchArgs",
                "void ResetObservationCounters");
            string initialize = Slice(world, "void InitializeNewTiles",
                "void ResetClaimQueueCounts");
            string prepareLoad = Slice(world, "void PrepareLoadedTiles",
                "groupshared uint gLoadSlot");

            Assert.That(address, Does.Not.Contain("M8TryPopPhysicalTile"));
            Assert.That(resolve, Does.Contain("_M8PendingNewTileRefs"));
            Assert.That(resolve, Does.Not.Contain("_M8ClaimQueue"));
            Assert.That(retry, Does.Contain("_M8PendingNewTileRefsRead"));
            Assert.That(retry, Does.Contain("uint2(tileRefIndex, 0u)"));
            Assert.That(prepare, Does.Contain(
                "reservationCount = min(pendingClaims, freeCount)"));
            Assert.That(prepare, Does.Contain(
                "M8_COUNTER_FREE_TILE_COUNT] = reservationBase"));
            Assert.That(initialize, Does.Contain(
                "_M8FreeTileStackRead[reservationBase + groupId.x]"));
            Assert.That(prepareLoad, Does.Contain("gLoadNeedCount"));
            Assert.That(prepareLoad, Does.Contain(
                "gLoadReservationCount = min(gLoadNeedCount, freeCount)"));
            Assert.That(prepareLoad, Does.Not.Contain(
                "if (id >= _M8StreamBatchCount) return"));

            foreach ((uint pending, uint free) in new[]
                     {
                         (64u, 32768u), (128u, 32768u), (256u, 32768u),
                         (300u, 100u), (32u, 0u)
                     })
            {
                uint reserved = Math.Min(pending, free);
                uint first = free - reserved;
                Assert.That(reserved, Is.EqualTo(Math.Min(pending, free)));
                Assert.That(first + reserved, Is.EqualTo(free));
                Assert.That(reserved, Is.LessThanOrEqualTo(pending));
            }
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
            string submit = Slice(integrator,
                "internal bool TrySubmitObservationAttempt()",
                "private bool CanRetryPreparedObservation()");
            string retire = Slice(integrator,
                "internal bool TryRetireObservationAttempt()",
                "internal bool TrySubmitObservationAttempt()");
            string retry = Slice(integrator,
                "private bool CanRetryPreparedObservation()",
                "private bool ObservationTimedOut()");
            string apply = Slice(storage, "private void ApplySampledCounters",
                "private void BeginLoadAddressReadback");

            Assert.That(address, Does.Contain(
                "#define M8_COUNTER_RESIDENCY_EPOCH 64u"));
            Assert.That(MerkabaGrid.CounterResidencyEpoch, Is.EqualTo(64));
            Assert.That(world, Does.Contain("M8SignalResidencyChange"));
            Assert.That(submit, Does.Contain(
                "_attemptResidencyEpoch = _grid.ResidencyEpoch"));
            Assert.That(retire, Does.Not.Contain(
                "_attemptResidencyEpoch ="));
            Assert.That(retry, Does.Contain(
                "_grid.ResidencyEpoch != _attemptResidencyEpoch"));
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
            string integrator = Source(
                "Runtime/Merkaba/MerkabaIntegrator.cs");
            string submit = Slice(integrator,
                "internal bool TrySubmitObservationAttempt()",
                "private bool CanRetryPreparedObservation()");
            string storage = Source(
                "Runtime/Merkaba/MerkabaGrid.Storage.cs");
            string pump = Slice(storage, "private void PumpStorage()",
                "internal void PumpStorageForLifecycleRetirement()");
            string exact = Slice(storage,
                "internal void RequestAttemptCompletion(",
                "private void PublishResidencyEpoch(");

            Assert.That(finalize, Does.Contain(
                "_M8AttemptCompletion[0] = uint4(_M8AttemptToken"));
            Assert.That(finalize, Does.Contain(
                "M8_COUNTER_OBSERVATION_TOKEN"));
            Assert.That(finalize, Does.Contain(
                "M8_COUNTER_RESIDENCY_EPOCH"));
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
            Assert.That(exact, Does.Not.Contain("SelectEvictionVictims"));
            Assert.That(exact, Does.Not.Contain("InstallLoadedTiles"));
            Assert.That(exact, Does.Not.Contain("Dispatch"));
        }

        [Test]
        public void FailedObservation_RollsBackOnlyStillClaimedNewTiles()
        {
            string integration = Source(
                "Runtime/Shaders/MerkabaIntegration.compute");
            string world = Source("Runtime/Shaders/MerkabaWorld.compute");
            string finalize = Slice(integration, "void FinalizeObservation",
                "\n}") + "\n}";
            string cleanup = Slice(world,
                "void ClearTouchedSurfaceCandidates",
                "void PrepareEvictionSelection");

            Assert.That(finalize, Does.Contain(
                "M8_COUNTER_CLEANUP_PENDING_COUNT"));
            Assert.That(finalize, Does.Contain("failure != 0u"));
            Assert.That(cleanup, Does.Contain(
                "MERKABA_REF_CLAIMED_NEW, MERKABA_REF_EMPTY"));
            Assert.That(cleanup, Does.Not.Contain("M8StoreKernelState"));
            Assert.That(cleanup, Does.Not.Contain("M8PushPhysicalTile"));
            Assert.That(cleanup, Does.Not.Contain("MERKABA_REF_COLD_ON_SSD"));
        }

        [Test]
        public void ZeroCarveHotTile_DoesNotRefreshItsResidencyEpoch()
        {
            string integration = Source(
                "Runtime/Shaders/MerkabaIntegration.compute");
            string query = Slice(integration, "void QueryCarveTiles",
                "void PrepareCarveArgs");
            int zeroCarve = query.IndexOf(
                "_M8TileRecords[M8TileMetaIndex(physicalSlot)].w == 0u",
                StringComparison.Ordinal);
            int residencyTouch = query.IndexOf(
                "_M8TileRecords[M8TileRuntimeIndex(physicalSlot)].z =",
                StringComparison.Ordinal);
            Assert.That(zeroCarve, Is.GreaterThanOrEqualTo(0));
            Assert.That(residencyTouch, Is.GreaterThan(zeroCarve));
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
