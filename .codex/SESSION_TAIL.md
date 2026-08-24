# Latest execution tail

## User

The installed build fails immediately on Start Scan. The scanner must work in
arbitrary spaces without a Meta room scan, while spatial anchors remain available
for correct large-space localization.

## Assistant

Device log proved the failure precedes capture: `EnsureScanAnchorAsync` returned
false because M5 retained the donor MRUK `IsRoomLoaded` gate. The host scene also
contains no `RoomSpaceRoot`, so the next bind gate would fail. The current repair
removes MRUK room-scene loading/fallback, creates `[SigmaRoomSpace]` at runtime and
keeps OVR spatial-anchor create/save/load/localization plus relocation matrices.
Unity Vulkan EditMode is 54/54 and the eight-UAV gate is green. Next action is an
exact repair commit, source archive, Release build/install and Start Scan retest;
S4-09 remains unopened.
