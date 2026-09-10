using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Genesis.RoomScan
{
    public static partial class MerkabaSphereFlowerAuthority
    {
        // Inverse of the generated child-flag incidence, not SectorPetalMask
        // ownership. Each row is an actual R1 child loop; peers are independent
        // R1 loops in the same construction. Row ordinals are immutable lookup
        // indices only, never persisted branch ranks or refinement cursors.
        private sealed class R1WitnessDecodeTables
        {
            internal readonly uint4[] Loops, Directions, Peers;
            internal R1WitnessDecodeTables(uint4[] loops, uint4[] directions, uint4[] peers)
            { Loops = loops; Directions = directions; Peers = peers; }
        }

        private static class R1WitnessDecodeData
        {
#if UNITY_EDITOR
            internal static readonly R1WitnessDecodeTables Tables = BuildR1WitnessDecode();
#else
            internal static readonly R1WitnessDecodeTables Tables = new(
                LoadGeneratedR1WitnessLoops(), LoadGeneratedR1WitnessDirections(),
                LoadGeneratedR1WitnessPeers());
#endif
        }

        internal static ReadOnlySpan<uint4> R1WitnessLoops => R1WitnessDecodeData.Tables.Loops;
        internal static ReadOnlySpan<uint4> R1WitnessDirections => R1WitnessDecodeData.Tables.Directions;
        internal static ReadOnlySpan<uint4> R1WitnessPeers => R1WitnessDecodeData.Tables.Peers;

        private static class CarrierDecodeData
        {
#if UNITY_EDITOR
            internal static readonly uint4[] Petals, Carriers;
            internal static readonly uint4[] Triples = BuildCarrierTripleMasks();
            static CarrierDecodeData() => BuildCarrierReach(out Petals, out Carriers);
#else
            internal static readonly uint4[] Petals = LoadGeneratedDecodePetalCarriers();
            internal static readonly uint4[] Carriers = LoadGeneratedDecodeCarrierPetals();
            internal static readonly uint4[] Triples = LoadGeneratedCarrierTripleMasks();
#endif
        }

        internal static ReadOnlySpan<uint4> DecodePetalCarriers => CarrierDecodeData.Petals;
        // xy: source-petal incidence; z: union of those sources' original
        // anchor nodes. Generated addressing metadata, not another root set.
        internal static ReadOnlySpan<uint4> DecodeCarrierPetals => CarrierDecodeData.Carriers;
        internal static ReadOnlySpan<uint4> CarrierTripleMasks => CarrierDecodeData.Triples;

        private static class ChildLoopDecodeData
        {
#if UNITY_EDITOR
            internal static readonly uint4[] Addresses;
            internal static readonly uint[] Creations;
            static ChildLoopDecodeData() => BuildChildLoopDecode(out Addresses, out Creations);
#else
            internal static readonly uint4[] Addresses = LoadGeneratedChildLoopAddresses();
            internal static readonly uint[] Creations = LoadGeneratedChildLoopCreations();
#endif
        }

        internal static ReadOnlySpan<uint4> ChildLoopAddresses => ChildLoopDecodeData.Addresses;
        internal static ReadOnlySpan<uint> ChildLoopCreations => ChildLoopDecodeData.Creations;

        internal static bool TryGetChildPhaseLoop(int petalClass, int parentContext,
            int knotSite, out int level, out int3 junctionOffset, out int lineClass,
            out int strandClass, out sbyte endpointOrientation,
            out sbyte phaseOrientation, out int inheritedParentNode)
        {
            level = lineClass = strandClass = 0;
            junctionOffset = default;
            endpointOrientation = phaseOrientation = 0;
            inheritedParentNode = -1;
            if ((uint)petalClass >= PetalClassCount ||
                (uint)parentContext >= ChildPhaseParentContextCount ||
                (uint)knotSite >= ChildPhaseKnotSiteCount) return false;
            uint4 row = ChildLoopAddresses[(petalClass * 5 + parentContext) * 6 + knotSite];
            junctionOffset = math.asint(row.xyz);
            level = (int)(row.w & 3u);
            lineClass = (int)((row.w >> 2) & 15u);
            strandClass = (int)((row.w >> 6) & 127u);
            endpointOrientation = (sbyte)((row.w & (1u << 13)) != 0u ? -1 : 1);
            phaseOrientation = (sbyte)((int)((row.w >> 14) & 3u) - 1);
            inheritedParentNode = (int)((row.w >> 16) & 3u) - 1;
            return true;
        }

        // A compact iterator over reached immutable identities. No search
        // ordinal is retained in the world or across observation boundaries.
        internal static bool TakeReachedCarrier(ref uint4 pending, out int carrier)
        {
            carrier = -1;
            if (math.all(pending == 0u)) return false;
            int word = pending.x != 0u ? 0 : pending.y != 0u ? 1 : pending.z != 0u ? 2 : 3;
            carrier = 32 * word + math.tzcnt(pending[word]);
            pending[word] &= pending[word] - 1u;
            return true;
        }

        internal static uint3 ReachedR1WitnessLoops(uint2 witness)
        {
            uint3 reached = default;
            for (int direction = 0; direction < 6; direction++)
                if ((((witness.x | witness.y) >> (5 * direction)) & 31u) != 0u)
                    reached |= R1WitnessDirections[direction].xyz;
            return reached;
        }

        internal static bool R1FlagWitness(uint2 witness, uint plane,
            float normalError, float offsetError)
        {
            uint3 pending = ReachedR1WitnessLoops(witness), admitted = default;
            for (int word = 0; word < 3; word++)
                while (pending[word] != 0u)
                {
                    int bit = math.tzcnt(pending[word]), index = 32 * word + bit;
                    pending[word] &= pending[word] - 1u;
                    uint4 row = R1WitnessLoops[index];
                    int level = (int)(row.w & 3u), direction = (int)(row.w >> 2);
                    bool certain = false;
                    for (int sign = 0; sign < 2; sign++)
                    {
                        uint expected = (witness[sign] >> (5 * direction)) & 31u;
                        if (expected == 0u) continue;
                        if (CarrierRootProof(int3.zero, plane, level, math.asint(row.xyz),
                                direction >> 1, sign != 0, normalError, offsetError, out var root) ==
                                ProofClassification.Certain &&
                            ((root.Symbol.Tag >> 8) & 31u) == expected - 1u)
                            certain = true;
                    }
                    if (!certain) continue;
                    if (math.any((R1WitnessPeers[index].xyz & admitted) != 0u)) return true;
                    admitted[word] |= 1u << bit;
                }
            return false;
        }

#if UNITY_EDITOR
        private static void BuildChildLoopDecode(out uint4[] rows, out uint[] creation)
        {
            rows = new uint4[PetalClassCount * 5 * 6];
            creation = new uint[rows.Length];
            for (int petal = 0; petal < PetalClassCount; petal++)
            for (int parent = 0; parent < 5; parent++)
            for (int site = 0; site < 6; site++)
            {
                if (!ComputeChildPhaseLoop(petal, parent, site,
                    out int level, out int3 offset, out int lineClass, out int strand,
                    out sbyte endpoint, out sbyte phase, out int inherited) ||
                    (uint)level > 2u || (uint)lineClass >= 13u || (uint)strand >= 72u ||
                    (endpoint != 1 && endpoint != -1) || phase < -1 || phase > 1 ||
                    inherited < -1 || inherited > 2)
                    throw new InvalidOperationException("Invalid generated child loop address.");
                uint tag = (uint)level | ((uint)lineClass << 2) | ((uint)strand << 6) |
                    (endpoint < 0 ? 1u << 13 : 0u) | ((uint)(phase + 1) << 14) |
                    ((uint)(inherited + 1) << 16);
                int index = (petal * 5 + parent) * 6 + site;
                rows[index] = new uint4(math.asuint(offset), tag);
                if (level != 0 && inherited >= 0) continue;
                int recordPetal = petal, path = 0, rootNode;
                if (level == 0)
                {
                    rootNode = PetalsValue[petal].Node(site);
                    ulong incidence = NodeIncidentPetalsValue[rootNode];
                    recordPetal = (uint)incidence != 0u ? math.tzcnt((uint)incidence) :
                        32 + math.tzcnt((uint)(incidence >> 32));
                }
                else
                {
                    PhaseFamilyRule family = PhaseFamiliesValue[strand];
                    rootNode = family.RootNode;
                    if (level == 1)
                    {
                        recordPetal = StrandsValue[strand].Petal0;
                        path = family.FinePath0;
                    }
                    else path = 4 * (parent - 1) + (site == 4 ? 1 : 0);
                }
                if ((uint)recordPetal >= 48u || (uint)path >= 16u || (uint)rootNode >= 26u)
                    throw new InvalidOperationException("Invalid generated canonical creation key.");
                creation[index] = (uint)recordPetal | ((uint)path << 6) | ((uint)rootNode << 10) |
                    (LinesValue[lineClass].Shell == Shell.R3Closure ? 1u << 15 : 0u) | 0x80000000u;
            }
        }

        private static void BuildCarrierReach(out uint4[] petals, out uint4[] carriers)
        {
            petals = new uint4[PetalClassCount];
            carriers = new uint4[L2HubCount];
            for (int petal = 0; petal < PetalClassCount; petal++)
                for (int path = 0; path < 16; path++)
                {
                    int wedge = L2WedgeIndex(petal, path), carrier = wedge / 6;
                    if (L2Wedges[wedge].Source != 16 * petal + path)
                        throw new InvalidOperationException("Broken child-to-carrier inverse incidence.");
                    petals[petal][carrier >> 5] |= 1u << (carrier & 31);
                    carriers[carrier][petal >> 5] |= 1u << (petal & 31);
                    PetalRule source = PetalsValue[petal];
                    carriers[carrier].z |= (1u << source.Node(0)) |
                        (1u << source.Node(1)) | (1u << source.Node(2));
                }
        }

        private static uint4[] BuildCarrierTripleMasks()
        {
            var masks = new uint4[6 * 8];
            for (int wedge = 0; wedge < 6; wedge++)
            {
                for (uint signs = 0; signs < 128u; signs++)
                    masks[8 * wedge + (int)CarrierTriple(signs, wedge)][(int)(signs >> 5)] |=
                        1u << (int)(signs & 31u);
                uint4 covered = 0u;
                for (int triple = 0; triple < 8; triple++)
                {
                    uint4 mask = masks[8 * wedge + triple];
                    if (math.any((covered & mask) != 0u) || math.csum(math.countbits(mask)) != 16)
                        throw new InvalidOperationException("Carrier triple inverse is not a partition.");
                    covered |= mask;
                }
                if (math.any(covered != uint.MaxValue))
                    throw new InvalidOperationException("Carrier triple inverse lost a root-sign assignment.");
            }
            return masks;
        }

        private static R1WitnessDecodeTables BuildR1WitnessDecode()
        {
            var indices = new Dictionary<uint4, int>();
            var loops = new List<uint4>();
            var pairs = new List<int2>();
            Span<int> child = stackalloc int[3];
            for (int petal = 0; petal < PetalClassCount; petal++)
                for (int part = 0; part < 4; part++)
                {
                    int count = 0;
                    for (int node = 0; node < 3; node++)
                    {
                        if (!TryGetChildPhaseLoop(petal, part + 1, node, out int level,
                                out int3 junction, out int line, out _, out sbyte endpoint,
                                out _, out _))
                            throw new InvalidOperationException("Missing generated R1 child incidence.");
                        if (line >= 3) continue;
                        if (level > 1 || (endpoint != -1 && endpoint != 1))
                            throw new InvalidOperationException("Invalid inherited R1 witness loop.");
                        int direction = 2 * line + (endpoint < 0 ? 1 : 0);
                        var key = new uint4(math.asuint(junction), (uint)(level | (direction << 2)));
                        if (!indices.TryGetValue(key, out int index))
                        {
                            index = loops.Count;
                            indices.Add(key, index);
                            loops.Add(key);
                        }
                        child[count++] = index;
                    }
                    for (int a = 0; a < count; a++)
                        for (int b = a + 1; b < count; b++)
                            if ((loops[child[a]].w >> 3) != (loops[child[b]].w >> 3))
                                pairs.Add(new int2(child[a], child[b]));
                }
            if (loops.Count > 96)
                throw new InvalidOperationException("R1 witness incidence exceeds its proven 96-bit domain.");
            var peers = new uint4[loops.Count];
            foreach (int2 pair in pairs)
            {
                peers[pair.x][pair.y >> 5] |= 1u << (pair.y & 31);
                peers[pair.y][pair.x >> 5] |= 1u << (pair.x & 31);
            }
            var directions = new uint4[6];
            for (int index = 0; index < loops.Count; index++)
                if (math.any(peers[index].xyz != 0u))
                    directions[loops[index].w >> 2][index >> 5] |= 1u << (index & 31);
            return new R1WitnessDecodeTables(loops.ToArray(), directions, peers);
        }
#endif
    }
}
