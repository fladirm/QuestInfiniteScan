using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Genesis.RoomScan
{
    public static partial class MerkabaSphereFlowerAuthority
    {
        public const int JunctionClassCount = 12;

        // The four equivalent starting flags identify ONE junction class.
        // Fields are indexed by R3 line class minus nine, not tetra-axis order.
        public readonly struct JunctionRule
        {
            public readonly uint Corners;
            public readonly uint Flags;
            public JunctionRule(uint corners, uint flags) { Corners = corners; Flags = flags; }
            public int Corner(int line) => (int)((Corners >> (5 * (line - 9))) & 31u);
            public int Flag(int line) => (int)((Flags >> (6 * (line - 9))) & 63u);
            public int Representative => (int)((Flags >> 24) & 63u);
            public int Chirality => (Flags & (1u << 30)) == 0u ? 1 : -1;
        }

        private static class JunctionData
        {
#if UNITY_EDITOR
            internal static readonly JunctionRule[] Rules = BuildJunctionRules();
#else
            internal static readonly JunctionRule[] Rules = LoadGeneratedJunctionRules();
#endif
        }

        public static ReadOnlySpan<JunctionRule> JunctionRules => JunctionData.Rules;

        internal enum JunctionClassification : uint
        {
            Impossible = 0,
            CertainClosure = 1,
            Ambiguous = 2,
            DetailCandidate = 3
        }

        internal struct JunctionSelection
        {
            internal JunctionClassification Classification;
            internal uint ClassIndex;
            internal uint RootSigns;
            internal uint CertainCount;
            internal uint AmbiguousCount;
            internal uint DetailCount;
            internal FloatInterval Scalar;
            internal Interval3 Vector;
        }

        // q already includes the actual TetraFrame eta. Each known alternative
        // must have come from certain prediction -> non-provisional direct SEAL
        // on the same Sigma. A missing persistent innovation is NOT known zero.
        // Index is 2*axis+algebraicSign (minus=0, plus=1). Allowed/veto are
        // explicit direct/dual constraints on these roots, NOT a whole-petal
        // footprint certificate. Completion must still prove its own footprint.
        internal static JunctionSelection SelectR3Junction(int parity,
            ReadOnlySpan<FloatInterval> q, ReadOnlySpan<uint> observedTags,
            uint knownMask, uint ambiguousMask, uint allowedMask, uint vetoMask,
            bool incidentReferences = false, int requiredPetal = -1)
        {
            var result = new JunctionSelection { Classification = JunctionClassification.Ambiguous,
                ClassIndex = uint.MaxValue, RootSigns = uint.MaxValue };
            if ((uint)parity >= TetraFrameCount || q.Length != 8 || observedTags.Length != 8 ||
                (requiredPetal != -1 && (uint)requiredPetal >= PetalClassCount) ||
                ((knownMask | ambiguousMask | allowedMask | vetoMask) & ~255u) != 0u ||
                (knownMask & ambiguousMask) != 0u) return result;

            TetraFrameRule frame = TetraFramesValue[parity];
            Span<ulong> rootFlags = stackalloc ulong[8];
            for (int alternative = 0; alternative < 8; alternative++)
            {
                rootFlags[alternative] = 0;
                uint bit = 1u << alternative;
                if ((knownMask & bit) == 0u) continue;
                int axis = alternative >> 1, line = frame.LineClasses[axis];
                int node = 2 * line + (frame.Eta[axis] < 0 ? 1 : 0);
                uint tag = observedTags[alternative];
                if ((tag & 7u) >= GeometryLevelCount || ((tag >> 3) & 15u) != line ||
                    ((tag >> 7) & 1u) != (alternative & 1) || !JunctionFinite(q[alternative]) ||
                    !TryGetAnchorRootFlags(node, tag, out ulong flags))
                {
                    knownMask &= ~bit;
                    ambiguousMask |= bit;
                    continue;
                }
                // Completion may reference the closed incidence of its
                // already-confirmed donor. This does not alter root ownership
                // and cannot make a numerical boundary touch admissible.
                if (incidentReferences && TryGetAnchorBoundaryReferences(node, tag, out ulong references))
                    flags |= references;
                rootFlags[alternative] = flags;
            }

            uint selectedClass = uint.MaxValue, selectedSigns = uint.MaxValue;
            FloatInterval selectedScalar = default;
            Interval3 selectedVector = default;
            Span<FloatInterval> bundle = stackalloc FloatInterval[4];
            for (uint signs = 0; signs < 16u; signs++)
            {
                uint required = 0u;
                for (int axis = 0; axis < 4; axis++)
                    required |= 1u << (2 * axis + (int)((signs >> axis) & 1u));
                if ((required & vetoMask) != 0u || (required & ~(knownMask | ambiguousMask)) != 0u)
                    continue;
                bool unresolved = (required & knownMask & allowedMask) != required;
                bool metricDetail = false;
                FloatInterval scalar = default;
                Interval3 vector = default;
                if ((required & knownMask) == required)
                {
                    uint level = uint.MaxValue;
                    for (int axis = 0; axis < 4; axis++)
                    {
                        int alternative = 2 * axis + (int)((signs >> axis) & 1u);
                        bundle[axis] = q[alternative];
                        metricDetail |= !bundle[axis].ContainsZero;
                        uint axisLevel = observedTags[alternative] & 7u;
                        if (axis != 0 && axisLevel != level) unresolved = true;
                        level = axisLevel;
                    }
                    TetraForwardIntervals(bundle, out scalar, out vector);
                    if (!JunctionFinite(scalar) || !JunctionFinite(vector.X) ||
                        !JunctionFinite(vector.Y) || !JunctionFinite(vector.Z)) unresolved = true;
                    metricDetail |= !scalar.ContainsZero || !vector.X.ContainsZero ||
                        !vector.Y.ContainsZero || !vector.Z.ContainsZero;
                }
                int branchChirality = frame.Chirality * ((math.countbits(signs) & 1) == 0 ? 1 : -1);
                for (int candidate = 0; candidate < JunctionClassCount; candidate++)
                {
                    JunctionRule rule = JunctionData.Rules[candidate];
                    // Section13 asks for closure of THIS flag. Unrelated
                    // junction classes are not competing completions of it.
                    if (requiredPetal >= 0 && rule.Flag(9) != requiredPetal &&
                        rule.Flag(10) != requiredPetal && rule.Flag(11) != requiredPetal &&
                        rule.Flag(12) != requiredPetal) continue;
                    if (rule.Chirality != branchChirality) continue;
                    bool admitted = true;
                    for (int axis = 0; axis < 4; axis++)
                    {
                        int line = frame.LineClasses[axis];
                        int node = 2 * line + (frame.Eta[axis] < 0 ? 1 : 0);
                        int alternative = 2 * axis + (int)((signs >> axis) & 1u);
                        if (rule.Corner(line) != node || ((knownMask & (1u << alternative)) != 0u &&
                            (rootFlags[alternative] & (1UL << rule.Flag(line))) == 0u)) admitted = false;
                    }
                    if (!admitted) continue;
                    if (unresolved) result.AmbiguousCount++;
                    else
                    {
                        if (metricDetail) result.DetailCount++;
                        else result.CertainCount++;
                        // This is only a tentative result. No candidate escapes
                        // until all classes and all sixteen branches were read.
                        selectedClass = (uint)candidate; selectedSigns = signs;
                        selectedScalar = scalar; selectedVector = vector;
                    }
                }
            }
            uint certain = result.CertainCount + result.DetailCount;
            if (result.AmbiguousCount != 0u || certain > 1u) return result;
            if (certain == 0u) { result.Classification = JunctionClassification.Impossible; return result; }
            result.Classification = result.CertainCount == 1u
                ? JunctionClassification.CertainClosure : JunctionClassification.DetailCandidate;
            result.ClassIndex = selectedClass; result.RootSigns = selectedSigns;
            result.Scalar = selectedScalar; result.Vector = selectedVector;
            return result;
        }

        private static bool JunctionFinite(FloatInterval value) =>
            math.isfinite(value.Lower) && math.isfinite(value.Upper) && value.Lower <= value.Upper;

#if UNITY_EDITOR
        public static JunctionRule[] BuildJunctionRules()
        {
            var rules = new List<JunctionRule>(JunctionClassCount);
            var identity = new Dictionary<ulong, int>();
            var multiplicity = new List<int>(JunctionClassCount);
            int canonicalChirality = Math.Sign(Determinant(TetraAxesInteger[1] - TetraAxesInteger[0],
                TetraAxesInteger[2] - TetraAxesInteger[0], TetraAxesInteger[3] - TetraAxesInteger[0]));
            foreach (PetalRule source in PetalsValue)
            {
                // f ⊂ e ⊂ c fixes the whole ordered signed Cartesian frame,
                // not merely one corner or a manually chosen sign tuple.
                int3 a = NodesValue[source.FaceNode].Direction;
                int3 b = NodesValue[source.EdgeNode].Direction - a;
                int3 c = NodesValue[source.CornerNode].Direction - a - b;
                int determinant = Determinant(a, b, c);
                if (Dot(a, a) != 1 || Dot(b, b) != 1 || Dot(c, c) != 1 ||
                    Dot(a, b) != 0 || Dot(a, c) != 0 || Dot(b, c) != 0 ||
                    Math.Abs(determinant) != 1 || determinant != source.Orientation)
                    throw new InvalidOperationException("Junction flag does not induce a signed Cartesian frame.");
                uint corners = 0u, flags = 0u;
                ulong quartet = 0;
                int representative = PetalClassCount;
                var actual = new int3[4];
                uint lines = 0u;
                for (int axis = 0; axis < 4; axis++)
                {
                    int3 t = TetraAxesInteger[axis];
                    int3 face = t.x * a, edge = face + t.y * b;
                    actual[axis] = edge + t.z * c;
                    int faceNode = FindNode(NodesValue, face), edgeNode = FindNode(NodesValue, edge);
                    int cornerNode = FindNode(NodesValue, actual[axis]);
                    int petal = -1;
                    for (int p = 0; p < PetalsValue.Length; p++)
                    {
                        PetalRule flag = PetalsValue[p];
                        if (flag.FaceNode != faceNode || flag.EdgeNode != edgeNode || flag.CornerNode != cornerNode)
                            continue;
                        if (petal >= 0) throw new InvalidOperationException("Junction transported flag is not unique.");
                        petal = p;
                    }
                    if (petal < 0 || NodesValue[cornerNode].Shell != Shell.R3Closure)
                        throw new InvalidOperationException("Junction transported flag/corner is absent from Flower incidence.");
                    PetalRule transported = PetalsValue[petal];
                    for (int e = 0; e < 3; e++)
                    {
                        StrandRule strand = StrandsValue[transported.Strand(e)];
                        if ((strand.Petal0 != petal && strand.Petal1 != petal) ||
                            (NodeIncidentPetalsValue[strand.Node0] & NodeIncidentPetalsValue[strand.Node1] &
                                (1UL << petal)) == 0)
                            throw new InvalidOperationException("Junction transported flag lost its actual strand boundary.");
                    }
                    int line = NodesValue[cornerNode].LineClass - 9;
                    if ((uint)line >= 4u || (lines & (1u << line)) != 0u)
                        throw new InvalidOperationException("Junction must contain exactly four distinct R3 loop families.");
                    lines |= 1u << line;
                    corners |= (uint)cornerNode << (5 * line);
                    flags |= (uint)petal << (6 * line);
                    quartet |= 1UL << petal;
                    representative = Math.Min(representative, petal);
                }
                int chirality = Math.Sign(Determinant(actual[1] - actual[0],
                    actual[2] - actual[0], actual[3] - actual[0]));
                if (lines != 15u || chirality != determinant * canonicalChirality ||
                    math.countbits((uint)quartet) + math.countbits((uint)(quartet >> 32)) != 4)
                    throw new InvalidOperationException("Junction tetra transport failed its oriented incidence proof.");
                flags |= (uint)representative << 24;
                if (chirality < 0) flags |= 1u << 30;
                if (identity.TryGetValue(quartet, out int existing))
                {
                    JunctionRule prior = rules[existing];
                    if (prior.Corners != corners || prior.Flags != flags)
                        throw new InvalidOperationException("Equivalent junction frames disagree on their frozen class/chirality.");
                    multiplicity[existing]++;
                }
                else
                {
                    identity.Add(quartet, rules.Count);
                    rules.Add(new JunctionRule(corners, flags));
                    multiplicity.Add(1);
                }
            }
            if (rules.Count != JunctionClassCount)
                throw new InvalidOperationException("Flower incidence must induce twelve distinct tetra junction classes.");
            foreach (int count in multiplicity)
                if (count != 4) throw new InvalidOperationException("A junction must have four equivalent starting flags.");
            for (int parity = 0; parity < TetraFrameCount; parity++)
            {
                TetraFrameRule frame = TetraFramesValue[parity];
                int positive = 0, negative = 0;
                foreach (JunctionRule rule in rules)
                {
                    bool same = true;
                    for (int axis = 0; axis < 4; axis++)
                        same &= rule.Corner(frame.LineClasses[axis]) ==
                            2 * frame.LineClasses[axis] + (frame.Eta[axis] < 0 ? 1 : 0);
                    if (!same) continue;
                    if (rule.Chirality > 0) positive++; else negative++;
                }
                if (positive != 3 || negative != 3)
                    throw new InvalidOperationException("Junction classes do not reproduce the actual parity TetraFrame.");
            }
            return rules.ToArray();
        }
#endif
    }
}
