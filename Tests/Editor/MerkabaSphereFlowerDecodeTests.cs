using NUnit.Framework;
using Unity.Mathematics;
using A = Genesis.RoomScan.MerkabaSphereFlowerAuthority;

namespace Genesis.RoomScan.Tests
{
    public sealed class MerkabaSphereFlowerDecodeTests
    {
        [Test]
        public void ChildLoopLookup_IsExhaustivelyIdenticalToIncidenceOracle()
        {
            for (int petal = 0; petal < A.PetalClassCount; petal++)
            for (int parent = 0; parent < 5; parent++)
            for (int site = 0; site < 6; site++)
            {
                Assert.That(A.ComputeChildPhaseLoop(petal, parent, site, out int level,
                    out int3 offset, out int line, out int strand, out sbyte endpoint,
                    out sbyte phase, out int inherited), Is.True);
                Assert.That(A.TryGetChildPhaseLoop(petal, parent, site, out int actualLevel,
                    out int3 actualOffset, out int actualLine, out int actualStrand, out sbyte actualEndpoint,
                    out sbyte actualPhase, out int actualInherited), Is.True);
                Assert.That((actualLevel, actualLine, actualStrand, actualEndpoint, actualPhase, actualInherited),
                    Is.EqualTo((level, line, strand, endpoint, phase, inherited)));
                Assert.That(math.all(actualOffset == offset), Is.True);
                uint creation = A.ChildLoopCreations[(petal * 5 + parent) * 6 + site];
                Assert.That((creation & 0x80000000u) != 0u, Is.EqualTo(level == 0 || inherited < 0));
            }
        }

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
        public void CarrierSourceNodes_AreExactGeneratedAnchorUnion()
        {
            for (int carrier = 0; carrier < A.L2HubCount; carrier++)
            {
                uint expected = 0u;
                for (int wedge = 0; wedge < 6; wedge++)
                {
                    var source = A.Petals[A.L2Wedges[6 * carrier + wedge].Petal];
                    for (int anchor = 0; anchor < 3; anchor++)
                        expected |= 1u << source.Node(anchor);
                }
                Assert.That(A.DecodeCarrierPetals[carrier].z, Is.EqualTo(expected));
            }
        }

        [Test]
        public void ShellReachedDecode_DoesNotOmitAnySurvivingSource()
        {
            Check(0u); Check(0x03ffffffu);
            for (int node = 0; node < 26; node++)
            {
                Check(1u << node); Check(0x03ffffffu ^ (1u << node));
            }
            foreach (var source in A.Petals)
                for (int subset = 0; subset < 8; subset++)
                {
                    uint absent = 0u;
                    for (int anchor = 0; anchor < 3; anchor++)
                        if ((subset & (1 << anchor)) != 0) absent |= 1u << source.Node(anchor);
                    Check(absent);
                }

            static void Check(uint absent)
            {
                uint knownAbsent = absent & 63u;
                for (int shell = 1; shell < 3; shell++)
                {
                    uint reachedNodes = 0u;
                    uint4 pending = A.M8FlowerReachedCarriers(knownAbsent);
                    while (A.TakeReachedCarrier(ref pending, out int carrier))
                        reachedNodes |= A.DecodeCarrierPetals[carrier].z;
                    knownAbsent |= absent & reachedNodes & (shell == 1 ? 0x0003ffc0u : 0x03fc0000u);
                }
                Assert.That(math.all(A.M8FlowerReachedCarriers(knownAbsent) ==
                    A.M8FlowerReachedCarriers(absent)), Is.True, $"Absent: 0x{absent:x8}");
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
