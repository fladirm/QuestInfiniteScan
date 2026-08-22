using System;
using System.IO;
using System.Threading.Tasks;
using Genesis.RoomScan.SigmaPrism;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Genesis.RoomScan.Editor
{
    /// <summary>
    /// Non-interactive equivalents of the setup wizard's game-ready + debug presets.
    /// These entry points make the first Quest smoke APK reproducible instead of relying
    /// on clicks in a particular editor session.
    /// </summary>
    public partial class RoomScanSetupWizard
    {
        private const string SmokeScenePath = "Assets/Scenes/QuestInfiniteScan.unity";
        private static bool _automationRunning;

        public static void PrepareQuestInfiniteScanSmokeProject()
        {
            if (_automationRunning)
                return;
            _automationRunning = true;
            _ = PrepareSmokeProjectAsync();
        }

        private static async Task PrepareSmokeProjectAsync()
        {
            RoomScanSetupWizard wizard = null;
            try
            {
                if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                    throw new InvalidOperationException(
                        "Launch Unity with -buildTarget Android before preparing the smoke project.");

                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                wizard = CreateInstance<RoomScanSetupWizard>();
                wizard.Refresh();

                PlayerSettings.companyName = "QuestInfiniteScan";
                PlayerSettings.productName = "Quest Infinite Scan";
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android,
                    "com.questinfinitescan.smoke");
                PlayerSettings.bundleVersion = "0.1.0-dev";
                PlayerSettings.Android.bundleVersionCode = 1;
                PlayerSettings.colorSpace = ColorSpace.Linear;

                EnsureURPSetup();
                VRProjectBootstrap.Audit();
                // Meta labels several capabilities that RoomScan actually
                // requires (environment depth, passthrough and scene/anchor
                // support) as Recommended rather than Outstanding.
                await VRProjectBootstrap.FixAllAsync(CheckSeverity.Recommended);
                VRProjectBootstrap.RequireQuestScanningFeatures();

                await wizard.EnsureRequiredBuildingBlocksAsync();
                wizard.Refresh();
                if (wizard._arSession == null)
                    wizard.FixARSession();
                if (wizard._cameraRig != null && wizard._arOcclusion == null)
                    wizard.FixAROcclusion();

                wizard.AddGameReadyComponentsToRoot();
                wizard.EnsurePassthroughSceneConfig();
                wizard.Refresh();
                wizard.FixDebugModules();
                wizard.Refresh();
                wizard.FixShaderWiring();

                EnsureQuestVRManifest();
                // Σ-PRISM-16 is pure on-device. No historical LAN/server exception
                // belongs in the generated product manifest.
                PlayerSettings.insecureHttpOption = InsecureHttpOption.NotAllowed;
                PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
                PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,
                    new[] { GraphicsDeviceType.Vulkan });

                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                GameObject roomScan = GameObject.Find("RoomScan");
                if (roomScan == null || roomScan.GetComponent<RoomScanner>() == null ||
                    roomScan.GetComponent<SigmaRigBridge>() == null ||
                    roomScan.GetComponent<DepthCapture>() == null)
                    throw new InvalidOperationException(
                        "The Σ-PRISM-16 capture/lifecycle shell was not created.");
                Debug.Log("[QuestInfiniteScan] Σ-PRISM-16 CPQ4 product shell; " +
                          "no legacy reconstruction or persistence path is active.");

                Directory.CreateDirectory(Path.GetDirectoryName(SmokeScenePath));
                Scene scene = SceneManager.GetActiveScene();
                if (!EditorSceneManager.SaveScene(scene, SmokeScenePath))
                    throw new IOException("Unity could not save the Quest smoke scene.");
                EditorBuildSettings.scenes = new[]
                {
                    new EditorBuildSettingsScene(SmokeScenePath, true)
                };
                AssetDatabase.SaveAssets();
                Debug.Log("[QuestInfiniteScan] Smoke project prepared: " + SmokeScenePath);
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogError("[QuestInfiniteScan] Smoke project preparation failed: " +
                               exception);
                EditorApplication.Exit(1);
            }
            finally
            {
                if (wizard != null)
                    DestroyImmediate(wizard);
                _automationRunning = false;
            }
        }

        public static void BuildQuestInfiniteScanSmokeApk()
        {
            try
            {
                if (!File.Exists(SmokeScenePath))
                    throw new FileNotFoundException("Prepare the smoke scene first.", SmokeScenePath);
                string output = Environment.GetEnvironmentVariable("QIS_APK_PATH");
                if (string.IsNullOrWhiteSpace(output))
                    output = Path.GetFullPath("Builds/QuestInfiniteScan-dev.apk");
                string parent = Path.GetDirectoryName(output);
                if (!string.IsNullOrEmpty(parent))
                    Directory.CreateDirectory(parent);

                var options = new BuildPlayerOptions
                {
                    scenes = new[] { SmokeScenePath },
                    locationPathName = output,
                    target = BuildTarget.Android,
                    targetGroup = BuildTargetGroup.Android,
                    options = BuildOptions.Development | BuildOptions.AllowDebugging
                };
                BuildReport report = BuildPipeline.BuildPlayer(options);
                BuildSummary summary = report.summary;
                Debug.Log($"[QuestInfiniteScan] APK build {summary.result}: {output}, " +
                          $"size={summary.totalSize}, errors={summary.totalErrors}, " +
                          $"warnings={summary.totalWarnings}");
                // Unity can report BuildResult.Succeeded while a player contains a
                // ComputeShader variant that failed Vulkan compilation. Such an APK
                // launches but silently lacks a reconstruction kernel, so it is not a
                // deployable success.
                bool clean = summary.result == BuildResult.Succeeded &&
                             summary.totalErrors == 0;
                if (!clean)
                    Debug.LogError("[QuestInfiniteScan] Refusing a player with " +
                                   $"result={summary.result}, errors={summary.totalErrors}.");
                EditorApplication.Exit(clean ? 0 : 1);
            }
            catch (Exception exception)
            {
                Debug.LogError("[QuestInfiniteScan] APK build failed: " + exception);
                EditorApplication.Exit(1);
            }
        }
    }
}
