# M8 JOINT FOUR-STREAM / MUTATION AUTHORITY CLOSURE

Frozen on 2026-08-30 against:

```text
repository  /mnt/aidisk/prace/simplescan
branch      fix/merkaba-runtime-root-causes
base HEAD   01fd188b171bfffeb184f265d3518a955adc38d6
baseline    164 passed, 0 failed, 0 skipped
device      Quest 3S 340YC20G7X0QZ4
```

This file is the execution oracle for the active S1-S3 pursuit. It supersedes
conflicting stereo-measurement and mutation-authority statements in historical
`.codex` repair ledgers. It does not rewrite history and it does not supersede
the frozen M8 physical address, storage, lifecycle, persistence, or readout
contracts already implemented in the current tree.

## 1. Ontology and scope lock

M8 remains the only world state. `KernelState` remains exactly:

```text
int  OccupancyEvidence
uint PackedColor
uint ColorConfidence
uint Flags
```

The following are frozen:

```text
LatticeStep               0.025 m
SupportSize               0.050 m
HalfSupport               0.025 m
OccupiedOnThreshold       512
OccupiedOffThreshold      128
SurfaceEvidenceScale      640
FreeEvidenceScale         256
EvidenceConfidenceLimit   2560
FreeFullClearance         0.150 m
NEEDS_CARVE persistence semantics
```

Preserve signed evidence, ON/OFF hysteresis, q-squared convergence weighting,
RGB accumulation, surface-before-carve ordering, replacement continuity, held
observation exactly-once semantics, cold-tile atomicity, and FREE-never-
allocates. Later FREE must never erase RGB. UNKNOWN is a bitwise no-op.

This pursuit must not change:

```text
M8 address/hash/radix contracts
KernelState ABI or persistence format
SSD/cache state machine
lifecycle/progress
readout/tetra skin/GLB
observation retry/admission cadence
geometry primitives
evidence scales or hysteresis thresholds
```

No TSDF, Surface Nets, ray-DDA carve, CPU world mirror, winner database,
per-kernel hash, mesh chunks, new render state, or fallback sensor path may be
introduced.

## 2. Verified defects in base HEAD

The current input resources are correctly owned and mandatory: Environment
Depth L/R plus PCA L/R, with immutable poses, projections, intrinsics, pixels,
and timestamps. The remaining defects begin after ownership:

1. `StereoRgbdRefine.compute` dispatches `z=2`; each source eye independently
   selects a refined endpoint. `DiscoverSurfaceCandidates` dispatches `z=2`
   again, so one four-stream observation can produce `L -> K` and `R -> K+1`.
2. Opposite-depth support uses Euclidean distance to four quantized opposite
   pixel centres. This rejects a valid sloped/distant plane when lateral pixel
   spacing exceeds the 12.5 mm normal-direction error budget.
3. PCA Census compares independent `+/-1` image pixels. Those samples are not
   homologous world points under perspective, baseline, or surface slope.
4. Textureless PCA regions can only validate coverage/chromatic consistency;
   they cannot physically choose a sub-depth hypothesis. Rejecting such a
   metric hit creates holes.
5. `DepthNorm` runs after the hard refined-depth mask and needs three valid
   samples. One rejected sample therefore invalidates neighbouring normals and
   expands holes.
6. `TrySurfaceMeasurement` conflates hard measurement validity, permission to
   mutate existing world, and q-squared convergence weight. Its secondary
   quality threshold can reject a four-stream-valid endpoint.
7. `_M8TileBits.z` deduplicates a canonical key, but the first GPU invocation
   also publishes its packed source pixel/eye/quality. Scheduling therefore
   chooses canonical source data nondeterministically.
8. CARVE independently reprojects every active kernel into both eye-specific
   refined endpoint fields and takes the strongest L/R classification. It does
   not use one joint endpoint and it grants peripheral/grazing observations the
   same mutation right as central observations.

QuestRoomScan did not solve these explicitly. Its useful property was
asymmetry: an empty TSDF sample was easy to seed, while an established sample
became progressively harder to move through the stability denominator. M8 must
preserve that behavioural property through evidence/hysteresis and explicit
mutation authority, without restoring TSDF.

## 3. Three distinct concepts

These concepts must never be collapsed again:

```text
measurementValid
    hard four-stream geometric/photometric contract

mutationAuthority
    DISCOVERY / SUPPORT / REVISION right derived from depth geometry

evidenceWeight
    continuous q^2 convergence rate after the requested mutation is legal
```

Four-stream validity says whether an observation is credible. It does not by
itself authorize moving or deleting stable canonical world.

## 4. S1 -- JOINT FOUR-STREAM METRIC TRUTH

### 4.1 One producer

Use depth-left pixels only as a deterministic reference sampling lattice. This
is not mono reconstruction. Every valid output requires all four inputs:

```text
Depth L prior + Depth R local support + PCA L + PCA R
                         -> one joint endpoint H
                         -> one joint normal N
                         -> one reference pixel
                         -> one nearest canonical coordinate K
```

There must be no second depth-eye canonical producer and no later L/R winner
vote. The joint derived observation is disposable GPU input data, never world
state and never persisted.

### 4.2 Metric solve

For each valid left reference pixel:

1. reconstruct its raw metric prior and a bounded local source plane;
2. project each bounded hypothesis into the right depth image;
3. derive right local plane support from valid neighbouring depth samples;
4. compare the hypothesis with point-to-plane residual, not Euclidean distance
   to a quantized pixel centre;
5. require absolute normal-direction residual `<= HalfSupport / 2 = 12.5 mm`;
6. align and combine the two valid local plane normals into joint `N`;
7. output one left-reference projection depth and the joint normal.

The correction remains bounded to `+/-12.5 mm` around the metric source prior.
R32 storage only prevents scanner-added quantization; it is not represented as
sensor accuracy.

### 4.3 Homologous stereo PCA

Build a fixed metric tangent basis `T,B` from `H,N`. Compare PCA L/R at the
same world samples `H + metricOffset.x*T + metricOffset.y*B`, projected through
each owned camera's own pose, intrinsics, crop, and resolution.

```text
structured and uniquely better photometry
    may select a bounded metric hypothesis

photometrically ambiguous/textureless but geometrically consistent
    keeps the joint metric prior

contradictory geometry, missing any of four coverages, or contradictory colour
    invalidates the sample
```

No same-UV, same-pixel-offset, mono-PCA, depth-only, or best-effort fallback is
allowed.

### 4.4 Downstream contract

The same joint pass emits the normal. Delete the obsolete post-mask `DepthNorm`
path rather than keeping a second normal authority. Dilation consumes the one
joint reference depth and remains derived occlusion/FREE support only.

A joint-valid hit is not rejected by a second pseudo-confidence threshold.
Distance and incidence may still produce `evidenceWeight` after validity; they
do not redefine measurement validity.

Dilation parameters come from canonical M8 constants:

```text
voxelDistance = FreeFullClearance = 0.150 m
voxelSize     = SupportSize       = 0.050 m
```

Remove orphan serialized dilation geometry values/fallbacks.

### 4.5 S1 acceptance

```text
one observation -> one joint endpoint -> one canonical candidate
no z=2 refined-output/discovery authority
no Euclidean opposite-pixel-centre precision test
no unwarped image-pixel Census
textureless valid plane keeps metric prior
sloped plane uses point-to-plane residual
joint normal does not erode through a second validity mask
all four resources remain mandatory
no additional full-frame dispatch
joint pass has the existing real GPU timestamp
```

Commit:

```text
fix(scan): solve one joint four-stream surface
```

## 5. S2 -- DETERMINISTIC CANONICAL OWNERSHIP

### 5.1 Authority classes

Every joint-valid candidate is transiently classified:

```text
DISCOVERY
    no compatible canonical surface exists; full common FOV may allocate K

SUPPORT
    a compatible existing canonical surface represents this hit; observation
    confirms/refines RGB/evidence on that owner but may not create adjacent K+1

REVISION
    a compatible owner exists and the observation is geometrically
    authoritative; Knew may be committed before Kold is corrected
```

These classes are derived per immutable observation. They are not persisted in
`KernelState` and do not create a second geometry authority.

### 5.2 Compatible owner

Do not suppress all 26-neighbour growth. Tangential extension of walls,
corners, stairs, and thin sheets must remain discovery-capable.

Search only the bounded local normal/ray support stencil around nearest K:

```text
K and the nearest +/- normal-direction lattice step(s)
```

An occupied candidate `Kold` is compatible only when its centre is both:

```text
perpendicular distance to current joint ray <= HalfSupport
absolute along-ray endpoint difference        <= SupportSize
```

This rejects a parallel/tangential neighbour as an owner while recognizing an
alternative depth layer of the same measured surface.

An existing non-HOT compatible path is unresolved and uses the existing SSD
load/held-observation mechanism. It must never be treated as empty merely to
allocate Knew.

### 5.3 Remove GPU first-wins source authority

Reuse existing candidate, resolve, tile-bit, and queue storage. Add no winner
buffer and no new dispatch.

- the existing resolve sequence deterministically routes/re-writes each
  candidate to K, Kold, or Knew before allocation;
- candidate-bit atomics only deduplicate the final canonical key;
- the surface queue carries the canonical key, not source eye/pixel/quality;
- integration deterministically reprojects the final key into the immutable
  joint reference observation and re-derives H/N/authority/evidence weight;
- delete packed-source code and dead queue fields.

### 5.4 Evidence

Evidence/hysteresis remain unchanged. A valid event still converges by q^2;
the authority class controls whether the event may allocate/migrate/carve, not
the numerical history ABI. Off-axis SUPPORT may confirm the compatible owner
and update its canonical RGB; it cannot create a competing normal layer or
decrement another owner.

### 5.5 S2 acceptance

```text
full common FOV can discover previously unknown world
off-axis/grazing hit near a compatible owner routes to SUPPORT
tangential wall growth remains DISCOVERY
only authoritative hit can create replacement Knew near Kold
cold compatible owner holds the same observation unresolved
queue contains no packed source measurement
GPU scheduling cannot choose colour/evidence source
negative/tile/chunk/M8 boundaries are identical
no new buffer, hash, dispatch, or persistent authority
```

Commit:

```text
fix(scan): stabilize canonical surface ownership
```

## 6. S3 -- REVISION-ONLY CARVE ATTENTION

### 6.1 Keep sparse Q_SCAN

Retain distance-only sparse presence traversal and carve-active tiles. Do not
introduce per-pixel DDA, a ray queue, or another spatial structure. Q_SCAN may
overinclude; the exact classifier provides mutation authority.

### 6.2 One joint endpoint for SURFACE and FREE

For an existing carve-active K:

```text
project K into the deterministic joint reference lattice
-> read the same joint H,N used by SURFACE
-> classify K against that one current ray
```

There is no L/R strongest vote. SURFACE from the same immutable observation
always wins over FREE. If the candidate bit says this observation targets K,
K cannot be freed by that observation.

### 6.3 Depth attention cone

Geometry authority is derived from the two depth projections and immutable
depth-eye poses, never from PCA fisheye UV and never from the current render
camera.

For both depth views compute normalized distance from optical centre. The
initial device contract uses a central full-authority region and a bounded
transition to exact zero authority outside it. The concrete inner/outer values
must be frozen from centre/mid/edge telemetry before the S3 commit; they must
not become serialized tuning or a runtime fallback.

```text
inside inner cone       attentionWeight = 1
inner..outer transition smooth deterministic falloff
outside outer cone      attentionWeight = 0 and classification is UNKNOWN
```

REVISION requires:

```text
joint measurement valid
both depth projections inside outer cone
non-degenerate joint incidence
joint normal-direction uncertainty <= 12.5 mm
```

PCA validates the four-stream measurement and supplies colour; it does not
grant geometric mutation authority.

### 6.4 Preserve corrective dynamics

FREE remains:

```text
q^2 * clearanceWeight(25..150 mm) * attentionWeight * FreeEvidenceScale
```

Outside the cone it is exactly zero, never `max(1)`. Near the measured surface
the existing clearance ramp remains weaker than clearly foreground state.
ON/OFF hysteresis, confidence cap, NEEDS_CARVE retirement, surface-before-carve,
and `OFF+1` replacement clamp remain unchanged.

### 6.5 S3 telemetry and acceptance

Use aggregate GPU counters only, sampled by the existing low-rate control
path. No per-frame readback. At minimum distinguish centre/mid/edge accepted
joint samples and DISCOVERY/SUPPORT/REVISION, blocked off-axis mutation,
replacement, FREE, and same-observation conflict.

```text
new peripheral world is discovered
known peripheral surface is supported but not moved/carved
central repeat can confirm or revise
temporary foreground is removed only after central replacement exists
no transient hole while Kold crosses OFF
same-observation surface/free conflict = 0
dual-eye canonical owner count = 0 by construction
```

Commit:

```text
fix(scan): restrict revision and carve to depth attention
```

## 7. Verification gates after every cut

```text
Tools/unity/run_merkaba_tests.sh
Tools/shaders/audit_merkaba_hash_spirv.sh
git diff --check
```

Manually inspect every changed shader for:

```text
Quest Vulkan UAV/storage bindings <= 8 per kernel
no divergent return before a group barrier
no cross-workgroup spin
no uint64/div/mod/helper loop in M8 hash
no new 1x1 kernel iterating a data domain
no buffer allocation above 128 MiB
read-only aliases used where no write occurs
no duplicate measurement/world authority
```

After S1-S3: full tests, shader/SPIR-V audit, clean Android APK, push all
commits, and clean install on the single authorized Quest when available.

## 8. Device proof (not a fourth architecture cut)

The final APK must collect actual `StereoRgbdRefine`, SURFACE, CARVE,
WORLD_QUERY, READOUT_BUILD, and DRAW GPU timestamps plus semantic counters.
Test white frontal wall, textured frontal wall, sloped ceiling, corner/doorway,
peripheral discovery, peripheral revisit, central revisit, temporary-object
removal, rapid head rotation, and negative/M8 boundaries.

Static success does not prove sensor timing, PCA image/metadata identity, clock
epoch equivalence, cone quality, or Quest performance. Those are accepted only
from the installed APK run. No further preventive redesign is authorized
before that evidence.
