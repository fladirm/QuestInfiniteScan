using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Genesis.RoomScan.Editor
{
    /// <summary>Writes the GPU skin LUT from the analytic CPU authority.</summary>
    public static class MerkabaSkinGenerator
    {
        public const string GeneratedAssetPath =
            "Packages/com.genesis.roomscan/Runtime/Shaders/" +
            "MerkabaSkin.generated.hlsl";

        [MenuItem("Quest Infinite Scan/Merkaba/Regenerate Union Skin LUT")]
        public static void Regenerate()
        {
            string path = Path.GetFullPath(GeneratedAssetPath);
            string expected = MerkabaSkin.BuildGeneratedHlsl();
            if (!File.Exists(path) || File.ReadAllText(path) != expected)
            {
                File.WriteAllText(path, expected);
                AssetDatabase.ImportAsset(GeneratedAssetPath,
                    ImportAssetOptions.ForceSynchronousImport);
            }
            Debug.Log($"[MerkabaSkin] Generated HLSL: {path} " +
                $"vertices={MerkabaSkin.VertexCount} " +
                $"facelets={MerkabaSkin.FaceletCount}");
        }

        public static void GenerateForBatch() => Regenerate();

        public static void CheckForBatch()
        {
            string path = Path.GetFullPath(GeneratedAssetPath);
            if (!File.Exists(path))
                throw new FileNotFoundException(
                    "Generated skin HLSL is missing.", path);
            string expected = MerkabaSkin.BuildGeneratedHlsl();
            if (!string.Equals(expected, File.ReadAllText(path),
                    StringComparison.Ordinal))
                throw new InvalidDataException(
                    "MerkabaSkin.generated.hlsl is stale.");
            Debug.Log($"[MerkabaSkin] HLSL matches the CPU authority: {path}");
        }
    }
}
