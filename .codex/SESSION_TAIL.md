# Latest execution tail

## Exchange 1 — user

Bound the N4.1R implementation to the Quest 3 / Adreno 740 production contract:
exact packed Q16.48, <=8 UAVs, <=128 MiB per buffer, 64-byte scratch alignment,
<=65535 direct dispatch dimensions, 14 entrypoints/exactly 16 native dispatches,
and no capacity-sized work after raw ingress. Required the previously specified
Observation, FOOTPRINT, TILE and RefinementScan cuts through physical Quest
evidence, with N5R unopened.

## Exchange 1 — assistant

Implemented the CUT A-E WIP without a new kernel, dispatch, buffer or ABI. Local
gates pass: Unity Vulkan 111/111, generator/check, diff, <=8 UAVs, 14/16 graph and
16/16 Vulkan 1.1 SPIR-V/spirv-val. Current LDS is Frame 28928, FOOTPRINT 17920,
TILE 15372, BOUNDARY 27984 and GLOBAL_CLOSE 31184 bytes.

## Exchange 2 — user

Requested the measured result and an immediate commit/push checkpoint.

## Exchange 2 — assistant

Physical Quest revision 1 measured FOOTPRINT 133.9806 ms, RefinementScan
52.5162 ms, BuildObservation 25.4787 ms, TILE_CLOSE 14.2876 ms and total timed
compute 249.6790 ms. Revision/root advanced 1 through 49; revision 50 produced
the preserved logical-extent PageFault 0x00000100 and retained root 49. There was
no KGSL/MMU/device-lost/fence-timeout event. The checkpoint is explicitly WIP:
correctness/lifecycle evidence passed, but the <50 ms N4.1R performance gate did
not.
