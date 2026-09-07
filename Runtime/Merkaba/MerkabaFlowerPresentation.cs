using System;
using System.Collections.Generic;
using System.IO;
using Unity.Mathematics;

namespace Genesis.RoomScan
{
    // Disposable export product of the shared evaluator. Position identity is
    // independent of carrier normals, winding, captured colour and material.
    internal sealed class MerkabaFlowerPresentation
    {
        internal readonly struct KnotAddress : IEquatable<KnotAddress>
        {
            internal readonly int3 Junction;
            internal readonly uint Tag;
            internal KnotAddress(MerkabaFlowerSymbolKey symbol)
            { Junction = symbol.Junction; Tag = symbol.Tag & 0x1fffu; }
            public bool Equals(KnotAddress other) => math.all(Junction == other.Junction) && Tag == other.Tag;
            public override bool Equals(object value) => value is KnotAddress other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(Junction.x, Junction.y, Junction.z, Tag);
        }

        internal sealed class Carrier
        {
            internal int3 Owner;
            internal MerkabaFlowerSymbolRecord Symbol;
            internal readonly uint[] PositionIndices = new uint[7];
            internal MerkabaFlowerSkinDrawHeader SkinHeader;
            internal MerkabaFlowerSkinDrawSample[] SkinSamples;
            // Transient coverage certificate from the same frozen reader;
            // this is neither a persisted adjacency graph nor scan truth.
            internal uint3 CoveragePairedEdges;
        }

        internal readonly List<KnotAddress> Knots = new();
        internal readonly List<float3> Positions = new();
        internal readonly List<Carrier> Carriers = new();
        internal readonly MerkabaTileAddress Tile;
        internal int OccupiedOwners;
        internal int UnresolvedWedges;
        internal int TriangleCount;

        internal MerkabaFlowerPresentation(MerkabaTileAddress tile) => Tile = tile;

        internal static MerkabaFlowerPresentation Build(
            MerkabaSphereFlowerAuthority.SnapshotReader reader,
            MerkabaTileAddress tile, float2 planeBounds)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            var result = new MerkabaFlowerPresentation(tile);
            var positionsByKnot = new Dictionary<KnotAddress, uint>();
            Span<MerkabaSphereFlowerAuthority.PhaseRootEvidence> roots =
                stackalloc MerkabaSphereFlowerAuthority.PhaseRootEvidence[7];
            Span<float3> positions = stackalloc float3[7];
            for (int local = 0; local < MerkabaSpatial.KernelsPerTile; local++)
            {
                int3 owner = MerkabaSpatial.Decode(tile.BlockCoord, tile.LocalAddress, local);
                if (!reader.TryReadOwner(owner, out KernelState state, out _))
                    throw new InvalidDataException("Export owner tile is unresolved in its frozen context.");
                if (!state.IsOccupied || !state.HasMeasuredSurfacePlane) continue;
                result.OccupiedOwners++;
                for (int carrierId = 0; carrierId < MerkabaSphereFlowerAuthority.L2HubCount; carrierId++)
                {
                    var status = reader.ClassifyPageCarrier(owner, carrierId, planeBounds,
                        out MerkabaFlowerSymbolRecord symbol, out uint unresolved, roots, positions);
                    result.UnresolvedWedges += math.countbits(unresolved);
                    if (status != MerkabaSphereFlowerAuthority.ProofClassification.Certain) continue;
                    var carrier = new Carrier { Owner = owner, Symbol = symbol };
                    carrier.SkinSamples = reader.ReadSkinSignal(owner, symbol, out carrier.SkinHeader);
                    carrier.CoveragePairedEdges = PairedCoverageEdges(reader, owner, carrierId,
                        symbol.ActiveWedgeMask, roots, positions, planeBounds);
                    uint used = 1u;
                    for (int wedge = 0; wedge < 6; wedge++)
                        if ((symbol.ActiveWedgeMask & (1u << wedge)) != 0u)
                            used |= (1u << (1 + wedge)) | (1u << (1 + (wedge + 1) % 6));
                    for (int site = 0; site < 7; site++)
                    {
                        if ((used & (1u << site)) == 0u) continue;
                        var key = new KnotAddress(roots[site].Symbol);
                        if (!positionsByKnot.TryGetValue(key, out uint index))
                        {
                            index = checked((uint)result.Positions.Count);
                            positionsByKnot.Add(key, index);
                            result.Knots.Add(key);
                            result.Positions.Add(positions[site]);
                        }
                        else if (math.any(math.asuint(result.Positions[(int)index]) != math.asuint(positions[site])))
                            throw new InvalidDataException("Shared Flower knot has different canonical binary32 evaluations.");
                        carrier.PositionIndices[site] = index;
                    }
                    result.TriangleCount += math.countbits(symbol.ActiveWedgeMask);
                    result.Carriers.Add(carrier);
                }
            }
            return result;
        }

        internal float3 Position(Carrier carrier, int site) => Positions[checked((int)carrier.PositionIndices[site])];

        private static uint3 PairedCoverageEdges(MerkabaSphereFlowerAuthority.SnapshotReader reader,
            int3 owner, int carrier, uint active,
            ReadOnlySpan<MerkabaSphereFlowerAuthority.PhaseRootEvidence> roots,
            ReadOnlySpan<float3> positions, float2 errors)
        {
            uint3 paired = default;
            Span<MerkabaSphereFlowerAuthority.PhaseRootEvidence> peerRoots =
                stackalloc MerkabaSphereFlowerAuthority.PhaseRootEvidence[7];
            Span<float3> peerPositions = stackalloc float3[7];
            for (int wedge = 0; wedge < 6; wedge++)
            {
                if ((active & (1u << wedge)) == 0u ||
                    !MerkabaSphereFlowerAuthority.TryL2AcrossEdge(6 * carrier + wedge, 1,
                        out int peer, out int peerEdge, out bool reversed) || !reversed ||
                    !MerkabaSphereFlowerAuthority.TryL2CanonicalWedgeOwner(owner, peer / 6, peer % 6,
                        out int3 peerOwner, out int canonicalPeer, out byte permutation)) continue;
                // Exactly the live page's complete 512-owner iteration set.
                // Merely having a halo snapshot is not proof that its other
                // boundaries were included in this page's union reduction.
                if (math.any((peerOwner >> 3) != (owner >> 3))) continue;
                var status = reader.ClassifyPageCarrier(peerOwner, canonicalPeer / 6, errors,
                    out MerkabaFlowerSymbolRecord peerSymbol, out _, peerRoots, peerPositions);
                if (status != MerkabaSphereFlowerAuthority.ProofClassification.Certain ||
                    (peerSymbol.ActiveWedgeMask & (1u << (canonicalPeer % 6))) == 0u) continue;
                int3 peerSites = MerkabaSphereFlowerAuthority.L2CarrierTriangleIndices(canonicalPeer % 6);
                int firstSite = peerSites[(permutation >> (2 * ((peerEdge + 1) % 3))) & 3];
                int lastSite = peerSites[(permutation >> (2 * peerEdge)) & 3];
                int thirdSite = peerSites[(permutation >> (2 * ((peerEdge + 2) % 3))) & 3];
                int first = 1 + wedge, last = 1 + (wedge + 1) % 6;
                uint axes = MerkabaSphereFlowerAuthority.SupportPairAxes(roots[first], roots[last],
                    peerRoots[firstSite], peerRoots[lastSite], positions[first], positions[last],
                    positions[0], peerPositions[thirdSite]);
                for (int axis = 0; axis < 3; axis++)
                    if ((axes & (1u << axis)) != 0u) paired[axis] |= 1u << wedge;
            }
            return paired;
        }

        internal MerkabaDirtFaceCoverage DirtCoverage(int3 cell, int face)
        {
            uint2 proof = default;
            Span<float3> positions = stackalloc float3[7];
            foreach (Carrier carrier in Carriers)
            {
                positions.Clear();
                uint used = 1u;
                for (int wedge = 0; wedge < 6; wedge++)
                    if ((carrier.Symbol.ActiveWedgeMask & (1u << wedge)) != 0u)
                        used |= (1u << (1 + wedge)) | (1u << (1 + (wedge + 1) % 6));
                for (int site = 0; site < 7; site++)
                    if ((used & (1u << site)) != 0u) positions[site] = Position(carrier, site);
                for (int half = 0; half < 2; half++)
                {
                    if ((proof[half] & MerkabaSphereFlowerAuthority.SupportCoverageComplete) != 0u) continue;
                    proof[half] |= MerkabaSphereFlowerAuthority.SupportCarrierCoverageProof(positions,
                        carrier.Symbol.ActiveWedgeMask, carrier.CoveragePairedEdges[face >> 1], cell, face, half);
                }
            }
            uint covered = 0u, partial = 0u;
            for (int half = 0; half < 2; half++)
            {
                var status = MerkabaSphereFlowerAuthority.SupportCoverageResult(proof[half]);
                if (status == MerkabaSphereFlowerAuthority.ProofClassification.Certain) covered |= 1u << half;
                else if (status == MerkabaSphereFlowerAuthority.ProofClassification.Ambiguous) partial |= 1u << half;
            }
            return new MerkabaDirtFaceCoverage(covered, partial);
        }

        internal void WedgeFrame(Carrier carrier, int wedge, out float3 tangent1,
            out float3 tangent2, out float3 normal, out float3 barycentricDu,
            out float3 barycentricDv)
        {
            float3 e1 = Position(carrier, 1 + wedge) - Position(carrier, 0);
            float3 e2 = Position(carrier, 1 + (wedge + 1) % 6) - Position(carrier, 0);
            float length = math.sqrt(math.dot(e1, e1));
            float3 cross = math.cross(e1, e2);
            float area = math.sqrt(math.dot(cross, cross));
            if (!(length > 0f) || !(area > 0f) || !math.isfinite(length) || !math.isfinite(area))
                throw new InvalidDataException("Admitted L2 wedge has a degenerate presentation frame.");
            tangent1 = e1 / length;
            normal = cross / area;
            tangent2 = math.cross(normal, tangent1);
            float x = math.dot(e2, tangent1), y = math.dot(e2, tangent2);
            if (!(y > 0f) || !math.isfinite(y))
                throw new InvalidDataException("Admitted L2 wedge has an invalid canonical world frame.");
            barycentricDu = new float3(-1f / length, 1f / length, 0f);
            barycentricDv = new float3((x / length - 1f) / y, -x / (length * y), 1f / y);
        }
    }
}
