using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Genesis.RoomScan
{
    /// <summary>
    /// Owns the localized <see cref="OVRSpatialAnchor"/> that defines the stable
    /// scan-space frame. It does not depend on a Meta room model: arbitrary rooms,
    /// corridors, outdoor spaces and later large-world anchor sets use the same
    /// anchor-local carrier coordinates. Computes relocation matrices via
    /// <c>R = A_now * Inv(A_create)</c> without giving anchors physical identity in
    /// the canonical carrier.
    /// </summary>
    [DisallowMultipleComponent]
    public class RoomAnchorManager : MonoBehaviour, IRoomScanModule
    {
        private const string PersistedAnchorUuidKey =
            "Genesis.RoomScan.RoomAnchorUuid.v1";

        /// <inheritdoc />
        public string ModuleName => "Room Anchor";

        /// <inheritdoc />
        public void OnModuleInitialize(RoomScanner scanner) { }

        /// <summary>Singleton instance set in <see cref="Awake"/>.</summary>
        public static RoomAnchorManager Instance { get; private set; }

        private OVRSpatialAnchor _activeSpatialAnchor;
        private readonly List<OVRSpatialAnchor.UnboundAnchor> _unboundAnchors = new();
        private bool _trackingReady;
        private uint _trackingValidationGeneration;

        internal static bool IsStablePoseStep(Vector3 previousPosition,
            Quaternion previousRotation, Vector3 currentPosition,
            Quaternion currentRotation) =>
            Vector3.Distance(previousPosition, currentPosition) < 0.001f &&
            Quaternion.Angle(previousRotation, currentRotation) < 0.1f;

        private void Awake()
        {
            Instance = this;
        }

        private void OnEnable()
        {
            OVRManager.TrackingLost += HandleTrackingLost;
            OVRManager.TrackingAcquired += HandleTrackingAcquired;
        }

        private void OnDisable()
        {
            OVRManager.TrackingLost -= HandleTrackingLost;
            OVRManager.TrackingAcquired -= HandleTrackingAcquired;
            _trackingReady = false;
            ++_trackingValidationGeneration;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        // ─────────────────────────────────────────────────────────────
        //  Spatial-anchor relocation
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// One-shot relocation: <c>R = A_now * Inv(A_save)</c>.
        /// </summary>
        public static Matrix4x4 ComputeRelocationMatrix(Matrix4x4 anchorNow, Matrix4x4 anchorAtSave)
        {
            Matrix4x4 reloc = anchorNow * anchorAtSave.inverse;
            Logger.Info($"ComputeRelocation: R = A_now * Inv(A_save)\n" +
                      $"  A_save col3(pos): {anchorAtSave.GetColumn(3)}\n" +
                      $"  A_now  col3(pos): {anchorNow.GetColumn(3)}\n" +
                      $"  R      col3(pos): {reloc.GetColumn(3)}");
            return reloc;
        }

        // ─────────────────────────────────────────────────────────────
        //  OVRSpatialAnchor API
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Current spatial anchor localization matrix. Valid after
        /// <see cref="CreateAndSaveSpatialAnchorAsync"/> or <see cref="LoadSpatialAnchorAsync"/>.
        /// Returns identity if no spatial anchor is active.
        ///
        /// <para><b>Relocating anchored local data.</b> Store this
        /// alongside the local frame and on load multiply
        /// by <c>ComputeRelocationMatrix(SpatialAnchorMatrix, stored)</c> to
        /// bring it into the current session's world frame. This is how the
        /// exact carrier readouts survive a restart. For content represented by a
        /// transform, prefer parenting through
        /// Anything you can parent should use <see cref="RoomSpaceRoot"/>
        /// instead and store plain local coordinates.</para>
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
        /// True only while the persisted room anchor is localized and tracked.
        /// Scanner admission pauses when tracking is lost; immutable readout may
        /// continue rendering in the last published room frame.
        /// </summary>
        public bool IsSpatialAnchorReady => _trackingReady &&
            _activeSpatialAnchor != null && _activeSpatialAnchor.Localized &&
            _activeSpatialAnchor.IsTracked;

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

        public bool TryGetPersistedSpatialAnchorUuid(out Guid uuid)
        {
            string encoded = PlayerPrefs.GetString(PersistedAnchorUuidKey,
                string.Empty);
            if (string.IsNullOrEmpty(encoded))
            {
                uuid = Guid.Empty;
                return false;
            }
            if (!TryDecodeSpatialAnchorUuid(encoded, out uuid))
                throw new InvalidOperationException(
                    "The persisted room-anchor UUID is corrupt; refusing to " +
                    "re-anchor an existing carrier to a different place.");
            return true;
        }

        internal static string EncodeSpatialAnchorUuid(Guid uuid)
        {
            if (uuid == Guid.Empty)
                throw new ArgumentOutOfRangeException(nameof(uuid));
            return uuid.ToString("D");
        }

        internal static bool TryDecodeSpatialAnchorUuid(string encoded,
            out Guid uuid) => Guid.TryParseExact(encoded, "D", out uuid) &&
                uuid != Guid.Empty;

        /// <summary>
        /// Creates an <see cref="OVRSpatialAnchor"/> at the given world pose, waits for
        /// creation, persists it, and returns the UUID + localToWorld matrix.
        /// </summary>
        public async Task<(Guid uuid, Matrix4x4 matrix)?> CreateAndSaveSpatialAnchorAsync(
            Vector3 position, Quaternion rotation)
        {
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

            PersistSpatialAnchorUuid(anchor.Uuid);

            // A saved handle is not yet a usable room frame. Wait until Meta
            // supplies a tracked pose and then require one stable pose epoch.
            if (!await WaitForStableTrackedAnchor(anchor))
            {
                Logger.Error("Spatial anchor did not reach a stable tracked pose.");
                Destroy(go);
                return null;
            }

            if (_activeSpatialAnchor != null && _activeSpatialAnchor.gameObject != go)
            {
                // Consumers of the room-local frame
                // commonly parent anchor-tracked content under the active
                // [SpatialAnchor] GO so it stays glued to the room across
                // drift correction. Destroying the GO with those children
                // still attached recursively destroys them too — the
                // gameplay scene loses its anchored root and the player's
                // UI vanishes mid-rescan. Detach first with world pose
                // preserved so the children survive and a downstream
                // adopter (for example RoomSpaceRoot.Update polling
                // SpatialAnchorTransform) can reparent them under the
                // new anchor on the next frame.
                DetachChildrenForReparent(_activeSpatialAnchor.transform);
                Destroy(_activeSpatialAnchor.gameObject);
            }
            _activeSpatialAnchor = anchor;
            _trackingReady = true;

            Matrix4x4 matrix = anchor.transform.localToWorldMatrix;
            return (anchor.Uuid, matrix);
        }

        /// <summary>
        /// Loads a previously persisted spatial anchor by UUID, localizes it, and returns
        /// the anchor's current localToWorld matrix. Returns null on failure.
        /// </summary>
        public async Task<Matrix4x4?> LoadSpatialAnchorAsync(Guid uuid)
        {
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

            OVRSpatialAnchor.UnboundAnchor unbound = default;
            bool found = false;
            for (int index = 0; index < _unboundAnchors.Count; ++index)
            {
                if (_unboundAnchors[index].Uuid != uuid)
                    continue;
                unbound = _unboundAnchors[index];
                found = true;
                break;
            }
            if (!found)
            {
                Logger.Warning("Spatial anchor load returned no exact UUID match.");
                return null;
            }

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
            if (unbound.TryGetPose(out Pose pose))
                go.transform.SetPositionAndRotation(pose.position,
                    pose.rotation);
            var anchor = go.AddComponent<OVRSpatialAnchor>();
            unbound.BindTo(anchor);

            Logger.Info($"Spatial anchor localized: {uuid}, pos={anchor.transform.position}");

            if (!await WaitForStableTrackedAnchor(anchor))
            {
                Logger.Warning("Loaded spatial anchor did not reach a stable " +
                    "tracked pose.");
                Destroy(go);
                return null;
            }

            if (_activeSpatialAnchor != null && _activeSpatialAnchor.gameObject != go)
            {
                // See note in CreateAndSaveSpatialAnchorAsync: detach
                // children first so anchor-tracked scene content survives the
                // destroy and can be re-adopted under the new anchor on
                // the next frame.
                DetachChildrenForReparent(_activeSpatialAnchor.transform);
                Destroy(_activeSpatialAnchor.gameObject);
            }
            _activeSpatialAnchor = anchor;
            _trackingReady = true;
            PersistSpatialAnchorUuid(uuid);

            return anchor.transform.localToWorldMatrix;
        }

        public async Task<bool> WaitForSpatialAnchorReadyAsync()
        {
            OVRSpatialAnchor anchor = _activeSpatialAnchor;
            if (anchor == null)
                return false;
            if (!await WaitForStableTrackedAnchor(anchor) ||
                anchor != _activeSpatialAnchor)
                return false;
            _trackingReady = true;
            return true;
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
            {
                Logger.Info($"Spatial anchor erased: {uuid}");
                if (TryGetPersistedSpatialAnchorUuid(out Guid persisted) &&
                    persisted == uuid)
                {
                    PlayerPrefs.DeleteKey(PersistedAnchorUuidKey);
                    PlayerPrefs.Save();
                }
            }
            else
                Logger.Warning($"Spatial anchor erase failed: {result.Status}");

            return result.Success;
        }

        /// <summary>
        /// Reparent every direct child of <paramref name="oldAnchor"/> to the
        /// scene root with world pose preserved. Called immediately before
        /// destroying a superseded <c>[SpatialAnchor]</c> GameObject so that
        /// anchor-tracked content parented underneath is not
        /// recursively destroyed by Unity's child-cascade. Once detached,
        /// any consumer polling <see cref="SpatialAnchorTransform"/> (the
        /// canonical pattern: <c>RoomSpaceRoot.Update</c>) will reparent them
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

        private static async Task<bool> WaitForTrackedAnchor(
            OVRSpatialAnchor anchor)
        {
            const float timeout = 10f;
            float elapsed = 0f;
            while (anchor != null &&
                (!anchor.Localized || !anchor.IsTracked) && elapsed < timeout)
            {
                await Task.Yield();
                elapsed += Time.unscaledDeltaTime;
            }
            return anchor != null && anchor.Localized && anchor.IsTracked;
        }

        /// <summary>
        /// Establishes one room-frame epoch only after Meta reports a tracked
        /// anchor and its translated and rotated pose is stable for five complete
        /// frames. Tracking can become true before the post-resume pose settles;
        /// canonical admission must not observe that transient coordinate frame.
        /// </summary>
        private static async Task<bool> WaitForStableTrackedAnchor(
            OVRSpatialAnchor anchor)
        {
            if (!await WaitForTrackedAnchor(anchor))
                return false;

            const int requiredStableFrames = 5;
            const int maximumPolls = 180;
            int stableFrames = 0;
            Vector3 previousPosition = anchor.transform.position;
            Quaternion previousRotation = anchor.transform.rotation;
            for (int poll = 0;
                anchor != null && poll < maximumPolls &&
                stableFrames < requiredStableFrames; ++poll)
            {
                await Task.Yield();
                if (anchor == null || !anchor.Localized || !anchor.IsTracked)
                {
                    stableFrames = 0;
                    continue;
                }

                Vector3 currentPosition = anchor.transform.position;
                Quaternion currentRotation = anchor.transform.rotation;
                if (IsStablePoseStep(previousPosition, previousRotation,
                        currentPosition, currentRotation))
                    ++stableFrames;
                else
                    stableFrames = 0;
                previousPosition = currentPosition;
                previousRotation = currentRotation;
            }
            return anchor != null && anchor.Localized && anchor.IsTracked &&
                stableFrames == requiredStableFrames;
        }

        private static void PersistSpatialAnchorUuid(Guid uuid)
        {
            PlayerPrefs.SetString(PersistedAnchorUuidKey,
                EncodeSpatialAnchorUuid(uuid));
            PlayerPrefs.Save();
        }

        private void HandleTrackingLost()
        {
            _trackingReady = false;
            ++_trackingValidationGeneration;
            Logger.Warning("Spatial anchor tracking lost; scan admission paused " +
                "while immutable FRONT remains available.");
        }

        private void HandleTrackingAcquired()
        {
            uint generation = ++_trackingValidationGeneration;
            RevalidateTrackingAfterResume(generation);
        }

        private async void RevalidateTrackingAfterResume(uint generation)
        {
            OVRSpatialAnchor anchor = _activeSpatialAnchor;
            if (anchor == null)
                return;
            bool ready = await WaitForStableTrackedAnchor(anchor);
            if (generation != _trackingValidationGeneration ||
                anchor != _activeSpatialAnchor)
                return;
            _trackingReady = ready;
            if (ready)
                Logger.Info("Spatial anchor stable tracking epoch reacquired: " +
                    "uuid=" + anchor.Uuid + " pos=" +
                    anchor.transform.position + " rotation=" +
                    anchor.transform.rotation.eulerAngles + ".");
            else
                Logger.Warning("Spatial anchor failed to reacquire; scan " +
                    "admission remains paused.");
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                _trackingReady = false;
                ++_trackingValidationGeneration;
            }
            else
                HandleTrackingAcquired();
            Logger.Info("Spatial anchor application pause=" + paused +
                " uuid=" + SpatialAnchorUuid + " ready=" +
                IsSpatialAnchorReady + ".");
        }
    }
}
