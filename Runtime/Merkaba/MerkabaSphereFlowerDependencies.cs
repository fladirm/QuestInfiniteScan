using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Genesis.RoomScan
{
    public static partial class MerkabaSphereFlowerAuthority
    {
        // The existing finite phase workset without its two algebraic signs:
        // twenty original R2/R3 nodes, seventy-two strands, 48*4*3 new edges.
        // R1-only entries are deliberately empty. This is a dependency index,
        // never a new root, persistent address or observation identity.
        public const int PhaseDependencyCount = 20 + StrandClassCount + PetalClassCount * 12;

        private static class PhaseDependencyData
        {
#if UNITY_EDITOR
            internal static readonly uint4[] Carriers = BuildPhaseDependentCarriers();
#else
            internal static readonly uint4[] Carriers = LoadGeneratedPhaseDependentCarriers();
#endif
        }

        public static ReadOnlySpan<uint4> PhaseDependentCarriers => PhaseDependencyData.Carriers;

        internal static bool TryPhaseDependencyIndex(MerkabaFlowerDetailKey key, out int index)
        {
            index = -1;
            int level = key.GeometryLevel, line = key.Channel, petal = key.PetalClass;
            int shell = (int)LinesValue[line].Shell;
            if (shell < 2 || key.Kind != (shell == 3 ? MerkabaFlowerDetailKind.R3Phase :
                    MerkabaFlowerDetailKind.R2Phase)) return false;
            if (level == 0)
            {
                if (key.GeometryChildPath != 0) return false;
                for (int anchor = 1; anchor < 3; anchor++)
                {
                    int node = PetalsValue[petal].Node(anchor);
                    if (NodesValue[node].LineClass != line || FirstIncidentPetal(node) != petal) continue;
                    index = node - 6;
                    return index >= 0 && index < 20;
                }
                return false;
            }
            if (shell != 2) return false;
            for (int edge = 0; edge < 3; edge++)
            {
                if (level == 1)
                {
                    int strand = PetalsValue[petal].Strand(edge);
                    PhaseFamilyRule family = PhaseFamiliesValue[strand];
                    if (StrandsValue[strand].Petal0 != petal ||
                        family.FinePath0 != key.GeometryChildPath ||
                        NodesValue[family.RootNode].LineClass != line) continue;
                    index = 20 + strand;
                    return true;
                }
                int parent = key.GeometryChildPath / 4;
                int path = 4 * parent + (edge == 1 ? 1 : 0);
                if (path != key.GeometryChildPath ||
                    !TryGetChildPhaseLoop(petal, parent + 1, edge + 3, out int actualLevel,
                        out _, out int actualLine, out _, out _, out _, out int inherited) ||
                    actualLevel != 2 || inherited >= 0 || actualLine != line) continue;
                index = 20 + StrandClassCount + 12 * petal + 3 * parent + edge;
                return true;
            }
            return false;
        }

        internal static bool TryPhaseDependentCarriers(MerkabaFlowerDetailKey key, out uint4 carriers)
        {
            carriers = default;
            if (!TryPhaseDependencyIndex(key, out int index)) return false;
            carriers = PhaseDependencyData.Carriers[index];
            return true;
        }

        private static int FirstIncidentPetal(int node)
        {
            ulong mask = NodeIncidentPetalsValue[node];
            return (uint)mask != 0u ? math.tzcnt((uint)mask) : 32 + math.tzcnt((uint)(mask >> 32));
        }

#if UNITY_EDITOR
        private static bool TryPhaseDependencyAddress(int index, out MerkabaFlowerDetailKey key)
        {
            key = default;
            int level, path = 0, petal, line;
            if (index < 20)
            {
                int node = index + 6;
                level = 0; petal = FirstIncidentPetal(node); line = NodesValue[node].LineClass;
            }
            else if (index < 20 + StrandClassCount)
            {
                int strand = index - 20;
                PhaseFamilyRule family = PhaseFamiliesValue[strand];
                level = 1; path = family.FinePath0; petal = StrandsValue[strand].Petal0;
                line = NodesValue[family.RootNode].LineClass;
                if (LinesValue[line].Shell != Shell.R2Shape) return false;
            }
            else
            {
                int ordinal = index - 20 - StrandClassCount;
                petal = ordinal / 12;
                int parent = (ordinal / 3) & 3, edge = ordinal % 3;
                if (!TryGetChildPhaseLoop(petal, parent + 1, edge + 3, out level,
                        out _, out line, out _, out _, out _, out int inherited) ||
                    level != 2 || inherited >= 0 || LinesValue[line].Shell != Shell.R2Shape) return false;
                path = 4 * parent + (edge == 1 ? 1 : 0);
            }
            key = MerkabaFlowerDetailKey.Create(level, path, petal, line,
                LinesValue[line].Shell == Shell.R3Closure ? MerkabaFlowerDetailKind.R3Phase :
                    MerkabaFlowerDetailKind.R2Phase, false, 0);
            return true;
        }

        private static uint4[] BuildPhaseDependentCarriers()
        {
            var result = new uint4[PhaseDependencyCount];
            var addresses = new Dictionary<uint, int>();
            for (int index = 0; index < result.Length; index++)
            {
                if (!TryPhaseDependencyAddress(index, out var key)) continue;
                if (!addresses.TryAdd(key.Value, index) ||
                    !TryPhaseDependencyIndex(key, out int inverse) || inverse != index)
                    throw new InvalidOperationException("Phase dependency address is not its canonical creation key.");
            }

            for (int carrier = 0; carrier < L2HubCount; carrier++)
            {
                var reads = new HashSet<int>();
                void Add(int level, int path, int petal, int line)
                {
                    int shell = (int)LinesValue[line].Shell;
                    if (shell == 1) return;
                    var key = MerkabaFlowerDetailKey.Create(level, path, petal, line,
                        shell == 3 ? MerkabaFlowerDetailKind.R3Phase : MerkabaFlowerDetailKind.R2Phase,
                        false, 0);
                    if (!addresses.TryGetValue(key.Value, out int address))
                        throw new InvalidOperationException("Actual L2 phase reader has no finite dependency address.");
                    reads.Add(address);
                }
                void AddOriginal(int node) => Add(0, 0, FirstIncidentPetal(node), NodesValue[node].LineClass);

                // SourceAnchorAlternatives is part of the uniqueness proof,
                // not merely the three roots eventually selected for drawing.
                // It reads all three original anchors of each actual source flag.
                for (int wedge = 0; wedge < L2CarrierWedgeCount; wedge++)
                {
                    int petal = CarrierData.Wedges[carrier * L2CarrierWedgeCount + wedge].Petal;
                    for (int anchor = 0; anchor < 3; anchor++) AddOriginal(PetalsValue[petal].Node(anchor));
                }
                for (int site = 0; site < L2CarrierSiteCount; site++)
                {
                    int knot = L2CarrierKnotIndex(carrier, site);
                    for (int incidence = CarrierData.IncidenceOffsets[knot];
                        incidence < CarrierData.IncidenceOffsets[knot + 1]; incidence++)
                    {
                        var source = new L2KnotRule(CarrierData.IncidenceSources[incidence]);
                        if (!TryGetChildPhaseLoop(source.Petal, source.ParentContext, source.KnotSite,
                            out int level, out _, out int line, out int strand,
                            out _, out _, out int inherited))
                            throw new InvalidOperationException("Invalid exact L2 dependency source.");
                        if (level == 0)
                        {
                            AddOriginal(PetalsValue[source.Petal].Node(source.KnotSite));
                            continue;
                        }
                        if (inherited >= 0 || level > 2)
                            throw new InvalidOperationException("Dependency source is not an original creation incidence.");
                        if (LinesValue[line].Shell == Shell.R1Core) continue;
                        PhaseFamilyRule family = PhaseFamiliesValue[strand];
                        int finePetal = StrandsValue[strand].Petal0;
                        AddOriginal(family.RootNode);
                        if (level == 2) Add(1, family.FinePath0, finePetal, line);
                        Add(level, level == 1 ? family.FinePath0 :
                                4 * (source.ParentContext - 1) + (source.KnotSite == 4 ? 1 : 0),
                            level == 1 ? finePetal : source.Petal, line);
                    }
                }
                // The actual classifier visits both signs. Its HasPhaseFamily
                // guards also consult all sectors of each address. Neither
                // sign nor sector may be guessed from a sparse skin hub key.
                foreach (int address in reads)
                    result[address][carrier >> 5] |= 1u << (carrier & 31);
            }
            return result;
        }
#endif
    }
}
