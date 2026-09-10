using NUnit.Framework;
using Unity.Mathematics;
using A = Genesis.RoomScan.MerkabaSphereFlowerAuthority;

namespace Genesis.RoomScan.Tests
{
    public sealed class MerkabaSphereFlowerDecodeTests
    {
        [Test]
        public void ReachedCarriers_AgreeWithIndependentSourceIncidence()
        {
            Check(0u);
            Check((1u << 26) - 1u);
            for (int node = 0; node < 26; node++)
            {
                Check(1u << node);
                Check(((1u << 26) - 1u) ^ (1u << node));
            }
            // Every presence/absence combination of every parent flag's
            // three anchors, including shared anchors of adjacent petals.
            foreach (var petal in A.Petals)
                for (int subset = 0; subset < 8; subset++)
                {
                    uint absent = 0u;
                    for (int anchor = 0; anchor < 3; anchor++)
                        if ((subset & (1 << anchor)) != 0) absent |= 1u << petal.Node(anchor);
                    Check(absent);
                }

            static void Check(uint absent)
            {
                uint4 expected = 0u;
                // Test oracle only: enumerate sources, not the production
                // inverse tables or the reached-mask implementation.
                for (int wedge = 0; wedge < A.L2WedgeCount; wedge++)
                {
                    var petal = A.Petals[A.L2Wedges[wedge].Petal];
                    uint needed = (1u << petal.Node(0)) | (1u << petal.Node(1)) | (1u << petal.Node(2));
                    if ((needed & absent) != 0u) continue;
                    int carrier = wedge / 6;
                    expected[carrier >> 5] |= 1u << (carrier & 31);
                }
                Assert.That(math.all(A.M8FlowerReachedCarriers(absent) == expected), Is.True,
                    $"Absent anchors: 0x{absent:x8}");
            }
        }

        [Test]
        public void CarrierTripleInverse_CoversExactlyEachSevenSiteAssignment()
        {
            for (int wedge = 0; wedge < 6; wedge++)
                for (uint signs = 0; signs < 128; signs++)
                    for (uint triple = 0; triple < 8; triple++)
                    {
                        uint4 mask = A.CarrierTripleMasks[8 * wedge + (int)triple];
                        bool contained = (mask[(int)(signs >> 5)] & (1u << (int)(signs & 31))) != 0u;
                        Assert.That(contained, Is.EqualTo(A.CarrierTriple(signs, wedge) == triple));
                    }
        }
    }
}
