using System.Collections.Generic;
using Unity.Mathematics;

namespace Genesis.RoomScan.Tests
{
    // Disposable writer input, deliberately not a scan/admission oracle. The
    // actual CPU/HLSL geometry proofs live in the SphereFlower parity fixtures.
    internal static class MerkabaFlowerWriterFixture
    {
        internal static readonly float3 Captured = new float3(25f, 100f, 220f) / 255f;

        internal static MerkabaFlowerPresentation Create(params int3[] owners)
        {
            if (owners.Length == 0) owners = new[] { new int3(0) };
            MerkabaSpatial.Address address = MerkabaSpatial.Encode(owners[0]);
            var result = new MerkabaFlowerPresentation(new MerkabaTileAddress(address.BlockCoord, address.LocalAddress));
            foreach (int3 owner in owners)
            {
                var carrier = new MerkabaFlowerPresentation.Carrier
                {
                    Owner = owner,
                    Symbol = MerkabaFlowerSymbolRecord.CreateCarrier(0, 0, 1, false, false, 63u, 0u, 0, 0u, 0u),
                    SkinHeader = new MerkabaFlowerSkinDrawHeader(),
                    SkinSamples = new[] { new MerkabaFlowerSkinDrawSample { CapturedRgb = Captured } }
                };
                for (int site = 0; site < 7; site++)
                {
                    float2 chart = MerkabaSphereFlowerAuthority.L2CarrierChartSite(site);
                    uint index = (uint)result.Positions.Count;
                    carrier.PositionIndices[site] = index;
                    result.Positions.Add((float3)owner * MerkabaConstants.LatticeStep +
                        new float3(0f, chart.x, chart.y) * 0.0125f);
                    result.Knots.Add(new MerkabaFlowerPresentation.KnotAddress(new MerkabaFlowerSymbolKey
                        { JunctionX = 2 * (int)index + 1, Tag = 2u }));
                }
                result.Carriers.Add(carrier);
                result.TriangleCount += 6;
                result.OccupiedOwners++;
            }
            return result;
        }

        internal static MerkabaTilesetResult StreamPackage(string directory,
            MerkabaFlowerPresentation presentation, MerkabaSpatialBinding? binding = null,
            long hardLeafBytes = 8000)
        {
            MerkabaTilesetWriter.BeginStreamingPackage(directory);
            var leaves = new List<MerkabaTilesetLeaf>();
            foreach (var carrier in presentation.Carriers)
            {
                MerkabaFlowerPresentation leaf = Create(carrier.Owner);
                leaves.Add(MerkabaTilesetWriter.WriteStreamingLeaf(directory,
                    leaves.Count, leaf, hardLeafBytes: hardLeafBytes));
            }
            return MerkabaTilesetWriter.CompleteStreamingPackage(directory, leaves, binding);
        }
    }
}
