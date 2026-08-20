# QuestInfiniteScan local compute server

The service is a clean implementation of the protocol in
[`contracts/v2/PROTOCOL.md`](contracts/v2/PROTOCOL.md). The production backend runs
the pinned upstream DiffSoup CUDA extension in a dedicated Python subprocess. A
CUDA-free fake backend remains available for deterministic API tests.

The repository contains only source and the small `uv.lock`. The Python 3.14 virtual
environment, package cache, SQLite data, uploads, and artifacts live on the Kingston
ext4 container via `Tools/server/dev_environment.sh`.

```bash
# all contract, hostile-bundle, persistence, restart, ASGI, and fake-backend tests
Tools/server/run_tests.sh

# LAN service on 0.0.0.0:8420; override QIS_SERVER_HOST/PORT if required
Tools/server/run_server.sh

# direct PyTorch + upstream DiffSoup CUDA rasterizer probe
Tools/server/probe_diffsoup_worker.sh

# first-time pinned Python 3.14.4 / Torch cu130 / DiffSoup build
Tools/server/bootstrap_diffsoup.sh

curl http://127.0.0.1:8420/v2/capabilities
```

Runtime state defaults to `/mnt/kingston-unity/Server/data` in the supplied scripts.
Set `QIS_SERVER_DATA_ROOT` to use a different durable location. Never place it on
exFAT: SQLite, atomic rename, fsync, and permissions are part of the persistence
contract.

The server reads uploads incrementally, verifies the declared length and SHA-256,
then validates every ZIP member and `input.json` declaration before registration.
It rejects traversal, links, encrypted members, duplicate/case-colliding names,
undeclared files, high compression ratios, and per-file/aggregate limit violations.
Job metadata uses SQLite WAL + `synchronous=FULL`; interrupted `running` jobs recover
as `queued`, while successful/canceled terminal jobs remain immutable.

## Worker boundary

`Tools/server/dev_environment.sh` selects the real `diffsoup` backend and points it
at `/mnt/kingston-unity/DiffSoup/.venv/bin/python`. The reproducible versions and
pinned upstream commit are recorded in `diffsoup-worker.lock.json`. The API process
never imports Torch or CUDA; it launches one worker, consumes bounded JSON progress
events, supports cooperative cancellation/timeout, validates the complete returned
ZIP, and only then atomically promotes it into durable artifacts.

The adapter accepts either QRS `live_mesh.qism` or `refined_mesh.qirm`, plus the
chunk-local `frames.jsonl` and declared JPEGs. Geometry stays in Unity left-handed,
Y-up, +Z-forward chunk coordinates. Camera views alone receive the OpenGL -Z camera
conversion required by upstream DiffSoup, so the artifact needs no lossy geometry
round trip before Quest rendering.

Warm start is intentionally exact. A previous server artifact must provide the
declared checkpoint, identical compatibility tag, same profile/feature topology,
same pinned source, and matching world/chunk/source revision. It restores triangle,
feature, alpha, MLP, optimizer, and step state. When any check fails, the worker
starts fresh only if `allowFreshFallback=true`; otherwise the durable job fails with
`warm_start_incompatible`.

Run the real two-revision CUDA acceptance (including incompatible fresh fallback):

```bash
Tools/server/run_cuda_tests.sh
```
