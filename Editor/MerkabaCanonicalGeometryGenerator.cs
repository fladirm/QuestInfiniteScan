using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Genesis.RoomScan.Editor
{
    /// <summary>Writes/checks the GPU include from the one CPU canonical authority.</summary>
    public static class MerkabaCanonicalGeometryGenerator
    {
        public const string GeneratedAssetPath =
            "Packages/com.genesis.roomscan/Runtime/Shaders/MerkabaCanonicalGeometry.generated.hlsl";

        [MenuItem("Quest Infinite Scan/Merkaba/Regenerate Canonical HLSL")]
        public static void Regenerate()
        {
            string path = Path.GetFullPath(GeneratedAssetPath);
            string expected = MerkabaCanonicalGeometry.BuildGeneratedHlsl();
            if (!File.Exists(path) || File.ReadAllText(path) != expected)
            {
                File.WriteAllText(path, expected);
                AssetDatabase.ImportAsset(GeneratedAssetPath,
                    ImportAssetOptions.ForceSynchronousImport);
            }
            Debug.Log($"[MerkabaGeometry] Generated canonical HLSL: {path}");
        }

        public static void GenerateForBatch() => Regenerate();

        public static void CheckForBatch()
        {
            string path = Path.GetFullPath(GeneratedAssetPath);
            if (!File.Exists(path))
                throw new FileNotFoundException("Generated canonical HLSL is missing.", path);
            string expected = MerkabaCanonicalGeometry.BuildGeneratedHlsl();
            string actual = File.ReadAllText(path);
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
                throw new InvalidDataException(
                    "MerkabaCanonicalGeometry.generated.hlsl is stale. Regenerate it from the CPU authority.");
            Debug.Log($"[MerkabaGeometry] Canonical HLSL matches CPU authority: {path}");
        }
    }
}
