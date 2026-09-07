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

        [Test]
        public void DirectedR1SectorFlags_MatchIndependentPowerOrderAndStrictBoundaries()
        {
            List<OracleCase> cases = R1SectorFlagCases(out List<int4> expected);
            AssertR1SectorFlagCpu(cases, expected);
        }

        [Test, Timeout(60000)]
        public void DirectedR1SectorFlags_ActualHlslMatchesIndependentPowerOrder()
        {
            List<OracleCase> cases = R1SectorFlagCases(out List<int4> expected);
            AssertR1SectorFlagCpu(cases, expected);
            OracleCase[] results = Dispatch(cases);
            for (int i = 0; i < cases.Count; i++)
                Assert.That(results[i].Control, Is.EqualTo(expected[i]),
                    $"R1 HLSL face/sector={cases[i].Control.yz}, interval={cases[i].A}");
        }

        private static List<OracleCase> R1SectorFlagCases(out List<int4> expected)
        {
            var cases = new List<OracleCase>();
            expected = new List<int4>();
            ulong everyFace = 0ul;
            bool exercisesBoundAboveOne = false;
            for (int node = 0; node < 6; node++)
            {
                var face = MerkabaSphereFlowerAuthority.Nodes[node];
                var line = MerkabaSphereFlowerAuthority.Lines[face.LineClass];
                ulong covered = 0ul;
                for (int sector = 0; sector < line.SectorCount; sector++)
                {
                    int petal = -1;
                    // Weighted sums of adjacent endpoints lie strictly inside
                    // their (< pi) sector. This samples the frozen chart; it
                    // does not regenerate its cuts or assume their flag IDs.
                    for (int numerator = 1; numerator <= 3; numerator++)
                    {
                        float2 phase = R1SectorInterior(face.LineClass, sector, numerator);
                        int independent = IndependentR1PowerFlag(node, phase,
                            ref exercisesBoundAboveOne);
                        if (petal >= 0) Assert.That(independent, Is.EqualTo(petal));
                        petal = independent;
                        cases.Add(new OracleCase(26, node)
                        {
                            Control = new int4(26, node, sector, 0),
                            A = new float4(phase.x, phase.x, phase.y, phase.y)
                        });
                        expected.Add(new int4(petal, sector, petal, 1));
                    }
                    covered |= 1ul << petal;
                    var boundary = MerkabaSphereFlowerAuthority.SectorBoundaries[
                        line.SectorOffset + sector].Enclosure;
                    cases.Add(new OracleCase(26, node)
                    {
                        Control = new int4(26, node, sector, 0),
                        A = new float4(boundary.X.Lower, boundary.X.Upper,
                            boundary.Y.Lower, boundary.Y.Upper)
                    });
                    expected.Add(new int4(petal, -1, -1, 0));
                    float2 before = R1SectorInterior(face.LineClass,
                        (sector + line.SectorCount - 1) % line.SectorCount, 2);
                    float2 after = R1SectorInterior(face.LineClass, sector, 2);
                    cases.Add(new OracleCase(26, node)
                    {
                        Control = new int4(26, node, sector, 0),
                        A = new float4(Math.Min(boundary.X.Lower, Math.Min(before.x, after.x)),
                            Math.Max(boundary.X.Upper, Math.Max(before.x, after.x)),
                            Math.Min(boundary.Y.Lower, Math.Min(before.y, after.y)),
                            Math.Max(boundary.Y.Upper, Math.Max(before.y, after.y)))
                    });
                    expected.Add(new int4(petal, -1, -1, 0));
                }
                ulong incident = 0ul;
                for (int petal = 0; petal < MerkabaSphereFlowerAuthority.Petals.Length; petal++)
                    if (MerkabaSphereFlowerAuthority.Petals[petal].FaceNode == node)
                        incident |= 1ul << petal;
                Assert.That(math.countbits((uint)covered) + math.countbits((uint)(covered >> 32)),
                    Is.EqualTo(8), $"directed face {face.Direction}");
                Assert.That(covered, Is.EqualTo(incident));
                everyFace |= covered;
                foreach (int invalidSector in new[] { -1, (int)line.SectorCount })
                {
                    cases.Add(new OracleCase(26, node)
                        { Control = new int4(26, node, invalidSector, 0) });
                    expected.Add(new int4(-1, -1, -1, 0));
                }
            }
            Assert.That(everyFace, Is.EqualTo((1ul << 48) - 1ul));
            Assert.That(exercisesBoundAboveOne, Is.True,
                "The accepted xi clip is 2, not the superseded bound 1.");
            foreach (int invalidNode in new[] { -1, 6, 25, 26 })
            {
                cases.Add(new OracleCase(26, invalidNode));
                expected.Add(new int4(-1, -1, -1, 0));
            }
            return cases;
        }

        private static float2 R1SectorInterior(int lineClass, int sector, int numerator)
        {
            var line = MerkabaSphereFlowerAuthority.Lines[lineClass];
            float2 start = MerkabaSphereFlowerAuthority.SectorBoundaries[
                line.SectorOffset + sector].Unit;
            float2 end = MerkabaSphereFlowerAuthority.SectorBoundaries[
                line.SectorOffset + (sector + 1) % line.SectorCount].Unit;
            double x = (4 - numerator) * (double)start.x + numerator * (double)end.x;
            double y = (4 - numerator) * (double)start.y + numerator * (double)end.y;
            double length = Math.Sqrt(x * x + y * y);
            Assert.That(length, Is.GreaterThan(0d));
            return new float2((float)(x / length), (float)(y / length));
        }

        private static int IndependentR1PowerFlag(int faceNode, float2 phase,
            ref bool exercisesBoundAboveOne)
        {
            var face = MerkabaSphereFlowerAuthority.Nodes[faceNode];
            var line = MerkabaSphereFlowerAuthority.Lines[face.LineClass];
            double3 point = 0.5d * new double3(face.Direction.x, face.Direction.y, face.Direction.z) +
                (Math.Sqrt(3d) / 2d) *
                (phase.x * new double3(line.E1.x, line.E1.y, line.E1.z) +
                 phase.y * new double3(line.E2.x, line.E2.y, line.E2.z));
            // For equal shell radii, maximal q.X is exactly minimal sphere
            // power. Enumerate the four legal edges directly in coordinates,
            // independently of SectorPetalMasks and the production sign code.
            int faceAxis = face.Direction.x != 0 ? 0 : face.Direction.y != 0 ? 1 : 2;
            int edgeAxis = -1;
            int3 edge = default;
            double best = double.NegativeInfinity;
            for (int axis = 0; axis < 3; axis++)
            {
                if (axis == faceAxis) continue;
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    int3 candidate = face.Direction;
                    candidate[axis] += sign;
                    double powerOrder = candidate.x * point.x + candidate.y * point.y +
                        candidate.z * point.z;
                    Assert.That(powerOrder, Is.Not.EqualTo(best), "Interior edge order cannot tie.");
                    if (powerOrder <= best) continue;
                    best = powerOrder;
                    edge = candidate;
                    edgeAxis = axis;
                }
            }
            int cornerAxis = 3 - faceAxis - edgeAxis;
            Assert.That(point[cornerAxis], Is.Not.EqualTo(0d), "Interior corner order cannot tie.");
            int3 corner = edge;
            corner[cornerAxis] = point[cornerAxis] > 0d ? 1 : -1;
            double edgeXi = 2d * edge[edgeAxis] * point[edgeAxis];
            double cornerXi = 2d * corner[cornerAxis] * point[cornerAxis];
            Assert.That(cornerXi, Is.GreaterThan(0d));
            Assert.That(edgeXi, Is.GreaterThan(cornerXi));
            Assert.That(edgeXi, Is.LessThanOrEqualTo(2d));
            exercisesBoundAboveOne |= edgeXi > 1d;
            for (int p = 0; p < MerkabaSphereFlowerAuthority.Petals.Length; p++)
            {
                var flag = MerkabaSphereFlowerAuthority.Petals[p];
                if (flag.FaceNode == faceNode &&
                    math.all(MerkabaSphereFlowerAuthority.Nodes[flag.EdgeNode].Direction == edge) &&
                    math.all(MerkabaSphereFlowerAuthority.Nodes[flag.CornerNode].Direction == corner))
                    return p;
            }
            Assert.Fail("Independent same-shell power maximum has no incident Flower flag.");
            return -1;
        }

        private static void AssertR1SectorFlagCpu(List<OracleCase> cases, List<int4> expected)
        {
            for (int i = 0; i < cases.Count; i++)
            {
                OracleCase input = cases[i];
                int node = input.Control.y, requestedSector = input.Control.z;
                bool tableValid = MerkabaSphereFlowerAuthority.TryGetR1SectorFlag(node,
                    requestedSector, out int tablePetal);
                int sector = -1, rootPetal = -1;
                bool certain = false;
                if ((uint)node < 6u)
                {
                    var root = new MerkabaSphereFlowerAuthority.Interval2(
                        new MerkabaSphereFlowerAuthority.FloatInterval(input.A.x, input.A.y),
                        new MerkabaSphereFlowerAuthority.FloatInterval(input.A.z, input.A.w));
                    certain = MerkabaSphereFlowerAuthority.ClassifySector(
                        MerkabaSphereFlowerAuthority.Nodes[node].LineClass, root, out sector) ==
                        MerkabaSphereFlowerAuthority.ProofClassification.Certain;
                }
                bool admitted = certain && MerkabaSphereFlowerAuthority.TryGetR1SectorFlag(
                    node, sector, out rootPetal);
                Assert.That(new int4(tableValid ? tablePetal : -1, certain ? sector : -1,
                    admitted ? rootPetal : -1, admitted ? 1 : 0), Is.EqualTo(expected[i]),
                    $"R1 CPU face/sector={node}/{requestedSector}, interval={input.A}");
                if (!tableValid) continue;
                var face = MerkabaSphereFlowerAuthority.Nodes[node];
                var line = MerkabaSphereFlowerAuthority.Lines[face.LineClass];
                ulong mask = MerkabaSphereFlowerAuthority.R1SectorPetalMasks[
                    2 * (line.SectorOffset + requestedSector) + (face.Orientation < 0 ? 1 : 0)];
                Assert.That(mask, Is.EqualTo(1ul << expected[i].x));
            }
        }

        [Test, Timeout(60000)]
        public void R1RootAndRelationSelectors_ExactIdentityParityAndBoundedAnalyticContainment()
        {
            var cases = new List<OracleCase>(640);
            float3[] normals =
            {
                math.normalize(new float3(2f, 3f, 5f)),
                math.normalize(new float3(7f, -4f, 2f)),
                // Near tangent on X: both distinct roots can lie in one flag.
                math.normalize(new float3(17f, 9f, 4f))
            };
            for (int plane = 0; plane < normals.Length; plane++)
            for (int petal = 0; petal < 48; petal++)
            {
                int3 owner = (petal & 1) == 0 ? new int3(-257, -33, -9) : new int3(256, 32, 8);
                int3 direction = MerkabaSphereFlowerAuthority.Nodes[
                    MerkabaSphereFlowerAuthority.Petals[petal].FaceNode].Direction;
                uint flags = KernelState.SetSurfacePlane(MerkabaConstants.OccupiedFlag,
                    normals[plane], 0f);
                KernelState.DecodeSurfacePlane(flags, out float3 decodedNormal, out float decodedOffset);
                uint neighbour = KernelState.SetSurfacePlane(MerkabaConstants.OccupiedFlag,
                    decodedNormal, decodedOffset - MerkabaSphereFlowerAuthority.LevelStep(0) *
                        math.dot(decodedNormal, (float3)direction));
                cases.Add(R1SelectorCase(false, owner, flags, neighbour, petal));
                cases.Add(R1SelectorCase(true, owner, flags, neighbour, petal));
                cases.Add(R1SelectorCase(true, owner, flags, 0u, petal));
                cases.Add(R1SelectorCase(true, owner, flags,
                    (neighbour & ~MerkabaConstants.OccupiedFlag) | MerkabaConstants.R1SeedFlag, petal));
            }
            for (int face = 0; face < 6; face++)
            {
                int petal = -1;
                for (int p = 0; p < 48 && petal < 0; p++)
                    if (MerkabaSphereFlowerAuthority.Petals[p].FaceNode == face) petal = p;
                uint flags = KernelState.SetSurfacePlane(MerkabaConstants.OccupiedFlag, normals[0], 0f);
                cases.Add(R1SelectorCase(false, new int3(-8, -32, -256), 0u, 0u, petal));
                cases.Add(R1SelectorCase(false, new int3(-8, -32, -256),
                    (flags & ~MerkabaConstants.OccupiedFlag) | MerkabaConstants.R1SeedFlag, 0u, petal));
                cases.Add(R1SelectorCase(false, new int3(-8, -32, -256), flags, 0u,
                    petal, 2f, 0.05f));
            }

            OracleCase[] results = Dispatch(cases);
            uint seenStatuses = 0u;
            bool multipleCertainAlternatives = false;
            for (int i = 0; i < cases.Count; i++)
            {
                OracleCase input = cases[i];
                int3 owner = math.asint(input.A.xyz);
                uint flags = unchecked((uint)input.Control.w), neighbour = math.asuint(input.A.w);
                int petal = input.Control.z;
                var expected = R1SelectorByExplicitSigns(input);
                var cpu = input.Control.y == 0
                    ? MerkabaSphereFlowerAuthority.SelectR1FlagRoots(owner, flags, petal, input.B.x, input.B.y)
                    : MerkabaSphereFlowerAuthority.SelectR1FlagRelation(owner, flags, neighbour,
                        petal, input.B.x, input.B.y);
                var status = cpu.Classify(out var unique);
                Assert.That(cpu.CandidateMask, Is.EqualTo(expected.CandidateMask), $"candidate case {i}");
                Assert.That(cpu.UnresolvedMask, Is.EqualTo(expected.UnresolvedMask), $"unresolved case {i}");
                Assert.That(status, Is.EqualTo(expected.Classify(out _)), $"status case {i}");
                uint tags = cpu.Minus.Symbol.Tag | (cpu.Plus.Symbol.Tag << 16);
                Assert.That(results[i].Control, Is.EqualTo(new int4((int)cpu.CandidateMask,
                    (int)cpu.UnresolvedMask, (int)status, unchecked((int)tags))),
                    $"selector case {i}, mode/petal={input.Control.yz}, owner={owner}, " +
                    $"flags={flags:x8}, neighbour={neighbour:x8}");
                Assert.That(math.asint(results[i].C.xyz), Is.EqualTo(unique.Symbol.Junction));
                Assert.That(math.asuint(results[i].C.w), Is.EqualTo(unique.Symbol.Tag));
                seenStatuses |= 1u << (int)status;
                multipleCertainAlternatives |= cpu.CandidateMask == 3u;
                for (int sign = 0; sign < 2; sign++)
                {
                    if ((cpu.CandidateMask & (1u << sign)) == 0u) continue;
                    var cpuRoot = sign == 0 ? cpu.Minus : cpu.Plus;
                    var expectedRoot = sign == 0 ? expected.Minus : expected.Plus;
                    float4 cpuBounds = RootBounds(cpuRoot.Root);
                    AssertBits(cpuBounds, RootBounds(expectedRoot.Root), $"explicit sign case {i}/{sign}");
                    float4 gpuBounds = sign == 0 ? results[i].A : results[i].B;
                    Assert.That(math.all(math.isfinite(gpuBounds)), Is.True);
                    Assert.That(gpuBounds.x, Is.LessThanOrEqualTo(gpuBounds.y));
                    Assert.That(gpuBounds.z, Is.LessThanOrEqualTo(gpuBounds.w));
                    Assert.That(Math.Max(gpuBounds.x, cpuBounds.x),
                        Is.LessThanOrEqualTo(Math.Min(gpuBounds.y, cpuBounds.y)));
                    Assert.That(Math.Max(gpuBounds.z, cpuBounds.z),
                        Is.LessThanOrEqualTo(Math.Min(gpuBounds.w, cpuBounds.w)));
                    if (math.any(math.asuint(gpuBounds) != math.asuint(cpuBounds)))
                        LogR1RootIntermediateParity(input);
                    AssertBits(gpuBounds, cpuBounds,
                        $"root endpoint case={i}, mode={input.Control.y}, petal={petal}, sign={sign}, " +
                        $"owner={owner}, flags={flags:x8}, neighbour={neighbour:x8}, errors={input.B.xy}; " +
                        $"CPU={BoundsHex(cpuBounds)}, HLSL={BoundsHex(gpuBounds)}");
                    double2 analytic = AnalyticR1SelectorRoot(input, sign != 0);
                    foreach (float4 bounds in new[] { cpuBounds, gpuBounds })
                    {
                        Assert.That(analytic.x, Is.InRange((double)bounds.x, (double)bounds.y),
                            $"analytic x containment case {i}/{sign}");
                        Assert.That(analytic.y, Is.InRange((double)bounds.z, (double)bounds.w),
                            $"analytic y containment case {i}/{sign}");
                    }
                }
            }
            Assert.That(seenStatuses, Is.EqualTo(7u), "Fixtures must exercise absent, certain and ambiguous results.");
            Assert.That(multipleCertainAlternatives, Is.True,
                "The near-tangent plane must retain both analytic alternatives, never choose one.");
        }

        private static OracleCase R1SelectorCase(bool relation, int3 owner, uint flags,
            uint neighbourFlags, int petal, float normalError = 0f, float offsetError = 0f) => new(27)
        {
            Control = new int4(27, relation ? 1 : 0, petal, unchecked((int)flags)),
            A = new float4(math.asfloat(owner), math.asfloat(neighbourFlags)),
            B = new float4(normalError, offsetError, 0f, 0f)
        };

        private static void LogR1RootIntermediateParity(OracleCase input)
        {
            var face = MerkabaSphereFlowerAuthority.Nodes[
                MerkabaSphereFlowerAuthority.Petals[input.Control.z].FaceNode];
            LogPlane(unchecked((uint)input.Control.w), face.Direction, "owner");
            if (input.Control.y != 0)
                LogPlane(math.asuint(input.A.w), -face.Direction, "neighbour");

            void LogPlane(uint flags, int3 direction, string endpoint)
            {
                MerkabaSphereFlowerAuthority.M8FlowerUnpackPlane(flags, out float3 normal, out float offset);
                var loop = MerkabaSphereFlowerAuthority.EvaluateLoop(0,
                    new MerkabaSphereFlowerAuthority.Long3(direction.x, direction.y, direction.z), face.LineClass);
                var abc = MerkabaSphereFlowerAuthority.RestrictPlaneToLoop(float3.zero,
                    normal, offset, input.B.x, input.B.y, loop);
                var q = MerkabaSphereFlowerAuthority.FloatInterval.Add(
                    MerkabaSphereFlowerAuthority.FloatInterval.Square(abc.Y),
                    MerkabaSphereFlowerAuthority.FloatInterval.Square(abc.Z));
                var delta = MerkabaSphereFlowerAuthority.FloatInterval.Subtract(q,
                    MerkabaSphereFlowerAuthority.FloatInterval.Square(abc.X));
                bool sqrtValid = MerkabaSphereFlowerAuthority.FloatInterval.TrySqrt(delta, out var turn);
                var expected = new OracleCase[3];
                expected[0].Control = math.asint(new float4(loop.E2, 0f));
                expected[0].A = new float4(normal, offset);
                expected[0].B = new float4(loop.Center, loop.Radius);
                expected[0].C = new float4(loop.E1, 0f);
                expected[1].Control = new int4(1, sqrtValid ? 1 : 0,
                    (int)MerkabaSphereFlowerAuthority.ClassifyRoots(abc), 0);
                expected[1].A = new float4(abc.X.Lower, abc.X.Upper, abc.Y.Lower, abc.Y.Upper);
                expected[1].B = new float4(abc.Z.Lower, abc.Z.Upper, q.Lower, q.Upper);
                expected[1].C = new float4(delta.Lower, delta.Upper, turn.Lower, turn.Upper);
                expected[2] = expected[1];
                var probes = new List<OracleCase>(3);
                for (int stage = 0; stage < 3; stage++)
                    probes.Add(new OracleCase(28, stage)
                    {
                        Control = new int4(28, stage, face.LineClass, unchecked((int)flags)),
                        A = new float4(math.asfloat(direction), input.B.x),
                        B = new float4(normal, offset),
                        C = new float4(input.B.y, 0f, 0f, 0f)
                    });
                OracleCase[] actual = Dispatch(probes);
                string[] labels = { "decode: A=normal/offset B=relative/radius C=E1 Control=E2",
                    "production: A=ABCxy B=ABCz/Q C=delta/sqrt",
                    "CPU-decoded plane injected: A=ABCxy B=ABCz/Q C=delta/sqrt" };
                for (int stage = 0; stage < 3; stage++)
                {
                    bool same = math.all(actual[stage].Control == expected[stage].Control) &&
                        math.all(math.asuint(actual[stage].A) == math.asuint(expected[stage].A)) &&
                        math.all(math.asuint(actual[stage].B) == math.asuint(expected[stage].B)) &&
                        math.all(math.asuint(actual[stage].C) == math.asuint(expected[stage].C));
                    TestContext.WriteLine($"R1 intermediate {endpoint} flags={flags:x8} stage={stage} " +
                        $"bitsEqual={same} {labels[stage]}; " +
                        $"CPU control={expected[stage].Control} A={BoundsHex(expected[stage].A)} " +
                        $"B={BoundsHex(expected[stage].B)} C={BoundsHex(expected[stage].C)}; " +
                        $"HLSL control={actual[stage].Control} A={BoundsHex(actual[stage].A)} " +
                        $"B={BoundsHex(actual[stage].B)} C={BoundsHex(actual[stage].C)}");
                }
            }
        }

        private static MerkabaSphereFlowerAuthority.R1FlagRoots R1SelectorByExplicitSigns(OracleCase input)
        {
            int3 owner = math.asint(input.A.xyz);
            uint flags = unchecked((uint)input.Control.w);
            const uint required = MerkabaConstants.OccupiedFlag | MerkabaConstants.SurfacePlaneValidFlag;
            if ((flags & (required | MerkabaConstants.R1SeedFlag)) != required) return default;
            int petal = input.Control.z;
            int faceNode = MerkabaSphereFlowerAuthority.Petals[petal].FaceNode;
            var face = MerkabaSphereFlowerAuthority.Nodes[faceNode];
            uint candidates = 0u, unresolved = 0u;
            MerkabaSphereFlowerAuthority.PhaseRootEvidence minus = default, plus = default;
            for (int sign = 0; sign < 2; sign++)
            {
                var proof = MerkabaSphereFlowerAuthority.CarrierRootProof(owner, flags, 0,
                    face.Direction, face.LineClass, sign != 0, input.B.x, input.B.y, out var root);
                if (proof == MerkabaSphereFlowerAuthority.ProofClassification.Impossible) continue;
                if (proof != MerkabaSphereFlowerAuthority.ProofClassification.Certain)
                { unresolved |= 1u << sign; continue; }
                Assert.That(MerkabaFlowerSymbolTag.TryDecode(root.Symbol.Tag, out var tag), Is.True);
                Assert.That(MerkabaSphereFlowerAuthority.TryGetR1SectorFlag(faceNode, tag.Sector,
                    out int selected), Is.True);
                if (selected != petal) continue;
                if (input.Control.y != 0)
                {
                    FixedR1EndpointPlanes(input, root.Symbol.Junction, face.LineClass,
                        out uint first, out uint second);
                    proof = MerkabaSphereFlowerAuthority.EvaluateCarrierRelation(root.Symbol.Junction,
                        face.LineClass, sign != 0, first, second, input.B.x, input.B.y, out root, out _);
                    if (proof == MerkabaSphereFlowerAuthority.ProofClassification.Impossible) continue;
                    if (proof != MerkabaSphereFlowerAuthority.ProofClassification.Certain)
                    { unresolved |= 1u << sign; continue; }
                    Assert.That(MerkabaFlowerSymbolTag.TryDecode(root.Symbol.Tag, out tag), Is.True);
                    Assert.That(MerkabaSphereFlowerAuthority.TryGetR1SectorFlag(faceNode, tag.Sector,
                        out selected), Is.True);
                    if (selected != petal) { unresolved |= 1u << sign; continue; }
                }
                candidates |= 1u << sign;
                if (sign == 0) minus = root; else plus = root;
            }
            return new MerkabaSphereFlowerAuthority.R1FlagRoots(candidates, unresolved, minus, plus);
        }

        private static void FixedR1EndpointPlanes(OracleCase input, int3 junction, int lineClass,
            out uint first, out uint second)
        {
            int3 owner = math.asint(input.A.xyz);
            Assert.That(MerkabaSphereFlowerAuthority.TryResolveEndpointPair(
                new MerkabaSphereFlowerAuthority.Long3(junction.x, junction.y, junction.z), lineClass,
                out var firstOwner, out var secondOwner), Is.True);
            bool ownerFirst = firstOwner.X == owner.x && firstOwner.Y == owner.y && firstOwner.Z == owner.z;
            var actualOwner = ownerFirst ? firstOwner : secondOwner;
            Assert.That(new int3((int)actualOwner.X, (int)actualOwner.Y, (int)actualOwner.Z), Is.EqualTo(owner));
            uint ownerFlags = unchecked((uint)input.Control.w), neighbourFlags = math.asuint(input.A.w);
            first = ownerFirst ? ownerFlags : neighbourFlags;
            second = ownerFirst ? neighbourFlags : ownerFlags;
        }

        private static double2 AnalyticR1SelectorRoot(OracleCase input, bool plus)
        {
            int petal = input.Control.z;
            var face = MerkabaSphereFlowerAuthority.Nodes[MerkabaSphereFlowerAuthority.Petals[petal].FaceNode];
            uint flags = unchecked((uint)input.Control.w);
            if (input.Control.y == 0) return AnalyticR1Root(flags, face.Direction, face.LineClass, plus);
            int3 owner = math.asint(input.A.xyz), junction = 2 * owner + face.Direction;
            FixedR1EndpointPlanes(input, junction, face.LineClass, out uint first, out uint second);
            int3 r = MerkabaSphereFlowerAuthority.Lines[face.LineClass].Direction;
            double2 sum = AnalyticR1Root(first, r, face.LineClass, plus) +
                AnalyticR1Root(second, -r, face.LineClass, plus);
            double norm = Math.Sqrt(sum.x * sum.x + sum.y * sum.y);
            Assert.That(norm, Is.GreaterThan(0d));
            return sum / norm;
        }

        private static double2 AnalyticR1Root(uint flags, int3 offset, int lineClass, bool plus)
        {
            KernelState.DecodeSurfacePlane(flags, out float3 normal, out float delta);
            var loop = MerkabaSphereFlowerAuthority.EvaluateLoop(0,
                new MerkabaSphereFlowerAuthority.Long3(offset.x, offset.y, offset.z), lineClass);
            double a = (double)normal.x * loop.Center.x + (double)normal.y * loop.Center.y +
                (double)normal.z * loop.Center.z - delta;
            double b = loop.Radius * ((double)normal.x * loop.E1.x + (double)normal.y * loop.E1.y +
                (double)normal.z * loop.E1.z);
            double c = loop.Radius * ((double)normal.x * loop.E2.x + (double)normal.y * loop.E2.y +
                (double)normal.z * loop.E2.z);
            double q = b * b + c * c, discriminant = q - a * a;
            Assert.That(q, Is.GreaterThan(0d));
            Assert.That(discriminant, Is.GreaterThanOrEqualTo(0d));
            double turn = (plus ? 1d : -1d) * Math.Sqrt(discriminant);
            return new double2((-a * b - turn * c) / q, (-a * c + turn * b) / q);
        }

        private static float4 RootBounds(MerkabaSphereFlowerAuthority.Interval2 root) => new(
            root.X.Lower, root.X.Upper, root.Y.Lower, root.Y.Upper);

        private static string BoundsHex(float4 value) =>
            $"{math.asuint(value.x):x8}/{math.asuint(value.y):x8}/{math.asuint(value.z):x8}/{math.asuint(value.w):x8}";

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
