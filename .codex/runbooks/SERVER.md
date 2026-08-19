# Local refinement server runbook

This runbook will become executable during nodes `C01`–`C03`.

- Bind to the LAN only by explicit CLI option; default to loopback.
- Store jobs under a configured, dedicated data root and resolve every request path
  beneath it. Never trust archive paths or client filenames.
- Key jobs by `(world_id, chunk_id, revision, input_hash, backend_version)`.
- Use a subprocess boundary for the pinned DiffSoup environment. Capture stdout,
  stderr, exit code, timeout, GPU metadata, and dependency version in job metadata.
- Publish artifacts through a temporary directory, validate, hash, fsync where
  supported, then atomically rename into the terminal job directory.
- On restart, keep terminal jobs; mark orphaned running jobs recoverable/failed per
  policy and never report a partial artifact as complete.
- A fake deterministic backend is mandatory for API and Quest-client testing on
  machines without NVIDIA CUDA.

