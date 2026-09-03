# Immutable Merkaba environment ledger

> Current production closure authority: [`contr.md`](../contr.md). Read and follow it verbatim; it supersedes conflicting historical notes in this file.

Recorded and verified: 2026-08-28 (Europe/Prague)

```text
TARGET_ROOT=/mnt/aidisk/prace/simplescan
TARGET_BASE_BRANCH=main
TARGET_BASE_COMMIT=2fdaaae71f60b21b7853e67db943fc42f75d0c2f
TARGET_WORK_BRANCH=feat/simple-infinite-merkaba

OTHER_SCAN_ROOT=/mnt/aidisk/prace/otherscan
OTHER_SCAN_BRANCH=forensic/n4r-cut-e-scheduler
OTHER_SCAN_COMMIT=aba829d921037cf98b2a62e6afe95dae2b41bb14

UNITY_VERSION=6000.5.9f1
UNITY_EXECUTABLE=/mnt/kingston-unity/Unity/Hub/Editor/6000.5.9f1/Editor/Unity
UNITY_PROJECT_ROOT=/mnt/kingston-unity/Unity/Projects
UNITY_HOST_PROJECT=/mnt/kingston-unity/Unity/Projects/QuestMerkabaScanHost

ANDROID_SDK_ROOT=/mnt/kingston-unity/Unity/Hub/Editor/6000.5.9f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK
ADB_EXECUTABLE=/mnt/kingston-unity/Unity/Hub/Editor/6000.5.9f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb

DONOR_ENV_SCRIPT=/mnt/aidisk/prace/otherscan/Tools/storage/dev_environment.sh
KNOWN_DONOR_BUILD_COMMAND=/mnt/aidisk/prace/otherscan/Tools/unity/build_smoke_apk.sh
TARGET_BUILD_COMMAND=/mnt/aidisk/prace/simplescan/Tools/unity/build_merkaba_apk.sh
TARGET_APK_PATH=/mnt/kingston-unity/Builds/QuestMerkabaScan/QuestMerkabaScan-release.apk
TARGET_DEPLOY_COMMAND=/mnt/aidisk/prace/simplescan/Tools/unity/deploy_merkaba_apk.sh
KNOWN_INSTALL_FORM=<ADB_EXECUTABLE> -s <single-authorized-serial> install -r -d <TARGET_APK_PATH>

GLB_EXPORT_DONOR=target git history e9f37c1:Runtime/Export/ChunkGlbWriter.cs and Runtime/Export/WorldGlbWriter.cs
GLB_VALIDATION_DONOR=target git history e9f37c1:Tools/gltf/verify_interoperability.mjs
UX_DONOR_FILES=/mnt/aidisk/prace/otherscan/Runtime/UI/ControllerRay.shader; ControllerRayDriver.cs; DebugMenu.uxml; DebugMenu.uss; DebugMenuController.cs; DebugMenuFollower.cs; VRDocumentRaycaster.cs; /mnt/aidisk/prace/otherscan/Runtime/RoomScanInputHandler.cs
SETUP_DONOR_FILES=/mnt/aidisk/prace/otherscan/Editor/RoomScanSetupWizard*.cs; VRProjectBootstrap.cs; MetaVrManifestPostprocessor.cs
```

Verified facts:

- The donor host is `/mnt/kingston-unity/Unity/Projects/QuestInfiniteScanHost` and its
  `Packages/com.genesis.roomscan` is a symlink to `OTHER_SCAN_ROOT`.
- The independent target host was created without Library/Temp/Logs/obj/Build and its
  package symlink resolves to `TARGET_ROOT`.
- Donor-only Sigma native/plugin, panel, and serialized scene assets were excluded.
- The Unity executable, target ProjectVersion.txt, Android SDK, bundled ADB, OpenJDK,
  and NDK were verified. Unity reports 6000.5.9f1; Android SDK platform-tools reports
  ADB 36.0.0; bundled NDK is 27.2.12479018.
- Donor build truth is two Android batch invocations: setup/prepare execute method,
  then APK build execute method, followed by non-empty, fresh-mtime, and Unity-log
  success-marker checks. Sigma-specific native/UAV prerequisites are not transferable.
