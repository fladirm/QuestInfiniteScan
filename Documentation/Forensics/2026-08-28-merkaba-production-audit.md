# Quest Merkaba production forensic audit — 2026-08-28

## Scope and immutable runtime baseline

This is a read-only forensic report. It does not contain a runtime fix.

- Repair baseline: `0e9081060ed1068aad6e075f4961ad25b72245ff`
- Audited runtime branch: `fix/merkaba-production-closure`
- Audited runtime commit: `3c12b99ded913e0f105a3ba54f747901fd567d40`
- Worktree before the audit branch was created: clean
- Unity: `6000.5.9f1`
- Target host: `/mnt/kingston-unity/Unity/Projects/QuestMerkabaScanHost`
- Quest evidence session: 2026-08-28 19:51–19:53 Europe/Prague
- Device was no longer attached when this report was written; no new device action was
  attempted.

The audit compares the implementation against both the original Infinite Merkaba
scanner contract and the external repair pursuit at:

`/mnt/aidisk/prace/.codex-pursuits/quest-merkaba-production-closure/REPAIR_GOAL.md`

The external authority SHA-256 is:

`9135e66973e4fe2e36af2cb67869a56afcc6aee75b9b57f50fdd269e109e923f`

Its overriding direct-Merkaba correction supersedes the earlier exact Boolean-union /
96-microtriangle interpretation.

## Executive verdict

The committed implementation is not production-closed. Depth acquisition,
surface-driven integration, reversible evidence, dirty publication, and corrected
direct octahedron/tip geometry are active on the Quest. The live scan is invisible
after publication, the menu reports stale zero canonical counts and disables save and
export, the draw timestamp does not measure the submitted rendering workload, and the
measured scan does not meet the 72 Hz performance requirement.

The strongest device observation is internally consistent:

1. The user stopped scanning at 19:52:57 and pressed `START / RESUME` at 19:53:09.
2. At 19:53:11 the app reported 22,582 surface candidates, 33,256 carve candidates,
   48 integration chunks, 84 resident chunks, 64 visible chunks, 8 dirty topology
   chunks, and 431,680 published primitives.
3. Screenshots taken at 19:53:17 and 19:53:27 contain passthrough and UI but no scan
   geometry.

This proves that START, depth, integration, occupancy transitions, topology and
publication ran. The first demonstrated failure is in live render/presentation, not in
the scanner start button or depth frontend.

## P0 root cause: Single Pass Instanced shader contract violation

The host uses OpenXR render mode `1` and stereo rendering path `2`, i.e. the Quest
Single Pass Instanced/multiview path. `MerkabaGrid.shader` enables instancing but:

- accepts raw `SV_InstanceID` as the primitive-record index;
- has no `UNITY_VERTEX_INPUT_INSTANCE_ID`;
- never calls `UNITY_SETUP_INSTANCE_ID`;
- has no `UNITY_VERTEX_OUTPUT_STEREO`;
- never calls `UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO`.

In Unity's SPI path, the input instance ID contains the eye index. Unity's own
`UnitySetupInstanceID` maps it to:

```text
unity_StereoEyeIndex = inputInstanceID & 1
unity_InstanceID = base + (inputInstanceID >> 1)
```

The production shader bypasses that mapping and uses the eye-expanded raw ID at
`MerkabaGrid.shader:64-73`. It also transforms with stereo view/projection state without
initializing the eye and does not route its output to the correct eye slice. The donor
style `ControllerRay.shader:25-45` uses the required Unity macros and provides a local
working contrast.

This is a compile-valid semantic XR error and therefore explains why all EditMode and
shader-import tests could pass while the Quest shows no geometry. It is the
high-confidence primary root cause of the invisible live scan. A corrected APK has not
been built or tested as part of this audit.

## P0 root cause: UI counters and action gating read stale CPU state

The normal integration authority is GPU resident state. New transient GPU pages are
not materialized into the CPU `_chunks` dictionary until synchronization or eviction
readback (`MerkabaGrid.Gpu.cs:492-510`, `604-645`). Nevertheless:

- `MerkabaGrid.ActiveChunkCount` is only `_chunks.Count`;
- `RoomScanner` exposes that CPU count directly;
- `DebugMenuController` displays it as the active chunk count;
- SAVE and EXPORT are enabled only when that CPU count is greater than zero.

Consequently the UI can show `0 chunks / 0 kernels` while the sampled GPU state has 84
resident chunks and 431,680 published primitives. This is not proof of an empty scan.
It is a status-source mismatch, and it also makes SAVE and EXPORT functionally
unreachable during the demonstrated session.

The fix must not introduce a full per-frame GPU readback. This report records the
fault only; it does not prescribe or implement the replacement status path.

## P0 measurement defect: MERKABA_DRAW timestamp is not the draw workload

The isolated timestamp plugin is a real Vulkan `vkCmdWriteTimestamp` mechanism and is
not Unity Profiler instrumentation. The five compute measurements are plausible.
However, graphics timestamps are issued with `GL.IssuePluginEvent` immediately around
calls to `Graphics.DrawProceduralIndirect` made from `LateUpdate`.

All 19 samples report exactly `MERKABA_DRAW=0.001 ms`, despite 359,308–460,276
published triangles and 56–64 indirect draw submissions. The marker is therefore
bracketing API/event submission rather than the deferred SRP/XR execution of those
draws. It cannot be used to attribute the rendering cost.

The pursuit's requirement for a valid per-stage GPU draw measurement is not met. The
compute timestamps remain useful; the draw value does not.

## Measured performance

Nineteen native timestamp samples from the captured scan:

| Stage | Average ms | Min ms | Max ms |
|---|---:|---:|---:|
| DEPTH_PREPROCESS | 3.320 | 2.655 | 3.952 |
| SURFACE_INTEGRATION | 1.448 | 0.590 | 4.381 |
| CARVE_INTEGRATION | 0.542 | 0.272 | 2.588 |
| TOPOLOGY_UPDATE | 0.174 | 0.005 | 0.368 |
| PUBLICATION_COMPACTION | 0.020 | 0.003 | 0.024 |
| MERKABA_DRAW | 0.001 | 0.001 | 0.001 |

The final row is invalid for workload attribution for the reason above.

VrApi samples classified inside the scanning intervals (`n=86`):

- FPS: average 60.663, range 58–70, target 72;
- App: average 13.297 ms, range 10.900–14.960 ms;
- CPU&GPU: average 20.170 ms, range 16.150–24.290 ms;
- GPU utilization: average 94.9%, range 92–97%.

The 11-second stopped interval still retained and rendered the published scan. It
averaged 67.1 FPS and 94.5% GPU utilization. A fresh idle app with no published scan
was approximately 72 FPS and 40–41% GPU utilization. This strongly locates the
remaining load in retained render/passthrough/URP work rather than dirty topology.

The renderer submits one indirect draw per visible chunk (`MerkabaGridRenderer.cs:
322-336`): normally 56–64 draws for roughly 431,480 triangles, or about 1.29 million
logical vertices before stereo expansion. This remains structurally expensive for the
requested simple/blazing-fast Quest renderer. Exact draw cost cannot be stated until
the timestamp span is attached to actual SRP/XR execution.

The dirty publication design itself is not the current bottleneck: a static sample at
revision 777 had zero dirty chunks, 0.005 ms topology, and 0.003 ms publication. It
therefore satisfies the requirement that a settled frame performs no meaningful
topology/publication rebuild.

## Memory evidence

Captured process memory:

| State | Total PSS | Total RSS | Graphics | GL mtrack |
|---|---:|---:|---:|---:|
| fresh idle | 1,232,169 KB | 1,327,318 KB | 844,074 KB | 791,496 KB |
| active/published scan | 1,394,353 KB | 1,485,182 KB | 969,894 KB | 904,568 KB |

The active state adds about 162 MB PSS and 126 MB graphics memory. Static scanner
allocations account for roughly 120 MB: 48 MiB kernel states, 48 MiB double publication
banks, about 12 MiB carve indices and about 12 MiB surface/carve work queues, plus
smaller tables. The remainder of the process is Unity/URP/OpenXR/Meta allocation and
must not be assigned to Merkaba without further evidence. No Quest OOM or crash is
present in the captured active log.

## Other verified defect: MRUK startup path

`RoomAnchorManager` invokes `LoadSceneFromDevice` without preventing the default scene
capture request. Two clean launches log `ErrorLimitReached xrRequestSceneCaptureFB`.
When START is pressed, `RoomScanner` waits up to ten seconds and then logs `MRUK room
load timed out; using the current world frame.` This delayed the demonstrated start but
did not cause the invisible rendering: scanning proceeded immediately after fallback.

## Geometry and integration findings

The current static geometry authority matches the latest corrected pursuit contract,
not the obsolete cube-patch implementation:

- one central octahedron per occupied coordinate;
- eight body-diagonal rules;
- absent body-diagonal neighbour emits one octahedron face;
- occupied body-diagonal neighbour emits three fixed tip sides;
- 8–24 triangles per occupied kernel;
- shared CPU authority and generated HLSL;
- no 96-microtriangle Boolean-union model and no axis-aligned cube patch authority.

The current surface work generation also preserves the requested isotropic ray-derived
three candidates and adds the dominant-axis boundary guards only as a deduplicated
union. Every queued candidate is revalidated with the actual two-eye depth relation,
normal, disparity/dilation and quality predicates before positive evidence. It did not
regress to dominant-axis-only integration.

Evidence/hysteresis, RGB accumulation, two-eye use, consumed-frame preprocessing,
FREE carve of existing evidence, no free-only canonical chunk allocation, signed
coordinates, 32-cubed chunks, canonical nonresident boundary summaries and dirty
publication are present and covered by the passing tests. Device counters corroborate
that these stages ran.

## GLB findings

The pre-repair device GLB `/home/wraith/Stažené/QuestMerkabaScan-latest.glb` is a valid
GLB 2.0 container, but it predates the repaired APK by more than four hours. It has:

- 775,724 triangles;
- 2,327,172 indices/normals;
- exactly six distinct normals: `+/-X`, `+/-Y`, `+/-Z`;
- therefore cube-only geometry.

It is baseline forensic evidence and is not proof of current export behavior.

The repaired fixture GLB is structurally valid and contains 40 triangles with the
eight expected non-axis normals `(+/-0.57735, +/-0.57735, +/-0.57735)`, plus indexed
POSITION/NORMAL/COLOR_0 and PBR material data. The validator now validates the exact
file passed to it.

There is no fresh device GLB from the repaired APK. The current UI counter bug also
disabled EXPORT in the demonstrated scan. Device export, evidence-aware shell cleanup,
same-geometry live/export equivalence, progress UX, and device save/load therefore
remain unverified.

## Requirement disposition

| Requirement group | Result | Evidence |
|---|---|---|
| single MerkabaGrid, 5 cm support, 2.5 cm lattice | PASS static/tests | canonical constants and direct geometry authority |
| corrected octahedron + conditional tip topology | PASS static/tests | 8 body-diagonal rules, 8–24 triangles |
| QRS-quality surface/free classification and stereo | PASS static/device counters | both eyes revalidated; nonzero candidates |
| reversible refinement and artefact carve | PASS automated | false foreground/wall and border tests |
| sparse signed 32-cubed chunks; no free-only allocation | PASS static/tests | transient pages materialize only after useful state |
| residency-independent topology | PASS static/tests | canonical boundary summary fallback |
| dirty persistent compact publication | PASS static/device timings | zero dirty work in settled sample |
| visible live Merkaba scan | FAIL device | 431,680 primitives but no geometry in two screenshots |
| smooth streaming/no popping | NOT VERIFIABLE | live geometry is invisible |
| opacity control | IMPLEMENTED, NOT ACCEPTED | shader/UI present; invisible scan prevents visual proof |
| live status and reactive Save/Export | FAIL device/code | stale CPU zero count disables actions |
| save/load/export progress | PARTIAL | UI state exists; operations were not reachable/verified |
| export-only evidence-aware shell | PASS tests, NOT DEVICE-VERIFIED | fixture only; no repaired device GLB |
| native stage GPU timestamps | PARTIAL | compute credible; draw span invalid |
| materially approaches/holds 72 Hz | FAIL device | 60.7 FPS average, 94.9% GPU |
| fresh Android APK | PASS | 63,166,319 bytes, build success |
| fresh Quest install | PASS captured evidence | package update 19:47:13, install result Success |
| no TSDF/SN/triplanar/GSplat/Sigma production authority | PASS forensic search | no production matches or forbidden directories |
| full Quest closure | FAIL | render, UX actions, performance and current GLB unresolved |

## Test and build evidence

- Unity EditMode: 58/58 passed, 0 failed, 0 skipped, duration 8.09 s.
- Unity Android build: `Build Finished, Result: Success`.
- APK: 63,166,319 bytes.
- APK SHA-256:
  `3880d10947dad1730251c53ea2f39298b19aebb6de5b891d2cecf3725515da62`.
- APK contains `libMerkabaVulkanTimestamps.so`; it contains no Sigma timing plugin.
- Fixture GLB interoperability validation: 0 errors, 0 warnings.
- The test log also reports 93 persistent Unity Editor allocations without stack
  attribution. This is not enough to assign a runtime leak, but a completely clean test
  process cannot be claimed from that log.

The 58 passing tests do not invalidate the device failure. They test geometry tables,
compute fixtures, source-level timestamp contracts, UI bindings and shader import, but
they do not execute the production procedural shader under Quest Single Pass
Instanced rendering or prove that the graphics timestamp spans the real SRP draw.

## Ledger and closure inconsistency

The repository's original `.codex/MERKABA_STATE.md` and
`.codex/MERKABA_DECISIONS.md` still describe the obsolete 24 cube-patch authority.
The newer repair ledger correctly describes direct octahedron/tip geometry but ends
before the later active device evidence. Consequently any earlier “complete” wording
or pending-only device status is stale and cannot be used as closure evidence.

## Evidence package

The external evidence archive is intentionally limited to the current repaired build
and current active device run: active app/metrics/VrApi/meminfo logs, both post-START
screenshots, current build/test logs, test XML, APK, repaired fixture GLB, source audit
excerpts, and a SHA-256 manifest. Historical idle/input logs and the pre-repair device
GLB are not bundled. Large binary evidence is deliberately not committed to this Git
branch.

No production source, Unity host setting, APK or Quest state was changed by this
audit.
