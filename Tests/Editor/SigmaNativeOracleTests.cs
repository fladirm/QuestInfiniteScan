using System;
using System.Collections.Generic;
using System.Linq;
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
        public void DirectionalMouldCorrectsNearSupportAndNeverActsBehindHit()
        {
            SigmaS16 nearState = State(14, 1);
            SigmaS16 mouldState = State(14, 2);
            SigmaNativeOracleQuery mould = LeftQuery(Point(Raw(3, 1)),
                Point(0), true, false);
            SigmaNativePreimageCandidate correction = new(0,
                Cell(0, nearState, 0, 0), Cell(0, mouldState, 0, 0),
                new SigmaNativeDeltaWitness(0, 0, mouldState), true, true);
            SigmaNativeContractBranch branch = SigmaMerkabaSemanticOracle
                .ContractNativeQuery(mould, new[] { correction }).Branches.Single();
            Assert.That(branch.Claim,
                Is.EqualTo(SigmaNativeQueryClaim.PreHitExclusion));
            Assert.That(branch.Action.Active, Is.True);
            Assert.That(branch.Action.Action.Lower, Is.GreaterThan(0L));
            Assert.That(branch.Delta.State, Is.EqualTo(mouldState),
                "The same support contracts to the mould; this is not carving.");

            SigmaNativePreimageCandidate unrelatedNear = new(1,
                Cell(1, nearState, 1, 0), Cell(1, mouldState, 1, 0),
                new SigmaNativeDeltaWitness(1, 0, mouldState), true, false);
            Assert.That(SigmaMerkabaSemanticOracle.ContractNativeQuery(mould,
                new[] { unrelatedNear }).Branches, Is.Empty,
                "An unrelated sheet may not be dragged to the mould.");

            SigmaNativePreimageCandidate behind = new(2,
                Cell(2, State(14, 3), 2, 0), Cell(2, mouldState, 2, 0),
                new SigmaNativeDeltaWitness(2, 0, mouldState), true, true);
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
                                SigmaS16.Zero), parent, commonDelta, true, true),
                    });
            SigmaNativeContractResult refinedReverse = SigmaMerkabaSemanticOracle
                .ContractNativeQuery(LeftQuery(refined.Order, Point(0), true, false),
                    children.Select((child, ordinal) =>
                        new SigmaNativePreimageCandidate(ordinal,
                            new SigmaNativeOracleCell(0, 0, child.Gauge,
                                SigmaS16.Zero), child, commonDelta, true, true)));
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
                    cells.Add(new SigmaNativeOracleCell(index, 0,
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
            Assert.That(AssetDatabase.FindAssets("SigmaNativeContract t:ComputeShader"),
                Has.Length.EqualTo(1));
            Assert.That(AssetDatabase.FindAssets("SigmaNativeQuery t:ComputeShader"),
                Has.Length.EqualTo(1));
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
        public void VulkanQueryContractOverflowAndWindowingMatchCpuOracle()
        {
            ComputeShader queryShader = LoadShader("SigmaNativeQuery");
            ComputeShader contractShader = LoadShader("SigmaNativeContract");
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
            UInt2[] packedStates = PackStates(cells.Select(value => value.State));
            UInt4[] sampleRecords = cells.Select((cell, index) => new UInt4
            {
                X = (uint)cell.SupportKey, Y = (uint)cell.Footprint,
                Z = (uint)(index * 16), W = 0u,
            }).ToArray();
            UInt2[] measures = cells.Select(value => Pack(value.Measure)).ToArray();
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

            UInt4[][] decompositions = new[] { 1, 2, 7 }.Select(windows =>
                RunGpuQuery(queryShader, cells, packedStates, sampleRecords, measures,
                    selected, query, windows, reverse: false)).ToArray();
            UInt4[] reversed = RunGpuQuery(queryShader, cells, packedStates,
                sampleRecords, measures, selected, query, 7, reverse: true);
            foreach (UInt4[] actual in decompositions.Skip(1))
                CollectionAssert.AreEqual(decompositions[0], actual);
            CollectionAssert.AreEqual(decompositions[0], reversed);
            Assert.That(decompositions[0][0].X, Is.EqualTo(1u));
            Assert.That(decompositions[0][0].Y, Is.EqualTo(2u));
            Assert.That(Unpack(new UInt2
            {
                X = decompositions[0][1].X, Y = decompositions[0][1].Y,
            }), Is.EqualTo(Raw(3, 2)));

            RunGpuContractOverflow(contractShader, query);
        }

        private static void RunGpuContractOverflow(ComputeShader shader,
            SigmaNativeOracleQuery query)
        {
            SigmaS16 zero = SigmaS16.Zero;
            SigmaS16 near = State(14, 1);
            SigmaS16 mould = State(14, 2);
            SigmaS16 behind = State(14, 3);
            SigmaS16[] states = { zero, near, mould, behind };
            UInt4[] candidates =
            {
                GpuCandidate(0, 1, 2, relation: true, transport: true),
                GpuCandidate(1, 3, 2, relation: true, transport: true),
                GpuCandidate(2, 0, 2, relation: true, transport: true),
                GpuCandidate(3, 1, 2, relation: false, transport: true),
                GpuCandidate(4, 1, 2, relation: true, transport: true),
                GpuCandidate(5, 1, 1, relation: true, transport: true),
            };
            uint[] hashes = Enumerable.Range(0, candidates.Length)
                .Select(value => (uint)(100 + value)).ToArray();
            UInt2[] observation =
            {
                Pack(query.MeasuredOrder.Lower), Pack(query.MeasuredOrder.Upper),
                Pack(query.MeasuredOptical.Lower), Pack(query.MeasuredOptical.Upper),
                Pack(query.Direction.Lower), Pack(query.Direction.Upper),
                Pack(SigmaNumericDomain.One), Pack(SigmaNumericDomain.One),
                Pack(0L), Pack(0L),
            };
            using GraphicsBuffer candidateBuffer = Buffer(candidates);
            using GraphicsBuffer hashBuffer = Buffer(hashes);
            using GraphicsBuffer stateBuffer = Buffer(PackStates(states));
            using GraphicsBuffer rowBuffer = Buffer(PackRows(query));
            using GraphicsBuffer observationBuffer = Buffer(observation);
            using GraphicsBuffer headers = Buffer<UInt4>(candidates.Length);
            using GraphicsBuffer actions = Buffer<UInt4>(candidates.Length);
            using GraphicsBuffer predictions = Buffer<UInt4>(candidates.Length);
            using var args = new GraphicsBuffer(GraphicsBuffer.Target.Structured |
                GraphicsBuffer.Target.IndirectArguments, 3, sizeof(uint));

            int hot = shader.FindKernel("ContractNativeQuery");
            int build = shader.FindKernel("BuildContractOverflowArgs");
            int overflow = shader.FindKernel("ResolveContractorOverflow");
            foreach (int kernel in new[] { hot, overflow })
            {
                shader.SetBuffer(kernel, "_NativeCandidates", candidateBuffer);
                shader.SetBuffer(kernel, "_NativeCandidateDeltaHashes", hashBuffer);
                shader.SetBuffer(kernel, "_NativeStates", stateBuffer);
                shader.SetBuffer(kernel, "_NativeQueryRows", rowBuffer);
                shader.SetBuffer(kernel, "_NativeObservationIntervals",
                    observationBuffer);
                shader.SetBuffer(kernel, "_NativeBranchHeaders", headers);
                shader.SetBuffer(kernel, "_NativeBranchActions", actions);
                shader.SetBuffer(kernel, "_NativeBranchPredictions", predictions);
            }
            shader.SetBuffer(build, "_NativeOverflowArgs", args);
            shader.SetInt("_NativeCandidateCount", candidates.Length);
            shader.SetInt("_NativeHotCapacity", 2);
            shader.SetInt("_NativeObservationFlags", 1);
            shader.Dispatch(hot, 1, 1, 1);
            shader.Dispatch(build, 1, 1, 1);
            uint[] indirect = Read<uint>(args, 3);
            CollectionAssert.AreEqual(new uint[] { 1, 1, 1 }, indirect);
            shader.DispatchIndirect(overflow, args);

            UInt4[] result = Read<UInt4>(headers, candidates.Length);
            int[] viable = result.Select((value, index) => (value, index))
                .Where(pair => (pair.value.Y & 1u) != 0u)
                .Select(pair => pair.index).ToArray();
            CollectionAssert.AreEqual(new[] { 0, 2, 4 }, viable);
            UInt4[] actionData = Read<UInt4>(actions, candidates.Length);
            Assert.That(actionData[1], Is.EqualTo(default(UInt4)),
                "Behind-hit candidate has no action.");

            SigmaNativePreimageCandidate[] cpuCandidates =
            {
                CpuCandidate(0, near, mould, true, true),
                CpuCandidate(1, behind, mould, true, true),
                CpuCandidate(2, zero, mould, true, true),
                CpuCandidate(3, near, mould, false, true),
                CpuCandidate(4, near, mould, true, true),
                CpuCandidate(5, near, near, true, true),
            };
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

        private static UInt4[] RunGpuQuery(ComputeShader shader,
            IReadOnlyList<SigmaNativeOracleCell> cells, UInt2[] packedStates,
            UInt4[] samples, UInt2[] measures, uint[] selected,
            SigmaNativeOracleQuery query, int windows, bool reverse)
        {
            using GraphicsBuffer worklist = Buffer(selected);
            using GraphicsBuffer stateBuffer = Buffer(packedStates);
            using GraphicsBuffer sampleBuffer = Buffer(samples);
            using GraphicsBuffer measureBuffer = Buffer(measures);
            using GraphicsBuffer rowBuffer = Buffer(PackRows(query));
            using GraphicsBuffer headers = Buffer<UInt4>(selected.Length);
            using GraphicsBuffer values = Buffer<UInt4>(selected.Length);
            using GraphicsBuffer contributionMeasures = Buffer<UInt2>(selected.Length);
            using GraphicsBuffer reducedHeaders = Buffer<UInt4>(1);
            using GraphicsBuffer reducedValues = Buffer<UInt4>(2);
            int evaluate = shader.FindKernel("EvaluateNativeQuery");
            int reduce = shader.FindKernel("ReduceNativeQuery");
            shader.SetBuffer(evaluate, "_NativeWorklist", worklist);
            shader.SetBuffer(evaluate, "_NativeStates", stateBuffer);
            shader.SetBuffer(evaluate, "_NativeSamples", sampleBuffer);
            shader.SetBuffer(evaluate, "_NativeMeasures", measureBuffer);
            shader.SetBuffer(evaluate, "_NativeQueryRows", rowBuffer);
            shader.SetBuffer(evaluate, "_NativeContributionHeaders", headers);
            shader.SetBuffer(evaluate, "_NativeContributionValues", values);
            shader.SetBuffer(evaluate, "_NativeContributionMeasures",
                contributionMeasures);
            shader.SetInt("_NativeFootprint", query.Footprint);

            var ranges = Partition(selected.Length, windows).ToList();
            if (reverse) ranges.Reverse();
            foreach ((int offset, int count) in ranges)
            {
                if (count == 0) continue;
                shader.SetInt("_NativeWorkOffset", offset);
                shader.SetInt("_NativeWorkCount", count);
                shader.Dispatch(evaluate, (count + 63) / 64, 1, 1);
            }
            shader.SetBuffer(reduce, "_NativeContributionHeaders", headers);
            shader.SetBuffer(reduce, "_NativeContributionValues", values);
            shader.SetBuffer(reduce, "_NativeContributionMeasures",
                contributionMeasures);
            shader.SetBuffer(reduce, "_NativeReducedHeaders", reducedHeaders);
            shader.SetBuffer(reduce, "_NativeReducedValues", reducedValues);
            shader.SetInt("_NativeContributionCount", selected.Length);
            shader.SetInt("_NativeFootprint", query.Footprint);
            shader.Dispatch(reduce, 1, 1, 1);
            return Read<UInt4>(reducedHeaders, 1)
                .Concat(Read<UInt4>(reducedValues, 2)).ToArray();
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
                query.Footprint);
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
            Cell(support, proposed, support, 0), delta, true, true);

        private static SigmaNativePreimageCandidate CpuCandidate(int ordinal,
            SigmaS16 prior, SigmaS16 proposed, bool relation, bool transport) => new(
            ordinal, Cell(ordinal, prior, ordinal, 0),
            Cell(ordinal, proposed, ordinal, 0),
            new SigmaNativeDeltaWitness(ordinal, 0, proposed), relation, transport);

        private static SigmaNativeOracleCell Cell(int support, SigmaS16 state,
            long u, long v) => new(support, 0,
            new SigmaGaugeCell(u, v, 0, $"support-{support}"), state);

        private static SigmaS16 State(int lane, int coefficient) =>
            SigmaS16.Basis(lane, SigmaNumericDomain.FromInteger(coefficient));

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
            opticalRow, order, optical, direction, orderEvidence,
            opticalEvidence, law);

        private static SigmaNativePhotometricLaw Law(bool metadataPresent,
            int scaleLower, int scaleUpper) => new(metadataPresent,
            calibrationMatches: metadataPresent,
            Point(SigmaNumericDomain.One),
            new SigmaQ48Interval(SigmaNumericDomain.FromInteger(scaleLower),
                SigmaNumericDomain.FromInteger(scaleUpper)),
            Point(SigmaNumericDomain.One), Point(0L), TransferFingerprint);

        private static long[] Axis(int axis) => Enumerable.Range(0, 4)
            .Select(index => index == axis ? SigmaNumericDomain.One : 0L).ToArray();

        private static SigmaQ48Interval Point(long value) => new(value, value);
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

        private static UInt4 GpuCandidate(uint support, uint priorState,
            uint proposedState, bool relation, bool transport) => new()
        {
            X = support,
            Y = priorState * 16u,
            Z = proposedState * 16u,
            W = (relation ? 1u : 0u) | (transport ? 2u : 0u),
        };

        private static UInt2[] PackRows(SigmaNativeOracleQuery query) =>
            query.OrderRow.Concat(query.OpticalRow).Select(Pack).ToArray();

        private static UInt2[] PackStates(IEnumerable<SigmaS16> states) => states
            .SelectMany(state => state.ToArray()).Select(Pack).ToArray();

        private static UInt2 Pack(long value) => new()
        {
            X = unchecked((uint)value),
            Y = unchecked((uint)(value >> 32)),
        };

        private static long Unpack(UInt2 value) => unchecked(
            (long)((ulong)value.X | ((ulong)value.Y << 32)));

        private static ComputeShader LoadShader(string name)
        {
            string[] guids = AssetDatabase.FindAssets($"{name} t:ComputeShader");
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
