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
            MerkabaTileAddress tile, float2 planeBounds,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            var result = new MerkabaFlowerPresentation(tile);
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
                var parent = reader.BeginFlowerDecode(owner, planeBounds);
                uint4 pending = parent.ReachedCarriers;
                while (MerkabaSphereFlowerAuthority.TakeReachedCarrier(ref pending, out int carrierId))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var status = reader.ClassifyPageCarrier(owner, carrierId, planeBounds,
                        out MerkabaFlowerSymbolRecord symbol, out uint unresolved, out _,
                        roots, positions, parent);
                    result.UnresolvedWedges += math.countbits(unresolved);
                    if (status != MerkabaSphereFlowerAuthority.ProofClassification.Certain) continue;
                    var carrier = new Carrier { Owner = owner, Symbol = symbol };
                    carrier.SkinSamples = reader.ReadSkinSignal(owner, symbol, out carrier.SkinHeader,
                        out carrier.RgbSplitBits);
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
