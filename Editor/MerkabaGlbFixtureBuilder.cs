using System;
using System.Collections.Generic;
using System.IO;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace Genesis.RoomScan.Editor
{
    /// <summary>Writes a deterministic production-writer fixture for external validation.</summary>
    internal static class MerkabaGlbFixtureBuilder
    {
        public static void BuildMerkabaGlbFixture()
        {
            string path = Environment.GetEnvironmentVariable(
                "QIS_MERKABA_GLB_FIXTURE_PATH");
            if (string.IsNullOrWhiteSpace(path))
                path = Path.GetFullPath(Path.Combine("Builds", "merkaba-fixture.glb"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            var kernels = new List<MerkabaKernelSnapshot>
            {
                new(new int3(-1, 0, 0), Occupied(new Color32(40, 150, 245, 255))),
                new(new int3(0, 0, 0), Occupied(new Color32(40, 150, 245, 255))),
                new(new int3(0, 1, 0), Occupied(new Color32(245, 150, 40, 255))),
                new(new int3(31, -1, 0), Occupied(new Color32(90, 220, 120, 255))),
                new(new int3(32, -1, 0), Occupied(new Color32(90, 220, 120, 255)))
            };
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write,
                FileShare.None);
            MerkabaGlbResult result = MerkabaGlbWriter.Write(stream, kernels);
            stream.Flush(true);
            if (result.VertexCount == 0 || new FileInfo(path).Length == 0)
                throw new InvalidDataException("Production Merkaba writer emitted no geometry.");
            Debug.Log($"[QuestMerkabaScan] GLB Fixture Succeeded: {path} " +
                $"({result.VertexCount} vertices, {result.ByteLength} bytes)");
        }

        private static KernelState Occupied(Color32 color)
        {
            KernelState state = default;
            MerkabaIntegrator.IntegrateClassified(ref state,
                MerkabaObservationKind.Surface, 1f, color);
            return state;
        }
    }
}
