using System;
using System.Threading.Tasks;
using Genesis.RoomScan.Prism;
using Genesis.RoomScan.UI;
using Genesis.RoomScan.World;
using UnityEngine;

namespace Genesis.RoomScan
{
    /// <summary>
    /// Which view of the one canonical Cone-PRISM manifold to display.
    /// </summary>
    public enum ScanRenderMode
    {
        /// <summary>Canonical measured ContactFilm meshlets.</summary>
        Vertex = 0,
        /// <summary>All scan rendering disabled.</summary>
        None = 6,
        /// <summary>Live GPU mesh wireframe via barycentric edge detection.</summary>
        Wireframe = 7
    }

    /// <summary>
    /// Serialized lifecycle of live sensor ingress. Starting is deliberately distinct
    /// from Running: UI/input retries during asynchronous chunk rehydration must not be
    /// interpreted as Stop requests and must never start canonical snapshot staging.
    /// </summary>
    public enum ScanLifecycleState
    {
        Stopped,
        Starting,
        Running,
        Stopping
    }

    /// <summary>
    /// Top-level orchestrator for room scanning. All sibling components live on
    /// the same GameObject and are resolved automatically via GetComponent.
    /// Input bindings are handled by <see cref="RoomScanInputHandler"/> (optional).
    /// </summary>
    [RequireComponent(typeof(DepthCapture))]
    [RequireComponent(typeof(RoomAnchorManager))]
    [RequireComponent(typeof(SubmapManager), typeof(PrismChunkResidencyManager))]
    public class RoomScanner : MonoBehaviour
    {
        public static RoomScanner Instance { get; private set; }

        [Header("Render Mode")]
        [SerializeField] private ScanRenderMode renderMode = ScanRenderMode.Vertex;

        [SerializeField, Range(0.2f, 5f), Tooltip("Wireframe line thickness multiplier")]
        private float wireThickness = 1.5f;

        [Header("Logging")]
        [SerializeField] private LogLevel logLevel = LogLevel.Info;

        // ─────────────────────────────────────────────────────────────
        //  Sibling component cache (resolved in Awake)
        // ─────────────────────────────────────────────────────────────

        private DepthCapture _depthCapture;
        private RoomAnchorManager _roomAnchor;

        // Optional modules (discovered, not required)
        private PassthroughCameraProvider _cameraProvider;
        private PrismRigCapture _prismRigCapture;
        private PrismDepthPreprocessor _prismDepthPreprocessor;
        private PrismPredictionRenderer _prismPredictionRenderer;
        private PrismConeClassifier _prismConeClassifier;
        private PrismFilmSpawner _prismFilmSpawner;
        private PrismPhotometricRefiner _prismPhotometricRefiner;
        private PrismFilmUpdater _prismFilmUpdater;
        private PrismBoundaryGraph _prismBoundaryGraph;
        private PrismDisplacementTopology _prismDisplacementTopology;
        private PrismPressureManifoldAtlas _prismPressureManifoldAtlas;
        private PrismEvidenceAlignedSplitter _prismEvidenceAlignedSplitter;
        private PrismMeshletBuilder _prismMeshletBuilder;
        private PrismChunkResidencyManager _prismChunkResidency;
        private PrismWorldMeshletRenderer _prismWorldRenderer;
        private PrismGpuWorkGraph _prismWorkGraph;
        private KeyframeCollector _keyframeCollector;
        private RoomUnderstanding _roomUnderstanding;
        private DebugMenuController _debugMenu;
        private SubmapManager _submapManager;
        private ICameraProvider _customCameraProvider;
        private IRoomScanModule[] _modules;

        // ─────────────────────────────────────────────────────────────
        //  Public read-only state
        // ─────────────────────────────────────────────────────────────

        /// <summary>The unified scene object registry (MRUK + AI detections).</summary>
        public SceneObjectRegistry SceneObjectRegistry => _sceneObjectRegistry;

        [SerializeField, Tooltip("Show scene object annotations overlay (wireframe boxes + labels)")]
        private bool showSceneObjects;
        [SerializeField, Tooltip("Shader used for debug overlay wireframes and labels")]
        internal Shader debugOverlayShader;

        /// <summary>Toggle scene object annotation overlay on any render mode.</summary>
        public bool ShowSceneObjects
        {
            get => showSceneObjects;
            set
            {
                showSceneObjects = value;
                if (value)
                {
                    if (_sceneObjectVisualizer == null)
                    {
                        var go = new GameObject("SceneObjectVisualizer");
                        go.transform.SetParent(transform, false);
                        _sceneObjectVisualizer = go.AddComponent<SceneObjectVisualizer>();
                        _sceneObjectVisualizer.SetShader(debugOverlayShader);
                    }
                    _sceneObjectVisualizer.Show(_sceneObjectRegistry);
                }
                else
                {
                    _sceneObjectVisualizer?.Hide();
                }
            }
        }

        public ScanLifecycleState ScanLifecycle { get; private set; } =
            ScanLifecycleState.Stopped;
        /// <summary>
        /// True while a scan is starting or running. Keeping Starting visible here is
        /// required by chunk residency, which prepares the active canonical arenas before
        /// sensor ingress opens.
        /// </summary>
        public bool IsScanning => ScanLifecycle is ScanLifecycleState.Starting or
                                  ScanLifecycleState.Running;
        public bool IsScanStarting => ScanLifecycle == ScanLifecycleState.Starting;
        public string LastScanStartError { get; private set; }
        public ScanRenderMode CurrentRenderMode => renderMode;
        public DebugMenuController DebugMenu => _debugMenu;

        /// <summary>The core depth capture component.</summary>
        public DepthCapture DepthCapture => _depthCapture;
        /// <summary>The optional keyframe collector used by large-world routing.</summary>
        public KeyframeCollector KeyframeCollector => _keyframeCollector;
        /// <summary>The active camera provider (custom or passthrough).</summary>
        public ICameraProvider ActiveCameraProvider => GetActiveCameraProvider();
        /// <summary>The coherent Cone-PRISM stereo RGB-D GPU capture.</summary>
        public PrismRigCapture PrismRigCapture => _prismRigCapture;
        /// <summary>Cone LUT and metric stereo-depth GPU preprocessing.</summary>
        public PrismDepthPreprocessor PrismDepthPreprocessor => _prismDepthPreprocessor;
        /// <summary>Dual-eye hardware first-hit prediction/association raster.</summary>
        public PrismPredictionRenderer PrismPredictionRenderer => _prismPredictionRenderer;
        /// <summary>GPU finite-cone first-hit event classifier.</summary>
        public PrismConeClassifier PrismConeClassifier => _prismConeClassifier;
        /// <summary>Canonical GPU ContactFilm pool and robust spawn stage.</summary>
        public PrismFilmSpawner PrismFilmSpawner => _prismFilmSpawner;
        /// <summary>GPU L/R and temporal normal-axis photometric pressure.</summary>
        public PrismPhotometricRefiner PrismPhotometricRefiner =>
            _prismPhotometricRefiner;
        /// <summary>Persistent pressure/information refinement of matched films.</summary>
        public PrismFilmUpdater PrismFilmUpdater => _prismFilmUpdater;
        /// <summary>Persistent GPU ContactBoundary graph.</summary>
        public PrismBoundaryGraph PrismBoundaryGraph => _prismBoundaryGraph;
        /// <summary>Sparse hierarchical micro-geometry and topology posterior.</summary>
        public PrismDisplacementTopology PrismDisplacementTopology =>
            _prismDisplacementTopology;
        /// <summary>Support-contour/half-edge PressureManifold topology atlas.</summary>
        public PrismPressureManifoldAtlas PrismPressureManifoldAtlas =>
            _prismPressureManifoldAtlas;
        /// <summary>GPU ContactFilm-to-meshlet publication stage.</summary>
        public PrismMeshletBuilder PrismMeshletBuilder => _prismMeshletBuilder;
        /// <summary>Native PRISM chunk staging, paging, restart, and revisit.</summary>
        public PrismChunkResidencyManager PrismChunkResidency =>
            _prismChunkResidency;

        // ─────────────────────────────────────────────────────────────
        //  Events
        // ─────────────────────────────────────────────────────────────

        public event Action ScanStarted;
        public event Action ScanStopped;
        public event Action<ScanRenderMode> RenderModeChanged;
        /// <summary>Raised after the per-scan spatial anchor is persisted successfully.</summary>
        public event Action<Guid, Matrix4x4> ScanAnchorCreated;

        // ─────────────────────────────────────────────────────────────
        //  Private state
        // ─────────────────────────────────────────────────────────────

        private bool _started;
        private bool _scanResourcesReleased;
        private bool _prismPipelinePrepared;
        private bool _prismCaptureRunning;
        private Task _scanStartTask = Task.CompletedTask;
        private uint _scanLifecycleGeneration;

        internal bool IsPrismCaptureRunning => _prismCaptureRunning;

        // Scene object registry
        private SceneObjectRegistry _sceneObjectRegistry;
        private SceneObjectVisualizer _sceneObjectVisualizer;

        // ─────────────────────────────────────────────────────────────
        //  Lifecycle
        // ─────────────────────────────────────────────────────────────

        private void Awake()
        {
            Instance = this;
            Logger.Level = logLevel;
            CacheComponents();
            SetSafeShaderDefaults();
        }

        private void Start()
        {
            // Match DepthCapture's Editor early-out: when no XR loader is
            // active, AR/MRUK subsystems are dead and any module that touches
            // them on init will NRE. The user can still load saved scans
            // through other code paths but cannot start a live scan.
            if (!XRRuntimeGuard.IsXRActive)
            {
                Logger.Warning("RoomScanner: " + XRRuntimeGuard.EditorDisabledMessage);
                enabled = false;
                return;
            }

            _modules = GetComponents<IRoomScanModule>();
            foreach (IRoomScanModule module in _modules)
                module.OnModuleInitialize(this);

            if (_roomAnchor != null && _roomAnchor.enabled)
            {
                if (_roomAnchor.IsRoomLoaded)
                    CompleteRoomStartup();
                else
                    _roomAnchor.RoomReady += OnRoomAnchorReady;
            }
            else
                CompleteRoomStartup();
        }

        private void OnRoomAnchorReady()
        {
            if (_roomAnchor != null)
                _roomAnchor.RoomReady -= OnRoomAnchorReady;
            if (_started)
                return;
            CompleteRoomStartup();
        }

        private void CacheComponents()
        {
            _depthCapture = GetComponent<DepthCapture>();
            _cameraProvider = GetComponent<PassthroughCameraProvider>();
            _prismRigCapture = GetComponent<PrismRigCapture>();
            if (_prismRigCapture == null)
                _prismRigCapture = gameObject.AddComponent<PrismRigCapture>();
            _prismDepthPreprocessor = GetComponent<PrismDepthPreprocessor>();
            if (_prismDepthPreprocessor == null)
                _prismDepthPreprocessor = gameObject.AddComponent<PrismDepthPreprocessor>();
            _prismPredictionRenderer = GetComponent<PrismPredictionRenderer>();
            if (_prismPredictionRenderer == null)
                _prismPredictionRenderer = gameObject.AddComponent<PrismPredictionRenderer>();
            _prismConeClassifier = GetComponent<PrismConeClassifier>();
            if (_prismConeClassifier == null)
                _prismConeClassifier = gameObject.AddComponent<PrismConeClassifier>();
            _prismFilmSpawner = GetComponent<PrismFilmSpawner>();
            if (_prismFilmSpawner == null)
                _prismFilmSpawner = gameObject.AddComponent<PrismFilmSpawner>();
            _prismPhotometricRefiner = GetComponent<PrismPhotometricRefiner>();
            if (_prismPhotometricRefiner == null)
                _prismPhotometricRefiner =
                    gameObject.AddComponent<PrismPhotometricRefiner>();
            _prismFilmUpdater = GetComponent<PrismFilmUpdater>();
            if (_prismFilmUpdater == null)
                _prismFilmUpdater = gameObject.AddComponent<PrismFilmUpdater>();
            _prismBoundaryGraph = GetComponent<PrismBoundaryGraph>();
            if (_prismBoundaryGraph == null)
                _prismBoundaryGraph = gameObject.AddComponent<PrismBoundaryGraph>();
            _prismDisplacementTopology = GetComponent<PrismDisplacementTopology>();
            if (_prismDisplacementTopology == null)
                _prismDisplacementTopology =
                    gameObject.AddComponent<PrismDisplacementTopology>();
            _prismPressureManifoldAtlas = GetComponent<PrismPressureManifoldAtlas>();
            if (_prismPressureManifoldAtlas == null)
                _prismPressureManifoldAtlas =
                    gameObject.AddComponent<PrismPressureManifoldAtlas>();
            _prismEvidenceAlignedSplitter =
                GetComponent<PrismEvidenceAlignedSplitter>();
            if (_prismEvidenceAlignedSplitter == null)
                _prismEvidenceAlignedSplitter =
                    gameObject.AddComponent<PrismEvidenceAlignedSplitter>();
            _prismMeshletBuilder = GetComponent<PrismMeshletBuilder>();
            if (_prismMeshletBuilder == null)
                _prismMeshletBuilder = gameObject.AddComponent<PrismMeshletBuilder>();
            _prismWorldRenderer = GetComponent<PrismWorldMeshletRenderer>();
            if (_prismWorldRenderer == null)
                _prismWorldRenderer =
                    gameObject.AddComponent<PrismWorldMeshletRenderer>();
            _prismWorkGraph = GetComponent<PrismGpuWorkGraph>();
            if (_prismWorkGraph == null)
                _prismWorkGraph = gameObject.AddComponent<PrismGpuWorkGraph>();
            _keyframeCollector = GetComponent<KeyframeCollector>();
            _roomUnderstanding = GetComponent<RoomUnderstanding>();
            _debugMenu = GetComponentInChildren<DebugMenuController>();
            _roomAnchor = GetComponent<RoomAnchorManager>();
            _submapManager = GetComponent<SubmapManager>();
            _prismChunkResidency = GetComponent<PrismChunkResidencyManager>();
            if (_submapManager == null || _prismChunkResidency == null)
                throw new InvalidOperationException(
                    "Cone-PRISM requires its world and residency owners.");
        }

        /// <summary>
        /// Finishes startup after MRUK room is ready (or immediately if <see cref="RoomAnchorManager"/> is disabled).
        /// </summary>
        private void CompleteRoomStartup()
        {
            if (_started)
                return;
            _started = true;
            Logger.Info("Room ready — call StartScanning() to begin");
        }

        private void OnDisable()
        {
            StopScanning();
            UnsubscribeFromAnchorsChanged();
        }

        private bool _subscribedToAnchorsChanged;

        private void Update()
        {
            if (_clearDone)
            {
                _clearDone = false;
                _clearInProgress = false;

                Logger.Info("All scan + export data cleared");
                _clearDoneCallback?.Invoke();
                _clearDoneCallback = null;
            }

            // Pixel association, topology and publication are owned by the GPU work
            // graph. RoomScanner never performs per-frame CPU geometry work.
        }

        // ═════════════════════════════════════════════════════════════
        //  PUBLIC API — call from any client, input handler, or UI
        // ═════════════════════════════════════════════════════════════

        /// <summary>
        /// Begins pure Quest Cone-PRISM capture and GPU reconstruction.
        ///
        /// <para>
        /// <b>Async by necessity, not by API preference.</b> The lazy GPU
        /// bring-up of the canonical ContactFilm arenas and the passthrough-camera
        /// handshake (PCA → MRUK) are both heavy work
        /// for the render thread, and if they land in the same Unity frame
        /// PCA's hardware-buffer-queue handshake loses the race against
        /// our compute dispatches: MRUK then spams "Hardware buffer queue
        /// is empty", Vulkan submit corrupts with
        /// VK_ERROR_INITIALIZATION_FAILED, and the compositor never
        /// recovers (perceived as a permanent hang on the user's first
        /// A-press). The fix is to do the GPU allocations + first
        /// dispatches first, yield twice between each step so the render
        /// thread commits the new resources across separate frames, and
        /// only then enable PCA + AROcclusionManager. Total wall-clock
        /// cost on a Quest 3 is ~56 ms (4 frames at 72 fps) — below the
        /// "press registered" threshold of human perception.
        /// </para>
        ///
        /// <para>
        /// Eager allocation in <c>Awake</c>/<c>Start</c> avoided the bug
        /// because the render thread had committed all VRAM and run the
        /// first dispatches across multiple uneventful boot-splash frames
        /// before the user could ever press A. The lazy-alloc landing
        /// regressed that without realising the timing was load-bearing.
        /// We keep lazy alloc (so the load-existing-scan path doesn't pay
        /// the 600 MB cost) and add the inline staging instead.
        /// </para>
        /// </summary>
        public Task StartScanningAsync()
        {
            if (ScanLifecycle == ScanLifecycleState.Running)
                return Task.CompletedTask;
            if (ScanLifecycle == ScanLifecycleState.Starting)
                return _scanStartTask;

            uint generation = ++_scanLifecycleGeneration;
            ScanLifecycle = ScanLifecycleState.Starting;
            LastScanStartError = null;
            _scanStartTask = StartScanningCoreAsync(generation);
            return _scanStartTask;
        }

        private async Task StartScanningCoreAsync(uint generation)
        {
            try
            {
                _scanResourcesReleased = false;
                bool resuming = _submapManager != null && _submapManager.HasWorld;

                if (!resuming)
                {
                    SetRenderMode(ScanRenderMode.Vertex);
                    _ = CreateScanAnchorAsync();
                }

                // Live reconstruction has one display source. Historical QRS modes
                // must never leave the canonical PRISM mesh hidden on resume.
                SetRenderMode(ScanRenderMode.Vertex);
                PreparePrismPipeline();

                if (!resuming)
                {
                    // Fresh scan: reset registry — stale AI detections from a previous
                    // session/load are in a different anchor frame and must be discarded.
                    _sceneObjectRegistry = new SceneObjectRegistry();
                }
                else
                {
                    _sceneObjectRegistry ??= new SceneObjectRegistry();
                }
                PopulateSceneObjectRegistry();
                SubscribeToAnchorsChanged();

                if (_modules != null)
                    foreach (var m in _modules) m.OnScanStarted();

                if (_prismChunkResidency != null)
                    await _prismChunkResidency.PrepareActiveChunkAsync();

                // A direct Stop/disable may invalidate an in-flight start while chunk I/O
                // is awaited. Never reopen sensors from the stale continuation.
                if (generation != _scanLifecycleGeneration ||
                    ScanLifecycle != ScanLifecycleState.Starting)
                {
                    Logger.Info("StartScanning cancelled before sensor ingress");
                    return;
                }

                _depthCapture.StartDepthCapture();
                StartPrismCapture();
                if (_prismRigCapture != null && !_prismCaptureRunning)
                    throw new InvalidOperationException(
                        "Cone-PRISM rig capture did not enter the requested state.");
                ScanLifecycle = ScanLifecycleState.Running;
                Logger.Info($"StartScanning — Cone-PRISM canonical, resuming={resuming}");
                ScanStarted?.Invoke();
            }
            catch (Exception exception)
            {
                if (generation == _scanLifecycleGeneration)
                {
                    ScanLifecycle = ScanLifecycleState.Stopped;
                    LastScanStartError = exception.Message;
                }
                PausePrismPipeline();
                _depthCapture?.StopDepthCapture();
                Logger.Error("StartScanning failed before sensor ingress: " + exception);
                throw;
            }
        }

        /// <summary>
        /// Creates the world relocation anchor. The SubmapManager persists the UUID
        /// directly into the canonical world manifest.
        /// </summary>
        private async Task CreateScanAnchorAsync()
        {
            var mgr = RoomAnchorManager.Instance;
            if (mgr == null || !mgr.IsRoomLoaded) return;

            var camPos = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            var result = await mgr.CreateAndSaveSpatialAnchorAsync(camPos, Quaternion.identity);
            if (result.HasValue)
            {
                ScanAnchorCreated?.Invoke(result.Value.uuid, result.Value.matrix);
            }
        }

        /// <summary>
        /// Pauses depth integration and stops the camera provider.
        /// </summary>
        public void StopScanning()
        {
            if (ScanLifecycle is ScanLifecycleState.Stopped or
                ScanLifecycleState.Stopping)
                return;

            ++_scanLifecycleGeneration;
            if (ScanLifecycle == ScanLifecycleState.Starting)
            {
                // No ScanStopped notification here: capture never opened, therefore
                // residency must not stage hundreds of MiB in response to a cancelled
                // warm-up. The stale async continuation observes the generation change.
                ScanLifecycle = ScanLifecycleState.Stopped;
                PausePrismPipeline();
                _depthCapture?.StopDepthCapture();
                Logger.Info("StartScanning cancelled while preparing active chunk");
                return;
            }

            ScanLifecycle = ScanLifecycleState.Stopping;

            PausePrismPipeline();
            _depthCapture.StopDepthCapture();

            ScanStopped?.Invoke();
            if (_modules != null)
                foreach (var m in _modules) m.OnScanStopped();
            ScanLifecycle = ScanLifecycleState.Stopped;
        }

        /// <summary>
        /// Pauses only Cone-PRISM consumers while a durable chunk is atomically
        /// rehydrated. There is no synchronous GPU readback in this path.
        /// </summary>
        internal void PausePrismForResidency()
        {
            if (!_prismCaptureRunning) return;
            _prismRigCapture?.StopCapture();
            _prismCaptureRunning = false;
        }

        internal void ResumePrismAfterResidency()
        {
            if (ScanLifecycle == ScanLifecycleState.Running) StartPrismCapture();
        }

        internal void ConfigurePrismChunk(ChunkRecord chunk)
        {
            if (chunk == null) return;
            uint numericId = PrismChunkIdentity.ToNumericId(chunk.chunkId);
            Matrix4x4 worldFromChunk = chunk.worldFromChunk.ToMatrix();
            _prismFilmSpawner?.SetChunkFrame(numericId, worldFromChunk);
            _prismPhotometricRefiner?.SetChunkFrame(worldFromChunk);
            _prismFilmUpdater?.SetChunkFrame(worldFromChunk);
            _prismBoundaryGraph?.SetChunkFrame(worldFromChunk);
            _prismDisplacementTopology?.SetChunkFrame(worldFromChunk);
            _prismPressureManifoldAtlas?.SetChunkFrame(numericId);
            _prismPredictionRenderer?.Meshlets?.SetChunkTransform(worldFromChunk);
        }

        private void PreparePrismPipeline()
        {
            if (_prismPipelinePrepared) return;
            _prismFilmSpawner?.StartSpawning(_prismConeClassifier, false);
            _prismPhotometricRefiner?.StartRefining(_prismFilmSpawner);
            _prismFilmUpdater?.StartUpdating(_prismConeClassifier,
                _prismFilmSpawner, false);
            _prismBoundaryGraph?.StartTracking(_prismFilmUpdater,
                _prismFilmSpawner, false);
            _prismDisplacementTopology?.StartUpdating(_prismBoundaryGraph,
                _prismFilmSpawner, false);
            _prismPressureManifoldAtlas?.StartAtlas(_prismFilmSpawner,
                _prismDisplacementTopology, _prismBoundaryGraph);
            _prismConeClassifier?.StartClassifying(_prismPredictionRenderer,
                _prismBoundaryGraph, _prismFilmSpawner?.PressureManifolds, false);
            _prismPredictionRenderer?.StartRendering(_prismDepthPreprocessor,
                false);
            _prismMeshletBuilder?.StartBuilding(_prismFilmSpawner,
                _prismPredictionRenderer, _prismBoundaryGraph,
                _prismDisplacementTopology, false);
            // The display path must be connected synchronously with the canonical
            // arenas. Chunk residency may later replace the active transform/state,
            // but first-frame visibility cannot depend on an ActiveChunkChanged
            // event having happened after the meshlet buffers were allocated.
            if (_prismWorldRenderer != null &&
                _prismPredictionRenderer?.Meshlets != null)
            {
                string activeChunkId = _submapManager?.ActiveChunk?.chunkId ??
                                       "cone-prism-active";
                _prismWorldRenderer.SetActive(activeChunkId,
                    _prismPredictionRenderer.Meshlets);
                _prismWorldRenderer.RenderVisible = true;
            }
            _prismWorkGraph?.StartGraph(_prismDepthPreprocessor,
                _prismPredictionRenderer, _prismConeClassifier,
                _prismFilmSpawner, _prismPhotometricRefiner,
                _prismFilmUpdater, _prismBoundaryGraph,
                _prismDisplacementTopology, _prismPressureManifoldAtlas,
                _prismMeshletBuilder);
            _prismDepthPreprocessor?.StartProcessing(_prismRigCapture);
            _prismPipelinePrepared = true;
        }

        private void StartPrismCapture()
        {
            PreparePrismPipeline();
            if (_prismCaptureRunning) return;
            _prismRigCapture?.StartCapture();
            _prismCaptureRunning = _prismRigCapture != null &&
                                   _prismRigCapture.IsCapturing;
        }

        /// <summary>
        /// Pauses sensor ingress only. Canonical arenas, prediction targets, GPU
        /// rings and the compiled work graph remain resident, so Stop -> Start is a
        /// true continuation without a render-thread destroy/reallocate storm.
        /// </summary>
        private void PausePrismPipeline()
        {
            _prismRigCapture?.StopCapture();
            _prismCaptureRunning = false;
        }

        /// <summary>
        /// Full graph teardown is reserved for explicit resource release. Ordinary
        /// scan Stop must never call it.
        /// </summary>
        private void ShutdownPrismPipeline()
        {
            PausePrismPipeline();
            if (!_prismPipelinePrepared) return;
            _prismWorkGraph?.StopGraph();
            _prismMeshletBuilder?.StopBuilding();
            _prismDisplacementTopology?.StopUpdating();
            _prismBoundaryGraph?.StopTracking();
            _prismFilmUpdater?.StopUpdating();
            _prismPhotometricRefiner?.StopRefining();
            _prismFilmSpawner?.StopSpawning();
            _prismConeClassifier?.StopClassifying();
            _prismPredictionRenderer?.StopRendering();
            _prismDepthPreprocessor?.StopProcessing();
            _prismPipelinePrepared = false;
        }

        /// <summary>
        /// Toggles between <see cref="StartScanningAsync"/> and <see cref="StopScanning"/>.
        /// The Start path is fire-and-forget here because this is a debug/dev API
        /// (typically wired to a debug-menu button) and the small ~56 ms delay
        /// before integration begins is not worth changing the toggle's signature
        /// for. Production callers should await <see cref="StartScanningAsync"/>
        /// directly so they can sequence UI feedback around the start.
        /// </summary>
        public void ToggleScanning()
        {
            if (ScanLifecycle == ScanLifecycleState.Starting)
            {
                Logger.Info("ToggleScanning ignored while start is already in progress");
                return;
            }
            if (ScanLifecycle == ScanLifecycleState.Running)
                StopScanning();
            else
                ObserveStart(StartScanningAsync());
        }

        private static async void ObserveStart(Task startTask)
        {
            try
            {
                await startTask;
            }
            catch
            {
                // StartScanningCoreAsync already records and logs the complete exception.
                // This observer exists solely to consume faults from UI fire-and-forget.
            }
        }

        /// <summary>True after <see cref="ReleaseScanResources"/> has been called. Cleared when scanning restarts.</summary>
        public bool ScanResourcesReleased => _scanResourcesReleased;

        /// <summary>
        /// Frees the live Cone-PRISM graph and sensor resources. Canonical chunks
        /// already published by residency remain durable on flash.
        /// </summary>
        public void ReleaseScanResources()
        {
            if (_scanResourcesReleased) return;
            StopScanning();
            ShutdownPrismPipeline();

            _depthCapture.ReleaseResources();

            _scanResourcesReleased = true;
            SetRenderMode(ScanRenderMode.None);
            Logger.Info("Cone-PRISM live GPU resources released");
        }

        /// <summary>
        /// Clears the live canonical GPU graph. Durable chunks are unchanged.
        /// </summary>
        public void ClearScan()
        {
            StopScanning();
            ShutdownPrismPipeline();
            _prismWorldRenderer?.SetActive(null, null);
            _scanResourcesReleased = true;
            SetRenderMode(ScanRenderMode.None);
        }

        /// <summary>
        /// Clears the live graph and the active package state. Safe to call at runtime.
        /// File I/O runs on a background thread via ThreadPool to avoid main-thread
        /// stalls and potential SynchronizationContext deadlocks on Quest/IL2CPP.
        /// GPU resources are disposed without immediate re-allocation to avoid
        /// Vulkan stalls when the GPU is still referencing the previous frame's buffers.
        /// Re-initialization happens lazily on the next <see cref="StartScanningAsync"/> or load.
        /// </summary>
        public void ClearAllDataAsync(Action onComplete = null)
        {
            if (_clearInProgress) return;
            _clearInProgress = true;

            try
            {
                StopScanning();

                ShutdownPrismPipeline();
                _prismWorldRenderer?.SetActive(null, null);
                _scanResourcesReleased = true;

                _keyframeCollector?.ClearInMemory();
            }
            catch (Exception e)
            {
                Logger.Error($"ClearAllData sync error: {e.Message}\n{e.StackTrace}");
                _clearInProgress = false;
                return;
            }

            _clearDoneCallback = onComplete;
            _clearDone = true;
        }

        private volatile bool _clearInProgress;
        private volatile bool _clearDone;
        private Action _clearDoneCallback;

        /// <summary>
        /// Switches the active render mode and updates mesh/splat visibility accordingly.
        /// </summary>
        public void SetRenderMode(ScanRenderMode newMode)
        {
            if (!IsModeAvailable(newMode))
                newMode = _scanResourcesReleased
                    ? ScanRenderMode.None
                    : ScanRenderMode.Vertex;
            renderMode = newMode;
            ApplyRenderMode();
            RenderModeChanged?.Invoke(renderMode);
            Logger.Info($"Render mode: {renderMode}");
        }

        /// <summary>
        /// Advances to the next available render mode, skipping modes whose backing
        /// data or module is not present.
        /// </summary>
        public void CycleRenderMode()
        {
            ScanRenderMode[] order =
            {
                ScanRenderMode.Wireframe, ScanRenderMode.Vertex,
                ScanRenderMode.None
            };

            int cur = Array.IndexOf(order, renderMode);
            if (cur < 0) cur = 0;

            for (int i = 1; i <= order.Length; i++)
            {
                var candidate = order[(cur + i) % order.Length];
                if (!IsModeAvailable(candidate)) continue;

                SetRenderMode(candidate);
                return;
            }
        }

        /// <summary>
        /// Returns true if the given render mode's backing module/data is present.
        /// </summary>
        public bool IsModeAvailable(ScanRenderMode mode)
        {
            return mode switch
            {
                ScanRenderMode.Vertex => !_scanResourcesReleased,
                ScanRenderMode.Wireframe => !_scanResourcesReleased,
                ScanRenderMode.None => true,
                _ => false
            };
        }

        /// <summary>Shows or hides the debug menu HUD if present.</summary>
        public void ToggleDebugMenu()
        {
            if (_debugMenu != null) _debugMenu.Toggle();
        }

        /// <summary>
        /// Set a custom camera provider (overrides PassthroughCameraProvider).
        /// </summary>
        public void SetCameraProvider(ICameraProvider provider)
        {
            _customCameraProvider = provider;
        }

        /// <summary>Sets the SceneObjectRegistry (used by persistence load).</summary>
        internal void SetSceneObjectRegistry(SceneObjectRegistry registry)
        {
            _sceneObjectRegistry = registry;
        }

        /// <summary>
        /// Populates the SceneObjectRegistry from live MRUK anchors.
        /// Always replaces stale MRUK entries with fresh tracking-accurate positions.
        /// Safe to call multiple times — AI detections are preserved.
        /// </summary>
        internal void PopulateSceneObjectRegistry()
        {
            _sceneObjectRegistry ??= new SceneObjectRegistry();
            if (_roomUnderstanding == null) return;

            _roomUnderstanding.RefreshRoom();
            _sceneObjectRegistry.RemoveBySource(SceneObjectSource.MRUK);
            _roomUnderstanding.PopulateRegistry(_sceneObjectRegistry);
        }

        private void SubscribeToAnchorsChanged()
        {
            if (_subscribedToAnchorsChanged || _roomUnderstanding == null) return;
            _roomUnderstanding.AnchorsChanged += OnMrukAnchorsChanged;
            _subscribedToAnchorsChanged = true;
        }

        private void UnsubscribeFromAnchorsChanged()
        {
            if (!_subscribedToAnchorsChanged || _roomUnderstanding == null) return;
            _roomUnderstanding.AnchorsChanged -= OnMrukAnchorsChanged;
            _subscribedToAnchorsChanged = false;
        }

        private void OnMrukAnchorsChanged()
        {
            Logger.Info("[RoomScanner] MRUK anchors changed — re-populating registry");
            PopulateSceneObjectRegistry();
        }

        private void SetSafeShaderDefaults()
        {
            Shader.SetGlobalFloat(WireframeID, 0f);
            Shader.SetGlobalFloat(WireThicknessID, wireThickness);
        }
        private static readonly int WireframeID = Shader.PropertyToID("_RSWireframe");
        private static readonly int WireThicknessID = Shader.PropertyToID("_RSWireThickness");

        private void ApplyRenderMode()
        {
            bool prismMeshVisible = renderMode == ScanRenderMode.Vertex
                                 || renderMode == ScanRenderMode.Wireframe;
            if (_prismWorldRenderer != null)
                _prismWorldRenderer.RenderVisible = prismMeshVisible;

            Shader.SetGlobalFloat(WireframeID, renderMode == ScanRenderMode.Wireframe ? 1f : 0f);
            Shader.SetGlobalFloat(WireThicknessID, wireThickness);
        }

        private ICameraProvider GetActiveCameraProvider()
        {
            if (_customCameraProvider != null) return _customCameraProvider;
            return _cameraProvider;
        }

    }
}
