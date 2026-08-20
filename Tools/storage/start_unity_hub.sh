#!/usr/bin/env bash
set -euo pipefail

qis_script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
"$qis_script_dir/mount_kingston_container.sh"
# shellcheck source=Tools/storage/dev_environment.sh
source "$qis_script_dir/dev_environment.sh"

exec unityhub
