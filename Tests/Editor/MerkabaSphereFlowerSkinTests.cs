using System;
using System.Collections.Generic;
using Genesis.RoomScan;
using NUnit.Framework;
using Unity.Mathematics;

namespace Genesis.RoomScan.Tests
{
    public sealed class MerkabaSphereFlowerSkinTests
    {
        [Test]
        public void FixedThread_IsBijectiveAndEverySubtreeIsContiguous()
        {
            Assert.That(MerkabaSphereFlowerAuthority.SkinL3Count, Is.EqualTo(7));
            Assert.That(MerkabaSphereFlowerAuthority.SkinL4Count, Is.EqualTo(49));
            Assert.That(MerkabaSphereFlowerAuthority.SkinL5Count, Is.EqualTo(343));
            Assert.That(MerkabaSphereFlowerAuthority.SkinThreadPositionCount,
                Is.EqualTo(399));
            Assert.That(MerkabaSphereFlowerAuthority.SkinSplitBitCount,
                Is.EqualTo(57));

            var seen = new bool[399];
            for (int canonical = 0; canonical < seen.Length; canonical++)
            {
                int thread = MerkabaSphereFlowerAuthority
                    .SkinCanonicalToThread[canonical];
                Assert.That(thread, Is.InRange(0, 398));
                Assert.That(seen[thread], Is.False, $"thread {thread}");
                seen[thread] = true;
                Assert.That(MerkabaSphereFlowerAuthority
                    .SkinThreadToCanonical[thread], Is.EqualTo(canonical));
            }

            for (int c3 = 0; c3 < 7; c3++)
            {
                int t3 = MerkabaSphereFlowerAuthority.SkinThreadPosition(3, c3);
                int r3 = MerkabaSphereFlowerAuthority
                    .SkinL3ParentThreadIndex(c3);
                Assert.That(t3, Is.EqualTo(57 * r3));
                var subtree = new HashSet<int> { t3 };
                for (int c4 = 0; c4 < 7; c4++)
                {
                    int t4 = MerkabaSphereFlowerAuthority
                        .SkinThreadPosition(4, c3, c4);
                    int r4 = MerkabaSphereFlowerAuthority
                        .SkinL4CompactChildRank(c3, c4);
                    Assert.That(t4, Is.EqualTo(t3 + 1 + 8 * r4));
                    var child = new HashSet<int> { t4 };
                    for (int c5 = 0; c5 < 7; c5++)
                    {
                        int t5 = MerkabaSphereFlowerAuthority
                            .SkinThreadPosition(5, c3, c4, c5);
                        Assert.That(t5, Is.EqualTo(t4 + 1 +
                            MerkabaSphereFlowerAuthority
                                .SkinL5CompactChildRank(c3, c4, c5)));
                        child.Add(t5);
                        subtree.Add(t5);
                        int terminal = 49 * c3 + 7 * c4 + c5;
                        Assert.That(MerkabaSphereFlowerAuthority
                            .SkinCanonicalL5ToThread[terminal], Is.EqualTo(t5));
                    }
                    Assert.That(child, Is.EquivalentTo(Range(t4, 8)));
                    subtree.Add(t4);
                }
                Assert.That(subtree, Is.EquivalentTo(Range(t3, 57)));
            }
            Assert.That(seen, Has.All.True);
        }

        [Test]
        public void FibonacciUsesParentThreadOrdinalAndLocalPathsStayAdjacent()
        {
            AssertLocalOrder(0, 0,
                c => MerkabaSphereFlowerAuthority.SkinL3ChildRank[c]);
            for (int c3 = 0; c3 < 7; c3++)
            {
                int t3 = MerkabaSphereFlowerAuthority.SkinThreadPosition(3, c3);
                int state3 = MerkabaSphereFlowerAuthority.SkinL3State[c3];
                AssertLocalOrder(state3, t3,
                    c4 => MerkabaSphereFlowerAuthority
                        .SkinL4CompactChildRank(c3, c4));
                for (int c4 = 0; c4 < 7; c4++)
                {
                    int t4 = MerkabaSphereFlowerAuthority
                        .SkinThreadPosition(4, c3, c4);
                    int state4 = MerkabaSphereFlowerAuthority
                        .SkinL4State[7 * c3 + c4];
                    AssertLocalOrder(state4, t4,
                        c5 => MerkabaSphereFlowerAuthority
                            .SkinL5CompactChildRank(c3, c4, c5));
                }
            }
        }

        [Test]
        public void OrderedChambersExactlyPartitionFlower7FootprintsAtAllDepths()
        {
            int[] footprintChambers = new int[7];
            for (int wedge = 0; wedge < 6; wedge++)
            {
                var orders = new HashSet<int>();
                int3 sites = WedgeSites(wedge);
                for (int order = 0; order < 6; order++)
                {
                    var rule = MerkabaSphereFlowerAuthority
                        .SkinChambers[6 * wedge + order];
                    Assert.That(new[] { (int)rule.High, rule.Middle, rule.Low },
                        Is.EquivalentTo(new[] { 0, 1, 2 }));
                    int encoded = 9 * rule.High + 3 * rule.Middle + rule.Low;
                    Assert.That(orders.Add(encoded), Is.True);
                    Assert.That(rule.ChildSite, Is.EqualTo(sites[rule.High]));
                    Assert.That(rule.ChildSite, Is.InRange(0, 6));
                    Assert.That(rule.ChildWedge, Is.InRange(0, 5));
                    Assert.That(rule.NextStitchState, Is.InRange(0, 11));
                    Assert.That(rule.Orientation, Is.EqualTo(1).Or.EqualTo(-1));
                    footprintChambers[rule.ChildSite]++;

                    float3 bc = default;
                    bc[rule.High] = 0.6f;
                    bc[rule.Middle] = 0.3f;
                    bc[rule.Low] = 0.1f;
                    var descended = MerkabaSphereFlowerAuthority.DescendSkin(
                        bc, wedge, out float3 child);
                    Assert.That(descended.ChildSite, Is.EqualTo(rule.ChildSite));
                    Assert.That(math.cmin(child), Is.GreaterThanOrEqualTo(0f));
                    Assert.That(math.csum(child), Is.EqualTo(1f).Within(2e-7f));
                }
                Assert.That(orders.Count, Is.EqualTo(6));
            }
            Assert.That(footprintChambers[0], Is.EqualTo(12));
            for (int ring = 1; ring < 7; ring++)
                Assert.That(footprintChambers[ring], Is.EqualTo(4));

            var reachableL4 = new HashSet<int>();
            var reachableL5 = new HashSet<int>();
            for (int wedge = 0; wedge < 6; wedge++)
            for (int order3 = 0; order3 < 6; order3++)
            for (int order4 = 0; order4 < 6; order4++)
            for (int order5 = 0; order5 < 6; order5++)
            {
                var expected3 = MerkabaSphereFlowerAuthority
                    .SkinChambers[6 * wedge + order3];
                var expected4 = MerkabaSphereFlowerAuthority
                    .SkinChambers[6 * expected3.ChildWedge + order4];
                var expected5 = MerkabaSphereFlowerAuthority
                    .SkinChambers[6 * expected4.ChildWedge + order5];
                reachableL4.Add(7 * expected3.ChildSite + expected4.ChildSite);
                reachableL5.Add(49 * expected3.ChildSite +
                    7 * expected4.ChildSite + expected5.ChildSite);
                float3 bc5 = OrderedPoint(expected5);
                float3 bc4 = InvertDescent(expected5, bc5);
                float3 bc3 = InvertDescent(expected4, bc4);
                float3 bc = InvertDescent(expected3, bc3);
                var r3 = MerkabaSphereFlowerAuthority.DescendSkin(
                    bc, wedge, out float3 actual3);
                var r4 = MerkabaSphereFlowerAuthority.DescendSkin(
                    actual3, r3.ChildWedge, out float3 actual4);
                var r5 = MerkabaSphereFlowerAuthority.DescendSkin(
                    actual4, r4.ChildWedge, out float3 actual5);
                Assert.That(r3.ChildSite, Is.EqualTo(expected3.ChildSite));
                Assert.That(r4.ChildSite, Is.EqualTo(expected4.ChildSite));
                Assert.That(r5.ChildSite, Is.EqualTo(expected5.ChildSite));
                Assert.That(math.cmax(math.abs(actual3 - bc3)),
                    Is.LessThanOrEqualTo(3e-6f));
                Assert.That(math.cmax(math.abs(actual4 - bc4)),
                    Is.LessThanOrEqualTo(3e-6f));
                Assert.That(math.cmax(math.abs(actual5 - bc5)),
                    Is.LessThanOrEqualTo(3e-6f));
            }
            // A successful round trip through the SAME transition table
            // alone does not prove that it represents all Flower children.
            // Every chamber is affinely onto its child simplex, so this
            // finite enumeration covers every nonempty terminal interior.
            Assert.That(new[] { reachableL4.Count, reachableL5.Count },
                Is.EqualTo(new[] { 49, 343 }),
                "The fixed thread must not contain unreachable L4/L5 locality addresses.");
        }

        [Test]
        public void ScanSplitPredicateIsIntervalExactAndMasksAreThreadOrdered()
        {
            var scalar = new MerkabaSphereFlowerAuthority.FloatInterval[7];
            for (int i = 0; i < scalar.Length; i++)
                scalar[i] = new MerkabaSphereFlowerAuthority.FloatInterval(0f, 1f);
            Assert.That(MerkabaSphereFlowerAuthority.ClassifyScalarSkinSplit(
                scalar, 0x7f), Is.EqualTo(
                MerkabaSphereFlowerAuthority.SplitClassification.Uniform));
            Assert.That(MerkabaSphereFlowerAuthority.ClassifyScalarSkinSplit(
                scalar, 0x3f), Is.EqualTo(
                MerkabaSphereFlowerAuthority.SplitClassification.Ambiguous));
            scalar[6] = new MerkabaSphereFlowerAuthority.FloatInterval(2f, 3f);
            Assert.That(MerkabaSphereFlowerAuthority.ClassifyScalarSkinSplit(
                scalar, 0x7f), Is.EqualTo(
                MerkabaSphereFlowerAuthority.SplitClassification.Split));

            var rgb = new MerkabaSphereFlowerAuthority.FloatInterval[21];
            for (int i = 0; i < rgb.Length; i++)
                rgb[i] = new MerkabaSphereFlowerAuthority.FloatInterval(0f, 1f);
            rgb[3 * 5 + 1] = new MerkabaSphereFlowerAuthority.FloatInterval(2f, 3f);
            Assert.That(MerkabaSphereFlowerAuthority.ClassifyRgbSkinSplit(
                rgb, 0x7f), Is.EqualTo(
                MerkabaSphereFlowerAuthority.SplitClassification.Split));

            uint[] masks = { 0u, 1u, 0x7fu, 0xa5a5a5a5u, 0xffffffffu };
            foreach (uint low in masks)
            foreach (uint high in masks)
            for (int bit = 0; bit <= 49; bit++)
                Assert.That(MerkabaSphereFlowerAuthority.Rank49(low, high, bit),
                    Is.EqualTo(ManualRank49(low, high, bit)));
            for (int bit = 0; bit <= 7; bit++)
                Assert.That(MerkabaSphereFlowerAuthority.Rank7(0x6du, bit),
                    Is.EqualTo(ManualRank49(0x6du, 0u, bit)));

            uint splitL3 = 1u << 2;
            uint splitL4Low = (1u << 14) | (1u << 20);
            Assert.That(MerkabaSphereFlowerAuthority.ValidateSkinSplitClosure(
                true, splitL3, splitL4Low, 0u), Is.True);
            Assert.That(MerkabaSphereFlowerAuthority.CompactSkinValueCount(
                true, splitL3, splitL4Low, 0u), Is.EqualTo(29));
            Assert.That(MerkabaSphereFlowerAuthority.ValidateSkinSplitClosure(
                false, splitL3, 0u, 0u), Is.False);
            Assert.That(MerkabaSphereFlowerAuthority.ValidateSkinSplitClosure(
                true, splitL3, 1u << 7, 0u), Is.False);
        }

        private static void AssertLocalOrder(int state, int parentThread,
            Func<int, int> actualRank)
        {
            int variant = MerkabaSphereFlowerAuthority
                .FibonacciBitForCodegen(parentThread);
            int[] source = variant == 0
                ? new[] { 1, 0, 2, 3, 4, 5, 6 }
                : new[] { 1, 2, 3, 4, 5, 0, 6 };
            int[] expected = new int[7];
            for (int rank = 0; rank < 7; rank++)
            {
                int site = MapSite(state, source[rank]);
                expected[site] = rank;
                if (rank > 0)
                    Assert.That(MerkabaSphereFlowerAuthority.SkinSitesAdjacent(
                        MapSite(state, source[rank - 1]), site), Is.True);
            }
            for (int site = 0; site < 7; site++)
                Assert.That(actualRank(site), Is.EqualTo(expected[site]));
        }

        private static int MapSite(int state, int site)
        {
            if (site == 0) return 0;
            int rotation = state >> 1;
            int ring = site - 1;
            int mapped = rotation + ((state & 1) != 0 ? -ring : ring);
            mapped %= 6;
            if (mapped < 0) mapped += 6;
            return mapped + 1;
        }

        private static int3 WedgeSites(int wedge) =>
            new(0, wedge + 1, ((wedge + 1) % 6) + 1);

        private static float3 OrderedPoint(
            MerkabaSphereFlowerAuthority.SkinChamberRule rule)
        {
            float3 result = default;
            result[rule.High] = 0.6f;
            result[rule.Middle] = 0.3f;
            result[rule.Low] = 0.1f;
            return result;
        }

        private static float3 InvertDescent(
            MerkabaSphereFlowerAuthority.SkinChamberRule rule, float3 child)
        {
            float3 result = default;
            result[rule.Low] = child.z / 3f;
            result[rule.Middle] = child.y / 2f + result[rule.Low];
            result[rule.High] = child.x + result[rule.Middle];
            return result;
        }

        private static IEnumerable<int> Range(int start, int count)
        {
            for (int i = 0; i < count; i++) yield return start + i;
        }

        private static int ManualRank49(uint low, uint high, int bit)
        {
            int count = 0;
            for (int i = 0; i < bit; i++)
            {
                uint word = i < 32 ? low : high;
                int shift = i < 32 ? i : i - 32;
                count += (int)((word >> shift) & 1u);
            }
            return count;
        }
    }
}
