# Session tail

## Latest user exchange

User requires the consolidated S4-08 package to be an actual release build rather
than a development/debug APK, followed by immediate Quest installation. S4-09 must
remain paused until that exact release checkpoint is archived and deployed.

## Latest implementation update

S4-08 is accepted in `0696228`. The first Android build was refused because the
Android compiler exposed one varying-flow barrier and two invalid fixed-loop
annotations in older S4-05/S4-04/S4-06 shader lowering. The precise three roots are
corrected with unchanged exact semantics; Unity Vulkan remains 64/64. Build tooling
now forces `BuildOptions.None`, clears debugging/profiling flags and selects IL2CPP
Release. Next is the correction commit, zero-error Android release build, source
archive and immediate Quest deploy; S4-09 remains paused.
