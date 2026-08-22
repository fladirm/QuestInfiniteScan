# Session tail

## Latest user exchange

The installed S4-08 Release freezes after Start Scan. The user requested a read-only
root-cause audit and then supplied a full hot-path performance audit. They require a
new `S4-08.2` run that rewrites every affected shader cleanly as a whole file—not by
patching the old implementation—and rewrites matching C# dispatch/binding code in
the same phase. No CPU readback, synchronous wait, compatibility fallback, stale
legacy branch or alternate ontology is allowed. This exchange authorizes control
plan changes only, not runtime implementation yet.

## Latest diagnostic and planning update

Read-only Quest evidence isolates the immediate freeze: the first inverse command
creates an `AsyncQueueSynchronisation` fence although the device reports async
compute unsupported. `GpuSubmission.IsComplete` polls `GraphicsFence.passed`, which
throws every frame before `_inFlight` can be cleared; future predictions are then
dropped. The log contains 1,703 repetitions in 28 seconds while coherent capture
continues. Capture/prediction rings share the same invalid fence assumption and
catch polling failure as success, risking early resource reuse. No runtime source
was changed. `S4-08` is reopened and `.codex/S4-08.2_PLAN.md` now specifies the
deterministic completion, ExactALU, generated-circuit and coordinate-major GPU
rewrite, matching C# ABI/dispatch replacement, parity/performance gates and final
Release device acceptance. `S4-09` remains pending.
