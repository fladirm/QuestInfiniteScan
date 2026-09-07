using System;
using System.Collections.Generic;
using System.IO;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace Genesis.RoomScan.Editor
{
    /// <summary>Writes a deterministic DIRT presentation-writer fixture.
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

            // Only symbolic FREE-side cell/face identities are supplied.
            // The production Flower evaluator generates every position;
            // there is no fixture-only surface solver or float weld.
            var triangles = new List<MerkabaDirtTriangle>();
            foreach (int3 cell in new[] { new int3(-1, 0, 0), new int3(32, -1, 0) })
                for (int face = 0; face < 6; face++)
                    for (int half = 0; half < 2; half++)
                        triangles.Add(new MerkabaDirtTriangle(cell, face, half));
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write,
                FileShare.None);
            MerkabaGlbResult result = MerkabaGlbWriter.WriteDirt(stream, triangles, float3.zero);
            stream.Flush(true);
            if (result.VertexCount == 0 || new FileInfo(path).Length == 0)
                throw new InvalidDataException("Production Merkaba writer emitted no geometry.");
            Debug.Log($"[QuestMerkabaScan] GLB Fixture Succeeded: {path} " +
                $"({result.VertexCount} vertices, {result.ByteLength} bytes)");
        }
    }
}
