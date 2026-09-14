// FinalScan host project setup: player settings, URP, XR bootstrap, scene, permission manifest, APK build.
// Batch entry points (Tools/unity/build_apk.sh):
//   FinalScan.Editor.FinalScanHostSetup.PrepareHostProject
//   FinalScan.Editor.FinalScanHostSetup.BuildApk
// Port (renamed, cleaned, minimized) of the donor setup wizard; MIGRATION_LEDGER §1, §2.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Meta.XR;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;

namespace FinalScan.Editor
{
    public sealed class FinalScanHostSetup : EditorWindow
    {
        const string Tag = "[FinalScan]";
        public const string ScenePath = "Assets/Scenes/FinalScan.unity";
        public const string PrepareSuccessMarker = "[FinalScan] Prepare Succeeded:";
        public const string BuildSuccessMarker = "[FinalScan] APK build Succeeded:";
        const string CompanyName = "monle";
        const string ProductName = "FinalScan";
        const string ApplicationId = "eu.monle.finalscan";
        const string RootObjectName = "[FinalScan]";
        const string ProbeHostTypeName = "FinalScan.Platform.Probe.DeviceProbeHost";
        const string ApkPathEnv = "FS_APK_PATH";
        const string DefaultApkName = "FinalScan-release.apk";

        string _status = "Ready";
        bool _busy;

        [MenuItem("FinalScan/Host Setup")]
        static void Open() => GetWindow<FinalScanHostSetup>(false, "FinalScan Host Setup");

        void OnGUI()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("FINALSCAN HOST", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Configures the Quest 3 / 3S OpenXR host: player settings, URP, XR features, " +
                                    "permission manifest and the FinalScan scene.", MessageType.Info);
            EditorGUILayout.LabelField("Scene", ScenePath);
            EditorGUILayout.LabelField("Status", _status);
            using (new EditorGUI.DisabledScope(_busy))
            {
                if (GUILayout.Button("Prepare Host Project", GUILayout.Height(30f))) _ = PrepareInteractiveAsync();
                if (GUILayout.Button("Build APK", GUILayout.Height(24f)))
                {
                    try { BuildApkCore(); _status = "Built"; }
                    catch (Exception exception) { _status = "Build failed: " + exception.Message; Debug.LogException(exception); }
                }
            }
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("XR checks", EditorStyles.boldLabel);
            foreach (XrCheck check in FinalScanXrBootstrap.Checks)
            {
                bool ok;
                string current;
                try { ok = check.IsOk(); current = check.Current(); }
                catch (Exception exception) { ok = false; current = exception.GetType().Name; }
                EditorGUILayout.LabelField((ok ? "OK   " : "FIX  ") + check.Id, current);
            }
        }

        async Task PrepareInteractiveAsync()
        {
            _busy = true;
            _status = "Preparing...";
            Repaint();
            try
            {
                await PrepareAsync();
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

        // -- Batch entry points ---------------------------------------------

        public static async void PrepareHostProject()
        {
            try
            {
                await PrepareAsync();
                Debug.Log($"{PrepareSuccessMarker} {ScenePath}");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogError($"{Tag} Prepare Failed: {exception}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        public static void BuildApk()
        {
            try
            {
                BuildApkCore();
            }
            catch (Exception exception)
            {
                Debug.LogError($"{Tag} APK build Failed: {exception}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw;
            }
        }

        // -- Prepare ------------------------------------------------------------

        static async Task PrepareAsync()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                throw new InvalidOperationException("Prepare requires the Android build target (-buildTarget Android).");

            ConfigurePlayerSettings();
            FinalScanUrpSetup.Ensure();
            await FinalScanXrBootstrap.FixAllAsync();
            ConfigureSystemKeyboard();
            RemoveStaleSimulationBuildCopies();
            SanitizeMetaDiagnostics();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            await FinalScanBuildingBlocks.EnsureAsync();
            EnsureRig();
            EnsureFinalScanRoot();
            EnsurePermissionManifest();

            if (!AssetDatabase.IsValidFolder("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new IOException("Unity could not save " + ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        static void ConfigurePlayerSettings()
        {
            PlayerSettings.companyName = CompanyName;
            PlayerSettings.productName = ProductName;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, ApplicationId);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            // Meta XR SDK 205 requires GameActivity on Unity 2023.2+ (Editor/Rules/AndroidCompatibility.cs).
            PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.GameActivity;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            PlayerSettings.stereoRenderingPath = StereoRenderingPath.Instancing;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.Android.textureCompressionFormats = new[] { TextureCompressionFormat.ASTC };
            EditorUserBuildSettings.buildAppBundle = false;
            Debug.Log($"{Tag} Player settings: {CompanyName}/{ProductName} {ApplicationId} IL2CPP ARM64 minSdk32 Vulkan SPI ASTC GameActivity");
        }

        static void ConfigureSystemKeyboard()
        {
            OVRProjectConfig config = OVRProjectConfig.CachedProjectConfig;
            if (config == null) throw new InvalidOperationException("Meta XR project config is unavailable.");
            config.requiresSystemKeyboard = true;
            OVRProjectConfig.CommitProjectConfig(config);
        }

        static void EnsureRig()
        {
            OVRManager manager = FindAny<OVRManager>();
            if (manager == null) manager = new GameObject("[OVRManager]").AddComponent<OVRManager>();
            manager.isInsightPassthroughEnabled = true;
            // Internal to com.meta.xr.sdk.core; written through the serialization layer.
            SetSerializedBool(manager, "requestPassthroughCameraAccessPermissionOnStartup", true);

            OVRCameraRig rig = FindAny<OVRCameraRig>();
            if (rig == null) rig = new GameObject("OVRCameraRig").AddComponent<OVRCameraRig>();

            if (FindAny<OVRPassthroughLayer>() == null)
                new GameObject("Passthrough Underlay").AddComponent<OVRPassthroughLayer>();

            EnsurePassthroughCamera(PassthroughCameraAccess.CameraPositionType.Left, rig.transform);
            EnsurePassthroughCamera(PassthroughCameraAccess.CameraPositionType.Right, rig.transform);

            Camera camera = rig.centerEyeAnchor != null ? rig.centerEyeAnchor.GetComponent<Camera>() : FindAny<Camera>();
            if (camera == null)
            {
                var cameraObject = new GameObject("CenterEyeAnchor");
                cameraObject.transform.SetParent(rig.transform, false);
                camera = cameraObject.AddComponent<Camera>();
            }
            camera.tag = "MainCamera";
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            if (camera.GetComponent<AROcclusionManager>() == null) camera.gameObject.AddComponent<AROcclusionManager>();
            if (FindAny<ARSession>() == null) new GameObject("[AR Session]").AddComponent<ARSession>();
            Debug.Log($"{Tag} Rig: OVRManager(passthrough, PCA permission on startup), OVRCameraRig, Passthrough Underlay, PCA L+R, AROcclusionManager, ARSession");
        }

        // Two PassthroughCameraAccess instances (Left + Right) are required to stream both cameras
        // (Meta.XR.PassthroughCameraAccess.CameraPosition, MRUK 205).
        static void EnsurePassthroughCamera(PassthroughCameraAccess.CameraPositionType position, Transform parent)
        {
            PassthroughCameraAccess[] all = UnityEngine.Object.FindObjectsByType<PassthroughCameraAccess>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (all.Any(access => access.CameraPosition == position)) return;

            // A Building-Block-installed instance defaults to Left; retarget a duplicate instead of adding a third.
            PassthroughCameraAccess duplicate = all.GroupBy(access => access.CameraPosition)
                .Where(group => group.Count() > 1).SelectMany(group => group.Skip(1)).FirstOrDefault();
            PassthroughCameraAccess access = duplicate;
            if (access == null)
            {
                var host = new GameObject("PassthroughCamera " + position);
                host.transform.SetParent(parent, false);
                access = host.AddComponent<PassthroughCameraAccess>();
            }
            access.CameraPosition = position;
            EditorUtility.SetDirty(access);
        }

        static void EnsureFinalScanRoot()
        {
            GameObject root = FindByName(RootObjectName) ?? new GameObject(RootObjectName);
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.transform.localScale = Vector3.one;
            if (root.GetComponent<FinalScanBootstrap>() == null) root.AddComponent<FinalScanBootstrap>();

            Type probeHost = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(ProbeHostTypeName, false))
                .FirstOrDefault(type => type != null);
            if (probeHost == null)
            {
                Debug.Log($"{Tag} {ProbeHostTypeName} not present; root has FinalScanBootstrap only.");
                return;
            }
            if (!typeof(Component).IsAssignableFrom(probeHost))
            {
                Debug.LogWarning($"{Tag} {ProbeHostTypeName} exists but is not a Component; skipped.");
                return;
            }
            if (root.GetComponent(probeHost) == null) root.AddComponent(probeHost);
            Debug.Log($"{Tag} Attached {ProbeHostTypeName} to {RootObjectName}.");
        }

        static void EnsurePermissionManifest()
        {
            string androidPlugins = Path.Combine(Application.dataPath, "Plugins", "Android");
            Directory.CreateDirectory(androidPlugins);
            string manifestPath = Path.Combine(androidPlugins, "AndroidManifest.xml");
            const string xml =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                "<manifest xmlns:android=\"http://schemas.android.com/apk/res/android\" " +
                "xmlns:tools=\"http://schemas.android.com/tools\" " +
                "xmlns:horizonos=\"http://schemas.horizonos/sdk\" " +
                "android:installLocation=\"auto\">\n" +
                "  <application android:label=\"@string/app_name\" android:icon=\"@mipmap/app_icon\" " +
                "android:allowBackup=\"false\">\n" +
                "    <activity android:name=\"com.unity3d.player.UnityPlayerGameActivity\" " +
                "android:theme=\"@style/Theme.AppCompat.DayNight.NoActionBar\" " +
                "android:launchMode=\"singleTask\" android:exported=\"true\" android:excludeFromRecents=\"true\" " +
                "android:configChanges=\"locale|fontScale|keyboard|keyboardHidden|mcc|mnc|navigation|orientation|" +
                "screenLayout|screenSize|smallestScreenSize|touchscreen|uiMode\">\n" +
                "      <intent-filter>\n" +
                "        <action android:name=\"android.intent.action.MAIN\" />\n" +
                "        <category android:name=\"android.intent.category.LAUNCHER\" />\n" +
                "        <category android:name=\"com.oculus.intent.category.VR\" />\n" +
                "      </intent-filter>\n" +
                "      <meta-data android:name=\"com.oculus.vr.focusaware\" android:value=\"true\" />\n" +
                "    </activity>\n" +
                "    <meta-data android:name=\"unityplayer.SkipPermissionsDialog\" android:value=\"false\" />\n" +
                "    <meta-data android:name=\"com.oculus.supportedDevices\" android:value=\"quest3|quest3s\" " +
                "tools:replace=\"android:value\" />\n" +
                "  </application>\n" +
                "  <uses-permission android:name=\"com.oculus.permission.USE_SCENE\" />\n" +
                "  <uses-permission android:name=\"com.oculus.permission.USE_ANCHOR_API\" />\n" +
                "  <uses-permission android:name=\"com.oculus.permission.USE_SPATIAL_ANCHOR\" />\n" +
                "  <uses-permission android:name=\"horizonos.permission.HEADSET_CAMERA\" />\n" +
                "  <uses-permission android:name=\"android.permission.CAMERA\" />\n" +
                "  <uses-feature android:name=\"android.hardware.vr.headtracking\" android:required=\"true\" " +
                "android:version=\"1\" />\n" +
                "  <uses-feature android:name=\"com.oculus.feature.PASSTHROUGH\" android:required=\"true\" />\n" +
                "  <horizonos:uses-horizonos-sdk horizonos:minSdkVersion=\"60\" horizonos:targetSdkVersion=\"205\" />\n" +
                "</manifest>\n";
            if (!File.Exists(manifestPath) || File.ReadAllText(manifestPath) != xml)
            {
                File.WriteAllText(manifestPath, xml);
                Debug.Log($"{Tag} Wrote {manifestPath}");
            }
        }

        // AR Foundation moves these assets during a build; an aborted build leaves both copies present.
        static void RemoveStaleSimulationBuildCopies()
        {
            AssetDatabase.DeleteAsset("Assets/XR/Temp/XRSimulationPreferences.asset");
            AssetDatabase.DeleteAsset("Assets/XR/Temp/XRSimulationRuntimeSettings.asset");
        }

        // Meta SDK recreates these Resources assets on editor start; keep the diagnostic services inert
        // so no dev-agent address or immersive debugger ships in the APK.
        internal static void SanitizeMetaDiagnostics()
        {
            UnityEngine.Object devAgent = AssetDatabase.LoadMainAssetAtPath("Assets/Resources/DevAgentSettings.asset");
            if (devAgent != null)
            {
                var serialized = new SerializedObject(devAgent);
                SetBool(serialized, "enabled", false);
                SetString(serialized, "serverAddress", "127.0.0.1");
                SetString(serialized, "accessToken", "disabled");
                SetString(serialized, "witClientAccessToken", string.Empty);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(devAgent);
            }
            UnityEngine.Object immersive = AssetDatabase.LoadMainAssetAtPath("Assets/Resources/ImmersiveDebuggerSettings.asset");
            if (immersive != null)
            {
                var serialized = new SerializedObject(immersive);
                SetBool(serialized, "immersiveDebuggerEnabled", false);
                SetBool(serialized, "immersiveDebuggerDisplayAtStartup", false);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(immersive);
            }
            UnityEngine.Object runtime = AssetDatabase.LoadMainAssetAtPath("Assets/Resources/OculusRuntimeSettings.asset");
            if (runtime != null)
            {
                var serialized = new SerializedObject(runtime);
                SetString(serialized, "telemetryProjectGuid", string.Empty);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(runtime);
            }
        }

        // -- Build ----------------------------------------------------------------

        static void BuildApkCore()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                throw new InvalidOperationException("APK build requires -buildTarget Android.");
            if (!File.Exists(ScenePath))
                throw new FileNotFoundException("Prepared FinalScan scene is missing; run PrepareHostProject first", ScenePath);

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            FinalScanBootstrap bootstrap = FindAny<FinalScanBootstrap>();
            if (bootstrap == null || bootstrap.gameObject.scene != scene)
                throw new BuildFailedException("Prepared scene has no FinalScanBootstrap.");

            ConfigurePlayerSettings();
            SanitizeMetaDiagnostics();
            string destination = Environment.GetEnvironmentVariable(ApkPathEnv);
            if (string.IsNullOrWhiteSpace(destination))
                destination = Path.GetFullPath(Path.Combine("Builds", DefaultApkName));
            Directory.CreateDirectory(Path.GetDirectoryName(destination));

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = destination,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException($"Android build {report.summary.result}: {report.summary.totalErrors} errors");
            var file = new FileInfo(destination);
            if (!file.Exists || file.Length == 0)
                throw new BuildFailedException("Unity reported success but the APK is missing or empty.");
            Debug.Log($"{BuildSuccessMarker} {destination} ({file.Length} bytes)");
        }

        // -- Helpers ------------------------------------------------------------

        static T FindAny<T>() where T : UnityEngine.Object =>
            UnityEngine.Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);

        static GameObject FindByName(string name)
        {
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                Transform found = FindNamed(root.transform, name);
                if (found != null) return found.gameObject;
            }
            return null;
        }

        static Transform FindNamed(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform result = FindNamed(root.GetChild(i), name);
                if (result != null) return result;
            }
            return null;
        }

        static void SetSerializedBool(UnityEngine.Object target, string field, bool value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null)
            {
                Debug.LogWarning($"{Tag} {target.GetType().Name}.{field} not found; SDK field renamed?");
                return;
            }
            property.boolValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        static void SetBool(SerializedObject serialized, string name, bool value)
        {
            SerializedProperty property = serialized.FindProperty(name);
            if (property != null) property.boolValue = value;
        }

        static void SetString(SerializedObject serialized, string name, string value)
        {
            SerializedProperty property = serialized.FindProperty(name);
            if (property != null) property.stringValue = value;
        }
    }
}
