# Quest Infinite Merkaba contract

- A kernel has exactly 5 cm cubic/Merkaba support on an ordinary signed cubic lattice.
- Lattice spacing is exactly 2.5 cm; support half-extent is exactly 2.5 cm.
- `MerkabaGrid` is the only reconstruction authority.
- Persistent reconstruction state is reversible saturating occupancy evidence, RGB,
  colour confidence, and minimal flags. It is never TSDF or mesh state.
- Preserve the clean QuestRoomScan depth, normal, dilation/disparity, projection,
  quality, RGB, XR/world-transform, anchor, repeated-observation, and valid-free-space
  semantics.
- A valid surface adds evidence and colour; valid observed free space subtracts
  evidence; unknown or behind-depth state is not destructively updated.
- Occupancy uses on/off hysteresis. Later clear observations must eat false surfaces.
- Repeated observations refine the same grid; there is no detail representation.
- Storage is an unbounded signed global lattice backed by sparse dense 32^3 chunks.
- Negative addressing uses floor division and floor modulo.
- Live geometry is fixed canonical cube/inscribed-Merkaba boundary geometry derived
  procedurally from occupancy. Neighbourhood masks are inputs, never a 2^26 table.
- Shared primitives have exact local integer ownership: exterior once, interior zero.
- Rendering is GPU-oriented, chunk-culled, direct procedural, and has no live mesh.
- RGB is a quality-weighted temporal average per kernel; there is no triplanar or atlas.
- Save/load persists canonical chunk state only and resumes in identical coordinates.
- GLB export is an offline readout with POSITION, NORMAL, COLOR_0, indices, and matte
  non-metallic glTF PBR material semantics.
- `/mnt/aidisk/prace/otherscan` is read-only donor material only for the verified Unity
  host/toolchain, generic Quest/OpenXR/Meta setup, controller-ray/menu UX, and isolated
  export/build utilities.
- Clean target `main` is the implementation base and sensor-semantics authority.
- Sigma/PRISM reconstruction, persistent TSDF, Surface Nets, trilinear reconstruction,
  triplanar appearance, GSplat, DiffSoup, old submaps/relocation, and alternate detail
  representations are forbidden in production.
