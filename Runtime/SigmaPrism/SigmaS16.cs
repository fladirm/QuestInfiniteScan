using System;
using System.Numerics;

namespace Genesis.RoomScan.SigmaPrism
{
    /// <summary>
    /// Immutable exact sixteen-lane value in the canonical Q16.48 domain. This is
    /// an algebra value, not a second world object or an execution-storage layout.
    /// </summary>
    public readonly struct SigmaS16 : IEquatable<SigmaS16>
    {
        public const int LaneCount = 16;

        private readonly long _x0;
        private readonly long _x1;
        private readonly long _x2;
        private readonly long _x3;
        private readonly long _x4;
        private readonly long _x5;
        private readonly long _x6;
        private readonly long _x7;
        private readonly long _x8;
        private readonly long _x9;
        private readonly long _x10;
        private readonly long _x11;
        private readonly long _x12;
        private readonly long _x13;
        private readonly long _x14;
        private readonly long _x15;

        public SigmaS16(long x0, long x1, long x2, long x3,
            long x4, long x5, long x6, long x7,
            long x8, long x9, long x10, long x11,
            long x12, long x13, long x14, long x15)
        {
            _x0 = x0;
            _x1 = x1;
            _x2 = x2;
            _x3 = x3;
            _x4 = x4;
            _x5 = x5;
            _x6 = x6;
            _x7 = x7;
            _x8 = x8;
            _x9 = x9;
            _x10 = x10;
            _x11 = x11;
            _x12 = x12;
            _x13 = x13;
            _x14 = x14;
            _x15 = x15;
        }

        public long this[int lane]
        {
            get
            {
                return lane switch
                {
                    0 => _x0,
                    1 => _x1,
                    2 => _x2,
                    3 => _x3,
                    4 => _x4,
                    5 => _x5,
                    6 => _x6,
                    7 => _x7,
                    8 => _x8,
                    9 => _x9,
                    10 => _x10,
                    11 => _x11,
                    12 => _x12,
                    13 => _x13,
                    14 => _x14,
                    15 => _x15,
                    _ => throw new ArgumentOutOfRangeException(nameof(lane)),
                };
            }
        }

        public static SigmaS16 Zero => default;

        public static SigmaS16 FromArray(long[] lanes)
        {
            if (lanes == null)
                throw new ArgumentNullException(nameof(lanes));
            if (lanes.Length != LaneCount)
                throw new ArgumentException("An S16 value has exactly sixteen lanes.",
                    nameof(lanes));
            return new SigmaS16(lanes[0], lanes[1], lanes[2], lanes[3],
                lanes[4], lanes[5], lanes[6], lanes[7],
                lanes[8], lanes[9], lanes[10], lanes[11],
                lanes[12], lanes[13], lanes[14], lanes[15]);
        }

        public static SigmaS16 Basis(int lane, long coefficient)
        {
            if ((uint)lane >= LaneCount)
                throw new ArgumentOutOfRangeException(nameof(lane));
            var lanes = new long[LaneCount];
            lanes[lane] = coefficient;
            return FromArray(lanes);
        }

        public long[] ToArray() =>
            new[] { _x0, _x1, _x2, _x3, _x4, _x5, _x6, _x7,
                _x8, _x9, _x10, _x11, _x12, _x13, _x14, _x15 };

        public bool IsZero
        {
            get
            {
                long aggregate = _x0 | _x1 | _x2 | _x3 | _x4 | _x5 | _x6 | _x7 |
                    _x8 | _x9 | _x10 | _x11 | _x12 | _x13 | _x14 | _x15;
                return aggregate == 0L;
            }
        }

        public bool Equals(SigmaS16 other)
        {
            for (int lane = 0; lane < LaneCount; ++lane)
            {
                if (this[lane] != other[lane])
                    return false;
            }
            return true;
        }

        public override bool Equals(object obj) => obj is SigmaS16 other && Equals(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            for (int lane = 0; lane < LaneCount; ++lane)
                hash.Add(this[lane]);
            return hash.ToHashCode();
        }

        public static bool operator ==(SigmaS16 left, SigmaS16 right) => left.Equals(right);
        public static bool operator !=(SigmaS16 left, SigmaS16 right) => !left.Equals(right);

        public override string ToString() => $"S16[{string.Join(",", ToArray())}]";
    }

    /// <summary>Canonical sparse signed basis dyad ±e_i ±e_j.</summary>
    public readonly struct SigmaSignedDyad : IEquatable<SigmaSignedDyad>,
        IComparable<SigmaSignedDyad>
    {
        public SigmaSignedDyad(int firstIndex, int firstSign,
            int secondIndex, int secondSign)
        {
            if ((uint)firstIndex >= SigmaS16.LaneCount)
                throw new ArgumentOutOfRangeException(nameof(firstIndex));
            if ((uint)secondIndex >= SigmaS16.LaneCount || secondIndex <= firstIndex)
                throw new ArgumentOutOfRangeException(nameof(secondIndex));
            if ((firstSign != -1 && firstSign != 1) ||
                (secondSign != -1 && secondSign != 1))
                throw new ArgumentOutOfRangeException(nameof(firstSign),
                    "Dyad signs are exactly -1 or +1.");
            FirstIndex = firstIndex;
            FirstSign = firstSign;
            SecondIndex = secondIndex;
            SecondSign = secondSign;
        }

        public int FirstIndex { get; }
        public int FirstSign { get; }
        public int SecondIndex { get; }
        public int SecondSign { get; }

        public SigmaS16 ToS16()
        {
            var lanes = new long[SigmaS16.LaneCount];
            lanes[FirstIndex] = FirstSign * SigmaNumericDomain.One;
            lanes[SecondIndex] = SecondSign * SigmaNumericDomain.One;
            return SigmaS16.FromArray(lanes);
        }

        public int CompareTo(SigmaSignedDyad other)
        {
            int result = FirstIndex.CompareTo(other.FirstIndex);
            if (result != 0) return result;
            result = FirstSign.CompareTo(other.FirstSign);
            if (result != 0) return result;
            result = SecondIndex.CompareTo(other.SecondIndex);
            return result != 0 ? result : SecondSign.CompareTo(other.SecondSign);
        }

        public bool Equals(SigmaSignedDyad other) =>
            FirstIndex == other.FirstIndex && FirstSign == other.FirstSign &&
            SecondIndex == other.SecondIndex && SecondSign == other.SecondSign;
        public override bool Equals(object obj) =>
            obj is SigmaSignedDyad other && Equals(other);
        public override int GetHashCode() =>
            HashCode.Combine(FirstIndex, FirstSign, SecondIndex, SecondSign);
        public static bool operator ==(SigmaSignedDyad left, SigmaSignedDyad right) =>
            left.Equals(right);
        public static bool operator !=(SigmaSignedDyad left, SigmaSignedDyad right) =>
            !left.Equals(right);
    }

    public readonly struct SigmaZeroDivisorEntry
    {
        public SigmaZeroDivisorEntry(SigmaSignedDyad witness,
            SigmaSignedDyad annihilator, int actionIndex)
        {
            Witness = witness;
            Annihilator = annihilator;
            ActionIndex = actionIndex;
        }

        public SigmaSignedDyad Witness { get; }
        public SigmaSignedDyad Annihilator { get; }
        public int ActionIndex { get; }
    }

    /// <summary>Exact reference algebra plus generated sparse hot-path actions.</summary>
    public static class SigmaS16Operators
    {
        public static string BundleFingerprint => SigmaGeneratedAlgebra.BundleFingerprint;

        public static SigmaSignedDyad ZeroDivisorDonorDyad => new(
            SigmaGeneratedAlgebra.ZNullDyad[0], SigmaGeneratedAlgebra.ZNullDyad[1],
            SigmaGeneratedAlgebra.ZNullDyad[2], SigmaGeneratedAlgebra.ZNullDyad[3]);

        // The generated dyad above is an exact zero-divisor witness only.  It is
        // not the native no-manifestation state.  N1R proves the complete-program
        // ZEmpty representative to be algebraic zero; sparse backing and the NULL
        // codec must therefore decode to these exact bytes in every context.
        public static SigmaS16 ZEmpty => SigmaS16.Zero;
        public static SigmaS16 NullState => ZEmpty;

        public static int BasisProductIndex(int left, int right)
        {
            ValidateBasis(left);
            ValidateBasis(right);
            return SigmaGeneratedAlgebra.MultiplicationIndices[(left << 4) + right];
        }

        public static int BasisProductSign(int left, int right)
        {
            ValidateBasis(left);
            ValidateBasis(right);
            return SigmaGeneratedAlgebra.MultiplicationSigns[(left << 4) + right];
        }

        public static SigmaS16 Conjugate(SigmaS16 value)
        {
            var output = new long[SigmaS16.LaneCount];
            output[0] = value[0];
            for (int lane = 1; lane < SigmaS16.LaneCount; ++lane)
                output[lane] = SigmaNumericDomain.QNegate(value[lane]);
            return SigmaS16.FromArray(output);
        }

        /// <summary>
        /// Dense coefficient product is deliberately a semantic oracle/fallback,
        /// never the default lowering for sparse basis/dyad/readout operators.
        /// </summary>
        public static SigmaS16 DenseReferenceMultiply(SigmaS16 left, SigmaS16 right)
        {
            var output = new long[SigmaS16.LaneCount];
            for (int leftLane = 0; leftLane < SigmaS16.LaneCount; ++leftLane)
            {
                for (int rightLane = 0; rightLane < SigmaS16.LaneCount; ++rightLane)
                {
                    long product = SigmaNumericDomain.QMul(left[leftLane], right[rightLane]);
                    int offset = (leftLane << 4) + rightLane;
                    if (SigmaGeneratedAlgebra.MultiplicationSigns[offset] < 0)
                        product = SigmaNumericDomain.QNegate(product);
                    int outputLane = SigmaGeneratedAlgebra.MultiplicationIndices[offset];
                    output[outputLane] = SigmaNumericDomain.QAdd(output[outputLane], product);
                }
            }
            return SigmaS16.FromArray(output);
        }

        public static SigmaS16 LeftBasisAction(int basis, SigmaS16 value) =>
            BasisAction(basis, value, SigmaGeneratedAlgebra.LeftBasisSources,
                SigmaGeneratedAlgebra.LeftBasisSigns);

        public static SigmaS16 RightBasisAction(SigmaS16 value, int basis) =>
            BasisAction(basis, value, SigmaGeneratedAlgebra.RightBasisSources,
                SigmaGeneratedAlgebra.RightBasisSigns);

        public static SigmaS16 LeftSignedDyadAction(SigmaSignedDyad dyad,
            SigmaS16 value) => CombineDyad(
                LeftBasisAction(dyad.FirstIndex, value), dyad.FirstSign,
                LeftBasisAction(dyad.SecondIndex, value), dyad.SecondSign);

        public static SigmaS16 RightSignedDyadAction(SigmaS16 value,
            SigmaSignedDyad dyad) => CombineDyad(
                RightBasisAction(value, dyad.FirstIndex), dyad.FirstSign,
                RightBasisAction(value, dyad.SecondIndex), dyad.SecondSign);

        public static SigmaS16 HadamardB(SigmaS16 value)
        {
            var output = new long[SigmaS16.LaneCount];
            for (int row = 0; row < SigmaS16.LaneCount; ++row)
                output[row] = HadamardRow(value, row);
            return SigmaS16.FromArray(output);
        }

        public static SigmaS16 HadamardBT(SigmaS16 value) => HadamardB(value);

        public static long HadamardRow(SigmaS16 value, int row)
        {
            ValidateBasis(row);
            long sum = 0L;
            int rowOffset = row << 4;
            for (int lane = 0; lane < SigmaS16.LaneCount; ++lane)
            {
                long term = value[lane];
                if (SigmaGeneratedAlgebra.HadamardSigns[rowOffset + lane] < 0)
                    term = SigmaNumericDomain.QNegate(term);
                sum = SigmaNumericDomain.QAdd(sum, term);
            }
            return sum;
        }

        public static long[] GeometryReadout(SigmaS16 value)
        {
            var output = new long[4];
            for (int index = 0; index < output.Length; ++index)
                output[index] = HadamardRow(value,
                    SigmaGeneratedAlgebra.GeometryRows[index]);
            return output;
        }

        public static long[] HiddenReadout(SigmaS16 value)
        {
            var output = new long[12];
            for (int index = 0; index < output.Length; ++index)
                output[index] = HadamardRow(value,
                    SigmaGeneratedAlgebra.HiddenRows[index]);
            return output;
        }

        public static SigmaS16 Transition(SigmaS16 left, SigmaS16 right) =>
            DenseReferenceMultiply(Conjugate(left), right);

        public static SigmaS16 Associator(SigmaS16 a, SigmaS16 b, SigmaS16 c)
        {
            SigmaS16 leftBracket = DenseReferenceMultiply(
                DenseReferenceMultiply(a, b), c);
            SigmaS16 rightBracket = DenseReferenceMultiply(a,
                DenseReferenceMultiply(b, c));
            return Subtract(leftBracket, rightBracket);
        }

        public static long L1RawChecked(SigmaS16 value)
        {
            BigInteger sum = BigInteger.Zero;
            for (int lane = 0; lane < SigmaS16.LaneCount; ++lane)
                sum += BigInteger.Abs(new BigInteger(value[lane]));
            if (sum > long.MaxValue)
                throw new OverflowException("S16 L1 accumulator overflow.");
            return (long)sum;
        }

        public static SigmaSignedDyad GetAnnihilatorAction(int actionIndex)
        {
            if ((uint)actionIndex >= SigmaGeneratedAlgebra.AnnihilatorActionCount)
                throw new ArgumentOutOfRangeException(nameof(actionIndex));
            int offset = actionIndex * SigmaGeneratedAlgebra.AnnihilatorActionStride;
            return new SigmaSignedDyad(
                SigmaGeneratedAlgebra.AnnihilatorActions[offset],
                SigmaGeneratedAlgebra.AnnihilatorActions[offset + 1],
                SigmaGeneratedAlgebra.AnnihilatorActions[offset + 2],
                SigmaGeneratedAlgebra.AnnihilatorActions[offset + 3]);
        }

        public static SigmaZeroDivisorEntry GetZeroDivisorEntry(int catalogIndex)
        {
            if ((uint)catalogIndex >= SigmaGeneratedAlgebra.ZeroDivisorCatalogCount)
                throw new ArgumentOutOfRangeException(nameof(catalogIndex));
            int offset = catalogIndex * SigmaGeneratedAlgebra.ZeroDivisorCatalogStride;
            short[] data = SigmaGeneratedAlgebra.ZeroDivisorCatalog;
            return new SigmaZeroDivisorEntry(
                new SigmaSignedDyad(data[offset], data[offset + 1],
                    data[offset + 2], data[offset + 3]),
                new SigmaSignedDyad(data[offset + 4], data[offset + 5],
                    data[offset + 6], data[offset + 7]), data[offset + 8]);
        }

        public static SigmaS16 Add(SigmaS16 left, SigmaS16 right) =>
            Combine(left, right, add: true);
        public static SigmaS16 Subtract(SigmaS16 left, SigmaS16 right) =>
            Combine(left, right, add: false);

        private static SigmaS16 BasisAction(int basis, SigmaS16 value,
            byte[] sources, sbyte[] signs)
        {
            ValidateBasis(basis);
            var output = new long[SigmaS16.LaneCount];
            int rowOffset = basis << 4;
            for (int lane = 0; lane < SigmaS16.LaneCount; ++lane)
            {
                long term = value[sources[rowOffset + lane]];
                output[lane] = signs[rowOffset + lane] < 0
                    ? SigmaNumericDomain.QNegate(term)
                    : term;
            }
            return SigmaS16.FromArray(output);
        }

        private static SigmaS16 CombineDyad(SigmaS16 first, int firstSign,
            SigmaS16 second, int secondSign)
        {
            var output = new long[SigmaS16.LaneCount];
            for (int lane = 0; lane < SigmaS16.LaneCount; ++lane)
            {
                long a = firstSign < 0
                    ? SigmaNumericDomain.QNegate(first[lane]) : first[lane];
                long b = secondSign < 0
                    ? SigmaNumericDomain.QNegate(second[lane]) : second[lane];
                output[lane] = SigmaNumericDomain.QAdd(a, b);
            }
            return SigmaS16.FromArray(output);
        }

        private static SigmaS16 Combine(SigmaS16 left, SigmaS16 right, bool add)
        {
            var output = new long[SigmaS16.LaneCount];
            for (int lane = 0; lane < SigmaS16.LaneCount; ++lane)
                output[lane] = add
                    ? SigmaNumericDomain.QAdd(left[lane], right[lane])
                    : SigmaNumericDomain.QSub(left[lane], right[lane]);
            return SigmaS16.FromArray(output);
        }

        private static void ValidateBasis(int basis)
        {
            if ((uint)basis >= SigmaS16.LaneCount)
                throw new ArgumentOutOfRangeException(nameof(basis));
        }
    }
}
