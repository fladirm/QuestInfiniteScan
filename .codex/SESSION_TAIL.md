# Latest execution tail

## User

Implement one narrow post-`9b3dd7f` cut: separate corrected prediction liveness,
bounded retained-evidence recycle, the already-existing exact block/stage/tail sort,
and a cheaper diagnostic-only XR preview. Add minimal profiling that identifies the
actual slow compute kernel, not merely whole-frame time; keep it one-shot with as
little readback as possible. Preserve S16/R3/R4/publication semantics, commit/push,
build/install the exact Release APK and stop.

## Assistant

The production cut is implemented. One 320x320 canonical submission is sampled
per kernel through GPU recorders; all other submissions contain no profiler markers
and no per-kernel async readback exists. Exact sort parity runs at the full 131072
record capacity. Unity 6000.5.9f1 Vulkan EditMode passes 71/71; S4-08.5 production
is net -61 LOC against `d3b83e1`. Current cursor is code-graph/control validation,
exact commit/push, source archive, Release APK build/install, then stop.
