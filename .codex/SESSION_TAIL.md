# Latest execution tail

## User

Fix only the continuation-anchor ownership alias proven by device fault `0x104`.
Snapshot the CURRENT target ordinal into existing `target.Candidate.x` before
pending retention reuses `_PendingLinks`; publication must consume the snapshot.
No performance, R3/R4, S16, allocation or ordering change, then build/install.

## Assistant

`FinalizeExactClosure` now freezes the resolved CURRENT ordinal only for accepted
CONTINUATION and `MapFrameTargets` reads it directly. `_PendingLinks` remains
component/retention scratch. The focused fixture mutates the root link from anchor
0 to retention slot 1 yet publishes root 2 with exact mapping and zero 0x104;
Vulkan EditMode passes 68/68. Current cursor is exact commit, push, archive,
Release APK and install, then stop for four successive device publications.
