# M8 Sphere–Flower — measurement-driven realtime rewrite

## 0. Authority and scope

Base: `9ab1f687691f91fbefb62e46cc308f8fb5327a8a`.
User-authorized rewrite, 2026-09-10. This is the current execution contract.
User-authorized direct-only cut, 2026-09-11: remove the excavation dual from
production, including its GPU jobs, allocations, persistence progression,
veto, DIRT and dual-dependent completion. This supersedes all requirements
below and in REV-C that require persistent negative volume. Do not synthesize
FULL/THROUGH answers to keep a removed consumer alive. Direct M8, geometry
L0-L2, RGB/V skin, epochs and explicit ERASE remain unchanged. This is a
greenfield scanner: no old-scan import, migration, compatibility streams or
retired record kinds. The session format contains M8 base/live, FlowerDetail
and ThreadAtlas only; unsupported versions are rejected. Reversal is a
version-control revert, not a second runtime mode or an inactive subsystem.
Build the APK only after the complete scanner implementation and host gates
are finished; intermediate cuts never produce a device-build checkpoint.
It replaces the execution/refinement/readout/export model of
`MERKABA_CLOSURE_CONTRACT.md`, the corresponding REV-C scheduling clauses,
and their old RUN/CUT instructions. It is not an optional optimization path.

REV-C remains the geometry/data authority except for the explicit changes
here. Its immutable copy at the start of `lasttrue.md` is historical source,
not a competing execution instruction. `M8-REALTIME-DAG.md` owns the new task
cursor; new receipts in `lasttrue.md` refer to that cursor. Do not resume an
old numbered run after compaction. `quest_guide.md` owns target GPU limits;
`Tools/unity/` owns the existing Kingston Unity build workflow.

MUST = mandatory; MUST NOT = forbidden. CERTAIN/IMPOSSIBLE/AMBIGUOUS retain
their local predicate meanings; none is an application-wide scan state.

## 1. The three boundaries

```text
SENSOR (ephemeral)
    Depth-L/R + RGB-L/R snapshot -> SimpleScan sensor solve
    refined depth, normal/confidence, calibrated RGB correspondence

WORLD (persistent)
    M8 KernelState + FlowerDetail + ThreadAtlas

PRESENTATION (derived)
    direct L2 carrier symbols, pages, draw, GLB/3D Tiles
```

Presentation MUST NOT write reconstruction evidence. Persistent refinement
memory is the world, never a reconstruction program counter.

Forbidden in production: persistent refinement cursor, `PHASE_TASKS`, a
1336-task tile program, refinement quantum, cross-snapshot candidate workset,
cross-snapshot skin receipt, exhaustive 128-carrier reconstruction per owner,
and a second solver used by readout/export. Renaming these is not removal.

## 2. Preserved geometry and storage

- KernelState remains 16 B; signed block/chunk/tile/M8 coordinates remain.
- J=2K+d, the 13 loops, 26/72/48 incidence, exact sectors/boundary ownership,
  rootSign, inherited knots and generated child transport remain.
- RootOwner is unique ownership. PetalUsesRoot is generated incidence.
  A root may be used by incident petals other than its canonical owner.
- R1 establishes the measured carrier; R2 stores phase innovation; R3 resolves
  junction/branch evidence. Prediction and observation are differenced;
  peer evidence and shared incidence are intersected.
- Geometry stops at L2. V is additive L3/L4/L5 microrelief for normals and
  appearance only: no vertices, displacement, silhouette or depth changes.
- Every L2 has the fixed logical 7+49+343=399 thread positions, clipped child
  supports, 36 chamber rows, three descents and 57 scan-authored split bits.
  No childExists, validDepth, readout-selected detail level or RGB layer sum.
- Fibonacci defines immutable subtree-contiguous thread ordering only.
- RGB belongs to ThreadAtlas; V innovations belong to FlowerDetail. Draw
  samples use their hierarchical split union; the authorities do not merge.
- Sparse parent epochs and structural invalidation remain. Compatible plane
  refinements retain epochs. No generation is added to every KernelState.
- No persistent negative volume, excavation veto or inferred DIRT participates
  in scan, readout or export. New direct measurements need no dual admission.
- Session records, manifest transactions, anchors, paint/design data and
  resumable frozen export remain. Presentation is never loadable as M8 truth.

## 3. One snapshot, one transaction

```text
capture N -> sensor once -> count HOT / request missing -> reserve/emit
          -> direct -> reached L1 -> reached L2 -> reached skin
          -> publish world changes + dirty bits -> Finalize -> release N
```

The graph may have real global dependency barriers. It MUST NOT implement a
general stage machine that advances an ordinal through all possible geometry.
Separate finite L1/L2 dispatches are permissible when different touched tiles
share ancestors; removing a necessary global barrier is not an optimization.

Missing addresses enqueue storage claims only. A separate serialized residency
job publishes Block/Chunk/Tile storage for subsequent snapshots. Observation N
MUST NOT allocate, recount, or replay its evidence after residency changes.
HOT owners continue independently. Storage requests are addresses, not pending
sensor evidence. GPU counts/indirect arguments decide work, not CPU readbacks.
Scheduled dispatches and actual nonzero work are reported separately.

DepthCertificate has no role without a real direct-world consumer. Remove its
production hierarchy, dispatches and resources once the complete consumer chain
is shown to be orphaned; retain independent mathematical oracles where useful.

Every resident consequence reached by the current observation is evaluated
through L2 and all three skin steps inside this transaction. No observation
ordinal, camera motion or future snapshot is used merely to finish available
work. No blind expansion of untouched theoretical symbols is required.

COLD: enqueue a residency request, skip the dependent mutation and finalize.
AMBIGUOUS: leave that local evidence unresolved and finalize. Neither can
stall unrelated accepted updates or turn a sensor snapshot into pending work.
Future snapshots read the improved persistent world and newly resident data.

Transient packets are permitted only within this graph. They have a snapshot
token, compact actual-item count and validated capacity. They are dead at
Finalize, are not saved and do not occupy canonical FlowerDetail backing.
Physical buffer capacity may be reused; semantic work must not survive.

Storage capacity, malformed input, missing residency and geometric ambiguity
have distinct counters/results. Capacity failure is explicit and local to its
publication unit; no silent prefix publication or synthetic certainty.

## 4. Sensor and admission

Keep the SimpleScan sensor algorithm installed by 9ab1: four actual streams,
SDK projections/crop, local depth planes, bounded hypotheses and stereo RGB
support. No Flower tables, sectors or world solver in preprocessing.

Measure strict endpoints and lower-confidence bootstrap separately. A useful
sensor bootstrap must not be reported as an accepted M8 endpoint if the
consumer requires normal.w==1. Do not silently upgrade confidence or loosen
world admission to mask an empty pipeline. Wire raw/strict/bootstrap/rejected,
requested/resident/touched and committed counters through existing telemetry.
Telemetry must not drive scan scheduling or require geometry readback.

R1 reduction continues to use the immutable per-tile observation bins. Seeds,
same-observation evidence and repeated-observation confirmation remain the
R1 policy; fine RGB/V does not become an existence prerequisite for a wall.

## 5. Generated evidence-driven DecodeFlower

Codegen supplies finite incidence, transport and candidate-address lookups.
Runtime evaluates only the metric predicates reached through those lookups.
Continuous measured plane/root arithmetic is not replaced by sampled geometry
or an unbounded plane-code table.

```text
owner plane + actual root identities
    -> generated admissible construction masks
    -> present R2/R3 records and generated transport
    -> reached child masks, L1 then L2
    -> actual carrier identities and valid wedges
```

Mask domain is a generated construction/branch, not an arbitrary nearby owner
list. Intersect constraints on the SAME construction; union alternatives.
Different sheets and independent relations are not intersected together.
Popcount 0/1/>1 classifies that construction, not the whole owner's surface.
Multiple compatible petals on one surface remain valid.

IMPORTANT: existing `SectorPetalMask` is RootOwner, not general admissibility.
Its singleton masks MUST NOT be intersected to force two independent R1
anchors into the same owner flag. Decode uses generated root/child incidence
to form admissible construction masks, preserving exact boundary identity and
the complete metric intervals. This preserves REV-C §8.4.2 rather than
reintroducing its previously fixed ownership/incidence error.

Child selection starts from the observed support and its parent construction:
four fixed children, then their fixed children. Include every touched support
and required shared-peer/sibling dependency. Boundaries may reach more than
one child; selecting only the nearest child is forbidden. The oracle proves
that work selection cannot omit an admissible reached result.

Present detail records are addressed by their generated channel/path. No scan
through 144 L1 or 1152 L2 ordinals to rediscover a record's identity. No giant
per-lane array of all root alternatives. Independent relations/items use lanes;
shared results are reused for the lifetime of a bounded local packet.

One semantic DecodeFlower with CPU and HLSL backends serves GPU readout and
CPU frozen export. Exhaustive enumeration survives only as an independent
test oracle. No production fallback to Parent48Snapshot/exhaustive Build.

## 6. Skin: reached signal addresses, not a 399-task program

A measured footprint is projected onto an actual resolved L2 carrier/wedge.
Exactly three generated chamber descents identify its locality. Projected
footprints spanning chambers contribute to all relevant clipped supports.

Accumulate RGB evidence and metric V residuals by those actual addresses.
Only parents reached by evidence and possible novelty evaluate their seven
child split predicate. Do not run 57 parents times 128 theoretical carriers.
Complete-support split requirements remain: a single point is not evidence
for an entire child footprint. Missing support inherits existing signal and
does not create a split; it does not block the rest of the scan.

Publish a split with its seven values atomically. RGB replaces the explicit
signal region; V adds A3/A4/A5 innovations. Empty clipped supports inherit
parent RGB/zero V and cannot prove novelty. All 399 logical addresses remain.

If shader size requires RGB/V stages, use compact snapshot-local SignalItems
for actual resolved carriers, with generated symbolic addresses and only the
needed evidence. No 512-owner fixed receipt region in FlowerDetail; no cursor
or deferred tiles on overflow. Count/reserve/emit and a bounded capacity result
are storage mechanics, not another refinement ontology.

## 7. Direct-only world

No dual hierarchy is allocated, traversed, updated or serialized by production.
Remove its jobs and bindings from both Unity and native command consumers.
Depth/RGB sensor processing stays intact. Deleting the dual does not authorize
weaker sensor admission, altered Flower geometry or a CPU reconstruction path.
Remove consumers requiring excavation evidence; do not replace them with a
constant predicate or an invented inferred surface. Existing session data is
not deleted by this cut.

## 8. Derived owner replacements and readout

Successful GPU canonical update -> OwnerDrawSnapshot replacement -> owner
segment replacement in a derived page -> atomic FRONT publication.

The replacement contains owner identity, parent epoch, actual carrier mask,
symbols and skin references/results. It is derived cache, never canonical world
truth or a saved record kind. It contains no observation cursor or unfinished
evidence. Structural deletion and ERASE publish an explicit empty replacement.
Retire stale epoch/generation replacements instead of reviving old geometry.

Live readout MUST NOT solve the same roots/carriers again. It packs completed
owner replacements. Canonical DecodeFlower is used for OPEN, cold/evicted page
rebuild and frozen offline export, with the same generated evaluation recipe.
Rebuild is bounded and separately accounted, not a hidden live reconstruction.

Readout takes canonical world state only. It has no camera input, sensor
hypothesis solve, 52-original-root packet or exhaustive 128-carrier sweep.
Only directly supported carriers are emitted. Dual-dependent completion and
DIRT are removed along with their coverage/neighbor sweeps.

Required COLD support preserves the old valid FRONT and requests a page retry.
Local undecided completion/skin is not an application-wide 'coverage not
certified' stop. No partial result may be labelled a complete page; genuinely
invalidated old geometry is never displayed merely to keep FRONT nonempty.

Draw keeps the existing shared 7-site indexed carrier, chart and three skin
descents. Last published FRONT is independent of snapshot capture. One native
queue remains; bounded jobs and barriers respect real GPU ordering. Do not
claim that a CPU priority flag preempts an already submitted long dispatch.

## 9. Frozen export, RGB and V

Export freezes canonical source generation, streams tiles and uses the SAME
DecodeFlower over that source. GPU page residency is not export authority.
Retain cancellation, journal/resume, bounded memory and atomic file publish.
No live data may be mixed into the frozen source. No float weld, fitted mesh,
per-wedge 256-square texture, or exhaustive neighbor carrier reconstruction.

Use the existing deterministic per-carrier atlas grid and chart. Evaluate the
same skin function to write captured-RGB base color and tangent-space V normal
atlas, including A3+A4+A5 gradients. Preserve L2 positions/indices. Emit the
matching UV/tangent convention; test mirrored/wound wedges and atlas borders.
Root COLOR_0 must not multiply an already absolute RGB atlas a second time.

Store exact RGB/V session records in the existing native package payload.
A baked normal image is presentation, not lossless V amplitude storage.
GLB/3D Tiles must not discard V merely because portable unlit rendering ignores
normal maps. Standard normalTexture has no visible effect in an unlit viewer;
do not promise otherwise or reinterpret captured radiance as measured albedo.
Native records reopen through the ordinary live readout with exact V. Portable
normal-map/fallback material metadata reports its presentation limitation.

## 10. Build, verification and completion

Use existing Kingston Unity scripts, not an IDE or an Android-native-only
build. Respect quest_guide: banked <=128 MiB SSBO bindings, <=32 KiB WG shared
memory (target 12–16 KiB), <=8 writable bindings, bounded WG/register lifetimes,
generated RO lookups, no CPU readback for scan/draw decisions. GPU->SSD and
export transfers are not reconstruction authority and must remain explicit.
CPU oracle/codegen is not a realtime execution backend. SENSOR, scan world
updates, live page compilation and draw MUST execute on the GPU; no CPU
DecodeFlower or fallback may enter that hot path. The frozen export backend
in §9 is offline, never a producer of live pages or scan evidence.

Commit each coherent replacement with consumers rewired and its predecessor
removed. Do not leave a compile-broken checkpoint labelled a closed cut.
During source cuts use only necessary compile/codegen checks: MUST NOT build an
APK, install to the headset or run device acceptance after an individual cut.
Only after the ENTIRE rewrite is wired, delivery runs the complete test/validation
and repair pass, then the APK build/install/device acceptance. Positive scan
fixtures must not be removed merely to obtain green results.

Required evidence:

- reached decode equals exhaustive oracle for actual input/support; shared
  boundary knots and negative/tile/chunk/block coordinates remain identical;
- runtime contains none of the forbidden cursor/task/receipt/sweep paths;
- uniform/partial/full skin shares geometry, masks are scan-authored, V only
  changes appearance, CPU/GPU skin and export normal atlas agree;
- unknown/COLD/ambiguous updates do not block unrelated strict endpoints;
  new direct objects and explicit ERASE work without any dual dependency;
- fresh frozen export carries RGB/V and reopens with ordinary readout;
- exact embedded SPIR-V audit: <=1 MiB and <=50000 body instructions per
  production entrypoint; existing warnings and hardware checks remain;
- HOT entrypoints above 20000 body instructions are release-blocked unless a
  recorded Adreno compiler/profile report proves acceptable instruction/GPR
  pressure and zero spills for that exact shader hash. A hard size PASS alone
  is not performance closure; thresholds MUST NOT be relaxed to ship;
- command graph reports actual dispatches/barriers/work counts; 9–11 normal
  hot-resident dispatches is the engineering target, not a reason to omit a
  required dependency or hide a stage;
- full tests, Unity APK build, install/hash, pipeline initialization and live
  Quest scan/draw timings are separate receipts, never interchangeable PASS.

Final means a working visible realtime scan, persistent/reopened/exported
data and accepted shader/runtime behavior. An APK or a green oracle alone is
not completion. Disconnected device means DEVICE ACCEPTANCE PENDING.

## 11. Driver binaries and final packaging

CAPTURE is a build-preparation mode on a compatible golden Quest. It captures
driver-produced VK_KHR_pipeline_binary payloads for all native compute AND
Unity graphics/compute PSOs. PC SPIR-V compilation, ordinary pipeline cache
and Unity WarmUp alone are not precompiled release delivery.

BINARY_ONLY is the release mode. Negotiate/query the required Vulkan features,
validate the global pipeline key and per-pipeline identity against the actual
device-create configuration, and create pipelines only from matching binaries.
Missing binary or incompatible key is an explicit initialization error. There
is no SPIR-V/JIT fallback. Intercept Unity device-proc lookup and both graphics
and compute pipeline creation; native-only coverage is insufficient. Do not
combine binary pipeline creation with FAIL_ON_PIPELINE_COMPILE_REQUIRED flags.

The user authorizes golden-device capture AFTER the complete source rewrite.
Capture final source/configuration, generate a versioned hashed binary bundle,
then package the final ARM64/IL2CPP release APK. Sign using an explicitly
provided production keystore from environment/secret storage; never invent a
release key or expose secrets. Verify signature, zip alignment, package ID,
ARM64 libraries, binary bundle hash and complete APK SHA-256.

Final device evidence includes cold install/cache-empty startup with zero
pipeline binary misses and zero runtime compile fallbacks; strict endpoint,
touched, occupied and triangle counts; visible scan from START; 10–20 minutes
at 72 Hz; head rotation, sleep/wake, STOP/START, SAVE/kill/OPEN/anchor, FINE/ERASE
and frozen RGB/V export. Capture success is not release device acceptance.
