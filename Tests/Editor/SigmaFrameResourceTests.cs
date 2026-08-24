using System.Collections.Generic;
using System.Runtime.InteropServices;
using Genesis.RoomScan.SigmaPrism;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class SigmaFrameResourceTests
    {
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct UInt4
        {
            public uint X;
            public uint Y;
            public uint Z;
            public uint W;
        }

        [Test]
        public void GeneratedFrameAbiHasExactManagedStrides()
        {
            Assert.That(Marshal.SizeOf<SigmaOwnedFrameGpu>(),
                Is.EqualTo(SigmaGeneratedFrame.OwnedFrameStride));
            Assert.That(Marshal.SizeOf<SigmaFrameCandidateGpu>(),
                Is.EqualTo(SigmaGeneratedFrame.FrameCandidateStride));
            Assert.That(Marshal.SizeOf<SigmaFrameOutcomeGpu>(),
                Is.EqualTo(SigmaGeneratedFrame.FrameOutcomeStride));
            Assert.That(Marshal.SizeOf<SigmaPendingGaugeGpu>(),
                Is.EqualTo(SigmaGeneratedFrame.PendingGaugeStride));
            Assert.That(Marshal.SizeOf<SigmaFrameDeltaGpu>(),
                Is.EqualTo(SigmaGeneratedFrame.FrameDeltaStride));
            Assert.That(Marshal.SizeOf<SigmaDirtyEdgeGpu>(),
                Is.EqualTo(SigmaGeneratedFrame.DirtyEdgeStride));
            Assert.That(Marshal.SizeOf<SigmaFrameRevisionGpu>(),
                Is.EqualTo(SigmaGeneratedFrame.FrameRevisionStride));
            Assert.That(SigmaGeneratedFrame.SourceCount, Is.EqualTo(4));
            Assert.That(SigmaGeneratedFrame.LaneCount, Is.EqualTo(16));
        }

        [Test]
        public void SourceStorageIsCompleteAndSegmentRangeNeverExceedsBinding()
        {
            const int footprints = 320 * 320;
            long expected = (long)footprints * 4L *
                (16L * (8L + 8L + 4L) + 16L);
            Assert.That(SigmaFrameResources.EstimateSourceBytes(footprints),
                Is.EqualTo(expected));

            const long binding = 32L * 1024L * 1024L;
            int records = SigmaFrameResources.ComputeSegmentRecordCapacity(
                binding, SigmaGeneratedFrame.PackedQ48Stride);
            Assert.That((long)records * SigmaGeneratedFrame.PackedQ48Stride,
                Is.LessThan(binding));
            Assert.That(records % 256, Is.EqualTo(0));
        }

        [Test]
        public void EvidenceTransferSurvivesExactFrameSlotReuse()
        {
            using var resources = new SigmaFrameResources(new Vector2Int(8, 8),
                SigmaFrameMemoryProfile.Minimum, 32L * 1024L * 1024L);
            Assert.That(resources.TryAcquireFrame(1u, 1u, 11u, 12u, 13u,
                14u, 1u, 1u, out SigmaOwnedFrameLease first), Is.True);
            int slot = first.Slot;
            var expected = new SigmaFrameUInt2Gpu
                { X = 0x76543210u, Y = 0x12345678u };
            first.Sources.Lower(SigmaFrameSource.DepthLeft).Segment(0).Buffer
                .SetData(new[] { expected });
            var published = new SigmaOwnedFrameGpu
            {
                Identity = new SigmaFrameUInt4Gpu { X = 1u },
                PoseSource = new SigmaFrameUInt4Gpu
                {
                    Z = (uint)SigmaOwnedFrameState.EvidenceRetained,
                },
            };
            Assert.That(SigmaFrameResources.IsPublishedEvidence(
                published, 1u, 1u), Is.True);
            Assert.That(SigmaFrameResources.IsPublishedEvidence(
                published, 2u, 1u), Is.True);
            Assert.That(SigmaFrameResources.IsPublishedEvidence(
                published, 0u, 1u), Is.False);
            Assert.That(SigmaFrameResources.IsPublishedEvidence(
                published, 2u, 2u), Is.False);
            published.PoseSource.Z =
                (uint)SigmaOwnedFrameState.Resolved;
            Assert.That(SigmaFrameResources.IsPublishedEvidence(
                published, 2u, 1u), Is.False);
            first.TransferEvidence(1u);
            first.Dispose();

            Assert.That(resources.TryAcquireFrame(2u, 1u, 21u, 22u, 23u,
                24u, 2u, 2u, out SigmaOwnedFrameLease second), Is.True);
            Assert.That(second.Slot, Is.EqualTo(slot));
            second.Sources.Lower(SigmaFrameSource.DepthLeft).Segment(0).Buffer
                .SetData(new[] { new SigmaFrameUInt2Gpu
                    { X = 0xaaaaaaaau, Y = 0xbbbbbbbbu } });

            SigmaFrameSourceStorage retained =
                resources.GetRetainedEvidence(1u);
            var actual = new SigmaFrameUInt2Gpu[1];
            retained
                .Lower(SigmaFrameSource.DepthLeft).Segment(0).Buffer
                .GetData(actual);
            Assert.That(actual[0].X, Is.EqualTo(expected.X));
            Assert.That(actual[0].Y, Is.EqualTo(expected.Y));

            resources.ReleaseRetainedEvidence(1u);
            Assert.Throws<KeyNotFoundException>(() =>
                resources.GetRetainedEvidence(1u));
            Assert.That(resources.TryAcquireFrame(3u, 1u, 31u, 32u, 33u,
                34u, 3u, 3u, out SigmaOwnedFrameLease third), Is.True);
            Assert.That(third.Slot, Is.Not.EqualTo(second.Slot));
            Assert.That(third.Sources, Is.SameAs(retained));
            third.Dispose();
            second.Dispose();
        }

        [Test]
        public void VulkanFixtureCompilesAllRecordsWithinEightUavs()
        {
            string[] guids = AssetDatabase.FindAssets(
                "SigmaFrameAbiFixture t:ComputeShader");
            Assert.That(guids, Has.Length.EqualTo(1));
            ComputeShader shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                AssetDatabase.GUIDToAssetPath(guids[0]));
            Assert.That(shader, Is.Not.Null);
            int kernel = shader.FindKernel("ValidateFrameAbi");

            using var owned = Buffer<SigmaOwnedFrameGpu>(
                SigmaGeneratedFrame.OwnedFrameStride);
            using var candidates = Buffer<SigmaFrameCandidateGpu>(
                SigmaGeneratedFrame.FrameCandidateStride);
            using var outcomes = Buffer<SigmaFrameOutcomeGpu>(
                SigmaGeneratedFrame.FrameOutcomeStride);
            using var pending = Buffer<SigmaPendingGaugeGpu>(
                SigmaGeneratedFrame.PendingGaugeStride);
            using var deltas = Buffer<SigmaFrameDeltaGpu>(
                SigmaGeneratedFrame.FrameDeltaStride);
            using var edges = Buffer<SigmaDirtyEdgeGpu>(
                SigmaGeneratedFrame.DirtyEdgeStride);
            using var revisions = Buffer<SigmaFrameRevisionGpu>(
                SigmaGeneratedFrame.FrameRevisionStride);
            using var result = Buffer<UInt4>(Marshal.SizeOf<UInt4>());
            shader.SetBuffer(kernel, "_OwnedFrames", owned);
            shader.SetBuffer(kernel, "_Candidates", candidates);
            shader.SetBuffer(kernel, "_Outcomes", outcomes);
            shader.SetBuffer(kernel, "_PendingGauges", pending);
            shader.SetBuffer(kernel, "_Deltas", deltas);
            shader.SetBuffer(kernel, "_DirtyEdges", edges);
            shader.SetBuffer(kernel, "_Revisions", revisions);
            shader.SetBuffer(kernel, "_Result", result);
            shader.Dispatch(kernel, 1, 1, 1);
            var actual = new UInt4[1];
            result.GetData(actual);
            Assert.That(actual[0].X, Is.EqualTo(1u));
            Assert.That(actual[0].Y, Is.EqualTo(24u));
            Assert.That(actual[0].Z,
                Is.EqualTo((uint)SigmaFrameClaimKind.Contact));
            Assert.That(actual[0].W, Is.EqualTo(80u));
        }

        private static GraphicsBuffer Buffer<T>(int stride) where T : struct =>
            new(GraphicsBuffer.Target.Structured, 1, stride);
    }
}
