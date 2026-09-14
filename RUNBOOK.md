# FinalScan runbook (canonical, derived from the donor tooling)

Environment: `Tools~/dev_environment.sh` (Unity 6000.5.9f1, NDK, adb, device serial, build dir).

| Step | Command | Output |
|---|---|---|
| ABI twins | `python3 Tools~/native/gen_abi.py` (`--check` in gates) | `Native~/shaders/world/fs_abi.glsl`, `Runtime/World/WorldAbi.cs` |
| Native plugin + kernels + envelope gate | `FS_NATIVE_GEN_DIR=<scratch> bash Tools~/native/build_native.sh` | `FinalScanHost/Assets/Plugins/Android/libFinalScanNative.so`, `FS-SPV-GATE` lines |
| Host tests (executor, measure, world, render math) | `FS_HOST_TEST_DIR=<scratch> bash Tools~/native/build_host_tests.sh` | suites must all pass |
| Unity EditMode tests | `bash Tools~/unity/run_unity_tests.sh` | `$FS_BUILD_DIR/TestResults` |
| APK (native → Prepare → Build → zipalign → apksigner) | `bash Tools~/unity/build_apk.sh` | `$FS_BUILD_DIR/FinalScan-release.apk` + SHA256 |
| Install + launch | `bash Tools~/unity/deploy_apk.sh` | grants HEADSET_CAMERA / CAMERA / USE_SCENE, wakes, launches |
| Canonical live run (clears debug props, logcat, meminfo, screens, host/sensor jsonl) | `bash Tools~/probe/run_live.sh <seconds> [--no-install]` | `$FS_BUILD_DIR/evidence/live-<utc>/` |
| Acceptance run 1 (synthetic dense + spin) | `FS_RUN_TAG=accept1 FS_RUN_SYNTHETIC=5 FS_RUN_SPIN=1 bash Tools~/probe/run_live.sh 90` | `$FS_BUILD_DIR/evidence/accept1-<utc>/` (+ `ACCEPTANCE_SHA`) |
| Acceptance run 2 (live 60 s incl. RESET WORLD at 30 s) | `FS_RUN_TAG=accept2 FS_RUN_RESET_AT=30 bash Tools~/probe/run_live.sh 60 --no-install` | `$FS_BUILD_DIR/evidence/accept2-<utc>/` |

Rules: the acceptance APK is built from a committed SHA (`ACCEPTANCE_SHA` recorded in the run directory);
receipts are the `FS-HOST` / `FS-HOST-CS` / `FS-SENSOR` / `FS-MEAS` / `FS-WORLD` / `FS-RENDER` log lines and the
`VrApi` FPS lines of the app pid; the full telemetry JSON is in `files/host/host-*.jsonl` on the device
(`adb pull`). Never test an uncommitted tree for acceptance.
