using System;
using System.Collections.Generic;
using Genesis.RoomScan.SigmaPrism;
using NUnit.Framework;

namespace Genesis.RoomScan.Tests
{
    public sealed class SigmaAlgebraOperatorTests
    {
        [Test]
        public void NumericDomainRegistryAndCheckedNearestEvenSemanticsAreExact()
        {
            Assert.That(SigmaNumericDomain.Id,
                Is.EqualTo("num.fixed.q16_48.checked.nearest_even"));
            Assert.That(SigmaNumericDomain.Signed, Is.True);
            Assert.That(SigmaNumericDomain.IntegerBits, Is.EqualTo(16));
            Assert.That(SigmaNumericDomain.FractionBits, Is.EqualTo(48));
            Assert.That(SigmaNumericDomain.StorageBits, Is.EqualTo(64));
            Assert.That(SigmaNumericDomain.RoundingMode, Is.EqualTo("NearestEven"));
            Assert.That(SigmaNumericDomain.OverflowMode, Is.EqualTo("Checked"));
            Assert.That(SigmaNumericDomain.ScaleKind, Is.EqualTo("BinaryPower"));
            Assert.That(SigmaNumericDomain.One, Is.EqualTo(1L << 48));

            Assert.That(SigmaNumericDomain.QAdd(7, -2), Is.EqualTo(5));
            Assert.That(SigmaNumericDomain.QSub(7, -2), Is.EqualTo(9));
            Assert.That(SigmaNumericDomain.QMul(1, 1L << 47), Is.Zero,
                "0.5 raw-LSB ties to even zero");
            Assert.That(SigmaNumericDomain.QMul(3, 1L << 47), Is.EqualTo(2),
                "1.5 raw-LSB ties to even two");
            Assert.That(SigmaNumericDomain.QMul(-3, 1L << 47), Is.EqualTo(-2));
            Assert.That(SigmaNumericDomain.QDiv(1, 2 * SigmaNumericDomain.One),
                Is.Zero);
            Assert.That(SigmaNumericDomain.QDiv(3, 2 * SigmaNumericDomain.One),
                Is.EqualTo(2));
            Assert.That(SigmaNumericDomain.QShiftRight(3, 1), Is.EqualTo(2));
            Assert.That(SigmaNumericDomain.QShiftRight(-3, 1), Is.EqualTo(-2));
            Assert.That(SigmaNumericDomain.QShiftRight(1, 1), Is.Zero);
            Assert.That(SigmaNumericDomain.QShiftRightLower(-1, 1), Is.EqualTo(-1));
            Assert.That(SigmaNumericDomain.QShiftRightUpper(-1, 1), Is.Zero);
            Assert.That(SigmaNumericDomain.QIntegerSquareRoot(ulong.MaxValue),
                Is.EqualTo(uint.MaxValue));
            Assert.That(SigmaNumericDomain.Quantize(0.5),
                Is.EqualTo(SigmaNumericDomain.Half));
            Assert.That(SigmaNumericDomain.Quantize(-0.5f),
                Is.EqualTo(-SigmaNumericDomain.Half));

            Assert.Throws<OverflowException>(() =>
                SigmaNumericDomain.QAdd(long.MaxValue, 1));
            Assert.Throws<OverflowException>(() =>
                SigmaNumericDomain.QNegate(long.MinValue));
            Assert.Throws<OverflowException>(() =>
                SigmaNumericDomain.QShiftLeft(long.MaxValue, 1));
            Assert.Throws<DivideByZeroException>(() => SigmaNumericDomain.QDiv(1, 0));
        }

        [Test]
        public void OutwardIntervalArithmeticNeverExcludesExactValue()
        {
            (long A, long B)[] fixtures =
            {
                (1, 1L << 47), (3, 1L << 47), (-3, 1L << 47),
                (SigmaNumericDomain.FromRatio(7, 3),
                    SigmaNumericDomain.FromRatio(-11, 5)),
            };
            foreach ((long a, long b) in fixtures)
            {
                long point = SigmaNumericDomain.QMul(a, b);
                long lower = SigmaNumericDomain.QMulLower(a, b);
                long upper = SigmaNumericDomain.QMulUpper(a, b);
                Assert.That(point, Is.InRange(lower, upper));
                if (b != 0)
                {
                    point = SigmaNumericDomain.QDiv(a, b);
                    lower = SigmaNumericDomain.QDivLower(a, b);
                    upper = SigmaNumericDomain.QDivUpper(a, b);
                    Assert.That(point, Is.InRange(lower, upper));
                }
            }
            var meet = new SigmaQ48Interval(-10, 20)
                .Intersect(new SigmaQ48Interval(5, 30));
            Assert.That(meet, Is.EqualTo(new SigmaQ48Interval(5, 20)));
            Assert.That(new SigmaQ48Interval(3, 2).IsEmpty, Is.True);
        }

        [Test]
        public void GeneratedBasisTableMatchesRecursiveCayleyDicksonExhaustively()
        {
            for (int left = 0; left < 16; ++left)
            {
                for (int right = 0; right < 16; ++right)
                {
                    (int sign, int index) = RecursiveBasisProduct(16, left, right);
                    Assert.That(SigmaS16Operators.BasisProductIndex(left, right),
                        Is.EqualTo(left ^ right));
                    Assert.That(SigmaS16Operators.BasisProductIndex(left, right),
                        Is.EqualTo(index));
                    Assert.That(SigmaS16Operators.BasisProductSign(left, right),
                        Is.EqualTo(sign));
                }
            }
            for (int lane = 1; lane < 16; ++lane)
            {
                Assert.That(SigmaS16Operators.BasisProductIndex(lane, lane), Is.Zero);
                Assert.That(SigmaS16Operators.BasisProductSign(lane, lane),
                    Is.EqualTo(-1));
            }
        }

        [Test]
        public void GeneratedSparseBasisAndConjugationMatchDenseReference()
        {
            SigmaS16 state = DeterministicState(19);
            for (int basis = 0; basis < 16; ++basis)
            {
                SigmaS16 e = SigmaS16.Basis(basis, SigmaNumericDomain.One);
                Assert.That(SigmaS16Operators.LeftBasisAction(basis, state),
                    Is.EqualTo(SigmaS16Operators.DenseReferenceMultiply(e, state)));
                Assert.That(SigmaS16Operators.RightBasisAction(state, basis),
                    Is.EqualTo(SigmaS16Operators.DenseReferenceMultiply(state, e)));
            }
            SigmaS16 conjugated = SigmaS16Operators.Conjugate(state);
            Assert.That(SigmaS16Operators.Conjugate(conjugated), Is.EqualTo(state));
            SigmaS16 other = DeterministicState(31);
            Assert.That(SigmaS16Operators.Conjugate(
                    SigmaS16Operators.DenseReferenceMultiply(state, other)),
                Is.EqualTo(SigmaS16Operators.DenseReferenceMultiply(
                    SigmaS16Operators.Conjugate(other),
                    SigmaS16Operators.Conjugate(state))));
            Assert.That(SigmaOperatorEvaluator.EvaluateS16(
                SigmaOperatorPlans.Conjugation, state), Is.EqualTo(conjugated));
        }

        [Test]
        public void CompleteGeneratedZeroDivisorCatalogIsExactAndNonZero()
        {
            Assert.That(SigmaGeneratedAlgebra.ZeroDivisorCatalogCount,
                Is.EqualTo(1344));
            Assert.That(SigmaGeneratedAlgebra.AnnihilatorActionCount,
                Is.EqualTo(168));
            SigmaZeroDivisorEntry previous = default;
            for (int index = 0;
                index < SigmaGeneratedAlgebra.ZeroDivisorCatalogCount; ++index)
            {
                SigmaZeroDivisorEntry entry =
                    SigmaS16Operators.GetZeroDivisorEntry(index);
                Assert.That(entry.Witness.ToS16().IsZero, Is.False);
                Assert.That(entry.Annihilator.ToS16().IsZero, Is.False);
                Assert.That(SigmaS16Operators.DenseReferenceMultiply(
                    entry.Witness.ToS16(), entry.Annihilator.ToS16()).IsZero, Is.True,
                    $"catalog entry {index}");
                Assert.That(SigmaS16Operators.RightSignedDyadAction(
                    entry.Witness.ToS16(), entry.Annihilator).IsZero, Is.True);
                Assert.That(SigmaS16Operators.GetAnnihilatorAction(entry.ActionIndex),
                    Is.EqualTo(entry.Annihilator));
                if (index > 0)
                    Assert.That(CompareEntries(previous, entry), Is.LessThanOrEqualTo(0));
                previous = entry;
            }
        }

        [Test]
        public void SignedDyadPlanUsesOnlyPermutationSignAndAddition()
        {
            SigmaS16 state = DeterministicState(47);
            for (int action = 0;
                action < SigmaGeneratedAlgebra.AnnihilatorActionCount; ++action)
            {
                SigmaSignedDyad dyad = SigmaS16Operators.GetAnnihilatorAction(action);
                SigmaOperatorPlan plan = SigmaOperatorPlans.RightSignedDyad(dyad);
                Assert.That(plan.Contains(SigmaOperatorOpcode.QMUL), Is.False);
                Assert.That(plan.Contains(SigmaOperatorOpcode.QDIV), Is.False);
                Assert.That(SigmaOperatorEvaluator.EvaluateS16(plan, state),
                    Is.EqualTo(SigmaS16Operators.DenseReferenceMultiply(
                        state, dyad.ToS16())), $"action {action}");
                string hlsl = SigmaHlslLowerer.Lower(plan,
                    SigmaBackendCapabilityProfile.Packed32Proven);
                Assert.That(hlsl, Does.Not.Contain("SigmaQ48MulNearestEven"));
                Assert.That(hlsl, Does.Not.Contain("SigmaQ48DivNearestEven"));
            }
        }

        [Test]
        public void ExplicitAssociatorBracketingsAndFusedPlanAgree()
        {
            SigmaS16 a = SigmaS16.Basis(1, SigmaNumericDomain.One);
            SigmaS16 b = SigmaS16.Basis(2, SigmaNumericDomain.One);
            SigmaS16 c = SigmaS16.Basis(4, SigmaNumericDomain.One);
            SigmaS16 reference = SigmaS16Operators.Associator(a, b, c);
            Assert.That(reference.IsZero, Is.False);
            Assert.That(reference[7], Is.EqualTo(2 * SigmaNumericDomain.One));
            Assert.That(SigmaOperatorPlans.Associator.BracketDescriptor,
                Is.EqualTo("sub(mul(mul(a,b),c),mul(a,mul(b,c)))"));
            Assert.That(SigmaOperatorEvaluator.EvaluateS16(
                SigmaOperatorPlans.Associator, a, b, c), Is.EqualTo(reference));
        }

        [Test]
        public void GeneratedTransitionHadamardAndReadoutPlansMatchReferences()
        {
            for (int fixture = 0; fixture < 8; ++fixture)
            {
                SigmaS16 left = DeterministicState(101 + fixture * 7);
                SigmaS16 right = DeterministicState(211 + fixture * 11);
                Assert.That(SigmaOperatorEvaluator.EvaluateS16(
                        SigmaOperatorPlans.Transition, left, right),
                    Is.EqualTo(SigmaS16Operators.Transition(left, right)));
                Assert.That(SigmaOperatorEvaluator.EvaluateS16(
                        SigmaOperatorPlans.HadamardB, left),
                    Is.EqualTo(SigmaS16Operators.HadamardB(left)));
                Assert.That(SigmaOperatorEvaluator.EvaluateS16(
                        SigmaOperatorPlans.HadamardBT, left),
                    Is.EqualTo(SigmaS16Operators.HadamardBT(left)));
                CollectionAssert.AreEqual(SigmaS16Operators.GeometryReadout(left),
                    SigmaOperatorEvaluator.Evaluate(SigmaOperatorPlans.GeometryG, left));
                CollectionAssert.AreEqual(SigmaS16Operators.HiddenReadout(left),
                    SigmaOperatorEvaluator.Evaluate(SigmaOperatorPlans.HiddenF, left));
            }
            CollectionAssert.AreEqual(new long[4],
                SigmaS16Operators.GeometryReadout(SigmaS16Operators.NullState));

            SigmaS16 roundTripSource = DeterministicState(63);
            SigmaS16 operatorCoordinates = SigmaS16Operators.HadamardB(roundTripSource);
            Assert.That(SigmaOperatorEvaluator.EvaluateS16(
                    SigmaOperatorPlans.ProjectiveCommit,
                    operatorCoordinates, operatorCoordinates, operatorCoordinates),
                Is.EqualTo(roundTripSource),
                "B^T B >> 4 must be an exact inverse lift");

            SigmaS16 lowerA = SigmaS16.Basis(0, -10);
            SigmaS16 upperA = SigmaS16.Basis(0, 20);
            SigmaS16 lowerB = SigmaS16.Basis(0, 5);
            SigmaS16 upperB = SigmaS16.Basis(0, 30);
            long[] meet = SigmaOperatorEvaluator.Evaluate(
                SigmaOperatorPlans.ProjectiveMeet,
                lowerA, upperA, lowerB, upperB);
            Assert.That(meet[0], Is.EqualTo(5));
            Assert.That(meet[16], Is.EqualTo(20));

            var nuLanes = new long[16];
            nuLanes[1] = SigmaNumericDomain.FromRatio(1, 2);
            nuLanes[2] = SigmaNumericDomain.FromRatio(-1, 4);
            nuLanes[3] = SigmaNumericDomain.FromRatio(3, 8);
            SigmaS16 nu = SigmaS16.FromArray(nuLanes);
            SigmaS16 source = DeterministicState(73);
            SigmaS16 viewReference = SigmaS16Operators.DenseReferenceMultiply(nu,
                SigmaS16Operators.DenseReferenceMultiply(source,
                    SigmaS16Operators.Conjugate(nu)));
            Assert.That(SigmaOperatorPlans.View.BracketDescriptor,
                Is.EqualTo("mul(nu,mul(s,conjugate(nu)))"));
            Assert.That(SigmaOperatorEvaluator.EvaluateS16(
                SigmaOperatorPlans.View, source, nu), Is.EqualTo(viewReference));

            long[] codecEqual = SigmaOperatorEvaluator.Evaluate(
                SigmaOperatorPlans.CodecPredicates, source, source);
            CollectionAssert.AreEqual(new long[16]
                { -1, -1, -1, -1, -1, -1, -1, -1,
                  -1, -1, -1, -1, -1, -1, -1, -1 }, codecEqual);
        }

        [Test]
        public void CseMaskSelectAndCapabilityRefusalPreserveExactSemantics()
        {
            SigmaOperatorPlan optimized = BuildCseFixture(true);
            SigmaOperatorPlan unoptimized = BuildCseFixture(false);
            Assert.That(optimized.Nodes.Count, Is.LessThan(unoptimized.Nodes.Count));
            SigmaS16 low = SigmaS16.Basis(0, -7);
            SigmaS16 high = SigmaS16.Basis(0, 11);
            CollectionAssert.AreEqual(
                SigmaOperatorEvaluator.Evaluate(unoptimized, low, high),
                SigmaOperatorEvaluator.Evaluate(optimized, low, high));
            Assert.That(SigmaOperatorEvaluator.Evaluate(optimized, low, high)[0],
                Is.EqualTo(8));
            Assert.Throws<InvalidOperationException>(() => SigmaHlslLowerer.Lower(
                optimized, SigmaBackendCapabilityProfile.NativeI64Unproven));
            Assert.That(SigmaHlslLowerer.Lower(optimized,
                SigmaBackendCapabilityProfile.Packed32Proven),
                Does.Contain(optimized.Fingerprint));
        }

        [Test]
        public void GeneratedFingerprintsAreStableAndBoundToCanonicalBundle()
        {
            Assert.That(SigmaOperatorSet.Canonical.NumericDomainId,
                Is.EqualTo(SigmaNumericDomain.Id));
            Assert.That(SigmaGeneratedAlgebra.NumericFingerprint,
                Is.EqualTo("01e3adab0934feff195ede86ad64e29612b14e929323d9203e93aa7359e772f2"));
            Assert.That(SigmaGeneratedAlgebra.MultiplicationFingerprint,
                Is.EqualTo("1b2e8721a74135cd268195d1cd8026f35962861e2eeb15fe573d2f550765d378"));
            Assert.That(SigmaGeneratedAlgebra.AnnihilatorFingerprint,
                Is.EqualTo("9401c2ff61f239d110624c6d694e842a59c2f891bae94e5e835056c15ec9dcaa"));
            Assert.That(SigmaS16Operators.BundleFingerprint,
                Is.EqualTo("976425702f4426d07ccad591c185a8c7841721a7cebb2d7aa6dfd283ade09e2e"));
            Assert.That(SigmaOperatorPlans.Transition.Fingerprint,
                Has.Length.EqualTo(64));
            Assert.That(SigmaOperatorPlans.Associator.Fingerprint,
                Has.Length.EqualTo(64));
            Assert.That(SigmaOperatorPlans.PlanBundleFingerprint,
                Has.Length.EqualTo(64));
            Assert.That(SigmaOperatorSet.Canonical.ExactPlanBundleFingerprint,
                Is.EqualTo(SigmaOperatorPlans.PlanBundleFingerprint));
            Assert.That(SigmaOperatorPlans.PlanBundleFingerprint,
                Is.EqualTo(SigmaOperatorPlans.PlanBundleFingerprint));
        }

        private static SigmaOperatorPlan BuildCseFixture(bool cse)
        {
            var builder = new SigmaOperatorPlanBuilder(2, cse);
            int a0 = builder.Gather(0, 0);
            int b0 = builder.Gather(1, 0);
            int sum1 = builder.Add(a0, b0);
            int sum2 = builder.Add(a0, b0);
            int predicate = builder.CompareLess(a0, b0);
            int selected = builder.Select(builder.Mask(predicate), sum1, sum2);
            return builder.Build("cse-mask-select", "select(a<b,a+b,a+b)",
                new[] { builder.Add(selected, sum2) });
        }

        private static SigmaS16 DeterministicState(int seed)
        {
            var lanes = new long[16];
            uint value = unchecked((uint)seed);
            for (int lane = 0; lane < lanes.Length; ++lane)
            {
                value = value * 1664525u + 1013904223u;
                int signed = (int)((value >> 16) & 0x1f) - 16;
                lanes[lane] = signed * (SigmaNumericDomain.One >> 8);
            }
            return SigmaS16.FromArray(lanes);
        }

        private static (int sign, int index) RecursiveBasisProduct(int dimension,
            int left, int right)
        {
            if (dimension == 1)
                return (1, 0);
            int half = dimension >> 1;
            if (left < half && right < half)
                return RecursiveBasisProduct(half, left, right);
            if (left < half && right >= half)
            {
                (int sign, int index) = RecursiveBasisProduct(half, left, right - half);
                return ((left == 0 ? 1 : -1) * sign, half + index);
            }
            if (left >= half && right < half)
            {
                (int sign, int index) = RecursiveBasisProduct(half, right, left - half);
                return (sign, half + index);
            }
            (int highSign, int highIndex) = RecursiveBasisProduct(
                half, right - half, left - half);
            return (-(left - half == 0 ? 1 : -1) * highSign, highIndex);
        }

        private static int CompareEntries(SigmaZeroDivisorEntry left,
            SigmaZeroDivisorEntry right)
        {
            int result = left.Witness.CompareTo(right.Witness);
            return result != 0 ? result : left.Annihilator.CompareTo(right.Annihilator);
        }
    }
}
