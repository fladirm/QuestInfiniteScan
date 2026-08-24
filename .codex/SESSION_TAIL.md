# Latest execution tail

## User

The `63ca337` device run proves the next frames stop at the artificial 240 MiB
decoded backing limit. Apply only a 1 GiB carrier-residency slice: 2048 physical
pages / 1024 current-shadow pairs, binding-derived maximum-sized segments near the
current segment count, no paging, scheduler, S16, R3/R4 or publication redesign;
then commit, build and install.

## Assistant

`SigmaCarrier` now defaults to 1024 MiB and removes the 240 MiB inspector ceiling
and fixed 32 MiB segment knob. Segment capacity is the largest legal even page
count under `SystemInfo.maxGraphicsBufferSize`, capped at 256 pages. The focused
capacity proof and full Vulkan EditMode suite pass 68/68; current cursor is code
graph/control validation, exact commit/push, archive, Release APK and install.
