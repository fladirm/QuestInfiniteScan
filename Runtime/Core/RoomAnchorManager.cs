using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Meta.XR.MRUtilityKit;
using UnityEngine;
using UnityEngine.XR;

namespace Genesis.RoomScan
{
    /// <summary>
    /// One coordinate authority generation. It advances on every discontinuity
    /// of the session room frame (anchor replaced, anchor untracked, pose jump,
    /// tracking-origin change, application pause) and is ready only after the
    /// bound anchor has been continuously tracked and stable. Canonical
    /// observations are admitted only inside one ready generation.
    /// </summary>
    internal sealed class CoordinateAuthorityGate
    {
        internal const int RequiredStableFrames = 5;
        internal const float PoseJumpMeters = 0.01f;
        internal const float PoseJumpDegrees = 0.5f;

        private object _anchorIdentity;
        private bool _hasPose;
        private Vector3 _position;
        private Quaternion _rotation;

        internal uint Generation { get; private set; } = 1u;
        internal int StableFrames { get; private set; }
        internal bool IsReady => StableFrames >= RequiredStableFrames;

        internal void Invalidate()
        {
            unchecked
            {
                Generation++;
                if (Generation == 0u) Generation = 1u;
            }
            StableFrames = 0;
            _hasPose = false;
        }

        internal void Sample(bool anchorUsable, object anchorIdentity,
            Vector3 position, Quaternion rotation)
        {
            if (!anchorUsable)
            {
                if (_hasPose || StableFrames > 0 || _anchorIdentity != null)
                    Invalidate();
                _anchorIdentity = null;
                return;
            }

            if (!ReferenceEquals(anchorIdentity, _anchorIdentity))
            {
                _anchorIdentity = anchorIdentity;
                Invalidate();
            }

            // _position/_rotation are the reference pose of this authority
            // generation, not the previous frame. Comparing only consecutive
            // samples lets a long sequence of sub-threshold corrections walk
            // arbitrarily far without ever advancing Generation.
            if (!_hasPose)
            {
                _position = position;
                _rotation = rotation;
                _hasPose = true;
                StableFrames = 1;
                return;
            }

            if (Vector3.Distance(position, _position) > PoseJumpMeters ||
                Quaternion.Angle(rotation, _rotation) > PoseJumpDegrees)
            {
                Invalidate();
                // The sample that exposed the discontinuity is the first
                // reference sample of the new authority generation.
                _anchorIdentity = anchorIdentity;
                _position = position;
                _rotation = rotation;
                _hasPose = true;
                StableFrames = 1;
                return;
            }

            if (StableFrames < RequiredStableFrames) StableFrames++;
        }
    }

    /// <summary>
    /// Room anchor manager. Owns the one persisted session
    /// <see cref="OVRSpatialAnchor"/> and its coordinate authority generation.
    /// Persistent content stores its pose relative to that anchor
    /// (<c>AnchorFromX</c>); there is no world-space relocation matrix.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RoomAnchorManager : MonoBehaviour
    {
        /// <summary>Singleton instance set in <see cref="Awake"/>.</summary>
        public static RoomAnchorManager Instance { get; private set; }

        /// <summary>Raised once when the MRUK room scene has been loaded and the anchor transform is available.</summary>
        public event Action RoomReady;

        /// <summary>True after the MRUK scene has loaded (even if no rooms were found).</summary>
        public bool IsRoomLoaded { get; private set; }

        private MRUK _mruk;
        private Transform _anchorTransform;

        private OVRSpatialAnchor _activeSpatialAnchor;
        private readonly List<OVRSpatialAnchor.UnboundAnchor> _unboundAnchors = new();
        private Task<bool> _ensureSessionAnchorTask;
        private Guid _ensureSessionAnchorUuid;
        private bool _ensureSessionAnchorMayCreate;
        private readonly CoordinateAuthorityGate _authority = new();
        private readonly Dictionary<Guid, OVRSpatialAnchor> _artifactAnchors =
            new();
        private readonly List<XRInputSubsystem> _inputSubsystems = new();
        private XRInputSubsystem _originSubsystem;

        internal const float AnchorReadyTimeoutSeconds = 10f;

        /// <summary>Current room-frame authority generation.</summary>
        internal uint CoordinateAuthorityGeneration => _authority.Generation;

        private void Awake()
        {
            Instance = this;
        }

        private void OnEnable()
        {
            OVRManager.TrackingOriginChangePending +=
                OnTrackingOriginChangePending;
        }

        private void OnDisable()
        {
            OVRManager.TrackingOriginChangePending -=
                OnTrackingOriginChangePending;
            if (_originSubsystem != null)
                _originSubsystem.trackingOriginUpdated -=
                    OnTrackingOriginUpdated;
            _originSubsystem = null;
            _authority.Invalidate();
        }

        private void OnApplicationPause(bool paused)
        {
            _authority.Invalidate();
        }

        private void LateUpdate()
        {
            SubscribeTrackingOriginUpdates();
            OVRSpatialAnchor anchor = _activeSpatialAnchor;
            bool usable = anchor != null && anchor.Localized &&
                anchor.IsTracked && RoomSpaceRoot.Instance != null &&
                RoomSpaceRoot.Instance.CurrentAnchor == anchor.transform;
            _authority.Sample(usable, usable ? anchor : null,
                usable ? anchor.transform.position : Vector3.zero,
                usable ? anchor.transform.rotation : Quaternion.identity);
        }

        /// <summary>
        /// True only while the exact session anchor is localized, tracked,
        /// bound to RoomSpaceRoot and stable inside one authority generation.
        /// </summary>
        internal bool IsCoordinateAuthorityReady(Guid requiredUuid,
            out uint generation)
        {
            generation = _authority.Generation;
            return requiredUuid != Guid.Empty && _activeSpatialAnchor != null &&
                _activeSpatialAnchor.Uuid == requiredUuid && _authority.IsReady;
        }

        private void SubscribeTrackingOriginUpdates()
        {
            if (_originSubsystem != null && _originSubsystem.running) return;
            if (_originSubsystem != null)
                _originSubsystem.trackingOriginUpdated -=
                    OnTrackingOriginUpdated;
            _originSubsystem = null;
            SubsystemManager.GetSubsystems(_inputSubsystems);
            foreach (XRInputSubsystem subsystem in _inputSubsystems)
            {
                if (!subsystem.running) continue;
                _originSubsystem = subsystem;
                subsystem.trackingOriginUpdated += OnTrackingOriginUpdated;
                break;
            }
        }

        private void OnTrackingOriginChangePending(
            OVRManager.TrackingOrigin origin, OVRPose? poseInPreviousSpace)
        {
            Logger.Warning($"Tracking origin change pending ({origin}); " +
                "closing canonical observation admission.");
            _authority.Invalidate();
        }

        private void OnTrackingOriginUpdated(XRInputSubsystem subsystem)
        {
            Logger.Warning("XR tracking origin updated; closing canonical " +
                "observation admission.");
            _authority.Invalidate();
        }

        private IEnumerator Start()
        {
            if (!enabled)
                yield break;

            _mruk = FindAnyObjectByType<MRUK>();
            if (_mruk == null)
            {
                var go = new GameObject("[MRUK]");
                go.transform.SetParent(transform, false);
                _mruk = go.AddComponent<MRUK>();
            }

            _mruk.SceneSettings ??= new MRUK.MRUKSettings();
            _mruk.SceneSettings.DataSource = MRUK.SceneDataSource.Device;
            _mruk.SceneSettings.LoadSceneOnStartup = false;
            _mruk.SceneSettings.EnableHighFidelityScene = true;

            if (_mruk.SceneLoadedEvent != null)
                _mruk.SceneLoadedEvent.AddListener(OnSceneLoaded);

            yield return null;
            _ = _mruk.LoadSceneFromDevice(sceneModel: MRUK.SceneModel.V2FallbackV1);
            Logger.Info("MRUK LoadSceneFromDevice started (V2FallbackV1, awaiting SceneLoadedEvent)...");
        }

        private void OnDestroy()
        {
            if (_mruk != null && _mruk.SceneLoadedEvent != null)
                _mruk.SceneLoadedEvent.RemoveListener(OnSceneLoaded);
            if (Instance == this)
                Instance = null;
        }

        private void OnSceneLoaded()
        {
            if (!enabled)
                return;

            if (_mruk.Rooms == null || _mruk.Rooms.Count == 0)
            {
                Logger.Warning("MRUK loaded but no rooms found");
                IsRoomLoaded = true;
                RoomReady?.Invoke();
                return;
            }

            MRUKRoom room = _mruk.GetCurrentRoom() ?? _mruk.Rooms[0];

            Logger.Info($"MRUK rooms={_mruk.Rooms.Count}, " +
                        $"current room anchors={room.Anchors.Count}");
            foreach (var a in room.Anchors)
                Logger.Info($"  anchor: {a.Label} vol={a.VolumeBounds.HasValue} plane={a.PlaneRect.HasValue}");

            MRUKAnchor floorAnchor = null;
            if (room.FloorAnchors != null && room.FloorAnchors.Count > 0)
                floorAnchor = room.FloorAnchors[0];

            _anchorTransform = floorAnchor != null ? floorAnchor.transform : room.transform;
            if (_anchorTransform == null)
            {
                Logger.Warning("No anchor transform");
                IsRoomLoaded = true;
                RoomReady?.Invoke();
                return;
            }

            if (floorAnchor != null)
                Logger.Info($"Using floor MRUKAnchor '{floorAnchor.name}' " +
                          $"(label={floorAnchor.Label}) pos={_anchorTransform.position}, rot={_anchorTransform.rotation.eulerAngles}");
            else
                Logger.Warning($"No FloorAnchors — falling back to MRUKRoom.transform (pos={_anchorTransform.position})");

            IsRoomLoaded = true;
            Logger.Info($"Room ready — anchor pos={_anchorTransform.position}, rot={_anchorTransform.rotation.eulerAngles}");
            RoomReady?.Invoke();
        }

        // ─────────────────────────────────────────────────────────────
        //  OVRSpatialAnchor API
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Current world pose of the session anchor. Persistent relations are
        /// stored as <c>SpatialAnchorMatrix.inverse * XToWorld</c>.
        /// Returns identity if no spatial anchor is active.
        /// </summary>
        public Matrix4x4 SpatialAnchorMatrix =>
            _activeSpatialAnchor != null
                ? _activeSpatialAnchor.transform.localToWorldMatrix
                : Matrix4x4.identity;

        /// <summary>
        /// Whether a spatial anchor is currently loaded and localized.
        /// </summary>
        public bool HasSpatialAnchor => _activeSpatialAnchor != null;

        /// <summary>
        /// Waits until the active anchor is both localized and currently
        /// tracked. A bound GameObject alone is not a valid observation frame
        /// after an application pause.
        /// </summary>
        internal async Task<bool> WaitForActiveSpatialAnchorReadyAsync(
            float timeoutSeconds = 10f)
        {
            OVRSpatialAnchor anchor = _activeSpatialAnchor;
            if (anchor == null) return false;
            bool ready = await WaitForSpatialAnchorReadyAsync(anchor,
                timeoutSeconds);
            if (!ready)
                Logger.Warning($"Active spatial anchor is not localized and " +
                    $"tracked after {timeoutSeconds:F1}s: {anchor.Uuid}.");
            return ready;
        }

        /// <summary>
        /// Live transform of the active spatial anchor. Parenting under it keeps
        /// content world-locked across tracking corrections.
        ///
        /// <para><b>Parenting under this alone does not make coordinates
        /// persistent.</b> <c>SetParent(anchor, worldPositionStays: true)</c>
        /// preserves the child's world pose and stores the difference as a local
        /// offset, so its local space remains world space plus a constant — and
        /// Unity's world origin is wherever the headset booted, so it means a
        /// different physical place next run. Within one session that is
        /// invisible, which is what makes it a trap. Use
        /// <see cref="RoomSpaceRoot"/>, which holds its own local transform at
        /// identity so that local space genuinely is the anchor's space.</para>
        /// </summary>
        public Transform SpatialAnchorTransform =>
            _activeSpatialAnchor != null ? _activeSpatialAnchor.transform : null;

        /// <summary>UUID of the active spatial anchor, or <see cref="Guid.Empty"/>.</summary>
        public Guid SpatialAnchorUuid =>
            _activeSpatialAnchor != null ? _activeSpatialAnchor.Uuid : Guid.Empty;

        /// <summary>
        /// Localizes the exact persisted anchor owned by an existing session,
        /// or creates one only for an explicit NEW-session request.
        /// </summary>
        internal async Task<bool> EnsureSessionAnchorAsync(Guid requiredUuid,
            bool allowCreate, CancellationToken cancellation = default)
        {
            if (requiredUuid == Guid.Empty && !allowCreate)
            {
                Logger.Error("An existing session requires a persisted room " +
                    "anchor UUID.");
                return false;
            }

            if (requiredUuid != Guid.Empty && _activeSpatialAnchor != null &&
                _activeSpatialAnchor.Uuid == requiredUuid)
            {
                // The SDK refuses to load a UUID that is already bound. The
                // bound instance relocalizes by itself after sleep or a
                // tracking-origin change, so the only correct action is to
                // wait for its exact coordinate authority.
                if (await WaitForCoordinateAuthorityAsync(requiredUuid,
                        AnchorReadyTimeoutSeconds, cancellation))
                    return true;
                if (cancellation.IsCancellationRequested ||
                    _activeSpatialAnchor == null ||
                    _activeSpatialAnchor.Uuid != requiredUuid ||
                    _activeSpatialAnchor.Localized)
                    return false;
                Logger.Warning($"Bound session anchor {requiredUuid:D} lost " +
                    "its locatable component; rebinding the same UUID.");
                DetachChildrenForReparent(_activeSpatialAnchor.transform);
                DestroyImmediate(_activeSpatialAnchor.gameObject);
                _activeSpatialAnchor = null;
            }

            Task<bool> pending = _ensureSessionAnchorTask;
            if (pending != null && !pending.IsCompleted &&
                (_ensureSessionAnchorUuid != requiredUuid ||
                 _ensureSessionAnchorMayCreate != allowCreate))
            {
                await pending;
                return await EnsureSessionAnchorAsync(requiredUuid,
                    allowCreate);
            }
            if (pending == null || pending.IsCompleted)
            {
                pending = EnsureSessionAnchorCoreAsync(requiredUuid,
                    allowCreate);
                _ensureSessionAnchorTask = pending;
                _ensureSessionAnchorUuid = requiredUuid;
                _ensureSessionAnchorMayCreate = allowCreate;
            }

            try
            {
                return await pending;
            }
            finally
            {
                if (ReferenceEquals(_ensureSessionAnchorTask, pending))
                {
                    _ensureSessionAnchorTask = null;
                    _ensureSessionAnchorUuid = Guid.Empty;
                    _ensureSessionAnchorMayCreate = false;
                }
            }
        }

        private async Task<bool> EnsureSessionAnchorCoreAsync(Guid requiredUuid,
            bool allowCreate)
        {
            if (requiredUuid != Guid.Empty)
            {
                Matrix4x4? localized = await LoadSpatialAnchorAsync(requiredUuid);
                if (!localized.HasValue) return false;
            }
            else
            {
                if (!allowCreate) return false;
                Camera camera = Camera.main;
                Vector3 position = camera != null
                    ? camera.transform.position
                    : (_anchorTransform != null
                        ? _anchorTransform.position : Vector3.zero);
                var created = await CreateAndSaveSpatialAnchorAsync(position,
                    Quaternion.identity);
                if (!created.HasValue) return false;
            }
            if (RoomSpaceRoot.Instance == null)
            {
                Logger.Error("Spatial anchor exists, but RoomSpaceRoot is missing.");
                return false;
            }
            return _activeSpatialAnchor != null &&
                await RoomSpaceRoot.WaitForAnchorBindAsync(
                    _activeSpatialAnchor.transform) &&
                await WaitForCoordinateAuthorityAsync(_activeSpatialAnchor.Uuid,
                    AnchorReadyTimeoutSeconds, default);
        }

        /// <summary>
        /// Waits in rendered frames (a sleeping headset renders none) until the
        /// exact anchor owns a ready coordinate authority generation.
        /// </summary>
        internal async Task<bool> WaitForCoordinateAuthorityAsync(
            Guid requiredUuid, float timeoutSeconds,
            CancellationToken cancellation)
        {
            float waited = 0f;
            while (!cancellation.IsCancellationRequested && this != null)
            {
                if (IsCoordinateAuthorityReady(requiredUuid, out _)) return true;
                if (waited >= timeoutSeconds) break;
                await Task.Yield();
                waited += Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            }
            return this != null && !cancellation.IsCancellationRequested &&
                IsCoordinateAuthorityReady(requiredUuid, out _);
        }

        /// <summary>
        /// Creates an <see cref="OVRSpatialAnchor"/> at the given world pose, waits for
        /// creation, persists it, and returns the UUID + localToWorld matrix.
        /// Falls back to MRUK anchor position if <paramref name="position"/> is default.
        /// </summary>
        private async Task<(Guid uuid, Matrix4x4 matrix)?> CreateAndSaveSpatialAnchorAsync(
            Vector3 position, Quaternion rotation)
        {
            if (position == Vector3.zero && rotation == Quaternion.identity && _anchorTransform != null)
            {
                position = _anchorTransform.position;
                rotation = _anchorTransform.rotation;
            }

            var go = new GameObject("[SpatialAnchor]");
            go.transform.SetPositionAndRotation(position, rotation);
            var anchor = go.AddComponent<OVRSpatialAnchor>();

            // Wait for async creation (up to 5s)
            float timeout = 5f;
            float elapsed = 0f;
            while (!anchor.Created && elapsed < timeout)
            {
                await Task.Yield();
                elapsed += Time.unscaledDeltaTime;
            }

            if (!anchor.Created)
            {
                Logger.Error("Spatial anchor creation timed out");
                Destroy(go);
                return null;
            }

            Logger.Info($"Spatial anchor created: {anchor.Uuid}, pos={position}");

            var saveResult = await anchor.SaveAnchorAsync();
            if (!saveResult.Success)
            {
                Logger.Error($"Spatial anchor save failed: {saveResult.Status}");
                Destroy(go);
                return null;
            }

            Logger.Info($"Spatial anchor persisted: {anchor.Uuid}");

            if (!await WaitForSpatialAnchorReadyAsync(anchor, 10f))
            {
                Logger.Error("New spatial anchor did not become localized " +
                    "and tracked.");
                Destroy(go);
                return null;
            }
            await StabilizeAnchorTransform(anchor.transform);

            if (_activeSpatialAnchor != null && _activeSpatialAnchor.gameObject != go)
            {
                // Consumers (game-side WorldRoot, refined-mesh holder, etc.)
                // commonly parent anchor-tracked content under the active
                // [SpatialAnchor] GO so it stays glued to the room across
                // drift correction. Destroying the GO with those children
                // still attached recursively destroys them too — the
                // gameplay scene loses its world root and the player's
                // UI vanishes mid-rescan. Detach first with world pose
                // preserved so the children survive and a downstream
                // adopter (e.g. WorldRoot.Update polling
                // SpatialAnchorTransform) can reparent them under the
                // new anchor on the next frame.
                DetachChildrenForReparent(_activeSpatialAnchor.transform);
                Destroy(_activeSpatialAnchor.gameObject);
            }
            _activeSpatialAnchor = anchor;

            Matrix4x4 matrix = anchor.transform.localToWorldMatrix;
            return (anchor.Uuid, matrix);
        }

        /// <summary>
        /// Loads exactly one previously persisted spatial anchor by UUID,
        /// localizes it, and returns its current localToWorld matrix. Returns
        /// null on failure; existing-session callers must fail closed.
        /// </summary>
        private async Task<Matrix4x4?> LoadSpatialAnchorAsync(Guid uuid)
        {
            if (_artifactAnchors.TryGetValue(uuid, out OVRSpatialAnchor shared) &&
                shared != null)
            {
                // An artifact view already bound this UUID. Promote that one
                // binding; the SDK would skip a second load of the same UUID.
                _artifactAnchors.Remove(uuid);
                if (_activeSpatialAnchor != null &&
                    _activeSpatialAnchor != shared)
                {
                    DetachChildrenForReparent(_activeSpatialAnchor.transform);
                    Destroy(_activeSpatialAnchor.gameObject);
                }
                _activeSpatialAnchor = shared;
                Logger.Info($"Promoted artifact binding of anchor {uuid:D} " +
                    "to the session anchor.");
                return shared.transform.localToWorldMatrix;
            }
            _artifactAnchors.Remove(uuid);
            Logger.Info($"Loading spatial anchor {uuid}...");

            _unboundAnchors.Clear();
            var loadResult = await OVRSpatialAnchor.LoadUnboundAnchorsAsync(
                new[] { uuid }, _unboundAnchors);

            if (!loadResult.Success || _unboundAnchors.Count == 0)
            {
                Logger.Warning($"Spatial anchor load failed: {loadResult.Status}, " +
                                 $"count={_unboundAnchors.Count}.");
                return null;
            }

            var unbound = _unboundAnchors[0];

            bool localized = await unbound.LocalizeAsync();
            if (!localized && !unbound.Localized)
            {
                // Poll for localization (up to 10s)
                float timeout = 10f;
                float elapsed = 0f;
                while (!unbound.Localized && elapsed < timeout)
                {
                    await Task.Yield();
                    elapsed += Time.unscaledDeltaTime;
                }
                if (!unbound.Localized)
                {
                    Logger.Warning("Spatial anchor localization timed out.");
                    return null;
                }
            }

            // Bind to a new OVRSpatialAnchor GO
            var go = new GameObject($"[SpatialAnchor-{uuid:N}]");
            var anchor = go.AddComponent<OVRSpatialAnchor>();
            unbound.BindTo(anchor);

            if (anchor.Uuid != uuid)
            {
                Logger.Error($"Spatial anchor UUID mismatch: requested={uuid}, " +
                    $"bound={anchor.Uuid}.");
                Destroy(go);
                return null;
            }

            Logger.Info($"Spatial anchor localized: {uuid}, pos={anchor.transform.position}");

            if (!await WaitForSpatialAnchorReadyAsync(anchor, 10f))
            {
                Logger.Warning("Bound spatial anchor did not become tracked.");
                Destroy(go);
                return null;
            }
            await StabilizeAnchorTransform(anchor.transform);

            if (_activeSpatialAnchor != null && _activeSpatialAnchor.gameObject != go)
            {
                // See note in CreateAndSaveSpatialAnchorAsync: detach
                // children first so anchor-tracked scene content (game-side
                // WorldRoot, refined-mesh holder, etc.) survives the
                // destroy and can be re-adopted under the new anchor on
                // the next frame.
                DetachChildrenForReparent(_activeSpatialAnchor.transform);
                Destroy(_activeSpatialAnchor.gameObject);
            }
            _activeSpatialAnchor = anchor;

            return anchor.transform.localToWorldMatrix;
        }

        /// <summary>
        /// Localizes an anchor for read-only artifact presentation without
        /// replacing the scanner's active anchor or rebinding RoomSpaceRoot.
        /// The caller owns and must destroy the returned object only when
        /// <c>owned</c> is true.
        /// </summary>
        internal async Task<(Transform transform, bool owned)?>
            LocalizeArtifactAnchorAsync(Guid uuid)
        {
            if (uuid == Guid.Empty) return null;
            if (_activeSpatialAnchor != null &&
                _activeSpatialAnchor.Uuid == uuid)
            {
                if (!await WaitForSpatialAnchorReadyAsync(
                        _activeSpatialAnchor, 10f))
                {
                    Logger.Warning($"Active artifact spatial anchor is not " +
                        $"tracked: {uuid}.");
                    return null;
                }
                return (_activeSpatialAnchor.transform, false);
            }
            if (_artifactAnchors.TryGetValue(uuid, out OVRSpatialAnchor existing))
            {
                if (existing != null &&
                    await WaitForSpatialAnchorReadyAsync(existing, 10f))
                    return (existing.transform, true);
                if (existing == null) _artifactAnchors.Remove(uuid);
                else return null;
            }

            var unboundAnchors =
                new List<OVRSpatialAnchor.UnboundAnchor>();
            var loadResult = await OVRSpatialAnchor.LoadUnboundAnchorsAsync(
                new[] { uuid }, unboundAnchors);
            if (!loadResult.Success || unboundAnchors.Count == 0)
            {
                Logger.Warning($"Artifact spatial anchor load failed: " +
                    $"{loadResult.Status}, uuid={uuid}, " +
                    $"count={unboundAnchors.Count}.");
                return null;
            }

            OVRSpatialAnchor.UnboundAnchor unbound = unboundAnchors[0];
            bool localized = await unbound.LocalizeAsync();
            if (!localized && !unbound.Localized)
            {
                float timeout = 10f;
                float elapsed = 0f;
                while (!unbound.Localized && elapsed < timeout)
                {
                    await Task.Yield();
                    elapsed += Time.unscaledDeltaTime;
                }
                if (!unbound.Localized)
                {
                    Logger.Warning($"Artifact spatial anchor localization " +
                        $"timed out: {uuid}.");
                    return null;
                }
            }

            var go = new GameObject($"[ArtifactSpatialAnchor-{uuid:N}]");
            var anchor = go.AddComponent<OVRSpatialAnchor>();
            unbound.BindTo(anchor);
            if (!await WaitForSpatialAnchorReadyAsync(anchor, 10f))
            {
                Logger.Warning($"Artifact spatial anchor did not become " +
                    $"tracked: {uuid}.");
                Destroy(go);
                return null;
            }
            await StabilizeAnchorTransform(anchor.transform);
            _artifactAnchors[uuid] = anchor;
            Logger.Info($"Artifact spatial anchor localized without " +
                $"changing scan authority: {uuid}.");
            return (anchor.transform, true);
        }

        /// <summary>
        /// Releases an artifact-owned binding immediately so the UUID can be
        /// bound again in the same frame. A binding already promoted to the
        /// session anchor is never destroyed.
        /// </summary>
        internal void ReleaseArtifactAnchor(Transform anchorTransform)
        {
            if (anchorTransform == null) return;
            OVRSpatialAnchor anchor =
                anchorTransform.GetComponent<OVRSpatialAnchor>();
            if (anchor == null || anchor == _activeSpatialAnchor) return;
            if (!_artifactAnchors.TryGetValue(anchor.Uuid,
                    out OVRSpatialAnchor registered) || registered != anchor)
                return;
            _artifactAnchors.Remove(anchor.Uuid);
            DetachChildrenForReparent(anchor.transform);
            DestroyImmediate(anchor.gameObject);
        }

        /// <summary>
        /// Erases a spatial anchor from persistent storage by UUID.
        /// Does not require the anchor to be loaded.
        /// </summary>
        public async Task<bool> EraseSpatialAnchorAsync(Guid uuid)
        {
            Logger.Info($"Erasing spatial anchor {uuid}...");
            var result = await OVRSpatialAnchor.EraseAnchorsAsync(
                null, new[] { uuid });

            if (result.Success)
                Logger.Info($"Spatial anchor erased: {uuid}");
            else
                Logger.Warning($"Spatial anchor erase failed: {result.Status}");

            return result.Success;
        }

        /// <summary>
        /// Reparent every direct child of <paramref name="oldAnchor"/> to the
        /// scene root with world pose preserved. Called immediately before
        /// destroying a superseded <c>[SpatialAnchor]</c> GameObject so that
        /// anchor-tracked content parented underneath (e.g. the game-side
        /// <c>WorldRoot</c>, refined-mesh holder GameObjects) is not
        /// recursively destroyed by Unity's child-cascade. Once detached,
        /// any consumer polling <see cref="SpatialAnchorTransform"/> (the
        /// canonical pattern: <c>WorldRoot.Update</c>) will reparent them
        /// under the new active anchor on the next frame, also with world
        /// pose preserved — the player sees no visible jump.
        ///
        /// <para>
        /// Iterates index 0 in a loop because <c>SetParent(null, ...)</c>
        /// mutates the child collection, so a forward-index <c>for</c> would
        /// skip every other element.
        /// </para>
        /// </summary>
        private static void DetachChildrenForReparent(Transform oldAnchor)
        {
            if (oldAnchor == null) return;
            while (oldAnchor.childCount > 0)
            {
                var child = oldAnchor.GetChild(0);
                child.SetParent(null, worldPositionStays: true);
            }
        }

        /// <summary>
        /// Waits for an anchor transform to stabilize (5 consecutive frames with &lt; 1mm movement).
        /// </summary>
        private static async Task StabilizeAnchorTransform(Transform t)
        {
            int stableFrames = 0;
            const int required = 5;
            const int maxPolls = 60;
            Vector3 prevPos = t.position;

            for (int i = 0; i < maxPolls && stableFrames < required; i++)
            {
                await Task.Yield();
                float delta = Vector3.Distance(prevPos, t.position);
                if (delta < 0.001f)
                    stableFrames++;
                else
                    stableFrames = 0;
                prevPos = t.position;
            }
        }

        private static async Task<bool> WaitForSpatialAnchorReadyAsync(
            OVRSpatialAnchor anchor, float timeoutSeconds)
        {
            float deadline = Time.realtimeSinceStartup +
                Mathf.Max(0.1f, timeoutSeconds);
            while (anchor != null && Time.realtimeSinceStartup < deadline)
            {
                if (anchor.Localized && anchor.IsTracked)
                    return true;
                await Task.Yield();
            }
            return anchor != null && anchor.Localized && anchor.IsTracked;
        }
    }
}
