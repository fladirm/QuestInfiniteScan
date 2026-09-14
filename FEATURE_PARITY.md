# FEATURE_PARITY

C00 deliverable. Authority: `FINALSCAN_V4_CONTRACT.md` §1.1, §18, §24 (C22–C29). Source of truth for the SimpleScan column: local audit of `/mnt/aidisk/prace/simplescan` (branch `fix/merkaba-runtime-root-causes`, HEAD `651cea6`), 2026-09-14. Paths are relative to that checkout.

Rule (contract C29): this file must contain **zero unexplained missing capabilities**. Every row is either PLANNED with a FinalScan cut, or explicitly OUT OF SCOPE with a decision reference.

Binding vocabulary (contract §9.3, §18.4):

* `A+S` = `AnchorID + SurfaceID` (surface-attached)
* `A+L` = `AnchorID + local anchor pose` (free-floating in an anchor frame)
* `chart` = `SurfaceID + chart (u,v)` on the SurfaceAtlas
* `A+T` = `AnchorID + transform` (model instance)
* `session` = scan project manifest, not spatial

## 1. Scan

| Capability | SimpleScan files | SimpleScan binding | Known problems | FinalScan binding | Cut | Status |
|---|---|---|---|---|---|---|
| START / STOP scan | `Runtime/Core/RoomScanner.cs` (integrationHz=20, :29-31, :399-484) | observations in room-space lattice under one anchor | 6.2 Hz effective vs 20 Hz target (`errors.md`); complex quiesce protocol (`QuiesceCoreAsync` :1062) | capture profile state machine (§3.2) + scheduler P1 SCAN | C22 | PLANNED |
| FINE refine brush (right trigger) | `RoomScanner.cs:135-160, 838-1030`; `Runtime/Core/FineBrushDescriptor.cs` (controller-ray cylinder: cursor, normal, axis, radius, length, op) | world → grid via `GridToWorldMatrix`; tiles resolved on GPU | cursor kernel `[numthreads(1,1,1)]` + AsyncGPUReadback; contract F3 parallel kernel never done | DETAIL request = local scheduler priority change (§18.2), cursor from FRONT surfel hit | C23 | PLANNED |
| ERASE brush (right grip) | same files; native `JobKind.FineErase` pipelines 44-48; `EraseFineTiles` | same | erase retired at GPU fences only since `de79665` | canonical deletion authority (§18.3): surfel `removed` flag, render/appearance/export retire | C23 | PLANNED |
| Opacity slider | `Runtime/UI/DebugMenuController.cs` | — | — | render uniform | C22 | PLANNED |
| Environment occlusion toggle | `Runtime/Merkaba/MerkabaRenderFeature.cs` keyword `M8_ENVIRONMENT_OCCLUSION` (`6a4f527`) | — | — | Env Depth occlusion in surfel draw pass | C22 | PLANNED |
| Depth-hit cursor (brush target) | `Runtime/Core/DepthCapture.cs:840-940` `TryUpdateFineSurfaceTarget` | Env Depth hit | readback latency | FRONT surfel ray hit (GPU), Env Depth fallback | C23 | PLANNED |

## 2. Sessions and persistence

| Capability | SimpleScan files | SimpleScan binding | Known problems | FinalScan binding | Cut | Status |
|---|---|---|---|---|---|---|
| NEW / OPEN / SAVE / SAVE AS / RENAME / DELETE | `Runtime/Merkaba/MerkabaPersistence.cs`, `MerkabaSessionCatalog.cs:491-730`, `RoomScanner.cs:493-655` | session ↔ one `OVRSpatialAnchor` UUID; checkpoint stores `AnchorAtSave`; load relocates via `RelocateForLoadedAnchor` | fail-closed on anchor mismatch; OPEN loads whole grid; `IsBusy` deadlock after exception (`55f6877`) | session manifest + AnchorGraph; OS anchor UUID per Anchor is an observation, not origin (§10); OPEN loads manifest + local readout sphere only (§17) | C26 | PLANNED |
| Autosave (continuous) | overlay journal `merkaba-live.m8log` written by GPU writeback (`MerkabaGrid.Storage.cs:591`), 50 ms storage pump | — | not a user-visible feature; journal only | journal → checkpoint → compaction, continuous, crash-safe (§17). Exposed in UI as "last saved" state | C26 | PLANNED |
| Crash recovery ("Recovered Scan") | `MerkabaSessionCatalog.cs:609-636` `RecoverMissingMetadata` | checkpoint + overlay replay | — | same principle, COW durable pages | C26 | PLANNED |
| Durable file publish | `MerkabaPersistence.cs:432` `MerkabaFilePublishing` (`.tmp` + `File.Replace`) | — | — | ported pattern (see MIGRATION_LEDGER) | C26 | PLANNED |

## 3. Export

| Capability | SimpleScan files | SimpleScan binding | Known problems | FinalScan binding | Cut | Status |
|---|---|---|---|---|---|---|
| GLB export | `Runtime/Merkaba/MerkabaExporter.cs` → `MerkabaExportShell.cs` → `MerkabaExportMembrane.cs` → `MerkabaGlbWriter.cs`; output `persistentDataPath/MerkabaScan/exports/QuestMerkabaScan.glb` | walks SSD tiles; spatial binding `{AnchorUuid, AnchorFromPackage}` | requires localizable session anchor (:379-388) | AnchorGraph-optimized surfel sheets → adaptive mesh → UV → keyframe bake → GLB (§18.5); streaming for large scans | C27, C28 | PLANNED |
| SAF "save to" picker | `Runtime/Plugins/Android/MerkabaPackagePicker.java` (280 lines) | — | — | ported, renamed (MIGRATION_LEDGER) | C28 | PLANNED |
| 3D Tiles ZIP (128/256 MB leaves) | `Runtime/Merkaba/MerkabaTilesetWriter.cs` (605 lines) | same membrane oracle | empty-leaf invariant throws (`0011dcf`) | tileset from export mesh, per-Anchor tiles | C28 | PLANNED |
| Browser viewer (Three.js) | `Runtime/Resources/Merkaba/QuestMerkabaScanViewer.txt` (+ licenses) | — | — | ported as static asset, rebranded | C28 | PLANNED |
| GLB validation tooling | `Tools/gltf/verify_merkaba_glb.mjs`, `validate_merkaba_glb.sh` | — | — | ported to `Tools/gltf/` | C28 | PLANNED |

## 4. Artifact viewer

| Capability | SimpleScan files | SimpleScan binding | Known problems | FinalScan binding | Cut | Status |
|---|---|---|---|---|---|---|
| Open GLB / 3D Tiles ZIP | `Runtime/UI/MerkabaArtifactViewer.cs` (4065 lines) | model instance in session room space | legacy packages without binding cannot align | `A+T` model instance | C25 | PLANNED |
| Plan view | same | — | — | derived from FRONT (top-down ortho of resident sphere) | C25 | PLANNED |
| Opacity, World Lock | same | — | — | render state | C25 | PLANNED |
| ALIGN 1:1 (via anchor) | same, `LocalizeArtifactAnchorAsync` | package anchor UUID ↔ session anchor | — | AnchorGraph relocalization + OS anchor hint (§10) | C25 | PLANNED |
| 6DoF grab (right grip), two-hand rotate/scale | same :1062-1145 | — | — | `A+T` transform edit with undo | C25 | PLANNED |
| Annotations: point / line / plane / note | same; `annotations.json` in session dir | session room coords | move wrongly after relocation (no surface binding) | `A+S` (surface-attached) or `A+L`; survive loop closure (§18.4) | C24 | PLANNED |
| Measurements | same | room coords | — | `A+S` endpoints; metric from canonical surfels | C24 | PLANNED |

## 5. Paint

| Capability | SimpleScan files | SimpleScan binding | Known problems | FinalScan binding | Cut | Status |
|---|---|---|---|---|---|---|
| Brush / surface / 3D / spray / line / eraser / eyedropper | `Runtime/Merkaba/MerkabaPaintEngine.cs` (857 lines), `MerkabaDesignDocument.cs` (`design.json`, strokes in room coords) | room coords; never touches M8 | added 2026-09-05 by a concurrent agent; unreviewed | `chart` coordinates on SurfaceAtlas (§18.4); 3D strokes `A+L` | C24 | PLANNED |
| HSV wheel, swatches | same | — | — | UI port | C24 | PLANNED |
| Undo / redo | same | — | — | stroke journal in session | C24 | PLANNED |
| Paint overlay in export | not present as bake | — | — | paint overlay in texture bake (§18.5) | C28 | PLANNED |

## 6. Object library / imported models

| Capability | SimpleScan files | SimpleScan binding | Known problems | FinalScan binding | Cut | Status |
|---|---|---|---|---|---|---|
| Import GLB (SHA-256 content-addressed under `MerkabaScan/library/`) | `Runtime/Merkaba/MerkabaDesignLibrary.cs` (871 lines) | instances in `design.json`, room space | ledger STATE stale | content-addressed library kept; instances `A+T` | C25 | PLANNED |
| Place / select / duplicate / hide / lock / snap | same | — | — | `A+T` with snap to FRONT surfel normals | C25 | PLANNED |
| Model instances in persistence and export | `design.json` | — | — | instances persisted in session; optional include in GLB | C26, C28 | PLANNED |

## 7. Input and UI shell

| Capability | SimpleScan files | SimpleScan binding | Known problems | FinalScan binding | Cut | Status |
|---|---|---|---|---|---|---|
| Menu toggle (left thumbstick click) | `Runtime/RoomScanInputHandler.cs:29-36` | — | — | same mapping, configurable | C22 | PLANNED |
| Select (right index trigger), UI wins raycast | `Runtime/UI/ControllerRayDriver.cs:149,400`, `VRDocumentRaycaster.cs` | — | UI layer mask rules (contr L) | ported pattern | C22 | PLANNED |
| Controller ray visual | `Runtime/UI/ControllerRay.shader`, `ControllerRayDriver.cs` | — | — | ported | C22 | PLANNED |
| Hands as pose source | `ControllerRayDriver.cs:272-300` | — | — | ported | C22 | PLANNED |
| World-space UI Toolkit panel + follower | `Runtime/UI/DebugMenu.uxml/.uss`, `DebugMenuController.cs` (1390 lines), `DebugMenuFollower.cs` | — | contr K: diagnostics visible by default | production panel: scan flow, quality guidance, sessions, tools; diagnostics hidden by default | C22 | PLANNED |
| Diagnostics: FPS, chunks/kernels, mesh readout, checker, occlusion toggle | `DebugMenuController.cs:157-161, 289-320` | — | hidden-by-default not done | telemetry chain counters (§20), hidden by default | C22 | PLANNED |

## 8. Capabilities that do not exist in SimpleScan (explained)

| Capability | Where it is | Decision | Status |
|---|---|---|---|
| Object detection / AI model import (ORT, TripoSR) | deleted from tree; historical branch `feature/object-reconstruction` (2026-04) | **OUT OF SCOPE** by user decision 2026-09-14 (contract §25). Geometry core has no neural dependency (§22). | OUT OF SCOPE |
| Wrist console | only a static mock `/home/wraith/Stažené/uniscan-wrist-console.html` | Not a SimpleScan capability. FinalScan panel (C22) may adopt its layout ideas; no parity obligation. | N/A |
| Autosave as explicit user feature | journal only, no UI | FinalScan makes autosave visible (last durable checkpoint time) — row in §2. | PLANNED (C26) |
| Loop closure / relocalization | none (one anchor per session, "old submaps/relocation" forbidden in donor) | FinalScan adds it (C19, C20); not a parity item. | PLANNED |
| DETAIL as local acquisition mode | FINE brush is a lattice refine, not an acquisition priority | FinalScan DETAIL (§18.2) supersedes FINE semantics; brush UX is kept. | PLANNED (C23) |

## 9. Scan UX improvements over SimpleScan (contract §18.1)

These are acceptance items for C22, not optional polish:

1. **Coverage / quality guidance**: the panel and in-world cues show where evidence is missing and where surfel uncertainty is high (from canonical covariance), so the user knows where to look next. SimpleScan showed only counters.
2. **DETAIL as a visible local state**: the DETAIL region is highlighted while the scheduler is in DETAIL priority; it ends when local uncertainty converges, not on a timer.
3. **No pop**: turning the head never changes geometry or texture quality (readout-sphere invariant §12). SimpleScan rebuilt readout on translation and showed 37–42 fps.
4. **Instant OPEN**: manifest + local sphere only; the user is in the scan within the residency budget, not after a whole-house load.
5. **Capture profile and thermal state visible but quiet**: current profile (NORMAL_30/60, DETAIL_60, LOWLIGHT, THERMAL_30) and scan cadence shown as one line; never a modal.
6. **UI activity equals GPU truth**: "scanning" is shown only while accepted measurements and publications are actually flowing (telemetry chain §20). SimpleScan showed "Active" with zero integration invocations for a whole session.
7. **Diagnostics hidden by default**, one gesture away.
8. **Permission and sensor readiness flow**: HEADSET_CAMERA / USE_SCENE / hand tracking requested with clear states; Env Depth restart after resume is automatic and visible as a short "sensors resuming" state.
