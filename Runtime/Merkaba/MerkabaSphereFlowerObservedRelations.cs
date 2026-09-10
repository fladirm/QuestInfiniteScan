using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Genesis.RoomScan
{
    public static partial class MerkabaSphereFlowerAuthority
    {
        // Inverse incidence from a measured dyadic support cell to its phase
        // relations. Enumerating the alphabet belongs to Editor codegen only.
        // These indices are immutable addresses, never saved work cursors.
        private static class ObservedRelationData
        {
#if UNITY_EDITOR
            internal static readonly uint4[] Relations, Cells, Levels;
            internal static readonly uint[] References;
            static ObservedRelationData() => BuildObservedRelations(
                out Relations, out Cells, out Levels, out References);
#else
            internal static readonly uint4[] Relations = LoadGeneratedObservedRelations();
            internal static readonly uint4[] Cells = LoadGeneratedObservedRelationCells();
            internal static readonly uint4[] Levels = LoadGeneratedObservedRelationLevels();
            internal static readonly uint[] References = LoadGeneratedObservedRelationReferences();
#endif
        }

        internal static ReadOnlySpan<uint4> ObservedRelations => ObservedRelationData.Relations;
        internal static ReadOnlySpan<uint4> ObservedRelationCells => ObservedRelationData.Cells;
        internal static ReadOnlySpan<uint4> ObservedRelationLevels => ObservedRelationData.Levels;
        internal static ReadOnlySpan<uint> ObservedRelationReferences => ObservedRelationData.References;

#if UNITY_EDITOR
        private static void BuildObservedRelations(out uint4[] relations, out uint4[] cells,
            out uint4[] levels, out uint[] references)
        {
            var rows = new List<uint4>();
            var cover = new List<uint4>();
            var refs = new List<uint>();
            levels = new uint4[3];
            for (int level = 0; level <= 2; level++)
            {
                int first = rows.Count / 2;
                if (level == 0)
                {
                    for (int node = 6; node < NodesValue.Length; node++)
                    {
                        ulong incidence = NodeIncidentPetalsValue[node];
                        int petal = (uint)incidence != 0u ? math.tzcnt((uint)incidence) :
                            32 + math.tzcnt((uint)(incidence >> 32));
                        int line = NodesValue[node].LineClass;
                        uint key = (uint)petal | ((uint)node << 10) |
                            (LinesValue[line].Shell == Shell.R3Closure ? 1u << 15 : 0u);
                        rows.Add(new uint4(math.asuint(NodesValue[node].Direction), (uint)line << 2));
                        rows.Add(new uint4(key, (uint)incidence, (uint)(incidence >> 32), 0u));
                    }
                }
                else
                {
                    // L1 uses each shared strand's canonical construction;
                    // L2 preserves each distinct parent prediction. No weld.
                    for (int petal = 0; petal < PetalClassCount; petal++)
                    for (int parent = level == 1 ? 0 : 1; parent < (level == 1 ? 1 : 5); parent++)
                    for (int site = 3; site < 6; site++)
                    {
                        int index = (petal * 5 + parent) * 6 + site;
                        uint4 address = ChildLoopAddresses[index];
                        uint creation = ChildLoopCreations[index];
                        int line = (int)((address.w >> 2) & 15u);
                        if ((creation & 0x80000000u) == 0u || (address.w & 3u) != level ||
                            LinesValue[line].Shell != Shell.R2Shape) continue;
                        if (level == 1 && (creation & 63u) != petal) continue;
                        uint key = (creation & 0xffffu) | ((uint)parent << 16) | ((uint)site << 19);
                        rows.Add(address);
                        StrandRule strand = StrandsValue[(int)((address.w >> 6) & 127u)];
                        ulong incidence = level == 1
                            ? (1UL << strand.Petal0) | (1UL << strand.Petal1)
                            : 1UL << petal;
                        rows.Add(new uint4(key, (uint)incidence, (uint)(incidence >> 32), 0u));
                    }
                }
                int count = rows.Count / 2 - first;
                int half = 1 << level, side = level == 0 ? 1 : 2 * half;
                int firstCell = cover.Count;
                for (int z = 0; z < side; z++)
                for (int y = 0; y < side; y++)
                for (int x = 0; x < side; x++)
                {
                    int begin = refs.Count;
                    int3 cell = new int3(x, y, z) - half;
                    for (int relation = first; relation < first + count; relation++)
                    {
                        uint4 address = rows[2 * relation];
                        int3 offset = math.asint(address.xyz);
                        int3 direction = LinesValue[(int)((address.w >> 2) & 15u)].Direction;
                        if (math.any(((offset - direction) & 1) != 0))
                            throw new InvalidOperationException("Nonintegral observed Flower endpoint.");
                        int3 endpoint = (offset - direction) / 2;
                        bool Covers(int3 center) => math.all(cell >= center - 1) && math.all(cell < center + 1);
                        if (level == 0 || Covers(endpoint) || Covers(endpoint + direction))
                            refs.Add((uint)(relation - first));
                    }
                    cover.Add(new uint4((uint)begin, (uint)(refs.Count - begin), 0u, 0u));
                }
                levels[level] = new uint4((uint)first, (uint)count, (uint)firstCell, (uint)side);
            }
            relations = rows.ToArray(); cells = cover.ToArray(); references = refs.ToArray();
        }
#endif
    }
}
