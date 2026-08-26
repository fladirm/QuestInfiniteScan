using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Genesis.RoomScan.SigmaPrism;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class SigmaNativeFrameTests
    {
        [Test]
        public void LiveGraphIsOneBoundedNativeCloseAndLegacyAbiIsAbsent()
        {
            Assert.That(SigmaNativeFrameGraph.HotDispatchCount, Is.EqualTo(9));

            ComputeShader frame = LoadShader("SigmaNativeFrame");
            ComputeShader query = LoadShader("SigmaNativeQuery");
            ComputeShader contract = LoadShader("SigmaNativeContract");
            foreach (string kernel in new[]
            {
                "BuildNativeObservation", "PrepareNativeRevision",
                "PrepareNativePage", "ScatterNativeState",
                "CloseAndPublishNativeRevision",
            })
                Assert.That(frame.HasKernel(kernel), Is.True, kernel);
            Assert.That(query.HasKernel("EvaluateNativeRelation"), Is.True);
            Assert.That(contract.HasKernel("ContractNativeQuery"), Is.True);

            string[] graphGuids = AssetDatabase.FindAssets(
                "SigmaNativeFrameGraph t:MonoScript");
            Assert.That(graphGuids, Has.Length.EqualTo(1));
            string graphSource = File.ReadAllText(
                AssetDatabase.GUIDToAssetPath(graphGuids[0]));
            Assert.That(Count(graphSource, "command.DispatchComputeProfiled("),
                Is.EqualTo(SigmaNativeFrameGraph.HotDispatchCount));
            Assert.That(graphSource, Does.Not.Contain("foreach (int relation"));
            Assert.That(graphSource, Does.Not.Contain("foreach (int page"));
            Assert.That(graphSource, Does.Not.Contain("foreach (int segment"));

            string[] controllerGuids = AssetDatabase.FindAssets(
                "SigmaInverseController t:MonoScript");
            Assert.That(controllerGuids, Has.Length.EqualTo(1));
            string controllerSource = File.ReadAllText(
                AssetDatabase.GUIDToAssetPath(controllerGuids[0]));
            Assert.That(controllerSource, Does.Not.Contain(
                "Queue<StereoRigFrameLease> _unresolvedEvidence"));
            Assert.That(controllerSource, Does.Contain(
                "SigmaExactConstraintJournal _constraintJournal"));
            Assert.That(controllerSource, Does.Not.Contain(
                "_freshCodeLeavesReadback"));
            Assert.That(controllerSource, Does.Not.Contain(
                "_completionReadbackPending"));
            Assert.That(controllerSource, Does.Not.Contain(
                "AsyncGPUReadback.Request("));
            Assert.That(controllerSource, Does.Contain(
                "SigmaNativeCompletionTransfer _completionTransfer"));
            string[] transferGuids = AssetDatabase.FindAssets(
                "SigmaExactConstraintJournal t:MonoScript");
            Assert.That(transferGuids, Has.Length.EqualTo(1));
            string transferSource = File.ReadAllText(
                AssetDatabase.GUIDToAssetPath(transferGuids[0]));
            Assert.That(Count(transferSource, "RequestAsyncReadback("),
                Is.EqualTo(1));
            Assert.That(Count(transferSource, "AsyncGPUReadback.Request("),
                Is.EqualTo(1),
                "Only sealed-batch and stopped partial-batch cold transfers exist.");
            Assert.That(transferSource, Does.Contain("RecordsPerBatch = 16"));

            foreach (string deleted in new[]
            {
                "SigmaFrameClosure", "SigmaFrameInverse", "SigmaFramePublish",
            })
                Assert.That(AssetDatabase.FindAssets(
                    $"{deleted} t:ComputeShader"), Is.Empty, deleted);

            Assembly runtime = typeof(SigmaGeneratedFrame).Assembly;
            foreach (string deleted in new[]
            {
                "SigmaFrameCandidateGpu", "SigmaPendingGaugeGpu",
                "SigmaDirtyEdgeGpu",
            })
                Assert.That(runtime.GetTypes().Any(value =>
                    value.Name == deleted), Is.False, deleted);
        }

        [Test]
        public void LiveScratchUsesCompletionJournalAsSolePackedBoundary()
        {
            using var resources = new SigmaNativeFrameResources(
                new Vector2Int(320, 320), 3);
            Assert.That(resources.TryLease(out int slot,
                out SigmaNativeFrameSlotResources native), Is.True);
            try
            {
                Assert.That(slot, Is.Zero);
                Assert.That(typeof(SigmaNativeFrameSlotResources).GetProperty(
                    "FreshObservationHeaders",
                    BindingFlags.Instance | BindingFlags.NonPublic), Is.Null);
                Assert.That(typeof(SigmaNativeFrameSlotResources).GetProperty(
                    "FreshRoomRays",
                    BindingFlags.Instance | BindingFlags.NonPublic), Is.Null);
                Assert.That(typeof(SigmaNativeFrameSlotResources).GetProperty(
                    "FreshCodeLeaves",
                    BindingFlags.Instance | BindingFlags.NonPublic), Is.Null);
                Assert.That(native.GaugeDelta.count, Is.EqualTo(
                    SigmaNativeFrameSlotResources.RepresentationDeltaCapacity));
                Assert.That(native.LocalityCertificateWords.count, Is.EqualTo(
                    (SigmaNativeFrameSlotResources.RepresentationDeltaCapacity + 1) *
                    SigmaNativeFrameSlotResources.CertificateWordCount));
                Assert.That(native.RelationInputs.count,
                    Is.EqualTo(SigmaNativeFrameSlotResources.RelationCapacity));
            }
            finally
            {
                resources.Release(slot);
            }
        }

        [Test]
        public void ProvenFreshAdmissionPublishesExactlyOneRoot()
        {
            ComputeShader frame = LoadShader("SigmaNativeFrame");
            int prepare = frame.FindKernel("PrepareNativeRevision");
            int clone = frame.FindKernel("PrepareNativePage");
            int scatter = frame.FindKernel("ScatterNativeState");
            int close = frame.FindKernel("CloseAndPublishNativeRevision");

            using var scratch = new SigmaNativeFrameSlotResources(0);
            using var carrierState = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                SigmaCarrier.PageLaneCount * 2, Marshal.SizeOf<UInt2>());
            using var carrierRepresentation = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                SigmaCarrier.SamplesPerPage * 2 *
                SigmaCarrier.RepresentationWordsPerSample,
                Marshal.SizeOf<UInt4>());
            using var metadata = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 2,
                SigmaCarrier.PageMetadataStride);
            using var dirty = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 2, sizeof(uint));
            using var readoutDirty = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 2, sizeof(uint));
            using var root = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 1, sizeof(uint));
            using var completionJournal = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 64, sizeof(uint) * 2);

            scratch.NativeFrame.SetData(new[]
            {
                new SigmaNativeFrameGpu
                {
                    Identity = U4(1u, 7u, 1u, 0u),
                    Disposition = U4(
                        (uint)SigmaNativeFrameDisposition.GpuOwned, 1u, 0u, 0u),
                },
            });
            scratch.Observation.SetData(new[]
            {
                new SigmaNativeObservationGpu
                {
                    Identity = U4(1u, 7u, 13u, 0x3fu),
                    Evidence = U4(11u, 12u, 13u, 14u),
                },
            });
            var states = new UInt2[scratch.States.count];
            states[SigmaS16.LaneCount] = new UInt2 { High = 0x00010000u };
            scratch.States.SetData(states);
            var branches = new UInt4[scratch.BranchHeaders.count];
            branches[SigmaNativeFrameSlotResources.LiveFreshBranchCount] =
                new UInt4
                {
                    X = (uint)SigmaFreshAdmissionStatus.Admitted,
                    Y = (uint)SigmaMerkabaRelationClass.NoRelation,
                    Z = 1u,
                };
            scratch.BranchHeaders.SetData(branches);
            var relations = new UInt4[scratch.RelationResults.count];
            relations[1] = new UInt4
            {
                X = (uint)SigmaMerkabaRelationClass.Regular,
                W = 1u,
            };
            scratch.RelationResults.SetData(relations);
            SetValidNextCertificate(scratch, 1u);
            carrierState.SetData(new UInt2[carrierState.count]);
            carrierRepresentation.SetData(
                new UInt4[carrierRepresentation.count]);
            metadata.SetData(new PageMeta[metadata.count]);
            dirty.SetData(new uint[2]);
            readoutDirty.SetData(new uint[2]);
            root.SetData(new uint[1]);

            foreach (int kernel in new[] { prepare, clone, scatter, close })
                BindPublication(frame, kernel, scratch, carrierState,
                    carrierRepresentation, metadata, dirty, readoutDirty, root,
                    completionJournal);
            frame.SetInt("_NativeRevision", 1);
            frame.SetInt("_NativeCalibrationEpoch", 7);
            frame.SetInt("_NativeTargetPageCapacity", 2);
            frame.Dispatch(prepare, 1, 1, 1);
            frame.Dispatch(clone, Math.Max(SigmaCarrier.PageLaneCount,
                SigmaCarrier.SamplesPerPage *
                SigmaCarrier.RepresentationWordsPerSample) / 256, 1, 1);
            frame.Dispatch(scatter, 1, 1, 1);
            frame.Dispatch(close, 1, 1, 1);

            uint[] published = { 0u };
            root.GetData(published);
            Assert.That(published[0], Is.EqualTo(1u));
            var terminal = new SigmaNativeFrameGpu[1];
            scratch.NativeFrame.GetData(terminal);
            Assert.That(terminal[0].Disposition.X,
                Is.EqualTo((uint)SigmaNativeFrameDisposition.Published));
            var firstLane = new UInt2[1];
            carrierState.GetData(firstLane, 0, 0, 1);
            Assert.That(firstLane[0].Low, Is.Zero);
            Assert.That(firstLane[0].High, Is.EqualTo(0x00010000u));
            var page = new PageMeta[1];
            metadata.GetData(page, 0, 0, 1);
            Assert.That(page[0].Generation, Is.EqualTo(1u));
            Assert.That(page[0].Revision, Is.EqualTo(1u));
        }

        [Test]
        public void FeasibleCurrentStateIsBytePreservedAndDoesNotClonePage()
        {
            ComputeShader frame = LoadShader("SigmaNativeFrame");
            int prepare = frame.FindKernel("PrepareNativeRevision");
            int clone = frame.FindKernel("PrepareNativePage");
            int scatter = frame.FindKernel("ScatterNativeState");
            int close = frame.FindKernel("CloseAndPublishNativeRevision");

            using var scratch = new SigmaNativeFrameSlotResources(0);
            using var carrierState = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                SigmaCarrier.PageLaneCount * 2, Marshal.SizeOf<UInt2>());
            using var carrierRepresentation = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                SigmaCarrier.SamplesPerPage * 2 *
                SigmaCarrier.RepresentationWordsPerSample,
                Marshal.SizeOf<UInt4>());
            using var metadata = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 2,
                SigmaCarrier.PageMetadataStride);
            using var dirty = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 2, sizeof(uint));
            using var readoutDirty = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 2, sizeof(uint));
            using var root = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 1, sizeof(uint));
            using var completionJournal = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 64, sizeof(uint) * 2);

            scratch.NativeFrame.SetData(new[]
            {
                new SigmaNativeFrameGpu
                {
                    Identity = U4(2u, 7u, 1u, 0u),
                    Disposition = U4(
                        (uint)SigmaNativeFrameDisposition.GpuOwned, 1u, 0u, 0u),
                    Evidence = U4(1u, 0u, 0u, 1u),
                    Publication = U4(1u, 0u, 1u, 0u),
                },
            });
            scratch.Observation.SetData(new[]
            {
                new SigmaNativeObservationGpu
                {
                    Identity = U4(2u, 7u, 13u, 0x7fu),
                    Evidence = U4(11u, 12u, 13u, 14u),
                },
            });
            var states = new UInt2[scratch.States.count];
            states[SigmaS16.LaneCount] = new UInt2 { High = 0x00010000u };
            states[3 * SigmaS16.LaneCount] =
                new UInt2 { High = 0x00010000u };
            scratch.States.SetData(states);
            var branches = new UInt4[scratch.BranchHeaders.count];
            branches[SigmaNativeFrameSlotResources.LiveFreshBranchCount] =
                new UInt4
                {
                    X = (uint)SigmaFreshAdmissionStatus.Admitted,
                    Y = (uint)SigmaMerkabaRelationClass.NoRelation,
                    Z = 1u,
                };
            scratch.BranchHeaders.SetData(branches);
            var relations = new UInt4[scratch.RelationResults.count];
            relations[1] = new UInt4
            {
                X = (uint)SigmaMerkabaRelationClass.Regular,
                W = 1u,
            };
            scratch.RelationResults.SetData(relations);
            SetEqualCertificates(scratch, 1u);
            scratch.Counters.SetData(new UInt4[scratch.Counters.count]);
            var carrier = new UInt2[carrierState.count];
            carrier[0] = new UInt2 { Low = 0x89abcdefu, High = 0x01234567u };
            carrierState.SetData(carrier);
            carrierRepresentation.SetData(
                new UInt4[carrierRepresentation.count]);
            metadata.SetData(new PageMeta[2]);
            dirty.SetData(new uint[2]);
            readoutDirty.SetData(new uint[2]);
            root.SetData(new[] { 1u });

            foreach (int kernel in new[] { prepare, clone, scatter, close })
                BindPublication(frame, kernel, scratch, carrierState,
                    carrierRepresentation, metadata, dirty, readoutDirty, root,
                    completionJournal);
            frame.SetInt("_NativeRevision", 2);
            frame.SetInt("_NativeCalibrationEpoch", 7);
            frame.SetInt("_NativeTargetPageCapacity", 2);
            frame.Dispatch(prepare, 1, 1, 1);
            frame.Dispatch(clone, Math.Max(SigmaCarrier.PageLaneCount,
                SigmaCarrier.SamplesPerPage *
                SigmaCarrier.RepresentationWordsPerSample) / 256, 1, 1);
            frame.Dispatch(scatter, 1, 1, 1);
            frame.Dispatch(close, 1, 1, 1);

            var terminal = new SigmaNativeFrameGpu[1];
            scratch.NativeFrame.GetData(terminal);
            Assert.That(terminal[0].Disposition.X,
                Is.EqualTo((uint)SigmaNativeFrameDisposition.NoChange));
            var published = new uint[1];
            root.GetData(published);
            Assert.That(published[0], Is.EqualTo(1u));
            var after = new UInt2[1];
            carrierState.GetData(after, 0, 0, 1);
            Assert.That(after[0].Low, Is.EqualTo(0x89abcdefu));
            Assert.That(after[0].High, Is.EqualTo(0x01234567u));
            var dirtyAfter = new uint[2];
            dirty.GetData(dirtyAfter);
            Assert.That(dirtyAfter, Is.All.Zero);
        }

        [Test]
        public void StereoStaticExclusionPublishesOnlyZEmptyAtTheSameGauge()
        {
            StaticExclusionSnapshot proven = RunStaticExclusion(proven: true);
            Assert.That(proven.Root, Is.EqualTo(2u));
            Assert.That(proven.Frame.Disposition.X,
                Is.EqualTo((uint)SigmaNativeFrameDisposition.Published));
            Assert.That(proven.Frame.Identity.W,
                Is.EqualTo((uint)SigmaNativeColdReason.StaticExclusion));
            Assert.That(proven.TargetState.All(word =>
                word.Low == 0u && word.High == 0u), Is.True,
                "Pass-through correction must publish exact ZEmpty.");
            CollectionAssert.AreEqual(proven.SourceGauge, proven.TargetGauge,
                "Static correction may not allocate or move the intrinsic gauge.");
            Assert.That(proven.Counters[0].X, Is.EqualTo(1u));
            Assert.That(proven.Counters[1].X, Is.Zero);
            Assert.That(proven.Counters[2].X, Is.EqualTo(1u));

            StaticExclusionSnapshot unproved = RunStaticExclusion(proven: false);
            Assert.That(unproved.Root, Is.EqualTo(1u));
            Assert.That(unproved.Frame.Disposition.X,
                Is.EqualTo((uint)SigmaNativeFrameDisposition.Unresolved));
            Assert.That(unproved.SourceState.Any(word =>
                word.Low != 0u || word.High != 0u), Is.True,
                "Unproved exclusion may not mutate the supported state.");
        }

        [Test]
        public void PreRootFaultLeavesStateGaugeCertificateAndRootUntouched()
        {
            ComputeShader frame = LoadShader("SigmaNativeFrame");
            int prepare = frame.FindKernel("PrepareNativeRevision");
            int clone = frame.FindKernel("PrepareNativePage");
            int scatter = frame.FindKernel("ScatterNativeState");
            int close = frame.FindKernel("CloseAndPublishNativeRevision");

            using var scratch = new SigmaNativeFrameSlotResources(0);
            using var carrierState = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                SigmaCarrier.PageLaneCount * 2, Marshal.SizeOf<UInt2>());
            using var carrierRepresentation = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                SigmaCarrier.SamplesPerPage * 2 *
                SigmaCarrier.RepresentationWordsPerSample,
                Marshal.SizeOf<UInt4>());
            using var metadata = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 2,
                SigmaCarrier.PageMetadataStride);
            using var dirty = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 2, sizeof(uint));
            using var readoutDirty = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 2, sizeof(uint));
            using var root = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 1, sizeof(uint));
            using var completionJournal = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 64, sizeof(uint) * 2);

            scratch.NativeFrame.SetData(new[]
            {
                new SigmaNativeFrameGpu
                {
                    Identity = U4(2u, 7u, 1u, 0u),
                    Disposition = U4(
                        (uint)SigmaNativeFrameDisposition.GpuOwned, 1u, 0u, 0u),
                    Evidence = U4(1u, 0u, 0u, 9u),
                    Publication = U4(1u, 0u, 1u, 0u),
                },
            });
            scratch.Observation.SetData(new[]
            {
                new SigmaNativeObservationGpu
                {
                    Identity = U4(2u, 7u, 13u, 0x7fu),
                    Evidence = U4(11u, 12u, 13u, 14u),
                },
            });
            var states = new UInt2[scratch.States.count];
            states[SigmaS16.LaneCount] = new UInt2 { High = 1u << 16 };
            states[3 * SigmaS16.LaneCount] =
                new UInt2 { High = 2u << 16 };
            scratch.States.SetData(states);
            var branches = new UInt4[scratch.BranchHeaders.count];
            branches[SigmaNativeFrameSlotResources.LiveFreshBranchCount] =
                new UInt4
                {
                    X = (uint)SigmaFreshAdmissionStatus.Admitted,
                    Y = (uint)SigmaMerkabaRelationClass.NoRelation,
                    Z = 1u,
                };
            scratch.BranchHeaders.SetData(branches);
            var relations = new UInt4[scratch.RelationResults.count];
            relations[1] = new UInt4
            {
                X = (uint)SigmaMerkabaRelationClass.Regular,
                W = 1u,
            };
            scratch.RelationResults.SetData(relations);
            SetEqualCertificates(scratch, 9u);

            var priorState = new UInt2[carrierState.count];
            priorState[0] = new UInt2
            {
                Low = 0x01234567u,
                High = 0x89abcdefu,
            };
            carrierState.SetData(priorState);
            var priorRepresentation = new UInt4[carrierRepresentation.count];
            priorRepresentation[0] = new UInt4
            {
                X = 0x10203040u, Y = 0x50607080u,
                Z = 0x90a0b0c0u, W = 0xd0e0f000u,
            };
            carrierRepresentation.SetData(priorRepresentation);
            var priorMetadata = new[]
            {
                new PageMeta
                {
                    Generation = 9u,
                    Revision = 1u,
                    CertificateCount = 1u,
                    GaugeGeneration = 4u,
                    CertificateGeneration = 9u,
                    RepresentationFlags = 3u,
                    ActiveSampleCount = 1u,
                },
                new PageMeta(),
            };
            metadata.SetData(priorMetadata);
            dirty.SetData(new uint[2]);
            readoutDirty.SetData(new uint[2]);
            root.SetData(new[] { 1u });

            foreach (int kernel in new[] { prepare, clone, scatter, close })
                BindPublication(frame, kernel, scratch, carrierState,
                    carrierRepresentation, metadata, dirty, readoutDirty, root,
                    completionJournal);
            frame.SetInt("_NativeRevision", 2);
            frame.SetInt("_NativeCalibrationEpoch", 7);
            // Source slot zero is valid, but its shadow target slot one is
            // deliberately outside this preflight capacity.
            frame.SetInt("_NativeTargetPageCapacity", 1);
            frame.Dispatch(prepare, 1, 1, 1);
            frame.Dispatch(clone, Math.Max(SigmaCarrier.PageLaneCount,
                SigmaCarrier.SamplesPerPage *
                SigmaCarrier.RepresentationWordsPerSample) / 256, 1, 1);
            frame.Dispatch(scatter, 1, 1, 1);
            frame.Dispatch(close, 1, 1, 1);

            var terminal = new SigmaNativeFrameGpu[1];
            scratch.NativeFrame.GetData(terminal);
            Assert.That(terminal[0].Disposition.X,
                Is.EqualTo((uint)SigmaNativeFrameDisposition.Faulted));
            var actualRoot = new uint[1];
            root.GetData(actualRoot);
            Assert.That(actualRoot[0], Is.EqualTo(1u));
            var actualState = new UInt2[1];
            carrierState.GetData(actualState, 0, 0, 1);
            Assert.That(actualState[0].Low, Is.EqualTo(priorState[0].Low));
            Assert.That(actualState[0].High, Is.EqualTo(priorState[0].High));
            var actualRepresentation = new UInt4[1];
            carrierRepresentation.GetData(actualRepresentation, 0, 0, 1);
            Assert.That(actualRepresentation[0],
                Is.EqualTo(priorRepresentation[0]));
            PageMeta actualMetadata = ReadPageMeta(metadata, 0u);
            Assert.That(actualMetadata.Generation,
                Is.EqualTo(priorMetadata[0].Generation));
            Assert.That(actualMetadata.CertificateGeneration,
                Is.EqualTo(priorMetadata[0].CertificateGeneration));
            var actualDirty = new uint[2];
            dirty.GetData(actualDirty);
            Assert.That(actualDirty, Is.All.Zero);
        }

        [Test]
        public void ExactConstraintJournalMinimizesWithoutBranchIdentity()
        {
            SigmaExactConstraintRecord broad = ConstraintRecord(1u, 11u,
                -8L, 8L);
            SigmaExactConstraintRecord narrow = ConstraintRecord(2u, 11u,
                -2L, 3L);
            var journal = new SigmaExactConstraintJournal();
            Assert.That(journal.Add(broad),
                Is.EqualTo(SigmaConstraintAdmission.Added));
            Assert.That(journal.Add(narrow),
                Is.EqualTo(SigmaConstraintAdmission.ReplacedWeaker));
            for (uint revision = 3u; revision < 10003u; ++revision)
                Assert.That(journal.Add(ConstraintRecord(revision, 11u,
                    -2L, 3L)),
                    Is.EqualTo(SigmaConstraintAdmission.DuplicateOrWeaker));
            Assert.That(journal.Count, Is.EqualTo(1));
            byte[] canonical = journal.EncodeCanonical();
            Assert.That(canonical.Length, Is.EqualTo(12 + 8 + 272));
            CollectionAssert.AreEqual(canonical,
                SigmaExactConstraintJournal.DecodeCanonical(canonical)
                    .EncodeCanonical());

            var leftFirst = new SigmaExactConstraintJournal();
            var rightFirst = new SigmaExactConstraintJournal();
            SigmaExactConstraintRecord other = ConstraintRecord(9u, 99u,
                -2L, 3L);
            leftFirst.Add(narrow);
            leftFirst.Add(other);
            rightFirst.Add(other);
            rightFirst.Add(narrow);
            CollectionAssert.AreEqual(leftFirst.EncodeCanonical(),
                rightFirst.EncodeCanonical());

            SigmaExactConstraintRecord differentProofRole = ConstraintRecord(
                10u, 11u, -2L, 3L, provenanceRole: 2u);
            Assert.That(journal.Add(differentProofRole),
                Is.EqualTo(SigmaConstraintAdmission.Added),
                "Different exact proof roles may not be minimized together.");
            Assert.That(journal.Count, Is.EqualTo(2));
        }

        [Test]
        public void MinimizedConstraintJournalPersistsAtomicallyOffFrameThread()
        {
            string directory = Path.Combine(Path.GetTempPath(),
                "sigma-n4-journal-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "constraints.sjc");
            try
            {
                var expected = new SigmaExactConstraintJournal();
                expected.Add(ConstraintRecord(1u, 11u, -8L, 8L));
                expected.Add(ConstraintRecord(2u, 11u, -2L, 3L));
                byte[] canonical = expected.EncodeCanonical();
                using (var store = new SigmaExactConstraintStore(path))
                    store.Stage(expected);
                Assert.That(File.Exists(path), Is.True);
                Assert.That(File.Exists(path + ".next"), Is.False);
                using var reopened = new SigmaExactConstraintStore(path);
                CollectionAssert.AreEqual(canonical,
                    reopened.Load().EncodeCanonical());
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
        }

        [Test]
        public void CompletionTransferBatchesWithoutBecomingIngressOwnership()
        {
            using var transfer = new SigmaNativeCompletionTransfer();
            GraphicsBuffer first = null;
            for (int index = 0;
                index < SigmaNativeCompletionTransfer.RecordsPerBatch; ++index)
            {
                SigmaNativeCompletionTransfer.Reservation reservation =
                    transfer.Reserve();
                first ??= reservation.Buffer;
                Assert.That(reservation.Buffer, Is.SameAs(first));
                Assert.That(reservation.RecordIndex, Is.EqualTo(index));
                Assert.That(reservation.SealsBatch, Is.EqualTo(index ==
                    SigmaNativeCompletionTransfer.RecordsPerBatch - 1));
            }
            SigmaNativeCompletionTransfer.Reservation next = transfer.Reserve();
            Assert.That(next.Buffer, Is.Not.SameAs(first),
                "A sealed cold batch may not be overwritten by hot ingress.");
            Assert.That(next.RecordIndex, Is.Zero);
            transfer.Cancel(next);
        }

        [Test]
        public void RepeatedRefinementNormalizesIndependentlyOfSplitOrder()
        {
            RefinementSnapshot leftFirst = RunRefinementSequence(new[]
            {
                new RefinementStep(new GaugeKey(0L, 0L, 0u), 2u, 100u),
                new RefinementStep(new GaugeKey(0L, 1L, 1u), 1u, 200u),
                new RefinementStep(new GaugeKey(8L, 0L, 0u), 3u, 300u),
            });
            RefinementSnapshot rightFirst = RunRefinementSequence(new[]
            {
                new RefinementStep(new GaugeKey(8L, 0L, 0u), 3u, 300u),
                new RefinementStep(new GaugeKey(0L, 0L, 0u), 2u, 100u),
                new RefinementStep(new GaugeKey(0L, 1L, 1u), 1u, 200u),
            });

            Assert.That(leftFirst.ActiveSampleCount, Is.EqualTo(11u));
            Assert.That(leftFirst.CertificateCount,
                Is.EqualTo(leftFirst.ActiveSampleCount));
            Assert.That(leftFirst.Root, Is.EqualTo(4u));
            CollectionAssert.AreEqual(leftFirst.StateWords,
                rightFirst.StateWords,
                "state bytes changed with refinement discovery order");
            CollectionAssert.AreEqual(leftFirst.RepresentationWords,
                rightFirst.RepresentationWords,
                "chi/kappa/certificate bytes changed with split order");
            CollectionAssert.AreEqual(leftFirst.GaugeKeys,
                rightFirst.GaugeKeys,
                "normalized gauge order changed with split order");
            Assert.That(leftFirst.GaugeKeys.Distinct().Count(),
                Is.EqualTo(leftFirst.GaugeKeys.Length));
            Assert.That(leftFirst.GaugeKeys, Is.Ordered.Using<GaugeKey>(
                GaugeKeyComparer.Instance));
        }

        [Test]
        public void RefinementCopiesTheCompletePriorBeforeAddingDetail()
        {
            RefinementSnapshot copy = RunRefinementSequence(new[]
            {
                new RefinementStep(new GaugeKey(0L, 0L, 0u), 0u, 100u),
            }, verifyFirstPreScatterCopy: true);
            Assert.That(copy.ActiveSampleCount, Is.EqualTo(5u));
            int parentCopies = 0;
            int refinedChildren = 0;
            int untouchedLocalities = 0;
            for (int sample = 0; sample < copy.GaugeKeys.Length; ++sample)
            {
                GaugeKey gauge = copy.GaugeKeys[sample];
                uint value = copy.StateWords[
                    sample * SigmaS16.LaneCount * 2 + 1] >> 16;
                parentCopies += value == 10u ? 1 : 0;
                refinedChildren += value == 100u ? 1 : 0;
                untouchedLocalities += value == 20u ? 1 : 0;
                for (int lane = 1; lane < SigmaS16.LaneCount; ++lane)
                {
                    Assert.That(copy.StateWords[
                            sample * SigmaS16.LaneCount * 2 + lane * 2],
                        Is.Zero, $"{gauge} lane {lane} low");
                    Assert.That(copy.StateWords[
                            sample * SigmaS16.LaneCount * 2 + lane * 2 + 1],
                        Is.Zero, $"{gauge} lane {lane} high");
                }
                int representationBase = sample *
                    SigmaCarrier.RepresentationWordsPerSample * 4;
                Assert.That(copy.RepresentationWords[
                        representationBase + 4 * 4],
                    Is.EqualTo(11u), $"{gauge} evidence receipt");
                Assert.That(copy.RepresentationWords[
                        representationBase + 4 * 4 + 1],
                    Is.EqualTo(12u), $"{gauge} evidence receipt");
            }
            Assert.That(parentCopies, Is.EqualTo(3));
            Assert.That(refinedChildren, Is.EqualTo(1));
            Assert.That(untouchedLocalities, Is.EqualTo(1));
        }

        private static ComputeShader LoadShader(string name)
        {
            string[] guids = AssetDatabase.FindAssets($"{name} t:ComputeShader")
                .Where(guid => string.Equals(Path.GetFileNameWithoutExtension(
                    AssetDatabase.GUIDToAssetPath(guid)), name,
                    StringComparison.Ordinal)).ToArray();
            Assert.That(guids, Has.Length.EqualTo(1), name);
            ComputeShader shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                AssetDatabase.GUIDToAssetPath(guids[0]));
            Assert.That(shader, Is.Not.Null, name);
            return shader;
        }

        private static StaticExclusionSnapshot RunStaticExclusion(bool proven)
        {
            ComputeShader frame = LoadShader("SigmaNativeFrame");
            int prepare = frame.FindKernel("PrepareNativeRevision");
            int clone = frame.FindKernel("PrepareNativePage");
            int scatter = frame.FindKernel("ScatterNativeState");
            int close = frame.FindKernel("CloseAndPublishNativeRevision");
            using var scratch = new SigmaNativeFrameSlotResources(0);
            using var state = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                SigmaCarrier.PageLaneCount * 2, Marshal.SizeOf<UInt2>());
            using var representation = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                SigmaCarrier.SamplesPerPage * 2 *
                SigmaCarrier.RepresentationWordsPerSample,
                Marshal.SizeOf<UInt4>());
            using var metadata = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 2,
                SigmaCarrier.PageMetadataStride);
            using var dirty = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 2, sizeof(uint));
            using var readoutDirty = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 2, sizeof(uint));
            using var root = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 1, sizeof(uint));
            using var completionJournal = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 64, sizeof(uint) * 2);

            scratch.NativeFrame.SetData(new[]
            {
                new SigmaNativeFrameGpu
                {
                    Identity = U4(2u, 7u, 1u, 0u),
                    Disposition = U4(
                        (uint)SigmaNativeFrameDisposition.GpuOwned, 1u, 0u, 0u),
                    Evidence = U4(1u, 0u, 0u, 1u),
                    Publication = U4(1u, 0u, 1u, 0u),
                },
            });
            scratch.Observation.SetData(new[]
            {
                new SigmaNativeObservationGpu
                {
                    Identity = U4(2u, 7u, 13u, 0x7fu),
                    Footprint = U4(17u, 0u, 23u, 0u),
                    Evidence = U4(11u, 12u, 13u, 14u),
                },
            });
            var scratchStates = new UInt2[scratch.States.count];
            scratchStates[3 * SigmaS16.LaneCount] =
                new UInt2 { High = 3u << 16 };
            scratchStates[SigmaS16.LaneCount] =
                new UInt2 { High = 5u << 16 };
            scratch.States.SetData(scratchStates);
            var branches = new UInt4[scratch.BranchHeaders.count];
            branches[SigmaNativeFrameSlotResources.LiveFreshBranchCount] =
                new UInt4
                {
                    X = (uint)SigmaFreshAdmissionStatus.Admitted,
                    Y = (uint)SigmaMerkabaRelationClass.NoRelation,
                    Z = 1u,
                    W = proven
                        ? (uint)SigmaNativeColdReason.StaticExclusion : 0u,
                };
            scratch.BranchHeaders.SetData(branches);
            var relations = new UInt4[scratch.RelationResults.count];
            relations[1] = new UInt4
            {
                X = (uint)SigmaMerkabaRelationClass.NoRelation,
                W = 0u,
            };
            scratch.RelationResults.SetData(relations);
            SetEqualCertificates(scratch, 1u);

            var carrierState = new UInt2[state.count];
            carrierState[0] = new UInt2 { High = 3u << 16 };
            state.SetData(carrierState);
            var carrierRepresentation = new UInt4[representation.count];
            WriteRepresentationCell(carrierRepresentation, 0, 0,
                new GaugeKey(17L, 23L, 0u), 1u);
            representation.SetData(carrierRepresentation);
            metadata.SetData(new[]
            {
                new PageMeta
                {
                    Generation = 1u,
                    Revision = 1u,
                    CertificateCount = 1u,
                    GaugeGeneration = 1u,
                    CertificateGeneration = 1u,
                    RepresentationFlags = 3u,
                    ActiveSampleCount = 1u,
                },
                new PageMeta(),
            });
            dirty.SetData(new uint[2]);
            readoutDirty.SetData(new uint[2]);
            root.SetData(new[] { 1u });
            foreach (int kernel in new[] { prepare, clone, scatter, close })
                BindPublication(frame, kernel, scratch, state, representation,
                    metadata, dirty, readoutDirty, root, completionJournal);
            frame.SetInt("_NativeRevision", 2);
            frame.SetInt("_NativeCalibrationEpoch", 7);
            frame.SetInt("_NativeTargetPageCapacity", 2);
            frame.Dispatch(prepare, 1, 1, 1);
            frame.Dispatch(clone, Math.Max(SigmaCarrier.PageLaneCount,
                SigmaCarrier.SamplesPerPage *
                SigmaCarrier.RepresentationWordsPerSample) / 256, 1, 1);
            frame.Dispatch(scatter, 1, 1, 1);
            frame.Dispatch(close, 1, 1, 1);

            var terminal = new SigmaNativeFrameGpu[1];
            scratch.NativeFrame.GetData(terminal);
            var published = new uint[1];
            root.GetData(published);
            var sourceState = new UInt2[SigmaS16.LaneCount];
            var targetState = new UInt2[SigmaS16.LaneCount];
            state.GetData(sourceState, 0, 0, SigmaS16.LaneCount);
            state.GetData(targetState, 0, SigmaCarrier.PageLaneCount,
                SigmaS16.LaneCount);
            var sourceGauge = new UInt4[2];
            var targetGauge = new UInt4[2];
            representation.GetData(sourceGauge, 0, 0, 2);
            representation.GetData(targetGauge, 0,
                SigmaCarrier.SamplesPerPage *
                SigmaCarrier.RepresentationWordsPerSample, 2);
            var counters = new UInt4[scratch.Counters.count];
            scratch.Counters.GetData(counters);
            return new StaticExclusionSnapshot(terminal[0], published[0],
                sourceState, targetState, sourceGauge, targetGauge, counters);
        }

        private static int Count(string source, string token)
        {
            int count = 0;
            for (int offset = 0; (offset = source.IndexOf(token, offset,
                StringComparison.Ordinal)) >= 0; offset += token.Length)
                count++;
            return count;
        }

        private static void BindPublication(ComputeShader shader, int kernel,
            SigmaNativeFrameSlotResources scratch, GraphicsBuffer carrierState,
            GraphicsBuffer carrierRepresentation, GraphicsBuffer metadata,
            GraphicsBuffer dirty,
            GraphicsBuffer readoutDirty, GraphicsBuffer root,
            GraphicsBuffer completionJournal)
        {
            shader.SetBuffer(kernel, "_NativeFrames", scratch.NativeFrame);
            shader.SetBuffer(kernel, "_NativeObservations", scratch.Observation);
            shader.SetBuffer(kernel, "_NativeStates", scratch.States);
            shader.SetBuffer(kernel, "_NativeBranchHeaders",
                scratch.BranchHeaders);
            shader.SetBuffer(kernel, "_NativeRelationResults",
                scratch.RelationResults);
            shader.SetBuffer(kernel, "_NativeRelationFactors",
                scratch.RelationFactors);
            shader.SetBuffer(kernel, "_NativeRelationHashes",
                scratch.RelationHashes);
            shader.SetBuffer(kernel, "_NativeStateDeltas", scratch.StateDelta);
            shader.SetBuffer(kernel, "_NativeGaugeDeltas", scratch.GaugeDelta);
            shader.SetBuffer(kernel, "_NativeLocalityCertificateWords",
                scratch.LocalityCertificateWords);
            shader.SetBuffer(kernel, "_NativeUnresolved", scratch.Unresolved);
            shader.SetBuffer(kernel, "_NativeRevisions", scratch.Revisions);
            shader.SetBuffer(kernel, "_NativeCounters", scratch.Counters);
            shader.SetBuffer(kernel, "_NativeCompletionJournal",
                completionJournal);
            shader.SetBuffer(kernel, "_NativeSourceCarrierState", carrierState);
            shader.SetBuffer(kernel, "_NativeSourceCarrierRepresentation",
                carrierRepresentation);
            shader.SetBuffer(kernel, "_NativeSourcePageMetadata", metadata);
            shader.SetBuffer(kernel, "_NativeSourcePublicationRoot", root);
            shader.SetBuffer(kernel, "_TargetCarrierState", carrierState);
            shader.SetBuffer(kernel, "_TargetCarrierRepresentation",
                carrierRepresentation);
            shader.SetBuffer(kernel, "_TargetPageMetadata", metadata);
            shader.SetBuffer(kernel, "_TargetDirtyFlags", dirty);
            shader.SetBuffer(kernel, "_TargetReadoutDirtyFlags", readoutDirty);
            shader.SetBuffer(kernel, "_PublishedRevisionRoot", root);
            shader.SetInt("_NativeCompletionRecordIndex", 0);
            shader.SetBuffer(kernel, "_NativeCloseObservations",
                scratch.Observation);
            shader.SetBuffer(kernel, "_NativeCloseStateDeltas",
                scratch.StateDelta);
            shader.SetBuffer(kernel, "_NativeCloseLocalityCertificateWords",
                scratch.LocalityCertificateWords);
            shader.SetBuffer(kernel, "_NativeCloseCounters", scratch.Counters);
            shader.SetBuffer(kernel, "_NativePrepareObservations",
                scratch.Observation);
            shader.SetBuffer(kernel, "_NativePrepareStates", scratch.States);
        }

        private static void SetValidNextCertificate(
            SigmaNativeFrameSlotResources scratch, uint generation)
        {
            var words = new UInt4[scratch.LocalityCertificateWords.count];
            words[SigmaNativeFrameSlotResources.CertificateWordCount] =
                new UInt4
                {
                    X = (uint)(SigmaNativeCertificateFlags.Valid |
                        SigmaNativeCertificateFlags.Directional |
                        SigmaNativeCertificateFlags.Minimized),
                    Y = 1u,
                    Z = 1u,
                    W = generation,
                };
            scratch.LocalityCertificateWords.SetData(words);
        }

        private static void SetEqualCertificates(
            SigmaNativeFrameSlotResources scratch, uint generation)
        {
            var words = new UInt4[scratch.LocalityCertificateWords.count];
            words[0] = new UInt4
            {
                X = (uint)(SigmaNativeCertificateFlags.Valid |
                    SigmaNativeCertificateFlags.Directional |
                    SigmaNativeCertificateFlags.Minimized),
                Y = 1u,
                Z = 1u,
                W = generation,
            };
            words[SigmaNativeFrameSlotResources.CertificateWordCount] = words[0];
            words[3] = new UInt4
            {
                Y = (uint)SigmaMerkabaRelationClass.Regular,
            };
            words[SigmaNativeFrameSlotResources.CertificateWordCount + 3] =
                words[3];
            scratch.LocalityCertificateWords.SetData(words);
        }

        private static SigmaExactConstraintRecord ConstraintRecord(uint revision,
            uint independence, long lower, long upper,
            uint provenanceRole = 1u)
        {
            var constraint = new SigmaUnresolvedConstraintGpu
            {
                Observation = U4(revision, 7u, 13u, 0x3fu),
                Relation = U4(6u, 1u, 2u, 3u),
                Evidence = U4(independence, independence + 1u,
                    independence + 2u, independence + 3u),
                Provenance = U4(provenanceRole, 0u, revision, 7u),
            };
            var headers = new[]
            {
                U4(0x3fu, revision, 7u, 13u),
                U4(1u, 1u, 7u, 17u),
            };
            var rays = new SigmaFrameUInt2Gpu[6];
            for (int index = 0; index < rays.Length; ++index)
                rays[index] = Q2(index + 1L);
            var leaves = new SigmaFrameUInt2Gpu[16];
            for (int leaf = 0; leaf < 8; ++leaf)
            {
                leaves[leaf * 2] = Q2(lower);
                leaves[leaf * 2 + 1] = Q2(upper);
            }
            return new SigmaExactConstraintRecord(constraint, headers, rays,
                leaves);
        }

        private static RefinementSnapshot RunRefinementSequence(
            RefinementStep[] steps, bool verifyFirstPreScatterCopy = false)
        {
            ComputeShader frame = LoadShader("SigmaNativeFrame");
            int prepare = frame.FindKernel("PrepareNativeRevision");
            int clone = frame.FindKernel("PrepareNativePage");
            int scatter = frame.FindKernel("ScatterNativeState");
            int close = frame.FindKernel("CloseAndPublishNativeRevision");

            using var scratch = new SigmaNativeFrameSlotResources(0);
            using var carrierState = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                SigmaCarrier.PageLaneCount * 2, Marshal.SizeOf<UInt2>());
            using var carrierRepresentation = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                SigmaCarrier.SamplesPerPage * 2 *
                SigmaCarrier.RepresentationWordsPerSample,
                Marshal.SizeOf<UInt4>());
            using var metadata = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 2,
                SigmaCarrier.PageMetadataStride);
            using var dirty = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 2, sizeof(uint));
            using var readoutDirty = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 2, sizeof(uint));
            using var root = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 1, sizeof(uint));
            using var completionJournal = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 64, sizeof(uint) * 2);

            var state = new UInt2[carrierState.count];
            state[0].High = 10u << 16;
            state[SigmaS16.LaneCount].High = 20u << 16;
            carrierState.SetData(state);
            var representation = new UInt4[carrierRepresentation.count];
            WriteRepresentationCell(representation, 0, 0,
                new GaugeKey(0L, 0L, 0u), 1u);
            WriteRepresentationCell(representation, 0, 1,
                new GaugeKey(8L, 0L, 0u), 1u);
            carrierRepresentation.SetData(representation);
            metadata.SetData(new[]
            {
                new PageMeta
                {
                    Generation = 1u,
                    Revision = 1u,
                    CertificateCount = 2u,
                    GaugeGeneration = 1u,
                    CertificateGeneration = 1u,
                    ActiveSampleCount = 2u,
                },
                new PageMeta(),
            });
            dirty.SetData(new uint[2]);
            readoutDirty.SetData(new uint[2]);
            root.SetData(new[] { 1u });

            foreach (int kernel in new[] { prepare, clone, scatter, close })
                BindPublication(frame, kernel, scratch, carrierState,
                    carrierRepresentation, metadata, dirty, readoutDirty,
                    root, completionJournal);
            frame.SetInt("_NativeCalibrationEpoch", 7);
            frame.SetInt("_NativeTargetPageCapacity", 2);

            uint sourceSlot = 0u;
            uint revision = 1u;
            foreach (RefinementStep step in steps)
            {
                revision++;
                PageMeta sourceMeta = ReadPageMeta(metadata, sourceSlot);
                int parentSample = FindGaugeSample(carrierRepresentation,
                    sourceSlot, sourceMeta.ActiveSampleCount, step.Parent);
                UInt2[] priorState = ReadState(carrierState, sourceSlot,
                    parentSample);
                UInt4[] priorCertificate = ReadCertificate(
                    carrierRepresentation, sourceSlot, parentSample);
                PrepareRefinementScratch(scratch, revision, sourceSlot,
                    (uint)parentSample, sourceMeta.Generation, step,
                    priorState, priorCertificate);

                frame.SetInt("_NativeRevision", (int)revision);
                frame.Dispatch(prepare, 1, 1, 1);
                frame.Dispatch(clone, Math.Max(SigmaCarrier.PageLaneCount,
                    SigmaCarrier.SamplesPerPage *
                    SigmaCarrier.RepresentationWordsPerSample) / 256, 1, 1);
                if (verifyFirstPreScatterCopy && revision == 2u)
                {
                    var stagedDeltas = new SigmaNativeStateDeltaGpu[
                        SigmaNativeFrameSlotResources.RepresentationDeltaCapacity];
                    scratch.StateDelta.GetData(stagedDeltas);
                    for (int child = 0; child < stagedDeltas.Length; ++child)
                    {
                        int stagedSample = checked((int)
                            stagedDeltas[child].Generation.Y);
                        UInt2[] stagedState = ReadState(carrierState,
                            sourceSlot ^ 1u, stagedSample);
                        CollectionAssert.AreEqual(priorState, stagedState,
                            $"child {child} changed before information scatter");
                        UInt4[] stagedCertificate = ReadCertificate(
                            carrierRepresentation, sourceSlot ^ 1u,
                            stagedSample);
                        CollectionAssert.AreEqual(priorCertificate,
                            stagedCertificate,
                            $"child {child} proof changed before scatter");
                    }
                    var stagedRoot = new uint[1];
                    root.GetData(stagedRoot);
                    Assert.That(stagedRoot[0], Is.EqualTo(revision - 1u),
                        "Prepared representation became visible before scatter/close.");
                }
                frame.Dispatch(scatter, 1, 1, 1);
                frame.Dispatch(close, 1, 1, 1);

                var terminal = new SigmaNativeFrameGpu[1];
                scratch.NativeFrame.GetData(terminal);
                Assert.That(terminal[0].Disposition.X,
                    Is.EqualTo((uint)SigmaNativeFrameDisposition.Published),
                    $"refinement revision {revision}, reason=" +
                    terminal[0].Disposition.W);
                var published = new uint[1];
                root.GetData(published);
                Assert.That(published[0], Is.EqualTo(revision));
                sourceSlot ^= 1u;
            }

            PageMeta finalMeta = ReadPageMeta(metadata, sourceSlot);
            int stateWordCount = checked((int)finalMeta.ActiveSampleCount *
                SigmaS16.LaneCount);
            var finalState = new UInt2[stateWordCount];
            carrierState.GetData(finalState, 0,
                checked((int)sourceSlot * SigmaCarrier.PageLaneCount),
                stateWordCount);
            int representationWordCount = checked(
                (int)finalMeta.ActiveSampleCount *
                SigmaCarrier.RepresentationWordsPerSample);
            var finalRepresentation = new UInt4[representationWordCount];
            carrierRepresentation.GetData(finalRepresentation, 0,
                checked((int)sourceSlot * SigmaCarrier.SamplesPerPage *
                    SigmaCarrier.RepresentationWordsPerSample),
                representationWordCount);
            var gaugeKeys = new GaugeKey[
                checked((int)finalMeta.ActiveSampleCount)];
            for (int sample = 0; sample < gaugeKeys.Length; ++sample)
                gaugeKeys[sample] = GaugeKey.From(
                    finalRepresentation[sample *
                        SigmaCarrier.RepresentationWordsPerSample],
                    finalRepresentation[sample *
                        SigmaCarrier.RepresentationWordsPerSample + 1].X);
            return new RefinementSnapshot(revision,
                finalMeta.ActiveSampleCount, finalMeta.CertificateCount,
                Flatten(finalState), Flatten(finalRepresentation), gaugeKeys);
        }

        private static void PrepareRefinementScratch(
            SigmaNativeFrameSlotResources scratch, uint revision,
            uint sourceSlot, uint parentSample, uint generation,
            RefinementStep step, UInt2[] priorState,
            UInt4[] priorCertificate)
        {
            scratch.NativeFrame.SetData(new[]
            {
                new SigmaNativeFrameGpu
                {
                    Identity = U4(revision, 7u, 1u, 0u),
                    Disposition = U4(
                        (uint)SigmaNativeFrameDisposition.GpuOwned, 1u, 0u, 0u),
                    Evidence = U4(1u, sourceSlot, parentSample, generation),
                    Publication = U4(revision - 1u, 0u, revision - 1u, 0u),
                },
            });
            scratch.Observation.SetData(new[]
            {
                new SigmaNativeObservationGpu
                {
                    Identity = U4(revision, 7u, 13u, 0x7fu),
                    Footprint = U4(unchecked((uint)step.Parent.U),
                        unchecked((uint)(step.Parent.U >> 32)),
                        unchecked((uint)step.Parent.V),
                        unchecked((uint)(step.Parent.V >> 32))),
                    Evidence = U4(11u, 12u, 13u, 14u),
                },
            });
            var states = new UInt2[scratch.States.count];
            Array.Copy(priorState, 0, states, 48, SigmaS16.LaneCount);
            Array.Copy(priorState, 0, states, 16, SigmaS16.LaneCount);
            states[16].High = step.SelectedStateInteger << 16;
            scratch.States.SetData(states);
            var branches = new UInt4[scratch.BranchHeaders.count];
            branches[SigmaNativeFrameSlotResources.LiveFreshBranchCount] =
                new UInt4
                {
                    X = (uint)SigmaFreshAdmissionStatus.Admitted,
                    Y = (uint)SigmaMerkabaRelationClass.Regular,
                    Z = step.SelectedChild,
                    W = (uint)SigmaNativeColdReason.RepresentationRefinement,
                };
            scratch.BranchHeaders.SetData(branches);
            var relations = new UInt4[scratch.RelationResults.count];
            relations[1] = new UInt4
            {
                X = (uint)SigmaMerkabaRelationClass.Regular,
                W = 1u,
            };
            scratch.RelationResults.SetData(relations);
            scratch.RelationFactors.SetData(
                new UInt4[scratch.RelationFactors.count]);
            scratch.RelationHashes.SetData(
                new UInt4[scratch.RelationHashes.count]);
            var certificates = new UInt4[
                scratch.LocalityCertificateWords.count];
            Array.Copy(priorCertificate, 0, certificates, 0,
                SigmaNativeFrameSlotResources.CertificateWordCount);
            Array.Copy(priorCertificate, 0, certificates,
                SigmaNativeFrameSlotResources.CertificateWordCount,
                SigmaNativeFrameSlotResources.CertificateWordCount);
            scratch.LocalityCertificateWords.SetData(certificates);
            scratch.StateDelta.SetData(new SigmaNativeStateDeltaGpu[
                scratch.StateDelta.count]);
            scratch.GaugeDelta.SetData(new SigmaNativeGaugeDeltaGpu[
                scratch.GaugeDelta.count]);
            scratch.Counters.SetData(new UInt4[scratch.Counters.count]);
            scratch.Unresolved.SetData(new SigmaUnresolvedConstraintGpu[
                scratch.Unresolved.count]);
        }

        private static void WriteRepresentationCell(UInt4[] words, int slot,
            int sample, GaugeKey gauge, uint generation)
        {
            int address = (slot * SigmaCarrier.SamplesPerPage + sample) *
                SigmaCarrier.RepresentationWordsPerSample;
            words[address] = gauge.ToRaw();
            words[address + 1] = new UInt4
            {
                X = gauge.Level,
                Y = (uint)(SigmaNativeGaugeCellFlags.Active |
                    SigmaNativeGaugeCellFlags.Normalized),
                Z = 0x5110ca34u,
                W = 0x08f90f9cu,
            };
            words[address + 2] = new UInt4
            {
                X = (uint)(SigmaNativeCertificateFlags.Valid |
                    SigmaNativeCertificateFlags.Directional |
                    SigmaNativeCertificateFlags.Minimized),
                Y = 1u,
                Z = 1u,
                W = generation,
            };
            words[address + 4] = new UInt4
            {
                X = 11u,
                Y = 12u,
                Z = 13u,
                W = 14u,
            };
            words[address + 5] = new UInt4
            {
                X = 0u,
                Y = (uint)SigmaMerkabaRelationClass.Regular,
                Z = 0u,
                W = 0u,
            };
        }

        private static PageMeta ReadPageMeta(GraphicsBuffer metadata,
            uint slot)
        {
            var result = new PageMeta[1];
            metadata.GetData(result, 0, (int)slot, 1);
            return result[0];
        }

        private static int FindGaugeSample(GraphicsBuffer representation,
            uint slot, uint activeSampleCount, GaugeKey expected)
        {
            int count = checked((int)activeSampleCount *
                SigmaCarrier.RepresentationWordsPerSample);
            var words = new UInt4[count];
            representation.GetData(words, 0,
                checked((int)slot * SigmaCarrier.SamplesPerPage *
                    SigmaCarrier.RepresentationWordsPerSample), count);
            for (int sample = 0;
                sample < checked((int)activeSampleCount); ++sample)
            {
                int address = sample *
                    SigmaCarrier.RepresentationWordsPerSample;
                if (GaugeKey.From(words[address], words[address + 1].X)
                    .Equals(expected))
                    return sample;
            }
            Assert.Fail($"Gauge parent {expected} was not retained.");
            return -1;
        }

        private static UInt2[] ReadState(GraphicsBuffer state, uint slot,
            int sample)
        {
            var result = new UInt2[SigmaS16.LaneCount];
            state.GetData(result, 0,
                checked((int)slot * SigmaCarrier.PageLaneCount +
                    sample * SigmaS16.LaneCount), SigmaS16.LaneCount);
            return result;
        }

        private static UInt4[] ReadCertificate(GraphicsBuffer representation,
            uint slot, int sample)
        {
            var result = new UInt4[
                SigmaNativeFrameSlotResources.CertificateWordCount];
            representation.GetData(result, 0,
                checked(((int)slot * SigmaCarrier.SamplesPerPage + sample) *
                    SigmaCarrier.RepresentationWordsPerSample + 2),
                result.Length);
            return result;
        }

        private static uint[] Flatten(UInt2[] values)
        {
            var result = new uint[values.Length * 2];
            for (int index = 0; index < values.Length; ++index)
            {
                result[index * 2] = values[index].Low;
                result[index * 2 + 1] = values[index].High;
            }
            return result;
        }

        private static uint[] Flatten(UInt4[] values)
        {
            var result = new uint[values.Length * 4];
            for (int index = 0; index < values.Length; ++index)
            {
                result[index * 4] = values[index].X;
                result[index * 4 + 1] = values[index].Y;
                result[index * 4 + 2] = values[index].Z;
                result[index * 4 + 3] = values[index].W;
            }
            return result;
        }

        private static SigmaFrameUInt2Gpu Q2(long value) => new()
        {
            X = unchecked((uint)value),
            Y = unchecked((uint)(value >> 32)),
        };

        private static SigmaFrameUInt4Gpu U4(uint x, uint y, uint z, uint w) =>
            new() { X = x, Y = y, Z = z, W = w };

        private readonly struct RefinementStep
        {
            internal RefinementStep(GaugeKey parent, uint selectedChild,
                uint selectedStateInteger)
            {
                Parent = parent;
                SelectedChild = selectedChild;
                SelectedStateInteger = selectedStateInteger;
            }

            internal GaugeKey Parent { get; }
            internal uint SelectedChild { get; }
            internal uint SelectedStateInteger { get; }
        }

        private sealed class RefinementSnapshot
        {
            internal RefinementSnapshot(uint root, uint activeSampleCount,
                uint certificateCount, uint[] stateWords,
                uint[] representationWords, GaugeKey[] gaugeKeys)
            {
                Root = root;
                ActiveSampleCount = activeSampleCount;
                CertificateCount = certificateCount;
                StateWords = stateWords;
                RepresentationWords = representationWords;
                GaugeKeys = gaugeKeys;
            }

            internal uint Root { get; }
            internal uint ActiveSampleCount { get; }
            internal uint CertificateCount { get; }
            internal uint[] StateWords { get; }
            internal uint[] RepresentationWords { get; }
            internal GaugeKey[] GaugeKeys { get; }
        }

        private sealed class StaticExclusionSnapshot
        {
            internal StaticExclusionSnapshot(SigmaNativeFrameGpu frame,
                uint root, UInt2[] sourceState, UInt2[] targetState,
                UInt4[] sourceGauge, UInt4[] targetGauge, UInt4[] counters)
            {
                Frame = frame;
                Root = root;
                SourceState = sourceState;
                TargetState = targetState;
                SourceGauge = sourceGauge;
                TargetGauge = targetGauge;
                Counters = counters;
            }

            internal SigmaNativeFrameGpu Frame { get; }
            internal uint Root { get; }
            internal UInt2[] SourceState { get; }
            internal UInt2[] TargetState { get; }
            internal UInt4[] SourceGauge { get; }
            internal UInt4[] TargetGauge { get; }
            internal UInt4[] Counters { get; }
        }

        private readonly struct GaugeKey : IEquatable<GaugeKey>
        {
            internal GaugeKey(long u, long v, uint level)
            {
                U = u;
                V = v;
                Level = level;
            }

            internal long U { get; }
            internal long V { get; }
            internal uint Level { get; }

            internal UInt4 ToRaw() => new()
            {
                X = unchecked((uint)U),
                Y = unchecked((uint)(U >> 32)),
                Z = unchecked((uint)V),
                W = unchecked((uint)(V >> 32)),
            };

            internal static GaugeKey From(UInt4 coordinate, uint level) =>
                new(unchecked((long)((ulong)coordinate.X |
                    ((ulong)coordinate.Y << 32))),
                    unchecked((long)((ulong)coordinate.Z |
                    ((ulong)coordinate.W << 32))), level);

            public bool Equals(GaugeKey other) => U == other.U &&
                V == other.V && Level == other.Level;
            public override bool Equals(object other) =>
                other is GaugeKey key && Equals(key);
            public override int GetHashCode() => HashCode.Combine(U, V, Level);
            public override string ToString() => $"({U},{V})@{Level}";
        }

        private sealed class GaugeKeyComparer : IComparer<GaugeKey>
        {
            internal static readonly GaugeKeyComparer Instance = new();

            public int Compare(GaugeKey left, GaugeKey right)
            {
                int level = left.Level.CompareTo(right.Level);
                if (level != 0)
                    return level;
                int morton = SignedMorton(left.U, left.V).CompareTo(
                    SignedMorton(right.U, right.V));
                if (morton != 0)
                    return morton;
                int u = left.U.CompareTo(right.U);
                return u != 0 ? u : left.V.CompareTo(right.V);
            }

            private static System.Numerics.BigInteger SignedMorton(long u,
                long v)
            {
                System.Numerics.BigInteger x = u >= 0L
                    ? (System.Numerics.BigInteger)u * 2
                    : -(System.Numerics.BigInteger)u * 2 - 1;
                System.Numerics.BigInteger y = v >= 0L
                    ? (System.Numerics.BigInteger)v * 2
                    : -(System.Numerics.BigInteger)v * 2 - 1;
                System.Numerics.BigInteger result =
                    System.Numerics.BigInteger.Zero;
                for (int bit = 0; bit < 64; ++bit)
                {
                    result |= ((x >> bit) & System.Numerics.BigInteger.One) <<
                        (bit * 2);
                    result |= ((y >> bit) & System.Numerics.BigInteger.One) <<
                        (bit * 2 + 1);
                }
                return result;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct UInt2
        {
            internal uint Low;
            internal uint High;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct UInt4
        {
            internal uint X;
            internal uint Y;
            internal uint Z;
            internal uint W;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PageMeta
        {
            internal uint PageXLow;
            internal uint PageXHigh;
            internal uint PageYLow;
            internal uint PageYHigh;
            internal uint Generation;
            internal uint Revision;
            internal uint CertificateLow;
            internal uint CertificateHigh;
            internal uint CertificateCount;
            internal uint Flags;
            internal uint GaugeGeneration;
            internal uint CertificateGeneration;
            internal uint RepresentationFlags;
            internal uint RepresentationFingerprint;
            internal uint ActiveSampleCount;
            internal uint Reserved0;
        }
    }
}
