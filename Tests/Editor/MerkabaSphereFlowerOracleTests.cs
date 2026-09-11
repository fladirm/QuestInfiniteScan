using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Genesis.RoomScan;
using NUnit.Framework;
using Unity.Mathematics;
using BigInteger = System.Numerics.BigInteger;
using Flower = Genesis.RoomScan.MerkabaSphereFlowerAuthority;

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
        public void PlaneDecode_AllOctCodeCentresPreserveExactSymmetriesAndGammaBound()
        {
            const int denominator = 1023;
            const double unitRoundoff = 1.0 / (1 << 24);
            const double doubleRoundoff = 1.0 / 9007199254740992.0;
            double gamma2 = 2.0 * unitRoundoff / (1.0 - 2.0 * unitRoundoff);
            double gamma3 = 3.0 * unitRoundoff / (1.0 - 3.0 * unitRoundoff);
            double referenceGamma2 = 2.0 * doubleRoundoff / (1.0 - 2.0 * doubleRoundoff);
            double comparisonGamma5 = 5.0 * doubleRoundoff / (1.0 - 5.0 * doubleRoundoff);
            // Integer S is exact. Only RNE sqrt(S), then RNE(component/root),
            // contribute FP32 error: ||decoded-exact|| <= gamma2. The direct
            // double reference and its squared-norm comparison fit strictly
            // inside gamma3; this margin is derived, not an epsilon.
            double comparisonBound = (gamma2 + referenceGamma2) * (gamma2 + referenceGamma2) *
                (1.0 + comparisonGamma5);
            Assert.That(comparisonBound, Is.LessThan(gamma3 * gamma3));
            double largestSquaredError = 0.0;
            for (int u = 0; u <= denominator; u++)
            for (int v = 0; v <= denominator; v++)
            {
                int x = 2 * u - denominator, y = 2 * v - denominator;
                int z = denominator - Math.Abs(x) - Math.Abs(y);
                if (z < 0)
                {
                    int oldX = x;
                    x = Math.Sign(x) * (denominator - Math.Abs(y));
                    y = Math.Sign(y) * (denominator - Math.Abs(oldX));
                }
                int square = x * x + y * y + z * z;
                float3 integers = new(x, y, z);
                float3 products = integers * integers;
                float sumXY = products.x + products.y;
                float fpSquare = sumXY + products.z;
                if (Math.Abs(x) + Math.Abs(y) + Math.Abs(z) != denominator ||
                    square <= 0 || square > denominator * denominator || fpSquare != square ||
                    products.x != x * x || products.y != y * y || products.z != z * z)
                    Assert.Fail($"Non-exact integer oct frame u={u},v={v},n=({x},{y},{z}),S={square},fpS={fpSquare:R}");
                uint flags = MerkabaConstants.SurfacePlaneValidFlag | MerkabaConstants.OccupiedFlag |
                    ((uint)u << MerkabaConstants.SurfacePlaneNormalUShift) |
                    ((uint)v << MerkabaConstants.SurfacePlaneNormalVShift);
                Flower.M8FlowerUnpackPlane(flags, out float3 decoded, out float offset);
                if (math.asuint(offset) != 0u || !math.all(math.isfinite(decoded)))
                    Assert.Fail($"Invalid oct decode u={u},v={v}: n={decoded},offset={offset:R}");
                for (int first = 0; first < 3; first++)
                for (int second = first + 1; second < 3; second++)
                {
                    float a = integers[first], b = integers[second];
                    uint da = math.asuint(decoded[first]), db = math.asuint(decoded[second]);
                    if ((a == b && da != db) || (a != 0f && a == -b && da != (db ^ 0x80000000u)))
                        Assert.Fail($"Broken exact oct symmetry u={u},v={v},axes={first}/{second},bits={da:x8}/{db:x8}");
                }
                double length = Math.Sqrt(square);
                double dx = decoded.x - x / length, dy = decoded.y - y / length, dz = decoded.z - z / length;
                double squaredError = (dx * dx + dy * dy) + dz * dz;
                if (!(squaredError <= gamma3 * gamma3))
                    Assert.Fail($"Oct norm bound u={u},v={v},n=({x},{y},{z}),error2={squaredError:R},gamma3={gamma3:R}");
                largestSquaredError = Math.Max(largestSquaredError, squaredError);
            }
            TestContext.WriteLine($"All 1024^2 code centres; max norm error={Math.Sqrt(largestSquaredError):R},gamma3={gamma3:R}");

            foreach (int offsetCode in new[] { -127, -1, 0, 1, 127 })
            for (int freeSide = 0; freeSide < 2; freeSide++)
            {
                uint flags = MerkabaConstants.SurfacePlaneValidFlag | MerkabaConstants.OccupiedFlag |
                    (682u << MerkabaConstants.SurfacePlaneNormalUShift) |
                    (682u << MerkabaConstants.SurfacePlaneNormalVShift) |
                    (((uint)offsetCode & 255u) << MerkabaConstants.SurfacePlaneOffsetShift) |
                    (freeSide != 0 ? MerkabaConstants.SurfacePlaneFreeSideFlag : 0u);
                uint unchanged = flags;
                Flower.M8FlowerUnpackPlane(flags, out float3 decoded, out float offset);
                Assert.That(math.asuint(decoded.x), Is.EqualTo(math.asuint(decoded.y)));
                Assert.That(math.asuint(decoded.y), Is.EqualTo(math.asuint(decoded.z)), "u=v=682 encodes exact (1,1,1).");
                float expectedOffset = (float)(offsetCode / 127.0) * MerkabaConstants.SurfacePlaneOffsetRange;
                Assert.That(math.asuint(offset), Is.EqualTo(math.asuint(expectedOffset)));
                Assert.That(flags, Is.EqualTo(unchanged));
                Assert.That(Flower.M8FlowerPlaneFreeSide(flags), Is.EqualTo(freeSide == 0 ? 1 : -1));
            }
        }

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
        public void CubeSymmetry_ClosesAllTablesAndOneCompleteFlagOrbit()
        {
            Assert.That(MerkabaSphereFlowerAuthority.ValidateCubeSymmetry(), Is.EqualTo(48));
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
                    MerkabaSphereFlowerAuthority.Shell.R1Core => 24,
                    MerkabaSphereFlowerAuthority.Shell.R2Shape => 12,
                    MerkabaSphereFlowerAuthority.Shell.R3Closure => 30,
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
            Assert.That(total, Is.EqualTo(264));
            Assert.That(MerkabaSphereFlowerAuthority.SectorBoundaries.Length,
                Is.EqualTo(total));
        }

        [Test]
        public void ExactBoundaryRootReferences_UseIncidenceWithoutChangingHalfOpenOwner()
        {
            int nonOwnerReferences = 0;
            for (int node = 0; node < Flower.NodeClassCount; node++)
            {
                var anchor = Flower.Nodes[node];
                var line = Flower.Lines[anchor.LineClass];
                for (int boundary = 0; boundary < line.SectorCount; boundary++)
                {
                    ulong expected = IndependentNodeIncidence(node);
                    ulong closedPower = IndependentBoundaryClosedPowerMask(node, boundary);
                    for (int sign = 0; sign < 2; sign++)
                    {
                        uint tag = BoundaryReferenceTag(node, boundary, sign != 0);
                        string context = $"node/boundary/sign={node}/{boundary}/{sign}";
                        Assert.That(Flower.TryGetAnchorRootFlags(node, tag, out ulong owner), Is.True, context);
                        Assert.That(Flower.TryGetAnchorRootReferences(node, tag, out ulong references), Is.True, context);
                        Assert.That(references, Is.EqualTo(expected), context);
                        Assert.That(owner & ~closedPower, Is.Zero, "Half-open ownership must still obey exact max-power ties.");
                        Assert.That(closedPower & ~references, Is.Zero);
                        Assert.That(owner & ~references, Is.Zero, context);
                        Assert.That(Flower.TryGetAnchorRootFlags(node, tag, out ulong after), Is.True);
                        Assert.That(after, Is.EqualTo(owner), "Reference lookup must not broaden ownership.");
                        for (int face = 0; face < 6; face++)
                        {
                            ulong faceMask = 0;
                            for (int petal = 0; petal < Flower.PetalClassCount; petal++)
                                if (Flower.Petals[petal].FaceNode == face) faceMask |= 1UL << petal;
                            ulong selected = owner & faceMask;
                            Assert.That(selected == 0 || (selected & (selected - 1)) == 0, Is.True,
                                $"{context}: one half-open owner per directed face");
                        }
                        if (anchor.Shell == Flower.Shell.R1Core) Assert.That(owner, Is.Not.Zero, context);
                        if ((references & ~owner) != 0) nonOwnerReferences++;

                        uint numericOnly = tag & ~Flower.BoundaryWitnessMask;
                        Assert.That(Flower.TryGetAnchorRootReferences(node, numericOnly, out ulong numericReferences), Is.True);
                        Assert.That(numericReferences, Is.EqualTo(expected),
                            "Incidence does not certify the supplied numeric root or manufacture a boundary witness.");
                        var cut = Flower.SectorBoundaries[line.SectorOffset + boundary].Enclosure;
                        var evidence = new Flower.PhaseRootEvidence(new MerkabaFlowerSymbolKey
                        { JunctionX = -514 + anchor.Direction.x, JunctionY = -66 + anchor.Direction.y,
                            JunctionZ = -18 + anchor.Direction.z, Tag = numericOnly }, cut, Flower.ProofClassification.Certain);
                        Assert.That(Flower.ClassifyPhaseSector(evidence, out _), Is.EqualTo(Flower.ProofClassification.Ambiguous),
                            $"{context}: numerical contact is not an exact symbolic witness");
                        uint wrongOwner = (tag & ~(31u << 8)) |
                            ((uint)((Flower.BoundaryRules[line.SectorOffset + boundary].Owner + 1) % line.SectorCount) << 8);
                        Assert.That(Flower.TryGetAnchorRootReferences(node, wrongOwner, out _), Is.False);
                        uint wrongLine = (tag & ~(15u << 3)) | ((uint)((anchor.LineClass + 1) % Flower.LineClassCount) << 3);
                        Assert.That(Flower.TryGetAnchorRootReferences(node, wrongLine, out _), Is.False);
                        uint wrongCode = numericOnly | ((uint)(line.SectorCount + 1) << 21);
                        Assert.That(Flower.TryGetAnchorRootReferences(node, wrongCode, out _), Is.False);
                    }
                }
            }
            Assert.That(nonOwnerReferences, Is.GreaterThan(0), "The fixture must exercise real shared-boundary references.");
        }

        [Test]
        public void StrictSectorRootReferences_AreAllIncidentPetalsForBothSignsAndEveryDirectedNode()
        {
            ulong nonOwnerUses = 0;
            for (int node = 0; node < Flower.NodeClassCount; node++)
            {
                ulong incidence = IndependentNodeIncidence(node);
                Assert.That(Flower.NodeIncidentPetals[node], Is.EqualTo(incidence));
                var anchor = Flower.Nodes[node];
                var line = Flower.Lines[anchor.LineClass];
                for (int sector = 0; sector < line.SectorCount; sector++)
                for (int sign = 0; sign < 2; sign++)
                {
                    Flower.PhaseRootEvidence root = StrictAnchorEvidence(node, sector, sign != 0);
                    uint tag = root.Symbol.Tag;
                    uint4 before = PhaseBits(root.Root);
                    string context = $"node/sector/sign={node}/{sector}/{sign}";
                    Assert.That(Flower.ClassifyPhaseSector(root, out int actualSector),
                        Is.EqualTo(Flower.ProofClassification.Certain), context);
                    Assert.That(actualSector, Is.EqualTo(sector));
                    Assert.That(tag & Flower.BoundaryWitnessMask, Is.Zero);
                    Assert.That(Flower.TryGetAnchorRootFlags(node, tag, out ulong owner), Is.True, context);
                    Assert.That(Flower.TryGetAnchorRootReferences(node, tag, out ulong references), Is.True, context);
                    Assert.That(references, Is.EqualTo(incidence), context);
                    Assert.That(owner & ~references, Is.Zero, context);
                    Assert.That(Flower.TryGetAnchorRootFlags(node, tag, out ulong after), Is.True);
                    Assert.That(after, Is.EqualTo(owner), "UsesRoot must not expand RootOwner.");
                    Assert.That(PhaseBits(root.Root), Is.EqualTo(before));
                    Assert.That(root.Symbol.Tag, Is.EqualTo(tag));
                    nonOwnerUses |= references & ~owner;
                    uint wrongSector = (tag & ~(31u << 8)) | ((uint)line.SectorCount << 8);
                    Assert.That(Flower.TryGetAnchorRootReferences(node, wrongSector, out ulong rejected), Is.False);
                    Assert.That(rejected, Is.Zero);
                    uint wrongLine = (tag & ~(15u << 3)) | ((uint)((anchor.LineClass + 1) % Flower.LineClassCount) << 3);
                    Assert.That(Flower.TryGetAnchorRootReferences(node, wrongLine, out _), Is.False);
                }
            }
            Assert.That(nonOwnerUses, Is.EqualTo((1UL << Flower.PetalClassCount) - 1),
                "Every petal must exercise root use independent of being its half-open owner.");
            Assert.That(Flower.TryGetAnchorRootReferences(-1, 0u, out _), Is.False);
            Assert.That(Flower.TryGetAnchorRootReferences(Flower.NodeClassCount, 0u, out _), Is.False);
        }

        [Test]
        public void EveryCompletionCandidateAndRequiredDonorCanReferenceTheSameIncidentRoot()
        {
            // This proves only the exact shared-reference seam. It is NOT a
            // positive completion fixture: D, metric and dual gates are not
            // asserted here and no positive Parent48 receipt is fabricated.
            for (int candidate = 0; candidate < Flower.PetalClassCount; candidate++)
            {
                ulong neighbours = 0;
                foreach (var strand in Flower.Strands)
                {
                    if (strand.Petal0 == candidate) neighbours |= 1UL << strand.Petal1;
                    if (strand.Petal1 == candidate) neighbours |= 1UL << strand.Petal0;
                }
                uint2 generated = Flower.M8FlowerCompletionBoundaryCandidates(new uint2((uint)neighbours, (uint)(neighbours >> 32)));
                Assert.That((generated.x | ((ulong)generated.y << 32)) & (1UL << candidate), Is.Not.Zero);
                for (int donor = 0; donor < Flower.PetalClassCount; donor++)
                {
                    if ((neighbours & (1UL << donor)) == 0u) continue;
                    ulong omitted = neighbours & ~(1UL << donor);
                    uint2 missing = Flower.M8FlowerCompletionBoundaryCandidates(new uint2((uint)omitted, (uint)(omitted >> 32)));
                    Assert.That((missing.x | ((ulong)missing.y << 32)) & (1UL << candidate), Is.Zero,
                        "Each signed-edge neighbour must remain a required donor.");
                }
                for (int site = 0; site < 3; site++)
                {
                    int node = Flower.Petals[candidate].Node(site);
                    ulong requiredDonors = neighbours & IndependentNodeIncidence(node);
                    Assert.That(requiredDonors, Is.Not.Zero);
                    var line = Flower.Lines[Flower.Nodes[node].LineClass];
                    for (int sector = 0; sector < line.SectorCount; sector++)
                    for (int sign = 0; sign < 2; sign++)
                    {
                        Flower.PhaseRootEvidence shared = StrictAnchorEvidence(node, sector, sign != 0);
                        MerkabaFlowerSymbolKey identity = shared.Symbol;
                        uint4 interval = PhaseBits(shared.Root);
                        Assert.That(Flower.TryGetAnchorRootReferences(node, shared.Symbol.Tag, out ulong uses), Is.True);
                        Assert.That(uses & (requiredDonors | (1UL << candidate)),
                            Is.EqualTo(requiredDonors | (1UL << candidate)),
                            $"candidate/site/sector/sign={candidate}/{site}/{sector}/{sign}");
                        Assert.That(shared.Symbol, Is.EqualTo(identity));
                        Assert.That(PhaseBits(shared.Root), Is.EqualTo(interval));
                    }
                }
            }
        }

        [Test]
        public void EmptyFrozenParent_HasNoReachedCarrierOrDerivedSurface()
        {
            var reader = new Flower.SnapshotReader(Array.Empty<MerkabaTileSnapshot>(),
                Array.Empty<MerkabaTileAddress>());
            int3 owner = new(-257, -33, -9);
            var parent = reader.BeginFlowerDecode(owner, float2.zero);
            Assert.That(parent.ReachedCarriers, Is.EqualTo(uint4.zero));
            Span<Flower.PhaseRootEvidence> roots = stackalloc Flower.PhaseRootEvidence[7];
            Span<float3> positions = stackalloc float3[7];
            for (int carrier = 0; carrier < Flower.L2HubCount; carrier++)
            {
                Assert.That(reader.ClassifyPageCarrier(owner, carrier, float2.zero,
                    out var symbol, out _, out _, roots, positions, parent),
                    Is.EqualTo(Flower.ProofClassification.Impossible));
                Assert.That(symbol.ActiveWedgeMask, Is.Zero);
                Assert.That(symbol.CompletedWedgeMask, Is.Zero);
            }
        }

        [TestCase(3, 3, 3, true)]
        [TestCase(-261, -37, -13, true)]
        public void CapturedFlatWall_ProducesDirectCarriersWithSharedCanonicalKnots(
            int x, int y, int z, bool bodyDiagonal)
        {
            int3 owner = new(x, y, z);
            float3 normal = bodyDiagonal ? new float3(1f) : new float3(1f, 0f, 0f);
            var reader = FrozenWallReader(owner, normal, 0f, out float2 errors);
            var parent = reader.BeginFlowerDecode(owner, errors);
            Span<Flower.PhaseRootEvidence> roots = stackalloc Flower.PhaseRootEvidence[7];
            Span<float3> positions = stackalloc float3[7];
            var shared = new Dictionary<int4, (uint4 Phase, uint3 Position)>();
            int certain = 0, ambiguous = 0, activeWedges = 0, directWedges = 0, sharedUses = 0;
            for (int carrier = 0; carrier < Flower.L2HubCount; carrier++)
            {
                var status = reader.ClassifyPageCarrier(owner, carrier, errors,
                    out var symbol, out _, out uint direct, roots, positions, parent);
                if (status == Flower.ProofClassification.Ambiguous) ambiguous++;
                if (status != Flower.ProofClassification.Certain) continue;
                certain++;
                Assert.That(symbol.IsCanonicalCarrier, Is.True, $"owner={owner}, carrier={carrier}");
                Assert.That(symbol.CompletedWedgeMask, Is.Zero,
                    "This pass proves ordinary captured surface, not derived completion.");
                activeWedges += math.countbits(symbol.ActiveWedgeMask);
                directWedges += math.countbits(direct & symbol.ActiveWedgeMask);
                for (int wedge = 0; wedge < 6; wedge++)
                {
                    if ((symbol.ActiveWedgeMask & (1u << wedge)) == 0u) continue;
                    int3 sites = Flower.L2CarrierTriangleIndices(wedge);
                    for (int vertex = 0; vertex < 3; vertex++)
                    {
                        int site = sites[vertex];
                        var root = roots[site];
                        Assert.That(root.Classification, Is.EqualTo(Flower.ProofClassification.Certain));
                        Assert.That(Flower.ClassifyPhaseSector(root, out _),
                            Is.EqualTo(Flower.ProofClassification.Certain));
                        Assert.That(Flower.TryRootGridPosition(root, out float3 evaluated), Is.True);
                        Assert.That(math.asuint(positions[site]), Is.EqualTo(math.asuint(evaluated)));
                        // Sigma = level/J/line/sector/sign, not float position,
                        // status, endpoint orientation or boundary-proof bits.
                        var identity = new int4(root.Symbol.Junction,
                            (int)(root.Symbol.Tag & ((1u << MerkabaFlowerSymbolTag.EndpointOrientationShift) - 1u)));
                        uint4 phase = PhaseBits(root.Root);
                        uint3 position = math.asuint(positions[site]);
                        if (shared.TryGetValue(identity, out var previous))
                        {
                            sharedUses++;
                            Assert.That(phase, Is.EqualTo(previous.Phase), $"shared Sigma={identity}");
                            Assert.That(position, Is.EqualTo(previous.Position), $"shared Sigma={identity}");
                        }
                        else shared.Add(identity, (phase, position));
                    }
                }
            }
            TestContext.WriteLine($"owner={owner}, bounds={errors}, certain={certain}, ambiguous={ambiguous}, " +
                $"active={activeWedges}, direct={directWedges}, sharedUses={sharedUses}");
            Assert.That(activeWedges, Is.GreaterThan(0),
                "A captured planar wall must produce actual direct raster geometry at the mandatory sensor/codec bounds.");
            Assert.That(directWedges, Is.GreaterThan(0), "Provisional higher-shell presentation alone is not a direct proof.");
            Assert.That(sharedUses, Is.GreaterThan(0), "Actual adjacent triangles must reuse a canonical knot.");
        }

        [TestCase(3, 3, 3)]
        [TestCase(-261, -37, -13)]
        public void SyntheticQuantizedAxisPlane_PreservesUnresolvedWithoutInventingSurface(int x, int y, int z)
        {
            // The same synthetic X/delta=0 M8 input and mandatory codec
            // bounds as before. This is not a camera/stereo observation and
            // makes no claim about a physical scene or device scan result.
            int3 owner = new(x, y, z);
            var reader = FrozenWallReader(owner, new float3(1f, 0f, 0f), 0f, out float2 errors);
            int certainRoots = 0, ambiguousRoots = 0;
            for (int node = 0; node < 6; node++)
            for (int sign = 0; sign < 2; sign++)
            {
                var status = reader.ReadOriginalShared(owner, node, sign != 0,
                    errors.x, errors.y, out _, out _);
                if (status == Flower.ProofClassification.Certain) certainRoots++;
                if (status == Flower.ProofClassification.Ambiguous) ambiguousRoots++;
            }
            Assert.That(certainRoots, Is.Zero, "This quantized input has no certified original R1 root.");
            Assert.That(ambiguousRoots, Is.GreaterThan(0), "Unresolved root evidence must not be treated as absence.");

            var parent = reader.BeginFlowerDecode(owner, errors);
            Span<Flower.PhaseRootEvidence> roots = stackalloc Flower.PhaseRootEvidence[7];
            Span<float3> positions = stackalloc float3[7];
            int ambiguousCarriers = 0;
            uint unresolvedWedges = 0u;
            for (int carrier = 0; carrier < Flower.L2HubCount; carrier++)
            {
                var status = reader.ClassifyPageCarrier(owner, carrier, errors,
                    out var symbol, out uint unresolved, out uint direct, roots, positions, parent);
                Assert.That(status, Is.Not.EqualTo(Flower.ProofClassification.Certain), $"carrier={carrier}");
                Assert.That(symbol.ActiveWedgeMask, Is.Zero, $"Unresolved carrier={carrier} must not emit a surface.");
                Assert.That(symbol.CompletedWedgeMask, Is.Zero, $"Unresolved carrier={carrier} is not completion evidence.");
                Assert.That(direct, Is.Zero, $"Unresolved carrier={carrier} is not a direct donor.");
                if (status != Flower.ProofClassification.Ambiguous) continue;
                ambiguousCarriers++;
                Assert.That(unresolved, Is.Not.Zero, $"carrier={carrier} must preserve discrete unresolved evidence.");
                unresolvedWedges |= unresolved;
            }
            Assert.That(ambiguousCarriers, Is.GreaterThan(0));
            Assert.That(unresolvedWedges, Is.Not.Zero);
            TestContext.WriteLine($"Synthetic owner={owner}, bounds={errors}, R1 certain={certainRoots}, " +
                $"R1 ambiguous={ambiguousRoots}, ambiguous carriers={ambiguousCarriers}, unresolved=0x{unresolvedWedges:x2}");
        }

        internal static Flower.SnapshotReader FrozenWallReader(int3 owner, float3 normal,
            float offset, out float2 errors)
        {
            // A real frozen M8 tile: every representable sample observes one
            // common plane, then passes through the production plane codec.
            // Missing samples remain absent; neither roots nor D are supplied.
            uint centerFlags = KernelState.SetSurfacePlane(MerkabaConstants.OccupiedFlag,
                normal, offset);
            KernelState.DecodeSurfacePlane(centerFlags, out float3 capturedNormal, out float capturedOffset);
            var encodedOwner = MerkabaSpatial.Encode(owner);
            var address = new MerkabaTileAddress(encodedOwner.BlockCoord, encodedOwner.LocalAddress);
            var states = new KernelState[MerkabaSpatial.KernelsPerTile];
            for (int local = 0; local < states.Length; local++)
            {
                int3 coord = MerkabaSpatial.Decode(address.BlockCoord, address.LocalAddress, local);
                float localOffset = capturedOffset - MerkabaConstants.LatticeStep *
                    math.dot(capturedNormal, (float3)(coord - owner));
                if (!KernelState.TrySetSurfacePlane(MerkabaConstants.OccupiedFlag,
                        capturedNormal, localOffset, out uint flags)) continue;
                states[local] = new KernelState
                {
                    Flags = flags,
                    OccupancyEvidence = MerkabaConstants.OccupiedOnThreshold,
                    PackedColor = 0xff80a0c0u,
                    ColorConfidence = 1
                };
            }

            // Exact synthetic measurements still incur both mandatory codec
            // bounds used by DepthCapture.TryGetFlowerPlaneBounds. Do not use
            // zero uncertainty, or reduce these after inspecting a result.
            errors = new float2(
                Flower.FloatInterval.Enclose(2.0 * Math.Sqrt(18.0) / 1023.0).Upper,
                Flower.FloatInterval.Enclose((double)MerkabaConstants.LatticeStep / (2.0 * 127.0)).Upper);
            var snapshot = new MerkabaTileSnapshot { Address = address, Generation = 1, States = states };
            return new Flower.SnapshotReader(new[] { snapshot }, new[] { address });
        }

        internal static uint BoundaryReferenceTag(int node, int boundary, bool plus)
        {
            var anchor = Flower.Nodes[node];
            int sector = Flower.BoundaryRules[Flower.Lines[anchor.LineClass].SectorOffset + boundary].Owner;
            return MerkabaFlowerSymbolTag.Create(0, anchor.LineClass, plus, sector, false, 0,
                MerkabaFlowerSymbolStatus.Confirmed).Value | ((uint)(boundary + 1) << 21);
        }

        internal static ulong IndependentNodeIncidence(int node)
        {
            ulong mask = 0;
            for (int petal = 0; petal < Flower.PetalClassCount; petal++)
            {
                var candidate = Flower.Petals[petal];
                if (candidate.FaceNode == node || candidate.EdgeNode == node || candidate.CornerNode == node)
                    mask |= 1UL << petal;
            }
            return mask;
        }

        internal static Flower.PhaseRootEvidence StrictAnchorEvidence(int node, int sector, bool plus)
        {
            var anchor = Flower.Nodes[node];
            var line = Flower.Lines[anchor.LineClass];
            float2 a = Flower.SectorBoundaries[line.SectorOffset + sector].Unit;
            float2 b = Flower.SectorBoundaries[line.SectorOffset + (sector + 1) % line.SectorCount].Unit;
            uint tag = MerkabaFlowerSymbolTag.Create(0, anchor.LineClass, plus, sector, false, 0,
                MerkabaFlowerSymbolStatus.Confirmed).Value;
            var key = new MerkabaFlowerSymbolKey
            {
                JunctionX = -514 + anchor.Direction.x, JunctionY = -66 + anchor.Direction.y,
                JunctionZ = -18 + anchor.Direction.z, Tag = tag
            };
            return new Flower.PhaseRootEvidence(key, Flower.Interval2.Singleton(math.normalize(a + b)),
                Flower.ProofClassification.Certain);
        }

        private static uint4 PhaseBits(Flower.Interval2 phase) => math.asuint(
            new float4(phase.X.Lower, phase.X.Upper, phase.Y.Lower, phase.Y.Upper));

        internal static ulong IndependentBoundaryClosedPowerMask(int node, int boundary)
        {
            var anchor = Flower.Nodes[node];
            var rule = Flower.BoundaryRules[Flower.Lines[anchor.LineClass].SectorOffset + boundary];
            ulong result = 0;
            for (int petal = 0; petal < Flower.PetalClassCount; petal++)
            {
                var candidate = Flower.Petals[petal];
                if (candidate.FaceNode != node && candidate.EdgeNode != node && candidate.CornerNode != node) continue;
                bool maximal = true;
                foreach (var other in Flower.Petals)
                {
                    if (other.FaceNode != candidate.FaceNode) continue;
                    maximal &= BoundaryDotSign(rule, anchor.Direction, (int)anchor.Shell,
                        Flower.Nodes[candidate.EdgeNode].Direction - Flower.Nodes[other.EdgeNode].Direction) >= 0;
                    if (other.EdgeNode == candidate.EdgeNode)
                        maximal &= BoundaryDotSign(rule, anchor.Direction, (int)anchor.Shell,
                            Flower.Nodes[candidate.CornerNode].Direction - Flower.Nodes[other.CornerNode].Direction) >= 0;
                }
                if (maximal) result |= 1UL << petal;
            }
            return result;
        }

        private static int BoundaryDotSign(Flower.BoundaryRule rule, int3 direction, int shell, int3 difference)
        {
            // Independently recover the radical from the exact circle radius
            // 3*m/4, then compare squared integers. No float sample/tolerance,
            // production SectorPredicateLimit, or reference-mask lookup.
            BigInteger w = rule.Rational.w, q2 = 0, d2 = 0, qd = 0, a = 0, b = 0;
            for (int axis = 0; axis < 3; axis++)
            {
                BigInteger q = rule.Rational[axis], d = rule.D[axis];
                q2 += q * q; d2 += d * d; qd += q * d;
                a += difference[axis] * (2 * q + w * direction[axis]);
                b += difference[axis] * d;
            }
            if (d2.IsZero || b.IsZero) return a.Sign;
            Assert.That(qd.IsZero, Is.True, "Exact cut decomposition must be orthogonal.");
            BigInteger numerator = 3 * shell * w * w - 4 * q2;
            BigInteger denominator = 4 * w * w * d2;
            Assert.That(numerator.Sign, Is.GreaterThan(0));
            b *= 2 * w;
            if (a.IsZero || a.Sign == b.Sign) return b.Sign;
            int magnitude = (a * a * denominator).CompareTo(b * b * numerator);
            return magnitude == 0 ? 0 : magnitude > 0 ? a.Sign : b.Sign;
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
            // The authority transports complete enclosures. A midpoint of
            // an inverse interval has no fixed per-component ULP roundtrip
            // guarantee (especially when one rotated component cancels).
            // Use the exact unit root (4/5,-3/5) and independently computed
            // rational rotations, including the exact binary32 value 0.3f.
            var predicted = new Flower.Interval2(
                Flower.FloatInterval.Enclose(4.0 / 5.0),
                Flower.FloatInterval.Enclose(-3.0 / 5.0));
            int line = Flower.FindLineClass(new int3(1, 1, 0), out _);
            Assert.That(Flower.ClassifySector(line, predicted, out int sector),
                Is.EqualTo(Flower.ProofClassification.Certain));
            AssertContainsRational(predicted.X, 4, 5);
            AssertContainsRational(predicted.Y, -3, 5);
            foreach (int2 fraction in new[]
                     { new int2(-1, 4), new int2(-1, 16), new int2(0, 1),
                         new int2(1, 8), new int2(5033165, 16777216) })
            {
                BigInteger t = fraction.x, d = fraction.y;
                BigInteger difference = d * d - t * t;
                BigInteger denominator = 5 * (d * d + t * t);
                BigInteger x = 4 * difference + 6 * t * d;
                BigInteger y = -3 * difference + 8 * t * d;
                Assert.That(x * x + y * y, Is.EqualTo(denominator * denominator));
                var observed = new Flower.Interval2(
                    Flower.FloatInterval.Enclose((double)x / (double)denominator),
                    Flower.FloatInterval.Enclose((double)y / (double)denominator));
                AssertContainsRational(observed.X, x, denominator);
                AssertContainsRational(observed.Y, y, denominator);
                Assert.That(Flower.TangentHalfAngle(predicted, observed, out var recovered),
                    Is.EqualTo(Flower.ProofClassification.Certain));
                AssertContainsRational(recovered, t, d);
                var proof = Flower.RotateTangentHalfAngle(predicted, recovered,
                    line, sector, out var synthesis);
                AssertContainsRational(synthesis.X, x, denominator);
                AssertContainsRational(synthesis.Y, y, denominator);
                Assert.That(Flower.ClassifySector(line, observed, out int observedSector),
                    Is.EqualTo(Flower.ProofClassification.Certain));
                Assert.That(proof, Is.EqualTo(observedSector == sector
                    ? Flower.ProofClassification.Certain : Flower.ProofClassification.Ambiguous),
                    "A valid metric rotation cannot silently change the immutable sector.");
            }
        }

        [Test]
        public void R2PredictionResidual_AdmitsDisjointSameSymbolEvidence()
        {
            var predicted = new MerkabaSphereFlowerAuthority.Interval2(
                MerkabaSphereFlowerAuthority.FloatInterval.Enclose(255.0 / 257.0),
                MerkabaSphereFlowerAuthority.FloatInterval.Enclose(32.0 / 257.0));
            var observed = new MerkabaSphereFlowerAuthority.Interval2(
                MerkabaSphereFlowerAuthority.FloatInterval.Enclose(63.0 / 65.0),
                MerkabaSphereFlowerAuthority.FloatInterval.Enclose(16.0 / 65.0));
            int line = MerkabaSphereFlowerAuthority.FindLineClass(new int3(1, 1, 0),
                out _);
            Assert.That(MerkabaSphereFlowerAuthority.ClassifySector(line,
                predicted, out int sector), Is.EqualTo(
                    MerkabaSphereFlowerAuthority.ProofClassification.Certain));
            var tag = MerkabaFlowerSymbolTag.Create(1, line, true, sector,
                false, 3u, MerkabaFlowerSymbolStatus.Confirmed);
            var symbol = new MerkabaFlowerSymbolKey(new int3(-3, 5, -8), tag);
            var p = new MerkabaSphereFlowerAuthority.PhaseRootEvidence(symbol,
                predicted, MerkabaSphereFlowerAuthority.ProofClassification.Certain);
            var o = new MerkabaSphereFlowerAuthority.PhaseRootEvidence(symbol,
                observed, MerkabaSphereFlowerAuthority.ProofClassification.Certain);
            Assert.That(MerkabaSphereFlowerAuthority.FloatInterval.TryIntersect(
                predicted.X, observed.X, out _), Is.False);
            var residual = MerkabaSphereFlowerAuthority.AnalyzePhaseResidual(p, o);
            Assert.That(residual.Classification, Is.EqualTo(
                MerkabaSphereFlowerAuthority.PhaseResidualClassification.CertainNonzero));
            Assert.That(residual.Residual.Lower, Is.LessThanOrEqualTo(8.0 / 129.0));
            Assert.That(residual.Residual.Upper, Is.GreaterThanOrEqualTo(8.0 / 129.0));
            Assert.That(residual.Lower, Is.GreaterThan(0));

            var detailKey = MerkabaFlowerDetailKey.Create(1, 0, 0, line,
                MerkabaFlowerDetailKind.R2Phase, true, sector);
            var record = MerkabaFlowerDetailRecord.Create(detailKey,
                residual.Lower, residual.Upper, 7u);
            Assert.That(MerkabaSphereFlowerAuthority.SynthesizePhaseRecord(p,
                detailKey, record, 7u, out var synthesized), Is.EqualTo(
                    MerkabaSphereFlowerAuthority.ProofClassification.Certain));
            Assert.That(MerkabaSphereFlowerAuthority.CloseSharedPhaseRoot(
                synthesized, o, out var closed), Is.EqualTo(
                    MerkabaSphereFlowerAuthority.ProofClassification.Certain));
            Assert.That(MerkabaSphereFlowerAuthority.CloseSharedPhaseRoot(
                o, synthesized, out var reversed), Is.EqualTo(
                    MerkabaSphereFlowerAuthority.ProofClassification.Certain));
            Assert.That(reversed.Symbol.Tag, Is.EqualTo(closed.Symbol.Tag));
            Assert.That(reversed.Root.X.Lower, Is.EqualTo(closed.Root.X.Lower));
            Assert.That(reversed.Root.Y.Upper, Is.EqualTo(closed.Root.Y.Upper));
            Assert.That(MerkabaSphereFlowerAuthority.CloseSharedPhaseRoot(p,
                o, out _), Is.EqualTo(
                    MerkabaSphereFlowerAuthority.ProofClassification.Impossible),
                "Peer closure intersects final knots; residual extraction does not.");
            Assert.That(MerkabaSphereFlowerAuthority.SynthesizePhaseRecord(p,
                detailKey, record, 8u, out _), Is.EqualTo(
                    MerkabaSphereFlowerAuthority.ProofClassification.Ambiguous));

            var uncertain = MerkabaSphereFlowerAuthority.AnalyzePhaseResidual(p, p);
            Assert.That(uncertain.Classification, Is.EqualTo(
                MerkabaSphereFlowerAuthority.PhaseResidualClassification.Ambiguous),
                "Equal independent interval boxes do not prove zero innovation.");
            var otherSymbol = new MerkabaFlowerSymbolKey(new int3(-1, 5, -8), tag);
            var other = new MerkabaSphereFlowerAuthority.PhaseRootEvidence(otherSymbol,
                observed, MerkabaSphereFlowerAuthority.ProofClassification.Certain);
            Assert.That(MerkabaSphereFlowerAuthority.AnalyzePhaseResidual(p, other)
                    .Classification, Is.EqualTo(
                MerkabaSphereFlowerAuthority.PhaseResidualClassification.Impossible));
        }

        [Test]
        public void R2PredictionResidual_SingletonIdentityHasExactZeroInnovation()
        {
            var root = MerkabaSphereFlowerAuthority.Interval2.Singleton(
                new float2(255f / 257f, 32f / 257f));
            int line = MerkabaSphereFlowerAuthority.FindLineClass(new int3(1, 1, 0),
                out _);
            Assert.That(MerkabaSphereFlowerAuthority.ClassifySector(line, root,
                out int sector), Is.EqualTo(
                    MerkabaSphereFlowerAuthority.ProofClassification.Certain));
            var symbol = new MerkabaFlowerSymbolKey(new int3(1, 1, 0),
                MerkabaFlowerSymbolTag.Create(2, line, false, sector,
                    false, 3u, MerkabaFlowerSymbolStatus.Confirmed));
            var evidence = new MerkabaSphereFlowerAuthority.PhaseRootEvidence(symbol,
                root, MerkabaSphereFlowerAuthority.ProofClassification.Certain);
            var result = MerkabaSphereFlowerAuthority.AnalyzePhaseResidual(
                evidence, evidence);
            Assert.That(result.Classification, Is.EqualTo(
                MerkabaSphereFlowerAuthority.PhaseResidualClassification.ExactZero));
            Assert.That(result.Residual.Lower, Is.Zero);
            Assert.That(result.Residual.Upper, Is.Zero);
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
            // Exact homogeneous equality proves this root, but must not
            // replace the outward metric computation with a singleton.
            var vx = Flower.FloatInterval.Subtract(
                Flower.FloatInterval.Multiply(first.Y, second.Z),
                Flower.FloatInterval.Multiply(first.Z, second.Y));
            var vy = Flower.FloatInterval.Subtract(
                Flower.FloatInterval.Multiply(first.Z, second.X),
                Flower.FloatInterval.Multiply(first.X, second.Z));
            var vz = Flower.FloatInterval.Subtract(
                Flower.FloatInterval.Multiply(first.X, second.Y),
                Flower.FloatInterval.Multiply(first.Y, second.X));
            var expectedX = Flower.FloatInterval.Divide(vy, vx);
            var expectedY = Flower.FloatInterval.Divide(vz, vx);
            var proof = MerkabaSphereFlowerAuthority.ClassifyHinge(first,
                second, out var root);
            Assert.That(proof,
                Is.EqualTo(MerkabaSphereFlowerAuthority.ProofClassification.Certain));
            Assert.That(root.X, Is.EqualTo(expectedX));
            Assert.That(root.Y, Is.EqualTo(expectedY));
            Assert.That(root.X.Lower, Is.LessThanOrEqualTo(1f));
            Assert.That(root.X.Upper, Is.GreaterThanOrEqualTo(1f));
            Assert.That(root.Y.Lower, Is.LessThanOrEqualTo(0f));
            Assert.That(root.Y.Upper, Is.GreaterThanOrEqualTo(0f));
            Assert.That(root.X.IsSingleton, Is.False,
                "The symbolic proof must preserve the full metric enclosure.");

            var uncertainSecond = new Flower.Interval3(
                new Flower.FloatInterval(-1f, math.asfloat(math.asuint(-1f) - 1u)),
                Flower.FloatInterval.Singleton(1f), Flower.FloatInterval.Singleton(0f));
            Assert.That(Flower.ClassifyHinge(first, uncertainSecond, out _),
                Is.EqualTo(Flower.ProofClassification.Ambiguous),
                "A numerical interval touching the exact hinge is not an equality witness.");
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
            StringAssert.Contains("#define M8_FLOWER_SECTOR_BOUNDARY_COUNT 264u",
                source);
            // The frozen tables are carried by the shared read-only buffer, not
            // by per-invocation static const arrays. The alphabet invariant is
            // unchanged: the same accessors must exist and the generated blob
            // row offsets must still encode exactly 72/48/36/399/343.
            StringAssert.Contains("M8FlowerStrandAt", source);
            StringAssert.Contains("M8FlowerPetalNodesAt", source);
            StringAssert.Contains("M8FlowerClassifyRoot", source);
            StringAssert.Contains("M8FlowerSkinChamberAt", source);
            StringAssert.Contains("M8FlowerSkinCanonicalToThreadAt", source);
            StringAssert.Contains("M8FlowerSkinCanonicalL5ToThreadAt", source);
            StringAssert.Contains("M8FlowerNestedV", source);
            // Two scalar constants remain by design; no table may be a
            // per-invocation static const array again.
            Assert.That(Regex.Matches(source, @"static const \w+ \w+\[").Count,
                Is.Zero, "generated tables must stay in the shared read-only buffer");
            Assert.That(MerkabaFlowerTableBlob.PetalNodesRowOffset -
                MerkabaFlowerTableBlob.StrandRowOffset, Is.EqualTo(72), "strands");
            Assert.That(MerkabaFlowerTableBlob.PetalStrandsRowOffset -
                MerkabaFlowerTableBlob.PetalNodesRowOffset, Is.EqualTo(48), "petals");
            Assert.That(MerkabaFlowerTableBlob.SkinCanonicalToThreadRowOffset -
                MerkabaFlowerTableBlob.SkinChamberRowOffset, Is.EqualTo(36), "chambers");
            Assert.That(MerkabaFlowerTableBlob.SkinThreadToCanonicalRowOffset -
                MerkabaFlowerTableBlob.SkinCanonicalToThreadRowOffset,
                Is.EqualTo(100), "399 thread positions packed four per row");
            Assert.That(MerkabaFlowerTableBlob.SkinL3ChildRankRowOffset -
                MerkabaFlowerTableBlob.SkinCanonicalL5ToThreadRowOffset,
                Is.EqualTo(86), "343 L5 addresses packed four per row");
            StringAssert.DoesNotContain("M8FlowerDeformedLoopUnit", source);
            StringAssert.DoesNotContain("M8FlowerEndpointBasis", source);
            StringAssert.DoesNotContain("atan", source);
            StringAssert.DoesNotContain("acos", source);
            // Named quantization/calibration enclosure bounds are not magic
            // epsilons. Their containment is checked by the interval fixtures.
            StringAssert.Contains("M8FlowerPrevious", source);
            StringAssert.Contains("M8FlowerNext", source);
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

        private static void AssertContainsRational(Flower.FloatInterval interval,
            BigInteger numerator, BigInteger denominator)
        {
            Assert.That(denominator.Sign, Is.GreaterThan(0));
            BigInteger exact = numerator << 149;
            Assert.That((Binary32Units(interval.Lower) * denominator).CompareTo(exact),
                Is.LessThanOrEqualTo(0), "Exact rational lower enclosure");
            Assert.That((Binary32Units(interval.Upper) * denominator).CompareTo(exact),
                Is.GreaterThanOrEqualTo(0), "Exact rational upper enclosure");
        }

        private static BigInteger Binary32Units(float value)
        {
            uint bits = math.asuint(value), exponent = (bits >> 23) & 255u;
            Assert.That(exponent, Is.LessThan(255u), "Finite binary32 enclosure required");
            BigInteger significand = bits & 0x7fffffu;
            if (exponent != 0u) significand = (significand + 0x800000) << ((int)exponent - 1);
            return (bits & 0x80000000u) == 0u ? significand : -significand;
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
