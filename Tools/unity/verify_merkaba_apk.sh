#!/usr/bin/env bash
set -euo pipefail
qis_verify_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
source "$qis_verify_dir/../storage/dev_environment.sh"
qis_apk="${1:?APK required}"
qis_manifest="${2:?pipeline manifest required}"
qis_build_tools="$(find "$QIS_ANDROID_SDK_ROOT/build-tools" -mindepth 1 -maxdepth 1 -type d | sort -V | tail -n 1)"
"$qis_build_tools/apksigner" verify --verbose --print-certs "$qis_apk"
"$qis_build_tools/zipalign" -c -P 16 4 "$qis_apk"
"$qis_build_tools/aapt" dump badging "$qis_apk" | rg "^package: name='com.genesis.questmerkabascan' "
python3 "$qis_verify_dir/generate_merkaba_pipeline_pack.py" --verify-apk "$qis_apk" --manifest "$qis_manifest"
sha256sum -- "$qis_apk" "$qis_manifest"
