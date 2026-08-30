using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Genesis.RoomScan.SigmaPrism;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class SigmaMerkabaProgramTests
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct UInt4
        {
            public uint X;
            public uint Y;
            public uint Z;
            public uint W;
        }

        [Test]
        public void AuthorityBoundaryAndExecutableIrAreFrozen()
        {
            Assert.That(SigmaGeneratedMerkabaProgram.ProgramVersion,
                Is.EqualTo("CPQ4-S16-MERKABA-N1R-8"));
            Assert.That(SigmaGeneratedMerkabaProgram.NumericDomainId,
                Is.EqualTo(SigmaNumericDomain.Id));
            Assert.That(SigmaGeneratedMerkabaProgram.DeclaredToeUpstreamFingerprint,
                Is.EqualTo("9d2e3604846305cfe5244a4ef49f169632c60582cf895256fadc36426dc5786f"));
            Assert.That(SigmaGeneratedMerkabaProgram.ToeCapsuleInputFingerprint,
                Has.Length.EqualTo(64));
            Assert.That(SigmaGeneratedMerkabaProgram.AlgebraCoreInputFingerprint,
                Has.Length.EqualTo(64));
            Assert.That(SigmaGeneratedMerkabaProgram.AlgebraCoreInputFingerprint,
                Is.Not.EqualTo(SigmaGeneratedAlgebra.BundleFingerprint),
                "Legacy readout/operator bundle cannot authorize Merkaba physics.");
            Assert.That(SigmaGeneratedMerkabaProgram.ProgramFingerprint,
                Has.Length.EqualTo(64));
            Assert.That(SigmaGeneratedMerkabaProgram.E22InventoryCount, Is.Zero);
            Assert.That(SigmaGeneratedMerkabaProgram.DirectS16DependenciesRetained,
                Is.True);
            Assert.That(SigmaGeneratedMerkabaProgram.CaptureBoundaryLeafCount,
                Is.EqualTo(8));
            Assert.That(SigmaGeneratedMerkabaProgram.CaptureBoundaryFingerprint,
                Has.Length.EqualTo(64));

            Assert.That(SigmaGeneratedMerkabaProgram.Expressions,
                Has.Length.EqualTo(SigmaGeneratedMerkabaProgram.ExpressionCount));
            Assert.That(SigmaGeneratedMerkabaProgram.IrNodes,
                Has.Length.EqualTo(SigmaGeneratedMerkabaProgram.IrNodeCount));
            Assert.That(SigmaGeneratedMerkabaProgram.IrOperands,
                Has.Length.EqualTo(SigmaGeneratedMerkabaProgram.IrOperandCount));
            Assert.That(SigmaGeneratedMerkabaProgram.EntryPoints,
                Has.Length.EqualTo(SigmaGeneratedMerkabaProgram.EntryPointCount));
            CollectionAssert.AllItemsAreUnique(
                SigmaGeneratedMerkabaProgram.ExpressionFingerprints);

            for (int expressionIndex = 0;
                 expressionIndex < SigmaGeneratedMerkabaProgram.Expressions.Length;
                 ++expressionIndex)
            {
                SigmaMerkabaExpression expression =
                    SigmaGeneratedMerkabaProgram.Expressions[expressionIndex];
                Assert.That(expression.Id, Is.Not.Empty);
                Assert.That(expression.Source, Is.Not.Empty);
                Assert.That(expression.Fingerprint, Has.Length.EqualTo(64));
                Assert.That(expression.RootNode,
                    Is.InRange(expression.NodeStart,
                        expression.NodeStart + expression.NodeCount - 1));
            }
            for (int nodeIndex = 0;
                 nodeIndex < SigmaGeneratedMerkabaProgram.IrNodes.Length;
                 ++nodeIndex)
            {
                SigmaMerkabaIrNode node = SigmaGeneratedMerkabaProgram.IrNodes[nodeIndex];
                Assert.That(node.OperandStart, Is.GreaterThanOrEqualTo(0));
                Assert.That(node.OperandStart + node.OperandCount,
                    Is.LessThanOrEqualTo(SigmaGeneratedMerkabaProgram.IrOperands.Length));
                for (int operand = 0; operand < node.OperandCount; ++operand)
                    Assert.That(SigmaGeneratedMerkabaProgram.IrOperands[
                        node.OperandStart + operand], Is.LessThan(nodeIndex));
            }
            CollectionAssert.IsSubsetOf(new[]
            {
                SigmaMerkabaIrOpcode.S16_MULTIPLY,
                SigmaMerkabaIrOpcode.INFORMATION_METRIC_APPLY,
                SigmaMerkabaIrOpcode.NORMALIZE_FACTOR,
                SigmaMerkabaIrOpcode.SCENE_REDUCE,
                SigmaMerkabaIrOpcode.PREIMAGE_UNION,
                SigmaMerkabaIrOpcode.FIRST_HIT_ACTION,
                SigmaMerkabaIrOpcode.CERTIFICATE_MINIMIZE,
                SigmaMerkabaIrOpcode.GAUGE_NORMALIZE,
                SigmaMerkabaIrOpcode.SHADOW_CELL_INTERSECT,
                SigmaMerkabaIrOpcode.TANGENT_MIN_CHANGE_SELECT,
                SigmaMerkabaIrOpcode.MERKABA_DUAL_FRAME_LIFT,
                SigmaMerkabaIrOpcode.FORWARD_RELATION_VERIFY,
                SigmaMerkabaIrOpcode.FRESH_BASE_PATTERN,
                SigmaMerkabaIrOpcode.COMMON_UNION_OR_UNRESOLVED,
                SigmaMerkabaIrOpcode.WHOLE_FRAME_REVERSE_SET,
                SigmaMerkabaIrOpcode.FOOTPRINT_CONTRACT,
                SigmaMerkabaIrOpcode.IMPLICIT_BOUNDARY_CONTRACT,
                SigmaMerkabaIrOpcode.GLOBAL_EXACT_CLOSE,
            }, SigmaGeneratedMerkabaProgram.IrNodes.Select(node => node.Opcode));
            Assert.That(SigmaGeneratedMerkabaProgram.Expressions.Any(expression =>
                expression.Id == "FRESH_BASE_ADMISSION"), Is.True);
            Assert.That(SigmaGeneratedMerkabaProgram.Expressions.Any(expression =>
                expression.Id == "FRESH_SUPPORT_SET_ADMISSION" &&
                expression.Source.Contains("constructiveModalStitching") &&
                expression.Source.Contains("stitchEmbedding")), Is.True);
            Assert.That(SigmaGeneratedMerkabaProgram.EntryPoints.Select(entry => entry.Id),
                Is.EquivalentTo(new[]
                {
                    "SENSOR_LEFT", "SENSOR_RIGHT", "EYE_PAIR",
                    "INTRINSIC_RELATION", "PREDICTION_SUPPORT", "EXPORT", "DEBUG",
                }));
            Assert.That(SigmaGeneratedMerkabaProgram.EntryPoints
                .Where(entry => entry.Id.StartsWith("SENSOR", StringComparison.Ordinal))
                .All(entry => entry.ReverseExpression >= 0), Is.True);
            Assert.That(SigmaGeneratedMerkabaProgram
                .ConstructiveStitchExternalSemanticTruthInputCount, Is.Zero);
            Assert.That(SigmaGeneratedMerkabaProgram
                .ConstructiveStitchCallerLoopTruthInputCount, Is.Zero);
            Assert.That(SigmaGeneratedMerkabaProgram
                .ConstructiveStitchHotSemanticPhaseCount, Is.EqualTo(3));
            Assert.That(SigmaGeneratedMerkabaProgram
                .ConstructiveStitchTargetAdditionalHotSubmissionCount,
                Is.LessThanOrEqualTo(2));
            Assert.That(SigmaGeneratedMerkabaProgram
                .ConstructiveStitchExternalBracketContextInputCount, Is.Zero);
            Assert.That(SigmaGeneratedMerkabaProgram
                .ConstructiveStitchCompleteAssociatorBasisContextCount,
                Is.EqualTo(16));
            Assert.That(SigmaGeneratedMerkabaProgram
                .ConstructiveStitchAssociatorProfileIsIntrinsicS16, Is.True);
            Assert.That(SigmaGeneratedMerkabaProgram
                .ConstructiveStitchS32Required, Is.False);
            CollectionAssert.IsEmpty(Enum.GetNames(typeof(SigmaMerkabaIrOpcode))
                .Intersect(new[]
                {
                    "CONTACT_CANDIDATE_SET",
                    "MERKABA_MODAL_STITCH",
                    "STITCH_LOOP_CLOSURE",
                    "FRESH_SUPPORT_SET_PATTERN",
                    "COMPONENT_TRANSLATION_NORMALIZE",
                }, StringComparer.Ordinal));
        }

        [Test]
        public void CompleteAssociatorBasisProfileReconstructsEveryS16Context()
        {
            var random = new System.Random(0x51616);
            string Fingerprint(char marker) => new(marker, 64);
            SigmaS16 SmallState()
            {
                var lanes = new long[SigmaS16.LaneCount];
                for (int lane = 0; lane < lanes.Length; ++lane)
                    lanes[lane] = SigmaNumericDomain.FromInteger(
                        random.Next(-3, 4));
                return SigmaS16.FromArray(lanes);
            }

            for (int fixture = 0; fixture < 12; ++fixture)
            {
                SigmaS16 left = fixture == 0
                    ? SigmaS16.Basis(0, SigmaNumericDomain.One) : SmallState();
                SigmaS16 right = fixture == 0
                    ? SigmaS16.Basis(3, SigmaNumericDomain.One) : SmallState();
                SigmaS16 context = SmallState();
                for (int leftSector = 0; leftSector < 4; ++leftSector)
                for (int rightSector = 0; rightSector < 4; ++rightSector)
                {
                    var a = (SigmaNativeBoundarySector)leftSector;
                    var b = (SigmaNativeBoundarySector)rightSector;
                    SigmaS16[] leftProfile = SigmaGeneratedMerkabaProgram
                        .EvaluateBasisAssociatorProfile(left, a);
                    SigmaS16[] rightProfile = SigmaGeneratedMerkabaProgram
                        .EvaluateBasisAssociatorProfile(right, b);
                    SigmaS16[] delta = Enumerable.Range(0, SigmaS16.LaneCount)
                        .Select(index => SigmaS16Operators.Subtract(
                            rightProfile[index], leftProfile[index])).ToArray();
                    SigmaS16 direct = SigmaS16Operators.Subtract(
                        SigmaS16Operators.Associator(right,
                            SigmaS16.Basis(1 << rightSector,
                                SigmaNumericDomain.One), context),
                        SigmaS16Operators.Associator(left,
                            SigmaS16.Basis(1 << leftSector,
                                SigmaNumericDomain.One), context));
                    Assert.That(SigmaGeneratedMerkabaProgram
                        .ReconstructAssociatorFromBasisProfile(delta, context),
                        Is.EqualTo(direct),
                        $"fixture {fixture}, sectors {leftSector}/{rightSector}");
                }
            }

            var contact = new SigmaStitchContactBranch(new[]
            {
                new SigmaQ48Interval(SigmaNumericDomain.One,
                    SigmaNumericDomain.One),
                new SigmaQ48Interval(SigmaNumericDomain.One,
                    SigmaNumericDomain.One),
                new SigmaQ48Interval(SigmaNumericDomain.One,
                    SigmaNumericDomain.One),
            });
            SigmaStitchWitnessSet witness = SigmaGeneratedMerkabaProgram
                .EvaluateModalStitch(new SigmaImplicitBoundaryRef(0, 1UL, 2UL,
                    SigmaSampleBoundarySide.Right, SigmaSampleBoundarySide.Left,
                    new[] { contact }),
                    new SigmaStitchLocality(1UL, 0,
                        SigmaS16.Basis(0, SigmaNumericDomain.One), Fingerprint('a')),
                    new SigmaStitchLocality(2UL, 0,
                        SigmaS16.Basis(3, SigmaNumericDomain.One), Fingerprint('b')),
                    new SigmaStitchNativeContext(Fingerprint('c')));
            SigmaStitchRelationReceipt decisive = witness.Receipts.Single(value =>
                value.LeftSector == SigmaNativeBoundarySector.Sector0 &&
                value.RightSector == SigmaNativeBoundarySector.Sector1);
            Assert.That(decisive.LinkClass,
                Is.EqualTo(SigmaExactFactorClass.ProvenExactClosed));
            Assert.That(decisive.ReverseLinkClass,
                Is.EqualTo(SigmaExactFactorClass.ProvenExactClosed));
            Assert.That(decisive.NonzeroAssociatorProfile, Is.True);
            Assert.That(decisive.AssociatorClass,
                Is.Not.EqualTo(SigmaExactFactorClass.ProvenExactClosed),
                "A zero caller context can no longer mask intrinsic nonassociativity.");
        }

        [Test]
        public void ConstructiveModalStitchingUsesAbstractWitnessesAndD4ChartGauge()
        {
            SigmaQ48Interval zeroToOne = new(0L, SigmaNumericDomain.One);
            SigmaQ48Interval oneToTwo = new(SigmaNumericDomain.One,
                SigmaNumericDomain.FromInteger(2));
            SigmaQ48Interval twoToThree = new(SigmaNumericDomain.FromInteger(2),
                SigmaNumericDomain.FromInteger(3));
            SigmaStitchBoundaryEnvelope Envelope(SigmaSampleBoundarySide side,
                SigmaQ48Interval region) => new(side,
                    new[] { region, region, region });
            SigmaFreshFootprintSample Sample(int x, ulong key,
                SigmaNativeQueryClaim claim, SigmaQ48Interval region,
                SigmaFootprintSupportDisposition disposition) => new(73UL,
                    x, 0, key, claim, disposition, new[]
                    {
                        Envelope(x == 0 ? SigmaSampleBoundarySide.Right :
                            SigmaSampleBoundarySide.Left, region),
                    });
            SigmaImplicitBoundaryRef[] boundaries = SigmaGeneratedMerkabaProgram
                .EnumerateImplicitBoundaryReference(new[]
                {
                    Sample(0, 11UL, SigmaNativeQueryClaim.FirstHitMould,
                        zeroToOne,
                        SigmaFootprintSupportDisposition.UnattachedFirstHit),
                    Sample(1, 22UL, SigmaNativeQueryClaim.FirstHitMould,
                        oneToTwo,
                        SigmaFootprintSupportDisposition.UnattachedFirstHit),
                }, 2, 1);
            Assert.That(boundaries, Has.Length.EqualTo(1));
            Assert.That(boundaries[0].EdgeIndex, Is.Zero);
            Assert.That(boundaries[0].ContactBranches, Has.Length.EqualTo(1));
            Assert.That(SigmaGeneratedMerkabaProgram.ImplicitBoundaryCount(320, 320),
                Is.EqualTo(204160));
            Assert.That(SigmaGeneratedMerkabaProgram
                .ConstructiveStitchImplicitPlaquetteCount320, Is.EqualTo(101761));
            Assert.That(SigmaGeneratedMerkabaProgram
                .ConstructiveStitchSamplingSideToDeltaAuthorityCount, Is.Zero);
            Assert.That(SigmaGeneratedMerkabaProgram
                .ConstructiveStitchAbstractNativeSectorCount, Is.EqualTo(4));
            Assert.That(SigmaGeneratedMerkabaProgram
                .ConstructiveStitchAbstractSectorChartAssignmentCount,
                Is.EqualTo(24));
            Assert.That(SigmaGeneratedMerkabaProgram
                .ConstructiveStitchAbstractSectorChartAssignmentOrbitCount,
                Is.EqualTo(3));
            Assert.That(SigmaGeneratedMerkabaProgram
                .ConstructiveStitchD4ChartImageCount, Is.EqualTo(8));
            Assert.That(SigmaGeneratedMerkabaProgram
                .NativeSectorChartAssignmentCount, Is.EqualTo(24));
            Assert.That(SigmaGeneratedMerkabaProgram
                .NativeSectorChartOrbitCount, Is.EqualTo(3));
            CollectionAssert.AreEqual(new[] { 8, 8, 8 },
                Enumerable.Range(0, 24).GroupBy(index =>
                        SigmaGeneratedMerkabaProgram
                            .NativeSectorChartOrbitAt(index))
                    .Select(group => group.Count()).OrderBy(value => value));
            Assert.That(SigmaGeneratedMerkabaProgram
                .EnumerateImplicitBoundaryReference(new[]
                {
                    Sample(0, 11UL, SigmaNativeQueryClaim.FirstHitMould,
                        zeroToOne,
                        SigmaFootprintSupportDisposition.UnattachedFirstHit),
                    Sample(1, 22UL, SigmaNativeQueryClaim.FirstHitMould,
                        twoToThree,
                        SigmaFootprintSupportDisposition.UnattachedFirstHit),
                }, 2, 1), Is.Empty);
            Assert.That(SigmaGeneratedMerkabaProgram
                .EnumerateImplicitBoundaryReference(new[]
                {
                    Sample(0, 11UL, SigmaNativeQueryClaim.NoClaim, zeroToOne,
                        SigmaFootprintSupportDisposition.UnresolvedExisting),
                    Sample(1, 22UL, SigmaNativeQueryClaim.FirstHitMould,
                        oneToTwo,
                        SigmaFootprintSupportDisposition.UnattachedFirstHit),
                }, 2, 1), Is.Empty);

            string Certificate(char marker) => new(marker, 64);
            SigmaStitchContactBranch Contact() => new(new[]
            {
                new SigmaQ48Interval(SigmaNumericDomain.One,
                    SigmaNumericDomain.One),
                new SigmaQ48Interval(SigmaNumericDomain.One,
                    SigmaNumericDomain.One),
                new SigmaQ48Interval(SigmaNumericDomain.One,
                    SigmaNumericDomain.One),
            });
            SigmaStitchNativeContext Context(char marker) =>
                new(Certificate(marker));
            SigmaStitchLocality Locality(ulong key, int basis, int level,
                char marker) => new(key, level,
                    SigmaS16.Basis(basis, SigmaNumericDomain.One),
                    Certificate(marker));
            SigmaStitchLocality SignedLocality(ulong key, int basis, int sign,
                int level, char marker) => new(key, level,
                    SigmaS16.Basis(basis, sign < 0
                        ? SigmaNumericDomain.QNegate(SigmaNumericDomain.One)
                        : SigmaNumericDomain.One), Certificate(marker));
            SigmaBoundaryNativeInput Edge(int edgeIndex, ulong left, ulong right,
                char marker,
                SigmaSampleBoundarySide leftSide = SigmaSampleBoundarySide.Right,
                SigmaSampleBoundarySide rightSide = SigmaSampleBoundarySide.Left) =>
                new(new SigmaImplicitBoundaryRef(edgeIndex, left, right,
                    leftSide, rightSide, new[] { Contact() }),
                    Context(marker));

            SigmaStitchLocality a = Locality(11UL, 1, 0, 'a');
            SigmaStitchLocality b = Locality(22UL, 2, 0, 'b');
            SigmaStitchWitnessSet abSet = SigmaGeneratedMerkabaProgram
                .EvaluateModalStitch(boundaries[0], a, b, Context('x'));
            Assert.That(abSet.Resolution, Is.EqualTo(
                SigmaStitchResolution.Resolved));
            Assert.That(abSet.HasOpenFactor, Is.False);
            SigmaResolvedStitch ab = abSet.Resolved;
            Assert.That(ab.LeftSector, Is.EqualTo(
                SigmaNativeBoundarySector.Sector0));
            Assert.That(ab.RightSector, Is.EqualTo(
                SigmaNativeBoundarySector.Sector1));
            Assert.That(ab.Receipt.TransportAddress, Is.EqualTo(3));
            Assert.That(typeof(SigmaResolvedStitch).GetProperty("DeltaU",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic), Is.Null);
            Assert.That(typeof(SigmaResolvedStitch).GetProperty("DeltaV",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic), Is.Null);
            CollectionAssert.AreEqual(
                Encoding.ASCII.GetBytes(SigmaGeneratedMerkabaProgram
                    .CanonicalStitchReceiptSerialization(ab.Receipt)),
                SigmaGeneratedMerkabaProgram.CanonicalStitchReceiptTokens(
                    ab.Receipt),
                "The accepted textual receipt and generated canonical byte " +
                "enumerator must be the same stream.");

            SigmaStitchWitnessSet provenanceCollision =
                SigmaGeneratedMerkabaProgram.EvaluateModalStitch(boundaries[0],
                    a, b, Context('y'));
            Assert.That(provenanceCollision.Resolution,
                Is.EqualTo(SigmaStitchResolution.Resolved));
            Assert.That(SigmaGeneratedMerkabaProgram.CompareCanonicalTokens(
                    SigmaGeneratedMerkabaProgram.CanonicalStitchReceiptTokens(
                        ab.Receipt),
                    SigmaGeneratedMerkabaProgram.CanonicalStitchReceiptTokens(
                        provenanceCollision.Resolved.Receipt)), Is.Not.Zero,
                "Equal old factor hashes cannot hide distinct exact provenance.");

            SigmaImplicitBoundaryRef reversedBoundary = new(0, 22UL, 11UL,
                SigmaSampleBoundarySide.Left, SigmaSampleBoundarySide.Right,
                new[] { Contact() });
            SigmaStitchWitnessSet baSet = SigmaGeneratedMerkabaProgram
                .EvaluateModalStitch(reversedBoundary, b, a, Context('x'));
            Assert.That(baSet.Resolution, Is.EqualTo(
                SigmaStitchResolution.Resolved));
            Assert.That(baSet.Resolved.LeftSector, Is.EqualTo(ab.RightSector));
            Assert.That(baSet.Resolved.RightSector, Is.EqualTo(ab.LeftSector));
            Assert.That(SigmaGeneratedMerkabaProgram.CanonicalStitchSerialization(
                    baSet.Resolved), Is.EqualTo(
                    SigmaGeneratedMerkabaProgram.CanonicalStitchSerialization(ab)));
            SigmaStitchWitnessSet sampleSidePermutation =
                SigmaGeneratedMerkabaProgram.EvaluateModalStitch(
                    new SigmaImplicitBoundaryRef(0, 11UL, 22UL,
                        SigmaSampleBoundarySide.Down,
                        SigmaSampleBoundarySide.Up, new[] { Contact() }),
                    a, b, Context('x'));
            Assert.That(sampleSidePermutation.Resolution,
                Is.EqualTo(SigmaStitchResolution.Resolved));
            Assert.That(SigmaGeneratedMerkabaProgram.CanonicalStitchSerialization(
                    sampleSidePermutation.Resolved), Is.EqualTo(
                    SigmaGeneratedMerkabaProgram.CanonicalStitchSerialization(ab)),
                "Sampling sides may propose contact but cannot choose a chart orbit.");

            SigmaStitchLocality c = Locality(33UL, 4, 0, 'c');
            SigmaStitchLocality[] localities =
            {
                a,
                b,
                c,
                Locality(44UL, 4, 1, 'd'),
                Locality(55UL, 5, 2, 'e'),
            };
            SigmaBoundaryNativeInput[] edges =
            {
                Edge(0, 11UL, 22UL, 'p'),
                Edge(1, 22UL, 33UL, 'q',
                    SigmaSampleBoundarySide.Down,
                    SigmaSampleBoundarySide.Up),
            };
            SigmaBoundaryNativeInput[] oneEdge = { edges[0] };
            Assert.That(SigmaGeneratedMerkabaProgram.TryIntegrateStitchPattern(
                localities, oneEdge, out SigmaStitchPattern pattern), Is.True);
            Assert.That(pattern.Resolution, Is.EqualTo(
                SigmaStitchResolution.Resolved));
            Assert.That(pattern.EmbeddingClassCount, Is.EqualTo(1));
            Assert.That(pattern.ComponentCount, Is.EqualTo(4));
            Assert.That(pattern.PackedCells.Count, Is.EqualTo(5));
            Assert.That(pattern.PackedCells.Where(value => value.Level > 0)
                .All(value => value.U % (1L << value.Level) == 0L &&
                    value.V % (1L << value.Level) == 0L), Is.True,
                "Backing translation must remain one exact integer at every level.");

            Assert.That(SigmaGeneratedMerkabaProgram.TryIntegrateStitchPattern(
                localities.Reverse(), oneEdge.Reverse(),
                out SigmaStitchPattern permuted), Is.True);
            Assert.That(permuted.Resolution, Is.EqualTo(
                SigmaStitchResolution.Resolved));
            Assert.That(permuted.CanonicalSerialization,
                Is.EqualTo(pattern.CanonicalSerialization));
            CollectionAssert.AreEqual(
                Encoding.ASCII.GetBytes(pattern.CanonicalSerialization),
                pattern.CanonicalTokens,
                "The structured comparator must consume the accepted complete " +
                "component serialization byte-for-byte.");
            Assert.That(SigmaGeneratedMerkabaProgram
                .CompareCompleteCanonicalComponentImage(pattern, permuted),
                Is.Zero);
            CollectionAssert.AreEqual(pattern.PackedCells.Select(value =>
                $"{value.Level}:{value.U}:{value.V}:{value.PayloadFingerprint}"),
                permuted.PackedCells.Select(value =>
                $"{value.Level}:{value.U}:{value.V}:{value.PayloadFingerprint}"));

            SigmaStitchLocality[] changedCertificate = localities.ToArray();
            changedCertificate[0] = Locality(11UL, 1, 0, 'z');
            Assert.That(SigmaGeneratedMerkabaProgram.TryIntegrateStitchPattern(
                changedCertificate, oneEdge,
                out SigmaStitchPattern changedCertificatePattern), Is.True);
            Assert.That(changedCertificatePattern.Resolution,
                Is.EqualTo(SigmaStitchResolution.Resolved));
            Assert.That(Math.Sign(SigmaGeneratedMerkabaProgram
                    .CompareCompleteCanonicalComponentImage(pattern,
                        changedCertificatePattern)),
                Is.EqualTo(Math.Sign(string.CompareOrdinal(
                    pattern.CanonicalSerialization,
                    changedCertificatePattern.CanonicalSerialization))));

            Assert.That(SigmaGeneratedMerkabaProgram.TryIntegrateStitchPattern(
                localities, new[] { Edge(0, 11UL, 22UL, 'z') },
                out SigmaStitchPattern changedProvenancePattern), Is.True);
            Assert.That(Math.Sign(SigmaGeneratedMerkabaProgram
                    .CompareCompleteCanonicalComponentImage(pattern,
                        changedProvenancePattern)),
                Is.EqualTo(Math.Sign(string.CompareOrdinal(
                    pattern.CanonicalSerialization,
                    changedProvenancePattern.CanonicalSerialization))));

            SigmaBoundaryNativeInput[] reversedEdges =
            {
                Edge(0, 22UL, 11UL, 'p',
                    SigmaSampleBoundarySide.Left,
                    SigmaSampleBoundarySide.Right),
            };
            Assert.That(SigmaGeneratedMerkabaProgram.TryIntegrateStitchPattern(
                localities.Reverse(), reversedEdges,
                out SigmaStitchPattern reversed), Is.True);
            Assert.That(reversed.Resolution, Is.EqualTo(
                SigmaStitchResolution.Resolved));
            Assert.That(reversed.CanonicalSerialization,
                Is.EqualTo(pattern.CanonicalSerialization),
                "Reversing equivalent stitch enumeration cannot change bytes.");

            Assert.That(SigmaGeneratedMerkabaProgram.TryIntegrateStitchPattern(
                localities, edges,
                out SigmaStitchPattern nonGaugeAmbiguity), Is.True);
            Assert.That(nonGaugeAmbiguity.Resolution, Is.EqualTo(
                SigmaStitchResolution.Unresolved),
                "Straight and corner embeddings are distinct D4 orbits; no " +
                "fixed native-sector chart convention may select one.");
            Assert.That(nonGaugeAmbiguity.EmbeddingClassCount,
                Is.GreaterThanOrEqualTo(2));

            Assert.That(SigmaGeneratedMerkabaProgram.TryIntegrateStitchPattern(
                localities, edges.Append(Edge(2, 11UL, 33UL, 'r')),
                out SigmaStitchPattern inconsistent), Is.True);
            Assert.That(inconsistent.Resolution, Is.EqualTo(
                SigmaStitchResolution.Unresolved),
                "An inconsistent generated fundamental cycle is never repaired.");

            SigmaStitchLocality[] intrinsicCycleLocalities =
            {
                SignedLocality(1001UL, 1, -1, 0, 'h'),
                SignedLocality(1002UL, 2, 1, 0, 'i'),
                SignedLocality(1003UL, 4, -1, 0, 'j'),
                SignedLocality(1004UL, 1, 1, 0, 'k'),
                SignedLocality(1005UL, 2, -1, 0, 'l'),
                SignedLocality(1006UL, 8, 1, 0, 'm'),
            };
            SigmaBoundaryNativeInput[] intrinsicCycleEdges =
            {
                Edge(10, 1001UL, 1002UL, 's'),
                Edge(11, 1002UL, 1003UL, 't'),
                Edge(12, 1003UL, 1004UL, 'u'),
                Edge(13, 1004UL, 1005UL, 'v'),
                Edge(14, 1005UL, 1006UL, 'w'),
                Edge(15, 1006UL, 1001UL, 'x'),
            };
            Assert.That(SigmaGeneratedMerkabaProgram.TryIntegrateStitchPattern(
                intrinsicCycleLocalities, intrinsicCycleEdges,
                out SigmaStitchPattern intrinsicCycle), Is.True);
            Assert.That(intrinsicCycle.Resolution,
                Is.EqualTo(SigmaStitchResolution.Resolved),
                $"Intrinsic four-sector cycle must close in one D4 orbit; " +
                $"classes={intrinsicCycle.EmbeddingClassCount}.");
            Assert.That(intrinsicCycle.ComponentCount, Is.EqualTo(1));

            SigmaStitchLocality nonAssociativeLeft =
                Locality(66UL, 9, 0, 'f');
            SigmaStitchLocality nonAssociativeRight =
                Locality(77UL, 15, 0, 'g');
            SigmaImplicitBoundaryRef nonAssociativeBoundary = new(3, 66UL, 77UL,
                SigmaSampleBoundarySide.Right, SigmaSampleBoundarySide.Left,
                new[] { Contact() });
            SigmaStitchWitnessSet intrinsicAssociator =
                SigmaGeneratedMerkabaProgram.EvaluateModalStitch(
                    nonAssociativeBoundary, nonAssociativeLeft,
                    nonAssociativeRight, Context('h'));
            Assert.That(intrinsicAssociator.Receipts.Any(value =>
                value.NonzeroAssociatorProfile), Is.True,
                "Nonassociativity is the complete intrinsic basis profile, not a " +
                "caller-selected context.");

            SigmaStitchWitnessSet incompatible =
                SigmaGeneratedMerkabaProgram.EvaluateModalStitch(
                    new SigmaImplicitBoundaryRef(4, 88UL, 99UL,
                        SigmaSampleBoundarySide.Right,
                        SigmaSampleBoundarySide.Left, new[] { Contact() }),
                    Locality(88UL, 0, 0, 'i'),
                    Locality(99UL, 7, 0, 'j'),
                    Context('k'));
            Assert.That(incompatible.Resolution, Is.EqualTo(
                SigmaStitchResolution.NoStitch));

            SigmaStitchWitnessSet uncertain = SigmaGeneratedMerkabaProgram
                .EvaluateModalStitch(
                    new SigmaImplicitBoundaryRef(5, 111UL, 122UL,
                        SigmaSampleBoundarySide.Right,
                        SigmaSampleBoundarySide.Left, new[] { Contact() }),
                    Locality(111UL, 0, 0, 'l'),
                    Locality(122UL, 8, 0, 'm'),
                    Context('n'));
            Assert.That(uncertain.Resolution, Is.EqualTo(
                SigmaStitchResolution.Unresolved));
            Assert.That(uncertain.HasOpenFactor, Is.True);

            SigmaGaugeCell[] chain =
            {
                new(0, 0, 0, "a"),
                new(1, 0, 0, "b"),
                new(2, 0, 0, "c"),
            };
            string chainCanonical = SigmaGeneratedMerkabaProgram
                .CanonicalD4GaugeSerialization(chain);
            for (int image = 0; image < 8; ++image)
            {
                SigmaGaugeCell[] transformed = SigmaGeneratedMerkabaProgram
                    .ApplyChartD4(chain, image)
                    .Select(value => new SigmaGaugeCell(
                        value.U + 7 * (1L << value.Level),
                        value.V - 5 * (1L << value.Level), value.Level,
                        value.PayloadFingerprint)).ToArray();
                Assert.That(SigmaGeneratedMerkabaProgram
                    .CanonicalD4GaugeSerialization(transformed),
                    Is.EqualTo(chainCanonical));
            }
            SigmaGaugeCell[] corner =
            {
                new(0, 0, 0, "a"),
                new(1, 0, 0, "b"),
                new(1, 1, 0, "c"),
            };
            Assert.That(SigmaGeneratedMerkabaProgram
                .TryCanonicalizeChartEmbeddingClasses(
                    new IEnumerable<SigmaGaugeCell>[] { chain, corner },
                    out _), Is.False,
                "Lexicographic order may not select across non-D4 classes.");

            SigmaGaugeCell[] mixed =
            {
                new(0, 0, 0, "l0"),
                new(4, 0, 1, "l1"),
                new(0, 12, 2, "l2"),
            };
            string mixedCanonical = SigmaGeneratedMerkabaProgram
                .CanonicalD4GaugeSerialization(mixed);
            for (int image = 0; image < 8; ++image)
            {
                SigmaGaugeCell[] transformed = SigmaGeneratedMerkabaProgram
                    .ApplyChartD4(mixed, image)
                    .Select(value => new SigmaGaugeCell(
                        value.U - 3 * (1L << value.Level),
                        value.V + 9 * (1L << value.Level), value.Level,
                        value.PayloadFingerprint)).ToArray();
                Assert.That(SigmaGeneratedMerkabaProgram
                    .CanonicalD4GaugeSerialization(transformed),
                    Is.EqualTo(mixedCanonical));
            }
        }

        [Test]
        public void ConstructiveCaptureBoundaryBuildsCalibratedEightLeafPair()
        {
            long[] target =
            {
                SigmaNumericDomain.One,
                -SigmaNumericDomain.One,
                SigmaNumericDomain.Half,
                -SigmaNumericDomain.Half,
            };
            SigmaInstrumentEyeBoundary Build(string side, long[] ray, char marker)
            {
                Assert.That(SigmaGeneratedMerkabaProgram
                    .TryBuildCalibratedRowPermutation(ray,
                        out int[] permutation, out _), Is.True);
                var code = new SigmaQ48Interval[4];
                for (int leaf = 0; leaf < 4; ++leaf)
                {
                    long value = SigmaNumericDomain.QAdd(
                        SigmaNumericDomain.Half,
                        SigmaNumericDomain.QShiftRight(target[permutation[leaf]], 3));
                    code[leaf] = new SigmaQ48Interval(value, value);
                }
                var footprint = new SigmaInstrumentFootprint(ray,
                    new[] { SigmaNumericDomain.FromRatio(1, 100), 0L, 0L },
                    new[] { 0L, SigmaNumericDomain.FromRatio(1, 100), 0L },
                    SigmaNumericDomain.FromRatio(1, 1000),
                    SigmaNumericDomain.FromRatio(1, 900),
                    SigmaNumericDomain.FromRatio(1, 10000));
                string fingerprint = new string(marker, 64);
                return new SigmaInstrumentEyeBoundary(side, 73UL, 11UL,
                    side == "LEFT" ? 101L : 102L,
                    side == "LEFT" ? 201L : 202L,
                    1000001L, 1000002L, 301UL, 401UL,
                    fingerprint, footprint, code[0],
                    new SigmaQ48Interval(SigmaNumericDomain.FromInteger(2),
                        SigmaNumericDomain.FromInteger(2)),
                    code.Skip(1).ToArray(),
                    SigmaInstrumentOpticalTransfer.SrgbDecodedLinear,
                    true, fingerprint);
            }

            SigmaInstrumentEyeBoundary left = Build("LEFT", new[]
            {
                SigmaNumericDomain.FromRatio(1, 5),
                SigmaNumericDomain.FromRatio(1, 3),
                SigmaNumericDomain.One,
            }, 'a');
            SigmaInstrumentEyeBoundary right = Build("RIGHT", new[]
            {
                SigmaNumericDomain.FromRatio(-1, 4),
                SigmaNumericDomain.FromRatio(2, 5),
                SigmaNumericDomain.One,
            }, 'b');
            Assert.That(SigmaGeneratedMerkabaProgram.TryAssembleSensorEye(
                left, out SigmaAssembledSensorEye leftQuery), Is.True);
            Assert.That(SigmaGeneratedMerkabaProgram.TryAssembleSensorEye(
                right, out SigmaAssembledSensorEye rightQuery), Is.True);
            Assert.That(leftQuery.Rows, Has.Length.EqualTo(4));
            Assert.That(rightQuery.Rows, Has.Length.EqualTo(4));
            Assert.That(leftQuery.Rows.SelectMany(row => row)
                .Count(value => value != 0L), Is.EqualTo(4));
            Assert.That(rightQuery.Rows.SelectMany(row => row)
                .Count(value => value != 0L), Is.EqualTo(4));
            Assert.That(leftQuery.Rows.SelectMany(row => row)
                .All(value => value == 0L ||
                    SigmaNumericDomain.QAbs(value) == SigmaNumericDomain.One),
                Is.True);
            Assert.That(leftQuery.Rows.Select(row => Array.FindIndex(row,
                    value => value != 0L)), Is.EquivalentTo(new[] { 0, 1, 2, 3 }));
            Assert.That(rightQuery.Rows.Select(row => Array.FindIndex(row,
                    value => value != 0L)), Is.EquivalentTo(new[] { 0, 1, 2, 3 }));
            CollectionAssert.AreNotEqual(
                leftQuery.Rows.SelectMany(row => row).ToArray(),
                rightQuery.Rows.SelectMany(row => row).ToArray(),
                "Left/right calibrated rays must construct their own row routing.");

            foreach (SigmaAssembledSensorEye query in new[] { leftQuery, rightQuery })
            {
                var pulledBack = new long[4];
                for (int leaf = 0; leaf < 4; ++leaf)
                {
                    int axis = Array.FindIndex(query.Rows[leaf], value => value != 0L);
                    int sign = query.Rows[leaf][axis] < 0L ? -1 : 1;
                    Assert.That(query.Measured[leaf].Lower,
                        Is.EqualTo(query.Measured[leaf].Upper));
                    pulledBack[axis] = sign > 0
                        ? query.Measured[leaf].Lower
                        : SigmaNumericDomain.QNegate(query.Measured[leaf].Lower);
                }
                CollectionAssert.AreEqual(target, pulledBack);
                Assert.That(query.MetricDirectOrder,
                    Is.EqualTo(new SigmaQ48Interval(
                        SigmaNumericDomain.FromInteger(2),
                        SigmaNumericDomain.FromInteger(2))));
                Assert.That(query.Footprint.SolidAngle, Is.GreaterThan(0L));
            }

            SigmaInstrumentEyeBoundary unsupported = new("LEFT", 73UL, 11UL,
                101L, 201L, 1000001L, 1000002L, 301UL, 401UL,
                new string('c', 64), left.Footprint, left.ProjectionDepth01,
                left.MetricDirectOrder, left.OpticalCode,
                SigmaInstrumentOpticalTransfer.Unsupported, true,
                new string('c', 64));
            Assert.That(SigmaGeneratedMerkabaProgram.TryAssembleSensorEye(
                unsupported, out _), Is.False);
        }

        [Test]
        public void SignedXorAssociatorDiffractionMetricAndHolonomyAreExact()
        {
            int nonzeroAssociators = 0;
            int negativeHolonomies = 0;
            for (int a = 0; a < 16; ++a)
            {
                for (int b = 0; b < 16; ++b)
                {
                    Assert.That(SigmaGeneratedMerkabaProgram.BasisSign(a, b),
                        Is.EqualTo(SigmaS16Operators.BasisProductSign(a, b)));
                    for (int c = 0; c < 16; ++c)
                    {
                        int coefficient =
                            SigmaGeneratedMerkabaProgram.AssociatorCoefficient(a, b, c);
                        SigmaS16 actual = SigmaS16Operators.Associator(
                            SigmaS16.Basis(a, SigmaNumericDomain.One),
                            SigmaS16.Basis(b, SigmaNumericDomain.One),
                            SigmaS16.Basis(c, SigmaNumericDomain.One));
                        SigmaS16 expected = SigmaS16.Basis(a ^ b ^ c,
                            coefficient * SigmaNumericDomain.One);
                        Assert.That(actual, Is.EqualTo(expected),
                            $"associator ({a},{b},{c})");
                        nonzeroAssociators += coefficient != 0 ? 1 : 0;
                        int holonomy = SigmaGeneratedMerkabaProgram
                            .PlaquetteHolonomy(a, c, b);
                        Assert.That(holonomy == -1 || holonomy == 1, Is.True);
                        negativeHolonomies += holonomy < 0 ? 1 : 0;
                    }
                }
            }
            Assert.That(nonzeroAssociators,
                Is.EqualTo(SigmaGeneratedMerkabaProgram.AssociatorNonzeroBasisTriples));
            Assert.That(negativeHolonomies,
                Is.EqualTo(SigmaGeneratedMerkabaProgram.NegativeHolonomyFixtures));

            for (int row = 0; row < 16; ++row)
            {
                for (int column = 0; column < 16; ++column)
                {
                    int a = SigmaGeneratedMerkabaProgram.DiffractionMatrix[
                        (row << 4) + column];
                    Assert.That(a, Is.EqualTo(-SigmaGeneratedMerkabaProgram
                        .DiffractionMatrix[(column << 4) + row]));
                    int ata = 0;
                    int square = 0;
                    for (int inner = 0; inner < 16; ++inner)
                    {
                        ata += SigmaGeneratedMerkabaProgram.DiffractionMatrix[
                                   (inner << 4) + row] *
                               SigmaGeneratedMerkabaProgram.DiffractionMatrix[
                                   (inner << 4) + column];
                        square += SigmaGeneratedMerkabaProgram.DiffractionMatrix[
                                      (row << 4) + inner] *
                                  SigmaGeneratedMerkabaProgram.DiffractionMatrix[
                                      (inner << 4) + column];
                    }
                    int metric = SigmaGeneratedMerkabaProgram.InformationMetric[
                        (row << 4) + column];
                    Assert.That(metric, Is.EqualTo(2 * ata));
                    Assert.That(metric, Is.EqualTo(-2 * square));
                }
            }
            Assert.That(SigmaGeneratedMerkabaProgram.IndependentClosureWeightCount,
                Is.Zero);
            Assert.That(SigmaGeneratedMerkabaProgram.EpsilonClExists, Is.False);
        }

        [Test]
        public void ShadowKernelDefaultAndExactFactorClassesFailClosed()
        {
            CollectionAssert.AreEqual(new sbyte[] { -1, -3, -7, -15 },
                SigmaGeneratedMerkabaProgram.ShellSquareByRank);
            Assert.That(SigmaGeneratedMerkabaProgram.CanFreezeShadowKernel, Is.False);
            Assert.That(SigmaGeneratedMerkabaProgram.IsZEmpty(SigmaS16.Zero), Is.True);
            Assert.That(SigmaGeneratedMerkabaProgram.IsZEmpty(
                SigmaS16.Basis(3, 1L)), Is.False);
            Assert.That(SigmaGeneratedMerkabaProgram.LegacyZNullAccepted, Is.False);
            SigmaS16[] defaultRepresentations =
            {
                SigmaGeneratedMerkabaProgram.DecodeDefaultRepresentation(
                    SigmaDefaultBackingKind.LogicalUnbacked),
                SigmaGeneratedMerkabaProgram.DecodeDefaultRepresentation(
                    SigmaDefaultBackingKind.ExplicitZEmpty),
                SigmaGeneratedMerkabaProgram.DecodeDefaultRepresentation(
                    SigmaDefaultBackingKind.NullCodec),
            };
            Assert.That(defaultRepresentations.All(value => value == SigmaS16.Zero),
                Is.True);
            Assert.That(SigmaGeneratedMerkabaProgram.DefaultRepresentationCount,
                Is.EqualTo(defaultRepresentations.Length));
            Assert.That(SigmaGeneratedMerkabaProgram.DefaultRepresentationQueryCount,
                Is.EqualTo(SigmaGeneratedMerkabaProgram.EntryPointCount));
            Assert.That(SigmaGeneratedMerkabaProgram.DefaultRepresentationFixtureCount,
                Is.EqualTo(defaultRepresentations.Length *
                    SigmaGeneratedMerkabaProgram.EntryPointCount));
            Assert.That(SigmaGeneratedMerkabaProgram.ClassifyAllDefault(
                    SigmaS16.Zero, SigmaS16.Zero),
                Is.EqualTo(SigmaMerkabaRelationClass.DefaultSat));
            Assert.That(SigmaGeneratedMerkabaProgram.AllDefaultActiveWork, Is.Zero);
            Assert.That(SigmaGeneratedMerkabaProgram.ClassifyExactZeroFactor(
                    new SigmaQ48Interval(1L, 2L)),
                Is.EqualTo(SigmaExactFactorClass.ProvenIncompatible));
            Assert.That(SigmaGeneratedMerkabaProgram.ClassifyExactZeroFactor(
                    new SigmaQ48Interval(0L, 0L)),
                Is.EqualTo(SigmaExactFactorClass.ProvenExactClosed));
            Assert.That(SigmaGeneratedMerkabaProgram.ClassifyExactZeroFactor(
                    new SigmaQ48Interval(-1L, 1L)),
                Is.EqualTo(SigmaExactFactorClass.Unresolved));

            Assert.That(SigmaGeneratedMerkabaProgram.TryNormalizePrimitiveDefect(
                SigmaS16.Basis(1, SigmaNumericDomain.One), out var normalized,
                out bool kernel), Is.True);
            Assert.That(kernel, Is.False);
            Assert.That(normalized, Has.Length.EqualTo(16));
            Assert.That(normalized[1].IsEmpty, Is.False);
            Assert.That(SigmaGeneratedMerkabaProgram.TryNormalizePrimitiveDefect(
                SigmaS16.Basis(0, SigmaNumericDomain.One), out _, out kernel),
                Is.False, "A nonzero diffraction-kernel factor must remain unresolved.");
            Assert.That(kernel, Is.True);
        }

        [Test]
        public void QuerySupportSummaryMatchesIndependentExhaustiveEvaluation()
        {
            int fixtures = 0;
            int falseNegatives = 0;
            foreach (bool refined in new[] { false, true })
            foreach (int storageClass in Enumerable.Range(0, 4))
            foreach (int stateMask in Enumerable.Range(0, 16))
            foreach (bool boundaryClosed in new[] { false, true })
            foreach (bool fingerprintsMatch in new[] { false, true })
            {
                _ = refined;
                _ = storageClass;
                bool exhaustiveContribution = stateMask != 0 || !boundaryClosed;
                bool omit = SigmaGeneratedMerkabaProgram.CanOmitQueryRegion(
                    stateMask == 0, boundaryClosed, fingerprintsMatch);
                falseNegatives += omit && exhaustiveContribution ? 1 : 0;
                ++fixtures;
            }
            Assert.That(falseNegatives, Is.Zero);
            Assert.That(fixtures,
                Is.EqualTo(SigmaGeneratedMerkabaProgram.QuerySupportFixtureCount));
            Assert.That(SigmaGeneratedMerkabaProgram.QuerySupportFalseNegatives,
                Is.Zero);
            Assert.That(SigmaGeneratedMerkabaProgram.QuerySupportRefinedFixtureCount,
                Is.GreaterThan(0));
            Assert.That(
                SigmaGeneratedMerkabaProgram.QuerySupportNonresidentFixtureCount,
                Is.GreaterThan(0));
            Assert.That(
                SigmaGeneratedMerkabaProgram.QuerySupportEvaluationFingerprint,
                Has.Length.EqualTo(64));
        }

        [Test]
        public void DirectionalActionCarriesIntervalsAndStopsAtFirstHit()
        {
            var direction = new SigmaQ48Interval(SigmaNumericDomain.One,
                SigmaNumericDomain.One);
            var residual = new SigmaQ48Interval(SigmaNumericDomain.Half,
                SigmaNumericDomain.Half);
            SigmaDirectionalActionWitness none = SigmaGeneratedMerkabaProgram
                .BuildDirectionalAction(SigmaNativeQueryClaim.NoClaim,
                    direction, residual);
            Assert.That(none.Active, Is.False);
            Assert.That(none.Action, Is.EqualTo(new SigmaQ48Interval(0L, 0L)));
            SigmaDirectionalActionWitness pre = SigmaGeneratedMerkabaProgram
                .BuildDirectionalAction(SigmaNativeQueryClaim.PreHitExclusion,
                    direction, residual);
            Assert.That(pre.Active, Is.True);
            Assert.That(pre.Action.Contains(SigmaNumericDomain.Half), Is.True);
            Assert.That(pre.StopsAtMeasuredMould, Is.False);
            SigmaDirectionalActionWitness mould = SigmaGeneratedMerkabaProgram
                .BuildDirectionalAction(SigmaNativeQueryClaim.FirstHitMould,
                    direction, residual);
            Assert.That(mould.Active, Is.True);
            Assert.That(mould.StopsAtMeasuredMould, Is.True);
            Assert.That(mould.Action.Contains(SigmaNumericDomain.Half), Is.True);
            Assert.That(SigmaGeneratedMerkabaProgram.BehindHitProducesAction, Is.False);
        }

        [Test]
        public void GeneratedReverseDescriptorsRetainZeroBranchesAndSceneDisjunction()
        {
            SigmaMerkabaExpression associator = SigmaGeneratedMerkabaProgram.Expressions
                .Single(expression => expression.Id == "K16_ASSOCIATOR");
            SigmaMerkabaIrNode[] nodes = SigmaGeneratedMerkabaProgram.IrNodes
                .Skip(associator.NodeStart).Take(associator.NodeCount).ToArray();
            Assert.That(nodes.Count(node =>
                    node.Opcode == SigmaMerkabaIrOpcode.S16_MULTIPLY), Is.EqualTo(4));
            Assert.That(nodes.Where(node =>
                    node.Opcode == SigmaMerkabaIrOpcode.S16_MULTIPLY)
                .All(node => node.ReverseRule ==
                    SigmaMerkabaReverseRule.OUTWARD_PRODUCT_ZERO_BRANCH_UNION),
                Is.True);
            Assert.That(SigmaGeneratedMerkabaProgram.IrNodes.Any(node =>
                node.Opcode == SigmaMerkabaIrOpcode.SCENE_REDUCE &&
                node.ReverseRule == SigmaMerkabaReverseRule.RETAIN_SUPPORT_DISJUNCTION),
                Is.True);
            Assert.That(SigmaGeneratedMerkabaProgram.ReverseZeroBranchRetained, Is.True);
            Assert.That(SigmaGeneratedMerkabaProgram.ReverseSceneDisjunctionCount,
                Is.GreaterThan(0));
            Assert.That(SigmaGeneratedMerkabaProgram.BracketNegativeControlCount,
                Is.EqualTo(1));
            Assert.That(SigmaGeneratedMerkabaProgram.ReverseIntervalSoundFixtureCount,
                Is.GreaterThan(5000));
            Assert.That(SigmaGeneratedMerkabaProgram.ReverseIrForwardFixtureCount,
                Is.EqualTo(17 * 17 * 17));
            Assert.That(SigmaGeneratedMerkabaProgram.ReverseIrAssociatorFingerprint,
                Is.EqualTo(associator.Fingerprint));
            Assert.That(
                SigmaGeneratedMerkabaProgram.ReverseIrAmbiguousPreimageOutputCount,
                Is.GreaterThan(0));
            Assert.That(SigmaGeneratedMerkabaProgram.ReverseIrMaxPreimageCount,
                Is.GreaterThan(1));
        }

        [Test]
        public void CertificateMinimizerIsExactBoundedAndCouplingAware()
        {
            var factors = new List<SigmaCertificateFactor>(10003);
            for (int index = 0; index < 10000; ++index)
                factors.Add(Factor("s0", "e0", "i0", "p0", "c0", "b0", -2, 3));
            factors.Add(Factor("s0", "e0", "i0", "p0", "c0", "b0", -4, 9));
            factors.Add(Factor("s1", "e1", "i1", "p1", "ca", "ba", -1, 2));
            factors.Add(Factor("s1", "e1", "i1", "p1", "cb", "bb", -1, 2));
            IReadOnlyList<SigmaMinimizedFactor> minimized =
                SigmaGeneratedMerkabaProgram.MinimizeCertificates(factors);
            Assert.That(minimized.Count, Is.EqualTo(
                SigmaGeneratedMerkabaProgram.DuplicateMinimizedFactorCount));
            SigmaMinimizedFactor duplicate = minimized.Single(value =>
                value.Factor.Scope == "s0");
            Assert.That(duplicate.Multiplicity,
                Is.EqualTo(SigmaGeneratedMerkabaProgram.DuplicateMultiplicity));
            Assert.That(minimized.Count(value => value.Factor.Scope == "s1"),
                Is.EqualTo(2), "Coupled/disjunctive factors must remain distinct.");
            for (long candidate = -6; candidate <= 11; ++candidate)
            {
                bool exhaustive = factors.Where(value => value.Scope == "s0")
                    .All(value => value.Lower <= candidate && candidate <= value.Upper);
                bool compact = minimized.Where(value => value.Factor.Scope == "s0")
                    .All(value => value.Factor.Lower <= candidate &&
                                  candidate <= value.Factor.Upper);
                Assert.That(compact, Is.EqualTo(exhaustive));
            }
        }

        [Test]
        public void GaugeSplitTransportCollapseAndNormalizationAreConstructive()
        {
            var parent = new SigmaGaugeCell(5, -3, 0,
                "state+factor+relation+evidence+information+bandwidth-A");
            SigmaGaugeCell[] children =
                SigmaGeneratedMerkabaProgram.SplitGaugeCell(parent);
            SigmaGaugeCell[] grandchildren = children.SelectMany(
                SigmaGeneratedMerkabaProgram.SplitGaugeCell).ToArray();
            Assert.That(children, Has.Length.EqualTo(4));
            Assert.That(grandchildren, Has.Length.EqualTo(16));
            Assert.That(children.All(child => child.PayloadFingerprint ==
                parent.PayloadFingerprint), Is.True);
            Assert.That(grandchildren.All(child => child.PayloadFingerprint ==
                parent.PayloadFingerprint), Is.True);
            string parentSerialization =
                SigmaGeneratedMerkabaProgram.CanonicalGaugeSerialization(new[] { parent });
            string childSerialization =
                SigmaGeneratedMerkabaProgram.CanonicalGaugeSerialization(children);
            string grandchildSerialization =
                SigmaGeneratedMerkabaProgram.CanonicalGaugeSerialization(grandchildren);
            Assert.That(childSerialization, Is.EqualTo(parentSerialization));
            Assert.That(grandchildSerialization, Is.EqualTo(parentSerialization));

            SigmaGaugeCell[] translated = children.Select(child =>
                new SigmaGaugeCell(child.U + 7 * (1L << child.Level),
                    child.V - 5 * (1L << child.Level), child.Level,
                    child.PayloadFingerprint)).ToArray();
            Assert.That(SigmaGeneratedMerkabaProgram.CanonicalGaugeSerialization(
                    translated), Is.EqualTo(parentSerialization));
            foreach (SigmaGaugeCell[] permutation in Permutations(children))
                Assert.That(SigmaGeneratedMerkabaProgram.CanonicalGaugeSerialization(
                    permutation), Is.EqualTo(parentSerialization));
            SigmaGaugeCell[] wide =
            {
                new(0L, 1L, 0, "wide-a"),
                new(0L, 1L << 32, 0, "wide-b"),
                new(1L, 0L, 0, "wide-c"),
                new(1L << 32, 0L, 0, "wide-d"),
            };
            string wideNormal = SigmaGeneratedMerkabaProgram
                .CanonicalGaugeSerialization(wide);
            foreach (SigmaGaugeCell[] permutation in Permutations(wide))
                Assert.That(SigmaGeneratedMerkabaProgram
                    .CanonicalGaugeSerialization(permutation),
                    Is.EqualTo(wideNormal));
            Assert.That(SigmaGeneratedMerkabaProgram.TryNormalizeFreshSupport(
                new IEnumerable<SigmaGaugeCell>[] { children, translated },
                out string freshSerialization), Is.True);
            Assert.That(freshSerialization, Is.EqualTo(parentSerialization));
            Assert.That(SigmaGeneratedMerkabaProgram.TryNormalizeFreshSupport(
                new IEnumerable<SigmaGaugeCell>[]
                {
                    children,
                    children.Append(new SigmaGaugeCell(9, 9, 0,
                        parent.PayloadFingerprint)),
                }, out _), Is.False);
            Assert.Throws<InvalidOperationException>(() =>
                SigmaGeneratedMerkabaProgram.NormalizeGauge(new[]
                {
                    parent,
                    new SigmaGaugeCell(10, -6, 1, parent.PayloadFingerprint),
                }));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new SigmaGaugeCell(0, 0, 63, parent.PayloadFingerprint));
            Assert.That(SigmaGeneratedMerkabaProgram.GaugePermutationCount,
                Is.EqualTo(24));
            Assert.That(SigmaGeneratedMerkabaProgram.GaugeTransportFieldCount,
                Is.EqualTo(6));
            Assert.That(SigmaGeneratedMerkabaProgram.FreshSupportUniqueModuloGauge,
                Is.True);
            Assert.That(
                SigmaGeneratedMerkabaProgram.FreshSupportNonEquivalentRejected,
                Is.True);
        }

        [Test]
        public void FreshAdmissionIsGeneratedFromShadowPreimageNotProposedState()
        {
            long[] target =
            {
                SigmaNumericDomain.One,
                -SigmaNumericDomain.One,
                SigmaNumericDomain.Half,
                -SigmaNumericDomain.Half,
            };
            SigmaQ48Interval[] exact = target.Select(value =>
                new SigmaQ48Interval(value, value)).ToArray();
            var branch = new SigmaFreshShadowBranch(exact, 3u, true,
                "left+right");
            Assert.That(SigmaGeneratedMerkabaProgram.TryResolveFreshBaseAdmission(
                new[] { branch, branch }, out SigmaFreshBaseAdmission admission),
                Is.True);
            Assert.That(admission.Status, Is.EqualTo(SigmaFreshAdmissionStatus.Admitted));
            Assert.That(admission.State.IsZero, Is.False);
            CollectionAssert.AreEqual(target,
                SigmaGeneratedMerkabaProgram.EvaluateMerkabaShadow(admission.State));
            Assert.That(admission.Support.Count, Is.EqualTo(1));
            Assert.That(admission.Support[0].U, Is.Zero);
            Assert.That(admission.Support[0].V, Is.Zero);
            Assert.That(admission.Support[0].Level, Is.Zero);
            Assert.That(admission.BoundaryRelation,
                Is.EqualTo(SigmaMerkabaRelationClass.NoRelation),
                "The mixed ZEmpty boundary must be derived by the generated law.");
            Assert.That(SigmaGeneratedMerkabaProgram
                    .FreshAdmissionExternalRelationTruthInputCount,
                Is.Zero);

            long[] otherTarget =
            {
                SigmaNumericDomain.Half,
                SigmaNumericDomain.Half,
                -SigmaNumericDomain.Half,
                -SigmaNumericDomain.Half,
            };
            var other = new SigmaFreshShadowBranch(otherTarget.Select(value =>
                new SigmaQ48Interval(value, value)), 3u, true, "other");
            Assert.That(SigmaGeneratedMerkabaProgram.TryResolveFreshBaseAdmission(
                new[] { branch, other }, out _), Is.False,
                "Non-equivalent reverse branches must remain unresolved.");
            Assert.That(SigmaGeneratedMerkabaProgram.TryResolveFreshBaseAdmission(
                new[] { new SigmaFreshShadowBranch(exact, 1u, true, "left-only") },
                out _), Is.False, "One eye cannot mint the coherent stereo base case.");
            Assert.That(SigmaGeneratedMerkabaProgram.TryResolveFreshBaseAdmission(
                new[] { new SigmaFreshShadowBranch(exact, 3u, false,
                    "incoherent") }, out _), Is.False);
            Assert.That(SigmaGeneratedMerkabaProgram.EvaluateFreshBoundaryRelation(
                    SigmaS16.Zero),
                Is.EqualTo(SigmaMerkabaRelationClass.DefaultSat));
            Assert.That(SigmaGeneratedMerkabaProgram.EvaluateFreshBoundaryRelation(
                    SigmaS16.Basis(0, SigmaNumericDomain.One)),
                Is.EqualTo(SigmaMerkabaRelationClass.Unresolved),
                "A nonzero diffraction-kernel boundary must fail closed.");
            Assert.That(SigmaGeneratedMerkabaProgram.EvaluateFreshBoundaryRelation(
                    SigmaS16.Basis(1, 1L)),
                Is.EqualTo(SigmaMerkabaRelationClass.NoRelation),
                "An exact one-LSB defect may not alias algebraic zero.");
            Assert.That(SigmaGeneratedMerkabaProgram.FreshAdmissionDualFrameRoundTripCount,
                Is.GreaterThan(100));
            Assert.That(SigmaGeneratedMerkabaProgram.FreshAdmissionProofFingerprint,
                Has.Length.EqualTo(64));
        }

        [Test]
        public void ExactZeroDivisorAndOneLsbResidualDoNotAlias()
        {
            SigmaZeroDivisorEntry entry = SigmaS16Operators.GetZeroDivisorEntry(0);
            SigmaS16 witness = entry.Witness.ToS16();
            SigmaS16 annihilator = entry.Annihilator.ToS16();
            Assert.That(SigmaS16Operators.DenseReferenceMultiply(
                witness, annihilator).IsZero, Is.True);
            Assert.That(SigmaGeneratedMerkabaProgram.ClassifyZeroDivisor(
                    true, true, true, false),
                Is.EqualTo(SigmaMerkabaRelationClass.ExactZeroDivisor));
            bool foundNonzeroResidual = false;
            for (int lane = 0; lane < 16 && !foundNonzeroResidual; ++lane)
            {
                long[] perturbed = witness.ToArray();
                perturbed[lane] = checked(perturbed[lane] + 1L);
                foundNonzeroResidual = !SigmaS16Operators.DenseReferenceMultiply(
                    SigmaS16.FromArray(perturbed), annihilator).IsZero;
            }
            Assert.That(foundNonzeroResidual, Is.True);
            Assert.That(SigmaGeneratedMerkabaProgram.ClassifyZeroDivisor(
                    true, true, false, true),
                Is.EqualTo(SigmaMerkabaRelationClass.NearSingularQ48));
        }

        [Test]
        public void GeneratedFiniteDyadicAndD4LoweringsMatchSemanticReference()
        {
            string[] fixtureGuids = AssetDatabase.FindAssets(
                "SigmaMerkabaProgramFixture t:ComputeShader");
            Assert.That(fixtureGuids, Has.Length.EqualTo(1));
            ComputeShader shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                AssetDatabase.GUIDToAssetPath(fixtureGuids[0]));
            Assert.That(shader, Is.Not.Null);

            var values = new List<long>
            {
                long.MinValue, long.MinValue + 1L, long.MinValue + 2L,
                -65L, -64L, -63L, -33L, -32L, -31L, -17L, -16L, -15L,
                -3L, -2L, -1L, 0L, 1L, 2L, 3L, 15L, 16L, 17L,
                31L, 32L, 33L, 63L, 64L, 65L,
                long.MaxValue - 2L, long.MaxValue - 1L, long.MaxValue,
            };
            for (int bit = 1; bit < 63; bit += 3)
            {
                long power = 1L << bit;
                values.Add(power - 1L);
                values.Add(power);
                values.Add(power + 1L);
                values.Add(-power - 1L);
                values.Add(-power);
                values.Add(-power + 1L);
            }
            var random = new System.Random(0x4d45524b);
            var randomBytes = new byte[8];
            for (int index = 0; index < 128; ++index)
            {
                random.NextBytes(randomBytes);
                values.Add(BitConverter.ToInt64(randomBytes, 0));
            }
            long[] inputs = values.Distinct().ToArray();

            // The generated matrix has two all-zero endpoint rows and a dense
            // 14x4 interior. The hot lowering therefore uses fixed address
            // 1..14 and axis 0..3 loops with no runtime sparse schedule.
            for (int axis = 0; axis < 4; ++axis)
            {
                for (int address = 0; address < 16; ++address)
                {
                    int dense = SigmaGeneratedMerkabaProgram.ShadowNumerator(
                        address, axis);
                    Assert.That(dense == 0,
                        Is.EqualTo(address == 0 || address == 15),
                        $"address={address} axis={axis}");
                }
            }
            for (int fixture = 0; fixture < 128; ++fixture)
            {
                var lanes = new long[16];
                for (int lane = 0; lane < lanes.Length; ++lane)
                    lanes[lane] = random.Next(-1 << 20, 1 << 20);
                SigmaS16 state = SigmaS16.FromArray(lanes);
                long[] sparse = SigmaGeneratedMerkabaProgram
                    .EvaluateMerkabaShadow(state);
                for (int axis = 0; axis < 4; ++axis)
                {
                    long dense = 0L;
                    for (int address = 0; address < 16; ++address)
                        dense = SigmaNumericDomain.QAdd(dense,
                            SigmaGeneratedMerkabaProgram
                                .MultiplyMerkabaShadowCoefficient(
                                    lanes[address], address, axis));
                    Assert.That(sparse[axis], Is.EqualTo(dense),
                        $"shadow fixture={fixture} axis={axis}");
                }

                long[] shadowInput = Enumerable.Range(0, 4)
                    .Select(_ => (long)random.Next(-1 << 20, 1 << 20))
                    .ToArray();
                SigmaS16 lifted = SigmaGeneratedMerkabaProgram
                    .LiftMerkabaShadow(shadowInput);
                for (int address = 0; address < 16; ++address)
                {
                    long dense = 0L;
                    for (int axis = 0; axis < 4; ++axis)
                        dense = SigmaNumericDomain.QAdd(dense,
                            SigmaGeneratedMerkabaProgram
                                .MultiplyMerkabaDualCoefficient(
                                    shadowInput[axis], address, axis));
                    Assert.That(lifted[address], Is.EqualTo(dense),
                        $"dual fixture={fixture} address={address}");
                }
            }
            var packedInputs = inputs.Select(value => new UInt4
            {
                X = unchecked((uint)value),
                Y = unchecked((uint)((ulong)value >> 32)),
            }).ToArray();
            var dyadicResults = new UInt4[inputs.Length * 14];
            using (GraphicsBuffer inputBuffer = Buffer(packedInputs.Length))
            using (GraphicsBuffer resultBuffer = Buffer(dyadicResults.Length))
            {
                int kernel = shader.FindKernel("MerkabaDyadicParity");
                inputBuffer.SetData(packedInputs);
                shader.SetInt("_MerkabaDyadicInputCount", inputs.Length);
                shader.SetBuffer(kernel, "_MerkabaDyadicInputs", inputBuffer);
                shader.SetBuffer(kernel, "_MerkabaDyadicResults", resultBuffer);
                shader.Dispatch(kernel, (inputs.Length + 63) / 64, 1, 1);
                resultBuffer.GetData(dyadicResults);
            }

            for (int input = 0; input < inputs.Length; ++input)
            for (int coefficient = 0; coefficient < 7; ++coefficient)
            for (int kind = 0; kind < 2; ++kind)
            {
                int numerator = coefficient * 2 - 6;
                int denominatorShift = kind == 0 ? 2 : 6;
                bool expectedValid = true;
                long expected = 0L;
                try
                {
                    long semanticCoefficient = SigmaNumericDomain.FromRatio(
                        numerator, 1L << denominatorShift);
                    expected = SigmaNumericDomain.QMul(inputs[input],
                        semanticCoefficient);
                }
                catch (OverflowException)
                {
                    expectedValid = false;
                }

                if (expectedValid)
                    Assert.That(SigmaGeneratedMerkabaProgram
                        .MultiplyMerkabaDyadic(inputs[input], numerator,
                            denominatorShift), Is.EqualTo(expected));
                else
                    Assert.Throws<OverflowException>(() =>
                        SigmaGeneratedMerkabaProgram.MultiplyMerkabaDyadic(
                            inputs[input], numerator, denominatorShift));

                UInt4 actual = dyadicResults[(input * 7 + coefficient) * 2 +
                    kind];
                Assert.That(actual.Z, Is.EqualTo(expectedValid ? 1u : 0u),
                    $"validity input={inputs[input]} numerator={numerator} " +
                    $"shift={denominatorShift}");
                if (expectedValid)
                    Assert.That(Join(actual.X, actual.Y), Is.EqualTo(expected),
                        $"value input={inputs[input]} numerator={numerator} " +
                        $"shift={denominatorShift}");
            }

            var chartResults = new UInt4[843];
            using (GraphicsBuffer chartBuffer = Buffer(chartResults.Length))
            {
                int kernel = shader.FindKernel("MerkabaFiniteChartParity");
                shader.SetBuffer(kernel, "_MerkabaFiniteChartResults",
                    chartBuffer);
                shader.Dispatch(kernel, 4, 1, 1);
                chartBuffer.GetData(chartResults);
            }
            for (int outer = 0; outer < 8; ++outer)
            for (int inner = 0; inner < 8; ++inner)
            {
                SigmaChartD4Transform a =
                    SigmaGeneratedMerkabaProgram.ChartD4[outer];
                SigmaChartD4Transform b =
                    SigmaGeneratedMerkabaProgram.ChartD4[inner];
                int m00 = a.M00 * b.M00 + a.M01 * b.M10;
                int m01 = a.M00 * b.M01 + a.M01 * b.M11;
                int m10 = a.M10 * b.M00 + a.M11 * b.M10;
                int m11 = a.M10 * b.M01 + a.M11 * b.M11;
                int expected = Array.FindIndex(
                    SigmaGeneratedMerkabaProgram.ChartD4, value =>
                        value.M00 == m00 && value.M01 == m01 &&
                        value.M10 == m10 && value.M11 == m11);
                int index = outer * 8 + inner;
                Assert.That(SigmaGeneratedMerkabaProgram.ComposeChartD4(
                    outer, inner), Is.EqualTo(expected));
                Assert.That(chartResults[index].X, Is.EqualTo((uint)expected));
                Assert.That(chartResults[index].Y, Is.EqualTo(1u));
            }
            for (int frame = 0; frame < 8; ++frame)
            {
                int expected = Enumerable.Range(0, 8).Single(candidate =>
                    SigmaGeneratedMerkabaProgram.ComposeChartD4(candidate,
                        frame) == 0);
                Assert.That(SigmaGeneratedMerkabaProgram.InverseChartD4(frame),
                    Is.EqualTo(expected));
                Assert.That(chartResults[64 + frame].X,
                    Is.EqualTo((uint)expected));
            }
            for (int orbit = 0; orbit < 3; ++orbit)
            {
                int expected = Enumerable.Range(0,
                    SigmaGeneratedMerkabaProgram.NativeSectorChartAssignmentCount)
                    .First(index => NativeSectorChartOrbit(
                        SigmaGeneratedMerkabaProgram
                            .NativeSectorChartAssignments[index]) == orbit);
                Assert.That(SigmaGeneratedMerkabaProgram
                    .ChartOrbitRepresentative(orbit), Is.EqualTo(expected));
                Assert.That(chartResults[72 + orbit].X,
                    Is.EqualTo((uint)expected));
            }

            int adjacentIndex = 75;
            for (int orbit = 0; orbit < 3; ++orbit)
            for (int currentFrame = 0; currentFrame < 8; ++currentFrame)
            for (int currentSector = 0; currentSector < 4; ++currentSector)
            for (int nextSector = 0; nextSector < 4; ++nextSector)
            for (int parityIndex = 0; parityIndex < 2; ++parityIndex)
            {
                int parity = parityIndex == 0 ? -1 : 1;
                int expected = ResolveAdjacentFrameReference(orbit,
                    currentFrame, currentSector, nextSector, parity);
                Assert.That(SigmaGeneratedMerkabaProgram
                    .ResolveAdjacentOrbitFrame(orbit, currentFrame,
                        currentSector, nextSector, parity), Is.EqualTo(expected));
                Assert.That(chartResults[adjacentIndex].X,
                    Is.EqualTo((uint)expected));
                Assert.That(chartResults[adjacentIndex].Y, Is.EqualTo(1u));
                ++adjacentIndex;
            }
            Assert.That(adjacentIndex, Is.EqualTo(chartResults.Length));
        }

        [Test]
        public void GeneratedHlslMatchesCpuTablesIrAndDirectionalAction()
        {
            string[] fixtureGuids = AssetDatabase.FindAssets(
                "SigmaMerkabaProgramFixture t:ComputeShader");
            Assert.That(fixtureGuids, Has.Length.EqualTo(1));
            ComputeShader shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                AssetDatabase.GUIDToAssetPath(fixtureGuids[0]));
            Assert.That(shader, Is.Not.Null);
            int programKernel = shader.FindKernel("MerkabaProgramParity");
            int matrixKernel = shader.FindKernel("MerkabaMatrixAndIrParity");
            int actionKernel = shader.FindKernel("MerkabaDirectionalActionParity");
            int freshKernel = shader.FindKernel("MerkabaFreshAdmissionParity");
            int instrumentKernel = shader.FindKernel(
                "MerkabaInstrumentBoundaryParity");
            int gaugeKernel = shader.FindKernel("MerkabaGaugeParity");
            int stitchKernel = shader.FindKernel("MerkabaStitchSetParity");
            var results = new UInt4[16 * 16 * 16];
            var matrices = new UInt4[16 * 16];
            var ir = new UInt4[SigmaGeneratedMerkabaProgram.IrNodeCount * 2];
            var actions = new UInt4[4];
            var fresh = new UInt4[23];
            var instrument = new UInt4[8];
            var gauge = new UInt4[40];
            var stitch = new UInt4[47];
            using var resultBuffer = Buffer(results.Length);
            using var matrixBuffer = Buffer(matrices.Length);
            using var irBuffer = Buffer(ir.Length);
            using var actionBuffer = Buffer(actions.Length);
            using var freshBuffer = Buffer(fresh.Length);
            using var instrumentBuffer = Buffer(instrument.Length);
            using var gaugeBuffer = Buffer(gauge.Length);
            using var stitchBuffer = Buffer(stitch.Length);
            shader.SetBuffer(programKernel, "_MerkabaResults", resultBuffer);
            shader.Dispatch(programKernel, 1, 1, 16);
            shader.SetBuffer(matrixKernel, "_MerkabaMatrixResults", matrixBuffer);
            shader.SetBuffer(matrixKernel, "_MerkabaIrResults", irBuffer);
            shader.Dispatch(matrixKernel, 1, 1, 1);
            shader.SetBuffer(actionKernel, "_MerkabaActionResults", actionBuffer);
            shader.Dispatch(actionKernel, 1, 1, 1);
            shader.SetBuffer(freshKernel, "_MerkabaFreshResults", freshBuffer);
            shader.Dispatch(freshKernel, 1, 1, 1);
            shader.SetBuffer(instrumentKernel, "_MerkabaInstrumentResults",
                instrumentBuffer);
            shader.Dispatch(instrumentKernel, 1, 1, 1);
            shader.SetBuffer(gaugeKernel, "_MerkabaGaugeResults", gaugeBuffer);
            shader.SetInts("_MerkabaGaugeParentCoordinate", 5, 0, -3, -1);
            shader.SetInt("_MerkabaGaugeParentLevel", 0);
            shader.Dispatch(gaugeKernel, 1, 1, 1);
            shader.SetBuffer(stitchKernel, "_MerkabaStitchResults", stitchBuffer);
            shader.Dispatch(stitchKernel, 1, 1, 1);
            resultBuffer.GetData(results);
            matrixBuffer.GetData(matrices);
            irBuffer.GetData(ir);
            actionBuffer.GetData(actions);
            freshBuffer.GetData(fresh);
            instrumentBuffer.GetData(instrument);
            gaugeBuffer.GetData(gauge);
            stitchBuffer.GetData(stitch);

            Assert.That(stitch.Take(32).Count(value => value.X ==
                    (uint)SigmaExactFactorClass.ProvenExactClosed),
                Is.EqualTo(2),
                "Forward and reversed actual S16 pairs each have one witness.");
            Assert.That(stitch[1].X, Is.EqualTo(
                (uint)SigmaExactFactorClass.ProvenExactClosed));
            Assert.That(stitch[16 + 4].X, Is.EqualTo(
                (uint)SigmaExactFactorClass.ProvenExactClosed));
            Assert.That(stitch[32].X, Is.EqualTo(
                (uint)SigmaStitchResolution.Resolved));
            Assert.That(stitch[32].Y, Is.Zero);
            Assert.That(stitch[32].Z, Is.EqualTo(1u));
            Assert.That(stitch[32].W, Is.EqualTo(3u));
            Assert.That(stitch[33].X, Is.EqualTo(
                (uint)SigmaStitchResolution.Resolved));
            Assert.That(stitch[33].Y, Is.EqualTo(1u));
            Assert.That(stitch[33].Z, Is.Zero);
            Assert.That(stitch[33].W, Is.EqualTo(3u));
            Assert.That(stitch[34].X, Is.EqualTo(1u));
            Assert.That(stitch[35].X, Is.Zero);
            Assert.That(stitch[36].X, Is.Zero);
            Assert.That(stitch.Skip(37).Take(8).All(value => value.W == 1u),
                Is.True);
            int TokenSign(string left, string right) => Math.Sign(
                SigmaGeneratedMerkabaProgram.CompareCanonicalTokens(
                    Encoding.ASCII.GetBytes(left),
                    Encoding.ASCII.GetBytes(right)));
            Assert.That(unchecked((int)stitch[45].X),
                Is.EqualTo(TokenSign("2", "10")));
            Assert.That(unchecked((int)stitch[45].Y),
                Is.EqualTo(TokenSign("-2", "-10")));
            Assert.That(unchecked((int)stitch[45].Z),
                Is.EqualTo(TokenSign("0000000100000000",
                    "00000000ffffffff")));
            Assert.That(unchecked((int)stitch[46].X),
                Is.EqualTo(TokenSign("2", "10")),
                "Vulkan must use the accepted textual prefix order.");
            Assert.That(unchecked((int)stitch[46].Y),
                Is.EqualTo(TokenSign(new string('p', 64),
                    new string('y', 64))),
                "The full provenance receipt must decide the complete suffix.");
            Assert.That(unchecked((int)stitch[46].Z),
                Is.EqualTo(TokenSign(new string('p', 64),
                    new string('y', 64))),
                "A complete 256-bit receipt compare may not collapse to a hash.");
            var d4Probe = new SigmaGaugeCell(2, -3, 0, "probe");
            for (int image = 0; image < 8; ++image)
            {
                SigmaGaugeCell expected = SigmaGeneratedMerkabaProgram
                    .ApplyChartD4(new[] { d4Probe }, image)[0];
                UInt4 actual = stitch[37 + image];
                Assert.That((long)unchecked((int)actual.X),
                    Is.EqualTo(expected.U));
                Assert.That((long)unchecked((int)actual.Y),
                    Is.EqualTo(expected.V));
                Assert.That(actual.Z, Is.EqualTo((uint)image));
            }

            for (int c = 0; c < 16; ++c)
            for (int b = 0; b < 16; ++b)
            for (int a = 0; a < 16; ++a)
            {
                UInt4 actual = results[(c * 16 + b) * 16 + a];
                Assert.That(unchecked((int)actual.X), Is.EqualTo(
                    SigmaGeneratedMerkabaProgram.AssociatorCoefficient(a, b, c)));
                Assert.That(unchecked((int)actual.Y), Is.EqualTo(
                    SigmaGeneratedMerkabaProgram.PlaquetteHolonomy(a, c, b)));
                Assert.That(unchecked((int)actual.Z), Is.EqualTo(
                    SigmaGeneratedMerkabaProgram.ShadowNumerator(a, c & 3)));
                Assert.That(unchecked((int)actual.W), Is.EqualTo(
                    SigmaGeneratedMerkabaProgram.BasisSign(a, b)));
            }
            for (int index = 0; index < matrices.Length; ++index)
            {
                Assert.That(unchecked((int)matrices[index].X), Is.EqualTo(
                    SigmaGeneratedMerkabaProgram.DiffractionMatrix[index]));
                Assert.That(unchecked((int)matrices[index].Y), Is.EqualTo(
                    SigmaGeneratedMerkabaProgram.InformationMetric[index]));
                Assert.That(unchecked((int)matrices[index].Z), Is.EqualTo(
                    SigmaGeneratedMerkabaProgram.VisibleProjectorNumerator256[index]));
                Assert.That(matrices[index].W, Is.Zero);
            }
            for (int index = 0; index < SigmaGeneratedMerkabaProgram.IrNodeCount; ++index)
            {
                SigmaMerkabaIrNode expected = SigmaGeneratedMerkabaProgram.IrNodes[index];
                Assert.That(ir[index * 2].X, Is.EqualTo((uint)expected.Opcode));
                Assert.That(ir[index * 2].Y, Is.EqualTo((uint)expected.OutputKind));
                Assert.That(ir[index * 2].Z, Is.EqualTo((uint)expected.ReverseRule));
                Assert.That(ir[index * 2].W, Is.EqualTo((uint)expected.OperandStart));
                Assert.That(unchecked((int)ir[index * 2 + 1].X),
                    Is.EqualTo(expected.OperandCount));
                Assert.That(unchecked((int)ir[index * 2 + 1].Y),
                    Is.EqualTo(expected.Argument0));
                Assert.That(unchecked((int)ir[index * 2 + 1].Z),
                    Is.EqualTo(expected.Argument1));
            }
            Assert.That(actions[0].Y, Is.Zero);
            Assert.That(actions[0].Z | actions[0].W, Is.Zero);
            Assert.That(actions[1].X,
                Is.EqualTo((uint)SigmaNativeQueryClaim.FirstHitMould));
            Assert.That(actions[1].Y, Is.EqualTo(1u));
            Assert.That(actions[1].Z, Is.Zero);
            Assert.That(actions[1].W, Is.EqualTo(0x00008000u));
            Assert.That(actions[2].X, Is.Zero);
            Assert.That(actions[2].Y, Is.EqualTo(0x00008000u));
            Assert.That(actions[2].Z, Is.EqualTo(1u));
            Assert.That(actions[2].W, Is.Zero);
            Assert.That(actions[3].X | actions[3].Y | actions[3].Z, Is.Zero);
            Assert.That(actions[3].W, Is.EqualTo(1u));
            long[] expectedShadow =
            {
                SigmaNumericDomain.One, -SigmaNumericDomain.One,
                SigmaNumericDomain.Half, -SigmaNumericDomain.Half,
            };
            SigmaS16 expectedState = SigmaGeneratedMerkabaProgram
                .LiftMerkabaShadow(expectedShadow);
            for (int axis = 0; axis < 4; ++axis)
            {
                Assert.That(Join(fresh[axis].X, fresh[axis].Y),
                    Is.EqualTo(expectedShadow[axis]));
                Assert.That(Join(fresh[axis].Z, fresh[axis].W),
                    Is.EqualTo(expectedShadow[axis]));
            }
            for (int lane = 0; lane < 16; ++lane)
                Assert.That(Join(fresh[4 + lane].X, fresh[4 + lane].Y),
                    Is.EqualTo(expectedState[lane]));
            Assert.That(fresh[20].X, Is.EqualTo(1u));
            Assert.That(fresh[20].Y, Is.EqualTo(1u));
            Assert.That(fresh[20].Z,
                Is.EqualTo((uint)SigmaFreshAdmissionStatus.Admitted));
            Assert.That(fresh[20].W,
                Is.EqualTo((uint)SigmaMerkabaRelationClass.NoRelation));
            Assert.That(fresh[21].X,
                Is.EqualTo((uint)SigmaGeneratedMerkabaProgram.ExpressionCount));
            Assert.That(fresh[21].Y, Is.Zero,
                "The GPU fresh program must not consume an external relation truth.");
            Assert.That(fresh[21].Z,
                Is.EqualTo((uint)SigmaMerkabaRelationClass.Unresolved));
            Assert.That(fresh[21].W, Is.EqualTo(1u));
            Assert.That(fresh[22].X,
                Is.EqualTo((uint)SigmaMerkabaRelationClass.NoRelation));
            Assert.That(fresh[22].Y, Is.EqualTo(1u));

            long[] instrumentRay =
            {
                SigmaNumericDomain.FromRatio(1, 4),
                SigmaNumericDomain.Half,
                SigmaNumericDomain.One,
            };
            Assert.That(SigmaGeneratedMerkabaProgram
                .TryBuildCalibratedRowPermutation(instrumentRay,
                    out int[] expectedPermutation, out int expectedSign), Is.True);
            long[] expectedInstrumentShadow =
            {
                SigmaNumericDomain.One,
                -SigmaNumericDomain.One,
                SigmaNumericDomain.Half,
                -SigmaNumericDomain.Half,
            };
            for (int leaf = 0; leaf < 4; ++leaf)
            {
                Assert.That(instrument[leaf].X,
                    Is.EqualTo((uint)expectedPermutation[leaf]),
                    $"leaf={leaf} cpu={string.Join(",", expectedPermutation)} " +
                    $"gpu={string.Join(",", instrument.Take(4).Select(value => value.X))}");
                Assert.That(unchecked((int)instrument[leaf].Y),
                    Is.EqualTo(expectedSign));
                Assert.That(instrument[leaf].Z, Is.EqualTo(1u));
                Assert.That(instrument[leaf].W, Is.EqualTo(1u));
                long expected = expectedSign > 0
                    ? expectedInstrumentShadow[leaf]
                    : SigmaNumericDomain.QNegate(expectedInstrumentShadow[leaf]);
                Assert.That(Join(instrument[4 + leaf].X,
                    instrument[4 + leaf].Y), Is.EqualTo(expected));
                Assert.That(Join(instrument[4 + leaf].Z,
                    instrument[4 + leaf].W), Is.EqualTo(expected));
            }
            SigmaGaugeCell[] expectedChildren =
                SigmaGeneratedMerkabaProgram.SplitGaugeCell(
                    new SigmaGaugeCell(5L, -3L, 0, "gauge-parity"));
            for (int child = 0; child < expectedChildren.Length; ++child)
            {
                Assert.That(Join(gauge[child * 2].X,
                    gauge[child * 2].Y), Is.EqualTo(expectedChildren[child].U));
                Assert.That(Join(gauge[child * 2].Z,
                    gauge[child * 2].W), Is.EqualTo(expectedChildren[child].V));
                Assert.That(gauge[child * 2 + 1].X,
                    Is.EqualTo((uint)expectedChildren[child].Level));
                Assert.That(gauge[child * 2 + 1].Y, Is.EqualTo(1u));
                for (int peer = 0; peer < expectedChildren.Length; ++peer)
                {
                    UInt4 order = gauge[8 + child * 4 + peer];
                    Assert.That(order.Y, Is.EqualTo(1u));
                    Assert.That(order.X, Is.EqualTo(
                        GaugeLess(expectedChildren[child], expectedChildren[peer])
                            ? 1u : 0u));
                }
            }
            SigmaGaugeCell[] wideCoordinates =
            {
                new(0L, 1L, 0, "wide-0"),
                new(0L, 1L << 32, 0, "wide-1"),
                new(1L, 0L, 0, "wide-2"),
                new(1L << 32, 0L, 0, "wide-3"),
            };
            for (int index = 0; index < wideCoordinates.Length; ++index)
            for (int peer = 0; peer < wideCoordinates.Length; ++peer)
            {
                UInt4 order = gauge[24 + index * 4 + peer];
                Assert.That(order.Y, Is.EqualTo(1u));
                Assert.That(order.X, Is.EqualTo(
                    GaugeLess(wideCoordinates[index], wideCoordinates[peer])
                        ? 1u : 0u));
            }
        }

        private static int NativeSectorChartOrbit(IReadOnlyList<int> assignment)
        {
            int opposite = (assignment[0] + 2) & 3;
            return (assignment[2] == opposite ? 1 : 0) |
                (assignment[3] == opposite ? 2 : 0);
        }

        private static int ResolveAdjacentFrameReference(int orbit,
            int currentFrame, int currentSector, int nextSector, int parity)
        {
            int assignmentIndex = Enumerable.Range(0,
                    SigmaGeneratedMerkabaProgram.NativeSectorChartAssignmentCount)
                .First(index => NativeSectorChartOrbit(
                    SigmaGeneratedMerkabaProgram
                        .NativeSectorChartAssignments[index]) == orbit);
            IReadOnlyList<int> assignment = SigmaGeneratedMerkabaProgram
                .NativeSectorChartAssignments[assignmentIndex];
            (int U, int V) Direction(int frameIndex, int sector)
            {
                (int U, int V)[] directions =
                {
                    (1, 0), (0, 1), (-1, 0), (0, -1),
                };
                (int U, int V) source = directions[assignment[sector]];
                SigmaChartD4Transform transform =
                    SigmaGeneratedMerkabaProgram.ChartD4[frameIndex];
                return (transform.M00 * source.U + transform.M01 * source.V,
                    transform.M10 * source.U + transform.M11 * source.V);
            }
            (int U, int V) current = Direction(currentFrame, currentSector);
            int requiredDeterminant = SigmaGeneratedMerkabaProgram
                .ChartD4[currentFrame].Determinant * parity;
            int[] matches = Enumerable.Range(0, 8).Where(candidate =>
            {
                (int U, int V) reverse = Direction(candidate, nextSector);
                return reverse.U == -current.U && reverse.V == -current.V &&
                    SigmaGeneratedMerkabaProgram.ChartD4[candidate]
                        .Determinant == requiredDeterminant;
            }).ToArray();
            Assert.That(matches, Has.Length.EqualTo(1));
            return matches[0];
        }

        private static long Join(uint low, uint high) =>
            unchecked((long)(((ulong)high << 32) | low));

        private static bool GaugeLess(SigmaGaugeCell left, SigmaGaugeCell right)
        {
            if (left.Level != right.Level) return left.Level < right.Level;
            System.Numerics.BigInteger leftMorton = SignedMorton(left.U, left.V);
            System.Numerics.BigInteger rightMorton = SignedMorton(right.U, right.V);
            if (leftMorton != rightMorton) return leftMorton < rightMorton;
            if (left.U != right.U) return left.U < right.U;
            return left.V < right.V;
        }

        private static System.Numerics.BigInteger SignedMorton(long u, long v)
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

        private static SigmaCertificateFactor Factor(string scope,
            string expression, string independence, string provenance,
            string coupling, string branch, long lower, long upper) =>
            new(scope, expression, independence, provenance, coupling, branch,
                lower, upper);

        private static IEnumerable<SigmaGaugeCell[]> Permutations(
            IReadOnlyList<SigmaGaugeCell> source)
        {
            int[] indices = Enumerable.Range(0, source.Count).ToArray();
            foreach (int[] permutation in Permute(indices, 0))
                yield return permutation.Select(index => source[index]).ToArray();
        }

        private static IEnumerable<int[]> Permute(int[] values, int offset)
        {
            if (offset == values.Length)
            {
                yield return (int[])values.Clone();
                yield break;
            }
            for (int index = offset; index < values.Length; ++index)
            {
                (values[offset], values[index]) = (values[index], values[offset]);
                foreach (int[] result in Permute(values, offset + 1)) yield return result;
                (values[offset], values[index]) = (values[index], values[offset]);
            }
        }

        private static GraphicsBuffer Buffer(int count) => new(
            GraphicsBuffer.Target.Structured, count, Marshal.SizeOf<UInt4>());
    }
}
