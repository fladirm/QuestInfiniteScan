# S4-08.5 forensic refactor checkpoint

Status: **FORENSIC RECORD — implementation cursor moved to controls**
Branch: `feat/sigma-prism-16-cpq4-20260822`
Audited HEAD: `fd0fee1bd4a6fbde27480a4e08214277f7f8ea04`
Frozen comparison base: `d3b83e1c47abfffe33ec759875a8b6545b78435b`

This file is the compact forensic handoff for the current working tree. It records
verified defects, not speculative redesign. The audit is reviewed and frozen; the
current implementation cursor is `.codex/S4-08.5_RESUME.md`. Do not restore the
retired stream/transaction/Morton graph.

## Frozen invariants

- The only canonical world is exact `Psi : Sigma_2 -> S16`.
- One coherent `D_L/D_R/RGB_L/RGB_R` frame is one owned observation/revision.
- Image tiles, workgroups, windows, pages and segments are execution/storage only.
- Exact four-source cells remain independent until their componentwise meet.
- Pending/continuation/current/novel are proposals; exact first-hit/S16 evidence
  decides identity. Image or XYZ proximity never does.
- Every visible revision owns a complete immutable exact evidence journal before
  the frame slot can be recycled.
- Pages are backing only. Publication is one atomic root-selected view.
- Capacity pressure may reclaim/spill/backpressure; it may never truncate evidence
  or impose a session ceiling.
- No legacy/fallback graph. Final production LOC must remain negative versus the
  frozen base.

## Verified immediate blocker

### R4 cannot admit one production 320x320 frame

The dirty R4 resource order allocates carrier-page evidence before acquiring a
source frame:

- `SigmaFrameGraph.cs:224-241` calls `TryEnsureEvidenceSegments()` for all carrier
  segments, then `TryAcquireFrame()`.
- `SigmaFrameResources.cs:657-679` rejects the frame when
  `allocatedBytes + SourceBytesPerFrame > budgetBytes`.

Exact 2048 MiB profile accounting at 320x320:

```text
shared frame resources                         ~= 424.51 MiB
one complete four-source frame journal         =  131.25 MiB
default decoded carrier budget                 =  240.00 MiB
decoded page size                              =    0.50 MiB
physical pages                                 =  480
page-owned R4 evidence/page                    =    3.3125 MiB
480 page-owned evidence records                = 1590.00 MiB
shared + page-owned evidence                   ~= 2014.51 MiB
remaining before source-frame acquisition      ~=   33.49 MiB
```

The remaining memory cannot hold a 131.25 MiB four-source frame. Production
`TryAcquire()` therefore returns false before inverse recording. Small 4/8-page
fixtures cannot expose this. This is a deterministic empty-scan blocker.

## Verified evidence-contract break

- Complete four-source cells exist only in recyclable frame slots
  (`SigmaFrameResources.cs:125-249`, release at `:1036-1054`).
- `SigmaInverseController.cs:1025-1035` releases the owned frame after its fence.
- Dirty R4 stores page-aligned extrema caches (`SigmaFrameResources.cs:251-319`).
- `ReduceEvidenceWindow` (`SigmaFramePublish.compute:276-367`) preserves only
  extrema, one bound witness and a small source/key summary. It drops non-extremal
  `D_L/D_R/RGB_L/RGB_R` cells and first-hit/RGB-observability detail without a
  redundancy proof.
- `SigmaFramePublish.compute:1001-1005` labels unchanged frames
  `EVIDENCE_RETAINED` without transferring their source journal.
- Pending retention (`SigmaFrameClosure.compute:1328-1424`) likewise stores a
  reduced state/cell and two keys, not the complete exact source evidence.
- Evidence is cloned per page generation (`SigmaFramePublish.compute:815-850`),
  duplicating observation data and causing the memory failure above.

Conclusion: the reduced joint cell is only a fast witness/cache. It is not a
replacement for the complete four-source observation journal.

## R3 is reopened: topology authority is still wrong

The dirty R3 change correctly removes XYZ gap as singularity authority, but does
not replace it with exact first-hit/readout transition evidence:

- `SigmaFrameClosure.compute:1014-1041` always writes discontinuity evidence zero.
- `SigmaTopologyMath.hlsl:194-235` therefore cannot close contact-contact
  folds/creases as singular; near zero-divisor contact-contact becomes unresolved.
- `BuildPendingEdges` (`SigmaFrameClosure.compute:885-938`) creates CONTACT claims
  from horizontal/vertical image adjacency whenever both endpoints are viable.
  It does not carry an exact first-hit relationship.
- Non-near optically adjacent samples can consequently become REGULAR and unioned,
  making image adjacency physical identity.

Correct qualification:

```text
no exact physical edge relation       -> NO_CLAIM
evidence-qualified regular relation   -> exact regular closure
claimed fold/contact-null/conflict     -> annihilator/associator closure
claimed unresolved                    -> defer incident changed mutation only
```

## Test-oracle drift

- Renaming the 5 mm fixture to `EuclideanGapDoesNotManufactureCarrierSingularity`
  is directionally correct, but expecting REGULAR union is not. With no exact
  first-hit/readout relation the oracle is NO_CLAIM.
- Replacing the cross-window thin scene with a catalog zero-divisor proves only
  algebra/window parity, not thin/fold/parallel physical semantics.
- Removing the continuation ownership assertion without an equivalent replacement
  weakens the handle-generation contract.
- R4 fixtures use tiny custom page sets and cannot test production memory,
  frame-slot reuse, root interleaving, revision wrap or reclaim.

The R3 gate is therefore not independent acceptance evidence and must not remain
listed as DONE.

## Extent and continuation remain image-authoritative

- `ReserveFrameExtent` (`SigmaFramePublish.compute:397-416`) derives one promoted
  extent from `_FrameResolution.x`.
- `BuildPageRequests` (`:418-490`) maps NOVEL by raw `(x,y)` plus guard.
- All disconnected novel components share one image-shaped extent.
- CONTINUATION uses raw pixel delta from an anchor (`:447-475`) as canonical sample
  mapping instead of an exact admitted local gauge map.

This violates carrier-gauge invariance. Extents must be allocated per exact
promoted pending component, ordered deterministically by canonical event evidence,
not by image dimensions or storage partition.

## Publication is not atomic

- `FinalizePageVisibility` (`SigmaFramePublish.compute:1033-1055`) mutates page
  metadata, `_CurrentFlags` and readout dirtiness before the root exchange.
- Prediction, inverse and renderer consume flags/metadata directly rather than a
  root-selected manifest (`SigmaForwardReadout.compute:39-45`,
  `SigmaFrameInverse.compute:1070-1084`, `SigmaRenderer.cs:409-465`).
- The later root exchange (`SigmaFramePublish.compute:1057-1074`) therefore cannot
  prevent readers from observing a partial multi-page/multi-segment revision.
- Two fixed revision banks have no reader pin/reclaim contract.

Root-last is valid only when every reader resolves the entire current view through
that root and old views remain pinned until all readers retire.

## Verified finite ceilings

- Page-pair allocation is a monotonic cursor with no free/reclaim
  (`SigmaFramePublish.compute:677-694`).
- `SigmaCarrier.cs:307-339,860-863` reserves the whole GPU pool; the existing CPU
  free-list is not used by the dirty GPU allocator.
- Pending capacity is exactly one 320x320 frame; `PendingControl.x` only grows and
  no producer reclaims PROMOTED/ABORTED/FREE handles
  (`SigmaFrameResources.cs:438-455,557-559`,
  `SigmaFrameClosure.compute:1301-1441`).
- Fixed ping-pong revision banks have no generation-safe reuse ownership.

These are session cliffs, not bounded execution windows.

## LOC and orchestration result

Production diff versus `d3b83e1` is approximately:

```text
+4448 / -927 = net +3521 production LOC
```

Largest additions are closure, frame graph, publication and resource ownership.
The old streaming graph deletion remains valuable, but S4-08.5 has rebuilt a large
sort/scan/label/page/evidence lifecycle instead of completing the planned negative
LOC simplification. Duplicate global sort/scan circuits, 18 label-relax passes,
cross-window gathers and page-owned evidence reduction must not be normalized as
the new architecture.

## Minimal corrective cut (not yet authorized implementation)

Preserve:

- R1/R2 exact independent four-source materialization;
- packed-eye masking and full `(segment,page,sample,generation)` target identity;
- stable exact target reduction and one final S16 reconstruction;
- generated exact algebra, carrier codec, pose and readout primitives.

Reopen R3:

1. Edge proposal must carry an exact first-hit/readout relation; adjacency alone
   yields NO_CLAIM.
2. Real fold/contact-null/conflict claims use generated transition algebra and
   incident-only monotonic deferral.
3. Restore independent thin/fold/parallel, no-claim and cross-window fixtures.

Replace, do not hotfix, dirty R4:

1. Retain the complete compact four-source journal once per observation/revision,
   generation-safe, before frame reuse. Reduced joint cells remain caches only.
2. Scatter only CHANGED reduced targets into shadow backing; do not allocate
   per-page copies of source certificates.
3. Use paired free/reclaim carrier allocation with growth/spill ownership; no
   monotonic session cursor.
4. Build an inactive immutable current-view manifest and expose it with one root;
   prediction/inverse/readout must resolve through that root only.
5. Pin old view and source journal until GPU readers and deterministic proof
   minimization release them.
6. Allocate one extent per exact promoted pending component; continuation consumes
   its exact admitted local gauge map, never raw pixel delta.

Do not revive transactions, bundles, Morton allocation, token scheduling, proof
owner, legacy branches or fallbacks.

## Audit cursor after compaction

DONE (do not repeat): inverse/reduction, pending closure, publication, resource,
controller, renderer/root-consumer, test-oracle and production-memory audits above.

CURRENT: audit-only cross-check whether existing `SigmaConstraintLedger` journal,
raw ownership and generated transition helpers can supply the minimal R3/R4 cut
without duplicating storage or restoring the retired graph. Open only those named
files/functions. No production edits.

NEXT: finish the short audit closure and present the exact revert/replace boundary
for user review. Only after explicit approval resume implementation with a new
negative-LOC cursor and milestone commits.
