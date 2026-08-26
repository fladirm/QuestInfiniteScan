using System;
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
                "Queue<SigmaUnresolvedEvidenceRecord> _unresolvedEvidence"));
            Assert.That(controllerSource, Does.Contain(
                "_freshCodeLeavesReadback"));
            Assert.That(controllerSource, Does.Contain(
                "ReleaseTransientInputs();\n" +
                "                    _completionReadbackPending = true"));

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
        public void LiveScratchCarriesRawInstrumentBoundaryNotPrebuiltRows()
        {
            using var resources = new SigmaNativeFrameResources(
                new Vector2Int(320, 320), 3);
            Assert.That(resources.TryLease(out int slot,
                out SigmaNativeFrameSlotResources native), Is.True);
            try
            {
                Assert.That(slot, Is.Zero);
                Assert.That(native.FreshObservationHeaders.count,
                    Is.EqualTo(
                        SigmaNativeFrameSlotResources.FreshBranchCapacity * 2));
                Assert.That(native.FreshRoomRays.count,
                    Is.EqualTo(
                        SigmaNativeFrameSlotResources.FreshBranchCapacity * 6));
                Assert.That(native.FreshCodeLeaves.count,
                    Is.EqualTo(
                        SigmaNativeFrameSlotResources.FreshBranchCapacity * 16));
                Assert.That(native.GaugeDelta.count, Is.EqualTo(1));
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
            using var metadata = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 2,
                SigmaCarrier.PageMetadataStride);
            using var dirty = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 2, sizeof(uint));
            using var readoutDirty = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 2, sizeof(uint));
            using var root = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 1, sizeof(uint));

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
            carrierState.SetData(new UInt2[carrierState.count]);
            metadata.SetData(new PageMeta[metadata.count]);
            dirty.SetData(new uint[2]);
            readoutDirty.SetData(new uint[2]);
            root.SetData(new uint[1]);

            foreach (int kernel in new[] { prepare, clone, scatter, close })
                BindPublication(frame, kernel, scratch, carrierState, metadata,
                    dirty, readoutDirty, root);
            frame.SetInt("_NativeRevision", 1);
            frame.SetInt("_NativeCalibrationEpoch", 7);
            frame.SetInt("_NativeTargetPageCapacity", 2);
            frame.Dispatch(prepare, 1, 1, 1);
            frame.Dispatch(clone, SigmaCarrier.PageLaneCount / 256, 1, 1);
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
            using var metadata = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 2,
                SigmaCarrier.PageMetadataStride);
            using var dirty = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 2, sizeof(uint));
            using var readoutDirty = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 2, sizeof(uint));
            using var root = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 1, sizeof(uint));

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
            var carrier = new UInt2[carrierState.count];
            carrier[0] = new UInt2 { Low = 0x89abcdefu, High = 0x01234567u };
            carrierState.SetData(carrier);
            metadata.SetData(new PageMeta[2]);
            dirty.SetData(new uint[2]);
            readoutDirty.SetData(new uint[2]);
            root.SetData(new[] { 1u });

            foreach (int kernel in new[] { prepare, clone, scatter, close })
                BindPublication(frame, kernel, scratch, carrierState, metadata,
                    dirty, readoutDirty, root);
            frame.SetInt("_NativeRevision", 2);
            frame.SetInt("_NativeCalibrationEpoch", 7);
            frame.SetInt("_NativeTargetPageCapacity", 2);
            frame.Dispatch(prepare, 1, 1, 1);
            frame.Dispatch(clone, SigmaCarrier.PageLaneCount / 256, 1, 1);
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

        private static ComputeShader LoadShader(string name)
        {
            string[] guids = AssetDatabase.FindAssets($"{name} t:ComputeShader");
            Assert.That(guids, Has.Length.EqualTo(1), name);
            ComputeShader shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                AssetDatabase.GUIDToAssetPath(guids[0]));
            Assert.That(shader, Is.Not.Null, name);
            return shader;
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
            GraphicsBuffer metadata, GraphicsBuffer dirty,
            GraphicsBuffer readoutDirty, GraphicsBuffer root)
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
            shader.SetBuffer(kernel, "_NativeUnresolved", scratch.Unresolved);
            shader.SetBuffer(kernel, "_NativeRevisions", scratch.Revisions);
            shader.SetBuffer(kernel, "_NativeCounters", scratch.Counters);
            shader.SetBuffer(kernel, "_NativeSourceCarrierState", carrierState);
            shader.SetBuffer(kernel, "_NativeSourcePageMetadata", metadata);
            shader.SetBuffer(kernel, "_NativeSourcePublicationRoot", root);
            shader.SetBuffer(kernel, "_TargetCarrierState", carrierState);
            shader.SetBuffer(kernel, "_TargetPageMetadata", metadata);
            shader.SetBuffer(kernel, "_TargetDirtyFlags", dirty);
            shader.SetBuffer(kernel, "_TargetReadoutDirtyFlags", readoutDirty);
            shader.SetBuffer(kernel, "_PublishedRevisionRoot", root);
        }

        private static SigmaFrameUInt4Gpu U4(uint x, uint y, uint z, uint w) =>
            new() { X = x, Y = y, Z = z, W = w };

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
            internal uint Reserved0;
            internal uint Reserved1;
        }
    }
}
