using System;
using System.IO;
using Unity.Mathematics;
using UnityEngine;

namespace Genesis.RoomScan.Editor
{
    /// <summary>Writes a deterministic direct-carrier presentation-writer fixture.
    /// This checks the file consumer, not scan admission or surface closure.</summary>
    internal static class MerkabaGlbFixtureBuilder
    {
        public static void BuildMerkabaGlbFixture()
        {
            string path = Environment.GetEnvironmentVariable(
                "QIS_MERKABA_GLB_FIXTURE_PATH");
            if (string.IsNullOrWhiteSpace(path))
                path = Path.GetFullPath(Path.Combine("Builds", "merkaba-fixture.glb"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            // Serializer fixture only: the generated planar seven-site chart,
            // not a substitute for the measured-surface admission fixtures.
            var presentation = new MerkabaFlowerPresentation(default);
            var carrier = new MerkabaFlowerPresentation.Carrier
            {
                Symbol = MerkabaFlowerSymbolRecord.CreateCarrier(
                    0, 0, 1, false, false, 63u, 0u, 0, 0u, 0u),
                SkinSamples = new[] { new MerkabaFlowerSkinDrawSample
                    { CapturedRgb = new float3(25f, 100f, 220f) / 255f } }
            };
            for (int site = 0; site < 7; site++)
            {
                float2 chart = MerkabaSphereFlowerAuthority.L2CarrierChartSite(site);
                carrier.PositionIndices[site] = (uint)site;
                presentation.Positions.Add(new float3(0f, chart.x, chart.y) *
                    (MerkabaConstants.LatticeStep * 0.5f));
            }
            presentation.Carriers.Add(carrier);
            presentation.TriangleCount = 6;
            presentation.OccupiedOwners = 1;
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write,
                FileShare.None);
            MerkabaGlbResult result = MerkabaGlbWriter.Write(stream, presentation, float3.zero);
            stream.Flush(true);
            if (result.VertexCount == 0 || new FileInfo(path).Length == 0)
                throw new InvalidDataException("Production Merkaba writer emitted no geometry.");
            Debug.Log($"[QuestMerkabaScan] GLB Fixture Succeeded: {path} " +
                $"({result.VertexCount} vertices, {result.ByteLength} bytes)");
        }
    }
}
