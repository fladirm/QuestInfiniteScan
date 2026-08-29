# Latest execution tail

## Exchange 1 — user

Directed a narrow fix for the first live blocker: valid publication must not stop
because `Disposition.w` contains a nonzero certificate-delta count.  Fence
timeouts are currently only evidence that Quest cannot keep up; do not implement
a watchdog/retry workaround before reducing the measured slow kernels.  Also
confirmed that this repository keeps `DEPTH_L`, `DEPTH_R`, `RGB_L` and `RGB_R` as
four separate exact query inputs rather than pre-fusing a world voxel.

## Exchange 1 — assistant

Corrected the host tagged-receipt interpretation without changing shaders or the
16-dispatch graph.  `FAULTED` alone interprets `Disposition.w` as a fault receipt;
`PUBLISHED` exposes it as `CertificateDeltaCount`.  Expected revision remains an
independent completion reservation.  Focused tests pass 3/3 and the complete
SigmaNativeFrame corpus passes 30/30.

## Exchange 2 — user

Requested the full test/build checkpoint, commit and push, with the resulting APK
installed on Quest but not launched.

## Exchange 2 — assistant

Complete Unity EditMode/Vulkan passes 97/97.  Release APK SHA-256
`ab0a979421bc8173c893b516bfaf27c8d54883369cf72eddab3330af4aa13cb7`
builds and installs successfully.  The next exact action after this checkpoint is
measured optimization of the slowest native kernels; N5R/S4-09 remain unopened.
