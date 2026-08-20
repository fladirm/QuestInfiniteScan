#!/usr/bin/env bash
set -euo pipefail

qis_script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=Tools/storage/dev_environment.sh
source "$qis_script_dir/../storage/dev_environment.sh"

qis_unity_version="${QIS_UNITY_VERSION:-6000.5.9f1}"
qis_editor_root="$QIS_UNITY_EDITOR_ROOT/$qis_unity_version/Editor"
qis_android="$qis_editor_root/Data/PlaybackEngines/AndroidPlayer"

test -x "$qis_editor_root/Unity"
test -d "$qis_android"
test -x "$qis_android/OpenJDK/bin/java"
test -x "$qis_android/SDK/platform-tools/adb"
test -x "$qis_android/NDK/toolchains/llvm/prebuilt/linux-x86_64/bin/clang"

"$qis_editor_root/Unity" -version
"$qis_android/OpenJDK/bin/java" -version
"$qis_android/SDK/platform-tools/adb" version
printf 'Android NDK: '
sed -n 's/^Pkg.Revision[[:space:]]*=[[:space:]]*//p' \
    "$qis_android/NDK/source.properties"
