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

MEASURED OWNERS=PrepareFlowerOwners groups immutable record indices by actual
measured owner using a tile-local parallel histogram/prefix/scatter. A stamped
16-word mask/rank index gives the fixed peer's range without a whole-bin rescan.
Root/L1/L2 and the carrier resolver dispatch one WG per measured owner using
GPU indirect counts, not four 128-owner batches. Their three 512-word owner
banks are gone. The existing 27-address halo is resolved once by the tile
grouping pass; owner WGs load it without repeating spatial hash discovery.
Two endpoint ranges use the unchanged metric/root/intersection predicates.
Peer ranges must belong to the current stamped bin, including after NEW/OPEN.

ABI=25, 31 pipelines, 44 resources. No buffer added or enlarged. The 32 MiB
snapshot buffer reuses dense 32-byte R1 changes (capacity 1,044,350) before
grouping, then holds record indices, stamped tile/owner ranges and skin items.
Measured-owner and skin capacities are 79,343 each; overflow is explicit, a
failed tile does not publish partial owner ranges, and no semantic work survives
Finalize. These are transient budgets, not world or logical-thread limits.

REQUIRED ROOTS=resolver original evidence is acquired only through R1-led source
survivors and generated knot dependencies with present phase records. A known
mask guards every packet read; original evidence is reused by actual carriers,
not eagerly evaluated as 52 tasks. Geometry reacquires only a required coarse
phase source after its endpoint buckets retire. The resolver now evaluates
18 source incidences and 48 wedge/sign predicates on independent lanes; only
their exact combination/publication remains scalar. Ordered COLD dependency
early-outs remain intact. CPU did not gain any hotpath work.

SKIN REACH=the resolver projects current measured-owner pixel footprints
onto evaluated L2 wedge frames and descends through the three existing generated
chambers. It retains every possible intersected branch; 57 reached group bits
occupy the unused final 8 bytes of the existing 224-byte SignalItem. RGB/V use
those bits to visit only reached split groups. An unavailable metric enclosure
retains the wedge's possible support masks rather than suppressing RGB or
manufacturing certainty. Actual seven-child complete-support predicates are
unchanged. This is a snapshot work mask, not persistent subdivision/cursor.

ERASE=uses the shared R1 preparation/epoch cut, unique peer
invalidation and M8 publisher in one GPU job. The finalizer only dirties affected
pages and retires the transient header. COLD peers enqueue existing SSD loads;
unrelated resident brush targets proceed. No M8-first deletion, pending epoch
finalizer, held ERASE retry or residency-epoch wait remains. Its token comes from
the same unique observation-token allocator; no collision with scan stamps.
Native schedule membership is explicit, so reusing peer/publication pipelines
does not import every pipeline between their numeric indices into ERASE.

RETIREMENT=scan and ERASE release at their GPU fences, not an AttemptCompletion
readback. RequestAttemptCompletion and its CPU result fields/callback are gone.
GPU indirect counts still own work. Existing SSD counter telemetry/IO remains;
storage capacity maintenance now recognizes a retired one-shot observation,
not the removed unfinished-observation condition. UI dirtiness is conservative
after a retired mutation; it does not select geometry or schedule refinement.

ALLOCATION=GPU-indirect publication/recount across the three fixed address
dependencies Block -> Chunk -> Tile. Count's NEXT packet reuses words 0..2
before Reserve; the active recount uses the existing words 12..14. HOT counts
are retained, COLD-only requests do not reset them. Final storage claims are
published before dual reuses their backing. No new shader, buffer or CPU work
decision. Dual no longer vetoes the entire observation on one unresolved direct
neighbour; complete-support/COLD decisions remain local.
Native dispatch modes belong to schedule commands, not only pipeline names.
The observation graph records 40 commands: 18 allocation/reset indirect,
3 recount indirect and 19 other commands. Zero-work commands remain visible
in accounting; no <=10-dispatch or speedup acceptance is claimed. C# timing
capacity is codegen-checked against this complete schedule.

NEXT=RT2 owner-local allocator outcomes: parallel owner writers must not turn
global arena-lock contention into lost available detail. Close malformed-peer
publication handling and verify complete THROUGH deletion against the current
contract. Do not redo grouping, roots, footprint reach, ERASE, fence retirement
or conditional allocation. RT3 remains next after these RT2 gaps.
RT4 must cover exact reach, partial/COLD ERASE, epochs/peer invalidation, native
schedule membership and no completion-readback-dependent scan decisions.

COMPILE=Unity Editor codegen/C# previously PASS in realtime-rt2-actual-owners.log.
This shader-only change: targeted exact native compile/spirv-val PASS in
/tmp/m8-rt2-reach-final-ifmx1p13: resolver 935964 B / 48376 body /
15464 B shared / 6 RW / 128 lanes. RGB/V compile in
/tmp/m8-rt2-skin-reach-3hv4y2in: 307940/503004 B / 15835/25509 body /
204 B shared / 4 RW / 64 lanes. No additional dispatch or buffer allocation.
No CPU geometry backend or scan-decision readback added.

ERASE COMPILE=Unity C#/Editor codegen PASS:
/mnt/kingston-unity/Builds/QuestMerkabaScan/realtime-rt2-erase-retirement.log.
Targeted exact native shader compile/spirv-val PASS, not full audit:
/tmp/m8-rt2-erase-bindings-z2mogf89 (ERASE, FlowerCommit),
/tmp/m8-rt2-erase-final-h8l_o2uo (query/finalizer),
/tmp/m8-rt2-erase-qjwrhbr1 (shared peer/publisher).
ERASE 67196 B / 3309 body / 124 B shared / 8 RW / 128 lanes;
query 25924 B / 1050 body / 52 B shared / 5 RW / 64 lanes;
finalizer 23516 B / 948 body / 8 B shared / 6 RW / 128 lanes.
ALLOCATION COMPILE=Unity C#/Editor codegen PASS:
/mnt/kingston-unity/Builds/QuestMerkabaScan/realtime-rt2-allocation.log.
Targeted exact native glslang/spirv-val PASS in
/tmp/m8-rt2-allocation-i6kujr5d: Count 25068 B / 1127 body / 0 shared / 8 RW;
Reset 7560 B / 242 / 12 / 5; Reserve 7816 B / 277 / 2060 / 4;
Emit 25012 B / 1122 / 0 / 4; certificate Reduce 8180 B / 300 / 8196 / 6;
dual 609132 B / 30721 / 112 / 8, still 64 lanes.
Native plugin/full embedded set is not rebuilt; ABI25 is compiled into the
final plugin/APK only in RT4. No APK, suite, device test or speedup claim.

RT3 NOT STARTED: presentation packet and exhaustive completion/readout/export
paths still need replacement; export V atlas remains. RT4 full tests, shader
and command graph gates, Unity Quest APK, install and device acceptance have
NOT run. Baseline tests/APK are not rewrite acceptance. Do not start RT4 or
claim completed rewrite until RT2 and RT3 are fully connected.
