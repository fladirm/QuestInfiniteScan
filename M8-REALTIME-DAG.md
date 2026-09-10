# Realtime rewrite — task cursor

AUTHORITY=M8-REALTIME-MEASUREMENT-DRIVEN-CONTRACT.md
BASE=9ab1f687691f91fbefb62e46cc308f8fb5327a8a
GOAL=active, created 2026-09-10; replaces the retired 'stop' goal
CURRENT=RT1
STATE=implementation
HOTPATH=GPU-only sensor/scan/live page compiler/draw. CPU codegen/oracle is
  not a runtime fallback. Frozen offline export remains outside that hotpath.
DELIVERY_GATE=No APK, install or Quest verification in RT1–RT3. Only RT4,
  after the complete rewrite. Intermediate cuts use compile/codegen checks
  and commits, not device builds or acceptance runs.

After compaction read this file, the new contract and the latest lasttrue.md
receipt, then the complete current-cut source files. Do not replay old RUN04,
RUN08 or historical stalled goals. Never infer completion from an old APK.

## RT1 — generated reached-symbol decode

Depends: baseline. Status: OPEN.

Files: MerkabaSphereFlowerAuthority partials, CarrierAdmission, Carrier,
Editor/MerkabaSphereFlowerCodegen.cs, generated CPU/HLSL/blob, shared decode
consumers in MerkabaFlowerCommit/Geometry and SphereFlowerReader.

Implement generated admissible construction/child masks from existing exact
incidence, distinct from RootOwner masks. Use reached roots and present
records to enumerate actual carriers; CPU/HLSL use one semantic recipe.
Replace R1FlagWitness's 48*4*3 search and its repeated metric solves through
that lookup. Preserve all required child/shared-root predicates. Do not replace
it with intersection of independent singleton sector-owner masks.

Close: generated lookups consumed by production; independent oracle comparison
available; compile coherent; commit with receipt. No new surface authority.

## RT2 — snapshot-local scanner and skin

Depends: RT1. Status: NOT STARTED.

Files: MerkabaFlowerRefinement/DrainBody/Commit/Sidecar/SkinSignalBody,
MerkabaIntegration.compute, ObservationBins, Integrator, native executor
generator/ABI/resource consumers, dual lifecycle and persistence readers.

Replace ordinal stage machine with observation-reached root/L1/L2 work and
compact actual SignalItems. Remove cursors, PHASE_TASKS, quantum, fixed skin
receipts in FlowerDetail and every cross-snapshot retry consumer. Keep true
parent/global barriers. Make allocation conditional; distinguish COLD requests,
ambiguity and capacity. Finalize releases every snapshot packet. Wire strict
versus bootstrap sensor telemetry without weakening admission silently.

Close: no old schedule/state remains in production or saved world; same-snapshot
direct/dual precedence and reached refinement are implemented; compile; commit.

## RT3 — readout/export decode and V preservation

Depends: RT1, RT2. Status: NOT STARTED.

Files: MerkabaReadout.compute, FlowerGeometry/Support/SkinReadout,
MerkabaFlowerPresentation, SphereFlowerReader, MaterialBake, GlbWriter,
TilesetWriter, Exporter and atlas/package consumers.

Use DecodeFlower for actual carriers in GPU dirty-page and CPU frozen-export
paths. Remove 52-alternative/128-carrier production packets and exhaustive
neighbor Build. Completion starts at actual direct boundary incidence; DIRT
coverage sees actual carriers. Preserve indexed draw and immutable FRONT.
Bake RGB and V micro-normal atlas with the shared skin evaluator; preserve
exact thread records and truthful unlit/fallback semantics. No new displacement.

Close: all presentation/export consumers rewired, no old solver fallback,
bounded frozen export/resume remains, compile; commit.

## RT4 — integrated acceptance and delivery

Depends: RT1–RT3. Status: NOT STARTED.

Reconcile tests and execution consumers, then one full validation/fix pass:
Tools/shaders/audit_merkaba_compute_spirv.sh
Tools/shaders/audit_merkaba_command_graph.sh
Tools/unity/run_merkaba_tests.sh
Tools/gltf/validate_merkaba_glb.sh
Tools/unity/build_merkaba_apk.sh

Use the configured /mnt/kingston-unity host and bounded build runner. Verify
install hash and native links; measure strict/touched/occupied/triangles and
all GPU stages on connected Quest, including sustained scan, sleep/resume,
new-object-in-FREE, SAVE/OPEN, frozen export/import. Do not delete app data.
Commit fixes, push, give commit/APK/evidence links. Goal completes only when
the new contract is actually satisfied; otherwise record the exact open gap.

## Current handoff

RT1 R1 admission replacement implemented: codegen emits 54 unique child R1
loops, six direction masks and same-construction peer masks. Production
M8FlowerR1FlagWitness now visits reached loop bits instead of 48*4*3 flags.
Root ownership is unchanged; peer lookup is generated child incidence.
Unity codegen and native FlowerCommit compile PASS; no tests/APK/device run.
RT1 second checkpoint: shared generated M8FlowerReachedCarriers selects by
original-anchor incidence (absence only), not by root ownership. GPU direct
and final carrier requests plus CPU export/neighbor coverage pop reached bits;
the unconditional 0..127 walks in those consumers are removed. Parent coverage
accounts for excluded constructions without inventing direct evidence.
CPU seven-root-sign closure now uses the same generated triple inverse as HLSL.
This is necessary-source selection, NOT the completed actual-branch decoder.
RT1 third checkpoint: CPU frozen decoder memoizes only requested original/knot
reads and direct results; completion evaluates actual boundary candidates and
revisits only affected carriers. This CPU decoder is NOT used by live scan or
readout. Child-loop address/creation is now one editor-generated lookup for
CPU and GPU; the incidence derivation is editor/oracle-only.
GPU carrier wedge/sign predicates now run as independent lane items. Shared
site supports are evaluated once; owner-prefix scratch is reused between its
disjoint lifetimes. Original endpoint and selected-site receipts stay exact.
NEXT=replace M8FlowerPacketAcquire's original/base/site reconstruction packet
with reached L1/L2 dependencies and present-phase decode, then RT2's world
cursor/1336-task/receipt removal. Selection/cooperative fan-out is not the
completed decoder; no RT1/RT2 closure may be inferred from this checkpoint.
Compile/codegen PASS. Compact 1248772 B / 66194 body / 31340 B shared, 7 RW:
shader-size gate OPEN; no runtime speedup claim. ResolveFlowerCarriers compile
PASS, 707884 B / 36752 body / 20628 B shared. No tests/APK/install/device run.
RT1 remains OPEN; RT2/RT3 not complete. Baseline tests 378/378 and installed
APK 03280202 are not rewrite acceptance. Delivery still waits for RT4.
