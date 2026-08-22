using System;
using System.Collections.Generic;

namespace Genesis.RoomScan.SigmaPrism
{
    public enum SigmaRgbSourceClass : byte
    {
        Left = 3,
        Right = 4,
        Temporal = 5,
    }

    public readonly struct SigmaRgbAdmissibleCell
    {
        private readonly SigmaQ48Interval[] _coordinates;

        public SigmaRgbAdmissibleCell(
            IReadOnlyList<SigmaQ48Interval> coordinates,
            SigmaRgbSourceClass source,
            uint independenceKey,
            ushort coordinateMask)
        {
            if (coordinates == null || coordinates.Count != SigmaS16.LaneCount)
                throw new ArgumentException(
                    "RGB cells require sixteen projective coordinates.",
                    nameof(coordinates));
            _coordinates = new SigmaQ48Interval[SigmaS16.LaneCount];
            for (int lane = 0; lane < _coordinates.Length; ++lane)
                _coordinates[lane] = coordinates[lane];
            Source = source;
            IndependenceKey = independenceKey;
            CoordinateMask = coordinateMask;
        }

        public SigmaRgbSourceClass Source { get; }
        public uint IndependenceKey { get; }
        public ushort CoordinateMask { get; }
        public SigmaQ48Interval this[int lane] => _coordinates[lane];
        public IReadOnlyList<SigmaQ48Interval> Coordinates => _coordinates;
        public bool IsInformative => CoordinateMask != 0;
    }

    public readonly struct SigmaConstraintCertificate : IComparable<SigmaConstraintCertificate>
    {
        public SigmaConstraintCertificate(
            ulong carrierBlock,
            ushort coordinateMask,
            IReadOnlyList<SigmaQ48Interval> bounds,
            byte sourceClass,
            uint independenceKey,
            uint calibrationEpoch,
            uint roleMask)
        {
            if (bounds == null || bounds.Count != SigmaS16.LaneCount)
                throw new ArgumentException(
                    "Certificates carry sixteen canonical coordinate bounds.",
                    nameof(bounds));
            CarrierBlock = carrierBlock;
            CoordinateMask = coordinateMask;
            var copy = new SigmaQ48Interval[SigmaS16.LaneCount];
            for (int lane = 0; lane < copy.Length; ++lane)
                copy[lane] = bounds[lane];
            Bounds = copy;
            SourceClass = sourceClass;
            IndependenceKey = independenceKey;
            CalibrationEpoch = calibrationEpoch;
            RoleMask = roleMask;
        }

        public ulong CarrierBlock { get; }
        public ushort CoordinateMask { get; }
        public IReadOnlyList<SigmaQ48Interval> Bounds { get; }
        public byte SourceClass { get; }
        public uint IndependenceKey { get; }
        public uint CalibrationEpoch { get; }
        public uint RoleMask { get; }

        public int CompareTo(SigmaConstraintCertificate other)
        {
            int order = CarrierBlock.CompareTo(other.CarrierBlock);
            if (order != 0) return order;
            order = RoleMask.CompareTo(other.RoleMask);
            if (order != 0) return order;
            order = IndependenceKey.CompareTo(other.IndependenceKey);
            if (order != 0) return order;
            order = SourceClass.CompareTo(other.SourceClass);
            if (order != 0) return order;
            order = CalibrationEpoch.CompareTo(other.CalibrationEpoch);
            if (order != 0) return order;
            order = CoordinateMask.CompareTo(other.CoordinateMask);
            if (order != 0) return order;
            for (int lane = 0; lane < SigmaS16.LaneCount; ++lane)
            {
                order = Bounds[lane].Lower.CompareTo(other.Bounds[lane].Lower);
                if (order != 0) return order;
                order = Bounds[lane].Upper.CompareTo(other.Bounds[lane].Upper);
                if (order != 0) return order;
            }
            return 0;
        }
    }

    /// <summary>
    /// Exact CPU semantic oracle for section 12.5 and section 30. Live samples use
    /// the equivalent packed-Q16.48 GPU lowering; this type owns no texture or
    /// geometry state.
    /// </summary>
    public static class SigmaRgbInverse
    {
        public const uint RoleSupport = 1u << 0;
        public const uint RoleAppearance = 1u << 2;

        /// <summary>Build A_omega = F T_omega B^T / 16 for a fixed quantized
        /// direction. The returned four rows are appearance support and RGB.</summary>
        public static long[,] BuildGeneratedViewMatrix(SigmaS16 quantizedView)
        {
            var matrix = new long[4, SigmaS16.LaneCount];
            for (int yLane = 0; yLane < SigmaS16.LaneCount; ++yLane)
            {
                SigmaS16 y = SigmaS16.Basis(yLane, SigmaNumericDomain.One);
                SigmaS16 inverse = SigmaS16Operators.HadamardBT(y);
                var projective = new long[SigmaS16.LaneCount];
                for (int lane = 0; lane < projective.Length; ++lane)
                    projective[lane] = SigmaNumericDomain.QShiftRight(
                        inverse[lane], 4);
                SigmaS16 viewed = SigmaOperatorEvaluator.EvaluateS16(
                    SigmaOperatorPlans.View,
                    SigmaS16.FromArray(projective),
                    quantizedView);
                long[] hidden = SigmaS16Operators.HiddenReadout(viewed);
                for (int row = 0; row < 4; ++row)
                    matrix[row, yLane] = hidden[row];
            }
            return matrix;
        }

        public static bool TryBuildCell(
            IReadOnlyList<SigmaQ48Interval> prior,
            IReadOnlyList<long> currentY,
            long[,] viewMatrix,
            IReadOnlyList<SigmaQ48Interval> rgb,
            long appearanceSupportFloor,
            SigmaRgbSourceClass source,
            uint independenceKey,
            out SigmaRgbAdmissibleCell cell)
        {
            ValidateDimensions(prior, currentY, viewMatrix, rgb);
            var box = CopyBounds(prior);
            SigmaQ48Interval denominator = EvaluateRow(box, viewMatrix, 0);
            int denominatorSign;
            if (denominator.Lower >= appearanceSupportFloor)
                denominatorSign = 1;
            else if (denominator.Upper <= -appearanceSupportFloor)
                denominatorSign = -1;
            else
            {
                cell = new SigmaRgbAdmissibleCell(box, source,
                    independenceKey, 0);
                return false;
            }

            var inequalities = BuildRatioInequalities(viewMatrix, rgb,
                appearanceSupportFloor, denominatorSign);
            // Exactly two forward, then two reverse constraint sweeps.  Each
            // inequality is a Jacobi box contraction from one immutable input
            // snapshot, matching the bounded Quest lowering.
            for (int repeat = 0; repeat < 2; ++repeat)
                for (int row = 0; row < inequalities.Count; ++row)
                    ContractLessOrEqual(box, inequalities[row]);
            for (int repeat = 0; repeat < 2; ++repeat)
                for (int row = inequalities.Count - 1; row >= 0; --row)
                    ContractLessOrEqual(box, inequalities[row]);

            ushort mask = 0;
            for (int lane = 0; lane < SigmaS16.LaneCount; ++lane)
            {
                if (box[lane].IsEmpty)
                {
                    cell = new SigmaRgbAdmissibleCell(box, source,
                        independenceKey, ushort.MaxValue);
                    return true;
                }
                if (box[lane] != prior[lane])
                    mask |= (ushort)(1u << lane);
            }
            cell = new SigmaRgbAdmissibleCell(box, source,
                independenceKey, mask);
            return mask != 0;
        }

        public static SigmaQ48Interval[] Meet(
            IReadOnlyList<SigmaQ48Interval> prior,
            IReadOnlyList<SigmaRgbAdmissibleCell> cells)
        {
            if (prior == null || prior.Count != SigmaS16.LaneCount || cells == null)
                throw new ArgumentException(
                    "A joint RGB meet requires one sixteen-lane prior.");
            SigmaQ48Interval[] result = CopyBounds(prior);
            for (int source = 0; source < cells.Count; ++source)
            for (int lane = 0; lane < SigmaS16.LaneCount; ++lane)
                result[lane] = result[lane].Intersect(cells[source][lane]);
            return result;
        }

        public static SigmaConstraintCertificate[] CoalesceCertificates(
            IReadOnlyList<SigmaConstraintCertificate> input)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));
            var sorted = new List<SigmaConstraintCertificate>(input);
            sorted.Sort();
            var output = new List<SigmaConstraintCertificate>();
            for (int index = 0; index < sorted.Count; ++index)
            {
                SigmaConstraintCertificate incoming = sorted[index];
                if (output.Count == 0 || !SameCoalesceKey(
                        output[output.Count - 1], incoming))
                {
                    output.Add(incoming);
                    continue;
                }
                SigmaConstraintCertificate prior = output[output.Count - 1];
                var bounds = new SigmaQ48Interval[SigmaS16.LaneCount];
                ushort mask = (ushort)(prior.CoordinateMask |
                    incoming.CoordinateMask);
                bool empty = false;
                for (int lane = 0; lane < bounds.Length; ++lane)
                {
                    bounds[lane] = prior.Bounds[lane].Intersect(
                        incoming.Bounds[lane]);
                    empty |= bounds[lane].IsEmpty;
                }
                // An empty meet is explicit unresolved evidence, never erased.
                if (empty)
                    output.Add(incoming);
                else
                    output[output.Count - 1] = new SigmaConstraintCertificate(
                        prior.CarrierBlock, mask, bounds, prior.SourceClass,
                        prior.IndependenceKey, prior.CalibrationEpoch,
                        prior.RoleMask);
            }
            return output.ToArray();
        }

        public static SigmaConstraintCertificate[] MinimizeProofSet(
            IReadOnlyList<SigmaConstraintCertificate> input,
            Func<IReadOnlyList<SigmaConstraintCertificate>, bool> preserves)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));
            if (preserves == null)
                throw new ArgumentNullException(nameof(preserves));
            var proof = new List<SigmaConstraintCertificate>(
                CoalesceCertificates(input));
            bool changed;
            do
            {
                changed = false;
                for (int index = proof.Count - 1; index >= 0; --index)
                {
                    var trial = new List<SigmaConstraintCertificate>(proof);
                    trial.RemoveAt(index);
                    if (!preserves(trial))
                        continue;
                    proof = trial;
                    changed = true;
                }
            } while (changed);
            return proof.ToArray();
        }

        private static List<LinearInequality> BuildRatioInequalities(
            long[,] matrix,
            IReadOnlyList<SigmaQ48Interval> rgb,
            long supportFloor,
            int denominatorSign)
        {
            var result = new List<LinearInequality>(7);
            long sign = denominatorSign > 0
                ? SigmaNumericDomain.One : -SigmaNumericDomain.One;
            var support = new long[SigmaS16.LaneCount];
            for (int lane = 0; lane < support.Length; ++lane)
                support[lane] = SigmaNumericDomain.QNegate(
                    SigmaNumericDomain.QMul(sign, matrix[0, lane]));
            result.Add(new LinearInequality(support,
                SigmaNumericDomain.QNegate(supportFloor)));
            for (int channel = 0; channel < 3; ++channel)
            {
                var low = new long[SigmaS16.LaneCount];
                var high = new long[SigmaS16.LaneCount];
                for (int lane = 0; lane < SigmaS16.LaneCount; ++lane)
                {
                    long denominator = SigmaNumericDomain.QMul(sign,
                        matrix[0, lane]);
                    low[lane] = SigmaNumericDomain.QSub(
                        SigmaNumericDomain.QMul(rgb[channel].Lower,
                            denominator),
                        matrix[channel + 1, lane]);
                    high[lane] = SigmaNumericDomain.QSub(
                        matrix[channel + 1, lane],
                        SigmaNumericDomain.QMul(rgb[channel].Upper,
                            denominator));
                }
                result.Add(new LinearInequality(low, 0));
                result.Add(new LinearInequality(high, 0));
            }
            return result;
        }

        private static void ContractLessOrEqual(
            SigmaQ48Interval[] box,
            LinearInequality inequality)
        {
            var snapshot = CopyBounds(box);
            var next = CopyBounds(box);
            var minimumContribution = new long[SigmaS16.LaneCount];
            long totalMinimum = 0;
            for (int coordinate = 0; coordinate < SigmaS16.LaneCount;
                 ++coordinate)
            {
                long coefficient = inequality.Coefficients[coordinate];
                SigmaQ48Interval product = coefficient == 0
                    ? new SigmaQ48Interval(0, 0)
                    : Product(coefficient, snapshot[coordinate]);
                minimumContribution[coordinate] = product.Lower;
                totalMinimum = SigmaNumericDomain.QAdd(totalMinimum,
                    product.Lower);
            }
            for (int coordinate = 0; coordinate < SigmaS16.LaneCount;
                 ++coordinate)
            {
                long coefficient = inequality.Coefficients[coordinate];
                if (coefficient == 0 || snapshot[coordinate].IsEmpty)
                    continue;
                long otherMinimum = SigmaNumericDomain.QSub(totalMinimum,
                    minimumContribution[coordinate]);
                long numerator = SigmaNumericDomain.QSub(
                    inequality.Upper, otherMinimum);
                SigmaQ48Interval narrowed = coefficient > 0
                    ? new SigmaQ48Interval(snapshot[coordinate].Lower,
                        Math.Min(snapshot[coordinate].Upper,
                            SigmaNumericDomain.QDivUpper(numerator,
                                coefficient)))
                    : new SigmaQ48Interval(
                        Math.Max(snapshot[coordinate].Lower,
                            SigmaNumericDomain.QDivLower(numerator,
                                coefficient)),
                        snapshot[coordinate].Upper);
                next[coordinate] = narrowed;
            }
            for (int coordinate = 0; coordinate < SigmaS16.LaneCount;
                 ++coordinate)
                box[coordinate] = next[coordinate];
        }

        private static SigmaQ48Interval EvaluateRow(
            IReadOnlyList<SigmaQ48Interval> box,
            long[,] matrix,
            int row)
        {
            var result = new SigmaQ48Interval(0, 0);
            for (int lane = 0; lane < SigmaS16.LaneCount; ++lane)
            {
                if (matrix[row, lane] == 0)
                    continue;
                SigmaQ48Interval product = Product(matrix[row, lane], box[lane]);
                result = new SigmaQ48Interval(
                    SigmaNumericDomain.QAdd(result.Lower, product.Lower),
                    SigmaNumericDomain.QAdd(result.Upper, product.Upper));
            }
            return result;
        }

        private static SigmaQ48Interval Product(
            long coefficient,
            SigmaQ48Interval value)
        {
            long lo0 = SigmaNumericDomain.QMulLower(coefficient, value.Lower);
            long lo1 = SigmaNumericDomain.QMulLower(coefficient, value.Upper);
            long hi0 = SigmaNumericDomain.QMulUpper(coefficient, value.Lower);
            long hi1 = SigmaNumericDomain.QMulUpper(coefficient, value.Upper);
            return new SigmaQ48Interval(Math.Min(lo0, lo1),
                Math.Max(hi0, hi1));
        }

        private static SigmaQ48Interval[] CopyBounds(
            IReadOnlyList<SigmaQ48Interval> source)
        {
            if (source == null || source.Count != SigmaS16.LaneCount)
                throw new ArgumentException("Expected sixteen bounds.",
                    nameof(source));
            var result = new SigmaQ48Interval[SigmaS16.LaneCount];
            for (int lane = 0; lane < result.Length; ++lane)
                result[lane] = source[lane];
            return result;
        }

        private static bool SameCoalesceKey(
            SigmaConstraintCertificate left,
            SigmaConstraintCertificate right) =>
            left.CarrierBlock == right.CarrierBlock &&
            left.RoleMask == right.RoleMask &&
            left.IndependenceKey == right.IndependenceKey &&
            left.SourceClass == right.SourceClass &&
            left.CalibrationEpoch == right.CalibrationEpoch;

        private static void ValidateDimensions(
            IReadOnlyList<SigmaQ48Interval> prior,
            IReadOnlyList<long> currentY,
            long[,] viewMatrix,
            IReadOnlyList<SigmaQ48Interval> rgb)
        {
            if (prior == null || currentY == null || rgb == null ||
                prior.Count != SigmaS16.LaneCount ||
                currentY.Count != SigmaS16.LaneCount ||
                rgb.Count != 3 || viewMatrix == null ||
                viewMatrix.GetLength(0) != 4 ||
                viewMatrix.GetLength(1) != SigmaS16.LaneCount)
                throw new ArgumentException("Invalid RGB inverse dimensions.");
        }

        private readonly struct LinearInequality
        {
            internal LinearInequality(long[] coefficients, long upper)
            {
                Coefficients = coefficients;
                Upper = upper;
            }

            internal long[] Coefficients { get; }
            internal long Upper { get; }
        }
    }
}
