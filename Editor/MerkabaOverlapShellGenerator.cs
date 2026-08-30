using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Genesis.RoomScan.Editor
{
    /// <summary>Writes/checks the GPU helpers from the R0 CPU oracle.</summary>
    public static class MerkabaOverlapShellGenerator
    {
        public const string GeneratedAssetPath =
            "Packages/com.genesis.roomscan/Runtime/Shaders/" +
            "MerkabaOverlapShell.generated.hlsl";

        [MenuItem("Quest Infinite Scan/Merkaba/Regenerate Overlap Shell HLSL")]
        public static void Regenerate()
        {
            string path = Path.GetFullPath(GeneratedAssetPath);
            string expected = MerkabaOverlapShell.BuildGeneratedHlsl();
            if (!File.Exists(path) || File.ReadAllText(path) != expected)
            {
                File.WriteAllText(path, expected);
                AssetDatabase.ImportAsset(GeneratedAssetPath,
                    ImportAssetOptions.ForceSynchronousImport);
            }
            Debug.Log($"[MerkabaOverlapShell] Generated HLSL: {path}");
        }

        public static void GenerateForBatch() => Regenerate();

        public static void CheckForBatch()
        {
            string path = Path.GetFullPath(GeneratedAssetPath);
            if (!File.Exists(path))
                throw new FileNotFoundException(
                    "Generated overlap-shell HLSL is missing.", path);
            string expected = MerkabaOverlapShell.BuildGeneratedHlsl();
            string actual = File.ReadAllText(path);
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
                throw new InvalidDataException(
                    "MerkabaOverlapShell.generated.hlsl is stale.");
            Debug.Log($"[MerkabaOverlapShell] HLSL matches CPU oracle: {path}");
        }
    }
}
