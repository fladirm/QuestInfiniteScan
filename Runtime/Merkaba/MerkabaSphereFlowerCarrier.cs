using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Genesis.RoomScan
{
    public static partial class MerkabaSphereFlowerAuthority
    {
        public const int L2KnotCount = 386;
        public const int L2HubCount = 128;
        public const int L2WedgeCount = PetalClassCount * 16;
        public const int L2CarrierSiteCount = 7;
        public const int L2CarrierWedgeCount = 6;

        // These are generated incidence references, not vertices. A knot is
        // evaluated on the original level/loop returned by its source rule.
        // The colour is inherited flag incidence, not a measured shell/LOD.
        public readonly struct L2KnotRule
        {
            public readonly uint Source;
            public int Petal => (int)(Source & 63u);
            public int ParentContext => (int)((Source >> 6) & 7u);
            public int KnotSite => (int)((Source >> 9) & 7u);
            public int Colour => (int)((Source >> 12) & 3u);
            internal L2KnotRule(uint source) => Source = source;
        }

        // Rows are hub-major, then the six canonically oriented ring wedges.
        // Source identifies the original petal and its two substitution digits.
        public readonly struct L2WedgeRule
        {
            public readonly ushort Hub;
            public readonly ushort Ring0;
            public readonly ushort Ring1;
            public readonly ushort Source;
            public int Petal => Source >> 4;
            public int ChildPath => Source & 15;
            internal L2WedgeRule(ushort hub, ushort ring0, ushort ring1, ushort source)
            { Hub = hub; Ring0 = ring0; Ring1 = ring1; Source = source; }
        }

        // One row is an exact translation of all three original loop
        // identities, not a match of one shared knot or of sampled positions.
        // VertexMap transports self H/R0/R1 to the partner's H/R0/R1. The
        // canonical line basis is unchanged, so sector/root-sign travel with
        // that permutation unchanged; normals/materials are not identity.
        public readonly struct L2WedgeOwnerRule
        {
            public readonly int3 OwnerOffset;
            public readonly ushort PartnerWedge;
            public readonly byte VertexMap;
            public uint Packed => (uint)PartnerWedge | (uint)VertexMap << 10;
            internal L2WedgeOwnerRule(int3 ownerOffset, ushort partnerWedge, byte vertexMap)
            { OwnerOffset = ownerOffset; PartnerWedge = partnerWedge; VertexMap = vertexMap; }
            public int PartnerSite(int selfSite)
            {
                if ((uint)selfSite >= 3u) throw new ArgumentOutOfRangeException(nameof(selfSite));
                return (VertexMap >> (2 * selfSite)) & 3;
            }
        }

        private static class L2OwnershipData
        {
            internal static readonly ushort[] Offsets;
            internal static readonly L2WedgeOwnerRule[] Owners;
            static L2OwnershipData()
            {
#if UNITY_EDITOR
                BuildL2WedgeOwners(out Offsets, out Owners);
#else
                LoadGeneratedL2WedgeOwners(out Offsets, out Owners);
#endif
                if (Offsets.Length != L2WedgeCount + 1 || Offsets[0] != 0 ||
                    Offsets[L2WedgeCount] != Owners.Length)
                    throw new InvalidOperationException("Invalid L2 incident-owner table extent.");
                for (int wedge = 0; wedge < L2WedgeCount; wedge++)
                {
                    int first = Offsets[wedge], end = Offsets[wedge + 1];
                    bool containsSelf = false;
                    if (first >= end)
                        throw new InvalidOperationException("An L2 wedge has no canonical owner.");
                    for (int index = first; index < end; index++)
                    {
                        L2WedgeOwnerRule row = Owners[index];
                        uint sites = 0u;
                        for (int site = 0; site < 3; site++) sites |= 1u << row.PartnerSite(site);
                        if (row.PartnerWedge >= L2WedgeCount || row.VertexMap >= 64 || sites != 7u ||
                            (index != first && CompareWedgeOwners(Owners[index - 1], row) >= 0))
                            throw new InvalidOperationException("Noncanonical L2 incident-owner permutation/order.");
                        containsSelf |= math.all(row.OwnerOffset == 0) &&
                            row.PartnerWedge == wedge && row.VertexMap == 36;
                    }
                    if (!containsSelf)
                        throw new InvalidOperationException("L2 incident ownership lost its identity translation.");
                }
            }
        }

        private static class CarrierData
        {
            internal static readonly L2KnotRule[] Knots;
            internal static readonly L2WedgeRule[] Wedges;
            internal static readonly ushort[] SourceToWedge;
            internal static readonly ushort[] IncidenceOffsets;
            internal static readonly uint[] IncidenceSources;
            static CarrierData()
            {
#if UNITY_EDITOR
                BuildL2Carrier(out Knots, out Wedges, out SourceToWedge,
                    out IncidenceOffsets, out IncidenceSources);
#else
                LoadGeneratedL2Carrier(out Knots, out Wedges, out SourceToWedge,
                    out IncidenceOffsets, out IncidenceSources);
#endif
                ValidateL2Carrier(Knots, Wedges, SourceToWedge);
                ValidateL2Incidence(Knots, IncidenceOffsets, IncidenceSources);
            }
        }

        public static ReadOnlySpan<L2KnotRule> L2Knots => CarrierData.Knots;
        public static ReadOnlySpan<L2WedgeRule> L2Wedges => CarrierData.Wedges;
        public static ReadOnlySpan<ushort> L2SourceToWedge => CarrierData.SourceToWedge;
        public static ReadOnlySpan<ushort> L2IncidenceOffsets => CarrierData.IncidenceOffsets;
        public static ReadOnlySpan<uint> L2IncidenceSources => CarrierData.IncidenceSources;
        public static ReadOnlySpan<ushort> L2WedgeOwnerOffsets => L2OwnershipData.Offsets;
        public static ReadOnlySpan<L2WedgeOwnerRule> L2WedgeOwners => L2OwnershipData.Owners;

        // Ownership is per actual wedge. A carrier can own a strict subset
        // of its six wedges. Equal owner coordinates use the generated wedge
        // ordinal as the secondary tie, never source traversal order.
        public static bool L2OwnsWedge(int carrier, int wedge)
        {
            if ((uint)carrier >= L2HubCount || (uint)wedge >= L2CarrierWedgeCount)
                return false;
            int index = L2CarrierWedgeCount * carrier + wedge;
            L2WedgeOwnerRule row = L2OwnershipData.Owners[L2OwnershipData.Offsets[index]];
            return math.all(row.OwnerOffset == 0) && row.PartnerWedge == index;
        }

        public static bool TryL2CanonicalWedgeOwner(int3 owner, int carrier, int wedge,
            out int3 canonicalOwner, out int partnerWedge, out byte vertexMap)
        {
            canonicalOwner = default;
            partnerWedge = 0;
            vertexMap = 0;
            if ((uint)carrier >= L2HubCount || (uint)wedge >= L2CarrierWedgeCount)
                return false;
            int index = L2CarrierWedgeCount * carrier + wedge;
            L2WedgeOwnerRule row = L2OwnershipData.Owners[L2OwnershipData.Offsets[index]];
            long x = (long)owner.x + row.OwnerOffset.x;
            long y = (long)owner.y + row.OwnerOffset.y;
            long z = (long)owner.z + row.OwnerOffset.z;
            if (x < int.MinValue || x > int.MaxValue || y < int.MinValue || y > int.MaxValue ||
                z < int.MinValue || z > int.MaxValue) return false;
            canonicalOwner = new int3((int)x, (int)y, (int)z);
            partnerWedge = row.PartnerWedge;
            vertexMap = row.VertexMap;
            return true;
        }

        private static int CompareWedgeOwners(L2WedgeOwnerRule a, L2WedgeOwnerRule b)
        {
            int order = a.OwnerOffset.x.CompareTo(b.OwnerOffset.x);
            if (order == 0) order = a.OwnerOffset.y.CompareTo(b.OwnerOffset.y);
            if (order == 0) order = a.OwnerOffset.z.CompareTo(b.OwnerOffset.z);
            return order != 0 ? order : a.PartnerWedge.CompareTo(b.PartnerWedge);
        }

        public static bool TryGetL2KnotLoop(int knot, out int level,
            out int3 junctionOffset, out int lineClass)
        {
            level = lineClass = 0;
            junctionOffset = default;
            if ((uint)knot >= L2KnotCount) return false;
            L2KnotRule source = CarrierData.Knots[knot];
            return TryGetChildPhaseLoop(source.Petal, source.ParentContext,
                source.KnotSite, out level, out junctionOffset, out lineClass,
                out _, out _, out _, out _);
        }

        public static int L2WedgeIndex(int petal, int childPath)
        {
            if ((uint)petal >= PetalClassCount || (uint)childPath >= 16u)
                throw new ArgumentOutOfRangeException(nameof(petal));
            return CarrierData.SourceToWedge[16 * petal + childPath];
        }

        // Affine presentation chart only. It does not place a world knot or
        // change the generated barycentric skin substitution inside a wedge.
        public static float2 L2CarrierChartSite(int site) => site switch
        {
            0 => new float2(0f, 0f),
            1 => new float2(1f, 0f),
            2 => new float2(1f, 1f),
            3 => new float2(0f, 1f),
            4 => new float2(-1f, 0f),
            5 => new float2(-1f, -1f),
            6 => new float2(0f, -1f),
            _ => throw new ArgumentOutOfRangeException(nameof(site))
        };

        public static bool TryL2CarrierChartWedge(float2 uv, out int wedge,
            out float3 barycentric, out float3 derivativeU, out float3 derivativeV)
        {
            wedge = 0;
            barycentric = derivativeU = derivativeV = default;
            if (!math.all(math.isfinite(uv))) return false;
            // Boundary ties belong to the lowest incident wedge ordinal.
            wedge = uv.y >= 0f ? (uv.x >= uv.y ? 0 : uv.x >= 0f ? 1 : 2) :
                (uv.x <= uv.y ? 3 : uv.x <= 0f ? 4 : 5);
            float2 a = L2CarrierChartSite(1 + wedge);
            float2 b = L2CarrierChartSite(1 + (wedge + 1) % 6);
            float beta = uv.x * b.y - uv.y * b.x;
            float gamma = a.x * uv.y - a.y * uv.x;
            barycentric = new float3((1f - beta) - gamma, beta, gamma);
            derivativeU = new float3(a.y - b.y, b.y, -a.y);
            derivativeV = new float3(b.x - a.x, -b.x, a.x);
            return math.all(barycentric >= 0f);
        }

        // One numbering for scanner, indexed readout and export: H=0,
        // Ri=1+i. This returns an existing knot, never a vertex copy with
        // a material/normal-dependent identity.
        public static int L2CarrierKnotIndex(int carrier, int site)
        {
            if ((uint)carrier >= L2HubCount || (uint)site >= L2CarrierSiteCount)
                throw new ArgumentOutOfRangeException(nameof(carrier));
            return site == 0 ? CarrierData.Wedges[6 * carrier].Hub :
                CarrierData.Wedges[6 * carrier + site - 1].Ring0;
        }

        public static int3 L2CarrierTriangleIndices(int wedge, bool reverse = false)
        {
            if ((uint)wedge >= L2CarrierWedgeCount)
                throw new ArgumentOutOfRangeException(nameof(wedge));
            int a = 1 + wedge, b = 1 + (wedge + 1) % L2CarrierWedgeCount;
            return reverse ? new int3(0, b, a) : new int3(0, a, b);
        }

        // Shared draw/export presentation value. The symbolic root has
        // already passed interval admission; this routine selects no branch.
        internal static bool TryRootGridPosition(PhaseRootEvidence root,
            out float3 position)
        {
            position = default;
            if (root.Classification != ProofClassification.Certain ||
                !TryPhaseIdentity(root, out var tag)) return false;
            float x = root.Root.X.Midpoint;
            float y = root.Root.Y.Midpoint;
            float halfStep = 0.5f * LevelStep(tag.Level);
            float3 centre = (float3)root.Symbol.Junction * halfStep;
            LineRule line = LinesValue[tag.LineClass];
            float3 e1 = line.E1 * x;
            float3 e2 = line.E2 * y;
            float radius = EvaluateLoop(tag.Level, new Long3(0, 0, 0),
                tag.LineClass).Radius;
            float3 radial = (e1 + e2) * radius;
            position = centre + radial;
            return math.all(math.isfinite(position));
        }

        private static void ValidateL2Carrier(L2KnotRule[] knots,
            L2WedgeRule[] wedges, ushort[] inverse)
        {
            if (knots.Length != L2KnotCount || wedges.Length != L2WedgeCount ||
                inverse.Length != L2WedgeCount || wedges.Length != L2HubCount * 6)
                throw new InvalidOperationException("Invalid generated L2 carrier counts.");
            for (int hub = 0; hub < L2HubCount; hub++)
            {
                ushort anchor = wedges[6 * hub].Hub;
                if (anchor >= knots.Length || knots[anchor].Colour != 2)
                    throw new InvalidOperationException("L2 hub is not inherited corner incidence.");
                for (int ring = 0; ring < 6; ring++)
                {
                    int index = 6 * hub + ring;
                    L2WedgeRule row = wedges[index];
                    L2WedgeRule next = wedges[6 * hub + (ring + 1) % 6];
                    if (row.Hub != anchor || row.Ring0 >= knots.Length ||
                        row.Ring1 >= knots.Length || row.Ring1 != next.Ring0 ||
                        row.Source >= inverse.Length || inverse[row.Source] != index ||
                        knots[row.Ring0].Colour == 2 || knots[row.Ring1].Colour == 2 ||
                        knots[row.Ring0].Colour == knots[row.Ring1].Colour)
                        throw new InvalidOperationException("L2 ring/flag ownership is not closed.");
                    for (int other = 0; other < ring; other++)
                        if (wedges[6 * hub + other].Ring0 == row.Ring0)
                            throw new InvalidOperationException("L2 ring repeats an incidence.");
                }
            }
        }

        private static void ValidateL2Incidence(L2KnotRule[] knots,
            ushort[] offsets, uint[] sources)
        {
            if (offsets.Length != knots.Length + 1 || offsets[0] != 0 ||
                offsets[knots.Length] != sources.Length)
                throw new InvalidOperationException("Incomplete L2 knot incidence.");
            for (int knot = 0; knot < knots.Length; knot++)
            {
                int first = offsets[knot], count = offsets[knot + 1] - first;
                if (count < 1 || count > 2 || sources[first] != knots[knot].Source)
                    throw new InvalidOperationException("L2 synthesis requires one or two creation incidences.");
                for (int i = first; i < first + count; i++)
                    if (!SameL2KnotLoop(knots[knot], new L2KnotRule(sources[i])))
                        throw new InvalidOperationException("L2 incidence references a different exact loop.");
            }
        }

        private static bool SameL2KnotLoop(L2KnotRule a, L2KnotRule b)
        {
            return TryGetChildPhaseLoop(a.Petal, a.ParentContext, a.KnotSite,
                out int la, out int3 ja, out int ca, out _, out _, out _, out _) &&
                TryGetChildPhaseLoop(b.Petal, b.ParentContext, b.KnotSite,
                out int lb, out int3 jb, out int cb, out _, out _, out _, out _) &&
                la == lb && ca == cb && math.all(ja == jb);
        }

#if UNITY_EDITOR
        private readonly struct L2OwnerFootprint
        {
            internal readonly int3 Types;
            internal readonly int3 First;
            internal readonly int3 Second;
            internal readonly int3 Third;
            internal readonly int3 Sites;
            internal L2OwnerFootprint(int3 types, int3 first, int3 second, int3 third, int3 sites)
            { Types = types; First = first; Second = second; Third = third; Sites = sites; }
            internal int3 Position(int rank) => rank == 0 ? First : rank == 1 ? Second : Third;
        }

        private static void BuildL2WedgeOwners(out ushort[] offsets, out L2WedgeOwnerRule[] owners)
        {
            // At original level L the symbolic junction is 2^(L+1) K + J.
            // Q=2^(2-L) J puts only those integer identities on common L2
            // scale: two owners differ by Delta iff Qself-Qpeer=8 Delta.
            // Type (original level,line) remains part of every comparison.
            var footprints = new L2OwnerFootprint[L2WedgeCount];
            var groups = new Dictionary<(int3 Type, int3 Edge0, int3 Edge1, int3 Residue), List<int>>();
            for (int wedge = 0; wedge < L2WedgeCount; wedge++)
            {
                L2WedgeRule row = CarrierData.Wedges[wedge];
                int3 knots = new int3(row.Hub, row.Ring0, row.Ring1);
                var positions = new int3[3];
                int3 types = default, order = new int3(0, 1, 2);
                for (int site = 0; site < 3; site++)
                {
                    if (!TryGetL2KnotLoop(knots[site], out int level, out int3 junction, out int line) ||
                        (uint)level >= GeometryLevelCount || (uint)line >= LineClassCount)
                        throw new InvalidOperationException("L2 ownership requires an original exact loop.");
                    types[site] = (level << 4) | line;
                    positions[site] = junction << (2 - level);
                }
                for (int first = 0; first < 2; first++)
                    for (int second = first + 1; second < 3; second++)
                    {
                        int a = order[first], b = order[second];
                        int comparison = types[a].CompareTo(types[b]);
                        if (comparison == 0) comparison = CompareCarrierAddress(positions[a], positions[b]);
                        if (comparison == 0)
                            throw new InvalidOperationException("One L2 wedge repeats an entire loop identity.");
                        if (comparison > 0) { order[first] = b; order[second] = a; }
                    }
                var footprint = new L2OwnerFootprint(new int3(types[order.x], types[order.y], types[order.z]),
                    positions[order.x], positions[order.y], positions[order.z], order);
                footprints[wedge] = footprint;
                var key = (footprint.Types, footprint.Second - footprint.First,
                    footprint.Third - footprint.First, footprint.First & 7);
                if (!groups.TryGetValue(key, out var group))
                { group = new List<int>(); groups.Add(key, group); }
                group.Add(wedge);
            }

            var result = new List<L2WedgeOwnerRule>();
            offsets = new ushort[L2WedgeCount + 1];
            for (int wedge = 0; wedge < L2WedgeCount; wedge++)
            {
                offsets[wedge] = checked((ushort)result.Count);
                L2OwnerFootprint self = footprints[wedge];
                var group = groups[(self.Types, self.Second - self.First,
                    self.Third - self.First, self.First & 7)];
                var rows = new List<L2WedgeOwnerRule>(group.Count);
                foreach (int partner in group)
                {
                    L2OwnerFootprint peer = footprints[partner];
                    int3 difference = self.First - peer.First;
                    if (math.any((difference & 7) != 0))
                        throw new InvalidOperationException("An incident owner is not on the M8 lattice.");
                    int3 delta = difference / 8;
                    // The full matched facet must also have the same
                    // original R1 endpoint relation. Admission at a peer
                    // cannot substitute some other source face just because
                    // one terminal loop happens to coincide.
                    NodeRule selfFace = NodesValue[PetalsValue[CarrierData.Wedges[wedge].Petal].Node(0)];
                    NodeRule peerFace = NodesValue[PetalsValue[CarrierData.Wedges[partner].Petal].Node(0)];
                    if (selfFace.LineClass != peerFace.LineClass ||
                        math.any(selfFace.Direction != peerFace.Direction + 2 * delta))
                        throw new InvalidOperationException("A translated whole petal changes its stable R1 source relation.");
                    int vertexMap = 0;
                    for (int rank = 0; rank < 3; rank++)
                    {
                        int level = self.Types[rank] >> 4;
                        int shift = 2 - level;
                        if (self.Types[rank] != peer.Types[rank] ||
                            math.any((self.Position(rank) >> shift) !=
                                (peer.Position(rank) >> shift) + (delta << (level + 1))))
                            throw new InvalidOperationException("Incident owners do not share the complete three-loop petal.");
                        vertexMap |= peer.Sites[rank] << (2 * self.Sites[rank]);
                    }
                    rows.Add(new L2WedgeOwnerRule(delta, checked((ushort)partner), checked((byte)vertexMap)));
                }
                rows.Sort(CompareWedgeOwners);
                result.AddRange(rows);
            }
            offsets[L2WedgeCount] = checked((ushort)result.Count);
            owners = result.ToArray();

            // Exhaustive group closure proves canonical ownership is the
            // same from every incident owner and preserves all three slots.
            for (int wedge = 0; wedge < L2WedgeCount; wedge++)
            {
                L2WedgeOwnerRule canonical = owners[offsets[wedge]];
                for (int index = offsets[wedge]; index < offsets[wedge + 1]; index++)
                {
                    L2WedgeOwnerRule incidence = owners[index];
                    L2WedgeOwnerRule peerCanonical = owners[offsets[incidence.PartnerWedge]];
                    if (canonical.PartnerWedge != peerCanonical.PartnerWedge ||
                        math.any(canonical.OwnerOffset != incidence.OwnerOffset + peerCanonical.OwnerOffset))
                        throw new InvalidOperationException("L2 owner translations do not have a unique canonical representative.");
                    for (int site = 0; site < 3; site++)
                        if (canonical.PartnerSite(site) != peerCanonical.PartnerSite(incidence.PartnerSite(site)))
                            throw new InvalidOperationException("L2 owner translation changes a canonical sector/root-sign slot.");
                    bool inverseFound = false;
                    for (int other = offsets[incidence.PartnerWedge];
                        other < offsets[incidence.PartnerWedge + 1]; other++)
                    {
                        L2WedgeOwnerRule inverse = owners[other];
                        if (inverse.PartnerWedge != wedge ||
                            math.any(inverse.OwnerOffset != -incidence.OwnerOffset)) continue;
                        for (int site = 0; site < 3; site++)
                            if (inverse.PartnerSite(incidence.PartnerSite(site)) != site)
                                throw new InvalidOperationException("L2 owner translation is not invertible.");
                        inverseFound = true;
                    }
                    if (!inverseFound)
                        throw new InvalidOperationException("An L2 incident-owner translation has no inverse.");
                }
            }
        }

        private readonly struct CarrierTriangle
        {
            internal readonly int3 Knots;
            internal readonly int Petal;
            internal readonly int Path;
            internal CarrierTriangle(int3 knots, int petal, int path)
            { Knots = knots; Petal = petal; Path = path; }
        }

        private static void BuildL2Carrier(out L2KnotRule[] knots,
            out L2WedgeRule[] wedges, out ushort[] inverse,
            out ushort[] incidenceOffsets, out uint[] incidenceSources)
        {
            // All addresses use the final half-grid scale. This dictionary is
            // codegen-only exact incidence deduplication; runtime does no search.
            var addresses = new List<int3>(L2KnotCount);
            var rules = new List<L2KnotRule>(L2KnotCount);
            var incidence = new List<List<uint>>(L2KnotCount);
            var indexByAddress = new Dictionary<int3, int>(L2KnotCount);
            int Add(int3 address, int colour, int petal, int context, int site)
            {
                uint source = (uint)(petal | context << 6 | site << 9 | colour << 12);
                var rule = new L2KnotRule(source);
                if (indexByAddress.TryGetValue(address, out int existing))
                {
                    L2KnotRule previous = rules[existing];
                    if (previous.Colour != colour || !SameL2KnotLoop(previous, rule))
                        throw new InvalidOperationException("Child substitution aliases unlike Flower knots.");
                    // Root/L1 records already have one canonical node/strand
                    // key. L2 creation in distinct parent contexts retains
                    // both predictions for section 10.6 interval closure.
                    if (context != 0 && !incidence[existing].Contains(source))
                        incidence[existing].Add(source);
                    return existing;
                }
                int index = rules.Count;
                indexByAddress.Add(address, index);
                addresses.Add(address);
                rules.Add(rule);
                incidence.Add(new List<uint>(2) { source });
                return index;
            }

            var current = new List<CarrierTriangle>(PetalClassCount);
            for (int petal = 0; petal < PetalClassCount; petal++)
            {
                PetalRule flag = PetalsValue[petal];
                int3 node = default;
                for (int corner = 0; corner < 3; corner++)
                    node[corner] = Add(4 * NodesValue[flag.Node(corner)].Direction,
                        corner, petal, 0, corner);
                current.Add(new CarrierTriangle(node, petal, 0));
            }
            for (int level = 1; level <= 2; level++)
            {
                var children = new List<CarrierTriangle>(current.Count * 4);
                foreach (CarrierTriangle parent in current)
                {
                    int[] sites = { parent.Knots.x, parent.Knots.y, parent.Knots.z, 0, 0, 0 };
                    for (int edge = 0; edge < 3; edge++)
                    {
                        int first = sites[edge], second = sites[(edge + 1) % 3];
                        int3 sum = addresses[first] + addresses[second];
                        if (math.any((sum & 1) != 0) || rules[first].Colour == rules[second].Colour)
                            throw new InvalidOperationException("Non-dyadic or uncoloured Flower edge.");
                        sites[3 + edge] = Add(sum / 2,
                            3 - rules[first].Colour - rules[second].Colour,
                            parent.Petal, level == 1 ? 0 : parent.Path + 1, edge + 3);
                    }
                    for (int child = 0; child < ChildPetalCount; child++)
                    {
                        int3 node = default;
                        for (int corner = 0; corner < 3; corner++)
                        {
                            int3 weights = ChildCoefficients(ChildPetalsValue[child].Vertex(corner));
                            int site = weights.x == 2 ? 0 : weights.y == 2 ? 1 :
                                weights.z == 2 ? 2 : weights.z == 0 ? 3 : weights.x == 0 ? 4 : 5;
                            node[corner] = sites[site];
                        }
                        children.Add(new CarrierTriangle(node, parent.Petal,
                            4 * parent.Path + child));
                    }
                }
                current = children;
            }
            knots = rules.ToArray();
            var hubNodes = new List<int>(L2HubCount);
            var incident = new List<CarrierTriangle>[knots.Length];
            foreach (CarrierTriangle triangle in current)
            {
                int h = -1;
                for (int corner = 0; corner < 3; corner++)
                    if (knots[triangle.Knots[corner]].Colour == 2)
                    {
                        if (h >= 0) throw new InvalidOperationException("Two hubs in one L2 flag.");
                        h = triangle.Knots[corner];
                    }
                if (h < 0) throw new InvalidOperationException("L2 flag has no hub.");
                if (incident[h] == null) { incident[h] = new List<CarrierTriangle>(6); hubNodes.Add(h); }
                incident[h].Add(triangle);
            }
            hubNodes.Sort((a, b) => CompareCarrierAddress(addresses[a], addresses[b]));
            var ordered = new List<L2WedgeRule>(L2WedgeCount);
            inverse = new ushort[L2WedgeCount];
            Array.Fill(inverse, ushort.MaxValue);
            foreach (int hub in hubNodes)
            {
                var next = new Dictionary<int, (int Ring, int Source)>(6);
                int start = -1;
                foreach (CarrierTriangle triangle in incident[hub])
                {
                    int3 node = triangle.Knots;
                    int h = node.x == hub ? 0 : node.y == hub ? 1 : 2;
                    int a = node[(h + 1) % 3], b = node[(h + 2) % 3];
                    if (PetalsValue[triangle.Petal].Orientation < 0) (a, b) = (b, a);
                    next.Add(a, (b, 16 * triangle.Petal + triangle.Path));
                    if (knots[a].Colour == 0 && (start < 0 ||
                        CompareCarrierAddress(addresses[a], addresses[start]) < 0)) start = a;
                }
                if (next.Count != 6 || start < 0)
                    throw new InvalidOperationException("A generated L2 hub must have six ring neighbours.");
                int ring = start;
                for (int wedge = 0; wedge < 6; wedge++)
                {
                    var step = next[ring];
                    if (inverse[step.Source] != ushort.MaxValue)
                        throw new InvalidOperationException("An L2 flag is owned by two hubs.");
                    inverse[step.Source] = checked((ushort)ordered.Count);
                    ordered.Add(new L2WedgeRule(checked((ushort)hub), checked((ushort)ring),
                        checked((ushort)step.Ring), checked((ushort)step.Source)));
                    ring = step.Ring;
                }
                if (ring != start) throw new InvalidOperationException("L2 ring does not close.");
            }
            wedges = ordered.ToArray();
            var allSources = new List<uint>(L2KnotCount * 2);
            incidenceOffsets = new ushort[knots.Length + 1];
            for (int knot = 0; knot < knots.Length; knot++)
            {
                incidenceOffsets[knot] = checked((ushort)allSources.Count);
                allSources.AddRange(incidence[knot]);
            }
            incidenceOffsets[knots.Length] = checked((ushort)allSources.Count);
            incidenceSources = allSources.ToArray();
        }
        private static int CompareCarrierAddress(int3 a, int3 b) =>
            a.x != b.x ? a.x.CompareTo(b.x) : a.y != b.y ? a.y.CompareTo(b.y) : a.z.CompareTo(b.z);
#endif
    }
}
