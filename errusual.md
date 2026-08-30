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
