# Goal

Build a production-oriented fork of QuestRoomScan, provisionally named
QuestInfiniteScan, that scans spaces larger than one TSDF volume with bounded Quest
GPU memory and can asynchronously refine completed chunks with DiffSoup on a local
CUDA notebook.

## Required outcome

- Quest keeps the existing Unity/Vulkan GPU mapping path for the active local
  volume.
- A world session owns versioned chunks/submaps, their local origins, bounds,
  revisions, anchor references, neighbors, quality, persistence state, and render
  artifacts.
- Chunk rollover uses an overlap/hysteresis policy and does not interrupt depth
  capture when storage or the notebook is unavailable.
- Inactive chunks can be finalized, persisted, unloaded, discovered again, and
  rendered around the user with a bounded active set.
- A versioned pose graph stores relative constraints and can update only
  `T_world_chunk`; V1 may use Quest tracking plus overlap constraints, with ICP and
  loop closure added behind explicit interfaces.
- Per-chunk keyframes and a refined QRS mesh can be queued to a LAN service.
- The service exposes idempotent world/chunk/revision jobs, runs an upstream
  DiffSoup Python/PyTorch/CUDA worker, validates output, and supports polling,
  cancellation, retry, and artifact download.
- Quest imports a compact, versioned triangle-soup artifact and renders it with a
  Unity/URP shader. Invalid or incomplete artifacts never replace the local mesh.
- Revisit increments chunk revision and supports worker warm-start when the
  upstream optimizer permits it.
- Completed refined/enhanced chunks can be exported as standards-compliant GLB
  with positions, normals, tangents, UV0, base-color atlas, normal texture, and an
  honest metallic-roughness material (`metallic=0`, configurable constant
  roughness until measured maps exist). A world export preserves pose-graph chunk
  nodes and can emit either one bounded GLB or a manifest plus per-chunk GLBs.
- Setup, operation, recovery, protocol, data formats, and device validation are
  documented and reproducible.

## V1 non-goals

- Full DiffSoup training, autograd, optimizer, or remeshing on Quest.
- CUDA-to-Vulkan or CUDA-to-wgpu training rewrite.
- One globally resident TSDF or one monolithic whole-building optimization job.
- Depending on Wi-Fi, server availability, or loop closure for real-time mapping.
- Claiming production performance without profiling on Quest 3 and the target CUDA
  notebook.

## Definition of done

1. All required DAG nodes are `done`, with acceptance evidence.
2. Control-plane validator, server tests, and available Unity/static checks pass.
3. A Unity sample or documented integration path can create/roll/persist/reload
   chunks and exercise a fake refinement backend without hardware.
4. The real server passes contract tests and a CUDA DiffSoup smoke job on supported
   hardware; if hardware is unavailable locally, the precise pending hardware gate
   remains explicit and the goal is not marked complete.
5. Android/Quest smoke run proves bounded active volumes, offline continuation,
   atomic artifact swap, and reload/relocation behavior.
6. Khronos glTF Validator and at least one independent consumer successfully load
   chunk and world GLB/PBR fixtures with correct transforms and texture semantics.
