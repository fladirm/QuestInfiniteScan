# QuestInfiniteScan LAN protocol v2

This is the frozen wire boundary between the Quest client and the local worker.
All JSON uses UTF-8, camelCase field names, finite numbers, and the strict parsers in
`quest_infinite_server.contracts`. Unknown fields and unsupported versions fail closed.

## Identity and idempotency

A job is identified only by `(worldId, chunkId, chunkRevision)`. `jobId` is the
SHA-256 of its versioned, length-prefixed identity. The immutable request fields have
a separate `requestFingerprint`:

- same `jobId` + same fingerprint is an idempotent replay and returns the existing job;
- same `jobId` + different fingerprint is HTTP `409 Conflict`;
- a newer chunk revision always has a different job ID and cannot be overwritten by
  a late response for an older revision.

The client persists the exact submission and upload descriptor before attempting the
network call. Losing Wi-Fi therefore leaves a retryable offline item and never blocks
the scan/integration path.

## HTTP resources

The C02 service implements these resources without global mutable job state:

```text
GET    /v2/capabilities
PUT    /v2/jobs/{jobId}                 create/replay immutable submission
PUT    /v2/jobs/{jobId}/input           bounded streaming upload
POST   /v2/jobs/{jobId}/enqueue         idempotent enqueue/retry
GET    /v2/jobs/{jobId}                 poll durable status
POST   /v2/jobs/{jobId}/cancel          idempotent cancel request
GET    /v2/jobs/{jobId}/artifact        streamed terminal artifact
```

The body hash and byte length are checked while streaming into a temporary file.
Only a complete matching body is atomically promoted. Request bodies are never read
wholesale into RAM. Artifact downloads expose the same media type, format version,
length, and SHA-256 as the terminal status.

## State machine

```text
awaiting_upload -> queued -> running -> succeeded
       |             |         |  \-> failed -> queued (explicit retry)
       \-------------+---------+----> canceled
                              running -> queued (server restart recovery)
```

Replaying the current state is idempotent. A terminal successful or canceled job is
immutable. Poll failure, timeout, or an offline server changes no job state on Quest.

## Input chunk bundle v1

Media type: `application/vnd.questinfinitescan.chunk+zip`, format version `1`.
The C03 adapter validates a safe archive before CUDA work. Its canonical contents are:

```text
input.json                         per-file hashes, sizes, coordinate contract
mesh/refined_mesh.qirm             preferred UV/refined QRS chunk mesh
mesh/live_mesh.qism                allowed fallback mapper mesh
keyframes/frames.jsonl             chunk-local camera poses and intrinsics
keyframes/images/NNNNNN.jpg        selected RGB observations
```

All geometry and poses are chunk-local in Unity's left-handed, Y-up, +Z-forward basis
and meters. Archive entries may not be links, absolute paths, duplicate normalized
paths, traversal paths, encrypted members, or exceed declared per-file/aggregate
limits. Every declared file is SHA-256 verified before a job becomes queued.

## DiffSoup runtime artifact v1

Media type: `application/vnd.questinfinitescan.diffsoup+zip`, format version `1`.
The ZIP contains `artifact.json` plus exactly the hashed files declared by it:

```text
model/mesh.ply
model/lut0.png
model/lut1.png
model/mlp_weights.json
model/meta.json
checkpoint/resume.pt               optional; never needed for Quest rendering
```

The worker converts output back to chunk-local Unity coordinates and clockwise front
faces. Features use `diffsoup-sh2-mlp16-v1`: seven LUT features plus alpha, second-order
view-direction spherical harmonics, two ReLU 16-wide layers, sigmoid RGB, and the LUT0
residual channel. The manifest fixes mesh/LUT/count limits and hashes every payload.

A checkpoint is compatible only when its `compatibilityTag` exactly matches the worker
code, schedule, tensor schema, topology identity, and optimizer format. If it does not
match, the worker may start fresh only when `allowFreshFallback` is true.

