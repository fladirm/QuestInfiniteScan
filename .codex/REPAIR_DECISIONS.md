# Quest Merkaba Repair Decisions

- Pursuit authority is external to Git at `/mnt/aidisk/prace/.codex-pursuits/quest-merkaba-production-closure/REPAIR_GOAL.md`.
- Repair baseline is immutable commit `0e9081060ed1068aad6e075f4961ad25b72245ff`.
- Every occupied coordinate owns one central octahedron. For each of eight body-diagonal directions it emits its base face when the neighbour is empty, or the three fixed tip sides toward that neighbour when occupied.
- Support size remains 0.050 m, lattice step 0.025 m, and chunk size 32.
- Existing reversible evidence, hysteresis, RGB accumulation, signed coordinates, persistence container, GLB container, Quest UI rays, and build plumbing are retained unless a targeted repair requires modification.
- Canonical geometry will have one CPU authority and one deterministically generated HLSL include consumed by all GPU stages.
- Runtime geometry uses only eight body-diagonal occupancy tests and a 32-bit active mask: one base or three tip sides per direction, hence 8..24 triangles per occupied kernel.
- Intentional primitive penetration/overlap is retained and hidden by ordinary opaque depth testing; no exact Boolean union, clipping, coverage fragments, or micro-triangle subdivision is permitted.
- Canonical CPU/HLSL tables contain six octahedron vertices, eight body-diagonal face rules, eight apexes at neighbour centres, and 32 possible triangles.
- Axis and face-diagonal neighbours do not activate tips.
- Export cleanup is a read-only sparse CPU readout using signed evidence and exactly one radius-1 morphological closing; it never changes canonical state or emits cube geometry.
