# Realtime rewrite — task cursor

AUTHORITY=M8-REALTIME-MEASUREMENT-DRIVEN-CONTRACT.md
BASE=9ab1f687691f91fbefb62e46cc308f8fb5327a8a
GOAL=product goal is blocked; it has not been marked complete. Current user
  overrides (direct-only/greenfield/no intermediate APK) are the authority.
CURRENT=RT4
STATE=RT4 direct-only greenfield source cut host-validated (2026-09-11).
  Dual execution/storage/veto/DIRT/dependent completion removed. M8 geometry,
  RGB/V and fine epochs retained. Four session streams, nine record kinds;
  manifest v5/184 B, record v6; no migrations or historical dual replay.
  Native ABI29: 32 pipelines, 41 resources, 39 timing slots. Unity 385/385,
  exact shader audit 75/75, ARM64 native build and GLB interoperability PASS.
  Gate logs: /mnt/kingston-unity/Builds/QuestMerkabaScan/direct-only-audit.
  No new APK/install/device run. The last installed APK is obsolete and failed
  device acceptance. APK only after the completed scanner; no intermediate build.
NEXT=Resolve the user's all-shader/no-first-start-compile requirement before
  final APK. PC HLSL->SPIR-V is implemented, not Adreno driver-binary compilation.
  Connected Quest advertises VK_KHR_pipeline_binary and cache-control; Unity
  feature enablement and portable build input are not established. Permission
  for any preparation-time Quest compilation was asked, not granted. Do not
  prewarm the headset or claim SPIR-V/cache alone guarantees no compilation.
  Command graph remains 39/7/6 scheduled scan/readout/ERASE; no <=10 claim,
  and no runtime/performance acceptance claim without the final device run.
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

Depends: RT1. Status: IMPLEMENTED; compile checkpoint, integrated acceptance in RT4.

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

Depends: RT1, RT2. Status: IMPLEMENTED; compile checkpoint, integrated acceptance in RT4.

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

Depends: RT1–RT3. Status: IN PROGRESS; direct-only greenfield host gates pass.
No APK for this source cut. Final packaging and device gates remain open.

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

RT4 SUITE=391/391 PASS, no ignored/skipped/inconclusive cases; full Kingston
  EditMode run 2026-09-11 03:32:13Z..03:33:40Z:
  /mnt/kingston-unity/Builds/QuestMerkabaScan/realtime-rt4-unity-fixed/TestResults/
  merkaba-results.xml and merkaba-tests.log. Includes the retained positive
  geometry fixtures, paired RGB/V atlas and two new IEEE rounding comparisons.
  Unity rejected three struct-valued conditional expressions in the first
  run (378/391); explicit branches/numeric component selection fixed them.
  The full suite then passed, without weakening predicates or expectations.
  Full exact native audit 78/78 PASS: production=61, native=33, oracle=17;
  all production size/body, shared-memory and descriptor hard gates pass.
  Compact: 930,876 B / 49,915 instructions / 26,552 B shared / 6 RW / 256 lanes.
  Emit: 647,240 B / 34,579 instructions. No budget was relaxed.
  Count/emit are distinct compiled entrypoints in the SAME seven-command
  readout schedule. Shared coverage, rotation and halo results replace repeated
  metric bodies; no predicate, source authority or CPU backend was introduced.
  Native ABI is 28 (33 pipelines, 44 resources, still 41 timing slots).
  Evidence: realtime-rt4-gates/spirv-unity-fixed.log and per-entry metrics.
  GLB interoperability PASS: zero Khronos errors/warnings and independent
  NodeIO load; merkaba-fixture.glb.validation.json in the same TestResults.
  This fixture is untextured DIRT; paired RGB/V coverage is in the full suite.
  Command graph: 41 observation / 7 readout / 6 ERASE scheduled dispatches;
  actual nonzero device work has not been measured.
  APK PASS from pushed source 5762d74817af8e34d0cb8746b8dca6cf8d98ad88:
  /mnt/kingston-unity/Builds/QuestMerkabaScan/realtime-rt4-apk/QuestMerkabaScan-release.apk
  74,471,044 B; SHA256 91a9d02b277cfad60dad70a9c5b83a71cf9d6c5707caca42f758887095e5c7fb.
  Unity build GUID 0890ba043d32468ca0dbd1bf960638c4; v2 signature verified.
  All 33 exact audited SPIR-V payloads occur byte-for-byte in the packaged
  AArch64 native plugin. Prepare/build logs are beside the APK. No shader/C#
  errors; no build OOM. This is an APK, not only a native plugin checkpoint.
  Deploy script returned 2: no authorized Quest attached; USB also lists none.
  Next: attach/authorize Quest, install this APK and perform device acceptance.
  Do not rebuild/rewrite already validated source merely to resume the cursor.
  Visible scan, sustained timings, sleep/resume and session/export round trips
  remain unverified on the device. The goal is active, not complete.

## Implementation receipts (historical compile results)

The metrics below record their individual cut, not the current validation
state. Current handoff above and the latest lasttrue.md receipt take precedence.

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

ABI=28, 33 pipelines, 44 resources. No buffer added or enlarged. The 32 MiB
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

ERASE=uses shared read-only R1 preparation, source epoch cut, unique peer
invalidation and M8 publication in one GPU job. The finalizer only dirties affected
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
The observation graph records 41 commands: 18 allocation/reset indirect,
3 recount indirect and 20 other commands. Zero-work commands remain visible
in accounting; no <=10-dispatch or speedup acceptance is claimed. C# timing
capacity is codegen-checked against this complete schedule.

OWNER UPDATES=existing owner lookup, phase replacement/insertion within its
allocation and phase removal no longer acquire the global buddy lease. Existing
RGB/V split replacement writes exactly seven values in place, preserving the
allocation, other groups, masks and program. RGB uses uint4, V uint2 transfers.
Actual growth now uses the atomic buddy bitplanes below; RGB never leases the
unrelated detail arena. Exclusive measured-owner WGs and raw-reader retirement
remain required.

ARENAS=free/allocated buddy bitplanes plus three availability summaries replace
the global allocator lease, inside the existing metadata reservation. Atomic
bit removal owns a block; pair CAS coalesces retired free buddies. Summary
misses fall back to the real bitmap before returning capacity. Owner/index
creation uses initialized-then-CAS publication; losing speculative allocations
are reclaimed immediately. Phase/RGB/V growth, epoch wrap, storage import and
page publication all use this allocator. Optical program references use atomic
refcounts. BUSY now denotes unretired readers, not allocator contention. No
new buffer, dispatch, CPU geometry, persistent workset or spin lock was added.

R1 PUBLICATION=source and actual receiver fine directories, allocated span
counts, sorted phase/run keys and live phase intervals are validated without
mutating fine data in FlowerCommit/ERASE. An absent owner is distinct from a
malformed directory. Local malformed input reports failedWrites, not capacity
or a global scan veto. The next GPU boundary cuts source epochs/runs, then
unique receivers cut dependent rows, then PublishFlowerR1 writes M8. The source
pass is indirect over the existing prepared records, not a world sweep or
cross-snapshot continuation. This additional real boundary is necessary for
read-only preflight and <=8 writable bindings: combining fine and banked M8
publication measured 9 RW and was removed. Native and Unity command consumers
are both connected. Observation records 41 commands; ERASE records 6.

RGB/V EXPORT=implemented paired RGB and linear V-normal atlas pages using the
same EvaluateSkinDrawSignal and additive A3/A4/A5 gradients. Cell allocation
uses the union masks, not RGB alone. Wedge frames come from evaluated positions;
padding uses active chart support only. Shared positions/indices are unchanged.
GLB omits per-knot NORMAL/TANGENT so its flat L2/UV frame is not replaced by an
arbitrary incident normal. The unlit foreign preview accepts absent normals;
normalTexture is explicitly a derived lit fallback, not albedo or displacement.
Native records remain the exact RGB/V representation. Both pixel streams are
flushed/checksummed under one export receipt; resume policy is versioned.
Unity C#/codegen compiled; no tests, APK or device acceptance were run. RT4
must verify union-only V, nested gradients, mirrored/wound UV frames, padding,
paired PNG/resume and native-package accessor offsets; reconcile old NORMAL
stream/RGB-only tests and the legacy PBR-only GLB validation fixture.

COVERAGE=the existing exact (carrier, cell, face, half) predicate now occupies
all 256 lanes: 32 lanes per actual carrier, not eight serial cell/face loops.
Only still-uncovered DIRT faces reach neighbor owners via the generated face
corner bounds and the existing +/-2a knot support. A 69-word transient mask
replaces unconditional neighbor endpoint/geometry queries over all 13^3 slots.
The CPU frozen reader uses identical per-face bounds and a visited bitmask;
it caches only reached neighbor footprints and excludes unrelated cached ones.
No coverage predicate, completion rule, counter, buffer or dispatch was added.
Unity C#/codegen and the exact Compact target compilation passed. Compact is
still 1,268,260 B / 67,329 body instructions, over the production size gate;
29,740 B shared, 256 lanes, 7 RW. No speedup/parity/device acceptance claim.

SOURCE CACHE=the fixed 52*(root + raw root + receipt) readout packet is gone.
The generated node/sign directory retains classification and endpoint receipts;
only reached nonempty Tag/interval values allocate compact scratch slots.
Raw/final bit-identical values share a slot. J is reconstructed exactly from
owner plus generated offset, with an explicit empty-J bit. The two endpoint
base restrictions stay private to their query lane until publication. Present
L1 bases use rank of reached strands and implicit J, not 144 addressed roots.
The cache checkpoint uses 26,556 B shared / 7 RW / 256 lanes; subsequent
transport compilation is recorded below. This is not RT3 closure.

ANCESTRY=production GPU and frozen CPU decode now consume each actual source
through the same generated Begin/ReadTerm/Finish transport predicates. Full
parent/ancestor root and record/key arrays are removed from the production
predictor. Two four-integer terms retain ordered fixed-point rotations; source
validation, epoch/key/sector checks, half-open witness and orientation remain.
The offline oracle bridge uses these same predicates. CPU frozen decode also
reuses its already classified child base rather than recomputing its loop.
Unity C#/codegen and five exact compute targets compiled. Compact is still
1,285,712 B / 68,316 body instructions: FAIL. Resolver is 936,388 B / 48,353;
Root 525,704 / 27,471; L1 739,632 / 38,702; L2 739,576 / 38,702. No new
dispatch/resource/CPU hotpath or APK. Integrated parity remains RT4 work.

JUNCTION=completion's ordered donor preflight requests eight independent R3
alternatives only after an actual candidate has certain anchors. Eight GPU
lanes evaluate them once per frozen owner. The 40-word q/tag/dual receipt cache
uses the existing control-scratch gap, survives only this owner compilation,
and serves every reached candidate batch. Junction precedence, per-candidate
required dual receipts, sign/orientation checks and all sixteen child proofs
remain. The old serial ReadR3Junction/PrepareCompletionPetal path is removed.
Frozen CPU decode lazily caches the same eight inputs; its uncached oracle
uses the same evidence collector. Unity C#/codegen and exact Compact compile
PASS; 1,291,628 B / 68,638 body / 26,556 B shared / 7 RW / 256 lanes / 75
barriers. Size gate STILL FAILS. No runtime speedup or parity claim.

SKIN EMISSION=32 lanes per actual carrier now materialize its root and stored
union groups through one CompileSkinRegion call site. A five-step integer
bit selection plus the existing generated inverse maps compact sample indices
to locality; absent groups are never visited. The resident RGB/metric layouts
are acquired once and shared in twenty existing footprint-scratch words.
All sample writes precede the header; malformed samples abort page publication.
Uniform M8-only carriers still allocate no skin bytes. The scalar program and
its duplicate region evaluator calls are removed, not retained as a fallback.
Exact Compact compilation PASS: 1,232,316 B / 65,201 body / 26,556 B shared /
7 RW / 256 lanes. Size gate STILL FAILS. No CPU path or dispatch was added.

DUAL SUPPORT=selected wedge knots now reuse the exact site enclosures already
computed by DecodeCarrierBatch. The callback validates the source/task and full
symbolic/interval identity before reading the same cached bounds. The hub and
ring no longer recompute coordinate intervals for each incident wedge. The
ordered translation, cell cover, dual classifications and receipts are unchanged.
Exact Compact compile PASS: 1,227,068 B / 64,938 body / 26,556 B shared /
7 RW / 256 lanes. Size gate STILL FAILS; no CPU or resource was added.

SELECTED GEOMETRY=common SelectL2Carrier/FinishL2Carrier predicates now serve
the scalar observation consumer and cooperative page materialization. Fifty-six
lanes evaluate only selected seven-site positions; forty-eight evaluate the
actual wedge dual covers. Ordered receipt reduction preserves the former
first-failed-site dependency prefix and partial output. The dead ancestor-base
scratch holds these results; no shared-memory growth or GPU dispatch was added.
PageCarrier now copies these results instead of recalculating positions/covers.
All child closure proofs and the dual cell traversal order remain unchanged.
Exact compile PASS: Compact 1,239,460 B / 65,745 body / 26,556 B shared / 7 RW /
256 lanes; resolver 938,760 B / 48,519 body / 15,464 B shared / 6 RW / 128 lanes.
Compact size gate STILL FAILS; parallelization is not a device speedup claim.

RT4 HANDOFF REQUIREMENTS (now covered by the host gates above, device pending):
RT1–RT3 consumers are rewired. Compact size failure is repaired, not exempted.
Do not resume historical runs or repeat source-cache,
ancestry, R3, skin, selected-site/dual or allocation rewrites. RT4 must
prove compact source-cache bit parity, reached-face owner
mask omits no coverage contributor and 32-lane accumulation matches scalar OR.
Do not add a spinning global lock, drop BUSY work as capacity, or retain a
snapshot/cursor. Complete-through hysteresis already matches retained REV-C
14.1; do not silently replace that explicit evidence rule during this execution
rewrite. Do not redo grouping, roots, footprint reach, ERASE, fence retirement,
conditional allocation, existing-value updates, atomic arenas or R1 preflight.
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
Native plugin/full embedded set is not rebuilt; ABI27 is compiled into the
final plugin/APK only in RT4. No APK, suite, device test or speedup claim.

ARENA COMPILE=exact native glslang/spirv-val compiled all nine affected entries
in /tmp/m8-rt2-atomic-arena-f_l4ao4d. Root 541576 B / 28307 body;
L1 739032 / 38711; L2 738976 / 38711; RGB 353136 / 18314; V 548368 / 28002;
FlowerCommit 906224 / 48574 / 24700 B shared / 8 RW; ERASE 80840 / 4086;
ReserveDirty 29264 / 1539; PublishDirty 37936 / 1991. These are compile
checks, not allocator concurrency proofs or runtime performance acceptance.
RT4 must additionally cover parallel allocate/free/coalesce, exact allocation
ownership, stale hints, capacity and concurrent first-owner publication.
Unity C#/Editor codegen PASS:
/mnt/kingston-unity/Builds/QuestMerkabaScan/realtime-rt2-atomic-arena.log.

R1 COMPILE=exact target glslang/spirv-val PASS in
/tmp/m8-rt2-peer-boundary-eps8j_uh: FlowerCommit 835372 B / 44443 body /
24700 B shared / 8 RW; InvalidateFlowerSources 68200 / 3580 / 0 / 6;
InvalidateFlowerPeers 55844 / 2746 / 120 / 5; PublishFlowerR1 12052 / 342 / 0 / 7;
ERASE 74792 / 3732 / 124 / 8; resolver 938184 / 48491 / 15464 / 6.
All six use 128 lanes. RT4 must cover malformed receiver rejection before any
epoch/M8 cut, concurrent adjacent sources, full THROUGH and explicit ERASE.
Unity C#/Editor codegen PASS:
/mnt/kingston-unity/Builds/QuestMerkabaScan/realtime-rt2-peer-publication.log.

RT3 IMPLEMENTED: paired RGB/V atlas, reached coverage, compact source cache,
streamed ancestry, shared R3 evidence, cooperative skin and selected-site/dual
materialization compile. RT4 now owns integrated proof/repair, including the
known Compact size failure. No full tests, command-graph acceptance, Unity
Quest APK, install or device acceptance have run for the complete rewrite.
Baseline tests/APK are not rewrite acceptance.
