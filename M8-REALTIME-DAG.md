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

RT2 checkpoint: fixed 64-tile/512-owner skin receipts have been removed from
FlowerDetail. The resolver visits measured owners and generated reached
carriers, appending validated SignalItems into a separate 32 MiB GPU buffer.
GPU indirect RGB/V consumers serialize each owner's run writers, execute the
three fixed signal substitutions, and neither read nor advance a world cursor.
The two signals are independent. Native/managed resource and dispatch ABI 22
is wired; snapshot completion/token bounds the handoff's semantic lifetime.
No CPU solver/readback was introduced. No APK/device/test-suite run.

RT2 geometry checkpoint: GeometryNodeAt, the 1336-task catalogue, quantum and
TileRefinementCursor have been removed from production. Editor derives phase
addresses (20 original, 24 L1, 192 L2 unsigned relations) and inverse support
cell incidence from the existing child-loop authority. GPU measurements mark
dyadic cells; lanes expand each touched cell once into reached relation bits.
Only reached bits, both signs, enter the unchanged metric/peer predicates.
This is a conservative inverse of the existing endpoint-support filter, not
a claim that all actual-parent/footprint routing and fan-out are finished.

RT2 epoch checkpoint: persistent invalidation receipts/ACK and the three stage,
owed/lease counters are removed. FlowerCommit prepares R1 changes and applies
structural owner epochs; an observation-local deduplicated receiver queue
invalidates dependent peer phases, then PublishFlowerR1 writes M8. The same
native queue completes these dependencies before fixed Root/L1/L2 entrypoints.
No AdvanceRefinementStage or world program counter remains. The 32 MiB transient
buffer reuses its prepared-R1 payload for skin only after a GPU transfer barrier.
Native/managed ABI 22 has 30 pipeline identities; no resource added in this cut.

NEXT=RT2 actual measured-owner fan-out and local publication outcomes. Current
geometry still reduces a touched tile per WG. The tile-wide dual-ready gate
has been replaced by a per-owner FULL guard before positive publication;
negative work and unrelated owners continue. Geometry COLD/write status no
longer breaks the reached-relation loops; residency requests are collected
after that finite work. Malformed input/publication failures still need their
own exact handling. Do not call this the completed measurement-driven scanner.
Also finish pixel/footprint-to-generated-child reach before all skin evidence
work; the current compact signal consumers retain complete footprint predicates
and prune by actual signal splits, but do not yet have that fine routing.
Resolver acquisition still retains an original-root packet for one active
owner; remove its remaining eager alternatives with reached dependencies.
Start at FlowerDrainBody and ReduceFineEndpoint: reached masks are still a
tile-wide union and the packet walks four owner batches. Keep observation
record grouping on GPU, without rescanning every tile bin for each owner.
Finish the ERASE pending invalidation path as part of RT2. Do not redo the
signal-buffer handoff, epoch cut or completed reached-relation codegen.

RT3 still owns removal of the remaining presentation reconstruction packet,
completion/count/emit re-evaluation and export V atlas. RT1 did not close those
tasks or the shader-size gate. Last RT1 Compact compile: 1263272 B / 67046 body /
29464 B shared / 7 RW / 256 lanes. ResolveFlowerCarriers: 708072 B / 36758 body /
20628 B shared / 6 RW at RT1. Current RT2 resolver: 718424 B / 36967 body /
20692 B shared / 6 RW. Local-outcome FlowerCommit: 865844 B / 46218 body /
24700 B shared / 8 RW. Root: 528996 B / 27612 body / 5 RW; L1:
729036 B / 38107 body / 5 RW; L2: 728956 B / 38107 body / 5 RW.
Signal RGB: 305536 B / 15696 body; V: 500384 B /
25370 body; both 64 lanes / 204 B shared / 4 RW. Finalize retains 8 RW.
Unity codegen/C# and targeted native shader compile/spirv-val PASS.
No tests/APK/install/device run. No measured speedup claim. Baseline tests
378/378 and installed APK 03280202 are not rewrite acceptance. RT2 and RT3
must finish before RT4 validates and delivers the complete rewrite.
