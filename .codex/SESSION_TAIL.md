# Latest exchange snapshots

After compaction read `specka.md`, goal, state, DAG, this file, and relevant ADRs in
the order in `AGENTS.md`. Trust code/evidence over prose, never repeat accepted work,
and never touch `~/.codex` or session/history data.

## Exchange 1

User: Asked whether the repaired version was installed, then reported that Start
Scanning failed on the deployed lifecycle-repair APK.

Assistant: Pulled the current device exception. The serialized lifecycle reached
Cone-PRISM allocation, where `PrismDisplacementTopology` requested one 138,412,032-
byte transient accumulator. Quest reports a 134,217,728-byte maximum storage-buffer
range, so Start failed deterministically before sensor ingress.

## Exchange 2

User: Requires the repair to be implemented, committed, archived with `git archive`,
and installed rather than receiving another speculative hotfix.

Assistant: Kept the complete accumulator capacity and its eleven information words,
but split its physical storage into the already canonical base and micro address
spaces (88 MiB and 44 MiB). Both compute shaders and bindings use the segmented ABI;
a regression test proves the old combined buffer is illegal and both replacements
are legal. Forced-Vulkan tests and Android/Vulkan IL2CPP build passed; commit/archive
and deployment are next.

## Exchange 3

User: Reported that the visible scan still corrupted geometry, slowed badly, and
stopped; required an exact source archive and comparison with working upstream GPU
execution rather than another speculative hotfix.

Assistant: Created the requested analysis archive for commit `349af41`, inspected
upstream QuestRoomScan and retained its proven `DispatchIndirect` approach. Device
errors and manual shader-call audit identified the shared cause: topology moments
were written through displacement addresses, several entry points exceeded the
eight-UAV Quest limit, split/merge retained three stale pressure words, and the
connector kernel lacked hierarchy bindings. These are now corrected as one GPU work-
graph change; build/commit/archive/deploy of the corrected tree is in progress.

## Exchange 4

User: Required a forensic audit only—no more hotfixing—of why the current output is
a flickering patch/meshlet soup rather than the continuous injected manifold.

Assistant: Verified the decisive source failures at commit `8f2b31b1bc72`: film and
vertex flag bit 8/9 collisions make normal films latent/discarded by prediction;
split children differ from spawn/merge; canonical PressureManifold/member/link/
frontier pools do not exist; spawn publishes per 8x8 tile/eye; full rectangles carry
real FilmIDs; five-wave proximity merge fills bounding rectangles; screen adjacency
creates physical curtains; FilmB remapping is asymmetric; H/g and sigma are not one
covariance-consistent, view-decorrelated posterior. Legacy TSDF is disabled.

## Exchange 5

User: Directed that every audit proposal become an exact `Q3-15.5` corrective run
before Q3-16, then be fully implemented, manually reviewed, committed, archived with
`git archive`, built and installed on Quest.

Assistant: Added and activated Q3-15.5, moved Q3-11 back behind it, and wrote the
mandatory phase A-I runbook at
`.codex/runbooks/Q3-15.5_PRESSURE_MANIFOLD_REPAIR.md`. Runtime implementation must
follow that order; use one batched device test after fixtures, not repeated per-step
deployments.

## Exchange 6

User: Repeatedly requested concrete progress and required the full forensic audit to
be implemented without simplifying the original pressure-manifold ontology.

Assistant: Implemented static Q3-15.5 phases A-K as one vertical repair: typed flag
ABIs, canonical generation-safe PressureManifold graph, candidate/union/publication
spawn, measured/latent tessellation, physical link validation, symmetric endpoint
and frontier remap, covariance/view-decorrelated posterior, dirty transactional
meshlets, GPU preview culling/front-depth rendering, schema-v5 persistence and pure
Cone-PRISM production setup/UI. The final working tree passes 102/102 Unity EditMode
tests and the eight-UAV validator. Remaining phase L is one commit, git-archive ZIP,
fresh APK build/install and the single physical Quest batch.
