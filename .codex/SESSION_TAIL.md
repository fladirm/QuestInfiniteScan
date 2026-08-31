# Session tail

## Exchange 1 — user

Required the complete scanner execution to stop starving XR: XR must only present
the immutable readout, while depth/PCA-derived reconstruction and the exact native
close run outside the XR graphics work. Requested the smallest systemic lowering,
no algebra/ontology change, using the proven SimpleScanner queue/front pattern and
kernel-time boundaries where one monolithic low-priority queue submission still
caused visible stutter.

## Exchange 1 — assistant

Kept the singular canonical carrier and double-buffered only disposable readout.
The preloaded Vulkan plugin obtains same-family queue 1; Unity/OpenXR remains on
queue 0. The unchanged fourteen-entrypoint/sixteen-dispatch native close is split
into seven fixed queue-1 command slices with at least one Unity frame between
submissions. XR reads FRONT only, BACK stays scanner-owned, only the final slice
signals terminal completion/readback/retirement, and all static, Unity, SPIR-V,
native-plugin, Release build and install gates passed.

## Exchange 2 — user

After the physical Quest run, stated that the observed behaviour is sufficient and
asked to freeze and accept it if the remaining pipeline and logs contain no error.

## Exchange 2 — assistant

Forensic audit accepted N4.2R execution/lifecycle. Root/revision advanced
monotonically 1 through 47 while XR held 71--74 FPS instead of the donor's 35--42
FPS collapse. Revision 48 completed all seven slices, then correctly failed closed
at the known 229376-sample resident boundary with receipt `0x120` and root 47
preserved. The two bits are per-item and terminal guards for the same capacity
boundary, not KGSL/MMU failure. KGSL, MMU, device-lost, fence-timeout, revision
mismatch, root regression and false unresolved/no-change counts were zero. The
application was explicitly stopped; ensuing camera/BufferQueue warnings are
teardown-only. The cut is being frozen, committed and pushed without opening N5R.
