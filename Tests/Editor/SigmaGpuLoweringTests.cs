using System;
using System.Runtime.InteropServices;
using Genesis.RoomScan.SigmaPrism;
using NUnit.Framework;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class SigmaGpuLoweringTests
    {
        private const int FixtureCount = 64;
        private const int NumericOperationCount = 12;

        [StructLayout(LayoutKind.Sequential)]
        private struct UInt2
        {
            public uint X;
            public uint Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct UInt4
        {
            public uint X;
            public uint Y;
            public uint Z;
            public uint W;
        }

        [Test]
        public void CanonicalPacked32BackendGatePassesOnlyAfterGpuSelfTest()
        {
            using SigmaExactBackendGate gate = SigmaExactBackendGate.Dispatch();
            var result = new uint[1];
            gate.Buffer.GetData(result);
            Assert.That(result[0], Is.EqualTo(1u));
        }

        [Test]
        public void Packed32NumericLoweringMatchesCpuSemanticDomainBitForBit()
        {
            ComputeShader shader = LoadFixture();
            int kernel = shader.FindKernel("NumericParity");
            var pairs = new UInt4[FixtureCount];
            var shifts = new uint[FixtureCount];
            var sourceA = new long[FixtureCount];
            var sourceB = new long[FixtureCount];
            for (int index = 0; index < FixtureCount; ++index)
            {
                long a = SigmaNumericDomain.FromRatio((index % 31) - 15, 4);
                long b = SigmaNumericDomain.FromRatio((index % 13) + 2,
                    (index & 1) == 0 ? 4 : -4);
                uint shift = (uint)(index & 3);
                if (index == FixtureCount - 1)
                {
                    a = long.MaxValue;
                    b = SigmaNumericDomain.One;
                    shift = 1;
                }
                sourceA[index] = a;
                sourceB[index] = b;
                UInt2 packedA = Pack(a);
                UInt2 packedB = Pack(b);
                pairs[index] = new UInt4
                    { X = packedA.X, Y = packedA.Y, Z = packedB.X, W = packedB.Y };
                shifts[index] = shift;
            }

            var results = new UInt4[FixtureCount * NumericOperationCount];
            using var pairBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                pairs.Length, Marshal.SizeOf<UInt4>());
            using var shiftBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                shifts.Length, sizeof(uint));
            using var resultBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                results.Length, Marshal.SizeOf<UInt4>());
            pairBuffer.SetData(pairs);
            shiftBuffer.SetData(shifts);
            shader.SetBuffer(kernel, "_NumericPairs", pairBuffer);
            shader.SetBuffer(kernel, "_ShiftCounts", shiftBuffer);
            shader.SetBuffer(kernel, "_NumericResults", resultBuffer);
            shader.Dispatch(kernel, 1, 1, 1);
            resultBuffer.GetData(results);

            for (int fixture = 0; fixture < FixtureCount; ++fixture)
            {
                for (int operation = 0; operation < NumericOperationCount; ++operation)
                {
                    (bool valid, long value) expected = EvaluateNumeric(operation,
                        sourceA[fixture], sourceB[fixture], (int)shifts[fixture]);
                    UInt4 actual = results[fixture * NumericOperationCount + operation];
                    Assert.That(actual.Z, Is.EqualTo(expected.valid ? 1u : 0u),
                        $"valid fixture={fixture} op={operation}");
                    Assert.That(actual.W, Is.EqualTo((uint)operation));
                    if (expected.valid)
                    {
                        Assert.That(Unpack(new UInt2 { X = actual.X, Y = actual.Y }),
                            Is.EqualTo(expected.value),
                            $"value fixture={fixture} op={operation}");
                    }
                }
            }
        }

        [Test]
        public void Packed32SparseAlgebraAndGeneratedBasisTableMatchCpuExactly()
        {
            ComputeShader shader = LoadFixture();
            SigmaS16 state = MakeState();
            var input = new UInt2[16];
            for (int lane = 0; lane < input.Length; ++lane)
                input[lane] = Pack(state[lane]);
            var algebraResults = new UInt4[36];
            int algebraKernel = shader.FindKernel("AlgebraParity");
            using (var inputBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                       input.Length, Marshal.SizeOf<UInt2>()))
            using (var outputBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                       algebraResults.Length, Marshal.SizeOf<UInt4>()))
            {
                inputBuffer.SetData(input);
                shader.SetBuffer(algebraKernel, "_AlgebraInput", inputBuffer);
                shader.SetBuffer(algebraKernel, "_AlgebraResults", outputBuffer);
                shader.Dispatch(algebraKernel, 1, 1, 1);
                outputBuffer.GetData(algebraResults);
            }

            SigmaS16 conjugated = SigmaS16Operators.Conjugate(state);
            long[] geometry = SigmaS16Operators.GeometryReadout(state);
            SigmaS16 dyad = SigmaS16Operators.RightSignedDyadAction(state,
                SigmaS16Operators.GetAnnihilatorAction(0));
            for (int lane = 0; lane < 16; ++lane)
            {
                AssertPacked(algebraResults[lane], conjugated[lane], $"conj {lane}");
                AssertPacked(algebraResults[20 + lane], dyad[lane], $"dyad {lane}");
            }
            for (int lane = 0; lane < 4; ++lane)
                AssertPacked(algebraResults[16 + lane], geometry[lane], $"G {lane}");

            int basisKernel = shader.FindKernel("BasisTableParity");
            var basisResults = new UInt2[256];
            using var basisBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                basisResults.Length, Marshal.SizeOf<UInt2>());
            shader.SetBuffer(basisKernel, "_BasisResults", basisBuffer);
            shader.Dispatch(basisKernel, 1, 1, 1);
            basisBuffer.GetData(basisResults);
            for (int left = 0; left < 16; ++left)
            {
                for (int right = 0; right < 16; ++right)
                {
                    UInt2 actual = basisResults[left * 16 + right];
                    Assert.That(actual.X,
                        Is.EqualTo((uint)SigmaS16Operators.BasisProductIndex(left, right)));
                    Assert.That(unchecked((int)actual.Y),
                        Is.EqualTo(SigmaS16Operators.BasisProductSign(left, right)));
                }
            }
        }

        private static ComputeShader LoadFixture()
        {
            ComputeShader shader = Resources.Load<ComputeShader>(
                "SigmaPrism/SigmaOperatorFixture");
            Assert.That(shader, Is.Not.Null,
                "Exact Sigma GPU fixture must be imported as a Vulkan compute shader.");
            return shader;
        }

        private static (bool valid, long value) EvaluateNumeric(int operation,
            long a, long b, int shift)
        {
            try
            {
                return (true, operation switch
                {
                    0 => SigmaNumericDomain.QAdd(a, b),
                    1 => SigmaNumericDomain.QSub(a, b),
                    2 => SigmaNumericDomain.QNegate(a),
                    3 => SigmaNumericDomain.QShiftLeft(a, shift),
                    4 => SigmaNumericDomain.QShiftRight(a, shift),
                    5 => SigmaNumericDomain.QMul(a, b),
                    6 => SigmaNumericDomain.QDiv(a, b),
                    7 => SigmaNumericDomain.QMulLower(a, b),
                    8 => SigmaNumericDomain.QMulUpper(a, b),
                    9 => SigmaNumericDomain.QDivLower(a, b),
                    10 => SigmaNumericDomain.QDivUpper(a, b),
                    11 => Math.Min(a, b),
                    _ => throw new ArgumentOutOfRangeException(nameof(operation)),
                });
            }
            catch (OverflowException)
            {
                return (false, 0L);
            }
        }

        private static SigmaS16 MakeState()
        {
            var lanes = new long[16];
            for (int lane = 0; lane < lanes.Length; ++lane)
                lanes[lane] = ((lane * 13) % 23 - 11) *
                    (SigmaNumericDomain.One >> 10);
            return SigmaS16.FromArray(lanes);
        }

        private static UInt2 Pack(long value) => new()
        {
            X = unchecked((uint)value),
            Y = unchecked((uint)(value >> 32)),
        };

        private static long Unpack(UInt2 value) =>
            unchecked((long)(((ulong)value.Y << 32) | value.X));

        private static void AssertPacked(UInt4 actual, long expected, string label)
        {
            Assert.That(actual.Z, Is.EqualTo(1u), $"valid {label}");
            Assert.That(Unpack(new UInt2 { X = actual.X, Y = actual.Y }),
                Is.EqualTo(expected), label);
        }
    }
}
