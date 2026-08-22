using System;
using System.Numerics;

namespace Genesis.RoomScan.SigmaPrism
{
    /// <summary>
    /// Semantic truth for <c>num.fixed.q16_48.checked.nearest_even</c>. Storage and
    /// GPU execution layouts are lowerings of these rules; none may change them.
    /// </summary>
    public static class SigmaNumericDomain
    {
        public const string Id = "num.fixed.q16_48.checked.nearest_even";
        public const bool Signed = true;
        public const int IntegerBits = 16;
        public const int FractionBits = 48;
        public const int StorageBits = 64;
        public const string RoundingMode = "NearestEven";
        public const string OverflowMode = "Checked";
        public const string ScaleKind = "BinaryPower";
        public const long One = 1L << FractionBits;
        public const long Half = One >> 1;
        public const long MinRaw = long.MinValue;
        public const long MaxRaw = long.MaxValue;

        private static readonly BigInteger RawScale = BigInteger.One << FractionBits;
        private static readonly BigInteger LongMin = new(long.MinValue);
        private static readonly BigInteger LongMax = new(long.MaxValue);

        public static long QAdd(long a, long b) => checked(a + b);
        public static long QSub(long a, long b) => checked(a - b);
        public static int QCompare(long a, long b) => a.CompareTo(b);

        public static long QNegate(long value)
        {
            if (value == long.MinValue)
                throw new OverflowException("Q16.48 negation overflow.");
            return -value;
        }

        public static long QAbs(long value)
        {
            if (value == long.MinValue)
                throw new OverflowException("Q16.48 absolute-value overflow.");
            return Math.Abs(value);
        }

        public static long QClamp(long value, long lower, long upper)
        {
            if (lower > upper)
                throw new ArgumentOutOfRangeException(nameof(lower),
                    "Q16.48 clamp bounds are inverted.");
            return value < lower ? lower : value > upper ? upper : value;
        }

        public static long QMul(long a, long b) => ToRawChecked(
            DivideNearestEven(new BigInteger(a) * b, RawScale));

        public static long QDiv(long a, long b)
        {
            if (b == 0L)
                throw new DivideByZeroException();
            return ToRawChecked(DivideNearestEven(new BigInteger(a) << FractionBits,
                new BigInteger(b)));
        }

        /// <summary>Outward lower bound of one exact Q16.48 product.</summary>
        public static long QMulLower(long a, long b) => ToRawChecked(
            DivideFloor(new BigInteger(a) * b, RawScale));

        /// <summary>Outward upper bound of one exact Q16.48 product.</summary>
        public static long QMulUpper(long a, long b) => ToRawChecked(
            DivideCeiling(new BigInteger(a) * b, RawScale));

        /// <summary>Outward lower bound of one exact Q16.48 quotient.</summary>
        public static long QDivLower(long a, long b)
        {
            if (b == 0L)
                throw new DivideByZeroException();
            return ToRawChecked(DivideFloor(new BigInteger(a) << FractionBits,
                new BigInteger(b)));
        }

        /// <summary>Outward upper bound of one exact Q16.48 quotient.</summary>
        public static long QDivUpper(long a, long b)
        {
            if (b == 0L)
                throw new DivideByZeroException();
            return ToRawChecked(DivideCeiling(new BigInteger(a) << FractionBits,
                new BigInteger(b)));
        }

        public static long QShiftLeft(long value, int count)
        {
            ValidateShift(count);
            return ToRawChecked(new BigInteger(value) << count);
        }

        /// <summary>Dyadic point scaling with nearest-even rounding.</summary>
        public static long QShiftRight(long value, int count)
        {
            ValidateShift(count);
            if (count == 0)
                return value;
            return ToRawChecked(DivideNearestEven(new BigInteger(value),
                BigInteger.One << count));
        }

        public static long QShiftRightLower(long value, int count)
        {
            ValidateShift(count);
            return ToRawChecked(DivideFloor(new BigInteger(value),
                BigInteger.One << count));
        }

        public static long QShiftRightUpper(long value, int count)
        {
            ValidateShift(count);
            return ToRawChecked(DivideCeiling(new BigInteger(value),
                BigInteger.One << count));
        }

        /// <summary>Floor square root of an unsigned integer, independent of FP.</summary>
        public static ulong QIntegerSquareRoot(ulong value)
        {
            ulong result = 0UL;
            ulong bit = 1UL << 62;
            while (bit > value)
                bit >>= 2;
            while (bit != 0UL)
            {
                ulong candidate = result + bit;
                if (value >= candidate)
                {
                    value -= candidate;
                    result = (result >> 1) + bit;
                }
                else
                {
                    result >>= 1;
                }
                bit >>= 2;
            }
            return result;
        }

        /// <summary>
        /// Deterministically quantizes an IEEE-754 double exactly once. The binary
        /// value is decoded as an integer ratio before nearest-even Q16.48 rounding.
        /// </summary>
        public static long Quantize(double value)
        {
            long bits = BitConverter.DoubleToInt64Bits(value);
            bool negative = bits < 0;
            int exponentBits = (int)((bits >> 52) & 0x7ffL);
            ulong fraction = (ulong)bits & 0x000f_ffff_ffff_ffffUL;
            if (exponentBits == 0x7ff)
                throw new ArgumentOutOfRangeException(nameof(value),
                    "NaN and infinity are not canonical Q16.48 inputs.");
            if (exponentBits == 0 && fraction == 0UL)
                return 0L;

            BigInteger mantissa;
            int binaryExponent;
            if (exponentBits == 0)
            {
                mantissa = fraction;
                binaryExponent = -1074;
            }
            else
            {
                mantissa = (BigInteger.One << 52) + fraction;
                binaryExponent = exponentBits - 1023 - 52;
            }
            if (negative)
                mantissa = -mantissa;

            int rawExponent = binaryExponent + FractionBits;
            BigInteger raw = rawExponent >= 0
                ? mantissa << rawExponent
                : DivideNearestEven(mantissa, BigInteger.One << -rawExponent);
            return ToRawChecked(raw);
        }

        public static long Quantize(float value) => Quantize((double)value);

        public static long FromInteger(long value) =>
            ToRawChecked(new BigInteger(value) << FractionBits);

        public static long FromRatio(long numerator, long denominator)
        {
            if (denominator == 0L)
                throw new DivideByZeroException();
            return ToRawChecked(DivideNearestEven(
                new BigInteger(numerator) << FractionBits, denominator));
        }

        /// <summary>Non-authoritative visualization/debug conversion.</summary>
        public static double ToDouble(long raw) => raw / (double)One;

        internal static BigInteger DivideNearestEven(BigInteger numerator,
            BigInteger denominator)
        {
            NormalizeDenominator(ref numerator, ref denominator);
            BigInteger quotient = BigInteger.DivRem(numerator, denominator,
                out BigInteger remainder);
            BigInteger twiceRemainder = BigInteger.Abs(remainder) << 1;
            int comparison = twiceRemainder.CompareTo(denominator);
            if (comparison > 0 || comparison == 0 && !quotient.IsEven)
                quotient += numerator.Sign < 0 ? -BigInteger.One : BigInteger.One;
            return quotient;
        }

        internal static BigInteger DivideFloor(BigInteger numerator,
            BigInteger denominator)
        {
            NormalizeDenominator(ref numerator, ref denominator);
            BigInteger quotient = BigInteger.DivRem(numerator, denominator,
                out BigInteger remainder);
            if (remainder.Sign < 0)
                quotient -= BigInteger.One;
            return quotient;
        }

        internal static BigInteger DivideCeiling(BigInteger numerator,
            BigInteger denominator)
        {
            NormalizeDenominator(ref numerator, ref denominator);
            BigInteger quotient = BigInteger.DivRem(numerator, denominator,
                out BigInteger remainder);
            if (remainder.Sign > 0)
                quotient += BigInteger.One;
            return quotient;
        }

        private static void NormalizeDenominator(ref BigInteger numerator,
            ref BigInteger denominator)
        {
            if (denominator.IsZero)
                throw new DivideByZeroException();
            if (denominator.Sign < 0)
            {
                numerator = -numerator;
                denominator = -denominator;
            }
        }

        private static long ToRawChecked(BigInteger value)
        {
            if (value < LongMin || value > LongMax)
                throw new OverflowException("Q16.48 result is outside signed 64-bit storage.");
            return (long)value;
        }

        private static void ValidateShift(int count)
        {
            if ((uint)count >= StorageBits)
                throw new ArgumentOutOfRangeException(nameof(count));
        }
    }

    /// <summary>Inclusive canonical Q16.48 interval with explicit empty state.</summary>
    public readonly struct SigmaQ48Interval : IEquatable<SigmaQ48Interval>
    {
        public SigmaQ48Interval(long lower, long upper)
        {
            Lower = lower;
            Upper = upper;
        }

        public long Lower { get; }
        public long Upper { get; }
        public bool IsEmpty => Lower > Upper;
        public ulong WidthRaw => IsEmpty ? 0UL : unchecked((ulong)Upper - (ulong)Lower);

        public static SigmaQ48Interval Full =>
            new(SigmaNumericDomain.MinRaw, SigmaNumericDomain.MaxRaw);
        public static SigmaQ48Interval Empty => new(1L, 0L);

        public bool Contains(long value) => !IsEmpty && value >= Lower && value <= Upper;

        public SigmaQ48Interval Intersect(SigmaQ48Interval other) => new(
            Math.Max(Lower, other.Lower), Math.Min(Upper, other.Upper));

        public long Clamp(long value)
        {
            if (IsEmpty)
                throw new InvalidOperationException("Cannot clamp into an empty interval.");
            return SigmaNumericDomain.QClamp(value, Lower, Upper);
        }

        public bool Equals(SigmaQ48Interval other) =>
            Lower == other.Lower && Upper == other.Upper;
        public override bool Equals(object obj) =>
            obj is SigmaQ48Interval other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Lower, Upper);
        public static bool operator ==(SigmaQ48Interval left, SigmaQ48Interval right) =>
            left.Equals(right);
        public static bool operator !=(SigmaQ48Interval left, SigmaQ48Interval right) =>
            !left.Equals(right);
        public override string ToString() => IsEmpty
            ? "empty"
            : $"[{Lower},{Upper}]";
    }
}
