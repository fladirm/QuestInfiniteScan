# Latest execution tail

## Exchange 1 — user

Ran the N4 working Release APK and reported a visible pause at scan start and then
approximately once per 16 frames. Required an audit before any hotfix and stated
that resident GPU memory size is irrelevant unless it causes a real capacity
fault.

## Exchange 1 — assistant

Device/source audit ruled out completion payload size and carrier memory. The
16-record batch is about 5.5 KiB and the native graph remains nine dispatches.
Quest timestamps show 0.800 s between revisions 2032/2033 and 0.854 s between
2160/2161; the maximum completion wall reached 4.432 s while the durable journal
grew past 2600 unique unresolved factors.

## Exchange 2 — user

Asked why the mechanism exists, whether it is only logging, and requested a
commit/push suitable for external analysis.

## Exchange 2 — assistant

Clarified that it is canonical unresolved-evidence persistence, not a physical
world or mere log. The source defect is a history-wide main-thread algorithm:
every Add scans and globally sorts the journal, and each comparator regenerates
allocated 272-byte canonical records; startup Load repeats the same sort. The
working checkpoint is being recorded explicitly as N4R in progress/not accepted,
with exact indexed/cached journal lowering as the next action.
