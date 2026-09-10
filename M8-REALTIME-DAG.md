# Realtime rewrite — task cursor

AUTHORITY=M8-REALTIME-MEASUREMENT-DRIVEN-CONTRACT.md
BASE=9ab1f687691f91fbefb62e46cc308f8fb5327a8a
GOAL=active, created 2026-09-10; replaces the retired 'stop' goal
CURRENT=RT2
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

Depends: baseline. Status: IMPLEMENTED; compile checkpoint, integrated acceptance in RT4.

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

Depends: RT1. Status: OPEN.

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

RT1 generated selection/transport implementation is complete: R1 witness
incidence, source/carrier masks, child creation addresses, sign-triple inverse,
and present-phase ancestry are shared by CPU oracle/export and GPU consumers.
GPU acquisition starts with R1 and requests higher-shell anchors only through
surviving source masks; carrier batches request their own inherited/phase
dependencies. Missing phase families do not trigger ancestor root evaluation.
The duplicate pure-original array is removed; complete raw/shared evidence and
invalidation guards remain distinct. No change to root ownership or geometry.

NEXT=RT2: replace the persisted 1336-task/cursor/quantum program and fixed skin
receipts with observation-reached relations and compact snapshot-local signal
items; update native dispatch/resource consumers together. Start with
MerkabaFlowerRefinement.hlsl, MerkabaFlowerDrainBody.hlsl and their Sidecar/
Integration/native schedule consumers. Do not revisit the completed RT1 tables.

RT3 still owns removal of the remaining presentation reconstruction packet,
completion/count/emit re-evaluation and export V atlas. RT1 did not close those
tasks or the shader-size gate. Compact compile: 1263272 B / 67046 body /
29464 B shared / 7 RW / 256 lanes. ResolveFlowerCarriers: 708072 B / 36758 body /
20628 B shared / 6 RW. Unity codegen/C# and exact native compile/spirv-val PASS.
No tests/APK/install/device run. No measured speedup claim. Baseline tests
378/378 and installed APK 03280202 are not rewrite acceptance. RT2 and RT3
must finish before RT4 validates and delivers the complete rewrite.
