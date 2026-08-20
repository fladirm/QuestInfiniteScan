# Known issues and unclosed acceptance gates

This file describes the current feature checkpoint. It is not a list of upstream
QuestRoomScan behavior and it must not be used to claim a release gate passed.

## P0 — repeated rollover/revisit can lose the prior presentation

Physical testing has reproduced this sequence after several chunk transitions:

1. rollover commits the source chunk as `Finalizing` and immediately reuses the GPU
   volume so scanning can continue;
2. slow volume/mesh/keyframe publication remains in the background;
3. another traversal can select a `Finalizing` revisit before its volume artifact is
   durable;
4. only the most recent CPU volume snapshot is retained, so the target may have no
   reloadable volume;
5. the bounded coarse-mesh cache can evict that same chunk, making it disappear while
   the revisit repeatedly fails.

Observed symptoms include a map that stops visibly updating after roughly four
transitions, a previously scanned chunk disappearing on return, repeated transition
attempts, and Stop Scan unable to close the outstanding lifecycle cleanly.

The next bugfix must introduce a coherent chunk state machine: a transition may not
activate an unavailable target; background publication needs explicit durable/failed
completion; revisit selection must account for loadability; and the presentation
cache must asynchronously rehydrate nearby durable chunks. This will be fixed and
retested after the feature checkpoint is pushed. It is not acceptable to hide it by
raising memory limits or allowing an unbounded cache.

## P1 — oblique/incomplete surface acquisition needs device tuning

Distance/normal/visibility arbitration prevents a useful class of far-view erosion
and scan-through-wall updates, but physical testing shows some oblique walls are hard
to complete once partially observed. Deterministic CPU and real-GPU parity tests pass;
that proves implementation parity, not that thresholds are optimal for every Quest
depth sequence.

After the lifecycle fix, tune from captured depth/incidence/confidence telemetry and
compare against an unmodified upstream build. Preserve these invariants:

- a genuinely better close observation can refine a weak distant surface;
- a later distant/grazing observation cannot pull a stable close surface;
- opposite sides of a thin wall remain distinct;
- missing evidence is not fabricated merely to make a mesh look closed.

## P1 — physical DiffSoup renderer acceptance remains open

The Unity shader, artifact validator, cache/promotion path, CPU golden pixel, NVIDIA
Vulkan parity test, and Android compile pass. A headset run must still explicitly
verify stereo parity, depth occlusion, back/front culling choice, pose relocation,
resource disposal, and a live CUDA artifact swap while the app is rendering.

## P1 — full device matrix remains open

The current APK builds, installs, launches, receives depth, scans, and performs initial
rollovers. Release acceptance still requires:

- repeated multi-chunk traversal with notebook disconnected;
- revisit/reload and anchor relocation after app restart;
- more than six transitions without loss of update or unbounded memory;
- stairs/vertical chunk traversal;
- atomic real-CUDA artifact return/swap;
- frame and resident-memory profile correlated with growing chunk count.

## Quality expectations

- Dark rooms can yield usable depth geometry and unusable RGB appearance.
- Default 5 cm voxels do not provide 0.8 cm geometric accuracy. An 8 mm color/atlas
  sampling figure is a texture figure.
- Blank, reflective, transparent, thin, distant, and grazing-angle surfaces remain
  difficult for active-depth reconstruction.
- Monolithic GLB is deliberately size-bounded. Sharded `building.json + chunks/*.glb`
  is the required fallback, not an export failure.
