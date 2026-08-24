# Latest execution tail

## User

Remove only production GPU profiler instrumentation from the direct scan hot path.
Keep dispatch validation, 2D grid lowering, kernel order and all R3/R4/S16 semantics
unchanged; prove the disabled path records the same production dispatch contract,
then commit, build/install and stop for a physical wall.direct comparison.

## Assistant

Normal non-development Release now performs FindKernel and direct validated
dispatches without CustomSampler/Recorder registration, Profiler enablement or
BeginSample/EndSample. Editor/Development/explicit diagnostic builds retain timing.
The 320x320 production graph trace is identical with profiling on/off and the OFF
path registers no samplers; Vulkan EditMode passes 67/67. Current cursor is exact
commit, push, archive, Release APK and install, then stop for device comparison.
