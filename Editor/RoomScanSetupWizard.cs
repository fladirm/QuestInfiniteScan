using Genesis.RoomScan.SigmaPrism;
using Genesis.RoomScan.UI;
using Meta.XR;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UIElements;
using UnityEngine.XR.ARFoundation;

namespace Genesis.RoomScan.Editor
{
    /// <summary>
    /// Minimal idempotent Quest shell setup. It deliberately knows nothing about a
    /// reconstruction representation beyond adding <see cref="SigmaRigBridge"/>.
    /// </summary>
    public partial class RoomScanSetupWizard : EditorWindow
    {
        internal ARSession _arSession;
        internal OVRCameraRig _cameraRig;
        internal AROcclusionManager _arOcclusion;
        private GameObject _roomScanRoot;

        [MenuItem("RoomScan/Setup Sigma-PRISM-16 Scene")]
        public static void Open()
        {
            GetWindow<RoomScanSetupWizard>("Σ-PRISM Setup").Show();
        }

        private void OnEnable() => Refresh();

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Σ-PRISM-16 Quest Shell", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Creates only the Meta XR/AR capture, lifecycle, input and operator UI " +
                "shell. The scanner implementation lives exclusively in Runtime/SigmaPrism.",
                MessageType.Info);

            if (GUILayout.Button("Refresh")) Refresh();
            if (GUILayout.Button("Create / Repair Quest Shell"))
            {
                FixARSession();
                if (_cameraRig != null) FixAROcclusion();
                AddGameReadyComponentsToRoot();
                EnsurePassthroughSceneConfig();
                FixDebugModules();
                EnsureVRInput();
                MarkDirty();
                Refresh();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("AR Session", _arSession != null ? "ready" : "missing");
            EditorGUILayout.LabelField("OVR Camera Rig", _cameraRig != null ? "ready" : "missing");
            EditorGUILayout.LabelField("Stereo Depth", _arOcclusion != null ? "ready" : "missing");
            EditorGUILayout.LabelField("Sigma shell", _roomScanRoot != null ? "ready" : "missing");
        }

        internal void Refresh()
        {
            _arSession = FindAny<ARSession>();
            _cameraRig = FindAny<OVRCameraRig>();
            _arOcclusion = FindAny<AROcclusionManager>();
            RoomScanner scanner = FindAny<RoomScanner>();
            _roomScanRoot = scanner != null ? scanner.gameObject : GameObject.Find("RoomScan");
            RefreshBuildingBlocksState();
        }

        internal void FixARSession()
        {
            GameObject host = GameObject.Find("AR Session") ?? new GameObject("AR Session");
            if (host.GetComponent<ARSession>() == null)
                Undo.AddComponent<ARSession>(host);
            MarkDirty();
            Refresh();
        }

        internal void FixAROcclusion()
        {
            if (_cameraRig == null)
                return;
            Camera camera = _cameraRig.GetComponentInChildren<Camera>();
            if (camera == null)
                throw new System.InvalidOperationException(
                    "OVRCameraRig has no camera for AR depth acquisition.");
            if (camera.GetComponent<ARCameraManager>() == null)
                Undo.AddComponent<ARCameraManager>(camera.gameObject);
            if (camera.GetComponent<AROcclusionManager>() == null)
                Undo.AddComponent<AROcclusionManager>(camera.gameObject);
            MarkDirty();
            Refresh();
        }

        internal void AddGameReadyComponentsToRoot()
        {
            GameObject root = _roomScanRoot ?? new GameObject("RoomScan");
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.transform.localScale = Vector3.one;

            EnsureComponent<DepthCapture>(root);
            EnsureComponent<RoomAnchorManager>(root);
            EnsureComponent<PassthroughCameraProvider>(root);
            EnsureComponent<SigmaRigBridge>(root);
            EnsureComponent<SigmaCarrier>(root);
            EnsureComponent<SigmaTopologyController>(root);
            EnsureComponent<SigmaRenderer>(root);
            EnsureComponent<SigmaInverseController>(root);
            EnsureComponent<RoomScanner>(root);
            EnsureComponent<RoomScanInputHandler>(root);
            _roomScanRoot = root;
            MarkDirty();
        }

        internal void FixDebugModules()
        {
            AddGameReadyComponentsToRoot();
            DebugMenuController controller =
                _roomScanRoot.GetComponentInChildren<DebugMenuController>(true);
            if (controller == null)
            {
                var host = new GameObject("DebugMenu");
                host.transform.SetParent(_roomScanRoot.transform, false);
                Undo.RegisterCreatedObjectUndo(host, "Create Sigma debug menu");
                Undo.AddComponent<UIDocument>(host);
                controller = Undo.AddComponent<DebugMenuController>(host);
            }
            EnsureDebugMenuAssets();
        }

        internal void FixShaderWiring()
        {
            // Sigma readout shaders are loaded explicitly by their owners. The retained
            // capture/debug shell has no mapper material to wire here.
        }

        internal static void EnsureQuestVRManifest()
        {
            VRProjectBootstrap.RequireQuestScanningFeatures();
        }

        internal static void EnsureDebugMenuAssets()
        {
            DebugMenuController controller = FindAny<DebugMenuController>();
            if (controller == null)
                return;
            UIDocument document = controller.GetComponent<UIDocument>();
            if (document == null)
                return;

            Undo.RecordObject(document, "Assign Sigma operator UI");
            document.visualTreeAsset ??= AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Packages/com.genesis.roomscan/Runtime/UI/DebugMenu.uxml");
            document.panelSettings ??= FindOrCreatePanelSettings();
            if (document.panelSettings != null)
                SetPanelRenderModeWorldSpace(document.panelSettings);
            document.worldSpaceSizeMode = WorldSpaceSizeMode.Dynamic;
            document.pivot = Pivot.Center;
            document.pivotReferenceSize = PivotReferenceSize.Layout;
            controller.transform.localScale = Vector3.one * 0.08f;
            EditorUtility.SetDirty(document);
        }

        internal static void EnsureVRInput()
        {
            EventSystem eventSystem = FindAny<EventSystem>();
            if (eventSystem == null)
            {
                var host = new GameObject("EventSystem");
                eventSystem = Undo.AddComponent<EventSystem>(host);
            }
            if (eventSystem.GetComponent<OVRInputModule>() == null)
                Undo.AddComponent<OVRInputModule>(eventSystem.gameObject);
            if (eventSystem.GetComponent<PanelInputConfiguration>() == null)
            {
                var config = Undo.AddComponent<PanelInputConfiguration>(eventSystem.gameObject);
                var serialized = new SerializedObject(config);
                SetBool(serialized, "m_DefaultEventCameraIsMainCamera", true);
                SetBool(serialized, "m_AutoCreatePanelComponents", true);
                serialized.ApplyModifiedProperties();
            }
            EnsureComponent<VRDocumentRaycaster>(eventSystem.gameObject);
            EnsureComponent<ControllerRayDriver>(eventSystem.gameObject);
        }

        internal static void WireComponent(Component component) { }

        private static T EnsureComponent<T>(GameObject host) where T : Component
        {
            T component = host.GetComponent<T>();
            return component != null ? component : Undo.AddComponent<T>(host);
        }

        internal static T FindAny<T>() where T : Object =>
            Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);

        private static void SetBool(SerializedObject serialized, string name, bool value)
        {
            SerializedProperty property = serialized.FindProperty(name);
            if (property != null) property.boolValue = value;
        }

        private static void SetPanelRenderModeWorldSpace(PanelSettings panel)
        {
            var serialized = new SerializedObject(panel);
            SerializedProperty property = serialized.FindProperty("m_RenderMode");
            if (property != null) property.intValue = 1;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static PanelSettings FindOrCreatePanelSettings()
        {
            const string directory = "Assets/Settings";
            const string path = directory + "/SigmaPrismPanelSettings.asset";
            PanelSettings panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
            if (panel != null) return panel;
            if (!AssetDatabase.IsValidFolder(directory))
                AssetDatabase.CreateFolder("Assets", "Settings");
            panel = CreateInstance<PanelSettings>();
            AssetDatabase.CreateAsset(panel, path);
            AssetDatabase.SaveAssets();
            return panel;
        }

        private static void MarkDirty()
        {
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }
    }
}
