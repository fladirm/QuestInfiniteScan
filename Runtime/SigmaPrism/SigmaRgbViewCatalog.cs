using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Genesis.RoomScan.SigmaPrism
{
    /// <summary>
    /// Exact, deterministic view-operator metadata for S4-06.  A direction is
    /// quantized by the canonical packed-Q48 selector to {-1,0,+1}^3.  The
    /// unnormalised lift is intentional: T scales by |nu|^2 and the projective
    /// colour ratio is invariant to that positive scale.  It also keeps every
    /// generated coefficient dyadic and avoids a generic dense S16 product in the
    /// per-source inverse hot path.
    /// </summary>
    public sealed class SigmaRgbViewCatalog
    {
        public const int AxisCardinality = 3;
        public const int DirectionCount = 27;
        public const int RowCount = 4;
        public const int MatrixValueCount =
            DirectionCount * RowCount * SigmaS16.LaneCount;
        public const int NullDirectionIndex = 13;

        private readonly long[] _operatorRaw;
        private readonly byte[] _supportScale;

        private SigmaRgbViewCatalog(long[] operatorRaw, byte[] supportScale,
            string fingerprint)
        {
            _operatorRaw = operatorRaw;
            _supportScale = supportScale;
            Fingerprint = fingerprint;
        }

        public IReadOnlyList<long> OperatorRaw => _operatorRaw;
        public IReadOnlyList<byte> SupportScale => _supportScale;
        public string Fingerprint { get; }

        public long this[int direction, int row, int lane] =>
            _operatorRaw[checked((direction * RowCount + row) *
                SigmaS16.LaneCount + lane)];

        public static SigmaRgbViewCatalog CreateCanonical()
        {
            var operators = new long[MatrixValueCount];
            var supportScale = new byte[DirectionCount];
            for (int z = -1; z <= 1; ++z)
            for (int y = -1; y <= 1; ++y)
            for (int x = -1; x <= 1; ++x)
            {
                int index = EncodeDirection(x, y, z);
                int scale = x * x + y * y + z * z;
                supportScale[index] = checked((byte)scale);
                if (scale == 0)
                    continue;
                var nu = new SigmaS16(0,
                    x * SigmaNumericDomain.One,
                    y * SigmaNumericDomain.One,
                    z * SigmaNumericDomain.One,
                    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
                long[,] matrix = SigmaRgbInverse.BuildGeneratedViewMatrix(nu);
                for (int row = 0; row < RowCount; ++row)
                for (int lane = 0; lane < SigmaS16.LaneCount; ++lane)
                    operators[(index * RowCount + row) *
                        SigmaS16.LaneCount + lane] = matrix[row, lane];
            }
            return new SigmaRgbViewCatalog(operators, supportScale,
                FingerprintOf(operators, supportScale));
        }

        /// <summary>
        /// Quantizes one non-zero exact direction without normalization.  The
        /// largest component is always retained; other components are retained
        /// when they are at least half the dominant magnitude.  Ties are exact
        /// and the mapping has no floating-point decision boundary.
        /// </summary>
        public static int QuantizeDirection(long x, long y, long z)
        {
            long ax = SigmaNumericDomain.QAbs(x);
            long ay = SigmaNumericDomain.QAbs(y);
            long az = SigmaNumericDomain.QAbs(z);
            long maximum = Math.Max(ax, Math.Max(ay, az));
            if (maximum == 0)
                return NullDirectionIndex;
            int qx = RetainedSign(x, ax, maximum);
            int qy = RetainedSign(y, ay, maximum);
            int qz = RetainedSign(z, az, maximum);
            return EncodeDirection(qx, qy, qz);
        }

        public static int EncodeDirection(int x, int y, int z)
        {
            if (x is < -1 or > 1 || y is < -1 or > 1 || z is < -1 or > 1)
                throw new ArgumentOutOfRangeException(nameof(x));
            return (z + 1) * 9 + (y + 1) * 3 + x + 1;
        }

        public static void DecodeDirection(int index, out int x, out int y,
            out int z)
        {
            if ((uint)index >= DirectionCount)
                throw new ArgumentOutOfRangeException(nameof(index));
            x = index % 3 - 1;
            index /= 3;
            y = index % 3 - 1;
            z = index / 3 - 1;
        }

        private static int RetainedSign(long value, long magnitude, long maximum)
        {
            // magnitude >= ceil(maximum/2), written without an overflowing *2.
            long threshold = (maximum >> 1) + (maximum & 1L);
            if (magnitude < threshold)
                return 0;
            return value < 0 ? -1 : 1;
        }

        private static string FingerprintOf(IReadOnlyList<long> operators,
            IReadOnlyList<byte> scales)
        {
            using SHA256 sha = SHA256.Create();
            var bytes = new byte[checked(operators.Count * sizeof(long) +
                scales.Count)];
            int cursor = 0;
            for (int index = 0; index < operators.Count; ++index)
            {
                byte[] raw = BitConverter.GetBytes(operators[index]);
                Buffer.BlockCopy(raw, 0, bytes, cursor, raw.Length);
                cursor += raw.Length;
            }
            for (int index = 0; index < scales.Count; ++index)
                bytes[cursor++] = scales[index];
            byte[] digest = sha.ComputeHash(bytes);
            var hexadecimal = new StringBuilder(digest.Length * 2);
            for (int index = 0; index < digest.Length; ++index)
                hexadecimal.Append(digest[index].ToString("x2"));
            return hexadecimal.ToString();
        }
    }
}
