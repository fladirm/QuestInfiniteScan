# Known recurring GPU/persistence failure patterns

This file records verified failure classes and the invariant that prevents each
one. It is diagnostic guidance, not reconstruction authority. `new_spec.md`
remains the sole physical/product specification.

## E01 — GPU resource retired before its last queued use

### Signature

```text
CPU submits publication/readout/migration work
    -> owner reference count reaches zero or a replacement becomes current
    -> old buffer/texture is released, destroyed or recycled
GPU later executes an already-queued command that still references it
    -> invalid VkDeviceMemory translation
    -> KGSL/MMU page fault, device instability or delayed fence failure
```

The crash may occur several revisions after the premature release. Queue delay
therefore makes the revision named near the crash an unreliable indication of the
revision that violated lifetime.

### Verified donor instance

SimpleScanner commit `24f84a5a1a678d3f83329ec7858402e93618624b` protected a
publication migration with `GraphicsFenceType.AsyncQueueSynchronisation` and
polled `GraphicsFence.passed`. That path was not a valid lifecycle proof on the
Quest graphics queue. Commit `ba286ac33c6152b0cea32e5dfc105c4528864bbd`
replaced it with a scanner-owned asynchronous completion token, retained the
source and replacement generations while completion was unproved, and kept prior
publication generations alive through queued draws.

### Mandatory invariant

A physical GPU slot/resource/generation is reusable or releasable only when all
three conditions hold:

```text
required durable/publication root is reachable
AND the last GPU reader and writer has verifiably completed
AND no publication/readout lease references that generation
```

Host ownership/reference count reaching zero is necessary but not sufficient.
Pending completion retains the resource. Failed or unprovable completion
quarantines it; failure never means safe reuse. Replacement publication must keep
both old and new generations alive until the handoff is proved.

### Current Sigma guard

`SigmaGpuCompletion.RecordAfterAllWork()` inserts a graphics-queue CPU-
synchronisation ticket after the complete native command buffer. Ingress,
prediction, capture and renderer resources remain owned until that ticket reports
`Complete`; a polling fault goes to `SigmaGpuRetirement` quarantine. Preserve this
contract in every N5 load/evict/migrate/replace path.

### Regression controls

- Delay the completion token while dropping all host leases: the resource remains
  allocated and cannot be selected as a new target.
- Fault completion polling: the generation is quarantined, not reused.
- Keep a readout lease on an old root while publishing a replacement: old backing
  survives until both GPU completion and lease release.
- Teardown with queued work: destruction occurs only through deferred retirement.

## E02 — Planning/scratch capacity mistaken for resident physical capacity

### Signature

```text
bounded logical/page-plan scratch has N entries
actual resident current/shadow pool has only M pairs, M < N
planner derives target slot from the scratch index
    -> target physical slot is outside the resident buffers
late close detects invalid metadata, or an earlier write goes out of range
```

The N4 Quest instance had `PagePlanCapacity = 128` scratch plans but only 112
physical resident pages, hence 56 legal current/shadow pairs. Treating plan 56 as
resident produced target slot 112, while valid slots were `0..111`. The warm
segment held `56 * 4096 = 229376` logical samples; revision 52 correctly retained
root 51 only after the capacity boundary was reached.

### Mandatory invariant

Scratch/worklist capacity is never storage availability and never logical world
size. Before any clone or scatter:

```text
legalResidentPairCount = min(planCapacity, physicalPageCapacity / 2)
logical target page < legalResidentPairCount
target physical slot < physicalPageCapacity
target sample < samplesPerPage
```

An N4 capacity miss must clear mutation/page indirect counts, publish nothing and
leave the prior root authoritative. Defensive clone/scatter bounds remain even
after planner validation so stale receipts cannot write.

In N5 the same condition becomes reason-coded asynchronous `COLD_DURABLE` page
fault/backpressure and retained observation replay. It must never become physical
`UNRESOLVED`, `ZEmpty`, a terminal scan stop or permission to address nonexistent
resident storage.

### Regression controls

- Use one resident current/shadow pair with a full source page and request the next
  logical page: mutation, clone and scatter counts are zero and the root is
  unchanged.
- Vary scratch plan capacity independently from physical page capacity.
- Invoke clone/scatter defensively after a rejected plan: no state,
  representation, metadata or root byte changes.
- Long N5 scans must cross the resident boundary by durable eviction/rehydration,
  not by enlarging the meaning of a physical slot index.

## E03 — Internal page-fault receipt confused with a GPU MMU page fault

These two failures share a name but not a cause:

```text
SIGMA_N4_FAULT_PAGE_VALIDATION / SigmaNativeColdReason.PageFault
    = shader/application fail-closed receipt; prior root remains readable

KGSL PageFault / Vulkan device-memory translation fault
    = driver/GPU accessed invalid or prematurely retired backing memory
```

Do not attribute one from the other without evidence. For an internal receipt,
record frame revision, fault mask, mutation/touched-page counts and root before/
after. For a KGSL fault, reconstruct resource generation, last submitting command,
completion token, leases, retire/reuse event and queued readers/writers. Fence
timeouts under a saturated GPU are performance/starvation evidence; alone they do
not prove either an out-of-bounds write or premature retirement.

## E04 — Cache/residency absence interpreted as physical emptiness

```text
logical page absent from a pinned durable root  !=
logical page present in that root but not GPU resident
```

Only the first may decode through the proved unbacked-`ZEmpty` equivalence. The
second is a cold miss. It must trigger conservative inclusion, rehydration or
backpressure and may never erase support, close a relation, expose farther first
hit or manufacture `ZEmpty`. Locator maps, hash buckets, segment/bank/slot and
resident generation are disposable execution data, never identity or canonical
ordering.

## E05 — Low dispatch count hides a full-capacity workgroup interpreter

### Signature

```text
submission count looks small
    -> one workgroup owns a complete frame, capacity or giant sort run
    -> every lane serially walks hundreds/thousands of entries per digit/stride
    -> Vulkan compiles, Quest takes tens to thousands of milliseconds
```

Reducing dispatches is not useful when global synchronization is replaced by a
serial scheduler inside one kernel. After the one allowed raster/FOOTPRINT/
boundary broad-phase passes, work must follow compact active/realized/touched
cardinality. No workgroup may own `O(frame)`, `O(world)`, `O(capacity)` or a 16K+
run. Use the already-frozen dispatch positions as wide global cuts and reduce only
small deterministic summaries at the end.

### Regression controls

- Report entries-per-lane and barrier stages for every hot kernel at 320x320.
- Reject a kernel whose inner iteration product grows with frame or allocation
  capacity even when dispatch count is fixed.
- Timestamp every mode/entry point separately on Quest; fewer workgroups alone is
  not a performance result.

## E06 — Finite exact algebra searched at runtime

### Signature

```text
an algebra has 8 D4 states / 24 assignments / a few dyadic coefficients
    -> shader loops over candidates and calls generic Q48 multiplication
    -> the same finite answer is rediscovered millions of times per frame
```

Finite algebra belongs in generated exact tables or specialized generated
operations. D4 compose/inverse/orbit/adjacent-frame action is lookup, not search.
Coefficients such as `0, +/-1/2, +/-1, +/-3/2` use a generated bit-parity-proved
operation, not an ad-hoc shift identity and not generic 64x64 Q48 multiplication.
Preserve the original per-term nearest-even rounding, overflow and accumulation
order; mathematical equality is not sufficient.

## E07 — Host Vulkan or full APK used as the first shader compiler gate

### Signature

```text
edit one shader line
    -> wait for Unity/full Android build
    -> Quest compiler rejects LDS/control-flow/unroll/binding shape
    -> patch one line and repeat
```

Freeze device limits before kernel design. Statically validate the exact dispatch
graph, thread shape, LDS, UAVs, direct-grid dimensions, names and both group-sync
intrinsics. Compile every exact production variant to the target Vulkan SPIR-V
environment and run `spirv-val`; then use a targeted Unity Android/Vulkan shader
compile before a full APK. Quest remains mandatory for driver/occupancy/timing
proof, but it is not the first syntax or resource-shape check.

Generated/new identifiers stay in `Sigma*` / `SIGMA_*`; loops reaching complete
comparators, associator profiles, generic Q48 or large call graphs are not blindly
force-unrolled. A whole kernel cut is made green against these gates before the
next expensive build.
