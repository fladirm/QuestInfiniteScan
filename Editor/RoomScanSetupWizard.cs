using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Genesis.RoomScan.Exporting;
using Genesis.RoomScan.Prism;
using Genesis.RoomScan.UI;
using Genesis.RoomScan.World;
using Meta.XR;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;
using UnityEngine.XR.ARFoundation;
using Unity.XR.CoreUtils;

namespace Genesis.RoomScan.Editor
{
    public partial class RoomScanSetupWizard : EditorWindow
    {
        double _lastRefresh;
        const double REFRESH_SEC = 0.8;
        Vector2 _scroll;

        // Cached scene state
        ARSession _arSession;
        AROcclusionManager _arOcclusion;
        GameObject _cameraRig;

        DepthCapture _depthCapture;
        RoomScanner _roomScanner;
        PassthroughCameraProvider _cameraProvider;
        PrismRigCapture _prismRigCapture;
        PrismDepthPreprocessor _prismDepthPreprocessor;
        PrismPredictionRenderer _prismPredictionRenderer;
        PrismConeClassifier _prismConeClassifier;
        PrismFilmSpawner _prismFilmSpawner;
        PrismFilmUpdater _prismFilmUpdater;
        PrismBoundaryGraph _prismBoundaryGraph;
        PrismDisplacementTopology _prismDisplacementTopology;
        PrismMeshletBuilder _prismMeshletBuilder;
        PassthroughCameraAccess _pcaComponent;
        CameraDebugOverlay _cameraDebug;
        DepthDebugOverlay _depthDebug;
        KeyframeCollector _keyframeCollector;
        DebugMenuController _debugMenu;
        RoomScanInputHandler _inputHandler;
        RoomAnchorManager _roomAnchor;
        SubmapManager _submapManager;
        GlbExportController _glbExportController;
        EventSystem _eventSystem;
        OVRInputModule _ovrInputModule;
        VRDocumentRaycaster _vrRaycaster;
        ControllerRayDriver _rayDriver;
        PanelInputConfiguration _panelInputConfig;

        bool _debugOverlayWired;
        bool _boundarylessManifest;

        // Style
        static readonly Color COL_OK   = new(0.25f, 0.82f, 0.35f);
        static readonly Color COL_WARN = new(0.95f, 0.78f, 0.15f);
        static readonly Color COL_MISS = new(0.92f, 0.28f, 0.25f);
        static readonly Color COL_INFO = new(0.45f, 0.72f, 0.95f);
        static readonly Color COL_SECT = new(0.18f, 0.18f, 0.22f);

        const string PKG = "Packages/com.genesis.roomscan/Runtime/Shaders/";

        [MenuItem("RoomScan/Setup Scene")]
        static void Open()
        {
            var w = GetWindow<RoomScanSetupWizard>("Room Scan Setup");
            w.minSize = new Vector2(420, 480);
        }

        void OnEnable()  => Refresh();
        void OnFocus()   => Refresh();

        void Update()
        {
            if (EditorApplication.timeSinceStartup - _lastRefresh > REFRESH_SEC)
            {
                Refresh();
                Repaint();
            }
        }

        // =================================================================
        //  REFRESH
        // =================================================================

        void Refresh()
        {
            _lastRefresh = EditorApplication.timeSinceStartup;

            _arSession = FindAny<ARSession>();
            _arOcclusion = FindAny<AROcclusionManager>();

            // Try to find camera rig — look for OVRCameraRig or XROrigin
            _cameraRig = null;
            var xrOrigin = FindAny<Unity.XR.CoreUtils.XROrigin>();
            if (xrOrigin != null)
                _cameraRig = xrOrigin.gameObject;
            if (_cameraRig == null)
            {
                var ovrRig = FindComponentByTypeName("OVRCameraRig");
                if (ovrRig != null) _cameraRig = ovrRig.gameObject;
            }

            _depthCapture = FindAny<DepthCapture>();
            _roomScanner = FindAny<RoomScanner>();
            _cameraProvider = FindAny<PassthroughCameraProvider>();
            _prismRigCapture = FindAny<PrismRigCapture>();
            _prismDepthPreprocessor = FindAny<PrismDepthPreprocessor>();
            _prismPredictionRenderer = FindAny<PrismPredictionRenderer>();
            _prismConeClassifier = FindAny<PrismConeClassifier>();
            _prismFilmSpawner = FindAny<PrismFilmSpawner>();
            _prismFilmUpdater = FindAny<PrismFilmUpdater>();
            _prismBoundaryGraph = FindAny<PrismBoundaryGraph>();
            _prismDisplacementTopology = FindAny<PrismDisplacementTopology>();
            _prismMeshletBuilder = FindAny<PrismMeshletBuilder>();
            _pcaComponent = FindAny<PassthroughCameraAccess>();
            _cameraDebug = FindAny<CameraDebugOverlay>();
            _depthDebug = FindAny<DepthDebugOverlay>();
            _keyframeCollector = FindAny<KeyframeCollector>();
            _debugMenu = FindAny<DebugMenuController>();
            _inputHandler = FindAny<RoomScanInputHandler>();
            _roomAnchor = FindAny<RoomAnchorManager>();
            _submapManager = FindAny<SubmapManager>();
            _glbExportController = FindAny<GlbExportController>();
            _eventSystem = FindAny<EventSystem>();
            _ovrInputModule = FindAny<OVRInputModule>();
            _vrRaycaster = FindAny<VRDocumentRaycaster>();
            _rayDriver = FindAny<ControllerRayDriver>();
            _panelInputConfig = FindAny<PanelInputConfiguration>();

            _debugOverlayWired = _roomScanner != null && AreFieldsAssigned(_roomScanner,
                "debugOverlayShader");
            RefreshAIDetection();
            RefreshVRProject();

            RefreshURPState();

            RefreshBuildingBlocksState();
            _boundarylessManifest = ManifestHasAllQuestVREntries();
        }

        // Partial methods implemented in RoomScanSetupWizard.AIDetection.cs when
        // HAS_AI_INFERENCE is defined; silent no-ops otherwise.
        partial void RefreshAIDetection();
        partial void DrawAIDetectionOptionalStatus();
        partial void CheckAIDetectionAnyMissing(ref bool anyMissing);
        partial void DrawAIDetectionShaderStatus(ref bool needsFix);
        partial void WireAIDetectionComponents();
        partial void SetupAIDetectionIfAvailable(GameObject root);

        // Partial methods implemented in RoomScanSetupWizard.VRProject.cs.
        // Always present (no #if guard) because OpenXR + Meta XR are core deps.
        partial void RefreshVRProject();
        partial void DrawVRProjectSection();

        // =================================================================
        //  GUI
        // =================================================================

        void OnGUI()
        {
            DrawHeader();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            GUILayout.Space(4);

            // Game-Ready Preset is the promoted, common workflow for game
            // developers — keep it at the top so it's the first thing seen.
            // Everything below is for inspection / piecemeal fixes / opt-in
            // modules / final "do absolutely everything" sweep.
            DrawGameReadyPreset();

            DrawPrerequisites();
            DrawProjectSettings();
            DrawComponents();
            DrawVRProjectSection();
            DrawShaderWiring();

            GUILayout.Space(12);
            DrawMasterButton();
            GUILayout.Space(8);

            EditorGUILayout.EndScrollView();
        }

        void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("QuestInfiniteScan Setup", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60)))
                Refresh();
            EditorGUILayout.EndHorizontal();
        }

        // -- Prerequisites ------------------------------------------------

        void DrawPrerequisites()
        {
            BeginSection("PREREQUISITES");

            string urpLabel = _urpAssetCached != null
                ? $"URP pipeline asset wired ({_urpAssetCached.name})"
                : "URP pipeline asset wired";
            StatusRow(urpLabel, _urpConfigured);
            StatusRow("ARSession", _arSession != null);
            StatusRow("Camera Rig (OVRCameraRig / XROrigin)", _cameraRig != null);
            StatusRow("AROcclusionManager", _arOcclusion != null);

            if (!_urpConfigured)
            {
                GUILayout.Space(2);
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Setup URP (Quest defaults)", GUILayout.Width(220)))
                {
                    EnsureURPSetup();
                    Refresh();
                }
                EditorGUILayout.EndHorizontal();
            }

            if (_arSession == null)
            {
                GUILayout.Space(2);
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Add ARSession", GUILayout.Width(200)))
                    FixARSession();
                EditorGUILayout.EndHorizontal();
            }

            if (_cameraRig == null)
            {
                EditorGUILayout.HelpBox(
                    "Add a Camera Rig via  Meta > Tools > Building Blocks.\n" +
                    "The wizard will add AROcclusionManager to it automatically.",
                    MessageType.Info);
            }
            else if (_arOcclusion == null)
            {
                GUILayout.Space(2);
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Add AROcclusionManager", GUILayout.Width(200)))
                    FixAROcclusion();
                EditorGUILayout.EndHorizontal();
            }

            EndSection();
        }

        void FixARSession()
        {
            var go = FindByName("AR Session");
            if (go == null)
            {
                go = new GameObject("AR Session");
                Undo.RegisterCreatedObjectUndo(go, "Create AR Session");
            }

            if (go.GetComponent<ARSession>() == null)
                Undo.AddComponent<ARSession>(go);

            MarkDirty();
            Refresh();
        }

        void FixAROcclusion()
        {
            if (_cameraRig == null) return;

            // Find the camera — typically CenterEyeAnchor or Camera child
            Camera cam = _cameraRig.GetComponentInChildren<Camera>();
            if (cam == null)
            {
                Debug.LogWarning("[RoomScan Setup] No Camera found under camera rig");
                return;
            }

            GameObject target = cam.gameObject;

            // Need ARCameraManager as well for AROcclusionManager to work.
            // Both throw a wall of "No active XRSubsystem" errors in Editor
            // play mode without an active XR loader (no Quest, no Quest
            // Link). On device they're fine. We previously tried to silence
            // the Editor errors with EditorPlayModeXRGuard but the AR
            // OnEnable order bug it relied on never reliably fired before
            // the AR components' own OnEnable, and the workaround
            // introduced its own NRE chain via AROcclusionManager.OnDisable
            // → DestroyTextures. Reverted; just live with the Editor errors
            // and build to device to actually test.
            if (target.GetComponent<ARCameraManager>() == null)
                Undo.AddComponent<ARCameraManager>(target);

            if (target.GetComponent<AROcclusionManager>() == null)
                Undo.AddComponent<AROcclusionManager>(target);

            MarkDirty();
            Refresh();
        }

        // -- Project Settings ---------------------------------------------

        const string MANIFEST_PATH = "Assets/Plugins/Android/AndroidManifest.xml";
        const string ROOMSCAN_MANIFEST_DIR =
            "Assets/Plugins/Android/QuestRoomScanManifest.androidlib";
        const string ROOMSCAN_MANIFEST_PATH =
            ROOMSCAN_MANIFEST_DIR + "/AndroidManifest.xml";

        // Every <uses-feature> + <uses-permission> entry that QRS or its
        // satellite modules expect at runtime. Some of these (HEADSET_CAMERA,
        // USE_SCENE, USE_ANCHOR_API, etc.) are NOT in Meta's templated
        // manifest set and get stripped any time OVRProjectSetup.FixAllAsync
        // or the Project Setup Tool regenerates the manifest from
        // OVRProjectConfig — hence this comprehensive ensure-pass that runs
        // AFTER Meta's tooling in the wizard orchestrators.
        //
        // Each entry is idempotent (skip if already present, never remove).
        struct ManifestFeature { public string Name; public bool Required; }
        static readonly ManifestFeature[] REQUIRED_FEATURES = new[]
        {
            new ManifestFeature { Name = "android.hardware.vr.headtracking", Required = true  },
            new ManifestFeature { Name = "oculus.software.handtracking",     Required = false },
            new ManifestFeature { Name = "com.oculus.feature.PASSTHROUGH",   Required = false },
            new ManifestFeature { Name = "com.oculus.feature.BOUNDARYLESS_APP", Required = true },
        };

        static readonly string[] REQUIRED_PERMISSIONS = new[]
        {
            "com.oculus.permission.HAND_TRACKING",
            "com.oculus.permission.USE_ANCHOR_API",
            "com.oculus.permission.USE_SCENE",
            "horizonos.permission.HEADSET_CAMERA",
        };

        // Fallback values only. At setup time the owned manifest mirrors the
        // installed Meta SDK's OVRProjectConfig, so upgrading Meta XR never
        // leaves this package pinned to an obsolete Horizon OS target.
        const string HORIZONOS_NS = "http://schemas.horizonos/sdk";
        const string HORIZONOS_MIN_SDK_VERSION = "60";
        const string HORIZONOS_TARGET_SDK_VERSION = "207";

        void DrawProjectSettings()
        {
            BeginSection("PROJECT SETTINGS");

            StatusRow("AndroidManifest Quest VR entries (features + permissions)",
                      _boundarylessManifest);

            if (!_boundarylessManifest)
            {
                GUILayout.Space(2);
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Add Quest VR Manifest Entries", GUILayout.Width(220)))
                {
                    EnsureQuestVRManifest();
                    Refresh();
                }
                EditorGUILayout.EndHorizontal();
            }


            EndSection();
        }

        /// <summary>
        /// Returns true iff every entry in REQUIRED_FEATURES /
        /// REQUIRED_PERMISSIONS plus the horizonos SDK declaration is
        /// already present in the manifest. Used by the status row + by the
        /// orchestrators to decide whether to re-run the ensure-pass.
        /// </summary>
        static List<XDocument> LoadQuestManifestDocuments()
        {
            var documents = new List<XDocument>();
            foreach (string relativePath in new[] { MANIFEST_PATH, ROOMSCAN_MANIFEST_PATH })
            {
                string fullPath = Path.Combine(Application.dataPath, "..", relativePath);
                if (!File.Exists(fullPath)) continue;

                try
                {
                    documents.Add(XDocument.Load(fullPath));
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[RoomScan Setup] Ignoring invalid manifest '{relativePath}': {ex.Message}");
                }
            }
            return documents;
        }

        /// <summary>
        /// Creates a merge-only Android library manifest owned by RoomScan.
        /// A minimal custom main manifest would replace Unity 6's generated
        /// GameActivity launcher, so fresh projects must not manufacture one.
        /// </summary>
        static string EnsureOwnedQuestManifest()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string manifestFull = Path.Combine(projectRoot, ROOMSCAN_MANIFEST_PATH);
            string libraryFull = Path.Combine(projectRoot, ROOMSCAN_MANIFEST_DIR);
            Directory.CreateDirectory(libraryFull);

            if (!File.Exists(manifestFull))
            {
                XNamespace android = "http://schemas.android.com/apk/res/android";
                var manifest = new XElement("manifest",
                    new XAttribute(XNamespace.Xmlns + "android", android.NamespaceName),
                    new XAttribute(XNamespace.Xmlns + "horizonos", HORIZONOS_NS),
                    new XAttribute("package", "com.genesis.roomscan.manifest"),
                    new XElement("application"));
                new XDocument(new XDeclaration("1.0", "utf-8", null), manifest)
                    .Save(manifestFull);
            }

            string propertiesFull = Path.Combine(libraryFull, "project.properties");
            if (!File.Exists(propertiesFull))
                File.WriteAllText(propertiesFull, "android.library=true\n");

            return manifestFull;
        }

        static bool SetAttribute(XElement element, XName name, string value)
        {
            var attribute = element.Attribute(name);
            if (attribute != null && attribute.Value == value) return false;

            if (attribute == null)
                element.Add(new XAttribute(name, value));
            else
                attribute.Value = value;
            return true;
        }

        static bool ManifestHasAllQuestVREntries()
        {
            try
            {
                var docs = LoadQuestManifestDocuments();
                if (docs.Count == 0) return false;

                XNamespace android = "http://schemas.android.com/apk/res/android";
                foreach (var f in REQUIRED_FEATURES)
                {
                    bool found = docs.Any(doc => doc.Root != null &&
                        doc.Root.Elements("uses-feature")
                            .Any(e => e.Attribute(android + "name")?.Value == f.Name));
                    if (!found) return false;
                }

                foreach (var p in REQUIRED_PERMISSIONS)
                {
                    bool found = docs.Any(doc => doc.Root != null &&
                        doc.Root.Elements("uses-permission")
                            .Any(e => e.Attribute(android + "name")?.Value == p));
                    if (!found) return false;
                }

                // Meta XR owns the Horizon OS SDK declaration and writes it
                // during manifest preprocessing. Duplicating it in this
                // merge-only fragment produces two declarations in the final
                // manifest. Validate the authoritative project config here.
                var projectConfig = OVRProjectConfig.CachedProjectConfig;
                if (projectConfig == null || projectConfig.horizonOsSdkDisabled ||
                    projectConfig.minHorizonOsSdkVersion < int.Parse(HORIZONOS_MIN_SDK_VERSION) ||
                    projectConfig.targetHorizonOsSdkVersion < System.Math.Max(
                        projectConfig.minHorizonOsSdkVersion,
                        int.Parse(HORIZONOS_TARGET_SDK_VERSION)))
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Idempotent: adds every required uses-feature and uses-permission if
        /// any are missing. Meta XR remains the sole owner of the Horizon OS
        /// SDK declaration generated from OVRProjectConfig.
        /// Never removes existing entries — safe to run after Meta's
        /// OVRProjectSetup.FixAllAsync has rewritten the manifest from
        /// OVRProjectConfig defaults (which strips MR-only permissions like
        /// HEADSET_CAMERA / USE_SCENE that aren't in OVR's template).
        /// </summary>
        static void EnsureQuestVRManifest()
        {
            try
            {
                // Meta XR Core 205 is the current package, while Horizon OS 2.7
                // exposes Meta VR API 207. Target the tested OS behavior without
                // raising the minimum API needed by older supported Quest builds.
                var projectConfig = OVRProjectConfig.CachedProjectConfig;
                if (projectConfig != null)
                {
                    int minimum = int.Parse(HORIZONOS_MIN_SDK_VERSION);
                    int target = int.Parse(HORIZONOS_TARGET_SDK_VERSION);
                    bool configDirty = false;
                    if (projectConfig.horizonOsSdkDisabled)
                    {
                        projectConfig.horizonOsSdkDisabled = false;
                        configDirty = true;
                    }
                    if (projectConfig.minHorizonOsSdkVersion < minimum)
                    {
                        projectConfig.minHorizonOsSdkVersion = minimum;
                        configDirty = true;
                    }
                    int requiredTarget = System.Math.Max(target,
                        projectConfig.minHorizonOsSdkVersion);
                    if (projectConfig.targetHorizonOsSdkVersion < requiredTarget)
                    {
                        projectConfig.targetHorizonOsSdkVersion = requiredTarget;
                        configDirty = true;
                    }
                    if (configDirty)
                        OVRProjectConfig.CommitProjectConfig(projectConfig);
                }

                string fullPath = EnsureOwnedQuestManifest();
                var doc = XDocument.Load(fullPath);
                if (doc.Root == null)
                {
                    Debug.LogError("[RoomScan Setup] AndroidManifest.xml has no <manifest> root.");
                    return;
                }

                XNamespace android = "http://schemas.android.com/apk/res/android";
                bool dirty = false;
                var added = new List<string>();

                // Remove obsolete LAN/server permissions left by historical branches.
                // The pure on-device scanner has no reason to weaken Android transport.
                var ownedApplication = doc.Root.Element("application");
                var legacyNetworkConfig = ownedApplication?
                    .Attribute(android + "networkSecurityConfig");
                if (legacyNetworkConfig != null)
                {
                    legacyNetworkConfig.Remove();
                    dirty = true;
                }
                var legacyCleartext = ownedApplication?
                    .Attribute(android + "usesCleartextTraffic");
                if (legacyCleartext != null)
                {
                    legacyCleartext.Remove();
                    dirty = true;
                }

                // <uses-feature>
                foreach (var f in REQUIRED_FEATURES)
                {
                    bool exists = doc.Root.Elements("uses-feature")
                        .Any(e => e.Attribute(android + "name")?.Value == f.Name);
                    if (exists) continue;

                    var el = new XElement("uses-feature",
                        new XAttribute(android + "name", f.Name),
                        new XAttribute(android + "required", f.Required ? "true" : "false"));

                    // headtracking gets a version attr by Android convention.
                    if (f.Name == "android.hardware.vr.headtracking")
                        el.Add(new XAttribute(android + "version", "1"));

                    doc.Root.Add(el);
                    added.Add($"feature:{f.Name}");
                    dirty = true;
                }

                // <uses-permission>
                foreach (var p in REQUIRED_PERMISSIONS)
                {
                    bool exists = doc.Root.Elements("uses-permission")
                        .Any(e => e.Attribute(android + "name")?.Value == p);
                    if (exists) continue;

                    doc.Root.Add(new XElement("uses-permission",
                        new XAttribute(android + "name", p)));
                    added.Add($"perm:{p}");
                    dirty = true;
                }

                // Remove legacy package-owned Horizon SDK declarations. Meta's
                // build preprocessor writes exactly one from OVRProjectConfig.
                XNamespace horizonos = HORIZONOS_NS;
                foreach (var horizonSdk in doc.Root
                             .Elements(horizonos + "uses-horizonos-sdk").ToList())
                {
                    horizonSdk.Remove();
                    dirty = true;
                }

                if (!dirty)
                {
                    return;
                }

                doc.Save(fullPath);
                AssetDatabase.Refresh();
                Debug.Log($"[RoomScan Setup] AndroidManifest: added {added.Count} entr{(added.Count == 1 ? "y" : "ies")} \u2192 " +
                          string.Join(", ", added));
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[RoomScan Setup] Failed to update manifest: {ex.Message}\n{ex.StackTrace}");
            }
        }

        // -- Components ---------------------------------------------------

        void DrawComponents()
        {
            // ── Core (required) ──
            BeginSection("CORE COMPONENTS (Required)");

            StatusRow("RoomScanner", _roomScanner != null);
            StatusRow("DepthCapture", _depthCapture != null);
            StatusRow("Cone-PRISM StereoRigCapture", _prismRigCapture != null);
            StatusRow("Cone-PRISM GPU Depth Frontend", _prismDepthPreprocessor != null);
            StatusRow("Cone-PRISM Prediction Raster", _prismPredictionRenderer != null);
            StatusRow("Cone-PRISM ConeEvent Classifier", _prismConeClassifier != null);
            StatusRow("Cone-PRISM ContactFilm Pool", _prismFilmSpawner != null);
            StatusRow("Cone-PRISM Meshlet Builder", _prismMeshletBuilder != null);
            StatusRow("RoomAnchorManager (MRUK + SpatialAnchor)", _roomAnchor != null);

            var ovrConfig = OVRProjectConfig.CachedProjectConfig;
            bool anchorSupportOk = ovrConfig != null
                && ovrConfig.anchorSupport != OVRProjectConfig.AnchorSupport.Disabled;
            StatusRow("OVRProjectConfig anchor support", anchorSupportOk);
            if (!anchorSupportOk && ovrConfig != null)
            {
                if (GUILayout.Button("Fix: Enable Spatial Anchor Support"))
                {
                    ovrConfig.anchorSupport = OVRProjectConfig.AnchorSupport.Enabled;
                    OVRProjectConfig.CommitProjectConfig(ovrConfig);
                }
            }

            bool coreMissing = _roomScanner == null || _depthCapture == null ||
                               _prismRigCapture == null ||
                               _prismDepthPreprocessor == null ||
                               _prismPredictionRenderer == null ||
                               _prismConeClassifier == null ||
                               _prismFilmSpawner == null ||
                               _prismMeshletBuilder == null || _roomAnchor == null;
            if (coreMissing)
            {
                GUILayout.Space(2);
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Add Core Components", GUILayout.Width(180)))
                    FixCoreComponents();
                EditorGUILayout.EndHorizontal();
            }

            EndSection();

            // ── Optional modules ──
            BeginSection("OPTIONAL MODULES");
            EditorGUILayout.HelpBox(
                "These are optional. Add them via the RoomScanner inspector's \"Add Module\" dropdown, or use \"Add All\" below.",
                MessageType.Info);

            StatusRowOptional("PassthroughCameraProvider", _cameraProvider != null);
            StatusRowOptional("PassthroughCameraAccess", _pcaComponent != null);
            StatusRowOptional("KeyframeCollector", _keyframeCollector != null);
            DrawAIDetectionOptionalStatus();
            StatusRowOptional("RoomUnderstanding (MRUK bridge)", _roomScanner != null && _roomScanner.GetComponent<RoomUnderstanding>() != null);
            StatusRowOptional("CameraDebugOverlay", _cameraDebug != null);
            StatusRowOptional("DepthDebugOverlay", _depthDebug != null);
            StatusRowOptional("RoomScanInputHandler", _inputHandler != null);
            StatusRowOptional("DebugMenuController (HUD)", _debugMenu != null);

            bool anyOptionalMissing = _cameraProvider == null || _debugMenu == null ||
                                      _inputHandler == null;
            CheckAIDetectionAnyMissing(ref anyOptionalMissing);
            if (anyOptionalMissing)
            {
                GUILayout.Space(2);
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Add All Optional", GUILayout.Width(160)))
                    FixAllOptionalModules();
                EditorGUILayout.EndHorizontal();
            }

            EndSection();

            // Game-Ready Preset is rendered at the very top of the wizard
            // (see OnGUI) — it is the promoted workflow.

            // ── Debug Preset ──
            DrawDebugPreset();
        }

        GameObject FindOrCreateRoot()
        {
            GameObject root = null;
            if (_roomScanner != null)
                root = _roomScanner.gameObject;
            else if (_depthCapture != null)
                root = _depthCapture.gameObject;

            if (root == null)
            {
                root = FindByName("RoomScan");
                if (root == null)
                {
                    root = new GameObject("RoomScan");
                    Undo.RegisterCreatedObjectUndo(root, "Create RoomScan");
                }
            }

            EnsureRoomScanIdentityTransform(root);
            return root;
        }

        // Canonical chunk transforms are explicit; the owner object stays identity
        // so capture, prediction and meshlet materialization share one frame.
        static void EnsureRoomScanIdentityTransform(GameObject root)
        {
            if (root == null) return;
            var t = root.transform;

            bool wrongScale = t.localScale != Vector3.one;
            bool wrongPos = t.localPosition != Vector3.zero;
            bool wrongRot = t.localRotation != Quaternion.identity;
            if (!wrongScale && !wrongPos && !wrongRot) return;

            Undo.RecordObject(t, "Reset RoomScan transform to identity");
            t.localScale = Vector3.one;
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            EditorUtility.SetDirty(root);

            Debug.LogWarning(
                $"[RoomScanSetupWizard] Reset '{root.name}' transform to identity " +
                $"(wrongScale={wrongScale}, wrongPos={wrongPos}, wrongRot={wrongRot}). " +
                "Canonical chunk transforms require an identity scanner root.");
        }

        void FixCoreComponents()
        {
            var root = FindOrCreateRoot();

            // RoomScanner auto-adds DepthCapture, RoomAnchorManager and the
            // canonical world/residency owners through RequireComponent.
            if (root.GetComponent<RoomScanner>() == null)
                Undo.AddComponent<RoomScanner>(root);
            if (root.GetComponent<PrismRigCapture>() == null)
                Undo.AddComponent<PrismRigCapture>(root);
            if (root.GetComponent<PrismDepthPreprocessor>() == null)
                Undo.AddComponent<PrismDepthPreprocessor>(root);
            if (root.GetComponent<PrismPredictionRenderer>() == null)
                Undo.AddComponent<PrismPredictionRenderer>(root);
            if (root.GetComponent<PrismConeClassifier>() == null)
                Undo.AddComponent<PrismConeClassifier>(root);
            if (root.GetComponent<PrismFilmSpawner>() == null)
                Undo.AddComponent<PrismFilmSpawner>(root);
            if (root.GetComponent<PrismPhotometricRefiner>() == null)
                Undo.AddComponent<PrismPhotometricRefiner>(root);
            if (root.GetComponent<PrismFilmUpdater>() == null)
                Undo.AddComponent<PrismFilmUpdater>(root);
            if (root.GetComponent<PrismBoundaryGraph>() == null)
                Undo.AddComponent<PrismBoundaryGraph>(root);
            if (root.GetComponent<PrismDisplacementTopology>() == null)
                Undo.AddComponent<PrismDisplacementTopology>(root);
            if (root.GetComponent<PrismPressureManifoldAtlas>() == null)
                Undo.AddComponent<PrismPressureManifoldAtlas>(root);
            if (root.GetComponent<PrismEvidenceAlignedSplitter>() == null)
                Undo.AddComponent<PrismEvidenceAlignedSplitter>(root);
            if (root.GetComponent<PrismMeshletBuilder>() == null)
                Undo.AddComponent<PrismMeshletBuilder>(root);
            if (root.GetComponent<PrismWorldMeshletRenderer>() == null)
                Undo.AddComponent<PrismWorldMeshletRenderer>(root);
            if (root.GetComponent<PrismGpuWorkGraph>() == null)
                Undo.AddComponent<PrismGpuWorkGraph>(root);

            // Wire shader/compute on newly added core components
            foreach (var c in root.GetComponents<Component>())
                WireComponent(c);

            MarkDirty();
            Refresh();
        }

        void FixAllOptionalModules()
        {
            var root = FindOrCreateRoot();

            // Ensure core exists first
            if (root.GetComponent<RoomScanner>() == null)
                Undo.AddComponent<RoomScanner>(root);
            if (root.GetComponent<PrismRigCapture>() == null)
                Undo.AddComponent<PrismRigCapture>(root);
            if (root.GetComponent<PrismDepthPreprocessor>() == null)
                Undo.AddComponent<PrismDepthPreprocessor>(root);
            if (root.GetComponent<PrismPredictionRenderer>() == null)
                Undo.AddComponent<PrismPredictionRenderer>(root);
            if (root.GetComponent<PrismConeClassifier>() == null)
                Undo.AddComponent<PrismConeClassifier>(root);
            if (root.GetComponent<PrismFilmSpawner>() == null)
                Undo.AddComponent<PrismFilmSpawner>(root);
            if (root.GetComponent<PrismPhotometricRefiner>() == null)
                Undo.AddComponent<PrismPhotometricRefiner>(root);
            if (root.GetComponent<PrismFilmUpdater>() == null)
                Undo.AddComponent<PrismFilmUpdater>(root);
            if (root.GetComponent<PrismBoundaryGraph>() == null)
                Undo.AddComponent<PrismBoundaryGraph>(root);
            if (root.GetComponent<PrismDisplacementTopology>() == null)
                Undo.AddComponent<PrismDisplacementTopology>(root);
            if (root.GetComponent<PrismPressureManifoldAtlas>() == null)
                Undo.AddComponent<PrismPressureManifoldAtlas>(root);
            if (root.GetComponent<PrismEvidenceAlignedSplitter>() == null)
                Undo.AddComponent<PrismEvidenceAlignedSplitter>(root);
            if (root.GetComponent<PrismMeshletBuilder>() == null)
                Undo.AddComponent<PrismMeshletBuilder>(root);
            if (root.GetComponent<PrismWorldMeshletRenderer>() == null)
                Undo.AddComponent<PrismWorldMeshletRenderer>(root);
            if (root.GetComponent<PrismGpuWorkGraph>() == null)
                Undo.AddComponent<PrismGpuWorkGraph>(root);

            // PassthroughCameraAccess isn't pulled in by RequireComponent.
            // It will spam "No active XRSubsystem" / NRE errors in Editor
            // play mode without an XR loader; that's expected and can't be
            // fixed from outside Meta's package — build to device to test.
            if (root.GetComponent<PassthroughCameraAccess>() == null)
                Undo.AddComponent<PassthroughCameraAccess>(root);
            if (root.GetComponent<PassthroughCameraProvider>() == null)
                Undo.AddComponent<PassthroughCameraProvider>(root);

            if (root.GetComponent<RoomUnderstanding>() == null)
                Undo.AddComponent<RoomUnderstanding>(root);

            SetupAIDetectionIfAvailable(root);

            // Optional components not covered by RequireComponent
            if (root.GetComponent<RoomScanInputHandler>() == null)
                Undo.AddComponent<RoomScanInputHandler>(root);

            // Debug overlays — disabled by default
            if (root.GetComponent<CameraDebugOverlay>() == null)
            {
                var c = Undo.AddComponent<CameraDebugOverlay>(root);
                c.enabled = false;
            }
            if (root.GetComponent<DepthDebugOverlay>() == null)
            {
                var c = Undo.AddComponent<DepthDebugOverlay>(root);
                c.enabled = false;
            }

            // DebugMenu lives on a child (needs UIDocument)
            if (FindAny<DebugMenuController>() == null)
            {
                var debugGo = new GameObject("DebugMenu");
                debugGo.transform.SetParent(root.transform);
                Undo.RegisterCreatedObjectUndo(debugGo, "Create DebugMenu");

                Undo.AddComponent<UIDocument>(debugGo);
                Undo.AddComponent<DebugMenuController>(debugGo);
            }

            // Always ensure UIDocument has its assets assigned
            EnsureDebugMenuAssets();

            // Wire all components (core + optional)
            foreach (var c in root.GetComponents<Component>())
                WireComponent(c);

            // EventSystem + VR controller UI input pipeline
            EnsureVRInputInfrastructure();

            MarkDirty();
            Refresh();
        }

        void FixComponents()
        {
            FixCoreComponents();
            FixAllOptionalModules();
        }

        // -- Game-Ready Preset ----------------------------------------------

        void DrawGameReadyPreset()
        {
            BeginSection("GAME-READY PRESET");
            EditorGUILayout.HelpBox(
                "One-click \"make this project actually buildable for Quest VR\":\n" +
                "  \u2022 Switch active build profile to Meta Quest if needed (re-click after the reload)\n" +
                "  \u2022 URP pipeline + renderer at Assets/Settings/ with Quest-friendly defaults (4x MSAA, no HDR, single shadow cascade)\n" +
                "  \u2022 VR project prerequisites (XR Plug-in, OpenXR features, OVRProjectConfig \u2014 Outstanding tier)\n" +
                "  \u2022 AndroidManifest: Quest camera, scene, anchor and boundaryless permissions\n" +
                "  \u2022 Meta XR Building Blocks: OVRCameraRig, Passthrough Underlay, PassthroughCameraAccess\n" +
                "  \u2022 AR Session + AROcclusionManager on the camera rig\n" +
                "  \u2022 Pure Quest Cone-PRISM world, UI, persistence and GLB export\n" +
                "  \u2022 Shader wiring for capture and diagnostics\n" +
                "No TSDF, Surface Nets, triplanar, Gaussian Splat, DiffSoup or server path.",
                MessageType.Info);

            // ── Scene-level state ──
            bool hasPCA = _pcaComponent != null;
            bool hasPCAProvider = _cameraProvider != null;
            bool hasRoomUnderstanding = _roomScanner != null && _roomScanner.GetComponent<RoomUnderstanding>() != null;

            StatusRowOptional("PassthroughCameraAccess (camera RGB)", hasPCA);
            StatusRowOptional("PassthroughCameraProvider", hasPCAProvider);
            StatusRowOptional("RoomUnderstanding (MRUK bridge)", hasRoomUnderstanding);
            StatusRowOptional("Infinite Cone-PRISM chunks + canonical persistence",
                              _submapManager != null && _submapManager.LargeWorldMode);
            StatusRowOptional("GLB/PBR chunk + world export", _glbExportController != null);
            if (_submapManager != null && _submapManager.LargeWorldMode)
            {
                StatusRowOptional("Local active-set memory defaults",
                    _submapManager.UsesLargeWorldDefaults);
            }

            // ── Project-level state (also fixed by this preset) ──
            GUILayout.Space(2);
            EditorGUILayout.LabelField("Project prerequisites", EditorStyles.miniLabel);
            bool buildTargetIsAndroid = EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android;
            bool activeProfileIsMetaQuest = IsActiveProfileMetaQuest();
            string profileLabel = activeProfileIsMetaQuest
                ? "Meta Quest"
                : (buildTargetIsAndroid ? "Android (plain)" : EditorUserBuildSettings.activeBuildTarget.ToString());
            StatusRowOptional($"Active build profile = Meta Quest (current: {profileLabel})", activeProfileIsMetaQuest);
            StatusRowOptional("URP pipeline asset (Quest defaults)", _urpConfigured);
            StatusRowOptional("Meta XR Building Blocks (Camera Rig + Passthrough + PCA)", _bbAllPresent);
            StatusRowOptional("Passthrough scene config (OVRManager + transparent center camera + HEADSET_CAMERA on startup)",
                              _ovrPassthroughReady);
            StatusRowOptional("AR Session + AROcclusionManager", _arSession != null && _arOcclusion != null);
            StatusRowOptional("AndroidManifest (Quest VR features + permissions)",
                              _boundarylessManifest);
            StatusRowOptional($"VR Project Bootstrap ({_vrOutstanding.Count} outstanding)", _vrOutstanding.Count == 0);

            bool sceneMissing   = !hasPCA || !hasPCAProvider || !hasRoomUnderstanding
                                  || _submapManager == null ||
                                  !_submapManager.LargeWorldMode ||
                                  _glbExportController == null;
            bool projectMissing = !buildTargetIsAndroid
                                  || !activeProfileIsMetaQuest
                                  || !_urpConfigured
                                  || !_bbAllPresent
                                  || !_ovrPassthroughReady
                                  || _arSession == null || _arOcclusion == null
                                  || !_boundarylessManifest
                                  || _vrOutstanding.Count > 0;

            if (sceneMissing || projectMissing)
            {
                GUILayout.Space(2);
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(_gameReadyFixInProgress))
                {
                    if (GUILayout.Button("Apply Game-Ready Setup", GUILayout.Width(220)))
                        FixGameReadyModules();
                }
                EditorGUILayout.EndHorizontal();

                if (_gameReadyFixInProgress)
                    EditorGUILayout.HelpBox("Game-Ready setup in progress (VR bootstrap + Meta XR sweep)\u2026", MessageType.Info);
            }

            EndSection();
        }

        void DrawDebugPreset()
        {
            BeginSection("DEBUG PRESET");
            EditorGUILayout.HelpBox(
                "Development tools: debug HUD, input handler, camera/depth overlays, " +
                "and VR input pipeline for interacting with the debug menu. " +
                "Overlays are added disabled by default.",
                MessageType.Info);

            bool hasInput = _inputHandler != null;
            bool hasDebug = _debugMenu != null;
            bool hasCamOverlay = _cameraDebug != null;
            bool hasDepthOverlay = _depthDebug != null;

            StatusRowOptional("RoomScanInputHandler (VR controls)", hasInput);
            StatusRowOptional("DebugMenuController (HUD)", hasDebug);
            StatusRowOptional("CameraDebugOverlay (disabled)", hasCamOverlay);
            StatusRowOptional("DepthDebugOverlay (disabled)", hasDepthOverlay);

            GUILayout.Space(4);
            EditorGUILayout.LabelField("VR Input (for debug menu buttons)", EditorStyles.miniLabel);
            StatusRowOptional("EventSystem + OVRInputModule", _eventSystem != null && _ovrInputModule != null);
            StatusRowOptional("VRDocumentRaycaster (UI pointer)", _vrRaycaster != null);
            StatusRowOptional("ControllerRayDriver (laser + cursor)", _rayDriver != null);
            StatusRowOptional("PanelInputConfiguration", _panelInputConfig != null);

            bool debugMissing = !hasInput || !hasDebug || !hasCamOverlay || !hasDepthOverlay
                                || _eventSystem == null || _ovrInputModule == null
                                || _vrRaycaster == null || _rayDriver == null
                                || _panelInputConfig == null;
            if (debugMissing)
            {
                GUILayout.Space(2);
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Add Debug Modules", GUILayout.Width(200)))
                    FixDebugModules();
                EditorGUILayout.EndHorizontal();
            }

            EndSection();
        }

        // Tracks whether either the Game-Ready preset or Setup Everything is
        // currently running, so the matching buttons can disable themselves and
        // we never re-enter the async orchestrator while VRProjectBootstrap.FixAllAsync
        // is still awaiting Meta's project setup tool.
        bool _gameReadyFixInProgress;

        async void FixGameReadyModules()
        {
            if (_gameReadyFixInProgress) return;
            _gameReadyFixInProgress = true;

            try
            {
                if (TrySwitchToAndroidBuildTarget("Game-Ready Setup")) return;

                EditorUtility.DisplayProgressBar("Game-Ready Setup",
                    "Auditing VR project settings\u2026", 0.05f);
                VRProjectBootstrap.Audit();

                // URP first — shaders fall back to magenta until the
                // pipeline asset exists and is wired into GraphicsSettings,
                // so any later step that touches a Material/Shader needs
                // this in place.
                EditorUtility.DisplayProgressBar("Game-Ready Setup",
                    "Ensuring URP pipeline + Quest-friendly defaults\u2026", 0.10f);
                EnsureURPSetup();

                EditorUtility.DisplayProgressBar("Game-Ready Setup",
                    "Fixing VR prerequisites (XR Plug-in, OpenXR, OVRProjectConfig\u2026)", 0.15f);
                // Environment depth, passthrough and scene/anchor support are
                // classified as Recommended by Meta's project setup tool, but
                // they are runtime requirements for RoomScan. Apply the full
                // RoomScan prerequisite set rather than only build blockers.
                await VRProjectBootstrap.FixAllAsync(CheckSeverity.Recommended);

                // EnsureQuestVRManifest is unconditional (and idempotent) on
                // purpose — it has to undo any permission stripping that
                // OVRProjectSetup.FixAllAsync may have done a moment ago when
                // it regenerated the manifest from OVRProjectConfig defaults.
                // HEADSET_CAMERA / USE_SCENE / USE_ANCHOR_API are not in
                // Meta's templated set and would otherwise vanish here.
                EditorUtility.DisplayProgressBar("Game-Ready Setup",
                    "Updating AndroidManifest + Player Settings\u2026", 0.50f);
                EnsureQuestVRManifest();
                PlayerSettings.insecureHttpOption = InsecureHttpOption.NotAllowed;

                // Meta XR Building Blocks: drops in OVRCameraRig +
                // Passthrough Underlay + PassthroughCameraAccess with
                // Meta's recommended wiring (TrackingOrigin = FloorLevel,
                // Underlay layer set up, etc.). Done before AR session
                // so AROcclusionManager can latch onto the new rig camera.
                EditorUtility.DisplayProgressBar("Game-Ready Setup",
                    "Installing Meta XR Building Blocks (Camera Rig + Passthrough)\u2026", 0.60f);
                await EnsureRequiredBuildingBlocksAsync();
                Refresh();

                EditorUtility.DisplayProgressBar("Game-Ready Setup",
                    "Setting up AR session + occlusion\u2026", 0.65f);
                if (_arSession == null) FixARSession();
                if (_cameraRig != null && _arOcclusion == null) FixAROcclusion();

                EditorUtility.DisplayProgressBar("Game-Ready Setup",
                    "Adding game-ready scene components\u2026", 0.80f);
                AddGameReadyComponentsToRoot();
                EnsurePassthroughSceneConfig();

                EditorUtility.DisplayProgressBar("Game-Ready Setup",
                    "Wiring shaders\u2026", 0.90f);
                FixShaderWiring();

                Debug.Log("[RoomScan Setup] Pure Cone-PRISM setup complete.");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[RoomScan Setup] Game-Ready setup failed: {ex}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                _gameReadyFixInProgress = false;
                MarkDirty();
                Refresh();
                Repaint();
            }
        }

        // Pure scene-component piece of the game-ready preset, callable
        // synchronously from the orchestrator above without owning Refresh().
        void AddGameReadyComponentsToRoot()
        {
            var root = FindOrCreateRoot();

            if (root.GetComponent<RoomScanner>() == null)
                Undo.AddComponent<RoomScanner>(root);
            if (root.GetComponent<PrismRigCapture>() == null)
                Undo.AddComponent<PrismRigCapture>(root);
            if (root.GetComponent<PrismDepthPreprocessor>() == null)
                Undo.AddComponent<PrismDepthPreprocessor>(root);
            if (root.GetComponent<PrismPredictionRenderer>() == null)
                Undo.AddComponent<PrismPredictionRenderer>(root);
            if (root.GetComponent<PrismConeClassifier>() == null)
                Undo.AddComponent<PrismConeClassifier>(root);
            if (root.GetComponent<PrismFilmSpawner>() == null)
                Undo.AddComponent<PrismFilmSpawner>(root);
            if (root.GetComponent<PrismPhotometricRefiner>() == null)
                Undo.AddComponent<PrismPhotometricRefiner>(root);
            if (root.GetComponent<PrismFilmUpdater>() == null)
                Undo.AddComponent<PrismFilmUpdater>(root);
            if (root.GetComponent<PrismBoundaryGraph>() == null)
                Undo.AddComponent<PrismBoundaryGraph>(root);
            if (root.GetComponent<PrismDisplacementTopology>() == null)
                Undo.AddComponent<PrismDisplacementTopology>(root);
            if (root.GetComponent<PrismPressureManifoldAtlas>() == null)
                Undo.AddComponent<PrismPressureManifoldAtlas>(root);
            if (root.GetComponent<PrismEvidenceAlignedSplitter>() == null)
                Undo.AddComponent<PrismEvidenceAlignedSplitter>(root);
            if (root.GetComponent<PrismMeshletBuilder>() == null)
                Undo.AddComponent<PrismMeshletBuilder>(root);
            if (root.GetComponent<PrismWorldMeshletRenderer>() == null)
                Undo.AddComponent<PrismWorldMeshletRenderer>(root);
            if (root.GetComponent<PrismGpuWorkGraph>() == null)
                Undo.AddComponent<PrismGpuWorkGraph>(root);

            // PassthroughCameraAccess is normally added by the Meta XR
            // Building Block (see EnsureRequiredBuildingBlocksAsync), but
            // fall back to a root-level component if the block didn't land
            // anywhere in the scene — RoomScanner needs a PCA somewhere.
            // PCA + ARSession + AROcclusionManager will spam errors in
            // Editor play mode without an XR loader; that's expected,
            // build to device.
            if (UnityEngine.Object.FindAnyObjectByType<PassthroughCameraAccess>() == null)
                Undo.AddComponent<PassthroughCameraAccess>(root);
            if (root.GetComponent<PassthroughCameraProvider>() == null)
                Undo.AddComponent<PassthroughCameraProvider>(root);

            if (root.GetComponent<RoomUnderstanding>() == null)
                Undo.AddComponent<RoomUnderstanding>(root);
            var submaps = root.GetComponent<SubmapManager>();
            bool addedSubmaps = submaps == null;
            if (submaps == null)
                submaps = Undo.AddComponent<SubmapManager>(root);
            if (addedSubmaps)
            {
                Undo.RecordObject(submaps, "Apply QuestInfiniteScan large-world defaults");
                submaps.ApplyLargeWorldDefaults();
                EditorUtility.SetDirty(submaps);
            }
            else if (!submaps.LargeWorldMode)
            {
                // Re-running setup enables the opt-in module but preserves an existing
                // operator's tuned overlap/hysteresis/residency values.
                Undo.RecordObject(submaps, "Enable Infinite Submaps");
                submaps.LargeWorldMode = true;
                EditorUtility.SetDirty(submaps);
            }
            if (root.GetComponent<PrismChunkResidencyManager>() == null)
                Undo.AddComponent<PrismChunkResidencyManager>(root);
            if (root.GetComponent<GlbExportController>() == null)
                Undo.AddComponent<GlbExportController>(root);

            foreach (var c in root.GetComponents<Component>())
                WireComponent(c);
        }

        /// <summary>
        /// If the active build target is not Android, switches it (which
        /// triggers a domain reload and aborts the current async pipeline)
        /// and returns true so the caller bails out cleanly. The user is
        /// informed via dialog that they need to re-click after the reload.
        /// </summary>
        bool TrySwitchToAndroidBuildTarget(string flowName)
        {
            // Already on the Meta Quest profile? Nothing to do.
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android
                && IsActiveProfileMetaQuest())
                return false;

            EditorUtility.ClearProgressBar();

            // Try the Meta Quest *build profile* first (Unity 6.1+). It's a
            // derived Android profile that ships Quest-tuned Player + Quality
            // overrides (Vulkan, IL2CPP, ARM64, Multiview, Quest quality
            // level), and it's what the user picks by hand in File > Build
            // Profiles. Falls back to plain Android if Meta Quest isn't
            // registered (older Unity, missing Android module, etc.).
            string what = "Meta Quest build profile";
            EditorUtility.DisplayDialog(flowName,
                "Active build target is " + EditorUserBuildSettings.activeBuildTarget +
                (IsActiveProfileMetaQuest() ? " (Meta Quest profile)" : "") + ".\n\n" +
                "Switching to the " + what + " now \u2014 this triggers a domain reload " +
                "and aborts the rest of this run.\n\n" +
                "Click \"" + flowName + "\" again after Unity finishes reloading to " +
                "apply the remaining fixes.",
                "Switch and reload");

            if (!TryActivateMetaQuestProfile())
            {
                Debug.LogWarning("[RoomScan Setup] Meta Quest classic build profile not " +
                                 "found \u2014 falling back to plain Android target. " +
                                 "Run File > Build Profiles once to let Unity register the " +
                                 "Meta Quest platform, then re-run this wizard.");
                EditorUserBuildSettings.SwitchActiveBuildTarget(
                    BuildTargetGroup.Android, BuildTarget.Android);
            }

            // Drop the in-progress flag — the domain reload will wipe state
            // anyway, but if for some reason it doesn't fire we don't want
            // to leave the wizard locked out forever.
            _gameReadyFixInProgress = false;
            return true;
        }

        void FixDebugModules()
        {
            var root = FindOrCreateRoot();

            if (root.GetComponent<RoomScanner>() == null)
                Undo.AddComponent<RoomScanner>(root);

            if (root.GetComponent<RoomScanInputHandler>() == null)
                Undo.AddComponent<RoomScanInputHandler>(root);

            if (root.GetComponent<CameraDebugOverlay>() == null)
            {
                var c = Undo.AddComponent<CameraDebugOverlay>(root);
                c.enabled = false;
            }
            if (root.GetComponent<DepthDebugOverlay>() == null)
            {
                var c = Undo.AddComponent<DepthDebugOverlay>(root);
                c.enabled = false;
            }

            if (FindAny<DebugMenuController>() == null)
            {
                var debugGo = new GameObject("DebugMenu");
                debugGo.transform.SetParent(root.transform);
                Undo.RegisterCreatedObjectUndo(debugGo, "Create DebugMenu");
                Undo.AddComponent<UIDocument>(debugGo);
                Undo.AddComponent<DebugMenuController>(debugGo);
            }
            EnsureDebugMenuAssets();

            foreach (var c in root.GetComponents<Component>())
                WireComponent(c);

            EnsureVRInputInfrastructure();

            MarkDirty();
            Refresh();
        }

        /// <summary>
        /// Static entry point for ensuring VR input infrastructure exists.
        /// Called by <see cref="RoomScannerEditor"/> when adding the Debug Menu module.
        /// </summary>
        internal static void EnsureVRInput()
        {
            // EventSystem
            var es = FindAny<EventSystem>();
            if (es == null)
            {
                var esGo = new GameObject("EventSystem");
                Undo.RegisterCreatedObjectUndo(esGo, "Create EventSystem");
                es = Undo.AddComponent<EventSystem>(esGo);
            }

            if (es.GetComponent<OVRInputModule>() == null)
            {
                var standalone = es.GetComponent<StandaloneInputModule>();
                if (standalone != null) Undo.DestroyObjectImmediate(standalone);
                Undo.AddComponent<OVRInputModule>(es.gameObject);
            }

            if (es.GetComponent<PanelInputConfiguration>() == null)
            {
                var pic = Undo.AddComponent<PanelInputConfiguration>(es.gameObject);
                var so = new SerializedObject(pic);
                SetBool(so, "m_DefaultEventCameraIsMainCamera", true);
                SetBool(so, "m_AutoCreatePanelComponents", true);
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(pic);
            }

            if (es.GetComponent<VRDocumentRaycaster>() == null)
                Undo.AddComponent<VRDocumentRaycaster>(es.gameObject);
            var rayDriver = es.GetComponent<ControllerRayDriver>();
            if (rayDriver == null)
                rayDriver = Undo.AddComponent<ControllerRayDriver>(es.gameObject);
            WireComponent(rayDriver);
        }

        void EnsureVRInputInfrastructure() => EnsureVRInput();

        static void SetBool(SerializedObject so, string fieldName, bool value)
        {
            var prop = so.FindProperty(fieldName);
            if (prop != null) prop.boolValue = value;
        }

        // -- Shader / Material Wiring ------------------------------------

        void DrawShaderWiring()
        {
            BeginSection("SHADER & MATERIAL WIRING");

            bool needsFix = false;

            // Core — always present
            if (_depthCapture != null)
                StatusRow("DepthCapture raw stereo ingress", true);
            if (_roomScanner != null)        { StatusRow("DebugOverlay shader (scene viz)", _debugOverlayWired);     needsFix |= !_debugOverlayWired; }
            DrawAIDetectionShaderStatus(ref needsFix);

            if (needsFix)
            {
                GUILayout.Space(2);
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Wire All Shaders", GUILayout.Width(160)))
                    FixShaderWiring();
                EditorGUILayout.EndHorizontal();
            }

            EndSection();
        }

        void FixShaderWiring()
        {
            WireComponent(_depthCapture);
            WireComponent(_depthDebug);
            WireComponent(_roomScanner);

            WireAIDetectionComponents();

            var rayDriver = FindAny<UI.ControllerRayDriver>();
            WireComponent(rayDriver);

            MarkDirty();
            Refresh();
        }

        static void AssignAsset<T>(SerializedObject so, string fieldName, string assetPath) where T : Object
        {
            var prop = so.FindProperty(fieldName);
            if (prop == null) return;
            if (prop.objectReferenceValue != null) return;

            var asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (asset != null)
                prop.objectReferenceValue = asset;
            else
                Debug.LogWarning($"[RoomScan Setup] Could not find {assetPath}");
        }

        /// <summary>
        /// Wires shader/compute/material references on a freshly added component.
        /// Called by both the setup wizard and the RoomScannerEditor "Add Module" dropdown.
        /// </summary>
        internal static void WireComponent(Component component)
        {
            if (component == null) return;

            const string PKG_SHADERS = "Packages/com.genesis.roomscan/Runtime/Shaders/";

            switch (component)
            {
                case DepthDebugOverlay dd:
                {
                    var so = new SerializedObject(dd);
                    AssignAsset<Shader>(so, "depthVisualizeShader", PKG_SHADERS + "DepthVisualize.shader");
                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(dd);
                    break;
                }
                case RoomScanner rs:
                {
                    var so = new SerializedObject(rs);
                    AssignAsset<Shader>(so, "debugOverlayShader", PKG_SHADERS + "DebugOverlay.shader");
                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(rs);
                    break;
                }
                case UI.ControllerRayDriver crd:
                {
                    var so = new SerializedObject(crd);
                    AssignAsset<Shader>(so, "overlayShader", PKG_SHADERS + "DebugOverlay.shader");
                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(crd);
                    break;
                }
            }

#if HAS_AI_INFERENCE
            WireAIDetectionComponent(component);
#endif
        }

        // -- Master Button ------------------------------------------------

        void DrawMasterButton()
        {
            var style = new GUIStyle(GUI.skin.button)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 14,
                fixedHeight = 36
            };

            using (new EditorGUI.DisabledScope(_gameReadyFixInProgress))
            {
                if (GUILayout.Button("\u2261  Setup Everything", style))
                    SetupEverything();
            }
        }

        async void SetupEverything()
        {
            if (_gameReadyFixInProgress) return;

            _gameReadyFixInProgress = true;
            try
            {
                if (TrySwitchToAndroidBuildTarget("Setup Everything")) return;

                EditorUtility.DisplayProgressBar("Setup Everything",
                    "Fixing VR prerequisites (Outstanding + Recommended)\u2026", 0.05f);
                VRProjectBootstrap.Audit();

                // URP must exist before anything else so shaders resolve.
                EditorUtility.DisplayProgressBar("Setup Everything",
                    "Ensuring URP pipeline + Quest-friendly defaults\u2026", 0.10f);
                EnsureURPSetup();

                await VRProjectBootstrap.FixAllAsync(CheckSeverity.Recommended);

                // Camera Rig + Passthrough via Meta XR Building Blocks
                // — does the right thing whether or not a rig is already
                // present. Done before AR session so AROcclusionManager
                // can attach to the rig camera.
                EditorUtility.DisplayProgressBar("Setup Everything",
                    "Installing Meta XR Building Blocks (Camera Rig + Passthrough)\u2026", 0.30f);
                await EnsureRequiredBuildingBlocksAsync();
                Refresh();

                EditorUtility.DisplayProgressBar("Setup Everything",
                    "Setting up AR session + occlusion\u2026", 0.35f);
                if (_arSession == null) FixARSession();
                if (_arOcclusion == null) FixAROcclusion();

                // See the matching comment in FixGameReadyModules — run
                // unconditionally so this restores anything OVRProjectSetup
                // stripped during the Recommended VR fix pass above.
                EditorUtility.DisplayProgressBar("Setup Everything",
                    "Updating AndroidManifest + Player Settings\u2026", 0.50f);
                EnsureQuestVRManifest();
                PlayerSettings.insecureHttpOption = InsecureHttpOption.NotAllowed;

                EditorUtility.DisplayProgressBar("Setup Everything",
                    "Adding all components + wiring shaders\u2026", 0.75f);
                FixComponents();
                EnsurePassthroughSceneConfig();
                FixShaderWiring();

                Debug.Log("[RoomScan Setup] Pure Cone-PRISM scene setup complete.");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[RoomScan Setup] Setup Everything failed: {ex}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                _gameReadyFixInProgress = false;
                MarkDirty();
                Refresh();
                Repaint();
            }
        }

        // =================================================================
        //  GUI HELPERS
        // =================================================================

        void BeginSection(string title)
        {
            GUILayout.Space(6);
            var rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
                GUILayout.ExpandWidth(true), GUILayout.Height(22));
            EditorGUI.DrawRect(rect, COL_SECT);
            var labelRect = new Rect(rect.x + 8, rect.y + 2, rect.width - 16, rect.height);
            var prev = GUI.color;
            GUI.color = Color.white;
            GUI.Label(labelRect, title, EditorStyles.boldLabel);
            GUI.color = prev;
        }

        static void EndSection() => GUILayout.Space(2);

        void StatusRow(string label, bool ok)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(12);

            string icon = ok ? "\u2713" : "\u2717";
            Color col = ok ? COL_OK : COL_MISS;
            string detail = ok ? "OK" : "Missing";

            var prev = GUI.color;
            GUI.color = col;
            GUILayout.Label(icon, EditorStyles.boldLabel, GUILayout.Width(18));
            GUI.color = prev;

            GUILayout.Label(label, GUILayout.ExpandWidth(true));

            prev = GUI.color;
            GUI.color = col;
            GUILayout.Label(detail, EditorStyles.miniLabel, GUILayout.Width(60));
            GUI.color = prev;

            EditorGUILayout.EndHorizontal();
        }

        void StatusRowOptional(string label, bool attached)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(12);

            string icon = attached ? "\u2713" : "\u2022";
            Color col = attached ? COL_OK : COL_INFO;
            string detail = attached ? "OK" : "Not Added";

            var prev = GUI.color;
            GUI.color = col;
            GUILayout.Label(icon, EditorStyles.boldLabel, GUILayout.Width(18));
            GUI.color = prev;

            GUILayout.Label(label, GUILayout.ExpandWidth(true));

            prev = GUI.color;
            GUI.color = col;
            GUILayout.Label(detail, EditorStyles.miniLabel, GUILayout.Width(60));
            GUI.color = prev;

            EditorGUILayout.EndHorizontal();
        }

        // =================================================================
        //  UTILITY
        // =================================================================

        static T FindAny<T>() where T : Object =>
            Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);

        static Component FindComponentByTypeName(string typeName)
        {
            foreach (var root in SceneRoots())
            {
                var found = root.GetComponentsInChildren<Component>(true)
                    .FirstOrDefault(c => c != null && c.GetType().Name == typeName);
                if (found != null) return found;
            }
            return null;
        }

        static bool AreFieldsAssigned(Object target, params string[] fieldNames)
        {
            var so = new SerializedObject(target);
            foreach (string name in fieldNames)
            {
                var prop = so.FindProperty(name);
                if (prop == null || prop.objectReferenceValue == null)
                    return false;
            }
            return true;
        }

        static GameObject FindByName(string exact)
        {
            foreach (var root in SceneRoots())
            {
                var t = DeepFind(root.transform,
                    tr => tr.name.Equals(exact, System.StringComparison.Ordinal));
                if (t != null) return t.gameObject;
            }
            return null;
        }

        static Transform DeepFind(Transform root, System.Func<Transform, bool> pred)
        {
            if (pred(root)) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var hit = DeepFind(root.GetChild(i), pred);
                if (hit != null) return hit;
            }
            return null;
        }

        static GameObject[] SceneRoots() =>
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();

        static void MarkDirty() =>
            EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        /// <summary>
        /// Sets m_RenderMode to 1 (WorldSpace). The public renderMode property
        /// and PanelRenderMode enum are internal in Unity 6000.3.
        /// </summary>
        static void SetPanelRenderModeWorldSpace(PanelSettings panel)
        {
            const int WorldSpace = 1;
            var so = new SerializedObject(panel);
            var prop = so.FindProperty("m_RenderMode");
            if (prop != null && prop.intValue != WorldSpace)
            {
                prop.intValue = WorldSpace;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(panel);
            }
        }

        internal static void EnsureDebugMenuAssets()
        {
            var ctrl = FindAny<DebugMenuController>();
            if (ctrl == null) return;

            var uiDoc = ctrl.GetComponent<UIDocument>();
            if (uiDoc == null) return;

            Undo.RecordObject(uiDoc, "Assign DebugMenu UIDocument assets");

            if (uiDoc.visualTreeAsset == null)
            {
                var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                    "Packages/com.genesis.roomscan/Runtime/UI/DebugMenu.uxml");
                if (uxml != null) uiDoc.visualTreeAsset = uxml;
            }

            if (uiDoc.panelSettings == null)
            {
                var panel = FindOrCreatePanelSettings();
                if (panel != null) uiDoc.panelSettings = panel;
            }

            // Ensure PanelSettings is configured for world-space VR rendering.
            // renderMode / PanelRenderMode are internal in 6000.3; use SerializedObject.
            if (uiDoc.panelSettings != null)
                SetPanelRenderModeWorldSpace(uiDoc.panelSettings);

            // World-space UIDocument properties:
            // - Dynamic size mode: panel auto-sizes to the content layout (480×640 from USS)
            // - Pivot = Center: transform position = center of the visible panel
            // - PivotReferenceSize = Layout: pivot calculated from root element layout, not bounding box
            uiDoc.worldSpaceSizeMode = WorldSpaceSizeMode.Dynamic;
            uiDoc.pivot = Pivot.Center;
            uiDoc.pivotReferenceSize = PivotReferenceSize.Layout;

            // 480px / 100 PPU = 4.8 local units. Scale 0.08 → 0.384m wide.
            const float worldScale = 0.08f;
            if (Mathf.Abs(ctrl.transform.localScale.x - worldScale) > 0.01f)
            {
                Undo.RecordObject(ctrl.transform, "Scale DebugMenu for VR");
                ctrl.transform.localScale = Vector3.one * worldScale;
            }

            EditorUtility.SetDirty(uiDoc);
        }

        static PanelSettings FindOrCreatePanelSettings()
        {
            const string assetName = "DebugMenuPanelSettings";

            string[] guids = AssetDatabase.FindAssets($"t:PanelSettings {assetName}");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                var existing = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
                if (existing != null) return existing;
            }

            const string dir = "Assets/Settings";
            if (!AssetDatabase.IsValidFolder(dir))
                AssetDatabase.CreateFolder("Assets", "Settings");

            const string assetPath = dir + "/" + assetName + ".asset";
            var panel = ScriptableObject.CreateInstance<PanelSettings>();
            AssetDatabase.CreateAsset(panel, assetPath);
            SetPanelRenderModeWorldSpace(panel);
            AssetDatabase.SaveAssets();
            Debug.Log($"[RoomScanWizard] Created PanelSettings (WorldSpace) at {assetPath}");
            return panel;
        }

    }
}
