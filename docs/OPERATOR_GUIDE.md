# QuestInfiniteScan operator guide

## Before scanning

- Wake and wear the Quest 3 or Quest 3S, keep Developer Mode enabled for diagnostic
  builds, and confirm required camera/scene permissions when prompted.
- Use useful room lighting. Depth may still produce geometry in darkness, but RGB
  color, atlas refinement, and DiffSoup observations will be poor.
- Clear a safe walking path. The app does not replace Guardian/boundary awareness.
- The CUDA notebook is optional. Scanning and local persistence must continue when
  it is off or unreachable.
- For an acceptance run, start with a new world. Preserve a failing world and its
  logs instead of repeatedly overwriting the evidence.

## Scan flow

1. Open the debug menu with the configured controller binding and select **Scan**.
2. Press **Start Scan** once. Wait for passthrough/depth delivery and the first mesh.
3. Move steadily and view surfaces from more than one useful angle. Fast motion,
   grazing views, blank/glossy surfaces, and darkness reduce observations.
4. Cross a chunk boundary naturally. The next local volume should begin in the
   overlap region; it should not require walking several metres through empty space.
5. Use **Infinite World** to watch the active chunk, lifecycle, background writes,
   resident meshes, graph edges, and refinement queue.
6. Stop the scan before active-chunk texture refinement or GLB export. Wait until the
   lifecycle/export status reports completion before closing the app.

Do not repeatedly press Start/Stop while an operation is busy. A disabled button is
an explicit lifecycle guard, not a frozen UI.

## Controls that are easy to confuse

| Control | What it changes | What it does not change |
|---|---|---|
| Freeze Tint | Blue diagnostic overlay on frozen voxels | TSDF integration |
| Freeze In View | Locks voxels in the current view frustum | Render mode |
| Unfreeze In View | Lets those voxels integrate again | Existing world transforms |
| Wireframe | Mesh presentation | Geometry, color, or persistence |
| Vertex | Stored vertex-color presentation | Integration policy |
| Refined | UV atlas/normal-map presentation when available | Active TSDF |
| DiffSoup | Validated triangle/LUT artifact when available | Server job state |
| None | Hides scan presentation | Saved data |

Freeze is useful after a surface is genuinely good, but freezing an incomplete or
misregistered surface preserves the error. The distance/normal fusion policy already
tries to prevent a distant low-quality observation from eroding a stable close one;
Freeze remains an explicit operator override.

## GLB/PBR export

In **Infinite World**:

- **Export Active Chunk GLB** exports the exact stopped active revision.
- **Export World GLB/PBR** always publishes `building.json` plus content-addressed
  `chunks/*.glb`; it also writes `world.glb` when the configured monolithic bound is
  safe.

The world transform comes from the current pose graph. Base color and normal maps are
embedded. Metallic is zero and roughness is a configured constant because those maps
are not measured by this version. Existing output directories are not overwritten;
each operation receives a new timestamped destination.

For very large buildings, use the sharded manifest. A multi-gigabyte monolithic GLB
is inconvenient for many consumers even when storage permits it.

## DiffSoup refinement

The Infinite World view distinguishes:

- **Offline-safe / None**: jobs remain local; scan operation is unaffected.
- **LAN idle/active**: the scheduler can reach the configured backend and is polling
  or transferring outside the scan frame.
- **pending / ready / failed**: durable local queue counts.
- **DiffSoup resident**: a validated artifact currently has a GPU presentation.

A corrupt, stale, incomplete, or unsupported artifact must leave the existing coarse
or prior DiffSoup renderer visible. Revisit creates a newer chunk revision; a late old
result cannot replace it.

## Capturing a useful failure

Keep the headset awake, the app running, and USB connected after reproducing. Then:

```bash
adb devices
Tools/unity/profile_tsdf_on_quest.sh 30
```

Continue scanning across boundaries during the capture. The output lives under the
external build root and contains raw logcat, GPU/process snapshots, frame data,
`performance-summary.json`, and CSV timelines. Also record:

- exact actions and number/direction of boundary crossings;
- whether the notebook was reachable;
- active chunk/lifecycle text visible in Infinite World;
- whether the problem affects geometry, color/render mode, or both;
- whether Stop Scan completes.

Never commit room imagery, keyframes, captures, generated models, LAN addresses, or
device identifiers to Git.

## Current field-test warning

The present feature checkpoint has reproduced a lifecycle bug after repeated
rollovers/revisits: an older chunk can remain `Finalizing`, lack an immediately
available volume snapshot, and disappear from the bounded presentation cache. Do not
use this build for irreplaceable capture work. Preserve the failing world for the
post-checkpoint lifecycle fix. Details are in [KNOWN_ISSUES.md](KNOWN_ISSUES.md).
