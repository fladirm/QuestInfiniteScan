using System;
using System.IO;
using System.Threading.Tasks;
using Genesis.RoomScan.UI;
using Meta.XR;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using UnityEngine.XR.ARFoundation;

namespace Genesis.RoomScan.Editor
{
    /// <summary>
    /// The one target bootstrap for the Quest Infinite Merkaba host. It creates a
    /// Quest/OpenXR scene, the preserved QRS sensor front end, the single Merkaba
    /// reconstruction authority, and the donor-style world-space controller menu.
    /// </summary>
    public partial class RoomScanSetupWizard : EditorWindow
    {
        internal const string ScenePath = "Assets/Scenes/QuestMerkabaScan.unity";
        internal const string BuildSuccessMarker =
            "[QuestMerkabaScan] APK build Succeeded:";
        private const string DefaultApkName = "QuestMerkabaScan-release.apk";
        private const string PanelPath = "Assets/Settings/MerkabaPanelSettings.asset";

        private string _status = "Ready";
        private bool _busy;

        [MenuItem("Quest Merkaba/Setup Target Host")]
        private static void Open() =>
            GetWindow<RoomScanSetupWizard>(false, "Quest Merkaba Setup");

        private void OnGUI()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("QUEST INFINITE MERKABA", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Creates the sole 5 cm support / 2.5 cm lattice scanner scene, " +
                "Quest depth/RGB frontend, persistence, GLB export, and six-action VR menu.",
                MessageType.Info);
            EditorGUILayout.LabelField("Scene", ScenePath);
            EditorGUILayout.LabelField("Status", _status);
            using (new EditorGUI.DisabledScope(_busy))
            {
                if (GUILayout.Button("Create / Refresh Canonical Host Scene", GUILayout.Height(30f)))
                    _ = PrepareInteractiveAsync();
            }
        }

        private async Task PrepareInteractiveAsync()
        {
            _busy = true;
            _status = "Preparing…";
            Repaint();
            try
            {
                await PrepareProjectAndSceneAsync();
                _status = "Ready: " + ScenePath;
            }
            catch (Exception exception)
            {
                _status = "Failed: " + exception.Message;
                Debug.LogException(exception);
            }
            finally
            {
                _busy = false;
                Repaint();
            }
        }

        /// <summary>Batch entry point used by Tools/unity/build_merkaba_apk.sh.</summary>
        public static async void PrepareQuestMerkabaScanProject()
        {
            RoomScanSetupWizard wizard = CreateInstance<RoomScanSetupWizard>();
            try
            {
                await wizard.PrepareProjectAndSceneAsync();
                Debug.Log("[QuestMerkabaScan] Prepare Succeeded: " + ScenePath);
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogError("[QuestMerkabaScan] Prepare Failed: " + exception);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
            finally
            {
                DestroyImmediate(wizard);
            }
        }

        private async Task PrepareProjectAndSceneAsync()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                throw new InvalidOperationException(
                    "Prepare with -buildTarget Android so Unity completes its target switch " +
                    "before the setup method runs.");

            ConfigurePlayerSettings();
            EnsureURPSetup();
            await VRProjectBootstrap.FixAllAsync(CheckSeverity.Recommended);
            RemoveStaleSimulationBuildCopies();
            RemoveDonorHostArtifacts();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
            await EnsureRequiredBuildingBlocksAsync();
            EnsureQuestRigAndDepthManager();
            EnsureCanonicalScanner();
            EnsureControllerMenu();
            EnsurePermissionManifest();

            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
                AssetDatabase.CreateFolder("Assets", "Scenes");
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new IOException("Unity could not save " + ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static void ConfigurePlayerSettings()
        {
            PlayerSettings.companyName = "Genesis";
            PlayerSettings.productName = "Quest Infinite Merkaba Scan";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android,
                "com.genesis.questmerkabascan");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android,
                ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,
                new[] { GraphicsDeviceType.Vulkan });
            PlayerSettings.stereoRenderingPath = StereoRenderingPath.Instancing;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;
        }

        private static void EnsureQuestRigAndDepthManager()
        {
            OVRManager manager = FindAny<OVRManager>();
            if (manager == null)
                manager = new GameObject("[OVRManager]").AddComponent<OVRManager>();
            manager.isInsightPassthroughEnabled = true;
            SetSerializedBool(manager,
                "requestPassthroughCameraAccessPermissionOnStartup", true);

            OVRCameraRig rig = FindAny<OVRCameraRig>();
            if (rig == null)
                rig = new GameObject("OVRCameraRig").AddComponent<OVRCameraRig>();

            if (FindAny<OVRPassthroughLayer>() == null)
                new GameObject("Passthrough Underlay").AddComponent<OVRPassthroughLayer>();
            if (FindAny<PassthroughCameraAccess>() == null)
                rig.gameObject.AddComponent<PassthroughCameraAccess>();

            Camera camera = rig.centerEyeAnchor != null
                ? rig.centerEyeAnchor.GetComponent<Camera>() : FindAny<Camera>();
            if (camera == null)
            {
                var cameraObject = new GameObject("CenterEyeAnchor");
                cameraObject.transform.SetParent(rig.transform, false);
                camera = cameraObject.AddComponent<Camera>();
            }
            camera.tag = "MainCamera";
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            if (camera.GetComponent<AROcclusionManager>() == null)
                camera.gameObject.AddComponent<AROcclusionManager>();
            if (FindAny<ARSession>() == null)
                new GameObject("[AR Session]").AddComponent<ARSession>();
        }

        private static void EnsureCanonicalScanner()
        {
            GameObject systems = FindByName("[Quest Infinite Merkaba]") ??
                new GameObject("[Quest Infinite Merkaba]");
            systems.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            systems.transform.localScale = Vector3.one;
            GetOrAdd<RoomAnchorManager>(systems);
            GetOrAdd<RoomScanInputHandler>(systems);

            GameObject roomSpace = FindByName("[Merkaba Room Space]") ??
                new GameObject("[Merkaba Room Space]");
            GetOrAdd<RoomSpaceRoot>(roomSpace);

            GameObject scannerObject = FindByName("MerkabaGrid") ?? new GameObject("MerkabaGrid");
            scannerObject.transform.SetParent(roomSpace.transform, false);
            scannerObject.transform.localPosition = Vector3.zero;
            scannerObject.transform.localRotation = Quaternion.identity;
            scannerObject.transform.localScale = Vector3.one;

            DepthCapture depth = GetOrAdd<DepthCapture>(scannerObject);
            GetOrAdd<PassthroughCameraProvider>(scannerObject);
            GetOrAdd<MerkabaGrid>(scannerObject);
            MerkabaIntegrator integrator = GetOrAdd<MerkabaIntegrator>(scannerObject);
            MerkabaGridRenderer renderer = GetOrAdd<MerkabaGridRenderer>(scannerObject);
            GetOrAdd<MerkabaPersistence>(scannerObject);
            GetOrAdd<MerkabaExporter>(scannerObject);
            GetOrAdd<RoomScanner>(scannerObject);

            AssignAsset(depth, "depthNormalCompute",
                "Packages/com.genesis.roomscan/Runtime/Shaders/DepthNormals.compute");
            AssignAsset(depth, "depthDilationCompute",
                "Packages/com.genesis.roomscan/Runtime/Shaders/DepthDilation.compute");
            AssignAsset(depth, "bilateralFilterCompute",
                "Packages/com.genesis.roomscan/Runtime/Shaders/BilateralDepthFilter.compute");
            AssignAsset(integrator, "compute",
                "Packages/com.genesis.roomscan/Runtime/Shaders/MerkabaIntegration.compute");
            AssignAsset(renderer, "topologyCompute",
                "Packages/com.genesis.roomscan/Runtime/Shaders/MerkabaTopology.compute");
            AssignAsset(renderer, "renderShader",
                "Packages/com.genesis.roomscan/Runtime/Shaders/MerkabaGrid.shader");
        }

        private static void EnsureControllerMenu()
        {
            EventSystem eventSystem = FindAny<EventSystem>();
            if (eventSystem == null)
                eventSystem = new GameObject("EventSystem").AddComponent<EventSystem>();
            StandaloneInputModule standalone = eventSystem.GetComponent<StandaloneInputModule>();
            if (standalone != null) DestroyImmediate(standalone);
            GetOrAdd<OVRInputModule>(eventSystem.gameObject);
            PanelInputConfiguration input = GetOrAdd<PanelInputConfiguration>(eventSystem.gameObject);
            SetSerializedBool(input, "m_DefaultEventCameraIsMainCamera", true);
            SetSerializedBool(input, "m_AutoCreatePanelComponents", true);
            GetOrAdd<VRDocumentRaycaster>(eventSystem.gameObject);
            ControllerRayDriver ray = GetOrAdd<ControllerRayDriver>(eventSystem.gameObject);
            AssignAsset(ray, "overlayShader",
                "Packages/com.genesis.roomscan/Runtime/UI/ControllerRay.shader");

            GameObject menu = FindByName("Merkaba Menu") ?? new GameObject("Merkaba Menu");
            UIDocument document = GetOrAdd<UIDocument>(menu);
            GetOrAdd<DebugMenuController>(menu);
            document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Packages/com.genesis.roomscan/Runtime/UI/DebugMenu.uxml");
            document.panelSettings = FindOrCreatePanelSettings();
            document.worldSpaceSizeMode = WorldSpaceSizeMode.Dynamic;
            document.pivot = Pivot.Center;
            document.pivotReferenceSize = PivotReferenceSize.Layout;
            menu.transform.localScale = Vector3.one * 0.08f;
            menu.transform.position = new Vector3(0f, 1.3f, 0.5f);
        }

        private static PanelSettings FindOrCreatePanelSettings()
        {
            PanelSettings panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
            if (panel == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Settings"))
                    AssetDatabase.CreateFolder("Assets", "Settings");
                panel = CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(panel, PanelPath);
            }
            var serialized = new SerializedObject(panel);
            SerializedProperty mode = serialized.FindProperty("m_RenderMode");
            if (mode != null) mode.intValue = 1; // WorldSpace in Unity 6.
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(panel);
            return panel;
        }

        private static void EnsurePermissionManifest()
        {
            string androidPlugins = Path.Combine(Application.dataPath, "Plugins", "Android");
            Directory.CreateDirectory(androidPlugins);
            string legacyDirectory = Path.Combine(androidPlugins,
                "QuestRoomScanManifest.androidlib");
            if (Directory.Exists(legacyDirectory)) Directory.Delete(legacyDirectory, true);
            string legacyMeta = legacyDirectory + ".meta";
            if (File.Exists(legacyMeta)) File.Delete(legacyMeta);
            string networkDirectory = Path.Combine(androidPlugins,
                "NetworkSecurityConfig.androidlib");
            if (Directory.Exists(networkDirectory)) Directory.Delete(networkDirectory, true);
            if (File.Exists(networkDirectory + ".meta"))
                File.Delete(networkDirectory + ".meta");
            string permissionDirectory = Path.Combine(androidPlugins,
                "QuestMerkabaScanManifest.androidlib");
            if (Directory.Exists(permissionDirectory))
                Directory.Delete(permissionDirectory, true);
            if (File.Exists(permissionDirectory + ".meta"))
                File.Delete(permissionDirectory + ".meta");

            string mainManifest = Path.Combine(androidPlugins, "AndroidManifest.xml");
            const string mainXml =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                "<manifest xmlns:android=\"http://schemas.android.com/apk/res/android\" " +
                "xmlns:tools=\"http://schemas.android.com/tools\" " +
                "xmlns:horizonos=\"http://schemas.horizonos/sdk\" " +
                "android:installLocation=\"auto\">\n" +
                "  <application android:label=\"@string/app_name\" " +
                "android:icon=\"@mipmap/app_icon\" android:allowBackup=\"false\">\n" +
                "    <activity android:name=\"com.unity3d.player.UnityPlayerGameActivity\" " +
                "android:theme=\"@style/Theme.AppCompat.DayNight.NoActionBar\" " +
                "android:launchMode=\"singleTask\" android:exported=\"true\" " +
                "android:excludeFromRecents=\"true\" " +
                "android:configChanges=\"locale|fontScale|keyboard|keyboardHidden|mcc|mnc|" +
                "navigation|orientation|screenLayout|screenSize|smallestScreenSize|" +
                "touchscreen|uiMode\">\n" +
                "      <intent-filter>\n" +
                "        <action android:name=\"android.intent.action.MAIN\" />\n" +
                "        <category android:name=\"android.intent.category.LAUNCHER\" />\n" +
                "        <category android:name=\"com.oculus.intent.category.VR\" />\n" +
                "      </intent-filter>\n" +
                "      <meta-data android:name=\"com.oculus.vr.focusaware\" " +
                "android:value=\"true\" />\n" +
                "    </activity>\n" +
                "    <meta-data android:name=\"unityplayer.SkipPermissionsDialog\" " +
                "android:value=\"false\" />\n" +
                "    <meta-data android:name=\"com.oculus.supportedDevices\" " +
                "android:value=\"quest2|questpro|quest3|quest3s\" " +
                "tools:replace=\"android:value\" />\n" +
                "  </application>\n" +
                "  <uses-permission android:name=\"com.oculus.permission.USE_SCENE\" />\n" +
                "  <uses-permission android:name=\"com.oculus.permission.USE_ANCHOR_API\" />\n" +
                "  <uses-permission android:name=\"com.oculus.permission.USE_SPATIAL_ANCHOR\" />\n" +
                "  <uses-permission android:name=\"horizonos.permission.HEADSET_CAMERA\" />\n" +
                "  <uses-feature android:name=\"android.hardware.vr.headtracking\" " +
                "android:required=\"true\" android:version=\"1\" />\n" +
                "  <uses-feature android:name=\"com.oculus.feature.PASSTHROUGH\" " +
                "android:required=\"true\" />\n" +
                "  <horizonos:uses-horizonos-sdk horizonos:minSdkVersion=\"60\" " +
                "horizonos:targetSdkVersion=\"205\" />\n" +
                "</manifest>\n";
            if (!File.Exists(mainManifest) || File.ReadAllText(mainManifest) != mainXml)
                File.WriteAllText(mainManifest, mainXml);
        }

        private static void RemoveStaleSimulationBuildCopies()
        {
            // AR Foundation temporarily moves these two assets here during a build.
            // A cancelled/failed inherited-host build can leave both source and
            // destination present, making every later preprocess noisy.
            AssetDatabase.DeleteAsset(
                "Assets/XR/Temp/XRSimulationPreferences.asset");
            AssetDatabase.DeleteAsset(
                "Assets/XR/Temp/XRSimulationRuntimeSettings.asset");
        }

        private static void RemoveDonorHostArtifacts()
        {
            AssetDatabase.DeleteAsset("Assets/Settings/DebugMenuPanelSettings.asset");
            AssetDatabase.DeleteAsset("Assets/Resources/PerformanceTestRunInfo.json");
            AssetDatabase.DeleteAsset("Assets/Resources/PerformanceTestRunSettings.json");

            SanitizeMetaDiagnostics();

            UnityEngine.Object runtime = AssetDatabase.LoadMainAssetAtPath(
                "Assets/Resources/OculusRuntimeSettings.asset");
            if (runtime == null) return;
            var serialized = new SerializedObject(runtime);
            SerializedProperty telemetry = serialized.FindProperty("telemetryProjectGuid");
            if (telemetry == null || string.IsNullOrEmpty(telemetry.stringValue)) return;
            telemetry.stringValue = string.Empty;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(runtime);
        }

        internal static void SanitizeMetaDiagnostics()
        {
            // Meta SDK recreates these Resources assets on editor startup. Keep its
            // required defaults present, but make the inherited diagnostic services
            // inert. A late build preprocessor calls this again after Meta's own
            // DevAgent hook tries to inject the workstation address and access token.
            UnityEngine.Object devAgent = AssetDatabase.LoadMainAssetAtPath(
                "Assets/Resources/DevAgentSettings.asset");
            if (devAgent != null)
            {
                var devAgentSettings = new SerializedObject(devAgent);
                SetBool(devAgentSettings, "enabled", false);
                SetString(devAgentSettings, "serverAddress", "127.0.0.1");
                SetString(devAgentSettings, "accessToken", "disabled");
                SetString(devAgentSettings, "witClientAccessToken", string.Empty);
                devAgentSettings.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(devAgent);
            }

            UnityEngine.Object immersive = AssetDatabase.LoadMainAssetAtPath(
                "Assets/Resources/ImmersiveDebuggerSettings.asset");
            if (immersive != null)
            {
                var immersiveSettings = new SerializedObject(immersive);
                SetBool(immersiveSettings, "immersiveDebuggerEnabled", false);
                SetBool(immersiveSettings, "immersiveDebuggerDisplayAtStartup", false);
                immersiveSettings.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(immersive);
            }
        }

        private static void SetBool(SerializedObject serialized, string name, bool value)
        {
            SerializedProperty property = serialized.FindProperty(name);
            if (property != null) property.boolValue = value;
        }

        private static void SetString(SerializedObject serialized, string name, string value)
        {
            SerializedProperty property = serialized.FindProperty(name);
            if (property != null) property.stringValue = value;
        }

        /// <summary>Batch entry point used after a successful prepare invocation.</summary>
        public static void BuildQuestMerkabaScanApk()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                throw new InvalidOperationException("APK build requires -buildTarget Android.");
            if (!File.Exists(ScenePath))
                throw new FileNotFoundException("Prepared Merkaba scene is missing", ScenePath);

            ConfigurePlayerSettings();
            string destination = Environment.GetEnvironmentVariable("QIS_MERKABA_APK_PATH");
            if (string.IsNullOrWhiteSpace(destination))
                destination = Path.GetFullPath(Path.Combine("Builds", DefaultApkName));
            Directory.CreateDirectory(Path.GetDirectoryName(destination));

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = destination,
                target = BuildTarget.Android,
                options = BuildOptions.None
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException(
                    $"Android build {report.summary.result}: {report.summary.totalErrors} errors");
            var file = new FileInfo(destination);
            if (!file.Exists || file.Length == 0)
                throw new BuildFailedException("Unity reported success but APK is missing/empty.");
            Debug.Log($"{BuildSuccessMarker} {destination} ({file.Length} bytes)");
        }

        private static T GetOrAdd<T>(GameObject owner) where T : Component =>
            owner.GetComponent<T>() ?? owner.AddComponent<T>();

        private static T FindAny<T>() where T : UnityEngine.Object =>
            UnityEngine.Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);

        private static GameObject FindByName(string name)
        {
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                Transform found = FindNamed(root.transform, name);
                if (found != null) return found.gameObject;
            }
            return null;
        }

        private static Transform FindNamed(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform result = FindNamed(root.GetChild(i), name);
                if (result != null) return result;
            }
            return null;
        }

        private static void AssignAsset(Component component, string field, string path)
        {
            UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if (asset == null) throw new FileNotFoundException("Required asset missing", path);
            var serialized = new SerializedObject(component);
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null)
                throw new MissingFieldException(component.GetType().Name, field);
            property.objectReferenceValue = asset;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(component);
        }

        private static void SetSerializedBool(UnityEngine.Object target, string field, bool value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null) return;
            property.boolValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }
    }
}
