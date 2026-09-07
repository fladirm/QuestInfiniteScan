#!/usr/bin/env bash
set -euo pipefail

qis_graph_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
# Reports the SAME finite schedules emitted into the native plugin, including
# repeated Reserve/allocation and count/emit calls. This is not a parser's
# guess at C++ loops and does not claim to measure nonzero GPU work.
exec python3 "$qis_graph_dir/../unity/generate_merkaba_native_executor_shaders.py" \
    --command-graph "$@"
