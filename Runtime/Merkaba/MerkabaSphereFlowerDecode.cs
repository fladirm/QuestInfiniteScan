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
