using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using Genesis.RoomScan.SigmaPrism;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class SigmaNativeOracleTests
    {
        private const string GaugeFingerprint =
            "1dd52ddd15b88f73cb6ad3e354ca6244bcbe5ff40efe2682f211edf794e08ac4";
        private const string TransferFingerprint =
            "b2769801f365ae83969640f86fb9f55ab43861ece88feacec126b73f5ea55511";

        [StructLayout(LayoutKind.Sequential)]
        private struct UInt2
        {
            public uint X;
            public uint Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct UInt4 : IEquatable<UInt4>
        {
            public uint X;
            public uint Y;
            public uint Z;
            public uint W;
            public bool Equals(UInt4 other) => X == other.X && Y == other.Y &&
                Z == other.Z && W == other.W;
            public override bool Equals(object obj) =>
                obj is UInt4 other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(X, Y, Z, W);
        }

        private sealed class GpuShadow
        {
            internal GpuShadow(uint reducer, ulong[] firstSupports,
                ulong[] behindSupports, SigmaQ48Interval order,
                SigmaQ48Interval[] optical, uint activeContributions,
                ulong[] relationSupports,
                SigmaMerkabaRelationClass[] relationClasses,
                bool requiresColdContinuation = false)
            {
                Reducer = reducer;
                FirstSupports = firstSupports;
                BehindSupports = behindSupports;
                Order = order;
                Optical = optical;
                ActiveContributions = activeContributions;
                RelationSupports = relationSupports;
                RelationClasses = relationClasses;
                RequiresColdContinuation = requiresColdContinuation;
            }

            internal uint Reducer { get; }
            internal ulong[] FirstSupports { get; }
            internal ulong[] BehindSupports { get; }
            internal SigmaQ48Interval Order { get; }
            internal SigmaQ48Interval[] Optical { get; }
            internal uint ActiveContributions { get; }
            internal ulong[] RelationSupports { get; }
            internal SigmaMerkabaRelationClass[] RelationClasses { get; }
            internal bool RequiresColdContinuation { get; }
        }

        private sealed class GpuFreshAdmission
        {
            internal GpuFreshAdmission(uint status, SigmaS16 state,
                SigmaMerkabaRelationClass relation, long supportU, long supportV,
                uint branchCount, uint coldReason)
            {
                Status = status;
                State = state;
                Relation = relation;
                SupportU = supportU;
                SupportV = supportV;
                BranchCount = branchCount;
                ColdReason = coldReason;
            }

            internal uint Status { get; }
            internal SigmaS16 State { get; }
            internal SigmaMerkabaRelationClass Relation { get; }
            internal long SupportU { get; }
            internal long SupportV { get; }
            internal uint BranchCount { get; }
            internal uint ColdReason { get; }
            internal bool Admitted => Status == 1u;
        }

        [Test]
        public void WholeQueryReductionOwnsFirstHitDisjunctionAndDefault()
        {
            SigmaNativeOracleQuery query = LeftQuery(Point(0), Point(0),
                orderEvidence: true, opticalEvidence: false);
            SigmaNativeOracleCell near = Cell(0, State(14, 1), 0, 0);
            SigmaNativeOracleCell far = Cell(1, State(14, 2), 1, 0);
            SigmaNativeSceneShadow one = SigmaMerkabaSemanticOracle.EvaluateAndReduce(
                new[] { near }, new[] { 0 }, query);
            CollectionAssert.AreEqual(new[] { 0 }, one.FirstSupports);
            Assert.That(one.BehindSupports, Is.Empty);

            SigmaNativeSceneShadow layered = SigmaMerkabaSemanticOracle
                .EvaluateAndReduce(new[] { near, far }, new[] { 0, 1 }, query);
            CollectionAssert.AreEqual(new[] { 0 }, layered.FirstSupports);
            CollectionAssert.AreEqual(new[] { 1 }, layered.BehindSupports);
            Assert.That(layered.Order, Is.EqualTo(Point(Raw(3, 2))));

            SigmaNativeOracleCell leftAmbiguous = Cell(2, State(4, 1), 2, 0);
            SigmaNativeOracleCell rightAmbiguous = Cell(3, State(8, 1), 3, 0);
            SigmaNativeSceneShadow ambiguous = SigmaMerkabaSemanticOracle
                .EvaluateAndReduce(new[] { leftAmbiguous, rightAmbiguous },
                    new[] { 0, 1 }, query);
            CollectionAssert.AreEqual(new[] { 2, 3 }, ambiguous.FirstSupports,
                "Equal first strata remain a support union.");

            SigmaNativeSceneShadow revealed = SigmaMerkabaSemanticOracle
                .EvaluateAndReduce(new[] { Cell(0, SigmaS16.Zero, 0, 0), far },
                    new[] { 0, 1 }, query);
            CollectionAssert.AreEqual(new[] { 1 }, revealed.FirstSupports,
                "Physical supported-to-ZEmpty correction may reveal a farther hit.");

            SigmaNativeSceneShadow allDefault = SigmaMerkabaSemanticOracle
                .EvaluateAndReduce(new[] { Cell(0, SigmaS16.Zero, 0, 0) },
                    new[] { 0 }, query);
            Assert.That(allDefault.IsDefault, Is.True);
            foreach (SigmaDefaultBackingKind backing in Enum.GetValues(
                         typeof(SigmaDefaultBackingKind)))
            {
                SigmaNativeSceneShadow decoded = SigmaMerkabaSemanticOracle
                    .EvaluateAndReduce(new[] { Cell(0,
                        SigmaGeneratedMerkabaProgram.DecodeDefaultRepresentation(backing),
                        0, 0) }, new[] { 0 }, query);
                Assert.That(decoded, Is.EqualTo(allDefault));
            }
        }

        [Test]
        public void ReverseKeepsUnionAndResolvesOnlyUniqueOrCommonDelta()
        {
            SigmaS16 a = State(4, 1);
            SigmaS16 b = State(8, 1);
            SigmaNativePreimageCandidate candidateA = Candidate(0, 2, a,
                new SigmaNativeDeltaWitness(0, 0, a));
            SigmaNativePreimageCandidate candidateB = Candidate(1, 3, b,
                new SigmaNativeDeltaWitness(1, 0, b));
            SigmaNativeOracleQuery left = LeftQuery(Point(Raw(1, 2)),
                Point(Raw(1, 2)), true, true);
            SigmaNativeContractResult leftResult = SigmaMerkabaSemanticOracle
                .ContractNativeQuery(left, new[] { candidateA, candidateB });
            Assert.That(leftResult.Resolution,
                Is.EqualTo(SigmaNativeOracleResolution.Ambiguous));
            Assert.That(leftResult.Branches.Select(value => value.CandidateOrdinal),
                Is.EquivalentTo(new[] { 0, 1 }));

            SigmaNativeOracleQuery right = RightQuery(Point(Raw(1, 2)),
                Point(Raw(-3, 2)), true, true);
            SigmaNativeContractResult joint = SigmaMerkabaSemanticOracle
                .ContractJoint(new[] { left, right },
                    new[] { candidateA, candidateB });
            Assert.That(joint.Resolution,
                Is.EqualTo(SigmaNativeOracleResolution.Unique));
            Assert.That(joint.Branches.Single().CandidateOrdinal, Is.EqualTo(1),
                "Right-eye-only information must eliminate A, not look up left again.");
            Assert.That(joint.Branches.Single().Actions, Has.Length.EqualTo(2),
                "Joint closure must retain both eye-direction witnesses.");

            SigmaNativeContractResult onlyA = SigmaMerkabaSemanticOracle
                .ContractNativeQuery(RightQuery(Point(Raw(-3, 2)),
                    Point(Raw(1, 2)), true, true),
                    new[] { candidateA, candidateB });
            Assert.That(onlyA.Branches.Single().CandidateOrdinal, Is.Zero);

            SigmaNativeDeltaWitness common = new(0, 0, a);
            SigmaNativeContractResult commonResult = SigmaMerkabaSemanticOracle
                .ContractNativeQuery(left, new[]
                {
                    Candidate(4, 4, a, common),
                    Candidate(5, 5, a, common),
                });
            Assert.That(commonResult.Resolution,
                Is.EqualTo(SigmaNativeOracleResolution.CommonDelta));
            Assert.That(commonResult.HasCanonicalAnswer, Is.True);

            SigmaGaugeCell baseCell = new(17, -9, 0, "fresh-full-s16");
            SigmaGaugeCell translated = new(4, 2, 0, "fresh-full-s16");
            Assert.That(SigmaGeneratedMerkabaProgram.TryNormalizeFreshSupport(
                new IEnumerable<SigmaGaugeCell>[]
                {
                    new[] { baseCell }, new[] { translated },
                }, out string normalized), Is.True);
            Assert.That(normalized, Is.EqualTo("0:0:0:fresh-full-s16"));
            Assert.That(SigmaGeneratedMerkabaProgram.TryNormalizeFreshSupport(
                new IEnumerable<SigmaGaugeCell>[]
                {
                    new[] { baseCell },
                    new[] { translated, new SigmaGaugeCell(8, 8, 0,
                        "fresh-full-s16") },
                }, out _), Is.False, "Non-equivalent fresh placement stays unresolved.");
        }

        [Test]
        public void FreshBaseAdmissionStartsAtCoherentObservationAndMatchesVulkan()
        {
            long[] target =
            {
                SigmaNumericDomain.One,
                -SigmaNumericDomain.One,
                SigmaNumericDomain.Half,
                -SigmaNumericDomain.Half,
            };
            long[] otherTarget =
            {
                SigmaNumericDomain.Half,
                SigmaNumericDomain.Half,
                -SigmaNumericDomain.Half,
                -SigmaNumericDomain.Half,
            };
            SigmaNativeFreshObservationBranch a = FreshObservation(target, 41UL,
                provenanceOrdinal: 1);
            SigmaNativeFreshObservationBranch b = FreshObservation(target, 41UL,
                provenanceOrdinal: 2);
            SigmaNativeFreshObservationBranch other = FreshObservation(otherTarget,
                41UL, provenanceOrdinal: 3);

            Assert.That(a.TryAssembleQueries(out SigmaNativeOracleQuery rawLeft,
                out SigmaNativeOracleQuery rawRight), Is.True);
            CollectionAssert.AreNotEqual(rawLeft.OrderRow, rawRight.OrderRow,
                "Independent calibrated eye rays must generate distinct routing.");
            Assert.That(a.InstrumentLeft.Footprint.SolidAngle, Is.GreaterThan(0L));
            Assert.That(a.InstrumentRight.Footprint.SolidAngle, Is.GreaterThan(0L));
            Assert.That(a.InstrumentLeft.MetricDirectOrder.IsEmpty, Is.False);
            Assert.That(a.InstrumentRight.MetricDirectOrder.IsEmpty, Is.False);

            Assert.That(SigmaMerkabaSemanticOracle.TryResolveFreshBaseAdmission(
                new[] { a }, out SigmaFreshBaseAdmission cpuUnique), Is.True);
            Assert.That(cpuUnique.State.IsZero, Is.False,
                "An all-ZEmpty prior is implicit; no proposed state enters N2.");
            Assert.That(cpuUnique.Support.Count, Is.EqualTo(1));
            Assert.That(cpuUnique.Support[0].U, Is.Zero);
            Assert.That(cpuUnique.Support[0].V, Is.Zero);
            Assert.That(cpuUnique.Support[0].Level, Is.Zero);
            AssertFreshGpuMatchesCpu(RunGpuFreshAdmission(new[] { a }), cpuUnique,
                expectedBranchCount: 1);

            Assert.That(SigmaMerkabaSemanticOracle.TryResolveFreshBaseAdmission(
                new[] { a, b }, out SigmaFreshBaseAdmission cpuCommon), Is.True);
            GpuFreshAdmission gpuAB = RunGpuFreshAdmission(new[] { a, b });
            GpuFreshAdmission gpuBA = RunGpuFreshAdmission(new[] { b, a });
            AssertFreshGpuMatchesCpu(gpuAB, cpuCommon, expectedBranchCount: 2);
            AssertFreshGpuMatchesCpu(gpuBA, cpuCommon, expectedBranchCount: 2);
            Assert.That(gpuBA.State, Is.EqualTo(gpuAB.State),
                "Reverse-branch/allocation order may not choose fresh physics.");

            Assert.That(SigmaMerkabaSemanticOracle.TryResolveFreshBaseAdmission(
                new[] { a, other }, out _), Is.False);
            Assert.That(RunGpuFreshAdmission(new[] { a, other }).Admitted, Is.False,
                "Non-equivalent surviving preimages remain unresolved.");

            SigmaNativeFreshObservationBranch rightDiscriminates = FreshObservation(
                target, 42UL, provenanceOrdinal: 4, leftBroad: true);
            Assert.That(SigmaMerkabaSemanticOracle.TryResolveFreshBaseAdmission(
                new[] { rightDiscriminates }, out SigmaFreshBaseAdmission cpuRight),
                Is.True);
            AssertFreshGpuMatchesCpu(RunGpuFreshAdmission(
                new[] { rightDiscriminates }), cpuRight, expectedBranchCount: 1);
            SigmaNativeFreshObservationBranch rightChanges = FreshObservation(
                otherTarget, 43UL, provenanceOrdinal: 5, leftBroad: true);
            Assert.That(SigmaMerkabaSemanticOracle.TryResolveFreshBaseAdmission(
                new[] { rightChanges }, out SigmaFreshBaseAdmission cpuRightChanged),
                Is.True);
            Assert.That(cpuRightChanged.State, Is.Not.EqualTo(cpuRight.State),
                "Changing only the exact right-eye shadow must change the lift.");

            SigmaNativeFreshObservationBranch signedRight = FreshObservation(
                target, 43UL, provenanceOrdinal: 8, negateRightRows: true);
            Assert.That(SigmaMerkabaSemanticOracle.TryResolveFreshBaseAdmission(
                new[] { signedRight }, out SigmaFreshBaseAdmission cpuSigned),
                Is.True);
            AssertFreshGpuMatchesCpu(RunGpuFreshAdmission(
                new[] { signedRight }), cpuSigned, expectedBranchCount: 1);
            Assert.That(cpuSigned.State, Is.EqualTo(cpuUnique.State),
                "Signed query-row routing may not change the physical preimage.");

            int[] lattice = { -1, 0, 1 };
            long[][] tangentCorpus =
                (from x0 in lattice
                 from x1 in lattice
                 from x2 in lattice
                 from x3 in lattice
                 where x0 + x1 + x2 + x3 == 0 &&
                       (x0 != 0 || x1 != 0 || x2 != 0 || x3 != 0)
                 select new[]
                 {
                     Raw(x0, 2), Raw(x1, 2), Raw(x2, 2), Raw(x3, 2),
                 }).ToArray();
            Assert.That(tangentCorpus.Length, Is.EqualTo(18));
            for (int index = 0; index < tangentCorpus.Length; ++index)
            {
                SigmaNativeFreshObservationBranch fixture = FreshObservation(
                    tangentCorpus[index], (ulong)(100 + index),
                    provenanceOrdinal: 1000 + index,
                    negateRightRows: (index & 1) != 0);
                Assert.That(SigmaMerkabaSemanticOracle.TryResolveFreshBaseAdmission(
                    new[] { fixture }, out SigmaFreshBaseAdmission cpuFixture),
                    Is.True, $"fresh tangent fixture {index}");
                AssertFreshGpuMatchesCpu(RunGpuFreshAdmission(
                    new[] { fixture }), cpuFixture, expectedBranchCount: 1);
            }

            SigmaNativeFreshObservationBranch behind = FreshObservation(target,
                44UL, provenanceOrdinal: 6, rightFirstHit: false);
            Assert.That(SigmaMerkabaSemanticOracle.TryResolveFreshBaseAdmission(
                new[] { behind }, out _), Is.False);
            Assert.That(RunGpuFreshAdmission(new[] { behind }).Admitted, Is.False,
                "Behind/no-first-hit evidence cannot mint support.");

            SigmaNativeFreshObservationBranch noEvidence = FreshObservation(target,
                45UL, provenanceOrdinal: 7, evidence: false);
            Assert.That(SigmaMerkabaSemanticOracle.TryResolveFreshBaseAdmission(
                new[] { noEvidence }, out _), Is.False);
            Assert.That(RunGpuFreshAdmission(new[] { noEvidence }).Admitted, Is.False);

            SigmaNativeFreshObservationBranch unsupported = FreshObservation(target,
                45UL, provenanceOrdinal: 9, unsupportedOptical: true);
            Assert.That(SigmaMerkabaSemanticOracle.TryResolveFreshBaseAdmission(
                new[] { unsupported }, out _), Is.False);
            Assert.That(RunGpuFreshAdmission(new[] { unsupported }).Admitted,
                Is.False, "Unbounded/unsupported optical transfer must fail closed.");

            SigmaNativeFreshObservationBranch[] cold = Enumerable.Range(0, 5)
                .Select(index => FreshObservation(target, 46UL,
                    provenanceOrdinal: 100 + index)).ToArray();
            Assert.That(SigmaMerkabaSemanticOracle.TryResolveFreshBaseAdmission(
                cold, out _), Is.True,
                "The semantic program has a common result beyond the hot bound.");
            GpuFreshAdmission coldGpu = RunGpuFreshAdmission(cold);
            Assert.That(coldGpu.Admitted, Is.False,
                "The bounded hot collective may never truncate five branches into support.");
            Assert.That(coldGpu.BranchCount, Is.EqualTo(5u));
            Assert.That(coldGpu.ColdReason, Is.EqualTo(2u),
                "Overflow must be explicit retained cold continuation, not a false answer.");
        }

        [Test]
        public void DirectionalMouldCorrectsNearSupportAndNeverActsBehindHit()
        {
            SigmaS16 nearState = State(14, 1);
            SigmaS16 mouldState = State(14, 2);
            SigmaNativeOracleQuery mould = LeftQuery(Point(Raw(3, 1)),
                Point(0), true, false);
            SigmaNativePreimageCandidate correction = new(0,
                Cell(0, nearState, 0, 0), Cell(0, mouldState, 0, 0),
                new SigmaNativeDeltaWitness(0, 0, mouldState),
                RelationFor(mouldState, valid: true));
            SigmaNativeContractBranch branch = SigmaMerkabaSemanticOracle
                .ContractNativeQuery(mould, new[] { correction }).Branches.Single();
            Assert.That(branch.Claim,
                Is.EqualTo(SigmaNativeQueryClaim.PreHitExclusion));
            Assert.That(branch.Action.Active, Is.True);
            Assert.That(branch.Action.Action.Lower, Is.GreaterThan(0L));
            Assert.That(branch.Delta.State, Is.EqualTo(mouldState),
                "The same support contracts to the mould; this is not carving.");

            SigmaNativePreimageCandidate unrelatedNear = new(1,
                Cell(99, nearState, 99, 0), Cell(1, mouldState, 1, 0),
                new SigmaNativeDeltaWitness(1, 0, mouldState),
                RelationFor(mouldState, valid: true));
            Assert.That(SigmaMerkabaSemanticOracle.ContractNativeQuery(mould,
                new[] { unrelatedNear }).Branches, Is.Empty,
                "An unrelated sheet may not be dragged to the mould.");

            SigmaNativePreimageCandidate behind = new(2,
                Cell(2, State(14, 3), 2, 0), Cell(2, mouldState, 2, 0),
                new SigmaNativeDeltaWitness(2, 0, mouldState),
                RelationFor(mouldState, valid: true));
            Assert.That(SigmaMerkabaSemanticOracle.ContractNativeQuery(mould,
                new[] { behind }).Branches, Is.Empty,
                "Behind-hit state receives exactly NO_CLAIM and no action.");

            SigmaNativePreimageCandidate a = Candidate(3, 3, State(4, 1),
                new SigmaNativeDeltaWitness(0, 0, State(4, 1)));
            SigmaNativePreimageCandidate b = Candidate(4, 4, State(8, 1),
                new SigmaNativeDeltaWitness(1, 0, State(8, 1)));
            SigmaNativeContractResult equilibrium = SigmaMerkabaSemanticOracle
                .ContractJoint(new[]
                {
                    LeftQuery(Point(Raw(1, 2)), Point(Raw(1, 2)), true, true),
                    RightQuery(Point(Raw(1, 2)), Point(Raw(-3, 2)), true, true),
                }, new[] { a, b });
            Assert.That(equilibrium.Branches.Single().CandidateOrdinal, Is.EqualTo(4),
                "Independent query directions converge to their common exact state.");
            Assert.That(equilibrium.Branches.Single().Actions, Has.Length.EqualTo(2));
            Assert.That(equilibrium.Branches.Single().Actions.All(value => value.Active),
                Is.True, "Neither independent directional constraint may be lost.");
        }

        [Test]
        public void FreshContractMarksOnlyExactStereoPreHitForStaticExclusion()
        {
            int[] lattice = { -1, 0, 1 };
            long[][] targets =
                (from x0 in lattice
                 from x1 in lattice
                 from x2 in lattice
                 from x3 in lattice
                 where x0 + x1 + x2 + x3 == 0 &&
                       (x0 != 0 || x1 != 0 || x2 != 0 || x3 != 0)
                 select new[]
                 {
                     Raw(x0, 2), Raw(x1, 2), Raw(x2, 2), Raw(x3, 2),
                 }).ToArray();
            SigmaNativeFreshObservationBranch? measured = null;
            long[] measuredTarget = null;
            SigmaS16 prior = SigmaS16.Zero;
            for (int measuredIndex = 0;
                !measured.HasValue && measuredIndex < targets.Length;
                ++measuredIndex)
            {
                SigmaNativeFreshObservationBranch probe = FreshObservation(
                    targets[measuredIndex], 70UL, 500 + measuredIndex);
                Assert.That(probe.TryAssembleQueries(
                    out SigmaNativeOracleQuery left,
                    out SigmaNativeOracleQuery right), Is.True);
                for (int lane = 0; lane < SigmaS16.LaneCount; ++lane)
                {
                    foreach (int sign in new[] { -1, 1 })
                    {
                        SigmaS16 candidate = StateRaw(
                            (lane, Raw(sign, 64)));
                        long[] shadow = SigmaMerkabaSemanticOracle
                            .EvaluateMerkabaShadow(candidate);
                        long leftOrder = Enumerable.Range(0, 4).Aggregate(0L,
                            (sum, axis) => SigmaNumericDomain.QAdd(sum,
                                SigmaNumericDomain.QMul(shadow[axis],
                                    left.OrderRow[axis])));
                        long rightOrder = Enumerable.Range(0, 4).Aggregate(0L,
                            (sum, axis) => SigmaNumericDomain.QAdd(sum,
                                SigmaNumericDomain.QMul(shadow[axis],
                                    right.OrderRow[axis])));
                        if (leftOrder < left.MeasuredOrder.Lower &&
                            rightOrder < right.MeasuredOrder.Lower)
                        {
                            measured = probe;
                            measuredTarget = targets[measuredIndex];
                            prior = candidate;
                            break;
                        }
                    }
                    if (measured.HasValue)
                        break;
                }
            }
            Assert.That(measured.HasValue, Is.True,
                "Bounded tangent corpus must contain a stereo pre-hit pair.");

            GpuFreshAdmission exclusion = RunGpuFreshAdmission(
                new[] { measured.Value }, prior);
            Assert.That(exclusion.Admitted, Is.True);
            Assert.That(exclusion.ColdReason,
                Is.EqualTo((uint)SigmaNativeColdReason.StaticExclusion));
            Assert.That(RunGpuFreshAdmission(new[] { measured.Value }).ColdReason,
                Is.Zero, "No current support means there is nothing to retire.");

            SigmaNativeFreshObservationBranch oneEye = FreshObservation(
                measuredTarget, 71UL, 900, rightFirstHit: false);
            Assert.That(RunGpuFreshAdmission(new[] { oneEye }, prior).ColdReason,
                Is.Not.EqualTo((uint)SigmaNativeColdReason.StaticExclusion),
                "One-eye/behind-hit evidence may not authorize deletion.");
        }

        [Test]
        public void ExactInformationAndPhotometricLawPreventWeakRevisitDegradation()
        {
            SigmaCertificateFactor strong = Factor(-1, 1);
            SigmaCertificateFactor weak = Factor(-4, 4);
            IReadOnlyList<SigmaMinimizedFactor> strongThenWeak =
                SigmaGeneratedMerkabaProgram.MinimizeCertificates(
                    new[] { strong }.Concat(Enumerable.Repeat(weak, 10000)));
            IReadOnlyList<SigmaMinimizedFactor> weakThenStrong =
                SigmaGeneratedMerkabaProgram.MinimizeCertificates(
                    Enumerable.Repeat(weak, 10000).Append(strong));
            Assert.That(strongThenWeak.Single().Factor.Lower, Is.EqualTo(-1));
            Assert.That(strongThenWeak.Single().Factor.Upper, Is.EqualTo(1));
            Assert.That(weakThenStrong.Single().Factor.Lower, Is.EqualTo(-1));
            Assert.That(weakThenStrong.Single().Factor.Upper, Is.EqualTo(1));
            for (long assignment = -6; assignment <= 6; ++assignment)
            {
                bool exhaustive = strong.Lower <= assignment && assignment <= strong.Upper &&
                    weak.Lower <= assignment && assignment <= weak.Upper;
                bool compact = strongThenWeak.All(value =>
                    value.Factor.Lower <= assignment && assignment <= value.Factor.Upper);
                Assert.That(compact, Is.EqualTo(exhaustive));
            }

            SigmaS16 state = State(4, 1);
            SigmaNativePreimageCandidate candidate = Candidate(0, 0, state,
                new SigmaNativeDeltaWitness(0, 0, state));
            SigmaNativeOracleQuery sharp = LeftQuery(Point(Raw(1, 2)),
                Point(Raw(1, 2)), true, true);
            SigmaNativeContractBranch sharpBranch = SigmaMerkabaSemanticOracle
                .ContractNativeQuery(sharp, new[] { candidate }).Branches.Single();
            Assert.That(sharpBranch.OpticalClaim, Is.True);

            SigmaNativePhotometricLaw missing = Law(metadataPresent: false,
                scaleLower: 1, scaleUpper: 1);
            SigmaNativeOracleQuery opticalOnlyMissing = Query("SENSOR_LEFT", 0,
                Axis(0), Axis(1), Point(Raw(1, 2)), Point(Raw(99, 1)),
                Point(SigmaNumericDomain.One), false, true, missing);
            Assert.That(SigmaMerkabaSemanticOracle.ContractNativeQuery(
                opticalOnlyMissing, new[] { candidate }).Branches, Is.Empty,
                "Missing metadata cannot silently make arbitrary RGB authoritative.");

            SigmaNativeOracleQuery poorLightValidDepth = Query("SENSOR_LEFT", 0,
                Axis(0), Axis(1), Point(Raw(1, 2)), Point(Raw(99, 1)),
                Point(SigmaNumericDomain.One), true, true, missing);
            SigmaNativeContractBranch depthBranch = SigmaMerkabaSemanticOracle
                .ContractNativeQuery(poorLightValidDepth,
                    new[] { candidate }).Branches.Single();
            Assert.That(depthBranch.OpticalClaim, Is.False);

            SigmaNativePhotometricLaw doubledIllumination = Law(true, 2, 2);
            SigmaNativeOracleQuery illuminationOnly = Query("SENSOR_LEFT", 0,
                Axis(0), Axis(1), Point(Raw(1, 2)), Point(Raw(1, 1)),
                Point(SigmaNumericDomain.One), true, true, doubledIllumination);
            SigmaNativeContractBranch lit = SigmaMerkabaSemanticOracle
                .ContractNativeQuery(illuminationOnly,
                    new[] { candidate }).Branches.Single();
            Assert.That(lit.Delta.State, Is.EqualTo(state),
                "Calibrated illumination changes optical prediction, not geometry.");

            SigmaNativeOracleQuery incompatibleStaticEpoch = LeftQuery(
                Point(Raw(5, 1)), Point(0), true, false);
            SigmaNativeContractResult moving = SigmaMerkabaSemanticOracle
                .ContractJoint(new[] { sharp, incompatibleStaticEpoch },
                    new[] { candidate });
            Assert.That(moving.Resolution, Is.EqualTo(SigmaNativeOracleResolution.None),
                "Irreconcilable static evidence stays unresolved for S4-09.");
        }

        [Test]
        public void GaugeRefinementRelationsAndCoupledShadowModesRemainOneField()
        {
            SigmaS16 state = State(4, 1);
            SigmaGaugeCell parentGauge = new(5, -3, 0, "six-field-proof");
            SigmaNativeOracleCell parent = new(0, 0, parentGauge, state);
            SigmaNativeOracleCell[] children = SigmaGeneratedMerkabaProgram
                .SplitGaugeCell(parentGauge).Select(cell =>
                    new SigmaNativeOracleCell(0, 0, cell, state)).ToArray();
            SigmaNativeOracleQuery query = LeftQuery(Point(0), Point(0), true, false);
            SigmaNativeSceneShadow coarse = SigmaMerkabaSemanticOracle
                .EvaluateAndReduce(new[] { parent }, new[] { 0 }, query);
            SigmaNativeSceneShadow refined = SigmaMerkabaSemanticOracle
                .EvaluateAndReduce(children, Enumerable.Range(0, 4), query);
            Assert.That(refined, Is.EqualTo(coarse),
                "Kappa child measure exactly sums to the parent before new information.");

            SigmaNativeDeltaWitness commonDelta = new(parentGauge.U, parentGauge.V,
                state);
            SigmaNativeContractResult coarseReverse = SigmaMerkabaSemanticOracle
                .ContractNativeQuery(LeftQuery(coarse.Order, Point(0), true, false),
                    new[]
                    {
                        new SigmaNativePreimageCandidate(0,
                            new SigmaNativeOracleCell(0, 0, parentGauge,
                                SigmaS16.Zero), parent, commonDelta,
                            RelationFor(state, valid: true)),
                    });
            SigmaNativeContractResult refinedReverse = SigmaMerkabaSemanticOracle
                .ContractNativeQuery(LeftQuery(refined.Order, Point(0), true, false),
                    children.Select((child, ordinal) =>
                        new SigmaNativePreimageCandidate(ordinal,
                            new SigmaNativeOracleCell(0, 0, child.Gauge,
                                SigmaS16.Zero), child, commonDelta,
                            RelationFor(state, valid: true))));
            Assert.That(coarseReverse.HasCanonicalAnswer, Is.True);
            Assert.That(refinedReverse.Resolution,
                Is.EqualTo(SigmaNativeOracleResolution.CommonDelta));
            Assert.That(refinedReverse.Branches.All(value =>
                value.Delta.Equals(coarseReverse.Branches[0].Delta)), Is.True,
                "Gauge-only refinement preserves the complete reverse result.");

            string parentNormal = SigmaGeneratedMerkabaProgram
                .CanonicalGaugeSerialization(new[] { parentGauge });
            string childNormal = SigmaGeneratedMerkabaProgram
                .CanonicalGaugeSerialization(children.Select(value => value.Gauge));
            Assert.That(childNormal, Is.EqualTo(parentNormal));
            Assert.That(SigmaGeneratedMerkabaProgram.CanonicalGaugeSerialization(
                children.Reverse().Select(value => value.Gauge)), Is.EqualTo(parentNormal));

            SigmaS16 right = State(1, 1);
            SigmaS16 context = State(2, 1);
            SigmaNativeRelationWitness local = SigmaMerkabaSemanticOracle
                .EvaluateNativeRelation(state, right, context);
            SigmaNativeRelationWitness rebuiltNonlocal = SigmaMerkabaSemanticOracle
                .EvaluateNativeRelation(state, right, context);
            Assert.That(rebuiltNonlocal, Is.EqualTo(local),
                "Backing decomposition/cache deletion cannot alter relation queries.");
            foreach (int windows in new[] { 1, 2, 7 })
            {
                SigmaNativeRelationWitness[] forward = EvaluateRelationWindows(
                    Enumerable.Repeat((state, right, context), 7).ToArray(),
                    windows, reverse: false);
                SigmaNativeRelationWitness[] reverse = EvaluateRelationWindows(
                    Enumerable.Repeat((state, right, context), 7).ToArray(),
                    windows, reverse: true);
                Assert.That(forward.All(value => value.Equals(local)), Is.True);
                CollectionAssert.AreEquivalent(forward, reverse);
            }

            SigmaS16 shadowKernelA = State(0, 1);
            SigmaS16 shadowKernelB = State(15, 1);
            CollectionAssert.AreEqual(
                SigmaMerkabaSemanticOracle.EvaluateMerkabaShadow(shadowKernelA),
                SigmaMerkabaSemanticOracle.EvaluateMerkabaShadow(shadowKernelB));
            Assert.That(SigmaMerkabaSemanticOracle.EvaluateNativeRelation(
                    shadowKernelA, right, context),
                Is.Not.EqualTo(SigmaMerkabaSemanticOracle.EvaluateNativeRelation(
                    shadowKernelB, right, context)),
                "A sensor-transparent direction remains live in the complete program.");

            SigmaGaugeCell a = new(9, 2, 0, "A");
            SigmaGaugeCell b = new(12, 2, 0, "B");
            Assert.That(SigmaGeneratedMerkabaProgram.CanonicalGaugeSerialization(
                    new[] { a, b }),
                Is.EqualTo(SigmaGeneratedMerkabaProgram.CanonicalGaugeSerialization(
                    new[] { b, a })));
        }

        [Test]
        public void SparseSupportIndexMatchesExhaustiveMixedRefinedAndNonresidentWorlds()
        {
            SigmaNativeOracleQuery query = LeftQuery(Point(0), Point(0), true, false);
            for (int stateMask = 0; stateMask < 256; ++stateMask)
            {
                var cells = new List<SigmaNativeOracleCell>(8);
                var summaries = new List<SigmaNativeQuerySupportSummary>(8);
                for (int index = 0; index < 8; ++index)
                {
                    int level = index & 1;
                    SigmaS16 state = (stateMask & (1 << index)) != 0
                        ? State(14, index + 1) : SigmaS16.Zero;
                    cells.Add(new SigmaNativeOracleCell((ulong)index, 0,
                        new SigmaGaugeCell(index << level, 0, level, $"p{index}"),
                        state, resident: index % 3 != 0));
                    bool boundaryClosed = index % 4 != 0;
                    summaries.Add(SigmaMerkabaSemanticOracle.SummarizeCell(index,
                        cells, boundaryClosed, GaugeFingerprint));
                }
                int[] indexed = SigmaMerkabaSemanticOracle.SelectNativeQuerySupport(
                    summaries, GaugeFingerprint);
                SigmaNativeSceneShadow indexedResult = SigmaMerkabaSemanticOracle
                    .EvaluateAndReduce(cells, indexed, query);
                SigmaNativeSceneShadow exhaustive = SigmaMerkabaSemanticOracle
                    .EvaluateAndReduce(cells, Enumerable.Range(0, cells.Count), query);
                Assert.That(indexedResult, Is.EqualTo(exhaustive),
                    $"query-support false negative for state mask {stateMask}");
            }

            var stale = new SigmaNativeQuerySupportSummary(7, true, true, false,
                true, "stale-program", "stale-gauge");
            CollectionAssert.AreEqual(new[] { 7 },
                SigmaMerkabaSemanticOracle.SelectNativeQuerySupport(
                    new[] { stale }, GaugeFingerprint),
                "Missing/stale nonresident summary must fail closed to inclusion.");
        }

        [Test]
        public void ExecutionWindowsAndOrderAreInvisibleAndScratchIsTestOnly()
        {
            SigmaNativeOracleQuery query = LeftQuery(Point(0), Point(0), true, false);
            SigmaNativeOracleCell[] cells = Enumerable.Range(0, 7).Select(index =>
                Cell(index, State(14, index + 1), index, 0)).ToArray();
            SigmaNativeSceneShadow expected = ReduceWindows(cells, query, 1, false);
            foreach (int windows in new[] { 1, 2, 7 })
            {
                Assert.That(ReduceWindows(cells, query, windows, false),
                    Is.EqualTo(expected));
                Assert.That(ReduceWindows(cells, query, windows, true),
                    Is.EqualTo(expected));
            }
            Assert.That(typeof(SigmaNativeContractBranch).Assembly.GetName().Name,
                Is.EqualTo("Genesis.RoomScan.Tests"));
            Assert.That(AssetDatabase.FindAssets(
                    "SigmaNativeContract t:ComputeShader").Count(guid =>
                    string.Equals(Path.GetFileNameWithoutExtension(
                        AssetDatabase.GUIDToAssetPath(guid)),
                        "SigmaNativeContract", StringComparison.Ordinal)),
                Is.EqualTo(1));
            Assert.That(AssetDatabase.FindAssets(
                    "SigmaNativeQuery t:ComputeShader").Count(guid =>
                    string.Equals(Path.GetFileNameWithoutExtension(
                        AssetDatabase.GUIDToAssetPath(guid)),
                        "SigmaNativeQuery", StringComparison.Ordinal)),
                Is.EqualTo(1));
            Assert.That(SigmaGeneratedMerkabaProgram.EntryPoints, Has.Length.EqualTo(7));
            foreach (SigmaDefaultBackingKind backing in Enum.GetValues(
                         typeof(SigmaDefaultBackingKind)))
            {
                SigmaS16 decoded = SigmaGeneratedMerkabaProgram
                    .DecodeDefaultRepresentation(backing);
                CollectionAssert.AreEqual(new long[4],
                    SigmaMerkabaSemanticOracle.EvaluateMerkabaShadow(decoded));
                Assert.That(SigmaMerkabaSemanticOracle.EvaluateNativeRelation(
                        decoded, decoded, decoded).RelationClass,
                    Is.EqualTo(SigmaMerkabaRelationClass.DefaultSat));
            }
        }

        [Test]
        public void OracleGpuGraphIsFixedParallelAndCardinalityInvariant()
        {
            string query = File.ReadAllText(AssetDatabase.GUIDToAssetPath(
                AssetDatabase.FindAssets("SigmaNativeQuery t:ComputeShader")
                    .Single(guid => string.Equals(Path.GetFileNameWithoutExtension(
                        AssetDatabase.GUIDToAssetPath(guid)),
                        "SigmaNativeQuery", StringComparison.Ordinal))));
            string queryOracle = File.ReadAllText(AssetDatabase.GUIDToAssetPath(
                AssetDatabase.FindAssets(
                    "SigmaNativeQueryOracle t:ComputeShader").Single(guid =>
                    string.Equals(Path.GetFileNameWithoutExtension(
                        AssetDatabase.GUIDToAssetPath(guid)),
                        "SigmaNativeQueryOracle", StringComparison.Ordinal))));
            string contract = File.ReadAllText(AssetDatabase.GUIDToAssetPath(
                AssetDatabase.FindAssets("SigmaNativeContract t:ComputeShader")
                    .Single(guid => string.Equals(Path.GetFileNameWithoutExtension(
                        AssetDatabase.GUIDToAssetPath(guid)),
                        "SigmaNativeContract", StringComparison.Ordinal))));
            static string[] Kernels(string source) => source.Split('\n')
                .Select(line => line.Trim()).Where(line =>
                    line.StartsWith("#pragma kernel ", StringComparison.Ordinal))
                .Select(line => line.Split((char[])null,
                    StringSplitOptions.RemoveEmptyEntries)[2]).ToArray();

            CollectionAssert.AreEqual(new[]
            {
                "EvaluateNativeRelation",
            }, Kernels(query));
            CollectionAssert.AreEqual(new[]
            {
                "SelectNativeQuerySupport", "EvaluateNativeQuery",
                "ReduceNativeQuery", "EvaluateNativeRelation",
                "EvaluateNativeStitchPairs", "CloseNativeStitchCases",
            }, Kernels(queryOracle));
            CollectionAssert.AreEqual(new[]
            {
                "ContractNativeQuery",
            }, Kernels(contract));
            Assert.That(query, Does.Contain("[numthreads(256, 1, 1)]\n" +
                "void EvaluateNativeRelation"));
            Assert.That(queryOracle, Does.Contain("[numthreads(128, 1, 1)]\n" +
                "void ReduceNativeQuery"));
            Assert.That(queryOracle, Does.Contain("[numthreads(256, 1, 1)]\n" +
                "void EvaluateNativeStitchPairs"));
            Assert.That(queryOracle, Does.Contain("[numthreads(256, 1, 1)]\n" +
                "void CloseNativeStitchCases"));
            Assert.That(queryOracle, Does.Contain(
                "#define SIGMA_NATIVE_STITCH_ASSIGNMENT_COUNT 24u"));
            Assert.That(queryOracle, Does.Contain(
                "#define SIGMA_NATIVE_STITCH_ASSIGNMENT_LANES 192u"));
            Assert.That(contract, Does.Contain(
                "#if defined(SIGMA_N4_TILE_CLOSE_VARIANT)\n" +
                "[numthreads(256, 1, 1)]\n#else\n" +
                "[numthreads(64, 1, 1)]\n#endif\n" +
                "void ContractNativeQuery"));
            Assert.That(contract, Does.Not.Contain("SigmaNativeContractOne"));
            Assert.That(contract, Does.Not.Contain("_NativeReverseKeys"));
            Assert.That(contract, Does.Contain(
                "SigmaNativeFreshLiftFootprint(groupId.x - 1u,"));
            Assert.That(contract, Does.Contain(
                "SigmaNativeFreshFinalize(groupThreadId.x)"));
            Assert.That(contract, Does.Not.Contain(
                "for (uint freshBranch"));
            Assert.That(queryOracle, Does.Not.Contain("for (uint action"));
            Assert.That(queryOracle, Does.Not.Contain("for (uint relationIndex"));
            Assert.That(queryOracle, Does.Not.Contain("ChartSectorDirection"));
            Assert.That(queryOracle, Does.Not.Contain("while ("));
            Assert.That(queryOracle, Does.Not.Contain("InterlockedCompareExchange"));
            Assert.That(queryOracle, Does.Not.Contain("for (uint member"));
            Assert.That(queryOracle, Does.Not.Contain("for (uint other"));
            Assert.That(query, Does.Not.Contain("SigmaNativeTryIntegralQ48"));
            Assert.That(queryOracle, Does.Contain(
                "SIGMA_NATIVE_REDUCE_COLD_CONTINUATION_REQUIRED"));
        }

        [Test]
        public void VulkanConstructiveStitchSetMatchesGeneratedCpuAuthority()
        {
            const int maxNodes = 8;
            const int pairStride = 3;
            const int componentReceiptOffset = 193;
            const int caseStride = 201;
            const int assignmentCount = 24;
            const int assignmentNodeStride = 8;
            string Fingerprint(char marker) => new(marker, 64);
            SigmaStitchContactBranch Contact() => new(new[]
            {
                Point(SigmaNumericDomain.One),
                Point(SigmaNumericDomain.One),
                Point(SigmaNumericDomain.One),
            });
            SigmaStitchLocality Locality(ulong key, SigmaS16 state, int level,
                char marker) => new(key, level, state, Fingerprint(marker));
            uint HashProfile(IEnumerable<SigmaS16> profile)
            {
                uint hash = 2166136261u;
                foreach (SigmaS16 factor in profile)
                    hash = unchecked((hash ^ HashS16(factor)) * 16777619u);
                return hash;
            }
            SigmaBoundaryNativeInput Edge(int ordinal, ulong left, ulong right,
                char marker, bool openContinuation = false,
                SigmaSampleBoundarySide leftSide = SigmaSampleBoundarySide.Right,
                SigmaSampleBoundarySide rightSide = SigmaSampleBoundarySide.Left)
            {
                SigmaStitchContactBranch contact = Contact();
                return new SigmaBoundaryNativeInput(new SigmaImplicitBoundaryRef(
                    ordinal, left, right, leftSide, rightSide,
                    openContinuation ? new[] { contact, contact } :
                        new[] { contact }),
                    new SigmaStitchNativeContext(Fingerprint(marker)));
            }

            SigmaStitchLocality a = Locality(11UL, State(1, 1), 0, 'a');
            SigmaStitchLocality b = Locality(22UL, State(2, 1), 0, 'b');
            SigmaStitchLocality c = Locality(33UL, State(4, 1), 0, 'c');
            SigmaStitchLocality nonAssociativeLeft = Locality(44UL,
                State(0, 1), 0, 'd');
            SigmaStitchLocality nonAssociativeRight = Locality(55UL,
                State(3, 1), 0, 'e');
            SigmaStitchLocality incompatibleLeft = Locality(66UL,
                State(0, 1), 0, 'f');
            SigmaStitchLocality incompatibleRight = Locality(77UL,
                State(7, 1), 0, 'g');
            SigmaStitchLocality uncertainRight = Locality(88UL,
                State(8, 1), 0, 'h');

            // Six distinct signed basis modes form the smallest exact intrinsic
            // cycle admitted by the complete associator profile. This is a
            // bounded frozen fixture, not a runtime search.
            SigmaStitchLocality q0 = Locality(101UL, State(1, -1), 0, 'i');
            SigmaStitchLocality q1 = Locality(102UL, State(2, 1), 0, 'j');
            SigmaStitchLocality q2 = Locality(103UL, State(4, -1), 0, 'k');
            SigmaStitchLocality q3 = Locality(104UL, State(1, 1), 0, 'l');
            SigmaStitchLocality q4 = Locality(105UL, State(2, -1), 0, 'L');
            SigmaStitchLocality q5 = Locality(106UL, State(8, 1), 0, 'K');
            SigmaS16 r0State = State(1, 1);
            SigmaS16 r1State = State(2, 1);
            SigmaStitchLocality r0 = Locality(401UL, r0State, 0, 'M');
            SigmaStitchLocality r1 = Locality(402UL, r1State, 0, 'N');
            SigmaBoundaryNativeInput[] firstOrbitCycle =
            {
                Edge(0, 101UL, 102UL, 'y'),
                Edge(1, 102UL, 103UL, 'z'),
                Edge(2, 103UL, 104UL, 'A'),
                Edge(3, 104UL, 105UL, 'B'),
                Edge(4, 105UL, 106UL, 'C'),
                Edge(5, 106UL, 101UL, 'D'),
            };
            SigmaBoundaryNativeInput[] secondOrbitCycle =
            {
                Edge(6, 401UL, 402UL, 'Q'),
            };

            var fixtures = new List<(string Name,
                SigmaStitchLocality[] Nodes,
                SigmaBoundaryNativeInput[] Edges)>
            {
                ("unique-neighbour", new[] { a, b },
                    new[] { Edge(0, 11UL, 22UL, 'm') }),
                ("reversed-neighbour", new[] { b, a },
                    new[] { Edge(0, 22UL, 11UL, 'm', leftSide:
                        SigmaSampleBoundarySide.Left, rightSide:
                        SigmaSampleBoundarySide.Right) }),
                ("sample-side-permutation", new[] { a, b },
                    new[] { Edge(0, 11UL, 22UL, 'm', leftSide:
                        SigmaSampleBoundarySide.Down, rightSide:
                        SigmaSampleBoundarySide.Up) }),
                ("prior-disconnected-components", new[] { a, b },
                    Array.Empty<SigmaBoundaryNativeInput>()),
                ("chain-non-gauge-ambiguity", new[] { a, b, c }, new[]
                {
                    Edge(0, 11UL, 22UL, 'n'),
                    Edge(1, 22UL, 33UL, 'o'),
                }),
                ("chain-edge-order-permutation", new[] { c, a, b }, new[]
                {
                    Edge(1, 22UL, 33UL, 'o'),
                    Edge(0, 11UL, 22UL, 'n'),
                }),
                ("intrinsic-associator-fold", new[]
                {
                    nonAssociativeLeft, nonAssociativeRight,
                }, new[]
                {
                    Edge(0, 44UL, 55UL, 'p'),
                }),
                ("incompatible-intrinsic-profile", new[]
                {
                    incompatibleLeft, incompatibleRight,
                }, new[]
                {
                    Edge(0, 66UL, 77UL, 'q'),
                }),
                ("uncertain-intrinsic-profile", new[]
                {
                    incompatibleLeft, uncertainRight,
                }, new[]
                {
                    Edge(0, 66UL, 88UL, 'r'),
                }),
                ("spatially-separated-equal-modes", new[]
                {
                    Locality(201UL, State(1, 1), 0, 's'),
                    Locality(202UL, State(1, 1), 0, 't'),
                }, Array.Empty<SigmaBoundaryNativeInput>()),
                ("thin-wall-two-sided", new[]
                {
                    Locality(211UL, State(1, 1), 0, 'u'),
                    Locality(212UL, State(2, 1), 0, 'v'),
                }, Array.Empty<SigmaBoundaryNativeInput>()),
                ("occlusion-continuation-open", new[] { a, b },
                    new[] { Edge(0, 11UL, 22UL, 'w',
                        openContinuation: true) }),
                ("later-side-view-resolves", new[] { a, b },
                    new[] { Edge(0, 11UL, 22UL, 'x') }),
                ("consistent-native-cycle", new[] { q0, q1, q2, q3, q4, q5 },
                    firstOrbitCycle),
                ("consistent-native-cycle-second-orbit",
                    new[] { r0, r1 }, secondOrbitCycle),
                ("disconnected-different-chart-orbits",
                    new[] { q0, q1, q2, q3, q4, q5, r0, r1 },
                    firstOrbitCycle.Concat(secondOrbitCycle).ToArray()),
                ("later-join-different-chart-orbits",
                    new[] { q0, q1, q2, q3, q4, q5, r0, r1 },
                    firstOrbitCycle.Concat(secondOrbitCycle).Append(
                        Edge(7, 101UL, 402UL, 'U')).ToArray()),
                ("inconsistent-fundamental-cycle", new[] { a, b, c }, new[]
                {
                    Edge(0, 11UL, 22UL, 'C'),
                    Edge(1, 22UL, 33UL, 'D'),
                    Edge(2, 11UL, 33UL, 'E'),
                }),
                ("mixed-level-disconnected", new[]
                {
                    Locality(301UL, State(1, 1), 0, 'F'),
                    Locality(302UL, State(2, 1), 1, 'G'),
                    Locality(303UL, State(4, 1), 2, 'H'),
                }, Array.Empty<SigmaBoundaryNativeInput>()),
            };

            var states = new List<SigmaS16>();
            var caseHeaders = new List<UInt4>();
            var nodeHeaders = new List<UInt4>();
            var edgeInputs = new List<UInt4>();
            var contacts = new List<UInt2>();
            var tags = new Dictionary<uint, string>();
            var cpuPatterns = new List<SigmaStitchPattern>();
            var cpuPairWitnesses = new List<SigmaStitchWitnessSet>();
            var cpuEdgeWitnesses = new List<SigmaStitchWitnessSet>();
            var fixtureEdgeOffsets = new List<int>();
            uint nextTag = 1u;

            foreach ((string name, SigmaStitchLocality[] nodes,
                         SigmaBoundaryNativeInput[] edges) in fixtures)
            {
                Assert.That(nodes.Length, Is.InRange(1, maxNodes), name);
                int nodeOffset = nodeHeaders.Count;
                int edgeOffset = edgeInputs.Count;
                fixtureEdgeOffsets.Add(edgeOffset);
                var nodeByKey = new Dictionary<ulong, int>();
                foreach (SigmaStitchLocality node in nodes)
                {
                    uint stateOffset = checked((uint)(states.Count *
                        SigmaS16.LaneCount));
                    states.Add(node.State);
                    uint tag = nextTag++;
                    tags.Add(tag, node.CompletePayloadFingerprint);
                    nodeByKey.Add(node.ScratchKey, nodeHeaders.Count);
                    nodeHeaders.Add(new UInt4
                    {
                        X = stateOffset,
                        Y = checked((uint)node.Level),
                        Z = tag,
                    });
                }
                foreach (SigmaBoundaryNativeInput edge in edges)
                {
                    SigmaStitchContactBranch contact =
                        edge.Boundary.ContactBranches[0];
                    uint contactOffset = checked((uint)contacts.Count);
                    for (int endpoint = 0; endpoint < 2; ++endpoint)
                        foreach (SigmaQ48Interval axis in contact.RoomBounds)
                        {
                            contacts.Add(Pack(axis.Lower));
                            contacts.Add(Pack(axis.Upper));
                        }
                    bool open = edge.Boundary.ContactBranches.Length != 1;
                    uint packed = contactOffset |
                        ((uint)SigmaNativeQueryClaim.FirstHitMould << 20) |
                        ((uint)SigmaNativeQueryClaim.FirstHitMould << 22) |
                        (open ? 1u << 24 : 0u);
                    edgeInputs.Add(new UInt4
                    {
                        X = checked((uint)nodeByKey[edge.Boundary.LeftKey]),
                        Y = checked((uint)nodeByKey[edge.Boundary.RightKey]),
                        Z = 0u,
                        W = packed,
                    });
                    SigmaStitchLocality leftLocality = nodes.Single(value =>
                        value.ScratchKey == edge.Boundary.LeftKey);
                    SigmaStitchLocality rightLocality = nodes.Single(value =>
                        value.ScratchKey == edge.Boundary.RightKey);
                    SigmaStitchWitnessSet edgeWitness =
                        SigmaGeneratedMerkabaProgram.EvaluateModalStitch(
                            edge.Boundary, leftLocality, rightLocality,
                            edge.NativeContext);
                    var pairBoundary = new SigmaImplicitBoundaryRef(
                        edge.Boundary.EdgeIndex, edge.Boundary.LeftKey,
                        edge.Boundary.RightKey, edge.Boundary.LeftSide,
                        edge.Boundary.RightSide,
                        new[] { edge.Boundary.ContactBranches[0] });
                    SigmaStitchWitnessSet pairWitness =
                        SigmaGeneratedMerkabaProgram.EvaluateModalStitch(
                            pairBoundary, leftLocality, rightLocality,
                            edge.NativeContext);
                    cpuEdgeWitnesses.Add(edgeWitness);
                    cpuPairWitnesses.Add(pairWitness);
                }
                Assert.That(SigmaGeneratedMerkabaProgram
                    .TryIntegrateStitchPattern(nodes, edges,
                        out SigmaStitchPattern pattern), Is.True, name);
                cpuPatterns.Add(pattern);
                caseHeaders.Add(new UInt4
                {
                    X = checked((uint)nodeOffset),
                    Y = checked((uint)nodes.Length),
                    Z = checked((uint)edgeOffset),
                    W = checked((uint)edges.Length),
                });
            }

            ComputeShader shader = LoadShader("SigmaNativeQueryOracle");
            using GraphicsBuffer stateBuffer = Buffer(PackStates(states));
            using GraphicsBuffer caseHeaderBuffer = Buffer(caseHeaders.ToArray());
            using GraphicsBuffer nodeHeaderBuffer = Buffer(nodeHeaders.ToArray());
            using GraphicsBuffer edgeBuffer = Buffer(edgeInputs.Count == 0
                ? new UInt4[1] : edgeInputs.ToArray());
            using GraphicsBuffer contactBuffer = Buffer(contacts.Count == 0
                ? new UInt2[1] : contacts.ToArray());
            int pairCount = edgeInputs.Count * 16;
            using GraphicsBuffer pairResultBuffer = Buffer<UInt4>(
                Math.Max(1, pairCount * pairStride));
            using GraphicsBuffer edgeResultBuffer = Buffer<UInt4>(
                Math.Max(1, edgeInputs.Count));
            using GraphicsBuffer caseResultBuffer = Buffer<UInt4>(
                fixtures.Count * caseStride);

            int pairKernel = shader.FindKernel("EvaluateNativeStitchPairs");
            shader.SetBuffer(pairKernel, "_NativeStates", stateBuffer);
            shader.SetBuffer(pairKernel, "_NativeStitchNodeHeaders",
                nodeHeaderBuffer);
            shader.SetBuffer(pairKernel, "_NativeStitchEdges", edgeBuffer);
            shader.SetBuffer(pairKernel, "_NativeStitchContacts", contactBuffer);
            shader.SetBuffer(pairKernel, "_NativeStitchPairResults",
                pairResultBuffer);
            shader.SetInt("_NativeStitchPairCount", pairCount);
            shader.Dispatch(pairKernel, Math.Max(1, pairCount), 1, 1);

            int closeKernel = shader.FindKernel("CloseNativeStitchCases");
            shader.SetBuffer(closeKernel, "_NativeStitchCaseHeaders",
                caseHeaderBuffer);
            shader.SetBuffer(closeKernel, "_NativeStitchNodeHeaders",
                nodeHeaderBuffer);
            shader.SetBuffer(closeKernel, "_NativeStitchEdges", edgeBuffer);
            shader.SetBuffer(closeKernel, "_NativeStitchPairResults",
                pairResultBuffer);
            shader.SetBuffer(closeKernel, "_NativeStitchEdgeResults",
                edgeResultBuffer);
            shader.SetBuffer(closeKernel, "_NativeStitchCaseResults",
                caseResultBuffer);
            shader.SetInt("_NativeStitchCaseCount", fixtures.Count);
            shader.Dispatch(closeKernel, fixtures.Count, 1, 1);

            UInt4[] pairResults = Read<UInt4>(pairResultBuffer,
                Math.Max(1, pairCount * pairStride));
            UInt4[] edgeResults = Read<UInt4>(edgeResultBuffer,
                Math.Max(1, edgeInputs.Count));
            UInt4[] caseResults = Read<UInt4>(caseResultBuffer,
                fixtures.Count * caseStride);

            int globalWitness = 0;
            var canonicalByFixture = new Dictionary<string, string>(
                StringComparer.Ordinal);
            var componentOrbitReceiptsByFixture =
                new Dictionary<string, uint[]>(StringComparer.Ordinal);
            for (int fixtureIndex = 0; fixtureIndex < fixtures.Count;
                 ++fixtureIndex)
            {
                (string name, SigmaStitchLocality[] nodes,
                    SigmaBoundaryNativeInput[] edges) = fixtures[fixtureIndex];
                int edgeOffset = fixtureEdgeOffsets[fixtureIndex];
                var resolvedPairs = new List<(ulong Left, ulong Right)>();
                for (int localEdge = 0; localEdge < edges.Length; ++localEdge)
                {
                    SigmaStitchWitnessSet cpuPair =
                        cpuPairWitnesses[globalWitness];
                    SigmaStitchWitnessSet cpuEdge =
                        cpuEdgeWitnesses[globalWitness++];
                    int globalEdge = edgeOffset + localEdge;
                    for (int pair = 0; pair < 16; ++pair)
                    {
                        SigmaStitchRelationReceipt receipt =
                            cpuPair.Receipts[pair];
                        int record = (globalEdge * 16 + pair) * pairStride;
                        UInt4 header = pairResults[record];
                        UInt4 hashes = pairResults[record + 1];
                        UInt4 factors = pairResults[record + 2];
                        Assert.That(header.X,
                            Is.EqualTo((uint)receipt.ClosureClass),
                            $"{name} edge {localEdge} pair {pair} closure");
                        Assert.That(header.Y & 0xffffu,
                            Is.EqualTo((uint)receipt.LeftSector), name);
                        Assert.That(header.Y >> 16,
                            Is.EqualTo((uint)receipt.RightSector), name);
                        Assert.That(header.Z,
                            Is.EqualTo((uint)receipt.TransportAddress), name);
                        Assert.That((header.W & 1u) != 0u ? -1 : 1,
                            Is.EqualTo(receipt.ForwardTransportSign), name);
                        Assert.That((header.W & 2u) != 0u ? -1 : 1,
                            Is.EqualTo(receipt.ReverseTransportSign), name);
                        Assert.That(header.W & 4u, Is.EqualTo(4u), name);
                        Assert.That(hashes.X, Is.EqualTo(
                            HashS16(receipt.LinkDefect)), name);
                        Assert.That(hashes.Y, Is.EqualTo(
                            HashS16(receipt.ReverseLinkDefect)), name);
                        Assert.That(hashes.Z, Is.EqualTo(
                            HashProfile(receipt.AssociatorProfile)), name);
                        Assert.That(hashes.W, Is.EqualTo(
                            HashProfile(receipt.ReverseAssociatorProfile)), name);
                        Assert.That(factors.X & 0xfu,
                            Is.EqualTo((uint)receipt.LinkClass), name);
                        Assert.That((factors.X >> 4) & 0xfu,
                            Is.EqualTo((uint)receipt.ReverseLinkClass), name);
                        Assert.That((factors.X >> 8) & 0xfu,
                            Is.EqualTo((uint)receipt.AssociatorClass), name);
                        Assert.That((factors.X >> 12) & 0xfu,
                            Is.EqualTo((uint)receipt.ReverseAssociatorClass),
                            name);
                    }
                    UInt4 gpuEdge = edgeResults[globalEdge];
                    Assert.That(gpuEdge.X,
                        Is.EqualTo((uint)cpuEdge.Resolution),
                        $"{name} edge result");
                    if (cpuEdge.Resolution == SigmaStitchResolution.Resolved)
                    {
                        resolvedPairs.Add((edges[localEdge].Boundary.LeftKey,
                            edges[localEdge].Boundary.RightKey));
                        Assert.That(gpuEdge.Y & 0xffffu,
                            Is.EqualTo((uint)cpuEdge.Resolved.LeftSector), name);
                        Assert.That(gpuEdge.Y >> 16,
                            Is.EqualTo((uint)cpuEdge.Resolved.RightSector), name);
                        Assert.That(gpuEdge.Z,
                            Is.EqualTo((uint)cpuEdge.Resolved.Receipt
                                .TransportAddress),
                            name);
                    }
                }

                // Derive transient connected components only for canonical
                // comparison. They are neither persisted nor supplied to GPU.
                var parent = nodes.ToDictionary(value => value.ScratchKey,
                    value => value.ScratchKey);
                ulong Root(ulong key)
                {
                    while (parent[key] != key) key = parent[key];
                    return key;
                }
                foreach ((ulong left, ulong right) in resolvedPairs)
                {
                    ulong leftRoot = Root(left);
                    ulong rightRoot = Root(right);
                    if (leftRoot != rightRoot) parent[rightRoot] = leftRoot;
                }
                ulong[][] components = nodes.GroupBy(value => Root(value.ScratchKey))
                    .Select(group => group.Select(value => value.ScratchKey)
                        .ToArray()).ToArray();

                int caseBase = fixtureIndex * caseStride;
                UInt4 resultHeader = caseResults[caseBase];
                var nodeIndexByKey = nodes.Select((value, index) =>
                        (Key: value.ScratchKey, Index: index))
                    .ToDictionary(value => value.Key,
                        value => value.Index);
                uint expectedRootMask = 0u;
                var componentGpuClasses = new List<HashSet<string>>();
                var componentOrbitReceipts = new List<uint>();
                foreach (ulong[] keys in components)
                {
                    int[] memberIndices = keys.Select(key => nodeIndexByKey[key])
                        .OrderBy(value => value).ToArray();
                    int root = memberIndices[0];
                    uint memberMask = memberIndices.Aggregate(0u,
                        (mask, value) => mask | (1u << value));
                    expectedRootMask |= 1u << root;
                    UInt4 receipt = caseResults[caseBase +
                        componentReceiptOffset + root];
                    Assert.That(receipt.X, Is.EqualTo((uint)root),
                        $"{name} component root receipt");
                    Assert.That(receipt.Y, Is.EqualTo(memberMask),
                        $"{name} component membership receipt");

                    var classes = new Dictionary<string, int>(
                        StringComparer.Ordinal);
                    uint observedValidMask = 0u;
                    for (int assignment = 0; assignment < assignmentCount;
                         ++assignment)
                    {
                        var cells = new List<SigmaGaugeCell>(memberIndices.Length);
                        bool valid = true;
                        foreach (int node in memberIndices)
                        {
                            UInt4 record = caseResults[caseBase + 1 +
                                assignment * assignmentNodeStride + node];
                            if (record.W == uint.MaxValue)
                            {
                                valid = false;
                                continue;
                            }
                            cells.Add(new SigmaGaugeCell(
                                unchecked((int)record.X),
                                unchecked((int)record.Y),
                                checked((int)record.Z), tags[record.W]));
                        }
                        Assert.That(valid,
                            Is.EqualTo((receipt.Z & (1u << assignment)) != 0u),
                            $"{name} component {root} assignment {assignment}");
                        if (!valid) continue;
                        observedValidMask |= 1u << assignment;
                        string canonical = SigmaGeneratedMerkabaProgram
                            .CanonicalD4GaugeSerialization(cells);
                        if (!classes.ContainsKey(canonical))
                            classes.Add(canonical, assignment);
                    }
                    uint independentlyDerivedOrbitMask = classes.Values.Aggregate(
                        0u, (mask, assignment) => mask | (1u << assignment));
                    Assert.That(receipt.Z, Is.EqualTo(observedValidMask),
                        $"{name} component {root} valid-assignment receipt");
                    Assert.That(receipt.W,
                        Is.EqualTo(independentlyDerivedOrbitMask),
                        $"{name} component {root} D4-orbit receipt; " +
                        string.Join(" | ", Enumerable.Range(0, 3).Select(
                            assignment => $"a{assignment}:" + string.Join(",",
                                memberIndices.Select(node =>
                                {
                                    UInt4 value = caseResults[caseBase + 1 +
                                        assignment * assignmentNodeStride + node];
                                    return $"({unchecked((int)value.X)}," +
                                        $"{unchecked((int)value.Y)})";
                                })))));
                    componentGpuClasses.Add(new HashSet<string>(classes.Keys,
                        StringComparer.Ordinal));
                    componentOrbitReceipts.Add(receipt.W);
                }
                for (int slot = 0; slot < maxNodes; ++slot)
                {
                    UInt4 receipt = caseResults[caseBase +
                        componentReceiptOffset + slot];
                    if ((expectedRootMask & (1u << slot)) == 0u)
                        Assert.That(receipt.X, Is.EqualTo(uint.MaxValue),
                            $"{name} non-root receipt {slot}");
                }
                Assert.That(resultHeader.Y, Is.EqualTo((uint)components.Length),
                    $"{name} component count");
                Assert.That(resultHeader.W, Is.EqualTo(expectedRootMask),
                    $"{name} component partition roots");
                componentOrbitReceiptsByFixture.Add(name,
                    componentOrbitReceipts.ToArray());
                if (string.Equals(name, "unique-neighbour",
                        StringComparison.Ordinal))
                {
                    UInt4 receipt = caseResults[caseBase +
                        componentReceiptOffset];
                    Assert.That(Enumerable.Range(0, assignmentCount).Count(
                            assignment => (receipt.Z &
                                (1u << assignment)) != 0u),
                        Is.EqualTo(assignmentCount),
                        "All 24 abstract-sector chart assignments, including " +
                        "all eight images of every D4 orbit, must be evaluated.");
                }
                SigmaStitchPattern cpuPattern = cpuPatterns[fixtureIndex];
                Assert.That(resultHeader.X,
                    Is.EqualTo((uint)cpuPattern.Resolution),
                    $"{name} GPU set closure must be final authority; " +
                    $"componentRoots=0x{resultHeader.W:x8}");
                if (cpuPattern.Resolution == SigmaStitchResolution.Resolved)
                {
                    Assert.That(componentGpuClasses.All(value => value.Count == 1),
                        Is.True, $"{name} GPU-resolved component orbit count");
                    string cpuCanonical = string.Join("||", components.Select(keys =>
                            SigmaGeneratedMerkabaProgram
                                .CanonicalD4GaugeSerialization(keys.Select(key =>
                                {
                                    string payload = nodes.Single(value =>
                                        value.ScratchKey == key)
                                        .CompletePayloadFingerprint;
                                    SigmaGaugeCell value = cpuPattern.PackedCells
                                        .Single(candidate => candidate
                                            .PayloadFingerprint.StartsWith(
                                                payload + "@",
                                                StringComparison.Ordinal));
                                    return new SigmaGaugeCell(value.U, value.V,
                                        value.Level, payload);
                                })))
                        .OrderBy(value => value, StringComparer.Ordinal));
                    string gpuCanonical = string.Join("||", componentGpuClasses
                        .Select(value => value.Single())
                        .OrderBy(value => value, StringComparer.Ordinal));
                    Assert.That(gpuCanonical, Is.EqualTo(cpuCanonical),
                        $"{name} canonical chart result");
                    canonicalByFixture[name] = cpuCanonical;
                }
            }

            Assert.That(canonicalByFixture["unique-neighbour"], Is.EqualTo(
                canonicalByFixture["reversed-neighbour"]));
            Assert.That(canonicalByFixture["unique-neighbour"], Is.EqualTo(
                canonicalByFixture["sample-side-permutation"]));
            Assert.That(cpuPatterns[fixtures.FindIndex(value => value.Name ==
                "prior-disconnected-components")].ComponentCount,
                Is.EqualTo(2));
            Assert.That(cpuPatterns[fixtures.FindIndex(value => value.Name ==
                "unique-neighbour")].ComponentCount, Is.EqualTo(1),
                "A later exact stitch joins prior components without retaining " +
                "a persistent component identity.");
            Assert.That(cpuPatterns[fixtures.FindIndex(value => value.Name ==
                "chain-edge-order-permutation")].Resolution, Is.EqualTo(
                    cpuPatterns[fixtures.FindIndex(value => value.Name ==
                        "chain-non-gauge-ambiguity")].Resolution));
            Assert.That(cpuPatterns[fixtures.FindIndex(value => value.Name ==
                "chain-non-gauge-ambiguity")].Resolution,
                Is.EqualTo(SigmaStitchResolution.Unresolved));
            Assert.That(cpuPatterns[fixtures.FindIndex(value => value.Name ==
                "occlusion-continuation-open")].Resolution,
                Is.EqualTo(SigmaStitchResolution.Unresolved));
            Assert.That(cpuPatterns[fixtures.FindIndex(value => value.Name ==
                "later-side-view-resolves")].Resolution,
                Is.EqualTo(SigmaStitchResolution.Resolved));
            Assert.That(cpuPatterns[fixtures.FindIndex(value => value.Name ==
                "consistent-native-cycle")].Resolution,
                Is.EqualTo(SigmaStitchResolution.Resolved));
            int disconnectedOrbitFixture = fixtures.FindIndex(value =>
                value.Name == "disconnected-different-chart-orbits");
            uint[] disconnectedOrbitReceipts =
                componentOrbitReceiptsByFixture[
                    "disconnected-different-chart-orbits"];
            Assert.That(cpuPatterns[disconnectedOrbitFixture].Resolution,
                Is.EqualTo(SigmaStitchResolution.Resolved));
            Assert.That(disconnectedOrbitReceipts.Length, Is.EqualTo(2));
            Assert.That(disconnectedOrbitReceipts.Distinct().Count(),
                Is.EqualTo(2),
                "Disconnected components may resolve independently in different " +
                "non-D4 chart orbits.");
            Assert.That(disconnectedOrbitReceipts.All(mask =>
                    Enumerable.Range(0, assignmentCount).Count(bit =>
                        (mask & (1u << bit)) != 0u) == 1),
                Is.True, "Each disconnected component must be uniquely resolved.");
            int joinedOrbitFixture = fixtures.FindIndex(value =>
                value.Name == "later-join-different-chart-orbits");
            Assert.That(cpuPatterns[joinedOrbitFixture].ComponentCount,
                Is.EqualTo(1),
                "The later exact stitch removes independent component gauge.");
            Assert.That(cpuPatterns[joinedOrbitFixture].Resolution,
                Is.EqualTo(SigmaStitchResolution.Unresolved),
                "Incompatible joined chart constraints remain unresolved; they " +
                "are never coordinate-repaired.");
            Assert.That(cpuPatterns[fixtures.FindIndex(value => value.Name ==
                "inconsistent-fundamental-cycle")].Resolution,
                Is.EqualTo(SigmaStitchResolution.Unresolved));
        }

        [Test]
        public void VulkanQueryContractOverflowAndWindowingMatchCpuOracle()
        {
            ComputeShader queryShader = LoadShader("SigmaNativeQueryOracle");
            ComputeShader contractShader = LoadShader(
                "SigmaNativeContractOracle");
            SigmaNativeOracleQuery query = LeftQuery(Point(Raw(3, 1)),
                Point(0), true, false);
            SigmaNativeOracleCell[] cells =
            {
                Cell(0, SigmaS16.Zero, 0, 0),
                Cell(0, State(14, 1), 1, 0),
                new SigmaNativeOracleCell(1, 0, new SigmaGaugeCell(2, 0, 0, "p2"),
                    State(14, 2), resident: false),
                Cell(2, SigmaS16.Zero, 3, 0),
            };
            UInt4[] summaries =
            {
                Summary(0, allDefault: true, boundaryClosed: true),
                Summary(1, allDefault: false, boundaryClosed: true),
                Summary(2, allDefault: false, boundaryClosed: true,
                    nonresident: true),
                Summary(3, allDefault: true, boundaryClosed: false),
            };
            using GraphicsBuffer summaryBuffer = Buffer(summaries);
            using GraphicsBuffer worklist = Buffer<uint>(cells.Length);
            using GraphicsBuffer counters = Buffer(new uint[2]);
            int select = queryShader.FindKernel("SelectNativeQuerySupport");
            queryShader.SetInt("_NativeSummaryCount", summaries.Length);
            queryShader.SetBuffer(select, "_NativeSummaries", summaryBuffer);
            queryShader.SetBuffer(select, "_NativeWorklist", worklist);
            queryShader.SetBuffer(select, "_NativeCounters", counters);
            queryShader.Dispatch(select, 1, 1, 1);
            uint[] countData = Read<uint>(counters, 2);
            Assert.That(countData[0], Is.EqualTo(3u));
            Assert.That(countData[1], Is.EqualTo(1u));
            uint[] selected = Read<uint>(worklist, cells.Length).Take(3)
                .OrderBy(value => value).ToArray();
            CollectionAssert.AreEqual(new uint[] { 1, 2, 3 }, selected);
            worklist.SetData(selected);

            GpuShadow[] decompositions = new[] { 1, 2, 7 }.Select(windows =>
                RunGpuQuery(queryShader, cells, selected, query, windows,
                    reverse: false)).ToArray();
            GpuShadow reversed = RunGpuQuery(queryShader, cells, selected, query,
                7, reverse: true);
            foreach (GpuShadow actual in decompositions.Skip(1))
                AssertGpuShadowEqual(decompositions[0], actual);
            AssertGpuShadowEqual(decompositions[0], reversed);
            CollectionAssert.AreEqual(new ulong[] { 0 },
                decompositions[0].FirstSupports);
            CollectionAssert.AreEqual(new ulong[] { 1 },
                decompositions[0].BehindSupports);
            Assert.That(decompositions[0].Order,
                Is.EqualTo(Point(Raw(3, 2))));

            SigmaNativeSceneShadow cpu = SigmaMerkabaSemanticOracle
                .EvaluateAndReduce(cells, selected.Select(value => (int)value), query);
            AssertGpuShadowMatchesCpu(decompositions[0], cpu);

            SigmaNativeOracleQuery opticalQuery = BuildPhotometricQuery(
                "SENSOR_LEFT", State(14, 2), PiecewiseLaw());
            RunGpuContractOverflow(queryShader, contractShader, opticalQuery,
                expectedViable: new[] { 0, 2, 4 }, behindOrdinal: 1,
                requireDistinctOptical: true);

            SigmaS16 supportA = State(4, 1);
            SigmaS16 supportB = State(8, 1);
            SigmaNativePreimageCandidate[] stereoCandidates =
            {
                Candidate(0, 2, supportA, new SigmaNativeDeltaWitness(2, 0, supportA)),
                Candidate(1, 3, supportB, new SigmaNativeDeltaWitness(3, 0, supportB)),
                Candidate(2, 4, supportA, new SigmaNativeDeltaWitness(4, 0, supportA)),
                Candidate(3, 5, supportB, new SigmaNativeDeltaWitness(5, 0, supportB)),
                Candidate(4, 6, supportA, new SigmaNativeDeltaWitness(6, 0, supportA)),
                Candidate(5, 7, supportB, new SigmaNativeDeltaWitness(7, 0, supportB)),
            };
            SigmaNativeOracleQuery leftStereo = LeftQuery(Point(Raw(1, 2)),
                Point(Raw(1, 2)), true, true);
            SigmaNativeOracleQuery rightStereo = RightQuery(Point(Raw(1, 2)),
                Point(Raw(-3, 2)), true, true);
            int[] leftViable = RunGpuContractOverflow(queryShader, contractShader,
                leftStereo, stereoCandidates, behindOrdinal: -1);
            int[] rightViable = RunGpuContractOverflow(queryShader, contractShader,
                rightStereo, stereoCandidates, behindOrdinal: -1);
            int[] jointGpu = leftViable.Intersect(rightViable).OrderBy(value => value)
                .ToArray();
            int[] jointCpu = SigmaMerkabaSemanticOracle.ContractJoint(
                    new[] { leftStereo, rightStereo }, stereoCandidates)
                .Branches.Select(value => value.CandidateOrdinal).ToArray();
            CollectionAssert.AreEqual(jointCpu, jointGpu,
                "The right generated entry point must eliminate the left-only " +
                "preimages without collapsing coherent query directions.");
        }

        [Test]
        public void VulkanFieldEntrypointsGroupRefinedU64SupportsBeforeReduction()
        {
            ComputeShader queryShader = LoadShader("SigmaNativeQueryOracle");
            const ulong refinedSupport = 1UL;
            const ulong formerlyAliasedSupport = 33UL;
            const ulong highSupport = (1UL << 40) | 1UL;
            SigmaGaugeCell parent = new(0, 0, 0, "refined-u64-support");
            var cells = SigmaGeneratedMerkabaProgram.SplitGaugeCell(parent)
                .Select(gauge => new SigmaNativeOracleCell(refinedSupport, 0,
                    gauge, State(14, 1))).ToList();
            cells.Add(new SigmaNativeOracleCell(formerlyAliasedSupport, 0,
                new SigmaGaugeCell(8, 0, 0, "support-33"), State(14, 2)));
            cells.Add(new SigmaNativeOracleCell(highSupport, 0,
                new SigmaGaugeCell(9, 0, 0, "support-high"), State(14, 1)));
            uint[] selected = Enumerable.Range(0, cells.Count)
                .Select(value => (uint)value).ToArray();
            string[] fieldEntries =
            {
                "SENSOR_LEFT", "SENSOR_RIGHT", "PREDICTION_SUPPORT",
                "EXPORT", "DEBUG",
            };
            foreach (string entry in fieldEntries)
            {
                SigmaNativeOracleQuery query = Query(entry, 0, Axis(0), Axis(1),
                    Point(0), Point(0), Point(SigmaNumericDomain.One),
                    orderEvidence: false, opticalEvidence: false, Law(true, 1, 1));
                GpuShadow gpu = RunGpuQuery(queryShader, cells, selected, query,
                    1, reverse: false);
                SigmaNativeSceneShadow cpu = SigmaMerkabaSemanticOracle
                    .EvaluateAndReduce(cells, Enumerable.Range(0, cells.Count), query);
                AssertGpuShadowMatchesCpu(gpu, cpu);
                Assert.That(gpu.FirstSupports.Distinct().Count(),
                    Is.EqualTo(gpu.FirstSupports.Length),
                    $"{entry} must group a refined support before reduction.");
                IEnumerable<ulong> representedSupports = entry == "DEBUG"
                    ? gpu.RelationSupports
                    : gpu.FirstSupports.Concat(gpu.BehindSupports);
                Assert.That(representedSupports,
                    Does.Contain(refinedSupport));
                Assert.That(representedSupports,
                    Does.Contain(formerlyAliasedSupport));
                Assert.That(representedSupports,
                    Does.Contain(highSupport),
                    $"{entry} may not truncate or alias a 64-bit support key.");
            }
        }

        [Test]
        public void VulkanBoundedQueryMatrixMatchesCpuAcrossRepresentationAndOrder()
        {
            ComputeShader queryShader = LoadShader("SigmaNativeQueryOracle");
            string[] entries =
            {
                "SENSOR_LEFT", "SENSOR_RIGHT", "PREDICTION_SUPPORT",
                "EXPORT", "DEBUG",
            };
            int caseOrdinal = 0;
            foreach (string entry in entries)
                for (int stateMask = 1; stateMask < 16; ++stateMask)
                {
                    bool refined = (stateMask & 8) != 0;
                    var cells = new List<SigmaNativeOracleCell>();
                    SigmaS16 firstState = (stateMask & 1) != 0
                        ? State(14, 1) : SigmaS16.Zero;
                    if (refined)
                    {
                        SigmaGaugeCell parent = new(0, 0, 0,
                            $"matrix-{caseOrdinal}-refined");
                        cells.AddRange(SigmaGeneratedMerkabaProgram
                            .SplitGaugeCell(parent).Select(gauge =>
                                new SigmaNativeOracleCell(1UL, 0, gauge,
                                    firstState, resident: (stateMask & 4) == 0)));
                    }
                    else
                    {
                        cells.Add(new SigmaNativeOracleCell(1UL, 0,
                            new SigmaGaugeCell(0, 0, 0,
                                $"matrix-{caseOrdinal}-uniform"), firstState,
                            resident: (stateMask & 4) == 0));
                    }
                    cells.Add(new SigmaNativeOracleCell(33UL, 0,
                        new SigmaGaugeCell(8, 0, 0, "matrix-33"),
                        (stateMask & 2) != 0 ? State(14, 2) : SigmaS16.Zero,
                        resident: (stateMask & 1) != 0));
                    cells.Add(new SigmaNativeOracleCell((1UL << 40) | 1UL, 0,
                        new SigmaGaugeCell(9, 0, 0, "matrix-high"),
                        (stateMask & 4) != 0 ? State(14, 1) : SigmaS16.Zero,
                        resident: false));
                    if (cells.All(value => value.State.IsZero))
                        cells[0] = new SigmaNativeOracleCell(cells[0].SupportKey,
                            0, cells[0].Gauge, State(4, 1), cells[0].Resident);

                    SigmaNativeOracleQuery query = Query(entry, 0, Axis(0), Axis(1),
                        Point(0), Point(0), Point(SigmaNumericDomain.One),
                        orderEvidence: false, opticalEvidence: false,
                        Law(true, 1, 1));
                    uint[] selected = Enumerable.Range(0, cells.Count)
                        .Select(value => (uint)value).ToArray();
                    int windows = new[] { 1, 2, 7 }[caseOrdinal % 3];
                    bool reverse = (caseOrdinal & 1) != 0;
                    GpuShadow gpu = RunGpuQuery(queryShader, cells, selected,
                        query, windows, reverse,
                        $"entry={entry}, mask={stateMask}, ordinal={caseOrdinal}, " +
                        $"windows={windows}, reverse={reverse}");
                    SigmaNativeSceneShadow cpu = SigmaMerkabaSemanticOracle
                        .EvaluateAndReduce(cells, Enumerable.Range(0, cells.Count),
                            query);
                    AssertGpuShadowMatchesCpu(gpu, cpu);
                    ++caseOrdinal;
                }
            Assert.That(caseOrdinal, Is.EqualTo(75),
                "The bounded CPU/Vulkan query corpus must exercise every field " +
                "single-query entry point across all nonempty four-bit world masks; " +
                "EYE_PAIR has its own two-query arity corpus.");
        }

        [Test]
        public void VulkanExportDebugAndEyePairExecuteDistinctGeneratedSemantics()
        {
            ComputeShader shader = LoadShader("SigmaNativeQueryOracle");
            SigmaS16 regularState = State(14, 1);
            SigmaS16 rejectedState = State(14, 2);
            SigmaNativeOracleCell[] cells =
            {
                new(1UL, 0, new SigmaGaugeCell(0, 0, 0, "export-regular"),
                    regularState, queryRelation: RelationFor(regularState,
                        valid: true)),
                new(2UL, 0, new SigmaGaugeCell(1, 0, 0, "export-rejected"),
                    rejectedState, queryRelation: RelationFor(rejectedState,
                        valid: false)),
                new(3UL, 0, new SigmaGaugeCell(2, 0, 0, "export-regular-2"),
                    regularState, queryRelation: RelationFor(regularState,
                        valid: true)),
            };
            uint[] selected = { 0u, 1u, 2u };
            SigmaNativeOracleQuery export = Query("EXPORT", 0, Axis(0), Axis(1),
                Point(0), Point(0), Point(SigmaNumericDomain.One), false, false,
                Law(true, 1, 1));
            SigmaNativeSceneShadow exportCpu = SigmaMerkabaSemanticOracle
                .EvaluateAndReduce(cells, Enumerable.Range(0, cells.Length), export);
            GpuShadow exportGpu = RunGpuQuery(shader, cells, selected, export, 1,
                reverse: false);
            AssertGpuShadowMatchesCpu(exportGpu, exportCpu);
            CollectionAssert.AreEquivalent(new ulong[] { 1, 2, 3 },
                exportGpu.FirstSupports,
                "Export keeps manifestation while relation-gating connectivity.");
            Assert.That(exportGpu.RelationSupports, Does.Contain(1UL));
            Assert.That(exportGpu.RelationSupports, Does.Contain(3UL));
            Assert.That(exportGpu.RelationSupports, Has.No.Member(2UL),
                "A generated NO_RELATION support may not enter export connectivity.");

            SigmaNativeOracleQuery debug = Query("DEBUG", 0, Axis(0), Axis(1),
                Point(0), Point(0), Point(SigmaNumericDomain.One), false, false,
                Law(true, 1, 1));
            SigmaNativeSceneShadow debugCpu = SigmaMerkabaSemanticOracle
                .EvaluateAndReduce(cells, Enumerable.Range(0, cells.Length), debug);
            GpuShadow debugGpu = RunGpuQuery(shader, cells, selected, debug, 1,
                reverse: false);
            AssertGpuShadowMatchesCpu(debugGpu, debugCpu);
            Assert.That(debugGpu.FirstSupports, Is.Empty,
                "Reducer NONE may not impersonate an all-support scene hull.");
            CollectionAssert.AreEqual(new ulong[] { 1, 2, 3 },
                debugGpu.RelationSupports);
            Assert.That(debugGpu.RelationClasses[1],
                Is.EqualTo(SigmaMerkabaSemanticOracle.EvaluateNativeRelation(
                    cells[1].QueryRelation).RelationClass));
            Assert.That(debugGpu.Order.IsEmpty, Is.True);
            Assert.That(debugGpu.Optical.All(value => value.IsEmpty), Is.True);

            SigmaNativeOracleCell[] eyeCells =
            {
                new(11UL, 0, new SigmaGaugeCell(0, 0, 0, "eye-left"),
                    State(1, 1)),
                new(22UL, 0, new SigmaGaugeCell(1, 0, 0, "eye-right"),
                    State(2, 1)),
            };
            SigmaNativeOracleQuery left = Query("SENSOR_LEFT", 0, Axis(0),
                Axis(2), Point(0), Point(0), Point(SigmaNumericDomain.One),
                false, false, Law(true, 1, 1));
            SigmaNativeOracleQuery right = Query("SENSOR_RIGHT", 0, Axis(1),
                Axis(3), Point(0), Point(0), Point(SigmaNumericDomain.One),
                false, false, Law(true, 1, 1));
            var pairContext = new SigmaNativeCoherentQueryContext(77,
                GaugeFingerprint);
            var pair = new SigmaNativeEyePairQuery(left, right, pairContext);
            Assert.That(pair.CoherentContext.ObservationRevision, Is.EqualTo(77));
            Assert.That(pair.CoherentContext.PoseCalibrationFingerprint,
                Is.EqualTo(GaugeFingerprint));
            SigmaNativeEyePairShadow pairCpu = SigmaMerkabaSemanticOracle
                .EvaluateAndReduceEyePair(eyeCells, new[] { 0, 1 }, pair);
            GpuShadow[] pairGpu = RunGpuEyePair(shader, eyeCells,
                new uint[] { 0u, 1u }, pair, 1, reverse: false);
            AssertGpuShadowMatchesCpu(pairGpu[0], pairCpu.Left);
            AssertGpuShadowMatchesCpu(pairGpu[1], pairCpu.Right);
            Assert.That(pairGpu[0].FirstSupports,
                Is.Not.EqualTo(pairGpu[1].FirstSupports),
                "One EYE_PAIR dispatch must retain two distinct retinal queries.");
        }

        [Test]
        public void VulkanReducerHandlesMoreThan64AndSignalsBoundedColdContinuation()
        {
            ComputeShader shader = LoadShader("SigmaNativeQueryOracle");
            SigmaNativeOracleQuery query = LeftQuery(Point(0), Point(0),
                orderEvidence: false, opticalEvidence: false);
            SigmaNativeOracleCell[] sameSupport = Enumerable.Range(0, 128)
                .Select(index => new SigmaNativeOracleCell(7UL, 0,
                    new SigmaGaugeCell(index, 0, 4, $"same-{index}"),
                    State(14, 1))).ToArray();
            uint[] selected128 = Enumerable.Range(0, 128).Select(value =>
                (uint)value).ToArray();
            GpuShadow sameGpu = RunGpuQuery(shader, sameSupport, selected128,
                query, 1, reverse: false,
                caseContext: "128 refined contributions on one support");
            SigmaNativeSceneShadow sameCpu = SigmaMerkabaSemanticOracle
                .EvaluateAndReduce(sameSupport, Enumerable.Range(0, 128), query);
            AssertGpuShadowMatchesCpu(sameGpu, sameCpu);
            CollectionAssert.AreEqual(new ulong[] { 7UL }, sameGpu.FirstSupports);
            Assert.That(sameGpu.ActiveContributions, Is.EqualTo(128u));

            SigmaNativeOracleCell[] mixed = Enumerable.Range(0, 96)
                .Select(index => new SigmaNativeOracleCell((ulong)index + 1UL, 0,
                    new SigmaGaugeCell(index, 1, 0, $"mixed-{index}"),
                    State(14, index % 3 + 1))).ToArray();
            uint[] selected96 = Enumerable.Range(0, 96).Select(value =>
                (uint)value).ToArray();
            GpuShadow mixedGpu = RunGpuQuery(shader, mixed, selected96, query, 1,
                reverse: true);
            SigmaNativeSceneShadow mixedCpu = SigmaMerkabaSemanticOracle
                .EvaluateAndReduce(mixed, Enumerable.Range(0, 96), query);
            AssertGpuShadowMatchesCpu(mixedGpu, mixedCpu);
            Assert.That(mixedGpu.FirstSupports.Concat(mixedGpu.BehindSupports)
                .Distinct().Count(), Is.EqualTo(96));

            SigmaNativeOracleCell[] overBound = Enumerable.Range(0, 129)
                .Select(index => new SigmaNativeOracleCell((ulong)index + 1UL, 0,
                    new SigmaGaugeCell(index, 2, 0, $"cold-{index}"),
                    State(14, 1))).ToArray();
            GpuShadow cold = RunGpuQuery(shader, overBound,
                Enumerable.Range(0, overBound.Length).Select(value => (uint)value)
                    .ToArray(), query, 1, reverse: false,
                caseContext: "129-contribution explicit cold continuation",
                allowColdContinuation: true);
            Assert.That(cold.RequiresColdContinuation, Is.True);
            Assert.That(cold.FirstSupports, Is.Empty,
                "A bounded reducer must fail closed, never silently truncate.");
        }

        [Test]
        public void VulkanRelationWorklistMatchesBoundedCartesianCpuCorpus()
        {
            ComputeShader queryShader = LoadShader("SigmaNativeQuery");
            SigmaS16[] states =
            {
                SigmaS16.Zero,
                State(0, 1),
                State(1, 1),
                State(4, 1),
                SigmaS16Operators.ZeroDivisorDonorDyad.ToS16(),
            };
            var relations = new List<SigmaNativeRelationInput>();
            for (int left = 0; left < states.Length; ++left)
                for (int right = 0; right < states.Length; ++right)
                    for (int context = 0; context < states.Length; ++context)
                    {
                        relations.Add(new SigmaNativeRelationInput(states[left],
                            states[right], states[context],
                            (left + right) & 15, (right + context) & 15,
                            (left * 3 + right) & 15,
                            (right * 5 + context) & 15,
                            (context * 7 + left) & 15));
                    }

            SigmaNativeRelationInput regular = new(State(0, 1), State(0, 1),
                SigmaS16.Zero, 0, 0, 0, 0, 0);
            int regularIndex = relations.Count;
            SigmaNativeRelationWitness regularWitness = SigmaMerkabaSemanticOracle
                .EvaluateNativeRelation(regular);
            Assert.That(regularWitness.MinimumAnnihilatorResidual,
                Is.GreaterThan(BigInteger.Zero));
            Assert.That(regularWitness.MinimumAnnihilatorResidual,
                Is.LessThanOrEqualTo(new BigInteger(long.MaxValue)));
            long calibratedResidual = (long)regularWitness
                .MinimumAnnihilatorResidual;
            SigmaQ48Interval nearInterval = Point(calibratedResidual);
            SigmaNativeNearSingularLaw nearLaw = new(nearInterval,
                SigmaNativeNearSingularLaw.ComputeFingerprint(nearInterval));
            relations.Add(regular);
            int nearIndex = relations.Count;
            relations.Add(new SigmaNativeRelationInput(regular.Left, regular.Right,
                regular.Context, regular.TransportGenerator,
                regular.TransportAddress, regular.PlaquetteA, regular.PlaquetteC,
                regular.PlaquetteBase, nearLaw));

            // Scanner states are arbitrary Q16.48 values, not a small integral
            // Cayley-Dickson lattice. These cases prevent the Vulkan relation
            // lowering from silently degrading every fractional live state to
            // UNRESOLVED merely because a coefficient is not an integer in a
            // convenient toy range.
            SigmaS16 fractional = StateRaw(
                (1, Raw(1, 2)),
                (4, Raw(-3, 8)),
                (9, Raw(5, 16)),
                (15, Raw(-7, 32)));
            SigmaS16 fractionalContext = StateRaw(
                (2, Raw(3, 8)),
                (5, Raw(-1, 4)),
                (11, Raw(9, 16)));
            SigmaS16 largeValid = StateRaw(
                (3, SigmaNumericDomain.FromInteger(30_000)),
                (12, Raw(31, 64)));
            relations.Add(new SigmaNativeRelationInput(SigmaS16.Zero,
                fractional, SigmaS16.Zero, 3, 7, 1, 6, 9));
            relations.Add(new SigmaNativeRelationInput(SigmaS16.Zero,
                largeValid, SigmaS16.Zero, 12, 5, 2, 9, 4));
            relations.Add(new SigmaNativeRelationInput(
                StateRaw((1, Raw(1, 2)), (6, Raw(-5, 16))),
                StateRaw((2, Raw(3, 8)), (10, Raw(7, 32))),
                fractionalContext, 5, 11, 3, 13, 7));

            SigmaNativeRelationWitness[] cpu = relations.Select(
                SigmaMerkabaSemanticOracle.EvaluateNativeRelation).ToArray();
            Assert.That(cpu.Select(value => value.RelationClass),
                Does.Contain(SigmaMerkabaRelationClass.DefaultSat));
            Assert.That(cpu[regularIndex].RelationClass,
                Is.EqualTo(SigmaMerkabaRelationClass.Regular));
            Assert.That(cpu[nearIndex].RelationClass,
                Is.EqualTo(SigmaMerkabaRelationClass.NearSingularQ48));
            Assert.That(cpu.Any(value => !value.Transition.IsZero &&
                value.ExactAnnihilatorAction >= 0), Is.True,
                "Bounded corpus must exercise an exact zero-divisor action.");
            AssertGpuRelationsMatchCpu(queryShader, relations, cpu);
        }

        private static GpuFreshAdmission RunGpuFreshAdmission(
            SigmaNativeFreshObservationBranch[] branches,
            SigmaS16? priorState = null)
        {
            Assert.That(branches, Is.Not.Null.And.Not.Empty);
            int branchCount = branches.Length;
            int admissionStateOffset = branchCount * SigmaS16.LaneCount;
            int zeroStateOffset = (branchCount + 1) * SigmaS16.LaneCount;
            UInt4[] freshHeaders = branches.SelectMany((branch, index) =>
            {
                uint flags = 1u |
                    (branch.LeftFirstHit ? 2u : 0u) |
                    (branch.RightFirstHit ? 4u : 0u) |
                    (branch.LeftEvidence ? 8u : 0u) |
                    (branch.RightEvidence ? 16u : 0u) |
                    (priorState.HasValue ? 64u : 0u);
                ulong revision = branch.CoherentContext.ObservationRevision;
                uint provenance = Convert.ToUInt32(
                    branch.ProvenanceFingerprint.Substring(56, 8), 16);
                ulong epoch = branch.InstrumentLeft.CalibrationEpoch;
                uint foldedEpoch = (uint)epoch ^ (uint)(epoch >> 32);
                uint poseProvenance = Convert.ToUInt32(
                    branch.InstrumentLeft.PoseCalibrationFingerprint
                        .Substring(56, 8), 16);
                return new[]
                {
                    new UInt4
                    {
                        X = flags,
                        Y = (uint)revision,
                        Z = (uint)(revision >> 32),
                        W = provenance == 0u ? (uint)index + 1u : provenance,
                    },
                    new UInt4
                    {
                        X = (uint)branch.InstrumentLeft.OpticalTransfer,
                        Y = (uint)branch.InstrumentRight.OpticalTransfer,
                        Z = foldedEpoch == 0u ? 1u : foldedEpoch,
                        W = poseProvenance == 0u ? 1u : poseProvenance,
                    },
                };
            }).ToArray();
            UInt2[] freshRays = branches.SelectMany(branch =>
                    branch.InstrumentLeft.Footprint.Ray.Concat(
                        branch.InstrumentRight.Footprint.Ray).Select(Pack))
                .ToArray();
            UInt2[] freshCodes = branches.SelectMany(branch =>
                    PackInstrumentCodes(branch.InstrumentLeft).Concat(
                        PackInstrumentCodes(branch.InstrumentRight)))
                .ToArray();
            var freshEvidence = new UInt2[branchCount *
                SigmaGeneratedFrame.CompletionWordCount];
            for (int branch = 0; branch < branchCount; ++branch)
            {
                int record = branch * SigmaGeneratedFrame.CompletionWordCount;
                for (int headerIndex = 0; headerIndex < 2; ++headerIndex)
                {
                    UInt4 value = freshHeaders[branch * 2 + headerIndex];
                    int word = record +
                        SigmaGeneratedFrame.CompletionObservationHeaders +
                        headerIndex * 2;
                    freshEvidence[word] = new UInt2 { X = value.X, Y = value.Y };
                    freshEvidence[word + 1] = new UInt2
                        { X = value.Z, Y = value.W };
                }
                Array.Copy(freshRays, branch * 6, freshEvidence,
                    record + SigmaGeneratedFrame.CompletionRoomRays, 6);
                Array.Copy(freshCodes, branch * 16, freshEvidence,
                    record + SigmaGeneratedFrame.CompletionCodeLeaves, 16);
            }

            var zeroStates = Enumerable.Repeat(SigmaS16.Zero,
                branchCount + 3).ToArray();
            if (priorState.HasValue)
                zeroStates[branchCount + 2] = priorState.Value;
            UInt4[] relationInputs = Enumerable.Range(0, branchCount)
                .Select(index => new UInt4
                {
                    X = (uint)(index * SigmaS16.LaneCount),
                    Y = 0u,
                }).ToArray();
            UInt4[] relationPlans = Enumerable.Range(0, branchCount)
                .Select(_ => new UInt4
                {
                    X = (uint)zeroStateOffset,
                    Y = (uint)zeroStateOffset,
                    Z = 0u,
                    W = 0u,
                }).ToArray();
            UInt4[] nearIntervals = Enumerable.Repeat(
                PackInterval(SigmaQ48Interval.Empty), branchCount).ToArray();

            ComputeShader queryShader = LoadShader("SigmaNativeQuery");
            ComputeShader contractShader = LoadShader("SigmaNativeContract");
            UnityEngine.Rendering.LocalKeyword boundaryVariant = new(queryShader,
                "SIGMA_N4_BOUNDARY_VARIANT");
            UnityEngine.Rendering.LocalKeyword globalCloseVariant = new(queryShader,
                "SIGMA_N4_GLOBAL_CLOSE_VARIANT");
            queryShader.SetKeyword(boundaryVariant, false);
            queryShader.SetKeyword(globalCloseVariant, false);
            UnityEngine.Rendering.LocalKeyword tileCloseVariant = new(contractShader,
                "SIGMA_N4_TILE_CLOSE_VARIANT");
            contractShader.SetKeyword(tileCloseVariant, false);
            int contract = contractShader.FindKernel("ContractNativeQuery");
            int relation = queryShader.FindKernel("EvaluateNativeRelation");

            using GraphicsBuffer stateBuffer = Buffer(PackStates(zeroStates));
            using GraphicsBuffer freshEvidenceBuffer = Buffer(freshEvidence);
            using GraphicsBuffer relationInputBuffer = Buffer(relationInputs);
            using GraphicsBuffer relationPlanBuffer = Buffer(relationPlans);
            using GraphicsBuffer nearBuffer = Buffer(nearIntervals);
            using GraphicsBuffer relationResults = Buffer<UInt4>(branchCount);
            using GraphicsBuffer relationFactors = Buffer<UInt4>(branchCount);
            using GraphicsBuffer relationHashes = Buffer<UInt4>(branchCount);
            using GraphicsBuffer relationNorms = Buffer<UInt4>(branchCount * 4);
            using GraphicsBuffer outputHeaders = Buffer<UInt4>(branchCount + 1);
            using GraphicsBuffer outputSupports = Buffer<UInt2>(branchCount + 1);
            using GraphicsBuffer outputPredictions = Buffer<UInt4>(
                Math.Max(4, branchCount * 4));
            using GraphicsBuffer counters = Buffer(new UInt4[4]);
            using GraphicsBuffer observations = new(
                GraphicsBuffer.Target.Structured, 1,
                SigmaGeneratedFrame.NativeObservationStride);
            // Fresh all-ZEmpty admission has no prior locality certificate.
            // GraphicsBuffer contents are undefined until initialized; an
            // uninitialized valid bit would invent a prior constraint and make
            // signed-eye parity allocation-order dependent.
            using GraphicsBuffer certificateWords = Buffer(new UInt4[80]);

            contractShader.SetBuffer(contract, "_NativeReverseRelationResults",
                relationResults);
            contractShader.SetBuffer(contract, "_NativeStates", stateBuffer);
            contractShader.SetBuffer(contract, "_NativeFreshEvidenceWords",
                freshEvidenceBuffer);
            contractShader.SetBuffer(contract, "_NativeObservations",
                observations);
            contractShader.SetBuffer(contract, "_NativeSourceCarrierState",
                stateBuffer);
            contractShader.SetBuffer(contract,
                "_NativeSourceCarrierRepresentation", certificateWords);
            contractShader.SetBuffer(contract, "_NativeCloseScratch",
                freshEvidenceBuffer);
            contractShader.SetBuffer(contract, "_NativeBranchHeaders",
                outputHeaders);
            contractShader.SetBuffer(contract, "_NativeBranchSupports",
                outputSupports);
            contractShader.SetBuffer(contract, "_NativeBranchPredictions",
                outputPredictions);
            contractShader.SetBuffer(contract, "_NativeCounters", counters);
            contractShader.SetBuffer(contract,
                "_NativeLocalityCertificateWords", certificateWords);
            contractShader.SetInt("_NativeFreshBranchCount", branchCount);
            contractShader.SetInt("_NativeFreshLeftEntryPointIndex",
                EntryPointIndex("SENSOR_LEFT"));
            contractShader.SetInt("_NativeFreshRightEntryPointIndex",
                EntryPointIndex("SENSOR_RIGHT"));
            contractShader.SetInt("_NativeContractMode", 1);
            contractShader.SetInt("_NativeFreshPriorStateOffset",
                (branchCount + 2) * SigmaS16.LaneCount);
            contractShader.SetInt("_NativeCompletionRecordIndex", 0);
            contractShader.SetInt("_NativeFootprintCount", 0);

            // One workgroup per reverse branch. The first 64 of the fixed 256
            // threads map eight raw
            // leaves, four axes and sixteen S16 lanes; branch count changes the
            // dispatch grid, never the command sequence.
            contractShader.Dispatch(contract, branchCount, 1, 1);

            queryShader.SetBuffer(relation, "_NativeStates", stateBuffer);
            queryShader.SetBuffer(relation, "_NativeRelationInputs",
                relationInputBuffer);
            queryShader.SetBuffer(relation, "_NativeRelationPlans",
                relationPlanBuffer);
            queryShader.SetBuffer(relation, "_NativeRelationNearIntervals",
                nearBuffer);
            queryShader.SetBuffer(relation, "_NativeRelationResults",
                relationResults);
            queryShader.SetBuffer(relation, "_NativeRelationFactors",
                relationFactors);
            queryShader.SetBuffer(relation, "_NativeRelationHashes",
                relationHashes);
            queryShader.SetBuffer(relation, "_NativeRelationNorms", relationNorms);
            queryShader.SetBuffer(relation, "_NativeObservations", observations);
            queryShader.SetBuffer(relation, "_NativeCloseScratch",
                freshEvidenceBuffer);
            queryShader.SetInt("_NativeEntryPointIndex", Array.FindIndex(
                SigmaGeneratedMerkabaProgram.EntryPoints,
                value => value.Id == "INTRINSIC_RELATION"));
            queryShader.SetInt("_NativeRelationCount", branchCount);
            // Existing 256-thread hyperdimensional relation workgroups derive
            // the boundary witness; no host truth or per-relation dispatch exists.
            queryShader.Dispatch(relation, branchCount, 1, 1);

            contractShader.SetInt("_NativeContractMode", 2);
            // One bounded collective compares all complete branch states and
            // emits a common relative support result or UNRESOLVED.
            contractShader.Dispatch(contract, 1, 1, 1);

            UInt4 header = Read<UInt4>(outputHeaders, branchCount + 1)[branchCount];
            UInt2[] packedStates = Read<UInt2>(stateBuffer,
                (branchCount + 2) * SigmaS16.LaneCount);
            var lanes = new long[SigmaS16.LaneCount];
            for (int lane = 0; lane < lanes.Length; ++lane)
                lanes[lane] = Unpack(packedStates[admissionStateOffset + lane]);
            UInt2 support = Read<UInt2>(outputSupports,
                branchCount + 1)[branchCount];
            return new GpuFreshAdmission(header.X, SigmaS16.FromArray(lanes),
                (SigmaMerkabaRelationClass)header.Y,
                unchecked((long)(ulong)support.X),
                unchecked((long)(ulong)support.Y), header.Z, header.W);
        }

        private static void AssertFreshGpuMatchesCpu(GpuFreshAdmission gpu,
            SigmaFreshBaseAdmission cpu, int expectedBranchCount)
        {
            Assert.That(gpu.Admitted, Is.True,
                $"status={gpu.Status}, relation={gpu.Relation}, " +
                $"branches={gpu.BranchCount}, cold={gpu.ColdReason}, " +
                $"stateZero={gpu.State.IsZero}");
            Assert.That(gpu.Status,
                Is.EqualTo((uint)SigmaFreshAdmissionStatus.Admitted));
            Assert.That(gpu.State, Is.EqualTo(cpu.State));
            Assert.That(gpu.Relation, Is.EqualTo(cpu.BoundaryRelation));
            Assert.That(gpu.SupportU, Is.Zero);
            Assert.That(gpu.SupportV, Is.Zero);
            Assert.That(gpu.BranchCount, Is.EqualTo((uint)expectedBranchCount));
            Assert.That(gpu.ColdReason, Is.Zero,
                "Bounded one-to-four branch proof must remain on the fixed hot graph.");
        }

        private static int[] RunGpuContractOverflow(ComputeShader queryShader,
            ComputeShader contractShader, SigmaNativeOracleQuery query,
            SigmaNativePreimageCandidate[] cpuCandidates = null,
            int[] expectedViable = null, int behindOrdinal = -1,
            bool requireDistinctOptical = false)
        {
            bool canonicalRoleCorpus = cpuCandidates == null;
            SigmaS16 zero = SigmaS16.Zero;
            SigmaS16 near = State(14, 1);
            SigmaS16 mould = State(14, 2);
            SigmaS16 behind = State(14, 3);
            cpuCandidates ??= new[]
            {
                CpuCandidate(0, near, mould, true, true),
                CpuCandidate(1, behind, mould, true, true),
                CpuCandidate(2, zero, mould, true, true),
                CpuCandidate(3, near, mould, false, true),
                CpuCandidate(4, near, mould, true, true),
                CpuCandidate(5, near, near, true, true),
            };
            int candidateCount = cpuCandidates.Length;
            var stateValues = new List<SigmaS16>(candidateCount * 4);
            var candidateKeys = new UInt4[candidateCount];
            var candidateStates = new UInt4[candidateCount];
            var gaugeCoordinates = new UInt4[candidateCount * 2];
            var gaugeMetadata = new UInt4[candidateCount * 2];
            var relationPlans = new UInt4[candidateCount];
            var relationInputs = new UInt4[candidateCount];
            var nearIntervals = new UInt4[candidateCount];
            var payloadIds = new Dictionary<string, ulong>(StringComparer.Ordinal);
            foreach (SigmaNativePreimageCandidate candidate in cpuCandidates)
            {
                int index = candidate.CandidateOrdinal;
                uint baseOffset = (uint)(stateValues.Count * 16);
                stateValues.Add(candidate.Prior.State);
                stateValues.Add(candidate.Proposed.State);
                stateValues.Add(candidate.Relation.Right);
                stateValues.Add(candidate.Relation.Context);
                candidateKeys[index] = PackKeys(candidate.Prior.SupportKey,
                    candidate.Proposed.SupportKey);
                candidateStates[index] = new UInt4
                {
                    X = baseOffset,
                    Y = baseOffset + 16u,
                    Z = baseOffset + 32u,
                    W = baseOffset + 48u,
                };
                gaugeCoordinates[index * 2] = PackGaugeCoordinate(
                    candidate.Prior.Gauge);
                gaugeCoordinates[index * 2 + 1] = PackGaugeCoordinate(
                    candidate.Proposed.Gauge);
                gaugeMetadata[index * 2] = PackGaugeMetadata(candidate.Prior.Gauge,
                    payloadIds);
                gaugeMetadata[index * 2 + 1] = PackGaugeMetadata(
                    candidate.Proposed.Gauge, payloadIds);
                UInt4 relationPlan = PackRelationPlan(candidate.Relation);
                relationPlan.X = baseOffset + 32u;
                relationPlan.Y = baseOffset + 48u;
                relationPlans[index] = relationPlan;
                nearIntervals[index] = PackInterval(candidate.Relation.NearLaw
                    .ResidualMagnitude);
                relationInputs[index] = new UInt4
                {
                    X = baseOffset + 16u,
                    Y = candidate.Relation.NearLaw.IsCalibrated ? 1u : 0u,
                };
            }
            uint[] hashes = Enumerable.Range(0, candidateCount)
                .Select(value => (uint)(100 + value)).ToArray();
            PackPhotometricLaw(query, out UInt2[] exposure,
                out UInt2[] channelParameters, out UInt2[] transferRanges,
                out UInt2[] transferData);
            using GraphicsBuffer keyBuffer = Buffer(candidateKeys);
            using GraphicsBuffer candidateStateBuffer = Buffer(candidateStates);
            using GraphicsBuffer gaugeCoordinateBuffer = Buffer(gaugeCoordinates);
            using GraphicsBuffer gaugeMetadataBuffer = Buffer(gaugeMetadata);
            using GraphicsBuffer relationPlanBuffer = Buffer(relationPlans);
            using GraphicsBuffer relationInputBuffer = Buffer(relationInputs);
            using GraphicsBuffer nearBuffer = Buffer(nearIntervals);
            using GraphicsBuffer hashBuffer = Buffer(hashes);
            using GraphicsBuffer stateBuffer = Buffer(PackStates(stateValues));
            using GraphicsBuffer rowBuffer = Buffer(PackRows(query));
            using GraphicsBuffer orderBuffer = Buffer(new[]
            {
                Pack(query.MeasuredOrder.Lower), Pack(query.MeasuredOrder.Upper),
            });
            using GraphicsBuffer opticalBuffer = Buffer(query.MeasuredOptical
                .SelectMany(value => new[] { Pack(value.Lower), Pack(value.Upper) })
                .ToArray());
            using GraphicsBuffer directionBuffer = Buffer(new[]
            {
                Pack(query.Direction.Lower), Pack(query.Direction.Upper),
            });
            using GraphicsBuffer exposureBuffer = Buffer(exposure);
            using GraphicsBuffer channelBuffer = Buffer(channelParameters);
            using GraphicsBuffer transferRangeBuffer = Buffer(transferRanges);
            using GraphicsBuffer transferDataBuffer = Buffer(transferData);
            using GraphicsBuffer headers = Buffer<UInt4>(candidateCount);
            using GraphicsBuffer supports = Buffer<UInt2>(candidateCount);
            using GraphicsBuffer actions = Buffer<UInt4>(candidateCount);
            using GraphicsBuffer predictions = Buffer<UInt4>(candidateCount * 4);
            using GraphicsBuffer relationResults = Buffer<UInt4>(candidateCount);
            using GraphicsBuffer computedRelationFactors = Buffer<UInt4>(
                candidateCount);
            using GraphicsBuffer computedRelationHashes = Buffer<UInt4>(
                candidateCount);
            using GraphicsBuffer computedRelationNorms = Buffer<UInt4>(
                candidateCount * 4);
            using GraphicsBuffer branchRelationFactors = Buffer<UInt4>(
                candidateCount);
            using GraphicsBuffer branchRelationHashes = Buffer<UInt4>(
                candidateCount);
            using GraphicsBuffer certificateWords = Buffer<UInt4>(80);
            using var args = new GraphicsBuffer(GraphicsBuffer.Target.Structured |
                GraphicsBuffer.Target.IndirectArguments, 3, sizeof(uint));

            int evaluateRelation = queryShader.FindKernel("EvaluateNativeRelation");
            queryShader.SetBuffer(evaluateRelation, "_NativeStates", stateBuffer);
            queryShader.SetBuffer(evaluateRelation, "_NativeRelationInputs",
                relationInputBuffer);
            queryShader.SetBuffer(evaluateRelation, "_NativeRelationPlans",
                relationPlanBuffer);
            queryShader.SetBuffer(evaluateRelation, "_NativeRelationNearIntervals",
                nearBuffer);
            queryShader.SetBuffer(evaluateRelation, "_NativeRelationResults",
                relationResults);
            queryShader.SetBuffer(evaluateRelation, "_NativeRelationFactors",
                computedRelationFactors);
            queryShader.SetBuffer(evaluateRelation, "_NativeRelationHashes",
                computedRelationHashes);
            queryShader.SetBuffer(evaluateRelation, "_NativeRelationNorms",
                computedRelationNorms);
            queryShader.SetInt("_NativeEntryPointIndex", Array.FindIndex(
                SigmaGeneratedMerkabaProgram.EntryPoints,
                value => value.Id == "INTRINSIC_RELATION"));
            queryShader.SetInt("_NativeRelationCount", candidateCount);
            // One workgroup evaluates one complete S16 relation. Workgroup
            // dimensions carry relation cardinality; no per-relation dispatch
            // sequence is introduced.
            queryShader.Dispatch(evaluateRelation, candidateCount, 1, 1);

            int hot = contractShader.FindKernel("ContractNativeQuery");
            int build = contractShader.FindKernel("BuildContractOverflowArgs");
            int overflow = contractShader.FindKernel("ResolveContractorOverflow");
            foreach (int kernel in new[] { hot, overflow })
            {
                contractShader.SetBuffer(kernel, "_NativeReverseKeys", keyBuffer);
                contractShader.SetBuffer(kernel, "_NativeReverseStates",
                    candidateStateBuffer);
                contractShader.SetBuffer(kernel,
                    "_NativeReverseGaugeCoordinates",
                    gaugeCoordinateBuffer);
                contractShader.SetBuffer(kernel, "_NativeReverseGaugeMetadata",
                    gaugeMetadataBuffer);
                contractShader.SetBuffer(kernel, "_NativeReverseRelationResults",
                    relationResults);
                contractShader.SetBuffer(kernel, "_NativeReverseRelationFactors",
                    computedRelationFactors);
                contractShader.SetBuffer(kernel, "_NativeReverseRelationHashes",
                    computedRelationHashes);
                contractShader.SetBuffer(kernel, "_NativeReverseDeltaHashes",
                    hashBuffer);
                contractShader.SetBuffer(kernel, "_NativeStates", stateBuffer);
                contractShader.SetBuffer(kernel, "_NativeQueryRows", rowBuffer);
                contractShader.SetBuffer(kernel, "_NativeObservationOrder",
                    orderBuffer);
                contractShader.SetBuffer(kernel, "_NativeMeasuredOptical",
                    opticalBuffer);
                contractShader.SetBuffer(kernel, "_NativeDirection",
                    directionBuffer);
                contractShader.SetBuffer(kernel, "_NativePhotometricExposure",
                    exposureBuffer);
                contractShader.SetBuffer(kernel,
                    "_NativePhotometricChannelParameters",
                    channelBuffer);
                contractShader.SetBuffer(kernel,
                    "_NativePhotometricTransferRanges",
                    transferRangeBuffer);
                contractShader.SetBuffer(kernel, "_NativePhotometricTransferData",
                    transferDataBuffer);
                contractShader.SetBuffer(kernel, "_NativeBranchHeaders", headers);
                contractShader.SetBuffer(kernel, "_NativeBranchSupports", supports);
                contractShader.SetBuffer(kernel, "_NativeBranchActions", actions);
                contractShader.SetBuffer(kernel, "_NativeBranchPredictions",
                    predictions);
                contractShader.SetBuffer(kernel,
                    "_NativeLocalityCertificateWords", certificateWords);
                contractShader.SetBuffer(kernel, "_NativeBranchRelationFactors",
                    branchRelationFactors);
                contractShader.SetBuffer(kernel, "_NativeBranchRelationHashes",
                    branchRelationHashes);
            }
            contractShader.SetBuffer(build, "_NativeOverflowArgs", args);
            contractShader.SetInt("_NativeReverseCount", candidateCount);
            contractShader.SetInt("_NativeHotCapacity", 2);
            contractShader.SetInt("_NativeContractMode", 0);
            contractShader.SetInt("_NativeFreshBranchCount", 0);
            uint observationFlags = (query.OrderEvidence ? 1u : 0u) |
                (query.OpticalEvidence ? 2u : 0u) |
                (query.PhotometricLaw.HasBoundedClaim ? 4u : 0u);
            contractShader.SetInt("_NativeObservationFlags", (int)observationFlags);
            contractShader.SetInt("_NativeEntryPointIndex", EntryPointIndex(query));
            contractShader.Dispatch(hot, 1, 1, 1);
            contractShader.Dispatch(build, 1, 1, 1);
            uint[] indirect = Read<uint>(args, 3);
            CollectionAssert.AreEqual(new uint[]
            {
                (uint)Math.Max(0, candidateCount - 2 + 63) / 64u, 1u, 1u,
            }, indirect);
            contractShader.DispatchIndirect(overflow, args);

            UInt4[] result = Read<UInt4>(headers, candidateCount);
            UInt4[] predictionData = Read<UInt4>(predictions,
                candidateCount * 4);
            int[] viable = result.Select((value, index) => (value, index))
                .Where(pair => (pair.value.Y & 1u) != 0u)
                .Select(pair => pair.index).ToArray();
            if (expectedViable != null)
                CollectionAssert.AreEqual(expectedViable, viable);
            UInt4[] actionData = Read<UInt4>(actions, candidateCount);
            if (behindOrdinal >= 0)
                Assert.That(actionData[behindOrdinal], Is.EqualTo(default(UInt4)),
                    "Behind-hit candidate has no action.");
            if (canonicalRoleCorpus)
            {
                Assert.That((result[0].Y >> 2) & 3u,
                    Is.EqualTo((uint)SigmaNativeQueryClaim.PreHitExclusion));
                Assert.That((result[1].Y >> 2) & 3u,
                    Is.EqualTo((uint)SigmaNativeQueryClaim.NoClaim));
                Assert.That((result[2].Y >> 2) & 3u,
                    Is.EqualTo((uint)SigmaNativeQueryClaim.FirstHitMould));
                Assert.That(actionData[0], Is.Not.EqualTo(default(UInt4)),
                    "Pre-hit action must initialize both interval endpoints.");
                Assert.That(actionData[1], Is.EqualTo(default(UInt4)),
                    "NO_CLAIM must initialize both interval endpoints to zero.");
                Assert.That(actionData[2], Is.Not.EqualTo(default(UInt4)),
                    "First-hit mould action must initialize both interval endpoints.");
            }
            int[] cpuViable = SigmaMerkabaSemanticOracle.ContractNativeQuery(query,
                    cpuCandidates).Branches.Select(value => value.CandidateOrdinal)
                .ToArray();
            CollectionAssert.AreEqual(cpuViable, viable);
            SigmaNativeContractBranch[] cpuBranches = SigmaMerkabaSemanticOracle
                .ContractNativeQuery(query, cpuCandidates).Branches;
            foreach (SigmaNativeContractBranch cpuBranch in cpuBranches)
            {
                UInt4 packedAction = actionData[cpuBranch.CandidateOrdinal];
                Assert.That(Unpack(new UInt2 { X = packedAction.X, Y = packedAction.Y }),
                    Is.EqualTo(cpuBranch.Action.Action.Lower));
                Assert.That(Unpack(new UInt2 { X = packedAction.Z, Y = packedAction.W }),
                    Is.EqualTo(cpuBranch.Action.Action.Upper));
            }
            var distinctOpticalPredictions = new HashSet<UInt4>();
            foreach (SigmaNativePreimageCandidate candidate in cpuCandidates)
            {
                int index = candidate.CandidateOrdinal;
                SigmaNativeContribution proposed = SigmaMerkabaSemanticOracle
                    .EvaluateNativeQuery(candidate.Proposed, index, query).Value;
                UInt4 packedOrder = predictionData[index * 4];
                Assert.That(Unpack(new UInt2
                {
                    X = packedOrder.X,
                    Y = packedOrder.Y,
                }), Is.EqualTo(proposed.Order.Lower));
                for (int channel = 0;
                    channel < SigmaNativePhotometricLaw.ChannelCount; ++channel)
                {
                    Assert.That(candidate.Proposed.Measure,
                        Is.EqualTo(SigmaNumericDomain.One));
                    Assert.That(query.PhotometricLaw.TryApply(channel,
                        proposed.WeightedOptical[channel],
                        out SigmaQ48Interval predicted), Is.True);
                    UInt4 packed = predictionData[index * 4 + 1 + channel];
                    Assert.That(Unpack(new UInt2 { X = packed.X, Y = packed.Y }),
                        Is.EqualTo(predicted.Lower));
                    Assert.That(Unpack(new UInt2 { X = packed.Z, Y = packed.W }),
                        Is.EqualTo(predicted.Upper));
                    distinctOpticalPredictions.Add(packed);
                }
            }
            if (requireDistinctOptical)
                Assert.That(distinctOpticalPredictions.Count, Is.GreaterThan(1),
                    "GPU must execute the per-channel PWL nuisance law, not only " +
                    "its fingerprint.");
            UInt4[] gpuFactors = Read<UInt4>(branchRelationFactors, candidateCount);
            UInt4[] gpuRelationHashes = Read<UInt4>(branchRelationHashes,
                candidateCount);
            foreach (SigmaNativePreimageCandidate candidate in cpuCandidates)
            {
                int index = candidate.CandidateOrdinal;
                SigmaNativeRelationWitness cpu = SigmaMerkabaSemanticOracle
                    .EvaluateNativeRelation(candidate.Relation);
                Assert.That(result[index].W,
                    Is.EqualTo((uint)cpu.RelationClass));
                Assert.That(gpuFactors[index].X,
                    Is.EqualTo((uint)cpu.Link.FactorClass));
                Assert.That(gpuFactors[index].Y,
                    Is.EqualTo((uint)cpu.Associator.FactorClass));
                Assert.That(gpuFactors[index].Z,
                    Is.EqualTo((uint)cpu.PlaquetteClass));
                Assert.That(gpuFactors[index].W & 0xffu,
                    Is.EqualTo((uint)cpu.ClosureClass));
                Assert.That((gpuFactors[index].W >> 8) & 1u,
                    Is.EqualTo(cpu.Link.DiffractionKernel ? 1u : 0u));
                Assert.That((gpuFactors[index].W >> 9) & 1u,
                    Is.EqualTo(cpu.Associator.DiffractionKernel ? 1u : 0u));
                Assert.That(gpuRelationHashes[index].X,
                    Is.EqualTo(HashS16(cpu.Link.Raw)));
                Assert.That(gpuRelationHashes[index].Y,
                    Is.EqualTo(HashS16(cpu.Associator.Raw)));
                Assert.That(gpuRelationHashes[index].Z,
                    Is.EqualTo(HashS16(cpu.Transition)));
            }
            return viable;
        }

        private static void AssertGpuRelationsMatchCpu(ComputeShader shader,
            IReadOnlyList<SigmaNativeRelationInput> relations,
            IReadOnlyList<SigmaNativeRelationWitness> cpu)
        {
            Assert.That(cpu.Count, Is.EqualTo(relations.Count));
            var states = new List<SigmaS16>(relations.Count * 3);
            var inputs = new UInt4[relations.Count];
            var plans = new UInt4[relations.Count];
            var nearIntervals = new UInt4[relations.Count];
            for (int index = 0; index < relations.Count; ++index)
            {
                SigmaNativeRelationInput relation = relations[index];
                uint offset = (uint)(states.Count * SigmaS16.LaneCount);
                states.Add(relation.Left);
                states.Add(relation.Right);
                states.Add(relation.Context);
                inputs[index] = new UInt4
                {
                    X = offset,
                    Y = relation.NearLaw.IsCalibrated ? 1u : 0u,
                };
                UInt4 plan = PackRelationPlan(relation);
                plan.X = offset + SigmaS16.LaneCount;
                plan.Y = offset + SigmaS16.LaneCount * 2u;
                plans[index] = plan;
                nearIntervals[index] = PackInterval(
                    relation.NearLaw.ResidualMagnitude);
            }
            using GraphicsBuffer stateBuffer = Buffer(PackStates(states));
            using GraphicsBuffer inputBuffer = Buffer(inputs);
            using GraphicsBuffer planBuffer = Buffer(plans);
            using GraphicsBuffer nearBuffer = Buffer(nearIntervals);
            using GraphicsBuffer resultBuffer = Buffer<UInt4>(relations.Count);
            using GraphicsBuffer factorBuffer = Buffer<UInt4>(relations.Count);
            using GraphicsBuffer hashBuffer = Buffer<UInt4>(relations.Count);
            using GraphicsBuffer normBuffer = Buffer<UInt4>(relations.Count * 4);
            int kernel = shader.FindKernel("EvaluateNativeRelation");
            UnityEngine.Rendering.LocalKeyword boundaryVariant = new(shader,
                "SIGMA_N4_BOUNDARY_VARIANT");
            UnityEngine.Rendering.LocalKeyword globalCloseVariant = new(shader,
                "SIGMA_N4_GLOBAL_CLOSE_VARIANT");
            shader.SetKeyword(boundaryVariant, false);
            shader.SetKeyword(globalCloseVariant, false);
            shader.SetBuffer(kernel, "_NativeStates", stateBuffer);
            shader.SetBuffer(kernel, "_NativeRelationInputs", inputBuffer);
            shader.SetBuffer(kernel, "_NativeRelationPlans", planBuffer);
            shader.SetBuffer(kernel, "_NativeRelationNearIntervals", nearBuffer);
            shader.SetBuffer(kernel, "_NativeRelationResults", resultBuffer);
            shader.SetBuffer(kernel, "_NativeRelationFactors", factorBuffer);
            shader.SetBuffer(kernel, "_NativeRelationHashes", hashBuffer);
            shader.SetBuffer(kernel, "_NativeRelationNorms", normBuffer);
            shader.SetInt("_NativeEntryPointIndex", Array.FindIndex(
                SigmaGeneratedMerkabaProgram.EntryPoints,
                value => value.Id == "INTRINSIC_RELATION"));
            shader.SetInt("_NativeRelationCount", relations.Count);
            shader.Dispatch(kernel, relations.Count, 1, 1);

            UInt4[] results = Read<UInt4>(resultBuffer, relations.Count);
            UInt4[] factors = Read<UInt4>(factorBuffer, relations.Count);
            UInt4[] hashes = Read<UInt4>(hashBuffer, relations.Count);
            UInt4[] norms = Read<UInt4>(normBuffer, relations.Count * 4);
            for (int index = 0; index < relations.Count; ++index)
            {
                SigmaNativeRelationWitness expected = cpu[index];
                Assert.That(results[index].W, Is.EqualTo(1u),
                    $"relation {index} arithmetic must remain valid");
                Assert.That(results[index].X,
                    Is.EqualTo((uint)expected.RelationClass), $"relation {index}");
                Assert.That(results[index].Y,
                    Is.EqualTo(expected.ExactAnnihilatorAction < 0
                        ? uint.MaxValue : (uint)expected.ExactAnnihilatorAction),
                    $"relation {index} exact ZD action");
                Assert.That(expected.MinimumAnnihilatorResidual,
                    Is.GreaterThanOrEqualTo(BigInteger.Zero));
                Assert.That(expected.MinimumAnnihilatorResidual,
                    Is.LessThanOrEqualTo(new BigInteger(ulong.MaxValue)));
                ulong residual = hashes[index].W | ((ulong)results[index].Z << 32);
                Assert.That(residual,
                    Is.EqualTo((ulong)expected.MinimumAnnihilatorResidual),
                    $"relation {index} minimum annihilator residual");
                Assert.That(factors[index].X,
                    Is.EqualTo((uint)expected.Link.FactorClass));
                Assert.That(factors[index].Y,
                    Is.EqualTo((uint)expected.Associator.FactorClass));
                Assert.That(factors[index].Z,
                    Is.EqualTo((uint)expected.PlaquetteClass));
                Assert.That(factors[index].W & 0xffu,
                    Is.EqualTo((uint)expected.ClosureClass));
                Assert.That((factors[index].W >> 8) & 1u,
                    Is.EqualTo(expected.Link.DiffractionKernel ? 1u : 0u));
                Assert.That((factors[index].W >> 9) & 1u,
                    Is.EqualTo(expected.Associator.DiffractionKernel ? 1u : 0u));
                Assert.That(hashes[index].X, Is.EqualTo(HashS16(expected.Link.Raw)));
                Assert.That(hashes[index].Y,
                    Is.EqualTo(HashS16(expected.Associator.Raw)));
                Assert.That(hashes[index].Z,
                    Is.EqualTo(HashS16(expected.Transition)));
                UInt4[] expectedLinkNorm = PackU256(expected.Link.NormSquare);
                UInt4[] expectedAssociatorNorm = PackU256(
                    expected.Associator.NormSquare);
                Assert.That(norms[index * 4], Is.EqualTo(expectedLinkNorm[0]),
                    $"relation {index} exact link G-norm low");
                Assert.That(norms[index * 4 + 1],
                    Is.EqualTo(expectedLinkNorm[1]),
                    $"relation {index} exact link G-norm high");
                Assert.That(norms[index * 4 + 2],
                    Is.EqualTo(expectedAssociatorNorm[0]),
                    $"relation {index} exact associator G-norm low");
                Assert.That(norms[index * 4 + 3],
                    Is.EqualTo(expectedAssociatorNorm[1]),
                    $"relation {index} exact associator G-norm high");
            }
        }

        private static SigmaNativeRelationWitness[] EvaluateRelationWindows(
            IReadOnlyList<(SigmaS16 left, SigmaS16 right, SigmaS16 context)> tuples,
            int windows, bool reverse)
        {
            var indexed = new List<(int index, SigmaNativeRelationWitness witness)>();
            var ranges = Partition(tuples.Count, windows).ToList();
            if (reverse) ranges.Reverse();
            foreach ((int offset, int count) in ranges)
                for (int index = offset; index < offset + count; ++index)
                {
                    (SigmaS16 left, SigmaS16 right, SigmaS16 context) = tuples[index];
                    indexed.Add((index, SigmaMerkabaSemanticOracle
                        .EvaluateNativeRelation(left, right, context)));
                }
            return indexed.OrderBy(value => value.index)
                .Select(value => value.witness).ToArray();
        }

        private static GpuShadow RunGpuQuery(ComputeShader shader,
            IReadOnlyList<SigmaNativeOracleCell> cells, uint[] selected,
            SigmaNativeOracleQuery query, int windows, bool reverse,
            string caseContext = null, bool allowColdContinuation = false) =>
            RunGpuQuerySet(shader, cells, selected, new[] { query },
                query.EntryPoint, windows, reverse, caseContext,
                allowColdContinuation).Single();

        private static GpuShadow[] RunGpuEyePair(ComputeShader shader,
            IReadOnlyList<SigmaNativeOracleCell> cells, uint[] selected,
            SigmaNativeEyePairQuery query, int windows, bool reverse,
            string caseContext = null) => RunGpuQuerySet(shader, cells, selected,
                query.Views, query.EntryPoint, windows, reverse, caseContext,
                allowColdContinuation: false);

        private static GpuShadow[] RunGpuQuerySet(ComputeShader shader,
            IReadOnlyList<SigmaNativeOracleCell> cells, uint[] selected,
            IReadOnlyList<SigmaNativeOracleQuery> queries,
            SigmaMerkabaEntryPoint entryPoint, int windows, bool reverse,
            string caseContext, bool allowColdContinuation)
        {
            int queryCount = queries.Count;
            int capacity = Math.Max(1, selected.Length);
            var states = cells.Select(value => value.State).ToList();
            var relationInputs = new UInt4[cells.Count];
            var relationPlans = new UInt4[cells.Count];
            var nearIntervals = new UInt4[cells.Count];
            for (int index = 0; index < cells.Count; ++index)
            {
                SigmaNativeRelationInput relation = cells[index].QueryRelation;
                uint leftOffset = (uint)(index * SigmaS16.LaneCount);
                uint rightOffset = (uint)(states.Count * SigmaS16.LaneCount);
                states.Add(relation.Right);
                uint contextOffset = (uint)(states.Count * SigmaS16.LaneCount);
                states.Add(relation.Context);
                relationInputs[index] = new UInt4
                {
                    X = leftOffset,
                    Y = relation.NearLaw.IsCalibrated ? 1u : 0u,
                };
                UInt4 plan = PackRelationPlan(relation);
                plan.X = rightOffset;
                plan.Y = contextOffset;
                relationPlans[index] = plan;
                nearIntervals[index] = PackInterval(
                    relation.NearLaw.ResidualMagnitude);
            }
            UInt4[] samples = cells.Select((cell, index) => new UInt4
            {
                X = (uint)cell.SupportKey,
                Y = (uint)(cell.SupportKey >> 32),
                Z = (uint)cell.Footprint,
                W = (uint)(index * SigmaS16.LaneCount),
            }).ToArray();
            UInt2[] measures = cells.Select(value => Pack(value.Measure)).ToArray();
            UInt2[] rows = queries.SelectMany(PackRows).ToArray();
            uint[] footprints = queries.Select(value => (uint)value.Footprint)
                .ToArray();

            using GraphicsBuffer worklist = Buffer(selected);
            using GraphicsBuffer stateBuffer = Buffer(PackStates(states));
            using GraphicsBuffer sampleBuffer = Buffer(samples);
            using GraphicsBuffer measureBuffer = Buffer(measures);
            using GraphicsBuffer rowBuffer = Buffer(rows);
            using GraphicsBuffer footprintBuffer = Buffer(footprints);
            using GraphicsBuffer relationInputBuffer = Buffer(relationInputs);
            using GraphicsBuffer relationPlanBuffer = Buffer(relationPlans);
            using GraphicsBuffer nearBuffer = Buffer(nearIntervals);
            using GraphicsBuffer relationResults = Buffer<UInt4>(cells.Count);
            using GraphicsBuffer relationFactors = Buffer<UInt4>(cells.Count);
            using GraphicsBuffer relationHashes = Buffer<UInt4>(cells.Count);
            using GraphicsBuffer relationNorms = Buffer<UInt4>(cells.Count * 4);
            using GraphicsBuffer headers = Buffer<UInt4>(capacity * queryCount);
            using GraphicsBuffer orderMeasures = Buffer<UInt4>(capacity * queryCount);
            using GraphicsBuffer optical = Buffer<UInt2>(capacity * queryCount *
                SigmaNativePhotometricLaw.ChannelCount);
            using GraphicsBuffer contributionRelations = Buffer<uint>(
                capacity * queryCount);
            using GraphicsBuffer reducedSupports = Buffer<UInt2>(
                capacity * queryCount * 2);
            using GraphicsBuffer reducedRelationSupports = Buffer<UInt2>(
                capacity * queryCount);
            using GraphicsBuffer reducedRelationClasses = Buffer<uint>(
                capacity * queryCount);
            using GraphicsBuffer reducedRecords = Buffer<UInt4>(6 * queryCount);
            using GraphicsBuffer overflowRecords = Buffer<UInt4>(queryCount);

            int relationKernel = shader.FindKernel("EvaluateNativeRelation");
            shader.SetBuffer(relationKernel, "_NativeStates", stateBuffer);
            shader.SetBuffer(relationKernel, "_NativeRelationInputs",
                relationInputBuffer);
            shader.SetBuffer(relationKernel, "_NativeRelationPlans",
                relationPlanBuffer);
            shader.SetBuffer(relationKernel, "_NativeRelationNearIntervals",
                nearBuffer);
            shader.SetBuffer(relationKernel, "_NativeRelationResults",
                relationResults);
            shader.SetBuffer(relationKernel, "_NativeRelationFactors",
                relationFactors);
            shader.SetBuffer(relationKernel, "_NativeRelationHashes",
                relationHashes);
            shader.SetBuffer(relationKernel, "_NativeRelationNorms",
                relationNorms);
            shader.SetInt("_NativeEntryPointIndex", Array.FindIndex(
                SigmaGeneratedMerkabaProgram.EntryPoints,
                value => value.Id == "INTRINSIC_RELATION"));
            shader.SetInt("_NativeRelationCount", cells.Count);
            shader.Dispatch(relationKernel, cells.Count, 1, 1);

            int evaluate = shader.FindKernel("EvaluateNativeQuery");
            int reduce = shader.FindKernel("ReduceNativeQuery");
            shader.SetBuffer(evaluate, "_NativeWorklist", worklist);
            shader.SetBuffer(evaluate, "_NativeStates", stateBuffer);
            shader.SetBuffer(evaluate, "_NativeSamples", sampleBuffer);
            shader.SetBuffer(evaluate, "_NativeMeasures", measureBuffer);
            shader.SetBuffer(evaluate, "_NativeQueryRows", rowBuffer);
            shader.SetBuffer(evaluate, "_NativeQueryFootprints", footprintBuffer);
            shader.SetBuffer(evaluate, "_NativeRelationResults", relationResults);
            shader.SetBuffer(evaluate, "_NativeContributionHeadersWrite", headers);
            shader.SetBuffer(evaluate, "_NativeContributionOrderMeasuresWrite",
                orderMeasures);
            shader.SetBuffer(evaluate, "_NativeContributionOpticalWrite", optical);
            shader.SetBuffer(evaluate, "_NativeContributionRelationsWrite",
                contributionRelations);
            shader.SetInt("_NativeEntryPointIndex", Array.FindIndex(
                SigmaGeneratedMerkabaProgram.EntryPoints,
                value => value.Id == entryPoint.Id));
            shader.SetInt("_NativeQueryCount", queryCount);
            shader.SetInt("_NativeContributionStride", capacity);
            shader.SetInt("_NativeDebugRequest", (int)queries[0].DebugRequest);

            var ranges = Partition(selected.Length, windows).ToList();
            if (reverse) ranges.Reverse();
            foreach ((int offset, int count) in ranges)
            {
                if (count == 0) continue;
                shader.SetInt("_NativeWorkOffset", offset);
                shader.SetInt("_NativeWorkCount", count);
                shader.Dispatch(evaluate, (count + 63) / 64, queryCount, 1);
            }
            foreach ((string name, GraphicsBuffer buffer) in new[]
            {
                ("_NativeContributionHeaders", headers),
                ("_NativeContributionOrderMeasures", orderMeasures),
                ("_NativeContributionOptical", optical),
                ("_NativeContributionRelations", contributionRelations),
                ("_NativeReducedSupports", reducedSupports),
                ("_NativeReducedRelationSupports", reducedRelationSupports),
                ("_NativeReducedRelationClasses", reducedRelationClasses),
                ("_NativeReducedRecords", reducedRecords),
                ("_NativeReduceOverflowRecords", overflowRecords),
                ("_NativeQueryFootprints", footprintBuffer),
            })
                shader.SetBuffer(reduce, name, buffer);
            shader.SetInt("_NativeContributionCount", selected.Length);
            shader.SetInt("_NativeEntryPointIndex", Array.FindIndex(
                SigmaGeneratedMerkabaProgram.EntryPoints,
                value => value.Id == entryPoint.Id));
            shader.SetInt("_NativeQueryCount", queryCount);
            shader.SetInt("_NativeContributionStride", capacity);
            shader.SetInt("_NativeDebugRequest", (int)queries[0].DebugRequest);
            shader.Dispatch(reduce, 1, queryCount, 1);

            UInt4[] records = Read<UInt4>(reducedRecords, 6 * queryCount);
            UInt4[] overflow = Read<UInt4>(overflowRecords, queryCount);
            UInt2[] supports = Read<UInt2>(reducedSupports,
                capacity * queryCount * 2);
            UInt2[] relationSupportData = Read<UInt2>(reducedRelationSupports,
                capacity * queryCount);
            uint[] relationClassData = Read<uint>(reducedRelationClasses,
                capacity * queryCount);
            var result = new GpuShadow[queryCount];
            for (int queryIndex = 0; queryIndex < queryCount; ++queryIndex)
            {
                int recordOffset = queryIndex * 6;
                UInt4 counts = records[recordOffset];
                bool requiresCold = (counts.W & 2u) != 0u;
                if (!allowColdContinuation)
                    Assert.That(requiresCold, Is.False,
                        "Unexpected reducer cold continuation. " +
                        (caseContext ?? string.Empty));
                if (requiresCold)
                {
                    Assert.That(overflow[queryIndex].Z, Is.EqualTo(1u));
                    result[queryIndex] = new GpuShadow((counts.W >> 8) & 0xffu,
                        Array.Empty<ulong>(), Array.Empty<ulong>(),
                        SigmaQ48Interval.Empty, EmptyOptical(), 0u,
                        Array.Empty<ulong>(),
                        Array.Empty<SigmaMerkabaRelationClass>(), true);
                    continue;
                }
                Assert.That((counts.W & 1u) != 0u, Is.True,
                    "Generated reducer descriptor must produce a valid result. " +
                    (caseContext ?? string.Empty) +
                    $" raw=({counts.X},{counts.Y},{counts.Z},{counts.W})" +
                    $" order=({records[recordOffset + 1].X}," +
                    $"{records[recordOffset + 1].Y}," +
                    $"{records[recordOffset + 1].Z}," +
                    $"{records[recordOffset + 1].W})");
                int supportBase = queryIndex * capacity * 2;
                int relationBase = queryIndex * capacity;
                int relationCount = (int)records[recordOffset + 5].X;
                SigmaQ48Interval[] opticalResult = Enumerable.Range(0,
                        SigmaNativePhotometricLaw.ChannelCount)
                    .Select(channel => new SigmaQ48Interval(
                        Unpack(new UInt2
                        {
                            X = records[recordOffset + 2 + channel].X,
                            Y = records[recordOffset + 2 + channel].Y,
                        }), Unpack(new UInt2
                        {
                            X = records[recordOffset + 2 + channel].Z,
                            Y = records[recordOffset + 2 + channel].W,
                        }))).ToArray();
                result[queryIndex] = new GpuShadow((counts.W >> 8) & 0xffu,
                    supports.Skip(supportBase).Take((int)counts.X)
                        .Select(UnpackKey).ToArray(),
                    supports.Skip(supportBase + capacity).Take((int)counts.Y)
                        .Select(UnpackKey).ToArray(),
                    new SigmaQ48Interval(
                        Unpack(new UInt2
                        {
                            X = records[recordOffset + 1].X,
                            Y = records[recordOffset + 1].Y,
                        }), Unpack(new UInt2
                        {
                            X = records[recordOffset + 1].Z,
                            Y = records[recordOffset + 1].W,
                        })), opticalResult, counts.W >> 16,
                    relationSupportData.Skip(relationBase).Take(relationCount)
                        .Select(UnpackKey).ToArray(),
                    relationClassData.Skip(relationBase).Take(relationCount)
                        .Select(value => (SigmaMerkabaRelationClass)value).ToArray());
            }
            return result;
        }

        private static SigmaNativeSceneShadow ReduceWindows(
            IReadOnlyList<SigmaNativeOracleCell> cells,
            SigmaNativeOracleQuery query, int windows, bool reverse)
        {
            var contributions = new List<SigmaNativeContribution>();
            var ranges = Partition(cells.Count, windows).ToList();
            if (reverse) ranges.Reverse();
            foreach ((int offset, int count) in ranges)
                for (int index = offset; index < offset + count; ++index)
                {
                    SigmaNativeContribution? value = SigmaMerkabaSemanticOracle
                        .EvaluateNativeQuery(cells[index], index, query);
                    if (value.HasValue) contributions.Add(value.Value);
                }
            return SigmaMerkabaSemanticOracle.ReduceNativeQuery(contributions,
                query);
        }

        private static IEnumerable<(int offset, int count)> Partition(int count,
            int windows)
        {
            int baseCount = count / windows;
            int remainder = count % windows;
            int offset = 0;
            for (int window = 0; window < windows; ++window)
            {
                int span = baseCount + (window < remainder ? 1 : 0);
                yield return (offset, span);
                offset += span;
            }
        }

        private static SigmaNativePreimageCandidate Candidate(int ordinal,
            int support, SigmaS16 proposed, SigmaNativeDeltaWitness delta) => new(
            ordinal, Cell(support, SigmaS16.Zero, support, 0),
            Cell(support, proposed, support, 0), delta,
            RelationFor(proposed, valid: true));

        private static SigmaNativePreimageCandidate CpuCandidate(int ordinal,
            SigmaS16 prior, SigmaS16 proposed, bool relation, bool transport) => new(
            ordinal, Cell(transport ? ordinal : ordinal + 1000, prior,
                transport ? ordinal : ordinal + 1000, 0),
            Cell(ordinal, proposed, ordinal, 0),
            new SigmaNativeDeltaWitness(ordinal, 0, proposed),
            RelationFor(proposed, relation));

        private static SigmaNativeRelationInput RelationFor(SigmaS16 proposed,
            bool valid) => new(proposed,
            valid ? proposed : SigmaS16Operators.Add(proposed, State(1, 1)),
            SigmaS16.Zero, 0, 0, 0, 0, 0);

        private static SigmaNativeOracleCell Cell(int support, SigmaS16 state,
            long u, long v) => new((ulong)support, 0,
            new SigmaGaugeCell(u, v, 0, $"support-{support}"), state);

        private static SigmaS16 State(int lane, int coefficient) =>
            SigmaS16.Basis(lane, SigmaNumericDomain.FromInteger(coefficient));

        private static SigmaS16 StateRaw(params (int lane, long raw)[] values)
        {
            var lanes = new long[SigmaS16.LaneCount];
            foreach ((int lane, long raw) in values)
            {
                Assert.That(lane, Is.InRange(0, SigmaS16.LaneCount - 1));
                lanes[lane] = raw;
            }
            return SigmaS16.FromArray(lanes);
        }

        private static SigmaNativeFreshObservationBranch FreshObservation(
            IReadOnlyList<long> target, ulong revision, int provenanceOrdinal,
            bool leftBroad = false, bool rightFirstHit = true,
            bool evidence = true, bool negateRightRows = false,
            bool unsupportedOptical = false)
        {
            Assert.That(target.Count, Is.EqualTo(4));
            Assert.That(target.Aggregate(0L, SigmaNumericDomain.QAdd), Is.Zero,
                "Fresh fixture must lie in the exact Merkaba tangent sector.");

            var intrinsics = new RigIntrinsics(
                new UnityEngine.Vector2(286f, 282f),
                new UnityEngine.Vector2(159.5f, 119.5f),
                new Vector2Int(320, 240), new Vector2Int(320, 240),
                Pose.identity,
                new UnityEngine.Vector4(-0.51f, 0.51f, 0.405f, -0.405f),
                0x5a17UL);
            UnityEngine.Vector2 nearFar = new(0.1f, 10f);

            SigmaInstrumentEyeBoundary BuildInstrument(string side, int pixelX,
                UnityEngine.Quaternion roomRotation, bool broad, bool firstHit,
                int ordinal)
            {
                RigCalibrationMath.ConeRayReference cone =
                    RigCalibrationMath.ConeRayAtPixel(intrinsics, pixelX, 120);
                UnityEngine.Vector3 roomRayFloat = roomRotation * cone.Center;
                UnityEngine.Vector3 roomDxFloat = roomRotation * cone.DifferentialX;
                UnityEngine.Vector3 roomDyFloat = roomRotation * cone.DifferentialY;
                long[] roomRay =
                {
                    SigmaNumericDomain.Quantize(roomRayFloat.x),
                    SigmaNumericDomain.Quantize(roomRayFloat.y),
                    SigmaNumericDomain.Quantize(roomRayFloat.z),
                };
                Assert.That(SigmaGeneratedMerkabaProgram
                    .TryBuildCalibratedRowPermutation(roomRay,
                        out int[] permutation, out _), Is.True);
                var exactCodes = new SigmaQ48Interval[4];
                for (int leaf = 0; leaf < exactCodes.Length; ++leaf)
                {
                    long code = SigmaNumericDomain.QAdd(
                        SigmaNumericDomain.Half,
                        SigmaNumericDomain.QShiftRight(
                            target[permutation[leaf]], 3));
                    exactCodes[leaf] = Point(code);
                }
                SigmaQ48Interval[] codes = broad
                    ? Enumerable.Repeat(new SigmaQ48Interval(0L,
                        SigmaNumericDomain.One), 4).ToArray()
                    : exactCodes;
                float rawDepth = (float)SigmaNumericDomain.ToDouble(
                    exactCodes[0].Lower);
                float metricRange = RigCalibrationMath
                    .RangeFromProjectionDepth01(rawDepth, nearFar, cone.Center);
                Assert.That(metricRange, Is.GreaterThan(0f));
                var footprint = new SigmaInstrumentFootprint(roomRay,
                    new[]
                    {
                        SigmaNumericDomain.Quantize(roomDxFloat.x),
                        SigmaNumericDomain.Quantize(roomDxFloat.y),
                        SigmaNumericDomain.Quantize(roomDxFloat.z),
                    },
                    new[]
                    {
                        SigmaNumericDomain.Quantize(roomDyFloat.x),
                        SigmaNumericDomain.Quantize(roomDyFloat.y),
                        SigmaNumericDomain.Quantize(roomDyFloat.z),
                    }, SigmaNumericDomain.Quantize(cone.HalfAngleX),
                    SigmaNumericDomain.Quantize(cone.HalfAngleY),
                    SigmaNumericDomain.Quantize(cone.SolidAngle));
                string provenance = ordinal.ToString("x64");
                return new SigmaInstrumentEyeBoundary(side, revision, 9UL,
                    ordinal * 4L + 1L, ordinal * 4L + 2L,
                    1000000L + ordinal * 10L,
                    1000001L + ordinal * 10L,
                    intrinsics.Signature, intrinsics.Signature,
                    GaugeFingerprint, footprint, codes[0],
                    Point(SigmaNumericDomain.Quantize(metricRange)),
                    codes.Skip(1).ToArray(), unsupportedOptical
                        ? SigmaInstrumentOpticalTransfer.Unsupported
                        : SigmaInstrumentOpticalTransfer.SrgbDecodedLinear,
                    firstHit, provenance);
            }

            SigmaInstrumentEyeBoundary left = BuildInstrument("LEFT", 146,
                UnityEngine.Quaternion.Euler(0f, -18f, 0f), leftBroad, true,
                provenanceOrdinal * 2 + 1);
            SigmaInstrumentEyeBoundary right = BuildInstrument("RIGHT", 178,
                UnityEngine.Quaternion.Euler(0f,
                    negateRightRows ? 198f : 18f, 0f),
                broad: false, firstHit: rightFirstHit,
                ordinal: provenanceOrdinal * 2 + 2);
            SigmaNativePhotometricLaw law = Law(true, 1, 1);
            return new SigmaNativeFreshObservationBranch(left, right,
                new SigmaNativeCoherentQueryContext(revision, GaugeFingerprint),
                evidence, evidence, law, law,
                provenanceOrdinal.ToString("x64"));
        }

        private static UInt2[] PackInstrumentCodes(
            SigmaInstrumentEyeBoundary instrument) =>
            new[] { instrument.ProjectionDepth01 }
            .Concat(instrument.OpticalCode)
            .SelectMany(value => new[] { Pack(value.Lower), Pack(value.Upper) })
            .ToArray();

        private static SigmaNativeOracleQuery LeftQuery(SigmaQ48Interval order,
            SigmaQ48Interval optical, bool orderEvidence,
            bool opticalEvidence) => Query("SENSOR_LEFT", 0, Axis(0), Axis(1),
            order, optical, Point(SigmaNumericDomain.One), orderEvidence,
            opticalEvidence, Law(true, 1, 1));

        private static SigmaNativeOracleQuery RightQuery(SigmaQ48Interval order,
            SigmaQ48Interval optical, bool orderEvidence,
            bool opticalEvidence) => Query("SENSOR_RIGHT", 0, Axis(2), Axis(3),
            order, optical, Point(SigmaNumericDomain.One), orderEvidence,
            opticalEvidence, Law(true, 1, 1));

        private static SigmaNativeOracleQuery Query(string entry, int footprint,
            long[] orderRow, long[] opticalRow, SigmaQ48Interval order,
            SigmaQ48Interval optical, SigmaQ48Interval direction,
            bool orderEvidence, bool opticalEvidence,
            SigmaNativePhotometricLaw law) => new(entry, footprint, orderRow,
            Enumerable.Range(0, SigmaNativePhotometricLaw.ChannelCount)
                .Select(_ => (IReadOnlyList<long>)opticalRow).ToArray(), order,
            Enumerable.Repeat(optical, SigmaNativePhotometricLaw.ChannelCount)
                .ToArray(), direction, orderEvidence,
            opticalEvidence, law,
            string.Equals(entry, "DEBUG", StringComparison.Ordinal)
                ? SigmaNativeDebugRequest.NativeRelationClass
                : SigmaNativeDebugRequest.None);

        private static SigmaNativeOracleQuery BuildPhotometricQuery(string entry,
            SigmaS16 measuredState, SigmaNativePhotometricLaw law)
        {
            IReadOnlyList<long>[] opticalRows =
            {
                Axis(0), Axis(1), Axis(2),
            };
            var probe = new SigmaNativeOracleQuery(entry, 0, Axis(0), opticalRows,
                Point(0L), Enumerable.Repeat(Point(0L),
                    SigmaNativePhotometricLaw.ChannelCount).ToArray(),
                Point(SigmaNumericDomain.One), orderEvidence: true,
                opticalEvidence: true, photometricLaw: law);
            SigmaNativeOracleCell cell = new(0UL, 0,
                new SigmaGaugeCell(0, 0, 0, "photometric-probe"), measuredState);
            SigmaNativeContribution contribution = SigmaMerkabaSemanticOracle
                .EvaluateNativeQuery(cell, 0, probe).Value;
            Assert.That(cell.Measure, Is.EqualTo(SigmaNumericDomain.One));
            SigmaQ48Interval[] measured = Enumerable.Range(0,
                    SigmaNativePhotometricLaw.ChannelCount)
                .Select(channel =>
                {
                    Assert.That(law.TryApply(channel,
                        contribution.WeightedOptical[channel],
                        out SigmaQ48Interval prediction), Is.True);
                    return prediction;
                }).ToArray();
            return new SigmaNativeOracleQuery(entry, 0, Axis(0), opticalRows,
                contribution.Order, measured, Point(SigmaNumericDomain.One),
                orderEvidence: true, opticalEvidence: true,
                photometricLaw: law);
        }

        private static SigmaNativePhotometricLaw Law(bool metadataPresent,
            int scaleLower, int scaleUpper)
        {
            var transfer = new[]
            {
                new SigmaNativePhotometricSegment(Raw(-32, 1), Raw(32, 1),
                    SigmaNumericDomain.One, 0L),
            };
            SigmaNativePhotometricChannelLaw[] channels = Enumerable.Range(0,
                    SigmaNativePhotometricLaw.ChannelCount)
                .Select(_ => new SigmaNativePhotometricChannelLaw(
                    new SigmaQ48Interval(SigmaNumericDomain.FromInteger(scaleLower),
                        SigmaNumericDomain.FromInteger(scaleUpper)),
                    Point(SigmaNumericDomain.One), Point(SigmaNumericDomain.One),
                    Point(0L), transfer)).ToArray();
            string fingerprint = SigmaNativePhotometricLaw
                .ComputeTransferFingerprint(channels);
            return new SigmaNativePhotometricLaw(metadataPresent,
                calibrationMatches: metadataPresent,
                Point(SigmaNumericDomain.One), channels, fingerprint);
        }

        private static SigmaNativePhotometricLaw PiecewiseLaw()
        {
            SigmaNativePhotometricChannelLaw[] channels = Enumerable.Range(0,
                    SigmaNativePhotometricLaw.ChannelCount)
                .Select(channel =>
                {
                    long positiveSlope = Raw(channel + 2, 1);
                    var transfer = new[]
                    {
                        new SigmaNativePhotometricSegment(Raw(-32, 1), 0L,
                            SigmaNumericDomain.One, 0L),
                        new SigmaNativePhotometricSegment(0L, Raw(32, 1),
                            positiveSlope, 0L),
                    };
                    return new SigmaNativePhotometricChannelLaw(
                        new SigmaQ48Interval(Raw(channel + 2, 2),
                            Raw(channel + 3, 2)),
                        Point(Raw(channel + 1, channel + 1)),
                        Point(Raw(channel + 2, channel + 2)),
                        Point(Raw(channel, 4)), transfer);
                }).ToArray();
            string fingerprint = SigmaNativePhotometricLaw
                .ComputeTransferFingerprint(channels);
            return new SigmaNativePhotometricLaw(metadataPresent: true,
                calibrationMatches: true,
                exposure: new SigmaQ48Interval(Raw(3, 4), Raw(5, 4)),
                channels: channels, transferFingerprint: fingerprint);
        }

        private static long[] Axis(int axis) => Enumerable.Range(0, 4)
            .Select(index => index == axis ? SigmaNumericDomain.One : 0L).ToArray();

        private static SigmaQ48Interval Point(long value) => new(value, value);
        private static SigmaQ48Interval[] EmptyOptical() => Enumerable.Repeat(
            SigmaQ48Interval.Empty, SigmaNativePhotometricLaw.ChannelCount).ToArray();
        private static long Raw(long numerator, long denominator) =>
            SigmaNumericDomain.FromRatio(numerator, denominator);

        private static SigmaCertificateFactor Factor(long lower, long upper) => new(
            "locality-0", "sensor", "view-0", "capture-0", "coupled-0",
            "branch-0", lower, upper);

        private static UInt4 Summary(uint sample, bool allDefault,
            bool boundaryClosed, bool nonresident = false)
        {
            uint flags = (allDefault ? 1u : 0u) |
                (boundaryClosed ? 2u : 0u) | 4u | 8u |
                (nonresident ? 32u : 0u);
            return new UInt4 { X = sample, Y = flags };
        }

        private static UInt4 PackKeys(ulong prior, ulong proposed) => new()
        {
            X = (uint)prior,
            Y = (uint)(prior >> 32),
            Z = (uint)proposed,
            W = (uint)(proposed >> 32),
        };

        private static UInt4 PackGaugeCoordinate(SigmaGaugeCell gauge) => new()
        {
            X = unchecked((uint)gauge.U),
            Y = unchecked((uint)(gauge.U >> 32)),
            Z = unchecked((uint)gauge.V),
            W = unchecked((uint)(gauge.V >> 32)),
        };

        private static UInt4 PackGaugeMetadata(SigmaGaugeCell gauge,
            IDictionary<string, ulong> payloadIds)
        {
            if (!payloadIds.TryGetValue(gauge.PayloadFingerprint,
                    out ulong payloadId))
            {
                payloadId = (ulong)payloadIds.Count + 1u;
                payloadIds.Add(gauge.PayloadFingerprint, payloadId);
            }
            return new UInt4
            {
                X = unchecked((uint)gauge.Level),
                Y = (uint)payloadId,
                Z = (uint)(payloadId >> 32),
                W = 0u,
            };
        }

        private static UInt4 PackRelationPlan(SigmaNativeRelationInput relation)
        {
            uint packed = (uint)relation.TransportGenerator |
                ((uint)relation.TransportAddress << 4) |
                ((uint)relation.PlaquetteA << 8) |
                ((uint)relation.PlaquetteC << 12) |
                ((uint)relation.PlaquetteBase << 16);
            return new UInt4
            {
                Z = packed,
                W = relation.NearLaw.IsCalibrated ? 1u : 0u,
            };
        }

        private static UInt4 PackInterval(SigmaQ48Interval interval) => new()
        {
            X = Pack(interval.Lower).X,
            Y = Pack(interval.Lower).Y,
            Z = Pack(interval.Upper).X,
            W = Pack(interval.Upper).Y,
        };

        private static void PackPhotometricLaw(SigmaNativeOracleQuery query,
            out UInt2[] exposure, out UInt2[] channelParameters,
            out UInt2[] transferRanges, out UInt2[] transferData)
        {
            exposure = new[]
            {
                Pack(query.PhotometricLaw.Exposure.Lower),
                Pack(query.PhotometricLaw.Exposure.Upper),
            };
            var parameters = new List<UInt2>(24);
            var ranges = new List<UInt2>(SigmaNativePhotometricLaw.ChannelCount);
            var segments = new List<UInt2>();
            foreach (SigmaNativePhotometricChannelLaw channel in
                     query.PhotometricLaw.Channels)
            {
                parameters.Add(Pack(channel.Gain.Lower));
                parameters.Add(Pack(channel.Gain.Upper));
                parameters.Add(Pack(channel.Illumination.Lower));
                parameters.Add(Pack(channel.Illumination.Upper));
                parameters.Add(Pack(channel.WhiteBalance.Lower));
                parameters.Add(Pack(channel.WhiteBalance.Upper));
                parameters.Add(Pack(channel.Offset.Lower));
                parameters.Add(Pack(channel.Offset.Upper));
                ranges.Add(new UInt2
                {
                    X = (uint)(segments.Count / 4),
                    Y = (uint)channel.Transfer.Length,
                });
                foreach (SigmaNativePhotometricSegment segment in channel.Transfer)
                {
                    segments.Add(Pack(segment.DomainLower));
                    segments.Add(Pack(segment.DomainUpper));
                    segments.Add(Pack(segment.Slope));
                    segments.Add(Pack(segment.Offset));
                }
            }
            channelParameters = parameters.ToArray();
            transferRanges = ranges.ToArray();
            transferData = segments.ToArray();
        }

        private static uint HashS16(SigmaS16 state)
        {
            uint hash = 2166136261u;
            unchecked
            {
                for (int lane = 0; lane < SigmaS16.LaneCount; ++lane)
                {
                    ulong packed = (ulong)state[lane];
                    hash = (hash ^ (uint)packed) * 16777619u;
                    hash = (hash ^ (uint)(packed >> 32)) * 16777619u;
                }
            }
            return hash;
        }

        private static UInt2[] PackRows(SigmaNativeOracleQuery query) =>
            query.OrderRow.Concat(query.OpticalRows.SelectMany(row => row))
                .Select(Pack).ToArray();

        private static UInt2[] PackStates(IEnumerable<SigmaS16> states) => states
            .SelectMany(state => state.ToArray()).Select(Pack).ToArray();

        private static UInt2 Pack(long value) => new()
        {
            X = unchecked((uint)value),
            Y = unchecked((uint)(value >> 32)),
        };

        private static UInt4[] PackU256(BigInteger value)
        {
            Assert.That(value.Sign, Is.GreaterThanOrEqualTo(0));
            var limbs = new uint[8];
            BigInteger remaining = value;
            for (int index = 0; index < limbs.Length; ++index)
            {
                limbs[index] = (uint)(remaining & uint.MaxValue);
                remaining >>= 32;
            }
            Assert.That(remaining == BigInteger.Zero, Is.True,
                "Admitted Q16.48 G-norm must fit the exact 256-bit GPU lowering; " +
                $"remaining high magnitude={remaining}.");
            return new[]
            {
                new UInt4 { X = limbs[0], Y = limbs[1], Z = limbs[2], W = limbs[3] },
                new UInt4 { X = limbs[4], Y = limbs[5], Z = limbs[6], W = limbs[7] },
            };
        }

        private static long Unpack(UInt2 value) => unchecked(
            (long)((ulong)value.X | ((ulong)value.Y << 32)));

        private static ulong UnpackKey(UInt2 value) => value.X |
            ((ulong)value.Y << 32);

        private static int EntryPointIndex(SigmaNativeOracleQuery query) =>
            Array.FindIndex(SigmaGeneratedMerkabaProgram.EntryPoints, value =>
                value.Id == query.EntryPoint.Id);

        private static int EntryPointIndex(string entryPoint) =>
            Array.FindIndex(SigmaGeneratedMerkabaProgram.EntryPoints, value =>
                value.Id == entryPoint);

        private static void AssertGpuShadowEqual(GpuShadow expected,
            GpuShadow actual)
        {
            Assert.That(actual.Reducer, Is.EqualTo(expected.Reducer));
            CollectionAssert.AreEqual(expected.FirstSupports,
                actual.FirstSupports);
            CollectionAssert.AreEqual(expected.BehindSupports,
                actual.BehindSupports);
            Assert.That(actual.Order, Is.EqualTo(expected.Order));
            CollectionAssert.AreEqual(expected.Optical, actual.Optical);
            Assert.That(actual.ActiveContributions,
                Is.EqualTo(expected.ActiveContributions));
            CollectionAssert.AreEqual(expected.RelationSupports,
                actual.RelationSupports);
            CollectionAssert.AreEqual(expected.RelationClasses,
                actual.RelationClasses);
            Assert.That(actual.RequiresColdContinuation,
                Is.EqualTo(expected.RequiresColdContinuation));
        }

        private static void AssertGpuShadowMatchesCpu(GpuShadow actual,
            SigmaNativeSceneShadow expected)
        {
            Assert.That(actual.Reducer, Is.EqualTo((uint)expected.Reducer));
            CollectionAssert.AreEqual(expected.FirstSupports,
                actual.FirstSupports);
            CollectionAssert.AreEqual(expected.BehindSupports,
                actual.BehindSupports);
            Assert.That(actual.Order, Is.EqualTo(expected.Order));
            CollectionAssert.AreEqual(expected.Optical, actual.Optical);
            CollectionAssert.AreEqual(expected.RelationSupports,
                actual.RelationSupports);
            CollectionAssert.AreEqual(expected.RelationClasses,
                actual.RelationClasses);
            Assert.That(actual.RequiresColdContinuation, Is.False);
        }

        private static ComputeShader LoadShader(string name)
        {
            string[] guids = AssetDatabase.FindAssets($"{name} t:ComputeShader")
                .Where(guid => string.Equals(Path.GetFileNameWithoutExtension(
                    AssetDatabase.GUIDToAssetPath(guid)), name,
                    StringComparison.Ordinal)).ToArray();
            Assert.That(guids, Has.Length.EqualTo(1));
            ComputeShader shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                AssetDatabase.GUIDToAssetPath(guids[0]));
            Assert.That(shader, Is.Not.Null);
            return shader;
        }

        private static GraphicsBuffer Buffer<T>(T[] data) where T : struct
        {
            GraphicsBuffer result = Buffer<T>(data.Length);
            result.SetData(data);
            return result;
        }

        private static GraphicsBuffer Buffer<T>(int count) where T : struct => new(
            GraphicsBuffer.Target.Structured, count, Marshal.SizeOf<T>());

        private static T[] Read<T>(GraphicsBuffer buffer, int count) where T : struct
        {
            var output = new T[count];
            buffer.GetData(output);
            return output;
        }
    }
}
