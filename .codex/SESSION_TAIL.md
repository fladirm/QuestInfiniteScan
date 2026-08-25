# Latest execution tail

## User

After committing the pending backing cut, replace the broken Release timestamp
capture only: delete global Vulkan dispatch hooks and use explicit submission and
per-dispatch plugin events around the centralized wrappers. Commit separately,
push, build and install.

## Assistant

Pending backing is committed at `e28a956`. Global Vulkan interception is deleted;
an armed submission now emits explicit begin/end events around each unchanged
dispatch and native callbacks timestamp their current Unity recording state. Native
Android compilation and managed production-dispatch parity pass. Current cursor:
regenerate/validate, commit the two-file diagnostic cut, push, Release build/install.
