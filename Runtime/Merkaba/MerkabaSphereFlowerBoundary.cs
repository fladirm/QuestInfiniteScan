using System;
using System.Numerics;
using Unity.Mathematics;

namespace Genesis.RoomScan
{
    public static partial class MerkabaSphereFlowerAuthority
    {
        // Proof metadata only. It is not part of Sigma or any persistent ABI.
        internal const uint BoundaryWitnessMask = 63u << 21;

        public readonly struct BoundaryRule
        {
            // X/a_L = Rational.xyz/Rational.w + loopOffset/2 + D*sqrt(r).
            // A nonsquare r is irrational: equality requires both coefficients
            // to vanish independently. Rational square roots are folded first.
            public readonly int4 Rational;
            public readonly int3 D;
            public readonly byte Owner;
            public BoundaryRule(int4 rational, int3 d, byte owner)
            { Rational = rational; D = d; Owner = owner; }
        }

        private static class BoundaryData
        {
#if UNITY_EDITOR
            internal static readonly BoundaryRule[] Rules = BuildBoundaryRules();
#else
            internal static readonly BoundaryRule[] Rules = LoadGeneratedBoundaryRules();
#endif
        }

        public static ReadOnlySpan<BoundaryRule> BoundaryRules => BoundaryData.Rules;

        private static class BoundaryReferenceData
        {
#if UNITY_EDITOR
            internal static readonly ulong[] Masks = BuildAnchorBoundaryReferences();
#else
            internal static readonly ulong[] Masks = LoadGeneratedAnchorBoundaryReferencePetalMasks();
#endif
        }

        public static ReadOnlySpan<ulong> AnchorBoundaryReferencePetalMasks => BoundaryReferenceData.Masks;

        // Reference incidence is not ownership/admission. This entry consumes
        // a retained, already-certified exact boundary witness from an existing
        // non-provisional confirmed donor. It never changes that donor's Sigma,
        // half-open sector, algebraic sign or metric interval. Without such a
        // witness, even numerical contact has no boundary reference authority.
        public static bool TryGetAnchorBoundaryReferences(int node, uint proofTag, out ulong mask)
        {
            mask = 0;
            uint code = (proofTag & BoundaryWitnessMask) >> 21;
            if ((uint)node >= NodesValue.Length || code == 0u) return false;
            NodeRule anchor = NodesValue[node];
            if (((proofTag >> 3) & 15u) != anchor.LineClass) return false;
            LineRule line = LinesValue[anchor.LineClass];
            if (code > line.SectorCount ||
                BoundaryRules[line.SectorOffset + (int)code - 1].Owner != ((proofTag >> 8) & 31u))
                return false;
            mask = BoundaryReferenceData.Masks[2 * (line.SectorOffset + (int)code - 1) +
                (anchor.Orientation < 0 ? 1 : 0)];
            return true;
        }

#if UNITY_EDITOR
        public static ulong[] BuildAnchorBoundaryReferences()
        {
            var masks = new ulong[2 * SectorBoundariesValue.Length];
            for (int node = 0; node < NodesValue.Length; node++)
            {
                NodeRule anchor = NodesValue[node];
                LineRule line = LinesValue[anchor.LineClass];
                var cuts = BuildLineBoundaryPoints(PetalsValue, NodesValue, line.Direction);
                if (cuts.Count != line.SectorCount)
                    throw new InvalidOperationException("Boundary references differ from the exact loop arrangement.");
                for (int boundary = 0; boundary < cuts.Count; boundary++)
                {
                    ExactBoundaryPoint cut = cuts[boundary];
                    ulong references = 0;
                    for (int petal = 0; petal < PetalClassCount; petal++)
                    {
                        if ((NodeIncidentPetalsValue[node] & (1UL << petal)) == 0u) continue;
                        PetalRule candidate = PetalsValue[petal];
                        bool maximal = true;
                        // Closed versions of the SAME two max-Phi decisions.
                        // Equal-shell constants cancel exactly. All ties are
                        // references; only PowerFlagAtCut owns a tied point.
                        foreach (PetalRule other in PetalsValue)
                        {
                            if (other.FaceNode != candidate.FaceNode) continue;
                            if (SectorPredicateLimit(line.Direction, anchor.Direction, cut,
                                NodesValue[candidate.EdgeNode].Direction - NodesValue[other.EdgeNode].Direction,
                                Rational.Zero, 0) < 0) maximal = false;
                            if (other.EdgeNode == candidate.EdgeNode &&
                                SectorPredicateLimit(line.Direction, anchor.Direction, cut,
                                    NodesValue[candidate.CornerNode].Direction - NodesValue[other.CornerNode].Direction,
                                    Rational.Zero, 0) < 0) maximal = false;
                        }
                        if (!maximal) continue;
                        int selected = PowerFlagAtCut(PetalsValue, NodesValue, line.Direction,
                            anchor.Direction, candidate.FaceNode, cut, 0);
                        if (selected != petal)
                        {
                            PetalRule owner = PetalsValue[selected];
                            int3 equality = candidate.EdgeNode != owner.EdgeNode
                                ? NodesValue[candidate.EdgeNode].Direction - NodesValue[owner.EdgeNode].Direction
                                : NodesValue[candidate.CornerNode].Direction - NodesValue[owner.CornerNode].Direction;
                            if (SectorPredicateLimit(line.Direction, anchor.Direction, cut,
                                equality, Rational.Zero, 0) != 0)
                                throw new InvalidOperationException("A non-owning boundary reference lacks exact power equality.");
                        }
                        references |= 1UL << petal;
                    }
                    ulong at = AnchorFlagMaskAtCut(PetalsValue, NodesValue, line.Direction, node, cut, 0);
                    ulong before = AnchorFlagMaskAtCut(PetalsValue, NodesValue, line.Direction, node, cut, -1);
                    ulong after = AnchorFlagMaskAtCut(PetalsValue, NodesValue, line.Direction, node, cut, 1);
                    if ((references & ~NodeIncidentPetalsValue[node]) != 0u ||
                        ((at | before | after) & ~references) != 0u)
                        throw new InvalidOperationException("Closed boundary references lost an incident half-open owner.");
                    masks[2 * (line.SectorOffset + boundary) + (anchor.Orientation < 0 ? 1 : 0)] = references;
                }
            }
            return masks;
        }

        private static BigInteger BoundaryIntegerSqrt(BigInteger value)
        {
            if (value.Sign < 0) throw new ArgumentOutOfRangeException(nameof(value));
            if (value.IsZero) return BigInteger.Zero;
            BigInteger x = value, next = (x + 1) >> 1;
            while (next < x) { x = next; next = (x + value / x) >> 1; }
            return x;
        }

        public static BoundaryRule[] BuildBoundaryRules()
        {
            byte[] owners = BuildAnchorBoundaryOwners();
            var result = new BoundaryRule[SectorBoundariesValue.Length];
            for (int line = 0; line < LineClassCount; line++)
            {
                int3 r = LinesValue[line].Direction;
                var points = BuildLineBoundaryPoints(PetalsValue, NodesValue, r);
                for (int b = 0; b < points.Count; b++)
                {
                    ExactBoundaryPoint p = points[b];
                    Rational3 radial = p.Origin - Scale(new Rational(1, 2), r);
                    int3 d = p.RootSign * p.Direction;
                    BigInteger numerator = BoundaryIntegerSqrt(p.Radicand.Numerator);
                    BigInteger denominator = BoundaryIntegerSqrt(p.Radicand.Denominator);
                    if (numerator * numerator == p.Radicand.Numerator &&
                        denominator * denominator == p.Radicand.Denominator)
                    {
                        radial += Scale(new Rational(numerator, denominator), d);
                        d = int3.zero;
                    }
                    // Even denominator makes the exact half-grid translation
                    // integral at every L0-L2 level; no FP coordinate is stored.
                    BigInteger scale = 2;
                    for (int axis = 0; axis < 3; axis++)
                        scale = scale / BigInteger.GreatestCommonDivisor(scale,
                            radial[axis].Denominator) * radial[axis].Denominator;
                    int4 q = new((int)(radial.X.Numerator * (scale / radial.X.Denominator)),
                        (int)(radial.Y.Numerator * (scale / radial.Y.Denominator)),
                        (int)(radial.Z.Numerator * (scale / radial.Z.Denominator)), (int)scale);
                    // The GPU exact-dot primitive multiplies two <=24-bit
                    // integers. Prove every translated coefficient fits it.
                    for (int axis = 0; axis < 3; axis++)
                        if (BigInteger.Abs(q[axis]) + 2 * scale >= (1 << 24) ||
                            BigInteger.Abs(d[axis]) >= (1 << 24))
                            throw new InvalidOperationException("Boundary coefficient exceeds exact-dot ABI.");
                    if (scale * 160 >= (1 << 24))
                        throw new InvalidOperationException("Boundary offset coefficient exceeds exact-dot ABI.");
                    // For an irrational cut, N.D=0 makes N.(r x Origin)=0.
                    // Thus the algebraic root sign is exactly sign N.(r x D).
                    int3 tangent = math.any(d != 0) ? Cross(r, d) : Cross(r, q.xyz);
                    if (math.any(math.abs(tangent) >= (1 << 24)) ||
                        (math.any(d != 0) && math.any(Cross(Cross(r, q.xyz), d) != 0)))
                        throw new InvalidOperationException("Boundary tangent factorization failed.");
                    int index = LinesValue[line].SectorOffset + b;
                    result[index] = new BoundaryRule(q, d, owners[index]);
                }
            }
            return result;
        }
#endif

        // Independent exact CPU oracle for the generated uint32 GPU limb
        // predicate. Each finite binary32 is an INTEGER times 2^-149; neither
        // rounded ABC values nor proximity to a boundary proves equality.
        private static bool BoundaryLinearSign(float4 value, int4 coefficient, out int sign)
        {
            sign = 0;
            BigInteger sum = BigInteger.Zero;
            for (int i = 0; i < 4; i++)
            {
                uint bits = math.asuint(value[i]);
                uint exponent = (bits >> 23) & 255u;
                if (exponent == 255u || coefficient[i] <= -(1 << 24) ||
                    coefficient[i] >= (1 << 24)) return false;
                uint mantissa = (bits & 0x7fffffu) | (exponent == 0u ? 0u : 0x800000u);
                BigInteger term = (BigInteger)mantissa * coefficient[i];
                if ((bits & 0x80000000u) != 0u) term = -term;
                sum += term << (exponent == 0u ? 0 : (int)exponent - 1);
            }
            sign = sum.Sign;
            return true;
        }

        private static bool ProvePlaneBoundary(int level, int3 loopOffset, int lineClass,
            bool plus, float3 normal, float offset, int boundary)
        {
            if ((uint)level >= GeometryLevelCount || (uint)lineClass >= LineClassCount ||
                math.any(loopOffset < -(1 << level)) || math.any(loopOffset > (1 << level))) return false;
            LineRule line = LinesValue[lineClass];
            if ((uint)boundary >= line.SectorCount) return false;
            BoundaryRule rule = BoundaryData.Rules[line.SectorOffset + boundary];
            int4 q = rule.Rational;
            int4 equation = new(q.xyz + (q.w / 2) * loopOffset, -q.w * (40 << level));
            float4 plane = new(normal, offset);
            if (!BoundaryLinearSign(plane, equation, out int equality) || equality != 0 ||
                !BoundaryLinearSign(plane, new int4(rule.D, 0), out int irrational) || irrational != 0)
                return false;
            int3 tangent = math.any(rule.D != 0) ? Cross(line.Direction, rule.D) :
                Cross(line.Direction, q.xyz);
            if (!BoundaryLinearSign(plane, new int4(tangent, 0), out int derivative)) return false;
            // For the canonical analytic +/- formula, derivative has opposite
            // sign. Exact tangent has only the canonical minus entry.
            return derivative == 0 ? !plus : (derivative < 0) == plus;
        }

        internal static ProofClassification ClassifyPlaneSector(int level, int3 loopOffset,
            int lineClass, bool plusRoot, float3 decodedNormal, float decodedOffset,
            Interval2 root, out int sector, out uint boundaryWitness)
        {
            sector = -1; boundaryWitness = 0;
            if ((uint)lineClass >= LineClassCount || !IsFinitePhaseRoot(root))
                return ProofClassification.Ambiguous;
            if (ClassifySector(lineClass, root, out sector) == ProofClassification.Certain)
                return ProofClassification.Certain;
            LineRule line = LinesValue[lineClass];
            for (int b = 0; b < line.SectorCount; b++)
            {
                Interval2 cut = SectorBoundariesValue[line.SectorOffset + b].Enclosure;
                if (root.X.Upper < cut.X.Lower || cut.X.Upper < root.X.Lower ||
                    root.Y.Upper < cut.Y.Lower || cut.Y.Upper < root.Y.Lower ||
                    !ProvePlaneBoundary(level, loopOffset, lineClass, plusRoot,
                        decodedNormal, decodedOffset, b)) continue;
                if (boundaryWitness != 0) { sector = -1; boundaryWitness = 0; return ProofClassification.Ambiguous; }
                sector = BoundaryData.Rules[line.SectorOffset + b].Owner;
                boundaryWitness = (uint)(b + 1) << 21;
            }
            return boundaryWitness != 0 ? ProofClassification.Certain : ProofClassification.Ambiguous;
        }

        internal static ProofClassification ClassifyPhaseSector(PhaseRootEvidence evidence, out int sector)
        {
            sector = -1;
            if (evidence.Classification != ProofClassification.Certain || !TryPhaseIdentity(evidence, out var tag))
                return ProofClassification.Ambiguous;
            uint code = (evidence.Symbol.Tag & BoundaryWitnessMask) >> 21;
            if (code == 0)
            {
                ProofClassification status = ClassifySector(tag.LineClass, evidence.Root, out sector);
                return status == ProofClassification.Certain && sector == tag.Sector ?
                    ProofClassification.Certain : ProofClassification.Ambiguous;
            }
            LineRule line = LinesValue[tag.LineClass];
            if (code > line.SectorCount || BoundaryData.Rules[line.SectorOffset + (int)code - 1].Owner != tag.Sector)
                return ProofClassification.Ambiguous;
            Interval2 cut = SectorBoundariesValue[line.SectorOffset + (int)code - 1].Enclosure;
            if (evidence.Root.X.Upper < cut.X.Lower || cut.X.Upper < evidence.Root.X.Lower ||
                evidence.Root.Y.Upper < cut.Y.Lower || cut.Y.Upper < evidence.Root.Y.Lower)
                return ProofClassification.Ambiguous;
            sector = tag.Sector;
            return ProofClassification.Certain;
        }

        internal static ProofClassification RotatePhaseEvidence(PhaseRootEvidence prediction,
            FloatInterval turn, out PhaseRootEvidence result)
        {
            result = default;
            if (ClassifyPhaseSector(prediction, out int sector) != ProofClassification.Certain)
                return ProofClassification.Ambiguous;
            if (turn.IsSingleton && turn.Lower == 0f) { result = prediction; return ProofClassification.Certain; }
            var tag = prediction.Symbol;
            tag.Tag &= ~BoundaryWitnessMask;
            int line = (int)((tag.Tag >> 3) & 15u);
            if (!RotatePhaseMetric(prediction.Root, turn, out Interval2 rotated))
                return ProofClassification.Ambiguous;
            ProofClassification status = ClassifySector(line, rotated, out int actual) == ProofClassification.Certain &&
                actual == sector ? ProofClassification.Certain : ProofClassification.Ambiguous;
            result = new PhaseRootEvidence(tag, rotated, status);
            return status;
        }

        private static bool RotatePhaseMetric(Interval2 prediction, FloatInterval turn, out Interval2 root)
        {
            root = default;
            FloatInterval one = FloatInterval.Singleton(1f), squared = FloatInterval.Square(turn);
            FloatInterval denominator = FloatInterval.Add(one, squared);
            if (!FloatInterval.TryDividePositive(FloatInterval.Subtract(one, squared), denominator, out FloatInterval c) ||
                !FloatInterval.TryDividePositive(FloatInterval.Multiply(FloatInterval.Singleton(2f), turn), denominator, out FloatInterval s))
                return false;
            root = new Interval2(FloatInterval.Subtract(FloatInterval.Multiply(c, prediction.X), FloatInterval.Multiply(s, prediction.Y)),
                FloatInterval.Add(FloatInterval.Multiply(s, prediction.X), FloatInterval.Multiply(c, prediction.Y)));
            return IsFinitePhaseRoot(root);
        }
    }
}
