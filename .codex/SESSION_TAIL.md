# Latest execution tail

## Exchange 1 — user

Directed N4R to continue from `e192462a` with the graph fixed at the hard ceiling
of sixteen submissions.  Required an exact execution-only boundary compaction,
original-boundary-id writes, lossless candidate filtering, no sample/index/hash
authority, and lossless GLOBAL_CLOSE unresolved propagation into CUT E.  Warned
that the existing cooperative FOOTPRINT and BOUNDARY primitives may not be
mechanically narrowed or serialized, and required CPU/Vulkan parity plus measured
Quest timings before a WIP commit and stop.

## Exchange 1 — assistant

Kept Build at its measured eight 8-lane teams and rewrote only the FOOTPRINT
contractor as sixteen fully partitioned 16-lane teams in one 256-thread group.
The old isolated algorithm used only lanes 0..15; its remaining lanes were neutral
reduction values.  Every shared array/scalar/reduction is now team-addressed and
results retain original footprint ids.  The full grid contracts from 102401 to
6401 groups.  Kept the compact BOUNDARY list execution-only, the exact 256-lane
heavy evaluator unchanged, explicit NO_STITCH writes, and unresolved propagation.
Unity Vulkan passes 95/95 with team/tail/permutation and boundary-order controls;
generator/check/UAV/diff and Android Release build pass.

## Exchange 2 — user

Required build/install/profile on Quest, actual per-kernel timings and result
counts, then commit/push the resulting N4R WIP and stop before N5R.

## Exchange 2 — assistant

Built APK SHA-256
`59042dacf1a94f8695da07796ac6bef40435d32189a2ad71997879f48f8178ad`
and streamed it successfully to the connected Quest.  The device is currently
blocked before application process creation by Meta's
`com.oculus.os.vrlockscreen/.SensorLockActivity`; ADB wake/power events do not
satisfy the physical camera-safety confirmation.  No Quest timing or geometry
PASS is claimed.  The exact next action after physical unlock is a clean Start
Scan capture of all sixteen timestamp rows, revisions/root and KGSL/fence evidence
from the already-installed APK, with no source changes before measurement.
