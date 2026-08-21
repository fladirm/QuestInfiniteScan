using System;
using System.IO;
using System.Runtime.InteropServices;
using Genesis.RoomScan.Prism;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.Tests
{
    public sealed class PrismTopologyContractTests
    {
        private const long MaxStorageBindingBytes = 128L * 1024L * 1024L;

        [Test]
        public void TopologyGpuAbiMatchesDeclaredStrides()
        {
            Assert.That(Marshal.SizeOf<ContactFilmHeaderGpu>(),
                Is.EqualTo(ContactFilmHeaderGpu.Stride));
            Assert.That(Marshal.SizeOf<DisplacementPageHeaderGpu>(),
                Is.EqualTo(DisplacementPageHeaderGpu.Stride));
            Assert.That(Marshal.SizeOf<DisplacementCellGpu>(),
                Is.EqualTo(DisplacementCellGpu.Stride));
            Assert.That(Marshal.SizeOf<ContactTopologyEvidenceGpu>(),
                Is.EqualTo(ContactTopologyEvidenceGpu.Stride));
            Assert.That(Marshal.SizeOf<TopologySplitRecordGpu>(),
                Is.EqualTo(TopologySplitRecordGpu.Stride));
            Assert.That(Marshal.SizeOf<TopologyBoundarySplitPlanGpu>(),
                Is.EqualTo(TopologyBoundarySplitPlanGpu.Stride));
            Assert.That(Marshal.SizeOf<PressureManifoldHeaderGpu>(),
                Is.EqualTo(PressureManifoldHeaderGpu.Stride));
            Assert.That(Marshal.SizeOf<FilmMembershipGpu>(),
                Is.EqualTo(FilmMembershipGpu.Stride));
            Assert.That(Marshal.SizeOf<ManifoldLinkGpu>(),
                Is.EqualTo(ManifoldLinkGpu.Stride));
            Assert.That(Marshal.SizeOf<ManifoldLinkIncidenceGpu>(),
                Is.EqualTo(ManifoldLinkIncidenceGpu.Stride));
            Assert.That(Marshal.SizeOf<ManifoldFrontierIncidenceGpu>(),
                Is.EqualTo(ManifoldFrontierIncidenceGpu.Stride));
            Assert.That(Marshal.SizeOf<LatentFrontierSegmentGpu>(),
                Is.EqualTo(LatentFrontierSegmentGpu.Stride));
        }

        [Test]
        public void DefaultSparseBuffersRemainBelowStorageBindingLimit()
        {
            const long films = 65_536;
            const long basePages = 8_192;
            const long microPages = 16_384;
            long baseCells = basePages * ContactDisplacementPool.BaseCellsPerPage;
            long microCells = microPages * ContactDisplacementPool.MicroCellsPerPage;
            long accumulatorWords =
                ContactDisplacementPool.TransientAccumulatorWordsPerCell;

            // The old combined transient arena is deliberately larger than the
            // Quest/Vulkan single-binding limit. Capacity and information are kept
            // by segmenting it along the canonical base/micro address spaces.
            long legacyCombinedAccumulator = (baseCells + microCells) *
                accumulatorWords * sizeof(int);
            Assert.That(legacyCombinedAccumulator,
                Is.GreaterThan(MaxStorageBindingBytes));

            long[] bindingBytes =
            {
                films * ContactFilmHeaderGpu.Stride,
                films * ContactFilmPool.InformationRecords * 16L,
                baseCells * DisplacementCellGpu.Stride,
                microCells * DisplacementCellGpu.Stride,
                baseCells * accumulatorWords * sizeof(int),
                microCells * accumulatorWords * sizeof(int),
                films * ContactTopologyEvidenceGpu.Stride,
                films * FilmMembershipGpu.Stride,
                films * ManifoldLinkGpu.Stride * 2L,
                films * ManifoldLinkIncidenceGpu.Stride * 4L,
                films * ManifoldFrontierIncidenceGpu.Stride * 4L,
                films * LatentFrontierSegmentGpu.Stride * 4L
            };
            foreach (long bytes in bindingBytes)
                Assert.That(bytes, Is.LessThan(MaxStorageBindingBytes),
                    $"A single Vulkan storage binding would be {bytes} bytes.");
        }

        [Test]
        public void RequiredQ311ComputeKernelsImport()
        {
            ComputeShader displacement = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.genesis.roomscan/Runtime/Resources/Prism/" +
                "DisplacementTopology.compute");
            ComputeShader topology = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.genesis.roomscan/Runtime/Resources/Prism/" +
                "TopologyAdapt.compute");
            ComputeShader manifold = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.genesis.roomscan/Runtime/Resources/Prism/" +
                "PressureManifoldTopology.compute");
            Assert.That(displacement, Is.Not.Null);
            Assert.That(topology, Is.Not.Null);
            Assert.That(manifold, Is.Not.Null);

            string[] displacementKernels =
            {
                "InitializeDisplacementState", "AllocateBasePages",
                "AllocateBasePagesBehind", "AllocateBasePagesOccluder",
                "AccumulateDisplacement", "AccumulateTopologyEvidence",
                "SolveDirtyDisplacement",
                "AccumulatePreHitPressure",
                "AccumulateOccluderPreHitPressure",
                "AllocateMicrotiles", "InitializeMicroPages",
                "SolveTopologyEvidence"
            };
            string[] topologyKernels =
            {
                "SplitContactFilms", "InitializeSplitDisplacement",
                "TransferSplitBoundaries", "ClearTopologyBoundaryHash",
                "RehashTopologyBoundaries"
            };
            foreach (string kernel in displacementKernels)
                Assert.DoesNotThrow(() => displacement.FindKernel(kernel), kernel);
            foreach (string kernel in topologyKernels)
                Assert.DoesNotThrow(() => topology.FindKernel(kernel), kernel);
            string[] manifoldKernels =
            {
                "InitializeManifoldTopology", "PlanSplitTransactions",
                "RemapSplitMemberships",
                "BuildCanonicalLinkHash", "LinkSplitChildren",
                "BuildFilmContinuationHash", "ProveMeasuredContinuationLinks",
                "ValidateFilmMemberships", "ValidateManifoldLinks",
                "FinalizeManifoldValidation"
            };
            foreach (string kernel in manifoldKernels)
                Assert.DoesNotThrow(() => manifold.FindKernel(kernel), kernel);
        }

        [Test]
        public void PressureAccumulatorAbiKeepsAllElevenMoments()
        {
            // displacement W/WD, coverage W/WD, opposing pressure W/WD,
            // nearest pre-hit, footprint W/WD, depth confidence and evidence.
            Assert.That(ContactDisplacementPool.TransientAccumulatorWordsPerCell,
                Is.EqualTo(11));
        }

        [Test]
        public void PressureManifoldDiagnosticAbiIsCompleteAndStable()
        {
            Array values = Enum.GetValues(typeof(PressureManifoldDiagnostic));
            Assert.That(values.Length, Is.EqualTo(PressureManifoldPool.DiagnosticWords));
            for (uint word = 0; word < PressureManifoldPool.DiagnosticWords; word++)
                Assert.That((uint)(PressureManifoldDiagnostic)values.GetValue((int)word),
                    Is.EqualTo(word), $"diagnostic word {word}");
            Assert.That((uint)PressureManifoldDiagnostic.LatentPredictionPixels,
                Is.EqualTo(4u));
            Assert.That((uint)PressureManifoldDiagnostic.UnpairedActiveEdges,
                Is.EqualTo(8u));
            Assert.That((uint)PressureManifoldDiagnostic.UnsupportedMeasuredTriangles,
                Is.EqualTo(14u));
            Assert.That((uint)PressureManifoldDiagnostic.MeshletAllocationOverflow,
                Is.EqualTo(15u));
        }

        [Test]
        public void SpawnPublicationIsOneGpuTransactionWithoutPerFilmAllocator()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath,
                "../Packages/com.genesis.roomscan/Runtime"));
            string shader = File.ReadAllText(Path.Combine(root,
                "Resources/Prism/ContactFilmSpawn.compute"));
            string scheduler = File.ReadAllText(Path.Combine(root,
                "Prism/Geometry/PrismFilmSpawner.cs"));

            StringAssert.Contains("Plan the complete batch in scratch storage first",
                shader);
            StringAssert.Contains("One deterministic commit", shader);
            StringAssert.DoesNotContain("AssignCandidatePublication", shader);
            StringAssert.DoesNotContain("AllocateFilmSlot(", shader);
            StringAssert.DoesNotContain("AssignCandidatePublication", scheduler);
        }

        [Test]
        public void FilmFlagsKeepOneSidedDetailAndRetiredParentsDistinct()
        {
            uint activeChild = (uint)(ContactFilmFlags.Active |
                ContactFilmFlags.OneSided | ContactFilmFlags.HasDisplacement);
            uint retiredParent = (uint)(ContactFilmFlags.SplitParent |
                ContactFilmFlags.Retired);

            Assert.That(activeChild & (uint)ContactFilmFlags.OneSided,
                Is.Not.Zero);
            Assert.That(activeChild & (uint)ContactFilmFlags.HasDisplacement,
                Is.Not.Zero);
            Assert.That(retiredParent & (uint)ContactFilmFlags.Active, Is.Zero);
            Assert.That(retiredParent & activeChild, Is.Zero);
        }

        [Test]
        public void ProductionValidatorAcceptsOneOrderedLoopAndRejectsDuplicateEdge()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null ||
                !SystemInfo.supportsComputeShaders)
                Assert.Ignore("Requires the Vulkan compute test runner.");

            ComputeShader shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.genesis.roomscan/Runtime/Resources/Prism/" +
                "PressureManifoldTopology.compute");
            Assert.That(shader, Is.Not.Null);

            using var films = Buffer<ContactFilmHeaderGpu>(1);
            using var filmAllocator = UIntBuffer(8);
            using var activeFilms = UIntBuffer(1);
            using var manifolds = Buffer<PressureManifoldHeaderGpu>(1);
            using var memberships = Buffer<FilmMembershipGpu>(1);
            using var links = Buffer<ManifoldLinkGpu>(1);
            using var linkIncidences = Buffer<ManifoldLinkIncidenceGpu>(2);
            using var frontiers = Buffer<LatentFrontierSegmentGpu>(4);
            using var frontierIncidences = Buffer<ManifoldFrontierIncidenceGpu>(4);
            using var manifoldAllocator = UIntBuffer(PressureManifoldPool.AllocatorWords);
            using var current = UIntBuffer(4);
            using var diagnostics = UIntBuffer(PressureManifoldPool.DiagnosticWords);

            films.SetData(new[] { ActiveFilm() });
            filmAllocator.SetData(new uint[] { 1, 1, 0, 1, 0, 0, 1, 0 });
            activeFilms.SetData(new uint[] { 0 });
            manifolds.SetData(new[] { ActiveManifold() });
            memberships.SetData(new[] { ActiveMembership() });
            links.SetData(new ManifoldLinkGpu[1]);
            linkIncidences.SetData(new ManifoldLinkIncidenceGpu[2]);
            LatentFrontierSegmentGpu[] loop = OrderedFrontierLoop();
            frontiers.SetData(loop);
            frontierIncidences.SetData(OrderedFrontierIncidences());
            manifoldAllocator.SetData(new uint[]
            {
                1, 1, 0, 1, 0, 0, 0, 1,
                4, 4, 0, 1, 1, 0, 0, 1
            });
            current.SetData(new uint[] { 1, 1, 1, 1 });
            diagnostics.SetData(new uint[PressureManifoldPool.DiagnosticWords]);

            int clear = shader.FindKernel("ClearManifoldValidation");
            int validateFilms = shader.FindKernel("ValidateFilmMemberships");
            int validateLinks = shader.FindKernel("ValidateManifoldLinks");
            int finalize = shader.FindKernel("FinalizeManifoldValidation");
            BindValidation(shader, clear, films, filmAllocator, activeFilms,
                manifolds, memberships, links, linkIncidences, frontiers,
                frontierIncidences, manifoldAllocator, current, diagnostics);
            BindValidation(shader, validateFilms, films, filmAllocator, activeFilms,
                manifolds, memberships, links, linkIncidences, frontiers,
                frontierIncidences, manifoldAllocator, current, diagnostics);
            BindValidation(shader, validateLinks, films, filmAllocator, activeFilms,
                manifolds, memberships, links, linkIncidences, frontiers,
                frontierIncidences, manifoldAllocator, current, diagnostics);
            BindValidation(shader, finalize, films, filmAllocator, activeFilms,
                manifolds, memberships, links, linkIncidences, frontiers,
                frontierIncidences, manifoldAllocator, current, diagnostics);

            shader.Dispatch(clear, 1, 1, 1);
            shader.Dispatch(validateFilms, 1, 1, 1);
            shader.Dispatch(validateLinks, 1, 1, 1);
            shader.Dispatch(finalize, 1, 1, 1);
            uint[] words = new uint[PressureManifoldPool.DiagnosticWords];
            diagnostics.GetData(words);
            Assert.That(words[(int)PressureManifoldDiagnostic.UnpairedActiveEdges],
                Is.Zero);
            Assert.That(words[(int)PressureManifoldDiagnostic.StaleLinkEndpoints],
                Is.Zero);
            var manifoldResult = new PressureManifoldHeaderGpu[1];
            manifolds.GetData(manifoldResult);
            Assert.That(manifoldResult[0].Flags &
                (uint)PressureManifoldFlags.Closed, Is.Not.Zero);

            // Two bottom edges leave the top edge unclassified. The production
            // validator must reject this generation rather than inventing a cap.
            loop[2].Uv01 = new Vector4(0, 0, 1, 0);
            frontiers.SetData(loop);
            shader.Dispatch(clear, 1, 1, 1);
            shader.Dispatch(validateFilms, 1, 1, 1);
            shader.Dispatch(finalize, 1, 1, 1);
            diagnostics.GetData(words);
            Assert.That(words[(int)PressureManifoldDiagnostic.UnpairedActiveEdges],
                Is.GreaterThan(0u));
            manifolds.GetData(manifoldResult);
            Assert.That(manifoldResult[0].Flags &
                (uint)PressureManifoldFlags.Closed, Is.Zero);
        }

        [Test]
        public void CandidateBatchReservationCommitsAllOrNoCanonicalSlots()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null ||
                !SystemInfo.supportsComputeShaders)
                Assert.Ignore("Requires the Vulkan compute test runner.");

            ComputeShader shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.genesis.roomscan/Runtime/Resources/Prism/" +
                "ContactFilmSpawn.compute");
            Assert.That(shader, Is.Not.Null);
            int kernel = shader.FindKernel("ReserveCandidatePublication");

            RunCandidateReservationFixture(shader, kernel, 2,
                out uint[] successFilmAllocator, out uint[] successManifoldAllocator,
                out uint[] successDispatch, out CandidatePublicationFixture[] publications);
            Assert.That(successFilmAllocator[0], Is.EqualTo(2u));
            Assert.That(successFilmAllocator[1], Is.EqualTo(2u));
            Assert.That(successFilmAllocator[6], Is.EqualTo(2u));
            Assert.That(successFilmAllocator[7], Is.EqualTo(2u));
            Assert.That(successManifoldAllocator[8], Is.EqualTo(8u));
            Assert.That(successDispatch[3], Is.EqualTo(2u));
            Assert.That(publications[0].Valid, Is.EqualTo(1u));
            Assert.That(publications[1].Valid, Is.EqualTo(1u));

            RunCandidateReservationFixture(shader, kernel, 1,
                out uint[] failedFilmAllocator, out uint[] failedManifoldAllocator,
                out uint[] failedDispatch, out _);
            Assert.That(failedFilmAllocator[0], Is.Zero,
                "overflow must not advance film high-water");
            Assert.That(failedFilmAllocator[1], Is.Zero,
                "overflow must not publish a partial live batch");
            Assert.That(failedFilmAllocator[6], Is.Zero);
            Assert.That(failedFilmAllocator[7], Is.Zero);
            Assert.That(failedManifoldAllocator[8], Is.Zero,
                "overflow must not reserve orphan frontier ranges");
            Assert.That(failedDispatch[3], Is.Zero,
                "canonical write dispatch must be disabled on transaction failure");
        }

        [Test]
        public void SplittingFilmBPartitionsLinkAndPreservesOrderedOuterLoop()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null ||
                !SystemInfo.supportsComputeShaders)
                Assert.Ignore("Requires the Vulkan compute test runner.");

            const int filmCapacity = 6;
            const int linkCapacity = 12;
            const int frontierCapacity = 24;
            ComputeShader shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.genesis.roomscan/Runtime/Resources/Prism/" +
                "PressureManifoldTopology.compute");
            Assert.That(shader, Is.Not.Null);

            using var films = Buffer<ContactFilmHeaderGpu>(filmCapacity);
            using var filmAllocator = UIntBuffer(8);
            using var activeFilms = UIntBuffer(filmCapacity);
            using var manifolds = Buffer<PressureManifoldHeaderGpu>(1);
            using var memberships = Buffer<FilmMembershipGpu>(filmCapacity);
            using var links = Buffer<ManifoldLinkGpu>(linkCapacity);
            using var linkIncidences =
                Buffer<ManifoldLinkIncidenceGpu>(linkCapacity * 2);
            using var frontiers =
                Buffer<LatentFrontierSegmentGpu>(frontierCapacity);
            using var frontierIncidences =
                Buffer<ManifoldFrontierIncidenceGpu>(frontierCapacity);
            using var manifoldAllocator =
                UIntBuffer(PressureManifoldPool.AllocatorWords);
            using var current = UIntBuffer(4);
            using var diagnostics =
                UIntBuffer(PressureManifoldPool.DiagnosticWords);
            using var splitRecords = Buffer<TopologySplitRecordGpu>(1);
            using var adaptState = UIntBuffer(8);

            var filmData = new ContactFilmHeaderGpu[filmCapacity];
            filmData[0] = ActiveFilm(1);
            filmData[1] = ActiveFilm(2);
            filmData[1].Flags = (uint)(ContactFilmFlags.SplitParent |
                ContactFilmFlags.Retired |
                ContactFilmFlags.PressureManifoldMember);
            filmData[1].Reserved1 = 4;
            for (uint child = 0; child < 4; child++)
                filmData[child + 2] = ActiveFilm(child + 3);
            films.SetData(filmData);
            filmAllocator.SetData(new uint[] { 6, 5, 0, 1, 0, 0, 5, 0 });
            activeFilms.SetData(new uint[] { 0, 2, 3, 4, 5, 0 });

            PressureManifoldHeaderGpu manifold = ActiveManifold();
            manifold.MembershipCount = 2;
            manifold.LinkStart = 1;
            manifold.LinkCount = 1;
            manifold.FrontierStart = 1;
            manifold.FrontierCount = 6;
            manifolds.SetData(new[] { manifold });

            var membershipData = new FilmMembershipGpu[filmCapacity];
            membershipData[0] = new FilmMembershipGpu
            {
                FilmId = 1, FilmGeneration = 1, ManifoldId = 1,
                ManifoldGeneration = 1, FirstLink = 1, LinkCount = 1,
                FirstFrontier = 1, FrontierCount = 3,
                Flags = (uint)(FilmMembershipFlags.Active |
                    FilmMembershipFlags.Measured), Revision = 1
            };
            membershipData[1] = new FilmMembershipGpu
            {
                FilmId = 2, FilmGeneration = 1, ManifoldId = 1,
                ManifoldGeneration = 1, FirstLink = 2, LinkCount = 1,
                FirstFrontier = 2, FrontierCount = 3,
                Flags = (uint)(FilmMembershipFlags.Active |
                    FilmMembershipFlags.Measured), Revision = 1
            };
            memberships.SetData(membershipData);

            var linkData = new ManifoldLinkGpu[linkCapacity];
            linkData[0] = new ManifoldLinkGpu
            {
                Id = 1, Generation = 1, ManifoldId = 1,
                ManifoldGeneration = 1, FilmA = 1, FilmAGeneration = 1,
                FilmB = 2, FilmBGeneration = 1,
                Type = (uint)ManifoldLinkType.Smooth,
                Flags = (uint)(ManifoldLinkFlags.Active |
                    ManifoldLinkFlags.MultiViewConfirmed), Revision = 1,
                UvA01 = new Vector4(1, 0, 1, 1),
                UvB01 = new Vector4(0, 0, 0, 1),
                Sigma = 0.002f, Support = 1, Confidence = 1
            };
            links.SetData(linkData);
            var linkIncidenceData =
                new ManifoldLinkIncidenceGpu[linkCapacity * 2];
            linkIncidenceData[0] = LinkIncidence(1, 1, 1, 0);
            linkIncidenceData[1] = LinkIncidence(2, 1, 2, 1);
            linkIncidences.SetData(linkIncidenceData);

            LatentFrontierSegmentGpu[] frontierData =
                new LatentFrontierSegmentGpu[frontierCapacity];
            Vector4[] edgeUv =
            {
                new(0, 0, 1, 0), // A bottom
                new(0, 0, 1, 0), // B bottom
                new(1, 0, 1, 1), // B right
                new(1, 1, 0, 1), // B top
                new(1, 1, 0, 1), // A top
                new(0, 1, 0, 0)  // A left
            };
            uint[] owners = { 1, 2, 2, 2, 1, 1 };
            for (uint i = 0; i < 6; i++)
            {
                frontierData[i] = new LatentFrontierSegmentGpu
                {
                    Id = i + 1, Generation = 1, ManifoldId = 1,
                    ManifoldGeneration = 1, FilmId = owners[i],
                    FilmGeneration = 1, NextId = (i + 1) % 6 + 1,
                    NextGeneration = 1, PreviousId = (i + 5) % 6 + 1,
                    PreviousGeneration = 1,
                    Flags = (uint)(LatentFrontierFlags.Active |
                        LatentFrontierFlags.Outer |
                        LatentFrontierFlags.Ordered),
                    Revision = 1, Uv01 = edgeUv[i], Sigma = 0.002f,
                    Support = 1, Confidence = 1
                };
            }
            frontiers.SetData(frontierData);
            var frontierIncidenceData =
                new ManifoldFrontierIncidenceGpu[frontierCapacity];
            frontierIncidenceData[0] = FrontierIncidence(1, 1, 1, 5);
            frontierIncidenceData[4] = FrontierIncidence(5, 5, 1, 6);
            frontierIncidenceData[5] = FrontierIncidence(6, 6, 1, 0);
            frontierIncidenceData[1] = FrontierIncidence(2, 2, 2, 3);
            frontierIncidenceData[2] = FrontierIncidence(3, 3, 2, 4);
            frontierIncidenceData[3] = FrontierIncidence(4, 4, 2, 0);
            frontierIncidences.SetData(frontierIncidenceData);

            manifoldAllocator.SetData(new uint[]
            {
                1, 1, 0, 1, 6, 1, 0, 1,
                9, 6, 0, 1, 2, 0, 0, 1
            });
            current.SetData(new uint[] { 1, 1, 1, 1 });
            diagnostics.SetData(new uint[PressureManifoldPool.DiagnosticWords]);
            splitRecords.SetData(new[]
            {
                new TopologySplitRecordGpu
                {
                    ParentFilmIndex = 1, ParentFilmGeneration = 1,
                    ChildFilmIndex0 = 2, ChildFilmIndex1 = 3,
                    ChildFilmIndex2 = 4, ChildFilmIndex3 = 5,
                    ChildCount = 4, ParentActiveOrdinal = 1,
                    FirstNewActiveOrdinal = 2, FirstDirtyOrdinal = 0,
                    ReservedLinkStart = 1, ReservedExternalLinkCount = 1,
                    ReservedLinkCount = 5, ReservedFrontierStart = 6,
                    ReservedFrontierCount = 3, TransactionState = 2
                }
            });
            adaptState.SetData(new uint[] { 1, 0, 0, 0, 0, 0, 0, 0 });

            int remap = shader.FindKernel("RemapSplitMemberships");
            int linkChildren = shader.FindKernel("LinkSplitChildren");
            BindSplitTopology(shader, remap, filmCapacity, linkCapacity,
                frontierCapacity, films, memberships, links, linkIncidences,
                frontiers, frontierIncidences, manifoldAllocator, diagnostics,
                splitRecords, adaptState);
            BindSplitTopology(shader, linkChildren, filmCapacity, linkCapacity,
                frontierCapacity, films, memberships, links, linkIncidences,
                frontiers, frontierIncidences, manifoldAllocator, diagnostics,
                splitRecords, adaptState);
            shader.Dispatch(remap, 1, 1, 1);
            shader.Dispatch(linkChildren, 1, 1, 1);

            int clear = shader.FindKernel("ClearManifoldValidation");
            int validateFilms = shader.FindKernel("ValidateFilmMemberships");
            int validateLinks = shader.FindKernel("ValidateManifoldLinks");
            int finalize = shader.FindKernel("FinalizeManifoldValidation");
            BindValidation(shader, clear, films, filmAllocator, activeFilms,
                manifolds, memberships, links, linkIncidences, frontiers,
                frontierIncidences, manifoldAllocator, current, diagnostics,
                filmCapacity, linkCapacity, frontierCapacity);
            BindValidation(shader, validateFilms, films, filmAllocator, activeFilms,
                manifolds, memberships, links, linkIncidences, frontiers,
                frontierIncidences, manifoldAllocator, current, diagnostics,
                filmCapacity, linkCapacity, frontierCapacity);
            BindValidation(shader, validateLinks, films, filmAllocator, activeFilms,
                manifolds, memberships, links, linkIncidences, frontiers,
                frontierIncidences, manifoldAllocator, current, diagnostics,
                filmCapacity, linkCapacity, frontierCapacity);
            BindValidation(shader, finalize, films, filmAllocator, activeFilms,
                manifolds, memberships, links, linkIncidences, frontiers,
                frontierIncidences, manifoldAllocator, current, diagnostics,
                filmCapacity, linkCapacity, frontierCapacity);
            shader.Dispatch(clear, 1, 1, 1);
            shader.Dispatch(validateFilms, 1, 1, 1);
            shader.Dispatch(validateLinks, 1, 1, 1);
            shader.Dispatch(finalize, 1, 1, 1);

            var membershipResult = new FilmMembershipGpu[filmCapacity];
            memberships.GetData(membershipResult);
            Assert.That(membershipResult[0].LinkCount, Is.EqualTo(2u));
            Assert.That(membershipResult[1].Flags, Is.Zero,
                "retired FilmB membership must no longer own topology");
            for (int child = 2; child < filmCapacity; child++)
                Assert.That(membershipResult[child].Flags &
                    (uint)FilmMembershipFlags.Active, Is.Not.Zero,
                    $"child {child} membership");

            var linkResult = new ManifoldLinkGpu[linkCapacity];
            links.GetData(linkResult);
            Assert.That(linkResult[0].FilmA, Is.EqualTo(1u));
            Assert.That(linkResult[0].FilmB, Is.EqualTo(3u));
            Assert.That(linkResult[1].FilmA, Is.EqualTo(1u));
            Assert.That(linkResult[1].FilmB, Is.EqualTo(5u));

            var frontierResult =
                new LatentFrontierSegmentGpu[frontierCapacity];
            frontiers.GetData(frontierResult);
            uint cursor = 1;
            for (int visited = 0; visited < 9; visited++)
            {
                Assert.That(cursor, Is.InRange(1u, 9u));
                LatentFrontierSegmentGpu segment = frontierResult[cursor - 1];
                LatentFrontierSegmentGpu next =
                    frontierResult[segment.NextId - 1];
                Assert.That(next.PreviousId, Is.EqualTo(segment.Id));
                cursor = segment.NextId;
            }
            Assert.That(cursor, Is.EqualTo(1u),
                "split outer frontier must remain one reciprocal ordered loop");

            uint[] words = new uint[PressureManifoldPool.DiagnosticWords];
            diagnostics.GetData(words);
            Assert.That(words[(int)PressureManifoldDiagnostic.UnpairedActiveEdges],
                Is.Zero);
            Assert.That(words[(int)PressureManifoldDiagnostic.StaleLinkEndpoints],
                Is.Zero);
            var manifoldResult = new PressureManifoldHeaderGpu[1];
            manifolds.GetData(manifoldResult);
            Assert.That(manifoldResult[0].Flags &
                (uint)PressureManifoldFlags.Closed, Is.Not.Zero);
        }

        [Test]
        public void SplitPlannerReservesEveryArenaOrLeavesParentUntouched()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null ||
                !SystemInfo.supportsComputeShaders)
                Assert.Ignore("Requires the Vulkan compute test runner.");

            ComputeShader shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.genesis.roomscan/Runtime/Resources/Prism/" +
                "PressureManifoldTopology.compute");
            Assert.That(shader, Is.Not.Null);
            int kernel = shader.FindKernel("PlanSplitTransactions");

            RunSplitPlanFixture(shader, kernel, 8, 2,
                out uint[] successFilm, out uint[] successDisplacement,
                out uint[] successManifold, out uint[] successBoundary,
                out uint[] successAdapt,
                out TopologySplitRecordGpu successRecord);
            Assert.That(successAdapt[0], Is.EqualTo(1u));
            Assert.That(successRecord.TransactionState, Is.EqualTo(1u));
            Assert.That(successFilm[0], Is.EqualTo(5u));
            Assert.That(successFilm[1], Is.EqualTo(4u));
            Assert.That(successFilm[6], Is.EqualTo(4u));
            Assert.That(successFilm[7], Is.EqualTo(4u));
            Assert.That(successDisplacement[0], Is.EqualTo(4u));
            Assert.That(successManifold[4], Is.EqualTo(4u));
            Assert.That(successManifold[8], Is.EqualTo(8u));
            Assert.That(successBoundary[0], Is.EqualTo(2u));
            Assert.That(successBoundary[1], Is.EqualTo(2u));
            Assert.That(successRecord.ReservedBoundaryStart, Is.EqualTo(1u));
            Assert.That(successRecord.ReservedBoundaryCount, Is.EqualTo(1u));

            RunSplitPlanFixture(shader, kernel, 3, 2,
                out uint[] failedFilm, out uint[] failedDisplacement,
                out uint[] failedManifold, out uint[] failedBoundary,
                out uint[] failedAdapt, out _);
            Assert.That(failedAdapt[0], Is.Zero);
            Assert.That(failedFilm[2], Is.GreaterThan(0u),
                "failed preflight must report capacity overflow");
            Assert.That(new[]
            {
                failedFilm[0], failedFilm[1], failedFilm[3], failedFilm[4],
                failedFilm[5], failedFilm[6], failedFilm[7]
            }, Is.EqualTo(new uint[] { 1, 1, 1, 0, 0, 1, 0 }),
                "failed preflight may change diagnostics, never canonical " +
                "film slots or compact-list cursors");
            Assert.That(failedDisplacement[0], Is.Zero);
            Assert.That(failedManifold[4], Is.Zero);
            Assert.That(failedManifold[8], Is.EqualTo(4u));
            Assert.That(failedBoundary, Is.EqualTo(new uint[] { 1, 1, 0, 1 }));

            RunSplitPlanFixture(shader, kernel, 8, 1,
                out uint[] boundaryFailedFilm,
                out uint[] boundaryFailedDisplacement,
                out uint[] boundaryFailedManifold,
                out uint[] boundaryFailedAllocator,
                out uint[] boundaryFailedAdapt, out _);
            Assert.That(boundaryFailedAdapt[0], Is.Zero);
            Assert.That(boundaryFailedFilm[0], Is.EqualTo(1u));
            Assert.That(boundaryFailedFilm[1], Is.EqualTo(1u));
            Assert.That(boundaryFailedFilm[6], Is.EqualTo(1u));
            Assert.That(boundaryFailedFilm[7], Is.Zero);
            Assert.That(boundaryFailedDisplacement[0], Is.Zero);
            Assert.That(boundaryFailedManifold[4], Is.Zero);
            Assert.That(boundaryFailedManifold[8], Is.EqualTo(4u));
            Assert.That(boundaryFailedAllocator[0], Is.EqualTo(1u));
            Assert.That(boundaryFailedAllocator[1], Is.EqualTo(1u));
            Assert.That(boundaryFailedAllocator[2], Is.GreaterThan(0u));
        }

        [Test]
        public void SharedBoundarySplitRemapsFilmBWithoutLateAllocation()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null ||
                !SystemInfo.supportsComputeShaders)
                Assert.Ignore("Requires the Vulkan compute test runner.");

            const int filmCapacity = 6;
            const int boundaryCapacity = 2;
            ComputeShader shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.genesis.roomscan/Runtime/Resources/Prism/" +
                "TopologyAdapt.compute");
            Assert.That(shader, Is.Not.Null);
            int kernel = shader.FindKernel("TransferSplitBoundaries");

            using var films = Buffer<ContactFilmHeaderGpu>(filmCapacity);
            using var boundaries =
                Buffer<ContactBoundaryHeaderGpu>(boundaryCapacity);
            using var information = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                boundaryCapacity * ContactBoundaryPool.InformationRecordsPerBoundary,
                sizeof(float) * 4);
            using var boundaryAllocator = UIntBuffer(4);
            using var splitRecords = Buffer<TopologySplitRecordGpu>(1);
            using var boundaryPlans =
                Buffer<TopologyBoundarySplitPlanGpu>(boundaryCapacity);
            using var adaptState = UIntBuffer(8);

            var filmData = new ContactFilmHeaderGpu[filmCapacity];
            filmData[0] = ActiveFilm(1);
            filmData[0].BoundaryStart = 1;
            filmData[0].BoundaryCount = 1;
            filmData[1] = ActiveFilm(2);
            filmData[1].Flags = (uint)(ContactFilmFlags.SplitParent |
                ContactFilmFlags.Retired |
                ContactFilmFlags.PressureManifoldMember);
            filmData[1].Reserved1 = 4;
            for (uint child = 0; child < 4; child++)
                filmData[child + 2] = ActiveFilm(child + 3);
            films.SetData(filmData);

            boundaries.SetData(new[]
            {
                new ContactBoundaryHeaderGpu
                {
                    Id = 1, Generation = 1, ChunkId = 1,
                    Flags = (uint)(ContactBoundaryFlags.Active |
                        ContactBoundaryFlags.Persistent),
                    FilmA = 1, FilmAGeneration = 1,
                    FilmB = 2, FilmBGeneration = 1,
                    ControlUv01 = new Vector4(0, 0.25f, 1f / 3f, 0.25f),
                    ControlUv23 = new Vector4(2f / 3f, 0.25f, 1, 0.25f),
                    Sigma = 0.002f, Support = 8, Confidence = 1,
                    Revision = 1
                },
                default
            });
            var informationData = new Vector4[
                boundaryCapacity * ContactBoundaryPool.InformationRecordsPerBoundary];
            informationData[3] = new Vector4(-0.5f, -0.25f, 0, 0.002f);
            informationData[4] = new Vector4(-1f / 6f, -0.25f, 0, 0.002f);
            informationData[5] = new Vector4(1f / 6f, -0.25f, 0, 0.002f);
            informationData[6] = new Vector4(0.5f, -0.25f, 0, 0.002f);
            informationData[7] = new Vector4(8, 0, 0, 2);
            information.SetData(informationData);
            boundaryAllocator.SetData(new uint[] { 2, 2, 0, 1 });
            splitRecords.SetData(new[]
            {
                new TopologySplitRecordGpu
                {
                    ParentFilmIndex = 1, ParentFilmGeneration = 1,
                    ChildFilmIndex0 = 2, ChildFilmIndex1 = 3,
                    ChildFilmIndex2 = 4, ChildFilmIndex3 = 5,
                    ChildCount = 4, ReservedBoundaryStart = 1,
                    ReservedBoundaryCount = 1, TransactionState = 2
                }
            });
            boundaryPlans.SetData(new[]
            {
                new TopologyBoundarySplitPlanGpu
                {
                    BoundaryIndex = 0, BoundaryGeneration = 1,
                    SplitRecordIndex = 0, ParentFilmIndex = 1,
                    ParentFilmGeneration = 1, ParentEndpoint = 1,
                    ReservedStart = 1, SegmentCount = 2
                },
                default
            });
            adaptState.SetData(new uint[] { 1, 0, 0, 1, 0, 0, 0, 0 });

            shader.SetInt("_FilmCapacity", filmCapacity);
            shader.SetInt("_BoundaryCapacity", boundaryCapacity);
            shader.SetInt("_BoundaryCellsPerAxis", 16);
            shader.SetBuffer(kernel, "_FilmHeaders", films);
            shader.SetBuffer(kernel, "_BoundaryHeaders", boundaries);
            shader.SetBuffer(kernel, "_BoundaryInformation", information);
            shader.SetBuffer(kernel, "_BoundaryAllocator", boundaryAllocator);
            shader.SetBuffer(kernel, "_SplitRecordsRead", splitRecords);
            shader.SetBuffer(kernel, "_BoundarySplitPlansRead", boundaryPlans);
            shader.SetBuffer(kernel, "_AdaptState", adaptState);
            shader.Dispatch(kernel, 1, 1, 1);

            var boundaryResult =
                new ContactBoundaryHeaderGpu[boundaryCapacity];
            boundaries.GetData(boundaryResult);
            uint[] adaptResult = new uint[8];
            adaptState.GetData(adaptResult);
            string transferState = $"b0=({boundaryResult[0].FilmA}," +
                $"{boundaryResult[0].FilmB}) b1=({boundaryResult[1].FilmA}," +
                $"{boundaryResult[1].FilmB}) writes={adaptResult[2]}";
            Assert.That(boundaryResult[0].FilmA, Is.EqualTo(1u));
            Assert.That(boundaryResult[1].FilmA, Is.EqualTo(1u), transferState);
            Assert.That(boundaryResult[0].FilmB, Is.EqualTo(3u));
            Assert.That(boundaryResult[1].FilmB, Is.EqualTo(4u));
            Assert.That(boundaryResult[0].FilmBGeneration, Is.EqualTo(1u));
            Assert.That(boundaryResult[1].FilmBGeneration, Is.EqualTo(1u));
            var filmResult = new ContactFilmHeaderGpu[filmCapacity];
            films.GetData(filmResult);
            Assert.That(filmResult[0].BoundaryCount, Is.EqualTo(2u),
                "the unchanged endpoint owns both exact curve segments");
            Assert.That(filmResult[2].BoundaryCount, Is.EqualTo(1u));
            Assert.That(filmResult[3].BoundaryCount, Is.EqualTo(1u));
            uint[] allocatorResult = new uint[4];
            boundaryAllocator.GetData(allocatorResult);
            Assert.That(allocatorResult, Is.EqualTo(new uint[] { 2, 2, 0, 1 }),
                "transfer must consume the planner range without allocating");
        }

        private static GraphicsBuffer Buffer<T>(int count) where T : struct =>
            new(GraphicsBuffer.Target.Structured, count, Marshal.SizeOf<T>());

        private static GraphicsBuffer UIntBuffer(int count) =>
            new(GraphicsBuffer.Target.Structured, count, sizeof(uint));

        [StructLayout(LayoutKind.Sequential)]
        private struct CandidatePublicationFixture
        {
            public uint Root;
            public uint FilmSlot;
            public uint FilmGeneration;
            public uint FrontierBase;
            public uint Valid;
        }

        private static void RunCandidateReservationFixture(ComputeShader shader,
            int kernel, int filmCapacity, out uint[] filmAllocatorData,
            out uint[] manifoldAllocatorData, out uint[] dispatchData,
            out CandidatePublicationFixture[] publicationData)
        {
            const int candidateCount = 2;
            using var candidateState = UIntBuffer(8);
            using var representatives = UIntBuffer(candidateCount);
            using var publications = Buffer<CandidatePublicationFixture>(candidateCount);
            using var dispatch = new GraphicsBuffer(GraphicsBuffer.Target.Structured |
                GraphicsBuffer.Target.IndirectArguments, 4, sizeof(uint) * 3);
            using var filmAllocator = UIntBuffer(8);
            using var slotStates = Buffer<ContactFilmSlotStateGpu>(filmCapacity);
            using var activeFilms = UIntBuffer(filmCapacity);
            using var dirtyFilms = UIntBuffer(filmCapacity);
            using var manifoldHeaders = Buffer<PressureManifoldHeaderGpu>(1);
            using var manifoldAllocator = UIntBuffer(PressureManifoldPool.AllocatorWords);
            using var current = UIntBuffer(4);

            uint[] state = new uint[8];
            state[3] = candidateCount;
            candidateState.SetData(state);
            representatives.SetData(new uint[] { 0, 1 });
            publications.SetData(new CandidatePublicationFixture[candidateCount]);
            dispatch.SetData(new uint[12]);
            filmAllocator.SetData(new uint[8]);
            slotStates.SetData(new ContactFilmSlotStateGpu[filmCapacity]);
            activeFilms.SetData(new uint[filmCapacity]);
            dirtyFilms.SetData(new uint[filmCapacity]);
            manifoldHeaders.SetData(new[] { ActiveManifold() });
            manifoldAllocator.SetData(new uint[PressureManifoldPool.AllocatorWords]);
            current.SetData(new uint[] { 1, 1, 1, 1 });

            shader.SetInt("_FilmCapacity", filmCapacity);
            shader.SetInt("_CandidateCapacity", candidateCount);
            shader.SetInt("_ManifoldCapacity", 1);
            shader.SetInt("_FrontierCapacity", 8);
            shader.SetBuffer(kernel, "_CandidateState", candidateState);
            shader.SetBuffer(kernel, "_CandidateRepresentativesRead", representatives);
            shader.SetBuffer(kernel, "_CandidatePublications", publications);
            shader.SetBuffer(kernel, "_CandidateDispatchArguments", dispatch);
            shader.SetBuffer(kernel, "_FilmAllocator", filmAllocator);
            shader.SetBuffer(kernel, "_FilmSlotStates", slotStates);
            shader.SetBuffer(kernel, "_ActiveFilmIndices", activeFilms);
            shader.SetBuffer(kernel, "_DirtyFilmIndices", dirtyFilms);
            shader.SetBuffer(kernel, "_ManifoldHeadersRead", manifoldHeaders);
            shader.SetBuffer(kernel, "_ManifoldAllocator", manifoldAllocator);
            shader.SetBuffer(kernel, "_CurrentManifoldRead", current);
            shader.Dispatch(kernel, 1, 1, 1);

            filmAllocatorData = new uint[8];
            manifoldAllocatorData = new uint[PressureManifoldPool.AllocatorWords];
            dispatchData = new uint[12];
            publicationData = new CandidatePublicationFixture[candidateCount];
            filmAllocator.GetData(filmAllocatorData);
            manifoldAllocator.GetData(manifoldAllocatorData);
            dispatch.GetData(dispatchData);
            publications.GetData(publicationData);
        }

        private static void RunSplitPlanFixture(ComputeShader shader, int kernel,
            int linkCapacity, int boundaryCapacity,
            out uint[] filmAllocatorData,
            out uint[] displacementAllocatorData,
            out uint[] manifoldAllocatorData, out uint[] boundaryAllocatorData,
            out uint[] adaptStateData,
            out TopologySplitRecordGpu recordData)
        {
            const int filmCapacity = 5;
            const int frontierCapacity = 8;
            const int basePageCapacity = 4;
            int allocatedLinkCapacity = Math.Max(1, linkCapacity);

            using var films = Buffer<ContactFilmHeaderGpu>(filmCapacity);
            using var filmAllocator = UIntBuffer(8);
            using var slotStates = Buffer<ContactFilmSlotStateGpu>(filmCapacity);
            using var activeFilms = UIntBuffer(filmCapacity);
            using var memberships = Buffer<FilmMembershipGpu>(filmCapacity);
            using var links = Buffer<ManifoldLinkGpu>(allocatedLinkCapacity);
            using var linkIncidences = Buffer<ManifoldLinkIncidenceGpu>(
                allocatedLinkCapacity * 2);
            using var frontiers = Buffer<LatentFrontierSegmentGpu>(frontierCapacity);
            using var frontierIncidences =
                Buffer<ManifoldFrontierIncidenceGpu>(frontierCapacity);
            using var manifoldAllocator =
                UIntBuffer(PressureManifoldPool.AllocatorWords);
            using var topologyEvidence =
                Buffer<ContactTopologyEvidenceGpu>(filmCapacity);
            using var dirtyTopologyIndices = UIntBuffer(filmCapacity);
            using var topologyState = UIntBuffer(4);
            using var displacementAllocator = UIntBuffer(8);
            using var splitRecords = Buffer<TopologySplitRecordGpu>(filmCapacity);
            using var boundaryHeaders =
                Buffer<ContactBoundaryHeaderGpu>(boundaryCapacity);
            using var boundaryInformation = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                boundaryCapacity * ContactBoundaryPool.InformationRecordsPerBoundary,
                sizeof(float) * 4);
            using var boundaryAllocator = UIntBuffer(4);
            using var boundaryPlans =
                Buffer<TopologyBoundarySplitPlanGpu>(boundaryCapacity);
            using var adaptState = UIntBuffer(8);

            var filmData = new ContactFilmHeaderGpu[filmCapacity];
            filmData[0] = ActiveFilm();
            filmData[0].BoundaryStart = 1;
            filmData[0].BoundaryCount = 1;
            films.SetData(filmData);
            uint[] initialFilmAllocator = { 1, 1, 0, 1, 0, 0, 1, 0 };
            filmAllocator.SetData(initialFilmAllocator);
            var slotData = new ContactFilmSlotStateGpu[filmCapacity];
            slotData[0] = new ContactFilmSlotStateGpu
            {
                Generation = 1,
                ActiveOrdinal = 0,
                Flags = (uint)(ContactFilmSlotFlags.Allocated |
                    ContactFilmSlotFlags.Active)
            };
            slotStates.SetData(slotData);
            activeFilms.SetData(new uint[filmCapacity]);

            var membershipData = new FilmMembershipGpu[filmCapacity];
            membershipData[0] = ActiveMembership();
            memberships.SetData(membershipData);
            links.SetData(new ManifoldLinkGpu[allocatedLinkCapacity]);
            linkIncidences.SetData(
                new ManifoldLinkIncidenceGpu[allocatedLinkCapacity * 2]);
            frontiers.SetData(OrderedFrontierLoop());
            frontierIncidences.SetData(OrderedFrontierIncidences());
            manifoldAllocator.SetData(new uint[]
            {
                1, 1, 0, 1,
                0, 0, 0, 1,
                4, 4, 0, 1,
                1, 0, 0, 1
            });

            var evidence = new ContactTopologyEvidenceGpu[filmCapacity];
            evidence[0] = new ContactTopologyEvidenceGpu
            {
                PositiveMoment = 0.04f,
                PositiveSupport = 4f,
                NegativeMoment = -0.04f,
                NegativeSupport = 4f,
                TotalSupport = 8f,
                Revision = 1
            };
            topologyEvidence.SetData(evidence);
            dirtyTopologyIndices.SetData(new uint[filmCapacity]);
            topologyState.SetData(new uint[] { 1, 0, 0, 0 });
            displacementAllocator.SetData(new uint[] { 0, 0, 0, 0, 1, 0, 0, 0 });
            splitRecords.SetData(new TopologySplitRecordGpu[filmCapacity]);
            var boundaryData = new ContactBoundaryHeaderGpu[boundaryCapacity];
            boundaryData[0] = new ContactBoundaryHeaderGpu
            {
                Id = 1, Generation = 1, ChunkId = 1,
                Flags = (uint)(ContactBoundaryFlags.Active |
                    ContactBoundaryFlags.Persistent),
                FilmA = 1, FilmAGeneration = 1,
                ControlUv01 = new Vector4(0, 0.25f, 1f / 3f, 0.25f),
                ControlUv23 = new Vector4(2f / 3f, 0.25f, 1, 0.25f),
                Sigma = 0.002f, Support = 8, Confidence = 1, Revision = 1
            };
            boundaryHeaders.SetData(boundaryData);
            boundaryInformation.SetData(new Vector4[
                boundaryCapacity * ContactBoundaryPool.InformationRecordsPerBoundary]);
            boundaryAllocator.SetData(new uint[] { 1, 1, 0, 1 });
            boundaryPlans.SetData(
                new TopologyBoundarySplitPlanGpu[boundaryCapacity]);
            adaptState.SetData(new uint[8]);

            shader.SetInt("_FilmCapacity", filmCapacity);
            shader.SetInt("_ManifoldCapacity", 1);
            shader.SetInt("_LinkCapacity", linkCapacity);
            shader.SetInt("_FrontierCapacity", frontierCapacity);
            shader.SetInt("_BasePageCapacity", basePageCapacity);
            shader.SetInt("_BoundaryCapacity", boundaryCapacity);
            shader.SetInt("_MaximumSplitDepth", 5);
            shader.SetFloat("_MinimumSplitExtent", 0.0125f);
            shader.SetFloat("_BimodalSeparation", 0.003f);
            shader.SetFloat("_SplitVariance", 0.000009f);
            shader.SetFloat("_SplitBoundarySupport", 8f);
            shader.SetBuffer(kernel, "_FilmHeadersRead", films);
            shader.SetBuffer(kernel, "_FilmAllocatorWrite", filmAllocator);
            shader.SetBuffer(kernel, "_FilmSlotStates", slotStates);
            shader.SetBuffer(kernel, "_ActiveFilmIndices", activeFilms);
            shader.SetBuffer(kernel, "_FilmMembershipsRead", memberships);
            shader.SetBuffer(kernel, "_ManifoldLinksRead", links);
            shader.SetBuffer(kernel, "_ManifoldLinkIncidencesRead", linkIncidences);
            shader.SetBuffer(kernel, "_ManifoldFrontierIncidencesRead",
                frontierIncidences);
            shader.SetBuffer(kernel, "_LatentFrontiersRead", frontiers);
            shader.SetBuffer(kernel, "_ManifoldAllocator", manifoldAllocator);
            shader.SetBuffer(kernel, "_SplitRecordsWrite", splitRecords);
            shader.SetBuffer(kernel, "_AdaptStateWrite", adaptState);
            shader.SetBuffer(kernel, "_TopologyEvidenceRead", topologyEvidence);
            shader.SetBuffer(kernel, "_DirtyTopologyIndices",
                dirtyTopologyIndices);
            shader.SetBuffer(kernel, "_TopologyState", topologyState);
            shader.SetBuffer(kernel, "_DisplacementAllocatorWrite",
                displacementAllocator);
            shader.SetBuffer(kernel, "_BoundaryHeadersRead", boundaryHeaders);
            shader.SetBuffer(kernel, "_BoundaryInformationRead",
                boundaryInformation);
            shader.SetBuffer(kernel, "_BoundaryAllocatorWrite",
                boundaryAllocator);
            shader.SetBuffer(kernel, "_BoundarySplitPlansWrite", boundaryPlans);
            shader.Dispatch(kernel, 1, 1, 1);

            filmAllocatorData = new uint[8];
            displacementAllocatorData = new uint[8];
            manifoldAllocatorData = new uint[PressureManifoldPool.AllocatorWords];
            boundaryAllocatorData = new uint[4];
            adaptStateData = new uint[8];
            var splitData = new TopologySplitRecordGpu[filmCapacity];
            filmAllocator.GetData(filmAllocatorData);
            displacementAllocator.GetData(displacementAllocatorData);
            manifoldAllocator.GetData(manifoldAllocatorData);
            boundaryAllocator.GetData(boundaryAllocatorData);
            adaptState.GetData(adaptStateData);
            splitRecords.GetData(splitData);
            recordData = splitData[0];
        }

        private static ContactFilmHeaderGpu ActiveFilm() => ActiveFilm(1);

        private static ContactFilmHeaderGpu ActiveFilm(uint id) => new()
        {
            Id = id, Generation = 1, ChunkId = 1,
            Flags = (uint)(ContactFilmFlags.Active | ContactFilmFlags.OneSided |
                ContactFilmFlags.PressureManifoldMember),
            Normal = Vector3.forward, TangentU = Vector3.right,
            TangentV = Vector3.up, ExtentU = 0.5f, ExtentV = 0.5f,
            SigmaNormal = 0.002f, Confidence = 1f,
            SupportMaskLow = uint.MaxValue, SupportMaskHigh = uint.MaxValue
        };

        private static ManifoldLinkIncidenceGpu LinkIncidence(uint id,
            uint linkId, uint filmId, uint endpoint) => new()
        {
            Id = id, Generation = 1, LinkId = linkId, LinkGeneration = 1,
            FilmId = filmId, FilmGeneration = 1, Endpoint = endpoint,
            Flags = 1
        };

        private static ManifoldFrontierIncidenceGpu FrontierIncidence(uint id,
            uint frontierId, uint filmId, uint nextId) => new()
        {
            Id = id, Generation = 1, FrontierId = frontierId,
            FrontierGeneration = 1, FilmId = filmId, FilmGeneration = 1,
            NextId = nextId, NextGeneration = nextId == 0 ? 0u : 1u,
            Flags = (uint)LatentFrontierFlags.Active
        };

        private static PressureManifoldHeaderGpu ActiveManifold() => new()
        {
            Id = 1, Generation = 1, ChunkId = 1,
            Flags = (uint)(PressureManifoldFlags.Active |
                PressureManifoldFlags.Closed),
            MembershipStart = 1, MembershipCount = 1,
            FrontierStart = 1, FrontierCount = 4
        };

        private static FilmMembershipGpu ActiveMembership() => new()
        {
            FilmId = 1, FilmGeneration = 1, ManifoldId = 1,
            ManifoldGeneration = 1, FirstFrontier = 1, FrontierCount = 4,
            Flags = (uint)(FilmMembershipFlags.Active |
                FilmMembershipFlags.Measured), Revision = 1
        };

        private static LatentFrontierSegmentGpu[] OrderedFrontierLoop()
        {
            Vector4[] edges =
            {
                new(0, 0, 1, 0), new(1, 0, 1, 1),
                new(1, 1, 0, 1), new(0, 1, 0, 0)
            };
            var result = new LatentFrontierSegmentGpu[4];
            for (uint i = 0; i < 4; i++)
            {
                result[i] = new LatentFrontierSegmentGpu
                {
                    Id = i + 1, Generation = 1, ManifoldId = 1,
                    ManifoldGeneration = 1, FilmId = 1, FilmGeneration = 1,
                    NextId = (i + 1) % 4 + 1, NextGeneration = 1,
                    PreviousId = (i + 3) % 4 + 1, PreviousGeneration = 1,
                    Flags = (uint)(LatentFrontierFlags.Active |
                        LatentFrontierFlags.Outer |
                        LatentFrontierFlags.Ordered),
                    Revision = 1, Uv01 = edges[i], Sigma = 0.002f,
                    Support = 1, Confidence = 1
                };
            }
            return result;
        }

        private static ManifoldFrontierIncidenceGpu[] OrderedFrontierIncidences()
        {
            var result = new ManifoldFrontierIncidenceGpu[4];
            for (uint i = 0; i < 4; i++)
            {
                result[i] = new ManifoldFrontierIncidenceGpu
                {
                    Id = i + 1, Generation = 1, FrontierId = i + 1,
                    FrontierGeneration = 1, FilmId = 1, FilmGeneration = 1,
                    NextId = i < 3 ? i + 2 : 0,
                    NextGeneration = i < 3 ? 1u : 0u,
                    Flags = (uint)LatentFrontierFlags.Active
                };
            }
            return result;
        }

        private static void BindValidation(ComputeShader shader, int kernel,
            GraphicsBuffer films, GraphicsBuffer filmAllocator,
            GraphicsBuffer activeFilms, GraphicsBuffer manifolds,
            GraphicsBuffer memberships, GraphicsBuffer links,
            GraphicsBuffer linkIncidences, GraphicsBuffer frontiers,
            GraphicsBuffer frontierIncidences, GraphicsBuffer manifoldAllocator,
            GraphicsBuffer current, GraphicsBuffer diagnostics,
            int filmCapacity = 1, int linkCapacity = 1,
            int frontierCapacity = 4)
        {
            shader.SetInt("_FilmCapacity", filmCapacity);
            shader.SetInt("_ManifoldCapacity", 1);
            shader.SetInt("_LinkCapacity", linkCapacity);
            shader.SetInt("_FrontierCapacity", frontierCapacity);
            shader.SetBuffer(kernel, "_FilmHeaders", films);
            shader.SetBuffer(kernel, "_FilmAllocator", filmAllocator);
            shader.SetBuffer(kernel, "_ActiveFilmIndices", activeFilms);
            shader.SetBuffer(kernel, "_ManifoldHeaders", manifolds);
            shader.SetBuffer(kernel, "_FilmMemberships", memberships);
            shader.SetBuffer(kernel, "_ManifoldLinks", links);
            shader.SetBuffer(kernel, "_ManifoldLinkIncidences", linkIncidences);
            shader.SetBuffer(kernel, "_LatentFrontiers", frontiers);
            shader.SetBuffer(kernel, "_ManifoldFrontierIncidences",
                frontierIncidences);
            shader.SetBuffer(kernel, "_ManifoldAllocator", manifoldAllocator);
            shader.SetBuffer(kernel, "_CurrentManifold", current);
            shader.SetBuffer(kernel, "_ManifoldDiagnostics", diagnostics);
        }

        private static void BindSplitTopology(ComputeShader shader, int kernel,
            int filmCapacity, int linkCapacity, int frontierCapacity,
            GraphicsBuffer films, GraphicsBuffer memberships,
            GraphicsBuffer links, GraphicsBuffer linkIncidences,
            GraphicsBuffer frontiers, GraphicsBuffer frontierIncidences,
            GraphicsBuffer manifoldAllocator, GraphicsBuffer diagnostics,
            GraphicsBuffer splitRecords, GraphicsBuffer adaptState)
        {
            shader.SetInt("_FilmCapacity", filmCapacity);
            shader.SetInt("_ManifoldCapacity", 1);
            shader.SetInt("_LinkCapacity", linkCapacity);
            shader.SetInt("_FrontierCapacity", frontierCapacity);
            shader.SetBuffer(kernel, "_FilmHeaders", films);
            shader.SetBuffer(kernel, "_FilmHeadersRead", films);
            shader.SetBuffer(kernel, "_FilmMemberships", memberships);
            shader.SetBuffer(kernel, "_ManifoldLinks", links);
            shader.SetBuffer(kernel, "_ManifoldLinkIncidences", linkIncidences);
            shader.SetBuffer(kernel, "_LatentFrontiers", frontiers);
            shader.SetBuffer(kernel, "_ManifoldFrontierIncidences",
                frontierIncidences);
            shader.SetBuffer(kernel, "_ManifoldAllocator", manifoldAllocator);
            shader.SetBuffer(kernel, "_ManifoldDiagnostics", diagnostics);
            shader.SetBuffer(kernel, "_SplitRecords", splitRecords);
            shader.SetBuffer(kernel, "_AdaptState", adaptState);
        }
    }
}
