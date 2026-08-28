# Durable Merkaba decisions

- Chunk edge is 32 kernels: 32^3 dense states per allocated sparse chunk, 0.8 m wide.
- Coordinates are signed `int3`; chunk/local split is mathematical floorDiv/floorMod.
- Evidence is signed saturating fixed-point with distinct occupied-on and occupied-off
  thresholds; occupancy is a cached minimal flag, never the authority.
- RGB is stored packed per kernel with an integer colour-confidence accumulator.
- Canonical support is the exact 5 cm cube decomposition into one central octahedron,
  eight corner tips, and twelve edge wedges; live fixed boundary patches are frozen
  subdivisions of the external edge-wedge faces.
- A 24-bit boundary-patch mask (six faces times four half-step quadrants) is sufficient;
  each patch emits two fixed triangles. It is derived only from the 26 neighbours.
- Patch ownership is the lexicographically least occupied centre sharing a coplanar
  patch; any occupied centre one half-support outward suppresses the patch as interior.
- Topology has exactly the specified immediate 26 inputs. Kernels separated by an empty
  lattice centre remain distinct close sheets even where their 5 cm support bounds touch;
  an unobservable distance-two kernel cannot silently become a topology input.
- Topology dirtiness propagates only to self plus the 26 local neighbours on occupancy
  threshold transitions.
- The target host is `/mnt/kingston-unity/Unity/Projects/QuestMerkabaScanHost` and embeds
  `/mnt/aidisk/prace/simplescan` via `Packages/com.genesis.roomscan`.
- GLB mechanics come from the target's own historical `e9f37c1` writer/validator, adapted
  to Merkaba kernel boundary readout; PRISM world/export controllers are not imported.
- Quest UI styling/ray mechanics come from the current read-only donor UI files, with all
  controller bindings rewritten to the one Merkaba scanner.
