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
            internal SigmaFrameRevisionGpu[] Revisions;
            internal PageMeta[] Metadata;
            internal uint[] Current;
            internal uint[] ReadoutDirty;
            internal UInt2[] State;
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

        private static void AssertPublished(PublicationSnapshot snapshot)
        {
            Assert.That(snapshot.Root, Is.EqualTo(1u),
                "publication root must flip exactly once to revision slot zero");
            Assert.That(snapshot.Revisions[0].Identity.X, Is.EqualTo(1u));
            Assert.That(snapshot.Revisions[0].Identity.Z,
                Is.EqualTo((uint)SigmaFrameRevisionState.Published));
            Assert.That(snapshot.Revisions[0].ChangedPages.Y, Is.EqualTo(1u));
            PageMeta page = CurrentPage(snapshot, out int slot);
            Assert.That(page.Revision, Is.EqualTo(1u));
            Assert.That(snapshot.ReadoutDirty[slot], Is.EqualTo(1u));
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

            internal FrameFixture()
            {
                _rgbLeftSource = RgbTexture(96, 128, 192);
                _rgbRightSource = RgbTexture(96, 128, 192);
                _depthSource = RawDepthTexture();
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

                RigIntrinsics intrinsics = Intrinsics();
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
                    depthRightView, new Vector2Int(Width, Height),
                    new Vector2(0.1f, 10f), new RigPairingHealth(0L, 0L, 0L));

                Assert.That(RigCalibration.TryCreate(_frame,
                    out RigCalibration calibration), Is.True);
                ComputeShader coneShader = Resources.Load<ComputeShader>(
                    "SigmaPrism/ConeLut");
                Assert.That(coneShader, Is.Not.Null);
                _coneSet = RigConeLutSet.Create(coneShader, calibration);
                _coneLease = _coneSet.Acquire();
                _metricDepth = ArrayRenderTexture(
                    GraphicsFormat.R32G32_SFloat);
                _depthFlags = ArrayRenderTexture(GraphicsFormat.R32_UInt);
                NormalizeDepth(_frame, _coneLease, _metricDepth, _depthFlags);
                _depthCalibration = Buffer(DepthCalibration(),
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
                _graph = new SigmaFrameGraph(new Vector2Int(Width, Height),
                    _backend, SigmaFrameMemoryProfile.Minimum);
                _input = new SigmaFrameInverseInput(_prediction, _metricDepth,
                    _depthFlags, _depthCalibration, _rgbCalibration, _poseResult,
                    _coneLease, DepthLeftKey, DepthRightKey, RgbLeftKey,
                    RgbRightKey, null);
                Assert.That(_graph.TryAcquire(1u, 1u, _input, out _owned),
                    Is.True);
            }

            internal Snapshot Run(int footprintWindow)
            {
                using var command = new CommandBuffer
                    { name = "Sigma M2 whole-frame fixture" };
                try
                {
                    _graph.RecordSourceAndResolve(command, _owned, 1u, _input,
                        footprintWindow);
                    Graphics.ExecuteCommandBuffer(command);
                }
                finally { command.Clear(); }
                return ReadSnapshot();
            }

            internal PublicationSnapshot RunPublished(int footprintWindow,
                int pageCapacity)
            {
                using var state = Buffer<UInt2>(checked(pageCapacity *
                    SigmaCarrier.PageLaneCount));
                using var metadata = Buffer<PageMeta>(pageCapacity);
                using var dirty = Buffer<uint>(pageCapacity);
                using var current = Buffer<uint>(pageCapacity);
                using var readoutDirty = Buffer<uint>(pageCapacity);
                state.SetData(new UInt2[state.count]);
                metadata.SetData(new PageMeta[pageCapacity]);
                dirty.SetData(new uint[pageCapacity]);
                current.SetData(new uint[pageCapacity]);
                readoutDirty.SetData(new uint[pageCapacity]);
                var target = new SigmaFramePublicationTarget(0, pageCapacity,
                    state, metadata, dirty, current, readoutDirty);
                using var command = new CommandBuffer
                    { name = "Sigma M3 atomic frame publication fixture" };
                try
                {
                    _graph.RecordSourceAndResolve(command, _owned, 1u, _input,
                        footprintWindow);
                    _graph.RecordClosureAndPublish(command, _owned, 1u, target);
                    Graphics.ExecuteCommandBuffer(command);
                }
                finally { command.Clear(); }
                return new PublicationSnapshot
                {
                    Root = Read<uint>(
                        _graph.Resources.RevisionRoot.Segments[0].Buffer, 1)[0],
                    Revisions = Read<SigmaFrameRevisionGpu>(
                        _graph.Resources.Revisions.Segments[0].Buffer,
                        checked((int)_graph.Resources.Revisions.RecordCapacity)),
                    Metadata = Read<PageMeta>(metadata, pageCapacity),
                    Current = Read<uint>(current, pageCapacity),
                    ReadoutDirty = Read<uint>(readoutDirty, pageCapacity),
                    State = Read<UInt2>(state, state.count),
                };
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
                        sources.Lower(kind).Segments[0].Buffer, Footprints * Lanes);
                    result.Upper[source] = Read<UInt2>(
                        sources.Upper(kind).Segments[0].Buffer, Footprints * Lanes);
                    result.Validity[source] = Read<uint>(
                        sources.Validity(kind).Segments[0].Buffer,
                        Footprints * Lanes);
                    result.Provenance[source] = Read<UInt4>(
                        sources.Provenance(kind).Segments[0].Buffer, Footprints);
                }
                result.Outcomes = Read<SigmaFrameOutcomeGpu>(
                    _graph.Resources.Outcomes.Segments[0].Buffer,
                    Footprints * Proposals);
                result.Candidates = Read<SigmaFrameCandidateGpu>(
                    _graph.Resources.Candidates.Segments[0].Buffer,
                    Footprints * Proposals);
                result.CandidateStates = Read<UInt2>(
                    _graph.Resources.CandidateStates.Segments[0].Buffer,
                    Footprints * Proposals * Lanes);
                return result;
            }
        }

        private static RigIntrinsics Intrinsics() => new(
            new Vector2(8f, 8f), new Vector2(4f, 4f),
            new Vector2Int(Width, Height), new Vector2Int(Width, Height),
            Pose.identity, new Vector4(-0.5f, 0.5f, 0.5f, -0.5f), 0x1234UL);

        private static Texture2D RgbTexture(byte r, byte g, byte b)
        {
            var texture = new Texture2D(Width, Height,
                GraphicsFormat.R8G8B8A8_UNorm, TextureCreationFlags.None);
            var pixels = new Color32[Footprints];
            Array.Fill(pixels, new Color32(r, g, b, 255));
            texture.SetPixelData(pixels, 0);
            texture.Apply(false, false);
            return texture;
        }

        private static Texture2DArray RawDepthTexture()
        {
            var texture = new Texture2DArray(Width, Height, 2,
                GraphicsFormat.R32_SFloat, TextureCreationFlags.None);
            var values = new float[Footprints];
            Array.Fill(values, 10f * (2f - 0.1f) / (2f * (10f - 0.1f)));
            texture.SetPixelData(values, 0, 0);
            texture.SetPixelData(values, 0, 1);
            texture.Apply(false, false);
            return texture;
        }

        private static void NormalizeDepth(StereoRigFrameLease frame,
            ConeLutLease luts, RenderTexture metricDepth,
            RenderTexture depthFlags)
        {
            ComputeShader shader = Resources.Load<ComputeShader>(
                "SigmaPrism/DepthNormalize");
            Assert.That(shader, Is.Not.Null);
            int kernel = shader.FindKernel("NormalizeStereoDepth");
            shader.SetInts("_Resolution", Width, Height);
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

        private static RenderTexture ArrayRenderTexture(GraphicsFormat format)
        {
            var descriptor = new RenderTextureDescriptor(Width, Height)
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

        private static UInt2[] DepthCalibration()
        {
            var result = new UInt2[DepthCalibrationStride * 2];
            for (int eye = 0; eye < 2; ++eye)
            {
                int at = eye * DepthCalibrationStride;
                Set(result, at + 0, 8f);
                Set(result, at + 1, 8f);
                Set(result, at + 2, 4f);
                Set(result, at + 3, 4f);
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
