using System;
using System.Runtime.InteropServices;
using Genesis.RoomScan.SigmaPrism;
using NUnit.Framework;
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
        public void AuthorityBoundaryAndDirectS16RouteAreFrozen()
        {
            Assert.That(SigmaGeneratedMerkabaProgram.ProgramVersion,
                Is.EqualTo("CPQ4-S16-MERKABA-N1R-1"));
            Assert.That(SigmaGeneratedMerkabaProgram.NumericDomainId,
                Is.EqualTo(SigmaNumericDomain.Id));
            Assert.That(SigmaGeneratedMerkabaProgram.DeclaredToeUpstreamFingerprint,
                Is.EqualTo("9d2e3604846305cfe5244a4ef49f169632c60582cf895256fadc36426dc5786f"));
            Assert.That(SigmaGeneratedMerkabaProgram.ToeCapsuleInputFingerprint,
                Is.EqualTo("36a584dcff0c0c340d491ab476aa7428f7b1edf0c97e1407022e0f71181fdee1"));
            Assert.That(SigmaGeneratedMerkabaProgram.AlgebraCoreInputFingerprint,
                Is.EqualTo(SigmaGeneratedAlgebra.NativeCoreFingerprint));
            Assert.That(SigmaGeneratedMerkabaProgram.AlgebraCoreInputFingerprint,
                Is.Not.EqualTo(SigmaGeneratedAlgebra.BundleFingerprint),
                "Legacy G/F/readout donor fingerprints cannot authorize Merkaba.");
            Assert.That(SigmaGeneratedMerkabaProgram.GeneratorSourceInputFingerprint,
                Has.Length.EqualTo(64));
            Assert.That(SigmaGeneratedMerkabaProgram.ProgramFingerprint,
                Has.Length.EqualTo(64));
            Assert.That(SigmaGeneratedMerkabaProgram.ExpressionCount, Is.EqualTo(19));
            foreach (string fingerprint in
                     SigmaGeneratedMerkabaProgram.ExpressionFingerprints)
                Assert.That(fingerprint, Has.Length.EqualTo(64));
            CollectionAssert.AllItemsAreUnique(
                SigmaGeneratedMerkabaProgram.ExpressionFingerprints);
            Assert.That(SigmaGeneratedMerkabaProgram.E22InventoryCount, Is.Zero);
            Assert.That(SigmaGeneratedMerkabaProgram.DirectS16DependenciesRetained,
                Is.True);
        }

        [Test]
        public void SignedXorAssociatorDiffractionAndHolonomyMatchCapsuleExhaustively()
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
                        Assert.That(coefficient == -2 || coefficient == 0 ||
                            coefficient == 2, Is.True);
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
                    Assert.That(SigmaGeneratedMerkabaProgram.DiffractionMatrix[
                            row * 16 + column],
                        Is.EqualTo(-SigmaGeneratedMerkabaProgram.DiffractionMatrix[
                            column * 16 + row]));
                }
            }
        }

        [Test]
        public void ShellRecurrenceReachesMinusFifteenWithoutInventingBaseOrientation()
        {
            CollectionAssert.AreEqual(new sbyte[] { -1, -3, -7, -15 },
                SigmaGeneratedMerkabaProgram.ShellSquareByRank);
            Assert.That(
                SigmaGeneratedMerkabaProgram.ShadowKernelDecouplingProofSupplied,
                Is.False,
                "The capsule supplies no concrete C_vk=C_kv=0 proof.");
        }

        [Test]
        public void MerkabaShadowFrameAndKernelCouplingProofRemainNative()
        {
            var frameNumerator = new int[4, 4];
            for (int address = 0; address < 16; ++address)
            {
                int sum = 0;
                for (int axis = 0; axis < 4; ++axis)
                    sum += SigmaGeneratedMerkabaProgram.ShadowNumerator(address, axis);
                Assert.That(sum, Is.Zero, $"shadow address {address}");
                for (int row = 0; row < 4; ++row)
                {
                    for (int column = 0; column < 4; ++column)
                    {
                        frameNumerator[row, column] +=
                            SigmaGeneratedMerkabaProgram.ShadowNumerator(address, row) *
                            SigmaGeneratedMerkabaProgram.ShadowNumerator(address, column);
                    }
                }
            }

            for (int row = 0; row < 4; ++row)
            {
                for (int column = 0; column < 4; ++column)
                {
                    Assert.That(frameNumerator[row, column],
                        Is.EqualTo(row == column ? 192 : -64));
                }
            }
            Assert.That(SigmaGeneratedMerkabaProgram.CanFreezeShadowKernel,
                Is.False,
                "A shadow-transparent mode cannot be frozen without decoupling.");
        }

        [Test]
        public void ZEmptyClaimsSupportAndRepresentationProofsFailClosed()
        {
            Assert.That(SigmaGeneratedMerkabaProgram.IsZEmpty(SigmaS16.Zero), Is.True);
            Assert.That(SigmaGeneratedMerkabaProgram.IsZEmpty(
                SigmaS16.Basis(3, 1L)), Is.False);
            Assert.That(SigmaGeneratedMerkabaProgram.LegacyZNullAccepted, Is.False,
                "The old nonzero ZNullDyad has no complete-program empty proof.");
            Assert.That(SigmaGeneratedMerkabaProgram.ClassifyAllDefault(
                    SigmaS16.Zero, SigmaS16.Zero),
                Is.EqualTo(SigmaMerkabaRelationClass.DefaultSat));
            Assert.That(SigmaGeneratedMerkabaProgram.AllDefaultActiveWork, Is.Zero);

            Assert.That(SigmaGeneratedMerkabaProgram.ReverseActionFor(
                    SigmaNativeQueryClaim.NoClaim),
                Is.EqualTo(SigmaNativeQueryClaim.NoClaim));
            Assert.That(SigmaGeneratedMerkabaProgram.ReverseActionFor(
                    SigmaNativeQueryClaim.PreHitExclusion),
                Is.EqualTo(SigmaNativeQueryClaim.PreHitExclusion));
            Assert.That(SigmaGeneratedMerkabaProgram.ReverseActionFor(
                    SigmaNativeQueryClaim.FirstHitMould),
                Is.EqualTo(SigmaNativeQueryClaim.FirstHitMould));
            Assert.That(SigmaGeneratedMerkabaProgram.BehindHitProducesAction, Is.False);
            Assert.That(
                SigmaGeneratedMerkabaProgram.MissingOpticalMetadataProducesClaim,
                Is.False);

            Assert.That(SigmaGeneratedMerkabaProgram.CanOmitQueryRegion(
                true, true, true), Is.True);
            Assert.That(SigmaGeneratedMerkabaProgram.CanOmitQueryRegion(
                false, true, true), Is.False);
            Assert.That(SigmaGeneratedMerkabaProgram.CanOmitQueryRegion(
                true, false, true), Is.False);
            Assert.That(SigmaGeneratedMerkabaProgram.CanOmitQueryRegion(
                true, true, false), Is.False);
            Assert.That(SigmaGeneratedMerkabaProgram.QuerySupportFalseNegatives,
                Is.Zero);
            Assert.That(SigmaGeneratedMerkabaProgram.QuerySupportFixtureCount,
                Is.EqualTo(32));
            Assert.That(SigmaGeneratedMerkabaProgram.ReverseIntervalSoundFixtureCount,
                Is.GreaterThan(0));
            Assert.That(SigmaGeneratedMerkabaProgram.DuplicateFixtureCount,
                Is.EqualTo(10000));
            Assert.That(SigmaGeneratedMerkabaProgram.DuplicateMinimizedFactorCount,
                Is.EqualTo(1));
            Assert.That(SigmaGeneratedMerkabaProgram.CoupledFactorInputCount,
                Is.EqualTo(2));
            Assert.That(SigmaGeneratedMerkabaProgram.CoupledFactorMinimizedCount,
                Is.EqualTo(2));
            Assert.That(SigmaGeneratedMerkabaProgram.WeakFactorPreservesStrongRegion,
                Is.True);
            Assert.That(SigmaGeneratedMerkabaProgram.RefinementChildCount,
                Is.EqualTo(4));
            Assert.That(SigmaGeneratedMerkabaProgram.RefinementCopiesFullS16,
                Is.True);
            Assert.That(SigmaGeneratedMerkabaProgram.RefinementExactHalfOpenCover,
                Is.True);
            Assert.That(SigmaGeneratedMerkabaProgram.RefinementPointwiseFullS16,
                Is.True);
            Assert.That(SigmaGeneratedMerkabaProgram.RefinementExactMeasure,
                Is.True);
            Assert.That(SigmaGeneratedMerkabaProgram.RepresentationDefaultParity,
                Is.True);
            Assert.That(SigmaGeneratedMerkabaProgram.CanFreezeShadowKernel,
                Is.False);
            Assert.That(SigmaGeneratedMerkabaProgram.OpticalCalibrationProvenance,
                Is.True);
            Assert.That(
                SigmaGeneratedMerkabaProgram.OpticalUnboundedExplanationForbidden,
                Is.True);
            CollectionAssert.AreEqual(
                new[] { 0, 0, 0, 7, 1, 0, 0, 9, 2, 1, 0, 11 },
                SigmaGeneratedMerkabaProgram.BaseGaugeNormalForm);
        }

        [Test]
        public void PrimitiveReverseIntervalsRetainEveryForwardSourcePoint()
        {
            int fixtures = 0;
            for (long left = -8; left <= 8; ++left)
            {
                for (long right = -8; right <= 8; ++right)
                {
                    foreach (long padding in new[] { 0L, 1L, 3L })
                    {
                        long add = SigmaNumericDomain.QAdd(left, right);
                        var addOutput = new SigmaQ48Interval(add - padding,
                            add + padding);
                        var reverseLeftAdd = new SigmaQ48Interval(
                            SigmaNumericDomain.QSub(addOutput.Lower, right),
                            SigmaNumericDomain.QSub(addOutput.Upper, right));
                        Assert.That(reverseLeftAdd.Contains(left), Is.True);
                        ++fixtures;

                        long subtract = SigmaNumericDomain.QSub(left, right);
                        var subtractOutput = new SigmaQ48Interval(
                            subtract - padding, subtract + padding);
                        var reverseLeftSubtract = new SigmaQ48Interval(
                            SigmaNumericDomain.QAdd(subtractOutput.Lower, right),
                            SigmaNumericDomain.QAdd(subtractOutput.Upper, right));
                        Assert.That(reverseLeftSubtract.Contains(left), Is.True);
                        ++fixtures;
                    }
                }
            }
            for (long value = -32; value <= 32; ++value)
            {
                foreach (long sign in new[] { -1L, 1L })
                {
                    Assert.That(sign * (sign * value), Is.EqualTo(value));
                    ++fixtures;
                }
            }
            for (long source = -4; source <= 4; ++source)
            {
                foreach (long coefficient in new[] { -3L, -2L, -1L, 1L, 2L, 3L })
                {
                    foreach (long padding in new[] { 0L, 1L })
                    {
                        long sourceRaw = SigmaNumericDomain.FromInteger(source);
                        long coefficientRaw =
                            SigmaNumericDomain.FromInteger(coefficient);
                        long product = SigmaNumericDomain.QMul(sourceRaw,
                            coefficientRaw);
                        long lower = product - padding;
                        long upper = product + padding;
                        var reverse = coefficient > 0
                            ? new SigmaQ48Interval(
                                SigmaNumericDomain.QDivLower(lower, coefficientRaw),
                                SigmaNumericDomain.QDivUpper(upper, coefficientRaw))
                            : new SigmaQ48Interval(
                                SigmaNumericDomain.QDivLower(upper, coefficientRaw),
                                SigmaNumericDomain.QDivUpper(lower, coefficientRaw));
                        Assert.That(reverse.Contains(sourceRaw), Is.True);
                        ++fixtures;
                    }
                }
            }
            Assert.That(SigmaGeneratedMerkabaProgram.ReverseZeroBranchRetained,
                Is.True);
            Assert.That(fixtures, Is.EqualTo(
                SigmaGeneratedMerkabaProgram.ReverseIntervalSoundFixtureCount));
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
            Assert.That(foundNonzeroResidual, Is.True,
                "A one-LSB nonzero residual must not classify as exact ZD.");
            Assert.That(SigmaGeneratedMerkabaProgram.ClassifyZeroDivisor(
                    true, true, false, true),
                Is.EqualTo(SigmaMerkabaRelationClass.NearSingularQ48));
            Assert.That(SigmaGeneratedMerkabaProgram.ClassifyZeroDivisor(
                    false, true, true, false),
                Is.EqualTo(SigmaMerkabaRelationClass.Regular),
                "A zero operand is not an S16 zero-divisor pair.");
        }

        [Test]
        public void GeneratedMerkabaHlslMatchesCpuForEveryBasisContext()
        {
            ComputeShader shader = Resources.Load<ComputeShader>(
                "SigmaPrism/SigmaOperatorFixture");
            Assert.That(shader, Is.Not.Null);
            int parityKernel = shader.FindKernel("MerkabaProgramParity");
            int matrixKernel = shader.FindKernel("MerkabaMatrixParity");
            var results = new UInt4[16 * 16 * 16];
            var matrices = new UInt4[16 * 16];
            using (var resultBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                       results.Length, Marshal.SizeOf<UInt4>()))
            using (var matrixBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                       matrices.Length, Marshal.SizeOf<UInt4>()))
            {
                shader.SetBuffer(parityKernel, "_MerkabaResults", resultBuffer);
                shader.Dispatch(parityKernel, 1, 1, 16);
                shader.SetBuffer(matrixKernel, "_MerkabaMatrixResults", matrixBuffer);
                shader.Dispatch(matrixKernel, 1, 1, 1);
                resultBuffer.GetData(results);
                matrixBuffer.GetData(matrices);
            }

            for (int c = 0; c < 16; ++c)
            {
                for (int b = 0; b < 16; ++b)
                {
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
                }
            }

            for (int index = 0; index < matrices.Length; ++index)
            {
                UInt4 actual = matrices[index];
                Assert.That(unchecked((int)actual.X), Is.EqualTo(
                    SigmaGeneratedMerkabaProgram.DiffractionMatrix[index]));
                Assert.That(unchecked((int)actual.Y), Is.EqualTo(
                    SigmaGeneratedMerkabaProgram.VisibleProjectorNumerator256[index]));
                Assert.That(unchecked((int)actual.Z), Is.EqualTo(
                    SigmaGeneratedMerkabaProgram.ShellSquareByRank[(index / 16) & 3]));
                Assert.That(actual.W, Is.Zero);
            }

            int defaultKernel = shader.FindKernel("MerkabaDefaultParity");
            var defaultResult = new UInt4[2];
            using var defaultBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                defaultResult.Length, Marshal.SizeOf<UInt4>());
            shader.SetBuffer(defaultKernel, "_MerkabaDefaultResult", defaultBuffer);
            shader.Dispatch(defaultKernel, 1, 1, 1);
            defaultBuffer.GetData(defaultResult);
            Assert.That(defaultResult[0].X, Is.EqualTo(1u));
            Assert.That(defaultResult[0].Y, Is.EqualTo(1u));
            Assert.That(defaultResult[0].Z, Is.EqualTo(0u));
            Assert.That(defaultResult[0].W, Is.EqualTo(15u));
            Assert.That(defaultResult[1].X,
                Is.EqualTo((uint)SigmaMerkabaRelationClass.ExactZeroDivisor));
            Assert.That(defaultResult[1].Y,
                Is.EqualTo((uint)SigmaMerkabaRelationClass.NearSingularQ48));
            Assert.That(defaultResult[1].Z,
                Is.EqualTo((uint)SigmaMerkabaRelationClass.Regular));
            Assert.That(defaultResult[1].W, Is.EqualTo(1u));
        }

    }
}
