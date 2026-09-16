using System.IO;
using UnityEditor;
using UnityEngine;

namespace Genesis.RoomScan.Editor
{
    internal static class MerkabaSkinGenerator
    {
        private const string OutputPath =
            "Runtime/Shaders/MerkabaSkin.generated.hlsl";

        [MenuItem("Tools/Merkaba/Regenerate Union Skin LUT")]
        internal static void Generate()
        {
            string generated = MerkabaSkin.BuildGeneratedHlsl()
                .Replace("\r\n", "\n");
            string current = File.Exists(OutputPath)
                ? File.ReadAllText(OutputPath).Replace("\r\n", "\n")
                : string.Empty;
            if (current == generated) return;
            File.WriteAllText(OutputPath, generated);
            AssetDatabase.ImportAsset(OutputPath,
                ImportAssetOptions.ForceUpdate);
            Debug.Log("Regenerated exact M8 union-skin LUT: " + OutputPath);
        }
    }
}
