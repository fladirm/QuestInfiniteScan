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
using UnityEngine.Experimental.Rendering;

namespace Genesis.RoomScan.Tests
{
    public sealed class SigmaNativeFrameTests
    {
        [Test]
        public void LiveGraphIsOneBoundedNativeCloseAndLegacyAbiIsAbsent()
        {
            Assert.That(SigmaNativeFrameGraph.HotDispatchCount, Is.EqualTo(16));

            ComputeShader frame = LoadShader("SigmaNativeFrame");
            ComputeShader query = LoadShader("SigmaNativeQuery");
            ComputeShader contract = LoadShader("SigmaNativeContract");
            foreach (string kernel in new[]
            {
                "BuildNativeObservation", "PrepareNativeCanonicalSeed",
                "PrepareNativeCanonicalRuns",
                "PrepareNativeCanonicalSelect",
                "PrepareNativeRefinementProof",
                "PrepareNativeComponentOrder",
                "PrepareNativeRefinementScan",
                "PrepareNativeRefinementPlan", "PrepareNativeRevision",
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

        [TestCase(320 * 320 + 1)]
        [TestCase(2 * 319 * 320 + 1)]
        public void FullFrameLinearDispatchGridIsQuestLegalAndBijective(
            int logicalGroups)
        {
            Vector2Int grid = SigmaGpuKernelTelemetry.ComputeLinearDispatchGrid(
                logicalGroups);
            Assert.That(grid.x, Is.InRange(1,
                SigmaGpuKernelTelemetry.MaximumThreadGroupsPerDimension));
            Assert.That(grid.y, Is.InRange(1,
                SigmaGpuKernelTelemetry.MaximumThreadGroupsPerDimension));
            Assert.That((long)grid.x * grid.y,
                Is.GreaterThanOrEqualTo(logicalGroups));

            var seen = new bool[logicalGroups];
            for (int y = 0; y < grid.y; ++y)
                for (int x = 0; x < grid.x; ++x)
                {
                    int logical = x + y * grid.x;
                    if (logical >= logicalGroups)
                        continue;
                    Assert.That(seen[logical], Is.False,
                        $"logical group {logical} was enumerated twice");
                    seen[logical] = true;
                }
            Assert.That(seen.All(value => value), Is.True);

            string graph = ReadAssetSource("SigmaNativeFrameGraph t:MonoScript");
            Assert.That(Count(graph, "ComputeLinearDispatchGrid("),
                Is.EqualTo(2));
            Assert.That(Count(graph,
                "\"_NativeLinearDispatchWidth\""), Is.EqualTo(2));
            foreach (string shaderName in new[]
            {
                "SigmaNativeContract", "SigmaNativeQuery",
            })
            {
                string shader = File.ReadAllText(AssetDatabase.GetAssetPath(
                    LoadShader(shaderName)));
                Assert.That(shader, Does.Contain(
                    "groupId.x + groupId.y * _NativeLinearDispatchWidth"));
            }
        }

        [Test]
        public void LiveScratchOwnsOneFullFrameArenaAndOneTerminalJournal()
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
                    native.MutationCapacity));
                Assert.That(native.LocalityCertificateWords.count, Is.EqualTo(
                    native.FootprintCertificateOffset + 320 * 320 *
                    SigmaNativeFrameSlotResources.CertificateWordCount));
                Assert.That(native.RelationInputs.count,
                    Is.EqualTo(SigmaNativeFrameSlotResources.RelationCapacity));
                Assert.That(native.FootprintCapacity, Is.EqualTo(320 * 320));
                Assert.That(native.Observation.count,
                    Is.EqualTo(320 * 320 + 1));
                Assert.That(native.BoundaryCapacity, Is.EqualTo(204160));
                Assert.That(native.CloseScratch.count, Is.EqualTo(
                    native.CanonicalRankScratchOffset +
                    native.CanonicalImageStride));
            }
            finally
            {
                resources.Release(slot);
            }
        }

        [Test]
        public void BuildObservationWritesEvery320By320FootprintOnGpu()
        {
            const int width = 320;
            const int height = 320;
            int footprintCount = width * height;
            ComputeShader frame = LoadShader("SigmaNativeFrame");
            int kernel = frame.FindKernel("BuildNativeObservation");
            using var scratch = new SigmaNativeFrameSlotResources(0,
                new Vector2Int(width, height));
            using var gate = UIntBuffer(1);
            using var depthCalibration = UInt2Buffer(72);
            using var rgbCalibration = UInt2Buffer(16);
            using var pose = UInt4Buffer(4);
            using var carrierState = UInt2Buffer(SigmaCarrier.PageLaneCount * 2);
            using var carrierRepresentation = UInt4Buffer(
                SigmaCarrier.SamplesPerPage * 2 *
                SigmaCarrier.RepresentationWordsPerSample);
            using var metadata = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 2,
                SigmaCarrier.PageMetadataStride);
            using var root = UIntBuffer(1);
            using var completion = UInt2Buffer(
                SigmaGeneratedFrame.CompletionWordCount);
            RenderTexture rawDepth = ZeroArrayRenderTexture(width, height,
                GraphicsFormat.R32_SFloat);
            RenderTexture metricDepth = ZeroArrayRenderTexture(width,
                height, GraphicsFormat.R32G32_SFloat);
            RenderTexture depthFlags = ZeroArrayRenderTexture(width, height,
                GraphicsFormat.R32_UInt);
            RenderTexture rays = ZeroRenderTexture(width, height,
                GraphicsFormat.R32G32B32A32_SFloat);
            RenderTexture rgb = ZeroRenderTexture(width, height,
                GraphicsFormat.R32G32B32A32_SFloat);
            RenderTexture predictionPage = ZeroArrayRenderTexture(width,
                height, GraphicsFormat.R32G32B32A32_UInt);
            RenderTexture predictionUv = ZeroArrayRenderTexture(width,
                height, GraphicsFormat.R32G32B32A32_SFloat);
            RenderTexture predictionKey = ZeroArrayRenderTexture(width,
                height, GraphicsFormat.R32G32B32A32_UInt);
            try
            {
                scratch.NativeFrame.SetData(new[]
                {
                    new SigmaNativeFrameGpu
                    {
                        Identity = U4(0xdeadbeefu, 0xaaaaaaaau,
                            0xbbbbbbbbu, 0xccccccccu),
                        Disposition = U4(6u, 7u, 8u, 9u),
                        Evidence = U4(10u, 11u, 12u, 13u),
                        Publication = U4(14u, 15u, 16u, 17u),
                    },
                });
                root.SetData(new[] { 5u });
                frame.SetBuffer(kernel, "_SigmaExactBackendGate", gate);
                frame.SetBuffer(kernel, "_DepthCalibrationQ48",
                    depthCalibration);
                frame.SetBuffer(kernel, "_RgbCalibrationQ48", rgbCalibration);
                frame.SetBuffer(kernel, "_PoseResult", pose);
                frame.SetBuffer(kernel, "_NativeFrames", scratch.NativeFrame);
                frame.SetBuffer(kernel, "_NativeObservations",
                    scratch.Observation);
                frame.SetBuffer(kernel, "_NativeCloseScratch",
                    scratch.CloseScratch);
                frame.SetBuffer(kernel, "_NativeStates", scratch.States);
                frame.SetBuffer(kernel, "_NativeLocalityCertificateWords",
                    scratch.LocalityCertificateWords);
                frame.SetBuffer(kernel, "_NativeCounters", scratch.Counters);
                frame.SetBuffer(kernel, "_NativeCompletionJournal", completion);
                frame.SetBuffer(kernel, "_NativeSourceCarrierState",
                    carrierState);
                frame.SetBuffer(kernel, "_NativeSourceCarrierRepresentation",
                    carrierRepresentation);
                frame.SetBuffer(kernel, "_NativeSourcePageMetadata", metadata);
                frame.SetBuffer(kernel, "_NativeSourcePublicationRoot", root);
                frame.SetTexture(kernel, "_NativeRawDepth", rawDepth);
                frame.SetTexture(kernel, "_NativeMetricDepth", metricDepth);
                frame.SetTexture(kernel, "_NativeDepthFlags", depthFlags);
                frame.SetTexture(kernel, "_NativeDepthRayCenterLeft", rays);
                frame.SetTexture(kernel, "_NativeDepthRayCenterRight", rays);
                frame.SetTexture(kernel, "_NativeDepthRayDifferentialXLeft",
                    rays);
                frame.SetTexture(kernel, "_NativeDepthRayDifferentialXRight",
                    rays);
                frame.SetTexture(kernel, "_NativeDepthRayDifferentialYLeft",
                    rays);
                frame.SetTexture(kernel, "_NativeDepthRayDifferentialYRight",
                    rays);
                frame.SetTexture(kernel, "_NativeDepthSlopeBoundsLeft", rays);
                frame.SetTexture(kernel, "_NativeDepthSlopeBoundsRight", rays);
                frame.SetTexture(kernel, "_NativeRgbLeft", rgb);
                frame.SetTexture(kernel, "_NativeRgbRight", rgb);
                frame.SetTexture(kernel, "_NativePredCarrierPage",
                    predictionPage);
                frame.SetTexture(kernel, "_NativePredCarrierUvNormal",
                    predictionUv);
                frame.SetTexture(kernel, "_NativePredStateKey", predictionKey);
                frame.SetInts("_NativeResolution", width, height);
                frame.SetInts("_NativeRgbLeftResolution", width, height);
                frame.SetInts("_NativeRgbRightResolution", width, height);
                frame.SetInts("_NativeOpticalTransfers", -1, -1);
                frame.SetInt("_NativeRevision", 17);
                frame.SetInt("_NativeCalibrationEpoch", 9);
                frame.SetInts("_NativeIndependenceKeys", 1, 2, 3, 4);
                frame.SetInt("_NativeTargetPageCapacity", 2);
                frame.SetInt("_NativeTargetSegmentIndex", 0);
                frame.SetInt("_NativeCompletionRecordIndex", 0);
                frame.SetInt("_NativeFootprintCount", footprintCount);
                frame.SetInt("_NativeBoundaryCount", scratch.BoundaryCapacity);
                frame.SetInt("_NativeBoundaryScratchOffset",
                    scratch.BoundaryScratchOffset);
                frame.SetMatrix("_NativeRoomFromDepthLeft", Matrix4x4.identity);
                frame.SetMatrix("_NativeRoomFromDepthRight", Matrix4x4.identity);
                frame.SetMatrix("_NativeRgbFromRoomLeft", Matrix4x4.identity);
                frame.SetMatrix("_NativeRgbFromRoomRight", Matrix4x4.identity);
                frame.SetMatrix("_PoseConsumeReferenceFromWorld",
                    Matrix4x4.identity);
                frame.SetMatrix("_PoseConsumeWorldFromReference",
                    Matrix4x4.identity);

                scratch.NativeFrame.SetData(new SigmaNativeFrameGpu[1]);
                root.SetData(new[] { 4u });
                frame.SetInt("_NativeRevision", 16);
                frame.SetInt("_NativeCalibrationEpoch", 8);
                frame.Dispatch(kernel, (footprintCount + 7) / 8, 1, 1);
                var zeroInitialized = new SigmaNativeFrameGpu[1];
                scratch.NativeFrame.GetData(zeroInitialized);
                Assert.That(zeroInitialized[0].Identity.X, Is.EqualTo(16u));
                Assert.That(zeroInitialized[0].Identity.Y, Is.EqualTo(8u));
                Assert.That(zeroInitialized[0].Disposition.X,
                    Is.EqualTo((uint)SigmaNativeFrameDisposition.GpuOwned));
                Assert.That(zeroInitialized[0].Publication.X, Is.EqualTo(4u));

                scratch.NativeFrame.SetData(new[]
                {
                    new SigmaNativeFrameGpu
                    {
                        Identity = U4(0xdeadbeefu, 0xaaaaaaaau,
                            0xbbbbbbbbu, 0xccccccccu),
                        Disposition = U4(6u, 7u, 8u, 9u),
                        Evidence = U4(10u, 11u, 12u, 13u),
                        Publication = U4(14u, 15u, 16u, 17u),
                    },
                });
                root.SetData(new[] { 5u });
                frame.SetInt("_NativeRevision", 17);
                frame.SetInt("_NativeCalibrationEpoch", 9);
                frame.Dispatch(kernel, (footprintCount + 7) / 8, 1, 1);

                var first = new SigmaNativeObservationGpu[1];
                var last = new SigmaNativeObservationGpu[1];
                scratch.Observation.GetData(first, 0, 1, 1);
                scratch.Observation.GetData(last, 0, footprintCount, 1);
                Assert.That(first[0].Query.X, Is.Zero);
                Assert.That(first[0].Query.Y, Is.Zero);
                Assert.That(last[0].Identity.X, Is.EqualTo(17u));
                Assert.That(last[0].Identity.Y, Is.EqualTo(9u));
                Assert.That(last[0].Query.X, Is.EqualTo(319u));
                Assert.That(last[0].Query.Y, Is.EqualTo(319u));
                var initialized = new SigmaNativeFrameGpu[1];
                scratch.NativeFrame.GetData(initialized);
                Assert.That(initialized[0].Identity.X, Is.EqualTo(17u));
                Assert.That(initialized[0].Identity.Y, Is.EqualTo(9u));
                Assert.That(initialized[0].Identity.W,
                    Is.EqualTo((uint)SigmaNativeColdReason.None));
                Assert.That(initialized[0].Disposition.X,
                    Is.EqualTo((uint)SigmaNativeFrameDisposition.GpuOwned));
                Assert.That(initialized[0].Disposition.Y, Is.Zero);
                Assert.That(initialized[0].Disposition.Z, Is.Zero);
                Assert.That(initialized[0].Disposition.W, Is.Zero);
                Assert.That(initialized[0].Publication.X, Is.EqualTo(5u));
                Assert.That(initialized[0].Publication.Y, Is.Zero);
                Assert.That(initialized[0].Publication.Z, Is.Zero);
                Assert.That(initialized[0].Publication.W, Is.Zero);
                var support = new UInt2[2];
                scratch.CloseScratch.GetData(support, 0,
                    (footprintCount - 1) *
                    SigmaNativeFrameSlotResources.FootprintEvidenceWordCount +
                    50, 2);
                Assert.That(support[0].Low, Is.EqualTo(uint.MaxValue));
                Assert.That(support[0].High, Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(rawDepth);
                UnityEngine.Object.DestroyImmediate(metricDepth);
                UnityEngine.Object.DestroyImmediate(depthFlags);
                UnityEngine.Object.DestroyImmediate(rays);
                UnityEngine.Object.DestroyImmediate(rgb);
                UnityEngine.Object.DestroyImmediate(predictionPage);
                UnityEngine.Object.DestroyImmediate(predictionUv);
                UnityEngine.Object.DestroyImmediate(predictionKey);
            }
        }

        [Test]
        public void ProductionBoundaryPlaneMatchesAcceptedGeneratedStitch()
        {
            ComputeShader query = LoadShader("SigmaNativeQuery");
            int kernel = query.FindKernel("EvaluateNativeRelation");
            UnityEngine.Rendering.LocalKeyword boundaryVariant = new(query,
                "SIGMA_N4_BOUNDARY_VARIANT");
            query.SetKeyword(boundaryVariant, true);
            using var scratch = new SigmaNativeFrameSlotResources(0,
                new Vector2Int(2, 1));

            SigmaS16 leftState = SigmaS16.Basis(1, SigmaNumericDomain.One);
            SigmaS16 rightState = SigmaS16.Basis(2, SigmaNumericDomain.One);
            var states = new UInt2[scratch.States.count];
            WriteState(states, scratch.FootprintStateOffset, leftState);
            WriteState(states, scratch.FootprintStateOffset +
                SigmaS16.LaneCount, rightState);
            scratch.States.SetData(states);

            const uint evidenceFlags = 0x1fu;
            var observations = new SigmaNativeObservationGpu[3];
            observations[1].Identity = U4(1u, 1u, 11u, evidenceFlags);
            observations[2].Identity = U4(1u, 1u, 12u, evidenceFlags);
            scratch.Observation.SetData(observations);

            var close = new UInt2[scratch.CloseScratch.count];
            UInt2 point = Packed(SigmaNumericDomain.One);
            WriteEnvelope(close, 0,
                SigmaNativeFrameSlotResources.FootprintEvidenceWordCount,
                side: 1, point);
            WriteEnvelope(close, 1,
                SigmaNativeFrameSlotResources.FootprintEvidenceWordCount,
                side: 0, point);
            scratch.CloseScratch.SetData(close);

            query.SetBuffer(kernel, "_NativeStates", scratch.States);
            query.SetBuffer(kernel, "_NativeRelationInputs",
                scratch.RelationInputs);
            query.SetBuffer(kernel, "_NativeRelationPlans",
                scratch.RelationPlans);
            query.SetBuffer(kernel, "_NativeRelationNearIntervals",
                scratch.RelationNearIntervals);
            query.SetBuffer(kernel, "_NativeRelationResults",
                scratch.RelationResults);
            query.SetBuffer(kernel, "_NativeRelationFactors",
                scratch.RelationFactors);
            query.SetBuffer(kernel, "_NativeRelationHashes",
                scratch.RelationHashes);
            query.SetBuffer(kernel, "_NativeRelationNorms",
                scratch.RelationNorms);
            query.SetBuffer(kernel, "_NativeObservations",
                scratch.Observation);
            query.SetBuffer(kernel, "_NativeCloseScratch",
                scratch.CloseScratch);
            query.SetBuffer(kernel, "_NativeLocalityCertificateWords",
                scratch.LocalityCertificateWords);
            query.SetBuffer(kernel, "_NativeCounters", scratch.Counters);
            query.SetInt("_NativeEntryPointIndex",
                SigmaGeneratedFrame.IntrinsicRelationEntryPoint);
            query.SetInt("_NativeRelationCount", 1);
            query.SetInt("_NativeRelationMode", 1);
            query.SetInts("_NativeResolution", 2, 1);
            query.SetInt("_NativeFootprintCount", 2);
            query.SetInt("_NativeFootprintStateOffset",
                scratch.FootprintStateOffset);
            query.SetInt("_NativeBoundaryCount", 1);
            query.SetInt("_NativeBoundaryScratchOffset",
                scratch.BoundaryScratchOffset);
            query.Dispatch(kernel, 2, 1, 1);

            var receipt = new UInt2[6];
            scratch.CloseScratch.GetData(receipt, 0,
                scratch.BoundaryScratchOffset, receipt.Length);
            var gpu = new UInt4
            {
                X = receipt[0].Low,
                Y = receipt[0].High,
                Z = receipt[1].Low,
                W = receipt[1].High,
            };

            var contact = new SigmaStitchContactBranch(new[]
            {
                new SigmaQ48Interval(SigmaNumericDomain.One,
                    SigmaNumericDomain.One),
                new SigmaQ48Interval(SigmaNumericDomain.One,
                    SigmaNumericDomain.One),
                new SigmaQ48Interval(SigmaNumericDomain.One,
                    SigmaNumericDomain.One),
            });
            var boundary = new SigmaImplicitBoundaryRef(0, 11UL, 22UL,
                SigmaSampleBoundarySide.Right, SigmaSampleBoundarySide.Left,
                new[] { contact });
            SigmaStitchWitnessSet cpu = SigmaGeneratedMerkabaProgram
                .EvaluateModalStitch(boundary,
                    new SigmaStitchLocality(11UL, 0, leftState,
                        new string('a', 64)),
                    new SigmaStitchLocality(22UL, 0, rightState,
                        new string('b', 64)),
                    new SigmaStitchNativeContext(new string('c', 64)));
            Assert.That(gpu.X, Is.EqualTo((uint)cpu.Resolution));
            Assert.That(gpu.Y, Is.EqualTo((uint)cpu.Resolved.LeftSector));
            Assert.That(gpu.Z, Is.EqualTo((uint)cpu.Resolved.RightSector));
            Assert.That(gpu.W,
                Is.EqualTo((uint)cpu.Resolved.Receipt.TransportAddress));

            // The old zero-context shortcut would resolve this pair from its
            // two exact links alone.  The complete intrinsic basis profile is
            // nonzero, so production must change closure exactly like generated
            // CPU authority without persisting the 16 full-S16 profile factors.
            SigmaS16 profileLeft = SigmaS16.Basis(0,
                SigmaNumericDomain.One);
            SigmaS16 profileRight = SigmaS16.Basis(3,
                SigmaNumericDomain.One);
            Array.Clear(states, 0, states.Length);
            WriteState(states, scratch.FootprintStateOffset, profileLeft);
            WriteState(states, scratch.FootprintStateOffset +
                SigmaS16.LaneCount, profileRight);
            scratch.States.SetData(states);
            query.Dispatch(kernel, 2, 1, 1);
            scratch.CloseScratch.GetData(receipt, 0,
                scratch.BoundaryScratchOffset, receipt.Length);
            SigmaStitchWitnessSet profileCpu = SigmaGeneratedMerkabaProgram
                .EvaluateModalStitch(boundary,
                    new SigmaStitchLocality(11UL, 0, profileLeft,
                        new string('d', 64)),
                    new SigmaStitchLocality(22UL, 0, profileRight,
                        new string('e', 64)),
                    new SigmaStitchNativeContext(new string('f', 64)));
            SigmaStitchRelationReceipt profileDecisive = profileCpu.Receipts
                .Single(value =>
                    value.LeftSector == SigmaNativeBoundarySector.Sector0 &&
                    value.RightSector == SigmaNativeBoundarySector.Sector1);
            Assert.That(profileDecisive.LinkClass,
                Is.EqualTo(SigmaExactFactorClass.ProvenExactClosed));
            Assert.That(profileDecisive.ReverseLinkClass,
                Is.EqualTo(SigmaExactFactorClass.ProvenExactClosed));
            Assert.That(profileDecisive.NonzeroAssociatorProfile, Is.True);
            Assert.That(profileDecisive.AssociatorClass,
                Is.Not.EqualTo(SigmaExactFactorClass.ProvenExactClosed));
            Assert.That(receipt[0].Low,
                Is.EqualTo((uint)profileCpu.Resolution));
            Assert.That(profileCpu.Resolution,
                Is.Not.EqualTo(SigmaStitchResolution.Resolved));

            Array.Clear(states, 0, states.Length);
            WriteState(states, scratch.FootprintStateOffset, leftState);
            WriteState(states, scratch.FootprintStateOffset +
                SigmaS16.LaneCount, rightState);
            scratch.States.SetData(states);

            // Sampling RIGHT/DOWN is broad phase only. Re-enumerating the
            // identical native endpoints through the vertical footprint side
            // must leave the generated stitch result byte-identical.
            Array.Clear(close, 0, close.Length);
            WriteEnvelope(close, 0,
                SigmaNativeFrameSlotResources.FootprintEvidenceWordCount,
                side: 3, point);
            WriteEnvelope(close, 1,
                SigmaNativeFrameSlotResources.FootprintEvidenceWordCount,
                side: 2, point);
            scratch.CloseScratch.SetData(close);
            query.SetInts("_NativeResolution", 1, 2);
            query.Dispatch(kernel, 2, 1, 1);
            scratch.CloseScratch.GetData(receipt, 0,
                scratch.BoundaryScratchOffset, receipt.Length);
            Assert.That(receipt[0].Low, Is.EqualTo(gpu.X));
            Assert.That(receipt[0].High, Is.EqualTo(gpu.Y));
            Assert.That(receipt[1].Low, Is.EqualTo(gpu.Z));
            Assert.That(receipt[1].High, Is.EqualTo(gpu.W));

            // Adjacent execution samples do not stitch when their exact
            // calibrated manifestation envelopes are disjoint.
            Array.Clear(close, 0, close.Length);
            WriteEnvelope(close, 0,
                SigmaNativeFrameSlotResources.FootprintEvidenceWordCount,
                side: 1, point);
            WriteEnvelope(close, 1,
                SigmaNativeFrameSlotResources.FootprintEvidenceWordCount,
                side: 0, Packed(checked(SigmaNumericDomain.One * 2L)));
            scratch.CloseScratch.SetData(close);
            query.SetInts("_NativeResolution", 2, 1);
            query.Dispatch(kernel, 2, 1, 1);
            scratch.CloseScratch.GetData(receipt, 0,
                scratch.BoundaryScratchOffset, receipt.Length);
            Assert.That(receipt[0].Low,
                Is.EqualTo((uint)SigmaStitchResolution.NoStitch));

            // A valid first-hit carrying no resolved S16 endpoint does not
            // become a physical tear. All native sector alternatives survive,
            // so the generated set result is explicitly unresolved.
            scratch.States.SetData(new UInt2[scratch.States.count]);
            Array.Clear(close, 0, close.Length);
            WriteEnvelope(close, 0,
                SigmaNativeFrameSlotResources.FootprintEvidenceWordCount,
                side: 1, point);
            WriteEnvelope(close, 1,
                SigmaNativeFrameSlotResources.FootprintEvidenceWordCount,
                side: 0, point);
            scratch.CloseScratch.SetData(close);
            query.Dispatch(kernel, 2, 1, 1);
            scratch.CloseScratch.GetData(receipt, 0,
                scratch.BoundaryScratchOffset, receipt.Length);
            Assert.That(receipt[0].Low,
                Is.EqualTo((uint)SigmaStitchResolution.Unresolved));
        }

        [Test]
        public void ProductionTileCloseMatchesBoundedN2ClosureAndMeets256Hits()
        {
            const int width = 16;
            const int height = 16;
            const uint evidenceFlags = (uint)(
                SigmaNativeObservationFlags.Coherent |
                SigmaNativeObservationFlags.LeftFirstHit |
                SigmaNativeObservationFlags.RightFirstHit |
                SigmaNativeObservationFlags.LeftEvidence |
                SigmaNativeObservationFlags.RightEvidence);
            ComputeShader query = LoadShader("SigmaNativeQuery");
            ComputeShader contract = LoadShader("SigmaNativeContract");
            int relation = query.FindKernel("EvaluateNativeRelation");
            int close = contract.FindKernel("ContractNativeQuery");
            UnityEngine.Rendering.LocalKeyword boundaryVariant = new(query,
                "SIGMA_N4_BOUNDARY_VARIANT");
            query.SetKeyword(boundaryVariant, true);
            UnityEngine.Rendering.LocalKeyword tileVariant = new(contract,
                "SIGMA_N4_TILE_CLOSE_VARIANT");
            using var scratch = new SigmaNativeFrameSlotResources(0,
                new Vector2Int(width, height));
            using var carrierState = UInt2Buffer(SigmaCarrier.PageLaneCount);
            using var carrierRepresentation = UInt4Buffer(
                SigmaCarrier.SamplesPerPage *
                SigmaCarrier.RepresentationWordsPerSample);
            using var completion = UInt2Buffer(
                SigmaGeneratedFrame.CompletionWordCount);

            void BindClose()
            {
                contract.SetBuffer(close, "_NativeReverseRelationResults",
                    scratch.RelationResults);
                contract.SetBuffer(close, "_NativeStates", scratch.States);
                contract.SetBuffer(close, "_NativeFreshEvidenceWords",
                    completion);
                contract.SetBuffer(close, "_NativeObservations",
                    scratch.Observation);
                contract.SetBuffer(close, "_NativeCloseScratch",
                    scratch.CloseScratch);
                contract.SetBuffer(close, "_NativeSourceCarrierState",
                    carrierState);
                contract.SetBuffer(close,
                    "_NativeSourceCarrierRepresentation",
                    carrierRepresentation);
                contract.SetBuffer(close, "_NativeBranchHeaders",
                    scratch.BranchHeaders);
                contract.SetBuffer(close, "_NativeBranchSupports",
                    scratch.BranchSupports);
                contract.SetBuffer(close, "_NativeBranchPredictions",
                    scratch.BranchPredictions);
                contract.SetBuffer(close, "_NativeLocalityCertificateWords",
                    scratch.LocalityCertificateWords);
                contract.SetBuffer(close, "_NativeCounters",
                    scratch.Counters);
                contract.SetInt("_NativeFootprintCount",
                    scratch.FootprintCapacity);
                contract.SetInt("_NativeFootprintStateOffset",
                    scratch.FootprintStateOffset);
                contract.SetInt("_NativeFootprintCertificateOffset",
                    scratch.FootprintCertificateOffset);
                contract.SetInts("_NativeResolution", width, height);
                contract.SetInts("_NativeTileCount", 1, 1);
                contract.SetInt("_NativeBoundaryCount",
                    scratch.BoundaryCapacity);
                contract.SetInt("_NativeBoundaryScratchOffset",
                    scratch.BoundaryScratchOffset);
                contract.SetInt("_NativeTileHeaderScratchOffset",
                    scratch.TileHeaderScratchOffset);
                contract.SetInt("_NativeTileFootprintScratchOffset",
                    scratch.TileFootprintScratchOffset);
                contract.SetInt("_NativeTileSupportSummaryScratchOffset",
                    scratch.TileSupportSummaryScratchOffset);
                contract.SetInt("_NativeTileComponentSummaryScratchOffset",
                    scratch.TileComponentSummaryScratchOffset);
                contract.SetInt("_NativeGlobalHeaderScratchOffset",
                    scratch.GlobalHeaderScratchOffset);
                contract.SetInt("_NativeActiveSupportMarkerScratchOffset",
                    scratch.ActiveSupportMarkerScratchOffset);
                contract.SetInt("_NativeActiveSupportListScratchOffset",
                    scratch.ActiveSupportListScratchOffset);
                contract.SetInt("_NativeSupportLocatorCapacity",
                    SigmaNativeFrameSlotResources.SupportLocatorCapacity);
                contract.SetInt("_NativeRevision", 1);
            }

            query.SetBuffer(relation, "_NativeStates", scratch.States);
            query.SetBuffer(relation, "_NativeRelationInputs",
                scratch.RelationInputs);
            query.SetBuffer(relation, "_NativeRelationPlans",
                scratch.RelationPlans);
            query.SetBuffer(relation, "_NativeRelationNearIntervals",
                scratch.RelationNearIntervals);
            query.SetBuffer(relation, "_NativeRelationResults",
                scratch.RelationResults);
            query.SetBuffer(relation, "_NativeRelationFactors",
                scratch.RelationFactors);
            query.SetBuffer(relation, "_NativeRelationHashes",
                scratch.RelationHashes);
            query.SetBuffer(relation, "_NativeRelationNorms",
                scratch.RelationNorms);
            query.SetBuffer(relation, "_NativeObservations",
                scratch.Observation);
            query.SetBuffer(relation, "_NativeCloseScratch",
                scratch.CloseScratch);
            query.SetBuffer(relation, "_NativeLocalityCertificateWords",
                scratch.LocalityCertificateWords);
            query.SetBuffer(relation, "_NativeCounters", scratch.Counters);
            query.SetInt("_NativeEntryPointIndex",
                SigmaGeneratedFrame.IntrinsicRelationEntryPoint);
            query.SetInt("_NativeRelationCount", 1);
            query.SetInt("_NativeRelationMode", 1);
            query.SetInts("_NativeResolution", width, height);
            query.SetInt("_NativeFootprintCount", scratch.FootprintCapacity);
            query.SetInt("_NativeFootprintStateOffset",
                scratch.FootprintStateOffset);
            query.SetInt("_NativeBoundaryCount", scratch.BoundaryCapacity);
            query.SetInt("_NativeBoundaryScratchOffset",
                scratch.BoundaryScratchOffset);
            query.SetInts("_NativeTileCount", scratch.TileCountX,
                scratch.TileCountY);
            query.SetInt("_NativeTileHeaderScratchOffset",
                scratch.TileHeaderScratchOffset);
            query.SetInt("_NativeTileFootprintScratchOffset",
                scratch.TileFootprintScratchOffset);
            query.SetInt("_NativeTileSupportSummaryScratchOffset",
                scratch.TileSupportSummaryScratchOffset);
            query.SetInt("_NativeTileComponentSummaryScratchOffset",
                scratch.TileComponentSummaryScratchOffset);
            query.SetInt("_NativeGlobalHeaderScratchOffset",
                scratch.GlobalHeaderScratchOffset);
            query.SetInt("_NativeActiveSupportListScratchOffset",
                scratch.ActiveSupportListScratchOffset);
            query.SetInt("_NativeGlobalParentScratchOffset",
                scratch.GlobalParentScratchOffset);
            query.SetInt("_NativeGlobalTransformScratchOffset",
                scratch.GlobalTransformScratchOffset);
            query.SetInt("_NativeGlobalBorderComponentCapacity",
                scratch.GlobalBorderComponentCapacity);
            BindClose();

            // The accepted intrinsic six-mode cycle occupies only a disposable
            // 2x3 footprint perimeter. Sampling positions do not supply the
            // native sectors or chart orientation.
            int[] cycleFootprints = { 1, 0, 16, 32, 33, 17 };
            SigmaS16[] cycleStates =
            {
                SigmaS16.Basis(1, -SigmaNumericDomain.One),
                SigmaS16.Basis(2, SigmaNumericDomain.One),
                SigmaS16.Basis(4, -SigmaNumericDomain.One),
                SigmaS16.Basis(1, SigmaNumericDomain.One),
                SigmaS16.Basis(2, -SigmaNumericDomain.One),
                SigmaS16.Basis(8, SigmaNumericDomain.One),
            };
            var observations = new SigmaNativeObservationGpu[
                scratch.FootprintCapacity + 1];
            var states = new UInt2[scratch.States.count];
            var certificates = new UInt4[
                scratch.LocalityCertificateWords.count];
            var arena = new UInt2[scratch.CloseScratch.count];
            UInt2 contactPoint = Packed(SigmaNumericDomain.One);
            for (int node = 0; node < cycleFootprints.Length; ++node)
            {
                int footprint = cycleFootprints[node];
                observations[footprint + 1].Identity = U4(1u, 1u,
                    (uint)(node + 1), evidenceFlags);
                WriteState(states, scratch.FootprintStateOffset +
                    footprint * SigmaS16.LaneCount, cycleStates[node]);
                int certificate = scratch.FootprintCertificateOffset +
                    footprint * SigmaNativeFrameSlotResources
                        .CertificateWordCount;
                certificates[certificate] = new UInt4
                {
                    X = (uint)(SigmaNativeCertificateFlags.Valid |
                        SigmaNativeCertificateFlags.Directional |
                        SigmaNativeCertificateFlags.Minimized),
                    Y = 1u,
                    Z = 1u,
                };
                for (int side = 0; side < 4; ++side)
                    WriteEnvelope(arena, footprint,
                        SigmaNativeFrameSlotResources
                            .FootprintEvidenceWordCount,
                        side, contactPoint);
            }
            // A 2x3 sampling rectangle has a seventh shared middle boundary
            // in addition to the six-edge perimeter cycle.  The accepted N2
            // fixture has exactly the perimeter edges, so make that interior
            // sampling boundary an exact calibrated gap instead of silently
            // adding a different native constraint problem.
            WriteEnvelope(arena, 16,
                SigmaNativeFrameSlotResources.FootprintEvidenceWordCount,
                side: 1, contactPoint);
            WriteEnvelope(arena, 17,
                SigmaNativeFrameSlotResources.FootprintEvidenceWordCount,
                side: 0, Packed(checked(SigmaNumericDomain.One * 2L)));
            scratch.Observation.SetData(observations);
            scratch.States.SetData(states);
            scratch.LocalityCertificateWords.SetData(certificates);
            scratch.CloseScratch.SetData(arena);
            query.Dispatch(relation, scratch.BoundaryCapacity + 1, 1, 1);
            contract.SetKeyword(tileVariant, true);
            contract.Dispatch(close, 1, 1, 1);
            contract.SetKeyword(tileVariant, false);

            var header = new UInt2[2];
            scratch.CloseScratch.GetData(header, 0,
                scratch.TileHeaderScratchOffset, header.Length);
            scratch.CloseScratch.GetData(arena);
            string cycleReceipts = string.Join(" | ",
                new[] { 0, 240, 256, 30, 257, 241 }
                .Select(boundary =>
                {
                    int address = scratch.BoundaryScratchOffset + boundary *
                        SigmaNativeFrameSlotResources.BoundaryReceiptWordCount;
                    return $"b{boundary}:" + string.Join(",",
                        Enumerable.Range(0, 6).Select(word =>
                            $"{arena[address + word].Low:x8}/" +
                            $"{arena[address + word].High:x8}"));
                }));
            var componentReceipt = new UInt2[2];
            scratch.CloseScratch.GetData(componentReceipt, 0,
                scratch.TileComponentSummaryScratchOffset,
                componentReceipt.Length);
            string chartReceipts = string.Join(" | ", cycleFootprints.Select(
                footprint =>
                {
                    int address = scratch.TileFootprintScratchOffset +
                        footprint * SigmaNativeFrameSlotResources
                            .TileFootprintReceiptWordCount;
                    return $"p{footprint}:" + string.Join(",",
                        Enumerable.Range(0, 4).Select(slot =>
                            $"{arena[address + slot * 2].Low:x8}/" +
                            $"{arena[address + slot * 2].High:x8}," +
                            $"{arena[address + slot * 2 + 1].Low:x8}/" +
                            $"{arena[address + slot * 2 + 1].High:x8}"));
                }));
            Assert.That(header[0].Low, Is.Zero,
                "Fresh cycle must not mint a published-support summary.");
            Assert.That(header[0].High, Is.EqualTo(1u));
            Assert.That(header[1].Low, Is.EqualTo(1u));
            Assert.That(header[1].High, Is.Zero,
                "The accepted N2 exact cycle must have one D4 orbit class; " +
                $"component={componentReceipt[0].Low:x8}/" +
                $"{componentReceipt[0].High:x8}," +
                $"{componentReceipt[1].Low:x8}/" +
                $"{componentReceipt[1].High:x8}; {cycleReceipts}; " +
                chartReceipts);

            // Corrupt one redundant abstract-incidence sector.  Native
            // transport is independently proved by Query; CLOSE must retain
            // this chart inconsistency as unresolved and never repair it.
            int redundantBoundary = scratch.BoundaryScratchOffset +
                241 * SigmaNativeFrameSlotResources.BoundaryReceiptWordCount;
            scratch.CloseScratch.GetData(arena);
            arena[redundantBoundary + 1].Low ^= 1u;
            scratch.CloseScratch.SetData(arena);
            contract.SetKeyword(tileVariant, true);
            contract.Dispatch(close, 1, 1, 1);
            contract.SetKeyword(tileVariant, false);
            scratch.CloseScratch.GetData(header, 0,
                scratch.TileHeaderScratchOffset, header.Length);
            Assert.That(header[1].High, Is.EqualTo(1u),
                "A contradictory redundant edge must remain unresolved.");

            // All 256 footprint records now prove one already-published support.
            // The exact interval meet is parallel and has no member capacity.
            observations = new SigmaNativeObservationGpu[
                scratch.FootprintCapacity + 1];
            states = new UInt2[scratch.States.count];
            certificates = new UInt4[scratch.LocalityCertificateWords.count];
            arena = new UInt2[scratch.CloseScratch.count];
            SigmaS16 shared = SigmaS16.Basis(1, SigmaNumericDomain.One);
            for (int footprint = 0; footprint < scratch.FootprintCapacity;
                 ++footprint)
            {
                observations[footprint + 1].Identity = U4(2u, 1u,
                    (uint)(footprint + 1), evidenceFlags |
                    (uint)SigmaNativeObservationFlags.PriorSupport);
                WriteState(states, scratch.FootprintStateOffset +
                    footprint * SigmaS16.LaneCount, shared);
                int certificate = scratch.FootprintCertificateOffset +
                    footprint * SigmaNativeFrameSlotResources
                        .CertificateWordCount;
                certificates[certificate] = new UInt4
                {
                    X = (uint)(SigmaNativeCertificateFlags.Valid |
                        SigmaNativeCertificateFlags.Directional |
                        SigmaNativeCertificateFlags.Minimized),
                    Y = 7u,
                    Z = 1u,
                };
                certificates[certificate + 1] = new UInt4
                    { X = 11u, Y = 12u, Z = 13u, W = 14u };
                certificates[certificate + 3] = new UInt4
                    { Y = (uint)SigmaMerkabaRelationClass.Regular };
                certificates[certificate + 12] = new UInt4
                    { X = 21u, Y = 22u, Z = 23u, W = 24u };
                certificates[certificate + 13] = new UInt4
                    { X = 31u, Y = 32u, Z = 33u, W = 34u };
                certificates[certificate + 15] = new UInt4
                    { X = 41u, Y = 42u, Z = 43u, W = 44u };
                long lower = -100L + footprint;
                long upper = 1000L - footprint;
                UInt2 packedLower = Packed(lower);
                UInt2 packedUpper = Packed(upper);
                for (int axis = 0; axis < 4; ++axis)
                    certificates[certificate + 4 + axis] = new UInt4
                    {
                        X = packedLower.Low,
                        Y = packedLower.High,
                        Z = packedUpper.Low,
                        W = packedUpper.High,
                    };
                int support = footprint *
                    SigmaNativeFrameSlotResources.FootprintEvidenceWordCount +
                    50;
                arena[support] = new UInt2 { Low = 0u, High = 0u };
            }
            scratch.Observation.SetData(observations);
            scratch.States.SetData(states);
            scratch.LocalityCertificateWords.SetData(certificates);
            scratch.CloseScratch.SetData(arena);
            contract.SetKeyword(tileVariant, true);
            contract.Dispatch(close, 1, 1, 1);
            contract.SetKeyword(tileVariant, false);
            scratch.CloseScratch.GetData(header, 0,
                scratch.TileHeaderScratchOffset, header.Length);
            Assert.That(header[0].Low, Is.EqualTo(1u));
            Assert.That(header[0].High, Is.EqualTo(1u));
            Assert.That(header[1].High, Is.Zero);
            var supportSummary = new UInt2[2];
            scratch.CloseScratch.GetData(supportSummary, 0,
                scratch.TileSupportSummaryScratchOffset,
                supportSummary.Length);
            Assert.That(supportSummary[0].Low, Is.Zero);
            Assert.That(supportSummary[1].Low, Is.EqualTo(256u),
                "Every same-support member must enter the exact meet.");
            Assert.That(supportSummary[1].High, Is.EqualTo(1u));
            var mergedAxis = new UInt4[1];
            scratch.LocalityCertificateWords.GetData(mergedAxis, 0,
                scratch.FootprintCertificateOffset + 4, 1);
            UInt2 expectedLower = Packed(155L);
            UInt2 expectedUpper = Packed(745L);
            Assert.That(mergedAxis[0].X, Is.EqualTo(expectedLower.Low));
            Assert.That(mergedAxis[0].Y, Is.EqualTo(expectedLower.High));
            Assert.That(mergedAxis[0].Z, Is.EqualTo(expectedUpper.Low));
            Assert.That(mergedAxis[0].W, Is.EqualTo(expectedUpper.High));
        }

        [Test]
        public void ProductionGlobalCloseJoinsTilesAndRejectsInconsistentCycle()
        {
            const int width = 32;
            const int height = 16;
            const uint evidenceFlags = (uint)(
                SigmaNativeObservationFlags.Coherent |
                SigmaNativeObservationFlags.LeftFirstHit |
                SigmaNativeObservationFlags.RightFirstHit |
                SigmaNativeObservationFlags.LeftEvidence |
                SigmaNativeObservationFlags.RightEvidence);
            ComputeShader query = LoadShader("SigmaNativeQuery");
            ComputeShader contract = LoadShader("SigmaNativeContract");
            int relation = query.FindKernel("EvaluateNativeRelation");
            int close = contract.FindKernel("ContractNativeQuery");
            UnityEngine.Rendering.LocalKeyword boundaryVariant = new(query,
                "SIGMA_N4_BOUNDARY_VARIANT");
            UnityEngine.Rendering.LocalKeyword globalVariant = new(query,
                "SIGMA_N4_GLOBAL_CLOSE_VARIANT");
            UnityEngine.Rendering.LocalKeyword tileVariant = new(contract,
                "SIGMA_N4_TILE_CLOSE_VARIANT");
            using var scratch = new SigmaNativeFrameSlotResources(0,
                new Vector2Int(width, height));
            using var carrierState = UInt2Buffer(SigmaCarrier.PageLaneCount);
            using var carrierRepresentation = UInt4Buffer(
                SigmaCarrier.SamplesPerPage *
                SigmaCarrier.RepresentationWordsPerSample);
            using var completion = UInt2Buffer(
                SigmaGeneratedFrame.CompletionWordCount);

            query.SetBuffer(relation, "_NativeStates", scratch.States);
            query.SetBuffer(relation, "_NativeRelationInputs",
                scratch.RelationInputs);
            query.SetBuffer(relation, "_NativeRelationPlans",
                scratch.RelationPlans);
            query.SetBuffer(relation, "_NativeRelationNearIntervals",
                scratch.RelationNearIntervals);
            query.SetBuffer(relation, "_NativeRelationResults",
                scratch.RelationResults);
            query.SetBuffer(relation, "_NativeRelationFactors",
                scratch.RelationFactors);
            query.SetBuffer(relation, "_NativeRelationHashes",
                scratch.RelationHashes);
            query.SetBuffer(relation, "_NativeRelationNorms",
                scratch.RelationNorms);
            query.SetBuffer(relation, "_NativeObservations",
                scratch.Observation);
            query.SetBuffer(relation, "_NativeCloseScratch",
                scratch.CloseScratch);
            query.SetBuffer(relation, "_NativeLocalityCertificateWords",
                scratch.LocalityCertificateWords);
            query.SetBuffer(relation, "_NativeCounters", scratch.Counters);
            query.SetInt("_NativeEntryPointIndex",
                SigmaGeneratedFrame.IntrinsicRelationEntryPoint);
            query.SetInt("_NativeRelationCount", 1);
            query.SetInts("_NativeResolution", width, height);
            query.SetInt("_NativeFootprintCount", scratch.FootprintCapacity);
            query.SetInt("_NativeFootprintStateOffset",
                scratch.FootprintStateOffset);
            query.SetInt("_NativeFootprintCertificateOffset",
                scratch.FootprintCertificateOffset);
            query.SetInt("_NativeBoundaryCount", scratch.BoundaryCapacity);
            query.SetInt("_NativeBoundaryScratchOffset",
                scratch.BoundaryScratchOffset);
            query.SetInts("_NativeTileCount", scratch.TileCountX,
                scratch.TileCountY);
            query.SetInt("_NativeTileHeaderScratchOffset",
                scratch.TileHeaderScratchOffset);
            query.SetInt("_NativeTileFootprintScratchOffset",
                scratch.TileFootprintScratchOffset);
            query.SetInt("_NativeTileSupportSummaryScratchOffset",
                scratch.TileSupportSummaryScratchOffset);
            query.SetInt("_NativeTileComponentSummaryScratchOffset",
                scratch.TileComponentSummaryScratchOffset);
            query.SetInt("_NativeGlobalHeaderScratchOffset",
                scratch.GlobalHeaderScratchOffset);
            query.SetInt("_NativeActiveSupportListScratchOffset",
                scratch.ActiveSupportListScratchOffset);
            query.SetInt("_NativeGlobalParentScratchOffset",
                scratch.GlobalParentScratchOffset);
            query.SetInt("_NativeGlobalTransformScratchOffset",
                scratch.GlobalTransformScratchOffset);
            query.SetInt("_NativeGlobalBorderComponentCapacity",
                scratch.GlobalBorderComponentCapacity);

            contract.SetBuffer(close, "_NativeReverseRelationResults",
                scratch.RelationResults);
            contract.SetBuffer(close, "_NativeStates", scratch.States);
            contract.SetBuffer(close, "_NativeFreshEvidenceWords",
                completion);
            contract.SetBuffer(close, "_NativeObservations",
                scratch.Observation);
            contract.SetBuffer(close, "_NativeCloseScratch",
                scratch.CloseScratch);
            contract.SetBuffer(close, "_NativeSourceCarrierState",
                carrierState);
            contract.SetBuffer(close, "_NativeSourceCarrierRepresentation",
                carrierRepresentation);
            contract.SetBuffer(close, "_NativeBranchHeaders",
                scratch.BranchHeaders);
            contract.SetBuffer(close, "_NativeBranchSupports",
                scratch.BranchSupports);
            contract.SetBuffer(close, "_NativeBranchPredictions",
                scratch.BranchPredictions);
            contract.SetBuffer(close, "_NativeLocalityCertificateWords",
                scratch.LocalityCertificateWords);
            contract.SetBuffer(close, "_NativeCounters", scratch.Counters);
            contract.SetInt("_NativeFootprintCount", scratch.FootprintCapacity);
            contract.SetInt("_NativeFootprintStateOffset",
                scratch.FootprintStateOffset);
            contract.SetInt("_NativeFootprintCertificateOffset",
                scratch.FootprintCertificateOffset);
            contract.SetInts("_NativeResolution", width, height);
            contract.SetInts("_NativeTileCount", scratch.TileCountX,
                scratch.TileCountY);
            contract.SetInt("_NativeBoundaryCount", scratch.BoundaryCapacity);
            contract.SetInt("_NativeBoundaryScratchOffset",
                scratch.BoundaryScratchOffset);
            contract.SetInt("_NativeTileHeaderScratchOffset",
                scratch.TileHeaderScratchOffset);
            contract.SetInt("_NativeTileFootprintScratchOffset",
                scratch.TileFootprintScratchOffset);
            contract.SetInt("_NativeTileSupportSummaryScratchOffset",
                scratch.TileSupportSummaryScratchOffset);
            contract.SetInt("_NativeTileComponentSummaryScratchOffset",
                scratch.TileComponentSummaryScratchOffset);
            contract.SetInt("_NativeGlobalHeaderScratchOffset",
                scratch.GlobalHeaderScratchOffset);
            contract.SetInt("_NativeActiveSupportMarkerScratchOffset",
                scratch.ActiveSupportMarkerScratchOffset);
            contract.SetInt("_NativeActiveSupportListScratchOffset",
                scratch.ActiveSupportListScratchOffset);
            contract.SetInt("_NativeSupportLocatorCapacity",
                SigmaNativeFrameSlotResources.SupportLocatorCapacity);
            contract.SetInt("_NativeRevision", 1);

            SigmaS16[] cycleStates =
            {
                SigmaS16.Basis(1, -SigmaNumericDomain.One),
                SigmaS16.Basis(2, SigmaNumericDomain.One),
                SigmaS16.Basis(4, -SigmaNumericDomain.One),
                SigmaS16.Basis(1, SigmaNumericDomain.One),
                SigmaS16.Basis(2, -SigmaNumericDomain.One),
                SigmaS16.Basis(8, SigmaNumericDomain.One),
            };
            UInt2 contactPoint = Packed(SigmaNumericDomain.One);

            void UploadCycles(params int[] leftColumns)
            {
                var observations = new SigmaNativeObservationGpu[
                    scratch.FootprintCapacity + 1];
                var states = new UInt2[scratch.States.count];
                var certificates = new UInt4[
                    scratch.LocalityCertificateWords.count];
                var arena = new UInt2[scratch.CloseScratch.count];
                uint identity = 1u;
                int[] stateOrder = { 1, 0, 2, 5, 3, 4 };
                foreach (int leftColumn in leftColumns)
                {
                    for (int localY = 0; localY < 3; ++localY)
                        for (int localX = 0; localX < 2; ++localX)
                        {
                            int footprint = localY * width + leftColumn +
                                localX;
                            int stateIndex = stateOrder[localY * 2 + localX];
                            observations[footprint + 1].Identity = U4(1u, 1u,
                                identity++, evidenceFlags);
                            WriteState(states, scratch.FootprintStateOffset +
                                footprint * SigmaS16.LaneCount,
                                cycleStates[stateIndex]);
                            int certificate =
                                scratch.FootprintCertificateOffset + footprint *
                                SigmaNativeFrameSlotResources
                                    .CertificateWordCount;
                            certificates[certificate] = new UInt4
                            {
                                X = (uint)(SigmaNativeCertificateFlags.Valid |
                                    SigmaNativeCertificateFlags.Directional |
                                    SigmaNativeCertificateFlags.Minimized),
                                Y = 1u,
                                Z = 1u,
                            };
                            for (int side = 0; side < 4; ++side)
                                WriteEnvelope(arena, footprint,
                                    SigmaNativeFrameSlotResources
                                        .FootprintEvidenceWordCount,
                                    side, contactPoint);
                        }
                    int middleLeft = width + leftColumn;
                    int middleRight = middleLeft + 1;
                    WriteEnvelope(arena, middleLeft,
                        SigmaNativeFrameSlotResources
                            .FootprintEvidenceWordCount,
                        side: 1, contactPoint);
                    WriteEnvelope(arena, middleRight,
                        SigmaNativeFrameSlotResources
                            .FootprintEvidenceWordCount,
                        side: 0,
                        Packed(checked(SigmaNumericDomain.One * 2L)));
                }
                // When two 2x3 cycle fixtures meet across a tile seam, retain
                // exactly the declared top and bottom redundant stitches.  The
                // middle sampling boundary is an exact gap; otherwise the
                // implicit stencil correctly adds a third native constraint
                // that is not part of the accepted two-edge global fixture.
                for (int leftIndex = 0; leftIndex < leftColumns.Length;
                     ++leftIndex)
                    for (int rightIndex = leftIndex + 1;
                         rightIndex < leftColumns.Length; ++rightIndex)
                    {
                        int leftColumn = Math.Min(leftColumns[leftIndex],
                            leftColumns[rightIndex]);
                        int rightColumn = Math.Max(leftColumns[leftIndex],
                            leftColumns[rightIndex]);
                        if (rightColumn != leftColumn + 2)
                            continue;
                        int seamLeft = width + leftColumn + 1;
                        int seamRight = seamLeft + 1;
                        WriteEnvelope(arena, seamLeft,
                            SigmaNativeFrameSlotResources
                                .FootprintEvidenceWordCount,
                            side: 1, contactPoint);
                        WriteEnvelope(arena, seamRight,
                            SigmaNativeFrameSlotResources
                                .FootprintEvidenceWordCount,
                            side: 0,
                            Packed(checked(SigmaNumericDomain.One * 2L)));
                    }
                scratch.Observation.SetData(observations);
                scratch.States.SetData(states);
                scratch.LocalityCertificateWords.SetData(certificates);
                scratch.CloseScratch.SetData(arena);
                scratch.Counters.SetData(new UInt4[4]);
            }

            void CloseFrame()
            {
                query.SetKeyword(boundaryVariant, true);
                query.SetKeyword(globalVariant, false);
                query.SetInt("_NativeRelationMode", 1);
                query.Dispatch(relation, scratch.BoundaryCapacity + 1, 1, 1);
                contract.SetKeyword(tileVariant, true);
                contract.Dispatch(close, scratch.TileCapacity, 1, 1);
                contract.SetKeyword(tileVariant, false);
                query.SetKeyword(boundaryVariant, false);
                query.SetKeyword(globalVariant, true);
                query.SetInt("_NativeRelationMode", 2);
                query.Dispatch(relation, 1, 1, 1);
                query.SetKeyword(globalVariant, false);
            }

            // Two exact local cycles share two implicit boundaries across the
            // sole tile seam and must close as one component.
            UploadCycles(14, 16);
            CloseFrame();
            var global = new UInt2[
                SigmaNativeFrameSlotResources.GlobalHeaderWordCount];
            scratch.CloseScratch.GetData(global, 0,
                scratch.GlobalHeaderScratchOffset, global.Length);
            string globalReceipt = string.Join(",", global.Select(value =>
                $"{value.Low:x8}/{value.High:x8}"));
            var tileHeaders = new UInt2[4];
            scratch.CloseScratch.GetData(tileHeaders, 0,
                scratch.TileHeaderScratchOffset, tileHeaders.Length);
            string tileReceipt = string.Join(",", tileHeaders.Select(value =>
                $"{value.Low:x8}/{value.High:x8}"));
            var tileComponents = new UInt2[4];
            scratch.CloseScratch.GetData(tileComponents, 0,
                scratch.TileComponentSummaryScratchOffset, 2);
            scratch.CloseScratch.GetData(tileComponents, 2,
                scratch.TileComponentSummaryScratchOffset +
                    SigmaNativeFrameSlotResources.TileFootprintCapacity *
                    SigmaNativeFrameSlotResources
                        .TileComponentSummaryWordCount,
                2);
            string componentReceipt = string.Join(",",
                tileComponents.Select(value =>
                    $"{value.Low:x8}/{value.High:x8}"));
            var seamReceipts = new UInt2[18];
            for (int seam = 0; seam < 3; ++seam)
                scratch.CloseScratch.GetData(seamReceipts, seam * 6,
                    scratch.BoundaryScratchOffset + (15 + seam * 31) *
                        SigmaNativeFrameSlotResources.BoundaryReceiptWordCount,
                    6);
            string seamReceipt = string.Join(",", seamReceipts.Select(value =>
                $"{value.Low:x8}/{value.High:x8}"));
            string closeReceipt = $"global={globalReceipt}; tiles={tileReceipt}; " +
                $"components={componentReceipt}; seams={seamReceipt}";
            Assert.That(global[0].Low, Is.Zero, closeReceipt);
            Assert.That(global[0].High, Is.EqualTo(2u), closeReceipt);
            Assert.That(global[1].Low, Is.EqualTo(1u), closeReceipt);
            Assert.That(global[1].High, Is.Zero, closeReceipt);
            Assert.That(global[2].Low, Is.EqualTo(16u), closeReceipt);

            // The second seam edge is redundant. Changing its abstract sector
            // must invalidate the integrated orbit instead of repairing it.
            int redundantBoundary = scratch.BoundaryScratchOffset +
                77 * SigmaNativeFrameSlotResources.BoundaryReceiptWordCount;
            var corrupt = new UInt2[1];
            scratch.CloseScratch.GetData(corrupt, 0,
                redundantBoundary + 1, 1);
            corrupt[0].Low ^= 1u;
            scratch.CloseScratch.SetData(corrupt, 0,
                redundantBoundary + 1, 1);
            query.SetKeyword(globalVariant, true);
            query.Dispatch(relation, 1, 1, 1);
            query.SetKeyword(globalVariant, false);
            scratch.CloseScratch.GetData(global, 0,
                scratch.GlobalHeaderScratchOffset, global.Length);
            Assert.That(global[1].High, Is.EqualTo(1u));

            // Two tile-local cycles without a shared valid footprint boundary
            // retain independent chart gauges and resolve independently.
            UploadCycles(0, 30);
            CloseFrame();
            scratch.CloseScratch.GetData(global, 0,
                scratch.GlobalHeaderScratchOffset, global.Length);
            Assert.That(global[0].High, Is.EqualTo(2u));
            Assert.That(global[1].Low, Is.EqualTo(2u));
            Assert.That(global[1].High, Is.Zero);
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
                GraphicsBuffer.Target.Structured,
                SigmaGeneratedFrame.CompletionWordCount, sizeof(uint) * 2);

            SeedSingleFootprintClosedCutE(scratch, 1u,
                StateWithLane0(0u, 0x00010000u));
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
            DispatchCutE(frame, scratch);
            frame.Dispatch(clone, Math.Max(SigmaCarrier.PageLaneCount,
                SigmaCarrier.SamplesPerPage *
                SigmaCarrier.RepresentationWordsPerSample) / 256, 1, 1);
            frame.Dispatch(scatter, 1, 1, 1);
            frame.Dispatch(close, 1, 1, 1);

            uint[] published = { 0u };
            root.GetData(published);
            var terminal = new SigmaNativeFrameGpu[1];
            scratch.NativeFrame.GetData(terminal);
            var debugCounters = new UInt4[scratch.Counters.count];
            scratch.Counters.GetData(debugCounters);
            var debugPlan = new UInt2[4];
            scratch.CloseScratch.GetData(debugPlan, 0,
                scratch.PagePlanScratchOffset, debugPlan.Length);
            var debugScan = new UInt2[1];
            scratch.CloseScratch.GetData(debugScan, 0,
                scratch.CanonicalImageScratchOffset +
                    9 * scratch.CanonicalImageStride, 1);
            var debugComponent = new UInt2[10];
            scratch.CloseScratch.GetData(debugComponent, 0,
                scratch.CanonicalComponentScratchOffset +
                    scratch.GlobalBorderComponentCapacity * 10,
                debugComponent.Length);
            var debugMeta = new UInt2[1];
            scratch.CloseScratch.GetData(debugMeta, 0,
                scratch.CanonicalImageScratchOffset +
                    6 * scratch.CanonicalImageStride, 1);
            Assert.That(published[0], Is.EqualTo(1u),
                $"disposition={terminal[0].Disposition.X}/" +
                $"{terminal[0].Disposition.Y}/" +
                $"{terminal[0].Disposition.Z}/" +
                $"{terminal[0].Disposition.W}, publication=" +
                $"{terminal[0].Publication.X}/" +
                $"{terminal[0].Publication.Y}/" +
                $"{terminal[0].Publication.Z}/" +
                $"{terminal[0].Publication.W}, plan=" +
                string.Join(",", debugPlan.Select(value =>
                    $"{value.Low}/{value.High}")) +
                $", scan={debugScan[0].Low}/{debugScan[0].High}, counters=" +
                string.Join(";", debugCounters.Select(value =>
                    $"{value.X}/{value.Y}/{value.Z}/{value.W}")) +
                ", component=" + string.Join(",", debugComponent.Select(value =>
                    $"{value.Low:x8}/{value.High:x8}")) +
                $", meta={debugMeta[0].Low:x8}/{debugMeta[0].High:x8}");
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
        public void ScatterConsumesMutationCountInsteadOfResidentWorldExtent()
        {
            ComputeShader frame = LoadShader("SigmaNativeFrame");
            int prepare = frame.FindKernel("PrepareNativeRevision");
            int clone = frame.FindKernel("PrepareNativePage");
            int scatter = frame.FindKernel("ScatterNativeState");
            int close = frame.FindKernel("CloseAndPublishNativeRevision");
            const int pageCapacity = 4;

            using var scratch = new SigmaNativeFrameSlotResources(0);
            using var carrierState = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                SigmaCarrier.PageLaneCount * pageCapacity,
                Marshal.SizeOf<UInt2>());
            using var carrierRepresentation = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                SigmaCarrier.SamplesPerPage * pageCapacity *
                SigmaCarrier.RepresentationWordsPerSample,
                Marshal.SizeOf<UInt4>());
            using var metadata = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, pageCapacity,
                SigmaCarrier.PageMetadataStride);
            using var dirty = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, pageCapacity, sizeof(uint));
            using var readoutDirty = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, pageCapacity, sizeof(uint));
            using var root = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 1, sizeof(uint));
            using var completionJournal = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                SigmaGeneratedFrame.CompletionWordCount, sizeof(uint) * 2);

            SeedSingleFootprintClosedCutE(scratch, 2u,
                StateWithLane0(0u, 5u << 16), priorSupport: true,
                sourceSlot: 2u, sourceSample: 0u, sourceGeneration: 1u);
            var stale = new SigmaNativeStateDeltaGpu
            {
                Generation = U4(0u, 123u, 1u, 0u),
                Changed = U4((uint)SigmaNativeDeltaFlags.StateChanged,
                    0u, 0u, 0u),
                State01 = U4(0u, 0xdeadbeefu, 0u, 0u),
            };
            scratch.StateDelta.SetData(new[] { stale }, 0, 1, 1);

            var state = new UInt2[carrierState.count];
            state[2 * SigmaCarrier.PageLaneCount].High = 3u << 16;
            carrierState.SetData(state);
            var representation = new UInt4[carrierRepresentation.count];
            WriteRepresentationCell(representation, 2, 0,
                new GaugeKey(0L, 0L, 0u), 1u);
            carrierRepresentation.SetData(representation);
            var pages = new PageMeta[pageCapacity];
            pages[2] = new PageMeta
            {
                PageXLow = 1u,
                Generation = 1u,
                Revision = 1u,
                CertificateCount = 1u,
                Flags = 3u,
                GaugeGeneration = 1u,
                CertificateGeneration = 1u,
                RepresentationFlags = 3u,
                ActiveSampleCount = 1u,
            };
            metadata.SetData(pages);
            dirty.SetData(new uint[pageCapacity]);
            readoutDirty.SetData(new uint[pageCapacity]);
            root.SetData(new[] { 1u });

            foreach (int kernel in new[] { prepare, clone, scatter, close })
                BindPublication(frame, kernel, scratch, carrierState,
                    carrierRepresentation, metadata, dirty, readoutDirty, root,
                    completionJournal);
            frame.SetInt("_NativeRevision", 2);
            frame.SetInt("_NativeCalibrationEpoch", 7);
            frame.SetInt("_NativeTargetPageCapacity", pageCapacity);
            DispatchCutE(frame, scratch);

            var counters = new UInt4[scratch.Counters.count];
            scratch.Counters.GetData(counters);
            Assert.That(counters[0].Y, Is.EqualTo(1u),
                "Prepare must append exactly one compact mutation.");
            Assert.That(counters[2].W, Is.EqualTo(1u),
                "Scatter indirect args must carry mutationCount, not extent.");
            var prepared = new SigmaNativeFrameGpu[1];
            scratch.NativeFrame.GetData(prepared);
            Assert.That(prepared[0].Publication.Z, Is.EqualTo(4097u),
                "The regression requires an extent larger than mutationCount.");

            frame.Dispatch(clone, Math.Max(SigmaCarrier.PageLaneCount,
                SigmaCarrier.SamplesPerPage *
                SigmaCarrier.RepresentationWordsPerSample) / 256, 1, 1);
            frame.Dispatch(scatter, 1, 1, 1);
            frame.Dispatch(close, 1, 1, 1);

            var target = new UInt2[1];
            carrierState.GetData(target, 0,
                3 * SigmaCarrier.PageLaneCount, 1);
            Assert.That(target[0].High, Is.EqualTo(5u << 16));
            var staleTarget = new UInt2[1];
            carrierState.GetData(staleTarget, 0,
                123 * SigmaS16.LaneCount, 1);
            Assert.That(staleTarget[0].Low, Is.Zero);
            Assert.That(staleTarget[0].High, Is.Zero,
                "Scatter read a stale delta beyond mutationCount.");
            var published = new uint[1];
            root.GetData(published);
            Assert.That(published[0], Is.EqualTo(2u));
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
                GraphicsBuffer.Target.Structured,
                SigmaGeneratedFrame.CompletionWordCount, sizeof(uint) * 2);

            UInt2[] current = StateWithLane0(0x89abcdefu, 0x01234567u);
            SeedSingleFootprintClosedCutE(scratch, 2u, current,
                priorSupport: true, sourceGeneration: 1u);
            var carrier = new UInt2[carrierState.count];
            carrier[0] = current[0];
            carrierState.SetData(carrier);
            var representation = new UInt4[carrierRepresentation.count];
            WriteRepresentationCell(representation, 0, 0,
                new GaugeKey(0L, 0L, 0u), 1u);
            carrierRepresentation.SetData(representation);
            metadata.SetData(new[]
            {
                new PageMeta
                {
                    Generation = 1u,
                    Revision = 1u,
                    CertificateCount = 1u,
                    Flags = 3u,
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
                BindPublication(frame, kernel, scratch, carrierState,
                    carrierRepresentation, metadata, dirty, readoutDirty, root,
                    completionJournal);
            frame.SetInt("_NativeRevision", 2);
            frame.SetInt("_NativeCalibrationEpoch", 7);
            frame.SetInt("_NativeTargetPageCapacity", 2);
            DispatchCutE(frame, scratch);
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
            StaticExclusionSnapshot proven = RunStaticExclusion();
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
            Assert.That(proven.Counters[0].Y, Is.EqualTo(1u));
            Assert.That(proven.Counters[3].Y, Is.Zero);
            Assert.That(proven.Counters[3].X, Is.Zero);
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
                GraphicsBuffer.Target.Structured,
                SigmaGeneratedFrame.CompletionWordCount, sizeof(uint) * 2);

            SeedSingleFootprintClosedCutE(scratch, 2u,
                StateWithLane0(0u, 2u << 16), priorSupport: true,
                sourceGeneration: 9u);

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
            WriteRepresentationCell(priorRepresentation, 0, 0,
                new GaugeKey(0L, 0L, 0u), 9u);
            carrierRepresentation.SetData(priorRepresentation);
            var priorMetadata = new[]
            {
                new PageMeta
                {
                    Generation = 9u,
                    Revision = 1u,
                    CertificateCount = 1u,
                    Flags = 3u,
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
            DispatchCutE(frame, scratch);
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
            Assert.That(canonical.Length, Is.GreaterThan(272));
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
                Assert.That(new FileInfo(path).Length, Is.EqualTo(8L),
                    "The durable marker must not contain a history snapshot.");
                Assert.That(Directory.GetFiles(path + ".entries", "*.scb"),
                    Has.Length.EqualTo(1),
                    "One minimized exact key must own one durable shard.");
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
        public void LegacyConstraintSnapshotMigratesOnceToBoundedDeltaStore()
        {
            string directory = Path.Combine(Path.GetTempPath(),
                "sigma-n4-migrate-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "constraints.sjc");
            try
            {
                Directory.CreateDirectory(directory);
                var source = new SigmaExactConstraintJournal();
                source.Add(CertifiedConstraintRecord(1u, -8L, 8L, 1u, 2u));
                File.WriteAllBytes(path, source.EncodeCanonical());
                using (var store = new SigmaExactConstraintStore(path))
                {
                    SigmaExactConstraintJournal loaded = store.Load();
                    loaded.Add(CertifiedConstraintRecord(2u, -2L, 3L,
                        17u, 33u));
                    store.Stage(loaded);
                }
                Assert.That(new FileInfo(path).Length, Is.EqualTo(8L));
                Assert.That(Directory.GetFiles(path + ".entries", "*.scb"),
                    Has.Length.EqualTo(1));
                using var reopened = new SigmaExactConstraintStore(path);
                SigmaExactConstraintJournal migrated = reopened.Load();
                Assert.That(migrated.CertificateCount, Is.EqualTo(1));
                Assert.That(migrated.RawEvidenceCount, Is.Zero);
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
        }

        [Test]
        public void LocalityCertificateBoundsTenThousandRevisitsAndIsOrderIndependent()
        {
            SigmaExactConstraintRecord broad = CertifiedConstraintRecord(1u,
                -8L, 8L, 1u, 2u);
            SigmaExactConstraintRecord narrow = CertifiedConstraintRecord(2u,
                -2L, 3L, 1u, 2u);
            var weakThenStrong = new SigmaExactConstraintJournal();
            Assert.That(weakThenStrong.Add(broad),
                Is.EqualTo(SigmaConstraintAdmission.Added));
            Assert.That(weakThenStrong.Add(narrow),
                Is.EqualTo(SigmaConstraintAdmission.ReplacedWeaker));
            SigmaExactConstraintCertificate broadCertificate =
                SigmaExactConstraintCertificate.From(broad);
            SigmaExactConstraintCertificate narrowCertificate =
                SigmaExactConstraintCertificate.From(narrow);
            Assert.That(broadCertificate.TryMeet(narrowCertificate,
                out SigmaExactConstraintCertificate expectedCertificate), Is.True);
            SigmaExactConstraintCertificate repeatedCertificate =
                SigmaExactConstraintCertificate.From(CertifiedConstraintRecord(
                    3u, -2L, 3L, 1u, 2u));
            Assert.That(expectedCertificate.TryMeet(repeatedCertificate,
                out SigmaExactConstraintCertificate repeatedMeet), Is.True);
            Assert.That(expectedCertificate.SameWords(repeatedMeet), Is.True,
                CertificateDifference(expectedCertificate.Words,
                    repeatedMeet.Words));
            for (uint revision = 3u; revision < 10003u; ++revision)
                Assert.That(weakThenStrong.Add(CertifiedConstraintRecord(
                    revision, -2L, 3L, 1u, 2u)),
                    Is.EqualTo(SigmaConstraintAdmission.DuplicateOrWeaker),
                    $"revision {revision}");
            Assert.That(weakThenStrong.Count, Is.EqualTo(1));
            Assert.That(weakThenStrong.CertificateCount, Is.EqualTo(1));
            Assert.That(weakThenStrong.RawEvidenceCount, Is.EqualTo(2),
                "Only the two not-yet-durable certificate generations own raw.");

            var strongThenWeak = new SigmaExactConstraintJournal();
            Assert.That(strongThenWeak.Add(narrow),
                Is.EqualTo(SigmaConstraintAdmission.Added));
            Assert.That(strongThenWeak.Add(broad),
                Is.EqualTo(SigmaConstraintAdmission.DuplicateOrWeaker));
            Assert.That(strongThenWeak.Count, Is.EqualTo(1));
            Assert.That(strongThenWeak.RawEvidenceCount, Is.EqualTo(1));

            string directory = Path.Combine(Path.GetTempPath(),
                "sigma-n4-certificate-" + Guid.NewGuid().ToString("N"));
            try
            {
                byte[] weakStrong = PersistAndReload(directory, "weak-strong.sjc",
                    weakThenStrong, out SigmaExactConstraintJournal weakReloaded);
                byte[] strongWeak = PersistAndReload(directory, "strong-weak.sjc",
                    strongThenWeak, out SigmaExactConstraintJournal strongReloaded);
                Assert.That(weakThenStrong.RawEvidenceCount, Is.Zero,
                    "Raw ownership must end after durable equivalence handoff.");
                Assert.That(strongThenWeak.RawEvidenceCount, Is.Zero);
                Assert.That(weakReloaded.RawEvidenceCount, Is.Zero);
                Assert.That(strongReloaded.RawEvidenceCount, Is.Zero);
                CollectionAssert.AreEqual(weakStrong, strongWeak,
                    "Canonical certificate changed with evidence order.");
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
        }

        [Test]
        public void LocalityCertificatePreservesViewDiversityAndRawContradiction()
        {
            SigmaExactConstraintRecord viewA = CertifiedConstraintRecord(1u,
                -8L, 8L, 1u, 2u);
            SigmaExactConstraintRecord viewB = CertifiedConstraintRecord(2u,
                -4L, 6L, 17u, 33u);
            SigmaExactConstraintRecord contradiction = CertifiedConstraintRecord(
                3u, 20L, 30L, 7u, 9u);
            var leftFirst = new SigmaExactConstraintJournal();
            Assert.That(leftFirst.Add(viewA),
                Is.EqualTo(SigmaConstraintAdmission.Added));
            Assert.That(leftFirst.Add(viewB),
                Is.EqualTo(SigmaConstraintAdmission.ReplacedWeaker),
                "A new directional mode must strengthen the same certificate.");
            Assert.That(leftFirst.Add(contradiction),
                Is.EqualTo(SigmaConstraintAdmission.IncompatibleRetained));
            Assert.That(leftFirst.CertificateCount, Is.EqualTo(1));
            Assert.That(leftFirst.Count, Is.EqualTo(2));

            var rightFirst = new SigmaExactConstraintJournal();
            rightFirst.Add(viewB);
            rightFirst.Add(viewA);
            rightFirst.Add(contradiction);

            string directory = Path.Combine(Path.GetTempPath(),
                "sigma-n4-diversity-" + Guid.NewGuid().ToString("N"));
            try
            {
                byte[] left = PersistAndReload(directory, "left.sjc", leftFirst,
                    out SigmaExactConstraintJournal leftReloaded);
                byte[] right = PersistAndReload(directory, "right.sjc", rightFirst,
                    out SigmaExactConstraintJournal rightReloaded);
                CollectionAssert.AreEqual(left, right);
                Assert.That(leftReloaded.CertificateCount, Is.EqualTo(1));
                Assert.That(leftReloaded.RawEvidenceCount, Is.EqualTo(1),
                    "Only the incompatible exact factor may remain raw.");
                Assert.That(rightReloaded.RawEvidenceCount, Is.EqualTo(1));
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
        }

        [Test]
        public void ReloadedCertificateMeetsLikeUninterruptedCertificate()
        {
            SigmaExactConstraintRecord first = CertifiedConstraintRecord(1u,
                -8L, 8L, 1u, 2u);
            SigmaExactConstraintRecord next = CertifiedConstraintRecord(2u,
                -3L, 5L, 17u, 33u);
            var uninterrupted = new SigmaExactConstraintJournal();
            uninterrupted.Add(first);
            uninterrupted.Add(next);

            string directory = Path.Combine(Path.GetTempPath(),
                "sigma-n4-restart-" + Guid.NewGuid().ToString("N"));
            try
            {
                var firstOnly = new SigmaExactConstraintJournal();
                firstOnly.Add(first);
                PersistAndReload(directory, "restart.sjc",
                    firstOnly, out SigmaExactConstraintJournal restarted);
                Assert.That(restarted.Add(next),
                    Is.EqualTo(SigmaConstraintAdmission.ReplacedWeaker));
                byte[] restartedBytes = PersistAndReload(directory,
                    "restart-next.sjc", restarted, out _);
                byte[] uninterruptedBytes = PersistAndReload(directory,
                    "uninterrupted.sjc", uninterrupted, out _);
                CollectionAssert.AreEqual(uninterruptedBytes, restartedBytes);
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
                    transfer.Reserve((uint)index + 1u);
                first ??= reservation.Buffer;
                Assert.That(reservation.Buffer, Is.SameAs(first));
                Assert.That(reservation.RecordIndex, Is.EqualTo(index));
                Assert.That(reservation.ExpectedRevision,
                    Is.EqualTo((uint)index + 1u));
                Assert.That(reservation.SealsBatch, Is.EqualTo(index ==
                    SigmaNativeCompletionTransfer.RecordsPerBatch - 1));
            }
            SigmaNativeCompletionTransfer.Reservation next =
                transfer.Reserve(17u);
            Assert.That(next.Buffer, Is.Not.SameAs(first),
                "A sealed cold batch may not be overwritten by hot ingress.");
            Assert.That(next.RecordIndex, Is.Zero);
            transfer.Cancel(next);
        }

        [Test]
        public void CompletionClassificationRejectsWrongNonzeroGpuRevision()
        {
            var frame = new SigmaNativeFrameGpu
            {
                Identity = U4(42u, 7u, 1u, 0u),
                Disposition = U4(
                    (uint)SigmaNativeFrameDisposition.Published, 1u, 0u, 0u),
                Publication = U4(40u, 42u, 1u, 1u),
            };

            SigmaFrameCompletionDisposition disposition =
                SigmaInverseController.ClassifyFrameCompletion(frame, 42u,
                    41u, out string error);

            Assert.That(disposition,
                Is.EqualTo(SigmaFrameCompletionDisposition.Faulted));
            StringAssert.Contains("expected 41, received 42", error);
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
                        representationBase + 14 * 4],
                    Is.EqualTo(11u), $"{gauge} evidence receipt");
                Assert.That(copy.RepresentationWords[
                        representationBase + 14 * 4 + 1],
                    Is.EqualTo(12u), $"{gauge} evidence receipt");
            }
            Assert.That(parentCopies, Is.EqualTo(3));
            Assert.That(refinedChildren, Is.EqualTo(1));
            Assert.That(untouchedLocalities, Is.EqualTo(1));
        }

        [Test]
        public void RefinementMultiplicityAndParentMeetArePermutationInvariant()
        {
            var step = new RefinementStep(
                new GaugeKey(0L, 0L, 0u), 2u, 100u);
            RefinementSnapshot forward = RunRefinementSequence(new[] { step },
                childByFootprint: new[] { 0, 0, 1, 2, 2, 3 },
                broadenFreshCertificate: true);
            RefinementSnapshot permuted = RunRefinementSequence(new[] { step },
                childByFootprint: new[] { 2, 0, 3, 2, 0, 1 },
                broadenFreshCertificate: true);

            Assert.That(forward.ActiveSampleCount, Is.EqualTo(5u));
            Assert.That(forward.CertificateCount,
                Is.EqualTo(forward.ActiveSampleCount));
            CollectionAssert.AreEqual(forward.StateWords,
                permuted.StateWords,
                "state bytes changed with duplicate-observation order");
            CollectionAssert.AreEqual(forward.RepresentationWords,
                permuted.RepresentationWords,
                "canonical publication changed with duplicate-observation order");
            CollectionAssert.AreEqual(forward.GaugeKeys,
                permuted.GaugeKeys,
                "refinement target order changed with observation order");

            UInt2 narrowLower = Packed(-3L << 48);
            UInt2 narrowUpper = Packed(3L << 48);
            int refinedChildren = 0;
            for (int sample = 0; sample < forward.GaugeKeys.Length; ++sample)
            {
                if (forward.GaugeKeys[sample].Level != 1u)
                    continue;
                refinedChildren++;
                int axis = (sample * SigmaCarrier.RepresentationWordsPerSample +
                    2 + 4) * 4;
                Assert.That(forward.RepresentationWords[axis],
                    Is.EqualTo(narrowLower.Low));
                Assert.That(forward.RepresentationWords[axis + 1],
                    Is.EqualTo(narrowLower.High));
                Assert.That(forward.RepresentationWords[axis + 2],
                    Is.EqualTo(narrowUpper.Low));
                Assert.That(forward.RepresentationWords[axis + 3],
                    Is.EqualTo(narrowUpper.High));
            }
            Assert.That(refinedChildren, Is.EqualTo(4),
                "all four physical children must receive the narrowed parent meet");
        }

        [Test]
        public void RefinementAtFullPageBoundarySpillsIntoNextLogicalPage()
        {
            ComputeShader frame = LoadShader("SigmaNativeFrame");
            int prepare = frame.FindKernel("PrepareNativeRevision");
            int clone = frame.FindKernel("PrepareNativePage");
            int scatter = frame.FindKernel("ScatterNativeState");
            int close = frame.FindKernel("CloseAndPublishNativeRevision");
            var resolution = new Vector2Int(2, 2);
            using var scratch = new SigmaNativeFrameSlotResources(0,
                resolution);
            const int physicalPages = 4;
            using var carrierState = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                SigmaCarrier.PageLaneCount * physicalPages,
                Marshal.SizeOf<UInt2>());
            using var carrierRepresentation = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                SigmaCarrier.SamplesPerPage * physicalPages *
                    SigmaCarrier.RepresentationWordsPerSample,
                Marshal.SizeOf<UInt4>());
            using var metadata = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, physicalPages,
                SigmaCarrier.PageMetadataStride);
            using var dirty = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, physicalPages, sizeof(uint));
            using var readoutDirty = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, physicalPages, sizeof(uint));
            using var root = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 1, sizeof(uint));
            using var completionJournal = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                SigmaGeneratedFrame.CompletionWordCount, sizeof(uint) * 2);

            var state = new UInt2[carrierState.count];
            int parentSample = SigmaCarrier.SamplesPerPage - 1;
            state[parentSample * SigmaS16.LaneCount].High = 10u << 16;
            carrierState.SetData(state);
            var representation = new UInt4[carrierRepresentation.count];
            for (int sample = 0; sample < SigmaCarrier.SamplesPerPage;
                 ++sample)
                WriteRepresentationCell(representation, 0, sample,
                    new GaugeKey(sample * 8L, 0L, 0u), 1u);
            carrierRepresentation.SetData(representation);
            metadata.SetData(new[]
            {
                new PageMeta
                {
                    Generation = 1u,
                    Revision = 1u,
                    CertificateCount = SigmaCarrier.SamplesPerPage,
                    Flags = 3u,
                    GaugeGeneration = 1u,
                    CertificateGeneration = 1u,
                    RepresentationFlags = 3u,
                    ActiveSampleCount = SigmaCarrier.SamplesPerPage,
                },
                new PageMeta(),
                new PageMeta { PageXLow = 1u },
                new PageMeta { PageXLow = 1u },
            });
            dirty.SetData(new uint[physicalPages]);
            readoutDirty.SetData(new uint[physicalPages]);
            root.SetData(new[] { 1u });

            var priorState = new UInt2[SigmaS16.LaneCount];
            Array.Copy(state, parentSample * SigmaS16.LaneCount,
                priorState, 0, SigmaS16.LaneCount);
            var priorCertificate = new UInt4[
                SigmaNativeFrameSlotResources.CertificateWordCount];
            Array.Copy(representation,
                parentSample * SigmaCarrier.RepresentationWordsPerSample + 2,
                priorCertificate, 0, priorCertificate.Length);
            PrepareRefinementScratch(scratch, 2u, 0u,
                (uint)parentSample, 1u,
                new RefinementStep(new GaugeKey(parentSample * 8L, 0L, 0u),
                    3u, 100u), priorState, priorCertificate);

            foreach (int kernel in new[] { prepare, clone, scatter, close })
                BindPublication(frame, kernel, scratch, carrierState,
                    carrierRepresentation, metadata, dirty, readoutDirty,
                    root, completionJournal, resolution);
            frame.SetInt("_NativeRevision", 2);
            frame.SetInt("_NativeCalibrationEpoch", 7);
            frame.SetInt("_NativeTargetPageCapacity", physicalPages);
            DispatchCutE(frame, scratch, resolution);
            int copyGroups = Math.Max(SigmaCarrier.PageLaneCount,
                SigmaCarrier.SamplesPerPage *
                    SigmaCarrier.RepresentationWordsPerSample) / 256;
            frame.Dispatch(clone, copyGroups, 2, 1);
            frame.Dispatch(scatter, 1, 1, 1);
            frame.Dispatch(close, 1, 1, 1);

            var published = new uint[1];
            root.GetData(published);
            Assert.That(published[0], Is.EqualTo(2u));
            PageMeta first = ReadPageMeta(metadata, 1u);
            PageMeta second = ReadPageMeta(metadata, 2u);
            Assert.That(first.ActiveSampleCount,
                Is.EqualTo(SigmaCarrier.SamplesPerPage));
            Assert.That(second.ActiveSampleCount, Is.EqualTo(3u));
            Assert.That(first.Revision, Is.EqualTo(2u));
            Assert.That(second.Revision, Is.EqualTo(2u));

            var children = new HashSet<GaugeKey>();
            for (int logical = SigmaCarrier.SamplesPerPage - 1;
                 logical < SigmaCarrier.SamplesPerPage + 3; ++logical)
            {
                uint slot = logical < SigmaCarrier.SamplesPerPage ? 1u : 2u;
                int sample = logical % SigmaCarrier.SamplesPerPage;
                var words = new UInt4[2];
                carrierRepresentation.GetData(words, 0,
                    checked(((int)slot * SigmaCarrier.SamplesPerPage + sample) *
                        SigmaCarrier.RepresentationWordsPerSample), 2);
                children.Add(GaugeKey.From(words[0], words[1].X));
            }
            Assert.That(children.Count, Is.EqualTo(4));
            Assert.That(children.All(value => value.Level == 1u), Is.True);
        }

        [Test]
        public void CutEUsesDirectedWitnessWhenD4CellStreamsTie()
        {
            ComputeShader frame = LoadShader("SigmaNativeFrame");
            int prepare = frame.FindKernel("PrepareNativeRevision");
            int clone = frame.FindKernel("PrepareNativePage");
            int scatter = frame.FindKernel("ScatterNativeState");
            int closeKernel = frame.FindKernel("CloseAndPublishNativeRevision");
            var resolution = new Vector2Int(2, 1);
            using var scratch = new SigmaNativeFrameSlotResources(0,
                resolution);
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
                GraphicsBuffer.Target.Structured,
                SigmaGeneratedFrame.CompletionWordCount, sizeof(uint) * 2);

            uint flags = (uint)(SigmaNativeObservationFlags.Coherent |
                SigmaNativeObservationFlags.LeftFirstHit |
                SigmaNativeObservationFlags.RightFirstHit |
                SigmaNativeObservationFlags.LeftEvidence |
                SigmaNativeObservationFlags.RightEvidence);
            var observations = new SigmaNativeObservationGpu[
                scratch.Observation.count];
            observations[1].Identity = U4(1u, 7u, 13u, flags);
            observations[2].Identity = U4(1u, 7u, 14u, flags);
            scratch.Observation.SetData(observations);
            scratch.NativeFrame.SetData(new[]
            {
                new SigmaNativeFrameGpu
                {
                    Identity = U4(1u, 7u, 1u, 0u),
                    Disposition = U4(
                        (uint)SigmaNativeFrameDisposition.GpuOwned,
                        2u, 0u, 0u),
                    Publication = U4(0u, 0u, 0u, 0u),
                },
            });
            var states = new UInt2[scratch.States.count];
            UInt2[] payload = StateWithLane0(0u, 5u << 16);
            for (int footprint = 0; footprint < 2; ++footprint)
                Array.Copy(payload, 0, states,
                    scratch.FootprintStateOffset +
                        footprint * SigmaS16.LaneCount,
                    SigmaS16.LaneCount);
            scratch.States.SetData(states);
            UInt4[] certificate = BuildLocalityCertificate(1u);
            var certificates = new UInt4[
                scratch.LocalityCertificateWords.count];
            for (int footprint = 0; footprint < 2; ++footprint)
                Array.Copy(certificate, 0, certificates,
                    scratch.FootprintCertificateOffset + footprint *
                        SigmaNativeFrameSlotResources.CertificateWordCount,
                    SigmaNativeFrameSlotResources.CertificateWordCount);
            scratch.LocalityCertificateWords.SetData(certificates);

            var close = new UInt2[scratch.CloseScratch.count];
            for (int footprint = 0; footprint < 2; ++footprint)
            {
                close[footprint *
                    SigmaNativeFrameSlotResources.FootprintEvidenceWordCount +
                    51] = new UInt2 { Low = 1u };
                int receipt = scratch.TileFootprintScratchOffset + footprint *
                    SigmaNativeFrameSlotResources.
                        TileFootprintReceiptWordCount;
                close[receipt] = new UInt2 { Low = 0u };
                close[receipt + 2] = new UInt2
                {
                    Low = (uint)footprint,
                    High = 0u,
                };
                close[receipt + 3] = new UInt2 { Low = 8u };
            }
            close[scratch.TileComponentSummaryScratchOffset] =
                new UInt2 { High = 1u };
            close[scratch.TileComponentSummaryScratchOffset + 1] =
                new UInt2 { High = 2u | (1u << 17) };
            int boundary = scratch.BoundaryScratchOffset;
            close[boundary] = new UInt2
            {
                Low = (uint)SigmaStitchResolution.Resolved,
                High = (uint)SigmaNativeBoundarySector.Sector3,
            };
            close[boundary + 1] = new UInt2
            {
                Low = (uint)SigmaNativeBoundarySector.Sector0,
                High = 9u,
            };
            close[boundary + 2] = new UInt2 { Low = 1u };
            close[boundary + 3] = new UInt2
            {
                Low = 0x1111u,
                High = 0x5a5a5a5au,
            };
            close[boundary + 4] = new UInt2
            {
                Low = 0x212b1e76u,
                High = 0x6094d138u,
            };
            close[boundary + 5] = new UInt2 { Low = 0u, High = 1u };
            scratch.CloseScratch.SetData(close);
            scratch.StateDelta.SetData(new SigmaNativeStateDeltaGpu[
                scratch.StateDelta.count]);
            scratch.GaugeDelta.SetData(new SigmaNativeGaugeDeltaGpu[
                scratch.GaugeDelta.count]);
            scratch.Counters.SetData(new UInt4[scratch.Counters.count]);
            carrierState.SetData(new UInt2[carrierState.count]);
            carrierRepresentation.SetData(new UInt4[
                carrierRepresentation.count]);
            metadata.SetData(new PageMeta[2]);
            dirty.SetData(new uint[2]);
            readoutDirty.SetData(new uint[2]);
            root.SetData(new uint[1]);

            foreach (int kernel in new[]
                     { prepare, clone, scatter, closeKernel })
                BindPublication(frame, kernel, scratch, carrierState,
                    carrierRepresentation, metadata, dirty, readoutDirty,
                    root, completionJournal, resolution);
            frame.SetInt("_NativeRevision", 1);
            frame.SetInt("_NativeCalibrationEpoch", 7);
            frame.SetInt("_NativeTargetPageCapacity", 2);
            DispatchCutE(frame, scratch, resolution);
            frame.Dispatch(clone, Math.Max(SigmaCarrier.PageLaneCount,
                SigmaCarrier.SamplesPerPage *
                    SigmaCarrier.RepresentationWordsPerSample) / 256, 1, 1);
            frame.Dispatch(scatter, 1, 1, 1);
            frame.Dispatch(closeKernel, 1, 1, 1);

            var counters = new UInt4[scratch.Counters.count];
            scratch.Counters.GetData(counters);
            Assert.That(counters[0].Y, Is.EqualTo(2u));
            var gauge = new SigmaNativeGaugeDeltaGpu[2];
            scratch.GaugeDelta.GetData(gauge, 0, 0, gauge.Length);
            Assert.That(gauge.All(value => value.Witness.X == 2u), Is.True,
                "Sector3->Sector0 makes the reverse directed edge token " +
                "lexically minimal; equal compact cache words may not choose " +
                "the D4 image.");
            var published = new uint[1];
            root.GetData(published);
            Assert.That(published[0], Is.EqualTo(1u));
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

        private static void SeedSingleFootprintClosedCutE(
            SigmaNativeFrameSlotResources scratch, uint revision,
            UInt2[] footprintState, bool priorSupport = false,
            uint sourceSlot = 0u, uint sourceSample = 0u,
            uint sourceGeneration = 1u, uint level = 0u,
            uint dispositionFlags = 0u, UInt4[] certificate = null)
        {
            Assert.That(scratch.FootprintCapacity, Is.EqualTo(1));
            Assert.That(footprintState, Has.Length.EqualTo(SigmaS16.LaneCount));

            uint flags = (uint)(SigmaNativeObservationFlags.Coherent |
                SigmaNativeObservationFlags.LeftFirstHit |
                SigmaNativeObservationFlags.RightFirstHit |
                SigmaNativeObservationFlags.LeftEvidence |
                SigmaNativeObservationFlags.RightEvidence);
            if (priorSupport)
                flags |= (uint)SigmaNativeObservationFlags.PriorSupport;

            scratch.NativeFrame.SetData(new[]
            {
                new SigmaNativeFrameGpu
                {
                    Identity = U4(revision, 7u, 1u, 0u),
                    Disposition = U4(
                        (uint)SigmaNativeFrameDisposition.GpuOwned,
                        1u, 0u, 0u),
                    Evidence = U4(1u, sourceSlot, sourceSample,
                        sourceGeneration),
                    Publication = U4(revision == 0u ? 0u : revision - 1u,
                        0u, revision == 0u ? 0u : revision - 1u, 0u),
                },
            });
            var observations = new SigmaNativeObservationGpu[
                scratch.Observation.count];
            observations[1] = new SigmaNativeObservationGpu
            {
                Identity = U4(revision, 7u, 13u, flags),
                Evidence = U4(11u, 12u, 13u, 14u),
            };
            scratch.Observation.SetData(observations);

            var states = new UInt2[scratch.States.count];
            Array.Copy(footprintState, 0, states,
                scratch.FootprintStateOffset, SigmaS16.LaneCount);
            scratch.States.SetData(states);

            certificate ??= BuildLocalityCertificate(sourceGeneration);
            Assert.That(certificate,
                Has.Length.EqualTo(
                    SigmaNativeFrameSlotResources.CertificateWordCount));
            var certificates = new UInt4[
                scratch.LocalityCertificateWords.count];
            Array.Copy(certificate, 0, certificates,
                scratch.FootprintCertificateOffset,
                SigmaNativeFrameSlotResources.CertificateWordCount);
            scratch.LocalityCertificateWords.SetData(certificates);

            var close = new UInt2[scratch.CloseScratch.count];
            close[50] = new UInt2 { Low = sourceSlot, High = sourceSample };
            close[51] = new UInt2
            {
                Low = sourceGeneration,
                High = level | dispositionFlags,
            };
            close[scratch.TileFootprintScratchOffset] =
                new UInt2 { Low = 0u };
            close[scratch.TileFootprintScratchOffset + 2] =
                new UInt2 { Low = 0u, High = 0u };
            close[scratch.TileFootprintScratchOffset + 3] =
                new UInt2 { Low = 8u, High = 0u };
            close[scratch.TileComponentSummaryScratchOffset] =
                new UInt2 { High = 1u };
            close[scratch.TileComponentSummaryScratchOffset + 1] =
                new UInt2 { High = 1u | (1u << 17) };
            if (priorSupport)
            {
                uint locator = sourceSlot * SigmaCarrier.SamplesPerPage +
                    sourceSample;
                close[scratch.ActiveSupportMarkerScratchOffset + locator] =
                    new UInt2 { Low = 1u, High = 0u };
            }
            scratch.CloseScratch.SetData(close);
            scratch.StateDelta.SetData(new SigmaNativeStateDeltaGpu[
                SigmaNativeFrameSlotResources.MaximumMutationsPerFootprint]);
            scratch.GaugeDelta.SetData(new SigmaNativeGaugeDeltaGpu[
                SigmaNativeFrameSlotResources.MaximumMutationsPerFootprint]);
            scratch.Counters.SetData(new UInt4[scratch.Counters.count]);
        }

        private static UInt4[] BuildLocalityCertificate(uint generation)
        {
            var certificate = new UInt4[
                SigmaNativeFrameSlotResources.CertificateWordCount];
            certificate[0] = new UInt4
            {
                X = (uint)(SigmaNativeCertificateFlags.Valid |
                    SigmaNativeCertificateFlags.Directional |
                    SigmaNativeCertificateFlags.Minimized),
                Y = 1u,
                Z = 1u,
                W = generation,
            };
            certificate[12] = new UInt4
            {
                X = 11u,
                Y = 12u,
                Z = 13u,
                W = 14u,
            };
            certificate[3] = new UInt4
            {
                Y = (uint)SigmaMerkabaRelationClass.Regular,
            };
            return certificate;
        }

        private static UInt2[] StateWithLane0(uint low, uint high)
        {
            var state = new UInt2[SigmaS16.LaneCount];
            state[0] = new UInt2 { Low = low, High = high };
            return state;
        }

        private static StaticExclusionSnapshot RunStaticExclusion()
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
                GraphicsBuffer.Target.Structured,
                SigmaGeneratedFrame.CompletionWordCount, sizeof(uint) * 2);

            SeedSingleFootprintClosedCutE(scratch, 2u,
                new UInt2[SigmaS16.LaneCount], priorSupport: true,
                sourceGeneration: 1u,
                dispositionFlags: 0x100u);

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
                    Flags = 3u,
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
            DispatchCutE(frame, scratch);
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

        private static void DispatchCutE(ComputeShader shader,
            SigmaNativeFrameSlotResources scratch,
            Vector2Int? resolutionOverride = null)
        {
            Vector2Int resolution = resolutionOverride ?? Vector2Int.one;
            int footprintCount = checked(resolution.x * resolution.y);
            foreach (string kernelName in new[]
            {
                "PrepareNativeCanonicalSeed",
                "PrepareNativeCanonicalRuns",
                "PrepareNativeCanonicalSelect",
                "PrepareNativeRefinementProof",
                "PrepareNativeComponentOrder",
                "PrepareNativeRefinementScan",
                "PrepareNativeRefinementPlan",
                "PrepareNativeRevision",
            })
            {
                int groups = 1;
                if (kernelName == "PrepareNativeCanonicalRuns")
                {
                    int runSize = Math.Min(16384,
                        scratch.CanonicalImageStride);
                    groups = ((footprintCount +
                        runSize - 1) / runSize) * 8;
                }
                else if (kernelName == "PrepareNativeCanonicalSelect")
                    groups = (footprintCount *
                        8 + 255) / 256;
                else if (kernelName == "PrepareNativeRefinementProof")
                    groups = (footprintCount * 28 + 255) / 256;
                else if (kernelName == "PrepareNativeComponentOrder")
                    groups = (footprintCount + 255) / 256;
                if (kernelName == "PrepareNativeRefinementScan" ||
                    kernelName == "PrepareNativeRefinementPlan" ||
                    kernelName == "PrepareNativeRevision")
                {
                    shader.DispatchIndirect(shader.FindKernel(kernelName),
                        scratch.Counters, 2u * sizeof(uint) * 4u);
                }
                else
                    shader.Dispatch(shader.FindKernel(kernelName), groups, 1, 1);
            }
        }

        private static void BindPublication(ComputeShader shader, int kernel,
            SigmaNativeFrameSlotResources scratch, GraphicsBuffer carrierState,
            GraphicsBuffer carrierRepresentation, GraphicsBuffer metadata,
            GraphicsBuffer dirty,
            GraphicsBuffer readoutDirty, GraphicsBuffer root,
            GraphicsBuffer completionJournal,
            Vector2Int? resolutionOverride = null)
        {
            if (kernel == shader.FindKernel("PrepareNativeRevision"))
            {
                foreach (string stageName in new[]
                {
                    "PrepareNativeCanonicalSeed",
                    "PrepareNativeCanonicalRuns",
                    "PrepareNativeCanonicalSelect",
                    "PrepareNativeRefinementProof",
                    "PrepareNativeComponentOrder",
                    "PrepareNativeRefinementScan",
                    "PrepareNativeRefinementPlan",
                })
                    BindPublication(shader, shader.FindKernel(stageName), scratch,
                        carrierState, carrierRepresentation, metadata, dirty,
                        readoutDirty, root, completionJournal,
                        resolutionOverride);
            }
            shader.SetBuffer(kernel, "_NativeFrames", scratch.NativeFrame);
            shader.SetBuffer(kernel, "_NativeObservations", scratch.Observation);
            shader.SetBuffer(kernel, "_NativeCloseScratch",
                scratch.CloseScratch);
            shader.SetBuffer(kernel, "_NativeStateDeltas", scratch.StateDelta);
            shader.SetBuffer(kernel, "_NativeGaugeDeltas", scratch.GaugeDelta);
            shader.SetBuffer(kernel, "_NativeLocalityCertificateWords",
                scratch.LocalityCertificateWords);
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
            if (kernel == shader.FindKernel("CloseAndPublishNativeRevision"))
                shader.SetBuffer(kernel, "_NativeSourceCarrierState",
                    scratch.CloseScratch);
            shader.SetInt("_NativeCompletionRecordIndex", 0);
            shader.SetInts("_NativeProvenanceReceipt",
                unchecked((int)0x10203040u),
                unchecked((int)0x50607080u),
                unchecked((int)0x90a0b0c0u),
                unchecked((int)0xd0e0f000u),
                unchecked((int)0x11223344u),
                unchecked((int)0x55667788u),
                unchecked((int)0x99aabbccu),
                unchecked((int)0xddeeff00u));
            shader.SetInt("_NativeTargetSegmentIndex", 0);
            shader.SetBuffer(kernel, "_NativePrepareObservations",
                scratch.Observation);
            shader.SetBuffer(kernel, "_NativePrepareStates", scratch.States);
            Vector2Int resolution = resolutionOverride ?? Vector2Int.one;
            int footprintCount = checked(resolution.x * resolution.y);
            int boundaryCount = checked(Math.Max(0, resolution.x - 1) *
                resolution.y + resolution.x * Math.Max(0, resolution.y - 1));
            shader.SetInt("_NativeFootprintCount", footprintCount);
            shader.SetInt("_NativeBoundaryCount", boundaryCount);
            shader.SetInt("_NativeBoundaryScratchOffset",
                scratch.BoundaryScratchOffset);
            shader.SetInt("_NativeFootprintStateOffset",
                scratch.FootprintStateOffset);
            shader.SetInt("_NativeFootprintCertificateOffset",
                scratch.FootprintCertificateOffset);
            shader.SetInts("_NativeResolution", resolution.x, resolution.y);
            shader.SetInts("_NativeTileCount", scratch.TileCountX,
                scratch.TileCountY);
            shader.SetInt("_NativeTileHeaderScratchOffset",
                scratch.TileHeaderScratchOffset);
            shader.SetInt("_NativeTileFootprintScratchOffset",
                scratch.TileFootprintScratchOffset);
            shader.SetInt("_NativeTileComponentSummaryScratchOffset",
                scratch.TileComponentSummaryScratchOffset);
            shader.SetInt("_NativeGlobalHeaderScratchOffset",
                scratch.GlobalHeaderScratchOffset);
            shader.SetInt("_NativeActiveSupportMarkerScratchOffset",
                scratch.ActiveSupportMarkerScratchOffset);
            shader.SetInt("_NativeGlobalParentScratchOffset",
                scratch.GlobalParentScratchOffset);
            shader.SetInt("_NativeGlobalTransformScratchOffset",
                scratch.GlobalTransformScratchOffset);
            shader.SetInt("_NativeGlobalBorderComponentCapacity",
                scratch.GlobalBorderComponentCapacity);
            shader.SetInt("_NativeMutationCapacity", scratch.MutationCapacity);
            shader.SetInt("_NativePagePlanScratchOffset",
                scratch.PagePlanScratchOffset);
            shader.SetInt("_NativePagePlanCapacity", scratch.PagePlanCapacity);
            shader.SetInt("_NativeCanonicalComponentScratchOffset",
                scratch.CanonicalComponentScratchOffset);
            shader.SetInt("_NativeCanonicalComponentCapacity",
                scratch.CanonicalComponentCapacity);
            shader.SetInt("_NativeCanonicalImageScratchOffset",
                scratch.CanonicalImageScratchOffset);
            shader.SetInt("_NativeCanonicalImageStride",
                scratch.CanonicalImageStride);
            shader.SetInt("_NativeCanonicalRankScratchOffset",
                scratch.CanonicalRankScratchOffset);
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

        private static SigmaExactConstraintRecord CertifiedConstraintRecord(
            uint revision, long lower, long upper, uint leftMode,
            uint rightMode)
        {
            const uint programFingerprint = 0x89d6d581u;
            const uint certificateFingerprint = 0x0b0317cfu;
            uint proof = (uint)(SigmaNativeConstraintProofFlags.BoundLocality |
                SigmaNativeConstraintProofFlags.LosslessPullback);
            var constraint = new SigmaUnresolvedConstraintGpu
            {
                Observation = U4(revision, 7u, 13u, 0x7fu),
                Relation = U4((uint)SigmaMerkabaRelationClass.Regular,
                    (uint)SigmaMerkabaRelationClass.Regular, 0u, 0u),
                Evidence = U4(revision, revision * 3u + 1u,
                    revision * 5u + 1u, revision * 7u + 1u),
                Provenance = U4(1u, 0u, revision, 7u),
                Frontier = U4(17u, 0u, 23u, 0u),
                Program = U4(programFingerprint, certificateFingerprint, 0u,
                    proof),
            };
            var headers = new[]
            {
                U4(0x7fu, revision, 7u, revision * 2166136261u),
                U4(0u, 0u, 7u, programFingerprint),
            };
            var rays = new SigmaFrameUInt2Gpu[6];
            for (int index = 0; index < rays.Length; ++index)
                rays[index] = Q2((long)revision * 17L + index + 1L);
            var leaves = new SigmaFrameUInt2Gpu[16];
            for (int leaf = 0; leaf < 8; ++leaf)
            {
                leaves[leaf * 2] = Q2(lower);
                leaves[leaf * 2 + 1] = Q2(upper);
            }
            var certificate = new SigmaFrameUInt4Gpu[16];
            certificate[0] = U4((uint)(SigmaNativeCertificateFlags.Valid |
                    SigmaNativeCertificateFlags.Directional |
                    SigmaNativeCertificateFlags.Minimized),
                certificateFingerprint, revision, revision);
            certificate[1] = U4(7u, 0x7fu, 0u, 0u);
            certificate[2] = U4(revision, leftMode, rightMode, 0u);
            certificate[3] = constraint.Relation;
            for (int axis = 0; axis < 4; ++axis)
            {
                certificate[4 + axis] = Q4(lower, upper);
                ulong width = unchecked((ulong)upper - (ulong)lower);
                certificate[8 + axis] = U4(unchecked((uint)width),
                    unchecked((uint)(width >> 32)), (uint)axis, 3u);
            }
            certificate[12] = U4(7u, 0x7fu, 0u, 0u);
            certificate[13] = U4(0u, 0u, 7u, programFingerprint);
            certificate[14] = DirectionModeMask(leftMode, rightMode);
            certificate[15] = U4(certificateFingerprint, 0x6c99954eu,
                0x32819303u, 0xebb6e400u);
            return new SigmaExactConstraintRecord(constraint, headers, rays,
                leaves, certificate);
        }

        private static byte[] PersistAndReload(string directory, string file,
            SigmaExactConstraintJournal journal,
            out SigmaExactConstraintJournal reloaded)
        {
            string path = Path.Combine(directory, file);
            using (var store = new SigmaExactConstraintStore(path))
                store.Stage(journal);
            using var reopened = new SigmaExactConstraintStore(path);
            reloaded = reopened.Load();
            return reloaded.EncodeCanonical();
        }

        private static SigmaFrameUInt4Gpu DirectionModeMask(uint leftMode,
            uint rightMode)
        {
            var result = new SigmaFrameUInt4Gpu();
            if (leftMode < 32u) result.X = 1u << (int)leftMode;
            else result.Y = 1u << (int)(leftMode - 32u);
            if (rightMode < 32u) result.Z = 1u << (int)rightMode;
            else result.W = 1u << (int)(rightMode - 32u);
            return result;
        }

        private static SigmaFrameUInt4Gpu Q4(long lower, long upper) => new()
        {
            X = unchecked((uint)lower),
            Y = unchecked((uint)(lower >> 32)),
            Z = unchecked((uint)upper),
            W = unchecked((uint)(upper >> 32)),
        };

        private static string CertificateDifference(
            SigmaFrameUInt4Gpu[] left, SigmaFrameUInt4Gpu[] right)
        {
            for (int index = 0; index < left.Length; ++index)
                if (!left[index].Equals(right[index]))
                    return $"certificate word {index}: " +
                        $"{left[index].X}/{left[index].Y}/{left[index].Z}/" +
                        $"{left[index].W} != {right[index].X}/{right[index].Y}/" +
                        $"{right[index].Z}/{right[index].W}";
            return "no differing certificate word";
        }

        private static RefinementSnapshot RunRefinementSequence(
            RefinementStep[] steps, bool verifyFirstPreScatterCopy = false,
            int[] childByFootprint = null,
            bool broadenFreshCertificate = false)
        {
            ComputeShader frame = LoadShader("SigmaNativeFrame");
            int prepare = frame.FindKernel("PrepareNativeRevision");
            int clone = frame.FindKernel("PrepareNativePage");
            int scatter = frame.FindKernel("ScatterNativeState");
            int close = frame.FindKernel("CloseAndPublishNativeRevision");

            int footprintCount = childByFootprint?.Length ?? 4;
            childByFootprint ??= new[] { 0, 1, 2, 3 };
            Assert.That(childByFootprint.Length, Is.EqualTo(footprintCount));
            Assert.That(childByFootprint.All(child => child >= 0 && child < 4),
                Is.True);
            var refinementResolution = footprintCount == 4
                ? new Vector2Int(2, 2)
                : new Vector2Int(footprintCount, 1);
            using var scratch = new SigmaNativeFrameSlotResources(0,
                refinementResolution);
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
                GraphicsBuffer.Target.Structured,
                SigmaGeneratedFrame.CompletionWordCount, sizeof(uint) * 2);

            var state = new UInt2[carrierState.count];
            state[0].High = 10u << 16;
            state[SigmaS16.LaneCount].High = 20u << 16;
            carrierState.SetData(state);
            var representation = new UInt4[carrierRepresentation.count];
            WriteRepresentationCell(representation, 0, 0,
                new GaugeKey(0L, 0L, 0u), 1u);
            WriteRepresentationCell(representation, 0, 1,
                new GaugeKey(8L, 0L, 0u), 1u);
            if (broadenFreshCertificate)
            {
                UInt4 parentInterval = PackedInterval(-3L << 48, 3L << 48);
                for (int sample = 0; sample < 2; ++sample)
                {
                    int axis = sample *
                        SigmaCarrier.RepresentationWordsPerSample + 2 + 4;
                    representation[axis] = parentInterval;
                }
            }
            carrierRepresentation.SetData(representation);
            metadata.SetData(new[]
            {
                new PageMeta
                {
                    Generation = 1u,
                    Revision = 1u,
                    CertificateCount = 2u,
                    Flags = 3u,
                    GaugeGeneration = 1u,
                    CertificateGeneration = 1u,
                    RepresentationFlags = 3u,
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
                    root, completionJournal, refinementResolution);
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
                UInt4[] freshCertificate = priorCertificate;
                if (broadenFreshCertificate)
                {
                    freshCertificate = (UInt4[])priorCertificate.Clone();
                    freshCertificate[4] = PackedInterval(-8L << 48,
                        8L << 48);
                }
                PrepareRefinementScratch(scratch, revision, sourceSlot,
                    (uint)parentSample, sourceMeta.Generation, step,
                    priorState, freshCertificate, childByFootprint);

                frame.SetInt("_NativeRevision", (int)revision);
                DispatchCutE(frame, scratch, refinementResolution);
                frame.Dispatch(clone, Math.Max(SigmaCarrier.PageLaneCount,
                    SigmaCarrier.SamplesPerPage *
                    SigmaCarrier.RepresentationWordsPerSample) / 256, 1, 1);
                if (verifyFirstPreScatterCopy && revision == 2u)
                {
                    var counters = new UInt4[scratch.Counters.count];
                    scratch.Counters.GetData(counters);
                    int mutationCount = checked((int)counters[0].Y);
                    var stagedDeltas = new SigmaNativeStateDeltaGpu[
                        mutationCount];
                    var stagedGauge = new SigmaNativeGaugeDeltaGpu[
                        mutationCount];
                    scratch.StateDelta.GetData(stagedDeltas);
                    scratch.GaugeDelta.GetData(stagedGauge);
                    var refinementDebug = new UInt2[2];
                    scratch.CloseScratch.GetData(refinementDebug, 0,
                        scratch.ActiveSupportListScratchOffset, 2);
                    var representativeDebug = new UInt2[2];
                    scratch.CloseScratch.GetData(representativeDebug, 0,
                        scratch.CanonicalImageScratchOffset +
                            2 * scratch.CanonicalImageStride, 1);
                    scratch.CloseScratch.GetData(representativeDebug, 1,
                        scratch.CanonicalImageScratchOffset +
                            3 * scratch.CanonicalImageStride, 1);
                    var rankDebug = new UInt2[footprintCount];
                    scratch.CloseScratch.GetData(rankDebug, 0,
                        scratch.CanonicalRankScratchOffset, footprintCount);
                    var componentListDebug = new UInt2[1];
                    scratch.CloseScratch.GetData(componentListDebug, 0,
                        scratch.CanonicalImageScratchOffset +
                            8 * scratch.CanonicalImageStride, 1);
                    int component = componentListDebug[0].Low == uint.MaxValue
                        ? scratch.GlobalBorderComponentCapacity
                        : checked((int)componentListDebug[0].Low);
                    var componentDebug = new UInt2[2];
                    scratch.CloseScratch.GetData(componentDebug, 0,
                        scratch.CanonicalComponentScratchOffset + component * 10,
                        2);
                    var streamDebug = new UInt2[footprintCount];
                    if (componentDebug[0].High != uint.MaxValue)
                        scratch.CloseScratch.GetData(streamDebug, 0,
                            scratch.CanonicalImageScratchOffset +
                                checked((int)componentDebug[0].High),
                            Math.Min(footprintCount,
                                checked((int)componentDebug[0].Low)));
                    int refined = 0;
                    for (int mutation = 0; mutation < mutationCount; ++mutation)
                    {
                        if ((stagedGauge[mutation].Next.Y &
                            (uint)SigmaNativeGaugeCellFlags.Refined) == 0u)
                            continue;
                        refined++;
                        uint child = stagedGauge[mutation].Witness.X;
                        uint expected = child == step.SelectedChild
                            ? step.SelectedStateInteger : priorState[0].High >> 16;
                        Assert.That(stagedDeltas[mutation].State01.Y >> 16,
                            Is.EqualTo(expected), $"refinement child {child}");
                    }
                    Assert.That(refined, Is.EqualTo(4),
                        "The compact schedule must contain all four children. " +
                        $"mutations={mutationCount}, counters3=" +
                        $"{counters[3].X}/{counters[3].Y}/" +
                        $"{counters[3].Z}/{counters[3].W}, active=" +
                        $"{refinementDebug[0].Low}/{refinementDebug[0].High};" +
                        $"{refinementDebug[1].Low:x8}/" +
                        $"{refinementDebug[1].High:x8}, representatives=" +
                        $"{representativeDebug[0].Low:x8}/" +
                        $"{representativeDebug[0].High:x8};" +
                        $"{representativeDebug[1].Low:x8}/" +
                        $"{representativeDebug[1].High:x8}, ranks=" +
                        string.Join(",", rankDebug.Select(value =>
                            $"{value.Low:x8}/{value.High:x8}")) +
                        $", list={componentListDebug[0].Low}/" +
                        $"{componentListDebug[0].High}, component=" +
                        $"{componentDebug[0].Low}/" +
                        $"{componentDebug[0].High};" +
                        $"{componentDebug[1].Low}/{componentDebug[1].High}, " +
                        "stream=" + string.Join(",", streamDebug.Select(value =>
                            $"{value.Low:x8}/{value.High:x8}")) +
                        (mutationCount == 0 ? "." :
                            $", delta0={stagedDeltas[0].Changed.X:x8}/" +
                            $"{stagedDeltas[0].Changed.Y:x8}/" +
                            $"{stagedDeltas[0].Changed.Z:x8}/" +
                            $"{stagedDeltas[0].Changed.W:x8}, gauge0=" +
                            $"{stagedGauge[0].Next.X:x8}/" +
                            $"{stagedGauge[0].Next.Y:x8}."));
                    var stagedRoot = new uint[1];
                    root.GetData(stagedRoot);
                    Assert.That(stagedRoot[0], Is.EqualTo(revision - 1u),
                        "Prepared representation became visible before scatter/close.");
                }
                frame.Dispatch(scatter, 1, 1, 1);
                frame.Dispatch(close, 1, 1, 1);

                var terminal = new SigmaNativeFrameGpu[1];
                scratch.NativeFrame.GetData(terminal);
                var terminalCounters = new UInt4[scratch.Counters.count];
                scratch.Counters.GetData(terminalCounters);
                var terminalRoot = new uint[1];
                root.GetData(terminalRoot);
                PageMeta terminalMeta0 = ReadPageMeta(metadata, 0u);
                PageMeta terminalMeta1 = ReadPageMeta(metadata, 1u);
                var targetDebug = new UInt2[2];
                scratch.CloseScratch.GetData(targetDebug, 0,
                    scratch.ActiveSupportMarkerScratchOffset +
                        scratch.PagePlanCapacity * SigmaCarrier.SamplesPerPage,
                    2);
                var childRankDebug = new UInt2[2];
                scratch.CloseScratch.GetData(childRankDebug, 0,
                    scratch.CanonicalImageScratchOffset +
                        4 * scratch.CanonicalImageStride, 1);
                scratch.CloseScratch.GetData(childRankDebug, 1,
                    scratch.CanonicalImageScratchOffset +
                        5 * scratch.CanonicalImageStride, 1);
                var pagePlanDebug = new UInt2[4];
                scratch.CloseScratch.GetData(pagePlanDebug, 0,
                    scratch.PagePlanScratchOffset, 4);
                Assert.That(terminal[0].Disposition.X,
                    Is.EqualTo((uint)SigmaNativeFrameDisposition.Published),
                    $"refinement revision {revision}, reason=" +
                    $"{terminal[0].Disposition.W}, publication=" +
                    $"{terminal[0].Publication.X}/" +
                    $"{terminal[0].Publication.Y}/" +
                    $"{terminal[0].Publication.Z}/" +
                    $"{terminal[0].Publication.W}, counters0=" +
                    $"{terminalCounters[0].X}/{terminalCounters[0].Y}/" +
                    $"{terminalCounters[0].Z}/{terminalCounters[0].W}, " +
                    $"counters1={terminalCounters[1].X}/" +
                    $"{terminalCounters[1].Y}/{terminalCounters[1].Z}/" +
                    $"{terminalCounters[1].W}, root={terminalRoot[0]}, " +
                    $"meta0={terminalMeta0.Revision}/" +
                    $"{terminalMeta0.ActiveSampleCount}, meta1=" +
                    $"{terminalMeta1.Revision}/" +
                    $"{terminalMeta1.ActiveSampleCount}, targets=" +
                    $"{targetDebug[0].Low:x8}/{targetDebug[0].High};" +
                    $"{targetDebug[1].Low:x8}/{targetDebug[1].High}, " +
                    $"childRanks={childRankDebug[0].Low}/" +
                    $"{childRankDebug[0].High};{childRankDebug[1].Low}/" +
                    $"{childRankDebug[1].High}, pagePlan=" +
                    string.Join(",", pagePlanDebug.Select(value =>
                        $"{value.Low:x8}/{value.High:x8}")));
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
            UInt4[] priorCertificate, int[] childByFootprint = null)
        {
            int footprintCount = scratch.FootprintCapacity;
            childByFootprint ??= new[] { 0, 1, 2, 3 };
            Assert.That(childByFootprint.Length, Is.EqualTo(footprintCount));
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
            uint observationFlags = (uint)(
                SigmaNativeObservationFlags.Coherent |
                SigmaNativeObservationFlags.LeftFirstHit |
                SigmaNativeObservationFlags.RightFirstHit |
                SigmaNativeObservationFlags.LeftEvidence |
                SigmaNativeObservationFlags.RightEvidence |
                SigmaNativeObservationFlags.PriorSupport);
            var observations = new SigmaNativeObservationGpu[
                scratch.Observation.count];
            for (int footprint = 0; footprint < footprintCount; ++footprint)
            {
                observations[footprint + 1] = new SigmaNativeObservationGpu
                {
                    Identity = U4(revision, 7u, (uint)(13 + footprint),
                        observationFlags),
                    Evidence = U4(11u, 12u, 13u, 14u),
                };
            }
            scratch.Observation.SetData(observations);

            var states = new UInt2[scratch.States.count];
            for (int footprint = 0; footprint < footprintCount; ++footprint)
            {
                int offset = scratch.FootprintStateOffset +
                    footprint * SigmaS16.LaneCount;
                Array.Copy(priorState, 0, states, offset,
                    SigmaS16.LaneCount);
                if ((uint)childByFootprint[footprint] == step.SelectedChild)
                    states[offset].High = step.SelectedStateInteger << 16;
            }
            scratch.States.SetData(states);

            var certificates = new UInt4[
                scratch.LocalityCertificateWords.count];
            for (int footprint = 0; footprint < footprintCount; ++footprint)
                Array.Copy(priorCertificate, 0, certificates,
                    scratch.FootprintCertificateOffset + footprint *
                        SigmaNativeFrameSlotResources.CertificateWordCount,
                    SigmaNativeFrameSlotResources.CertificateWordCount);
            scratch.LocalityCertificateWords.SetData(certificates);

            var close = new UInt2[scratch.CloseScratch.count];
            uint support = sourceSlot * SigmaCarrier.SamplesPerPage +
                parentSample;
            close[scratch.ActiveSupportMarkerScratchOffset + support] =
                new UInt2 { Low = 0u, High = 0u };
            for (int footprint = 0; footprint < footprintCount; ++footprint)
            {
                int child = childByFootprint[footprint];
                int evidence = footprint *
                    SigmaNativeFrameSlotResources.FootprintEvidenceWordCount;
                close[evidence + 50] = new UInt2
                {
                    Low = sourceSlot,
                    High = parentSample,
                };
                close[evidence + 51] = new UInt2
                {
                    Low = generation,
                    High = step.Parent.Level,
                };
                int receipt = scratch.TileFootprintScratchOffset + footprint *
                    SigmaNativeFrameSlotResources.
                        TileFootprintReceiptWordCount;
                close[receipt] = new UInt2 { Low = 0u };
                close[receipt + 2] = new UInt2
                {
                    Low = (uint)(child & 1),
                    High = (uint)(child >> 1),
                };
                close[receipt + 3] = new UInt2 { Low = 8u };
            }
            close[scratch.TileComponentSummaryScratchOffset] =
                new UInt2 { High = 1u };
            close[scratch.TileComponentSummaryScratchOffset + 1] =
                new UInt2 { High = (uint)footprintCount | (1u << 17) };
            scratch.CloseScratch.SetData(close);
            scratch.StateDelta.SetData(new SigmaNativeStateDeltaGpu[
                scratch.StateDelta.count]);
            scratch.GaugeDelta.SetData(new SigmaNativeGaugeDeltaGpu[
                scratch.GaugeDelta.count]);
            scratch.Counters.SetData(new UInt4[scratch.Counters.count]);
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
            words[address + 14] = new UInt4
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

        private static UInt2 Packed(long value) => new()
        {
            Low = unchecked((uint)value),
            High = unchecked((uint)(value >> 32)),
        };

        private static UInt4 PackedInterval(long lower, long upper) => new()
        {
            X = unchecked((uint)lower),
            Y = unchecked((uint)(lower >> 32)),
            Z = unchecked((uint)upper),
            W = unchecked((uint)(upper >> 32)),
        };

        private static void WriteState(UInt2[] target, int offset,
            SigmaS16 state)
        {
            long[] lanes = state.ToArray();
            for (int lane = 0; lane < lanes.Length; ++lane)
                target[offset + lane] = Packed(lanes[lane]);
        }

        private static void WriteEnvelope(UInt2[] target, int footprint,
            int stride, int side, UInt2 point)
        {
            int offset = footprint * stride + 26 + side * 6;
            for (int axis = 0; axis < 3; ++axis)
            {
                target[offset + axis * 2] = point;
                target[offset + axis * 2 + 1] = point;
            }
        }

        private static SigmaFrameUInt2Gpu Q2(long value) => new()
        {
            X = unchecked((uint)value),
            Y = unchecked((uint)(value >> 32)),
        };

        private static SigmaFrameUInt4Gpu U4(uint x, uint y, uint z, uint w) =>
            new() { X = x, Y = y, Z = z, W = w };

        private static GraphicsBuffer UIntBuffer(int count) =>
            new(GraphicsBuffer.Target.Structured, count, sizeof(uint));

        private static GraphicsBuffer UInt2Buffer(int count) =>
            new(GraphicsBuffer.Target.Structured, count, sizeof(uint) * 2);

        private static GraphicsBuffer UInt4Buffer(int count) =>
            new(GraphicsBuffer.Target.Structured, count, sizeof(uint) * 4);

        private static string ReadAssetSource(string filter)
        {
            string[] guids = AssetDatabase.FindAssets(filter);
            Assert.That(guids, Has.Length.EqualTo(1), filter);
            return File.ReadAllText(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        private static RenderTexture ZeroArrayRenderTexture(int width,
            int height, GraphicsFormat format)
        {
            var descriptor = new RenderTextureDescriptor(width, height)
            {
                graphicsFormat = format,
                depthBufferBits = 0,
                msaaSamples = 1,
                dimension = UnityEngine.Rendering.TextureDimension.Tex2DArray,
                volumeDepth = 2,
                enableRandomWrite = true,
            };
            var texture = new RenderTexture(descriptor);
            Assert.That(texture.Create(), Is.True, format.ToString());
            RenderTexture previous = RenderTexture.active;
            for (int layer = 0; layer < 2; ++layer)
            {
                Graphics.SetRenderTarget(texture, 0, CubemapFace.Unknown,
                    layer);
                GL.Clear(false, true, Color.clear);
            }
            RenderTexture.active = previous;
            return texture;
        }

        private static RenderTexture ZeroRenderTexture(int width, int height,
            GraphicsFormat format)
        {
            var descriptor = new RenderTextureDescriptor(width, height)
            {
                graphicsFormat = format,
                depthBufferBits = 0,
                msaaSamples = 1,
                enableRandomWrite = true,
            };
            var texture = new RenderTexture(descriptor);
            Assert.That(texture.Create(), Is.True, format.ToString());
            RenderTexture previous = RenderTexture.active;
            Graphics.SetRenderTarget(texture);
            GL.Clear(false, true, Color.clear);
            RenderTexture.active = previous;
            return texture;
        }

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
