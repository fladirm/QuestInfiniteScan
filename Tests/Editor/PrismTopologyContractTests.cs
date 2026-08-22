using System;
using System.IO;
using System.Runtime.InteropServices;
using Genesis.RoomScan.Prism;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class PrismTopologyContractTests
    {
        private const long MaxStorageBindingBytes = 128L * 1024L * 1024L;
        private const string PackageRoot =
            "Packages/com.genesis.roomscan/Runtime/Resources/Prism/";

        [Test]
        public void CanonicalAtlasAbiMatchesDeclaredStrides()
        {
            AssertStride<ContactFilmHeaderGpu>(ContactFilmHeaderGpu.Stride);
            AssertStride<PressureManifoldHeaderGpu>(
                PressureManifoldHeaderGpu.Stride);
            AssertStride<FilmMembershipGpu>(FilmMembershipGpu.Stride);
            AssertStride<SupportContourPageGpu>(SupportContourPageGpu.Stride);
            AssertStride<SupportContourSegmentGpu>(
                SupportContourSegmentGpu.Stride);
            AssertStride<SurfaceHalfEdgeGpu>(SurfaceHalfEdgeGpu.Stride);
            AssertStride<FrontierLoopGpu>(FrontierLoopGpu.Stride);
            AssertStride<ContinuationEvidenceGpu>(
                ContinuationEvidenceGpu.Stride);
            AssertStride<ElasticChartStateGpu>(ElasticChartStateGpu.Stride);
            AssertStride<CrossChunkTopologyPortalGpu>(
                CrossChunkTopologyPortalGpu.Stride);
            AssertStride<BoundaryCurveTopologyGpu>(BoundaryCurveTopologyGpu.Stride);
            AssertStride<EvidenceAlignedSplitPlanGpu>(
                EvidenceAlignedSplitPlanGpu.Stride);
        }

        [Test]
        public void DefaultAtlasBindingsFitQuestVulkanStorageLimit()
        {
            const long films = 65_536;
            long contourSegments = NextPowerOfTwo(films * 3);
            long contourPages = contourSegments /
                PressureManifoldPool.ContourSegmentsPerPage;
            long portals = NextPowerOfTwo(Math.Max(1024, films / 8));
            long[] bindingBytes =
            {
                films * ContactFilmHeaderGpu.Stride,
                films * ContactFilmPool.InformationRecords * 16L,
                films * FilmMembershipGpu.Stride,
                contourPages * SupportContourPageGpu.Stride,
                contourSegments * SupportContourSegmentGpu.Stride,
                contourSegments * SurfaceHalfEdgeGpu.Stride,
                contourSegments * FrontierLoopGpu.Stride,
                contourSegments * ContinuationEvidenceGpu.Stride,
                films * ElasticChartStateGpu.Stride,
                portals * CrossChunkTopologyPortalGpu.Stride
            };
            foreach (long bytes in bindingBytes)
                Assert.That(bytes, Is.LessThan(MaxStorageBindingBytes),
                    $"A single Vulkan storage binding would be {bytes} bytes.");
        }

        [Test]
        public void AtlasWorkGraphKernelsImport()
        {
            AssertKernels("ContactComponentReduce.compute",
                "PrepareComponentReduction", "HookComponentGraph",
                "ShortcutComponentParents", "FinalizeComponentFrames",
                "AccumulateComponentPosterior", "SolveComponentPosterior",
                "EvaluateComponentModel", "ExpandRejectedComponents");
            AssertKernels("SupportContourExtract.compute",
                "CountSupportContourSegments", "PreflightSupportContourPages",
                "CommitSupportContourPages", "WriteSupportContourSegments");
            AssertKernels("ManifoldHalfEdgeUpdate.compute",
                "MaterializeMeasuredHalfEdges", "ProveHalfEdgeTwins",
                "OrderOuterHalfEdges", "CreateFrontierLoops",
                "FinalizeFrontierLoops");
            AssertKernels("BoundaryCurveUpdate.compute",
                "ClaimBoundaryHalfEdges", "CommitBoundaryCurves");
            AssertKernels("ElasticIslandSolve.compute",
                "AccumulateElasticConstraints", "AccumulatePortalConstraints",
                "SolveElasticChartCorrections");
            AssertKernels("EvidenceAlignedSplitPlan.compute",
                "BuildEvidenceAlignedSplitPlans", "ReserveSplitTransactions");
            AssertKernels("ContactChartSplit.compute",
                "CreateEvidenceAlignedChildren");
            AssertKernels("PublishChartSplit.compute",
                "ValidateEvidenceAlignedSplits",
                "CommitEvidenceAlignedSplits");
            AssertKernels("ChunkTopologyStage.compute",
                "StageContourTopology", "StageFrontierLoops",
                "StageCrossChunkPortals");
            AssertKernels("CrossChunkPortalUpdate.compute",
                "BootstrapManifoldIdentity", "BootstrapPortalGhosts",
                "ReconcilePortalGhosts");
            AssertKernels("InformationGainKeyframes.compute",
                "EvaluateVisibleFilmGain", "ReduceFrameInformationGain",
                "FinalizeKeyframeDecision", "CommitKeyframeMetadata");
        }

        [Test]
        public void RectangleFrontierOntologyIsAbsent()
        {
            string runtime = RuntimeRoot();
            string[] files = Directory.GetFiles(runtime, "*.*",
                SearchOption.AllDirectories);
            string[] banned =
            {
                "FrontierUv(", "UnitEdge(", "CompleteOuterEdge(",
                "frontierCount = 4", "FrontierCapacity = FilmCapacity * 4",
                "BuildLegacyRestoredTopology", "ManifoldLinkGpu",
                "LatentFrontierSegmentGpu"
            };
            foreach (string file in files)
            {
                string extension = Path.GetExtension(file);
                if (extension != ".cs" && extension != ".compute" &&
                    extension != ".hlsl" && extension != ".shader")
                    continue;
                string source = File.ReadAllText(file);
                foreach (string token in banned)
                    StringAssert.DoesNotContain(token, source,
                        $"{token} survived in {file}");
            }
        }

        [Test]
        public void SpawnPublishesOneCoordinateConsistentComponentPosterior()
        {
            string reducer = Source("ContactComponentReduce.compute");
            string scheduler = File.ReadAllText(Path.Combine(RuntimeRoot(),
                "Prism/Geometry/PrismFilmSpawner.cs"));
            string spawn = Source("ContactFilmSpawn.compute");

            StringAssert.Contains("directly from the original finite-cone samples",
                reducer);
            StringAssert.Contains("FinalizeComponentFrames", reducer);
            StringAssert.Contains("AccumulateComponentPosterior", reducer);
            StringAssert.Contains("EvaluateComponentModel", reducer);
            StringAssert.Contains("CeilLog2(_filmPool.Capacity) + 2", scheduler);
            StringAssert.Contains("ExpandRejectedComponents", scheduler);
            StringAssert.DoesNotContain("for (int wave = 0; wave < 2", scheduler);
            StringAssert.Contains("#include \"PressureManifoldAtlasAbi.hlsl\"",
                spawn);
            StringAssert.DoesNotContain("struct PressureManifoldHeader", spawn);
            StringAssert.DoesNotContain("_ManifoldAllocator[12]", spawn);
        }

        [Test]
        public void MeasuredSupportCreatesContoursAndLatentStateNeverPredicts()
        {
            string contour = Source("SupportContourExtract.compute");
            string mesh = Source("MeshletBuild.compute");
            string prediction = Source("PredictContactFilm.shader");

            StringAssert.Contains("CaseSegmentCount", contour);
            StringAssert.Contains("CaseEdges", contour);
            StringAssert.Contains("_CoverageThreshold", contour);
            StringAssert.Contains("Canonical UNKNOWN closure remains topology-only",
                mesh);
            StringAssert.Contains("VERTEX_LATENT_FRONTIER", mesh);
            StringAssert.Contains("measuredContact ? film.id : 0u", mesh);
            StringAssert.Contains("BuildFilmMeshletSeams", mesh);
            StringAssert.Contains("VERTEX_MEASURED_CONTACT", mesh);
            StringAssert.Contains("only explicitly measured fragments", prediction);
            StringAssert.Contains("input.filmId == 0u", prediction);
        }

        [Test]
        public void MeshPublicationUsesPerFilmAtlasValidityAndProvenSeams()
        {
            string topology = Source("ManifoldHalfEdgeUpdate.compute");
            string mesh = Source("MeshletBuild.compute");

            StringAssert.Contains("SpliceConfirmedTwins", topology);
            StringAssert.Contains("AccumulateFilmTopologyValidity", topology);
            StringAssert.Contains("FinalizeFilmTopologyValidity", topology);
            StringAssert.Contains("MEMBERSHIP_TOPOLOGY_VALID", topology);
            StringAssert.Contains("FilmTopologyPublishable", mesh);
            StringAssert.Contains("OwnedConfirmedSeam", mesh);
            StringAssert.Contains("countbits(evidence.independentViewMask) < 2u",
                mesh);
            StringAssert.DoesNotContain("_ManifoldDiagnostics[8] != 0u", mesh);
            StringAssert.DoesNotContain("_ManifoldDiagnostics[9] != 0u", mesh);
        }

        [Test]
        public void ContinuationsRequireIndependentPhysicalEvidence()
        {
            string topology = Source("ManifoldHalfEdgeUpdate.compute");
            StringAssert.Contains("countbits(viewMask) >= 2u", topology);
            StringAssert.Contains("firstHitAccepted", topology);
            StringAssert.Contains("visibilityAccepted", topology);
            StringAssert.Contains("poseCalibrationAccepted", topology);
            StringAssert.Contains("EVIDENCE_COMMITTABLE", topology);
            StringAssert.DoesNotContain("LINK_MULTIVIEW", topology);
        }

        [Test]
        public void BoundaryCurveIsSharedAndCachedBeforeMaterialization()
        {
            string boundary = Source("BoundaryCurveUpdate.compute");
            string mesh = Source("MeshletBuild.compute");
            StringAssert.Contains("topology.leftFilmId", boundary);
            StringAssert.Contains("topology.rightFilmId", boundary);
            StringAssert.Contains("boundary.filmB = filmB.id", boundary);
            StringAssert.Contains("_BoundaryCurveCache[boundaryIndex] = cache",
                boundary);
            StringAssert.Contains("StructuredBuffer<BoundaryCurveCache>", mesh);
        }

        [Test]
        public void CanonicalSplitFollowsEvidenceAndCreatesTwoHypotheses()
        {
            string plan = Source("EvidenceAlignedSplitPlan.compute");
            string split = Source("ContactChartSplit.compute");
            string publish = Source("PublishChartSplit.compute");
            StringAssert.Contains("separatorUv", plan);
            StringAssert.Contains("boundary", plan.ToLowerInvariant());
            StringAssert.Contains("childFilmIndex0", split);
            StringAssert.Contains("childFilmIndex1", split);
            StringAssert.DoesNotContain("childFilmIndex2", split);
            StringAssert.Contains("accepted * 2u", publish);
            StringAssert.DoesNotContain("quadrant", split.ToLowerInvariant());
        }

        [Test]
        public void ChunkStagePreservesGlobalManifoldAndPortalTopology()
        {
            string spawn = Source("ContactFilmSpawn.compute");
            string stage = Source("ChunkTopologyStage.compute");
            string portals = Source("CrossChunkPortalUpdate.compute");
            StringAssert.Contains("Component identity is global", spawn);
            StringAssert.DoesNotContain("live.chunkId == _ChunkId", spawn);
            StringAssert.Contains("StageCrossChunkPortals", stage);
            StringAssert.Contains("BootstrapManifoldIdentity", portals);
            StringAssert.Contains("PORTAL_GHOST", portals);
            StringAssert.DoesNotContain("latent cut", stage.ToLowerInvariant());
        }

        [Test]
        public void KeyframesAreSelectedByGpuInformationGain()
        {
            string ingress = Source("InformationGainKeyframes.compute");
            string refiner = File.ReadAllText(Path.Combine(RuntimeRoot(),
                "Prism/Refinement/PrismPhotometricRefiner.cs"));
            StringAssert.Contains("posteriorGain", ingress);
            StringAssert.Contains("footprintGain", ingress);
            StringAssert.Contains("angularGain", ingress);
            StringAssert.Contains("float unresolved", ingress);
            StringAssert.Contains("starvation", ingress.ToLowerInvariant());
            StringAssert.DoesNotContain("translation > 0.04", refiner);
            StringAssert.DoesNotContain("rotation > 4", refiner);
        }

        [Test]
        public void PressureDiagnosticAbiIsStable()
        {
            Array values = Enum.GetValues(typeof(PressureManifoldDiagnostic));
            Assert.That(values.Length,
                Is.EqualTo(PressureManifoldPool.DiagnosticWords));
            for (uint word = 0; word < PressureManifoldPool.DiagnosticWords; word++)
                Assert.That((uint)(PressureManifoldDiagnostic)
                    values.GetValue((int)word), Is.EqualTo(word));
            Assert.That((uint)PressureManifoldDiagnostic.ConfirmedContinuations,
                Is.EqualTo(6u));
            Assert.That((uint)PressureManifoldDiagnostic.OuterFrontierHalfEdges,
                Is.EqualTo(7u));
        }

        private static void AssertStride<T>(int expected) where T : struct =>
            Assert.That(Marshal.SizeOf<T>(), Is.EqualTo(expected), typeof(T).Name);

        private static void AssertKernels(string asset, params string[] kernels)
        {
            ComputeShader shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                PackageRoot + asset);
            Assert.That(shader, Is.Not.Null, asset);
            foreach (string kernel in kernels)
                Assert.DoesNotThrow(() => shader.FindKernel(kernel),
                    $"{asset}:{kernel}");
        }

        private static string Source(string asset) => File.ReadAllText(
            Path.Combine(RuntimeRoot(), "Resources/Prism", asset));

        private static string RuntimeRoot() => Path.GetFullPath(Path.Combine(
            Application.dataPath, "../Packages/com.genesis.roomscan/Runtime"));

        private static long NextPowerOfTwo(long value)
        {
            long result = 1;
            while (result < value) result <<= 1;
            return result;
        }
    }
}
