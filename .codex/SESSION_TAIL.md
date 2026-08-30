# Latest execution tail

## Exchange 1 — user

Superseded the prior N4.1R performance plan with one narrow Quest aperture cut
from `29eef420`: change only fixed admission to 10 Hz, PCA L/R to actual
640x480@30, and native inverse execution to a centred 256x192 ROI over unchanged
320x320 physical depth at sensor offset 32,64. Keep the measured 128-thread
FOOTPRINT winner and all algebra/component/refinement/canonical/page semantics;
retain exactly 14 entrypoints and 16 dispatches. Build/install/profile >=32 warm
publications and accept p95 <90 ms; only then may the already-prescribed R1/R2
resume if required. N5R remains unopened.

## Exchange 1 — assistant

Completed the full shader/ABI/resource/bind/test review and implemented only the
admission-domain cut. Physical depth/LUT/prediction textures remain 320x320,
native scratch is 49152 footprints / 97856 boundaries / 192 tiles, and only
physical sampling uses `localPixel + _NativeSensorOffset`; topology/canonical
receipts retain local coordinates. The setup wizard serializes scanHz=10 and two
640x480@30 eye PCAs. Generator/check, graph/UAV contract, Quest SPIR-V 16/16 and
Unity Vulkan 113/113 pass. Exact-SHA Release build/install/device profile is next.

## Exchange 2 — user

Requested end-to-end physical Quest closure, one tested/pushed checkpoint and no
N5R or reconstruction redesign.

## Exchange 2 — assistant

Source is frozen after clean local gates. The next action is code-graph/goal-state
validation, a local exact-SHA checkpoint, Release Android/Vulkan build, install
and >=32-publication capture before deciding whether the new <90 ms gate passes.
