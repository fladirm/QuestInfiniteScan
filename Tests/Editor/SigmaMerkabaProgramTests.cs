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
                Is.EqualTo("CPQ4-S16-MERKABA-N1R-1"));
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
            }, SigmaGeneratedMerkabaProgram.IrNodes.Select(node => node.Opcode));
            Assert.That(SigmaGeneratedMerkabaProgram.EntryPoints.Select(entry => entry.Id),
                Is.EquivalentTo(new[]
                {
                    "SENSOR_LEFT", "SENSOR_RIGHT", "EYE_PAIR",
                    "INTRINSIC_RELATION", "PREDICTION_SUPPORT", "EXPORT", "DEBUG",
                }));
            Assert.That(SigmaGeneratedMerkabaProgram.EntryPoints
                .Where(entry => entry.Id.StartsWith("SENSOR", StringComparison.Ordinal))
                .All(entry => entry.ReverseExpression >= 0), Is.True);
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
            var results = new UInt4[16 * 16 * 16];
            var matrices = new UInt4[16 * 16];
            var ir = new UInt4[SigmaGeneratedMerkabaProgram.IrNodeCount * 2];
            var actions = new UInt4[4];
            using var resultBuffer = Buffer(results.Length);
            using var matrixBuffer = Buffer(matrices.Length);
            using var irBuffer = Buffer(ir.Length);
            using var actionBuffer = Buffer(actions.Length);
            shader.SetBuffer(programKernel, "_MerkabaResults", resultBuffer);
            shader.Dispatch(programKernel, 1, 1, 16);
            shader.SetBuffer(matrixKernel, "_MerkabaMatrixResults", matrixBuffer);
            shader.SetBuffer(matrixKernel, "_MerkabaIrResults", irBuffer);
            shader.Dispatch(matrixKernel, 1, 1, 1);
            shader.SetBuffer(actionKernel, "_MerkabaActionResults", actionBuffer);
            shader.Dispatch(actionKernel, 1, 1, 1);
            resultBuffer.GetData(results);
            matrixBuffer.GetData(matrices);
            irBuffer.GetData(ir);
            actionBuffer.GetData(actions);

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
