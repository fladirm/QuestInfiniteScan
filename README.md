# Quest Infinite Merkaba Scan

> Production closure contract: [`contr.md`](contr.md). It is authoritative over conflicting historical documentation.

A simple, fast room scanner for Meta Quest. Depth, normals, and passthrough RGB
update one reversible sparse occupancy grid. Fixed local boundary primitives are
drawn directly on the GPU; live scanning never constructs a Unity mesh.

## Fixed geometry

- Support size: exactly 5 cm
- Lattice spacing: exactly 2.5 cm
- Support half-extent: exactly 2.5 cm
- Chunk edge: 32 kernels, or 0.8 m
- World address: signed integer `int3`
- Reconstruction authority: `MerkabaGrid`

Each support is the canonical cube/inscribed-Merkaba decomposition: a central
octahedral kernel, eight tetrahedral tips, and twelve tetrahedral edge wedges.
The renderer uses 24 frozen face-quadrant patches. Their predicates inspect only
the immediate 26 neighbours and apply exact integer ownership, so exterior
patches appear once and interior patches never appear.

## Observation and refinement

The QuestRoomScan sensor frontend is retained: stereo depth reconstruction,
depth normals, dilation and disparity rejection, occlusion checks, camera
projection, distance/angle quality, exclusions, RGB capture, and XR/world
transforms.

A projected lattice centre is classified as `FREE`, `SURFACE`, or `UNKNOWN`.
Surface evidence increases occupancy confidence and updates a quality-weighted
RGB average. Proven free space decreases confidence. Invalid samples and points
behind the measured surface do nothing. Separate on/off thresholds provide
hysteresis. Repeated observations stabilize real geometry, while a later clear
view removes an earlier false foreground without touching the real wall behind.

## Infinite sparse runtime

Untouched space allocates nothing. Allocated chunks contain dense arrays of
16-byte canonical kernel records: evidence, packed RGB, colour confidence, and
minimal flags. Negative coordinates use mathematical floor division/modulo.

Quest integration is one coarse GPU pass over current-frustum chunks. Residency
is bounded to 96 chunks, with at most 48 integration chunks and 64 visible
chunks per frame. Occupancy transitions dirty only the changed kernel and its
26-neighbour topology region. Rendering compacts visible boundary records and
uses an indirect procedural draw. There is no normal-frame CPU readback; the
status counter is sampled asynchronously once per second.

## Quest controls

Press the left thumbstick to open or close the controller-following menu. Point
with the right controller and select with the right index trigger.

- `START / RESUME`
- `STOP`
- `SAVE`
- `LOAD`
- `NEW / CLEAR`
- `EXPORT GLB`

The menu reports scanning state, active chunks, occupied kernels, visible
boundary count, saved-session state, export state, controller tracking, and FPS.

## Persistence and export

Save/load stores canonical chunk records, integration count, and spatial-anchor
metadata. Topology and GPU render records are rebuilt. Resuming retains the
same signed room-space lattice coordinates.

GLB export is an explicit offline readout. It writes indexed `POSITION`,
`NORMAL`, and normalized `COLOR_0` attributes with a white base factor,
metallic factor 0, and roughness 0.85. The live representation remains the
kernel grid.

## Target host and commands

The local target host is independent from any donor source and embeds this
package at `Packages/com.genesis.roomscan`.

```bash
Tools/unity/verify_unity_install.sh
Tools/unity/run_merkaba_tests.sh
Tools/gltf/validate_merkaba_glb.sh
Tools/unity/build_merkaba_apk.sh
Tools/unity/deploy_merkaba_apk.sh
```

The build script runs the canonical target setup, builds Android with the exact
verified Unity editor, and accepts the APK only when it is non-empty, newer than
the previous output, and accompanied by Unity's explicit success marker. The
deploy script installs only when exactly one authorized device is attached.

The editor menu `Quest Merkaba > Setup Target Host` creates or refreshes the
canonical Quest/OpenXR scene and wires all depth, compute, render, persistence,
export, UI, and controller assets.

## Core source

```text
Runtime/Core/DepthCapture.cs
Runtime/Camera/PassthroughCameraProvider.cs
Runtime/Core/RoomScanner.cs
Runtime/Core/RoomAnchorManager.cs
Runtime/Core/RoomSpaceRoot.cs

Runtime/Merkaba/MerkabaGrid.cs
Runtime/Merkaba/MerkabaChunk.cs
Runtime/Merkaba/MerkabaIntegrator.cs
Runtime/Merkaba/MerkabaGridRenderer.cs
Runtime/Merkaba/MerkabaPersistence.cs
Runtime/Merkaba/MerkabaExporter.cs

Runtime/Shaders/MerkabaIntegration.compute
Runtime/Shaders/MerkabaTopology.compute
Runtime/Shaders/MerkabaGrid.shader
Runtime/UI/
```

## Verification

EditMode tests cover canonical decomposition, single and neighbouring kernels,
solid blocks, axis walls, corners, diagonal and sheet patterns, close parallel
sheets, cylinder/sphere-like patterns, negative coordinates, and chunk borders.
They also cover repeated refinement, RGB convergence, false-surface carving,
true-wall preservation, multi-angle corners, deterministic persistence, GLB
contracts, and Vulkan compute parity with the production integration and
topology paths.

## License

MIT. See [LICENSE.md](LICENSE.md).
