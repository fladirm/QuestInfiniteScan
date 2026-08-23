using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Genesis.RoomScan.SigmaPrism;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class SigmaStreamingContractTests
    {
        private const uint Invalid = uint.MaxValue;

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct PageMetadataGpu
        {
            public uint XLo;
            public uint XHi;
            public uint YLo;
            public uint YHi;
            public uint Generation;
            public uint Revision;
            public uint CertificateOffsetLo;
            public uint CertificateOffsetHi;
            public uint CertificateCount;
            public uint Flags;
            public uint Reserved0;
            public uint Reserved1;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct UInt2Gpu
        {
            public uint X;
            public uint Y;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct BoundsGpu
        {
            public UInt2Gpu Lo;
            public UInt2Gpu Hi;
        }

        [Test]
        public void StreamingWorkGraphKernelsAndHistoricalRasterCompile()
        {
            var contracts = new Dictionary<string, string[]>
            {
                ["SigmaPrism/SigmaInverseWorkGraph"] = new[]
                {
                    "InitializeStreamingGraph", "InitializeStreamingOwnership",
                    "ClearIngressWork", "CompactIngressBundles",
                    "FinalizeExtractedBundles", "ScheduleSigmaTransactions",
                    "FinalizeStreamingScheduleDiagnostics",
                    "ReleaseProbationAssociations", "PrepareTransactionPages"
                },
                ["SigmaPrism/SigmaSourceBundle"] = new[]
                {
                    "CopySealedBundleMetadata", "ExtractSealedBundleSamples"
                },
                ["SigmaPrism/SigmaStreamInverse"] = new[]
                {
                    "PrepareTransactionMicrotile",
                    "EvaluateTransactionRgbLeft",
                    "EvaluateTransactionRgbRight",
                    "MeetTransactionMicrotile",
                    "EvaluateTransactionMicrotile"
                },
                ["SigmaPrism/SigmaStreamProof"] = new[]
                {
                    "ReduceTransactionSourceBlock", "PrepareProofOrder",
                    "MergeProofRuns", "CoalesceProofWindow",
                    "PrepareProofRedundancy", "EvaluateProofRedundancyWindow",
                    "EmitProofCertificates", "RetainProofRawWindow",
                    "CompleteProofBlock"
                },
                ["SigmaPrism/SigmaStreamTransition"] = new[]
                {
                    "ValidateCandidateTransitionChunk",
                    "ValidateCandidateAssociatorChunk"
                },
                ["SigmaPrism/SigmaStreamPublication"] = new[]
                {
                    "InitializeManifestVisibility",
                    "PreparePublicationManifest", "PublishPublicationManifest",
                    "ResolvePublishedPageCaches", "RetirePublishedTransaction"
                },
                ["SigmaPrism/SigmaStreamDerived"] = new[]
                {
                    "MaterializePublishedTopology"
                },
                ["SigmaPrism/SigmaStreamDormant"] = new[]
                {
                    "PrepareDormantParking", "ParkDormantSources",
                    "ParkDormantSegments", "ReleaseDormantPage",
                    "FinalizeDormantParking", "RecheckDormantProbations"
                },
                ["SigmaPrism/SigmaStreamRevalidation"] = new[]
                {
                    "PrepareHistoricalRevalidation",
                    "BuildHistoricalDrawArguments",
                    "RebuildHistoricalAssociation",
                    "FinalizeHistoricalRevalidation",
                    "CancelHistoricalRevalidation"
                }
            };

            foreach (KeyValuePair<string, string[]> contract in contracts)
            {
                ComputeShader shader = Resources.Load<ComputeShader>(
                    contract.Key);
                Assert.That(shader, Is.Not.Null, contract.Key);
                foreach (string kernel in contract.Value)
                    Assert.That(shader.HasKernel(kernel), Is.True,
                        $"{contract.Key}:{kernel}");
            }

            Shader raster = Resources.Load<Shader>(
                "SigmaPrism/SigmaStreamRevalidation");
            Assert.That(raster, Is.Not.Null);
            var material = new Material(raster);
            try
            {
                Assert.That(material.FindPass("SigmaHistoricalClear"),
                    Is.EqualTo(0));
                Assert.That(material.FindPass("SigmaHistoricalRevalidation"),
                    Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void StreamingRepairContractUsesProductionBindingsAndClosure()
        {
            Assert.That(Marshal.SizeOf<SigmaTransactionGpu>(),
                Is.EqualTo(SigmaGeneratedStreaming.TransactionStride));
            Assert.That(SigmaGeneratedStreaming.TransactionStride,
                Is.EqualTo(368));
            Assert.That(SigmaGeneratedStreaming.ExecutionPhaseAll,
                Is.EqualTo(0x3fu));

            string graph = ReadScript("SigmaStreamingGraph.cs");
            StringAssert.Contains(
                "_ledger.BindReadOnly(command, _inverse, kernel);", graph);
            StringAssert.IsMatch(
                @"BindScheduleDiagnostics[\s\S]*?_StreamProbation",
                graph);
            StringAssert.Contains("_PoseConsumeReferenceFromWorld", graph);
            StringAssert.Contains("_PoseConsumeWorldFromReference", graph);
            StringAssert.Contains(
                "private const int CanonicalRoundsPerSubmission = 8;", graph);
            StringAssert.Contains("round == 0", graph);
            StringAssert.Contains(
                "round + 1 == CanonicalRoundsPerSubmission", graph);

            string work = ReadCompute("SigmaPrism/SigmaInverseWorkGraph");
            StringAssert.Contains("_RefillBudgets", work);
            StringAssert.Contains("_EmitPublication", work);
            StringAssert.Contains("transaction.execution.z = " +
                "SIGMA_STREAM_EXECUTION_ISSUED", work);
            StringAssert.Contains("transaction.page1.coordinate = " +
                "SIGMA_STREAM_INVALID", work);

            string inverse = ReadCompute("SigmaPrism/SigmaStreamInverse");
            StringAssert.Contains("SigmaEvalFailWork", inverse);
            StringAssert.Contains("SigmaEvalCompletePhase", inverse);
            StringAssert.Contains("SIGMA_STREAM_PHASE_RGB_LEFT", inverse);
            StringAssert.Contains("SIGMA_STREAM_PHASE_RGB_RIGHT", inverse);
            StringAssert.Contains("SIGMA_STREAM_PHASE_MET", inverse);

            string revalidation = ReadCompute(
                "SigmaPrism/SigmaStreamRevalidation");
            StringAssert.Contains("SigmaRevalidationClosedState",
                revalidation);
            StringAssert.Contains("SIGMA_PROPOSAL_ACCEPTED", revalidation);
            StringAssert.Contains("SIGMA_STREAM_OUTCOME_NULL_PROMOTION",
                revalidation);
            StringAssert.Contains("SIGMA_STREAM_OUTCOME_EXISTING_UPDATE",
                revalidation);

            string publication = ReadCompute(
                "SigmaPrism/SigmaStreamPublication");
            StringAssert.Contains("transaction.publication.z", publication);
            StringAssert.Contains("if (count == 0u || count > " +
                "SIGMA_STREAM_MAX_PAGES)", publication);
        }

        [Test]
        public void TransactionTelemetryDecodesExecutionGeneration()
        {
            var words = new uint[
                SigmaGeneratedStreaming.TransactionStride / sizeof(uint)];
            words[0] = 2u;
            words[1] = 7u;
            words[18] = 1u;
            words[24] = 11u;
            words[25] = 13u;
            words[26] = 17u;
            words[27] = 19u;
            words[72] = 3u;
            words[73] = 9u;
            words[74] = SigmaGeneratedStreaming.ExecutionPhaseAll;
            words[75] = 1u |
                (1u << SigmaGeneratedStreaming.ExecutionOutcomeShift) |
                SigmaGeneratedStreaming.ExecutionFault;
            words[76] = 23u;
            words[80] = 29u;

            var telemetry = new SigmaTransactionTelemetry(0, words, 0);
            Assert.That(telemetry.Generation, Is.EqualTo(7u));
            Assert.That(telemetry.PublicationPageCount, Is.EqualTo(1u));
            Assert.That(telemetry.Page0Source, Is.EqualTo(11u));
            Assert.That(telemetry.Page0Target, Is.EqualTo(13u));
            Assert.That(telemetry.Page0SourceGeneration, Is.EqualTo(17u));
            Assert.That(telemetry.Page0TargetGeneration, Is.EqualTo(19u));
            Assert.That(telemetry.ExecutionSource, Is.EqualTo(3u));
            Assert.That(telemetry.ExecutionBlockMicrotile, Is.EqualTo(9u));
            Assert.That(telemetry.ExecutionPhaseMask, Is.EqualTo(0x3fu));
            Assert.That(telemetry.ExecutionProposalMask, Is.EqualTo(1u));
            Assert.That(telemetry.ExecutionOutcomeMask,
                Is.EqualTo(1u <<
                    SigmaGeneratedStreaming.ExecutionOutcomeShift));
            Assert.That(telemetry.ExecutionFaulted, Is.True);
            Assert.That(telemetry.ScratchSegment, Is.EqualTo(23u));
            Assert.That(telemetry.TransitionEdge, Is.EqualTo(29u));
        }

        [Test]
        public void HistoricalSnapshotIsPinnedAndNeverUpgradesInFlight()
        {
            ComputeShader shader = Resources.Load<ComputeShader>(
                "SigmaPrism/SigmaStreamRevalidation");
            Assert.That(shader, Is.Not.Null);
            int prepare = shader.FindKernel("PrepareHistoricalRevalidation");
            int build = shader.FindKernel("BuildHistoricalDrawArguments");
            int cancel = shader.FindKernel("CancelHistoricalRevalidation");

            var workCountsData = new uint[SigmaGeneratedStreaming.OpcodeCount];
            workCountsData[(int)SigmaStreamOpcode.REVALIDATE] = 1u;
            var workItemsData = new SigmaStreamWorkItemGpu[
                SigmaGeneratedStreaming.OpcodeCount * 64];
            workItemsData[(int)SigmaStreamOpcode.REVALIDATE * 64].Identity =
                U4((uint)SigmaStreamOpcode.REVALIDATE, 0u, 1u, 0u);
            var transactionsData = new SigmaTransactionGpu[
                SigmaGeneratedStreaming.TransactionCapacity];
            transactionsData[0].Identity = U4(5u, 1u, 0u, 1u);
            transactionsData[0].Source = U4(0u, 1u, 1u, 0u);
            transactionsData[0].Scratch = U4(0u, 1u, 0u, 0u);
            transactionsData[0].Page0.Coordinate = U4(0u, 0u, 0u, 0u);
            transactionsData[0].Page0.State = U4(0u, Invalid, 1u, 0u);

            var bundlesData = new SigmaSealedSourceBundleGpu[
                SigmaGeneratedStreaming.BundleCapacity];
            bundlesData[0].Identity = U4(5u, 1u, 0u, 4u);
            bundlesData[0].Raw = U4(0u, 0u, 0u, 0u);
            bundlesData[0].Dependency = U4(0u, 1u, 1u, 2u);
            var segmentsData = new SigmaSourceHandleSegmentGpu[1];
            segmentsData[0].Identity = U4(2u, 1u, 1u, 0u);
            segmentsData[0].Link = U4(Invalid, 0u, 0u, 0u);
            segmentsData[0].Handle01 = U4(0u, 1u, Invalid, 0u);
            var manifestsData = new SigmaPublicationManifestGpu[1];
            manifestsData[0].Identity = U4(2u, 1u, 1u, Invalid);
            manifestsData[0].Closure = U4(1u, 1u, 1u, 1u);
            manifestsData[0].Pages = U4(0u, Invalid, Invalid, Invalid);
            var visibilityData = new SigmaPageVisibilityGpu[1];
            visibilityData[0].BornRetired = U4(0u, 1u, Invalid, 0u);
            visibilityData[0].Pins = U4(0u, 0u, 1u, 0u);
            var metadataData = new[]
            {
                new PageMetadataGpu
                {
                    Generation = 1u,
                    Revision = 1u,
                    Flags = 7u
                }
            };
            var associationOwnersData = new SigmaStreamUInt4Gpu[
                SigmaGeneratedStreaming.TransactionCapacity];
            for (int index = 0; index < associationOwnersData.Length; ++index)
                associationOwnersData[index] = U4(Invalid, 0u, 0u, 0u);
            associationOwnersData[0] = U4(0u, 1u, 0u, 0u);
            var schedulerData = new uint[32];
            schedulerData[6] = 3u;
            schedulerData[9] = 0u;
            var contextData = new SigmaStreamUInt4Gpu[4];
            contextData[0] = U4(Invalid, 0u, 0u, 0u);
            contextData[1] = U4(Invalid, 0u, 0u, 0u);

            var owned = new List<GraphicsBuffer>();
            GraphicsBuffer Buffer<T>(T[] data, int stride) where T : struct
            {
                var result = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured, data.Length, stride);
                result.SetData(data);
                owned.Add(result);
                return result;
            }

            GraphicsBuffer workCounts = Buffer(workCountsData, sizeof(uint));
            GraphicsBuffer workItems = Buffer(workItemsData,
                SigmaGeneratedStreaming.WorkItemStride);
            GraphicsBuffer transactions = Buffer(transactionsData,
                SigmaGeneratedStreaming.TransactionStride);
            GraphicsBuffer bundles = Buffer(bundlesData,
                SigmaGeneratedStreaming.BundleStride);
            GraphicsBuffer segments = Buffer(segmentsData,
                SigmaGeneratedStreaming.SourceHandleSegmentStride);
            GraphicsBuffer manifests = Buffer(manifestsData,
                SigmaGeneratedStreaming.PublicationManifestStride);
            GraphicsBuffer visibility = Buffer(visibilityData,
                SigmaGeneratedStreaming.PageVisibilityStride);
            GraphicsBuffer metadata = Buffer(metadataData,
                Marshal.SizeOf<PageMetadataGpu>());
            GraphicsBuffer associationOwners = Buffer(associationOwnersData,
                sizeof(uint) * 4);
            GraphicsBuffer scheduler = Buffer(schedulerData, sizeof(uint));
            GraphicsBuffer context = Buffer(contextData, sizeof(uint) * 4);
            GraphicsBuffer snapshot = Buffer(new[] { U4(0u, 0u, 0u, 0u) },
                sizeof(uint) * 4);
            GraphicsBuffer diagnostics = Buffer(
                new SigmaStreamDiagnosticGpu[1],
                SigmaGeneratedStreaming.DiagnosticStride);
            GraphicsBuffer drawArguments = Buffer(new uint[8], sizeof(uint));

            try
            {
                Bind(shader, prepare, "_StreamWorkCounts", workCounts);
                Bind(shader, prepare, "_StreamWorkItems", workItems);
                Bind(shader, prepare, "_StreamSourceSegments", segments);
                Bind(shader, prepare, "_StreamManifests", manifests);
                Bind(shader, prepare, "_StreamPageVisibility", visibility);
                Bind(shader, prepare, "_PageMetadata", metadata);
                Bind(shader, prepare, "_StreamTransactions", transactions);
                Bind(shader, prepare, "_StreamBundles", bundles);
                Bind(shader, prepare, "_StreamAssociationOwners",
                    associationOwners);
                Bind(shader, prepare, "_StreamSchedulerControl", scheduler);
                Bind(shader, prepare, "_RevalidationContext", context);
                Bind(shader, prepare, "_RevalidationPageSnapshot", snapshot);
                Bind(shader, prepare, "_StreamDiagnostics", diagnostics);
                shader.SetInt("_PageCapacity", 1);
                shader.SetInt("_StreamManifestCapacity", 1);
                shader.SetInt("_StreamSourceSegmentCapacity", 1);
                shader.Dispatch(prepare, 1, 1, 1);

                visibility.GetData(visibilityData);
                context.GetData(contextData);
                bundles.GetData(bundlesData);
                var snapshotData = new SigmaStreamUInt4Gpu[1];
                snapshot.GetData(snapshotData);
                Assert.That(visibilityData[0].Pins.W, Is.EqualTo(1u));
                Assert.That(contextData[1].Z, Is.EqualTo(3u));
                Assert.That(contextData[2].X, Is.EqualTo(1u));
                Assert.That(bundlesData[0].Dependency.W, Is.EqualTo(3u));
                Assert.That(snapshotData[0].X, Is.EqualTo(0u));
                Assert.That(snapshotData[0].Y, Is.EqualTo(1u));
                Assert.That(snapshotData[0].Z, Is.EqualTo(1u));

                schedulerData[6] = 5u;
                scheduler.SetData(schedulerData);
                shader.Dispatch(prepare, 1, 1, 1);
                context.GetData(contextData);
                bundles.GetData(bundlesData);
                Assert.That(contextData[1].Z, Is.EqualTo(3u),
                    "an in-flight R snapshot may not upgrade to R+N");
                Assert.That(bundlesData[0].Dependency.W, Is.EqualTo(3u));

                Bind(shader, build, "_StreamWorkCounts", workCounts);
                Bind(shader, build, "_StreamSchedulerControl", scheduler);
                Bind(shader, build, "_RevalidationContext", context);
                Bind(shader, build, "_RevalidationDrawArguments",
                    drawArguments);
                shader.Dispatch(build, 1, 1, 1);
                var arguments = new uint[8];
                drawArguments.GetData(arguments);
                Assert.That(arguments[0], Is.EqualTo(3u));
                Assert.That(arguments[4], Is.EqualTo(24576u));
                Assert.That(arguments[6], Is.Zero);

                Bind(shader, cancel, "_StreamSchedulerControl", scheduler);
                Bind(shader, cancel, "_RevalidationContext", context);
                Bind(shader, cancel, "_StreamPageVisibility", visibility);
                Bind(shader, cancel, "_RevalidationPageSnapshot", snapshot);
                shader.SetInt("_PageCapacity", 1);
                shader.Dispatch(cancel, 1, 1, 1);
                visibility.GetData(visibilityData);
                context.GetData(contextData);
                Assert.That(visibilityData[0].Pins.W, Is.Zero);
                Assert.That(contextData[0].X, Is.EqualTo(Invalid));
            }
            finally
            {
                for (int index = owned.Count - 1; index >= 0; --index)
                    owned[index].Dispose();
            }
        }

        [Test]
        public void ProofClosureIsIndependentOfWindowsAndInterleaving()
        {
            List<SigmaConstraintCertificate> evidence =
                BuildPartitionedProofEvidence();
            SigmaQ48Interval[] target = MeetCertificates(evidence);
            SigmaConstraintCertificate[] baseline = CloseProof(
                evidence, target);

            Assert.That(evidence.Count, Is.GreaterThan(12));
            Assert.That(baseline.Length, Is.EqualTo(16),
                "the canonical proof itself may exceed a 12-record work window");

            int[] windows = { 1, 2, 7, evidence.Count };
            foreach (int window in windows)
            for (int interleaving = 0; interleaving < 3; ++interleaving)
            {
                List<SigmaConstraintCertificate> replay = ReplayWindows(
                    evidence, window, interleaving);
                SigmaConstraintCertificate[] actual = CloseProof(
                    replay, target);
                AssertProofEqual(baseline, actual,
                    $"window={window}, interleaving={interleaving}");
            }
        }

        [Test]
        public void ProofGpuReverseSweepMatchesCanonicalEssentialSet()
        {
            const int opcode = 5;
            const int proofOwner = 22;
            const uint proofPending = 3u;
            const uint proofPrefix = 5u;
            const uint emitCertificates = 7u;
            const int candidateCount = 30;
            const int candidateCapacity = 261;

            ComputeShader shader = Resources.Load<ComputeShader>(
                "SigmaPrism/SigmaStreamProof");
            Assert.That(shader, Is.Not.Null);
            int prepare = shader.FindKernel("PrepareProofRedundancy");
            int evaluate = shader.FindKernel(
                "EvaluateProofRedundancyWindow");

            var workCountsData = new uint[SigmaGeneratedStreaming.OpcodeCount];
            workCountsData[opcode] = 1u;
            var workItemsData = new SigmaStreamWorkItemGpu[
                SigmaGeneratedStreaming.OpcodeCount * 64];
            workItemsData[opcode * 64].Identity = U4(opcode, 0u, 1u, 0u);
            var transactionsData = new SigmaTransactionGpu[
                SigmaGeneratedStreaming.TransactionCapacity];
            transactionsData[0].Identity = U4(proofPending, 1u, 0u, 0u);
            var closuresData = new SigmaProofClosureGpu[
                SigmaGeneratedStreaming.TransactionCapacity];
            closuresData[0].Identity = U4(proofPrefix, 1u, 0u, 0u);
            closuresData[0].Journal = U4(candidateCount, candidateCount,
                0u, 0u);
            closuresData[0].Ordering = U4(0u, 0u, 0u, 0u);

            var candidatesData = new SigmaProofCandidateGpu[
                candidateCapacity];
            var boundsData = new BoundsGpu[candidateCapacity * 16];
            BoundsGpu full = Bounds(long.MinValue, long.MaxValue);
            for (int index = 0; index < boundsData.Length; ++index)
                boundsData[index] = full;
            for (int lane = 0; lane < 16; ++lane)
            {
                candidatesData[lane].Identity = U4(1u << lane, 1u, 0u, 11u);
                candidatesData[lane].Provenance = U4(1u, 0u, 0u, 1u);
                candidatesData[lane].Source = U4((uint)lane, 0u,
                    (uint)lane, 0u);
                boundsData[lane * 16 + lane] = Bounds(lane + 1L,
                    lane + 1L);
            }
            for (int index = 16; index < candidateCount; ++index)
            {
                int lane = (index - 16) & 15;
                candidatesData[index].Identity = U4(1u << lane,
                    (uint)index + 1u, 0u, 11u);
                candidatesData[index].Provenance = U4(1u, 0u, 0u, 1u);
                candidatesData[index].Source = U4((uint)index, 0u,
                    (uint)index, 0u);
            }
            var sortData = new uint[candidateCapacity];
            for (int index = 0; index < candidateCount; ++index)
                sortData[index] = (uint)index;
            var schedulerData = new uint[32];
            schedulerData[proofOwner] = 0u;

            var owned = new List<GraphicsBuffer>();
            GraphicsBuffer Buffer<T>(T[] data, int stride) where T : struct
            {
                var result = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured, data.Length, stride);
                result.SetData(data);
                owned.Add(result);
                return result;
            }

            GraphicsBuffer workCounts = Buffer(workCountsData, sizeof(uint));
            GraphicsBuffer workItems = Buffer(workItemsData,
                SigmaGeneratedStreaming.WorkItemStride);
            GraphicsBuffer transactions = Buffer(transactionsData,
                SigmaGeneratedStreaming.TransactionStride);
            GraphicsBuffer closures = Buffer(closuresData,
                SigmaGeneratedStreaming.ProofClosureStride);
            GraphicsBuffer candidates = Buffer(candidatesData,
                SigmaGeneratedStreaming.ProofCandidateStride);
            GraphicsBuffer candidateBounds = Buffer(boundsData,
                Marshal.SizeOf<BoundsGpu>());
            GraphicsBuffer sortA = Buffer(sortData, sizeof(uint));
            GraphicsBuffer sortB = Buffer(new uint[candidateCapacity],
                sizeof(uint));
            GraphicsBuffer prefix = Buffer(
                new SigmaProofPrefixGpu[candidateCapacity],
                SigmaGeneratedStreaming.ProofPrefixStride);
            GraphicsBuffer prefixBounds = Buffer(
                new BoundsGpu[candidateCapacity * 16],
                Marshal.SizeOf<BoundsGpu>());
            GraphicsBuffer keep = Buffer(new uint[(candidateCapacity + 31) / 32],
                sizeof(uint));
            GraphicsBuffer scheduler = Buffer(schedulerData, sizeof(uint));

            try
            {
                using SigmaExactBackendGate gate =
                    SigmaExactBackendGate.Dispatch();
                foreach (int kernel in new[] { prepare, evaluate })
                {
                    gate.Bind(shader, kernel);
                    Bind(shader, kernel, "_StreamWorkItems", workItems);
                    Bind(shader, kernel, "_StreamWorkCounts", workCounts);
                    Bind(shader, kernel, "_StreamTransactionsRead",
                        transactions);
                    Bind(shader, kernel, "_StreamTransactions", transactions);
                    Bind(shader, kernel, "_StreamProofClosures", closures);
                    Bind(shader, kernel, "_StreamProofCandidatesRead",
                        candidates);
                    Bind(shader, kernel, "_StreamProofCandidates", candidates);
                    Bind(shader, kernel, "_StreamProofCandidateBoundsRead",
                        candidateBounds);
                    Bind(shader, kernel, "_StreamProofCandidateBounds",
                        candidateBounds);
                    Bind(shader, kernel, "_StreamProofSortIndicesARead", sortA);
                    Bind(shader, kernel, "_StreamProofSortIndicesBRead", sortB);
                    Bind(shader, kernel, "_StreamProofSortIndicesA", sortA);
                    Bind(shader, kernel, "_StreamProofSortIndicesB", sortB);
                    Bind(shader, kernel, "_StreamProofPrefix", prefix);
                    Bind(shader, kernel, "_StreamProofPrefixBounds",
                        prefixBounds);
                    Bind(shader, kernel, "_StreamProofKeepWordsRead", keep);
                    Bind(shader, kernel, "_StreamProofKeepWords", keep);
                    Bind(shader, kernel, "_StreamSchedulerControlRead",
                        scheduler);
                    Bind(shader, kernel, "_StreamSchedulerControl", scheduler);
                }
                shader.SetInt("_StreamProofCandidateCapacity",
                    candidateCapacity);

                bool completed = false;
                for (int quantum = 0; quantum < 512; ++quantum)
                {
                    shader.Dispatch(prepare, 1, 1, 1);
                    shader.Dispatch(evaluate, 1, 1, 1);
                    closures.GetData(closuresData);
                    if (closuresData[0].Identity.X == emitCertificates)
                    {
                        completed = true;
                        break;
                    }
                }
                Assert.That(completed, Is.True,
                    "bounded reverse sweeps must reach their fixed point");
                var kept = new uint[(candidateCapacity + 31) / 32];
                keep.GetData(kept);
                Assert.That(kept[0] & 0xffffu, Is.EqualTo(0xffffu));
                Assert.That(kept[0] & 0x3fff0000u, Is.Zero,
                    "all fourteen broad records must be redundant");
                candidateBounds.GetData(boundsData);
                for (int lane = 0; lane < 16; ++lane)
                {
                    BoundsGpu actual = boundsData[
                        (candidateCapacity - 1) * 16 + lane];
                    Assert.That(actual.Lo.X, Is.EqualTo((uint)(lane + 1)));
                    Assert.That(actual.Lo.Y, Is.Zero);
                    Assert.That(actual.Hi.X, Is.EqualTo((uint)(lane + 1)));
                    Assert.That(actual.Hi.Y, Is.Zero);
                }
            }
            finally
            {
                for (int index = owned.Count - 1; index >= 0; --index)
                    owned[index].Dispose();
            }
        }

        private static List<SigmaConstraintCertificate>
            BuildPartitionedProofEvidence()
        {
            var evidence = new List<SigmaConstraintCertificate>();
            for (int lane = 0; lane < SigmaS16.LaneCount; ++lane)
            {
                long lower = -500L + lane * 7L;
                long upper = 700L - lane * 5L;
                byte source = (byte)(lane & 3);
                uint key = (uint)lane + 1u;
                ushort mask = (ushort)(1u << lane);
                if (lane < 5)
                {
                    evidence.Add(ProofCertificate((ulong)(100 + lane),
                        lane, new SigmaQ48Interval(lower,
                            SigmaQ48Interval.Full.Upper), source, key, mask));
                    evidence.Add(ProofCertificate((ulong)(100 + lane),
                        lane, new SigmaQ48Interval(
                            SigmaQ48Interval.Full.Lower, upper), source, key,
                        mask));
                }
                else
                {
                    evidence.Add(ProofCertificate((ulong)(100 + lane),
                        lane, new SigmaQ48Interval(lower, upper), source, key,
                        mask));
                }
            }

            for (int index = 0; index < 9; ++index)
            {
                int lane = index % SigmaS16.LaneCount;
                evidence.Add(ProofCertificate((ulong)(1000 + index), lane,
                    new SigmaQ48Interval(-10000L, 10000L),
                    (byte)((index + 1) & 3), (uint)(100 + index),
                    (ushort)(1u << lane)));
            }
            return evidence;
        }

        private static SigmaConstraintCertificate ProofCertificate(
            ulong block, int lane, SigmaQ48Interval bound, byte source,
            uint key, ushort mask)
        {
            var bounds = new SigmaQ48Interval[SigmaS16.LaneCount];
            for (int coordinate = 0; coordinate < bounds.Length; ++coordinate)
                bounds[coordinate] = SigmaQ48Interval.Full;
            bounds[lane] = bound;
            return new SigmaConstraintCertificate(block, mask, bounds, source,
                key, 11u, SigmaRgbInverse.RoleSupport);
        }

        private static SigmaConstraintCertificate[] CloseProof(
            IReadOnlyList<SigmaConstraintCertificate> evidence,
            IReadOnlyList<SigmaQ48Interval> target) =>
            SigmaRgbInverse.MinimizeProofSet(evidence,
                candidate => BoundsEqual(MeetCertificates(candidate), target));

        private static SigmaQ48Interval[] MeetCertificates(
            IReadOnlyList<SigmaConstraintCertificate> evidence)
        {
            var result = new SigmaQ48Interval[SigmaS16.LaneCount];
            for (int lane = 0; lane < result.Length; ++lane)
                result[lane] = SigmaQ48Interval.Full;
            for (int source = 0; source < evidence.Count; ++source)
            for (int lane = 0; lane < result.Length; ++lane)
                result[lane] = result[lane].Intersect(
                    evidence[source].Bounds[lane]);
            return result;
        }

        private static List<SigmaConstraintCertificate> ReplayWindows(
            IReadOnlyList<SigmaConstraintCertificate> evidence, int window,
            int interleaving)
        {
            int count = (evidence.Count + window - 1) / window;
            var replay = new List<SigmaConstraintCertificate>(evidence.Count);
            for (int ordinal = 0; ordinal < count; ++ordinal)
            {
                int chunk = interleaving == 0 ? ordinal :
                    interleaving == 1 ? count - 1 - ordinal :
                    (ordinal & 1) == 0 ? ordinal / 2 :
                    count - 1 - ordinal / 2;
                int begin = chunk * window;
                int end = Math.Min(evidence.Count, begin + window);
                if (interleaving == 2)
                {
                    for (int index = end - 1; index >= begin; --index)
                        replay.Add(evidence[index]);
                }
                else
                {
                    for (int index = begin; index < end; ++index)
                        replay.Add(evidence[index]);
                }
            }
            return replay;
        }

        private static bool BoundsEqual(
            IReadOnlyList<SigmaQ48Interval> left,
            IReadOnlyList<SigmaQ48Interval> right)
        {
            if (left.Count != right.Count)
                return false;
            for (int lane = 0; lane < left.Count; ++lane)
            {
                if (left[lane] != right[lane])
                    return false;
            }
            return true;
        }

        private static void AssertProofEqual(
            IReadOnlyList<SigmaConstraintCertificate> expected,
            IReadOnlyList<SigmaConstraintCertificate> actual, string context)
        {
            Assert.That(actual.Count, Is.EqualTo(expected.Count), context);
            for (int index = 0; index < expected.Count; ++index)
            {
                Assert.That(actual[index].CompareTo(expected[index]),
                    Is.Zero, $"{context}, certificate={index}");
                Assert.That(BoundsEqual(actual[index].Bounds,
                    expected[index].Bounds), Is.True,
                    $"{context}, bounds={index}");
            }
        }

        private static SigmaStreamUInt4Gpu U4(uint x, uint y, uint z, uint w)
        {
            return new SigmaStreamUInt4Gpu { X = x, Y = y, Z = z, W = w };
        }

        private static BoundsGpu Bounds(long lo, long hi)
        {
            ulong lower = unchecked((ulong)lo);
            ulong upper = unchecked((ulong)hi);
            return new BoundsGpu
            {
                Lo = new UInt2Gpu { X = (uint)lower, Y = (uint)(lower >> 32) },
                Hi = new UInt2Gpu { X = (uint)upper, Y = (uint)(upper >> 32) }
            };
        }

        private static void Bind(ComputeShader shader, int kernel,
            string property, GraphicsBuffer buffer)
        {
            shader.SetBuffer(kernel, property, buffer);
        }

        private static string ReadCompute(string resource)
        {
            ComputeShader shader = Resources.Load<ComputeShader>(resource);
            Assert.That(shader, Is.Not.Null, resource);
            return File.ReadAllText(AssetDatabase.GetAssetPath(shader));
        }

        private static string ReadScript(string fileName)
        {
            foreach (string guid in AssetDatabase.FindAssets(
                Path.GetFileNameWithoutExtension(fileName) + " t:MonoScript"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith('/' + fileName,
                        StringComparison.Ordinal))
                    return File.ReadAllText(path);
            }
            Assert.Fail("Missing script asset " + fileName);
            return string.Empty;
        }
    }
}
