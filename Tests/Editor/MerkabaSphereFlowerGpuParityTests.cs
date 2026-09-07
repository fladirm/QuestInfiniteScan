using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Genesis.RoomScan;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace Genesis.RoomScan.Tests
{
    public sealed class MerkabaSphereFlowerGpuParityTests
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct OracleCase
        {
            internal int4 Control;
            internal float4 A;
            internal float4 B;
            internal float4 C;

            internal OracleCase(int operation, int index = 0)
            {
                Control = new int4(operation, index, 0, 0);
                A = default;
                B = default;
                C = default;
            }
        }

        private static readonly int[] SignedBoundaries =
        {
            -257, -256, -255, -33, -32, -31, -9, -8, -1,
            0, 1, 7, 8, 31, 32, 33, 255, 256, 257
        };

        [Test, Timeout(60000)]
        public void GeneratedHlsl_IsBitIdenticalAndMatchesCpuOracle()
        {
            var cases = new List<OracleCase>(4096);

            for (int i = 0; i < MerkabaSphereFlowerAuthority.Lines.Length; i++)
                cases.Add(new OracleCase(0, i));
            for (int i = 0; i < MerkabaSphereFlowerAuthority.Directions.Length; i++)
                cases.Add(new OracleCase(1, i));
            for (int i = 0; i < MerkabaSphereFlowerAuthority.Nodes.Length; i++)
                cases.Add(new OracleCase(2, i));
            for (int i = 0; i < MerkabaSphereFlowerAuthority.Strands.Length; i++)
                cases.Add(new OracleCase(3, i));
            for (int i = 0; i < MerkabaSphereFlowerAuthority.Petals.Length; i++)
                cases.Add(new OracleCase(4, i));
            for (int i = 0; i < MerkabaSphereFlowerAuthority.ChildPetals.Length; i++)
                cases.Add(new OracleCase(5, i));
            for (int i = 0; i < MerkabaSphereFlowerAuthority.SectorBoundaries.Length; i++)
                cases.Add(new OracleCase(6, i));
            for (int i = 0; i < MerkabaSphereFlowerAuthority.TetraFrames.Length; i++)
                cases.Add(new OracleCase(7, i));

            foreach (int value in SignedBoundaries)
            foreach (var direction in MerkabaSphereFlowerAuthority.Directions)
            {
                var item = new OracleCase(8)
                {
                    Control = new int4(8, value, -value, value - 1),
                    A = new float4(direction.Direction, direction.LineClass),
                    B = new float4((value & 7) % 6, 0f, 0f, 0f)
                };
                cases.Add(item);
            }

            float3[] coefficients =
            {
                new(1f, 0f, 0f), new(0f), new(0f, 1f, 0f),
                new(1f, 1f, 0f), new(1.1f, 1f, 0f),
                new(5f, 3f, 4f),
                new(0.25f, 0.75f, -0.5f), new(-0.4f, 0.2f, 0.9f)
            };
            foreach (float3 abc in coefficients)
            foreach (int plus in new[] { 0, 1 })
            {
                var item = new OracleCase(9)
                {
                    Control = new int4(9, plus, 0, 0),
                    A = new float4(abc, 0f)
                };
                cases.Add(item);
            }

            float2 baseRoot = math.normalize(new float2(0.8f, -0.6f));
            foreach (float turn in new[] { -0.3f, -0.125f, 0f, 0.0625f, 0.25f })
            {
                float2 target = MerkabaSphereFlowerAuthority
                    .RotateTangentHalfAngle(baseRoot, turn);
                var item = new OracleCase(10)
                {
                    A = new float4(baseRoot, target)
                };
                cases.Add(item);
            }

            float4[] tetraCases =
            {
                new(0f), new(1f, -2f, 3f, -4f),
                new(0.125f, 0.25f, -0.5f, 1f)
            };
            foreach (float4 q in tetraCases)
            {
                var item = new OracleCase(11) { A = q };
                cases.Add(item);
            }

            for (int i = 0;
                 i < MerkabaSphereFlowerAuthority.SkinChambers.Length; i++)
                cases.Add(new OracleCase(12, i));
            for (int i = 0;
                 i < MerkabaSphereFlowerAuthority.SkinThreadPositionCount; i++)
                cases.Add(new OracleCase(13, i));
            for (int i = 0; i < MerkabaSphereFlowerAuthority.SkinL5Count; i++)
                cases.Add(new OracleCase(16, i));
            for (int i = 0; i < MerkabaSphereFlowerAuthority.SkinL3Count; i++)
                cases.Add(new OracleCase(17, i));
            for (int i = 0; i < MerkabaSphereFlowerAuthority.SkinL4Count; i++)
                cases.Add(new OracleCase(18, i));
            for (int i = 0;
                 i < MerkabaSphereFlowerAuthority.SkinChambers.Length; i++)
            {
                var rule = MerkabaSphereFlowerAuthority.SkinChambers[i];
                float3 barycentric = default;
                barycentric[rule.High] = 0.6f;
                barycentric[rule.Middle] = 0.3f;
                barycentric[rule.Low] = 0.1f;
                cases.Add(new OracleCase(19, i / 6) { A = barycentric.xyzz });
            }
            for (int bit = 0; bit <= 49; bit++)
            {
                var item = new OracleCase(20)
                {
                    Control = new int4(20, bit, math.min(bit, 7), 0),
                    A = new float4(math.asfloat(0x6du),
                        math.asfloat(0xa5a5a5a5u),
                        math.asfloat(0x00015555u), 0f)
                };
                cases.Add(item);
            }
            float3[] skinCoordinates =
            {
                new(1f / 3f), new(0.2f, 0.3f, 0.5f),
                new(0.1f, 0.4f, 0.5f), new(0f, 0.25f, 0.75f)
            };
            for (int i3 = 0; i3 < skinCoordinates.Length; i3++)
            for (int i4 = 0; i4 < skinCoordinates.Length; i4++)
            {
                float3 c3 = skinCoordinates[i3];
                float3 c4 = skinCoordinates[i4];
                float3 c5 = skinCoordinates[(i3 + i4) % skinCoordinates.Length];
                float3 amplitude = new(0.003f, -0.001f, 0.00025f);
                cases.Add(new OracleCase(21)
                {
                    Control = new int4(21, math.asint(amplitude)),
                    A = c3.xyzz,
                    B = c4.xyzz,
                    C = c5.xyzz
                });
            }
            var splitCases = new[]
            {
                new int4(0, 0, 0, 0),
                new int4(1, 1 << 2, (1 << 14) | (1 << 20), 0),
                new int4(1, 1 << 2, 1 << 7, 0),
                new int4(1, 0x7f, -1, 0x1ffff)
            };
            foreach (int4 split in splitCases)
                cases.Add(new OracleCase(22)
                {
                    Control = new int4(22, split.x, split.y, 0),
                    A = new float4(math.asfloat(split.z),
                        math.asfloat(split.w), 0f, 0f)
                });
            for (int classificationCase = 0; classificationCase < 4;
                 classificationCase++)
                cases.Add(new OracleCase(23, classificationCase));

            var intervalCases = new[]
            {
                new OracleCase(14)
                {
                    A = new float4(1f, 1f, 0f, 0f),
                    B = new float4(0f, 0f, 0f, 0f)
                },
                new OracleCase(14)
                {
                    A = new float4(0f, 0f, 0f, 0f),
                    B = new float4(0f, 0f, 0f, 0f)
                },
                new OracleCase(14)
                {
                    A = new float4(0f, 0f, 1f, 1f),
                    B = new float4(0f, 0f, 0f, 0f)
                },
                new OracleCase(14)
                {
                    A = new float4(1f, 1f, 1f, 1f),
                    B = new float4(0f, 0f, 0f, 0f)
                },
                new OracleCase(14)
                {
                    A = new float4(0.99f, 1.01f, 1f, 1f),
                    B = new float4(0f, 0f, 0f, 0f)
                },
                new OracleCase(14)
                {
                    A = new float4(1.1f, 1.2f, 0.9f, 1f),
                    B = new float4(-0.1f, 0.1f, 0f, 0f)
                }
            };
            cases.AddRange(intervalCases);
            cases.Add(new OracleCase(15));

            OracleCase[] results = Dispatch(cases);
            int cursor = 0;

            for (int i = 0; i < MerkabaSphereFlowerAuthority.Lines.Length; i++)
            {
                var expected = MerkabaSphereFlowerAuthority.Lines[i];
                OracleCase actual = results[cursor++];
                Assert.That(actual.Control,
                    Is.EqualTo(new int4(expected.Direction, (int)expected.Shell)));
                AssertBits(actual.A.xyz, expected.UnitDirection, $"line {i} unit");
                AssertBits(actual.B.xyz, expected.E1, $"line {i} e1");
                AssertBits(actual.C.xyz, expected.E2, $"line {i} e2");
                Assert.That(actual.A.w, Is.EqualTo((float)expected.SectorOffset));
                Assert.That(actual.B.w, Is.EqualTo((float)expected.SectorCount));
            }
            for (int i = 0; i < MerkabaSphereFlowerAuthority.Directions.Length; i++)
            {
                var expected = MerkabaSphereFlowerAuthority.Directions[i];
                OracleCase actual = results[cursor++];
                Assert.That(actual.Control, Is.EqualTo(new int4(expected.Direction,
                    expected.LineClass)));
                Assert.That(actual.A.x, Is.EqualTo((float)expected.Orientation));
                Assert.That(actual.A.y, Is.EqualTo((float)expected.Shell));
            }
            for (int i = 0; i < MerkabaSphereFlowerAuthority.Nodes.Length; i++)
            {
                var expected = MerkabaSphereFlowerAuthority.Nodes[i];
                Assert.That(results[cursor++].Control,
                    Is.EqualTo(new int4(expected.Direction, expected.LineClass)));
            }
            for (int i = 0; i < MerkabaSphereFlowerAuthority.Strands.Length; i++)
            {
                var expected = MerkabaSphereFlowerAuthority.Strands[i];
                uint petals = (uint)(expected.Petal0 | (expected.Petal1 << 8));
                Assert.That(results[cursor++].Control,
                    Is.EqualTo(new int4(expected.Node0, expected.Node1,
                        expected.IncidenceKind, unchecked((int)petals))));
            }
            for (int i = 0; i < MerkabaSphereFlowerAuthority.Petals.Length; i++)
            {
                var expected = MerkabaSphereFlowerAuthority.Petals[i];
                OracleCase actual = results[cursor++];
                Assert.That(actual.Control, Is.EqualTo(new int4(expected.FaceNode,
                    expected.EdgeNode, expected.CornerNode,
                    expected.Orientation > 0 ? 1 : 0)));
                AssertBits(actual.A.xyz,
                    new float3(expected.Strand0, expected.Strand1,
                        expected.Strand2), $"petal {i} strands");
                AssertBits(actual.B.xyz,
                    new float3(expected.StrandSign0, expected.StrandSign1,
                        expected.StrandSign2), $"petal {i} signs");
            }
            for (int i = 0; i < MerkabaSphereFlowerAuthority.ChildPetals.Length; i++)
            {
                var expected = MerkabaSphereFlowerAuthority.ChildPetals[i];
                Assert.That(results[cursor++].Control.xyz,
                    Is.EqualTo(new int3(expected.Vertex0, expected.Vertex1,
                        expected.Vertex2)));
            }
            for (int i = 0; i < MerkabaSphereFlowerAuthority.SectorBoundaries.Length;
                 i++)
            {
                var expected = MerkabaSphereFlowerAuthority.SectorBoundaries[i];
                OracleCase actual = results[cursor++];
                AssertBits(actual.A.xy, expected.Unit, $"sector {i} unit");
                AssertBits(actual.A.zw,
                    new float2(expected.Enclosure.X.Lower,
                        expected.Enclosure.X.Upper), $"sector {i} x bounds");
                AssertBits(actual.B.xy,
                    new float2(expected.Enclosure.Y.Lower,
                        expected.Enclosure.Y.Upper), $"sector {i} y bounds");
            }
            for (int i = 0; i < MerkabaSphereFlowerAuthority.TetraFrames.Length; i++)
            {
                var expected = MerkabaSphereFlowerAuthority.TetraFrames[i];
                OracleCase actual = results[cursor++];
                Assert.That(actual.Control, Is.EqualTo(expected.LineClasses));
                AssertBits(actual.A, (float4)expected.Eta, $"tetra {i} eta");
                Assert.That(actual.B.x, Is.EqualTo((float)expected.Chirality));
            }

            foreach (int value in SignedBoundaries)
            foreach (var direction in MerkabaSphereFlowerAuthority.Directions)
            {
                OracleCase actual = results[cursor++];
                int3 kernel = new(value, -value, value - 1);
                var junction = MerkabaSphereFlowerAuthority.JunctionAddress(kernel,
                    direction.Direction);
                Assert.That(actual.Control, Is.EqualTo(new int4(
                    checked((int)junction.X), checked((int)junction.Y),
                    checked((int)junction.Z),
                    MerkabaSphereFlowerAuthority.JunctionParity(junction))));
                AssertUlp(actual.A.x,
                    MerkabaSphereFlowerAuthority.LevelStep((value & 7) % 6),
                    1, "level step");
                Assert.That(MerkabaSphereFlowerAuthority.TryResolveEndpointPair(
                    junction, direction.LineClass, out var first, out var second),
                    Is.True);
                AssertBits(actual.B.xyz,
                    new float3(first.X, first.Y, first.Z), "first endpoint");
                Assert.That(actual.B.w, Is.EqualTo(1f));
                AssertBits(actual.C.xyz,
                    new float3(second.X, second.Y, second.Z), "second endpoint");
            }

            foreach (float3 abc in coefficients)
            foreach (int plus in new[] { 0, 1 })
            {
                OracleCase actual = results[cursor++];
                bool valid = MerkabaSphereFlowerAuthority.TryEvaluateRoot(abc,
                    plus != 0, out float2 root);
                Assert.That(actual.Control.x, Is.EqualTo(valid ? 1 : 0));
                Assert.That(actual.Control.y, Is.EqualTo((int)
                    MerkabaSphereFlowerAuthority.ClassifyRoots(
                        MerkabaSphereFlowerAuthority.Interval3.Singleton(abc))));
                if (valid) AssertUlp(actual.A.xy, root, 12, $"root {abc}");
            }

            foreach (float turn in new[] { -0.3f, -0.125f, 0f, 0.0625f, 0.25f })
            {
                OracleCase actual = results[cursor++];
                float2 target = MerkabaSphereFlowerAuthority
                    .RotateTangentHalfAngle(baseRoot, turn);
                var proof = MerkabaSphereFlowerAuthority.TangentHalfAngle(
                    MerkabaSphereFlowerAuthority.Interval2.Singleton(baseRoot),
                    MerkabaSphereFlowerAuthority.Interval2.Singleton(target),
                    out var recovered);
                Assert.That(proof,
                    Is.EqualTo(MerkabaSphereFlowerAuthority.ProofClassification.Certain));
                Assert.That(actual.Control.x, Is.EqualTo(1));
                Assert.That(actual.A.x, Is.GreaterThanOrEqualTo(recovered.Lower)
                    .And.LessThanOrEqualTo(recovered.Upper));
                AssertUlp(actual.B.xy, target, 12, "tau synthesis");
            }

            foreach (float4 q in tetraCases)
            {
                OracleCase actual = results[cursor++];
                float4 forward = MerkabaSphereFlowerAuthority.TetraForward(q);
                float4 inverse = MerkabaSphereFlowerAuthority.TetraInverse(
                    forward.x, forward.yzw);
                AssertUlp(actual.A, forward, 16, "tetra forward");
                AssertUlp(actual.B, inverse, 24, "tetra inverse");
            }

            for (int i = 0;
                 i < MerkabaSphereFlowerAuthority.SkinChambers.Length; i++)
            {
                var expected = MerkabaSphereFlowerAuthority.SkinChambers[i];
                OracleCase actual = results[cursor++];
                Assert.That(actual.Control, Is.EqualTo(new int4(expected.High,
                    expected.Middle, expected.Low, expected.ChildSite)));
                AssertBits(actual.A.xyz, new float3(expected.ChildWedge,
                    expected.NextStitchState, expected.Orientation),
                    $"skin chamber {i}");
            }
            for (int i = 0;
                 i < MerkabaSphereFlowerAuthority.SkinThreadPositionCount; i++)
            {
                OracleCase actual = results[cursor++];
                Assert.That(actual.Control.xy, Is.EqualTo(new int2(
                    MerkabaSphereFlowerAuthority.SkinCanonicalToThread[i],
                    MerkabaSphereFlowerAuthority.SkinThreadToCanonical[i])));
            }
            for (int i = 0; i < MerkabaSphereFlowerAuthority.SkinL5Count; i++)
            {
                OracleCase actual = results[cursor++];
                Assert.That(actual.Control.xy, Is.EqualTo(new int2(
                    MerkabaSphereFlowerAuthority.SkinCanonicalL5ToThread[i],
                    MerkabaSphereFlowerAuthority.SkinL5ChildRank[i])));
            }
            for (int i = 0; i < MerkabaSphereFlowerAuthority.SkinL3Count; i++)
            {
                OracleCase actual = results[cursor++];
                Assert.That(actual.Control.xy, Is.EqualTo(new int2(
                    MerkabaSphereFlowerAuthority.SkinL3ChildRank[i],
                    MerkabaSphereFlowerAuthority.SkinL3State[i])));
            }
            for (int i = 0; i < MerkabaSphereFlowerAuthority.SkinL4Count; i++)
            {
                OracleCase actual = results[cursor++];
                Assert.That(actual.Control.xyz, Is.EqualTo(new int3(
                    MerkabaSphereFlowerAuthority.SkinL4ParentThread[i],
                    MerkabaSphereFlowerAuthority.SkinL4ChildRank[i],
                    MerkabaSphereFlowerAuthority.SkinL4State[i])));
            }
            for (int i = 0;
                 i < MerkabaSphereFlowerAuthority.SkinChambers.Length; i++)
            {
                var rule = MerkabaSphereFlowerAuthority.SkinChambers[i];
                float3 barycentric = default;
                barycentric[rule.High] = 0.6f;
                barycentric[rule.Middle] = 0.3f;
                barycentric[rule.Low] = 0.1f;
                var expected = MerkabaSphereFlowerAuthority.DescendSkin(
                    barycentric, i / 6, out float3 child);
                OracleCase actual = results[cursor++];
                Assert.That(actual.Control, Is.EqualTo(new int4(
                    expected.ChildSite, expected.ChildWedge,
                    expected.NextStitchState,
                    expected.Orientation > 0 ? 1 : 0)));
                AssertBits(actual.B.xyz, child, $"skin descend {i}");
            }
            for (int bit = 0; bit <= 49; bit++)
            {
                OracleCase actual = results[cursor++];
                AssertBits(actual.A.xy, new float2(
                    MerkabaSphereFlowerAuthority.Rank7(0x6du,
                        math.min(bit, 7)),
                    MerkabaSphereFlowerAuthority.Rank49(0xa5a5a5a5u,
                        0x00015555u, bit)), $"rank bit {bit}");
            }
            for (int i3 = 0; i3 < skinCoordinates.Length; i3++)
            for (int i4 = 0; i4 < skinCoordinates.Length; i4++)
            {
                float3 c3 = skinCoordinates[i3];
                float3 c4 = skinCoordinates[i4];
                float3 c5 = skinCoordinates[(i3 + i4) % skinCoordinates.Length];
                float3 amplitude = new(0.003f, -0.001f, 0.00025f);
                OracleCase actual = results[cursor++];
                AssertUlp(actual.A.xyz, new float3(
                    MerkabaSphereFlowerAuthority.SkinBubble(c3),
                    MerkabaSphereFlowerAuthority.SkinBubble(c4),
                    MerkabaSphereFlowerAuthority.SkinBubble(c5)), 4,
                    $"skin bubble {i3}/{i4}");
                AssertUlp(actual.A.w,
                    MerkabaSphereFlowerAuthority.EvaluateNestedSkinV(
                        c3, c4, c5, amplitude), 8,
                    $"nested V {i3}/{i4}");
            }
            foreach (int4 split in splitCases)
            {
                OracleCase actual = results[cursor++];
                bool valid = MerkabaSphereFlowerAuthority.ValidateSkinSplitClosure(
                    split.x != 0, unchecked((uint)split.y),
                    unchecked((uint)split.z), unchecked((uint)split.w));
                Assert.That(actual.Control.x, Is.EqualTo(valid ? 1 : 0));
                uint expected = valid ? (uint)MerkabaSphereFlowerAuthority
                    .CompactSkinValueCount(split.x != 0,
                        unchecked((uint)split.y), unchecked((uint)split.z),
                        unchecked((uint)split.w)) : uint.MaxValue;
                AssertBits(actual.A.x, (float)expected,
                    $"split count {split}");
            }
            for (int classificationCase = 0; classificationCase < 4;
                 classificationCase++)
            {
                var scalar = new MerkabaSphereFlowerAuthority.FloatInterval[7];
                var rgb = new MerkabaSphereFlowerAuthority.FloatInterval[21];
                for (int i = 0; i < scalar.Length; i++)
                    scalar[i] = new MerkabaSphereFlowerAuthority.FloatInterval(
                        0f, 1f);
                for (int i = 0; i < rgb.Length; i++)
                    rgb[i] = new MerkabaSphereFlowerAuthority.FloatInterval(0f, 1f);
                uint certainMask = classificationCase == 2 ? 0x3fu : 0x7fu;
                if (classificationCase == 1)
                {
                    scalar[6] = new MerkabaSphereFlowerAuthority.FloatInterval(
                        2f, 3f);
                    rgb[18] = scalar[6];
                }
                else if (classificationCase == 3)
                {
                    rgb[19] = new MerkabaSphereFlowerAuthority.FloatInterval(
                        2f, 3f);
                }
                OracleCase actual = results[cursor++];
                Assert.That(actual.Control.xy, Is.EqualTo(new int2(
                    (int)MerkabaSphereFlowerAuthority.ClassifyScalarSkinSplit(
                        scalar, certainMask),
                    (int)MerkabaSphereFlowerAuthority.ClassifyRgbSkinSplit(
                        rgb, certainMask))),
                    $"split classification {classificationCase}");
            }

            foreach (OracleCase item in intervalCases)
            {
                OracleCase actual = results[cursor++];
                var abc = new MerkabaSphereFlowerAuthority.Interval3(
                    new MerkabaSphereFlowerAuthority.FloatInterval(item.A.x,
                        item.A.y),
                    new MerkabaSphereFlowerAuthority.FloatInterval(item.A.z,
                        item.A.w),
                    new MerkabaSphereFlowerAuthority.FloatInterval(item.B.x,
                        item.B.y));
                Assert.That(actual.Control.x, Is.EqualTo(
                    (int)MerkabaSphereFlowerAuthority.ClassifyRoots(abc)));
            }

            Assert.That(unchecked((uint)results[cursor++].Control.x),
                Is.EqualTo(MerkabaSphereFlowerAuthority.FrozenTableHash));
            Assert.That(MerkabaSphereFlowerAuthority.ComputeFrozenTableHash(),
                Is.EqualTo(MerkabaSphereFlowerAuthority.FrozenTableHash));
            Assert.That(cursor, Is.EqualTo(results.Length));
        }

        [Test, Timeout(60000)]
        public void CompactThreadAddress_IsExhaustivelyCpuHlslIdentical()
        {
            const uint groupBase = 20u;
            uint partialLo = 1u | ((1u << 1 | 1u << 5) << 1) |
                (1u << (8 + 7));
            uint partialHi = 1u << (8 + 35 - 32);
            var splits = new[]
            {
                new uint2(uint.MaxValue, 0x01ffffffu),
                new uint2(partialLo, partialHi)
            };
            var cases = new List<OracleCase>(2058);
            foreach (uint2 split in splits)
            for (int c3 = 0; c3 < 7; c3++)
            for (int c4 = 0; c4 < 7; c4++)
            for (int c5 = 0; c5 < 7; c5++)
            for (int level = 3; level <= 5; level++)
                cases.Add(new OracleCase(24)
                {
                    Control = new int4(24, level, c3, c4),
                    A = new float4(math.asfloat((uint)c5),
                        math.asfloat(groupBase), math.asfloat(split.x),
                        math.asfloat(split.y))
                });

            OracleCase[] results = Dispatch(cases,
                "SphereFlowerSkinAddressOracle");
            int cursor = 0;
            foreach (uint2 split in splits)
            for (int c3 = 0; c3 < 7; c3++)
            for (int c4 = 0; c4 < 7; c4++)
            for (int c5 = 0; c5 < 7; c5++)
            for (int level = 3; level <= 5; level++)
            {
                bool valid = MerkabaFlowerSkinSplitBits.TryCompactChildAddress(
                    groupBase, split.x, split.y, level, c3, c4, c5,
                    out uint groupIndex, out int childStitchRank);
                Assert.That(results[cursor++].Control, Is.EqualTo(new int4(
                    valid ? 1 : 0, unchecked((int)groupIndex),
                    childStitchRank, 0)),
                    $"compact thread address {split}/{c3}/{c4}/{c5}/L{level}");
            }
            Assert.That(cursor, Is.EqualTo(results.Length));
        }

        [Test, Timeout(120000)]
        public void ClippedSkinSplit_ExhaustivelySeparatesEmptyFromMissingEvidence()
        {
            // Every support/certainty mask, with either one distinct child
            // or seven distinct children. Out-of-support values must not
            // manufacture novelty; uncovered nonempty support stays ambiguous.
            var cases = new List<OracleCase>(2 * 128 * 128 + 1);
            for (int variant = 0; variant < 2; variant++)
            for (int support = 0; support < 128; support++)
            for (int certain = 0; certain < 128; certain++)
                cases.Add(new OracleCase(25)
                {
                    Control = new int4(25, variant, certain, support)
                });
            cases.Add(new OracleCase(25) { Control = new int4(25, 1, 127, 128) });
            OracleCase[] actual = Dispatch(cases);
            var scalar = new MerkabaSphereFlowerAuthority.FloatInterval[7];
            var rgb = new MerkabaSphereFlowerAuthority.FloatInterval[21];
            for (int index = 0; index < cases.Count; index++)
            {
                int4 input = cases[index].Control;
                uint support = (uint)input.w, certain = (uint)input.z;
                for (int child = 0; child < 7; child++)
                {
                    float lower = input.y == 0 ? (child == 6 ? 2f : 0f) : 2f * child;
                    scalar[child] = new MerkabaSphereFlowerAuthority.FloatInterval(
                        lower, lower + 1f);
                    for (int channel = 0; channel < 3; channel++)
                        rgb[3 * child + channel] = scalar[child];
                }
                bool missing = (support & ~127u) != 0u ||
                    (support & certain) != support;
                bool distinct = input.y == 0
                    ? (support & 64u) != 0u && (support & 63u) != 0u
                    : math.countbits(support) >= 2;
                int expected = missing ? 2 : distinct ? 1 : 0;
                Assert.That((int)MerkabaSphereFlowerAuthority.ClassifyScalarSkinSplit(
                    scalar, certain, support), Is.EqualTo(expected));
                Assert.That((int)MerkabaSphereFlowerAuthority.ClassifyRgbSkinSplit(
                    rgb, certain, support), Is.EqualTo(expected));
                Assert.That(actual[index].Control.xy, Is.EqualTo(new int2(expected)),
                    $"clipped split variant/certain/support={input.yzw}");
            }
        }

        private static OracleCase[] Dispatch(List<OracleCase> cases,
            string kernelName = "SphereFlowerOracle")
        {
            const string path =
                "Packages/com.genesis.roomscan/Tests/Editor/" +
                "MerkabaSphereFlowerOracle.compute";
            ComputeShader shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
            Assert.That(shader, Is.Not.Null, path);
            int kernel = shader.FindKernel(kernelName);
            int stride = Marshal.SizeOf<OracleCase>();
            Assert.That(stride, Is.EqualTo(64));
            using var input = new ComputeBuffer(cases.Count, stride,
                ComputeBufferType.Structured);
            using var output = new ComputeBuffer(cases.Count, stride,
                ComputeBufferType.Structured);
            input.SetData(cases);
            shader.SetBuffer(kernel, "_SphereFlowerOracleCases", input);
            shader.SetBuffer(kernel, "_SphereFlowerOracleResults", output);
            shader.SetInt("_SphereFlowerOracleCaseCount", cases.Count);
            shader.Dispatch(kernel, (cases.Count + 63) / 64, 1, 1);
            var result = new OracleCase[cases.Count];
            output.GetData(result);
            return result;
        }

        private static void AssertBits(float actual, float expected,
            string context) => Assert.That(math.asuint(actual),
            Is.EqualTo(math.asuint(expected)), context);

        private static void AssertBits(float2 actual, float2 expected,
            string context)
        {
            Assert.That(math.asuint(actual.x), Is.EqualTo(math.asuint(expected.x)),
                context + ".x");
            Assert.That(math.asuint(actual.y), Is.EqualTo(math.asuint(expected.y)),
                context + ".y");
        }

        private static void AssertBits(float3 actual, float3 expected,
            string context)
        {
            AssertBits(actual.xy, expected.xy, context);
            Assert.That(math.asuint(actual.z), Is.EqualTo(math.asuint(expected.z)),
                context + ".z");
        }

        private static void AssertBits(float4 actual, float4 expected,
            string context)
        {
            AssertBits(actual.xyz, expected.xyz, context);
            Assert.That(math.asuint(actual.w), Is.EqualTo(math.asuint(expected.w)),
                context + ".w");
        }

        private static void AssertUlp(float actual, float expected, int maximum,
            string context) => Assert.That(UlpDistance(actual, expected),
            Is.LessThanOrEqualTo(maximum), context);

        private static void AssertUlp(float2 actual, float2 expected, int maximum,
            string context)
        {
            AssertUlp(actual.x, expected.x, maximum, context + ".x");
            AssertUlp(actual.y, expected.y, maximum, context + ".y");
        }

        private static void AssertUlp(float3 actual, float3 expected, int maximum,
            string context)
        {
            AssertUlp(actual.xy, expected.xy, maximum, context);
            AssertUlp(actual.z, expected.z, maximum, context + ".z");
        }

        private static void AssertUlp(float4 actual, float4 expected, int maximum,
            string context)
        {
            AssertUlp(actual.xyz, expected.xyz, maximum, context);
            AssertUlp(actual.w, expected.w, maximum, context + ".w");
        }

        private static int UlpDistance(float left, float right)
        {
            if (float.IsNaN(left) || float.IsNaN(right)) return int.MaxValue;
            int a = OrderedBits(left);
            int b = OrderedBits(right);
            long distance = Math.Abs((long)a - b);
            return distance > int.MaxValue ? int.MaxValue : (int)distance;
        }

        private static int OrderedBits(float value)
        {
            int bits = BitConverter.SingleToInt32Bits(value);
            return bits < 0 ? int.MinValue - bits : bits;
        }
    }
}
