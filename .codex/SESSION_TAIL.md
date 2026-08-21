# Latest exchange snapshots

After compaction read `specka.md`, goal, state, DAG, this file, and relevant ADRs in
the order in `AGENTS.md`. Trust code/evidence over prose, never repeat accepted work,
and never touch `~/.codex` or session/history data.

## Exchange 1

User: Asked whether the repaired version was installed, then reported that Start
Scanning failed on the deployed lifecycle-repair APK.

Assistant: Pulled the current device exception. The serialized lifecycle reached
Cone-PRISM allocation, where `PrismDisplacementTopology` requested one 138,412,032-
byte transient accumulator. Quest reports a 134,217,728-byte maximum storage-buffer
range, so Start failed deterministically before sensor ingress.

## Exchange 2

User: Requires the repair to be implemented, committed, archived with `git archive`,
and installed rather than receiving another speculative hotfix.

Assistant: Kept the complete accumulator capacity and its eleven information words,
but split its physical storage into the already canonical base and micro address
spaces (88 MiB and 44 MiB). Both compute shaders and bindings use the segmented ABI;
a regression test proves the old combined buffer is illegal and both replacements
are legal. Forced-Vulkan tests and Android/Vulkan IL2CPP build passed; commit/archive
and deployment are next.

## Exchange 3

User: Reported that the visible scan still corrupted geometry, slowed badly, and
stopped; required an exact source archive and comparison with working upstream GPU
execution rather than another speculative hotfix.

Assistant: Created the requested analysis archive for commit `349af41`, inspected
upstream QuestRoomScan and retained its proven `DispatchIndirect` approach. Device
errors and manual shader-call audit identified the shared cause: topology moments
were written through displacement addresses, several entry points exceeded the
eight-UAV Quest limit, split/merge retained three stale pressure words, and the
connector kernel lacked hierarchy bindings. These are now corrected as one GPU work-
graph change; build/commit/archive/deploy of the corrected tree is in progress.
