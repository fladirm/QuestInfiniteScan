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

RT1 generated selection/transport is an implementation checkpoint, not RT4
acceptance. Its root ownership/incidence distinction, source/carrier masks,
child addresses and phase ancestry are already wired; do not regenerate a new
geometry or redo that cut.

RT2 implemented: separate observation-local SignalItems, no fixed receipt in
FlowerDetail; no 1336-task catalogue, quantum, TileRefinementCursor, persistent
invalidation/ACK, INVALIDATION_OWED or AdvanceRefinementStage. Structural epoch
cuts, unique receiver invalidation and R1 publication share this snapshot's
serialized GPU graph. R1 FULL admission and fine COLD/write outcomes are local.

CURRENT CHANGE=PrepareFlowerOwners groups immutable record indices by actual
measured owner using a tile-local parallel histogram/prefix/scatter. A stamped
16-word mask/rank index gives the fixed peer's range without a whole-bin rescan.
Root/L1/L2 and the carrier resolver dispatch one WG per measured owner using
GPU indirect counts, not four 128-owner batches. Their three 512-word owner
banks are gone. The existing 27-address halo is resolved once by the tile
grouping pass; owner WGs load it without repeating spatial hash discovery.
Two endpoint ranges use the unchanged metric/root/intersection predicates.
Peer ranges must belong to the current stamped bin, including after NEW/OPEN.

ABI=23, 31 pipelines, 44 resources. No buffer added or enlarged. The 32 MiB
snapshot buffer reuses dense 32-byte R1 changes (capacity 1,044,350) before
grouping, then holds record indices, stamped tile/owner ranges and skin items.
Measured-owner and skin capacities are 79,343 each; overflow is explicit, a
failed tile does not publish partial owner ranges, and no semantic work survives
Finalize. These are transient budgets, not world or logical-thread limits.

NEXT=RT2 footprint-directed skin evidence and resolver original-root acquisition.
The resolver still acquires 52 originals for one active owner; replace eager
alternatives with required source/phase dependencies. Finish ERASE pending epoch
path, conditional sparse allocation and owner-local storage/allocator outcomes.
In particular parallel owner writers must not turn allocator contention into
unreported lost detail. Start at FlowerResolveSignals / PrepareFinePacket and
SignalBody; do not redo grouping, the fixed level graph or R1/peer epoch cut.

COMPILE=Unity Editor codegen/C# PASS in realtime-rt2-actual-owners.log.
Targeted exact native compile/spirv-val PASS: grouped-lookup Prepare 26316 B /
1070 body / 6232 B shared / 4 RW; resolver 704608 B / 36263 body /
14496 B shared / 6 RW. Root/L1/L2 use 14564 B shared / 5 RW each.
No CPU geometry backend or scan-decision readback added.

RT3 NOT STARTED: presentation packet and exhaustive completion/readout/export
paths still need replacement; export V atlas remains. RT4 full tests, shader
and command graph gates, Unity Quest APK, install and device acceptance have
NOT run. Baseline tests/APK are not rewrite acceptance. Do not start RT4 or
claim completed rewrite until RT2 and RT3 are fully connected.
