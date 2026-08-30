# Latest execution tail

## Exchange 1 — user

Asked whether reconstruction already runs as an independent 15 Hz scan beside a
72 Hz XR readout, as in SimpleScanner, or whether every XR frame drives scanning.

## Exchange 1 — assistant

Source and Quest-log audit showed no explicit 15 Hz scheduler. The bridge admitted
the next strictly newer coherent frame as soon as the renderer consumed the prior
one; after reconstruction faulted, accepted capture ran at about 25 Hz. Published-
carrier preview was an XR-frame draw, but scan prediction/admission was still
triggered from renderer `LateUpdate`, allowing native GPU work to starve XR.

## Exchange 2 — user

Directed a donor-shaped 15 Hz scheduler, required the rule and the known prior
failure cause in spec/run controls, forbade ontology or unrelated changes, and
requested commit/push plus a new APK.

## Exchange 2 — assistant

Implemented fixed 15 Hz admission in `RoomScanner`, gated on an empty pending/in-
flight native pipeline, and moved sensor transfer out of renderer `LateUpdate` so
immutable-root preview remains XR-cadenced. Missed ticks never catch up. The run
records revision-52 warm-segment exhaustion separately from GPU-starvation fence
warnings. Focused cadence/rig tests pass 16/16 and full Unity Vulkan passes 100/100;
the next action is checkpoint/push then same-SHA Release build/install.
