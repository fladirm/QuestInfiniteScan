# Claude execution authority — uniscan

Follow [`AGENTS.md`](AGENTS.md). Before changes, read these current authorities:

1. [`M8-REALTIME-MEASUREMENT-DRIVEN-CONTRACT.md`](M8-REALTIME-MEASUREMENT-DRIVEN-CONTRACT.md) in full.
2. [`M8-REALTIME-DAG.md`](M8-REALTIME-DAG.md): current state, NEXT, remaining gates and artifact paths.
3. The latest implementation receipt at the end of [`lasttrue.md`](lasttrue.md).

The realtime contract supersedes old execution contracts. REV-C is the referenced
geometry/data authority only as retained by that contract. `contr.md`,
`MERKABA_CLOSURE_CONTRACT.md`, old RUN/CUT cursors and `.claude/` or `.codex/`
ledgers are historical context, not a competing execution authority. Do not
resume them or restore the removed dual/DIRT, refinement-cursor or OverlapShell
paths. Do not reread the immutable historical prefix of `lasttrue.md` to find NEXT.

## Workspace and continuation

- Work in `/mnt/aidisk/prace/uniscan`, branch
  `refactor/m8-dual-sphere-flower-rev-b`. `/mnt/aidisk/prace/simplescan` is a
  comparison baseline, not this task's edit target.
- Read current `git status` and `git log` before editing; preserve unrelated
  work and untracked files. Another agent may have changed this shared tree.
- The DAG alone owns CURRENT/NEXT; append actual receipts to `lasttrue.md`.
  Do not create another state ledger. Historical PIDs, logs and APKs are not
  proof of what is running now: verify the installed build before diagnosis.
- Read complete affected source units, not arbitrary fragments or the entire
  unrelated repository. Replace superseded code and reconnect its consumers;
  do not add a parallel fallback. Preserve scene-bound component names.
- Change generated data through its current generator and regenerate. CPU
  oracle/codegen is not a live scanner/readout backend; follow the contract's
  separate frozen offline export rules.
- Commit coherent cuts. A checkpoint, host compile or old green suite does
  not close a device/performance gate.

## Quest / Unity execution

Read `quest_guide.md`, `Tools/storage/dev_environment.sh` and
`Tools/unity/MERKABA_PIPELINE_DELIVERY.md` before platform/build work. Use the
Kingston Unity host at `/mnt/kingston-unity/Unity/Projects/QuestMerkabaScanHost`
and the supplied build scripts, not another IDE/project. Respect memory bounds.

Use the existing shader/command-graph audits, full Unity tests, GLB validation,
`Tools/unity/build_merkaba_apk.sh` and `Tools/unity/verify_merkaba_apk.sh` as
required by the current contract and user overrides. A FUNCTIONAL_TEST/CAPTURE
APK is diagnostic, not final BINARY_ONLY/no-JIT delivery.

Report exactly which gates ran. Never claim measured performance without
timestamps from the identified Quest build. Unrun final checks remain
`DEVICE ACCEPTANCE PENDING`.
