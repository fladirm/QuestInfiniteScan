using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Genesis.RoomScan.Editor
{
    /// <summary>
    /// Runs after Meta SDK's DevAgent preprocessor, which otherwise injects a LAN
    /// address and authentication token even when its runtime feature is disabled.
    /// </summary>
    internal sealed class MerkabaBuildSanitizer : IPreprocessBuildWithReport
    {
        public int callbackOrder => 10_000;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (PlayerSettings.GetApplicationIdentifier(
                    NamedBuildTarget.Android) != "com.genesis.questmerkabascan")
                return;
            RoomScanSetupWizard.SanitizeMetaDiagnostics();
            AssetDatabase.SaveAssets();
        }
    }
}
