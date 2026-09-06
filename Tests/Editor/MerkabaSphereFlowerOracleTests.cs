using System;
using System.Collections.Generic;
using System.IO;
using Genesis.RoomScan;
using NUnit.Framework;
using Unity.Mathematics;

namespace Genesis.RoomScan.Tests
{
    public sealed class MerkabaSphereFlowerOracleTests
    {
        private static readonly int[] Boundaries =
        {
            -257, -256, -255, -33, -32, -31, -9, -8, -1, 0, 1,
            7, 8, 31, 32, 33, 255, 256, 257
        };

        [Test]
        public void JunctionIdentity_IsExactForEveryDirectionAndSignedBoundary()
        {
            foreach (int value in Boundaries)
            foreach (var direction in MerkabaSphereFlowerAuthority.Directions)
            {
                int3 kernel = new(value, -value, value - 1);
                var left = MerkabaSphereFlowerAuthority.JunctionAddress(kernel,
                    direction.Direction);
                var right = MerkabaSphereFlowerAuthority.JunctionAddress(
                    kernel + direction.Direction, -direction.Direction);
                Assert.That(left, Is.EqualTo(right),
                    $"K={kernel} d={direction.Direction}");
                Assert.That(MerkabaSphereFlowerAuthority.JunctionParity(left),
                    Is.EqualTo((int)direction.Shell));
            }
        }

        [Test]
        public void JunctionAndLineClass_ResolveOneCanonicalEndpointPair()
        {
            foreach (int value in Boundaries)
            foreach (var direction in MerkabaSphereFlowerAuthority.Directions)
            {
                int3 kernel = new(value, -value - 1, value + 2);
                var junction = MerkabaSphereFlowerAuthority.JunctionAddress(
                    kernel, direction.Direction);
                Assert.That(MerkabaSphereFlowerAuthority.TryResolveEndpointPair(
                    junction, direction.LineClass, out var first, out var second),
                    Is.True);
                var firstJunction = new MerkabaSphereFlowerAuthority.Long3(
                    first.X * 2 +
                    MerkabaSphereFlowerAuthority.Lines[direction.LineClass]
                        .Direction.x,
                    first.Y * 2 +
                    MerkabaSphereFlowerAuthority.Lines[direction.LineClass]
                        .Direction.y,
                    first.Z * 2 +
                    MerkabaSphereFlowerAuthority.Lines[direction.LineClass]
                        .Direction.z);
                var secondJunction = new MerkabaSphereFlowerAuthority.Long3(
                    second.X * 2 -
                    MerkabaSphereFlowerAuthority.Lines[direction.LineClass]
                        .Direction.x,
                    second.Y * 2 -
                    MerkabaSphereFlowerAuthority.Lines[direction.LineClass]
                        .Direction.y,
                    second.Z * 2 -
                    MerkabaSphereFlowerAuthority.Lines[direction.LineClass]
                        .Direction.z);
                Assert.That(firstJunction, Is.EqualTo(junction));
                Assert.That(secondJunction, Is.EqualTo(junction));
            }

            Assert.That(MerkabaSphereFlowerAuthority.TryResolveEndpointPair(
                new MerkabaSphereFlowerAuthority.Long3(1, 0, 0), 1,
                out _, out _), Is.False,
                "A Y line cannot own an X-parity junction.");
        }

        [Test]
        public void CanonicalLineAlphabet_IsUniqueAndEndpointInvariant()
        {
            var seen = new HashSet<string>();
            int[] shells = new int[4];
            for (int i = 0; i < MerkabaSphereFlowerAuthority.Lines.Length; i++)
            {
                var line = MerkabaSphereFlowerAuthority.Lines[i];
                Assert.That(seen.Add(line.Direction.ToString()), Is.True);
                Assert.That(MerkabaSphereFlowerAuthority.FindLineClass(
                    line.Direction, out sbyte positive), Is.EqualTo(i));
                Assert.That(positive, Is.EqualTo(1));
                Assert.That(MerkabaSphereFlowerAuthority.FindLineClass(
                    -line.Direction, out sbyte negative), Is.EqualTo(i));
                Assert.That(negative, Is.EqualTo(-1));
                shells[(int)line.Shell]++;

                AssertRoundoff(math.lengthsq(line.UnitDirection), 1.0, 8, 3.0,
                    $"line {i} unit length");
                AssertRoundoff(math.lengthsq(line.E1), 1.0, 8, 3.0,
                    $"line {i} e1 length");
                AssertRoundoff(math.lengthsq(line.E2), 1.0, 8, 3.0,
                    $"line {i} e2 length");
                AssertRoundoff(math.dot(line.UnitDirection, line.E1), 0.0,
                    8, 3.0, $"line {i} unit/e1");
                AssertRoundoff(math.dot(line.UnitDirection, line.E2), 0.0,
                    8, 3.0, $"line {i} unit/e2");
                Assert.That(math.dot(math.cross(line.UnitDirection, line.E1),
                    line.E2), Is.GreaterThan(0f));
            }
            Assert.That(shells[1], Is.EqualTo(3));
            Assert.That(shells[2], Is.EqualTo(6));
            Assert.That(shells[3], Is.EqualTo(4));
        }

        [Test]
        public void DirectedNodes_AreExactlySixTwelveEight()
        {
            int[] shellCounts = new int[4];
            var directions = new HashSet<string>();
            foreach (var node in MerkabaSphereFlowerAuthority.Nodes)
            {
                shellCounts[(int)node.Shell]++;
                Assert.That(directions.Add(node.Direction.ToString()), Is.True);
            }
            Assert.That(shellCounts[1], Is.EqualTo(6));
            Assert.That(shellCounts[2], Is.EqualTo(12));
            Assert.That(shellCounts[3], Is.EqualTo(8));
        }

        [Test]
        public void FlagAlphabet_HasExactIncidenceAndWinding()
        {
            int[] referenced = new int[MerkabaSphereFlowerAuthority.StrandClassCount];
            int[] signedBoundary = new int[
                MerkabaSphereFlowerAuthority.StrandClassCount];
            for (int p = 0; p < MerkabaSphereFlowerAuthority.Petals.Length; p++)
            {
                var petal = MerkabaSphereFlowerAuthority.Petals[p];
                Assert.That(MerkabaSphereFlowerAuthority.Nodes[petal.FaceNode].Shell,
                    Is.EqualTo(MerkabaSphereFlowerAuthority.Shell.R1Core));
                Assert.That(MerkabaSphereFlowerAuthority.Nodes[petal.EdgeNode].Shell,
                    Is.EqualTo(MerkabaSphereFlowerAuthority.Shell.R2Shape));
                Assert.That(MerkabaSphereFlowerAuthority.Nodes[petal.CornerNode].Shell,
                    Is.EqualTo(MerkabaSphereFlowerAuthority.Shell.R3Closure));
                int determinant = Determinant(
                    MerkabaSphereFlowerAuthority.Nodes[petal.FaceNode].Direction,
                    MerkabaSphereFlowerAuthority.Nodes[petal.EdgeNode].Direction,
                    MerkabaSphereFlowerAuthority.Nodes[petal.CornerNode].Direction);
                Assert.That(Math.Sign(determinant), Is.EqualTo(petal.Orientation));
                for (int edge = 0; edge < 3; edge++)
                {
                    referenced[petal.Strand(edge)]++;
                    signedBoundary[petal.Strand(edge)] +=
                        petal.StrandSign(edge);
                    Assert.That(Math.Abs(petal.StrandSign(edge)), Is.EqualTo(1));
                }
            }

            for (int i = 0; i < MerkabaSphereFlowerAuthority.Strands.Length; i++)
            {
                var strand = MerkabaSphereFlowerAuthority.Strands[i];
                Assert.That(referenced[i], Is.EqualTo(2), $"strand {i}");
                Assert.That(strand.Petal0, Is.Not.EqualTo(strand.Petal1));
                Assert.That(PetalContainsStrand(strand.Petal0, i), Is.True);
                Assert.That(PetalContainsStrand(strand.Petal1, i), Is.True);
                Assert.That(signedBoundary[i], Is.Zero,
                    $"strand {i} must cancel between its two incident petals");
            }
        }

        [Test]
        public void SectorAlphabet_IsSymbolicallyDeduplicatedAndBelowAbiLimit()
        {
            int total = 0;
            for (int line = 0; line < MerkabaSphereFlowerAuthority.Lines.Length;
                 line++)
            {
                var rule = MerkabaSphereFlowerAuthority.Lines[line];
                int expected = rule.Shell switch
                {
                    MerkabaSphereFlowerAuthority.Shell.R1Core => 16,
                    MerkabaSphereFlowerAuthority.Shell.R2Shape => 6,
                    MerkabaSphereFlowerAuthority.Shell.R3Closure => 12,
                    _ => throw new InvalidOperationException()
                };
                Assert.That(rule.SectorCount, Is.EqualTo(expected),
                    $"line {line}");
                Assert.That(rule.SectorCount,
                    Is.LessThanOrEqualTo(
                        MerkabaSphereFlowerAuthority.MaximumSectorCount));
                Assert.That(rule.SectorOffset, Is.EqualTo(total));
                total += rule.SectorCount;

                for (int sector = 0; sector < rule.SectorCount; sector++)
                {
                    var a = MerkabaSphereFlowerAuthority.SectorBoundaries[
                        rule.SectorOffset + sector];
                    var b = MerkabaSphereFlowerAuthority.SectorBoundaries[
                        rule.SectorOffset + (sector + 1) % rule.SectorCount];
                    Assert.That(a.Enclosure.X.Lower, Is.LessThanOrEqualTo(a.Unit.x));
                    Assert.That(a.Enclosure.X.Upper, Is.GreaterThanOrEqualTo(a.Unit.x));
                    Assert.That(a.Enclosure.Y.Lower, Is.LessThanOrEqualTo(a.Unit.y));
                    Assert.That(a.Enclosure.Y.Upper, Is.GreaterThanOrEqualTo(a.Unit.y));
                    Assert.That(Cross(a.Unit, b.Unit), Is.GreaterThan(0f),
                        $"line {line} sector {sector}");

                    float2 midpoint = math.normalize(a.Unit + b.Unit);
                    var proof = MerkabaSphereFlowerAuthority.ClassifySector(line,
                        MerkabaSphereFlowerAuthority.Interval2.Singleton(midpoint),
                        out int classified);
                    Assert.That(proof,
                        Is.EqualTo(MerkabaSphereFlowerAuthority.ProofClassification.Certain),
                        $"line {line} sector {sector}");
                    Assert.That(classified, Is.EqualTo(sector));
                }
            }
            Assert.That(total, Is.EqualTo(132));
            Assert.That(MerkabaSphereFlowerAuthority.SectorBoundaries.Length,
                Is.EqualTo(total));
        }

        [Test]
        public void RootClassification_CoversEveryDegeneracy()
        {
            AssertRoot(new float3(1f, 0f, 0f),
                MerkabaSphereFlowerAuthority.RootClassification.Impossible);
            AssertRoot(float3.zero,
                MerkabaSphereFlowerAuthority.RootClassification.CoplanarLoop);
            AssertRoot(new float3(0f, 1f, 0f),
                MerkabaSphereFlowerAuthority.RootClassification.CertainSecant);
            AssertRoot(new float3(1f, 1f, 0f),
                MerkabaSphereFlowerAuthority.RootClassification.CertainTangent);
            AssertRoot(new float3(5f, 3f, 4f),
                MerkabaSphereFlowerAuthority.RootClassification.CertainTangent);

            var crossing = new MerkabaSphereFlowerAuthority.Interval3(
                new MerkabaSphereFlowerAuthority.FloatInterval(0.99f, 1.01f),
                MerkabaSphereFlowerAuthority.FloatInterval.Singleton(1f),
                MerkabaSphereFlowerAuthority.FloatInterval.Singleton(0f));
            Assert.That(MerkabaSphereFlowerAuthority.ClassifyRoots(crossing),
                Is.EqualTo(MerkabaSphereFlowerAuthority.RootClassification.Ambiguous));
        }

        [Test]
        public void OutwardIntervalArithmetic_ContainsExactDyadicResults()
        {
            var values = new[]
            {
                new MerkabaSphereFlowerAuthority.FloatInterval(-1.25f, -0.5f),
                new MerkabaSphereFlowerAuthority.FloatInterval(-0.25f, 0.75f),
                new MerkabaSphereFlowerAuthority.FloatInterval(0.125f, 2f)
            };
            foreach (var left in values)
            foreach (var right in values)
            {
                var sum = MerkabaSphereFlowerAuthority.FloatInterval.Add(left,
                    right);
                AssertContains(sum, (double)left.Lower + right.Lower);
                AssertContains(sum, (double)left.Upper + right.Upper);

                var difference = MerkabaSphereFlowerAuthority.FloatInterval
                    .Subtract(left, right);
                AssertContains(difference, (double)left.Lower - right.Upper);
                AssertContains(difference, (double)left.Upper - right.Lower);

                var product = MerkabaSphereFlowerAuthority.FloatInterval
                    .Multiply(left, right);
                AssertContains(product, (double)left.Lower * right.Lower);
                AssertContains(product, (double)left.Lower * right.Upper);
                AssertContains(product, (double)left.Upper * right.Lower);
                AssertContains(product, (double)left.Upper * right.Upper);
            }

            var square = MerkabaSphereFlowerAuthority.FloatInterval.Square(
                new MerkabaSphereFlowerAuthority.FloatInterval(-2f, 0.5f));
            AssertContains(square, 0.0);
            AssertContains(square, 4.0);
            var root = MerkabaSphereFlowerAuthority.FloatInterval.Sqrt(
                new MerkabaSphereFlowerAuthority.FloatInterval(0.25f, 2f));
            AssertContains(root, 0.5);
            AssertContains(root, Math.Sqrt(2.0));
        }

        [Test]
        public void AnalyticRoots_SatisfyLoopAndUnitEquations()
        {
            float3[] coefficients =
            {
                new(0f, 1f, 0f), new(0.25f, 0.75f, -0.5f),
                new(-0.4f, 0.2f, 0.9f), new(1f, 1f, 0f)
            };
            foreach (float3 abc in coefficients)
            foreach (bool plus in new[] { false, true })
            {
                Assert.That(MerkabaSphereFlowerAuthority.TryEvaluateRoot(abc,
                    plus, out float2 root), Is.True, abc.ToString());
                double residual = abc.x + abc.y * (double)root.x +
                    abc.z * (double)root.y;
                double unit = root.x * (double)root.x + root.y * (double)root.y;
                Assert.That(Math.Abs(residual), Is.LessThanOrEqualTo(4e-7));
                Assert.That(Math.Abs(unit - 1.0), Is.LessThanOrEqualTo(4e-7));
            }
        }

        [Test]
        public void SharedLoop_ProducesCompatibleAbcForOneWorldPlane()
        {
            float3[] normals =
            {
                new(1f, 0f, 0f),
                math.normalize(new float3(1f, 2f, -3f)),
                math.normalize(new float3(-7f, 5f, 2f))
            };
            int3 kernel = new(-33, 31, -257);
            for (int level = 0;
                 level < MerkabaSphereFlowerAuthority.GeometryLevelCount; level++)
            foreach (var line in MerkabaSphereFlowerAuthority.Lines)
            foreach (float3 normal in normals)
            {
                float step = MerkabaSphereFlowerAuthority.LevelStep(level);
                int3 neighbour = kernel + line.Direction;
                float3 firstCenter = step * (float3)kernel;
                float3 secondCenter = step * (float3)neighbour;
                float worldOffset = 0.37f;
                float firstOffset = worldOffset - math.dot(normal, firstCenter);
                float secondOffset = worldOffset - math.dot(normal, secondCenter);
                var junction = MerkabaSphereFlowerAuthority.JunctionAddress(
                    kernel, line.Direction);
                int lineClass = MerkabaSphereFlowerAuthority.FindLineClass(
                    line.Direction, out _);
                var loop = MerkabaSphereFlowerAuthority.EvaluateLoop(level,
                    junction, lineClass);
                var first = MerkabaSphereFlowerAuthority.RestrictPlaneToLoop(
                    firstCenter, normal, firstOffset, 0f, 0f, loop);
                var second = MerkabaSphereFlowerAuthority.RestrictPlaneToLoop(
                    secondCenter, normal, secondOffset, 0f, 0f, loop);
                AssertIntervalOverlap(first.X, second.X, "A");
                AssertIntervalOverlap(first.Y, second.Y, "B");
                AssertIntervalOverlap(first.Z, second.Z, "C");
                Assert.That(MerkabaSphereFlowerAuthority.ClassifyRoots(first),
                    Is.EqualTo(MerkabaSphereFlowerAuthority.ClassifyRoots(second)));
            }
        }

        [Test]
        public void ChildSubstitution_PreservesParentsAndClassifiesNewMidpoints()
        {
            foreach (var petal in MerkabaSphereFlowerAuthority.Petals)
            {
                int3 a = MerkabaSphereFlowerAuthority.Nodes[petal.FaceNode].Direction;
                int3 b = MerkabaSphereFlowerAuthority.Nodes[petal.EdgeNode].Direction;
                int3 c = MerkabaSphereFlowerAuthority.Nodes[petal.CornerNode].Direction;
                var expected = new HashSet<string>
                {
                    (2 * a).ToString(), (2 * b).ToString(), (2 * c).ToString(),
                    (a + b).ToString(), (b + c).ToString(), (c + a).ToString()
                };
                var actual = new HashSet<string>();
                foreach (var child in MerkabaSphereFlowerAuthority.ChildPetals)
                {
                    actual.Add(EvaluateBarycentric(child.Vertex0, a, b, c).ToString());
                    actual.Add(EvaluateBarycentric(child.Vertex1, a, b, c).ToString());
                    actual.Add(EvaluateBarycentric(child.Vertex2, a, b, c).ToString());
                }
                Assert.That(actual, Is.EquivalentTo(expected));
                Assert.That(Parity(a + b), Is.EqualTo(1));
                Assert.That(Parity(b + c), Is.EqualTo(1));
                Assert.That(Parity(c + a), Is.EqualTo(2));
            }
        }

        [Test]
        public void R2TauAndSynthesis_AreExactRationalRotationPair()
        {
            float2 predicted = math.normalize(new float2(0.8f, -0.6f));
            foreach (float turn in new[] { -0.25f, -0.0625f, 0f, 0.125f, 0.3f })
            {
                float2 observed = MerkabaSphereFlowerAuthority
                    .RotateTangentHalfAngle(predicted, turn);
                var proof = MerkabaSphereFlowerAuthority.TangentHalfAngle(
                    MerkabaSphereFlowerAuthority.Interval2.Singleton(predicted),
                    MerkabaSphereFlowerAuthority.Interval2.Singleton(observed),
                    out var recovered);
                Assert.That(proof,
                    Is.EqualTo(MerkabaSphereFlowerAuthority.ProofClassification.Certain));
                Assert.That(recovered.Lower, Is.LessThanOrEqualTo(turn));
                Assert.That(recovered.Upper, Is.GreaterThanOrEqualTo(turn));
                float2 synthesis = MerkabaSphereFlowerAuthority
                    .RotateTangentHalfAngle(predicted, recovered.Midpoint);
                AssertUlp(synthesis.x, observed.x, 8, "R2 synthesis x");
                AssertUlp(synthesis.y, observed.y, 8, "R2 synthesis y");
            }
        }

        [Test]
        public void R3TetraTransform_RoundTripsAndFramesHaveIncidenceChirality()
        {
            float4[] cases =
            {
                new(0f), new(1f, -2f, 3f, -4f),
                new(0.125f, 0.25f, -0.5f, 1f)
            };
            foreach (float4 input in cases)
            {
                float4 transformed = MerkabaSphereFlowerAuthority.TetraForward(input);
                float4 roundTrip = MerkabaSphereFlowerAuthority.TetraInverse(
                    transformed.x, transformed.yzw);
                for (int component = 0; component < 4; component++)
                    AssertUlp(roundTrip[component], input[component], 16,
                        $"R3 round trip component {component}");
            }

            for (int parity = 0;
                 parity < MerkabaSphereFlowerAuthority.TetraFrames.Length; parity++)
            {
                var frame = MerkabaSphereFlowerAuthority.TetraFrames[parity];
                int3[] vectors = new int3[4];
                for (int i = 0; i < 4; i++)
                    vectors[i] = MerkabaSphereFlowerAuthority.Lines[
                        frame.LineClasses[i]].Direction * frame.Eta[i];
                Assert.That(vectors[0] + vectors[1] + vectors[2] + vectors[3],
                    Is.EqualTo(int3.zero));
                int determinant = Determinant(vectors[1] - vectors[0],
                    vectors[2] - vectors[0], vectors[3] - vectors[0]);
                Assert.That(Math.Sign(determinant), Is.EqualTo(frame.Chirality));
                Assert.That(MerkabaSphereFlowerAuthority.BranchChirality(parity,
                    new int4(1, 1, 1, 1)), Is.EqualTo(frame.Chirality));
            }
        }

        [Test]
        public void Hinge_UsesHomogeneousUnitCircleIntersection()
        {
            var first = Singleton(new float3(0f, 0f, 1f));
            var second = Singleton(new float3(-1f, 1f, 0f));
            var proof = MerkabaSphereFlowerAuthority.ClassifyHinge(first,
                second, out var root);
            Assert.That(proof,
                Is.EqualTo(MerkabaSphereFlowerAuthority.ProofClassification.Certain));
            Assert.That(root.X, Is.EqualTo(
                MerkabaSphereFlowerAuthority.FloatInterval.Singleton(1f)));
            Assert.That(root.Y, Is.EqualTo(
                MerkabaSphereFlowerAuthority.FloatInterval.Singleton(0f)));
        }

        [Test]
        public void ParallelOwners_HaveDifferentExactJunctionRelations()
        {
            int3 direction = new(1, 0, 0);
            var first = MerkabaSphereFlowerAuthority.JunctionAddress(
                new int3(0, 0, 0), direction);
            var second = MerkabaSphereFlowerAuthority.JunctionAddress(
                new int3(0, 1, 0), direction);
            Assert.That(first, Is.Not.EqualTo(second));
        }

        [Test]
        public void SkinBubble_IsUnitAtCenterAndClampedOnEveryBoundary()
        {
            Assert.That(MerkabaSphereFlowerAuthority.SkinBubble(
                new float3(1f / 3f)), Is.EqualTo(1f).Within(2e-6f));
            for (int axis = 0; axis < 3; axis++)
            {
                float3 boundary = axis == 0
                    ? new float3(0f, 0.25f, 0.75f)
                    : axis == 1
                        ? new float3(0.25f, 0f, 0.75f)
                        : new float3(0.25f, 0.75f, 0f);
                Assert.That(MerkabaSphereFlowerAuthority.SkinBubble(boundary),
                    Is.Zero);
                Assert.That(MerkabaSphereFlowerAuthority
                    .SkinBubbleGradient(boundary), Is.EqualTo(float3.zero));
            }

            float3 child3 = new(1f / 3f);
            float3 child4 = new(0.2f, 0.3f, 0.5f);
            float3 child5 = new(0.1f, 0.4f, 0.5f);
            float3 amplitude = new(0.003f, -0.001f, 0.00025f);
            float expected = amplitude.x *
                MerkabaSphereFlowerAuthority.SkinBubble(child3) +
                amplitude.y * MerkabaSphereFlowerAuthority.SkinBubble(child4) +
                amplitude.z * MerkabaSphereFlowerAuthority.SkinBubble(child5);
            Assert.That(MerkabaSphereFlowerAuthority.EvaluateNestedSkinV(
                child3, child4, child5, amplitude), Is.EqualTo(expected));
        }

        [Test]
        public void GeneratedHlsl_ContainsOnlyTheFrozenOracleAlphabet()
        {
            string path = Path.GetFullPath(
                "Packages/com.genesis.roomscan/Runtime/Shaders/" +
                "MerkabaSphereFlower.generated.hlsl");
            string source = File.ReadAllText(path);
            StringAssert.Contains("#define M8_FLOWER_SECTOR_BOUNDARY_COUNT 132u",
                source);
            StringAssert.Contains("static const uint4 M8FlowerStrand[72]", source);
            StringAssert.Contains("static const uint4 M8FlowerPetalNodes[48]", source);
            StringAssert.Contains("M8FlowerClassifyRoot", source);
            StringAssert.Contains("static const int4 M8FlowerSkinChamber[36]",
                source);
            StringAssert.Contains("M8FlowerSkinCanonicalToThread[399]", source);
            StringAssert.Contains("M8FlowerSkinCanonicalL5ToThread[343]", source);
            StringAssert.Contains("M8FlowerNestedV", source);
            StringAssert.DoesNotContain("M8FlowerDeformedLoopUnit", source);
            StringAssert.DoesNotContain("M8FlowerEndpointBasis", source);
            StringAssert.DoesNotContain("atan", source);
            StringAssert.DoesNotContain("acos", source);
            StringAssert.DoesNotContain("epsilon", source.ToLowerInvariant());
        }

        private static void AssertRoot(float3 abc,
            MerkabaSphereFlowerAuthority.RootClassification expected) =>
            Assert.That(MerkabaSphereFlowerAuthority.ClassifyRoots(Singleton(abc)),
                Is.EqualTo(expected));

        private static MerkabaSphereFlowerAuthority.Interval3 Singleton(float3 v) =>
            MerkabaSphereFlowerAuthority.Interval3.Singleton(v);

        private static int3 EvaluateBarycentric(byte packed, int3 a, int3 b,
            int3 c)
        {
            MerkabaSphereFlowerAuthority.DecodeBarycentric(packed,
                out int wa, out int wb, out int wc);
            return wa * a + wb * b + wc * c;
        }

        private static bool PetalContainsStrand(int petal, int strand)
        {
            var value = MerkabaSphereFlowerAuthority.Petals[petal];
            return value.Strand0 == strand || value.Strand1 == strand ||
                   value.Strand2 == strand;
        }

        private static int Parity(int3 value) =>
            (value.x & 1) + (value.y & 1) + (value.z & 1);

        private static float Cross(float2 a, float2 b) =>
            a.x * b.y - a.y * b.x;

        private static void AssertContains(
            MerkabaSphereFlowerAuthority.FloatInterval interval,
            double value)
        {
            Assert.That((double)interval.Lower, Is.LessThanOrEqualTo(value));
            Assert.That((double)interval.Upper, Is.GreaterThanOrEqualTo(value));
        }

        private static void AssertIntervalOverlap(
            MerkabaSphereFlowerAuthority.FloatInterval left,
            MerkabaSphereFlowerAuthority.FloatInterval right,
            string context) => Assert.That(
            MerkabaSphereFlowerAuthority.FloatInterval.TryIntersect(left,
                right, out _), Is.True, context);

        private static void AssertRoundoff(double actual, double expected,
            int operations, double absoluteOperandSum, string context)
        {
            const double unitRoundoff = 1.0 / 16777216.0;
            double product = operations * unitRoundoff;
            double bound = product / (1.0 - product) * absoluteOperandSum;
            Assert.That(Math.Abs(actual - expected), Is.LessThanOrEqualTo(bound),
                context);
        }

        private static void AssertUlp(float actual, float expected,
            int maximumOperationUlps, string context)
        {
            int actualBits = OrderedBits(actual);
            int expectedBits = OrderedBits(expected);
            long distance = Math.Abs((long)actualBits - expectedBits);
            Assert.That(distance, Is.LessThanOrEqualTo(maximumOperationUlps),
                context);
        }

        private static int OrderedBits(float value)
        {
            int bits = BitConverter.SingleToInt32Bits(value);
            return bits < 0 ? int.MinValue - bits : bits;
        }

        private static int Determinant(int3 a, int3 b, int3 c) =>
            a.x * (b.y * c.z - b.z * c.y) -
            a.y * (b.x * c.z - b.z * c.x) +
            a.z * (b.x * c.y - b.y * c.x);
    }
}
