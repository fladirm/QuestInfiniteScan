using System;
using System.Runtime.InteropServices;
using Genesis.RoomScan.SigmaPrism;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Genesis.RoomScan.Tests
{
    public sealed class SigmaFrameInverseTests
    {
        private const int Width = 8;
        private const int Height = 8;
        private const int Footprints = Width * Height;
        private const int Lanes = 16;
        private const int Proposals = 4;
        private const int NovelProposalSlot = Proposals - 1;
        private const int DepthCalibrationStride = 36;
        private const int RgbCalibrationStride = 8;
        private const uint DepthLeftKey = 11u;
        private const uint DepthRightKey = 22u;
        private const uint RgbLeftKey = 33u;
        private const uint RgbRightKey = 44u;

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct UInt2
        {
            public uint X;
            public uint Y;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct UInt4
        {
            public uint X;
            public uint Y;
            public uint Z;
            public uint W;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct PageMeta
        {
            public uint XLo;
            public uint XHi;
            public uint YLo;
            public uint YHi;
            public uint Generation;
            public uint Revision;
            public uint CertificateLo;
            public uint CertificateHi;
            public uint CertificateCount;
            public uint Flags;
            public uint SourceSlot;
            public uint Root;
        }

        private sealed class Snapshot
        {
            internal readonly UInt2[][] Lower = new UInt2[4][];
            internal readonly UInt2[][] Upper = new UInt2[4][];
            internal readonly uint[][] Validity = new uint[4][];
            internal readonly UInt4[][] Provenance = new UInt4[4][];
            internal SigmaFrameCandidateGpu[] Candidates;
            internal SigmaFrameOutcomeGpu[] Outcomes;
            internal UInt2[] CandidateStates;
        }

        private sealed class PublicationSnapshot
        {
            internal uint Root;
            internal UInt4[] Counters;
            internal SigmaOwnedFrameGpu[] Owned;
            internal SigmaFrameDeltaGpu[] Targets;
            internal uint[] Deferred;
            internal SigmaFrameRevisionGpu[] Revisions;
            internal PageMeta[] Metadata;
            internal uint[] Current;
            internal uint[] ReadoutDirty;
            internal UInt2[] State;
        }

        private sealed class ReductionSnapshot
        {
            internal uint TargetCount;
            internal SigmaFrameDeltaGpu Target;
            internal UInt2[] Lower;
            internal UInt2[] Upper;
            internal UInt2[] Gap;
            internal uint[] Validity;
            internal UInt2[] State;
            internal uint FaultStage;
        }

        private readonly struct ClosureTarget
        {
            internal ClosureTarget(int footprint, SigmaFrameProposalKind kind,
                long[] state, uint sourceMask, bool changed = true,
                bool pending = false, long[] geometryLower = null,
                long[] geometryUpper = null)
            {
                Footprint = footprint;
                Kind = kind;
                State = state;
                SourceMask = sourceMask;
                Changed = changed;
                Pending = pending;
                GeometryLower = geometryLower;
                GeometryUpper = geometryUpper;
            }

            internal int Footprint { get; }
            internal SigmaFrameProposalKind Kind { get; }
            internal long[] State { get; }
            internal uint SourceMask { get; }
            internal bool Changed { get; }
            internal bool Pending { get; }
            internal long[] GeometryLower { get; }
            internal long[] GeometryUpper { get; }
        }

        private sealed class ClosureSnapshot
        {
            internal SigmaFrameDeltaGpu[] Targets;
            internal uint[] Labels;
            internal uint[] Links;
            internal uint[] Deferred;
            internal SigmaPendingGaugeGpu[] Gauges;
            internal SigmaDirtyEdgeGpu[] Edges;
            internal UInt4[] Counters;
            internal UInt4 Control;
        }

        [Test]
        public void RoomFrameKeepsCanonicalPoseStableAcrossAnchorCorrection()
        {
            Vector3 roomPosition = new(1.25f, -0.4f, 2.1f);
            Quaternion roomRotation = Quaternion.Euler(8f, 32f, -4f);
            Matrix4x4 roomFromCamera = Matrix4x4.TRS(roomPosition,
                roomRotation, Vector3.one);
            Matrix4x4 firstAnchor = Matrix4x4.TRS(
                new Vector3(3f, 1f, -2f),
                Quaternion.Euler(0f, 47f, 0f), Vector3.one);
            Matrix4x4 correctedAnchor = Matrix4x4.TRS(
                new Vector3(2.7f, 1.15f, -1.8f),
                Quaternion.Euler(0f, 41f, 0f), Vector3.one);
            Pose firstWorldPose = ComposePose(firstAnchor, roomPosition,
                roomRotation);
            Pose correctedWorldPose = ComposePose(correctedAnchor,
                roomPosition, roomRotation);

            Matrix4x4 firstRoom = SigmaRoomFrame.FromCamera(
                firstAnchor.inverse, firstWorldPose);
            Matrix4x4 correctedRoom = SigmaRoomFrame.FromCamera(
                correctedAnchor.inverse, correctedWorldPose);
            Matrix4x4 presentedWorld = correctedAnchor * correctedRoom;

            AssertMatrix(firstRoom, roomFromCamera, 2e-5f);
            AssertMatrix(correctedRoom, roomFromCamera, 2e-5f);
            AssertMatrix(presentedWorld,
                Matrix4x4.TRS(correctedWorldPose.position,
                    correctedWorldPose.rotation, Vector3.one), 2e-5f);
        }

        [Test]
        public void WholeFrameFourSourceInverseIsExactAndPartitionInvariant()
        {
            using var fixture = new FrameFixture();
            Snapshot whole = fixture.Run(int.MaxValue);
            Snapshot partitioned = fixture.Run(3);

            for (int source = 0; source < 4; ++source)
            {
                Assert.That(partitioned.Lower[source],
                    Is.EqualTo(whole.Lower[source]), $"source {source} lower");
                Assert.That(partitioned.Upper[source],
                    Is.EqualTo(whole.Upper[source]), $"source {source} upper");
                Assert.That(partitioned.Validity[source],
                    Is.EqualTo(whole.Validity[source]), $"source {source} validity");
                Assert.That(partitioned.Provenance[source],
                    Is.EqualTo(whole.Provenance[source]),
                    $"source {source} provenance");
            }
            Assert.That(partitioned.Outcomes, Is.EqualTo(whole.Outcomes));
            Assert.That(partitioned.CandidateStates,
                Is.EqualTo(whole.CandidateStates));

            int center = 3 * Width + 3;
            uint[] keys = { DepthLeftKey, DepthRightKey, RgbLeftKey, RgbRightKey };
            for (int source = 0; source < 4; ++source)
            {
                Assert.That(Contains(whole.Provenance[source][center], keys[source]),
                    Is.True, $"source {source} lost its independent key");
                int constrained = ConstrainedLaneCount(
                    whole.Validity[source], center);
                if (source < 2)
                    Assert.That(constrained, Is.GreaterThan(0),
                        $"depth source {source} emitted no exact cell");
                else
                    Assert.That(constrained > 0 ||
                        (whole.Provenance[source][center].W &
                            (uint)SigmaFrameCellFlags.Unobservable) != 0u,
                        Is.True, $"RGB source {source} emitted neither an exact " +
                        "constraint nor an exact UNOBSERVABLE cell");
            }

            int novel = center * Proposals + NovelProposalSlot;
            long[] gpuState = Candidate(whole.CandidateStates, novel);
            SigmaFrameOutcomeGpu novelOutcome = whole.Outcomes[novel];
            SigmaFrameCandidateGpu novelCandidate = whole.Candidates[novel];
            Assert.That(SigmaGeometryReadout.TryRead(SigmaS16.FromArray(gpuState),
                out SigmaGeometrySample readout), Is.True,
                $"outcome={novelOutcome.Classification.X:x8}/" +
                $"{novelOutcome.Classification.Y:x8}/" +
                $"{novelOutcome.Classification.Z:x8}/" +
                $"{novelOutcome.Classification.W:x8}; " +
                $"candidate={novelCandidate.Identity.X}/" +
                $"{novelCandidate.Identity.Y}/" +
                $"{novelCandidate.Identity.Z}/" +
                $"{novelCandidate.Identity.W}; " +
                $"state0={gpuState[0]},state1={gpuState[1]}");
            Assert.That(readout.Position.z, Is.EqualTo(2f).Within(0.03f));
            Assert.That(whole.Outcomes[novel],
                Is.Not.EqualTo(default(SigmaFrameOutcomeGpu)));

            long[] expected = BuildCpuOracle(whole, center);
            Assert.That(gpuState, Is.EqualTo(expected),
                "GPU whole-frame inverse diverged from the exact CPU oracle");
        }

        [Test]
        public void DirectFramePublicationIsAtomicAndLayoutInvariant()
        {
            PublicationSnapshot compact;
            PublicationSnapshot roomy;
            using (var fixture = new FrameFixture())
                compact = fixture.RunPublished(int.MaxValue, 4);
            using (var fixture = new FrameFixture())
                roomy = fixture.RunPublished(3, 8);

            AssertPublished(compact);
            AssertPublished(roomy);
            PageMeta compactPage = CurrentPage(compact, out int compactSlot);
            PageMeta roomyPage = CurrentPage(roomy, out int roomySlot);
            Assert.That(roomyPage.XLo, Is.EqualTo(compactPage.XLo));
            Assert.That(roomyPage.XHi, Is.EqualTo(compactPage.XHi));
            Assert.That(roomyPage.YLo, Is.EqualTo(compactPage.YLo));
            Assert.That(roomyPage.YHi, Is.EqualTo(compactPage.YHi));
            Assert.That(roomyPage.Generation,
                Is.EqualTo(compactPage.Generation));
            Assert.That(roomyPage.Revision, Is.EqualTo(compactPage.Revision));
            Assert.That(roomyPage.CertificateLo,
                Is.EqualTo(compactPage.CertificateLo));
            Assert.That(roomyPage.CertificateHi,
                Is.EqualTo(compactPage.CertificateHi));
            Assert.That(roomyPage.CertificateCount,
                Is.EqualTo(compactPage.CertificateCount));
            Assert.That(roomyPage.Root, Is.EqualTo(compactPage.Root));
            Assert.That(roomy.Revisions[0].WitnessJournal,
                Is.EqualTo(compact.Revisions[0].WitnessJournal));
            Assert.That(PageState(roomy, roomySlot),
                Is.EqualTo(PageState(compact, compactSlot)));

            int supported = 0;
            UInt2[] state = PageState(compact, compactSlot);
            for (int sample = 0; sample < SigmaCarrier.SamplesPerPage; ++sample)
            {
                var raw = new long[Lanes];
                for (int lane = 0; lane < Lanes; ++lane)
                    raw[lane] = Signed(state[sample * Lanes + lane]);
                if (SigmaGeometryReadout.TryRead(SigmaS16.FromArray(raw),
                        out _))
                    ++supported;
            }
            Assert.That(supported, Is.EqualTo(Footprints));
        }

        [Test]
        public void ProductionFrameDispatchesEveryWindowAndNormalizesTargets()
        {
            const long bindingLimit = 8L * 1024L * 1024L;
            using var fixture = new FrameFixture(320, 320, bindingLimit);
            fixture.RunSegmentedTargetFixture();
        }

        [Test]
        public void Production320FrameLargeBindingRecordsOnlyLegalDispatches()
        {
            const long largeBinding = 512L * 1024L * 1024L;
            using var fixture = new FrameFixture(320, 320, largeBinding);
            Assert.That(fixture.ExecutionWindowCount, Is.EqualTo(1));
            fixture.RecordProductionGraphOnly();
        }

        [Test]
        public void TargetReductionIsExactPermutationAndWindowInvariant()
        {
            const long bindingLimit = 160L * 1024L;
            const long largeBinding = 512L * 1024L * 1024L;
            ReductionSnapshot whole;
            using (var fixture = new FrameFixture(1024, 1, largeBinding))
            {
                Assert.That(fixture.ExecutionWindowCount, Is.EqualTo(1));
                whole = fixture.RunTargetReduction(false);
            }
            ReductionSnapshot forward;
            ReductionSnapshot reverse;
            using (var fixture = new FrameFixture(1024, 1, bindingLimit))
            {
                Assert.That(fixture.ExecutionWindowCount, Is.EqualTo(4));
                forward = fixture.RunTargetReduction(false);
                reverse = fixture.RunTargetReduction(true);
            }

            Assert.That(forward.TargetCount, Is.EqualTo(1u));
            Assert.That(reverse.TargetCount, Is.EqualTo(1u));
            Assert.That(forward.TargetCount, Is.EqualTo(whole.TargetCount));
            AssertUInt4(forward.Target.Coordinate, whole.Target.Coordinate);
            AssertUInt4(forward.Target.Candidate, whole.Target.Candidate);
            AssertUInt4(forward.Target.Evidence, whole.Target.Evidence);
            Assert.That(forward.Lower, Is.EqualTo(whole.Lower));
            Assert.That(forward.Upper, Is.EqualTo(whole.Upper));
            Assert.That(forward.Gap, Is.EqualTo(whole.Gap));
            Assert.That(forward.Validity, Is.EqualTo(whole.Validity));
            Assert.That(forward.State, Is.EqualTo(whole.State));
            AssertUInt4(reverse.Target.Coordinate, forward.Target.Coordinate);
            AssertUInt4(reverse.Target.Candidate, forward.Target.Candidate);
            AssertUInt4(reverse.Target.Evidence, forward.Target.Evidence);
            Assert.That(reverse.Lower, Is.EqualTo(forward.Lower));
            Assert.That(reverse.Upper, Is.EqualTo(forward.Upper));
            Assert.That(reverse.Gap, Is.EqualTo(forward.Gap));
            Assert.That(reverse.Validity, Is.EqualTo(forward.Validity));
            Assert.That(reverse.State, Is.EqualTo(forward.State));

            int coordinate = SigmaGeneratedAlgebra.GeometryRows[1];
            Assert.That(Signed(forward.Lower[coordinate]),
                Is.EqualTo(SigmaNumericDomain.Quantize(0.30)));
            Assert.That(Signed(forward.Upper[coordinate]),
                Is.EqualTo(SigmaNumericDomain.Quantize(0.40)));
            Assert.That(Signed(forward.Gap[coordinate]), Is.EqualTo(0L));
            Assert.That(forward.Validity[coordinate] &
                (uint)SigmaFrameCellFlags.Constrained, Is.Not.EqualTo(0u));
            Assert.That(forward.Validity[coordinate] &
                (uint)SigmaFrameCellFlags.Accepted, Is.Not.EqualTo(0u));
            Assert.That(forward.Target.Candidate.X, Is.EqualTo(9u));
            Assert.That(forward.Target.Candidate.Y, Is.EqualTo(0u));
            Assert.That(forward.Target.Candidate.Z, Is.EqualTo(3u));
            Assert.That(forward.Target.Candidate.W, Is.EqualTo(7u));
            Assert.That(forward.Target.Evidence.Y &
                (uint)SigmaFrameOutcomeFlags.Accepted, Is.Not.EqualTo(0u),
                $"final target outcome=0x{forward.Target.Evidence.Y:x8}, " +
                $"faultStage=0x{forward.FaultStage:x8}, validity=" +
                string.Join(",", Array.ConvertAll(forward.Validity,
                    value => value.ToString("x8"))));
            Assert.That(forward.Target.Evidence.Y &
                (uint)SigmaFrameOutcomeFlags.Unchanged, Is.EqualTo(0u));
        }

        [Test]
        public void UnobservedAdjacencyMakesNoTransitionClaim()
        {
            using var fixture = new FrameFixture();
            ClosureSnapshot snapshot = fixture.RunExactClosure(
                new ClosureTarget(0, SigmaFrameProposalKind.Novel,
                    ScalarState(1), 0x3u),
                new ClosureTarget(1, SigmaFrameProposalKind.Novel,
                    ScalarState(2), 0u));

            Assert.That(snapshot.Edges[0].Closure.X,
                Is.EqualTo((uint)SigmaFrameClaimKind.None));
            Assert.That(snapshot.Edges[0].Closure.Y & 3u,
                Is.EqualTo((uint)SigmaTopologyClass.Unsupported));
            Assert.That(snapshot.Deferred[0], Is.Zero);
            Assert.That(snapshot.Deferred[1], Is.Zero);
            Assert.That(snapshot.Labels[0], Is.Not.EqualTo(snapshot.Labels[1]));
        }

        [Test]
        public void ExactRegularEdgesJoinPendingAndAnchorContinuation()
        {
            using var fixture = new FrameFixture();
            ClosureSnapshot pending = fixture.RunExactClosure(
                new ClosureTarget(0, SigmaFrameProposalKind.Novel,
                    ScalarState(1), 0x3u),
                new ClosureTarget(1, SigmaFrameProposalKind.Novel,
                    ScalarState(2), 0x3u));

            Assert.That(pending.Edges[0].Closure.Y & 3u,
                Is.EqualTo((uint)SigmaTopologyClass.Regular));
            Assert.That(pending.Labels[0], Is.EqualTo(pending.Labels[1]));
            Assert.That(pending.Targets[0].Evidence.Z,
                Is.EqualTo((uint)SigmaFrameProposalKind.Novel));
            Assert.That(pending.Targets[1].Evidence.Z,
                Is.EqualTo((uint)SigmaFrameProposalKind.Novel));

            ClosureSnapshot continuation = fixture.RunExactClosure(
                new ClosureTarget(0, SigmaFrameProposalKind.Current,
                    ScalarState(1), 0x3u, false),
                new ClosureTarget(1, SigmaFrameProposalKind.Novel,
                    ScalarState(2), 0x3u));
            Assert.That(continuation.Edges[0].Closure.Y & 3u,
                Is.EqualTo((uint)SigmaTopologyClass.Regular));
            Assert.That(continuation.Targets[1].Evidence.Z,
                Is.EqualTo((uint)SigmaFrameProposalKind.Continuation));
            Assert.That(continuation.Links[1], Is.EqualTo(0u));
        }

        [Test]
        public void ClaimedUnresolvedEdgeDefersOnlyIncidentChanges()
        {
            using var fixture = new FrameFixture();
            GeometryRelationCell(out long[] lower, out long[] upper);
            ClosureSnapshot snapshot = fixture.RunExactClosure(
                new ClosureTarget(0, SigmaFrameProposalKind.Novel,
                    ScalarState(1), 0x1u, geometryLower: lower,
                    geometryUpper: upper),
                new ClosureTarget(1, SigmaFrameProposalKind.Novel,
                    ContactZeroDivisorState(), 0x1u, geometryLower: lower,
                    geometryUpper: upper),
                new ClosureTarget(Footprints - 1,
                    SigmaFrameProposalKind.Novel, ScalarState(3), 0x3u));

            Assert.That(snapshot.Edges[0].Closure.X,
                Is.EqualTo((uint)SigmaFrameClaimKind.Contact));
            Assert.That(snapshot.Edges[0].Closure.Y & 3u,
                Is.EqualTo((uint)SigmaTopologyClass.Unresolved));
            Assert.That(snapshot.Deferred[0], Is.EqualTo(1u));
            Assert.That(snapshot.Deferred[1], Is.EqualTo(1u));
            Assert.That(snapshot.Deferred[2], Is.Zero);
            Assert.That(snapshot.Targets[0].Evidence.Y &
                (uint)SigmaFrameOutcomeFlags.Accepted, Is.Zero);
            Assert.That(snapshot.Targets[0].Evidence.Y &
                (uint)SigmaFrameOutcomeFlags.Deferred, Is.Not.Zero);
            Assert.That(snapshot.Targets[2].Evidence.Y &
                (uint)SigmaFrameOutcomeFlags.Accepted, Is.Not.Zero);
        }

        [Test]
        public void SeparatedExactCellsRemainDistinctWithoutPhysicalClaim()
        {
            using var fixture = new FrameFixture();
            long mass = SigmaNumericDomain.FromInteger(8);
            long[] front = SigmaGeometryReadout.LiftFixture(mass,
                0L, 0L,
                SigmaNumericDomain.Quantize(0.500)).ToArray();
            long[] back = SigmaGeometryReadout.LiftFixture(mass,
                0L, 0L,
                SigmaNumericDomain.Quantize(0.505)).ToArray();
            ClosureSnapshot snapshot = fixture.RunExactClosure(
                new ClosureTarget(0, SigmaFrameProposalKind.Novel,
                    front, 0x3u),
                new ClosureTarget(1, SigmaFrameProposalKind.Novel,
                    back, 0x3u));

            Assert.That(snapshot.Edges[0].Closure.X,
                Is.EqualTo((uint)SigmaFrameClaimKind.None));
            Assert.That(snapshot.Edges[0].Closure.Y & 3u,
                Is.EqualTo((uint)SigmaTopologyClass.Unsupported));
            Assert.That(snapshot.Labels[0], Is.Not.EqualTo(snapshot.Labels[1]));
            Assert.That(snapshot.Deferred[0], Is.Zero);
            Assert.That(snapshot.Deferred[1], Is.Zero);
        }

        [Test]
        public void ClaimedFoldMatchesFullIntrinsicS16Oracle()
        {
            using var fixture = new FrameFixture();
            long[] center = ScalarState(1);
            long[] right = ContactZeroDivisorState();
            long[] down = ScalarState(1);
            GeometryRelationCell(out long[] lower, out long[] upper);
            ClosureSnapshot snapshot = fixture.RunExactClosure(
                new ClosureTarget(0, SigmaFrameProposalKind.Novel, center,
                    0x3u, geometryLower: lower, geometryUpper: upper),
                new ClosureTarget(1, SigmaFrameProposalKind.Novel, right,
                    0x3u, geometryLower: lower, geometryUpper: upper),
                new ClosureTarget(Width, SigmaFrameProposalKind.Novel, down,
                    0x3u, geometryLower: lower, geometryUpper: upper));
            SigmaIntrinsicTopologySignature expected =
                SigmaIntrinsicTopology.EvaluateCell(
                    SigmaS16.FromArray(center), SigmaS16.FromArray(right),
                    SigmaS16.FromArray(down), DepthLeftKey, DepthRightKey,
                    false);

            Assert.That(snapshot.Edges[0].Closure.X,
                Is.EqualTo((uint)SigmaFrameClaimKind.Contact));
            Assert.That(snapshot.Edges[0].Closure.Y & 3u,
                Is.EqualTo((uint)expected.Classification));
            Assert.That((snapshot.Edges[0].Closure.Y >> 8) & 0xffu,
                Is.EqualTo((uint)expected.AnnihilatorId));
        }

        [Test]
        public void PendingEvidenceSurvivesAndReusesItsExactHandle()
        {
            using var fixture = new FrameFixture();
            ClosureSnapshot retained = fixture.RunExactClosure(
                new ClosureTarget(0, SigmaFrameProposalKind.Novel,
                    BuildCarrierPrior(), 0x1u, true, true));

            Assert.That(retained.Control.X, Is.EqualTo(1u));
            Assert.That(retained.Targets[0].Evidence.Y &
                (uint)SigmaFrameOutcomeFlags.Pending, Is.Not.Zero);
            Assert.That(retained.Targets[0].Evidence.Z,
                Is.EqualTo((uint)SigmaFrameProposalKind.Pending));
            Assert.That(retained.Targets[0].Candidate.Y, Is.EqualTo(0u));
            Assert.That(retained.Gauges[0].Identity.X, Is.Not.Zero);

            Snapshot revisit = fixture.Run(int.MaxValue, 3u);
            SigmaFrameCandidateGpu[] pending = PendingCandidates(revisit);
            Assert.That(pending, Is.Not.Empty,
                "the next coherent frame did not propose retained evidence");
            for (int index = 0; index < pending.Length; ++index)
            {
                Assert.That(pending[index].Handle.Y, Is.EqualTo(0u));
                Assert.That(pending[index].Handle.Z,
                    Is.EqualTo(retained.Gauges[0].Identity.X));
            }

            fixture.CloseCurrentFrame(3u);
            SigmaPendingGaugeGpu updated = fixture.ReadPendingGauge(0);
            Assert.That(updated.Identity.X,
                Is.EqualTo(retained.Gauges[0].Identity.X));
            Assert.That(updated.Identity.Z,
                Is.GreaterThanOrEqualTo(retained.Gauges[0].Identity.Z),
                "the retained handle was replaced instead of reused");
        }

        [Test]
        public void PendingProjectionIsInvariantAcrossOneTwoAndSevenWindows()
        {
            SigmaFrameCandidateGpu[] one = RunPendingProjectionLayout(0L,
                out int oneWindows);
            SigmaFrameCandidateGpu[] two = RunPendingProjectionLayout(
                520L * 1024L, out int twoWindows);
            SigmaFrameCandidateGpu[] seven = RunPendingProjectionLayout(
                160L * 1024L, out int sevenWindows);

            Assert.That(oneWindows, Is.EqualTo(1));
            Assert.That(twoWindows, Is.EqualTo(2));
            Assert.That(sevenWindows, Is.EqualTo(7));
            AssertCandidates(two, one);
            AssertCandidates(seven, one);
        }

        [Test]
        public void PendingEdgesCrossExecutionWindowsWithoutChangingPhysics()
        {
            ClosureSnapshot regularWhole = RunBoundaryClosure(0L, false,
                out int regularWholeWindows);
            ClosureSnapshot regularSplit = RunBoundaryClosure(160L * 1024L,
                false, out int regularSplitWindows);
            ClosureSnapshot thinWhole = RunBoundaryClosure(0L, true,
                out _);
            ClosureSnapshot thinSplit = RunBoundaryClosure(160L * 1024L,
                true, out _);
            const int boundaryEdge = 255 * 2;

            Assert.That(regularWholeWindows, Is.EqualTo(1));
            Assert.That(regularSplitWindows, Is.EqualTo(2));
            AssertUInt4(regularSplit.Edges[boundaryEdge].Closure,
                regularWhole.Edges[boundaryEdge].Closure);
            Assert.That(regularSplit.Edges[boundaryEdge].Closure.Y & 3u,
                Is.EqualTo((uint)SigmaTopologyClass.Regular));
            Assert.That(regularSplit.Labels[255],
                Is.EqualTo(regularSplit.Labels[256]));
            Assert.That(regularSplit.Deferred[255], Is.Zero);
            Assert.That(regularSplit.Deferred[256], Is.Zero);

            AssertUInt4(thinSplit.Edges[boundaryEdge].Closure,
                thinWhole.Edges[boundaryEdge].Closure);
            Assert.That(thinSplit.Edges[boundaryEdge].Closure.Y & 3u,
                Is.EqualTo((uint)SigmaTopologyClass.Unresolved));
            Assert.That(thinSplit.Deferred[255], Is.EqualTo(1u));
            Assert.That(thinSplit.Deferred[256], Is.EqualTo(1u));
        }

        private static void AssertPublished(PublicationSnapshot snapshot)
        {
            int accepted = 0;
            int changed = 0;
            int deferred = 0;
            for (int index = 0; index < snapshot.Targets.Length; ++index)
            {
                uint flags = snapshot.Targets[index].Evidence.Y;
                if ((flags & (uint)SigmaFrameOutcomeFlags.Accepted) != 0u)
                    ++accepted;
                if ((flags & ((uint)SigmaFrameOutcomeFlags.Accepted |
                        (uint)SigmaFrameOutcomeFlags.Unchanged)) ==
                    (uint)SigmaFrameOutcomeFlags.Accepted)
                    ++changed;
                if (snapshot.Deferred[index] != 0u)
                    ++deferred;
            }
            Assert.That(snapshot.Root, Is.EqualTo(1u),
                "publication root must flip exactly once to revision slot zero; " +
                $"targets={snapshot.Counters[0].X}, " +
                $"publish={snapshot.Counters[5].X}/" +
                $"{snapshot.Counters[5].Y}/" +
                $"{snapshot.Counters[5].Z}/" +
                $"{snapshot.Counters[5].W}, " +
                $"fault=0x{snapshot.Counters[6].X:x8}, " +
                $"accepted={accepted}, changed={changed}, " +
                $"deferred={deferred}, " +
                $"frame={snapshot.Owned[0].PoseSource.Z}/" +
                $"0x{snapshot.Owned[0].PoseSource.W:x8}");
            Assert.That(snapshot.Revisions[0].Identity.X, Is.EqualTo(1u));
            Assert.That(snapshot.Revisions[0].Identity.Z,
                Is.EqualTo((uint)SigmaFrameRevisionState.Published));
            Assert.That(snapshot.Revisions[0].ChangedPages.Y, Is.EqualTo(1u));
            PageMeta page = CurrentPage(snapshot, out int slot);
            Assert.That(page.Revision, Is.EqualTo(1u));
            Assert.That(snapshot.ReadoutDirty[slot], Is.EqualTo(1u));
        }

        private static void AssertUInt4(SigmaFrameUInt4Gpu actual,
            SigmaFrameUInt4Gpu expected)
        {
            Assert.That(actual.X, Is.EqualTo(expected.X));
            Assert.That(actual.Y, Is.EqualTo(expected.Y));
            Assert.That(actual.Z, Is.EqualTo(expected.Z));
            Assert.That(actual.W, Is.EqualTo(expected.W));
        }

        private static SigmaFrameCandidateGpu[] RunPendingProjectionLayout(
            long bindingLimit, out int windowCount)
        {
            using var fixture = new FrameFixture(1792, 1, bindingLimit);
            fixture.SeedPending(BuildCarrierPrior(), 17u);
            windowCount = fixture.ExecutionWindowCount;
            return PendingCandidates(fixture.Run(int.MaxValue, 2u));
        }

        private static ClosureSnapshot RunBoundaryClosure(long bindingLimit,
            bool thin, out int windowCount)
        {
            const int count = 257;
            using var fixture = new FrameFixture(count, 1, bindingLimit);
            windowCount = fixture.ExecutionWindowCount;
            long[] front = ScalarState(1);
            long[] singular = ContactZeroDivisorState();
            GeometryRelationCell(out long[] lower, out long[] upper);
            var targets = new ClosureTarget[count];
            for (int index = 0; index < targets.Length; ++index)
            {
                targets[index] = new ClosureTarget(index,
                    SigmaFrameProposalKind.Novel,
                    thin && index == targets.Length - 1 ? singular : front,
                    thin ? 0x1u : 0x3u, geometryLower: lower,
                    geometryUpper: upper);
            }
            return fixture.RunExactClosure(targets);
        }

        private static SigmaFrameCandidateGpu[] PendingCandidates(
            Snapshot snapshot) => Array.FindAll(snapshot.Candidates,
                candidate => candidate.Identity.Z ==
                    (uint)SigmaFrameProposalKind.Pending);

        private static void AssertCandidates(SigmaFrameCandidateGpu[] actual,
            SigmaFrameCandidateGpu[] expected)
        {
            Assert.That(actual, Has.Length.EqualTo(expected.Length));
            for (int index = 0; index < expected.Length; ++index)
            {
                AssertUInt4(actual[index].Identity, expected[index].Identity);
                AssertUInt4(actual[index].Handle, expected[index].Handle);
                AssertUInt4(actual[index].Coordinate,
                    expected[index].Coordinate);
            }
        }

        private static PageMeta CurrentPage(PublicationSnapshot snapshot,
            out int slot)
        {
            slot = Array.FindIndex(snapshot.Current, value => value != 0u);
            Assert.That(slot, Is.GreaterThanOrEqualTo(0));
            Assert.That(Array.FindAll(snapshot.Current, value => value != 0u),
                Has.Length.EqualTo(1));
            return snapshot.Metadata[slot];
        }

        private static UInt2[] PageState(PublicationSnapshot snapshot, int slot)
        {
            var page = new UInt2[SigmaCarrier.PageLaneCount];
            Array.Copy(snapshot.State, slot * SigmaCarrier.PageLaneCount, page,
                0, page.Length);
            return page;
        }

        private static long[] BuildCpuOracle(Snapshot snapshot, int footprint)
        {
            byte[] rows = SigmaGeneratedAlgebra.GeometryRows;
            var y = new long[Lanes];
            y[rows[0]] = SigmaNumericDomain.One;
            long maximumDepthWidth = 0L;
            for (int axis = 0; axis < 3; ++axis)
            {
                int coordinate = rows[axis + 1];
                long lower = Math.Max(Signed(snapshot.Lower[0][
                    footprint * Lanes + coordinate]), Signed(snapshot.Lower[1][
                    footprint * Lanes + coordinate]));
                long upper = Math.Min(Signed(snapshot.Upper[0][
                    footprint * Lanes + coordinate]), Signed(snapshot.Upper[1][
                    footprint * Lanes + coordinate]));
                Assert.That(lower, Is.LessThanOrEqualTo(upper));
                y[coordinate] = SigmaNumericDomain.QMidpoint(lower, upper);
                maximumDepthWidth = Math.Max(maximumDepthWidth,
                    SigmaNumericDomain.QSub(upper, lower));
            }

            long mass = SigmaDepthInverse.InformationMassForWidth(
                maximumDepthWidth, SigmaDepthInverse.DefaultPriorFloorRaw,
                SigmaDepthInverse.DefaultContactMassMinRaw);
            long[] initial = SigmaS16Operators.HadamardBT(
                SigmaS16.FromArray(y)).ToArray();
            for (int lane = 0; lane < Lanes; ++lane)
                initial[lane] = SigmaNumericDomain.QMul(
                    SigmaNumericDomain.QShiftRight(initial[lane], 4), mass);

            long[] currentY = SigmaS16Operators.HadamardB(
                SigmaS16.FromArray(initial)).ToArray();
            for (int lane = 0; lane < Lanes; ++lane)
                currentY[lane] = SigmaNumericDomain.QDiv(currentY[lane], mass);

            long priorWidth = SigmaDepthInverse.PriorHalfWidth(mass,
                SigmaDepthInverse.DefaultPriorFloorRaw,
                SigmaDepthInverse.DefaultPriorCeilingRaw);
            var lowerMeet = new long[Lanes];
            var upperMeet = new long[Lanes];
            var constrained = new bool[Lanes];
            var key0 = new uint[Lanes];
            var key1 = new uint[Lanes];
            for (int lane = 0; lane < Lanes; ++lane)
            {
                lowerMeet[lane] = SigmaNumericDomain.QSub(currentY[lane],
                    priorWidth);
                upperMeet[lane] = SigmaNumericDomain.QAdd(currentY[lane],
                    priorWidth);
            }

            for (int source = 0; source < 4; ++source)
            {
                uint key = SourceKey(snapshot.Provenance[source][footprint]);
                for (int lane = 0; lane < Lanes; ++lane)
                {
                    int address = footprint * Lanes + lane;
                    if ((snapshot.Validity[source][address] &
                        (uint)SigmaFrameCellFlags.Constrained) == 0u)
                        continue;
                    constrained[lane] = true;
                    lowerMeet[lane] = Math.Max(lowerMeet[lane],
                        Signed(snapshot.Lower[source][address]));
                    upperMeet[lane] = Math.Min(upperMeet[lane],
                        Signed(snapshot.Upper[source][address]));
                    Assert.That(lowerMeet[lane],
                        Is.LessThanOrEqualTo(upperMeet[lane]));
                    if (key == 0u || key0[lane] == key)
                        key0[lane] = key;
                    else if (key1[lane] == 0u)
                        key1[lane] = key;
                }
            }

            long maximumJointWidth = 0L;
            bool allIndependent = true;
            for (int lane = 0; lane < Lanes; ++lane)
            {
                if (!constrained[lane])
                    continue;
                maximumJointWidth = Math.Max(maximumJointWidth,
                    SigmaNumericDomain.QSub(upperMeet[lane], lowerMeet[lane]));
                allIndependent &= key0[lane] != 0u && key1[lane] != 0u;
                currentY[lane] = SigmaNumericDomain.QClamp(currentY[lane],
                    lowerMeet[lane], upperMeet[lane]);
            }
            long targetMass = mass;
            if (allIndependent)
            {
                targetMass = Math.Max(targetMass,
                    SigmaDepthInverse.InformationMassForWidth(maximumJointWidth,
                        SigmaDepthInverse.DefaultPriorFloorRaw,
                        SigmaDepthInverse.DefaultContactMassMinRaw));
            }

            long[] result = SigmaS16Operators.HadamardBT(
                SigmaS16.FromArray(currentY)).ToArray();
            for (int lane = 0; lane < Lanes; ++lane)
                result[lane] = SigmaNumericDomain.QMul(
                    SigmaNumericDomain.QShiftRight(result[lane], 4), targetMass);
            return result;
        }

        private static int ConstrainedLaneCount(uint[] validity, int footprint)
        {
            int count = 0;
            for (int lane = 0; lane < Lanes; ++lane)
            {
                if ((validity[footprint * Lanes + lane] &
                    (uint)SigmaFrameCellFlags.Constrained) != 0u)
                    ++count;
            }
            return count;
        }

        private static uint SourceKey(UInt4 provenance)
        {
            if (provenance.X == DepthLeftKey || provenance.X == DepthRightKey ||
                provenance.X == RgbLeftKey || provenance.X == RgbRightKey)
                return provenance.X;
            if (provenance.Y == DepthLeftKey || provenance.Y == DepthRightKey ||
                provenance.Y == RgbLeftKey || provenance.Y == RgbRightKey)
                return provenance.Y;
            if (provenance.Z == DepthLeftKey || provenance.Z == DepthRightKey ||
                provenance.Z == RgbLeftKey || provenance.Z == RgbRightKey)
                return provenance.Z;
            return provenance.W;
        }

        private static bool Contains(UInt4 value, uint expected) =>
            value.X == expected || value.Y == expected || value.Z == expected ||
            value.W == expected;

        private static long[] Candidate(UInt2[] states, int candidate)
        {
            var result = new long[Lanes];
            for (int lane = 0; lane < Lanes; ++lane)
                result[lane] = Signed(states[candidate * Lanes + lane]);
            return result;
        }

        private static long Signed(UInt2 value) => unchecked((long)(
            ((ulong)value.Y << 32) | value.X));

        private sealed class FrameFixture : IDisposable
        {
            private readonly int _width;
            private readonly int _height;
            private readonly int _footprints;
            private readonly Texture2D _rgbLeftSource;
            private readonly Texture2D _rgbRightSource;
            private readonly Texture2DArray _depthSource;
            private readonly GpuTextureRing _rgbLeftRing;
            private readonly GpuTextureRing _rgbRightRing;
            private readonly GpuTextureRing _depthRing;
            private readonly StereoRigFrameLease _frame;
            private readonly SigmaPredictionTargetRing _predictionRing;
            private readonly SigmaPredictionFrameLease _prediction;
            private readonly RenderTexture _metricDepth;
            private readonly RenderTexture _depthFlags;
            private readonly GraphicsBuffer _depthCalibration;
            private readonly GraphicsBuffer _rgbCalibration;
            private readonly GraphicsBuffer _poseResult;
            private readonly RigConeLutSet _coneSet;
            private readonly ConeLutLease _coneLease;
            private readonly SigmaExactBackendGate _backend;
            private readonly SigmaFrameGraph _graph;
            private readonly SigmaOwnedFrameLease _owned;
            private readonly SigmaFrameInverseInput _input;

            internal int ExecutionWindowCount =>
                _graph.Resources.ExecutionWindowCount;

            internal FrameFixture(int width = Width, int height = Height,
                long bindingLimit = 0L)
            {
                if (width <= 0 || height <= 0)
                    throw new ArgumentOutOfRangeException(nameof(width));
                _width = width;
                _height = height;
                _footprints = checked(width * height);
                _rgbLeftSource = RgbTexture(width, height, 96, 128, 192);
                _rgbRightSource = RgbTexture(width, height, 96, 128, 192);
                _depthSource = RawDepthTexture(width, height);
                _rgbLeftRing = new GpuTextureRing("M2 RGB L", 3);
                _rgbRightRing = new GpuTextureRing("M2 RGB R", 3);
                _depthRing = new GpuTextureRing("M2 Depth", 3,
                    GraphicsFormat.R32_SFloat,
                    GpuTextureCopyMode.ProjectionDepthArray);
                Assert.That(_rgbLeftRing.TryCopy(_rgbLeftSource,
                    out GpuTextureLease rgbLeft, out _), Is.True);
                Assert.That(_rgbRightRing.TryCopy(_rgbRightSource,
                    out GpuTextureLease rgbRight, out _), Is.True);
                Assert.That(_depthRing.TryCopy(_depthSource,
                    out GpuTextureLease depth, out _), Is.True);

                RigIntrinsics intrinsics = Intrinsics(width, height);
                Pose left = new(Vector3.zero, Quaternion.identity);
                Pose right = new(new Vector3(0.064f, 0f, 0f),
                    Quaternion.identity);
                RigTimestamp timestamp = new(RigClockDomain.UnityMonotonicSimulation,
                    1_000_000L, 2_000_000L, 0L);
                var rgbLeftView = new GpuImageView(RigStreamKind.Rgb,
                    RigEye.Left, rgbLeft.Texture, 0, 1L, timestamp, left,
                    intrinsics, rgbLeft.GraphicsFormat);
                var rgbRightView = new GpuImageView(RigStreamKind.Rgb,
                    RigEye.Right, rgbRight.Texture, 0, 1L, timestamp, right,
                    intrinsics, rgbRight.GraphicsFormat);
                var depthLeftView = new GpuImageView(RigStreamKind.Depth,
                    RigEye.Left, depth.Texture, 0, 1L, timestamp, left,
                    intrinsics, depth.GraphicsFormat,
                    RigDepthEncoding.ProjectionDepth01, new Vector2(0.1f, 10f));
                var depthRightView = new GpuImageView(RigStreamKind.Depth,
                    RigEye.Right, depth.Texture, 1, 1L, timestamp, right,
                    intrinsics, depth.GraphicsFormat,
                    RigDepthEncoding.ProjectionDepth01, new Vector2(0.1f, 10f));
                _frame = new StereoRigFrameLease(1L, 1u, rgbLeft,
                    rgbLeftView, rgbRight, rgbRightView, depth, depthLeftView,
                    depthRightView, new Vector2Int(width, height),
                    new Vector2(0.1f, 10f), new RigPairingHealth(0L, 0L, 0L));

                Assert.That(RigCalibration.TryCreate(_frame,
                    out RigCalibration calibration), Is.True);
                ComputeShader coneShader = Resources.Load<ComputeShader>(
                    "SigmaPrism/ConeLut");
                Assert.That(coneShader, Is.Not.Null);
                _coneSet = RigConeLutSet.Create(coneShader, calibration);
                _coneLease = _coneSet.Acquire();
                _metricDepth = ArrayRenderTexture(width, height,
                    GraphicsFormat.R32G32_SFloat);
                _depthFlags = ArrayRenderTexture(width, height,
                    GraphicsFormat.R32_UInt);
                NormalizeDepth(_frame, _coneLease, _metricDepth, _depthFlags,
                    width, height);
                _depthCalibration = Buffer(DepthCalibration(width, height),
                    Marshal.SizeOf<UInt2>());
                _rgbCalibration = Buffer(RgbCalibration(),
                    Marshal.SizeOf<UInt2>());
                _poseResult = Buffer(new UInt4[4], Marshal.SizeOf<UInt4>());

                _predictionRing = new SigmaPredictionTargetRing(3);
                Assert.That(_predictionRing.TryBegin(_frame,
                    SigmaPoseGaugeState.Identity(1u, 1u), Matrix4x4.identity,
                    out _prediction),
                    Is.True);
                ClearPrediction(_prediction);
                _prediction.CommitGpuWrite();

                _backend = SigmaExactBackendGate.Dispatch();
                _graph = bindingLimit > 0L
                    ? new SigmaFrameGraph(new Vector2Int(width, height),
                        _backend, SigmaFrameMemoryProfile.Minimum, bindingLimit)
                    : new SigmaFrameGraph(new Vector2Int(width, height),
                        _backend, SigmaFrameMemoryProfile.Minimum);
                _input = new SigmaFrameInverseInput(_prediction, _metricDepth,
                    _depthFlags, _depthCalibration, _rgbCalibration, _poseResult,
                    _coneLease, DepthLeftKey, DepthRightKey, RgbLeftKey,
                    RgbRightKey, null);
                Assert.That(_graph.TryAcquire(1u, 1u, _input, out _owned),
                    Is.True);
            }

            internal Snapshot Run(int footprintWindow, uint revision = 1u)
            {
                using var command = new CommandBuffer
                    { name = "Sigma M2 whole-frame fixture" };
                try
                {
                    _graph.RecordSourceAndResolve(command, _owned, revision,
                        _input,
                        footprintWindow);
                    Graphics.ExecuteCommandBuffer(command);
                }
                finally { command.Clear(); }
                return ReadSnapshot();
            }

            internal void CloseCurrentFrame(uint revision)
            {
                using var command = new CommandBuffer
                    { name = "Sigma R3 cross-frame pending closure" };
                try
                {
                    _graph.RecordExactClosure(command, _owned, revision, _input);
                    Graphics.ExecuteCommandBuffer(command);
                }
                finally { command.Clear(); }
            }

            internal SigmaPendingGaugeGpu ReadPendingGauge(int handle) =>
                ReadAt<SigmaPendingGaugeGpu>(
                    _graph.Resources.PendingGauges.Segment(
                        handle / _graph.Resources.FootprintsPerWindow).Buffer,
                    handle % _graph.Resources.FootprintsPerWindow);

            internal void SeedPending(long[] state, uint generation)
            {
                Assert.That(state, Has.Length.EqualTo(Lanes));
                var packedState = new UInt2[Lanes];
                var lower = new UInt2[Lanes];
                var upper = new UInt2[Lanes];
                var validity = new uint[Lanes];
                long[] transformed = SigmaS16Operators.HadamardB(
                    SigmaS16.FromArray(state)).ToArray();
                long mass = transformed[
                    SigmaGeneratedAlgebra.GeometryRows[0]];
                for (int lane = 0; lane < Lanes; ++lane)
                {
                    packedState[lane] = Packed(state[lane]);
                    long projective = SigmaNumericDomain.QDiv(
                        transformed[lane], mass);
                    lower[lane] = Packed(projective);
                    upper[lane] = Packed(projective);
                    validity[lane] = (uint)(SigmaFrameCellFlags.Constrained |
                        SigmaFrameCellFlags.Observed) |
                        (1u << SigmaGeneratedFrame.SourceMaskShift);
                }
                var gauge = new SigmaPendingGaugeGpu
                {
                    Identity = Gpu4(generation,
                        (uint)SigmaPendingGaugeState.Open, 1u, 0u),
                    Provenance = Gpu4(1u, DepthLeftKey, 0u, 1u),
                    LocalExtent = Gpu4(0u, 0u, 0u, 0u),
                };
                _graph.Resources.PendingGauges.Segment(0).Buffer.SetData(
                    new[] { gauge }, 0, 0, 1);
                _graph.Resources.PendingStates.Segment(0).Buffer.SetData(
                    packedState, 0, 0, Lanes);
                _graph.Resources.PendingLower.Segment(0).Buffer.SetData(
                    lower, 0, 0, Lanes);
                _graph.Resources.PendingUpper.Segment(0).Buffer.SetData(
                    upper, 0, 0, Lanes);
                _graph.Resources.PendingValidity.Segment(0).Buffer.SetData(
                    validity, 0, 0, Lanes);
                _graph.Resources.PendingControl.Segment(0).Buffer.SetData(
                    new[] { Gpu4(1u, unchecked((uint)_footprints), 1u, 0u) });
            }

            internal void RunSegmentedTargetFixture()
            {
                Assert.That(_graph.Resources.ExecutionWindowCount,
                    Is.GreaterThan(1));
                using (var command = new CommandBuffer
                    { name = "Sigma R1 segmented whole-frame fixture" })
                {
                    try
                    {
                        _graph.RecordSourceAndResolve(command, _owned, 1u,
                            _input);
                        Graphics.ExecuteCommandBuffer(command);
                    }
                    finally { command.Clear(); }
                }

                int targets = Math.Min(2,
                    _graph.Resources.ExecutionWindowCount);
                for (int index = 0; index < targets; ++index)
                {
                    SigmaFrameExecutionWindow window =
                        _graph.Resources.ExecutionWindow(index);
                    int localFootprint = window.FootprintCount - 1;
                    int candidateIndex = localFootprint * Proposals;
                    uint globalFootprint = unchecked((uint)(
                        window.FirstFootprint + localFootprint));
                    uint segment = unchecked((uint)(7 + index * 11));
                    uint page = unchecked((uint)(13 + index * 17));
                    uint generation = unchecked((uint)(19 + index * 23));
                    uint sample = unchecked((uint)(
                        SigmaCarrier.SamplesPerPage - 1 - index));
                    var candidate = new SigmaFrameCandidateGpu
                    {
                        Identity = Gpu4(1u, globalFootprint,
                            (uint)SigmaFrameProposalKind.Current,
                            sample | (1u << 16)),
                        Handle = Gpu4(segment, page, generation, 29u),
                        Coordinate = Gpu4(31u + (uint)index, 0u,
                            37u + (uint)index, 0u),
                    };
                    var outcome = new SigmaFrameOutcomeGpu
                    {
                        Classification = Gpu4(
                            (uint)SigmaFrameOutcomeFlags.Accepted, 0u, 0u, 0u),
                    };
                    SigmaFrameBufferSegment candidateSegment =
                        _graph.Resources.Candidates.Segment(index);
                    SigmaFrameBufferSegment outcomeSegment =
                        _graph.Resources.Outcomes.Segment(index);
                    candidateSegment.Buffer.SetData(new[] { candidate }, 0,
                        candidateIndex, 1);
                    outcomeSegment.Buffer.SetData(new[] { outcome }, 0,
                        candidateIndex, 1);

                    using var compact = new CommandBuffer
                        { name = "Sigma R1 normalized target fixture" };
                    try
                    {
                        _graph.RecordCompactWindow(compact, index, 1u);
                        Graphics.ExecuteCommandBuffer(compact);
                    }
                    finally { compact.Clear(); }

                    SigmaFrameDeltaGpu delta = ReadAt<SigmaFrameDeltaGpu>(
                        _graph.Resources.Deltas.Segment(0).Buffer,
                        checked((int)globalFootprint));
                    Assert.That(delta.Candidate.X, Is.EqualTo(segment));
                    Assert.That(delta.Candidate.Y, Is.EqualTo(page));
                    Assert.That(delta.Candidate.Z, Is.EqualTo(generation));
                    Assert.That(delta.Candidate.W, Is.EqualTo(sample));
                    Assert.That(delta.Evidence.X, Is.EqualTo(globalFootprint));
                    Assert.That(delta.Evidence.Z,
                        Is.EqualTo((uint)SigmaFrameProposalKind.Current));
                    Assert.That(delta.Evidence.W, Is.EqualTo(0u));
                    Assert.That(delta.Coordinate, Is.EqualTo(candidate.Coordinate));
                }

                for (int index = 0;
                    index < _graph.Resources.ExecutionWindowCount; ++index)
                {
                    SigmaFrameExecutionWindow window =
                        _graph.Resources.ExecutionWindow(index);
                    SigmaFrameOutcomeGpu novel = ReadAt<SigmaFrameOutcomeGpu>(
                        _graph.Resources.Outcomes.Segment(index).Buffer,
                        (window.FootprintCount - 1) * Proposals +
                            NovelProposalSlot);
                    SigmaFrameCandidateGpu proposal =
                        ReadAt<SigmaFrameCandidateGpu>(
                            _graph.Resources.Candidates.Segment(index).Buffer,
                            (window.FootprintCount - 1) * Proposals +
                                NovelProposalSlot);
                    Assert.That(proposal.Identity.Z,
                        Is.EqualTo((uint)SigmaFrameProposalKind.Novel),
                        $"window {index} proposal did not execute");
                    Assert.That(novel.Classification.X, Is.Not.EqualTo(0u),
                        $"window {index} inverse did not execute");
                }
            }

            internal void RecordProductionGraphOnly()
            {
                const int pageCapacity = 2;
                using var state = Buffer<UInt2>(pageCapacity *
                    SigmaCarrier.PageLaneCount);
                using var metadata = Buffer<PageMeta>(pageCapacity);
                using var dirty = Buffer<uint>(pageCapacity);
                using var readoutDirty = Buffer<uint>(pageCapacity);
                using var publicationRoot = Buffer<uint>(1);
                var batch = new SigmaCarrierReadBatch(0, pageCapacity, 0,
                    state, metadata, dirty, readoutDirty, publicationRoot);
                var input = new SigmaFrameInverseInput(_input.Prediction,
                    _input.MetricDepth, _input.DepthFlags,
                    _input.DepthCalibration, _input.RgbCalibration,
                    _input.PoseResult, _input.ConeLuts, _input.DepthLeftKey,
                    _input.DepthRightKey, _input.RgbLeftKey, _input.RgbRightKey,
                    new[] { batch });
                using var command = new CommandBuffer
                    { name = "Sigma 320 production dispatch contract" };
                _graph.RecordSourceAndResolve(command, _owned, 1u, input);
                _graph.RecordExactClosure(command, _owned, 1u, input);
                _graph.RecordPublication(command, _owned, 1u, input);
            }

            internal ReductionSnapshot RunTargetReduction(bool reverse)
            {
                const int firstFootprint = 3;
                const int secondFootprint = 300;
                const int proposal = 0;
                const uint segment = 9u;
                const int page = 0;
                const uint generation = 3u;
                const uint sample = 7u;
                var logical = Gpu4(5u, 0u, 7u, 0u);

                ClearSegmented<UInt2>(_graph.Resources.CandidateLower);
                ClearSegmented<UInt2>(_graph.Resources.CandidateUpper);
                ClearSegmented<uint>(_graph.Resources.CandidateValidity);
                ClearSegmented<UInt2>(_graph.Resources.CandidateStates);
                _graph.Resources.Deltas.Segment(0).Buffer.SetData(
                    new SigmaFrameDeltaGpu[_graph.Resources.TargetSortCapacity]);

                long firstLower = SigmaNumericDomain.Quantize(
                    reverse ? 0.30 : 0.20);
                long firstUpper = SigmaNumericDomain.Quantize(
                    reverse ? 0.50 : 0.40);
                long secondLower = SigmaNumericDomain.Quantize(
                    reverse ? 0.20 : 0.30);
                long secondUpper = SigmaNumericDomain.Quantize(
                    reverse ? 0.40 : 0.50);
                int constrainedCoordinate =
                    SigmaGeneratedAlgebra.GeometryRows[1];
                uint validity = (uint)(SigmaFrameCellFlags.Constrained |
                    SigmaFrameCellFlags.Observed) |
                    (3u << SigmaGeneratedFrame.SourceMaskShift);
                SetCandidateCell(firstFootprint, proposal,
                    constrainedCoordinate, firstLower, firstUpper, validity);
                SetCandidateCell(secondFootprint, proposal,
                    constrainedCoordinate, secondLower, secondUpper, validity);

                var target = new SigmaFrameDeltaGpu
                {
                    Coordinate = logical,
                    Candidate = Gpu4(segment, (uint)page, generation, sample),
                    Evidence = Gpu4(firstFootprint,
                        (uint)SigmaFrameOutcomeFlags.Accepted,
                        (uint)SigmaFrameProposalKind.Current, proposal),
                };
                var other = target;
                other.Evidence.X = secondFootprint;
                _graph.Resources.Deltas.Segment(0).Buffer.SetData(
                    new[] { target }, 0, firstFootprint, 1);
                _graph.Resources.Deltas.Segment(0).Buffer.SetData(
                    new[] { other }, 0, secondFootprint, 1);

                const int pageCapacity = 2;
                using var state = Buffer<UInt2>(checked(pageCapacity *
                    SigmaCarrier.PageLaneCount));
                using var metadata = Buffer<PageMeta>(pageCapacity);
                using var dirty = Buffer<uint>(pageCapacity);
                using var readoutDirty = Buffer<uint>(pageCapacity);
                using var publicationRoot = Buffer<uint>(1);
                var stateData = new UInt2[state.count];
                long[] prior = BuildCarrierPrior();
                for (int lane = 0; lane < Lanes; ++lane)
                    stateData[sample * Lanes + lane] = Packed(prior[lane]);
                state.SetData(stateData);
                var metadataData = new PageMeta[pageCapacity];
                metadataData[page] = new PageMeta
                {
                    XLo = logical.X,
                    XHi = logical.Y,
                    YLo = logical.Z,
                    YHi = logical.W,
                    Generation = generation,
                    Revision = 1u,
                    Flags = 1u,
                };
                metadata.SetData(metadataData);
                dirty.SetData(new uint[pageCapacity]);
                readoutDirty.SetData(new uint[pageCapacity]);
                publicationRoot.SetData(new uint[] { 1u });
                var batch = new SigmaCarrierReadBatch((int)segment,
                    pageCapacity, 0, state, metadata, dirty, readoutDirty,
                    publicationRoot);
                var input = new SigmaFrameInverseInput(_prediction, _metricDepth,
                    _depthFlags, _depthCalibration, _rgbCalibration, _poseResult,
                    _coneLease, DepthLeftKey, DepthRightKey, RgbLeftKey,
                    RgbRightKey, new[] { batch });

                using var command = new CommandBuffer
                    { name = "Sigma R2 exact target reduction fixture" };
                try
                {
                    _graph.RecordTargetReduction(command, _owned, input);
                    Graphics.ExecuteCommandBuffer(command);
                }
                finally { command.Clear(); }

                UInt4[] counters = Read<UInt4>(
                    _graph.Resources.ClosureCounters.Segment(0).Buffer, 4);
                return new ReductionSnapshot
                {
                    TargetCount = counters[0].X,
                    Target = ReadAt<SigmaFrameDeltaGpu>(
                        _graph.Resources.TargetScratch.Segment(0).Buffer, 0),
                    Lower = Read<UInt2>(_graph.Resources.ReducedLower, Lanes),
                    Upper = Read<UInt2>(_graph.Resources.ReducedUpper, Lanes),
                    Gap = Read<UInt2>(_graph.Resources.ReducedGap, Lanes),
                    Validity = Read<uint>(_graph.Resources.ReducedValidity,
                        Lanes),
                    State = Read<UInt2>(_graph.Resources.ReducedStates, Lanes),
                    FaultStage = counters[3].X,
                };
            }

            internal ClosureSnapshot RunExactClosure(
                params ClosureTarget[] targets)
            {
                if (targets == null || targets.Length == 0)
                    throw new ArgumentException("Closure targets are required.",
                        nameof(targets));
                int targetCount = targets.Length;
                Assert.That(targetCount, Is.LessThanOrEqualTo(_footprints));

                SigmaFrameBufferSegment targetScratch =
                    _graph.Resources.TargetScratch.Segment(0);
                targetScratch.Buffer.SetData(
                    new SigmaFrameDeltaGpu[targetScratch.RecordCount]);
                var mapping = new uint[_footprints];
                Array.Fill(mapping, SigmaGeneratedFrame.Invalid);
                var states = new UInt2[_footprints * Lanes];
                var lower = new UInt2[_footprints * Lanes];
                var upper = new UInt2[_footprints * Lanes];
                var validity = new uint[_footprints * Lanes];
                var records = new SigmaFrameDeltaGpu[targetCount];
                for (int ordinal = 0; ordinal < targetCount; ++ordinal)
                {
                    ClosureTarget target = targets[ordinal];
                    Assert.That(target.Footprint,
                        Is.InRange(0, _footprints - 1));
                    Assert.That(target.State, Has.Length.EqualTo(Lanes));
                    mapping[target.Footprint] = unchecked((uint)ordinal);
                    uint flags = target.Pending
                        ? (uint)SigmaFrameOutcomeFlags.Pending
                        : (uint)SigmaFrameOutcomeFlags.Accepted;
                    if (!target.Pending && !target.Changed)
                        flags |= (uint)SigmaFrameOutcomeFlags.Unchanged;
                    records[ordinal] = new SigmaFrameDeltaGpu
                    {
                        Coordinate = Gpu4(unchecked((uint)(ordinal + 1)), 0u,
                            unchecked((uint)(ordinal + 17)), 0u),
                        Candidate = Gpu4(0u, 0u, 1u,
                            unchecked((uint)ordinal)),
                        Evidence = Gpu4(unchecked((uint)target.Footprint),
                            flags, (uint)target.Kind, unchecked((uint)ordinal)),
                    };
                    uint cellFlags = target.SourceMask == 0u ? 0u :
                        (uint)(SigmaFrameCellFlags.Constrained |
                            SigmaFrameCellFlags.Observed) |
                        (target.Pending ? 0u :
                            (uint)SigmaFrameCellFlags.Accepted) |
                        (target.SourceMask <<
                            SigmaGeneratedFrame.SourceMaskShift);
                    long[] transformed = SigmaS16Operators.HadamardB(
                        SigmaS16.FromArray(target.State)).ToArray();
                    long mass = transformed[
                        SigmaGeneratedAlgebra.GeometryRows[0]];
                    for (int lane = 0; lane < Lanes; ++lane)
                    {
                        int address = ordinal * Lanes + lane;
                        states[address] = Packed(target.State[lane]);
                        long projective = target.SourceMask == 0u ? 0L :
                            SigmaNumericDomain.QDiv(transformed[lane], mass);
                        for (int axis = 0; axis < 3; ++axis)
                        {
                            if (lane != SigmaGeneratedAlgebra.GeometryRows[
                                    axis + 1])
                                continue;
                            if (target.GeometryLower != null)
                                projective = target.GeometryLower[axis];
                        }
                        lower[address] = Packed(projective);
                        long projectiveUpper = projective;
                        for (int axis = 0; axis < 3; ++axis)
                        {
                            if (lane == SigmaGeneratedAlgebra.GeometryRows[
                                    axis + 1] && target.GeometryUpper != null)
                                projectiveUpper = target.GeometryUpper[axis];
                        }
                        upper[address] = Packed(projectiveUpper);
                        validity[address] = cellFlags;
                    }
                }
                targetScratch.Buffer.SetData(records, 0, 0, records.Length);
                _graph.Resources.ResolvedIndices.Segment(0).Buffer.SetData(
                    mapping);
                SetSegmented(_graph.Resources.ReducedStates, states);
                SetSegmented(_graph.Resources.ReducedLower, lower);
                SetSegmented(_graph.Resources.ReducedUpper, upper);
                SetSegmented(_graph.Resources.ReducedValidity, validity);
                var counters = new UInt4[4];
                counters[0].X = unchecked((uint)targetCount);
                _graph.Resources.ClosureCounters.Segment(0).Buffer.SetData(
                    counters);

                using var command = new CommandBuffer
                    { name = "Sigma R3 exact pending closure fixture" };
                try
                {
                    _graph.RecordExactClosureOnly(command, _owned, 2u, _input);
                    Graphics.ExecuteCommandBuffer(command);
                }
                finally { command.Clear(); }

                return new ClosureSnapshot
                {
                    Targets = Read<SigmaFrameDeltaGpu>(
                        _graph.Resources.TargetScratch, targetCount),
                    Labels = Read<uint>(_graph.Resources.PendingLabels,
                        targetCount),
                    Links = Read<uint>(_graph.Resources.PendingLinks,
                        targetCount),
                    Deferred = Read<uint>(_graph.Resources.DeferredFlags,
                        targetCount),
                    Gauges = Read<SigmaPendingGaugeGpu>(
                        _graph.Resources.PendingGauges, targetCount),
                    Edges = Read<SigmaDirtyEdgeGpu>(
                        _graph.Resources.DirtyEdges, _footprints * 2),
                    Counters = Read<UInt4>(_graph.Resources.ClosureCounters, 4),
                    Control = Read<UInt4>(
                        _graph.Resources.PendingControl, 1)[0],
                };
            }

            private void SetCandidateCell(int footprint, int proposal,
                int coordinate, long lower, long upper, uint validity)
            {
                int windowIndex = footprint /
                    _graph.Resources.FootprintsPerWindow;
                SigmaFrameExecutionWindow window =
                    _graph.Resources.ExecutionWindow(windowIndex);
                int localFootprint = footprint - window.FirstFootprint;
                int address = checked((localFootprint * Proposals + proposal) *
                    Lanes + coordinate);
                _graph.Resources.CandidateLower.Segment(windowIndex).Buffer.SetData(
                    new[] { Packed(lower) }, 0, address, 1);
                _graph.Resources.CandidateUpper.Segment(windowIndex).Buffer.SetData(
                    new[] { Packed(upper) }, 0, address, 1);
                _graph.Resources.CandidateValidity.Segment(windowIndex).Buffer.SetData(
                    new[] { validity }, 0, address, 1);
            }

            internal PublicationSnapshot RunPublished(int footprintWindow,
                int pageCapacity)
            {
                using var state = Buffer<UInt2>(checked(pageCapacity *
                    SigmaCarrier.PageLaneCount));
                using var metadata = Buffer<PageMeta>(pageCapacity);
                using var dirty = Buffer<uint>(pageCapacity);
                using var readoutDirty = Buffer<uint>(pageCapacity);
                using var publicationRoot = Buffer<uint>(1);
                state.SetData(new UInt2[state.count]);
                metadata.SetData(new PageMeta[pageCapacity]);
                dirty.SetData(new uint[pageCapacity]);
                readoutDirty.SetData(new uint[pageCapacity]);
                publicationRoot.SetData(new uint[1]);
                var batch = new SigmaCarrierReadBatch(0, pageCapacity, 0,
                    state, metadata, dirty, readoutDirty, publicationRoot);
                var publishInput = new SigmaFrameInverseInput(
                    _input.Prediction, _input.MetricDepth, _input.DepthFlags,
                    _input.DepthCalibration, _input.RgbCalibration,
                    _input.PoseResult, _input.ConeLuts,
                    _input.DepthLeftKey, _input.DepthRightKey,
                    _input.RgbLeftKey, _input.RgbRightKey,
                    new[] { batch });
                using var command = new CommandBuffer
                    { name = "Sigma M3 atomic frame publication fixture" };
                try
                {
                    _graph.RecordSourceAndResolve(command, _owned, 1u,
                        publishInput, footprintWindow);
                    _graph.RecordExactClosure(command, _owned, 1u,
                        publishInput);
                    _graph.RecordPublication(command, _owned, 1u,
                        publishInput);
                    Graphics.ExecuteCommandBuffer(command);
                }
                finally { command.Clear(); }
                uint root = Read<uint>(publicationRoot, 1)[0];
                PageMeta[] metadataSnapshot = Read<PageMeta>(metadata,
                    pageCapacity);
                var visible = new uint[pageCapacity];
                for (int page = 0; page < pageCapacity; ++page)
                    visible[page] = VisibleAtRoot(metadataSnapshot, page, root)
                        ? 1u : 0u;
                return new PublicationSnapshot
                {
                    Root = root,
                    Counters = Read<UInt4>(
                        _graph.Resources.ClosureCounters.Segments[0].Buffer,
                        SigmaFrameResources.ClosureCounterRecords),
                    Targets = Read<SigmaFrameDeltaGpu>(
                        _graph.Resources.TargetScratch,
                        _graph.Resources.FootprintCount),
                    Deferred = Read<uint>(_graph.Resources.DeferredFlags,
                        _graph.Resources.FootprintCount),
                    Owned = Read<SigmaOwnedFrameGpu>(
                        _graph.Resources.OwnedFrames,
                        _graph.Resources.FrameCapacity),
                    Revisions = Read<SigmaFrameRevisionGpu>(
                        _graph.Resources.Revisions.Segments[0].Buffer,
                        checked((int)_graph.Resources.Revisions.RecordCapacity)),
                    Metadata = metadataSnapshot,
                    Current = visible,
                    ReadoutDirty = Read<uint>(readoutDirty, pageCapacity),
                    State = Read<UInt2>(state, state.count),
                };
            }

            private static bool VisibleAtRoot(PageMeta[] metadata, int page,
                uint root)
            {
                PageMeta value = metadata[page];
                if (root == 0u || (value.Flags & 1u) == 0u ||
                    value.Revision == 0u || value.Revision > root)
                    return false;
                int sibling = page ^ 1;
                if ((uint)sibling >= (uint)metadata.Length)
                    return true;
                PageMeta other = metadata[sibling];
                bool same = value.XLo == other.XLo && value.XHi == other.XHi &&
                    value.YLo == other.YLo && value.YHi == other.YHi;
                if ((other.Flags & 1u) == 0u || other.Revision == 0u ||
                    other.Revision > root || !same)
                    return true;
                return value.Revision > other.Revision ||
                    (value.Revision == other.Revision &&
                        value.Generation >= other.Generation);
            }

            public void Dispose()
            {
                _owned?.Dispose();
                _graph?.Dispose();
                _backend?.Dispose();
                _prediction?.Dispose();
                _predictionRing?.Dispose();
                _coneLease?.Dispose();
                _coneSet?.Retire();
                _poseResult?.Dispose();
                _rgbCalibration?.Dispose();
                _depthCalibration?.Dispose();
                Destroy(_depthFlags);
                Destroy(_metricDepth);
                _frame?.Dispose();
                _depthRing?.Dispose();
                _rgbRightRing?.Dispose();
                _rgbLeftRing?.Dispose();
                Destroy(_depthSource);
                Destroy(_rgbRightSource);
                Destroy(_rgbLeftSource);
            }

            private Snapshot ReadSnapshot()
            {
                var result = new Snapshot();
                SigmaFrameSourceStorage sources = _owned.Sources;
                for (int source = 0; source < 4; ++source)
                {
                    SigmaFrameSource kind = (SigmaFrameSource)source;
                    result.Lower[source] = Read<UInt2>(
                        sources.Lower(kind), _footprints * Lanes);
                    result.Upper[source] = Read<UInt2>(
                        sources.Upper(kind), _footprints * Lanes);
                    result.Validity[source] = Read<uint>(
                        sources.Validity(kind), _footprints * Lanes);
                    result.Provenance[source] = Read<UInt4>(
                        sources.Provenance(kind), _footprints);
                }
                result.Outcomes = Read<SigmaFrameOutcomeGpu>(
                    _graph.Resources.Outcomes, _footprints * Proposals);
                result.Candidates = Read<SigmaFrameCandidateGpu>(
                    _graph.Resources.Candidates, _footprints * Proposals);
                result.CandidateStates = Read<UInt2>(
                    _graph.Resources.CandidateStates,
                    _footprints * Proposals * Lanes);
                return result;
            }
        }

        private static RigIntrinsics Intrinsics(int width, int height) => new(
            new Vector2(width, height), new Vector2(width * 0.5f, height * 0.5f),
            new Vector2Int(width, height), new Vector2Int(width, height),
            Pose.identity, new Vector4(-0.5f, 0.5f, 0.5f, -0.5f), 0x1234UL);

        private static Texture2D RgbTexture(int width, int height,
            byte r, byte g, byte b)
        {
            var texture = new Texture2D(width, height,
                GraphicsFormat.R8G8B8A8_UNorm, TextureCreationFlags.None);
            var pixels = new Color32[checked(width * height)];
            Array.Fill(pixels, new Color32(r, g, b, 255));
            texture.SetPixelData(pixels, 0);
            texture.Apply(false, false);
            return texture;
        }

        private static Texture2DArray RawDepthTexture(int width, int height)
        {
            var texture = new Texture2DArray(width, height, 2,
                GraphicsFormat.R32_SFloat, TextureCreationFlags.None);
            var values = new float[checked(width * height)];
            Array.Fill(values, 10f * (2f - 0.1f) / (2f * (10f - 0.1f)));
            texture.SetPixelData(values, 0, 0);
            texture.SetPixelData(values, 0, 1);
            texture.Apply(false, false);
            return texture;
        }

        private static void NormalizeDepth(StereoRigFrameLease frame,
            ConeLutLease luts, RenderTexture metricDepth,
            RenderTexture depthFlags, int width, int height)
        {
            ComputeShader shader = Resources.Load<ComputeShader>(
                "SigmaPrism/DepthNormalize");
            Assert.That(shader, Is.Not.Null);
            int kernel = shader.FindKernel("NormalizeStereoDepth");
            shader.SetInts("_Resolution", width, height);
            shader.SetVector("_NearFar", new Vector4(0.1f, 10f, 0f, 0f));
            shader.SetTexture(kernel, "_RawDepth", frame.DepthLeft.Texture);
            shader.SetTexture(kernel, "_DepthRayCenterLeft",
                luts.DepthLeft.CenterRaySolidAngle);
            shader.SetTexture(kernel, "_DepthRayCenterRight",
                luts.DepthRight.CenterRaySolidAngle);
            shader.SetTexture(kernel, "_MetricDepth", metricDepth);
            shader.SetTexture(kernel, "_DepthFlags", depthFlags);
            shader.Dispatch(kernel, 1, 1, 2);
        }

        private static RenderTexture ArrayRenderTexture(int width, int height,
            GraphicsFormat format)
        {
            var descriptor = new RenderTextureDescriptor(width, height)
            {
                graphicsFormat = format,
                depthBufferBits = 0,
                msaaSamples = 1,
                dimension = TextureDimension.Tex2DArray,
                volumeDepth = 2,
                enableRandomWrite = true,
                useMipMap = false,
                autoGenerateMips = false,
            };
            var texture = new RenderTexture(descriptor)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            Assert.That(texture.Create(), Is.True);
            return texture;
        }

        private static void ClearPrediction(SigmaPredictionFrameLease prediction)
        {
            using var command = new CommandBuffer { name = "Clear M2 prediction" };
            try
            {
                var mrt = new RenderTargetIdentifier[]
                {
                    prediction.DepthSupport,
                    prediction.CarrierPage,
                    prediction.CarrierUvNormal,
                    prediction.StateKey,
                };
                for (int eye = 0; eye < 2; ++eye)
                {
                    command.SetRenderTarget(mrt, prediction.HardwareDepth, 0,
                        CubemapFace.Unknown, eye);
                    command.ClearRenderTarget(true, true, Color.clear, 1f);
                }
                Graphics.ExecuteCommandBuffer(command);
            }
            finally { command.Clear(); }
        }

        private static UInt2[] DepthCalibration(int width, int height)
        {
            var result = new UInt2[DepthCalibrationStride * 2];
            for (int eye = 0; eye < 2; ++eye)
            {
                int at = eye * DepthCalibrationStride;
                Set(result, at + 0, width);
                Set(result, at + 1, height);
                Set(result, at + 2, width * 0.5f);
                Set(result, at + 3, height * 0.5f);
                Set(result, at + 4, 1f);
                Set(result, at + 8, 1f);
                Set(result, at + 12, 1f);
                Set(result, at + 13, eye == 0 ? 0f : 0.064f);
                Set(result, at + 16, 0.1f);
                Set(result, at + 17, 10f);
                Set(result, at + 18, 0.001f);
                float[] thresholds = { 0.5f, 1f, 2f, 3f, 5f, 32767f };
                float[] widths = { 0.003f, 0.0045f, 0.007f, 0.012f, 0.025f, 0.05f };
                for (int index = 0; index < 6; ++index)
                {
                    Set(result, at + 19 + index, thresholds[index]);
                    Set(result, at + 25 + index, widths[index]);
                }
                Set(result, at + 31, 0.001f);
                Set(result, at + 32, 0.05f);
                Set(result, at + 33, 1f / 64f);
            }
            return result;
        }

        private static UInt2[] RgbCalibration()
        {
            var result = new UInt2[RgbCalibrationStride * 2];
            for (int eye = 0; eye < 2; ++eye)
            {
                int at = eye * RgbCalibrationStride;
                Set(result, at + 0, eye == 0 ? 0f : 0.064f);
                Set(result, at + 3, 2f / 255f);
                Set(result, at + 4, 1f / 64f);
                Set(result, at + 5, 0.0005f);
                Set(result, at + 6, 1f / 255f);
            }
            return result;
        }

        private static void Set(UInt2[] target, int index, float value)
        {
            ulong raw = unchecked((ulong)SigmaNumericDomain.Quantize(value));
            target[index] = new UInt2
            {
                X = unchecked((uint)raw),
                Y = unchecked((uint)(raw >> 32)),
            };
        }

        private static long[] BuildCarrierPrior()
        {
            var projective = new long[Lanes];
            byte[] rows = SigmaGeneratedAlgebra.GeometryRows;
            projective[rows[0]] = SigmaNumericDomain.One;
            projective[rows[3]] = SigmaNumericDomain.FromInteger(2);
            long[] state = SigmaS16Operators.HadamardBT(
                SigmaS16.FromArray(projective)).ToArray();
            for (int lane = 0; lane < state.Length; ++lane)
                state[lane] = SigmaNumericDomain.QShiftRight(state[lane], 4);
            return state;
        }

        private static long[] ScalarState(int value) =>
            SigmaS16.Basis(0, SigmaNumericDomain.FromInteger(value)).ToArray();

        private static long[] ContactZeroDivisorState()
        {
            for (int index = 0; index < 1344; ++index)
            {
                SigmaS16 state = SigmaS16Operators.GetZeroDivisorEntry(index)
                    .Witness.ToS16();
                if (SigmaGeometryReadout.TryRead(state, out _))
                    return state.ToArray();
            }
            throw new InvalidOperationException(
                "Generated catalog has no contact-readable zero divisor.");
        }

        private static void GeometryRelationCell(out long[] lower,
            out long[] upper)
        {
            lower = new long[3];
            upper = new long[3];
            long lo = SigmaNumericDomain.FromInteger(-4);
            long hi = SigmaNumericDomain.FromInteger(4);
            Array.Fill(lower, lo);
            Array.Fill(upper, hi);
        }

        private static UInt2 Packed(long value)
        {
            ulong raw = unchecked((ulong)value);
            return new UInt2
            {
                X = unchecked((uint)raw),
                Y = unchecked((uint)(raw >> 32)),
            };
        }

        private static GraphicsBuffer Buffer<T>(T[] values, int stride)
            where T : struct
        {
            var buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                values.Length, stride);
            buffer.SetData(values);
            return buffer;
        }

        private static GraphicsBuffer Buffer<T>(int count) where T : struct =>
            new(GraphicsBuffer.Target.Structured, count, Marshal.SizeOf<T>());

        private static T[] Read<T>(GraphicsBuffer buffer, int count)
            where T : struct
        {
            var values = new T[count];
            buffer.GetData(values, 0, 0, count);
            return values;
        }

        private static T[] Read<T>(SigmaFrameSegmentedBuffer buffer, int count)
            where T : struct
        {
            var values = new T[count];
            int destination = 0;
            for (int index = 0; index < buffer.Segments.Count; ++index)
            {
                SigmaFrameBufferSegment segment = buffer.Segment(index);
                int copy = Math.Min(segment.RecordCount, count - destination);
                if (copy <= 0)
                    break;
                segment.Buffer.GetData(values, destination, 0, copy);
                destination += copy;
            }
            Assert.That(destination, Is.EqualTo(count));
            return values;
        }

        private static void ClearSegmented<T>(SigmaFrameSegmentedBuffer buffer)
            where T : struct
        {
            for (int index = 0; index < buffer.Segments.Count; ++index)
            {
                SigmaFrameBufferSegment segment = buffer.Segment(index);
                segment.Buffer.SetData(new T[segment.RecordCount]);
            }
        }

        private static void SetSegmented<T>(SigmaFrameSegmentedBuffer buffer,
            T[] values) where T : struct
        {
            int source = 0;
            for (int index = 0;
                index < buffer.Segments.Count && source < values.Length;
                ++index)
            {
                SigmaFrameBufferSegment segment = buffer.Segment(index);
                int copy = Math.Min(segment.RecordCount,
                    values.Length - source);
                segment.Buffer.SetData(values, source, 0, copy);
                source += copy;
            }
            Assert.That(source, Is.EqualTo(values.Length));
        }

        private static T ReadAt<T>(GraphicsBuffer buffer, int index)
            where T : struct
        {
            var value = new T[1];
            buffer.GetData(value, 0, index, 1);
            return value[0];
        }

        private static SigmaFrameUInt4Gpu Gpu4(uint x, uint y, uint z,
            uint w) => new() { X = x, Y = y, Z = z, W = w };

        private static void Destroy(UnityEngine.Object value)
        {
            if (value != null)
                UnityEngine.Object.DestroyImmediate(value);
        }

        private static void AssertMatrix(Matrix4x4 actual,
            Matrix4x4 expected, float tolerance)
        {
            for (int row = 0; row < 4; ++row)
            for (int column = 0; column < 4; ++column)
                Assert.That(actual[row, column],
                    Is.EqualTo(expected[row, column]).Within(tolerance),
                    $"matrix[{row},{column}]");
        }

        private static Pose ComposePose(Matrix4x4 worldFromRoom,
            Vector3 roomPosition, Quaternion roomRotation) => new(
            worldFromRoom.MultiplyPoint3x4(roomPosition),
            worldFromRoom.rotation * roomRotation);
    }
}
