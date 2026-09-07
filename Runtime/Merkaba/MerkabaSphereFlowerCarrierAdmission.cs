using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Genesis.RoomScan
{
    public static partial class MerkabaSphereFlowerAuthority
    {
        private static class AnchorFlagData
        {
#if UNITY_EDITOR
            internal static readonly ulong[] Masks = BuildAnchorSectorPetalMasks();
            internal static readonly ulong[] BoundaryMasks = BuildAnchorBoundaryPetalMasks();
#else
            internal static readonly ulong[] Masks = LoadGeneratedAnchorSectorPetalMasks();
            internal static readonly ulong[] BoundaryMasks = LoadGeneratedAnchorBoundaryPetalMasks();
#endif
        }

        private static class L2BoundaryFlagData
        {
            internal static readonly ushort[] Offsets;
            internal static readonly byte[] Indices;
            internal static readonly ulong[] Masks;
            static L2BoundaryFlagData()
            {
#if UNITY_EDITOR
                BuildL2BoundaryFlags(out Offsets, out Indices, out Masks);
#else
                LoadGeneratedL2BoundaryFlags(out Offsets, out Indices, out Masks);
#endif
            }
        }

        public static ReadOnlySpan<ushort> L2BoundaryFlagOffsets => L2BoundaryFlagData.Offsets;
        public static ReadOnlySpan<byte> L2BoundaryFlagIndices => L2BoundaryFlagData.Indices;
        public static ReadOnlySpan<ulong> L2BoundaryFlagMasks => L2BoundaryFlagData.Masks;

        public static ReadOnlySpan<ulong> AnchorSectorPetalMasks => AnchorFlagData.Masks;
        public static ReadOnlySpan<ulong> AnchorBoundaryPetalMasks => AnchorFlagData.BoundaryMasks;
        public static ReadOnlySpan<ulong> R1SectorPetalMasks => AnchorSectorPetalMasks.Slice(
            0, 2 * (LinesValue[2].SectorOffset + LinesValue[2].SectorCount));

        // A valid higher-shell sector may have no incident admissible flag.
        // False means an invalid address, not an empty-but-valid mask.
        public static bool TryGetAnchorSectorFlags(int node, int sector, out ulong mask)
        {
            mask = 0u;
            if ((uint)node >= NodesValue.Length) return false;
            NodeRule anchor = NodesValue[node];
            LineRule line = LinesValue[anchor.LineClass];
            if ((uint)sector >= line.SectorCount) return false;
            mask = AnchorFlagData.Masks[2 * (line.SectorOffset + sector) +
                (anchor.Orientation < 0 ? 1 : 0)];
            return true;
        }

        // Shared Sigma sector ownership is canonical to the undirected loop.
        // At an exact boundary, each endpoint's local Node tie remains its
        // own directed flag decision, not the adjacent open-sector mask.
        public static bool TryGetAnchorRootFlags(int node, uint proofTag, out ulong mask)
        {
            mask = 0u;
            if ((uint)node >= NodesValue.Length) return false;
            NodeRule anchor = NodesValue[node];
            if (((proofTag >> 3) & 15u) != anchor.LineClass) return false;
            int sector = (int)((proofTag >> 8) & 31u);
            uint code = (proofTag & BoundaryWitnessMask) >> 21;
            if (code == 0u) return TryGetAnchorSectorFlags(node, sector, out mask);
            LineRule line = LinesValue[anchor.LineClass];
            if (code > line.SectorCount ||
                BoundaryRules[line.SectorOffset + (int)code - 1].Owner != sector) return false;
            mask = AnchorFlagData.BoundaryMasks[2 * (line.SectorOffset + (int)code - 1) +
                (anchor.Orientation < 0 ? 1 : 0)];
            return true;
        }

        public static bool TryGetR1SectorFlag(int faceNode, int sector, out int petal)
        {
            petal = -1;
            if ((uint)faceNode >= 6u || !TryGetAnchorSectorFlags(faceNode, sector, out ulong mask) ||
                mask == 0u || (mask & (mask - 1u)) != 0u) return false;
            uint lo = (uint)mask;
            petal = lo != 0u ? math.tzcnt(lo) : 32 + math.tzcnt((uint)(mask >> 32));
            return true;
        }

        internal readonly struct R1FlagRoots
        {
            internal readonly uint CandidateMask, UnresolvedMask;
            internal readonly PhaseRootEvidence Minus, Plus;

            internal R1FlagRoots(uint candidates, uint unresolved,
                PhaseRootEvidence minus, PhaseRootEvidence plus)
            { CandidateMask = candidates; UnresolvedMask = unresolved; Minus = minus; Plus = plus; }

            internal ProofClassification Classify(out PhaseRootEvidence uniqueRoot)
            {
                uniqueRoot = default;
                if (UnresolvedMask != 0u || math.countbits(CandidateMask) > 1)
                {
                    uniqueRoot = new PhaseRootEvidence(default, default, ProofClassification.Ambiguous);
                    return ProofClassification.Ambiguous;
                }
                if (CandidateMask == 0u) return ProofClassification.Impossible;
                uniqueRoot = CandidateMask == 1u ? Minus : Plus;
                return ProofClassification.Certain;
            }
        }

        // Same bounded plane/root and exact half-open sector evaluator as GPU.
        // This classifies an explicit algebraic sign; it never chooses a flag.
        internal static ProofClassification CarrierRootProof(int3 owner, uint flags,
            int level, int3 offset, int lineClass, bool plus, float normalError,
            float offsetError, out PhaseRootEvidence root)
        {
            root = new PhaseRootEvidence(default, default, ProofClassification.Ambiguous);
            if (!M8FlowerHasPlane(flags)) return ProofClassification.Ambiguous;
            if ((uint)level >= GeometryLevelCount || (uint)lineClass >= LineClassCount)
            { root = default; return ProofClassification.Impossible; }
            long scale = 1L << (level + 1);
            long x = (long)owner.x * scale + offset.x;
            long y = (long)owner.y * scale + offset.y;
            long z = (long)owner.z * scale + offset.z;
            if (x < int.MinValue || x > int.MaxValue || y < int.MinValue || y > int.MaxValue ||
                z < int.MinValue || z > int.MaxValue)
            { root = default; return ProofClassification.Impossible; }
            if (!float.IsFinite(normalError) || !float.IsFinite(offsetError) ||
                normalError < 0f || offsetError < 0f) return ProofClassification.Ambiguous;
            M8FlowerUnpackPlane(flags, out float3 normal, out float delta);
            LoopFrame loop = EvaluateLoop(level, new Long3(offset.x, offset.y, offset.z), lineClass);
            RootResult roots = EvaluateRoots(RestrictPlaneToLoop(float3.zero, normal,
                delta, normalError, offsetError, loop));
            if (roots.Classification == RootClassification.Impossible ||
                (roots.Classification == RootClassification.CertainTangent && plus))
            { root = default; return ProofClassification.Impossible; }
            if (roots.Classification != RootClassification.CertainSecant &&
                roots.Classification != RootClassification.CertainTangent)
                return ProofClassification.Ambiguous;
            Interval2 phase = plus ? roots.Plus : roots.Minus;
            if (ClassifyPlaneSector(level, offset, lineClass, plus, normal, delta,
                    phase, out int sector, out uint boundaryWitness) != ProofClassification.Certain)
                return ProofClassification.Ambiguous;
            var tag = MerkabaFlowerSymbolTag.Create(level, lineClass, plus, sector,
                false, 0u, MerkabaFlowerSymbolStatus.Confirmed);
            var symbol = new MerkabaFlowerSymbolKey(new int3((int)x, (int)y, (int)z), tag);
            symbol.Tag |= boundaryWitness;
            root = new PhaseRootEvidence(symbol, phase, ProofClassification.Certain);
            return ProofClassification.Certain;
        }

        // One original anchor, one R1-led root flag, and two finite signs.
        // Each shell reads its OWN loop sectors; there is no face-plane or
        // owner-box constraint on the R2/R3 anchor position.
        internal static R1FlagRoots SelectAnchorFlagRoots(int3 owner, uint flags,
            int petal, int anchorIndex, float normalError, float offsetError)
        {
            const uint required = M8_FLOWER_OCCUPIED_FLAG | M8_FLOWER_PLANE_VALID;
            if ((uint)petal >= PetalClassCount || (uint)anchorIndex >= 3u ||
                (flags & (required | M8_FLOWER_SEED_FLAG)) != required) return default;
            int anchor = PetalsValue[petal].Node(anchorIndex);
            NodeRule node = NodesValue[anchor];
            uint candidates = 0u, unresolved = 0u;
            PhaseRootEvidence minus = default, plus = default;
            for (int sign = 0; sign < 2; sign++)
            {
                ProofClassification result = CarrierRootProof(owner, flags, 0,
                    node.Direction, node.LineClass, sign != 0, normalError, offsetError, out var root);
                if (result == ProofClassification.Impossible) continue;
                if (result != ProofClassification.Certain ||
                    ClassifyPhaseSector(root, out _) != ProofClassification.Certain ||
                    !TryGetAnchorRootFlags(anchor, root.Symbol.Tag, out ulong allowed))
                { unresolved |= 1u << sign; continue; }
                if ((allowed & (1UL << petal)) == 0u) continue;
                candidates |= 1u << sign;
                if (sign == 0) minus = root; else plus = root;
            }
            return new R1FlagRoots(candidates, unresolved, minus, plus);
        }

        internal static R1FlagRoots SelectR1FlagRoots(int3 owner, uint flags,
            int petal, float normalError, float offsetError) =>
            SelectAnchorFlagRoots(owner, flags, petal, 0, normalError, offsetError);

        internal static R1FlagRoots SelectAnchorFlagRelation(int3 owner, uint flags,
            uint neighbourFlags, int petal, int anchorIndex, float normalError, float offsetError)
        {
            R1FlagRoots local = SelectAnchorFlagRoots(owner, flags, petal, anchorIndex, normalError, offsetError);
            if (local.CandidateMask == 0u) return local;
            int anchorNode = PetalsValue[petal].Node(anchorIndex);
            NodeRule node = NodesValue[anchorNode];
            uint candidates = local.CandidateMask, unresolved = local.UnresolvedMask;
            PhaseRootEvidence minus = local.Minus, plus = local.Plus;
            for (int sign = 0; sign < 2; sign++)
            {
                uint bit = 1u << sign;
                if ((candidates & bit) == 0u) continue;
                PhaseRootEvidence anchor = sign == 0 ? minus : plus;
                ProofClassification result = EvaluateCarrierRelation(anchor.Symbol.Junction,
                    node.LineClass, sign != 0, node.Orientation < 0 ? neighbourFlags : flags,
                    node.Orientation < 0 ? flags : neighbourFlags, normalError, offsetError,
                    out PhaseRootEvidence relation, out _);
                if (result == ProofClassification.Certain &&
                    (ClassifyPhaseSector(relation, out _) != ProofClassification.Certain ||
                     !TryGetAnchorRootFlags(anchorNode, relation.Symbol.Tag, out ulong allowed) ||
                     (allowed & (1UL << petal)) == 0u)) result = ProofClassification.Ambiguous;
                if (result != ProofClassification.Certain)
                {
                    candidates &= ~bit;
                    if (result != ProofClassification.Impossible) unresolved |= bit;
                    if (sign == 0) minus = default; else plus = default;
                }
                else if (sign == 0) minus = relation;
                else plus = relation;
            }
            return new R1FlagRoots(candidates, unresolved, minus, plus);
        }

        internal static R1FlagRoots SelectR1FlagRelation(int3 owner, uint flags,
            uint neighbourFlags, int petal, float normalError, float offsetError) =>
            SelectAnchorFlagRelation(owner, flags, neighbourFlags, petal, 0, normalError, offsetError);

        // Geometric source identity is per wedge. It must not be replaced by
        // the shared carrier's canonical appearance-key petal.
        internal static R1FlagRoots SelectL2WedgeR1FlagRoots(int3 owner, uint flags,
            int carrier, int wedge, float normalError, float offsetError)
        {
            if ((uint)carrier >= L2HubCount || (uint)wedge >= L2CarrierWedgeCount)
                return default;
            int sourcePetal = CarrierData.Wedges[6 * carrier + wedge].Petal;
            return SelectR1FlagRoots(owner, flags, sourcePetal, normalError, offsetError);
        }

        // CPU mirror of the production relative-coordinate enclosure. The
        // generated original loop is unchanged; only three coordinate forms
        // are evaluated, avoiding cancellation in large world coordinates.
        internal static bool RootRelativeBounds(int knot, PhaseRootEvidence root,
            out Interval3 bounds)
        {
            bounds = default;
            if (root.Classification != ProofClassification.Certain ||
                !TryGetL2KnotLoop(knot, out int level, out int3 offset, out int lineClass) ||
                !MerkabaFlowerSymbolTag.TryDecode(root.Symbol.Tag, out var tag) ||
                tag.Level != level || tag.LineClass != lineClass) return false;
            LoopFrame loop = EvaluateLoop(level, new Long3(offset.x, offset.y, offset.z), lineClass);
            Span<FloatInterval> coordinates = stackalloc FloatInterval[3];
            for (int axis = 0; axis < 3; axis++)
            {
                float3 direction = float3.zero; direction[axis] = 1f;
                Interval3 abc = RestrictPlaneToLoop(float3.zero, direction, 0f, 0f, 0f, loop);
                coordinates[axis] = FloatInterval.Add(FloatInterval.Add(abc.X,
                    FloatInterval.Multiply(abc.Y, root.Root.X)), FloatInterval.Multiply(abc.Z, root.Root.Y));
                if (!float.IsFinite(coordinates[axis].Lower) || !float.IsFinite(coordinates[axis].Upper))
                    return false;
            }
            bounds = new Interval3(coordinates[0], coordinates[1], coordinates[2]);
            return true;
        }

        private static FloatInterval CarrierDot(int3 direction, Interval3 position) =>
            FloatInterval.Add(FloatInterval.Add(
                FloatInterval.Multiply(FloatInterval.Singleton(direction.x), position.X),
                FloatInterval.Multiply(FloatInterval.Singleton(direction.y), position.Y)),
                FloatInterval.Multiply(FloatInterval.Singleton(direction.z), position.Z));

        // Equal-shell Phi differences are linear at EVERY X. No original
        // face equation or owner-box constraint is applied to a child loop.
        internal static ProofClassification SourceFlagContainment(int petal, int knot,
            uint proofTag, Interval3 position)
        {
            if ((uint)petal >= PetalClassCount) return ProofClassification.Impossible;
            PetalRule rule = PetalsValue[petal];
            int3 edge = NodesValue[rule.EdgeNode].Direction - NodesValue[rule.FaceNode].Direction;
            int3 corner = NodesValue[rule.CornerNode].Direction - NodesValue[rule.EdgeNode].Direction;
            FloatInterval cornerOrder = CarrierDot(corner, position);
            FloatInterval edgeOrder = CarrierDot(edge - corner, position);
            if (cornerOrder.Upper < 0f || edgeOrder.Upper < 0f) return ProofClassification.Impossible;
            if (cornerOrder.Lower > 0f && edgeOrder.Lower > 0f) return ProofClassification.Certain;
            if (TryL2BoundaryFlags(knot, proofTag, out ulong allowed))
                return (allowed & (1UL << petal)) != 0u ?
                    ProofClassification.Certain : ProofClassification.Impossible;
            return ProofClassification.Ambiguous;
        }

        // proofTag is retained only after ClassifyPhaseSector has certified
        // the actual reader result. The original knot, not its requesting
        // petal or a midpoint, identifies this boundary's translated frame.
        private static bool TryL2BoundaryFlags(int knot, uint proofTag, out ulong mask)
        {
            mask = 0u;
            uint code = (proofTag & BoundaryWitnessMask) >> 21;
            if (code == 0u || !TryGetL2KnotLoop(knot, out int level,
                out _, out int lineClass) || (proofTag & 7u) != (uint)level ||
                ((proofTag >> 3) & 15u) != (uint)lineClass) return false;
            LineRule line = LinesValue[lineClass];
            if (code > line.SectorCount ||
                BoundaryRules[line.SectorOffset + (int)code - 1].Owner != ((proofTag >> 8) & 31u))
                return false;
            int start = L2BoundaryFlagData.Offsets[knot];
            if (code > L2BoundaryFlagData.Offsets[knot + 1] - start) return false;
            mask = L2BoundaryFlagData.Masks[L2BoundaryFlagData.Indices[start + (int)code - 1]];
            return true;
        }

        internal static ProofClassification CarrierWedgeOrientation(Interval3 a, Interval3 b,
            Interval3 c, float3 normal, float normalError)
        {
            if (!math.all(math.isfinite(normal)) || !float.IsFinite(normalError) || normalError < 0f)
                return ProofClassification.Ambiguous;
            var u = new Interval3(FloatInterval.Subtract(b.X, a.X), FloatInterval.Subtract(b.Y, a.Y),
                FloatInterval.Subtract(b.Z, a.Z));
            var v = new Interval3(FloatInterval.Subtract(c.X, a.X), FloatInterval.Subtract(c.Y, a.Y),
                FloatInterval.Subtract(c.Z, a.Z));
            var area = new Interval3(
                FloatInterval.Subtract(FloatInterval.Multiply(u.Y, v.Z), FloatInterval.Multiply(u.Z, v.Y)),
                FloatInterval.Subtract(FloatInterval.Multiply(u.Z, v.X), FloatInterval.Multiply(u.X, v.Z)),
                FloatInterval.Subtract(FloatInterval.Multiply(u.X, v.Y), FloatInterval.Multiply(u.Y, v.X)));
            FloatInterval det = FloatInterval.Add(FloatInterval.Add(
                FloatInterval.Multiply(area.X, OrderedCenterRadius(normal.x, normalError)),
                FloatInterval.Multiply(area.Y, OrderedCenterRadius(normal.y, normalError))),
                FloatInterval.Multiply(area.Z, OrderedCenterRadius(normal.z, normalError)));
            if (!float.IsFinite(det.Lower) || !float.IsFinite(det.Upper)) return ProofClassification.Ambiguous;
            if (det.Lower > 0f) return ProofClassification.Certain;
            if (det.Upper < 0f) return ProofClassification.Impossible;
            return ProofClassification.Ambiguous;
        }

        internal static uint CarrierTriple(uint signs, int wedge) => (signs & 1u) |
            (((signs >> (1 + wedge)) & 1u) << 1) |
            (((signs >> (1 + (wedge + 1) % 6)) & 1u) << 2);

        // This finite mask intersection is the shader's exact 128-pattern
        // combiner, not a root ranking. Unused sites never create ambiguity.
        internal static ProofClassification CombineCarrierCandidates(ReadOnlySpan<uint> certain,
            ReadOnlySpan<uint> uncertain, out uint signs, out uint active, out uint unresolved)
        {
            signs = active = unresolved = 0u;
            if (certain.Length != 6 || uncertain.Length != 6)
                throw new ArgumentException("A generated L2 carrier has exactly six wedges.");
            uint potential = 0u;
            for (int wedge = 0; wedge < 6; wedge++)
            {
                if (((certain[wedge] | uncertain[wedge]) & ~255u) != 0u)
                    throw new ArgumentException("Each wedge has exactly eight algebraic sign triples.");
                if ((certain[wedge] | uncertain[wedge]) != 0u) potential |= 1u << wedge;
            }
            if (potential == 0u) return ProofClassification.Impossible;
            uint first = uint.MaxValue, agreed = potential;
            for (uint candidate = 0u; candidate < 128u; candidate++)
            {
                bool possible = true;
                uint certainWedges = 0u;
                for (int wedge = 0; wedge < 6; wedge++)
                {
                    uint bit = 1u << wedge;
                    if ((potential & bit) == 0u) continue;
                    uint tripleBit = 1u << (int)CarrierTriple(candidate, wedge);
                    if (((certain[wedge] | uncertain[wedge]) & tripleBit) == 0u)
                    { possible = false; break; }
                    if ((certain[wedge] & tripleBit) != 0u) certainWedges |= bit;
                }
                if (!possible) continue;
                if (first == uint.MaxValue) { first = candidate; agreed &= certainWedges; }
                else
                {
                    agreed &= certainWedges;
                    for (int wedge = 0; wedge < 6; wedge++)
                        if (CarrierTriple(first, wedge) != CarrierTriple(candidate, wedge))
                            agreed &= ~(1u << wedge);
                }
            }
            if (first == uint.MaxValue)
            { unresolved = potential; return ProofClassification.Ambiguous; }
            active = agreed;
            unresolved = potential & ~active;
            uint used = 0u;
            for (int wedge = 0; wedge < 6; wedge++)
                if ((active & (1u << wedge)) != 0u)
                    used |= 1u | (1u << (1 + wedge)) | (1u << (1 + (wedge + 1) % 6));
            signs = first & used;
            return active != 0u ? ProofClassification.Certain : ProofClassification.Ambiguous;
        }

        internal static bool SupportWedgeBounds(int3 owner, int carrier, int wedge,
            ReadOnlySpan<PhaseRootEvidence> roots, out int3 firstCell, out int3 lastCell)
        {
            firstCell = lastCell = default;
            if ((uint)carrier >= L2HubCount || (uint)wedge >= 6u || roots.Length != 7) return false;
            int3 origin = (owner >> 3) << 3;
            int3 relativeOwner = owner - origin;
            FloatInterval step = FloatInterval.Singleton(LevelStep(0));
            var translation = new Interval3(
                FloatInterval.Multiply(FloatInterval.Singleton(relativeOwner.x), step),
                FloatInterval.Multiply(FloatInterval.Singleton(relativeOwner.y), step),
                FloatInterval.Multiply(FloatInterval.Singleton(relativeOwner.z), step));
            int3 sites = new(0, 1 + wedge, 1 + (wedge + 1) % 6);
            Span<Interval3> bounds = stackalloc Interval3[3];
            for (int vertex = 0; vertex < 3; vertex++)
            {
                int site = sites[vertex];
                if (!RootRelativeBounds(L2CarrierKnotIndex(carrier, site), roots[site], out Interval3 relative))
                    return false;
                bounds[vertex] = new Interval3(FloatInterval.Add(relative.X, translation.X),
                    FloatInterval.Add(relative.Y, translation.Y), FloatInterval.Add(relative.Z, translation.Z));
            }
            for (int axis = 0; axis < 3; axis++)
            {
                var extent = new FloatInterval(Math.Min(Math.Min(bounds[0][axis].Lower, bounds[1][axis].Lower), bounds[2][axis].Lower),
                    Math.Max(Math.Max(bounds[0][axis].Upper, bounds[1][axis].Upper), bounds[2][axis].Upper));
                FloatInterval cells = FloatInterval.Divide(extent, step);
                if (!float.IsFinite(cells.Lower) || !float.IsFinite(cells.Upper) || cells.Lower < -8f || cells.Upper >= 15f)
                    return false;
                firstCell[axis] = (int)Math.Floor(cells.Lower);
                lastCell[axis] = (int)Math.Floor(cells.Upper);
            }
            return true;
        }

        // Classification of a THROUGH claim: Certain=veto, Impossible=no
        // portion of this metric cover is through, Ambiguous=mixed or cold.
        internal static ProofClassification SupportWedgeDual(int3 owner, int carrier, int wedge,
            ReadOnlySpan<PhaseRootEvidence> roots, Func<int3, ExcavationCellState> readCell)
        {
            if (readCell == null) throw new ArgumentNullException(nameof(readCell));
            if (!SupportWedgeBounds(owner, carrier, wedge, roots, out int3 first, out int3 last))
                return ProofClassification.Ambiguous;
            int3 origin = (owner >> 3) << 3;
            bool full = false, through = false;
            for (int z = first.z; z <= last.z; z++)
                for (int y = first.y; y <= last.y; y++)
                    for (int x = first.x; x <= last.x; x++)
                    {
                        long cx = (long)origin.x + x, cy = (long)origin.y + y, cz = (long)origin.z + z;
                        if (cx < int.MinValue || cx > int.MaxValue || cy < int.MinValue || cy > int.MaxValue ||
                            cz < int.MinValue || cz > int.MaxValue) return ProofClassification.Ambiguous;
                        ExcavationCellState state = readCell(new int3((int)cx, (int)cy, (int)cz));
                        if (state == ExcavationCellState.Ambiguous) return ProofClassification.Ambiguous;
                        if (state == ExcavationCellState.Free) through = true; else full = true;
                        if (full && through) return ProofClassification.Ambiguous;
                    }
            return through ? ProofClassification.Certain : ProofClassification.Impossible;
        }

        private static FloatInterval SupportDifference(float a, float b) => a == b
            ? FloatInterval.Singleton(0f) : FloatInterval.Subtract(FloatInterval.Singleton(a), FloatInterval.Singleton(b));

        private static FloatInterval SupportOrient2(float2 a, float2 b, float2 p) =>
            FloatInterval.Subtract(
                FloatInterval.Multiply(SupportDifference(b.x, a.x), SupportDifference(p.y, a.y)),
                FloatInterval.Multiply(SupportDifference(b.y, a.y), SupportDifference(p.x, a.x)));

        private static FloatInterval SupportOriented(FloatInterval value, int orientation) =>
            orientation < 0 ? new FloatInterval(-value.Upper, -value.Lower) : value;

        private static int SupportWinding(FloatInterval value) => value.Lower > 0f ? 1 : value.Upper < 0f ? -1 : 0;

        internal static ProofClassification SupportTriangleCoverage(ReadOnlySpan<float3> directVertices,
            int3 cell, int face, int halfFace)
        {
            if (directVertices.Length != 3 || (uint)face >= 6u || (uint)halfFace >= 2u)
                return ProofClassification.Ambiguous;
            int axis = face >> 1, u = (axis + 1) % 3, v = (axis + 2) % 3;
            Span<float2> direct = stackalloc float2[3], half = stackalloc float2[3];
            float plane = DirtFaceGridPosition(cell, face, halfFace, 0)[axis];
            for (int i = 0; i < 3; i++)
            {
                if (!math.all(math.isfinite(directVertices[i]))) return ProofClassification.Ambiguous;
                if (directVertices[i][axis] != plane) return ProofClassification.Impossible;
                float3 position = DirtFaceGridPosition(cell, face, halfFace, i);
                direct[i] = new float2(directVertices[i][u], directVertices[i][v]);
                half[i] = new float2(position[u], position[v]);
            }
            int winding = SupportWinding(SupportOrient2(direct[0], direct[1], direct[2]));
            if (winding == 0) return ProofClassification.Ambiguous;
            bool covered = true;
            for (int edge = 0; edge < 3; edge++)
            {
                bool outside = true;
                for (int sample = 0; sample < 3; sample++)
                {
                    FloatInterval side = SupportOriented(SupportOrient2(direct[edge], direct[(edge + 1) % 3], half[sample]), winding);
                    covered &= side.Lower >= 0f;
                    outside &= side.Upper < 0f;
                }
                if (outside) return ProofClassification.Impossible;
            }
            if (covered) return ProofClassification.Certain;
            int halfWinding = SupportWinding(SupportOrient2(half[0], half[1], half[2]));
            if (halfWinding == 0) return ProofClassification.Ambiguous;
            for (int edge = 0; edge < 3; edge++)
            {
                bool outside = true;
                for (int sample = 0; sample < 3; sample++)
                    outside &= SupportOriented(SupportOrient2(half[edge], half[(edge + 1) % 3], direct[sample]), halfWinding).Upper < 0f;
                if (outside) return ProofClassification.Impossible;
            }
            return ProofClassification.Ambiguous;
        }

        private static bool SupportSegmentMayEnter(float2 first, float2 last, ReadOnlySpan<float2> half, int winding)
        {
            float lower = 0f, upper = 1f;
            for (int edge = 0; edge < 3; edge++)
            {
                FloatInterval a = SupportOriented(SupportOrient2(half[edge], half[(edge + 1) % 3], first), winding);
                FloatInterval b = SupportOriented(SupportOrient2(half[edge], half[(edge + 1) % 3], last), winding);
                if (a.Upper <= 0f && b.Upper <= 0f) return false;
                if (a.Upper > 0f && b.Upper > 0f) continue;
                FloatInterval numerator, denominator;
                bool lowerLimit = a.Upper <= 0f;
                if (lowerLimit)
                {
                    numerator = FloatInterval.Singleton(-a.Upper);
                    denominator = FloatInterval.Subtract(FloatInterval.Singleton(b.Upper), FloatInterval.Singleton(a.Upper));
                }
                else
                {
                    numerator = FloatInterval.Singleton(a.Upper);
                    denominator = FloatInterval.Subtract(FloatInterval.Singleton(a.Upper), FloatInterval.Singleton(b.Upper));
                }
                if (!(denominator.Lower > 0f)) return true;
                FloatInterval limit = FloatInterval.Divide(numerator, denominator);
                if (lowerLimit) lower = Math.Max(lower, limit.Lower); else upper = Math.Min(upper, limit.Upper);
                if (lower >= upper) return false;
            }
            return lower < upper;
        }

        private static bool SupportContainsWitness(float2 a, float2 b, float2 c, Interval2 witness)
        {
            int winding = SupportWinding(SupportOrient2(a, b, c));
            if (winding == 0) return false;
            Span<float2> vertices = stackalloc float2[3] { a, b, c };
            for (int edge = 0; edge < 3; edge++)
            {
                float2 first = vertices[edge], last = vertices[(edge + 1) % 3];
                FloatInterval x = FloatInterval.Subtract(witness.X, FloatInterval.Singleton(first.x));
                FloatInterval y = FloatInterval.Subtract(witness.Y, FloatInterval.Singleton(first.y));
                FloatInterval side = FloatInterval.Subtract(FloatInterval.Multiply(SupportDifference(last.x, first.x), y),
                    FloatInterval.Multiply(SupportDifference(last.y, first.y), x));
                if (SupportOriented(side, winding).Lower < 0f) return false;
            }
            return true;
        }

        private static bool SupportSameRoot(PhaseRootEvidence first, PhaseRootEvidence second) =>
            first.Classification == ProofClassification.Certain && second.Classification == ProofClassification.Certain &&
            math.all(first.Symbol.Junction == second.Symbol.Junction) &&
            (first.Symbol.Tag & 0x1fffu) == (second.Symbol.Tag & 0x1fffu) &&
            math.all(math.asuint(new float4(first.Root.X.Lower, first.Root.X.Upper, first.Root.Y.Lower, first.Root.Y.Upper)) ==
                math.asuint(new float4(second.Root.X.Lower, second.Root.X.Upper, second.Root.Y.Lower, second.Root.Y.Upper)));

        // An existing generated edge incidence must identify the endpoints
        // first. These exact symbols/intervals reproduce the same positions;
        // coincident positions or normals alone are never identity.
        internal static uint SupportPairAxes(PhaseRootEvidence firstRoot, PhaseRootEvidence lastRoot,
            PhaseRootEvidence peerFirst, PhaseRootEvidence peerLast,
            float3 first, float3 last, float3 ownThird, float3 peerThird)
        {
            if (!SupportSameRoot(firstRoot, peerFirst) || !SupportSameRoot(lastRoot, peerLast)) return 0u;
            uint axes = 0u;
            for (int axis = 0; axis < 3; axis++)
            {
                if (first[axis] != last[axis] || first[axis] != ownThird[axis] || first[axis] != peerThird[axis]) continue;
                int u = (axis + 1) % 3, v = (axis + 2) % 3;
                float2 a = new float2(first[u], first[v]), b = new float2(last[u], last[v]);
                FloatInterval own = SupportOrient2(a, b, new float2(ownThird[u], ownThird[v]));
                FloatInterval peer = SupportOrient2(a, b, new float2(peerThird[u], peerThird[v]));
                if ((own.Lower > 0f && peer.Upper < 0f) || (own.Upper < 0f && peer.Lower > 0f)) axes |= 1u << axis;
            }
            return axes;
        }

        internal const uint SupportCoverageOverlap = 1u;
        internal const uint SupportCoverageWitness = 2u;
        internal const uint SupportCoverageBoundary = 4u;
        internal const uint SupportCoverageComplete = 8u;

        // OR these contributions across the COMPLETE source set, then call
        // SupportCoverageResult once. A local witness is not early coverage:
        // another carrier can still contribute an uncancelled hole boundary.
        internal static uint SupportCarrierCoverageProof(ReadOnlySpan<float3> positions,
            uint active, uint pairedOuterEdges, int3 cell, int face, int halfFace)
        {
            if (positions.Length != 7 || ((active | pairedOuterEdges) & ~63u) != 0u ||
                (uint)face >= 6u || (uint)halfFace >= 2u)
                return SupportCoverageOverlap | SupportCoverageBoundary;
            int axis = face >> 1, u = (axis + 1) % 3, v = (axis + 2) % 3;
            Span<float2> half = stackalloc float2[3], sites = stackalloc float2[7];
            float plane = DirtFaceGridPosition(cell, face, halfFace, 0)[axis];
            for (int i = 0; i < 3; i++)
            {
                float3 p = DirtFaceGridPosition(cell, face, halfFace, i);
                half[i] = new float2(p[u], p[v]);
            }
            for (int site = 0; site < 7; site++) sites[site] = new float2(positions[site][u], positions[site][v]);
            uint coplanar = 0u; bool overlaps = false; int commonWinding = 0;
            Span<float3> direct = stackalloc float3[3];
            for (int wedge = 0; wedge < 6; wedge++)
            {
                if ((active & (1u << wedge)) == 0u) continue;
                int first = 1 + wedge, last = 1 + (wedge + 1) % 6;
                if (positions[0][axis] != plane || positions[first][axis] != plane || positions[last][axis] != plane) continue;
                coplanar |= 1u << wedge;
                direct[0] = positions[0]; direct[1] = positions[first]; direct[2] = positions[last];
                int directWinding = SupportWinding(SupportOrient2(sites[0], sites[first], sites[last]));
                if (directWinding == 0 || (commonWinding != 0 && directWinding != commonWinding))
                    return SupportCoverageOverlap | SupportCoverageBoundary;
                commonWinding = directWinding;
                ProofClassification individual = SupportTriangleCoverage(direct, cell, face, halfFace);
                if (individual == ProofClassification.Certain) return SupportCoverageOverlap | SupportCoverageComplete;
                overlaps |= individual == ProofClassification.Ambiguous;
            }
            if (!overlaps) return 0u;
            int winding = SupportWinding(SupportOrient2(half[0], half[1], half[2]));
            if (winding == 0) return SupportCoverageOverlap | SupportCoverageBoundary;
            uint proof = SupportCoverageOverlap;
            bool ownBoundary = false;
            for (int wedge = 0; wedge < 6; wedge++)
            {
                if ((coplanar & (1u << wedge)) == 0u) continue;
                int first = 1 + wedge, last = 1 + (wedge + 1) % 6;
                if (SupportSegmentMayEnter(sites[first], sites[last], half, winding))
                {
                    ownBoundary = true;
                    if ((pairedOuterEdges & (1u << wedge)) == 0u) proof |= SupportCoverageBoundary;
                }
                if ((coplanar & (1u << ((wedge + 5) % 6))) == 0u &&
                    SupportSegmentMayEnter(sites[0], sites[first], half, winding))
                { ownBoundary = true; proof |= SupportCoverageBoundary; }
                if ((coplanar & (1u << ((wedge + 1) % 6))) == 0u &&
                    SupportSegmentMayEnter(sites[last], sites[0], half, winding))
                { ownBoundary = true; proof |= SupportCoverageBoundary; }
            }
            Span<FloatInterval> witness = stackalloc FloatInterval[2];
            for (int component = 0; component < 2; component++)
                witness[component] = FloatInterval.Multiply(FloatInterval.Add(FloatInterval.Add(
                    FloatInterval.Multiply(FloatInterval.Singleton(half[0][component]), FloatInterval.Singleton(2f)),
                    FloatInterval.Singleton(half[1][component])), FloatInterval.Singleton(half[2][component])), FloatInterval.Singleton(0.25f));
            var interior = new Interval2(witness[0], witness[1]);
            for (int wedge = 0; wedge < 6; wedge++)
                if ((coplanar & (1u << wedge)) != 0u && SupportContainsWitness(sites[0],
                    sites[1 + wedge], sites[1 + (wedge + 1) % 6], interior)) proof |= SupportCoverageWitness;
            if (!ownBoundary && (proof & SupportCoverageWitness) != 0u) proof |= SupportCoverageComplete;
            return proof;
        }

        internal static ProofClassification SupportCoverageResult(uint proof)
        {
            if ((proof & SupportCoverageComplete) != 0u ||
                (proof & (SupportCoverageWitness | SupportCoverageBoundary)) == SupportCoverageWitness)
                return ProofClassification.Certain;
            return (proof & SupportCoverageOverlap) != 0u ? ProofClassification.Ambiguous : ProofClassification.Impossible;
        }

        internal static ProofClassification SupportCarrierCoverage(ReadOnlySpan<float3> positions,
            uint active, int3 cell, int face, int halfFace)
        {
            return SupportCoverageResult(SupportCarrierCoverageProof(positions, active, 0u, cell, face, halfFace));
        }

        // A same-shell power comparison has no constant term. Generate its
        // cuts with the existing exact sphere/plane solver, not angular samples.
        // This is the supplied R1 face chart, not a new R2/R3 spatial domain.
        private static void AddR1FlagOrderCuts(PetalRule[] petals, NodeRule[] nodes,
            int3 line, List<ExactBoundaryPoint> cuts)
        {
            if (Dot(line, line) != 1) return;
            for (int p = 0; p < petals.Length; p++)
            {
                PetalRule first = petals[p];
                if (!math.all(nodes[first.FaceNode].Direction == line)) continue;
                for (int q = p + 1; q < petals.Length; q++)
                {
                    PetalRule second = petals[q];
                    if (first.FaceNode != second.FaceNode) continue;
                    int a = first.EdgeNode, b = second.EdgeNode;
                    if (a == b) { a = first.CornerNode; b = second.CornerNode; }
                    BuildBoundaryPoints(line, nodes[a].Direction - nodes[b].Direction,
                        Rational.Zero, cuts);
                }
            }
        }

        // The same max-power order applies to every original shell anchor.
        // Keep every existing radical cut. Add only order cuts at which the
        // incident flag mask changes; comparisons between losing alternatives
        // do not partition an otherwise identical admission domain.
        private static void AddAnchorFlagOrderCuts(PetalRule[] petals, NodeRule[] nodes,
            int3 line, List<ExactBoundaryPoint> cuts)
        {
            if (Dot(line, line) == 1)
            {
                AddR1FlagOrderCuts(petals, nodes, line, cuts);
                return;
            }
            int forward = FindNode(nodes, line), backward = FindNode(nodes, -line);
            var candidates = new List<ExactBoundaryPoint>(32);
            for (int endpoint = 0; endpoint < 2; endpoint++)
            {
                int node = endpoint == 0 ? forward : backward;
                for (int face = 0; face < 6; face++)
                {
                    if (!FaceIncidentToAnchor(petals, node, face)) continue;
                    for (int p = 0; p < petals.Length; p++)
                    {
                        PetalRule first = petals[p];
                        if (first.FaceNode != face) continue;
                        for (int q = p + 1; q < petals.Length; q++)
                        {
                            PetalRule second = petals[q];
                            if (second.FaceNode != face) continue;
                            int a = first.EdgeNode, b = second.EdgeNode;
                            if (a == b) { a = first.CornerNode; b = second.CornerNode; }
                            int3 difference = nodes[a].Direction - nodes[b].Direction;
                            // Both endpoint loops use the canonical basis.
                            // Translate a backward owner's order plane before
                            // intersecting the canonical forward loop.
                            Rational offset = new(Dot(difference,
                                line - nodes[node].Direction), 2);
                            BuildBoundaryPoints(line, difference, offset, candidates);
                        }
                    }
                }
            }
            foreach (ExactBoundaryPoint point in candidates)
            {
                bool changes = false;
                for (int endpoint = 0; endpoint < 2; endpoint++)
                {
                    int node = endpoint == 0 ? forward : backward;
                    ulong before = AnchorFlagMaskAtCut(petals, nodes, line, node, point, -1);
                    ulong after = AnchorFlagMaskAtCut(petals, nodes, line, node, point, 1);
                    ulong equality = AnchorFlagMaskAtCut(petals, nodes, line, node, point, 0);
                    changes |= before != after || equality != before;
                }
                if (changes) AddUnique(cuts, point);
            }
        }

        private static bool FaceIncidentToAnchor(PetalRule[] petals, int node, int face)
        {
            foreach (PetalRule petal in petals)
                if (petal.FaceNode == face && (petal.FaceNode == node ||
                    petal.EdgeNode == node || petal.CornerNode == node)) return true;
            return false;
        }

        private static int PowerFlagAtCut(PetalRule[] petals, NodeRule[] nodes,
            int3 line, int3 loopDirection, int face, ExactBoundaryPoint point, int side)
        {
            int edge = -1, corner = -1, selected = -1;
            for (int p = 0; p < petals.Length; p++)
            {
                PetalRule candidate = petals[p];
                if (candidate.FaceNode != face) continue;
                int next = candidate.EdgeNode;
                int order = edge < 0 ? 1 : SectorPredicateLimit(line, loopDirection, point,
                    nodes[next].Direction - nodes[edge].Direction, Rational.Zero, side);
                if (order > 0 || (order == 0 && next < edge)) edge = next;
            }
            for (int p = 0; p < petals.Length; p++)
            {
                PetalRule candidate = petals[p];
                if (candidate.FaceNode != face || candidate.EdgeNode != edge) continue;
                int next = candidate.CornerNode;
                int order = corner < 0 ? 1 : SectorPredicateLimit(line, loopDirection, point,
                    nodes[next].Direction - nodes[corner].Direction, Rational.Zero, side);
                if (order > 0 || (order == 0 && next < corner))
                { corner = next; selected = p; }
            }
            if (selected < 0) throw new InvalidOperationException("Power order has no incident face flag.");
            return selected;
        }

        private static ulong AnchorFlagMaskAtCut(PetalRule[] petals, NodeRule[] nodes,
            int3 line, int node, ExactBoundaryPoint point, int side)
        {
            ulong result = 0u;
            for (int face = 0; face < 6; face++)
            {
                if (!FaceIncidentToAnchor(petals, node, face)) continue;
                int selected = PowerFlagAtCut(petals, nodes, line,
                    nodes[node].Direction, face, point, side);
                PetalRule flag = petals[selected];
                if (flag.FaceNode == node || flag.EdgeNode == node || flag.CornerNode == node)
                    result |= 1UL << selected;
            }
            return result;
        }

        // Exact value at the boundary, or its positive-angle interior limit.
        // No finite step or epsilon is used to choose an interior representative.
        private static int SectorPredicateSign(int3 line, int3 ownerDirection,
            ExactBoundaryPoint start, int3 q, Rational offset, bool interior)
            => SectorPredicateLimit(line, ownerDirection, start, q, offset, interior ? 1 : 0);

        private static int SectorPredicateLimit(int3 line, int3 ownerDirection,
            ExactBoundaryPoint start, int3 q, Rational offset, int side)
        {
            Rational alpha = new Rational(Dot(q, ownerDirection), 2) - offset;
            LinearSurd radial = start.RadialDot(line, q);
            int sign = new LinearSurd(radial.A + alpha, radial.B, radial.Radicand).Sign;
            if (sign != 0 || side == 0) return sign;
            sign = start.RadialDot(line, Cross(q, line)).Sign;
            // The first nonzero derivative gives the exact one-sided limit.
            // At a tangent equality the second derivative has alpha's sign
            // on both sides. No finite angular step is taken.
            return sign != 0 ? side * sign : alpha.Sign;
        }

#if UNITY_EDITOR
        public static void BuildL2BoundaryFlags(out ushort[] offsets,
            out byte[] indices, out ulong[] masks)
        {
            // The lookup stores only deduplicated exact admission masks.
            // No exact boundary coordinate or dense 48-bit-per-knot table is
            // introduced into the runtime geometry representation.
            var cuts = new List<ExactBoundaryPoint>[LineClassCount];
            for (int line = 0; line < cuts.Length; line++)
            {
                cuts[line] = BuildLineBoundaryPoints(PetalsValue, NodesValue, LinesValue[line].Direction);
                if (cuts[line].Count != LinesValue[line].SectorCount)
                    throw new InvalidOperationException("L2 boundary flags differ from the canonical arrangement.");
            }
            offsets = new ushort[L2KnotCount + 1];
            var entries = new List<byte>(L2KnotCount * MaximumSectorCount);
            var unique = new List<ulong>();
            var byMask = new Dictionary<ulong, byte>();
            for (int knot = 0; knot < L2KnotCount; knot++)
            {
                offsets[knot] = checked((ushort)entries.Count);
                if (!TryGetL2KnotLoop(knot, out _, out int3 junction, out int lineClass))
                    throw new InvalidOperationException("An L2 boundary has no original loop identity.");
                int3 line = LinesValue[lineClass].Direction;
                foreach (ExactBoundaryPoint cut in cuts[lineClass])
                {
                    ulong mask = 0u;
                    for (int face = 0; face < 6; face++)
                    {
                        // X/a_L = P + (Joffset-line)/2. Positive scale a_L
                        // cancels from equal-shell Phi differences. Thus the
                        // existing exact predicate uses Joffset as its centre
                        // argument, not an unrelated root-face direction.
                        int selected = PowerFlagAtCut(PetalsValue, NodesValue,
                            line, junction, face, cut, 0);
                        PetalRule flag = PetalsValue[selected];
                        int3 edge = NodesValue[flag.EdgeNode].Direction - NodesValue[flag.FaceNode].Direction;
                        int3 corner = NodesValue[flag.CornerNode].Direction - NodesValue[flag.EdgeNode].Direction;
                        if (SectorPredicateLimit(line, junction, cut, corner, Rational.Zero, 0) < 0 ||
                            SectorPredicateLimit(line, junction, cut, edge - corner, Rational.Zero, 0) < 0)
                            throw new InvalidOperationException("Exact boundary power ownership fails source containment.");
                        mask |= 1UL << selected;
                    }
                    if (!byMask.TryGetValue(mask, out byte index))
                    {
                        if (unique.Count >= 256)
                            throw new InvalidOperationException("L2 boundary flag lookup exceeds 256 distinct masks.");
                        index = checked((byte)unique.Count);
                        byMask.Add(mask, index); unique.Add(mask);
                    }
                    entries.Add(index);
                }
            }
            offsets[L2KnotCount] = checked((ushort)entries.Count);
            // HLSL packs two offsets/four byte indices in one uint; masks are
            // uint2. Prove the actual emitted payload, including final padding.
            int payloadBytes = 4 * ((offsets.Length + 1) / 2 + (entries.Count + 3) / 4) + 8 * unique.Count;
            if (payloadBytes > 65536)
                throw new InvalidOperationException("L2 boundary flag lookup exceeds its 64KiB finite budget.");
            indices = entries.ToArray(); masks = unique.ToArray();
        }

        // One local sector ordinal for each canonical boundary, shared by
        // both directed endpoints of the loop. This is only ownership data:
        // it neither proves that a measured root equals a boundary nor alters
        // that root's outward metric enclosure.
        public static byte[] BuildAnchorBoundaryOwners()
        {
            var owners = new byte[SectorBoundariesValue.Length];
            for (int lineClass = 0; lineClass < LinesValue.Length; lineClass++)
            {
                LineRule line = LinesValue[lineClass];
                List<ExactBoundaryPoint> cuts = BuildLineBoundaryPoints(
                    PetalsValue, NodesValue, line.Direction);
                if (cuts.Count != line.SectorCount)
                    throw new InvalidOperationException(
                        "Boundary ownership differs from the canonical sector arrangement.");
                int canonicalNode = FindNode(NodesValue, line.Direction);
                int canonicalFace = -1;
                for (int face = 0; face < 6; face++)
                    if (FaceIncidentToAnchor(PetalsValue, canonicalNode, face))
                    { canonicalFace = face; break; }
                if (canonicalFace < 0)
                    throw new InvalidOperationException("A canonical loop has no incident R1 face.");
                for (int boundary = 0; boundary < cuts.Count; boundary++)
                {
                    ExactBoundaryPoint cut = cuts[boundary];
                    int before = PowerFlagAtCut(PetalsValue, NodesValue, line.Direction,
                        line.Direction, canonicalFace, cut, -1);
                    int at = PowerFlagAtCut(PetalsValue, NodesValue, line.Direction,
                        line.Direction, canonicalFace, cut, 0);
                    int after = PowerFlagAtCut(PetalsValue, NodesValue, line.Direction,
                        line.Direction, canonicalFace, cut, 1);
                    uint sides = (at == before ? 1u : 0u) | (at == after ? 2u : 0u);
                    if (sides == 0u)
                        throw new InvalidOperationException(
                            $"Canonical face boundary tie has no adjacent sector: line={lineClass}, " +
                            $"boundary={boundary}, node={canonicalNode}, face={canonicalFace}, " +
                            $"before/at/after petals={before}/{at}/{after}.");

                    // One canonical face fixes the shared Sigma. If its flag
                    // does not change here, use the canonical [start,end)
                    // convention. Opposite endpoint flags are stored below
                    // at side=0; their local tie may prefer the other side.
                    int sector = (sides & 2u) != 0u ? boundary :
                        (boundary + cuts.Count - 1) % cuts.Count;
                    owners[line.SectorOffset + boundary] = checked((byte)sector);
                }
            }
            return owners;
        }

        public static ulong[] BuildAnchorBoundaryPetalMasks()
        {
            var masks = new ulong[2 * SectorBoundariesValue.Length];
            for (int node = 0; node < NodesValue.Length; node++)
            {
                NodeRule anchor = NodesValue[node];
                LineRule line = LinesValue[anchor.LineClass];
                List<ExactBoundaryPoint> cuts = BuildLineBoundaryPoints(PetalsValue, NodesValue, line.Direction);
                if (cuts.Count != line.SectorCount)
                    throw new InvalidOperationException("Boundary flags differ from the canonical arrangement.");
                for (int boundary = 0; boundary < cuts.Count; boundary++)
                {
                    ulong at = AnchorFlagMaskAtCut(PetalsValue, NodesValue, line.Direction, node, cuts[boundary], 0);
                    if ((at & ~NodeIncidentPetalsValue[node]) != 0u ||
                        (anchor.Shell == Shell.R1Core && (at == 0u || (at & (at - 1u)) != 0u)))
                        throw new InvalidOperationException("Exact boundary flag ownership is not incident and unique.");
                    masks[2 * (line.SectorOffset + boundary) + (anchor.Orientation < 0 ? 1 : 0)] = at;
                }
            }
            return masks;
        }

        public static ulong[] BuildAnchorSectorPetalMasks()
        {
            var masks = new ulong[2 * SectorBoundariesValue.Length];
            for (int node = 0; node < NodesValue.Length; node++)
            {
                NodeRule anchor = NodesValue[node];
                LineRule line = LinesValue[anchor.LineClass];
                List<ExactBoundaryPoint> cuts = BuildLineBoundaryPoints(
                    PetalsValue, NodesValue, line.Direction);
                if (cuts.Count != line.SectorCount)
                    throw new InvalidOperationException("Anchor order differs from the canonical sector arrangement.");
                ulong covered = 0u;
                for (int sector = 0; sector < cuts.Count; sector++)
                {
                    ulong mask = AnchorFlagMaskAtCut(PetalsValue, NodesValue,
                        line.Direction, node, cuts[sector], 1);
                    ulong nextLimit = AnchorFlagMaskAtCut(PetalsValue, NodesValue,
                        line.Direction, node, cuts[(sector + 1) % cuts.Count], -1);
                    if (mask != nextLimit || (mask & ~NodeIncidentPetalsValue[node]) != 0u)
                        throw new InvalidOperationException("An anchor flag changes inside a generated sector.");
                    if (anchor.Shell == Shell.R1Core && (mask == 0u || (mask & (mask - 1u)) != 0u))
                        throw new InvalidOperationException("An R1 face sector must own one flag.");
                    if (anchor.Shell == Shell.R1Core)
                    {
                        // Preserve the original R1 chart proof, including
                        // exact tie ownership, over the shared order data.
                        ValidateR1PowerChart(node, cuts[sector], 1);
                        ValidateR1PowerChart(node, cuts[sector], 0);
                    }
                    masks[2 * (line.SectorOffset + sector) +
                        (anchor.Orientation < 0 ? 1 : 0)] = mask;
                    covered |= mask;
                }
                if (covered != NodeIncidentPetalsValue[node])
                    throw new InvalidOperationException("Anchor order fails to cover its incident flags.");
            }
            return masks;
        }

        private static void ValidateR1PowerChart(int faceNode, ExactBoundaryPoint point, int side)
        {
            NodeRule face = NodesValue[faceNode];
            int3 line = LinesValue[face.LineClass].Direction;
            int selected = PowerFlagAtCut(PetalsValue, NodesValue, line,
                face.Direction, faceNode, point, side);
            int edge = PetalsValue[selected].EdgeNode, corner = PetalsValue[selected].CornerNode;
            int3 j = NodesValue[edge].Direction - face.Direction;
            int3 k = NodesValue[corner].Direction - NodesValue[edge].Direction;
            // aL=1 in the exact codegen chart: 0<=k.X<=j.X<=1 is
            // precisely 0<=s_k*xi_k<=s_j*xi_j<=2 for xi=2*(X-C)/aL.
            if (SectorPredicateLimit(line, face.Direction, point, k, Rational.Zero, side) < 0 ||
                SectorPredicateLimit(line, face.Direction, point, j - k, Rational.Zero, side) < 0 ||
                SectorPredicateLimit(line, face.Direction, point, j, new Rational(1), side) > 0)
                throw new InvalidOperationException("R1 power order disagrees with its clipped face chart.");
        }
#endif

        /// <summary>
        /// Signs of all directed radical planes incident to one loop node.
        /// Bits address the existing 26 Node entries, not new plane IDs.
        /// Incident bits outside Positive/Negative are identically zero on
        /// this loop. This is provenance, not a petal-admission decision.
        /// </summary>
        public readonly struct RadicalSectorPattern
        {
            public readonly uint Incident;
            public readonly uint Positive;
            public readonly uint Negative;

            internal RadicalSectorPattern(uint incident, uint positive,
                uint negative)
            {
                Incident = incident;
                Positive = positive;
                Negative = negative;
            }
        }

#if UNITY_EDITOR
        // Code generation only. Runtime classification reads the emitted
        // finite table after the root interval is CERTAIN in a strict sector.
        // No side of a flag is inferred from its sector ordinal here.
        public static RadicalSectorPattern[] BuildRadicalSectorPatterns()
        {
            var result = new RadicalSectorPattern[2 * SectorBoundariesValue.Length];
            var assigned = new bool[result.Length];
            for (int nodeIndex = 0; nodeIndex < NodesValue.Length; nodeIndex++)
            {
                NodeRule node = NodesValue[nodeIndex];
                LineRule line = LinesValue[node.LineClass];
                int3 representative = CanonicalLinesValue[node.LineClass];
                List<ExactBoundaryPoint> boundaries = BuildLineBoundaryPoints(
                    PetalsValue, NodesValue, representative);
                if (boundaries.Count != line.SectorCount)
                    throw new InvalidOperationException(
                        "Radical provenance differs from the generated sector partition.");

                uint incident = 0u;
                ulong petals = NodeIncidentPetalsValue[nodeIndex];
                for (int petal = 0; petal < PetalsValue.Length; petal++)
                {
                    if ((petals & (1UL << petal)) == 0u) continue;
                    for (int site = 0; site < 3; site++)
                        incident |= 1u << PetalsValue[petal].Node(site);
                }

                // The opposite endpoint has the same canonical loop basis,
                // but its directed radical plane is relative to its own
                // centre. Translate that equation before comparing cuts.
                for (int planeNode = 0; planeNode < NodesValue.Length; planeNode++)
                {
                    if ((incident & (1u << planeNode)) == 0u) continue;
                    int3 q = NodesValue[planeNode].Direction;
                    Rational translatedOffset = new(Dot(q, q) +
                        Dot(q, representative - node.Direction), 2);
                    var cuts = new List<ExactBoundaryPoint>(2);
                    BuildBoundaryPoints(representative, q, translatedOffset, cuts);
                    foreach (ExactBoundaryPoint cut in cuts)
                    {
                        bool represented = false;
                        foreach (ExactBoundaryPoint boundary in boundaries)
                            represented |= ExactBoundaryPoint.SquaredDistanceSign(
                                cut, boundary) == 0;
                        if (!represented)
                            throw new InvalidOperationException(
                                $"Directed radical plane {planeNode} crosses the interior " +
                                $"of a generated sector at loop node {nodeIndex}.");
                    }
                }

                for (int sector = 0; sector < boundaries.Count; sector++)
                {
                    ExactBoundaryPoint start = boundaries[sector];
                    uint positive = 0u, negative = 0u;
                    for (int planeNode = 0; planeNode < NodesValue.Length; planeNode++)
                    {
                        uint bit = 1u << planeNode;
                        if ((incident & bit) == 0u) continue;
                        int3 q = NodesValue[planeNode].Direction;
                        int sign = SectorPredicateSign(representative, node.Direction,
                            start, q, new Rational(Dot(q, q), 2), true);
                        if (sign > 0) positive |= bit;
                        else if (sign < 0) negative |= bit;
                    }

                    int index = 2 * (line.SectorOffset + sector) +
                        (node.Orientation < 0 ? 1 : 0);
                    if (assigned[index] || (positive & negative) != 0u ||
                        ((positive | negative) & ~incident) != 0u)
                        throw new InvalidOperationException(
                            "Directed radical sector provenance is not unique.");
                    result[index] = new RadicalSectorPattern(incident, positive, negative);
                    assigned[index] = true;
                }
            }
            foreach (bool present in assigned)
                if (!present) throw new InvalidOperationException(
                    "A directed loop has no generated radical provenance.");
            return result;
        }
#endif
    }
}
