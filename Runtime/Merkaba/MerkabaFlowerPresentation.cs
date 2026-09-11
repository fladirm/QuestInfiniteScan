using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Unity.Mathematics;

namespace Genesis.RoomScan
{
    // Disposable export product of the shared evaluator. Position identity is
    // independent of carrier normals, winding, captured colour and material.
    internal sealed class MerkabaFlowerPresentation
    {
        internal sealed class RequiredSupportUnresolvedException : InvalidOperationException
        {
            internal readonly MerkabaTileAddress Tile;
            internal readonly MerkabaSphereFlowerAuthority.RequiredSupportReceipt Support;

            internal RequiredSupportUnresolvedException(MerkabaTileAddress tile,
                MerkabaSphereFlowerAuthority.RequiredSupportReceipt support, int evaluatedTriangles)
                : base($"Required dual support is unresolved for Flower tile {tile}: " +
                    $"owner={support.Owner}, carrier={support.Carrier}, wedges=0x{support.WedgeMask:x2}; " +
                    $"junctionPetal={support.JunctionPetal}, rootAlternatives=0x{support.RootAlternatives:x2}; " +
                    $"page not published ({evaluatedTriangles} evaluated triangles). More support evidence is required.")
            { Tile = tile; Support = support; }
        }

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
            // Source RGB topology, separate from metric V. Presentation uses
            // their already-compiled union in SkinHeader.
            internal uint2 RgbSplitBits;
            // Transient coverage certificate from the same frozen reader;
            // this is neither a persisted adjacency graph nor scan truth.
            internal uint3 CoveragePairedEdges;
        }

        // Disposable coverage-only footprints from the same frozen reader.
        // No halo carrier is appended to exported geometry, skin or indices.
        private readonly struct CoverageFootprint
        {
            internal readonly uint Active;
            internal readonly uint3 Paired;
            internal readonly int3 RelativeOwner;
            private readonly float3 _h, _r0, _r1, _r2, _r3, _r4, _r5;
            internal CoverageFootprint(int3 relativeOwner, uint active, uint3 paired, ReadOnlySpan<float3> positions)
            {
                RelativeOwner = relativeOwner; Active = active; Paired = paired;
                _h = positions[0]; _r0 = positions[1]; _r1 = positions[2]; _r2 = positions[3];
                _r3 = positions[4]; _r4 = positions[5]; _r5 = positions[6];
            }
            internal void CopyTo(Span<float3> positions)
            {
                positions[0] = _h; positions[1] = _r0; positions[2] = _r1; positions[3] = _r2;
                positions[4] = _r3; positions[5] = _r4; positions[6] = _r5;
            }
        }

        private const int CoverageOwnerMin = -2;
        private const int CoverageOwnerMax = 10;
        private MerkabaSphereFlowerAuthority.SnapshotReader _coverageReader;
        private float2 _coverageErrors;
        private ulong _requiredSupportVersion;
        private CancellationToken _cancellationToken;
        private readonly List<CoverageFootprint> _neighborCoverage = new();
        private readonly uint[] _coverageOwnersVisited = new uint[(13 * 13 * 13 + 31) / 32];
        private bool _neighborCoverageUnresolved;

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
            MerkabaTileAddress tile, float2 planeBounds,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            var result = new MerkabaFlowerPresentation(tile)
            {
                _coverageReader = reader, _coverageErrors = planeBounds,
                _requiredSupportVersion = reader.RequiredSupportVersion,
                _cancellationToken = cancellationToken
            };
            int3 coverageOrigin = MerkabaSpatial.Decode(tile.BlockCoord, tile.LocalAddress, 0);
            var positionsByKnot = new Dictionary<KnotAddress, uint>();
            Span<MerkabaSphereFlowerAuthority.PhaseRootEvidence> roots =
                stackalloc MerkabaSphereFlowerAuthority.PhaseRootEvidence[7];
            Span<float3> positions = stackalloc float3[7];
            for (int local = 0; local < MerkabaSpatial.KernelsPerTile; local++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int3 owner = MerkabaSpatial.Decode(tile.BlockCoord, tile.LocalAddress, local);
                if (!reader.TryReadOwner(owner, out KernelState state, out _))
                    throw new InvalidDataException("Export owner tile is unresolved in its frozen context.");
                if (!state.IsOccupied || !state.HasMeasuredSurfacePlane) continue;
                result.OccupiedOwners++;
                // Inverse source incidence reaches the candidates before
                // child metric evaluation. Completion still requires every
                // actual child of its parent, before emission ownership.
                var parent = reader.BeginFlowerDecode(owner, planeBounds);
                uint4 pending = parent.ReachedCarriers;
                int carrierId;
                while (MerkabaSphereFlowerAuthority.TakeReachedCarrier(ref pending, out carrierId))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    reader.ClassifyPageCarrier(owner, carrierId, planeBounds,
                        out _, out _, out _, roots, positions, parent);
                }
                parent.Complete();
                if (parent.EvaluateCompletion(out var completion) ==
                    MerkabaSphereFlowerAuthority.ProofClassification.Ambiguous)
                    result.UnresolvedWedges++;
                pending = parent.ReachedCarriers;
                if (completion.Classification == MerkabaSphereFlowerAuthority.ProofClassification.Certain)
                    pending |= MerkabaSphereFlowerAuthority.DecodePetalCarriers[completion.CompletedPetal];
                while (MerkabaSphereFlowerAuthority.TakeReachedCarrier(ref pending, out carrierId))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var status = parent.ReadCarrier(carrierId, completion,
                        out MerkabaFlowerSymbolRecord symbol, out uint unresolved, roots, positions);
                    result.UnresolvedWedges += math.countbits(unresolved);
                    if (status != MerkabaSphereFlowerAuthority.ProofClassification.Certain) continue;
                    var carrier = new Carrier { Owner = owner, Symbol = symbol };
                    carrier.SkinSamples = reader.ReadSkinSignal(owner, symbol, out carrier.SkinHeader,
                        out carrier.RgbSplitBits);
                    carrier.CoveragePairedEdges = PairedCoverageEdges(reader, coverageOrigin, owner, carrierId,
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
            // No caller receives a thinner successful page when any required
            // dual cover was mixed/uncertain. Export's existing transaction
            // reports this region and preserves its previous published file.
            result.RequireResolvedSupport();
            return result;
        }

        private void RequireResolvedSupport()
        {
            if (_coverageReader != null &&
                _coverageReader.TryRequiredSupportSince(_requiredSupportVersion, out var receipt))
                throw new RequiredSupportUnresolvedException(Tile, receipt, TriangleCount);
        }

        internal float3 Position(Carrier carrier, int site) => Positions[checked((int)carrier.PositionIndices[site])];

        private static uint3 PairedCoverageEdges(MerkabaSphereFlowerAuthority.SnapshotReader reader,
            int3 coverageOrigin, int3 owner, int carrier, uint active,
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
                // Exactly the GPU's complete conservative source stencil,
                // not arbitrary residents outside the accumulated domain.
                long dx = (long)peerOwner.x - coverageOrigin.x;
                long dy = (long)peerOwner.y - coverageOrigin.y;
                long dz = (long)peerOwner.z - coverageOrigin.z;
                if (dx < CoverageOwnerMin || dx > CoverageOwnerMax ||
                    dy < CoverageOwnerMin || dy > CoverageOwnerMax ||
                    dz < CoverageOwnerMin || dz > CoverageOwnerMax) continue;
                var status = reader.ClassifyPageCarrier(peerOwner, canonicalPeer / 6, errors,
                    out MerkabaFlowerSymbolRecord peerSymbol, out _, peerRoots, peerPositions,
                    requiredSupport: false);
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

        private void ReadNeighborCoverage(int3 first, int3 last)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (_coverageReader == null)
            { _neighborCoverageUnresolved = true; return; }
            int3 origin = MerkabaSpatial.Decode(Tile.BlockCoord, Tile.LocalAddress, 0);
            Span<MerkabaSphereFlowerAuthority.PhaseRootEvidence> roots =
                stackalloc MerkabaSphereFlowerAuthority.PhaseRootEvidence[7];
            Span<float3> positions = stackalloc float3[7];
            // The requested exposed face reaches this box, never the whole
            // 13^3 halo. Repeated queries reuse decoded actual footprints.
            for (int z = first.z; z <= last.z; z++)
                for (int y = first.y; y <= last.y; y++)
                    for (int x = first.x; x <= last.x; x++)
                    {
                        _cancellationToken.ThrowIfCancellationRequested();
                        if (x >= 0 && x <= 7 && y >= 0 && y <= 7 && z >= 0 && z <= 7) continue;
                        int index = x + 2 + 13 * (y + 2 + 13 * (z + 2));
                        uint bit = 1u << (index & 31);
                        if ((_coverageOwnersVisited[index >> 5] & bit) != 0u) continue;
                        _coverageOwnersVisited[index >> 5] |= bit;
                        long ox = (long)origin.x + x, oy = (long)origin.y + y, oz = (long)origin.z + z;
                        if (ox < int.MinValue || ox > int.MaxValue || oy < int.MinValue || oy > int.MaxValue ||
                            oz < int.MinValue || oz > int.MaxValue)
                        { _neighborCoverageUnresolved = true; continue; }
                        int3 owner = new int3((int)ox, (int)oy, (int)oz);
                        if (!_coverageReader.TryReadOwner(owner, out KernelState state, out _))
                        { _neighborCoverageUnresolved = true; continue; }
                        if (!state.IsOccupied || !state.HasMeasuredSurfacePlane) continue;
                        var decode = _coverageReader.BeginFlowerDecode(owner, _coverageErrors);
                        uint4 pending = decode.ReachedCarriers;
                        while (MerkabaSphereFlowerAuthority.TakeReachedCarrier(ref pending, out int carrier))
                        {
                            _cancellationToken.ThrowIfCancellationRequested();
                            var status = _coverageReader.ClassifyPageCarrier(owner, carrier, _coverageErrors,
                                out MerkabaFlowerSymbolRecord symbol, out _, out _, roots, positions,
                                decode, recordParentDirect: false);
                            if (status != MerkabaSphereFlowerAuthority.ProofClassification.Certain) continue;
                            uint3 paired = PairedCoverageEdges(_coverageReader, origin, owner, carrier,
                                symbol.ActiveWedgeMask, roots, positions, _coverageErrors);
                            _neighborCoverage.Add(new CoverageFootprint(new int3(x, y, z),
                                symbol.ActiveWedgeMask, paired, positions));
                        }
                    }
        }

        internal MerkabaDirtFaceCoverage DirtCoverage(int3 cell, int face)
        {
            RequireResolvedSupport();
            uint2 proof = default;
            int3 origin = MerkabaSpatial.Decode(Tile.BlockCoord, Tile.LocalAddress, 0);
            int3 relative = new(checked(cell.x - origin.x), checked(cell.y - origin.y), checked(cell.z - origin.z));
            MerkabaSphereFlowerAuthority.DirtCoverageOwnerBounds(relative, face, out int3 first, out int3 last);
            Span<float3> positions = stackalloc float3[7];
            foreach (Carrier carrier in Carriers)
            {
                int3 relativeOwner = carrier.Owner - origin;
                if (math.any(relativeOwner < first) || math.any(relativeOwner > last)) continue;
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
            // A local witness can depend on a cancelled cross-page edge.
            // Only an independent COMPLETE proof permits skipping the other
            // contributors; every remaining boundary must otherwise join OR.
            if ((proof.x & MerkabaSphereFlowerAuthority.SupportCoverageComplete) == 0u ||
                (proof.y & MerkabaSphereFlowerAuthority.SupportCoverageComplete) == 0u)
            {
                ReadNeighborCoverage(first, last);
                foreach (CoverageFootprint footprint in _neighborCoverage)
                {
                    if (math.any(footprint.RelativeOwner < first) || math.any(footprint.RelativeOwner > last)) continue;
                    footprint.CopyTo(positions);
                    for (int half = 0; half < 2; half++)
                        if ((proof[half] & MerkabaSphereFlowerAuthority.SupportCoverageComplete) == 0u)
                            proof[half] |= MerkabaSphereFlowerAuthority.SupportCarrierCoverageProof(positions,
                                footprint.Active, footprint.Paired[face >> 1], cell, face, half);
                }
            }
            uint covered = 0u, partial = 0u;
            for (int half = 0; half < 2; half++)
            {
                var status = MerkabaSphereFlowerAuthority.SupportCoverageResult(proof[half]);
                if (status == MerkabaSphereFlowerAuthority.ProofClassification.Certain) covered |= 1u << half;
                else if (status == MerkabaSphereFlowerAuthority.ProofClassification.Ambiguous ||
                    _neighborCoverageUnresolved) partial |= 1u << half;
            }
            // Neighbor support becomes required only for an actual DIRT
            // query. Do not turn failed support into absent direct coverage.
            RequireResolvedSupport();
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
