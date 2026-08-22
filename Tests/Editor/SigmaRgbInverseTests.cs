using System;
using System.Collections.Generic;
using Genesis.RoomScan.SigmaPrism;
using NUnit.Framework;

namespace Genesis.RoomScan.Tests
{
    public sealed class SigmaRgbInverseTests
    {
        [Test]
        public void GeneratedViewMatrixEqualsExplicitBracketedSemanticOperator()
        {
            var directions = new[]
            {
                new SigmaS16(0, Q(1), 0, 0, 0, 0, 0, 0,
                    0, 0, 0, 0, 0, 0, 0, 0),
                new SigmaS16(0, Q(1), -Q(1), Q(1), 0, 0, 0, 0,
                    0, 0, 0, 0, 0, 0, 0, 0),
            };
            foreach (SigmaS16 direction in directions)
            {
                long[,] matrix = SigmaRgbInverse.BuildGeneratedViewMatrix(direction);
                Assert.That(matrix.GetLength(0), Is.EqualTo(4));
                Assert.That(matrix.GetLength(1), Is.EqualTo(16));
                for (int yLane = 0; yLane < 16; ++yLane)
                {
                    SigmaS16 y = SigmaS16.Basis(yLane, Q(1));
                    SigmaS16 inverse = SigmaS16Operators.HadamardBT(y);
                    var p = new long[16];
                    for (int lane = 0; lane < 16; ++lane)
                        p[lane] = SigmaNumericDomain.QShiftRight(
                            inverse[lane], 4);
                    SigmaS16 viewed = SigmaOperatorEvaluator.EvaluateS16(
                        SigmaOperatorPlans.View, SigmaS16.FromArray(p), direction);
                    long[] hidden = SigmaS16Operators.HiddenReadout(viewed);
                    for (int row = 0; row < 4; ++row)
                        Assert.That(matrix[row, yLane], Is.EqualTo(hidden[row]),
                            $"row {row}, y lane {yLane}");
                }
            }
        }

        [Test]
        public void ExactDirectionCatalogIsDeterministicAndHasFailClosedNullSlot()
        {
            SigmaRgbViewCatalog first = SigmaRgbViewCatalog.CreateCanonical();
            SigmaRgbViewCatalog second = SigmaRgbViewCatalog.CreateCanonical();
            Assert.That(first.OperatorRaw.Count,
                Is.EqualTo(SigmaRgbViewCatalog.MatrixValueCount));
            Assert.That(first.Fingerprint, Is.EqualTo(second.Fingerprint));
            Assert.That(first.SupportScale[SigmaRgbViewCatalog.NullDirectionIndex],
                Is.Zero);
            Assert.That(SigmaRgbViewCatalog.QuantizeDirection(Q(1), Q(0.49), 0),
                Is.EqualTo(SigmaRgbViewCatalog.EncodeDirection(1, 0, 0)));
            Assert.That(SigmaRgbViewCatalog.QuantizeDirection(Q(1), Q(0.5), 0),
                Is.EqualTo(SigmaRgbViewCatalog.EncodeDirection(1, 1, 0)));
            Assert.That(SigmaRgbViewCatalog.QuantizeDirection(0, 0, 0),
                Is.EqualTo(SigmaRgbViewCatalog.NullDirectionIndex));
        }

        [Test]
        public void UnobservableAppearanceDenominatorContributesNoBound()
        {
            SigmaS16 direction = Direction(1, 0, 0);
            long[,] matrix = SigmaRgbInverse.BuildGeneratedViewMatrix(direction);
            SigmaQ48Interval[] prior = BoxAround(new long[16], Q(0.05));
            var rgb = new[] { Interval(0.2, 0.3), Interval(0.3, 0.4),
                Interval(0.4, 0.5) };
            bool informative = SigmaRgbInverse.TryBuildCell(prior,
                new long[16], matrix, rgb, Q(0.01),
                SigmaRgbSourceClass.Left, 11u, out SigmaRgbAdmissibleCell cell);
            Assert.That(informative, Is.False);
            Assert.That(cell.CoordinateMask, Is.Zero);
            for (int lane = 0; lane < 16; ++lane)
                Assert.That(cell[lane], Is.EqualTo(prior[lane]));
        }

        [Test]
        public void SevenInequalityContractionContainsKnownSolutionAndNarrowsBox()
        {
            SigmaS16 direction = Direction(1, -1, 1);
            long[,] matrix = SigmaRgbInverse.BuildGeneratedViewMatrix(direction);
            (long[] y, long[] colour) = FindObservableProjectivePoint(matrix);
            var prior = new SigmaQ48Interval[16];
            for (int lane = 0; lane < prior.Length; ++lane)
                prior[lane] = new SigmaQ48Interval(y[lane], y[lane]);
            int observableLane = FindObservableLane(matrix, y);
            prior[observableLane] = new SigmaQ48Interval(
                SigmaNumericDomain.QSub(y[observableLane], Q(0.20)),
                SigmaNumericDomain.QAdd(y[observableLane], Q(0.20)));
            long channelWidth = Q(0.015);
            var rgb = new SigmaQ48Interval[3];
            for (int channel = 0; channel < 3; ++channel)
                rgb[channel] = new SigmaQ48Interval(
                    SigmaNumericDomain.QSub(colour[channel], channelWidth),
                    SigmaNumericDomain.QAdd(colour[channel], channelWidth));
            Assert.That(SigmaRgbInverse.TryBuildCell(prior, y, matrix, rgb,
                Q(0.01), SigmaRgbSourceClass.Left, 23u,
                out SigmaRgbAdmissibleCell cell), Is.True);
            Assert.That(cell.CoordinateMask, Is.Not.Zero);
            for (int lane = 0; lane < 16; ++lane)
                Assert.That(cell[lane].Contains(y[lane]), Is.True,
                    $"known solution excluded at lane {lane}: {cell[lane]}");
        }

        [Test]
        public void LeftRightMeetIsExactAndSourceOrderInvariant()
        {
            SigmaQ48Interval[] prior = UniformBox(-10, 10);
            SigmaQ48Interval[] left = UniformBox(-8, 7);
            SigmaQ48Interval[] right = UniformBox(-3, 5);
            var l = new SigmaRgbAdmissibleCell(left, SigmaRgbSourceClass.Left,
                1u, ushort.MaxValue);
            var r = new SigmaRgbAdmissibleCell(right, SigmaRgbSourceClass.Right,
                2u, ushort.MaxValue);
            SigmaQ48Interval[] lr = SigmaRgbInverse.Meet(prior, new[] { l, r });
            SigmaQ48Interval[] rl = SigmaRgbInverse.Meet(prior, new[] { r, l });
            CollectionAssert.AreEqual(lr, rl);
            Assert.That(lr[0], Is.EqualTo(new SigmaQ48Interval(-3, 5)));
        }

        [Test]
        public void IncompatibleRgbDepthStyleCellStaysExplicitlyEmpty()
        {
            SigmaQ48Interval[] prior = UniformBox(-10, 10);
            SigmaQ48Interval[] depth = UniformBox(-5, 0);
            SigmaQ48Interval[] rgb = UniformBox(1, 6);
            var d = new SigmaRgbAdmissibleCell(depth,
                SigmaRgbSourceClass.Temporal, 1u, ushort.MaxValue);
            var c = new SigmaRgbAdmissibleCell(rgb,
                SigmaRgbSourceClass.Left, 2u, ushort.MaxValue);
            SigmaQ48Interval[] meet = SigmaRgbInverse.Meet(prior,
                new[] { d, c });
            Assert.That(meet[0].IsEmpty, Is.True);
            Assert.That(prior[0], Is.EqualTo(new SigmaQ48Interval(-10, 10)),
                "forming an empty meet may not mutate its prior");
        }

        [Test]
        public void CertificateCoalescingAndMinimalProofAreDeterministic()
        {
            SigmaQ48Interval[] broad = UniformBox(-8, 8);
            SigmaQ48Interval[] narrow = UniformBox(-3, 3);
            var a = Certificate(7, broad, SigmaRgbSourceClass.Left, 2);
            var b = Certificate(7, narrow, SigmaRgbSourceClass.Left, 2);
            SigmaConstraintCertificate[] coalesced =
                SigmaRgbInverse.CoalesceCertificates(new[] { a, b });
            Assert.That(coalesced.Length, Is.EqualTo(1));
            Assert.That(coalesced[0].Bounds[0],
                Is.EqualTo(new SigmaQ48Interval(-3, 3)));

            var redundant = Certificate(7, narrow,
                SigmaRgbSourceClass.Right, 3);
            SigmaConstraintCertificate[] minimal = SigmaRgbInverse.MinimizeProofSet(
                new[] { redundant, coalesced[0] }, PreservesNarrowMeet);
            Assert.That(minimal.Length, Is.EqualTo(1));
            Assert.That(minimal[0].IndependenceKey, Is.EqualTo(2u),
                "reverse lexicographic sweep retains the first proof");

            SigmaQ48Interval[] disjoint = UniformBox(9, 11);
            var conflict = Certificate(7, disjoint,
                SigmaRgbSourceClass.Left, 2);
            Assert.That(SigmaRgbInverse.CoalesceCertificates(
                new[] { a, conflict }).Length, Is.EqualTo(2),
                "empty coalescing intersections remain explicit evidence");
        }

        [Test]
        public void DifferentlyPhasedCellsSharpenByIntersectionNotSummation()
        {
            SigmaQ48Interval[] prior = UniformBox(-100, 100);
            SigmaQ48Interval[] phaseA = UniformBox(-20, 12);
            SigmaQ48Interval[] phaseB = UniformBox(-5, 24);
            var a = new SigmaRgbAdmissibleCell(phaseA,
                SigmaRgbSourceClass.Left, 101u, ushort.MaxValue);
            var b = new SigmaRgbAdmissibleCell(phaseB,
                SigmaRgbSourceClass.Left, 202u, ushort.MaxValue);
            SigmaQ48Interval[] joint = SigmaRgbInverse.Meet(prior,
                new[] { a, b });
            Assert.That(joint[0], Is.EqualTo(new SigmaQ48Interval(-5, 12)));
            Assert.That(joint[0].WidthRaw, Is.LessThan(phaseA[0].WidthRaw));
            Assert.That(joint[0].WidthRaw, Is.LessThan(phaseB[0].WidthRaw));
        }

        private static bool PreservesNarrowMeet(
            IReadOnlyList<SigmaConstraintCertificate> proof)
        {
            if (proof.Count == 0)
                return false;
            SigmaQ48Interval meet = SigmaQ48Interval.Full;
            for (int index = 0; index < proof.Count; ++index)
                meet = meet.Intersect(proof[index].Bounds[0]);
            return meet == new SigmaQ48Interval(-3, 3);
        }

        private static SigmaConstraintCertificate Certificate(ulong block,
            IReadOnlyList<SigmaQ48Interval> bounds, SigmaRgbSourceClass source,
            uint key) => new(block, ushort.MaxValue, bounds, (byte)source, key,
                9u, SigmaRgbInverse.RoleAppearance);

        private static (long[] Y, long[] Colour) FindObservableProjectivePoint(
            long[,] matrix)
        {
            for (int seed = 1; seed < 5000; ++seed)
            {
                var y = new long[16];
                for (int lane = 0; lane < 16; ++lane)
                    y[lane] = Q((((seed * (lane + 5) * 37) % 101) - 50) /
                        160.0);
                long[] q = Multiply(matrix, y);
                long denominator = Math.Abs(q[0]);
                if (denominator < Q(0.15))
                    continue;
                var colour = new long[3];
                bool valid = true;
                for (int channel = 0; channel < 3; ++channel)
                {
                    colour[channel] = SigmaNumericDomain.QDiv(q[channel + 1],
                        denominator);
                    valid &= colour[channel] > Q(0.08) &&
                        colour[channel] < Q(0.92);
                }
                if (valid)
                    return (y, colour);
            }
            throw new AssertionException("Unable to construct observable RGB fixture.");
        }

        private static int FindObservableLane(long[,] matrix,
            IReadOnlyList<long> y)
        {
            long[] projected = Multiply(matrix, y);
            long denominator = projected[0];
            for (int lane = 0; lane < 16; ++lane)
            for (int channel = 0; channel < 3; ++channel)
            {
                long first = SigmaNumericDomain.QMul(
                    matrix[channel + 1, lane], denominator);
                long second = SigmaNumericDomain.QMul(
                    projected[channel + 1], matrix[0, lane]);
                if (first != second)
                    return lane;
            }
            throw new AssertionException(
                "Generated view fixture has no locally observable coordinate.");
        }

        private static long[] Multiply(long[,] matrix, IReadOnlyList<long> value)
        {
            var result = new long[4];
            for (int row = 0; row < 4; ++row)
            for (int lane = 0; lane < 16; ++lane)
                result[row] = SigmaNumericDomain.QAdd(result[row],
                    SigmaNumericDomain.QMul(matrix[row, lane], value[lane]));
            return result;
        }

        private static SigmaQ48Interval[] BoxAround(IReadOnlyList<long> centre,
            long width)
        {
            var result = new SigmaQ48Interval[16];
            for (int lane = 0; lane < 16; ++lane)
                result[lane] = new SigmaQ48Interval(
                    SigmaNumericDomain.QSub(centre[lane], width),
                    SigmaNumericDomain.QAdd(centre[lane], width));
            return result;
        }

        private static SigmaQ48Interval[] UniformBox(long lower, long upper)
        {
            var result = new SigmaQ48Interval[16];
            for (int lane = 0; lane < 16; ++lane)
                result[lane] = new SigmaQ48Interval(lower, upper);
            return result;
        }

        private static SigmaQ48Interval Interval(double lower, double upper) =>
            new(Q(lower), Q(upper));
        private static SigmaS16 Direction(int x, int y, int z) => new(0,
            x * Q(1), y * Q(1), z * Q(1), 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0);
        private static long Q(double value) => SigmaNumericDomain.Quantize(value);
    }
}
