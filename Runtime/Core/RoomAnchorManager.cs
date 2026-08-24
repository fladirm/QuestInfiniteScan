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
        /// <inheritdoc />
        public string ModuleName => "Room Anchor";

        /// <inheritdoc />
        public void OnModuleInitialize(RoomScanner scanner) { }

        /// <summary>Singleton instance set in <see cref="Awake"/>.</summary>
        public static RoomAnchorManager Instance { get; private set; }

        private OVRSpatialAnchor _activeSpatialAnchor;
        private readonly List<OVRSpatialAnchor.UnboundAnchor> _unboundAnchors = new();

        private void Awake()
        {
            Instance = this;
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

            // Wait a few frames for transform to stabilize
            await StabilizeAnchorTransform(anchor.transform);

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

            Logger.Info($"Spatial anchor localized: {uuid}, pos={anchor.transform.position}");

            await StabilizeAnchorTransform(anchor.transform);

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

            return anchor.transform.localToWorldMatrix;
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
    }
}
