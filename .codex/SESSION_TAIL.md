# Latest execution tail

## Exchange 1 — user

Required a clean N4R restart from accepted `b4399db`, preserving the existing
nine-dispatch graph and lowering full `320x320` ingress through one 16x16 tile
level plus a fixed global summary close. Explicitly forbade the rejected `1abb`
kernel/shader/component zoo and required a hard stop if accepted N1 did not expose
enough executable data for complete canonical component ordering.

## Exchange 1 — assistant

Implemented and checkpointed CUT A--D in local WIP commits `05b43f5` and
`a637252`. Production Vulkan tests pass full-frame footprint/boundary, 400-tile
local close, cross-tile join, disconnected components and inconsistent redundant
edge while the hot graph remains exactly nine dispatches and adds no kernel or
shader family.

## Exchange 2 — user

Asked to continue N4R after the partial checkpoints.

## Exchange 2 — assistant

Confirmed the explicit CUT E hard stop: the generated runtime lacks N1's complete
component canonical serializer/comparator, and the six-word production boundary
receipt retains only compact masks/hashes rather than the full S16 factor,
interval, provenance and certificate payload required by the accepted canonical
order. No ad-hoc hash/tile winner or CUT E mutation was added.
