# M8 DUAL SPHERE–FLOWER

## CLOSED PRODUCTION ALGORITHMIC CONTRACT

### Mandatory base

```text
c34d27f0ecb51500b12209ed5d2fe72b893726f5
```

This contract replaces every previous Sphere–Flower draft.

**REV-C closure:** this revision preserves every REV-B closure, removes acquisition throttling, and freezes the skin representation. Fibonacci is never a refinement gate and never ties detail convergence to new camera observations. One accepted immutable observation must drain every finite child test that its own bounded evidence can resolve; new viewpoints are required only for new information or ambiguity. Geometry ends at L2. Every valid L2 carrier owns the same immutable planar Flower-7-in-Flower L3/L4/L5 address space and the same recursively subtree-contiguous 399-position embroidery. Scan evidence refines only the piecewise RGB and additive metric-V signal written on that fixed thread. Readout always performs the same three exact barycentric descents and never selects a detail level.

**Excavation-support amendment, 2026-09-06:** FULL is the default matter model of the complementary excavation view. Its boundary against certified FREE supplies derived DIRT support where direct surface coverage is missing. This is an explicit model assumption, not fabricated direct sensor evidence. Direct measurements have precedence. Sections 0.1 and 13.4 define this support and supersede the previous veto-only restriction.

No backward-compatibility layer is permitted. No legacy surface authority may remain beside the final path.

Normative terms:

```text
MUST       mandatory
MUST NOT   forbidden
CERTAIN    mathematically proven by bounded intervals
IMPOSSIBLE mathematically excluded
AMBIGUOUS  insufficient information; request refinement
```

---

# 0. Single production ontology

The persistent scan truth is exactly:

```text
Canonical measured coarse surface:
    M8 KernelState

Persistent complementary excavation model:
    sparse SEE_THROUGH hierarchy; implicit FULL is the default matter model

Persistent fine metric detail:
    FlowerDetail sidecar under an M8 FlowerAddress

Persistent fine appearance:
    ThreadAtlas under the same FlowerAddress
```

Derived only:

```text
Sphere–Flower incidence
Flower symbols
petals
completed petals
DIRT support from the FREE/FULL excavation boundary
procedural draw pages
GLB/3D Tiles geometry
```

Prohibited authorities:

```text
second surface world
persistent mesh
persistent XYZ knots
persistent PortKey graph
implicit scalar field
surface fitter
mesh compiler as world truth
```

`SEE_THROUGH` means that the complete represented support was certified as
visible free volume. The complementary FULL volume is treated as matter by
the excavation model until visibility removes it. FULL is not a direct
measurement of a physical surface, but it IS supporting matter in the model;
it is not merely a passive veto mask.

## 0.1 Two stored views of the same M8 space

Both views use the same signed M8 addresses and are persisted together.
There is no second coordinate index, persistent mesh or independent world.

```text
D(x): direct view
    SURFACE  direct measured R1 / Sphere-Flower surface
    UNKNOWN  no direct surface or free-volume evidence resolves this location
    FREE     certified visible free volume

E(x): complementary excavation view
    FULL     default matter, until certified visibility removes it
    FREE     certified visible free volume
    SURFACE  derived interface: direct surface where available, otherwise DIRT
```

E.SURFACE is a derived interface, not a third persistent occupancy encoding.
The two-bit node states in section 2 remain unchanged.

A reliable observation removes only its certified visible prefix:

```text
camera ---- certified FREE ---- endpoint SURFACE ---- remaining FULL
```

The endpoint is direct evidence. The unobserved region behind it retains the
FULL matter assumption. Missing direct geometry does not make that region
empty. Where a direct room/corner/wall surface is absent, the exposed
FREE/FULL interface MUST supply DIRT support as defined in section 13.4.
This support is part of the rendered/exported model, not merely a diagnostic
overlay, and does NOT require a unique direct Flower completion.

Direct surface has precedence over DIRT for the same represented footprint.
DIRT does not write an occupied M8 kernel, invent an R1 plane, create R2/R3
measurement evidence, seed FlowerDetail/ThreadAtlas, or count as a confirmed
petal in a subsequent direct completion. Its authority is the excavation
model's boundary itself, not invented sensor evidence.

The distinction is provenance, not an optional support feature: direct
geometry reconstructs observed surfaces; excavation geometry supports missing
surfaces using the default FULL matter model. Both are evaluated from the one
persistent ontology above.

Same-observation endpoint support is excluded from THROUGH before mutation.
A later direct object restores FULL over its endpoint support and enters
normal R1 admission. FREE is not an irreversible ban on later matter.
Conversely, later certified visibility removes contradictory matter and DIRT.
No DIRT or direct matter may remain inside currently certified FREE.

Two real close parallel sheets MUST NOT be merged just because both exist.
Only certified free-volume evidence excludes contradictory/front surfaces.
The boundary at a field-of-view, range or validity frontier may also be DIRT
under the explicit FULL model; it MUST NOT be labelled as an observed wall.
No missing captured texture is manufactured for it.

---

# 1. Exact persistent data model

## 1.1 M8

`KernelState` remains exactly 16 bytes:

```c
struct KernelState
{
    int  OccupancyEvidence;
    uint PackedColor;
    uint ColorConfidence;
    uint Flags;
}
```

Its position remains implicit:

\[
C_K=aK,\qquad a=0.025\ {\rm m},\qquad K\in\mathbb Z^3.
\]

It remains the only measured coarse positive surface authority. Derived DIRT support is supplied by the persistent excavation model, not by an additional positive KernelState field.

## 1.2 First-hit seed encoding

No new `KernelState` field is added.

At the ABI cutover the former legacy `NeedsCarve` flag bit is reclaimed and renamed `R1_SEED`. It has no remaining CARVE meaning.

```text
Occupied=0, PlaneValid=0, R1_SEED=0
    no surface

Occupied=0, PlaneValid=1, R1_SEED=1
    strict unconfirmed R1 seed

Occupied=1, PlaneValid=1, R1_SEED=0
    stable canonical R1 surface
```

A seed stores:

- the current quantized measured plane;
- current packed color;
- positive evidence below `OccupiedOnThreshold`;
- no drawable canonical surface.

A seed cannot create FlowerDetail, ThreadAtlas state or a COMPLETED petal. A certified THROUGH contradiction over its complete support deletes it immediately.

## 1.3 FlowerDetail and exact parent invalidation

Fine metric truth is subordinate to one parent M8 owner, but **not** to every small metric update of that owner. Fine detail is invalidated only by a structural change of the parent Flower anchor:

```text
R1 deleted or created with a different sheet
root sector changes
rootSign changes
free-side orientation changes
explicit ERASE
```

A compatible plane refinement that remains inside the same generated root sector/rootSign does not invalidate descendants; the descendants are re-evaluated relative to the refined parent.

No generation field is added to every `KernelState`.

A sparse owner epoch exists only for M8 owners that actually have persistent fine state:

```c
struct FlowerOwnerEpoch
{
    uint KernelLocal;   // 0..511 inside the owning tile/page
    uint Epoch;         // 0 is invalid; starts at 1
}
```

The epoch table belongs to the FlowerDetail page and is absent for tiles with no fine state. A structural parent change increments only that owner's epoch in the same transaction as the M8 change. `0xffffffff -> 0` is forbidden; before wrap, that owner is transactionally rebased: descendants are tombstoned, epoch becomes 1, and only subsequently re-observed detail may reappear.

One geometric fine-metric record remains 16 bytes and is used only by the
L1/L2 Sphere–Flower evaluator:

```c
struct FlowerDetailRecord
{
    uint Key;
    int  Lower;
    int  Upper;
    uint ParentEpoch;
}
```

`Key` is parent-owner local and contains **no dynamic branch ordinal**:

```text
bits  0..1   geometryLevel      2   // L0..L2 only
bits  2..5   geometryChildPath  4   // 2 bits × L1/L2
bits  6..11  petalClass         6   // 0..47
bits 12..15  channel            4   // 13 line classes / local channel
bits 16..18  kind               3
bit      19  rootSign           1
bits 20..24  sector             5   // generated fixed loop sector
bits 25..31  reserved           7   // MUST be zero
```

Codegen MUST prove `maxSectorCount <= 32` for every line class. If this proof fails, the ABI build fails; sectors are never truncated or aliased.

Kinds:

```text
R2_PHASE
R3_PHASE
KNOT_METRIC
TOMBSTONE
```

`Lower/Upper` are fixed-point interval endpoints:

- phase values use signed Q2.29 tangent-half-angle within one generated sector;
- no single best float is persisted.

Render value is the deterministic interval midpoint:

\[
x_{\rm draw}=x_-+\left\lfloor\frac{x_+-x_-}{2}\right\rfloor.
\]

A record is valid iff its `ParentEpoch` equals the current sparse owner epoch. Parent structural invalidation therefore removes every descendant logically without walking the descendant records.

L3/L4/L5 metric V does not use `geometryChildPath`. It is the additive signal
on the fixed L2 skin thread defined in section 20. One L2 carrier with any
persistent V innovation has exactly one run:

```c
struct FlowerSkinMetricRun
{
    uint FlowerKey;       // canonical L2 carrier identity
    uint GroupBase;       // first compact seven-child V group
    uint SplitBitsLo;     // split bits 0..31
    uint SplitBitsHi;     // split bits 32..56; bits 57..63 MUST be zero
    uint ParentEpoch;
    uint Reserved;        // MUST be zero
}
```

The 57 bits are exactly:

```text
bit  0       L2 -> L3 V split
bits 1..7    L3 -> L4 V splits in L3-parent thread order
bits 8..56   L4 -> L5 V splits in L4-parent thread order
```

Each set split bit owns one compact group of exactly seven intervals:

```c
struct FlowerVInterval
{
    int Lower;            // signed Q5.26 metres
    int Upper;            // signed Q5.26 metres
}

struct FlowerVGroup
{
    FlowerVInterval Child[7];
}
```

`FlowerVGroup` order and child order are the immutable embroidery order from
section 20. A split bit and all seven child intervals are published in one
transaction. A missing group is not zero-filled storage: its logical
descendants inherit zero innovation at that level. The fixed 399-position
topology is implicit and is never stored in FlowerDetail.

## 1.4 ThreadAtlas

ThreadAtlas is persistent appearance truth under the same parent-owner epoch. It is not a cache and not another spatial world.

```c
struct ThreadRun
{
    uint FlowerKey;       // canonical L2 carrier identity
    uint ProgramRef;      // optional certified optical program
    uint GroupBase;       // first compact seven-child RGB group
    uint SplitBitsLo;     // split bits 0..31
    uint SplitBitsHi;     // split bits 32..56; bits 57..63 MUST be zero
    uint ParentEpoch;
}
```

The RGB mask has the same bit meanings as the V mask, but it is an independent
persistent authority. Each RGB split adds exactly seven actual captured-color
intervals, not seven residuals:

```c
struct ThreadColorInterval
{
    half4 LowerLinearRgba;
    half4 UpperLinearRgba;
}

struct ThreadColorGroup
{
    ThreadColorInterval Child[7];
}
```

The unsplit root color is the L2 carrier's canonical M8 captured color. A
child color replaces its nearest explicit RGB ancestor for its footprint;
colors from several levels are never accumulated. Thread programs contain
only reusable optional certified optical response. They contain no geometry,
topology, world XYZ, Fibonacci origin or refinement state.

The immutable topology and embroidery order are generated globally, not
persisted per L2. A Thread record whose parent epoch is stale is ignored
identically to stale FlowerDetail.

---

# 2. Sparse SEE_THROUGH hierarchy

The dual uses the existing signed Block→Chunk→Tile coordinate hierarchy.

It does not introduce another coordinate index.

## 2.1 Node states

Every dual node has exactly one 2-bit state:

```text
00 ALL_FULL
01 ALL_THROUGH
10 MIXED
11 INVALID
```

A missing block means `ALL_FULL`: the excavation model starts as matter, not as empty space. An unresolved COLD payload is still AMBIGUOUS, not permission to substitute FULL or emit DIRT.

## 2.2 Block payload

A block spans \(256^3\) L0 kernels.

```c
struct DualBlockMeta
{
    uint StateAndGeneration;
    uint PayloadIndex;
}
```

For a MIXED block, `PayloadIndex` addresses 512 child states:

```text
512 × 2 bits = 128 bytes
```

A MIXED child chunk is resolved through the same existing block/chunk address relation used by M8.

## 2.3 Chunk payload

A chunk contains 64 tiles.

```c
struct DualChunkPayload
{
    uint2 NonFullMask;   // 64 bits
    uint2 MixedMask;     // 64 bits
    uint  LeafRefBase;
    uint  Generation;
    uint  Reserved0;
    uint  Reserved1;
}
```

Interpretation per tile:

```text
NonFull=0
    ALL_FULL

NonFull=1, Mixed=0
    ALL_THROUGH

Mixed=1
    MIXED leaf
```

MIXED leaf index:

\[
i=\operatorname{popcount}
\left(
MixedMask\land((1\ll tileLocal)-1)
\right).
\]

The corresponding ref is:

```text
DualLeafRefs[LeafRefBase + i]
```

## 2.4 Leaf payload

One mixed tile:

```c
struct DualLeaf
{
    uint ThroughBits[16];
}
```

Cost:

```text
512 bits = 64 bytes/tile
32768 HOT leaves = 2 MiB
```

## 2.5 Promotion and collapse

Writing one leaf bit:

1. descend through the fixed Block→Chunk→Tile address;
2. materialize a child from its parent uniform value;
3. modify the bit;
4. collapse upward immediately when all children agree.

Exact collapse:

```text
all 512 leaf bits zero
    tile -> ALL_FULL

all 512 leaf bits one
    tile -> ALL_THROUGH

all 64 tile children same
    chunk collapses

all 512 chunk children same
    block collapses
```

Discarded child payloads become reclaimable only after the publishing generation retires.

## 2.6 GPU residency cost

At maximum current capacities:

```text
mixed block masks       <= 1.0 MiB
chunk summaries         <= 8.0 MiB
HOT dual leaves          = 2.0 MiB
dense dual leaf refs    <= 0.125 MiB
27-tile halo             = 3.375 MiB
```

Missing/cold nodes remain represented by their sparse persistent state.

## 2.7 Failure rule

An unresolved COLD dual node yields `AMBIGUOUS`.

It never yields `THROUGH`.

No destructive mutation is allowed until the required node is resident.

---

# 3. Exact Sphere–Flower geometry

For level \(L\):

\[
a_L=a\,2^{-L}.
\]

Directions:

\[
d\in\{-1,0,1\}^3\setminus\{0\}.
\]

Shell:

\[
m=d\cdot d.
\]

```text
R1: m=1,  6 directions,  core/face
R2: m=2, 12 directions,  shape/edge
R3: m=3,  8 directions,  closure/corner
```

Sphere radius:

\[
R_d=a_L\sqrt m.
\]

Neighbour distance is also \(R_d\).

## 3.1 Shared address

\[
J_L(K,d)=2K_L+d.
\]

For \(Q=K+d\):

\[
J_L(Q,-d)=J_L(K,d).
\]

`J` is computation, not persistent data.

## 3.2 Junction type

\[
p(J)=
(j_x\&1)+(j_y\&1)+(j_z\&1).
\]

```text
p=1 face junction
p=2 edge junction
p=3 corner junction
```

## 3.3 Canonical undirected line

For directed \(d\), choose representative \(r(d)\) so its first nonzero component is positive.

This gives exactly:

```text
3 axis classes
6 face-diagonal classes
4 body-diagonal classes
```

The generated line-class table contains only these 13 vectors.

## 3.4 Canonical loop basis

Let:

\[
\hat r=\frac r{|r|}.
\]

Choose Cartesian axis \(e_p\) whose absolute dot with \(\hat r\) is smallest; ties are X, then Y, then Z.

Then:

\[
E_1=
\frac{e_p-(e_p\cdot\hat r)\hat r}
{|e_p-(e_p\cdot\hat r)\hat r|}
\]

\[
E_2=\hat r\times E_1.
\]

This basis depends only on `lineClass`, never on endpoint or camera.

## 3.5 Shared loop

\[
M=\frac{a_L}{2}J.
\]

\[
\rho=\frac{\sqrt3}{2}R_d.
\]

\[
X(\theta)=M+\rho(E_1\cos\theta+E_2\sin\theta).
\]

Both endpoints therefore evaluate the identical loop using identical operation order.

---

# 4. Typed transient Flower symbol

The mathematical symbol is:

\[
\boxed{\Sigma=(L,J,lineClass,sector,rootSign)}.
\]

There is deliberately no `branchRank`.

For a fixed valid `(J,lineClass)` the two endpoint kernels of the undirected relation are algebraically unique. Let `r` be the canonical representative of the line class. The valid orientation is the one for which `J-r` is componentwise even:

\[
K=\frac{J-r}{2},\qquad Q=K+r.
\]

If the opposite directed representative is required, endpoint orientation is a generated sign; it never creates another endpoint pair.

Therefore a shared loop never needs to sort roots from arbitrary nearby owners. It compares only the roots of its two fixed endpoints. Parallel sheets survive because the observation is allowed to occupy different members of the eight-overlap M8 owner set; those sheets consequently live on different fixed Flower relations instead of being renumbered inside one relation.

Two endpoint roots correspond iff all are true:

```text
same J
same lineClass
same generated sector
same rootSign
metric intervals not mutually impossible
```

If two possible correspondences remain after interval classification, the result is `AMBIGUOUS`; no ordinal is assigned.

`Σ` is transient. It is not persisted as a graph.

GPU scratch representation:

```c
struct FlowerSymbolKey
{
    int3 Junction;
    uint Tag;
}
```

`Tag`:

```text
bits  0..2   level
bits  3..6   lineClass
bit       7  rootSign
bits  8..12  sector
bit      13  endpointOrientation
bits 14..16  shellValidity
bits 17..19  status
bits 20..31  generated/local scratch
```

Statuses:

```text
CONFIRMED
VETO
HOLE_CANDIDATE
COMPLETED
UNRESOLVED
REFINE
```

These statuses are outputs of direct Boolean/interval predicates, not a persistent state machine.

---

# 5. Plane and interval authority

Stored plane:

\[
N\cdot(X-C_K)-\delta=0.
\]

## 5.1 Quantization bounds

Offset quantization contributes:

\[
\epsilon_{\delta,q}=\frac{a}{2\cdot127}.
\]

The octahedral normal code has half-cell width \(1/1023\) in each encoded axis.

A conservative vector-normal bound is:

\[
\epsilon_{N,q}=\frac{2\sqrt{18}}{1023}.
\]

The realtime frontend accepts a digital measurement using both depth views
and both captured RGB views (§19). It does NOT require an external sensor
accuracy certificate, a laboratory error profile, or a user-supplied asset.

\[
\epsilon_{N,s},\qquad \epsilon_{\delta,s}
\]

describe additional enclosure of that accepted measurement, not guaranteed
physical sensor accuracy. With no additional representation error these terms
are zero; stored-plane quantization and ordered FP32 operation bounds still
apply. Zero here is NOT a claim of zero physical depth noise.

CERTAIN/IMPOSSIBLE classify the generated geometry relative to accepted
observations. Sensor admission is the realtime four-stream consistency policy;
it is not a metrological certification of the environment.

Persistent plane bounds:

\[
\epsilon_N=\epsilon_{N,q}+\epsilon_{N,s}
\]

\[
\epsilon_\delta=\epsilon_{\delta,q}+\epsilon_{\delta,s}.
\]

## 5.2 Floating-point enclosure

For \(n\) ordered binary32 operations:

\[
\gamma_n=\frac{nu}{1-nu},
\qquad
u=2^{-24}.
\]

Shaders compile with contraction/fast reassociation disabled for these expressions.

`A` uses \(\gamma_6\); `B,C` use \(\gamma_7\).

No manually selected epsilon exists.

## 5.3 ABC interval

Central decoded values:

\[
A_0=N_0\cdot(M-C_K)-\delta_0
\]

\[
B_0=\rho\,N_0\cdot E_1
\]

\[
C_0=\rho\,N_0\cdot E_2.
\]

Bounds:

\[
\epsilon_A=
\epsilon_N|M-C_K|
+\epsilon_\delta
+\gamma_6\,S_A
\]

\[
\epsilon_B=
\rho\epsilon_N+\gamma_7\,S_B
\]

\[
\epsilon_C=
\rho\epsilon_N+\gamma_7\,S_C,
\]

where \(S_A,S_B,S_C\) are the exact sums of absolute operands used by the corresponding expression.

Therefore:

\[
A\in[A_0-\epsilon_A,A_0+\epsilon_A]
\]

and identically for \(B,C\).

These intervals exist only in observation/page-compilation scratch.

---

# 6. Root algebra and all degeneracies

For a scalar triple:

\[
L(\theta)=A+B\cos\theta+C\sin\theta.
\]

Define:

\[
Q=B^2+C^2,
\qquad
\Delta=Q-A^2.
\]

## 6.1 Scalar nondegenerate roots

For \(Q>0,\Delta>0\):

\[
u_\pm=
\frac{
-A(B,C)
\pm\sqrt{\Delta}(-C,B)
}{Q},
\]

where:

\[
u=(\cos\theta,\sin\theta).
\]

World knot:

\[
X_\pm=M+\rho(E_1u_x+E_2u_y).
\]

## 6.2 Exact degeneracies

```text
Q=0, A≠0
    IMPOSSIBLE: no root

Q=0, A=0
    COPLANAR_LOOP: every phase is a root;
    no isolated knot may be emitted

Q>0, Δ<0
    IMPOSSIBLE

Q>0, Δ=0
    one tangent root

Q>0, Δ>0
    two roots
```

`COPLANAR_LOOP` is a finite alphabet class. It may support a parent petal boundary but cannot independently select a branch.

## 6.3 Interval classification

Let:

\[
|A|_{\min},|A|_{\max}
\]

be exact interval absolute bounds and:

\[
Q_{\min},Q_{\max}
\]

the outward-rounded interval square sum.

```text
|A|max² < Qmin
    CERTAIN_SECANT

|A|min² > Qmax
    IMPOSSIBLE

singleton equality
    CERTAIN_TANGENT

otherwise
    AMBIGUOUS
```

An interval crossing \(Q=0\) or \(\Delta=0\) is always `AMBIGUOUS`.

No root is emitted from an ambiguous interval.

---

# 7. Root sector, sign and branch correspondence

## 7.1 Generated phase sectors

Each loop is cut by radical planes belonging to incident `R1⊂R2⊂R3` cube flags.
The R1 face loops additionally include the exact same-shell power-order
boundaries required by section 8.4.2. These are differences of incident
radical-plane functions, not new measured planes or fitted geometry.

For an incident direction \(q\), substitute the loop into:

\[
q\cdot(X-C_K)=\frac{a_L}{2}(q\cdot q).
\]

This produces a fixed equation:

\[
\alpha+\beta\cos\theta+\gamma\sin\theta=0.
\]

Its exact algebraic roots partition the loop into half-open sectors.

For R1, codegen uses the common arrangement of the radical cuts and the
incident edge/edge and corner/corner equality cuts from section 8.4.2.
All such cuts use the same exact loop/plane solver. A changed ordering may
never remain hidden inside one sector. Codegen re-proves the section 1.3
five-bit sector bound before publishing the shared CPU/HLSL tables.

All coefficients belong to:

\[
\mathbb Q(\sqrt2,\sqrt3,\sqrt6).
\]

Code generation evaluates them symbolically and emits outward-rounded interval boundaries. No sampled adjacency is generated.

A non-boundary root is `CERTAIN` in a sector only when its complete metric
interval lies strictly inside that sector.

An exact symbolic proof that a root equals a generated sector boundary
classifies it as `CERTAIN_BOUNDARY`. The generated canonical half-open
tie-break assigns that boundary identity to exactly one adjacent sector
and flag. Its complete metric interval is retained unchanged; it MUST NOT
be clipped, narrowed or shifted into the chosen sector. Topological
ownership follows the proven symbolic boundary identity, not the width
of its numerical enclosure.

An interval which merely touches or crosses a boundary without that exact
equality proof remains `AMBIGUOUS`. A numerical midpoint, tolerance or
overlap with the boundary enclosure is not an equality proof.

## 7.2 Root sign

`rootSign` is the algebraic `±` from the canonical formula above.

The plane sign is already canonicalized by the existing first-nonzero-normal rule.

Because `E1/E2` use undirected `lineClass`, changing \(d\to-d\) does not swap the basis or root sign.

## 7.3 Exact root correspondence without branch ranks

A fixed `(J,lineClass)` relation has exactly two M8 endpoint kernels. Each endpoint plane contributes at most two analytic roots to the shared loop.

After canonical basis/orientation transport, correspondence is purely symbolic:

```text
same generated sector
same algebraic rootSign
intervals overlap or are mutually compatible
```

The possible outcomes are:

```text
exactly one compatible pair
    CERTAIN shared root

no compatible pair
    IMPOSSIBLE for this relation

more than one compatible pairing
    AMBIGUOUS -> refinement
```

No root is matched by Euclidean distance, no nearby-owner list is sorted, and no branch ordinal can change when later observations arrive.

Parallel or thin sheets are represented by different overlapping M8 carrier owners. Their Flower relations therefore have different algebraic endpoint pairs / `J` addresses. They do not require multiple dynamically ranked branches inside one shared relation.

Hinges and corners may share a world knot while remaining distinct symbolic petals/line classes; their identity is topological, not an ordinal branch number.

---

# 8. Finite Flower alphabet

The finite topology is the generated **48-cell Sphere–Flower flag alphabet** of one M8 support cube. The word `petal` here means a curvilinear Flower cell bounded by analytic loop strands. It is **not** a canonical planar triangle and it is never stored as three XYZ vertices.

## 8.1 Nodes

```text
6  R1 face nodes
12 R2 edge nodes
8  R3 corner nodes
---------------
26 nodes
```

Each node is an analytic root symbol on its generated R1/R2/R3 loop.

## 8.2 Flag petals

Let:

\[
f=s_i e_i
\]

\[
e=f+s_j e_j,\qquad j\ne i
\]

\[
c=e+s_k e_k,
\]

where `k` is the remaining axis.

Every chain

\[
f\subset e\subset c
\]

defines one oriented three-strand Flower petal.

Count:

\[
6\cdot4\cdot2=48.
\]

Its combinatorial orientation is:

\[
\omega=\operatorname{sign}\det(f,e,c)\in\{-1,+1\}.
\]

The three anchors are the loop-root symbols associated with `f,e,c`. The geometric boundary between them is the generated analytic/nested Flower strand, not a straight canonical edge. Straight triangles may appear only as presentation tessellation after evaluating the nested Flower knots.

## 8.3 Strand incidences

The 48 petals contain 72 generated oriented boundary-strand classes:

```text
24 face↔edge incidences
24 edge↔corner incidences
24 corner↔face incidences
```

Static tables:

```text
Node[26]
Strand[72]
Petal[48]
PetalBoundary[48][3]
StrandIncidentPetals[72]
```

These tables encode incidence only; no runtime mesh adjacency is reconstructed.

## 8.4 Runtime petal predicate

A flag petal is admissible only when:

1. all required anchor roots are `CERTAIN` or are exact inherited parent roots;
2. every root has one certain canonical generated sector identity, and the
   petal uses that root through the generated Node/Strand/Petal incidence;
3. each shared-loop endpoint correspondence satisfies section 7.3;
4. the three boundary-strand tangent/orientation intervals are nondegenerate;
5. winding agrees with the direct free-side orientation;
6. no node/strand/petal support is dual-vetoed.

R2/R3 direct measurement is not required for basic R1 existence. When absent, the R1 carrier may predict the corresponding higher-shell anchor **only as provisional geometry inside the same unique flag**. Such a prediction does not create persistent R2/R3 detail and cannot by itself prove a hole completion.

### 8.4.2 Exact R1 root ownership and shared-anchor incidence

For a fixed directed R1 face carrier \(f=s_i e_i\), use the existing power
residual:

\[
\Phi_q(X)=q\cdot(X-C_K)-\frac{a_L}{2}(q\cdot q).
\]

For canonical root ownership only, select the maximum `Phi` among the four R2 edge directions incident to
`f`, then the maximum among the two R3 corner directions incident to that
selected edge. Exact ties use the smaller frozen generated `Node` ordinal
at each step. No observed score, distance, epsilon or runtime search is used.

Candidates compared at each step belong to the same shell, so their
constant terms cancel:

\[
\Phi_a-\Phi_b=(a-b)\cdot(X-C_K).
\]

In the chart \(\xi=2(X-C_K)/a_L\), the R1 shared loop and selected flag
\(f\subset e=f+s_j e_j\subset c=e+s_k e_k\) obey:

\[
s_i\xi_i=1,\qquad \xi_j^2+\xi_k^2=3,
\qquad 0\le s_k\xi_k\le s_j\xi_j\le2.
\]

The support half-width is `a_L`, hence the chart clipping bound is exactly
`2`, not `1`. On the R1 loop the bound is redundant because the absolute
tangential coordinates are at most \(\sqrt3<2\). Signs and relative order
partition each face into eight flag cells, giving the existing 48 flags.
This face chart does not replace or flatten the R2/R3 world loops. It
classifies the canonical owner of a root, not the admissibility of a
neighbouring petal that references that root.

Codegen uses the existing `Petal[48]` incidence, exact loop boundaries and
exact sign algebra on the power differences. The sign at a certified
sector interior may be evaluated by its exact positive-angle boundary
limit; no finite angular step or sampled adjacency is permitted.
It emits one `Sector→PetalMask` for each directed R1 face and sector.
Each mask MUST contain exactly one incident flag; all eight flags of each
face MUST be covered. Zero or multiple incompatible flags is a codegen
failure, not a runtime choice.

The half-open equality convention defines exact ownership. A root proven
symbolically equal to a generated boundary is `CERTAIN_BOUNDARY` and is
owned by exactly one adjacent sector/flag under the generated canonical
tie-break. Its full metric enclosure remains unchanged even when that
enclosure crosses the boundary. The strict-interior condition in section
7.1 applies only to non-boundary roots. Touching or crossing without an
exact symbolic equality proof is still `AMBIGUOUS`; no epsilon or interval
clamp is permitted. Direction, sector and rootSign transport remain the
generated shared-loop conventions.

Canonical root ownership and topological petal incidence are distinct:

```text
RootOwner(root) = canonical half-open sector/flag ownership
PetalUsesRoot(petal,root) = generated Node/Strand/Petal incidence

PetalUsesRoot MUST NOT require Petal == RootOwner.
```

`Sector→PetalMask` supplies only canonical root ownership and unique
symbolic identity. All petals incident to that root under the generated
incidence may reference the same identity, including when its canonical
owner is another petal. This applies to certain interior roots as well as
symbolically proved boundary roots. It does not duplicate roots, create
geometry, change a rootSign or sector, or modify a metric enclosure.

An anchor reference MUST NOT be tested against the intersection of its
incident petals' sector-owner interior constraints. In particular, a
completion donor MUST NOT require a shared R1 knot to lie simultaneously
inside both donors' owner cells. Those cells classify ownership; incidence
defines sharing. Every reference still requires a CERTAIN root, the exact
generated loop identity, compatible shared-root evidence and the existing
orientation, closure and dual predicates. Numeric boundary touching without
symbolic equality remains AMBIGUOUS.

There is one canonical boundary-root identity, not one copy per incident
petal. Runtime and export references resolve that same identity. The
geometric domain of a generated child knot remains its exact generated
loop; a referencing petal's ownership region must not replace that domain.

## 8.5 Child substitution

For a parent petal with half-grid node addresses `A,B,C`, the identical inherited nodes at `L+1` are:

\[
2A,\quad2B,\quad2C.
\]

New child addresses are:

\[
A+B,\quad B+C,\quad C+A.
\]

The four exact child flag cells are:

\[
(2A,A+B,C+A)
\]

\[
(A+B,2B,B+C)
\]

\[
(C+A,B+C,2C)
\]

\[
(A+B,B+C,C+A).
\]

Therefore inherited knot identity obeys:

\[
N_L\subset N_{L+1}
\]

by integer address identity. Codegen MUST additionally prove that every midpoint address maps to the expected R1/R2/R3 loop class and that every inherited boundary strand has the same canonical orientation on both incident children.

The canonical geometry of a level is the evaluated nested Flower knots/strands. This geometric substitution is applied only for L0 -> L1 and L1 -> L2. L2 is the terminal raster carrier. It MUST NOT be applied to create L3, L4 or L5 world knots, vertices, silhouettes or depth.

Presentation rasterization may connect evaluated L2 knots into triangles, but those triangles are disposable output and never define the scan truth. The planar L3/L4/L5 skin hierarchy inside each L2 carrier is the distinct fixed address construction in section 20; it never changes this geometric alphabet or its knot identity.

## 8.6 Failure

```text
zero admissible petal classes
    UNRESOLVED or VETO

one admissible class
    CERTAIN

more than one incompatible class
    AMBIGUOUS -> REFINE
```

No class is selected by smoothness, distance or score.

## 8.7 Cost

Generated immutable topology remains a small finite table set (target below 64 KiB for incidence/orientation/sector/child tables). Runtime adjacency-search cost is zero.

---

# 9. SEAL/BEND

For corresponding endpoint coefficient intervals \(P,Q\) in the common loop basis:

\[
SEAL=\frac{P+Q}{2}
\]

\[
BEND=\frac{P-Q}{2}.
\]

These are transient interval expressions.

## 9.1 Root-phase form

For certain unit-circle roots \(u_P,u_Q\), define:

\[
u_S=
\frac{u_P+u_Q}{|u_P+u_Q|}.
\]

Antipodal uncertainty is `AMBIGUOUS`.

Signed tangent-half-angle displacement:

\[
\tau(u,v)=
\frac{\operatorname{cross}_2(u,v)}
{1+u\cdot v}.
\]

Then:

\[
q_B=\tau(u_S,u_Q)
=-\tau(u_S,u_P).
\]

Interpretation:

```text
qB interval contains zero
    flat-compatible seal

qB certain and representable at child
    coherent BEND -> refinement

qB crosses sector/denominator singularity
    AMBIGUOUS

no compatible sector/root correspondence
    IMPOSSIBLE
```

The shared draw root is computed once from the canonically ordered endpoint pair and the same `uS` expression.

## 9.2 Evidence merge, residual extraction and incidence closure

These are three different operations:

```text
EVIDENCE MERGE != RESIDUAL EXTRACTION != INCIDENCE CLOSURE
```

Multiple direct measurements claiming the same physical symbol/quantity are
merged by interval intersection. An empty intersection is conflicting evidence
and is IMPOSSIBLE for that candidate.

Parent prediction and direct observation are differenced by the exact
tangent-half-angle residual. They are NOT competing estimates to intersect;
their separation is precisely the detail being represented. No pred/obs
overlap requirement is permitted in R2 or R3.

Multiple incident syntheses claiming the same physical child knot are closed
by interval intersection as in section 10.6. An empty intersection is an
incidence conflict, not a reason to reject a nonzero prediction residual.

Global invariant:

> Prediction and observation are differenced; peer evidence and shared incidence are intersected.

---

# 10. Exact R2 analysis/synthesis

R2 uses the six undirected face-diagonal channels:

```text
xy+
xy-
xz+
xz-
yz+
yz-
```

R2 geometric analysis/synthesis refines the Sphere–Flower carrier only through
L2. It never creates an L3/L4/L5 geometric child. Those names denote only the
planar skin signal hierarchy of section 20.

R2 detail is **not** obtained by splitting a parent phase in half and it is not a Hessian. It is the exact residual between:

1. the child-loop root predicted by the already committed parent Sphere–Flower evaluator; and
2. the directly observed root on that exact generated child loop.

This ties refinement to the actual nested sphere geometry instead of to an invented phase-wavelet identity.

## 10.1 Canonical phase displacement

For two certain roots `u,v` in the same generated sector:

\[
\tau(u,v)=
\frac{\operatorname{cross}_2(u,v)}{1+u\cdot v}.
\]

`τ` is the signed tangent-half-angle rotation taking `u` to `v`. A denominator interval containing zero or a sector crossing is `AMBIGUOUS`.

For a scalar tangent-half-angle turn `r`, define the exact rational rotation:

\[
c(r)=\frac{1-r^2}{1+r^2},\qquad
s(r)=\frac{2r}{1+r^2}.
\]

\[
\mathcal R_r(u)=
\begin{pmatrix}
c(r)&-s(r)\\s(r)&c(r)\end{pmatrix}u.
\]

No inverse trigonometric function is required.

## 10.2 Self-similar parent-to-child phase transport

M8 and its three R1/R2/R3 sphere shells define one fixed 3D Flower lattice.
L1 and L2 are the two dyadic, self-similar substitutions of the same Flower
petal from section 8.5. R2_PHASE is not transferred between unrelated loops
and is never interpolated.

For each newly introduced child knot `A+B`, `B+C` or `C+A`, codegen derives
the exact child J, lineClass, canonical E1/E2, sector, parent-channel mapping
and orientation sign `sigma_c` from that substitution's incidence. The
mapping MUST be unique. Failure to derive a unique mapping is a codegen
contract failure, never a runtime choice or search.

First restrict the canonical M8 R1 carrier to that exact generated child loop:

\[
u_c^{base}=\operatorname{Root}(R1\text{ carrier restricted to exact child loop}).
\]

An ancestor R2_PHASE is a dimensionless tangent-half-angle bend. Its
self-similar pullback preserves the residual magnitude; only the generated
channel and orientation transport apply:

\[
\boxed{u_c^{pred}=\mathcal R_{\sigma_c r_{parent}}(u_c^{base})}.
\]

With multiple committed ancestor R2_PHASE records, apply their section 10.1
exact rational rotations in ancestry order, from coarse to fine, using each
ancestor's generated channel mapping and orientation sign. Every interval
operation remains outward-rounded and every required root/sector predicate
must be CERTAIN. No phase halving, interpolation, fitted child plane, runtime
mapping search or independently selected transform is permitted.

Inherited `2A/2B/2C` knots are never recomputed: reuse the exact parent root,
including its interval and deterministic draw identity. Only new knots receive
the prediction above. Direct child evidence supplies the additional innovation:

\[
r_c=\tau(u_c^{pred},u_c^{obs}).
\]

Section 10.3 classifies that innovation without a pred/obs overlap requirement.
Normalized Flower phase is scale-invariant; a child stores only its new
residual, never a divided or duplicated parent coefficient.

Metric V ancestors MUST NOT enter this prediction. REV-C world geometry ends
at L2; V belongs exclusively to the fixed L3-L5 skin signal.

## 10.3 Analysis

U_pred and U_obs are NOT competing estimates to be intersected.
U_pred is the parent-generated prediction; U_obs is direct evidence.
Their metric separation is exactly the R2 innovation being represented.

First require both roots to be CERTAIN members of the same generated
child symbolic relation:

```text
same child J
same lineClass
same sector
same rootSign
compatible canonical orientation
```

Then compute

\[
r_c=\tau(U_{pred},U_{obs})
\]

without any pred/obs overlap requirement.

Classification:

```text
r_c == {0}
    exact parent prediction; write no R2_PHASE

r_c is CERTAIN and excludes zero,
and synthesis remains inside the generated child sector
    persist one R2_PHASE interval

r_c contains zero but is not exactly {0}
    AMBIGUOUS; the observation does not prove whether an innovation exists

tau denominator contains zero,
root interval touches/crosses a generated sector boundary without the
exact symbolic boundary equality proof of section 7.1,
or symbolic correspondence is not uniquely CERTAIN
    AMBIGUOUS / re-evaluate the generated alternatives

the observation is CERTAIN to belong to a different generated symbol
    this candidate is IMPOSSIBLE; evaluate that other generated symbol
```

No condition of the form

```text
"U_pred and U_obs are disjoint -> IMPOSSIBLE"
```

exists.

Disjoint U_pred/U_obs intervals inside the same CERTAIN generated relation
are a normal nonzero R2 residual.

Only the innovation is stored. The predicted child root is always regenerated from the current parent evaluator.

## 10.4 Synthesis

For a stored certain innovation interval `r_c`, the synthesized child root is:

\[
\boxed{U_c=\mathcal R_{r_c}(U_c^{pred})}.
\]

Inherited parent nodes (`2A,2B,2C`) are never modified by a child R2 record. Only newly introduced child nodes may carry R2_PHASE innovations. Parent-knot invariance is therefore structural, not a cancellation trick.

## 10.5 Orientation transport

For each of the 48 petal classes, codegen emits only:

```text
child R2 lineClass
sector
rootSign transport
orientation sign
incident child-petal list
```

Transport is a finite signed permutation into the canonical loop basis. No runtime matrix, Hessian or generic transform engine exists.

## 10.6 Exact child closure

A new child knot may be incident to more than one child petal. Every incident synthesis must produce the same symbolic key and compatible root interval.

For generated incidence set `I(k)` of child knot `k`:

\[
U_k=\bigcap_{p\in I(k)}U_{k,p}.
\]

```text
nonempty CERTAIN intersection
    closed child knot

empty intersection
    IMPOSSIBLE / conflicting observation

more than one sector/rootSign remains
    AMBIGUOUS
```

Thus R2 closure is equality of the actual generated child knot, not an independent four-cycle phase equation.

## 10.7 Coefficient count and promotion

At most the six active R2 child channels of one parent petal can receive scalar interval innovations. Only intervals excluding zero are persisted.

If an innovation would leave its generated child sector, it is not clamped and no child record is written. The same direct observation is re-evaluated against the first coarser Flower level whose exact loop/sector can contain it.

```text
coarser level gives one CERTAIN representation
    commit there

no level gives one CERTAIN representation
    UNRESOLVED
```

No fit, smoothing or invented split coefficient exists.

---

# 11. Exact R3 closure

Use the canonical tetrahedral axes:

\[
t_0=\frac{(1,1,1)}{\sqrt3},\quad
t_1=\frac{(1,-1,-1)}{\sqrt3},\quad
t_2=\frac{(-1,1,-1)}{\sqrt3},\quad
t_3=\frac{(-1,-1,1)}{\sqrt3}.
\]

R3 closes branch, chirality and junction geometry only on the L0–L2 carrier
hierarchy. It is never a skin-detail level and never makes L3/L4/L5 geometry.

R3 orientation MUST come from the actual generated corner incidence. It is not manually toggled by `+L`.

For each parity of the level-local integer cell coordinate

\[
p=(K_{Lx}\&1, K_{Ly}\&1, K_{Lz}\&1)
\]

codegen emits one of eight `TetraFrame[p]` rows containing:

```text
signed permutation of t0..t3
eta[4] loop-orientation signs
chiCell = determinant sign of that actual generated frame
```

If symbolic simplification proves a shorter XOR/parity formula, codegen may emit it, but the authority remains the determinant/incidence proof. There is no explicit `(-1)^(Kx+Ky+Kz+L)` rule in production semantics.

## 11.1 Definition of q_i

For each of the four R3 axes:

- `pred` is the root synthesized on that exact R3 loop by the current R1 + committed R2 evaluator;
- `obs` is the direct certain R3 SEAL root on the same loop;
- `eta_i` is the generated orientation sign from `TetraFrame`.

Then:

\[
\boxed{
q_i=\eta_i\,
\tau(u_i^{pred},u_i^{obs})
}
\]

with the same interval `τ` algebra as section 10.

This is the prediction-to-observation residual of section 9.2. Its inputs
MUST NOT be required to overlap. Only peer direct evidence and shared
incidences that claim the same physical quantity are intersected; a nonzero
R3 prediction residual is not an evidence conflict merely because pred and
obs are disjoint.

`q_i` is a dimensionless signed tangent-half-angle residual. Raw `ABC` triples from different loop bases are never added.

## 11.2 Tetra transform

\[
q_s=\frac14\sum_{i=0}^{3}q_i
\]

\[
q_v=\frac34\sum_{i=0}^{3}q_i t_i.
\]

Inverse:

\[
q_i=q_s+t_i\cdot q_v.
\]

Meaning:

```text
qs interval contains zero
    no common tetra phase bias proved

qv components contain zero
    no directional tetra opening proved

all closure intervals CERTAIN around zero
    R3 metric closure

one CERTAIN nonzero generated R3 residual
    child/junction detail candidate

interval ambiguity
    AMBIGUOUS -> refinement
```

R3 validates/extends R1+R2; it never predicts or replaces them.

## 11.3 Chirality

For the four algebraic root signs `s_i` in the generated tetra frame:

\[
\boxed{\chi_B=\chi_{Cell}\prod_{i=0}^{3}s_i}.
\]

`χCell` is the determinant sign from the generated frame table. A candidate junction/petal class is chirally valid only if its frozen generated chirality equals `χB`.

## 11.4 Junction decision

For every finite generated candidate junction class:

1. transport its four roots by the generated frame;
2. compute interval `q_i`;
3. compute `q_s,q_v`;
4. check generated chirality;
5. intersect with direct/dual veto constraints.

```text
0 valid classes
    contradiction or unresolved frontier

1 CERTAIN class
    R3 closure proven

>1 classes or any unresolved root interval
    AMBIGUOUS -> REFINE
```

R3 failure never removes R1.

---

# 12. Direct/dual symbol algebra

For one candidate \(\Sigma\), define:

```text
D(Σ) direct certain root/petal exists
T(Σ) its complete represented support is THROUGH_CERTAIN
A(Σ) generated incidence permits it
I(Σ) metric/root intervals are certain and nonempty
```

Same-observation direct endpoint has precedence: its endpoint support is excluded from the observation’s through projection before classification.

Outputs:

\[
CONFIRMED=D\land A\land I\land\neg T
\]

\[
VETO=T
\]

\[
HOLE\_CANDIDATE=
\neg D\land A\land\neg T\land BoundaryNeeded
\]

\[
REFINE=
AMBIGUOUS(I)\lor SymbolAmbiguous\lor DualIntervalWide
\]

\[
UNRESOLVED=
\neg CONFIRMED\land\neg VETO\land\neg UniqueCompletion.
\]

Dual never creates a direct Flower `A(Σ)` or measured root evidence. Within this direct-symbol algebra it eliminates candidates or narrows allowed sectors. Independently, the same excavation state generates DIRT support under section 13.4; that support is not a CONFIRMED or COMPLETED direct petal.

---

# 13. Unique hole completion

Completion operates inside exactly one 48-petal parent Flower at one level.

It never propagates from another completed petal in the same generation.

## 13.1 Boundary operator

Let:

```text
x = 48-bit mask of CONFIRMED direct petals
```

The generated signed incidence matrix:

\[
B\in\{-1,0,1\}^{72\times48}
\]

contains exactly three nonzero entries per petal.

Direct boundary:

\[
b=Bx.
\]

This is a fixed 72-edge add/sub operation, not a graph traversal.

## 13.2 Candidate predicate

An absent petal \(p\) is a valid completion candidate only if:

1. for each of its three edges:

\[
b_e=-B_{ep};
\]

2. adding it removes all three corresponding defects;
3. all three knot/root intervals already exist uniquely from incident confirmed
   petals, through generated incidence rather than sector-owner equality;
4. its generated petal/junction class, sector and rootSign are unique;
5. R1 is certain;
6. required R2/R3 closure for that particular flag is certain;
7. no portion of its metric interval is `THROUGH_CERTAIN`;
8. its generated anchor/tangent orientation determinant interval excludes zero.

Donors supply the actual root identities selected by their confirmed direct
geometry. Evidence for the same shared knot is intersected as before; the
donors' root-ownership interior constraints are not intersected. Completion
does not reselect a donor root by petal ownership, invent a root, or merge
different sector/rootSign identities.

Candidate bitmask:

\[
C_{hole}=
C_{edge}
\land C_{root}
\land C_{symbol}
\land C_{shell}
\land\neg C_{dualVeto}.
\]

## 13.3 Decision

\[
N_{valid}=\operatorname{popcount}(C_{hole}).
\]

```text
Nvalid = 0
    no completion; contradiction or unresolved frontier

Nvalid = 1
    emit one derived COMPLETED petal

Nvalid > 1
    UNRESOLVED + refinement request
```

A completed petal:

- is never written into M8;
- is never used to complete a second missing petal;
- disappears immediately on a THROUGH veto;
- becomes confirmed only through a direct endpoint.

A larger hole is completed only if it is one generated petal at a coarser level. Otherwise it remains unresolved.

No new knot position is extrapolated during completion.

---

## 13.4 Excavation DIRT support

DIRT is a derived supporting surface of the FULL matter model. It is not
the unique direct completion of sections 13.1–13.3 and MUST NOT be restricted
to that completion gate. A hole in direct geometry may therefore retain
excavation support even when its direct Flower evidence is unresolved.

The supporting geometry is exactly the exposed boundary of the represented
FREE volume against FULL, never an arbitrary patch fitted across the hole.

### Exact represented boundary

For an L0 integer lattice coordinate U, define the elementary half-open cell

```text
Cell(U) = a * (U + [0,1)^3),  a = 0.025 m.
```

This is a derived subdivision of existing M8 support, not a stored grid.
A kernel K represents support a*(K+[-1,1)^3); consequently Cell(U) belongs
exactly to the eight supports K=U+b, b in {0,1}^3.

```text
FreeCell(U) = OR of SEE_THROUGH(U+b), for all b in {0,1}^3

at least one covering support is certified THROUGH
    Cell(U) is FREE

all eight covering states resolve and none is THROUGH
    Cell(U) is FULL in the excavation matter model

otherwise
    AMBIGUOUS; request residency, do not invent a boundary
```

Thus the union of FreeCell cells equals the union of the represented
certified supports. There is no scalar field, surface fit, float weld,
persistent cell list, or new coordinate index.

For each of the six unit signed axis directions n, a support face exists iff

```text
FreeCell(U) = FREE
FreeCell(U+n) = FULL
```

The face is the exact common face of those cells, with matter on the FULL
side and presentation normal pointing toward the FREE cell. Its canonical
identity is the signed existing lattice address U plus n. Only the FREE-side
owner emits it. Generated face vertex order is fixed and camera-independent.
A fixed presentation diagonal may split a face into two raster triangles;
it neither defines nor changes the persistent excavation volume.

The readout suppresses DIRT wherever direct or uniquely COMPLETED Flower
geometry already covers the same represented surface footprint. Direct
coverage wins; DIRT must not become a duplicate foreground layer over a
valid direct surface. A missing RGB sample alone does not create a second
surface: the existing direct carrier keeps its canonical color fallback.

### Material and lifetime

DIRT uses an explicitly identified support material, not invented captured
RGB, ThreadAtlas detail or certified optical response. Its material color
does not assert that the unknown matter was observed to be soil.

DIRT is an actual depth/occlusion-bearing support surface of the model.
Its inferred origin remains explicit in live symbols and export metadata.
It cannot feed back as measured occupancy, closure evidence, or completion
input. Its existence changes only with direct coverage, excavation state or
required residency, never with head rotation or display LOD.

A later direct surface supersedes its support footprint. A later THROUGH
certificate removes it. A later inserted direct object may restore FULL
where earlier observations saw FREE. The two persistent views are committed
together; no independent persistent DIRT mesh or DIRT log exists.

---

# 14. Ghost and refinement semantics

## 14.1 Ghost

A direct symbol is a contradiction only when its entire interval support is inside certified THROUGH:

\[
G(\Sigma)=D(\Sigma)\land T(\Sigma).
\]

Partial overlap is `AMBIGUOUS`, not a ghost.

Response:

1. invalidate R3-dependent details;
2. invalidate R2-dependent details;
3. decrement existing R1 `OccupancyEvidence`;
4. clear R1 only when existing OFF hysteresis is crossed.

An unoccupied seed is cleared immediately.

## 14.2 Refinement

A region requests child work only for a discrete reason:

```text
ROOT_INTERVAL_AMBIGUOUS
DIRECT_DUAL_DISAGREEMENT
R2_COMPOSITION_UNRESOLVED
R3_JUNCTION_AMBIGUOUS
DIRECT_BOUNDARY_WITH_ONE_OR_MORE_COMPLETION_CANDIDATES
RGBV_NOVELTY
```

There is no scalar refinement score.

### 14.2.1 Observation-local refinement closure

Refinement is **evidence-bounded, not observation-count-bounded**. Geometry may
refine only through L2. Skin evidence is evaluated on the already existing
fixed L3/L4/L5 footprints of section 20 and may publish only scan-authored
signal splits.

One accepted immutable observation MUST evaluate every geometric L1/L2
candidate and every RGB/V split predicate that its own bounded evidence can
make `CERTAIN`. If all work fits the current GPU quantum, it is consumed
immediately. Otherwise the remaining finite workset stays attached to that
same immutable observation and drains in later quanta without a new frame or
camera motion.

```text
new viewpoint is required only for new information or AMBIGUOUS evidence,
never merely to schedule computation already supported by the frozen observation.
```

The drained result MUST be bit/interval-identical to eager evaluation of the
same complete workset. Scheduling order changes latency only. Fibonacci does
not order this work. `observationOrdinal` MUST NOT appear in geometry validity,
skin split validity, completion, metric state or appearance state.

### 14.2.2 Exact scan split predicates

For closed scalar intervals `a=[a-,a+]` and `b=[b-,b+]`, define certified
disjointness only as:

\[
Disjoint(a,b)=(a^+<b^-)\lor(b^+<a^-).
\]

For two linear RGB interval vectors, `RgbDistinct` is true iff at least one of
R, G or B is `Disjoint`. No distance, variance, confidence score or epsilon is
permitted.

Every footprint below is clipped to the current parent support as defined
in section 20.3. Let supportMask contain exactly its geometrically nonempty
child intersections, not merely children hit by valid measurement pixels.
An empty intersection needs no observation and cannot prove a distinction.
All nonempty intersections still require complete-footprint CERTAIN evidence.
The seven-value publication retains the ancestor RGB interval and zero V
innovation in empty slots; these are inherited signal, not measured samples,
and are excluded from the distinct-pair test. Empty support is UNIFORM and
requests no work. A missing or invalid observation on nonempty support is
AMBIGUOUS, never EMPTY.

For one unsplit RGB parent with seven generated skin-child footprints:

```text
all nonempty complete-footprint RGB measurements are CERTAIN
AND at least one nonempty child pair is RgbDistinct
    -> publish the parent RGB split and all seven child RGB intervals

any required child measurement is AMBIGUOUS
    -> do not publish; request genuinely new evidence

all nonempty children are CERTAIN but no nonempty pair is provably distinct
    -> retain the uniform parent RGB signal
```

V uses the identical finite decision over scalar metric-innovation intervals
relative to the already committed parent V signal:

```text
all nonempty complete-footprint V innovation intervals are CERTAIN
AND at least one nonempty child pair is Disjoint
    -> publish the parent V split and all seven additive child innovations

any required child interval is AMBIGUOUS
    -> do not publish; request genuinely new evidence

all nonempty children are CERTAIN but no nonempty pair is provably distinct
    -> retain the unsplit parent; no child V innovation is stored
```

A split bit and its seven values are one atomic publication. Descendant split
bits may be set only when every ancestor split for the same authority is set.
RGB and V decisions remain independent; their exact union is derived only for
resident draw data.

### 14.2.3 Finite workset

For each L2 carrier, codegen exposes the complete finite geometric candidates
through L2 and the fixed seven-child footprint masks for the three skin
substitution steps. Runtime removes parents with no discrete reason or no
possible interval distinction, tests every remaining finite candidate, commits
`CERTAIN` results, and stops locally at `AMBIGUOUS` evidence.

No blind geometric expansion, sparse child discovery, pointer tree or generic
score exists. The fixed skin topology already contains every logical child;
work is proportional only to actual evidence/novelty and ambiguity.

## 14.3 Three-shell protection

```text
R3 contradiction
    invalidate only R3-dependent descendants

R2 contradiction
    invalidate only R2 shape descendants

R1 contradiction
    candidate for canonical evidence removal
```

R2/R3 never own canonical occupancy.

---

# 15. Full-support SEE_THROUGH certificate

## 15.1 Frozen depth interval

Each valid depth pixel supplies:

\[
z\in[z^-,z^+].
\]

The interval encloses the captured digital depth and ordered reprojection
operations. Negative-volume queries additionally reserve one L0 lattice step
of clearance before the observed hit. This is a conservative realtime scan
policy, NOT a claimed maximum physical sensor error.

The frozen GPU vector is `(metric enclosure, reprojection enclosure,
visibility clearance, valid)`. The clearance is used only by the THROUGH
certificate, never by plane derivatives, R2 prediction, or skin V measurement.
The standard capture path uses `(0,0,a,1)`; FP32 enclosure is performed by
the evaluator, not omitted. Actual SDK per-eye intrinsics and poses remain
mandatory. Invalid/missing pixels never become free volume.

## 15.2 Certificate hierarchy

For each eye, build a fixed min/validity pyramid.

One texel stores:

```text
MinLowerDepth
AllValid
```

Reduction:

\[
MinLower_{L+1}
=
\min_{4\ children}MinLower_L
\]

\[
AllValid_{L+1}
=
\bigwedge_{4\ children}AllValid_L.
\]

This is not dilation or morphology. It is a conservative range certificate.

For a 512² source, the complete stereo hierarchy is approximately 5.33 MiB at 8 bytes/texel.

## 15.3 Convex support query

For one represented support cube:

1. project its eight uncertainty-expanded corners;
2. compute the conservative integer screen AABB;
3. reject the eye as `AMBIGUOUS` if any required portion leaves the calibrated image;
4. query the min/validity pyramid over a **superset** of that AABB, never over sparse point samples.

### 15.3.1 One-read common-prefix fast path

Let `[xmin,xmax]` and `[ymin,ymax]` be inclusive pixel bounds. Find the smallest aligned dyadic square whose binary x/y prefixes contain both interval endpoints. This is the quadtree node at the first differing bit of either axis.

That one node is a conservative envelope of the complete projected support footprint.

If its `AllValid` bit is true and

\[
z^{+}_{support,max}<MinLowerDepth_{envelope},
\]

then the complete support is `THROUGH_CERTAIN` for that eye.

Because the queried region is a superset, unrelated foreground can only make the test fail; it cannot create a false THROUGH.

### 15.3.2 Exact refinement of a failed envelope

If the common-prefix envelope is too conservative, descend only into children intersecting the AABB. A child wholly contained by the AABB contributes its stored `(MinLowerDepth,AllValid)` directly. A partial child descends until a wholly covered node or source pixel is reached.

The final cover is a disjoint set of dyadic nodes whose union contains the complete AABB. THROUGH requires:

```text
AllValid for every cover node
AND
z_support,max+ < minimum(MinLowerDepth of every cover node)
```

The semantic CPU oracle has no query budget. The GPU fast path uses a fixed 64-node local stack; exhaustion returns `AMBIGUOUS`, never THROUGH. This affects completeness only, never safety.

Stereo result:

```text
either eye proves complete-support THROUGH
    THROUGH_CERTAIN

strict endpoint interval intersects support
    NOT_THROUGH

otherwise
    AMBIGUOUS
```

No morphology, dilation or six-point approximation is used.

## 15.4 Hierarchical dual update

Traverse only Block nodes intersecting the frozen observation frustum and scan range.

At each node:

```text
whole node THROUGH_CERTAIN
    write ALL_THROUGH; stop descending

NOT_THROUGH
    keep/restore FULL for endpoint support

AMBIGUOUS and node above leaf
    descend fixed children

AMBIGUOUS leaf
    no destructive write
```

This traversal updates the sparse volume; it is not surface reconstruction.

---

# 16. First-hit admission and retirement

## 16.1 Owner set

Per axis use half-open support:

\[
[C_K-a,C_K+a).
\]

Every endpoint belongs to exactly two owners per axis and therefore exactly eight overlap owners.

Ordering:

1. squared distance to center;
2. lexicographic signed coordinate.

No normal-step owner is used.

An occupied overlap owner whose fixed sector/rootSign class is incompatible is skipped, not overwritten.

## 16.2 Seed

First strict endpoint without local closure:

```text
stores plane and color
sets PlaneValid
sets R1_SEED
keeps Occupied clear
caps evidence below OccupiedOnThreshold
does not draw
```

## 16.3 Same-observation promotion

A seed becomes stable immediately when the current attempt contains two non-collinear compatible R1 relations whose generated sectors share at least one common flag petal.

This is a frozen R1 incidence bitmask test.

No R2/R3 proof is required.

## 16.4 Cross-observation promotion

If no same-attempt closure exists, a later immutable observation promotes the seed only when:

- it resolves to the same fixed relation, sector and rootSign;
- its plane/root intervals are compatible;
- it is a different observation generation.

The existing M8 evidence is then allowed to cross `OccupiedOnThreshold`.

No new timer or confidence field exists.

## 16.5 Retirement

```text
THROUGH_CERTAIN over complete seed support
    delete seed immediately

incompatible later direct endpoint
    replace dormant seed deterministically

neither
    retain dormant seed; do not draw
```

A dormant seed cannot create children, completion or ThreadAtlas records.

---

# 17. Deterministic attempt reduction

Let maximum frozen observation size be `512²` and each measurement have at most eight arithmetic overlap owners.

Maximum transient records:

\[
8\cdot512^2=2,097,152.
\]

## 17.1 Record layout

```c
struct ObservationRecord
{
    uint TileAndKernel;
    uint SourcePixel;
    uint SymbolTag;
    uint PrecisionKey;
}
```

Maximum storage remains 32 MiB and reuses the capacity class of the old `SurfaceCandidates` buffer.

`PrecisionKey` is **not** a branch identity and is never allowed to resolve incompatible geometry. It is used only to choose the deterministic representative central sample after compatible interval evidence has been intersected.

## 17.2 Count / reserve / emit

`CountObservationBins`:

- one lane per valid joint measurement;
- derive the eight overlap owners arithmetically;
- resolve only their logical root tiles;
- append missing allocation requests;
- atomically count one record in each resolved physical tile;
- stamp the tile with the attempt generation.

Per-HOT-tile transient metadata:

```text
AttemptStamp   128 KiB
RecordCount    128 KiB
RecordOffset   128 KiB
EmitCursor     128 KiB
```

A bounded prefix scan over the 32768 physical slots reserves only stamped tile spans.

`EmitObservationBins` recomputes the same owners and writes records into those spans. Write order is irrelevant.

## 17.3 Exactly one commit workgroup per touched tile

`FlowerCommit` dispatches exactly one workgroup per touched tile.

Groupshared baseline:

```text
512 representative PrecisionKeys   2048 B
SURFACE bits                          64 B
FREE bits                             64 B
THROUGH bits                          64 B
13 relation masks                    832 B
halo refs                            108 B
interval / conflict scratch
```

For each owner/symbol bucket the workgroup performs:

1. generated sector/root classification;
2. interval intersection over mutually compatible records;
3. mark `UNRESOLVED` immediately if incompatible symbolic classes compete;
4. only after a nonempty compatible intersection exists, choose the central source sample with minimum `PrecisionKey` for the persisted quantized plane/color representative.

`PrecisionKey` ordering is:

```text
interval class
normalized interval width
source pixel
```

and is used only inside an already compatible symbolic bucket. It may never select between different sectors/rootSigns/petal classes.

Thus the **interval intersection is the geometry evidence**; the key chooses only a deterministic stored representative.

## 17.4 Halo correctness

Each HOT tile stores 27 packed halo refs:

```text
15-bit physical slot
15-bit slot generation
2-bit residency state
```

Every access validates slot generation. Eviction cannot create an ABA neighbour alias.

## 17.5 Removed cost

This deletes:

```text
4 × SurfaceWinnerRanks = 64 MiB
SurfaceQueue            = 8 MiB
world-wide per-kernel winner clearing
```

Transient observation records remain bounded at 32 MiB and disappear after `FinalizeObservation`.

---

# 18. Semantic observation pipeline

The semantic pipeline is exactly:

```text
StereoFlowerRefine

BuildDepthCertificate

CountObservationBins
ResolveMissingSpatialNodes
ReserveObservationBins
EmitObservationBins

FlowerCommit
    direct endpoint
    through update
    R1
    conditional R2
    conditional R3
    dual veto/completion/refinement
    mark changed excavation boundary / direct-coverage pages dirty for DIRT
    generate complete finite L1/L2 geometry workset
    evaluate exact L2-skin RGB/V split predicates
    commit current-quantum FlowerDetail/Thread work

DrainObservationRefinement
    the finite tile-local consequences reachable inside THIS transaction
    root -> L1 -> L2 -> skin are dependency barriers of one command graph
    never a continuation of this snapshot into a later frame

FinalizeObservation
    publish the canonical generation
    mark the changed readout pages dirty
    RELEASE THE SNAPSHOT
```

`DrainObservationRefinement` is a semantic obligation, not a mandatory extra micro-dispatch. Production MAY fuse it into `FlowerCommit` or process the fixed L2-skin stages in one workgroup/dispatch. The implementation MUST prefer fused local work over a dispatch zoo. Its order is a scheduler implementation detail and MUST NOT use Fibonacci. Several entry points MAY remain where an Adreno shader-size limit requires them; none of them may mean "continue this snapshot next frame".

One snapshot is one bounded synchronous scan transaction. It is never retained to exhaust derived refinement work. The snapshot processes all directly addressed resident evidence and all finite tile-local consequences reachable in that transaction, commits them to canonical world state, marks derived readout pages dirty, and retires. COLD dependencies are requested and skipped; AMBIGUOUS evidence is skipped; neither retains the snapshot. Further geometry, excavation and skin refinement is driven by later observations against the persistent world. No per-observation cursor, pending tile, refinement quantum or continuation workset survives `FinalizeObservation`.

The persistent M8/dual/FlowerDetail/ThreadAtlas world is the refinement memory. A camera observation is evidence, not a work queue.

The same rule binds the excavation view: what a snapshot certified THROUGH it stores, and what it did not certify simply stays FULL. FULL is re-examined by a NEW observation with a new camera, never by re-running the same frozen certificate.

Sparse allocation barriers do not contain geometry decisions.

Priority on the existing native queue:

```text
FINE/ERASE
normal observation
dirty Flower page compaction
warm residency
```

CPU SSD completions and log accounting continue independently of native GPU job lifetime.

---

# 19. StereoFlowerRefine

Preserve:

```text
Depth-L
Depth-R
PCA-L
PCA-R
five bounded depth hypotheses
joint immutable measurement
chromatic consistency
```

Remove the square eight-sample tangent census.

For each hypothesis plane:

1. restrict the hypothesis to its six R1 loops;
2. calculate certain root intervals;
3. project those exact root positions into both eyes;
4. compare only roots belonging to the same generated sectors;
5. require two independent non-collinear R1 relations;
6. invoke R3 tetra roots only when multiple hypotheses remain;
7. invoke R2 only when the R1 roots show a representable bend interval.

Thus:

\[
sensor\ support=world\ Flower\ support.
\]

A hypothesis with invalid projection or insufficient valid root support
remains unresolved. The measured Depth-L position is the metric prior, tested
against Depth-R and both RGB images at generated R1 roots. Five distinct
hypotheses span at most ±a/2 along its viewing ray. Both depth normals must be
compatible (absolute aligned dot >= 0.3); the opposite-plane residual must lie
within a/2. The accepted endpoint normal uses both measured depth normals.

RGB comparison uses the existing per-eye camera projection and bilinear
sampler at generated root positions. Chromaticity L1 difference <= 0.35 is
the realtime consistency policy inherited from simplescan, not a topology
score or an interval-equality proof. Quantized black carries no chromatic
direction. Crossing a bilinear texel boundary is not a missing observation.

A uniquely supported correction may refine the metric prior. If correction
hypotheses remain ambiguous but the original measured depth has valid
two-depth/two-RGB R1 support, retain that measured depth unchanged. Ambiguity
in refinement MUST NOT erase this basic R1 evidence. Without that support,
do not fabricate an endpoint. No mono fallback or lower-confidence bypass.

R2/R3 ambiguity remains local to refinement and cannot independently veto
basic supported R1 existence. Flower root/sector/incidence interval predicates
and the L0–L2 / L3–L5 geometry/skin split are unchanged.

No tangent PCA or arbitrary square stencil is introduced.

---

# 20. L0–L2 geometry and fixed 399-position Flower skin thread

## 20.1 Hard level split

```text
L0 25.00000 mm   geometric Sphere–Flower carrier
L1 12.50000 mm   geometric Sphere–Flower refinement
L2  6.25000 mm   terminal geometric/raster carrier
L3  3.12500 mm   planar skin signal address
L4  1.56250 mm   planar skin signal address
L5  0.78125 mm   planar skin signal address
```

The level names state canonical scale, not six geometric LODs:

\[
L0\rightarrow L1\rightarrow L2=\text{geometry refinement}
\]

\[
L2\triangleright(L3\rightarrow L4\rightarrow L5)
=\text{fixed planar Flower skin signal}.
\]

L2 is the final world-space raster geometry. L3/L4/L5 never move a carrier
vertex, create a world knot, change silhouette or write depth. They address RGB
and metric microrelief over the local plane of the evaluated L2 carrier.
Fine skin state cannot exist without a valid parent R1/L2 FlowerAddress.

## 20.2 Fixed Flower-7 hierarchy

The canonical surface slice of one Flower has exactly seven sites:

```text
H                    hub in the surface slice
R0,R1,R2,R3,R4,R5    ordered tangential/ring sites
```

The axial `+N` and inward `-N` 3D lobes participate in geometric closure,
metric interpretation and optical response only. They MUST NOT appear in a
skin child address. `H` is not the inward axial lobe.

Every valid L2 carrier always owns the identical complete logical hierarchy:

```text
L3: 7
L4: 7^2 = 49
L5: 7^3 = 343
total L3–L5 thread positions: 7 + 49 + 343 = 399
```

The canonical radix-7 identities are:

\[
I_3(c_3)=c_3,
\]

\[
I_4(c_3,c_4)=7+7c_3+c_4,
\]

\[
I_5(c_3,c_4,c_5)=56+49c_3+7c_4+c_5,
\]

where every `c` is in `0..6`. These 399 identities exist immediately when L2
exists. Refinement changes only the signal subdivision and interval values;
it never creates a node or changes topology.

Prohibited:

```text
validDepth / validDepthCode / deepestValidFlower / drawDepth
7-, 56- or 399-node topology variants
whole-level promotion
childExists bits or per-child allocation
sparse child lists, pointers, hashes or compaction maps
presentation selection among L3/L4/L5
```

## 20.3 Exact three-step barycentric address

Each L2 skin Flower consists of the six canonical wedges:

\[
W_i=(H,R_i,R_{(i+1)\bmod6}),\quad i=0\ldots5.
\]

Codegen owns only their canonical UV convention, vertex order, orientation and
finite transition tables. The actual world frame:

\[
(O,T_1,T_2,N)
\]

MUST be supplied at runtime by the canonical evaluator of the particular
scanned L2 carrier. A generated/static world frame is forbidden.

The L2 vertex shader emits the three evaluated world positions and canonical
wedge coordinates `(1,0,0)`, `(0,1,0)`, `(0,0,1)`. Raster interpolation gives
`bc=(lambda0,lambda1,lambda2)`. Each skin descent uses the complete stable
ordering of the three components, not `argmax` alone. Ties are resolved by
increasing generated canonical skin-site identity; no epsilon is used.

For ordered components `x0 >= x1 >= x2`, the exact child barycentrics are:

\[
bc'=(x_0-x_1,\;2(x_1-x_2),\;3x_2).
\]

They are nonnegative and sum to one. A generated `6 parent wedges × 6 stable
orders = 36` transition table returns:

```text
child site c in 0..6
child wedge in 0..5
orientation/permutation
next stitch state
```

The fragment applies that same ordered-chamber transform exactly three times,
unconditionally, producing the terminal locality `(c3,c4,c5)`. At every
recursive level, codegen MUST prove both:

1. the 36 half-open chambers partition the six parent wedges exactly; and
2. the union of all chambers mapped to each child `c` equals the exact
   canonical child footprint clipped to the current parent support.

All seven children always exist logically. Their represented support is
`F_c = F_canonical_child_c intersect F_parent_support`; clipping never removes
an identity or creates a `childExists` state. A logical descendant outside
the current L2/parent support has no raster preimage and need not be reached
by that carrier. The 36 rows partition the existing parent domain; they do
not materialize 42 full child wedges outside it. Recursion transports a point
in the current support through one chamber and repeats inside that support.
The full 7/49/343 identity space and the immutable 399-position thread remain
unchanged, independently of which addresses have a preimage in this carrier.

A merely convenient triangular fractal partition that does not reproduce the
generated Flower-7 footprint is forbidden.

## 20.4 Immutable recursively contiguous embroidery

The logical positions are physically ordered by one global generated thread
template `Gamma_L2`. It is reused by every L2 carrier; only its signal data
differs. Every subtree is one contiguous interval:

```text
one L4 subtree = one L4 anchor + seven L5 sites = 8 positions
one L3 subtree = one L3 anchor + seven L4 subtrees = 57 positions
seven L3 subtrees = 399 positions
```

Let `r3(c3)`, `r4(state3,c4)` and `r5(state4,c5)` be generated local stitch
ranks. Exact thread coordinates are:

\[
T_3=57r_3(c_3),
\]

\[
T_4=57r_3(c_3)+1+8r_4(state_3,c_4),
\]

\[
T_5=57r_3(c_3)+1+8r_4(state_3,c_4)+1+r_5(state_4,c_5).
\]

They cover `0..398` bijectively. Codegen emits at minimum
`CanonicalL5ToThread[343]`, `CanonicalToThread[399]`, their inverse/debug
mapping, and canonical-parent-to-thread-parent mappings.

The canonical Flower-7 adjacency graph is fixed:

```text
H--Ri for i=0..5
Ri--R(i+1 mod 6) for i=0..5
```

For each finite `(entry port, exit port, orientation)` stitch state, codegen
derives two mirror-equivalent valid templates `A` and `B`. In the canonical
positive state `(entry=R0, exit=R5)`, template A is the adjacency path
`R0,H,R1,R2,R3,R4,R5`; B is `reverse(mirror(A)) =
R0,R1,R2,R3,R4,H,R5`. Rotations/reflections transport these templates to every
other state and generated transition rows transport child entry, exit and
orientation. Codegen rejects any row that is not a seven-site bijection, uses
a non-adjacent transition, or changes its declared ports/orientation.

## 20.5 Fibonacci has one compile-time role

Define the exact Fibonacci word:

```text
W0 = "0"
W1 = "01"
Wn = Wn-1 || Wn-2
```

`FibBit(n)` chooses the smallest `k` with `|Wk| > n`, then repeatedly descends
to `Wk-1` or `Wk-2` using integer compare/subtract until reaching W0/W1.

Fibonacci chooses only A/B during code generation. The index is the actual
preorder thread ordinal of the parent whose children are about to be expanded:

\[
FibBit(T_{parent}).
\]

The root L2 seed uses `FibBit(0)` to choose `r3`. Once `r3` is known, each L3
parent has `Tparent=T3`; once `r4` is known, each L4 parent has `Tparent=T4`.
Thus generation is finite and non-circular. Using canonical tree ordinals such
as `1+c3` or `8+7c3+c4` is forbidden.

There is no runtime Fibonacci evaluation, route origin or Fibonacci state.
Fibonacci MUST NOT affect scan scheduling, observation draining, refinement,
split decisions, color, V, confidence, read depth, camera LOD or raster
traversal.

## 20.6 Fixed topology, scan-authored signal

For each of RGB and V independently, one signal region has only:

```text
UNIFORM    the complete descendant footprint inherits this region's signal
SPLIT      all seven skin children have explicit interval values
```

The complete subdivision topology state is exactly 57 bits per authority:

```text
1 bit    L2 -> L3
7 bits   L3 -> L4
49 bits  L4 -> L5
```

The seven L3-parent bits and 49 L4-parent bits are stored in **thread-parent
order**, never canonical order. Define:

\[
j_3=r_3(c_3),
\]

\[
j_4=7r_3(c_3)+r_4(state_3,c_4).
\]

`SplitL3Thread` bit `j3` and `SplitL4Thread` bit `j4` therefore follow the same
physical ordering as `Gamma_L2`. Canonical identity remains radix-7; generated
maps convert canonical identities to thread parent positions.

Only scan evidence satisfying section 14.2.2 may set a split. Readout, camera
distance, raster footprint and Fibonacci never set or clear one. A split and
its seven child intervals publish atomically. Parent closure is mandatory:

```text
SplitL2 = 0        -> SplitL3Thread = 0 and SplitL4Thread = 0
SplitL3Thread[j]=0 -> the seven corresponding L4-parent bits are zero
```

## 20.7 Compact storage in thread order

The 399 positions are a logical address space, not mandatory dense storage.
Every split adds exactly one seven-value group. Let:

```text
rank7(mask,j)   = popcount(mask & ((1<<j)-1))
rank49(mask,j)  = popcount of the at-most-two words strictly before bit j
```

For a root split, its seven L3 values are the first group and:

\[
index_3=Base_3+r_3(c_3).
\]

For a split L3 parent:

\[
group_4=rank_7(SplitL3Thread,j_3),
\]

\[
index_4=Base_4+7group_4+r_4(state_3,c_4).
\]

For a split L4 parent:

\[
group_5=rank_{49}(SplitL4Thread,j_4),
\]

\[
index_5=Base_5+7group_5+r_5(state_4,c_5).
\]

where `Base4=Base3+7` and
`Base5=Base4+7*popcount(SplitL3Thread)`. Rank uses at most two integer words,
two masks and two `countbits`; there is no loop, search, hash or pointer chase.

Including the canonical L2 root value, physical signal value count is exactly:

\[
1+7SplitL2+7popcount(SplitL3Thread)+7popcount(SplitL4Thread).
\]

The fully split signal has 400 stored values including its distinct L2 root;
the immutable L3–L5 thread itself remains exactly 399 positions. A uniform L2
stores only its root signal and maps all 399 logical positions to it.

## 20.8 Deterministic readout and RGB/V union

The fragment always computes `(c3,c4,c5)` by three descents. It then follows
the scan-authored masks from the root and resolves the nearest explicit value.
For RGB, the deepest explicit actual color replaces its ancestor. For V, the
explicit values at each split level are additive innovations as section 22
defines.

Persistent masks remain separate:

```text
RGB splits -> ThreadAtlas
V splits   -> FlowerDetail
```

Dirty-page compaction derives, bit for bit:

\[
SplitDraw=SplitRGB\lor SplitV.
\]

For every resulting draw region it inherits the nearest RGB ancestor and zero
for absent V innovations, then packs one aligned hot RGB/V/optical sample.
This merge is derived readout data and never changes either persistent
authority.

Antialiasing may filter the fully evaluated signal over a pixel footprint. It
MUST NOT mutate subdivision, choose a stored level, suppress an explicit split
or create a mip/LOD authority.

## 20.9 Invalidation

```text
parent R1/L2 structural anchor changes or explicit ERASE
    increment FlowerOwnerEpoch; every geometric and skin record becomes stale

compatible parent metric refinement inside the same sector/rootSign/frame
    retain the epoch and re-evaluate descendants against the refined L2 frame

R2 invalidated
    ignore only R2-dependent L1/L2 geometry and subordinate skin state

R3 invalidated
    ignore only junction/chirality-dependent state; never delete independent R1
```

Cleanup is deferred log compaction, never an immediate subtree traversal.

---

# 21. Fixed-thread captured RGB signal

RGB is an actual captured linear-radiance interval on the fixed L2 skin
thread. It is not a multilevel residual series. The L2 root color comes from
the canonical M8 owner. Every explicit child group contains seven actual child
colors. At a terminal locality, readout returns the deepest explicit color on
the scan-authored path:

\[
C(p)=C_{nearest\ explicit\ RGB\ ancestor}(p).
\]

No L3+L4+L5 color accumulation, interpolation across unrelated regions, RGB
inpainting or invented texture is permitted. Half-open chamber ownership makes
boundary evaluation deterministic. Shared carrier identities and generated
wedge orientation make duplicate evaluations identical.

RGB cannot modify V or any geometry/split predicate. The render value of each
stored color interval is its componentwise deterministic interval midpoint.

For a COMPLETED petal:

```text
unique existing ThreadAddress continuation
    use that exact scan-authored signal

otherwise
    use canonical owner M8 PackedColor
    create no child RGB group
```

---

# 22. Additive nested metric V on the L2 plane

## 22.1 Authority and basis

V is metric microrelief over the frozen L2 carrier, not another raster
geometry level and not a replacement color channel. For canonical child
barycentrics `lambda=(lambda0,lambda1,lambda2)`, use the fixed bubble:

\[
\psi(\lambda)=729\lambda_0^2\lambda_1^2\lambda_2^2.
\]

It satisfies:

\[
\psi(1/3,1/3,1/3)=1,
\]

and both its value and first derivative are exactly zero on every child
boundary. A V group stores interval amplitudes in signed Q5.26 metres.

## 22.2 Nested innovation

For the three ordered chamber descents, let `psi3`, `psi4`, `psi5` be the
bubble evaluated in the corresponding local child coordinates. The physical
microrelief is the additive innovation:

\[
\boxed{V(u,v)=A_3\psi_3(u,v)+A_4\psi_4(u,v)+A_5\psi_5(u,v)}.
\]

`A3` exists only when the V root is split; `A4` only when its L3 parent is
split; `A5` only when its L4 parent is split. An absent term is exactly zero.
A child term augments and never replaces its parent term. Therefore adding or
removing child detail preserves the parent V and its first derivative on the
child boundary. At most three bubbles are evaluated per fragment.

## 22.3 World evaluation and actual micro-normal

The canonical L2 evaluator, not codegen, supplies an orthonormal runtime frame
`(O,T1,T2,N)` for the particular carrier. With its planar coordinates `(u,v)`:

\[
P_{L2}(u,v)=O+uT_1+vT_2,
\]

\[
P_\mu(u,v)=P_{L2}(u,v)+V(u,v)N.
\]

This micro-surface is evaluated for material response; raster position,
silhouette and depth remain the L2 carrier. Exact derivatives are obtained by
the generated affine chamber transforms and analytic derivatives of `psi`:

\[
P_u=T_1+V_uN,\qquad P_v=T_2+V_vN,
\]

\[
N_\mu=
\frac{N-V_uT_1-V_vT_2}
{|N-V_uT_1-V_vT_2|}.
\]

Because the frame is orthonormal, the squared denominator is
`1+Vu^2+Vv^2` and is strictly positive without an angular epsilon. Finite
differences, neighbor fetches and fitted normal maps are forbidden.

## 22.4 Admission and representability

V is admitted only from a metric stereo/multiview innovation interval made
`CERTAIN` over the complete child footprint by section 14.2.2. Stored bounds
must fit signed Q5.26 and all outward-rounded analytic evaluation bounds must
remain finite. No amplitude is clamped.

If evidence cannot certify a single-valued metric graph over the L2 frame, it
is not skin V: the L0–L2 geometric carrier must be re-evaluated if its finite
Sphere–Flower candidates permit that evidence; otherwise the result is
`UNRESOLVED`. V never changes L2 occupancy or creates geometry by itself.

---

# 23. Micro-normal and optical synthesis

## 23.1 Exact signal evaluation and filtering

The fragment evaluates the exact terminal locality, deepest explicit RGB and
all present additive V innovations before shading. Distance and pixel footprint
never choose L3/L4/L5. Presentation antialiasing may integrate this final
piecewise signal and its analytic micro-normal over a pixel footprint, but may
not change a split bit or substitute a coarser stored signal.

For exact covered subregions with analytic micro-normals `Ni` and exact
projected-area weights `wi`, optional derived prefilter moments are:

\[
\bar N=\frac{\sum_iw_iN_i}{\sum_iw_i},\qquad
M_N=\frac{\sum_iw_iN_iN_i^T}{\sum_iw_i},
\]

\[
C_N=M_N-\bar N\bar N^T.
\]

The tangent-plane eigenvalues of `C_N` are deterministic directional
spread/anisotropy descriptors. They are derived presentation data, never a
stored normal map, fitted Gaussian geometry or alternate surface authority.

## 23.2 Captured-radiance invariant

The scanned RGB channel is treated as captured linear radiance, **not as known diffuse albedo**. Therefore the production renderer MUST NOT invent an ambient irradiance value or fully relight the captured diffuse component.

Base output is exactly:

\[
\boxed{C_{base}=C_{capture}}.
\]

V changes the analytic micro-normal and certified optical response inside the
L2 footprint. It does not move raster geometry, silhouette or depth.

For unresolved microgeometry, a relative diffuse micro-correction is allowed only when the parent directional response is interval-proven positive. With presentation light direction `L`:

\[
E_0=\max(\bar N\cdot L,0)
\]

\[
E_F=\frac{\sum_iw_i\max(N_i\cdot L,0)}{\sum_iw_i}.
\]

```text
lower interval bound of E0 > 0
    Cdiff = Ccapture * (EF / E0)

otherwise
    Cdiff = Ccapture
```

No `E_a`, no epsilon and no inferred albedo exist. This correction is presentation-only and cannot feed back into scan geometry or V.

## 23.3 Specular / view-dependent appearance

A ThreadProgram may contain a view-dependent optical interval only when repeated multiview RGB observations make that interval `CERTAIN`. If not certified:

```text
OPTICAL_VALID = 0
specular correction = 0
```

When certified, the renderer evaluates the optical response over the actual
analytic V micro-normal field, area-weighted by `w_i`. Derived normal moments
may choose the directional width of the presentation lobe, but never replace
the analytic signal or become geometry authority.

Required ThreadProgram optical fields when valid:

```text
F0 interval
roughness-floor interval
capture/view residual interval
OPTICAL_VALID
```

The presentation BRDF is isolated from reconstruction: changing its closed shader formula cannot move a knot, create a petal, alter V, change occupancy, or affect dual/hole logic.

---

# 24. Procedural L2 readout ABI

Readout pages contain compact active L2 carrier symbols, derived DIRT support
symbols and compact skin samples, never persistent vertices, indices or
L3/L4/L5 geometry. DIRT is derived from the same persistent excavation state;
it is not a separate stored mesh, world, or fallback scanner.

## 24.1 Symbol record

One active L2 carrier, with up to six active wedges, remains 16 bytes.
A DIRT record still represents one exact presentation half-face.

```c
struct FlowerSymbolRecord
{
    uint OwnerAndCarrier;
    uint RootsAndWedges;
    uint DetailRef;          // resident owner reference; 0 means no fine state
    uint ThreadRef;          // one derived carrier skin header; invalid when absent
}
```

`OwnerAndCarrier` packs:

```text
bits  0..8   kernelLocal          9
bits  9..15  generated L2 carrier 7   // 0..127
bits 16..18  validShellMask       3
bit      19  directFreeSide       1
bit      20  HINGE                1
bit      21  DIRT                 1
bits 22..27  activeWedgeMask      6
bits 28..31  reserved             4   // MUST be zero
```

`RootsAndWedges` packs:

```text
bits  0..6   rootSigns            7   // H,R0,R1,R2,R3,R4,R5
bits  7..11  hubSector            5
bits 12..17  completedWedgeMask   6   // subset of activeWedgeMask
bits 18..23  reverseWedgeMask     6   // subset of activeWedgeMask
bits 24..31  reserved             8   // MUST be zero
```

The carrier ID addresses the existing generated `L2Wedges` incidence, not a
new spatial index. The local position numbering is exactly `H=0, Ri=1+i`.
Each site retains its original `(level,J,lineClass,sector,rootSign)` identity.
Its normal, appearance and requesting wedge MUST NOT split that position
identity. Sector and phase are regenerated by the same certain knot evaluator;
the record cannot choose an unresolved branch.

There is no geometry-depth, valid-depth or dynamic branch-rank field.
Only admissible active wedges and uniquely COMPLETED wedges set mask bits.
A carrier with no active wedges receives no record.

`ThreadRef`, when present, addresses one derived 16-byte skin header in a
64-byte aligned header slot. All six wedges use that same carrier thread;
the wedge affects locality evaluation only. Compact RGB/A3/A4/A5 samples
follow the header slot and are compiled from the same persistent FlowerDetail
and ThreadAtlas carrier key and owner epoch. Generated source-petal/child-path
aliases of the same L2 carrier MUST resolve to one canonical skin key, never
six independent wedge tapes. This creates no additional persistent authority.

For DIRT, the same 16 bytes have this explicit interpretation:

```text
OwnerAndCarrier.kernelLocal
    FREE-side elementary cell U within the existing LogicalTile address

OwnerAndCarrier.carrier
    generated face class: +X, -X, +Y, -Y, +Z, -Z (0..5)

OwnerAndCarrier.DIRT = 1
OwnerAndCarrier.activeWedgeMask = 1

OwnerAndCarrier other semantic bits
    0; no measured shell, completion, hinge, or direct free-side claim

RootsAndWedges bit 0
    presentation half-face 0/1

RootsAndWedges bits 1..31
    0; no invented Flower sector or branch

DetailRef / ThreadRef
    invalid; no fabricated metric or captured appearance
```

The DIRT half-face uses sites 0,1,2; the remaining sites evaluate to site 0,
so the other five static fan triangles are degenerate. Its positions are the
existing generated integer Cell(U)/Cell(U+n) interface of section 13.4,
not direct Flower knots or a persistent mesh.

## 24.2 Page header

```c
struct FlowerPageHeader
{
    int3 LogicalTile;
    uint Generation;
    uint FirstSymbol;
    uint SymbolCount;
    uint FirstDrawSample;
    uint DrawSampleCount;
}
```

Every non-DIRT symbol evaluates to L2 raster geometry. Missing direct L1/L2
innovation means the exact certain parent evaluator is repeatedly restricted
to L2; it does not authorize invented child geometry. DIRT symbols instead
evaluate the exact exposed excavation faces of section 13.4.

## 24.3 Deterministic page compaction

For every dirty L2 carrier, compaction reads the independent RGB and V split
masks, validates their common `ParentEpoch`, takes their exact hierarchical
union, and writes compact groups in the thread-parent order of section 20.7.
Each resulting hot sample contains in one aligned record:

```text
deepest explicit captured RGB
A3 additive V innovation (or exact zero)
A4 additive V innovation (or exact zero)
A5 additive V innovation (or exact zero)
certified optical/material fields
```

The derived masks and group offsets allow one final structured sample load per
fragment. Failed validation or allocation leaves the old FRONT page untouched;
it never publishes a partial union.

The same dirty-page publication derives DIRT support from resolved excavation
state and direct coverage. It emits only exposed FREE/FULL interfaces not
already covered by higher-priority direct/COMPLETED geometry. DIRT consumes
the same arena and page directory; it allocates no 399-sample skin, no Thread
run, no fine metric run, and no second queue.

## 24.4 Directory publication — one native queue

```c
struct FlowerPageDirectory
{
    uint FrontHeader;
    uint FrontGeneration;
    uint PendingHeader;
    uint PendingGeneration;
}
```

Dirty-page procedure:

1. schedule compaction as a bounded low-priority job on the **existing single serialized native Vulkan queue**;
2. allocate new immutable arena blocks;
3. compact new symbol records;
4. write the pending header;
5. signal the queue's `FlowerPageReady` timeline/fence value;
6. before first graphics use, wait on that published value only if platform queue ownership requires it;
7. atomically replace FRONT pointer/generation;
8. retire old arena blocks after the last graphics fence that referenced them.

No second compute/background queue is created. Scanner/FINE/ERASE jobs remain higher priority on the same native queue.

Failed compaction leaves FRONT untouched. There is no whole-world FRONT/BACK.

## 24.5 Generational symbol arena

Use one 64 MiB generational arena:

```text
64 MiB / 16 B = 4,194,304 symbol records maximum
```

Dirty pages allocate fresh blocks; old and new blocks coexist only until the old page generation retires, then blocks return to the arena free list. The arena is not split into scene-wide front/back halves.

Allocation failure:

```text
retain old FRONT page
report capacity
never partially publish
never silently shrink draw radius
```

## 24.6 Static procedural topology

The existing generated L2 incidence supplies seven position sites per carrier:

```text
H=0, R0=1, R1=2, R2=3, R3=4, R4=5, R5=6
```

The immutable indexed fan is:

```text
0,1,2   0,2,3   0,3,4   0,4,5   0,5,6   0,6,1
```

It addresses shared positions, not eighteen independent knot evaluations.
Every site is evaluated on its original generated loop through the same
L0–L2 evaluator used by export. No XYZ, normal, or float-weld key is added.
L3/L4/L5 never appear in the vertex-ID table.

One immutable fan index buffer repeats this pattern for a page's bounded
carrier slots, not for the entire world arena. Its bound is the existing
512 owners times 128 generated carriers plus 512*6*2 DIRT half-faces:
71680 records, 1290240 uint indices, 5160960 bytes. Exceeding that page bound
is a failed publication, never truncation or an alternative geometry path.

Each shared vertex carries a canonical planar chart coordinate. One allowed
fixed affine convention is:

```text
H=(0,0)
R0=(1,0), R1=(1,1), R2=(0,1),
R3=(-1,0), R4=(-1,-1), R5=(0,-1)
```

This is only a presentation coordinate convention for the same six W_i.
Recovering the containing wedge and its affine barycentrics MUST give the
same barycentrics as direct interpolation of that W_i. It MUST NOT replace
section 20's Flower child footprints or chamber transitions.

The fragment rejects inactive wedges using activeWedgeMask. This preserves
seven shared position sites even for an arbitrary active subset; no normal or
material is appended to a position key to implement that subset. The actual
runtime L2 frame and analytic V derivatives remain wedge/material attributes,
not geometry ownership. Raster is two-sided; reverseWedgeMask transports the
existing generated orientation for material evaluation and export winding.

A source-generation/slot-generation check rejects a stale carrier as a whole.
The serialized source lease excludes M8/sidecar mutation during its graphics
use. A failed page build cannot expose a mixture of old and new knots.

Triangles are disposable raster presentation of the evaluated carrier.
The fragment always performs the three generated ordered-barycentric descents,
reads the scan-authored union masks and one compact RGB/A3/A4/A5 sample.
DIRT uses its explicit support material; it never fabricates captured RGBV.

## 24.7 Draw

Visible pages use:

```text
one vkCmdDrawIndexedIndirectCount
```

Each visible page contributes one 20-byte VkDrawIndexedIndirectCommand:

```text
indexCount    = 18 * SymbolCount
instanceCount = viewInstanceCount
firstIndex    = 0
vertexOffset  = 7 * FirstSymbol
firstInstance = physicalPageSlot * viewInstanceCount
```

viewInstanceCount is 1 for mono/multiview and 2 for instanced stereo, matching
the active Unity XR variant. It is not a geometry or refinement choice.
Unity owns the graphics pipeline, index binding and XR render target; the
existing native serialized dependency chain supplies count-driven submission.

Maximum command memory:

```text
32768 pages * 20 B = 655360 B
plus one 4-byte GPU count
```

The command count is reset in the GPU command stream before current-view
culling. No CPU readback supplies a vertex, index, page count or draw decision.

## 24.8 View culling

Each XR frame may classify HOT page AABBs against the current stereo union frustum/environment-depth visibility for draw command generation only.

This changes only indirect commands. It never changes:

- Flower symbols;
- page membership;
- residency;
- topology;
- SSD state.

Head rotation therefore performs only view cull + ordinary draw and exactly zero storage/readout rebuild work.

---

# 25. Spherical residency

Three concentric regions remain:

```text
SCAN sphere
DRAW sphere
WARM sphere
```

Classification operates only over at most 32768 HOT physical tiles.

For each HOT tile:

\[
d^2=\operatorname{distanceSquared}
(AABB_{tile},headPosition).
\]

Parallel comparison assigns:

```text
OUT
WARM
DRAW
SCAN
```

Triggers:

```text
head rotation
    no residency classification

head translation within same residency cell
    no classification

translation crossing residency cell
    classify HOT slots

canonical residency install/eviction
    classify changed slot
```

No precomputed 12m logical sphere stencil exists.

WARM performs asynchronous SSD prefetch.

SCAN has mutation priority.

---

# 26. SAVE, OPEN and crash consistency

Files:

```text
merkaba-base.bin
merkaba-live.m8log

through-base.bin
through-live.tlog

flower-detail.flog
thread-atlas.tlog

session-manifest.bin
```

## 26.1 Record header

Every append record contains:

```c
struct RecordHeader
{
    uint  Magic;
    ushort Version;
    ushort Kind;
    ulong CommitGeneration;
    uint  AddressBytes;
    uint  PayloadBytes;
    uint  Crc32;
}
```

Dual node records contain:

- existing M8 block/chunk/tile address;
- node state;
- payload when MIXED;
- generation.

An ancestor uniform record with generation \(g\) supersedes every older descendant record.

## 26.2 Commit

One observation/save transaction:

1. append dirty M8 records;
2. append dirty dual records;
3. append FlowerOwnerEpoch changes and FlowerDetail records/tombstones;
4. append ThreadAtlas records;
5. flush each modified stream once;
6. write `session-manifest.bin.tmp` containing:
   - committed generation;
   - valid end offset of every log;
   - active base generations;
   - anchor/session metadata;
7. flush manifest;
8. atomically rename it over the previous manifest.

## 26.3 OPEN

1. read manifest first;
2. ignore every log tail after its recorded end offset;
3. load sparse indices;
4. replay records only through committed generation;
5. reject FlowerDetail/Thread records whose ParentEpoch does not match the replayed sparse FlowerOwnerEpoch;
6. localize the existing session anchor;
7. resume scan after SCAN residency is ready;
8. populate DRAW/WARM independently.

## 26.4 Compaction

Compaction runs only as idle maintenance.

It writes new base files, then publishes a manifest pointing to them.

SAVE never rereads and rewrites the complete world.

CPU log/index progression continues while the native GPU queue has work in flight.

---

# 27. GLB and 3D Tiles

Both exporters consume the same CPU Sphere–Flower/excavation evaluation authority and generated tables as live readout. DIRT support is included from the same resolved FREE/FULL boundary, never from an export-only repair solver.

## 27.1 Petal ownership

For every petal symbol, generated incidence lists its finite incident M8 owners.

Canonical owner:

\[
Owner(p)=
\operatorname{lexicographicMin}(incident\ owner\ coordinates).
\]

Only that owner emits the petal.

## 27.2 Knot identity

Identity is:

```text
(geometry level L0..L2, J, lineClass, sector, rootSign)
```

For a fixed `(J,lineClass)` the endpoint pair is algebraically unique, so no observation-dependent branch ordinal is part of knot identity.

No float weld is performed.

Every duplicate boundary evaluation uses identical:

- ordered owners;
- interval midpoint;
- generated basis;
- operation sequence.

## 27.3 Winding

Winding is:

\[
winding=
flagOrientation
\times directFreeSide.
\]

Hinge branches share position but retain separate normals.

## 27.4 Confirmed/completed/support policy

Export includes:

```text
CONFIRMED petals
unique COMPLETED petals
DIRT support faces from the same excavation evaluator
```

Completed petals carry a metadata flag.

Completed petals disappear on a later dual veto. DIRT carries explicit inferred
support metadata, uses the integer (U, face, presentation-half) identity of
section 13.4, and disappears when excavation removes it or direct coverage
supersedes it. Its winding faces FREE and does not borrow a direct root sign.

## 27.5 Color

```text
valid ThreadRun for the owner epoch
    evaluate the same fixed three-descents + scan-authored RGB signal

otherwise
    export canonical owner M8 PackedColor
```

No RGB inpainting is performed. DIRT exports its identified support material,
not fabricated M8/ThreadAtlas captured color or certified optical detail.

L3/L4/L5 never add export vertices. If output material baking requests
micro-normal, color or optical values, the exporter evaluates the same fixed
399-position address template, thread-order masks and additive `A3/A4/A5` V
signal as live readout. Canonical skin identity is
`(L2 carrier, c3, c4, c5)`; its physical tape coordinate is the generated
deterministic mapping, never a float weld.

## 27.6 Position encoding

The canonical evaluator performs exactly one round-to-nearest binary32 conversion in grid-local coordinates.

3D Tiles use a tile RTC origin. Shared world positions are evaluated before subtracting RTC origin, preserving identical canonical binary32 world values.

## 27.7 Presentation materialization

GLB/3D Tiles may materialize:

- vertices;
- indices;
- baked base color;
- baked normals/roughness.

Materialized geometry is the L2 carrier plus derived DIRT support faces.
These files remain presentation products and can never be loaded as canonical
M8, excavation state, FlowerDetail or ThreadAtlas truth.

---

# 28. Exact replacement of `c34d27f` legacy paths

## 28.1 Removed integration kernels

Remove as semantic production paths:

```text
DiscoverSurfaceCandidates
InitializeSurfaceWinners
SelectSurfaceWinners
QueueResolvedSurfaceCandidates
PrepareIntegrateArgs
IntegrateSurfaceCandidates

QueryCarveTiles
PrepareCarveArgs
IntegrateCarveTiles
ClearTouchedSurfaceCandidates
```

Replace with:

```text
CountObservationBins
ReserveObservationBins
EmitObservationBins
FlowerCommit
FinalizeObservation
```

The existing block/chunk/tile allocation primitives may remain only as storage publication barriers and contain no owner/surface math.

## 28.2 Removed integration functions/authorities

Remove:

```text
MerkabaNearestNormalStep
nearest/+normal/-normal routing
M8IsCurrentFrameSheetSupport
M8HasCurrentFramePlanarSupport
sameM8Sheet normal classification
evidence-weighted plane fusion as geometry authority
26-plane continuing-sheet CARVE
world-sized per-kernel winner selection
```

## 28.3 Removed buffers

Remove:

```text
SurfaceWinnerRanks0
SurfaceWinnerRanks1
SurfaceWinnerRanks2
SurfaceWinnerRanks3

SurfaceQueue

CarveTiles
CarveDispatchArgs
```

Reuse the old 32 MiB `SurfaceCandidates` capacity class only as attempt-local `ObservationRecord` storage.

## 28.4 Removed depth path

Remove:

```text
DepthDilation.compute
InitDepthDilation
DilateDepthStep[8..0]
DilationA
DilationB
gsDilatedDepth carve authority
```

Replace with the min-depth/validity certificate hierarchy.

## 28.5 Removed stereo neighbourhood

Remove:

```text
JointTangentBasis
8-sample square census offsets
arbitrary 12.5mm tangent/bitangent stencil
```

Replace with exact hypothesis-plane R1/R2/R3 loop projection.

## 28.6 Removed readout pipelines

Remove:

```text
ResetReadoutBuild
QueryM8Readout
PrepareReadoutBuild
BuildReadoutVertices
FinalizeReadout

MeshResetReadoutBuild
MeshQueryM8Readout
MeshPrepareReadoutBuild
ProjectReadoutMeshPins
BuildReadoutMesh
MeshFinalizeReadout
```

Replace with:

```text
ClassifyHotFlowerPages
CompactDirtyFlowerSymbols
PublishDirtyFlowerPages
CullFlowerPages
```

## 28.7 Removed readout resources

Remove both publication copies of:

```text
ReadoutVertices0
ReadoutVertices1
ReadoutIndices
M8 readout Mesh objects
MeshReadout scratch/envelope
```

This removes the approximately 480 MiB FRONT/BACK reservation present in `c34d27f`.

## 28.8 Removed geometry authorities

Remove production authority from:

```text
MerkabaCanonicalGeometry octahedron/tip surface output
MerkabaOverlapShell
MerkabaOverlapShell.generated.hlsl
MerkabaSurfaceOrientation.generated.hlsl where derived from OverlapShell
MerkabaExportMembrane surface solver
donor/hole patch solver
float vertex weld
```

Canonical orientation constants needed by the new system move into the single Sphere–Flower authority/codegen.

## 28.9 Removed persistence hot path

Remove SAVE dependence on:

```text
CaptureStoredSnapshotAsync
ReadCanonicalSnapshotAsync
PublishCheckpoint of the complete world
whole-world checkpoint rewrite
```

Replace with the generation-bound append transaction in section 26.

## 28.10 Native ABI

Bump ABI and remove resources/pipeline names listed above.

Add only:

```text
TileHalo
DualBlockState
DualChunkState
DualLeaves
DepthCertificate
ObservationRecords
ObservationTileBins
FlowerDetailPages
ThreadAtlasPages
FlowerSymbolArena
FlowerPageDirectory
FlowerIndirectCommands
```

No compatibility aliases remain.

---

# 29. Required algebraic and system proofs

Before any production GPU cutover, one CPU oracle + codegen authority MUST prove all of the following against the exact same generated tables later compiled into HLSL/native code:

```text
J endpoint identity
negative-coordinate identity
13 line-class uniqueness
fixed endpoint-pair uniqueness for every valid (J,lineClass)
26/72/48 Flower alphabet counts
O_h equivariance under all 48 signed axis permutations: nodes, strands, petals, signed boundaries, incident-petal relations and child substitution; winding transports by the determinant and all 48 flags form one orbit
every strand incidence has the expected two-sided petal relation where topology requires it
deterministic flag/petal winding
child midpoint R1/R2/R3 class correctness
parent node identity under child substitution
parent boundary-strand orientation invariance
tile/chunk/block boundary invariance
loop basis endpoint invariance
all root degeneracies
interval containment / outward rounding
root sector stability
canonical half-open root ownership is independent of petal anchor incidence
every generated incident petal references the same unchanged root identity and enclosure
strict and exact-boundary root references never intersect donor owner-cell interiors
actual confirmed donor root selections are preserved through completion
positive parent48 completion uses actual shared knots, not synthetic candidate masks
R1 same-shell power-order cuts are complete in the shared sector arrangement
R1 clipping is exactly 2 in xi=2*(X-C)/aL and preserves every R1 world loop
each directed R1 sector selects exactly one incident flag and covers all eight face flags
exact symbolic boundary equality yields CERTAIN_BOUNDARY with one generated half-open owner
CERTAIN_BOUNDARY preserves the complete metric enclosure without clipping or shifting
boundary-touching/crossing intervals without exact equality proof remain AMBIGUOUS
exact z=0 boundary roots remain admissible under the generated half-open ownership
R1 Sector-to-PetalMask is exhaustively CPU/HLSL identical and maxSectorCount<=32
rootSign stability under d -> -d
NO observation-dependent branch renumbering
parallel-sheet separation through distinct overlap-owner relations
R2 prediction uses exact generated child loop
R2 self-similar transport preserves the parent residual magnitude
R2 parent-channel/orientation transport is uniquely generated for every new child knot
R2 ancestor rotations use coarse-to-fine ancestry order and outward-rounded Q2.29 intervals
R2 prediction never reads L3-L5 metric V
R2 analysis -> synthesis reproduces the observed child root interval
disjoint same-symbol prediction/observation yields the exact R2 residual
non-singleton residual containing zero remains AMBIGUOUS
peer evidence and shared incidence intersect; R2/R3 pred/obs do not
R2 inherited parent knots remain bit-identical
R2 shared-child knot interval intersection closure
R3 forward/inverse tetra transform
R3 TetraFrame determinant/chirality for all 8 level-local parities
R3 has no manual +L chirality flip
one immutable observation drains all CERTAIN geometry and skin-split work without new camera input
stationary-camera refinement reaches the same final state as immediate eager evaluation
no observationOrdinal-dependent metric/refinement state exists
every L2 has the same complete logical 399-position skin thread
logical thread topology never changes after L2 creation
refinement changes signal splits/values, never topology
zero-detail L2 maps all 399 positions to one root signal
one L2 split creates exactly seven L3 signal regions
one L3 split creates exactly seven L4 signal regions
one L4 split creates exactly seven L5 signal regions
exactly 57 split bits represent complete L3-L5 signal subdivision
no axial lobe enters a skin split address
stable ordered-barycentric chambers partition every wedge exactly
union of chambers mapped to each child equals its exact canonical Flower-7 footprint clipped to the current parent support at all three recursions
empty clipped footprints retain logical identity but cannot prove novelty; missing evidence on nonempty support stays AMBIGUOUS
every generated Flower subtree is contiguous on Gamma_L2
one L3 subtree occupies exactly 57 thread positions
one L4 subtree occupies exactly 8 thread positions
seven L3 subtrees cover exactly 399 positions
all consecutive embroidery transitions are valid Flower adjacency
Fibonacci A/B substitution uses Tparent and preserves subtree contiguity, entry and exit
scan refinement cannot alter thread order
readout cannot alter scan subdivision
canonical L5 address -> thread coordinate is deterministic and bijective
CPU/HLSL mapping is exhaustive-identical for all 343 L5 addresses and boundary tie cases
thread-order split rank addresses exactly the compact parent groups
57-bit parent closure rejects every orphan descendant split
RGB interval split predicate has no false CERTAIN distinction
V interval split predicate has no false CERTAIN distinction
uniform / partial / fully detailed skin has identical L2 geometry
persistent RGB and V authorities remain separate
derived draw split is the exact hierarchical union of RGB/V splits
no dense 399-value allocation is required for uniform L2
additive V preserves every parent innovation
V bubble value and derivative vanish on every child boundary
runtime L2 evaluator supplies world frame; generated tables contain no world frame
hinge shared position with distinct normals
sparse FlowerOwnerEpoch invalidates descendants exactly
compatible parent metric refinement does not spuriously invalidate descendants
dual uniform-node collapse/expand round trip
common-prefix THROUGH fast path has zero false positives
dyadic-cover THROUGH slow path has zero false positives
FreeCell eight-support union equals the complete represented certified volume
missing resolved excavation nodes supply the FULL matter default; COLD does not
DIRT is exactly the exposed resolved FREE/FULL boundary and not a fitted patch
each support face has one FREE-side owner and deterministic winding
direct/COMPLETED coverage supersedes DIRT without duplicate foreground faces
direct geometry holes can retain DIRT without a unique direct completion
DIRT creates no measured R1/R2/R3, captured RGB, or recursive completion evidence
DIRT uses the same live/export evaluator, signed addresses, arena and publication
new direct objects restore FULL over earlier FREE support
new THROUGH removes previously exposed DIRT
completed petal count 0/1/>1
completed petals cannot recursively seed completion
parent deletion invalidates every descendant
L3/L4/L5 cannot emit geometry, alter silhouette or alter depth
analytic V micro-normal and filtered normal moments preserve the evaluated signal
single-native-queue page publication
CPU/HLSL symbol parity
export/live knot parity
export/live fixed-thread RGBV parity
```

Required scene fixtures:

```text
flat wall
translated flat wall
all quantized orientations
smooth bend
convex corner
concave corner
doorway
FOV boundary
invalid-depth boundary
occlusion boundary
range boundary
thin partition
two close parallel sheets
isolated first hit
repeated isolated real object
comet-tail depth error
open direct hole with unique petal
ambiguous large direct hole with resolved excavation support
missing room/cellar corner backed by FULL excavation matter
missing RGB on an existing direct carrier without duplicate DIRT geometry
FOV/range frontier DIRT is labelled inferred, never observed wall
COLD excavation frontier emits no assumed DIRT until resolved
new object introduced into previously FREE volume
closed ghost inside THROUGH
negative coordinates
tile/chunk/block boundary
L0-L2 parent knot invariance
uniform L2 skin over all 399 logical positions
partially split L3/L4/L5 skin
fully split L3/L4/L5 skin
all 343 terminal localities and chamber boundaries
thread-order compact rank across the 32-bit split-mask boundary
RGB-only split, V-only split and exact union draw split
additive parent+child+grandchild V continuity
single accepted close-up observation drained while camera remains stationary
GPU-budget split of one observation into multiple quanta with identical final state
flat wall proves no scan-authored skin split
V Q5.26 representable limit
non-graph metric evidence remains unresolved or revises L0-L2 geometry
parent compatible plane refinement with surviving detail
parent structural sector change with exact detail invalidation
```

No measured or COMPLETED Flower surface is emitted from unresolved direct
evidence. A resolved excavation boundary may independently emit DIRT support
under section 13.4, even where direct geometry is unresolved. Unresolved COLD
or contradictory excavation state emits no DIRT.

**Cutover gate:** until every proof above passes in the CPU oracle and CPU/HLSL parity suite, production scanner/readout/storage authority remains unchanged. Passing the oracle authorizes implementation; it does not authorize retaining a parallel legacy geometry path after final cutover.

---

# 30. Final invariant

The production path is exactly:

\[
\boxed{
\begin{array}{c}
Stereo\ interval\ evidence\\
\downarrow\\
M8\ positive\ endpoints
+
sparse\ SEE\_THROUGH\ negative\ volume\\
\downarrow\\
J=2K+d\\
\downarrow\\
13\ exact\ shared\ loops\\
\downarrow\\
ABC\ interval\ roots\\
\downarrow\\
26\ nodes/72\ edges/48\ flag\ petals\\
\downarrow\\
R1\ core\\
R2\ exact\ phase\ refinement\\
R3\ tetra\ branch/closure\\
\downarrow\\
unique\ direct\ completion\ +\ excavation\ DIRT\ support\\
\downarrow\\
exact\ geometric\ refinement\ through\ L2\\
\downarrow\\
fixed\ planar\ Flower\!\!-7^3\ skin\ address\\
\downarrow\\
immutable\ 399-position\ embroidery\\
\downarrow\\
scan-authored\ 57-bit\ RGB/V\ subdivision\\
\downarrow\\
captured\ RGB+additive\ nested\ V\\
\downarrow\\
three\ exact\ barycentric\ descents\\
\downarrow\\
analytic\ micro-normal/optical\ response\\
\downarrow\\
compact\ procedural\ L2+DIRT\ sphere-resident\ draw
\end{array}
}
\]

Operational meaning:

```text
M8 addresses and confirms.
Sphere loops carry exact metric relations.
R1 keeps a valid surface alive.
R2 refines its shape.
R3 proves branch and junction closure.
Certified visibility excavates FREE out of the default FULL matter model.
The exposed FREE/FULL boundary supplies DIRT support for missing direct geometry.
Direct surface supersedes DIRT; DIRT does not counterfeit direct measurement.
Direct Flower completion still requires one unique finite Flower symbol.
Direct ambiguity does not erase resolved excavation support; unresolved excavation remains unresolved.
FlowerDetail persists metric refinement.
ThreadAtlas persists captured RGB novelty.
L0-L2 are geometry; L3-L5 are the immutable planar Flower-7 skin address.
Every L2 owns the same complete 399-position thread; only its scan-authored signal becomes finer.
Fibonacci selects compile-time mirror stitch templates using the actual parent thread ordinal and has no runtime or scheduling role.
RGB selects the deepest explicit captured value; V adds its L3/L4/L5 metric innovations.
Readout always executes three exact descents and never chooses detail depth.
One observation drains all work its evidence can make CERTAIN; only genuinely AMBIGUOUS detail requires another viewpoint.
Renderer and exporters synthesize the same authority.
```

Repository state was not changed. No commit or patch was created.

---

# REV-C IMPLEMENTATION CONTROL LEDGER

This ledger is mutable implementation state. Everything above this separator is the immutable REV-C contract and MUST remain byte-for-byte identical to `M8-DUAL-SPHERE-FLOWER-CLOSED-PRODUCTION-CONTRACT-REV-C.md`.

## Authority identity

```text
REPOSITORY=fladirm/QuestInfiniteScan
MANDATORY_BASE=c34d27f0ecb51500b12209ed5d2fe72b893726f5
CONTRACT_FILE=M8-DUAL-SPHERE-FLOWER-CLOSED-PRODUCTION-CONTRACT-REV-C.md
CONTRACT_BYTES=102416
CONTRACT_SHA256=11a62ea7ac00230ee547277d7bc3a6211b2c1be2b867485e2cffbc74581680ad
WORKTREE=/mnt/aidisk/prace/uniscan
BRANCH=refactor/m8-dual-sphere-flower-rev-b
CURRENT_COMMIT=70c9f43fa37f3e70b38072cfc12615af291abc87 + uncommitted RUN_04
CURRENT_CUT=RUN_04_IN_PROGRESS
DAG_REVISION=COMPACT_FOUR_RUNS_2026_09_06
DAG_AUDIT=PENDING_EXCAVATION_AMENDMENT
FINAL_DAG_AUDIT=PENDING
FINAL_CONTRACT_AUDIT=PENDING
FINAL_LEGACY_AUDIT=PENDING
FINAL_BUILD=PENDING
FINAL_RUNTIME_FIXTURES=PENDING
```

## Allowed source roots

```text
Runtime/
Editor/
Tests/
Tools/
package.json
root authority/build documentation and matching Unity .meta files
```

Existing untracked user-owned `.claude/`, `CLAUDE.md`, and `CLAUDE.md.meta` are out of scope and MUST remain untouched. Historical `.codex/`, `ALGORITHM.md`, `kontrakt.md`, `errors.md`, and superseded `contr.md` are not algorithmic authority. CUT 0 must make the repository authority chain unambiguous without erasing history.

## Hard invariant registry

```text
H01 Mandatory ancestry is c34d27f0ecb51500b12209ed5d2fe72b893726f5.
H02 KernelState remains 16 B and the only measured coarse positive surface authority; it never stores derived DIRT.
H03 Excavation starts as FULL model matter and certified visibility removes FREE; its exposed boundary supplies DIRT support without requiring unique direct completion. DIRT never counterfeits measured evidence.
H04 FlowerDetail and ThreadAtlas are subordinate persistent truth under an M8 FlowerAddress and sparse parent epoch.
H05 J_L(K,d)=2*K_L+d=J_L(K+d,-d), including negative coordinates and geometric L0..L2.
H06 R1=core, R2=native shape refinement, R3=branch/corner/chirality closure; shell is neither LOD nor confidence.
H07 No branch ranks, nearest-root matching, normal-angle ownership, generic fitting, or magic epsilon.
H08 ABC/root/SEAL/BEND/R2/R3 use outward-rounded CERTAIN/IMPOSSIBLE/AMBIGUOUS interval algebra.
H09 Direct Flower topology is the generated 26-node/72-strand/48-petal alphabet; DIRT uses the six exact integer excavation face classes. Neither creates a persistent mesh or runtime adjacency graph.
H10 Geometric child substitution stops at L2 and preserves parent knot identity and boundary orientation exactly.
H11 Direct Flower completion requires exactly one finite candidate; neither COMPLETED nor DIRT can recursively seed direct completion. DIRT support is independently derived from excavation.
H12 THROUGH requires conservative full projected-support proof; AMBIGUOUS performs no destructive write.
H13 Seeds do not draw or own fine state; stable R1 requires finite incidence or a compatible later observation.
H14 Reduction is touched-only, deterministic, with exactly one commit workgroup per touched tile.
H15 L0..L2 are geometry; L3..L5 are always-existing planar Flower-7 skin addresses and can never emit geometry, silhouette or depth.
H16 RGB cannot create V; RGB is deepest explicit captured radiance while V is additive nested metric innovation with exact boundary value/derivative preservation.
H17 Captured RGB remains captured radiance; no invented albedo or ambient field.
H18 Readout uses one compact arena/publication for L2 and DIRT symbols. L2 skin always executes three descents without validDepth/LOD authority; DIRT evaluates the exact excavation boundary and its support material.
H19 Head rotation causes zero residency, SSD, topology, page, or Flower rebuild work.
H20 SAVE is dirty append plus atomic manifest; OPEN rejects tails/orphans and resumes SCAN independently of DRAW/WARM.
H21 Live, GLB and 3D Tiles share the L0-L2 evaluator, fixed-thread RGB/V and exact DIRT boundary evaluator/provenance. Export mesh is presentation only.
H22 One serialized native Vulkan queue; scanner/FINE/ERASE outrank bounded page compaction.
H23 Superseded authority is removed in its replacement cut; no fallback, alias, flag, or second authority remains.
H24 Insufficient information yields UNRESOLVED plus a discrete refinement reason, never inferred conventional geometry.
H25 Refinement is evidence-bounded: one immutable observation drains every CERTAIN geometry/signal test without observationOrdinal, camera motion, blind expansion or scheduling-dependent results.
H26 Every L2 owns one immutable recursively subtree-contiguous 399-position thread; Fibonacci selects compile-time A/B stitch templates only by actual parent-thread ordinal.
H27 RGB and V own independent exact 57-bit thread-order split masks; interval predicates alone publish atomic seven-child groups and draw subdivision is their exact union.
```

## Forbidden legacy registry

```text
DiscoverSurfaceCandidates; InitializeSurfaceWinners; SelectSurfaceWinners; QueueResolvedSurfaceCandidates
PrepareIntegrateArgs; IntegrateSurfaceCandidates; QueryCarveTiles; PrepareCarveArgs; IntegrateCarveTiles
ClearTouchedSurfaceCandidates; MerkabaNearestNormalStep; nearest/+normal/-normal routing
M8IsCurrentFrameSheetSupport; M8HasCurrentFramePlanarSupport; sameM8Sheet; weighted plane fusion authority
26-plane continuing-sheet CARVE; SurfaceWinnerRanks0..3; SurfaceQueue; CarveTiles; CarveDispatchArgs
DepthDilation.compute; InitDepthDilation; DilateDepthStep[8..0]; DilationA/B; gsDilatedDepth
JointTangentBasis; square census/tangent stencil
Reset/Query/Prepare/Build/Finalize legacy readout; all MeshReadout variants
ReadoutVertices0/1; ReadoutIndices; M8 readout Mesh objects; MeshReadout scratch/envelope
MerkabaCanonicalGeometry surface output; MerkabaOverlapShell and generated HLSL
OverlapShell-derived surface orientation; MerkabaExportMembrane; donor/hole solver; float weld
CaptureStoredSnapshotAsync/ReadCanonicalSnapshotAsync/whole-world PublishCheckpoint SAVE path
compatibility resource/pipeline aliases
TSDF; Surface Nets; Marching Cubes; QEF; Delaunay; generic Voronoi/implicit phi
finite-difference Hessian/Taylor/least-squares/PCA reconstruction
generic graph/holonomy/flood/fill/min-cut/membrane repair; morphology/dilation geometry
Gaussian splats; generic/dynamic canonical mesh; persistent XYZ/PortKey graph
world winner banks; nearest-neighbour ownership; confidence/refinement score soup
per-frame world traversal or topology rebuild
```

## Implementation DAG status

Only the compact DAG below is executable. Historical CUT 04–17 are coverage
references, not separate runs or acceptance cycles.

| Work | Status | Commit / owner |
|---|---|---|
| CUT 0 — authority | PASS | `7a42dda` |
| CUT 1 — exact fixed-thread oracle/codegen | PASS | `3d78451` |
| CUT 2 — fixed-thread ABI | PASS | `d5551bf` |
| CUT 14 — append/manifest foundation | PASS | `0c59ff1` |
| CUT 3 — GPU bins/reduction/publication | IMPLEMENTED; final validation pending | HEAD |
| RUN 4 — complete scanner and detail | PENDING | replacement of old CUT 4–11 |
| RUN 5 — procedural readout and residency | PENDING | replacement of old CUT 12–13 |
| RUN 6 — shared export and app wiring | PENDING | old CUT 15 / lifecycle integration |
| RUN 7 — final integration/validation | PENDING | final closure, no RUN 8 |

## Historical bootstrap proof/test evidence — not current acceptance

```text
PHASE_0 current contract bytes/SHA-256: 86895 / a49c511126750cd46cde2fa06f12d27410c94a3f47c50c58114316261fae9c0b
PHASE_0 branch ancestry: exact base HEAD
CPU_ORACLE=NOT_RUN
CPU_HLSL_PARITY=NOT_RUN
UNITY_TESTS=PASS 293/293 EditMode (2026-09-06)
SHADER_AUDIT=NOT_RUN
GLB_VALIDATION=NOT_RUN
TILES_VALIDATION=NOT_RUN
QUEST_APK=NOT_BUILT
DEVICE_ACCEPTANCE=NOT_RUN
```

## Removed legacy authorities

None yet. Removal is recorded only after the replacement cut and manual audit pass.

## Historical bootstrap state — superseded

```text
Working branch is exactly at mandatory base c34d27f0ecb51500b12209ed5d2fe72b893726f5.
REV-B was read completely and is copied above as immutable authority.
Superseded contr.md was read completely but is not authority for this refactor.
No production source, ABI, shader, persistence format, readout, export path, or generated file changed.
Forensic mapping and complete DAG audit are in progress. No implementation cut is closed.
```

## PHASE 1 — forensic map of mandatory base `c34d27f`

The map below was derived from the physical source at the mandatory base, not from historical notes.

### Current authority classification

| Area | Current physical authority | Disposition | Replacement/deletion cut |
|---|---|---|---|
| Signed sparse address | `Runtime/Merkaba/MerkabaSpatial.cs`, `Runtime/Shaders/MerkabaSpatial.hlsl` — PCG block hash, Block→Chunk→Tile→Kernel, signed floor division/modulo | KEEP storage/residency addressing; remove it from geometry inner loops | CUT 2, CUT 3 |
| Coarse ABI | `KernelState.cs`, `MerkabaConstants.cs`, matching HLSL — exact 16-byte state; occupancy/color/plane packed in flags | KEEP 16 B; reclaim legacy `NeedsCarve` bit as `R1_SEED`; remove old meaning | CUT 2, CUT 5 |
| Direct geometry authority | `MerkabaCanonicalGeometry.cs` and generated HLSL — octahedron plus eight tip primitives, explicitly named direct geometry authority | REWRITE into one Sphere–Flower authority; delete surface-output role | CUT 1, CUT 12, CUT 16 |
| Overlap/membrane authority | `MerkabaOverlapShell.cs` and generated overlap/orientation HLSL — dominant-axis patches, nearest normal step, corner donor search, free-side signatures | DELETE_AFTER_CUTOVER | CUT 15, CUT 16 |
| Observation semantics | `MerkabaObservation.cs` — QRS surface/free classifier using dilated depth, ray tube, disparity and normal thresholds | REWRITE with immutable interval endpoint and full-support THROUGH certificate | CUT 4, CUT 6 |
| Candidate/winner authority | `MerkabaSurfaceMeasurement.cs`, `SurfaceCandidates`, `SurfaceQueue`, four `SurfaceWinnerRanks` banks | DELETE_AFTER_REPLACEMENT; reuse only the 32 MiB transient capacity class for `ObservationRecord` | CUT 3, final deletion CUT 5 |
| Integration orchestration | `MerkabaIntegrator.cs` — discover/resolve/winner/queue/integrate/carve/finalize dispatch chain and native submission | REWRITE semantic chain; KEEP immutable-observation retry and single-queue scheduling concepts | CUT 3–9, ABI closure CUT 16 |
| Integration GPU | `MerkabaIntegration.compute` — nearest/±normal owners, current-frame 4-pixel planar support, world winner selection, weighted plane fusion, 26-plane CARVE | REWRITE; remove each superseded kernel/function in its replacement cut | CUT 3–9, sweep CUT 16 |
| Stereo | `StereoRgbdRefine.compute` — five hypotheses and strict L/R evidence, but `JointTangentBasis` plus 8-sample square census | KEEP bounded hypotheses/joint evidence; REWRITE support to exact Flower loops | CUT 4 |
| Depth free proof | `DepthDilation.compute`, `DepthCapture.ComputeDilation`, `DilationA/B`, `gsDilatedDepth` | ABI_REPLACE with min-depth/all-valid certificate hierarchy | CUT 6, native cleanup CUT 16 |
| FINE target | `DepthNormals.compute` — one parallel 128-lane reduction and tiny asynchronous target readback | KEEP acquisition UX/parallel target; route refinement into FlowerDetail | CUT 10, CUT 17 |
| ERASE geometry | `FineBrushDescriptor.cs` exact cylinder plus integration `QueryFineEraseBlocks/EraseFineBlocks` | KEEP descriptor and hard-delete semantics; REWRITE mutation/invalidation for M8, dual and descendants | CUT 6, CUT 10, CUT 17 |
| GPU resource authority | `MerkabaGrid.Gpu.cs` — M8 tables plus candidates/winners/carve and two ~240 MiB readout slots | KEEP M8 spatial buffers; ABI_REPLACE superseded resources with dual/detail/symbol/page data | CUT 2, CUT 3, CUT 6, CUT 10, CUT 12, CUT 16 |
| Legacy readout GPU | `MerkabaReadout.compute` — canonical primitive materialization and alternate depth-grid mesh path | REWRITE completely to compact dirty symbols, page cull and indirect commands | CUT 12 |
| Renderer | `MerkabaGridRenderer.cs` — radial `renderDistance=12m`, full FRONT/BACK readout builds, optional MeshReadout, loaded-coverage scan warmup | KEEP spherical intent, opaque/environment-depth/FINE preview; REWRITE publication and remove mesh fallback/warmup coupling | CUT 12, CUT 13, CUT 17 |
| Render shader/feature | `MerkabaGrid.shader`, `MerkabaRenderFeature.cs` | REWRITE vertex/fragment evaluator to shared Flower/RGBV authority; KEEP opaque XR/environment-depth integration | CUT 11, CUT 12 |
| Native Vulkan executor | managed `MerkabaNativeVulkanExecutor.cs`, native `MerkabaVulkanTimestamps.cpp`, shader generator — ABI v1, 45 resources, 49 pipelines, one in-flight job | KEEP one serialized queue/timestamps; ABI_REPLACE pipeline/resource contract | CUT 2–16, final ABI cut CUT 16 |
| HOT storage pump | `MerkabaGrid.Storage.cs` — load/writeback tasks but returns before CPU completion while native job is in flight | REWRITE into independent CPU progression and GPU publication | CUT 14 |
| Persistent M8 store | `MerkabaSsdStore.cs` format v3 — checkpoint plus write-through overlay; snapshot read and whole checkpoint publication | ABI_REPLACE with generation-bound append logs and manifest | CUT 14 |
| SAVE/OPEN | `MerkabaPersistence.cs`, `MerkabaSessionCatalog.cs` — flush, capture whole stored snapshot, publish whole checkpoint; named session/anchor metadata | KEEP named-session/anchor UX; REWRITE transaction and replay | CUT 14, CUT 17 |
| Export shell repair | `MerkabaExportShell.cs` — closing offsets, synthetic kernels/colors, one-pass hole healing | DELETE_AFTER_CUTOVER | CUT 15 |
| Export membrane repair | `MerkabaExportMembrane.cs` — overlap patches, donor inference, sparse max-flow/min-cut partition | DELETE_AFTER_CUTOVER | CUT 15 |
| GLB geometry | `MerkabaGlbWriter.cs` — membrane quads, dynamic lists, `Dictionary<VertexKey,uint>` float weld | REWRITE to shared CPU Flower evaluator and canonical KnotAddress | CUT 15 |
| 3D Tiles | `MerkabaTilesetWriter.cs`, `MerkabaExporter.cs` — chunk streaming and anchor binding but membrane geometry input | KEEP package/streaming/RTC/anchor UX; REWRITE geometry input/ownership | CUT 15, CUT 17 |
| Scanner lifecycle | `RoomScanner.cs`, `ScanOperationState.cs` — SCAN/STOP/RESUME/WAKE/SAVE/OPEN/FINE/ERASE/export quiesce | KEEP behavior; rewire completion/readiness rules | CUT 14, CUT 17 |
| Session anchor | `RoomAnchorManager.cs`, `RoomSpaceRoot.cs` | KEEP authority and persisted UUID relocation semantics | CUT 14, CUT 15, CUT 17 |
| ALIGN/viewer | `MerkabaArtifactViewer.cs` — 3D Tiles load, persisted anchor 1:1 alignment, controller/hand manipulation, annotations | KEEP public UX; consume new export output without becoming scan authority | CUT 15, CUT 17 |
| Controller/UI | `RoomScanInputHandler.cs`, `ControllerRayDriver.cs`, `DebugMenuController.cs` plus UXML/USS | KEEP actions/workflow; update status/progress bindings only where formats change | CUT 17 |
| Design/paint workspace | `MerkabaPaintEngine.cs`, `MerkabaDesignDocument.cs`, `MerkabaDesignLibrary.cs`, viewer integration | KEEP independent design authority and two-hand workflow; regression-test against new scan/export lifecycle | CUT 17 |
| Existing tests | 19 EditMode suites, including 61 GPU integration tests and legacy overlap/membrane/export tests | REWRITE deleted-authority tests; add CPU oracle, parity and scene fixtures | CUT 1 onward; stale-test sweep CUT 16/17 |
| Build/proof tooling | Unity EditMode/APK scripts, SPIR-V audit, native shader generator, GLB validator | KEEP entry points; update ABI/pipeline manifests and add generated-table integrity checks | CUT 1, CUT 16, CUT 17 |

### Verified base facts and costs

```text
TileSize=8; KernelsPerTile=512; PhysicalTileCapacity=32768.
Block span=256 kernels; 512 chunks/block; 64 tiles/chunk; BlockCapacity=8192.
KernelState=16 B; one tile payload=8192 B.
SurfaceCandidates=2,097,152 × 16 B = 32 MiB.
SurfaceQueue=1,048,576 × 8 B = 8 MiB.
SurfaceWinnerRanks0..3=4 × 8192 × 512 × 4 B = 64 MiB.
Legacy readout=~96 MiB stream0 + ~96 MiB stream1 + ~48 MiB indices per slot;
two publication slots reserve approximately 480 MiB.
One dense HOT SEE_THROUGH leaf plane would be 2 MiB; cold persistence is presently absent.
The native executor serializes observation, readout/MeshReadout and FINE/ERASE as one job authority.
The current SAVE path rereads canonical tiles and rebuilds a complete checkpoint.
```

### Manual forensic evidence

```text
Files physically reviewed:
  Runtime/Merkaba/MerkabaConstants.cs
  Runtime/Merkaba/KernelState.cs
  Runtime/Merkaba/MerkabaSpatial.cs
  Runtime/Merkaba/MerkabaCanonicalGeometry.cs
  Runtime/Merkaba/MerkabaObservation.cs
  Runtime/Merkaba/MerkabaSurfaceMeasurement.cs
  Runtime/Merkaba/MerkabaMutationCoverage.cs
  Runtime/Merkaba/MerkabaReadoutCoverage.cs
  Runtime/Merkaba/MerkabaGrid.Gpu.cs
  Runtime/Merkaba/MerkabaGrid.Storage.cs
  Runtime/Merkaba/MerkabaIntegrator.cs
  Runtime/Shaders/MerkabaIntegration.compute
  Runtime/Shaders/StereoRgbdRefine.compute
  Runtime/Shaders/DepthDilation.compute
  Runtime/Shaders/DepthNormals.compute
  Runtime/Shaders/MerkabaReadout.compute
  Runtime/Merkaba/MerkabaGridRenderer.cs
  Runtime/Telemetry/MerkabaNativeVulkanExecutor.cs
  Runtime/Telemetry/Native/MerkabaVulkanTimestamps.cpp
  Runtime/Merkaba/MerkabaSsdStore.cs
  Runtime/Merkaba/MerkabaPersistence.cs
  Runtime/Merkaba/MerkabaSessionCatalog.cs
  Runtime/Merkaba/MerkabaOverlapShell.cs
  Runtime/Merkaba/MerkabaExportShell.cs
  Runtime/Merkaba/MerkabaExportMembrane.cs
  Runtime/Merkaba/MerkabaExporter.cs
  Runtime/Merkaba/MerkabaGlbWriter.cs
  Runtime/Merkaba/MerkabaTilesetWriter.cs
  Runtime/Core/RoomScanner.cs
  Runtime/Core/RoomAnchorManager.cs
  Runtime/Core/RoomSpaceRoot.cs
  Runtime/Core/ScanOperationState.cs
  Runtime/RoomScanInputHandler.cs
  Runtime/UI/ControllerRayDriver.cs
  Runtime/UI/DebugMenuController.cs
  Runtime/UI/MerkabaArtifactViewer.cs
  Runtime/Merkaba/MerkabaPaintEngine.cs
  Runtime/Merkaba/MerkabaDesignDocument.cs
  Runtime/Merkaba/MerkabaDesignLibrary.cs
  Tests/Editor/* and Tools/{unity,shaders,gltf} entry points

PHASE_1_MANUAL_AUDIT=PASS
Production mutations during forensic audit=0
```

## Závazný implementační DAG — CUT 3 + nejvýše čtyři další runy

Revize 2026-09-06 podle přímého pokynu uživatele. Tento DAG nahrazuje původní
samostatné CUT 04–17, jejich samostatné closure cykly a jejich pořadí.
Nemění žádnou rovnici, datový model, invariant ani acceptance podmínku
zmrazeného kontraktu. Staré číslování níže slouží pouze k dohledání pokrytí,
nikoli jako další seznam runů.

### Autorita a skutečný výchozí stav

- Repo: `/mnt/aidisk/prace/uniscan`, `fladirm/QuestInfiniteScan`.
- Mandatory base: `c34d27f0ecb51500b12209ed5d2fe72b893726f5`.
- Pracovní commit při sestavení DAGu: `d5551bfba0012c2e72a7071f38d023909bcb23c9`;
  aktuální CUT 3 handoff je HEAD připravovaného review commitu.
- Autorita: celý zmrazený `M8-DUAL-SPHERE-FLOWER-CLOSED-PRODUCTION-CONTRACT-REV-C.md`,
  včetně fixed-399-thread a všech šesti posledních zpřesnění; jeho nezměněná
  kopie tvoří prvních 86895 bajtů `lasttrue.md`.
- Uzavřené základy: CUT 0 (`7a42dda`), CUT 1 (`3d78451`),
  CUT 2 (`d5551bf`), persistence CUT 14 (`0c59ff1`).
- CUT 3 substrate je implementovaný pro RUN 4; formální runtime/proof validace
  je podle pokynu uživatele odložená. Není to nový PASS celé aplikace.
  Přeskupení DAGu samo o sobě nedokončilo žádný produkční mechanismus.
- Povolené source roots: `Runtime/`, `Editor/`, `Tests/`, `Tools/`;
  `package.json` a build/authority metadata jen podle skutečné potřeby.
  Uživatelské `.claude/`, `CLAUDE.md`, `CLAUDE.md.meta` zůstávají nedotčené.

### Jediné pořadí provádění

```text
HOTOVÉ: CUT 0 -> CUT 1 -> CUT 2 -> CUT 14
                                      |
                             dokončit CUT 3
                                      |
                       RUN 4: celý scanner
                                      |
                 RUN 5: readout + spherical residency
                                      |
                 RUN 6: export + aplikační napojení
                                      |
                 RUN 7: finální integrace a validace
```

Po CUT 3 existují přesně čtyři další runy. Nevzniknou pod-runy, další
samostatné authority cuts ani nové per-module closure cykly. Vnitřní
závislosti se implementují uvnitř daného runu, ne jako nový projektový DAG.

### Pravidla čisté implementace

1. Upravit existující consumer flow a používat již napsanou Sphere–Flower
   authority, codegen, datové typy a append storage. Nevytvářet druhý evaluator,
   obecný framework, další orchestration vrstvu, druhou frontu ani fallback.
2. Samostatný shader dispatch vzniká pouze pro potřebnou GPU synchronizaci,
   kapacitu či datovou závislost. R1/R2/R3, completion a skin nejsou automaticky
   samostatné dispatch fáze. Preferovat lokální práci uvnitř FlowerCommit.
3. Každá nahrazená stará cesta se odstraní spolu se svými volajícími,
   buffery, bindingy, native jmény a obsolete generovanými soubory ve stejném
   runu. RUN 7 není odkladiště ponechaných legacy authorities.
4. Během implementace nepouštět testy, benchmarky ani opakované audity.
   Jen compile/build check nezbytný pro pokračování. Jeden závěrečný
   validační průchod v RUN 7; potom cílené opravy skutečných selhání.
5. IMPLEMENTED není VALIDATED/PASS. Dřívější proof evidence se zachovává,
   ale nepovažuje se za důkaz nově změněného kódu. Gate §29 zůstává závazná:
   produkční GPU cutover nesmí proběhnout před požadovanými CPU/HLSL důkazy.
   Lokální implementace není automatické schválení produkčního nasazení.
6. Commit uzavřeného runu a finální PASS vyžadují skutečně splněné podmínky,
   nikoli jen upravený seznam. Quest je odpojený; device acceptance se nefinguje.
7. Po kompaktaci pokračovat z aktuálního kurzoru, relevantního diffu a posledních
   2 KiB historie před kompaktací; nečíst znovu celý historický ledger.
8. GPU hot path nemá geometry readback, CPU surface solve, world clear ani
   rebuild při otočení hlavy. CPU dále zajišťuje storage/session I/O, oracle
   a kontraktem předepsaný CPU export; to není druhá scan authority.

### CUT 3 — dokončit GPU observation substrate, nerozšiřovat jej

```text
ID=CUT_03
NAME=Deterministické binning/reduction a storage publication
STATUS=IMPLEMENTED_FOR_RUN_04; VALIDATION_DEFERRED_TO_RUN_07
DEPENDS_ON=CUT_01,CUT_02,CUT_14
FILES_TOUCHED=existující ObservationBins/Owners/Reduction; FlowerTileHalo; World.hlsl/compute; MerkabaObservationBinsGpu; Grid.Gpu; managed/native executor a shader generator
NEW_AUTHORITY=žádná nová surface authority; dokončený GPU vstup pro společný FlowerCommit
LEGACY_REMOVED=nahrazené attempt-local obsluhy; pozitivní winner/queue a CARVE se odstraní společně v RUN_04, nikoli třemi samostatnými cutovers
INVARIANTS=H05,H07,H08,H14,H22,H24,H25; osm ownerů; 32 MiB records; jeden WG/touched tile; interval před PrecisionKey; 27 generation-valid halo refs
CPU_PROOFS=existující owner/root/reduction oracle; doplněné povinnosti se validují v závěrečném průchodu
GPU_TESTS=permutace recordů, overflow, nekompatibilní kořeny, záporné/boundary adresy, allocation retry, generation/ABA halo
SCENE_FIXTURES=flat/shuffled, thin parallel sheets, 512² maximum, missing/COLD retry
PERF_CHECKS=žádný world winner clear, per-record geometry hash ani další 32 MiB arena
ACCEPTANCE=kompletní callable reduction/storage průchod bez placeholderu; explicitně napojené všechny jeho resources a retry lifecycle; validace nesmí být předstírána
ROLLBACK_BOUNDARY=dosud neaktivovaný nový observation vstup; nepřepínat produkční R1 v tomto runu
```

Dokončit pouze zbývající propojení: root/sector reduction a PrecisionKey,
alokaci a binding TileHalo, publication chybějících prostorových uzlů a
native/managed obsluhu opakování téže immutable observation. Vlastní
přepnutí Integratoru na stereo + FlowerCommit patří do následujícího
jediného scanner runu; není důvod před ním zavádět dočasný stereo/R1 bridge.

### RUN 4 — jeden kompletní scanner včetně detailu

```text
ID=RUN_04
NAME=Stereo -> M8 + SEE_THROUGH + FlowerDetail + ThreadAtlas
STATUS=PENDING
DEPENDS_ON=CUT_03
FILES_TOUCHED=StereoRgbdRefine.compute; DepthCapture; MerkabaObservation; MerkabaIntegration.compute/Integrator; Grid.Gpu/Storage; existující SphereFlower authority/codegen, DualVisibility, FlowerDetail, ThreadAtlas; native executor/generator
NEW_AUTHORITY=jediný GPU observation/refinement/ERASE flow zapisující čtyři kontraktní autority přes existující append transaction
LEGACY_REMOVED=square census; nearest/normal-step/current-frame routing; weighted plane fusion; Discover/Initialize/Select/Queue/IntegrateSurfaceCandidates; winner banks/SurfaceQueue; CARVE kernels/resources; DepthDilation; NeedsCarve význam; provisional fine-state shortcuts
INVARIANTS=H02-H17,H20,H22-H27; R1 jediný vlastní measured occupancy; R2/R3 jej nemažou; excavation FULL je výchozí hmota a FREE ji vyžírá, DIRT support je derived; skin nemění L2 geometrii
CPU_PROOFS=všechny příslušné §29: stereo/root, R1 seed/promotion, dual/certificate, R2/R3/hinge, completion, epoch, split a eager/drain parity
GPU_TESTS=stejné generované tabulky; jeden WG/tile; immutable work drain; negativní a hierarchy boundaries; zero-false-THROUGH; atomic epoch a sedm child values
SCENE_FIXTURES=kontraktní geometry/sensor/ghost/hole fixtures; stationary observation; uniform/partial/full skin; RGB-only/V-only; parent refine/delete
PERF_CHECKS=bounded hypotheses, fused conditional shell work, 32 MiB records, sparse dual/detail, novelty-proportional skin; bez readbacku, blind expansion a dispatch zoo
ACCEPTANCE=nový scanner implementuje celou observation transaction včetně SAVE/OPEN datového napojení; jeho stará pozitivní i negativní větev jsou odstraněny
ROLLBACK_BOUNDARY=celý scanner run; žádný newEnabled/oldPath fallback
```

Vnitřní implementační pořadí, nikoli další runy:

- Zachovat Depth-L/R, PCA-L/R a pět bounded hypotheses. Nahradit square
  support přesnými Flower loops a zavést min-depth/AllValid certificate.
- Napojit CUT 3 na R1 seed/promotion/retirement a sparse dual update
  s úplným support certificate a direct endpoint precedence.
- Zachovat dynamické FULL obnovení novým endpointem. Změna excavation nebo
  direct coverage označí dotčené DIRT pages; žádný nový persistent DIRT svět.
- Ve stejném commit flow použít generated R2 child-loop residual,
  R3 determinant/chirality a jedinou finite completion/ghost algebru.
- Doplnit L1/L2 metric records, structural owner epochs, FINE/ERASE
  invalidation a skutečné GPU zápisy obou skin autorit.
- RGB/V split rozhoduje scan přes sedm CERTAIN complete-footprint intervalů
  a prokazatelně disjunktní dvojici. Atomicky publikuje split + sedm hodnot.
  Žádný validDepth ani existence tree.
- RGB zůstává skutečná captured radiance; V je additive nested innovation.
  Jedna observation vyčerpá všechny vlastní CERTAIN geometric/skin práce.
  AMBIGUOUS žádá novou informaci, nikoli nový frame pro pouhé scheduling.

### RUN 5 — jeden procedural readout a jeho residency

```text
ID=RUN_05
NAME=Dirty L2 pages -> compact RGBV -> cull/draw
STATUS=PENDING
DEPENDS_ON=RUN_04
FILES_TOUCHED=MerkabaReadout.compute; GridRenderer; Grid.Gpu; Grid.shader; RenderFeature; ReadoutCoverage a existující storage residency hooks; sdílený evaluator/codegen; native executor/generator
NEW_AUTHORITY=derived L2 + DIRT symbol pages a RGB/V union samples; 64 MiB společná generational arena; jedna serialized-queue publication; SCAN/DRAW/WARM
LEGACY_REMOVED=oba legacy readout pipelines; MeshReadout; vertex/index/Mesh FRONT/BACK rezervace; octahedron/tip output; camera-dependent rebuild; giant logical sphere stencil a loaded-DRAW gate pro SCAN
INVARIANTS=H09-H11,H15-H19,H21-H27; L2 measured geometry + exact DIRT boundary; thread order; runtime L2 frame; pro L2 skin vždy tři sestupy a jeden aligned hot sample; direct coverage má před DIRT přednost
CPU_PROOFS=§29 page/arena/ownership/winding, exact chamber footprints, 399/343 bijections, thread ranks, RGB/V union, additive V/normal moments, publication; §13.4 FreeCell union, unique DIRT face ownership, COLD ambiguity a no-FREE-interior matter
GPU_TESTS=CPU/HLSL vertex/signal parity; page failure retains FRONT; epoch validity; single queue; rotation mění jen cull commands
SCENE_FIXTURES=uniform/partial/full skin se stejnou geometrií; hinge; completed/veto; RGB/V union; head rotation, translation, COLD/WARM a arena capacity
PERF_CHECKS=odstranit ~480 MiB rezervaci; arena 64 MiB; commands <=512 KiB; jeden vkCmdDrawIndirectCount; žádná L3-L5 geometrie, runtime Fibonacci ani per-frame rebuild
ACCEPTANCE=renderer používá společný L2 evaluator, scan-authored signal a schválený exact excavation DIRT evaluator; nevytváří measured detail ani nemění subdivision; DIRT skutečně podporuje chybějící direct povrch a není jen diagnostic overlay
ROLLBACK_BOUNDARY=celý readout/residency run; starý renderer není emergency fallback
```

Struna zůstává vždy 399 logických pozic. Identity jsou radix-7, masks/rank
jsou v thread-parent order. Fibonacci zůstává v codegenu jako
`FibBit(Tparent)` pro portově/orientačně platné A/B embroidery; runtime jej
nevypočítává. World frame dodává skutečný L2 evaluator. Fragment vyhodnocuje
nejhlubší explicitní RGB a součet A3ψ3+A4ψ4+A5ψ5. Captured-light-preserving
normal/optical synthesis nevytváří albedo, ambient ani nepozorovaný specular.

### RUN 6 — společný export a napojení celé aplikace

```text
ID=RUN_06
NAME=SAVE/OPEN, GLB/3D Tiles a existující aplikační lifecycle
STATUS=PENDING
DEPENDS_ON=RUN_05,CUT_14
FILES_TOUCHED=MerkabaExporter/GlbWriter/TilesetWriter; existující SsdStore/replay/persistence/session; RoomScanner/ScanOperationState; anchor/ALIGN/FINE/ERASE/controller/design call sites pouze podle potřeby
NEW_AUTHORITY=žádná další; všechny consumery napojené na tytéž M8/dual/detail/thread records a společný evaluator
LEGACY_REMOVED=ExportShell/ExportMembrane, donor/repair/min-cut/float weld, zbývající OverlapShell a jeho codegen po odstranění posledního consumeru; staré lifecycle/save/readout gate a export-only solver
INVARIANTS=H02-H04,H09-H11,H15-H17,H19-H27; dirty append; manifest-first replay; SCAN readiness nezávislá na DRAW/WARM; export je presentation, nikoli world truth
CPU_PROOFS=§26 crash/generation/epoch replay a §27 live/export knot+RGBV+DIRT parity, ownership, winding, RTC, support provenance a deterministic output
GPU_TESTS=upload/publication a priority consumerů; žádný nový CPU hot-path geometry solve
SCENE_FIXTURES=SAVE/OPEN fine+dual, stale descendants, SCAN/STOP/RESUME/WAKE/ALIGN/FINE/ERASE, GLB/3D Tiles, session anchor, controller/two-hand/design, multiroom/stairs/large space
PERF_CHECKS=O(dirty) SAVE, CPU storage pokračuje při GPU jobu, bounded export streaming; žádný whole-world snapshot rebuild či export repair pass
ACCEPTANCE=aplikační flow je plně implementované bez UX redesignu a bez další geometry authority; exporter i live čtou stejný signal
ROLLBACK_BOUNDARY=celé consumer napojení; ponechat existující session/anchor/UI/design ownership
```

Již hotový CUT 14 se nepíše znovu. Pouze se dopojí skutečné nové GPU
producenty a consumery a opraví prokazatelné lifecycle nesoulady.
UI, paint/design a viewer se nepřestavují; zachovávají svoje současné funkce.

### RUN 7 — dokončení integrované aplikace a jeden validační průchod

```text
ID=RUN_07
NAME=Finální integrace, opravy skutečných selhání a release closure
STATUS=PENDING
DEPENDS_ON=RUN_06
FILES_TOUCHED=jen skutečné integration/build/validation fixy v již změněných files; existující test/build entry points; final native ABI/generator; lasttrue evidence
NEW_AUTHORITY=žádná
LEGACY_REMOVED=poslední nefunkční reference, obsolete serialized fields/metas/bindingy a stale tests; aktivní nahrazené authorities již musejí být pryč z RUN_04/05/06
INVARIANTS=H01-H27 a celý zmrazený kontrakt, bez výjimek
CPU_PROOFS=celý existující oracle/codegen, všechny požadavky §29, persistence crash a export parity
GPU_TESTS=celá CPU/HLSL parity, shader/native ABI compile, synchronizace/publication a runtime integration
SCENE_FIXTURES=všechny fixtures §29 a všechny požadované aplikační flow; nic se neoznačí PASS bez vykonání
PERF_CHECKS=stereo/certificate/binning/commit/compaction/cull/draw; CPU storage/session/submission; konečná paměť, rotation a large-space regresní profil
ACCEPTANCE=FINAL_DAG_AUDIT, FINAL_CONTRACT_AUDIT, FINAL_LEGACY_AUDIT, FINAL_BUILD, FINAL_RUNTIME_FIXTURES skutečně PASS; produkční cutover až po gate §29
ROLLBACK_BOUNDARY=finální opravný/closure run; nevracet legacy jako fallback a nevytvářet RUN_08
```

Jediný závěrečný průchod: CPU/proofs → CPU/HLSL/build → export/app/runtime →
profiling/legacy closure. Potom opravit konkrétní nalezené chyby a zopakovat
jen dotčené kontroly. Odpojený Quest znamená neprovedenou device validaci,
nikoli automatický PASS ani důvod pro další návrhový run.

### Úplné pokrytí původního DAGu

| Původní oblast | Jediný nový implementation owner |
|---|---|
| CUT 0, 1, 2, 14 — authority/oracle/ABI/append foundation | zachovat hotové; jejich integrace v RUN 4–6 |
| CUT 3 — binning/reduction/halo | dokončit současný CUT 3 |
| CUT 4, 5, 6 — stereo, R1, dual | RUN 4 |
| CUT 7, 8, 9 — R2, R3, completion/ghost | RUN 4 |
| CUT 10, 11 — metric/ThreadAtlas/split producer | RUN 4; sdílená draw evaluace v RUN 5 |
| CUT 12, 13 — readout/residency/photoreal consumer | RUN 5 |
| CUT 15 — společný export | RUN 6 |
| CUT 17 — aplikační wiring a UX zachování | RUN 6 |
| CUT 16 — removal/ABI | každý replacement RUN 4/5/6; finální absence v RUN 7 |
| CUT 17 — plná validace a integration fixy | RUN 7 |

### Pokrytí kontraktu a deletion dependencies

| Contract authority | Implementace / odstranění staré cesty |
|---|---|
| §0–1, §3–11: ontology, ABI, geometry, intervaly, R1/R2/R3 | hotové CUT 1/2 + RUN 4; consumers RUN 5/6 |
| §2, §12–16: sparse dual, full support, seed, completion/ghost | RUN 4; CARVE/dilation odstranění v témže runu |
| §0.1/13.4: FULL matter model a DIRT support | RUN 4 excavation/dirty producer; RUN 5 shared boundary readout; RUN 6 stejné export faces/provenance; RUN 7 fixtures; žádný nový run |
| §17–19: bins, observation lifetime a stereo | CUT 3 + RUN 4; winner/queue/square support odstranění v RUN 4 |
| §20–22: fixed thread, Fib(Tparent), independent splits, RGB/V | hotové codegen/ABI + scan RUN 4 + readout RUN 5 + export RUN 6 |
| §23–24: micro-normal/optical a procedural pages | RUN 5; obě mesh readout větve a buffery odstraněny zde |
| §25: SCAN/DRAW/WARM | RUN 5; head-rotation rebuild odstraněn zde |
| §26: dirty append/manifest/open | hotový CUT 14 + producenti RUN 4 a consumer/lifecycle RUN 6 |
| §27: shared export | RUN 6; membrane/shell/repair/float-weld odstraněny zde |
| §28: úplné replacement/excision + ABI | RUN 4/5/6 podle consumeru; finální kontrola absence RUN 7 |
| §29–30: proofy, fixtures a jediná finální cesta | RUN 7; povinnosti všech předchozích ownerů zachovány |

H01 je vlastněn hotovým CUT 0. H02–H14,H20,H22–H27 mají implementačního
ownera v CUT 3/RUN 4 a podle consumeru RUN 5/6. H15–H19,H21,H26,H27 jsou
explicitní povinností RUN 4/5/6. RUN 7 ověřuje všech H01–H27 a celé §29.
Žádný invariant ani delete nemá ownera „později“ mimo tento DAG.

Šest posledních zpřesnění je zachováno: thread-order rank, Fibonacci podle
Tparent, additive V, world frame od runtime L2 evaluatoru, exact
chamber-to-Flower footprint proof a intervalově přesný scan split predicate.

### Výsledek revize DAGu

```text
DAG_REVISION=COMPACT_FOUR_RUNS_2026_09_06
DAG_ACYCLIC=PASS
DAG_CONTRACT_COVERAGE=PASS
DAG_DELETE_DEPENDENCIES=PASS
DAG_AUDIT=PASS (dokumentové pokrytí, nikoli implementace nebo runtime)
NEXT=RUN_04_AFTER_CUT_03_REVIEW_COMMIT
AFTER_CURRENT_CUT=RUN_04 -> RUN_05 -> RUN_06 -> RUN_07
ADDITIONAL_RUN_COUNT=4
PRODUCTION_IMPLEMENTATION_STATUS=IN_PROGRESS
FINAL_CONTRACT_AUDIT=PENDING
FINAL_LEGACY_AUDIT=PENDING
FINAL_BUILD=PENDING
FINAL_RUNTIME_FIXTURES=PENDING
```

## Historical implementation evidence — not an executable DAG

All CUT numbers, CURRENT/NEXT state lines and earlier audit statements below
record past work only. They do not reopen closed foundations, prescribe extra
runs or override the compact four-run DAG above. Resume at the latest cursor
at the end of this file. Preserve past test results without treating them as
validation of newer edits.

## Historical state — after original DAG audit

```text
HEAD remains the exact mandatory base.
Only authority/control documentation exists in the worktree; production behavior is unchanged.
The immutable REV-B prefix still owns the target semantics.
The physical c34d27f source has been mapped across scanner, GPU/native ABI, storage, export and application UX.
All contract invariants and every legacy deletion now have explicit cut ownership.
The topological implementation order is fixed above; CUT_00 is the next executable cut.
```

## CUT 0 closure record

```text
CUT_00_STATUS=PASS
FILES_CHANGED:
  AGENTS.md
  contr.md
  M8-DUAL-SPHERE-FLOWER-CLOSED-PRODUCTION-CONTRACT-REV-B.md(.meta)
  lasttrue.md(.meta)
  Runtime/Merkaba/MerkabaSphereFlowerAuthority.cs(.meta)
  Editor/MerkabaSphereFlowerCodegen.cs(.meta)
  Runtime/Shaders/MerkabaSphereFlower.generated.hlsl(.meta)
  Tests/Editor/MerkabaSphereFlowerBootstrapTests.cs(.meta)

INVARIANTS_PROVEN:
  mandatory base HEAD exact
  immutable contract prefix byte-identical
  a=25mm and L0..L5 frozen
  schema counts 26 directed / 13 undirected / 26 nodes / 72 strands / 48 petals
  new bootstrap has zero production consumer and allocates no resource

TESTS_RUN:
  Tools/unity/run_merkaba_tests.sh = PASS 293/293
  MerkabaSphereFlowerCodegen.CheckForBatch = PASS
  git diff --check = PASS
  immutable-prefix SHA-256 = 75f67ad9080fcbd999ba9ee7f0e30312201cc6dc671112700f403ddd4a309012

PERF:
  production buffers added=0
  production dispatches added=0
  production runtime call sites added=0
  behavioral performance delta=0 by construction

LEGACY_REMOVED:
  contr.md removed as competing authority and replaced with a historical pointer
  no production path was eligible for deletion in this bootstrap cut

DEFERRED_DEPENDENCIES:
  exact oracle/tables=CUT_01
  old canonical production geometry remains only until its replacement CUT_12/CUT_16

CUT_00_MANUAL_AUDIT:
  PASS
  files reviewed=all files listed above
  authority duplication=none (new code is unbound gate skeleton)
  hidden fallback=none
  new buffer/dispatch/property/native pipeline=none
  user-owned .claude and CLAUDE files touched=none

NEXT_CUT=CUT_01 CPU exact oracle + codegen
```

## CURRENT TRUE STATE — CUT 0 closed

```text
The working tree is ready to commit one closed CUT 0.
Production scanner/readout/storage/export behavior remains exactly c34d27f.
REV-B and lasttrue are now the unambiguous repository authority.
The complete DAG remains PASS.
CUT 1 must replace the bootstrap generator with the exhaustive exact CPU oracle before any production GPU cutover.
```

## CUT 1 closure record

```text
CUT_01_STATUS=PASS
CURRENT_COMMIT=HEAD (self; cut 01 exact oracle and codegen)
CURRENT_CUT=CUT_01
DAG_STATUS=CUT_00_PASS;CUT_01_PASS;CUT_02_NEXT;ALL_OTHERS_PENDING

FILES_CHANGED:
  Runtime/Merkaba/MerkabaSphereFlowerAuthority.cs
  Runtime/Merkaba/MerkabaSphereFlower.generated.cs(.meta)
  Runtime/Shaders/MerkabaSphereFlower.generated.hlsl
  Editor/MerkabaSphereFlowerCodegen.cs
  Tests/Editor/MerkabaSphereFlowerOracleTests.cs(.meta)
  Tests/Editor/MerkabaSphereFlowerGpuParityTests.cs(.meta)
  Tests/Editor/MerkabaSphereFlowerOracle.compute(.meta)
  Tests/Editor/MerkabaGpuIntegrationTests.cs
  Tools/shaders/audit_merkaba_compute_spirv.sh

INVARIANTS_PROVEN:
  exact signed J=2K+d identity across negative/tile/chunk/block coordinates
  one algebraic endpoint pair for every valid (J,lineClass)
  exactly 13 undirected line classes and 6/12/8 directed shell nodes
  canonical endpoint-independent loop basis with positive-zero normalization
  symbolic radical-plane sector partition: R1=16, R2=6, R3=12, total=132, maximum=16<=32
  exact root degeneracies including dyadic 3/4/5 tangency without tolerance
  outward ABC/interval containment and flat shared-loop SEAL compatibility
  exactly 26 nodes / 72 strands / 48 flag petals
  signed petal boundary incidence cancels identically on every shared strand
  deterministic determinant winding and four-child address substitution
  R2 tangent-half-angle analysis/synthesis and sector-preserving representation
  all eight generated R3 tetra frames, forward/inverse transform and determinant chirality
  homogeneous hinge position and distinct relation identity for parallel owners
  V endpoint value/tangent invariance, representability, nonzero tangent and angular order
  generated CPU/HLSL tables carry the same frozen 32-bit hash
  generated player tables load without editor codegen or runtime adjacency construction

TESTS_RUN:
  Tools/unity/run_merkaba_tests.sh = PASS 311/311
  filtered Sphere-Flower suite = PASS 21/21
  GPU table/algebra parity fixture = PASS 1/1 over all frozen table rows and adversarial primitives
  MerkabaSphereFlowerCodegen.CheckForBatch = PASS byte-for-byte for generated C# and HLSL
  Tools/shaders/audit_merkaba_compute_spirv.sh = PASS 58/58 Vulkan kernels
  oracle SPIR-V local size=64; float64 path=absent; NoContraction gate=PASS
  fresh Quest APK = PASS, 58,792,993 bytes, SHA-256 79c9c3bd15864bb710efcc3cf5cc4e986008a414561fb14e2e2216acab040564
  git diff --check = PASS
  immutable REV-B prefix SHA-256 = 75f67ad9080fcbd999ba9ee7f0e30312201cc6dc671112700f403ddd4a309012

PERF:
  generated HLSL=33,438 bytes (<64KiB table/code target)
  generated player C# literals=37,191 bytes
  evaluator hot primitives allocate zero managed heap objects
  runtime adjacency/root search=0
  production buffers added=0
  production dispatches added=0
  production call sites added=0
  table parity scratch exists under Tests/Editor only

LEGACY_REMOVED:
  none eligible before the CPU oracle gate; current scanner/readout behavior remains c34d27f

DEFERRED_DEPENDENCIES:
  old MerkabaCanonicalGeometry surface-output authority remains until procedural readout CUT_12/CUT_16
  production scanner/winner/carve/readout branches remain until their explicit replacement cuts

CUT_01_MANUAL_AUDIT:
  PASS
  files reviewed=all files listed above plus generated artifacts and SPIR-V disassembly gate
  authority duplication=none; exact oracle has no production consumer yet
  hidden fallback=none
  runtime adjacency search=none in player initialization; player uses generated literals
  dynamic branch rank=absent
  magic tolerance/nearest-root/inverse-trig production path=absent
  signed incidence omission found and corrected before closure
  CPU BigInteger removed from evaluator hot primitive; retained only by editor symbolic generation
  new production buffer/dispatch/property/native pipeline=none
  user-owned .claude and CLAUDE files touched=none

NEXT_CUT=CUT_02 exact subordinate data model and persistence record ABI
```

## CURRENT TRUE STATE — CUT 1 closed

```text
CUT 0 and CUT 1 are closed.
The immutable REV-B prefix remains byte-identical and authoritative.
The exact finite Sphere-Flower CPU oracle, generated player literals and generated HLSL are reproducible and parity-proven.
No production scanner/readout/storage/export behavior has switched yet.
No legacy production path is eligible for deletion until its named replacement cut.
CUT 2 must now introduce only the exact subordinate packed data types and record ABI; it must not create a second spatial or surface authority.
```

## CUT 2 closure record

```text
CUT_02_STATUS=PASS
CURRENT_COMMIT=HEAD (self; cut 02 exact subordinate data model and persistence record ABI)
CURRENT_CUT=CUT_02
DAG_STATUS=CUT_00_PASS;CUT_01_PASS;CUT_02_PASS;CUT_14_NEXT;ALL_OTHERS_PENDING

FILES_CHANGED:
  Runtime/Merkaba/KernelState.cs
  Runtime/Merkaba/MerkabaConstants.cs
  Runtime/Merkaba/MerkabaGrid.Gpu.cs
  Runtime/Merkaba/MerkabaDualVisibility.cs(.meta)
  Runtime/Merkaba/MerkabaFlowerDetail.cs(.meta)
  Runtime/Merkaba/MerkabaThreadAtlas.cs(.meta)
  Runtime/Merkaba/MerkabaSphereFlowerDataAbi.cs(.meta)
  Runtime/Merkaba/MerkabaSphereFlowerPersistenceAbi.cs(.meta)
  Runtime/Shaders/MerkabaSphereFlowerDataAbi.generated.hlsl(.meta)
  Runtime/Telemetry/Native/MerkabaSphereFlowerDataAbi.h(.meta)
  Runtime/Telemetry/Native/MerkabaVulkanTimestamps.cpp
  Editor/MerkabaSphereFlowerCodegen.cs
  Tests/Editor/MerkabaSphereFlowerDataAbiTests.cs(.meta)
  Tests/Editor/MerkabaSphereFlowerDataAbi.compute(.meta)
  Tests/Editor/MerkabaGpuIntegrationTests.cs
  Tools/shaders/audit_merkaba_compute_spirv.sh

INVARIANTS_PROVEN:
  KernelState remains exactly 16 bytes with unchanged production semantics
  future R1_SEED bit is frozen at bit 1 but has zero CUT 02 consumers
  dual node state is exactly two bits; missing world state remains implicit ALL_FULL
  MIXED block children are exactly 512 two-bit states in 128 bytes
  MIXED chunk summary is exactly 32 bytes with MixedMask subset of NonFullMask
  MIXED tile leaf is exactly 512 SEE_THROUGH bits in 64 bytes
  uniform expand/mutate/collapse is exact for block/chunk/tile levels
  cold/unresolved dual state is distinct from every persistent node state
  dual generation wrap requires transactional rebase rather than aliasing
  sparse FlowerOwnerEpoch is exactly 8 bytes and epoch zero never validates detail
  owner epoch wrap requires transactional tombstone/rebase to epoch 1
  FlowerDetailRecord is exactly 16 bytes and all 32 key bits have one canonical meaning
  childPath has one canonical 4-ary encoding for its declared L0-L5 level
  Q2.29 phase and Q5.26 metre intervals round outward and never clamp
  ThreadRun is 16 bytes, ThreadResidual 32 bytes and ThreadProgram 48 bytes
  CPU half4 layout is bit-identical to the generated uint2 HLSL/native ABI
  transient FlowerSymbolKey and ObservationRecord are each exactly 16 bytes
  no branch ordinal, world PortKey or second coordinate hierarchy was introduced
  append RecordHeader is exactly 28-byte little-endian and CRC-binds metadata/address/payload
  every record kind has one exact address/payload shape using existing M8 addresses
  owner-local tombstone is exactly 8 bytes and targets only subordinate fine records
  stale FlowerDetail/Thread records are rejected solely by exact parent epoch equality
  C#, generated HLSL and native C++ mirrors agree on every packed stride/offset

TESTS_RUN:
  Tools/unity/run_merkaba_tests.sh = PASS 326/326
  filtered CUT 02 ABI suite = PASS 15/15 including live Vulkan buffer roundtrip
  MerkabaSphereFlowerCodegen.CheckForBatch = PASS byte-for-byte for C#/HLSL/native outputs
  Tools/shaders/audit_merkaba_compute_spirv.sh = PASS 59/59 Vulkan kernels
  VerifySphereFlowerDataAbi SPIR-V = 11 readonly buffers + 1 writable test sink
  native Android build static_assert gate = PASS
  fresh Quest APK = PASS, 58,811,365 bytes, SHA-256 1b604e1b409209e14357138ce42ad8661bfad4be9b09d5b3b3f664029403d055
  git diff --check = PASS
  immutable REV-B prefix SHA-256 = 75f67ad9080fcbd999ba9ee7f0e30312201cc6dc671112700f403ddd4a309012

PERF:
  production buffers added=0
  production dispatches added=0
  production executor resources added=0; ResourceCount remains 45
  production executor pipelines added=0; PipelineCount remains 49
  behavioral performance delta=0 by construction
  future dual worst-case packed bounds: block headers=64KiB; block children=1MiB; chunk summaries=8MiB; HOT leaves=2MiB
  sparse fine overhead: owner epoch=8B only for owners with fine truth; detail=16B; thread run=16B; residual=32B; program=48B

LEGACY_REMOVED:
  none eligible in a production-behavior-neutral ABI/schema cut

DEFERRED_DEPENDENCIES:
  existing NeedsCarve semantics remain untouched through CUT 02; its flag dependency and bit meaning are atomically replaced in CUT 05
  remaining legacy CARVE/dilation implementation is removed in CUT 06
  new append record ABI is unbound until transactional persistence CUT 14
  sparse dual GPU allocation/binding waits for its certificate consumer in CUT 06
  FlowerDetail/ThreadAtlas allocation waits for CUT 10/CUT 11

CUT_02_MANUAL_AUDIT:
  PASS
  files reviewed=all files listed above plus generated output and native build artifact
  authority duplication=none; every new persistent fine record is parent-addressed and epoch-gated
  second coordinate hierarchy=absent; persistence codecs use MerkabaTileAddress/MerkabaSpatial only
  hidden fallback=none
  new production buffer/resource/dispatch/property/native pipeline=none
  dynamic branch rank/persistent PortKey=absent
  unsafe uniform/cold dual promotion=absent
  invalid/truncated record acceptance=absent
  user-owned .claude and CLAUDE files touched=none

NEXT_CUT=CUT_14 generation-bound dirty append SAVE/OPEN foundation
```

## CURRENT TRUE STATE — CUT 2 closed

```text
CUT 0, CUT 1 and CUT 2 are closed.
The immutable REV-B prefix remains byte-identical and authoritative.
The complete subordinate dual/detail/thread/transient/persistence ABI is exact across C#, HLSL and native C++.
No new data resource is allocated and production scanner/readout/storage/export behavior still follows c34d27f.
The old whole-snapshot persistence path is now the next authority eligible for replacement in CUT 14.
```

## CUT 14 closure record

```text
CUT_14_STATUS=PASS
CURRENT_COMMIT=HEAD (self; cut 14 transactional persistence)
CURRENT_CUT=CUT_14
DAG_STATUS=CUT_00_PASS;CUT_01_PASS;CUT_02_PASS;CUT_14_PASS;CUT_03_NEXT;ALL_OTHERS_PENDING

FILES_CHANGED:
  Runtime/Merkaba/MerkabaSessionManifest.cs(.meta)
  Runtime/Merkaba/MerkabaSphereFlowerReplayIndex.cs(.meta)
  Runtime/Merkaba/MerkabaSsdStore.cs
  Runtime/Merkaba/MerkabaGrid.Storage.cs
  Runtime/Merkaba/MerkabaPersistence.cs
  Runtime/Merkaba/MerkabaSessionCatalog.cs
  Runtime/Merkaba/MerkabaSphereFlowerPersistenceAbi.cs
  Runtime/Merkaba/MerkabaTileState.cs
  Runtime/Merkaba/MerkabaExportShell.cs
  Runtime/Merkaba/MerkabaGridRenderer.cs
  Runtime/Core/RoomScanner.cs
  Tests/Editor/MerkabaPersistenceTests.cs
  Tests/Editor/MerkabaSessionCatalogTests.cs
  Tests/Editor/MerkabaLifecycleProgressTests.cs
  Tests/Editor/MerkabaExportShellTests.cs
  Tests/Editor/DepthPreprocessTests.cs

INVARIANTS_PROVEN:
  session-manifest.bin is the sole published session transaction authority
  M8, sparse dual, FlowerDetail and ThreadAtlas have separate append streams and one commit generation
  every manifest records the exact valid byte end of all six streams
  dirty streams are durably flushed once before the manifest is durably written and atomically renamed
  parent-directory fsync follows every authoritative rename on Android/Linux
  OPEN reads and validates the manifest before replay, ignores/truncates every excluded tail and rejects corrupt ranges
  base/live generation boundaries and record CRC/address/payload shapes are fail-closed
  sparse dual ancestor records supersede older descendants and implicit ALL_FULL requires no stored record
  FlowerDetail and ThreadAtlas records whose sparse parent epoch is absent, stale or lacks a canonical R1 owner are rejected
  compaction publishes a complete recovery live image before replacing either base and preserves truth originally present only in base
  SAVE AS clones exact committed prefixes and changes only session identity; an existing root cannot be relabeled
  session metadata, manifest session UUID and anchor UUID must agree before world adoption
  candidate OPEN is fully replayed before anchor localization and GPU-world retirement
  SCAN resume no longer waits for DRAW/WARM readout coverage
  CPU storage task completion/accounting executes before the native-GPU in-flight gate
  SAVE requests idle compaction but does not await or perform a whole-world rewrite

TESTS_RUN:
  filtered MerkabaPersistenceTests = PASS 48/48
  Tools/unity/run_merkaba_tests.sh = PASS 364/364
  MerkabaSphereFlowerCodegen.CheckForBatch = PASS byte-for-byte
  Tools/shaders/audit_merkaba_compute_spirv.sh = PASS 59/59 Vulkan kernels
  fresh Quest APK build = PASS, 59,026,337 bytes, SHA-256 1b15e5b91544afc16e036e579d534e3f37644469c860998e51318e83428c872d
  git diff --check = PASS
  immutable REV-B prefix SHA-256 = 75f67ad9080fcbd999ba9ee7f0e30312201cc6dc671112700f403ddd4a309012
  device runtime acceptance = NOT RUN (Quest disconnected by user)

PERF:
  SAVE performs O(dirty M8/subordinate append) work plus six fixed stream flush checks and one manifest publish
  SAVE performs no canonical tile-index traversal, complete-world readback or checkpoint rewrite
  idle base compaction is independently scheduled, cancellable and never part of SAVE completion
  CPU storage completions/rates/idle maintenance progress before native GPU availability is tested
  OPEN registers only the committed logical M8 address index; DRAW/WARM population remains independent

LEGACY_REMOVED:
  MerkabaSessionSnapshot
  CaptureStoredSnapshotAsync and ReadCanonicalSnapshotAsync
  WriteCheckpoint/ReadCheckpoint and whole-world PublishCheckpoint SAVE path
  merkaba-grid.bin session authority and legacy checkpoint migration/recovery alias
  durable fsync on every tiny write-through batch
  loaded-coverage/DRAW-WARM wait from SCAN resume

DEFERRED_DEPENDENCIES:
  production sparse SEE_THROUGH mutation and GPU residency=CUT_06
  production FlowerDetail and ThreadAtlas writers=CUT_10/CUT_11
  legacy winner/integration path=CUT_03/CUT_05
  legacy dilation/CARVE path=CUT_06
  legacy readout and renderer geometry=CUT_12/CUT_13
  legacy shell/membrane/float-weld export=CUT_15
  physical stale fine-log compaction follows the production fine writers in CUT_10/CUT_11
  on-device runtime/performance acceptance remains pending until the user reconnects Quest

CUT_14_MANUAL_AUDIT:
  PASS
  files reviewed=every production and test file listed above, including complete new manifest/replay sources
  legacy snapshot/checkpoint symbols in Runtime=absent
  session/anchor relabel fallback=absent
  hidden compatibility reader/migration=absent
  SAVE whole-world traversal/rewrite=absent
  orphan subordinate acceptance=absent
  manifest-ignored tail authority=absent
  CPU storage completion blocked by native GPU job=absent
  readout wait blocking SCAN resume=absent
  user-owned .claude and CLAUDE files touched=none

NEXT_CUT=CUT_03 deterministic observation reduction
```

## CURRENT TRUE STATE — CUT 14 closed

```text
CUT 0, CUT 1, CUT 2 and CUT 14 are closed.
REV-B remains the byte-identical sole authority and the dependency DAG remains PASS.
The old snapshot/checkpoint persistence authority is absent; manifest-bound dirty append/replay is the only session storage path.
Production scanner, stereo, positive integration, negative-volume mutation, detail, RGBV, procedural readout and export have not yet cut over and their named legacy paths remain only until CUT 3-13 and CUT 15.
No photorealistic procedural Sphere-Flower readout is claimed at this state.
The next implementation cut is CUT 3: touched-tile ObservationRecords and deterministic one-workgroup-per-tile reduction.
```

## REV-C authority transition and DAG delta audit

```text
AUTHORITY_TRANSITION=REV-B -> REV-C
REV_C_CONTRACT_FILE=M8-DUAL-SPHERE-FLOWER-CLOSED-PRODUCTION-CONTRACT-REV-C.md
REV_C_CONTRACT_BYTES=86895
REV_C_CONTRACT_SHA256=a49c511126750cd46cde2fa06f12d27410c94a3f47c50c58114316261fae9c0b
REV_C_IMMUTABLE_PREFIX_CMP=PASS
OLDER_CONTRACT_AUTHORITY=INVALIDATED

REV_C_DELTA_OWNERS:
  exact Flower-7 chambers, fixed 399 thread and FibBit(Tparent)=CUT_01
  fixed-thread split/run/group ABI=CUT_02
  immutable observation retention across compute quanta=CUT_03
  exact metric split predicate and additive V workset drain=CUT_10
  exact RGB split predicate and derived RGB/V union=CUT_11
  forbidden validDepth/runtime-Fibonacci/observationOrdinal audit=CUT_16
  stationary/eager/multi-quantum end-to-end closure=CUT_17

REV_C_OWNERLESS_INVARIANTS=0
REV_C_DELETE_DEPENDENCY_CHANGES=none
REV_C_PARALLEL_AUTHORITY_CREATED=none
REV_C_ONTOLOGY_CHANGE_FROM_CONTRACT=none
REV_C_DAG_ACYCLIC=PASS
REV_C_DAG_AUDIT=PASS

Previously closed CUT_00 and CUT_14 remain semantically valid under revised REV-C.
CUT_01 and CUT_02 are reopened for the fixed-thread oracle/codegen and ABI obligations. Both must pass before CUT_03 production changes begin.
```

## CURRENT TRUE STATE — REV-C gate in progress

```text
REV-C is the byte-identical sole production contract at the start of this file.
The mandatory ancestry remains c34d27f0ecb51500b12209ed5d2fe72b893726f5 and HEAD remains 0c59ff120610c6f38d6afbc347bc4f20f8901f58 before the REV-C transition commit.
CUT 0 and CUT 14 remain closed. CUT 1 and CUT 2 are reopened for the fixed-thread proof/codegen and ABI replacement.
No CUT 3 production integration mutation has begun.
Current implementation work is the allocation-free fixed 399-position thread, exact chamber maps, thread-order compact rank, compile-time FibBit(Tparent), additive V basis and CPU/HLSL proof gate.
No photorealistic procedural Sphere-Flower readout is claimed at this state.
Quest runtime acceptance remains NOT RUN because the device is disconnected by the user.
```

## REV-C fixed-thread closure audit

```text
REVIEW_FIX_1_THREAD_ORDER_RANK=INCORPORATED
  j3=r3(c3); j4=7*r3(c3)+r4(state3,c4)
  SplitL3Thread/SplitL4Thread and rank7/rank49 share Gamma physical order
REVIEW_FIX_2_FIBONACCI_PARENT_THREAD_ORDINAL=INCORPORATED
  FibBit(Tparent) is compile-time only; canonical q indices are forbidden
REVIEW_FIX_3_ADDITIVE_NESTED_V=INCORPORATED
  V=A3*psi3+A4*psi4+A5*psi5; child innovation never replaces parent
REVIEW_FIX_4_RUNTIME_L2_WORLD_FRAME=INCORPORATED
  codegen owns convention/tables; evaluated scanned L2 carrier owns O,T1,T2,N
REVIEW_FIX_5_EXACT_FLOWER7_FOOTPRINT_PROOF=INCORPORATED
  chamber partition plus per-child union equality required at all three recursions
REVIEW_FIX_6_EXACT_INTERVAL_SPLIT_PREDICATE=INCORPORATED
  seven CERTAIN footprint intervals plus a provably disjoint child pair; no epsilon/variance
VALID_DEPTH_AUTHORITY=FORBIDDEN
L3_L5_GEOMETRY_AUTHORITY=FORBIDDEN
DAG_OWNERLESS_INVARIANTS=0
DAG_DELETE_DEPENDENCIES=PASS
DAG_PARALLEL_AUTHORITY=PASS
DAG_ACYCLIC=PASS
DAG_AUDIT=PASS
```

## CURRENT TRUE STATE — REV-C CUT 01 closed

```text
CURRENT_COMMIT=0c59ff120610c6f38d6afbc347bc4f20f8901f58 (pre-CUT-01-reclosure)
CURRENT_CUT=CUT_01 PASS
DAG_STATUS=PASS
AUTHORITY=M8-DUAL-SPHERE-FLOWER-CLOSED-PRODUCTION-CONTRACT-REV-C.md
AUTHORITY_BYTES=86895
AUTHORITY_SHA256=a49c511126750cd46cde2fa06f12d27410c94a3f47c50c58114316261fae9c0b
AUTHORITY_PREFIX_CMP=PASS

FILES_CHANGED:
  Editor/MerkabaSphereFlowerCodegen.cs
  Runtime/Merkaba/MerkabaSphereFlowerAuthority.cs
  Runtime/Merkaba/MerkabaSphereFlowerSkin.cs
  Runtime/Merkaba/MerkabaSphereFlower.generated.cs
  Runtime/Shaders/MerkabaSphereFlower.generated.hlsl
  Tests/Editor/MerkabaSphereFlowerBootstrapTests.cs
  Tests/Editor/MerkabaSphereFlowerGpuParityTests.cs
  Tests/Editor/MerkabaSphereFlowerOracle.compute
  Tests/Editor/MerkabaSphereFlowerOracleTests.cs
  Tests/Editor/MerkabaSphereFlowerSkinTests.cs
  authority/bootstrap documents listed by git diff

INVARIANTS_PROVEN:
  world Sphere-Flower geometry terminates at L2
  every L2 has one immutable 399-position L3-L5 logical thread
  canonical radix-7 identity and Gamma thread order are bijective
  every L3 subtree is exactly 57 contiguous positions
  every L4 subtree is exactly 8 contiguous positions
  generated stitch transitions are Flower-adjacent
  FibBit uses actual Tparent and exists only in editor codegen
  player loads baked immutable stitch/address tables
  exact ordered chamber descent uses stable generated-site tie ordering
  chamber unions reproduce all seven generated child footprints recursively
  split masks/ranks use thread-parent order
  scalar/RGB split predicates require seven CERTAIN intervals and strict disjointness
  V evaluation is additive across L3/L4/L5 bubble innovations
  no validDepth, drawDepth, deepestValid or runtime Fibonacci path exists in CUT-01 output

TESTS_RUN:
  full EditMode suite: 369/369 PASS
    /mnt/kingston-unity/Builds/TestResults/merkaba-results.xml
  hardened skin plus CPU/HLSL GPU parity: 5/5 PASS
    /mnt/kingston-unity/Builds/TestResults/revc-cut1-hardened.xml
  Quest SPIR-V validation: 59/59 PASS
    /mnt/kingston-unity/Builds/TestResults/revc-cut1-spirv-hardened.log
  Android player/APK compile: PASS
    /mnt/kingston-unity/Builds/TestResults/revc-cut1-apk-hardened.log
    /mnt/kingston-unity/Builds/QuestMerkabaScan/QuestMerkabaScan-release.apk

PERF:
  generated Sphere-Flower HLSL=54293 bytes (<64 KiB topology/table target)
  player runtime performs no Fibonacci generation
  proof readback exists only in EditMode CPU/HLSL parity tests
  production behavior remains unchanged in this oracle/codegen cut

LEGACY_REMOVED:
  REV-B L3-L5 world-loop/deformed-loop oracle semantics
  EndpointBasis/DeformedLoop generated CPU/HLSL helpers

DEFERRED_DEPENDENCIES:
  old ThreadResidual/FibonacciRouteOrigin ABI is owned by reopened CUT_02 and is the next removal
  production legacy scanner/readout kernels remain dependency-deferred to their named replacement cuts

CUT_01_MANUAL_AUDIT:
  PASS
  files reviewed=all changed non-generated oracle/codegen/test files plus generated C#/HLSL
  hidden runtime Fibonacci=absent
  validDepth/read-depth authority=absent
  alternate L3-L5 geometry authority=absent
  generated/static world L2 frame=absent
  remaining deferred dependencies=CUT_02 ABI only

QUEST_DEVICE_RUNTIME=NOT RUN (device disconnected by user)
NEXT_CUT=CUT_02 ABI/data model fixed-thread replacement
```

## CURRENT TRUE STATE — REV-C CUT 02 closed

```text
CURRENT_COMMIT=3d78451c472688cbdc8a03466f5d097b888ed543 (pre-CUT-02-reclosure)
CURRENT_CUT=CUT_02 PASS
DAG_STATUS=CUT_00_PASS;CUT_01_PASS;CUT_02_PASS;CUT_14_PASS;CUT_03_NEXT
AUTHORITY_PREFIX_CMP=PASS (86895 bytes; frozen REV-C unchanged)
FILES_CHANGED:
  Runtime/Merkaba/MerkabaFlowerDetail.cs
  Runtime/Merkaba/MerkabaThreadAtlas.cs
  Runtime/Merkaba/MerkabaSphereFlowerDataAbi.cs
  Runtime/Merkaba/MerkabaSphereFlowerPersistenceAbi.cs
  Runtime/Merkaba/MerkabaSphereFlowerReplayIndex.cs
  Runtime/Merkaba/MerkabaSsdStore.cs
  Runtime/Merkaba/MerkabaGrid.Gpu.cs
  Editor/MerkabaSphereFlowerCodegen.cs
  Runtime/Shaders/MerkabaSphereFlower.generated.hlsl
  Runtime/Shaders/MerkabaSphereFlowerDataAbi.generated.hlsl
  Runtime/Telemetry/Native/MerkabaSphereFlowerDataAbi.h
  Tests/Editor/MerkabaPersistenceTests.cs
  Tests/Editor/MerkabaSphereFlowerDataAbi.compute
  Tests/Editor/MerkabaSphereFlowerDataAbiTests.cs
  Tests/Editor/MerkabaSphereFlowerGpuParityTests.cs
  Tests/Editor/MerkabaSphereFlowerOracle.compute
  Tests/Editor/MerkabaGpuIntegrationTests.cs
  Tools/shaders/audit_merkaba_compute_spirv.sh
INVARIANTS_PROVEN:
  exact L0-L2 metric keys; independent 57-bit thread-order RGB/V masks
  sparse seven-child groups; no validDepth or dense 399-value requirement
  Q2.29 phase and Q5.26 V intervals; deterministic interval midpoints
  owner epoch rejects stale RGB/V; optical programs contain no runtime Fibonacci
  replay requires every live run's groups from its own commit generation
  another run cannot make an overwritten group valid for an older run
  wrapped group ranges rejected before replay; generation-bound append/open intact
TESTS_RUN:
  full Vulkan EditMode: 376/376 PASS
    /mnt/kingston-unity/Builds/TestResults/merkaba-results.xml
  replay regression: 53/53 PASS
    /mnt/kingston-unity/Builds/TestResults/revc-cut2-replay-owner-10.xml
  Quest SPIR-V: 60/60 PASS
    /mnt/kingston-unity/Builds/TestResults/revc-cut2-spirv-2.log
  fresh Android APK build: PASS
    /mnt/kingston-unity/Builds/QuestMerkabaScan/build.log
  git diff --check: PASS
PERF:
  KernelState=16B; detail=16B; metric/RGB run=24B; V group=56B; RGB group=112B
  group reachability examines only the affected owner's index, never all owners
  no new production GPU allocation or dispatch in this ABI cut
LEGACY_REMOVED=ThreadResidual; VAmplitude detail kind; old ThreadProgram endpoint/residual/FibonacciRouteOrigin fields
CUT_02_MANUAL_AUDIT=PASS (changed production/data/codegen files and generated ABI reviewed)
CONTRACT_AUDIT=PASS
DEFERRED_DEPENDENCIES=production observation/scanner consumers CUT_03-11; procedural readout CUT_12; export CUT_15; final native ABI excision CUT_16
QUEST_DEVICE_RUNTIME=NOT RUN (device disconnected by user)
NEXT_CUT=CUT_03 deterministic GPU observation bins and reduction
```

## CURRENT TRUE STATE — CUT 03 implementation cursor

```text
CURRENT_COMMIT=d5551bf (CUT_02 closed)
CURRENT_CUT=CUT_03 IN_PROGRESS; not closed and not committed
IMPLEMENTED:
  Runtime/Shaders/MerkabaObservationBins.compute: one 256-lane HOT-slot prefix/reservation dispatch; stable physical-slot spans; exactly one indirect group per touched tile; fail-closed bounded/saturating capacity arithmetic
  Runtime/Shaders/MerkabaObservationBins.hlsl: resolved-owner atomic count and bounded 16-byte record emission; no geometry decision
  Runtime/Shaders/MerkabaObservationReduction.hlsl: 512 owner buckets; exact signed interval intersection and symbolic conflict before representative selection; 10 KiB groupshared, no world winner buffer
  signed endpoint atomics use monotone sign-bit-biased uint encoding, verified across negative/positive interval conflicts
  record/source capacities emitted from the managed ABI authority
TESTS_RUN=3/3 Vulkan tests PASS: /mnt/kingston-unity/Builds/TestResults/cut3-bins-3.xml
SPIRV=64/64 PASS via Tools/shaders/audit_merkaba_compute_spirv.sh
MANUAL_AUDIT=written shaders reviewed; latest targeted run has no shader warnings; git diff --check PASS
PRODUCTION_CUTOVER=NOT DONE; scanner remains unchanged pending CUT_03-05 dependencies
REMAINING_CUT_03:
  arithmetic eight-owner enumeration, unique logical root-tile resolution and allocation retries
  generated root/sector scratch feeding the GPU reducer
  slot-generation-validated TileHalo and touched-only bin lifecycle
  Integrator/native bindings and immutable-observation refinement lifetime
  full cut closure, dependent legacy removals and one cut commit
CURSOR:
  updated by the later cursor below; do not restart completed owner/bin work
  do not restart CUT_02, re-read historical contracts, or repeat the model-comparison answer
QUEST_DEVICE_RUNTIME=NOT RUN (user-disconnected)
```

## CURRENT TRUE STATE — CUT 03 runtime cursor, 2026-09-06

```text
CURRENT_COMMIT=d5551bf; CURRENT_CUT=CUT_03 IN_PROGRESS; no new closed-cut commit
IMPLEMENTED:
  generated arithmetic eight-owner/boundary masks; one lookup per distinct block/chunk/tile
  GPU CountObservationBins/EmitObservationBins and deduplicated EMPTY/COLD tile requests
  allocation-incomplete reservation emits zero commit groups; touched list survives failure/retry
  ResetObservationBins retires only touched bins and retains fatal diagnostics
  generated interval ABC/root/sector evaluator; sqrt/division enclosure certified by exact dyadic comparison
  CPU ABC central values now use explicit ordered binary32 steps matching precise HLSL, not library dot order
  MerkabaObservationBinsGpu runtime bindings: one borrowed 32 MiB record arena plus 512 KiB bin metadata
  same immutable field/token retained across retries; indirect commit; no geometry readback
  native executor ABI=2, resources=47, pipelines=54; ObservationBins job on the existing serialized queue
  frozen native uniform payload serialized once and reused across quanta
EVIDENCE:
  10/10 targeted Vulkan tests PASS: /mnt/kingston-unity/Builds/TestResults/cut3-roots-3.xml
  72/72 SPIR-V PASS: /mnt/kingston-unity/Builds/TestResults/cut3-roots-spirv.log
  Android native plugin build PASS: /mnt/kingston-unity/Builds/TestResults/cut3-native-build.log
  runtime C# compilation + exact codegen check PASS: /mnt/kingston-unity/Builds/TestResults/cut3-runtime-compile.log
  last two native uniform snapshot-cache fields added after that compile; full cut closure still pending
  shader compiler emitted potential-uninitialized warnings in new probes/inlined helpers; review before cut closure
MANUAL_AUDIT=IN_PROGRESS; reviewed changed expressions, bin lifetime, new runtime bindings, native pipeline ranges
PRODUCTION_CUTOVER=NOT DONE; no new positive surface authority is enabled
LEGACY_REMOVED=none in CUT_03 yet; winner/queue/old R1 execution explicitly deferred to CUT_05
REMAINING:
  generated roots into final owner/symbol reducer; deterministic PrecisionKey construction
  generation-validated TileHalo
  Integrator activation/lifetime and fused allocation publication, with old R1 replacement in CUT_05
  full cut manual audit/closure; do not call this cut PASS yet
RECOVERY_CURSOR:
  continue runtime reduction and Integrator wiring, not another contract rewrite or test framework
  runtime controller: Runtime/Merkaba/MerkabaObservationBinsGpu.cs
  HLSL: Runtime/Shaders/MerkabaObservationBins.compute, Owners.hlsl, Reduction.hlsl
  native ABI/range edits: Runtime/Telemetry/MerkabaNativeVulkanExecutor.cs;
    Runtime/Telemetry/Native/MerkabaVulkanTimestamps.cpp;
    Tools/unity/generate_merkaba_native_executor_shaders.py
  ROOT INTERVAL FAIL in cut3-roots-1 was an invalid sector-midpoint fixture; corrected angular fixture covers all 132 sectors
  PLANE FAIL in cut3-roots-2 was CPU library dot order; corrected CPU ordered operations, no epsilon widening
  contract immutable prefix unchanged; Fibonacci/399-position thread untouched
QUEST_DEVICE_RUNTIME=NOT RUN (user-disconnected)
NEXT_CUT=CUT_03 until closure; CUT_04 only afterwards
```

## CURRENT TRUE STATE — CUT 03 implementation handoff, compact DAG

```text
CURRENT_COMMIT=HEAD; parent before CUT 03 commit=d5551bfba0012c2e72a7071f38d023909bcb23c9
CURRENT_CUT=CUT_03 IMPLEMENTED_FOR_RUN_04
CUT_03_VALIDATION=DEFERRED_TO_RUN_07 by explicit user execution instruction; NOT a new runtime/proof PASS
DAG_STATUS=foundations CUT_00/01/02/14 PASS; CUT_03 implementation done; RUN_04/05/06/07 pending
NEXT=RUN_04 complete scanner; do not reopen completed CUT 3 substrate or recreate the 18-cut DAG
```

Implemented substrate:
- Arithmetic eight-owner Count, stable touched-HOT prefix Reserve, bounded
  Emit and one GPU-indirect future FlowerCommit group per touched tile.
- Generated bounded ABC/root/sector evaluator; both root coordinates
  intersect before class/normalized-width/source-pixel representative
  selection; conflicting/ambiguous buckets cannot select a surface.
  Signed zero shares one interval key; tangent root has one canonical entry.
- Complete storage publication sequence: Count claims, fused publication
  of Block/Chunk nodes, deduplicated EMPTY/COLD requests, pending HOT
  reservation and existing tile initialization, Reserve/Emit. Missing owners
  leave commit count zero and require recount of the same frozen field.
- Managed Record() and native ObservationBins job record the same sequence.
  Frozen input/calibration and serialized uniform payload survive retries;
  no camera timer or geometry readback is added by this substrate.
- 27 packed TileHalo refs per HOT tile (3.375 MiB), cooperative cache entry,
  validation of slot generation, live spatial ref and exact logical owner.
  Slot generation uses existing TileRuntime.w, not KernelState or fine epochs.
- Native ABI 2: 48 resources, 56 pipeline entries; still one serialized queue.
  Bins own 512 KiB metadata and borrow the existing single 32 MiB record arena.

Files changed are the observation shader/controller set, World slot lifecycle,
Grid.Gpu halo resource, shared CPU/codegen/generated math, native executor/
shader generator and the existing related tests/binding manifests. No
production sensor/R1/dual/readout switch is claimed here.

Build evidence:
- Native HLSL embedding and Android plugin compilation completed successfully,
  all 56 embedded pipeline entries built. Tool output is in this session.
- C# import/compile: `/mnt/kingston-unity/Builds/cut3-final-compile.log`.
- Earlier 10/10 Vulkan and 72/72 SPIR-V results remain historical evidence,
  not validation of these later edits. No tests/benchmarks executed during
  this implementation handoff. Device acceptance remains NOT RUN.

Legacy dependency:
- Old positive winner/queue, stereo and CARVE consumers remain only until the
  single RUN 4 replacement; they are not new fallback branches. Consumer
  activation, FinalizeObservation workset exhaustion and complete scanner
  lease release belong together to RUN 4, as explicitly bounded in the DAG.
- Readout removal is RUN 5; export repair removal is RUN 6.
- Changed storage publication is free of root/surface decisions.

Recovery cursor:
- Start RUN 4 at `StereoRgbdRefine.compute` / `DepthCapture` and
  `MerkabaIntegrator`; use existing CPU/codegen/data/append foundations.
- New backend entry: `MerkabaObservationBinsGpu.Record` or
  `TryCreateNativeJob`. Its input requires categorical accepted normal.w=1;
  it is deliberately not called with legacy confidence-valued stereo output.
- Do not change fixed-399/Fibonacci/57-bit/thread-order/additive-V semantics.
- DAG review copy: `/home/wraith/Stažené/M8-DAG-4-RUNY-K-REVIZI.md`.
- User requested commit + GitHub push of this CUT 3 handoff and the amended DAG.

## CURRENT TRUE STATE — RUN 04 production cursor, 2026-09-06

```text
CURRENT_COMMIT=70c9f43fa37f3e70b38072cfc12615af291abc87
CURRENT_CUT=RUN_04 IN_PROGRESS; not closed, not committed
DAG_STATUS=CUT_03 handoff committed/pushed; RUN_04 active; RUN_05/06/07 pending
TESTS_RUN=NONE during this implementation; final tests/benchmarks remain RUN_07
MANUAL_AUDIT=NOT a closure PASS
```

Implemented since CUT 03:
- Replaced the stereo kernel with `StereoFlowerRefine`, connected through
  DepthCapture, Integrator and native pipeline/reflection. Preserved both
  depth eyes, both RGB eyes, five bounded hypotheses and immutable input.
  Removed square census, photometric ranking and bootstrap endpoint fallback.
- Replaced depth dilation with the generated min-depth/validity certificate:
  complete projected-support envelope/dyadic cover; interval-enclosed lateral
  reprojection uncertainty; managed/native resources and host scene switched.
  `DepthDilation.compute` and its meta are deleted.
- Removed observation age timeout and its shader/native uniform consumer.
  Grid transform, scan range and exclusions are frozen at acquisition and
  rebound on every retry, including after FINE/ERASE used the shared shader.
  Removed the observation-count-triggered startup world
  clear. PCA-copy lifecycle retirement now uses a graphics fence, not a pixel
  readback. This does not claim the full refinement workset drain is connected.
- Moved the plane codec into one shared CPU/HLSL expression source in
  SphereFlowerCodegen and connected KernelState, integration and remaining
  readout/export shader consumers to it. Deleted the OverlapShell-owned
  `BuildSurfaceOrientationHlsl` and `MerkabaSurfaceOrientation.generated.hlsl`
  plus its meta. Unrepresentable offsets are rejected, not clamped. Existing
  spare flag bit 30 now carries the measured free-side sign; 16-byte state
  size is unchanged. Full seed/epoch/ABI cutover is still part of RUN 04.
- Removed weighted plane fusion and `sameM8Sheet` from the positive writer.
  Compatible carrier replacement uses generated R1 root/sector intervals;
  captured color uses the selected observation instead of historical blending.
- Removed the 26-neighbour continuing-sheet classifier, its CARVE halo,
  center-ray/tube classifier and clearance-weighted negative update. The
  negative M8 consumer now requires full-support certification within scan
  range and uses the generated R1 contradiction/hysteresis operator. Direct
  support has precedence. This is not yet persistent sparse-dual integration.

Build-only evidence:
- Latest C# import/codegen completed: `/mnt/kingston-unity/Builds/run4-through-r1-compile.log`.
- Complete native rebuild after removing the timeout uniform compiled all
  48 pipelines and rebuilt the Android plugin. StereoFlowerRefine is
  3,194,632 SPIR-V bytes; this is compiler output, not a performance result.
- Latest native rebuild includes the shared plane codec and new negative
  consumer: all 48 pipelines compiled; positive writer 290,168 bytes, negative
  consumer 185,080 bytes. Counter count is 98 after deleting the CARVE halo
  request counter. No build process remains running at this cursor.
- No semantic, CPU/HLSL, scene, performance or Quest acceptance PASS claimed.

Remaining RUN 04 production work:
- Connect existing observation bins to the real FlowerCommit consumer; replace
  and delete old positive winner/queue/routing and remaining CARVE dispatch/
  query/bindings/ABI resources. Do not recreate removed fusion or 26-neighbour
  logic. `M8FlowerAdmitR1` is generated but not yet the live direct writer;
  `M8FlowerContradictR1` is connected to the negative consumer.
- Complete R1 seed/promotion/retirement, persistent sparse dual (including
  restoring FULL for new direct objects), R2/R3, completion, owner epochs,
  fine RGB/V producers and same-observation refinement workset exhaustion.
- RUN 05 remains procedural L2 readout/residency; RUN 06 shared export/app;
  RUN 07 single final validation/fix pass. Do not recreate or expand this DAG.

Calibration constraint: authoritative sensor/plane/reprojection/RGB error
bounds have not been supplied. Serialized bounds default to invalid; stereo
then accepts no endpoints and the certificate proves no THROUGH. Never invent
calibration constants or call this a verified functioning scanner yet.

Recovery: continue RUN 04 from this cursor and the current diff. Do not reopen
CUT 03. Frozen REV-C, fixed 399 positions, thread-order split masks, compile-time
Fib(Tparent) and additive V are unchanged. User `.claude/` and `CLAUDE.md*`
remain untouched. Working repository is `/mnt/aidisk/prace/uniscan`, not the
different `/mnt/aidisk/prace/simplescan` contract.

## CURRENT TRUE STATE — RUN 04 live bins/commit cursor, 2026-09-06

CURRENT_COMMIT=70c9f43fa37f3e70b38072cfc12615af291abc87
CURRENT_CUT=RUN_04_IN_PROGRESS
DAG_STATUS=unchanged; RUN 04 -> RUN 05 -> RUN 06 -> RUN 07
CONTRACT=unchanged immutable REV-C prefix; fixed-399/Fibonacci/nested-V unchanged
COMMITS_THIS_CONTINUATION=none; RUN 04 is not closed

Implemented and connected:
- `MerkabaFlowerCommit.hlsl` is now the live direct writer, one workgroup per
  touched tile, consuming the existing 16-byte ObservationRecords. It reduces
  compatible plane bounds and fixed R1 root/sector buckets before choosing a
  representative. Competing certain sectors, empty root intersections and
  certain/impossible root conflicts do not select a winning surface.
- Existing occupied-carrier compatibility is classified once per record,
  cached only in the transient symbol scratch bit. Direct support excludes
  negative mutation even when the occupied owner rejects that incoming sheet.
- Managed Integrator and the single native observation job now execute
  count/publication/reserve/emit/FlowerCommit. There is no separate bins job.
  Bin stamps use the immutable observation token, not the retry attempt token.
  Radix publication signals residency progress so allocation retries can drain
  the same observation without a new viewpoint. Retirement clears only touched
  bins/direct masks and abandoned claims after FinalizeObservation completes.
- Count, emit and direct commit share `MerkabaObservationScope.hlsl`: the same
  frozen scan range, exclusions and FINE brush; out-of-scope measurements do not
  allocate or request COLD owners.
- Removed old positive discovery/routing/current-frame-support/winner/queue
  shader functions and pragmas, their Integrator dispatches/bindings, the four
  SurfaceWinnerRanks allocations and SurfaceQueue (72 MiB), and their native
  resource entries. The single 32 MiB allocation is now ObservationRecords.
  Removed ClearTouchedSurfaceCandidates; its necessary storage retirement is
  part of RetireObservationBins. Storage publication no longer derives
  dispatch arguments from the deleted candidate count.
- Native ABI is 4, resources 41, pipelines 33. Both C#/C++ tables and shader
  generator changed together. SetupWizard and the host integrator scene now
  reference MerkabaObservationBins.compute.
- Reclaimed bit 1 as R1SeedFlag; no NeedsCarve alias/property remains in
  production. SSD validation accepts strict non-drawing seeds and rejects
  illegal seed/occupied/plane combinations. Tile load rebuilds the active R1
  mask from occupied OR seed, not the former CARVE flag.
- Removed the old CPU weighted integration API and its obsolete fixture uses.
  Added shared interval SEAL/BEND and rational R2 rotation algebra; stereo
  consumes the shared SEAL/BEND expression. Fixed carrier compatibility's
  line-class lookup to use Direction.w, not the orientation sign in Meta.x.

BUILD_EVIDENCE:
- C# import/codegen completed successfully at
  `/mnt/kingston-unity/Builds/run4-flower-commit-compile.log` after the scope
  consolidation. All 33 native pipelines compiled and Android plugin rebuilt;
  the connected FlowerCommit is 865276 SPIR-V bytes. Both commands exited 0.
- These are compile results only. No semantic tests, benchmarks, CPU/HLSL
  parity or Quest acceptance run; execution-mode instruction keeps those for
  RUN 07. No compiler process remains running at this cursor.

DO_NOT_MISREPORT:
- Direct first-hit seed and cross-observation admission are connected.
  The same-observation generated common-flag R1 promotion predicate is NOT
  connected: the final M8FlowerAdmitR1 call still passes false for that
  predicate. Do not label R1 promotion or RUN 04 complete, and do not replace
  the missing incidence predicate with a score/angle/observation-count hack.
- Persistent sparse-dual GPU update, dependent owner epochs, R2/R3 producers,
  completion and complete same-observation metric/skin refinement drain remain
  RUN 04 work. Current negative consumer is still the full-support certificate
  against M8; remaining CARVE query/dispatch/resources must be replaced in RUN 04.
- Readout/export replacements are still RUN 05/06. Existing 16-byte attempt
  completion readback remains; the complete app is NOT claimed readback-free.
- Calibration bounds remain unsupplied and default invalid; do not invent them.

NEXT_CURSOR=finish RUN 04 from the live FlowerCommit and this diff; do not
reopen CUT 03 or reconstruct the DAG. The two RGB-D/PCA views remain held for
the immutable observation. User .claude/ and CLAUDE.md files were not edited.

## CURRENT TRUE STATE — parallel implementation cursor, 2026-09-06

```text
CURRENT_COMMIT=70c9f43fa37f3e70b38072cfc12615af291abc87 + uncommitted implementation
CURRENT_CUT=RUN_04_IN_PROGRESS; no newly closed run
DAG_STATUS=RUN_04 -> RUN_05 -> RUN_06 -> RUN_07 unchanged
CONTRACT_STATUS=frozen REV-C text unchanged, including fixed399/Fibonacci corrections
TESTS_RUN=NONE this continuation
BUILD_THIS_CONTINUATION=NOT_RUN
APK_THIS_CONTINUATION=NOT_BUILT; no final APK/acceptance claim
MANUAL_AUDIT=partial changed-source review, NOT full-repository PASS
```

Execution environment was read from the existing runbook/environment script:

```text
SOURCE_ROOT=/mnt/aidisk/prace/uniscan
UNITY_EXECUTABLE=/mnt/kingston-unity/Unity/Hub/Editor/6000.5.9f1/Editor/Unity
UNITY_HOST_PROJECT=/mnt/kingston-unity/Unity/Projects/QuestMerkabaScanHost
UNITY_PACKAGE_LINK=Packages/com.genesis.roomscan -> /mnt/aidisk/prace/uniscan
BUILD_ENTRY=Tools/unity/build_merkaba_apk.sh
APK_TARGET=/mnt/kingston-unity/Builds/QuestMerkabaScan/QuestMerkabaScan-release.apk
```

The shell's initial `/mnt/aidisk/prace/simplescan` directory is a different
worktree, not this pursuit's source root. Historical environment-ledger source
and contract entries do not override this repository's AGENTS/REV-C/lasttrue.
No VS Code or IDE was launched. Quest remains unavailable for acceptance.

Implemented changes in this continuation:

- `MerkabaObservationReduction.hlsl` now requires a CERTAIN matching fixed R1
  root/sector/sign with intersecting phase intervals for seed correspondence;
  identical packed planes alone cannot promote an unresolved seed.
- `MerkabaFlowerCommit.hlsl` applies that predicate, uses generated line-class
  identity and transforms normals as covectors, normalizing before computing
  the metric plane offset. `MerkabaIntegrator.cs` refuses non-finite or scaled
  acquisition frames rather than applying metre-calibrated bounds to them.
- `MerkabaIntegration.compute` shares the complete-support enclosure for
  owner/tile spans. A tile-wide THROUGH proof can serve all its contained
  owners; a failed envelope still falls back to the exact per-owner support
  query. Same-observation direct-support exclusion remains in effect. The
  unused canonical-octahedron include was removed from this shader. This is
  not yet the persistent sparse-dual writer or CARVE-resource cutover.
- `MerkabaSsdStore.cs` publishes M8 plus sidecar append batches through one
  I/O transaction and rolls back appended stream tails before index publication
  on a write failure. It no longer clones the whole replay index for append
  validation. `MerkabaSphereFlowerReplayIndex.cs` preflights touched epochs and
  captures bounded tile sidecars; `MerkabaTileState.cs` carries the resulting
  coherent M8/sidecar snapshot. GPU upload/writeback is not yet connected to
  these sidecar packets.
- `MerkabaSphereFlowerSkinEvaluation.cs` and
  `MerkabaFlowerSkinReadout.hlsl` implement CPU/HLSL fixed399 signal evaluation:
  thread-order masks/rank, epoch checks, RGB/V union, three descents, additive
  V and a 64-byte sample with inline certified optical values. Codegen owns
  the shared ordered-chamber transform/Jacobian. These are shared consumers,
  NOT a connected replacement renderer/exporter; parity has not been run.

### Historical normative dependency report — §10.3 resolved by subsequent user correction

1. RESOLVED: the user explicitly replaced section 10.3 with residual extraction
   without pred/obs overlap and added the global section 9.2 distinction.
   The following is the historical counterexample, NOT an active blocker.
   The former section 10.3 had overlapping incompatible decisions. On the generated
   xy+ loop sector 0, take singleton unit roots
   `u=(255/257,32/257)` and `v=(63/65,16/65)` with the same algebraic rootSign.
   Both lie strictly between the sector boundaries `(1,0)` and
   `(1/sqrt(6),sqrt(5/6))`. Their exact tangent-half-angle residual is `8/129`:
   nonzero, nonsingular and sector-preserving. The nonzero-residual clause
   therefore says to persist R2_PHASE, while the raw pred/obs-disjoint clause
   says IMPOSSIBLE. Both predicates hold for the same input. No branch-order
   precedence or conventional fit was invented to suppress this conflict.

   Applied correction: disjoint pred/obs is permitted within a CERTAIN shared
   generated relation. Peer evidence and same-knot incident syntheses retain
   their intersection requirements. The corrected source contract and its
   lasttrue prefix were updated together under explicit user authority.

2. Section 10.2 refers to restricting `E_L` (carrier plus ancestor R2 records)
   to a child loop but does not define the nonlinear parent strand/cell
   evaluator or its conversion to child ABC. Existing codegen/oracle proves
   tau/rotation and integer substitutions, not that missing evaluator. No
   interpolated child plane, Hessian, fitter or mesh solver was introduced.

3. Section 16.3 needs two non-collinear R1 relations sharing a generated flag.
   Under the literal section 8 table every `(f,e,c)` flag has one R1 anchor.
   Distinct R1-anchor masks are disjoint. A translated-relation/sector-to-flag
   transport is needed; neither its rule nor its generated table is present.

4. Sections 8/20/24 require a shared mapping from nested f/e/c flag cells to
   the evaluated L2 H/R0..R5 skin carrier. That map is not present. Section
   24's per-L2 symbol also has no explicit child path while reserved bits must
   be zero; distinct geometric child identities must not silently alias.

Affected scanner geometry/readout/export replacements remain unclosed. No
fallback switch, splat, persistent mesh or alternative geometry was added.
The old readout/export consumers remain explicitly deferred dependencies,
not final-contract compliance. Persistent dual, GPU fine producers/drain,
owner-epoch mutation and their residency/writeback still require implementation.

NEXT_CURSOR=resume from these files and the exact unresolved dependencies;
do not reread historical plans, recreate the DAG, claim full RUN_04/05/06 PASS,
or build/publish this partial scanner as the completed contract application.

## CURRENT TRUE STATE — excavation proposal analysis, 2026-09-06

The authorized section 10.3 correction IS incorporated in the normative
contract and this file's identical prefix. Prediction/observation residuals
do not require overlap; peer evidence and shared-knot incidence still do.

The subsequent proposal `FREE/FULL boundary -> inferred DIRT surface` is
recorded here as UNRESOLVED, not silently made a normative surface rule.
It conflicts with FULL's defined meaning in sections 0/0.1 and the finite
completion requirements in sections 12/13. Its claimed physical implication
has this counterexample:

Camera at the origin observes a wall at z=5 through a finite field of view.
In the actual scene, an open neighborhood on both sides of a lateral frustum
boundary at z=2 is empty. Certified interior supports can become FREE while
unobserved exterior supports remain implicit FULL. Thus a FREE/FULL boundary
exists there without any physical surface. A scan-range boundary or a hole in
depth validity can produce the same information frontier. Labeling it DIRT
does not supply missing endpoint/root evidence or prove a surface exists.

The proposal's `permanently empty` wording also conflicts with dynamic scenes:
an object introduced later into previously certified FREE must be admitted by
new direct evidence, restoring FULL over its endpoint support as section 0.1
already requires. Historical FREE is not proof against later matter.

Smallest Sphere-Flower-native interpretation preserving current truth:
use excavation information to eliminate impossible members of an existing
finite direct Flower completion set; emit only its unique admissible petal.
A bare FREE/FULL frontier remains unknown, not a knot or a surface. A separately
labelled, non-authoritative DIRT visualization would require an explicit user
decision about presentation semantics; it has NOT been implemented or used
to seed occupancy, refinement, completion or export.

Implementation continues only on unaffected, already authorized mechanisms.
GPU dual raw buffers are allocated/bound and cleared; native ABI exposes the
three resources. Sparse hierarchy helpers and the shared storage packet layout
are present. Grid staging sizes now match the 529/528-record writeback/load
packets, and dual-node registration is passed to the existing world allocator.
The storage producer/consumer connection and generation retirement are still
in progress. No new build/tests/benchmark, commit or APK is claimed.

NEXT_CURSOR=finish the existing dual storage/generation hooks and receive the
shared residual classifier handoff. Do not re-open the resolved section 10.3
contradiction. Do not treat this unclosed DIRT proposal as surface authority.

## CURRENT TRUE STATE — RUN_04 dual integration handoff, 2026-09-06

CURRENT_COMMIT=70c9f43fa37f3e70b38072cfc12615af291abc87
CURRENT_CUT=RUN_04, implementation in progress; NOT closed or validated.
CONTRACT_SHA256=51656a9c3e43caef5925f726914f863f1e419ac2a6c047ae56c8a7a1d800d28f
Normative text is unchanged by this handoff. The excavation/DIRT proposal
above remains unresolved; FULL is not silently reinterpreted as proven matter.

IMPLEMENTED_IN_WORKTREE:
- UpdateObservationDual now updates the sparse block/chunk/tile hierarchy
  from full-support certificates. Strict direct supports are marked by Emit
  before the negative pass and restored to FULL before R1 admission. New
  objects are not forbidden by earlier free-space evidence.
- FlowerCommit combines R1 admission and certified negative contradictions
  in one workgroup per touched tile, including negative-only touched tiles.
  Tile scratch retains the observation token across retries to prevent
  repeated R1 admission or evidence decrement in the same resident tile.
- After FlowerCommit, the existing allocation publication kernels service
  requests produced by the dual pass before FinalizeObservation. Managed
  and native dispatches reuse these kernels; no second native queue exists.
- Dual mutation generations use GPU fence retirement, an OPEN generation
  floor, and a durable generation watermark. Cold payload retirement is
  conditional on residency and durable storage, not an assumed CPU epoch.
- GPU dual writeback/reload/eviction and dual-only OPEN are connected through
  existing staging and M8 spatial addresses. Uniform dirty nodes use a
  bounded dirty-bit selection (at most 32 records), not an O(world) SAVE scan.
  M8 and sidecars append together; ACK checks the captured generation.
- The shared CPU/generated-HLSL residual classifier now separates peer
  evidence intersection from parent/observation tau residual extraction.
  Q2.29 intervals that include zero after outward encoding are AMBIGUOUS,
  not persisted as proved nonzero innovations. RGB half-interval encoding
  rounds outward and rejects unrepresentable finite bounds without clamping.

FILES_CHANGED include the Integration/FlowerCommit/ObservationBins shaders,
MerkabaDualHierarchy.hlsl, MerkabaDualGpuLayout.cs, Grid.Gpu/Storage/DualStorage,
Integrator, native executor bindings, World.compute, SsdStore, ReplayIndex,
ThreadAtlas, shared SphereFlower authority and its codegen/HLSL output.

LEGACY_REMOVED: observation QueryCarveTiles/PrepareCarveArgs/IntegrateCarveTiles
dispatch paths; CarveTiles/CarveDispatchArgs allocations, properties, bindings,
native resource entries and carve_indirect dispatch mode. FINE/ERASE now uses
the existing touched queue and observation indirect arguments. Admission has
an explicit FrozenObservation guard; successful observation retirement clears
its GPU reservation before releasing the CPU lease. A held retry cannot have
its queue overwritten. Queue serialization alone is not used as the proof.
NATIVE_ABI=7; RESOURCE_COUNT=42; PIPELINE_COUNT=31. Shader include regeneration
belongs to the final build and has NOT been run. Legacy CARVE telemetry names
still exist; absence of the removed resource names is not a whole-repo audit.

DEFERRED_DEPENDENCIES: GPU fine-detail/Thread producers and same-observation
drain, atomic owner-epoch mutation, the exact parent-to-child evaluator,
same-observation R1 flag transport, evaluated L2 skin carrier mapping, and
replacement procedural renderer/export consumers remain unclosed. Existing
CPU/skin evaluation helpers are not a completed production renderer/exporter.

TESTS_RUN=none in this implementation phase.
PERF=not measured; no Quest runtime or zero-readback claim.
MANUAL_AUDIT=targeted source integration review only, not full RUN_04 PASS.
No new build, APK, commit, push, or closed run is claimed. Storage readbacks
transport persistence packets; observation completion and old readout still
have readbacks, so the application is not yet wholly free of hot-path readback.

NEXT_CURSOR=CARVE resource excision handoffs received; resume exact remaining
dependencies above. Do not redo the DAG, dual integration handoff, or resolved
section 10.3 analysis. Do not implement DIRT as a positive surface authority
without resolving the recorded counterexample.

## CURRENT TRUE STATE — authorized excavation support, 2026-09-06

The user explicitly confirmed FULL as default supporting matter, not merely
unknown/veto, and required FREE/FULL to supply DIRT where direct geometry is
missing. This decision SUPERSEDES the previous pending DIRT question and the
veto-only H03. Do not ask that question again or restore the previous restriction.

CONTRACT_FILE=M8-DUAL-SPHERE-FLOWER-CLOSED-PRODUCTION-CONTRACT-REV-C.md
CONTRACT_BYTES=100653
CONTRACT_SHA256=0097f6b916c8fd1b49f83b443f0be0f587c5bb047769052f6e2502d69d722775
LASTTRUE_CONTRACT_PREFIX=byte-identical, checked after applying the amendment.

Normative amendment is physically applied to ontology, direct/dual algebra,
new section 13.4, readout symbols/publication, export, required proofs/scenes,
and the final invariant. FULL is matter in the model, not a claim of direct
sensor observation. DIRT is actual rendered/exported depth-bearing support,
not merely diagnostic. It is independent of the unique direct-completion gate.
No synthetic R1/R2/R3 evidence or captured RGB is generated from this support.

The boundary is defined exactly from existing represented M8 supports:
Cell(U) is covered by K=U+b for the eight b in {0,1}^3; any THROUGH covering
support proves FREE, all resolved non-THROUGH states give model FULL, and
otherwise residency is AMBIGUOUS. Exposed FREE/FULL faces have a unique
FREE-side integer owner and six generated axis classes. No new persistent
coordinate index, mesh, implicit field, fitter, or DIRT log was authorized.
Direct coverage supersedes DIRT; new direct objects restore earlier FREE;
new THROUGH removes DIRT. Missing texture alone does not create another surface.

DAG_STATUS=RUN_04 -> RUN_05 -> RUN_06 -> RUN_07 unchanged; amendment obligations
are assigned in those existing runs. DAG_AUDIT=PENDING_EXCAVATION_AMENDMENT.
CURRENT_CUT=RUN_04_IN_PROGRESS; no run closed by this contract update.
DIRT_IMPLEMENTATION=NOT YET CONNECTED. The new support rule is normative now;
existing scanner dual storage works toward it, but the procedural support
symbols, publication and exporter still require implementation.

Additional production change in this turn: R1 packed RGB now requires valid
bilinear-cell coverage from both frozen RGB cameras, using four explicit
texel loads rather than a clamp sampler. The legacy 0.001 projection epsilon
and invalid-UV border color path were removed. Depth-L/R, PCA-L/R and the
five bounded Flower-supported hypotheses were not reduced.

Unrelated exact direct-geometry dependencies remain unresolved: §10.2 lacks
the ancestor-R2-to-child-ABC restriction operator; §16.3 lacks translated
R1 evidence-to-flag sector transport; the L2 six-wedge carrier/planarity and
geometry-child identity mapping are not complete. Agent derivations did not
invent fitted planes or fake closure to hide those gaps. The DIRT authorization
does not silently resolve or waive the required direct-geometry equations.

TESTS_RUN=none; BUILD=not run; PERF=not measured; no new commit/push/APK.
NEXT_CURSOR=implement the authorized exact DIRT support in the existing shared
readout/export path while retaining all unresolved direct-geometry obligations.
The DIRT decision is resolved; do not repeat the prior clarification request.

## CURRENT TRUE STATE — connected observation liveness, 2026-09-06

CURRENT_COMMIT=70c9f43fa37f3e70b38072cfc12615af291abc87
CURRENT_CUT=RUN_04_IN_PROGRESS; no additional run closed.
CONTRACT_UNCHANGED=the authorized excavation revision above remains normative.

Connected production changes in this continuation:
- Counter 56 is now ObservationChangeMask, not obsolete CarveClassifiedFree.
  R1 writes reduce the change bit once per tile workgroup; uniform-block and
  chunk/leaf dual publications set the dual bit even with no touched HOT R1.
- FinalizeObservation reports actual changes for pending/failed quanta too.
  Integrator.AuthorityChanged is connected to RoomScanner readout/persistence
  invalidation independently of the successful-observation UI event.
- ResetObservationBins clears the per-attempt change, unresolved-dual and
  backpressure counters. The old sticky unresolved-dual counter could prevent
  every later FlowerCommit after the first residency miss.
- Frozen-observation retry consumes actual authority/residency, acknowledged
  load-cursor or durable-generation progress. Failed submission does not eat
  the wake-up; a no-change retry does not wake itself through its own fence.
- Pending-observation R1-committed tiles are pinned against normal eviction.
  Writeback-only capture restores them HOT on acknowledgement, retaining the
  existing once-per-observation token and preventing repeated seed admission.
- Capacity-only reclaim is connected through the existing storage pump and
  PrepareLoadedTiles(count=0), at most 256 chunks per quantum, one finite sweep.
  Only actual COMMITTED reclaim advances residency; no extra pipeline, camera
  input, readback or timer was introduced. Only proven durable spans qualify.

DIRT code now present: shared CPU/codegen/HLSL cell/face/vertex authority,
16-byte inferred symbol, GPU halo/face-mask/shared-arena writer, persistent-dual
extraction and GLB/3D Tiles material/provenance consumers. The GPU support helper
is NOT yet called by a production page compiler. Ordinary GLB/Viewer entry
points do NOT yet call the DIRT hooks: actual shared direct-L2 coverage is
missing. Legacy readout/export remain unclosed; this is not a new fallback mode.

Remaining implementation dependency: automatic durable release before SAVE
needs a real whole-generation GPU drain receipt. Current SSD pendingGeneration
is not GPU source generation, and an empty dual root/writeback queue does not
prove no HOT dirty M8 remains. Example: A+B M8 and C dual change; A+C append
while B is still dirty. Committing that as the whole observation is forbidden.
No speculative commit API or false durable watermark was added.

The previously recorded direct-geometry dependencies remain: ancestor-R2
restriction to exact child ABC (§10.2), translated R1 flag/sector transport
(§16.3), and the L2 carrier/child-symbol mapping (§8/20/24). They were not
replaced by fitted planes, mesh topology, or a default all-UNCOVERED provider.

TESTS_RUN=none; BUILD=not run; PERF=not measured; no new commit/push/APK.
NEXT_CURSOR=all three agent handoffs received. Continue from connected retry/
storage consumers above; do not redo those changes or claim RUN_04 PASS.
The unresolved direct-geometry definitions still require explicit closure;
DIRT authorization does not define them or authorize an alternate evaluator.

## CURRENT TRUE STATE — connected durable source drain, 2026-09-06

CURRENT_COMMIT=70c9f43fa37f3e70b38072cfc12615af291abc87 + uncommitted RUN_04.
CURRENT_CUT=RUN_04_IN_PROGRESS; no additional run closed.
CONTRACT_UNCHANGED; RUN_04 -> RUN_05 -> RUN_06 -> RUN_07 unchanged.

Implemented and connected since the preceding cursor:
- MerkabaGrid.DurableCommit.cs now defines the previously missing coordinator.
  Memory pressure selects a source cut between completed observation attempts;
  normal and FINE/ERASE submission use ObservationMutationSubmissionAllowed.
  Semantic writes pause only for the GPU-to-SSD drain. The CPU manifest/flush
  task does not hold the GPU source gate or require another camera observation.
- Counter 57 is exact DirtyTileCount, maintained by atomic dirty transitions.
  Empty dual gather returns a 32-byte control receipt: held source generation,
  zero dirty dual nodes, dirty tile count and actual occupied kernel count.
  Nonempty packets retain their existing layout. A nonzero dirty tile count
  sends the drain back to tile writeback; an empty queue alone is insufficient.
- Writeback-only drain uses the transient KEEP_HOT queue bit; ACK retains the
  physical slot and once-per-observation token. No new GPU buffer or pipeline.
- SSD CaptureAppendPosition + expected-position CommitAsync reject an overtaking
  append before publication. Durable generation advances only after Committed.
  Incomplete rollback/index publication throws, rather than causing infinite
  stale-prefix retries; successful OPEN/Clear is required to recover that store.
- SAVE uses the receipt occupancy, not telemetry. Its source/store/generation
  inputs are captured before asynchronous commit. Quiesce and teardown await the
  current cut before closing GPU submission; direct Clear rejects an active cut.
  Storage CPU completion also progresses while GPU submission is suspended.
- Automatic cut retries are driven by new observation/actual authority changes
  or an overtaken append prefix, not by read-only dispatch generations. This is
  the capacity-release integration, not a claim that all fine producers exist.

FILES_CHANGED=this continuation: Grid.DurableCommit.cs/.meta, Grid.Storage.cs,
Grid.DualStorage.cs, Grid.Gpu.cs, Integrator.cs, Persistence.cs, SsdStore.cs,
RoomScanner.cs. Prior handoff shader/manifest changes are connected above.
MANUAL_REVIEW=changed submission/drain/commit/lifecycle paths and dirty shader
transitions read; not full-run acceptance or a substitute for deferred tests.
TESTS_RUN=none; BUILD=not run; PERF=not measured; no new commit/push/APK.
NEXT_CURSOR=do not redo durable coordinator or legacy status discovery. RUN_04
still needs actual fine-state producers and observation-local refinement;
recorded exact R1/R2/L2 geometry dependencies are unchanged. The shared Flower
page compiler/live DIRT coverage and ordinary export entry connections remain
unimplemented; existing helper presence is not a completed renderer/exporter.

### Fine producer prerequisite — current blocked cursor

The next attempted production task was the real GPU FlowerDetail/ThreadAtlas
producer, not another storage wrapper. Its ancestor R2 prediction input remains
undefined in §10.2. Source confirmation: RestrictPlaneToLoop / generated
M8FlowerPlaneIntervals consume one plane; FlowerCommit supplies the observed R1
plane. AnalyzePhaseResidual consumes an already predicted root and does not
produce it. ChildPetal carries integer substitution, not metric restriction.
There is no existing ancestor-R2-to-child-ABC operator to connect.

User input required: the exact Sphere–Flower-native restriction of committed
ancestor R2_PHASE onto a different generated child loop (ABC or equivalent root
formula). Reusing only the R1 plane would discard committed shape, and inventing
an interpolation/fitter would change the frozen authority. Neither was done.
No production code, contract text, tests/builds, or run status changed in this
prerequisite check. Do not repeat source discovery at the next continuation;
resume the producer from this input when its definition is supplied.

GOAL_STATUS=BLOCKED after the same prerequisite persisted through three
consecutive goal turns. No new normative definition or implementation of the
ancestor-R2-to-child-loop restriction was supplied. RUN_04 is not complete;
all uncommitted implementation work is retained. Resume requires the exact
§10.2 operator, not another source-discovery pass or a fallback geometry rule.

### RUN_04 resumed — user-defined §10.2 phase pullback, 2026-09-06

The immediately preceding §10.2 prerequisite is SUPERSEDED by the user's
explicit self-similar transport. The contract and immutable prefix above now
define the exact child R1 base root followed by the unchanged parent R2
tangent-half-angle residual, with generated channel/orientation transport.
Ancestor rotations are coarse-to-fine; inherited knots copy their exact parent
root. Metric V is excluded from geometry prediction. Do not reopen the old
off-loop-extension question or substitute an R1-only prediction.

CURRENT_CUT=RUN_04_IN_PROGRESS; no run closed, committed, built or pushed.
CONTRACT_BYTES=102416
CONTRACT_SHA256=11a62ea7ac00230ee547277d7bc3a6211b2c1be2b867485e2cffbc74581680ad
CURRENT_TRUE_STATE=CPU and HLSL ordered phase prediction and outward Q2.29
endpoint decoding are implemented; finite generated L1/L2 transport rows and
their codegen/caller connection are being completed. No GPU fine-page producer
or complete RUN_04 acceptance is implied by these algebra helpers.

Transport convention follows the user's same-facet pullback: use the ordered
§8.5 barycentric child substitution, not an isometry between unrelated circles.
The integer child matrices have determinant +2 (normalized determinant +1/4);
this determines facet orientation, not by itself the full channel mapping.
The attempted reduction of the parent channel bundle to its single R2 anchor
was incorrect: use the whole generated strand/loop incidence. Any additional
canonical vertex reordering must carry its determinant sign.

TESTS_RUN=none; BUILD=not run; PERF=not measured; generation is implementation.
NEXT_CURSOR=finish the generated transport and connect valid epoch-bound R2
records to child prediction. Continue the existing RUN_04→05→06→07 DAG.
The goal tool still reports its historical BLOCKED state; implementation has
resumed on the user's supplied definition, not on an invented fallback.

### Correction: shared-knot closure is after synthesis, not before residual extraction

The supplied rational parent-to-child operator is implemented. It is no
longer missing. The subsequent claim of a new shared-ancestry contract
failure was INCORRECT and is withdrawn. The exact §8.5 example is retained
below to explain the distinction, not as a production blocker:

```text
root flag: A=(1,0,0), B=(1,1,0), C=(1,1,1)

L1 child 1 = (A+B, 2B, B+C)
           = ((2,1,0), (2,2,0), (2,2,1))
L1 child 3 = (A+B, B+C, C+A)
           = ((2,1,0), (2,2,1), (2,1,1))

child 1's new C+A at L2: (4,3,1), line YZ+
child 3's new A+B at L2: (4,3,1), line YZ+
fixed endpoint pair: (2,1,0), (2,2,1)

child 1 parent R2 = inherited original B (L0, XY+)
child 3 parent R2 = new original C+A (L1, YZ+)
```

Thus these are the SAME L2 loop/SYMBOL, not two unrelated circle intersections.
Set the original L0 residual to zero (no record), and give the admitted L1
C+A root a nonzero certain residual r1. For any certain base root u inside a
sector, with r1 small enough that its rotation stays in that sector:

```text
child 1 prediction = u
child 3 prediction = R_(sigma*r1)(u) != u
```

With direct observation u, the first incidence requires no child innovation,
whereas the second requires -sigma*r1. BOTH synthesize u exactly. Section
10.6 requires compatible final synthesized root intervals under the same
transient Sigma. It does NOT require identical parent predictions, ancestor
chains or local residual coefficients. The persistent key in section 1.3
includes petal/path and is not the transient Sigma from section 4.

The erroneous proposed gate compared ancestor chains before applying child
innovations and would reject this valid closure. Remove it; it is not a
contract requirement. Do not fix that invented requirement by arbitrarily
choosing one global parent ancestry. Distinct parent-context predictions may
have distinct local innovations, followed by the exact section 10.6 interval
intersection of their synthesized shared root. No new contract decision is
required to permit this: sections 9.2, 10.3, 10.4 and 10.6 already define it.

CURRENT_TRUE_STATE=ordered rotation, exact inherited-copy handling, outward
Q2.29 decoding and epoch/key-bound record bridge implemented. Correcting the
mistaken pre-synthesis ancestry gate and global residual-key aliasing. The
single-R2-anchor interpretation is incorrect and is being removed. In the
example above D=A+B and F=B+C obey F-D=C-A. The shared DF child loop is an exact
half-scale translated copy of the CA loop, so its channel support exists in
both incident facets' lattice even when CA is not one of a facet's vertices.
Generate channel-local transport from that complete fixed incidence.
RUN_04 remains open; the supplied operator is not a missing-definition blocker.
TESTS_RUN=none; no production build, commit, push or APK. A standalone codegen
compile attempt lacked a Unity math reference before emission; it did not run
oracle tests or publish generated transport data. No contract failure has
been established by the example; the erroneous blocker claim is withdrawn.

### RUN_04 closure recovery — 2026-09-06, current working tree

CURRENT_COMMIT=70c9f43fa37f3e70b38072cfc12615af291abc87 (unchanged).
CURRENT_CUT=RUN_04; STATUS=OPEN; no commit/push/APK.
Latest user review explicitly authorizes full shader audit and Unity tests now.

IMPLEMENTED: generated transport uses the whole lattice's 72 shared strand
families, 12 signed child-edge rules and 39 loop radii, not a single R2 anchor.
Metadata delta=1356 bytes; frozen table hash=0xb3fa3f5f. CPU/HLSL caller builds
actual decoded-R1 child ABC, applies epoch/key-checked ancestor phase in order,
and preserves inherited roots exactly. Real codegen compile and CheckForBatch
of all five generated artifacts passed. Runtime fine-page producer/drain is
NOT implied by these helpers and still must be connected.

QUEST_BINDINGS: same backing allocations, never cloned buffers. SelectEvictionVictims
and AcknowledgeWritebackBatch now have 7 writable bindings. Dual restore/rollback
moved into existing PrepareLoadedTiles (7 RW); InstallLoadedTiles publishes M8
only (8 RW). Prepared dual refs remain unreadable while M8 is LOADING. FlowerCommit
has 8 RW after read-only views of three dual buffers, touched queue and tile bins.
ObservationRecords remains RW because its eligibility prepass writes it.
Audit now continues across every kernel and returns failure for ANY violation.
UpdateObservationDual still requires storage-intent/publication separation:
reuse existing claim arena, ResolveMissingSpatialNodes/ResolveObservationTileRequests
and reserve's touched-publication pass; no new geometry solver/resource/kernel.
Uniform sparse fast paths, direct restoration, frozen retries and the 8-RW limit
must survive this change. export_persistence owns this active edit.

TESTS_RUN: Tools/unity/run_merkaba_tests.sh, kingston Unity 6000.5.9f1, Vulkan.
First current-tree run: total=360, passed=312, failed=48, skipped=0 (21:32 UTC).
GPU tests were blocked by Unity rejecting uint4(0u) in generated HLSL, not proof
of their numeric results. Fixed at shared source and regenerated as explicit
four-component constructor. Removed obsolete CARVE/winner test requirements;
retained behavior suites, updated strict plane-bearing persistence fixtures.
Second full run is in progress. XML root status, not a matching Passed child,
now determines test-script success. No overall build/test PASS yet.

MAINLINE_DECISION (read-only six-commit review; no merge/cherry-pick):
- a4613eb: SAF Save As/name/UI and robust viewer import are missing orthogonal UX;
  adapt in RUN_06, without importing old membrane export or vertex formats.
- 98009a0: session-bound annotation/design/SaveAs and early UI capture are missing
  orthogonal UX; adapt in RUN_06 and verify RUN_07 lifecycle regression.
- 2a813b0: do not port old single-vertex-stream readout ABI; RUN_05 replaces it
  with procedural Flower symbols and must retain stereo correctness/memory benefit.
- 4c2577b: do not port old indexed compaction; RUN_05 culls indirect commands only,
  retaining explicit render resource dependencies and stereo-union visibility.
- 4112aeb: do not port either morphological export shell solver; remove with its
  replacement in RUN_06, consuming the shared Flower/unique-completion/DIRT authority.
- 26fff13: adapt bounded presentation leaf spooling/RTC in RUN_06 over the new
  Flower stream, without legacy membrane result types or float weld.
Mandatory base remains c34d27f; the legacy 480-MiB readout removal is still RUN_05.

NEXT_CURSOR: root finishes current Unity failures; export_persistence fixes the
remaining 15-RW dual stage using the existing storage/publication stages;
scanner_refinement implements actual frozen peer/child-flag R1 same-observation
witness; procedural_readout fixed Unity constructor and depth/FINE tests.
Do not claim RUN_04 closed before actual fine producer/drain, all bindings and
required closure pass. Do not revive the withdrawn single-anchor/ancestry blocker.

### Implementation cursor — 2026-09-06 22:30 UTC

CURRENT_COMMIT=70c9f43fa37f3e70b38072cfc12615af291abc87; RUN_04=OPEN.
USER_DIRECTION=continue implementation; defer draw-record granularity/capacity discussion.
CONTRACT_UNCHANGED; no commit/push/APK; RUN_04 -> RUN_05 -> RUN_06 -> RUN_07 unchanged.

IMPLEMENTED_THIS_CONTINUATION:
- R1 same-observation admission reads actual distinct frozen peer pixels and
  generated R1/flag witnesses; no one-pixel self-promotion through eight owners.
- Dual address-only intents reuse the existing claim arena/publication stages.
  Complete shader audit passed 64 kernels (before fine drain was added), all <=8 RW.
- Shared CPU/codegen now exposes carrier SEAL, stored phase synthesis, final
  shared-root intersection, interval tetra transform and 26 node incidence masks.
  All generated artifacts regenerated and CheckForBatch byte-exact PASS.
- Actual GPU R2 producer/drain consumes frozen endpoint records and exact
  generated ancestry; pending raw tile cursor survives quanta. Structural
  owner changes use TileBits.w pending epoch mask; observation pin uses Runtime.x.
- Drain dispatch connected in managed/native observation before storage reuses
  indirect args. Counters100..103, total104; completion bit30 reports actual
  refinement progress. Pending work does not require camera motion/new input.
- Sparse GPU fine allocator/pages connected to world allocation/binding/reset.
  Complete fine writeback capture and epoch-snapshot replay under implementation;
  source capture/eviction refuses pending structural epoch masks.
- Native registered Flower argument buffer maps Unity's single procedural
  draw to vkCmdDrawIndirectCount, preserving Unity material/XR bindings.
  Native capability/32768-command checks do not silently fall back to one page.
- Native shader embedding now reflects only descriptors actually emitted in
  SPIR-V; dead unnamed glslang function-parameter reflection is not an ABI resource.

BUILD_EVIDENCE=Tools/unity/build_merkaba_vulkan_timestamps.sh PASS, 32 embedded
pipelines and ARM64 libMerkabaVulkanTimestamps.so installed in kingston Unity host.
R2 drain standalone glslang+spirv-val PASS:6 RW,10 SRV,20608 B group scratch.
No current full Unity suite PASS: latest completed XML362/342/20 predates fix
of reserved HLSL token shared; subsequent FXC-heavy run terminated for loop fix.
Stereo loop annotations changed compile expansion only, not five hypotheses,
Depth-L/R, PCA-L/R or interval predicates. No Quest runtime/performance claim.

NEXT_CURSOR:
scanner_refinement: R3 metric bundle and skin producer; drain structural epochs
even after partial coarse failure before releasing observation/pins.
export_persistence: TransferFlowerStorage bounded INSTALL/ACK/RETIRE stage;
LOADING never publishes HOT before fine/epoch restore, no fine-free rollback to HOT.
procedural_readout: actual symbol compaction/publication/cull and renderer;
native count registration API is available. Direct L2 wedge evaluation and
uniform child-path identity mapping remain unresolved, not fabricated.
root: integrate the above, compile/build, then actual closure checks. No additional
run is closed by helper compilation, native build, or an unconnected consumer.

### Resume cursor — 2026-09-06 23:00 UTC

RUN_04=OPEN; HEAD unchanged; no cut commit, push or fresh APK.
Fine capture/import/ACK/cancel completed and connected under existing storage
lease. Epoch invalidation now drains before coarse failure; ordinary owner
epoch advance needs no allocator CAS. R2/R3 endpoint evidence covers all4/8
halo spans; exact-zero evidence retires an old residual. Skin producer still
not connected, so observation-local RGBV closure is NOT complete.
BUILD: native32pipeline ARM64 build PASS; 69kernel SPIR-V audit PASS
(/tmp/m8-fine-storage-audit.log). Unity Android C#compile and generated-table
CheckForBatch PASS (/mnt/kingston-unity/Builds/QuestMerkabaScan/compile-flower-current.log).
Codegen float text uses portable G9, replacing runtime-dependent R spelling;
existing frozen hash remains0xb3fa3f5f. No fresh full suite/device acceptance.
Native timing stage offsets corrected after adding Drain; auxiliary publication
dispatches are still aggregated within their enclosing stage, not individually timed.
NEXT IMPLEMENTATION: generate actual L2 wheel incidence from §8.5: original
R1/R2/R3 colours0/1/2, inherited colours retained, midpoint gets missing colour.
After two substitutions each triangle contains one colour2 hub; incidence yields
128 hubs with six real neighbours and768 wedges. This is an identified derivation,
NOT yet implemented/generated proof. Do not replace it with a fitted hexagon.
Symbol→L2childPath representation still needs exact closure without repurposing
contract-reserved bits. Procedural allocation registration patch may be partial:
inspect Grid.FlowerPages before continuing. Both geometry/readout agents hit
provider usage limit; export agent scope handed off completed.
GOAL: user requested current REV-C pursuit; create_goal rejected because old
REV-B goal remains unfinished/blocked. Tools expose no resume/objective update;
do not mark old objective complete merely to bypass this limitation.

### Resume cursor — L2 incidence / shell synergy / Quest bounds

CURRENT_TRUE_STATE: RUN_04 OPEN; HEAD still70c9f43; no cut commit/push or fresh APK.
GOAL=ACTIVE (user resumed the existing pursuit); the old blocked line above is
superseded. Work remains exclusively /mnt/aidisk/prace/uniscan under REV-C.
Terminal cwd /mnt/aidisk/prace/simplescan is not a target/contract migration;
no files in simplescan were changed by this continuation.

IMPLEMENTED:
- Generated actual section8.5 L2 carrier:386 knots,128 six-ring hubs,768 wedges.
  Creation-incidence offsets387 +sources674 preserve both distinct L2 parent
  predictions. Root/L1 aliases already share one canonical key. Codegen proves
  each L2 knot has at most2 creation predictions on the same original loop.
- Shared MerkabaFlowerGeometry.hlsl now evaluates original knot creation,
  R2 ancestry/R3 oriented metric records, and intersects every generated
  creation incidence before returning a certain shared L2 root. Conflicting
  incidence never picks the requesting wedge's prediction. No XYZ storage.
- User-requested O_h equivariance proof added to section29 and CPU authority:
  all48 signed permutations preserve node/strand/petal incidence and child
  substitution, signed boundaries transport with determinant, flags one orbit.
  Codegen executes this proof, no runtime symmetry table or matrix added.
- StereoCandidate no longer skips R2 on a certain R1 bend just because only
  one hypothesis remains. R3 still requires competing hypotheses; two certain
  non-collinear R1 axes remain mandatory. Five hypotheses and both RGB/depth
  eyes retained; no new measurement or heuristic inferred from sphere radii.
- Existing managed allocators and native descriptor imports reject buffers
  above min(128MiB,device limit), never clamp capacities or truncate descriptors.
  Drain binding uses the same frozen two-eye RGB error bounds as stereo.
- Native ABI9 records each actual native dispatch separately, including
  repeated auxiliary dispatches. Earlier native build PASS is a .so build,
  NOT a Quest APK and predates the latest buffer-check changes.
- DIRT suffix compaction uses the same page/arena transaction and explicit
  direct completion; incomplete direct prefix cannot publish a page. The
  helper compiles but live CompactDirtyFlowerSymbols is NOT connected yet.

PROOF/COMPILE EVIDENCE:
- Portable codegen +CheckForBatch PASS; current generated hash0x9322aee8.
  Includes exact48-transform symmetry proof and L2 creation-incidence bounds.
  This checks artifact identity, not all section29 semantics.
- Shared L2 reader glslang compile PASS after multi-incidence integration.
- DIRT compiler-entry glslang+spirv-val PASS:128 lanes,3 writable buffers.
- REV-C/lasttrue contract prefix byte-identical; git diff --check PASS.
- No new full Unity test suite, no measured Quest performance or device run.

EXACT OPEN DEPENDENCIES (do not claim these are new-evidence ambiguity):
1. Current BuildSkinChambers misses recursive footprints. Exhaustive traversal
   of the actual generated36 rows reaches31/49 L4 and115/343 L5 addresses.
   c3=1 can reach onlyc4={0,1,2,6}; (1,3,0) has no footprint. Existing test
   only round-tripped the same table; now it also requires all49/343 addresses.
   This known implementation failure blocks skin closure. No arbitrary child
   wedge permutation or fabricated empty-child RGB value was substituted.
2. Direct readout terminal identity still lacks an unambiguous symbol encoding:
   generated row5=(hub0,petal0,path10,wedge5),row11=(hub1,petal0,path6,wedge5).
   Uniform DetailRef/ThreadRef are invalid, reserved bits MUST remain zero.
   Do not claim (petal,wedge) alone uniquely identifies the terminal carrier.
3. Unique sector/root/admissible7-site carrier predicate is not yet connected;
   RGB full-footprint producer cannot select all-plus/first-compatible roots.
   Same-observation skin drain remains incomplete until these inputs close.
4. Live renderer/export cutover, remaining legacy excision, complete regression
   and APK generation remain unfinished. Do not erase old FRONT without a
   complete proven direct replacement; DIRT is not a fallback renderer.

HW SOURCE CURSOR:
.codex/M8_STEREO_AUTHORITY_CLOSURE.md:420-430 supplies <=8 writable bindings,
<=128MiB per buffer, readonly aliases, no divergent return before group barrier,
no cross-workgroup spin/no1x1 data-domain loop. Historical algorithm text in
that file does not supersede REV-C. M8_SOTA_EXECUTION_PLAN.md:445-453 records
cooperative tile/halo workgroup guidance, not permission for new variant zoo.

LATEST_HANDOFF: MerkabaFlowerSkinObservation.hlsl now contains actual complete
two-eye RGB footprint measurement with frozen calibrated bounds, strict joint
depth carrier coverage, outward binary16 intervals and atomic seven-child
thread groups. Its bounded57-parent scheduling cursor is CANONICAL order;
only storage addresses convert to thread order (Fibonacci never schedules).
The producer body compiles/spirv-validates through an active compiler adapter;
the live drain cannot invoke it until the unique7-root admission input exists.
Do not count this helper as full observation/skin closure.

LATEST_COMPILE: Unity Android script compile +CheckForBatch process48214 PASS.
Prior process52167 failed only on unsupported NUnit Assert.Multiple; replaced
with one ordinary collection assertion, then recompiled successfully. No tests
were executed by CheckForBatch. StereoFlowerRefine compile17557+spirv-val PASS.
All these processes have exited; no active Unity/compiler process remains.
The new recursive skin coverage assertion is a known semantic FAIL until
the generated transitions are corrected. No APK build was attempted.

NEXT_CURSOR:
Correct the actual skin transition coverage from generated Flower geometry;
do not weaken57bits/399logicaladdresses or silently amend the frozen transform.
Then resolve direct symbol/admission closure and connect actual consumers.

### Resume cursor — exact skin gate / Quest device requirements

CURRENT_COMMIT=70c9f43fa37f3e70b38072cfc12615af291abc87
CURRENT_CUT=RUN_04 OPEN; RUN_05/06/07 NOT CLOSED
PREVIOUS_GOAL_TURN=PROGRESS: user-supplied quest_guide.md saved completely,
585 lines/15840 bytes with all12 source links. No source claims were independently
verified by that copy operation; actual device properties must be queried.
CONTRACT=REV-C prefix unchanged by this continuation; no ontology amendment.

IMPLEMENTED_NOW:
- Skin.cs editor/codegen authority now checks recursive locality reachability
  with exact finite transition enumeration, not UV samples or an epsilon.
  Invalid tables cannot be republished by Generate/CheckForBatch. This is a
  necessary coverage gate, NOT a replacement for exact geometric footprints.
- Native actual-device properties/features logging and requirements checking
  is being completed by export_persistence in the existing executor.

COMPILE/PROOF_EVIDENCE:
- Portable C# codegen compiled, then correctly FAILED before emitting files:
  "31/49 L4 and115/343 L5 addresses reachable; first missing(0,1,3)".
  Evidence: /tmp/m8-skin-check.uqbNKs/codegen.log, process27644 exit134.
  The earlier CheckForBatch PASS checked artifact identity and insufficient
  structural predicates; it is NOT current semantic closure. Generated hash
  0x9322aee8 remains the last artifact hash, not an authorization to cut over.
- No full Unity test run, no APK, no device test. Skin diff --check PASS.

EXACT_SKIN_FINDING:
- Current rule is ChildSite=SkinWedgeSites(wedge)[High]. A ring child occurs
  in four chambers total. All six child wedges are needed after each c3 to
  allow all seven c4 regions to themselves expose seven c5 regions: if one
  wedge is missing, an incident ring c4 is reached from at most one remaining
  wedge, hence at most two next chamber states, which expose at most five
  sites (hub plus four rings), not seven. Covering six states for every c3
  would require at least7*6=42 transitions, while the table has36.
- Therefore merely permuting current ChildWedge values cannot repair full
  terminal coverage while retaining this max-site rule. This is NOT a proof
  that every possible Flower representation is impossible. Section20.3 does
  not supply the geometric child footprints/pullback needed to replace the
  max-site assumption without choosing a new arbitrary triangular partition.
- Requested the missing exact Flower-7 footprint/parent-child transform from
  the user asynchronously. No arbitrary remapping, empty-child measurement,
  nearest rule, validDepth or new geometry was substituted.

OTHER_OPEN_DEPENDENCIES:
- Readout identity: the earlier petal/wedge table-row alias alone does NOT
  prove a collision of fully admissible symbols; sector/root and admission
  must also be checked. procedural_readout is checking this exact question.
- scanner_refinement is checking the missing generated flag/sector admission
  predicate. No all-plus/first-compatible seven-root carrier is authorized.
- The live skin drain, direct page compiler, renderer/export replacement and
  full app closure remain incomplete. DIRT is not an emergency fallback.

NATIVE_HW_RESULT=IMPLEMENTED; native ARM64 build PASS exit0, ABI9,
32 pipelines/47 resources unchanged. Properties2/Features2 log actual limits
and enabled versus supported features. Embedded LocalSize/descriptor needs,
actual dispatch dimensions and bound ranges are checked against the device.
Allocation/buffer size and descriptor range are distinct; no descriptor is
silently truncated. Optional numeric/subgroup features are only logged.
Files: Native/MerkabaVulkanTimestamps.cpp and Native/MerkabaFlowerDraw.h.
Build evidence: /tmp/m8-native-abi9-hardware-build.log.
SO_SHA256=624ce7f3529d5c2d7d88f9a44f4e912e9a5ba4f2f263775ace147954fb2203c8
This is a .so build, NOT an APK, device observation or performance proof.

APK_ENTRY=BuildQuestMerkabaScanApk now calls existing CheckForBatch before
BuildPlayer. Previously that entry did not check the generated Flower tables.
This prevents producing a success-labelled APK around known-invalid generated
geometry. The new entry call is manually reviewed, not a completed APK build.
MANUAL_REVIEW=Skin.cs and RoomScanSetupWizard.cs read completely; native
Properties2/Features2, pipeline requirements and descriptor-range changes read.
git diff --check PASS; REV-C/lasttrue prefix byte-identical PASS.

DIRECT_IDENTITY_RESULT=not enough authority to prove an admissible collision
or unique inverse. Read-only packed-plane fixture0xbb100bfc makes all three
minus roots of rows5/11 CERTAIN, with sectors(7,7,3)/(7,3,8), so hub-sector
alone does not establish uniqueness. This is NOT a proof that both are valid
§8.4 petals: flag-sector membership and analytic strand orientation are still
missing. Evidence: /tmp/flower-symbol-identity.dEra5l/Program.cs.

NEXT_CURSOR=incorporate final bounded admission finding; await explicit skin
footprint/flag membership closure before changing the frozen classifier or
publishing its tables. All native/skin-codegen processes have finished. No
commit, push, APK or runtime acceptance in this continuation. Do not repeat
broad audits or treat the old artifact-identity PASS as semantic closure.

### RUN_04 implementation checkpoint — 2026-09-07

CURRENT_COMMIT=checkpoint containing this entry; parent70c9f43
CURRENT_CUT=RUN_04 OPEN; this checkpoint is NOT a cut-closure/PASS commit.
USER_EXECUTION_OVERRIDE=checkpoint first, then current build/full suite,
targeted parity/binning/workgroup fixes and THROUGH/eager-drain proofs.
Do not restart the DAG or infer acceptance from old test counts.

CURRENT_TRUE_STATE:
- The working scanner/dual/epoch/storage replacement is checkpointed together
  with the already-started RUN_05 consumer changes. This is a recoverable task
  snapshot, not a claim that either run is finished.
- Observation, ERASE, load/eviction and changed dual ancestors invalidate only
  affected pages and validated HOT neighbours. ERASE drains owner epochs in
  its finalizer before the canonical transaction can finish.
- Page source invalidity now survives dirty-queue removal. Superseded pending
  allocations are discarded without publishing; only current Publish clears
  the independent invalid guard. Residency changes preserve that guard.
- Carrier ABI is16B with seven shared knots/six wedge bits. Managed/native
  indexed consumers and ABI10 replace their previous readout resource path;
  this new native ABI has NOT yet been built as a .so or Quest APK.
- One L2 has one canonical skin key and one ThreadRef/header. The six source
  wedge aliases no longer create six persistent/draw skin programs. RGB and V
  remain separate persistent authorities. Phase keys are unchanged.
- Stale readout counter consumers and guessed stereo vertex statistics were
  removed. Current timing labels name actual Flower stages; unsupported
  page/symbol counts are explicitly unavailable, not fabricated zeroes.

OPEN_IMPLEMENTATION_GAPS:
- Complete generated flag/strand admission is not supplied by the new exact
  radical provenance alone. No guessed sign mask or loop-normal substitute
  has been admitted as surface geometry.
- RGB carrier drain is implemented but has no live caller yet. Complete V
  measurement/skin drain and direct carrier page production are not closed.
- The current skin reachability failure remains a real gate; generated key
  helper edits are not a full generated-table or CPU/HLSL parity PASS.

TESTS_RUN=no new full suite yet; old342/362 and69-kernel audit supplied by the
user are historical evidence, not current-tree acceptance.
BUILD=no fresh Unity/Quest build; PERF=not measured; DEVICE=not run.
LEGACY_REMOVED=preserve the actual diff; no fallback/compatibility is restored.
NEXT_CURSOR=commit this checkpoint, run the existing Kingston Unity test entry,
then fix its concrete failures and add the two requested behavioral proofs.

### RUN_04 closure repairs checkpoint — 2026-09-07 02:06:43Z

CURRENT_COMMIT=SELF (the commit containing this entry; resolve with git log -1).
PARENT_COMMIT=dcb83cea6e9e9be637d6e8a2fdd45ab3ccb2a52c
CURRENT_CUT=RUN_04 OPEN. RUN_05 consumers remain started, not closed.
DAG_STATUS=unchanged; no new architecture or cut reordering.

IMPLEMENTED_FIXES:
- Vulkan Unity shaders explicitly use DXC. IEEE exponent-bit finite tests
  replace the IsNan lowering that produced invalid SPIR-V in Unity. Native
  glslang remains the native compilation path; both are checked separately.
- Generated interval division now certifies a finite binary32 bracket using
  exact dyadic products, then tightens it with bounded integer bisection.
  Native division is only a starting hint, not an assumed one-ULP oracle.
  Signed-zero/subnormal comparisons and denominator endpoint selection use
  bits; finite overflow is proved, not clamped. Exact-zero add/sub/multiply/
  square identities avoid artificial subnormal uncertainty. The same source
  changes are present in codegen and its generated HLSL output.
- RGB-only/skin-V-only owner state no longer requests blind L1/L2 geometry
  refinement. Structural epoch invalidation clears the stale active phase
  count in O(1), retaining the allocation and existing publication leases.
- Sparse epoch append validation distinguishes a complete frozen fine-image
  snapshot from a raw epoch update. Raw skips/decreases/orphans are rejected;
  certified dirty-state coalescing survives SAVE/OPEN without fabricated
  descendants. This receipt is transient metadata, not a new persistent ABI.
- Stale tests now bind the actual tile endpoint bits, include the current
  packing authority, and check current lifecycle/readout/timing consumers.
  Missing-kernel and geometry/codegen gates were NOT removed or weakened.

INVARIANTS_PROVEN / TESTS_RUN:
- Kingston Unity6000.5.9f1, real Vulkan EditMode suite:
  /mnt/kingston-unity/Builds/UniscanRun04ClosureChecks2/TestResults/merkaba-results.xml
  389 total; 382 PASS; 7 FAIL; 0 skipped; 180.156s.
  Matching merkaba-tests.log has no C# or shader compilation errors.
- All ObservationBins tests PASS, including the actual root/sector interval
  probe and new directed division GPU probe over1036 binary32 input cases.
  Division is checked with independent exact binary64 products, including
  signed zero, both underflow signs, every finite exponent and overflow.
- All26 depth-certificate tests PASS. Common-prefix and dyadic-cover GPU
  queries use actual production functions and an independent all-pixel
  oracle, including exhaustive9x7 rectangles, interior foreground, invalid
  pixels, FOV/near/range boundaries, NPOT padding and512x512 sources.
  Full-support stereo fixtures include either-eye proof and direct endpoint
  precedence for a new object. These are certificate predicate fixtures,
  NOT a claim of a completed on-device dynamic-scene observation test.
- Nine reduced-evidence eager/drain tests PASS: dependent nonzero L1/L2 R2
  records, same frozen two-eye RGB evidence, quantum/retry/backpressure,
  byte-identical payloads, parent epoch and uniform no-blind-expansion cases.
  The oracle is UNITY_EDITOR only. It does NOT execute the production GPU
  drain or prove the currently unwired full RGB/V observation pipeline.
- SPIR-V audit PASS for75 actual kernels, <=8 writable buffer/image bindings,
  no RW/read alias pair. Output:
  /mnt/kingston-unity/Builds/UniscanRun04Checkpoint/TestResults/spirv-audit-closure-final.log
- git diff --check PASS; immutable REV-C prefix of lasttrue.md byte-identical.

FAILED_GATES (seven tests, two current causes):
1. SkinData rejects its current generated chamber transitions:31/49 L4 and
   115/343 L5 addresses reachable; first missing(c3,c4,c5)=(0,1,3).
   This causes six skin/ABI/codegen parity failures. Artifact regeneration
   alone cannot prove the Flower-7 footprint. Do not disable this guard or
   permute transitions merely to make address counts pass.
2. CompactDirtyFlowerSymbols still has no implementation; the compute import
   test correctly fails. Existing ABI/renderer consumers are not a producer.
   Complete direct carrier admission and full live RGB/V drain remain open
   exactly as recorded above. No empty/DIRT-only kernel was added as a bypass.

REMAINING_PROOF_SCOPE:
- A nonzero test of the actual DrainObservationRefinement requires accepted
  frozen depth/normal/source-pixel input; the reduced root-arc oracle is not
  that input. This is missing fixture/implementation work, not a claim that
  the R2 drain depends on skin admission or is mathematically impossible.
- Geometry/ghost/hole/full stationary-observation/whole-scene fixtures and
  full CPU/HLSL/live/export parity are not closed by these unit predicates.

BUILD=Unity C#/shader compilation succeeded in the test host; full test gate
FAIL as above. Native ABI10 plugin / Quest APK NOT built in this continuation.
PERF=not measured on Quest; cold shader compilation time is not device timing.
QUEST_DEVICE_RUNTIME=NOT RUN; adb listed no attached device.
LEGACY_REMOVED=no legacy authority restored; prior checkpoint excisions remain.
MANUAL_AUDIT=changed interval, phase-count/epoch, snapshot-validation, compiler
and fixture code reviewed; this is NOT a full-cut manual-audit PASS.
FILES_CHANGED=the exact diff from PARENT_COMMIT in this checkpoint, including
the new certificate/drain fixtures and editor-only drain oracle. Unrelated
.claude/, CLAUDE.md and CLAUDE.md.meta remain untracked and excluded.
NEXT_CURSOR=do not redo the now-green binning/THROUGH/division fixes. The
remaining skin footprint closure, direct carrier/page producer and actual
frozen GPU observation fixture must be completed before any RUN_04 PASS,
generated-table gate override, APK success or final-contract claim.

### RUN_04 actual GPU drain proof — 2026-09-07 02:26:32Z

CURRENT_COMMIT=SELF; PARENT_COMMIT=0ff3b55ac6f3249f0eceed7a8e9198242f661a4b
CURRENT_CUT=RUN_04 OPEN. This entry supersedes the missing R2 GPU input
fixture noted above; it does not close the skin/readout implementation gaps.

FILES_CHANGED=Tests/Editor/MerkabaObservationDrainGpuTests.cs and its meta,
plus this ledger. No production shader, ABI or contract change in this step.
GPU_PROOF=PASS, actual DrainObservationRefinement, not a probe replacement.
The fixture uses an orthographic camera, two frozen depth/normal pixels,
their16 exact arithmetic overlap-owner records, stable occupied R1 owners
K=(3,4,3) and Q=(4,3,3), and the production sparse allocator/phase writer.
Normal=normalize(1,1,1); canonical offset0; measured offset0.006m. The kernel
adds the mandatory quantization bounds. No roots/residuals are uploaded.
The fixed relation d=(1,-1,0),line4 has certain nonzero R2 evidence under both
signs. A CPU construction check used the current compiled authority before
the GPU test; it did not introduce search or a fixture-specific production rule.

The GPU test requires at least one nonzero R2 record and compares every
owner identity and all four words of every16B phase record for eager1336
versus quanta1/7/64. It also holds the real allocator lock, requires pending
and backpressure without publication, then drains the SAME frozen observation
after retirement/unlock. Completion is idempotent; canonical R1 state and
its once-per-observation stamp are unchanged. Test-only readbacks verify
results and retirement; no production readback was added.

TESTS_RUN=Kingston Unity Vulkan full suite390 total,383 PASS,7 FAIL,0 skipped,
80.322s; actual GPU drain test PASS in58.789s including cold pipeline setup.
RESULT=/mnt/kingston-unity/Builds/UniscanRun04GpuDrain/TestResults/merkaba-results.xml
LOG=/mnt/kingston-unity/Builds/UniscanRun04GpuDrain/TestResults/merkaba-tests.log
No C# or shader compilation errors. The75-kernel native SPIR-V PASS still
applies: this step changes no compute source. Quest runtime/performance and
APK remain NOT RUN. This fixture starts after coarse commit; it does NOT
claim end-to-end stereo acquisition, RGB/V skin drain or complete app parity.

SKIN_AUTHORITY_QUESTION=requested from user; no answer applied. Section20.3
has36 ordered chambers, each mapped onto one canonical child wedge. If all
seven children are complete six-wedge Flowers, they require42 distinct
(child,wedge) footprint images;36 single-wedge images cannot cover42.
The minimal cardinality correction for that interpretation is at least six
additional chamber images, still requiring an exact Flower-native partition.
Alternatively clipped edge-child footprints need an explicit normative
definition and corresponding proof, not an assumed row permutation. Neither
interpretation has been silently substituted for the frozen contract.

MANUAL_REVIEW=raw pixel projection, exact owners, buffer bindings, allocator
initialization/lock, complete record comparison and disposal reviewed.
RUN_04_CLOSURE=NOT PASS: six skin/codegen parity failures and the missing
CompactDirtyFlowerSymbols producer remain. Source work is committed; unrelated
.claude/ and CLAUDE.md files remain excluded. No existing authority restored.
NEXT_CURSOR=preserve the now-passing THROUGH and real R2 eager/drain proofs.
Resolve the precise section20.3 footprint decision, then complete the live
carrier/skin/page producer and rerun remaining gates; do not restart the DAG.

### RUN_04 clipped skin closure — 2026-09-07 03:27:49Z

CURRENT_COMMIT=SELF; PARENT_COMMIT=af758cc599b6dfd6731842ccf33158aa727a3838
CURRENT_CUT=RUN_04 OPEN. User resolved the section20.3 question explicitly:
all seven children exist logically; their support is clipped to the current
parent/L2 domain. The36 chamber rows are correct;42 full child wedges are not
required. This supersedes the previous footprint blocker and the assertion
that every logical L4/L5 address must have a raster preimage in one carrier.
DO_NOT_REOPEN_THIS_QUESTION. The31/115 reachable addresses of this clipped
mapping are not missing topology. All7/49/343 identities and399 thread
positions remain, without childExists or a new persistent support mask.

CONTRACT_CHANGE=user-authorized clipped-footprint clarification in section20.3;
section14.2.2 consistently applies measurement/novelty to nonempty intersections,
and section29 names the corresponding proof. Empty group slots inherit parent
RGB or zero V innovation, are not measured evidence, and cannot prove novelty.
Missing evidence on nonempty support is still AMBIGUOUS. The lasttrue immutable
prefix was compared byte-for-byte with the amended REV-C source:PASS.

IMPLEMENTED=exact integer chamber-domain area/orientation proof replaces the
incorrect all-address reachability gate; CPU/HLSL scalar+RGB split predicates
share geometric supportMask semantics; GPU RGB child measurement skips EMPTY
footprints and preserves the actual parent Thread interval in unused slots.
The36 transition rows, Fibonacci order,399 logical addresses,57 persistent
split bits and all hot/persistent ABI layouts are unchanged. Generated CPU/HLSL
artifacts were regenerated through the existing Kingston Unity codegen,
including its already-authored radical-sector provenance tables. Provenance
does not itself decide carrier admission.

TESTS_RUN=Kingston Unity Vulkan full suite391 total,390 PASS,1 FAIL,0 skipped;
195.860s. All six prior skin/codegen parity failures now PASS. The new exhaustive
CPU/GPU split test covers32769 support/certainty cases with independent expected
outcomes, including empty, partial, invalid and uncovered nonempty support.
Actual frozen-depth R2 eager/drain/backpressure proof remains PASS(58.556s).
RESULT=/mnt/kingston-unity/Builds/UniscanClippedSkin/TestResults/merkaba-results.xml
LOG=/mnt/kingston-unity/Builds/UniscanClippedSkin/TestResults/merkaba-tests.log
CODEGEN=/mnt/kingston-unity/Builds/UniscanClippedSkin/TestResults/codegen.log;PASS
SPIRV=75 kernels PASS; writable buffer/image bindings<=8; no RW/read alias pair.
PERF=Quest sustained runtime NOT MEASURED; suite time includes pipeline creation,
not a device frame-time measurement. APK/Quest runtime NOT RUN.

REMAINING=CompactDirtyFlowerSymbols still absent, the sole suite failure.
Its real direct carrier selector still needs the section8.4 sector/flag admission
predicate; existing readers take root signs/sectors as inputs, and generated
L2 incidence/provenance does not select them. The same selector is needed by
live RGB/V drain. Direct/partial DIRT coverage is also not a complete producer.
No no-op kernel, guessed root selection or DIRT-only fallback was introduced.
Therefore these green skin tests do not certify full scanner/readout integration.

MANUAL_REVIEW=changed CPU/HLSL predicates, exact domain proof, inherited Thread
address/epoch handling, generated artifact parity and test expectations reviewed.
No legacy authority restored and no production readback or new dispatch added.
FILES_CHANGED=the exact diff from PARENT_COMMIT; unrelated.claude/,CLAUDE.md and
CLAUDE.md.meta remain excluded. This is a verified checkpoint, not RUN_04 PASS.
NEXT_CURSOR=preserve the now-green clipped-skin, THROUGH and real R2 drain
proofs; finish actual shared carrier admission and its skin/page consumers.

### RUN_04/RUN_05 dead readout excision — 2026-09-07 03:50:14 UTC

CURRENT_COMMIT=SELF; PARENT_COMMIT=eada6eee1a19f315ca7b696065a61830d454e112
CURRENT_CUT=RUN_04 OPEN; RUN_05 producer integration remains OPEN.
IMPLEMENTED=removed the seven unused legacy readout entry points and their
vertex/index streams, depth-mesh/pin path, uniforms and helpers. The existing
ClassifyHotFlowerPages, PublishDirtyFlowerPages and CullFlowerPages bodies are
unchanged. Removed the three unreferenced whole-mesh capacity constants and
the obsolete audit branch/alias for those deleted resources. No new geometry,
optical formula, fallback, empty replacement kernel or dispatch was added.
FILES_CHANGED=Runtime/Shaders/MerkabaReadout.compute;
Runtime/Shaders/MerkabaWorld.hlsl;
Tools/shaders/audit_merkaba_compute_spirv.sh; lasttrue.md implementation ledger.
LEGACY_REMOVED=ResetReadoutBuild, QueryM8Readout, PrepareReadoutBuild,
BuildReadoutVertices, ProjectReadoutMeshPins, BuildReadoutMesh, FinalizeReadout;
their readout vertex/index bindings and obsolete mesh capacity constants.
MANUAL_AUDIT=remaining readout shader and changed constant/audit hunks reviewed;
direct Runtime/Editor/Tests/Tools search finds no deleted entry/buffer/capacity
names. Existing managed/native consumers already target the Flower pipeline.
This removes dead code, not a working fallback behind the missing producer.
TESTS_RUN=fresh SPIR-V compile/validation and binding audit PASS,68 kernels,
writable buffer/image bindings<=8, no RW/read alias pairs. bash -n and
git diff --check PASS. The first audit invocation was invalidated by an edit
to its running shell script and ended with a shell parse error; it is NOT
acceptance evidence. The unmodified rerun completed with exit0:
LOG=/mnt/kingston-unity/Builds/UniscanClippedSkin/TestResults/readout-excision-spirv.log
FULL_SUITE=not rerun for this dead-code-only change. Latest prior result remains
391 total/390 PASS/1 FAIL at the preceding checkpoint; CompactDirtyFlowerSymbols
is still absent. No Quest APK/runtime/performance acceptance is claimed.
CONTRACT_CHANGE=none; immutable REV-C prefix unchanged.
REMAINING=the section8.4.2 flag/sector admissibility rule is still needed by the
shared live carrier selector, RGB/V drain and real page producer. Exact sectors,
directed radical sign provenance and L2 incidence exist; they do not specify
which sign combinations belong to a flag. Requested that precise missing
relation from the user rather than introducing a root-selection heuristic.
Partial direct/DIRT coverage and later RUN_06/RUN_07 consumers remain open.
GOAL_TOOL_STATE=blocked; available goal tools cannot resume or rewrite an
unfinished goal. Work continued directly under the user's amended REV-C task;
the old tool objective is not implementation authority.
NEXT_CURSOR=do not repeat the resolved36-chamber/clipped-support question or
completed proofs. Resume the shared selector when its exact flag/sector
relation is supplied; do not replace CompactDirtyFlowerSymbols with a stub.

### Supplied flag-order rule; clipping counterexample — 2026-09-07 04:26:29 UTC

CURRENT_COMMIT=8cf7081d9cfb995e89daa55bf933dd2da3b43fcc
CURRENT_CUT=RUN_04 OPEN. The user supplied the missing order predicate:
given an R1 face f, maximize Phi among its four incident R2 edges, then among
the chosen edge's two R3 corners; resolve exact ties canonically in codegen.
DO_NOT_REQUEST_A_MANUAL_SIGN_TABLE_AGAIN. Pairwise same-shell Phi differences
are linear predicates and can use the existing exact boundary/sign solver.

COUNTEREXAMPLE_TO_THE_SUPPLIED_CLIP=for f=(1,0,0), at every geometry level,
the existing section3.5 loop satisfies x-Cx=aL/2 and
(y-Cy)^2+(z-Cz)^2=3*aL^2/4. With the proposed xi=2*(X-C)/aL this is
xi_y^2+xi_z^2=3. But 0<=s_k*xi_k<=s_j*xi_j<=1 implies that the same sum
is <=2. Therefore the supplied clipping admits no R1 loop point at all;
all eight flags of each face are empty. Equality tie-breaking cannot fix this.
The unclipped order inequalities are not equivalent to the clipped formula.

MINIMUM_NATIVE_CORRECTION_PROPOSED=if parent support means the existing M8
support [C-aL,C+aL), its bound in this xi chart is2, not1. This preserves all
world loops and the supplied winner/order predicate. It requires an explicit
user decision before replacing the stated bound. A differently normalized
face chart would instead need its actual world-to-chart map.
CODEGEN_CONSEQUENCE=Phi_a-Phi_b=0 order boundaries can cross old radical
sectors; the common arrangement must include the required order cuts and
re-prove maxSectorCount<=32 before any new sector/flag lookup is published.
STATE=production/generated files and the immutable REV-C prefix unchanged;
no guessed mask, silent clipping change, root heuristic or new solver added.
PROOF=exact algebra from the contract and EvaluateLoop, not a new GPU test.
NEXT_CURSOR=the relative-order rule is supplied; only the inconsistent
clipping/chart definition needs correction before implementing that lookup.

### R1 power-order admission implemented — 2026-09-07

CURRENT_COMMIT=8cf7081d9cfb995e89daa55bf933dd2da3b43fcc
CURRENT_CUT=RUN_04 OPEN.
USER_DECISION=clipping2 explicitly confirmed; the preceding bound1 question
is resolved. Do not request a manual sign table, a different chart, or another
support decision. No world radius, basis, Fibonacci route or skin topology changes.
CONTRACT_CHANGE=authorized sections7.1/8.4.2/29 amendment; exact same update
applied to the REV-C file and lasttrue prefix.
IMPLEMENTED=existing exact radical solver now includes R1 same-shell
edge/edge and corner/corner equality cuts; exact positive-angle interior
signs select maximum edge then corner via frozen Petal incidence.
Exact equality uses minimum frozen Node ordinal; interval boundary roots
remain AMBIGUOUS. No finite-step sampling or invented geometric solver.
GENERATED=24 sectors per R1 line,6 per R2 line,12 per R3 line;
156 total boundaries;144 directed R1 masks, each exactly one incident flag;
all eight flags per directed face covered; maxSectorCount24<=32.
CODEGEN=PASS using Kingston Unity6000.5.9f1 GenerateForBatch, exit0.
LOG=/mnt/kingston-unity/Builds/UniscanR1Order/TestResults/codegen.log
CPU/HLSL tables regenerated from the same authority, including table hash.
CONSUMERS=R1 peer witness, seed correspondence and chromatically certain
stereo support use the generated directed-face lookup. The old16-sector
witness guard is replaced with generated sectorCount and field-width guard.
No parent/child face equality was guessed.
IN_PROGRESS=exhaustive CPU/actual-HLSL order/clip fixtures, production SPIR-V
audit, shared direct flag-root reader and live carrier producer continuation.
BUILD=codegen/editor compilation passed; no APK/Quest runtime/perf claimed.
RUN_04_CLOSURE=NOT PASS. CompactDirtyFlowerSymbols and complete RGB/V drain
remain actual implementation dependencies, not empty kernels to satisfy tests.
NEXT_CURSOR=continue from the generated R1 order lookup and its consumers;
do not reopen the resolved clipping2 or36-chamber skin questions.

R1_ORDER_TARGETED_PROOFS=PASS4/4,2026-09-07T04:55:43Z.
Actual HLSL and independent CPU power-order tests each cover736cases:
all6directed faces,24sectors per face,three interior samples per sector,
all48flags,strict boundary enclosures/crossings,and invalid addresses.
The independent order fixture verifies accepted coordinates above1 and<=2;
it does not merely compare two copies of the generated mask.
GeneratedHlsl_IsBitIdenticalAndMatchesCpuOracle and the exact sector ABI
count test also PASS. Test duration0.886s, Unity process exit0.
RESULTS=/mnt/kingston-unity/Builds/UniscanR1Order/TestResults/r1-order-results.xml
SPIRV=PASS68kernels,exit0,writable storage<=8,no RW/read aliases.
LOG=/mnt/kingston-unity/Builds/UniscanR1Order/TestResults/spirv.log
PREFIX_CHECK=REV-C and lasttrue prefix byte-identical,110215bytes.

R1_READER_IMPLEMENTED=bounded typed CarrierRootProof now serves the existing
bool geometry reader. New CPU/HLSL SelectR1FlagRoots filters the two fixed
algebraic signs by the directed face lookup; SelectR1FlagRelation also uses
the canonical endpoint pair and existing SEAL evaluator. Candidate/unresolved
masks are scratch only, not persisted branches. These selectors still need
their full direct page/scan callers; they are not a complete Compact producer.
R1_READER_VALIDATION=pending; do not confuse the preceding green lookup
proofs with acceptance of this later reader patch or of all RUN_04.

### CURRENT TRUE STATE — 2026-09-07, shared L2 reader implementation

CURRENT_COMMIT=8cf7081d9cfb995e89daa55bf933dd2da3b43fcc
CURRENT_CUT=RUN_04 OPEN; no closed-cut commit or APK/runtime acceptance yet.
ANCHOR_ORDER=the same supplied max-Phi rule now generates incident masks on
all original R1/R2/R3 loops. Exact pruning removes only order cuts whose
incident mask is unchanged on both sides and at equality; radical cuts stay.
GENERATED=24/12/30 sectors by shell,264 boundaries,528 directed anchor masks;
max30<=32. R1 remains the first144 masks of this single table, not a second
authority. Higher-shell loops are not clipped to the R1 face plane.
CODEGEN=PASS, Kingston Unity6000.5.9f1; all five generated outputs are built
and proved before the first output is written. Latest log:
/mnt/kingston-unity/Builds/UniscanR1Order/TestResults/canonical-rounding-codegen2.log
CPU_HLSL_ROOT_PARITY=PASS, strict endpoint-bit comparison as well as symbolic
identity and independent analytic containment; no tolerance substituted.
Exact adjacent-float midpoint/RNE decisions now share one emitted source for
plane decoding and interval sqrt. Targeted test1/1,32.11s,06:24:31Z:
/mnt/kingston-unity/Builds/UniscanR1Order/TestResults/canonical-rounding-parity2-results.xml
IMPLEMENTING=endpoint-local synthesis followed by canonical ordered SEAL;
read-only indexed-vertex halo, source-generation validation and bindings;
finite7-site carrier admission; complete-footprint RGB/V reducer and frozen
observation drain; same SSD-backed CPU reader for export.
READOUT_RESOURCE_VIEW=FlowerDetail is SRV during compaction; only the existing
ThreadAtlas draw pool is writable. No extra buffer, queue or authority.
REMAINING=real CompactDirtyFlowerSymbols producer with direct/completed/DIRT
coverage, shared carrier admission consumers and full RGB/V drain closure.
TEST_SCOPE=the preceding targeted root parity is not a full-suite/scene/Quest
result. Last full suite before these changes was392/394; obsolete SSD text
assertion was subsequently fixed and passed its targeted run.
PERF=not measured on Quest. BUILD=editor/codegen only, not an Android APK.
NEXT_CURSOR=finish the actual shared reader/carrier/skin call sites; do not
reopen clipping2,36chambers,Fibonacci order,or the now-fixed RNE parity gap.

### CURRENT TRUE STATE — shared producer and staged observation wiring

CURRENT_COMMIT=8cf7081d9cfb995e89daa55bf933dd2da3b43fcc; RUN_04 still OPEN.
GOAL_PURSUIT=active as returned by get_goal; its old REV-B objective text
does not override the amended REV-C authority above.
IMPLEMENTED=actual CompactDirtyFlowerSymbols count/reserve/emit/skin/page
publication; descriptor-correct shared endpoint halo; requested-COLD mask;
root/L1/L2/skin GPU stage publication using existing counter buffer slot104;
same-observation RGB/V drain and actual FinalizeObservation fixture wiring.
READOUT=real direct carrier classifier + interval-support dual veto + DIRT
coverage are now called, not a no-op/always-unresolved replacement kernel.
STILL_OPEN=parent48 completion/junction decision and cross-carrier partial
direct/DIRT union coverage; final CPU/GPU/export support parity.
BUILD_FINDING=reachable shared integer/RNE callgraph overflows glslang's
default inliner ID bound. Unoptimized SPIR-V is not accepted as a workaround.
FIX_IN_PROGRESS=consolidate repeated scalar calls into fixed ordered endpoint
loops in the SAME codegen (UnpackPlane, interval divide/sqrt); no changed
rounding, epsilon, geometry, source ontology or extra dispatch.
CODEGEN=bounded-inline-codegen2/3/6 PASS on Kingston Unity6000.5.9f1;
attempts4/5 exposed stale export fixture calls, fixed before6. Latest
bounded-integer-codegen PASS includes exact integer division/sqrt and the
generated whole-wedge owner incidence. Logs under
/mnt/kingston-unity/Builds/UniscanR1Order/TestResults.
READOUT_COMPILE=CompactDirtyFlowerSymbols glslang+spirv-val PASS with fresh
bounded-readout3.spv: 256 lanes,20 storage bindings(13 SRV/7 RW),13928 bytes
groupshared. This is a compiler/resource result, not a Quest runtime result.
REMAINING_COMPILE=the earlier Drain inliner overflow is now fixed; fresh
staged-drain-integer.spv passes ordinary glslang and spirv-val(vulkan1.1).
Reflection:17 storage buffers(7 RW/10 RO),zero RW/read aliases,128 lanes,
20624 bytes groupshared. No relaxed ID/buffer/workgroup limits are used.
EXPORT=ordinary GLB/Tiles now use the frozen shared reader, complete-support
dual veto, DIRT coverage, captured-color/V material bake and canonical owner
mapping. Shell/Membrane producers and float weld removed with old consumers;
writer/RTC/progress/design test sources migrated, NOT RUN.
VALIDATION=full current suite/APK/device/performance NOT RUN. RUN_04/05/06
are not closed merely because individual producer/compiler seams now work.
NEXT_CURSOR=completion/junction decision and cross-carrier partial coverage.
For the latter, generate the finite original-knot edge incidence alongside
the existing whole-wedge owners, then consume the same CPU/HLSL mapping.
Do not restart the geometry/contract investigation or claim an APK from a
native-only build.

### CURRENT TRUE STATE — integer-kernel implementation checkpoint

CHECKPOINT_PARENT=8cf7081d9cfb995e89daa55bf933dd2da3b43fcc.
CURRENT_COMMIT=the git HEAD containing this checkpoint; no self-referential
commit hash is embedded. RUN_04=OPEN; RUN_05=IMPLEMENTING; RUN_06=IMPLEMENTING.
FILES_CHANGED=shared codegen/numerics, actual procedural producer, staged
same-observation RGB/V drain, generated owner incidence, frozen CPU reader,
GLB/Tiles consumers and presentation material bake; migrated writer fixtures.
NUMERIC_AUTHORITY=unchanged tight directed binary32 division and canonical
round-to-nearest sqrt. Integer quotient/remainder(max23 fraction bits) and
restoring sqrt(exact24 bit-pairs) replace the approximate-hint search graph.
Signed zero/subnormal/carry/overflow decisions remain explicit IEEE-bit
operations. No epsilon, approximate geometry or relaxed parity assertion.
EVIDENCE=Kingston Unity codegen PASS; ordinary production Drain compile and
SPIR-V validation PASS. Fresh integer-readout glslang compile and
spirv-val(vulkan1.1) PASS. These are build checks, not a runtime test suite.
TESTS_RUN=no new Unity/runtime suite in this implementation checkpoint.
PERF=not measured; APK=not built; QUEST_DEVICE_RUNTIME=not run.
MANUAL_AUDIT=changed numerical equations checked including subnormal
normalization, odd negative sqrt exponent and finite upper overflow;
this is not a full cut/contract/legacy closure audit.
LEGACY_REMOVED=export Shell/Membrane solvers, their old producer overloads
and obsolete solver-specific tests; writer tests now use presentation input.
Unrelated .claude/CLAUDE files are excluded from this checkpoint.
DEFERRED_DEPENDENCIES=parent48 completion/junction predicate; cross-carrier
partial direct/DIRT union; final shared-reader/parity/runtime/perf validation.
NEXT_CUT=finish those existing RUN_04/RUN_05 seams; do not mark any run PASS
merely because production shaders now compile.

### CURRENT TRUE STATE — shared coverage, R3 metric reader and telemetry

CHECKPOINT_PARENT=239863f; CURRENT_COMMIT=git HEAD containing this entry.
RUN_04=OPEN; RUN_05=IMPLEMENTING; RUN_06=IMPLEMENTING.
IMPLEMENTED=AcrossEdge2304 generated original-knot incidences with unique
partner/orientation/involution proof; actual GPU and CPU direct-coverage
OR-union with exact paired edges, covered witness and remaining boundary.
CPU export caller now consumes the same union. Both enumerate the same
512-owner page domain; a partner outside that domain never silently cancels
an edge. Cross-page partial coverage remains unresolved, not false FULL.
IMPLEMENTED_R3=shared actual endpoint-local synthesis/SEAL metric reader,
q[4]/qs/qv/chirality and resolved/exact-zero/provisional masks; finite
zero-containing residual is not relabelled exact zero. Child frame parity
uses generated integer endpoint K_L, never a manual +L flip.
IMPLEMENTED_COMPLETION_CORE=one emitted CPU/HLSL 48-petal boundary predicate,
derived and proved from the existing signed72x48 incidence. It accepts only
an immutable direct mask plus separately proved candidate masks and returns
0/1/>1. The full parent/junction admission consumer is STILL NOT COMPLETE.
IMPLEMENTED_TELEMETRY=actual draw timestamps, Android scheduler progression,
sum of native quanta belonging to one frozen observation, existing sampled
counter logging, and individual/storage-batch Transfer/Gather/Install/ACK
timestamps. No new synchronous readback, GPU queue or native resource ABI.
Counter87/97 describe the last successful compact page, not visible totals.
CODEGEN=shared-coverage-codegen and shared-coverage-telemetry-codegen PASS
in Kingston Unity6000.5.9f1. Generated-table proofs ran during emission.
GPU_COMPILE=actual Drain/Finalize ordinary glslang+spirv-val(vulkan1.1) PASS;
Drain7RW/10RO,128lanes,20624B GS; Finalize4RW/7RO,128lanes,12B GS; no aliases.
Actual Compact ordinary glslang+spirv-val(vulkan1.1) PASS;
shared-coverage-readout.spv:7RW/13RO,256lanes,15464B GS,4177632B SPIR-V,
zero writable-image bindings and zero RW/read aliases. No compiler limit raised.
TESTS_RUN=no new Unity/runtime suite; APK=not built; PERF=not measured.
MANUAL_AUDIT=targeted source/equation inspection only; full cut closure NOT PASS.

### R1_BOUNDARY_GATE — exact counterexample; contract text unchanged

STATUS=mathematical admission blocker for literal sections7.1/8.4.2,
confirmed independently against current generated tables and CPU/HLSL readers.
Take the physical plane z=0 and any dyadic level with lattice step h=a_L.
For R1 directions +/-X or +/-Y, the shared loop centre has z=h*Kz and
radius rho=sqrt(3)*h/2<h. It intersects z=0 only for integer Kz=0.
The resulting unit phases in the canonical X/Y loop bases are (+/-1,0).
Those are actual generated corner/corner power-order boundaries: the two
incident corners differing by +/-Z have equal Phi on this entire plane.
For R1 directions +/-Z the complete loop instead lies in the parallel plane
z=h*(Kz+/-1/2), so no integer Kz gives an isolated root on z=0.

Authority.cs ClassifySector requires BOTH interval Cross.Lower>0;
generated HLSL M8FlowerRootSector has the same strict predicate.
Even an exact singleton boundary root fails. Every sound quantization/sensor
enclosure containing the actual plane root therefore fails too; re-observing
or refining cannot remove that true boundary point. This applies to R1,
not to missing R2/R3, and already prevents L0 certain-sector admission.
The same arithmetic holds at every dyadic level; there is no child escape.

EVIDENCE=Authority.cs EvaluateLoop/ClassifySector and generated X/Y sector
endpoints(+/-1,0); contract8.4.2 explicitly classifies touching as AMBIGUOUS.
This is an exact geometric counterexample, not an unrun fixture reported PASS.
IMPACT=literal admission cannot reconstruct an exactly lattice-aligned wall
as a certain direct R1 surface. It conflicts with the functional flat-wall/
all-orientation closure target. Compilation does not resolve this condition.
NO_WORKAROUND=no jitter, rotated hidden lattice, epsilon, fitter, fabricated
root/sector or legacy surface fallback has been introduced.
MINIMAL_NATIVE_CORRECTION_DIRECTION=distinguish a mathematically certain
root on a generated sewing boundary from an unresolved physical root;
resolve the compatible boundary incidence/ownership by the frozen generator
while retaining its complete metric enclosure. This requires an explicit
amendment of strict-sector admission and its consumers, not an unauthorized
replacement of > by >= or the removal of interval uncertainty.
DECISION_REQUIRED=authoritative boundary-root admission semantics. Contract
prefix remains byte-for-byte unchanged pending the user's decision.
NEXT_CURSOR=do not reopen numerical/codegen compile work or invent junction
chirality; finish independent integration only, then resume R1/completion
admission from the explicitly approved boundary amendment.

### CURRENT TRUE STATE — cross-page coverage and release/resume consumers

CHECKPOINT_PARENT=08be6ed; CURRENT_COMMIT=git HEAD containing this entry.
RUN_04=OPEN; RUN_05=IMPLEMENTING; RUN_06=IMPLEMENTING; RUN_07=NOT_RUN.
CONTRACT_UNCHANGED; R1_BOUNDARY_GATE above still requires the user's decision.
IMPLEMENTED_COVERAGE=CPU/live coverage now includes neighbouring M8 owners
inside the fixed origin+[-2,10]^3 source bound:2197 positions minus512 central
owners, at most1685 neighbours. The bound follows from generated loop centres
within a/2 and radius at most3a/2; it excludes sources only, never proves
coverage. Actual generated AcrossEdge/Sigma/metric predicates still decide.
No neighbouring carrier is published into the current page/export geometry.
No remaining candidate DIRT face means no neighbour traversal. COLD/overflow
remains unresolved. Existing27-tile generation-checked halo supplies both
source and target refs; no new coordinate index, buffer or geometry lookup.
COMPACTION=central count, neighbouring coverage and central emit now use ONE
syntactic evaluator call in one WG/queue lease. Prefix/allocation follows
the complete coverage barrier. The first duplicated-call attempt emitted
optimizer ID-overflow diagnostics; it was replaced, not accepted or bypassed
by compiler-limit flags. The final shared-call ordinary glslang compile PASS.
IMPLEMENTED_RESIDENCY=new installation is classified at stationary head
using the same AABB metric as translation, checked by slot generation AND
logical tile. Ordinary already-published source edits do not reclassify.
Retired installation clears its residency receipt; cull remains view-only.
IMPLEMENTED_SCHEDULING=one transient circular cursor in existing directory
control offset8 makes COLD/capacity retries yield to other dirty pages.
Selection remains three fixed bit levels, not a HOT/world traversal.
IMPLEMENTED_LIFECYCLE=actual GPU release first completes the existing dirty
append/manifest SAVE. Session/anchor metadata is captured before teardown
awaits; final integration count follows drain. Disabled Update cannot stall
SAVE because it pumps the existing storage retirement. Failure retains GPU
resources; cancellation does not mark them released. Actual release records
the committed state, restored through existing manifest/index replay before
Start/Save/GLB/Tiles. Ordinary retained STOP/PAUSE causes no save/reload.
IMPLEMENTED_UNIFORMS=explicit little-endian scalar/vector/matrix writes remove
per-field temporary arrays. Page quanta reuse table/resource arrays and exact
serialized payload capacity; native CreateJob still copies before returning.
ABI bytes, column-major matrices and immutable held observations unchanged.
CODEGEN=cross-page-lifecycle-codegen.log Kingston Unity6000.5.9f1 PASS.
GPU_COMPILE=cross-page-readout-shared-compile.log ordinary glslang PASS;
cross-page-readout-shared.spv spirv-val(vulkan1.1) PASS;7RW/13RO,no images
or RW/RO aliases,256lanes,15472B groupshared,4221804B SPIR-V. No limits raised.
TESTS_RUN=no new test suite or benchmark. APK=not built; QUEST_RUNTIME=not run.
MANUAL_AUDIT=changed callers, generation/lifetime paths and bounded addressing
reviewed; this is NOT a full cut/contract/legacy closure PASS.
NEXT_CURSOR=R1 boundary admission and full parent48 completion/junction
consumer remain open; no return to same-page-only coverage or legacy export.

### CURRENT TRUE STATE — authorized exact-boundary admission, in progress

CURRENT_COMMIT=93d6a5fd08f3fa976eba6f0fa4b20b57d32a31b1 (checkpoint, pushed).
CURRENT_CUT=RUN_04; RUN_05/06 consumer implementation remains in this tree.
USER_DECISION=exact symbolic boundary equality is now explicitly authorized;
the earlier R1_BOUNDARY_GATE decision requirement is superseded, not retried.
CONTRACT_AMENDMENT=7.1/8.4.2 and matching analysis/proof clauses updated in
REV-C and its exact lasttrue prefix. Whole metric intervals remain unchanged.
IMPLEMENTING=generated common half-open owner; exact decoded-plane/boundary
equality using integer/surd algebra; transient witness in existing scratch;
CPU/HLSL root, phase, reduction and L2 containment consumer propagation.
PROHIBITED=no >= shortcut, epsilon, interval clamp, midpoint equality or
boundary witness selected by PrecisionKey. Mere numeric contact is ambiguous.
TESTS_RUN=none in this implementation continuation; compiler/codegen pending.
NEXT_CURSOR=finish shared boundary emission and its consumers, generate with
Kingston Unity, then continue actual parent48 completion/junction admission.

### CURRENT TRUE STATE — exact-boundary implementation checkpoint

CHECKPOINT_PARENT=93d6a5fd08f3fa976eba6f0fa4b20b57d32a31b1.
CURRENT_COMMIT=git HEAD containing this entry; CURRENT_CUT=RUN_04 (OPEN).
BOUNDARY_AMENDMENT=implemented in common CPU/HLSL authority and consumers.
EXACT_PROOF=generated rational/surd boundary predicates evaluate original
decoded plane inputs, not rounded ABC/root midpoints. GPU uses bounded
uint32 limbs; CPU oracle/export uses exact integer arithmetic. No geometry
readback, new persistent field, epsilon or enclosure clamp was introduced.
OWNERSHIP=the canonical endpoint's first incident face fixes one common
sector with its frozen Node tie. Each endpoint retains its own exact local
flag mask. Requiring both local flags to choose the same open-sector side
was an implementation error exposed by codegen and has been removed.
WITNESS=boundaryLocal+1 occupies six existing transient scratch bits21..26.
It survives inherited identity and agreeing SEAL/shared-incidence proofs;
nonzero rotation discards it and must prove its result anew. Root reductions
merge proof metadata separately from physical identity, in order-independent
AND/OR and interval intersection operations. No representative selects proof.
L2_CONSUMERS=actual original knot/frame and retained witness reach final flag
containment. Position, normal and orientation determinant intervals remain
unchanged. The packed lookup is9128B (78 distinct48-bit masks); boundary
coefficients/owners8448B and endpoint boundary masks4224B are immutable.
NO_FALSE_CERTAINTY=raw stereo evidence without an exact canonical equality
witness remains strict/ambiguous. A numerical touch is never promoted by
this amendment; the original scalar root-degeneracy gates still apply.
CODEGEN=exact-boundary-codegen-final.log Kingston Unity6000.5.9f1 exit0;
generated C#/HLSL tables emitted. Exact contract-prefix cmp and diff-check PASS.
COMPILER=CompactDirtyFlowerSymbols,FlowerCommit,DrainObservationRefinement
ordinary glslang(vulkan1.1) compilation and spirv-val PASS. No relaxed flags.
READOUT_RECEIPT=exact-boundary-readout.spv:7RW/13RO,0images/aliases,
256lanes,15472B groupshared,4594356B SPIR-V. No new resources/dispatches.
EVIDENCE_ROOT=/mnt/kingston-unity/Builds/UniscanR1Order/TestResults.
TESTS_RUN=no full suite/scene fixtures/benchmarks; PERF/QUEST_RUNTIME=not run;
APK=not built. Compiler success is not runtime or whole-RUN04 acceptance.
MANUAL_AUDIT=boundary provenance, half-open endpoint transport, full enclosure
preservation and changed CPU/GPU reader/reduction callsites reviewed.
NEXT_CURSOR=do not reopen clipping2, canonical boundary admission, codegen
rounding or previous cross-page coverage. Remaining RUN04 implementation is
the actual parent48 direct snapshot and finite R3 junction-class selector
feeding the existing CompletionCandidates/UniqueCompletion predicates.
Those predicates currently have no production caller; the R3 metric bundle
alone is not a junction-class proof. This is remaining implementation, not
a new mathematical/environmental blocker or a closed cut. RUN05/06 retain
their implemented consumers; RUN07 final validation/APK is still NOT_RUN.

### CURRENT TRUE STATE — generated R3 selector and boundary transport follow-up

CHECKPOINT_PARENT=40aec96; CURRENT_COMMIT=git HEAD containing this entry.
CURRENT_CUT=RUN_04 (OPEN); no RUN/CUT or final acceptance is claimed.
CONTRACT=the authorized CERTAIN_BOUNDARY amendment remains unchanged, including
its full enclosure, canonical half-open ownership and no-epsilon rules.
BOUNDARY_FOLLOWUP=the remaining GPU R3 commit and L1 ancestry rotation now use
the same typed RotatePhaseEvidence as CPU. A nonzero rotation cannot retain
an old exact-boundary witness. No phase/metric interval is clamped.
R3_IMPLEMENTED=Petal f<e<c induces Q=[f,e-f,c-e]. Exact generation applies Q
to all four tetra corners and their corresponding face/edge/corner flags;
actual strand incidence and determinant are checked. Four equivalent start
flags deduplicate to one class:12 classes,96 bytes, no runtime branch ordinal.
The common CPU/HLSL selector enumerates the16 algebraic sign bundles and
those12 incidence classes. It uses root sector/boundary masks, frozen
chirality, explicit direct/dual constraints and ordered q/qs/qv intervals.
CERTAIN_CLOSURE,AMBIGUOUS,DETAIL_CANDIDATE and IMPOSSIBLE remain distinct;
a finite/nonzero metric bundle alone is not a junction closure.
GPU_CONSUMER=DrainObservationRefinement now invokes that selector at the
original-phase boundary, replacing its old four-persistent-residual shortcut.
Eight axis/sign alternatives are evaluated once each; only q/tag evidence
is retained. A shared mini dual-halo cache supplies full root-support covers
without per-root spatial hashing. No new buffer, dispatch or queue exists.
R1/R2 protection and observation-local draining remain unchanged.
CPU_CONSUMER=SnapshotReader.ReadR3Junction mirrors the same eight alternatives
and uses the same bounds/full-cell-cover predicate as evaluated wedges.
PARENT48_PARTIAL=CPU snapshot input can AND all16 actual L2 children of each
source petal through the existing128-carrier pass and exclude provisional
anchors. CPU/GPU source admission now exposes that direct/provisional
distinction independently of drawable R1. This is not an emitted completion.
COMPLETION_OPEN=CompletionCandidates/UniqueCompletion still have no production
caller. The actual donor-root candidate admission and completed-petal output
must be connected; reusing own-source direct admission for both D and the
candidate would make C AND NOT D empty. Do not call this finished, fabricate
all-true shell masks, or weaken canonical half-open ownership to hide it.
EXPORT_FIX=measured patch counts exclude completed wedges; inferred counts
count them exactly once. Existing COMPLETED symbol/GLB/Tiles metadata remains.
CODEGEN=r3-junction-codegen-final.log, Kingston Unity6000.5.9f1 exit0. Exact
incidence generation guards ran during emission; this is not a fixture suite.
COMPILER=FlowerCommit,DrainObservationRefinement,CompactDirtyFlowerSymbols
ordinary glslang(vulkan1.1) compile and spirv-val PASS. The first compile
exposed the reserved HLSL identifier line; source generator was corrected
to lineClass and regenerated, not patched only in generated output.
DRAIN_RECEIPT=7RW/13RO,128lanes,22804B groupshared,0unknown GS types,
no RW/SRV aliases;5283176B SPIR-V. Readout4604224B;FlowerCommit2229044B.
EVIDENCE_ROOT=/mnt/kingston-unity/Builds/UniscanR1Order/TestResults.
TESTS_RUN=no full suite/scene fixtures/benchmarks; PERF/QUEST_RUNTIME=NOT_RUN;
APK=NOT_BUILT. Compile/reflection receipts are not device acceptance.
FILES_REVIEWED=Junction,Authority,Codegen,Reader,CarrierAdmission,Geometry,
Refinement,Support,Exporter and generated output. Legacy residual-only R3
diagnostic removed; no legacy geometry authority restored.
NEXT_CURSOR=actual parent48 donor/completion producer and its GPU/readout
consumer; do not redo clipping2, CERTAIN_BOUNDARY, generated12-class R3,
mini dual cache, previous coverage/publication or durable release work.

### CURRENT TRUE STATE — unique completion connected; one readout evaluator

CHECKPOINT_PARENT=f57577ea3e2f9d4d6667672121129bc5c4afa15d;
CURRENT_COMMIT=git HEAD containing this entry. CONTRACT_UNCHANGED: the complete
111444-byte amended REV-C remains the exact prefix of this file.
IMPLEMENTATION_CURSOR=the outstanding RUN_04 parent48 completion producer is
now connected to actual GPU readout and CPU/live-export consumers. RUN_04,
RUN_05 and RUN_06 implementation is ready for the consolidated RUN_07 checks;
this is NOT a claim that their unexecuted acceptance fixtures have passed.
COMPLETION_IMPLEMENTED=freeze D from all16 actual L2 children of each of the
48 parent petals, before output ownership. Provisional anchors cannot enter D.
Generated boundary-neighbour masks are the proved specialization of Bx.
Each candidate reads its required CONFIRMED edge-donors' actual shared roots;
every donor must agree on the same symbolic endpoint and compatible interval.
The generated R3 selector is restricted to the candidate's incident classes
before counting. Actual orientation, all16 child closures and whole-support
dual predicates must pass. Any still-possible ambiguous candidate prevents
unique completion. Only exactly one candidate supplies a transient 9-bit
petal/sign token; no result feeds D or recursively seeds another completion.
BOUNDARY_REFERENCES=528 immutable closed-incidence masks are generated by the
existing exact Phi-order algebra, not guessed signs. Canonical half-open
ownership remains UNIQUE. Only a proved CERTAIN_BOUNDARY witness from an
actual confirmed donor may reference its adjacent closed petal. Intervals
are never clamped or narrowed. Translated L1/L2 boundaries retain their own
loop-specific proof; original-loop references are not applied to them.
CONSUMERS=actual carrier COMPLETED masks reach procedural pages and existing
GLB/3D Tiles metadata. CPU Parent48Snapshot caches only frozen evidence and
requires its own completion receipt. GPU count/emit uses the same immutable
page lease and512 transient uint tokens (2048B groupshared); no new persistent
authority, SSBO, native resource, dispatch or queue was added.
SHADER_FIX=ordinary compiler initially exhausted SSA IDs because partner-edge
coverage inlined a second complete PageCarrier. The nested call was removed:
own/source/child/peer evaluation now shares ONE call site. The fixed generated
peer mapping and exact PairAxes proof are unchanged, as are coverage bounds.
Only one carrier is retained while each needed peer is consumed; no page XYZ
cache exists. Heavy3-vertex/3-axis/2-half interval bodies use bounded loops,
not forced unrolling. R3 raw prediction is retained BEFORE ApplyGeometryDetail
in the same local->peer reader; its duplicate raw root calculation is gone.
COMPILER_POLICY=unchanged ordinary glslang vulkan1.1 and unchanged spirv-val;
no raised ID/hardware limit, no variable-pointer capability, no -Od artifact
published. Diagnostic optimizer files are evidence only, not production input.
CODEGEN=completion-wiring-codegen.log and completion-final-csharp.log:
Kingston Unity6000.5.9f1 exit0; generated guards executed, not a fixture suite.
READOUT_RECEIPT=completion-one-evaluator-CompactDirtyFlowerSymbols.spv validates;
7RW/13RO,0images,0RW/SRV aliases,256lanes,17520B groupshared,3773NoContraction,
noFP64,only Shader capability. Source-native fusion fixed the compiler failure.
Final loop/raw-base source compilation also exited0; final module reflection
and affected Commit/Drain compiler checks are in progress at this cursor.
COVERAGE_SCOPE=COMPLETED geometry participates in actual direct/support coverage.
Across-peer cancellation still requires that peer's ordinary direct proof;
an unproved completed peer remains conservative partial/AMBIGUOUS, never a
false complete-cover proof. No recursive parent48 solve was added per edge.
FILES_CHANGED=Authority,Boundary,Junction,Reader,Presentation,Codegen and both
generated tables; FlowerGeometry,FlowerSupport,MerkabaReadout.compute.
MANUAL_REVIEW=changed producer, donor identity, interval/tag transport, candidate
filtering, readout call graph and export receipt consumers read against the
contract. This targeted review is not FINAL_CONTRACT_AUDIT or device acceptance.
TESTS_RUN=no new full suite/scenes/benchmarks in this implementation continuation.
PERF=NOT_MEASURED; QUEST_RUNTIME=NOT_RUN; APK=NOT_BUILT. Compiler size/bindings
are not a frame-time claim. Existing earlier receipts remain historical.
LEGACY_REMOVED=duplicate nested readout evaluation and duplicate raw R3 root
work; no superseded surface authority or fallback was restored.
NEXT_CURSOR=finish final module receipts, commit this implementation checkpoint,
then execute the existing consolidated RUN_07 validation/native/Unity APK
workflow and fix concrete failures. Do not repeat the parent48 wiring or
reopen clipping2, exact boundary ownership, fixed399/Fibonacci or the DAG.
PURSUIT_PRODUCT_STATE=existing goal is still BLOCKED in the product. An actual
create_goal attempt failed because that unfinished goal already exists; no
available tool resumes it. Work continues, but automatic pursuit is not
claimed active and the old unfinished objective is not falsely marked complete.

### RUN_07 consolidated validation / repair cursor — 2026-09-07

CURRENT_COMMIT=b4054557ab9f935dfa11dfd576a9ead01ee449e4; pushed to the existing
refactor/m8-dual-sphere-flower-rev-b remote branch. Current repairs are uncommitted.
CONTRACT=unchanged; REV-C remains the immutable byte-identical prefix.
MODULE_RECEIPTS=completion-final Compact/Commit/Drain ordinary glslang and
spirv-val PASS. Respectively: 7/8/7 writable storage bindings; 17520/26752/22804
bytes groupshared; 256/128/128 lanes. These are NOT device performance results.
NATIVE_BUILD=completion-native-build-fixed.log PASS: all25 executor pipelines
embedded and Android native plugin built. Fixed reflection uses actual emitted
SPIR-V descriptor-variable identity, not omitted/dead source parameters.
LEGACY_REMOVED=CanonicalGeometry/OverlapShell CPU authorities, both old editor
generators, their generated HLSL and obsolete tests/metas; recoverable in Git.
FIRST_FULL_SUITE=Run07/TestResults/merkaba-results.xml: 275/336 passed,61 failed.
REPAIRS=Unity-incompatible struct ternaries replaced in codegen with identical
ordered if/else expressions and regenerated; exact singleton hinge witness
preserves the complete interval; bounded drain fixtures use actual certain
sectors and nonzero records; viewer/design now consume the writer's submeshes,
material factors and captured textures. No contract epsilon or new geometry.
RERUN=Run07Repair01/TestResults/merkaba-results.xml: 330/338 passed,8 failed,
0 skipped, completed exit2. All generated CPU/HLSL parity tests passed and no
C#/HLSL compile error remains in that run. Six failures share the optional
GLB baseColorTexture presence bug; CountReserveEmit exceeded its unchanged
30-second test timeout during the cold run; GPU Drain lacked three dual SRVs.
The three real dual backing buffers are now bound in the fixture; no uploaded
roots, threshold changes, timeout increases, or empty-output parity claims.
GLB presence and exact linear four-tap eyedropper repairs are now source-ready,
with concrete row/texel/blend and malformed-input fixtures. These last repairs
have not yet been rerun in Unity; source-ready is not a test PASS.
COMPLETION_BLOCKER=do NOT apply the initially suspected boundary-reference-only
fix. It is insufficient. Both required direct edge-neighbours must reference
the same physical R1 knot, but even their closed face-sector domains are
disjoint. This was checked against all48 actual generated neighbour rows and
every corresponding R1 open-sector and closed-boundary reference mask.
EXACT_COUNTEREXAMPLE=petal0 has f=(-1,0,0), e=(-1,-1,0), c=(-1,-1,-1).
Its required face donors are1 and4; the third edge neighbour is16. In the
contract chart: p0 has y<=z<=0; donor1 has z>=0 and y<=-z; donor4 has z<=y<=0.
A shared F knot forces z=0 and y=z, hence y=z=0, but its R1 loop requires
y*y+z*z=3. The clipping2 bound and exact boundary equality cannot fix this.
GENERAL_PROOF=write u=s_j*xi_j, v=s_k*xi_k. The candidate domain is 0<=v<=u,
its FE neighbour is 0<=-v<=u and its FC neighbour is 0<=u<=v. Their common
knot would require u=v=0 against u*u+v*v=3. Signed permutations cover all48.
DECISION_REQUIRED=the face-sector admissibility applied to shared direct
anchors and the required same-knot incidence closure cannot both produce a
positive completion fixture. No existing sign/child transport changes this
original R1 knot identity. Do not weaken closure, widen masks, move/clamp roots
or claim a positive fixture. Contract amendment/interpretation requires the
user's explicit decision; contract text has NOT been changed.
RUN_STATUS=RUN04/05/06 acceptance remains OPEN; RUN07 fixes in progress.
APK=NOT_BUILT; QUEST_RUNTIME=NOT_RUN; PERF=NOT_MEASURED.

### RUN_04 approved ownership/incidence correction — 2026-09-07

USER_DECISION=explicitly approved: RootOwner is canonical half-open sector/flag
ownership; PetalUsesRoot is generated incidence and MUST NOT require equality
with RootOwner. The previous completion blocker is authorized for correction.
CONTRACT_CHANGED=only this approved clarification in sections8.4/8.4.2/13.2
and its proof obligations in29; the same exact text is applied to REV-C and
the lasttrue.md contract prefix. Geometry, root identity, intervals, original
and dyadic loop equations, canonical owner tie-break and four authorities stay.
IMPLEMENTATION_CURSOR=replace ownership-mask admission with generated anchor
incidence for direct and completed geometry. Keep actual selected direct
donor root receipts; do not invent/reselect signs from ownership regions.
CPU=CarrierAdmission/Reader/Junction. GPU=FlowerGeometry/FlowerSupport and
single authoritative codegen. Generated output is refreshed only by codegen.
VALIDATION_PENDING=affected CPU/HLSL sharing proofs and actual positive parent48
completion; previous330/338 suite predates this approved correction. Existing
GLB and drain-binding fixes remain source-ready, not newly claimed PASS.
CURRENT_COMMIT=b4054557ab9f935dfa11dfd576a9ead01ee449e4; no closed-run commit
has been claimed for the unvalidated correction. RUN04 remains open until
actual closure; do not reopen the superseded ownership-intersection problem.

### RUN_04 ownership/incidence validation cursor — 2026-09-07 13:15Z

IMPLEMENTED=canonical ownership and shared anchor incidence are separate in
CPU/GPU admission and completion. Direct donor receipts retain actual chosen
roots; no owner-interior intersection, boundary-only reference exception,
root re-ranking, extra dispatch or persistent XYZ was added. Obsolete
BoundaryReference/L2BoundaryFlag tables and two unused wedge-reader wrappers
are removed. Existing exact boundary ownership and full enclosures remain.
CODEGEN=PASS; ownership-incidence-codegen.log; generated hash=0xc0f90ea1.
CONTRACT_PREFIX=byte-for-byte cmp PASS after the authorized amendment.
SPIRV=69/69 PASS; ownership-incidence-compute-audit.log. Compact/Commit/Drain
RW=7/8/7; groupshared=17520/26752/22804 bytes; no alias or FP64. The two dead
wrapper deletions followed this audit and do not change its active callgraph.
NATIVE_BUILD=25 pipelines and Android ARM64 plugin PASS;
ownership-incidence-native-build.log. This is NOT an APK or Quest runtime test.
TESTS_RUN=Run07Repair02/TestResults/merkaba-results.xml, actual Unity/Vulkan:
349/352 passed,3 failed,0 skipped; duration440.387s; no C#/HLSL compile errors.
Actual generated CPU/HLSL parity, ownership/incidence parity, GLB capture
material/texture tests and unchanged CountReserveEmit test PASS.
FAILURES=GPU frozen-observation drain commits no nonzero R2; two new honest
flat-wall positive fixtures emit no direct carrier. Empty outputs are failures,
not eager/drain or surface proofs. Positive parent48 completion remains unproved.
BOUNDED_DIAGNOSIS=same frozen world planes X/offset0 and (1,2,3)/offset0.006,
all eight arithmetic overlap owners333+(0/1)^3, mandatory quantization bounds:
active=0 and D=0. Axis wall already has ambiguous original roots; oblique wall
has certain triples but no agreement in the finite carrier combiner. Merely
grouping by hub sign does not solve it. This is NOT a proved contract conflict.
NEXT_IMPLEMENTATION=actual GPU drain gate; generated parent/child branch
constraints in admission. No arbitrary sign, reduced bound or replacement
geometry is authorized. RUN04 remains OPEN; APK NOT_BUILT; Quest NOT_RUN.

### RUN_04 exact codec and ancestor-branch cursor — 2026-09-07 13:45Z

CODEC_REPAIR=the shared CPU/HLSL oct decoder now normalizes exact integer
code-centre numerators. The former early binary32 division by1023 destroyed
equal-component identities (u=v=682 produced unequal x/y/z), preventing exact
boundary proofs. This is evaluation of the same rational oct centre, not a
new codebook, geometry equation, tolerance, field or pipeline stage. Its
binary32 output intentionally corrects the premature rounding; it is not
claimed bit-identical to the previous faulty decoder.
CODEGEN=exact-oct-codegen.log PASS. TARGETED_TESTS=exact-oct-targeted-results.xml:
1/5 PASS,4 FAIL; includes one temporary stage diagnostic in addition to the
three existing failures. Exhaustive1024^2 codec symmetry/error proof PASS;
max vector error8.9268448371064223e-8 < gamma3=1.788139663006007e-7.
GPU_EVIDENCE=actual frozen-pixel task4/6 now reaches CERTAIN parent prediction,
measured endpoint reduction and SEAL. Residual excludes zero, but its full
outward-Q2.29 synthesis crosses a sector boundary: correctly AMBIGUOUS, not a
zero-residual or missing-binding failure. The original positive test input is
not a proved representable R2 fixture. Gates and quantization bounds remain.
READOUT_REPAIR_IN_PROGRESS=generated ancestry consistency for seven carrier
sites: a child prediction and a selected site naming its actual ancestor
must use the same rootSign already consumed by that prediction. Derive the
finite mask from existing phase-family incidence, never from observed scores,
owner-cell clipping, normal-angle choices or a new branch convention. Both
sign alternatives and all remaining ambiguity are retained.
NEXT=finish CPU/HLSL generated-mask wiring; regenerate; run affected parity
and an actually representable frozen-pixel drain fixture. Keep the failing
axis-wall fixtures; do not replace them with an easier orientation and call
flat-wall closure proved. No new run closure, commit, APK or device PASS.

### RUN_04 / APK compiler repair cursor — 2026-09-07

ANCESTRY_IMPLEMENTED=finite masks derived from existing phase-family incidence
are consumed identically by CPU and HLSL; generated authority hash0x618c4aa1.
Run07Repair03 actual Unity/Vulkan=353/357 PASS,4 FAIL,0 skipped. The two actual
ancestor-mask GPU parity proofs and both N111 shared-knot fixtures PASS.
Two failures were unchanged Count/Drain timeouts during cold compilation;
the new nonzero frozen-pixel drain result is NOT yet proved. Two other failures
were synthetic quantized X/offset0 snapshots incorrectly asserted as certain.
Those identical X inputs are retained as explicit unresolved/no-emission tests;
N111 positive assertions, production bounds, rules and timeouts are unchanged.
This synthetic M8 fixture has no camera/view angle and is not a device scene.
POSITIVE_PARENT48_COMPLETION=still unproved; current real N111 receipt has D=0.
Neither incidence parity nor the negative empty-parent receipt proves positive
unique completion. RUN04/05/06 acceptance remains OPEN.

APK_ATTEMPT_1=failed actual Android shaders: graphics automatic SRV binding
collisions and legacy FXC/HLSLcc forced-unroll conflict in CompactDirtyFlowerSymbols.
REPAIRS=graphics-only explicit t0..t11 SRVs and t12 environment texture; same
resource names/backing and unchanged compute ABI. Readout uses native Vulkan
DXC like the procedural graphics pass, retaining the bounded generated loops.
No geometry equation, root interval, workgroup size or dispatch was changed.
ACTUAL_GRAPHICS_CHECK=quest-graphics-compile-repair.log PASS for both Android
multiview variants (minimal and all-feature/instancing), exit0; peak4.4GiB.
HOST_MEMORY=APK script now encloses the complete compiler/Gradle process tree
in a systemd scope: MemoryHigh12GiB, MemoryMax16GiB, MemorySwapMax2GiB; two host
CPU cores and two Unity job workers. This does not serialize GPU workgroups.
CURRENT_BUILD=exec session17096, scope run-p3632042-i3678882.scope; driver
/mnt/kingston-unity/Builds/UniscanR1Order/TestResults/quest-apk-build-repair-driver.log.
Native25 pipelines rebuilt successfully; actual Unity Android player build
running. Do not restart a live build or treat the old September6 APK as new.
NEXT=finish current APK; verify actual fresh artifact, then warm final tests
and remaining completion evidence. No final contract/device/performance PASS.

### APK compiler failure / exact continuation — 2026-09-07 15:49Z

APK_ATTEMPT_2=FAILED, exit137. systemd journal for
run-p3632042-i3678882.scope records systemd-oomd killing the build tree at
15:49:22Z; peak12.5GiB RAM plus2GiB swap. The16GiB hard limit was not reached.
Actual Android compilation completed128 graphics variants,9 integration
variants and4 readout variants (57.15s). It stopped during MerkabaWorld's27
variants. No new APK was produced; the September6 artifact remains stale.
REPAIR_IN_PROGRESS=MerkabaWorld now explicitly selects Vulkan DXC, matching
integration/readout. No shader equation, workgroup size, storage layout,
dispatch boundary or ontology changes. This is a compiler-path repair, not
a proved runtime performance improvement. RAM scope limits are unchanged.
NEXT=retry the real bounded Android build and report its actual outcome.

### CURRENT TRUE STATE — fresh Quest APK, 2026-09-07 16:04Z

CURRENT_COMMIT=b4054557ab9f935dfa11dfd576a9ead01ee449e4 plus the existing
uncommitted implementation/repair tree. No new closed-run commit is claimed.
APK_ATTEMPT_3=PASS, exec43382 exit0. MerkabaWorld's27 Android Vulkan variants
compiled through DXC in2.94s, zero local/remote cache hits. The previous
graphics/integration/readout compiler repairs also passed the real build.
Full Unity Android IL2CPP/Gradle build and fresh-mtime/success-marker checks PASS.
APK=/mnt/kingston-unity/Builds/QuestMerkabaScan/QuestMerkabaScan-release.apk
APK_MTIME=2026-09-07 18:04:14.437321800 +0200
APK_BYTES=92206786
APK_SHA256=ff3b6b2b9e7accb8ed02cb61b0d3889e282c9c502da318815175e643bf47f903
PACKAGE=com.genesis.questmerkabascan; versionCode8; versionName0.1.0;
minSdk32; targetSdk36; native-code=arm64-v8a. apksigner verify PASS (v2).
PACKAGED_NATIVE=libMerkabaVulkanTimestamps.so and libil2cpp.so verified present.
BUILD_LOG=/mnt/kingston-unity/Builds/QuestMerkabaScan/build.log
DRIVER_LOG=/mnt/kingston-unity/Builds/UniscanR1Order/TestResults/quest-apk-build-world-dxc-driver.log
HOST_LIMITS=MemoryHigh12GiB/MemoryMax16GiB/MemorySwapMax2GiB remained unchanged.
After all shader compilation completed, only this build scope's CPU affinity
was widened from0-1 to0-7 for C++/Gradle. No GPU execution setting changed.
LAST_MEASURED_MEMORY_PEAK=10863464448 bytes; no OOM in this successful attempt.
QUEST_DEVICE_RUNTIME=NOT_RUN; device performance and full contract closure
are not implied by this test APK. Positive Parent48 completion evidence and
the warm frozen-observation GPU drain parity remain outstanding as recorded
above. RUN04/05/06 acceptance remains OPEN. Do not repeat this APK build merely
because context was compacted; the actual fresh artifact and evidence exist.
NEXT=handoff this APK for testing, then resume the outstanding consolidated
validation/repair cursor; no ontology, contract or geometry redesign.

### User-requested checkpoint / device handoff — 2026-09-07 16:08Z

CURRENT_COMMIT=the implementation checkpoint containing this ledger entry;
parent=b4054557ab9f935dfa11dfd576a9ead01ee449e4. The user explicitly requested
commit and push now. This checkpoint does not claim closed-run acceptance.
QUEST_ACCESS=explicitly reauthorized by the user for install and log capture.
DEVICE=340YC20G7X0QZ4, adb modelQuest_3S; package UID10178.
INSTALL=adb install -r PASS for the exact ff3b6b2b APK above;
device lastUpdateTime2026-09-07 18:08:15, versionCode8/versionName0.1.0.
APP_LAUNCH=left to the user; no am start, force-stop or app-data clear issued.
DEVICE_LOG=/mnt/kingston-unity/Builds/QuestMerkabaScan/DeviceLogs/quest-20260907-180815-apk-ff3b6b2b.log
Log capture is restricted to this package UID from installation time onward;
device log buffers were not cleared. Runtime acceptance remains unproved.
NEXT=observe this installed APK's user-driven run and repair concrete findings.

### CURRENT TRUE STATE — actual Quest startup failure, 2026-09-07 16:36Z

CURRENT_COMMIT=d60e053468c5e592d5635f370f6f0089bea2f8d2, pushed to
refactor/m8-dual-sphere-flower-rev-b. This is the requested checkpoint,
not closed RUN04/05/06 acceptance. Installed APK remains ff3b6b2b above.
DEVICE_FAILURE=actual user launch PID25038 on Quest_3S340YC20G7X0QZ4:
18:09:07 initialization ->18:12:58.951 Adreno "Failed to link shaders",
vkCreateComputePipelines=-13. Native pipeline creation blocks UnityMain
before first frame. The old binary lacks per-pipeline creation labels;
the exact failed entry is therefore not yet established. This is not PASS
and not just a slow loading screen. Device process PSS was about188MiB.
DEVICE_FEATURE_FAILURE=multiDrawIndirect/firstInstance/countDraw supported1
but enabled0; Flower draw unavailable. Feature negotiation repair is in
the worktree, preserving Unity's requested features/const pNext chain.
REPAIR_IN_PROGRESS=per-pipeline driver creation labels/times; bounded ordered
decoder callsites in StereoDepthPlane/ChromaticSupport/LoopSupport; one host
initialization worker so compiler work cannot hold Unity startup. No new GPU
queue, geometry equation, measurement omission, fallback or ABI change.
STEREO_FIRST_MEASURE=depth decoder callsite consolidation:1589116->984084B,
98377->60421 SPIR-V instructions; actual native compile_pipeline, no-Os.
spirv-val PASS; descriptors/uniform offsets/global1012B/WG8x8x1 identical;
no Fma/RelaxedPrecision/FPFastMathMode. Ordered-eye consolidation is newer
than that receipt and is being compiled separately before APK packaging.
REJECTED_COMPILER_SHORTCUT=glslang-Os stereo saved only4.27% bytes; Drain
exceeded a bounded6GiB host compile scope. No-Os switch was adopted.
DEVICE_LOG_CAPTURE=exec68869, package UID10178 only, same DeviceLogs path.
The user controls launch; no force-stop, app-data clear or log-buffer clear.
NEXT=finish actual source compile/native ARM64 build, bounded fresh APK,
install and observe user-driven runtime. Do not relabel this worktree as the
already installed APK or claim the driver failure is repaired without evidence.

### CURRENT TRUE STATE — authorized execution closure RUN_08–14

CURRENT_COMMIT=d60e053468c5e592d5635f370f6f0089bea2f8d2 plus preserved repairs.
EXECUTION_AUTHORITY=MERKABA_CLOSURE_CONTRACT.md, the user-approved corrected
closure, subordinate to immutable REV-C. Downloads copy is the review copy;
this repository copy is the execution reference. No old CUT14/REV-B goal
text overrides the current closure. Product goal is PAUSED; API cannot resume it.
CURRENT_RUN=RUN_08 OPEN. Main=existing native startup/features/cache repair;
parallel bounded work=shader size gate, refinement callsite reduction, RUN_11
export-name/Android Save-As. No geometry or persistence ABI change authorized.
DAG=08->09->10;11 independent;10+11->12;11->13(final wiring10/12);all->14.
COMMITS=first size-gate checkpoint; cohesive run checkpoints thereafter;
never label unvalidated implementation as PASS. Full validation stays RUN_14.
USER_BUDGET=90% implementation, 10% control/tests; only necessary interim checks.
NEXT=commit measuring gate, finish RUN_08 execution consumers; continue DAG.

RUN_08_GATE_CHECKPOINT=exact native payload size/body/GS/binding/ABI receipts
implemented in the existing generator and compute audit. Syntax checks PASS;
full shader gate NOT RUN and known oversized production modules remain FAIL.
This commit freezes the approved closure and measuring gate, not runtime repair.

RUN_08_STARTUP_TABLE_CHECKPOINT=ABI11,43 resources; one12672B/792uint4
read-only sector blob replaces per-invocation constant-array copies. All3168
words match the previous SPIR-V constants. CPU/codegen/HLSL and managed,
native, graphics and oracle bindings use the same generated payload.
Native startup negotiates required supported features, compiles on one host
worker, publishes READY/FAILED, logs each compile directly on Android and
atomically persists a bounded device/driver/shader-keyed pipeline cache.
Stereo/refinement callsites consolidated without changing interval equations.
COMPILE=all25 embedded entrypoints compiled/spirv-val; actual Android ARM64
plugin build PASS in6GiB bounded scope. This is NOT a Unity/Quest APK build.
SIZE_GATE=OPEN/FAIL; Drain4857076B, Stereo859508B/51060body instructions.
TESTS=full suite deferred RUN_14; DEVICE=not run on this source.
NEXT=RUN_08 dirty-page batching/parallel prefix, finite lookup and command graph;
RUN_11 export picker/name and session-note checkpoints proceed independently.

RUN_11_EXPORT_UI_CHECKPOINT=export-name and named GLB/ZIP consumers; Android
ACTION_CREATE_DOCUMENT streams a pinned completed app export, keeps source
intact, reports partial destination failure, retains only actually granted
persistable URI permissions. Current viewer resolves the last named ZIP.
Existing status label displays native startup progress/failure from ABI11.
CHECKS=Android36/Unity Java8 compile and UXML syntax PASS; C#/device pending14.
No exporter geometry or presentation authority was changed by this UI slice.

RUN_11_NOTES_CHECKPOINT=annotations bound to the loaded document/session;
dirty mutations join SaveDesign, Save-As copies the durable file before
storage-root switch, rebind loads the next document. Foreign anchored previews
do not mark another active session dirty. Failed save does not silently close
the live workspace. Existing annotation coordinates/format remain unchanged.
CHECKS=diff syntax only; C#/session round trip remains RUN_14.

RUN_11_WORKSPACE_CHECKPOINT=UI ray ownership before paint/annotation input;
laser displays the same cached hit. Session design stays anchor-local and
uses the evaluated package/display frame for preview and ALIGN; no record
coordinate conversion. paint-row controls follow actual active-tool consumers.
CHECKS=UXML syntax/diff PASS; Unity input/device acceptance remains RUN_14.

RUN_08_FINITE_LUT_CHECKPOINT=same RO blob now27008B/1688uint4 rows;
generated canonical L2 wedge owners, seven carrier knots, ownership mask and
canonical skin source are direct finite lookups. Previous792 rows unchanged;
768owners/896sites/768wedge incidences/128sources match checkpoint authority.
No new measured-plane solver, geometry coordinate or resource was introduced.
COMPILE=combined readout build waits for in-progress batch consumer cut;
isolated Compact attempt hit that unfinished callsite, not reported as PASS.

RUN_13_UI_CHECKPOINT=Library Scans/Exports/Models under session action;
diagnostics in overflow, one named/formatted export action, seven paint tools
and active-only inspector. 144 controller queries resolve to typed UXML IDs.
Actual SDK input order is OVRManager(-100), ray(-50), consumers(0).
COMPILE=actual Android runtime C# response compiled PASS, one pre-existing
PaintEngine member-hiding warning. UI XML/syntax PASS; HMD/USS import pending.
No synthetic coverage, readout LOD, geometry or new scan authority introduced.
RUN_08 remains current: batched native compilation exposed malformed SPIR-V
in PrepareDirtyFlowerBatch; repair in progress, not marked acceptance PASS.

RUN_08_BATCH_EXECUTION_CHECKPOINT:
  CURRENT_CUT=RUN_08; DAG_STATUS=implementation open, acceptance not PASS.
  FILES_CHANGED=Codegen/RO blob; Flower page batch/support; observation bins,
    Commit/Drain/finalization; managed/native queue ABI15; counter consumers.
  IMPLEMENTED=32-page bounded batch with ordered bitmap prefix and per-page
    reservation/abort receipts; parallel512-owner prefix; exact support-bound
    reuse;256-lane page WG. Allocator metadata remains bounded/serialized.
  TABLES=46 arrays moved into existing105600B/6600uint4 RO blob;14500 original
    components and1688-row prefix bit-identical; CPU generated unchanged.
  LEGACY_REMOVED=47 dead counters (105->58), separate observation-counter
    reset and retirement dispatches. Finalize performs its own WG retirement.
  COMMANDS=single generated native schedule; allocation miss/installs use
    GPU indirect packets in existing64B resource.18 scheduled normal commands,
    6 allocation-gated;12 non-allocation commands, not a measured<=10 PASS.
  COMPILE=all25 actual embedded SPIR-V compile/spirv-val/reflection PASS;
    ARM64 native plugin build PASS with6GiB cap; runtime Android C# compile
    PASS (one pre-existing PaintEngine warning). Not a Unity Quest APK build.
  SIZE_GATE=FAIL: Stereo853836B/51060body; Commit1439048B/84303body;
    Drain4789528B/279573body; Compact3332192B/193682body.
  ABI=all25<=8 writable; maximum reflected shared24704B (Commit), no32KiB breach.
  TESTS_RUN=compile checks only; full tests/fixtures/device remain RUN_14.
  PERF=not measured on Quest; no throughput/occupancy claim.
  RECEIPTS=/tmp/m8-native-tables-rQ9o20.log;
    /tmp/m8-runtime-abi15-kdnqMU/compiler.log.
  NEXT=RUN_08 remaining emitted-body duplication; RUN_09 required-support and
    selective invalidation consumers; RUN_10 carrier export/atlas in parallel.

CLOSURE_EXPORT_MUTATION_LEASE_AMENDMENT:
  AUTHORITY=explicit user clarification: scan and canonical mutations stay
    paused for the entire export. Closure sections6.2/6.7 updated in both
    matching copies; immutable REV-C prefix unchanged. No historical MVCC.
  IMPLEMENTED=RoomScanner owns admission/quiesce/durable-cut/export/finally;
    public Exporter entrypoints route through that operation. Existing held
    observation/fine work and SSD append/ACK retire before the ordinary SAVE
    commit selects the source. Existing idle base compaction retires first.
  GUARDS=normal scan/start, FINE/ERASE admission, authority switch, canonical
    clear, direct persistence/session entrypoints and Viewer document mutation
    remain held. Pending immutable retries remain permitted to finish.
  CANCEL=token reaches tile/context/coverage evaluation, GLB/3D Tiles bake and
    writers, DIRT and bounded ZIP copy. Workers are awaited; lifecycle teardown
    signals cancel then awaits export before resource release. Own staging
    cleanup precedes lease release; export does not automatically restart scan.
  STATUS=implementation checkpoint only; full fixture/device acceptance and
    resumable receipt/package completion remain open in the closure DAG.
  CHECKS=diff/source checks only here; consolidated compile coordinated by root.

### CURRENT TRUE STATE — closure shader/optical cursor, 2026-09-07 22:05Z

CURRENT_COMMIT=08e5e46 + current working diff; MERKABA_CLOSURE remains OPEN.
SOURCE_READY=held export/native package; ERASE3 dispatches and ABI16/23 pipelines;
  scanner/page priority; source-capture/local-epoch/peer-ACK invalidation with
  retry touched retention; optical V-1 final RGB consumer + ordered CPU twin.
CODEGEN=finite skin parent work/charts, canonical child creation addresses,
  128-bit carrier triple projections, 881 dyadic loop length bounds; known
  sectors checked by two generated boundaries, not re-enumerated. CPU frozen
  table hash cee1b7c3; no measured root or new geometry authority tabulated.
IN_PROGRESS=Ohm: readout cooperative 52-original/8-carrier packet;
  Raman: DrainSkin parallel packet, following completed live invalidation.
CHECKS=source diff-check and bounded compilation only. Pre-latest-LUT Commit
  1648052B/96513 instructions, Drain4464864B/259972: size FAIL, not closure.
  Finalize corrected RW view:43444B/2036 instructions,8RW,no read/write alias.
NEXT=finish packet sources, consolidated shader/full-suite checks, Kingston
  Unity Quest APK, coherent commit+push. No new APK or device PASS claimed.

### CURRENT TRUE STATE — requested source checkpoint, 2026-09-07 22:45Z

CURRENT_COMMIT=the checkpoint commit containing this entry; parent 08e5e46.
CURRENT_CUT=MERKABA_CLOSURE; DAG_STATUS=OPEN, not a completed run or cut.
AUTHORITY=REV-C plus the current MERKABA_CLOSURE_CONTRACT.md amendments.
CHECKPOINT_SCOPE=all coordinated closure source changes below; no new APK.
SOURCE_FREEZE=readout, scanner/refinement and export/persistence writers stopped.
This entry supersedes older IN_PROGRESS/cursor claims above, not contract text.

IMPLEMENTED_SINCE_PARENT:
  - Shared bounded evidence-packet codec. Readout cooperatively acquires raw
    local/peer/child roots before original/shared and synthesized-site phases;
    one CarrierRootProof callsite. Actual COLD receipts remain consumption-bound.
  - Drain uses one fine packet, owner phase lanes and 21 child measurement
    lanes (three groups of seven); original same-observation cursors and
    publication backpressure remain. Removed superseded serial skin consumers.
  - Generated finite child/address/incidence projections and packed-plane
    normal decoding. All 1048576 normal codes compared bitwise against the
    CPU decoder during codegen. 87723 signed-permutation normal rows; total
    RO blob 1645664 B / 102854 uint4 rows. Frozen geometry hash cee1b7c3.
    Measured root/interval algebra is unchanged. Blob source uses a primitive
    literal array, not 87723 constructor calls in one initialization method.
  - Known-sector classification reads its two generated boundaries. R1 seed
    correspondence reuses compatible-carrier evidence instead of a second
    root solver; identical packed seed planes still require a certain witness.
  - New-observation reset fused into eye-0 depth-certificate reduction; the
    existing dispatch boundary precedes Count. Retry retains its standalone
    reset. No cross-workgroup synchronization replaced by a local barrier.
  - Native ABI16 / 43 resources / 23 pipelines; ERASE has three dispatches.
    Explicit queue priority and immutable observation/source-retirement guards.
  - Export holds canonical/document mutations through quiesce, durable cut,
    all read/write workers and cancellation. Native GLB/Tiles payload preserves
    records, RGBV and session documents; imported native data uses live readout.
  - Shared-carrier export and bounded atlas; unlit preview does not multiply
    captured RGB twice. Portable preview is not claimed to encode procedural V.
  - GLB resume journal: source/options/codegen receipt, tile and DIRT cursors,
    append-tail and atlas-cell checksums, writer restoration. Resume validates
    the same committed source after quiescing, rather than issuing a new SAVE.
  - V-1 presentation-light consumer and ordered CPU twin; default/off unchanged.
    V-1 neither manufactures OPTICAL_VALID nor implements specular/V-2.
  - Dead thumbnailPath removed; Library uses an explicit text placeholder.

CHECKS_RUN:
  - Fresh bounded csc: Editor Runtime, Android Player Runtime, Editor/codegen
    and Tests all PASS; source includes ExportJournal and current generated blob.
    Test compilation is not a test execution or Unity APK build.
    Receipts: /tmp/m8-v1-source-compile.wMkoxR/journal-{runtime,player,editor,tests}-csc.log
    (actual exit codes and output-DLL SHA256; 6/1/0/0 compiler warnings).
  - Actual native entry compilation, spirv-val and reflected ABI PASS for the
    following current sources. Size/occupancy gates are independently reported:

    entry                         SPIR-V B  body instructions  size gate  shared B
    StereoFlowerRefine               828516       49652          REVIEW       96
    FlowerCommit                   1079720       62615          FAIL      24704
    DrainObservationRefinement      2978956      175244          FAIL      22800
    CompactDirtyFlowerSymbols       1536092       89920          FAIL      30024
    ReduceDepthCertificate + reset     8464         302          PASS       8196

    Shared memory above 16 KiB remains REVIEW; none above exceeds 32 KiB.
    Compile PASS must not be relabelled shader-size or performance PASS.
  - Command graph: new observation 17 scheduled / 6 allocation-indirect /
    11 non-allocation / 16 between-dispatch barriers; retry 15 / 6 / 9 / 14.
    Readout 7 dispatches; ERASE 3. Actual nonzero counts are device-unmeasured.
  - Full Unity suite, current full all-entry shader audit, glTF validation,
    complete native/Unity Quest APK and Quest device acceptance NOT RUN here.
    Historical 353/357 suite results are not current-tree evidence.
PERF=not measured on Quest. No FPS, startup or ETA claim.
SHADER_RECEIPTS=/mnt/kingston-unity/Builds/QuestMerkabaScan/ShaderChecks/
  closure-packed-normal-stereo, closure-r1-shared-compare,
  closure-drain-packet, closure-readout-rawbase-normal-lut,
  closure-certificate-reset (metrics.json + pipeline-0.spv).

REMAINING_CLOSURE — exact continuation cursor:

  OPEN-1 SHADER EXECUTION:
    ObservationReduction::CompatibleCarrier and FlowerCommit::OwnPlaneEligible,
    R1FlagWitness, ParentStructureChanged still duplicate deep measured-root
    work across owner admission/witness/structural checks. Reuse the same
    per-owner/relation evidence and generated incidence masks at their actual
    consumers; retain seed, compatible-plane and structural-change predicates.
    Refinement::PrepareFinePacket/DrainSkinPacket still expand raw-base and
    ancestor synthesis in separate deep consumers. Factor existing finite
    packet work, keeping actual child measurements distributed across lanes.
    Readout::PacketAcquire has one raw root callsite now. Remaining regions are
    Geometry::ReadOriginalLocal/ApplyGeometryDetail, PredictGeometryNode/
    TransportChildFromFamily, ReadL2Incidence/CloseSharedPhaseRoot,
    ClassifyL2Carrier and SkinReadout::CompileCarrierSkin. Reuse endpoint/child
    evidence inside the current bounded packet; measure actual emitted module
    after each structural change. No per-function size attribution proved yet.
    Acceptance: each production entry <=1 MiB AND <=50000 body instructions,
    <=8 writable bindings, <=32 KiB shared; packet parity and device occupancy.

  OPEN-2 SELECTIVE INVALIDATION — concrete source gap:
    FlowerSidecar::CaptureInvalidation captures only LOCAL original phase
    presence. Counterexample: K has no own R2_PHASE, Q=K+d has the node^1
    R2_PHASE and predicts from the shared original that reads raw K. Deleting
    K can produce an empty outgoing peer mask, leaving Q's dependent detail.
    Gather the actual TWO-ENDPOINT pre-mutation read-set using generated
    relation/dependency mappings and peer phase presence. Retain that receipt
    under the same observation token; require all affected peer ACKs before
    source retirement. COLD/BUSY keeps the transaction pending. Do not replace
    selective invalidation by an epoch blanket, runtime search or new solver.
    Acceptance includes this peer-only phase example, structural/THROUGH
    deletion, compatible refinement survival and retry/order parity.

  OPEN-3 COMMAND GRAPH:
    New observation has 11 non-allocation boundaries, target <=10. The second
    Reserve after dual publishes the updated touched set; Dual already has
    8 writable bindings. Only fuse publication if the same global dependency,
    deterministic touched spans and hardware limits remain true. Otherwise
    closure section4.1 requires an explicit dependency/device-cost receipt;
    never hide a dispatch or erase its necessary barrier to satisfy a count.

  OPEN-4 EXPORT/IMPORT:
    GLB journal is source-complete but crash/cancel/resume behavior untested.
    Exporter::ClearExport still needs explicit discard of the selected owned
    .resume receipt; do not remove another export or arbitrary staging directory.
    Tiles export still needs its canonical leaf cursor and completed-entry
    receipts connected to the same journal; resume only verified entries,
    repair the incomplete final entry and reject changed source G/options.
    Wire ExportViewerPackageCoreAsync/BuildStreamingTilesetAsync/
    AppendDirtToTilesetAsync; repeat final ZIP assembly from verified leaf files.
    ArtifactViewer already has ReadPackageIndex/ReadGlbTile/ParseGlbForPreview;
    do not add another parser to match historical names. Remaining foreign
    preview gaps: accumulated tile transforms (currently translation-only)
    and accessor decoding independent of the old fixed Flower GLB stream ABI.
    CollectTiles computes the matrix, but Tile/CreateTileObject drop rotation
    and scale. Preserve that full matrix; extend existing preview decoding for
    optional NORMAL/COLOR, index component types and interleaved accessors.
    Bound/cancel ReadPackageIndex JSON traversal and reject malformed matrices
    rather than substituting identity.
    Native payload restore must remain separate from foreign Mesh preview.
    Prove current CPU/GPU shared evaluator and fixed-thread RGBV parity;
    retaining the CPU backend itself is expressly allowed by closure6.1.

  OPEN-5 OPTICAL EVIDENCE:
    V-1 source consumer is connected, but InstallOpticalProgram is an
    imported/persisted-program installer, not measured optical certification.
    Trace and complete the live evidence predicate -> ThreadProgram intervals
    -> persistence -> compact sample -> consumer per closure6.4. An always-zero
    flag is not completion; do not invent albedo, ambient or an optical fit.
    V-2/filtering/specular is not silently folded into the separate V-1 cut.

  OPEN-6 FINAL VALIDATION / APK:
    Run current full suite; preserve and repair CountReserveEmit,
    FrozenDepthObservation_NonzeroR2 and both CapturedFlatWall positive fixtures
    if still failing. Run all-entry shader and command-graph audits, actual
    GLB validation, then Kingston Unity Quest build (not native .so alone).
    Finally Quest launch/startup logs, scan/drain/readout, FINE/ERASE, dynamic
    object add/remove, SAVE/OPEN/anchor, export/import/cancel/resume, controls
    and sustained memory/performance acceptance. These are unverified, not PASS.

NEXT_ACTION=checkpoint commit/push and handoff requested by user; resume at
  OPEN-2 correctness and OPEN-1 shader gates, not a new audit or new DAG.
WORKTREE_SCOPE=commit all coordinated task sources; unrelated .claude/,
  CLAUDE.md and CLAUDE.md.meta remain untouched and untracked.

---

### CURRENT TRUE STATE — OPEN-2 two-endpoint read-set, 2026-09-08 01:20Z

OPEN-2 SOURCE FIX APPLIED — Runtime/Shaders/MerkabaFlowerRefinement.hlsl:
  M8FlowerDrainPeerInvalidations gathered only the locally captured roots, so a
  SOURCE whose own original phase presence is empty was skipped entirely: its
  PEERS_PENDING was never set by M8FlowerCaptureInvalidation and the peer
  holding the node^1 phase — predicting from the shared original that reads the
  raw source endpoint — kept its dependent detail after the source was deleted.
  The stale comment above M8FlowerResolveInvalidationPeer asserted exactly that
  disproven assumption and is corrected.
    gather now gates on M8_FLOWER_INVALIDATION_SOURCE; mutation keeps its
      existing PEERS_PENDING gate.
    gather walks all 20 generated relations once, reads the resident peer's
      M8FlowerReadOriginalPhasePresence and admits the relation when the peer
      carries bit ((node^1)-6).
    a failed presence read admits the relation rather than stranding dependent
      peer detail; this stays per-relation and never becomes an epoch blanket.
    the completed two-endpoint read-set is stored back into the same receipt
      under the same observation token and re-arms PEERS_PENDING, so source
      retirement still requires every affected peer ACK.
    COLD/BUSY peers keep the transaction pending through the existing
      m8FlowerHaloUnresolvedReads/residency-request path; no new solver, no
      runtime search, no epoch blanket.
  Emitted module cost: DrainObservationRefinement 2 978 956 B -> 2 989 480 B
  (+0.35%). No gate verdict changes; that entry remains an OPEN-1 FAIL.

MEASURED SHADER BASELINE at 96b34c4, all-entry audit, EXIT=1:
  FAIL 3 of 67 (production=51 native=23 oracle=16)
    FlowerCommit                 1079720 B  62615 body  GS=24704  FAIL
    DrainObservationRefinement   2978956 B 175244 body  GS=22800  FAIL
    CompactDirtyFlowerSymbols    1536092 B  89920 body  GS=30024  FAIL
    StereoFlowerRefine            828516 B  49652 body            REVIEW
    UpdateObservationDual         619016 B  37365 body            REVIEW
  CompactDirtyFlowerSymbols groupshared 30024 B is close to the 32768 B hard
  limit; any further shared growth in that entry fails outright.

MEASURED FULL SUITE — corrects the earlier 357/353/4 figure:
  baseline 96b34c4, no local change: total=362 passed=344 failed=18
  with the OPEN-2 fix applied:       total=362 passed=344 failed=18
  set difference in both directions is empty: zero regressions, zero repairs.
  Receipt: /mnt/kingston-unity/Builds/TestResults/merkaba-results.xml, 09-08 01:05.
  The handover checkpoint is NOT suite-green. OPEN-6 must carry 18, not 4.

  The 18 are dominated by source-shape assertions whose invariant moved during
  the RUN_08/10/11/13 refactors, e.g.
    OwnedDepthSnapshot_IsPreprocessedOnlyOnConsume
      Expected: String containing "RecordDepthCertificate(command);"
    TrueStereoRgbdContract_IsFailClosedAndWorldReprojected
      Expected: String containing "StereoRootRgb(0u,world,left)"
    NewTileWork_UsesMeasuredIndirectDomainsInsteadOfCacheCapacity
      Expected: String containing "_bins.Record(command)"
  plus behavioural C# failures in export/session/preview/menu and the already
  tracked FrozenDepthObservation_NonzeroR2 and PlaneIntervals entries.
  Per closure section10.1 each one is decided individually: rewrite the
  assertion where the invariant survived and moved, replace with a behavioural
  test where the asserted architecture is genuinely gone. Do not delete a
  failing positive fixture to make the suite green.

OPEN-2 REMAINING: the peer-only phase fixture itself. The harness exists —
  Tests/Editor/MerkabaObservationDrainGpuTests.cs binds the full resource set,
  starts from an empty detail arena and lets DrainObservationRefinement author
  the shared phase from a real observation. Required shape: observation authors
  the shared phase, the source is then marked structural under a new token,
  both invalidation stages run, and the peer's dependent detail must be gone.
  Also required by OPEN-2 acceptance: structural/THROUGH deletion, compatible
  refinement survival and retry/order parity.

NEXT_ACTION=OPEN-2 peer-only fixture, then the 18 stale/behavioural suite
  failures per section10.1, then OPEN-1 fan-out for the three FAIL entries.
WORKTREE_SCOPE=Runtime/Shaders/MerkabaFlowerRefinement.hlsl and lasttrue.md;
  unrelated .claude/, CLAUDE.md and CLAUDE.md.meta remain untouched and untracked.

---

### CURRENT TRUE STATE — suite repair per closure section10.1, 2026-09-08 03:40Z

MEASURED: total=362 passed=359 failed=3 (baseline at 96b34c4 was 344/18).
Fifteen failures were repaired; none was deleted and no threshold was lowered.

MOVED INVARIANTS — assertion rewritten at the new site:
  ExportGlbAsync()/ExportViewerPackageAsync() are no longer async; both
    overloads delegate to BeginExportAsync, where the quiesce-before-read
    ordering now lives and is asserted against both exporter core calls.
  RecordDepthCertificate moved from DepthCapture.ConsumeLatestDepthFrame to
    MerkabaIntegrator; the producer-callback prohibition is unchanged and the
    single recording entry point is asserted on both sides.
  M8_COUNTER_RESIDENCY_EPOCH 64u -> 44u after the CARVE counter excision.
  _save?.SetEnabled(!busy) -> (!operationBusy && ActiveSessionId != Guid.Empty).
  StereoRootRgb per-eye literals -> one loop over eye.
  _bins.Record(command) -> _bins.Record(command, reset: !newObservation).
  BuildStreamingTilesetAsync / BeginStreamingPackage / AppendDirtToTilesetAsync /
    ReadStoredFlowerContextAsync / MerkabaFlowerPresentation.Build /
    ReadGlbTile all gained cancellation and package arguments.

REPLACED ARCHITECTURE — behavioural test substituted:
  btn-export-tiles is gone; one export action selects its destination from the
    export-format dropdown. The test now proves both canonical formats stay
    reachable and that the controller routes each one, instead of asserting a
    removed control.
  design-panel is no longer a ScrollView; one console-body carries every mode
    panel. The test proves the design workflow stays inside that scroll view.
  diagnostics-foldout is no longer a Foldout but a hidden destination panel.
    The test proves it exists, is hidden by default and is closable.
  static const tables became shared read-only buffer accessors. The oracle
    alphabet test now proves the accessors exist, that no table is a static
    const array again, and that the generated blob row offsets still encode
    72 strands / 48 petals / 36 chambers / 399 thread / 343 L5 positions.
    This is strictly stronger than the previous string match.
  GLB emits the shared seven-site packet, not 18 vertices per carrier. The
    normal-stride assertion derives its stride from result.VertexCount, and
    VertexCount=14 vs IndexCount=36 is asserted as the sharing invariant.
  The GLB spool file set follows the writer's accessor set; the test names the
    required streams instead of a frozen count that grows with UV/atlas.
  Atlas texel readback is expressed in cell-local coordinates via
    CellSize/Gutter. Both original invariants are kept verbatim: no row flip
    and no filtering blend, plus a probed>0 guard so it cannot pass vacuously.
  UVs are seven shared chart sites, all inside one atlas cell.

FIXTURE GAP, NOT A PRODUCT DEFECT:
  QuestArtifactPreview... supplied differing RGB samples but left RgbSplitBits
  empty. The atlas correctly demands a scan-authored RGB split; the fixture
  now declares the L2 split it claims.

REMAINING 3 — all pre-existing at 96b34c4, none caused or weakened here:
  PlaneIntervals_EncloseCpuQuantizationAndOrderedOperationBounds
    GPU probe status 7 where 1 is required, plane 0.
  FrozenDepthObservation_NonzeroR2IsBitIdenticalAcrossQuantaAndBackpressure
    drain reports 0 where 1 is required.
  TestOnlyGpuConsumer_SeesIdenticalPackedFieldsAndStrides
    uint[58] index 0 differs: CPU 70, GPU 37.
    Excluded by inspection, so the next attempt need not repeat it:
      HLSL struct M8DualBlockMeta { uint StateAndGeneration; uint PayloadIndex; }
        matches the C# [StructLayout(Sequential, Pack=4)] pair.
      C# packing is (generation << 2) | state, so Create(Mixed,17,23) = 70.
      Upload is Marshal.SizeOf = 8 bytes, one element, ComputeBufferType.Structured.
      The probe declares 58 outputs, indices 0..57, and index 0 is unambiguously
        block.StateAndGeneration.
      37 = (9 << 2) | 1 is data no caller wrote, so this is neither a field
        order nor an output ordering drift. Instrument the binding/compiled ABI
        include next; do not re-audit the declarations.

NEXT_ACTION=APK build and install for device testing, then the three GPU
  failures above, then OPEN-1 fan-out for the three shader size FAIL entries.
WORKTREE_SCOPE=Tests/Editor sources and lasttrue.md; unrelated .claude/,
  CLAUDE.md and CLAUDE.md.meta remain untouched and untracked.

---

### CURRENT TRUE STATE — OPEN-3 dependency/device-cost receipt, 2026-09-08 02:10Z

OPEN-3 asked for <=10 non-allocation boundaries on a new observation, or an
explicit dependency/device-cost receipt for the eleventh. Measured graph from
Tools/shaders/audit_merkaba_command_graph.sh at 85db10c:

  new observation   17 commands, 16 barriers, 11 non-allocation
  retry             15 commands, 14 barriers,  9 non-allocation

The eleven non-allocation boundaries are:
  StereoFlowerRefine, BuildDepthCertificate, ReduceDepthCertificate,
  CountObservationBins, ReserveObservationBins, EmitObservationBins,
  UpdateObservationDual, ReserveObservationBins (second), FlowerCommit,
  DrainObservationRefinement, FinalizeObservation.

THE SECOND RESERVE CANNOT BE FUSED. Two independent blockers, either alone
sufficient; the ledger's own test is "only fuse if the same global dependency
and hardware limits remain true", and both fail.

1. GLOBAL DEPENDENCY. ReserveObservationBins is a single 256-lane workgroup
   running a complete prefix scan over the per-slot bin counts
   (MerkabaObservationBins.compute:326-352, publicationOnly gated on
   M8_COUNTER_DUAL_TOUCH_PUBLICATION). UpdateObservationDual writes those
   counts from many groups, so the scan cannot run inside it; a dispatch has
   no global barrier. FlowerCommit reads its reserved span in every group, so
   the scan must be complete before its first group starts. The boundary is
   the dependency, not a scheduling choice.

2. DEVICE BUDGET. Writable storage bindings, measured per entry point:
     UpdateObservationDual  8/8  _M8ClaimQueue _M8Counters _M8DualBlockState
                                 _M8DualChunkState _M8DualLeaves
                                 _M8ObservationDispatchArgs
                                 _M8ObservationTileBins _M8TileRecords
     ReserveObservationBins 4    _M8Counters _M8ObservationDispatchArgs
                                 _M8ObservationTileBins _M8TouchedTileQueue
     FlowerCommit           8/8
     CountObservationBins   8/8
     FinalizeObservation    8/8
   Reserve \ Dual is exactly one buffer, _M8TouchedTileQueue, so the fused
   union is 9 > 8. Dual, Commit, Count and Finalize are each already at the
   hard limit, so no neighbour can host it either.

No other pair is fusible: Build/Reduce certificate is a pyramid with a real
global barrier between its two halves, and Count -> Reserve -> Emit is the
same count/scan/write dependency at the depth domain.

THE ONLY LAWFUL PATH TO TEN is making the Drain continuation conditional.
REV-C and closure section4.2 already frame DrainObservationRefinement as a
semantic obligation rather than a mandatory micro-dispatch: when the work fits
inside the same tile quantum it belongs inside FlowerCommit, and only genuine
finite remaining work should reach a second dispatch. That is OPEN-1 packet
work, not a command-graph trick, and it must not be faked by hiding a dispatch
or removing a necessary barrier to satisfy a count.

OPEN-3 STATUS=receipt delivered; count stays 11 until OPEN-1 folds the drain.

---

### CURRENT TRUE STATE — OPEN-1 exact interval arithmetic without control flow, 2026-09-08 03:05Z

MEASURED CAUSE, not the ledger's assumption. The ledger named three sites in
MerkabaFlowerCommit.hlsl that "do the same deep work three times". Stubbing
each candidate and recompiling showed that is not where the mass is:

  stub M8FlowerCompatibleCarrier     1 079 720 -> 748 016 B
  stub M8FlowerParentStructureChanged 1 079 720 -> 787 804 B
  stub M8FlowerR1FlagWitness          1 079 720 -> 983 844 B

Those overlap because each drags the same interval algebra with it. Compiling
with -g and histogramming every SPIR-V instruction by source line gave the
real answer: 51 304 of FlowerCommit's 62 615 body instructions come from
MerkabaSphereFlower.generated.hlsl, and over 28 000 of them from lines
495-560 alone — M8FlowerNext, M8FlowerPrevious and the four interval
operations. glslangValidator inlines every HLSL function (there is exactly
one OpFunction in the module and [noinline] is not a glslang attribute), so
static cost is call-site count times body size. A static call-graph walk from
each entry point gives the multiplicities:

  DrainObservationRefinement  IZero 3567  Next 2401  Previous 1852  IMul 1017
  CompactDirtyFlowerSymbols   IZero 2814  Next 1879  Previous 1463  IMul  805
  FlowerCommit                Next   529  IZero  480  Previous  249  IMul  144

Each branch inside those leaves therefore costs a selection merge and a phi
at every one of those sites. M8FlowerNext and M8FlowerPrevious were rewritten
as pure selection, and the four interval operations evaluate their guarded
expression unconditionally and select the result — the guarded expression was
already emitted statically under the branch, so this is strictly smaller and
never adds an operation to the module.

SEMANTICS ARE UNCHANGED, AND PROVEN SO, NOT ASSUMED. The ULP-step pair was
checked exhaustively over all 2^32 binary32 encodings: both zeros, both
infinities, every denormal and every NaN. Mismatches: 0. Guard priority in
the four interval operations is preserved exactly by select order, and the
bit-inspecting zero test is kept as it is — a flushed denormal is not the
exact zero interval, so a float comparison is not a lawful substitute.

THE GLSLANG GATE ALONE IS NOT SUFFICIENT EVIDENCE. The first revision used a
conditional operator on the M8FlowerInterval struct. glslang accepts it; the
Unity Vulkan front end rejects it with "conditional operator only supports
results with numeric scalar, vector, or matrix types", which took the suite
from 18 failures to 44 and left the probes reading unbound memory. The fix is
per-component scalar selection. Run Tools/unity/run_merkaba_tests.sh, never
the SPIR-V audit alone, before believing a shader edit.

MEASURED RESULT — Tools/shaders/audit_merkaba_compute_spirv.sh:

  FlowerCommit                1 079 720 B / 62 615  FAIL
                           ->   944 896 B / 49 137  REVIEW
  CompactDirtyFlowerSymbols   1 536 092 B / 89 920  FAIL
                           -> 1 335 616 B / 70 069  FAIL
  DrainObservationRefinement  2 978 956 B /175 244  FAIL
                           -> 2 595 448 B /136 755  FAIL

  failing kernels 3 -> 2 of 67.

Tools/unity/run_merkaba_tests.sh: 362 total, 361 passed, 1 failed, 0 shader
errors. The remaining failure is the known OPEN-6 entry
FrozenDepthObservation_NonzeroR2IsBitIdenticalAcrossQuantaAndBackpressure.

spirv-opt IS NOT THE ANSWER AND WAS MEASURED, NOT GUESSED: -O gives
FlowerCommit 944 896 -> 855 020 B, Drain 2 595 448 -> 2 431 404 B, and makes
CompactDirtyFlowerSymbols LARGER, 1 335 616 -> 2 084 088 B, because it
unrolls. It cannot close the remaining two.

REMAINING WORK IS STRUCTURAL, NOT LOCAL. Drain must lose 64 percent and
CompactDirtyFlowerSymbols 29 percent of their body instructions. The
attribution shows no single dominant site: the largest single contributor of
interval-operation call sites in Drain is M8FlowerRootInSector at 750 of
roughly 3 500. That is a fan-out of the exact algebra across the whole
kernel, so the lawful lever is the reachable call graph per dispatch, not
another leaf rewrite.

OPEN-1 STATUS=FlowerCommit closed; Drain and CompactDirtyFlowerSymbols open.
DEVICE ACCEPTANCE PENDING.

---

### CURRENT TRUE STATE — OPEN-4 owned resume receipt discard, 2026-09-08 04:20Z

ClearExport deleted the published artefacts and their .tmp siblings but left
the .resume journal behind, so clearing an export and exporting the same name
again resumed a cursor the user believed was gone. ClearExport now discards
the receipt for exactly the two destinations it already owns, ExportPath and
ViewerPackagePath.

IT CANNOT REMOVE ANYTHING ELSE, and that is the tested part: a directory that
does not carry the journal's own source.json is not a receipt and is left
untouched however it is named, a symbolic link is never followed, and a
missing destination or a null/empty name is ordinary rather than a failure.
Tests/Editor/MerkabaTilesetWriterTests.cs
ClearExportDiscardsOnlyItsOwnResumeReceipt covers all four.

Tools/unity/run_merkaba_tests.sh: 363 total, 362 passed, 1 failed.

OPEN-4 STATUS=tile transform and resume discard closed. Still open: the Tiles
canonical leaf cursor and completed-entry receipts on the same journal, and
preview accessor decoding independent of the fixed Flower GLB stream ABI.

---

### CURRENT TRUE STATE — OPEN-6 suite green, stale counter slot found, 2026-09-08 04:55Z

MEASURED: Tools/unity/run_merkaba_tests.sh result=Passed total=363 passed=363
failed=0 skipped=0. The baseline at the handover checkpoint 96b34c4 was
362/344/18.

THE LAST FAILURE WAS A STALE ABI LITERAL IN THE FIXTURE, NOT A PRODUCTION
DEFECT, and it was found by measurement rather than by reading. Instrumenting
one drain-only dispatch showed pendingTiles=0, workProgress=0, an all-zero
tile directory and an all-zero arena — the kernel had returned before its
first guard could even fail. MerkabaObservationDrainGpuTests carried

  private const int TouchedTileCountCounter = 15; // M8_COUNTER_TOUCHED_TILE_COUNT

but MerkabaWorld.hlsl defines M8_COUNTER_TOUCHED_TILE_COUNT as 13; slot 15 is
M8_COUNTER_WRITEBACK_COUNT. The fixture therefore raised the writeback count
and left the touched-tile count at zero, so DrainObservationRefinement exited
at group.x>=min(touchedTileCount,32768) on its very first line and no stage
ever ran. FrozenDepthObservation_NonzeroR2 had been asserting against a
kernel that never executed.

THE FIX REMOVES THE LITERAL RATHER THAN CORRECTING IT. MerkabaGrid gains
CounterTouchedTileCount=13 and the fixture reads it from there. That constant
is covered by the existing MerkabaGpuIntegrationTests
M8CounterAbi_UsesEverySlotExactlyOnce, which checks every MerkabaGrid.Counter*
field against the shader define, so this particular drift cannot recur
silently. No threshold was lowered and no fixture was deleted.

OPEN-6 STATUS=suite closed at 363/363. DEVICE ACCEPTANCE PENDING.

DEVICE, MEASURED THIS SESSION: Tools/unity/build_merkaba_apk.sh produced
QuestMerkabaScan-release.apk (79 018 072 B) and deploy_merkaba_apk.sh
installed it on 340YC20G7X0QZ4 successfully. It could not be exercised. The
headset is not being worn, so HorizonOS paused the activity immediately
(wm_pause_activity ... sleep, then makeInvisible) and refused the relaunch
with "Launch is blocked because: a Reprojected OS dialog is currently
showing". The process sat at 0% CPU with all 18 threads asleep and emitted no
MerkabaNative line at all, so pipeline creation was never reached and nothing
about the driver can be claimed from this run. DEVICE ACCEPTANCE PENDING.

---

### CURRENT TRUE STATE — OPEN-1 residual and OPEN-2 fixture cost, measured, 2026-09-08 05:40Z

OPEN-1: THE SPLIT DOES NOT CLOSE THE GATE, AND THAT IS MEASURED, NOT ASSUMED.
DrainObservationRefinement already carries a GPU-side stage counter, and stage
3 is the skin phase, which the call-graph attribution shows is 67 percent of
its interval-operation sites. Compiling the kernel with M8FlowerDrainSkinPacket
stubbed gives the exact size of the phase half:

  full drain                   2 595 448 B / 136 755 instructions
  phase half, skin removed     1 151 508 B /  60 341 instructions

Both halves therefore stay above 1 MiB and above 50 000. A two-entry split by
the existing stage would not have needed a new barrier — the two entries are
mutually exclusive on the stage, so OPEN-3's boundary count would not move —
but it does not reach the gate, so it buys nothing and was not made.

CompactDirtyFlowerSymbols is the same shape: 100 percent of its interval sites
arrive through the single subtree M8FlowerCompileOwner, and below that the
mass spreads again (PageCarrier 36 percent, PacketAcquire 35 percent,
PrepareCompletionPetal 20 percent). There is no dominant site to hoist.

WHAT REMAINS IS AN ALGEBRA CHANGE, NOT A RESTRUCTURING. glslang inlines every
HLSL function, so cost is call-site count times body size, and the sites are
spread across the whole exact-interval evaluation. Collapsing repeated call
edges into runtime loops was measured too: folding M8FlowerRootInSector's two
M8FlowerICross calls and M8FlowerICross's two M8FlowerIMul calls took the
drain only from 2 595 448/136 755 to 2 434 992/130 027, about 5 percent, and
the remaining collapsible edges are the same order. spirv-opt was measured and
rejected above. The lawful lever is the one the closure already names: move
lattice-determined evaluation out of the shader into the generated tables and
read it, rather than recomputing it per site. That is a deliberate cut with
real exactness risk and it is NOT started here.

  OPEN-1 STATUS=FlowerCommit closed (FAIL -> REVIEW). DrainObservationRefinement
  2 595 448 B/136 755 and CompactDirtyFlowerSymbols 1 335 616 B/70 069 remain
  FAIL. 2 of 67 kernels fail the gate, from 3 at the handover checkpoint.

OPEN-2: WHAT THE PEER-ONLY FIXTURE ACTUALLY COSTS, established by building it
far enough to measure and then removing it rather than committing a fixture
that proves nothing.

  The frozen harness can drive FlowerCommit: it needs only the writable
  _M8KernelStates0..3 bindings and the depth/normal/RGB textures bound to that
  kernel as well. Everything else FlowerCommit reads is already bound.
  A COMPLETED OBSERVATION RETIRES BOTH THE TOUCHED-TILE COUNT AND THE BIN
  SPAN. A second observation must republish both, exactly as the bins stage
  does; without that FlowerCommit exits at its group.x guard and nothing runs.
  With them republished it runs correctly and re-arms the R1 stamp
  (tileRecords runtime.x becomes the new token, tile bits .z reaches 12).
  A STRUCTURAL CAPTURE CANNOT BE PRODUCED FROM THE FINE STAGE ALONE. Three
  distinct attempts were measured, all leaving structural bits at zero and the
  stage at zero: a 25 degree tilt of the measured normal, an observation of
  the same surface from its other side, and seeding the coarse source with the
  opposite plane free side. That is the documented contract, not a defect —
  M8FlowerCompatibleCarrier preserves the existing R1 on failure and never
  chooses a different sheet. Clearing the coarse occupancy flag and its tile
  bit did not make the commit re-admit either.
  THE STRUCTURAL PATH IS AUTHORED BY THE COARSE EVIDENCE STAGE, so the
  peer-only fixture needs the full observation pipeline — bins, dual, commit,
  drain — not the three kernels the frozen harness binds today. That is the
  real remaining cost of OPEN-2 acceptance.

  OPEN-2 STATUS=source fix committed at edf7b78; the fixture needs a
  full-pipeline harness and is not written.

NEXT_ACTION=OPEN-5 optical evidence chain; then OPEN-2's full-pipeline harness;
  OPEN-1's remaining two kernels need the tabulation cut, not another rewrite.
WORKTREE_SCOPE=lasttrue.md only; unrelated .claude/, CLAUDE.md and
  CLAUDE.md.meta remain untouched and untracked.

---

### CURRENT TRUE STATE — OPEN-5 optical evidence receipt, 2026-09-08 06:15Z

TRACED THE WHOLE CHAIN AS THE CLOSURE ASKS: live evidence predicate ->
ThreadProgram intervals -> persistence -> compact sample -> consumer.

  producer      NONE EXISTS. Every occurrence of OpticalLower, OpticalUpper,
                CaptureViewLower and CaptureViewUpper in Runtime/ is a read, a
                store or a persistence copy. Nothing computes them from
                measurement.
  install       M8FlowerInstallOpticalProgram (MerkabaFlowerSidecar.hlsl:1126)
                is reached only from MerkabaFlowerStorageTransfer.hlsl:118,
                the header.x==10 record of the load/import path. It installs a
                program that already exists; it certifies nothing.
  persistence   MerkabaThreadProgramFlags.OpticalValid = 1u<<0, stored and
                restored intact.
  sample        MerkabaFlowerSkinReadout.hlsl:277 copies that one bit to
                sample.Flags. In a live scan it is therefore always zero.
  consumer      COMPLETE AND CORRECT, and now guarded.

THE CONSUMER SIDE IS DONE. M8FlowerRelativeDiffuse and the CPU RelativeDiffuse
implement closure section6.4.1 exactly: normalize L, e0 = N.L, the explicit
2^-5 presentation floor, ef = Nmicro.L, saturate(max(ef,0)/e0), and a
bit-identical captured colour whenever the light is off, the direction is not
a valid presentation input, or OPTICAL_VALID is unset. MerkabaGrid.shader only
evaluates the micro-normal when the light is enabled AND the sample carries
OPTICAL_VALID, so V-1 cannot manufacture the flag.

WHAT THIS SESSION ADDED IS THE MISSING TWIN GUARD, not a producer. CLAUDE.md
requires the hand-maintained CPU and HLSL twins to change together with a
parity test, and RelativeDiffuse had none: the only check was that
MerkabaGrid.shader mentions the call. MerkabaSphereFlowerSkinTests
RelativeDiffuse_CpuAndHlslTwinsCarryTheSameOperationOrder now extracts both
function bodies and asserts the same fourteen operations appear in the same
order in both, plus that each text has exactly three untouched-capture exits.
Only the Mathematics namespace prefix and HLSL's precise qualifier are
normalized away; every operation and its order is compared as written.

THE PRODUCER IS NOT WRITTEN, AND WRITING IT HERE WOULD VIOLATE THE CLOSURE.
Section 6.4 states that a certified optical formula without a closed consumer
in the current code is an explicit implementation gap of this run and NOT
permission to invent another material authority, and it forbids adding an
albedo estimate, an ambient field or a new optical fitting rule. Section 6.4.1
states that V-1 does not create OPTICAL_VALID and does not change its
producer. There is no existing certified predicate anywhere in the repository
to connect, so closing OPEN-5 by authoring one would mean inventing exactly
the material authority both sections forbid. An always-zero flag is not
completion, and neither is a fabricated certification.

Tools/unity/run_merkaba_tests.sh: 364 total, 364 passed, 0 failed.

OPEN-5 STATUS=chain traced, consumer complete and now twin-guarded, producer
declared an explicit gap under closure section6.4 rather than invented.

---

### CURRENT TRUE STATE — device run, two real defects found and one fixed, 2026-09-08 13:35Z

THE APK RUNS ON THE HEADSET NOW. Measured on 340YC20G7X0QZ4 with the headset
worn, so this supersedes the earlier PENDING note for launch itself.

CORRECTION TO AN EARLIER CLAIM IN THIS SESSION. I wrote that the build got
past pipeline creation. It does not. A clean capture from process start:

  index=10 UpdateObservationDual bytes=494360  ms=1.965    result=0
  index=11 FlowerCommit          bytes=944896  ms=2084.557 result=-13
  vkCreateComputePipelines failed VkResult=-13
  init worker: state=failed family=0 requiredPipelines=23 elapsedMs=2122

What I had actually observed was g_flowerDrawReady being true, and that flag is
set in InitializeFlowerDraw during device initialization, BEFORE any compute
pipeline is created. It proves nothing about them.

The interval-arithmetic cut did move this: FlowerCommit went from 1 079 720 B
failing after 26 499 ms to 944 896 B failing after 2 085 ms, a 13x shorter
compile. The driver still rejects it. Every pipeline that succeeds is at most
494 360 B, every one that fails is above 900 KB, and the three failing kernels
are also the only ones with more than 16 KiB of groupshared, so module size
and groupshared size are still confounded by the available evidence. Do not
assert either as the cause.

spirv-opt SATURATES AND CANNOT CLOSE THIS. Measured on the actual 944 896 B
module: -O 855 020, -Os 853 940, -O -Os 854 036, and a hand-tuned redundancy
pass list 854 684. About ten percent, then nothing.

DEFECT A, FIXED AND PROVEN ON DEVICE. Every frame threw

  NotSupportedException: Cannot determine if this AsyncQueueSynchronisation
  GraphicsFence has passed as this platform does not support async compute.
    at GraphicsFence.get_passed ()
    at MerkabaGrid.PollDualRetirement () -> ReserveDualMutation ()
    -> RecordDualMutation () -> ExecuteDualWorldBatch ()

Three fences were created as AsyncQueueSynchronisation and then read on the
CPU: _dualRetirementFence, MerkabaGridRenderer._managedBuildFence and the PCA
copy retirement fence in MerkabaIntegrator. An async-queue fence is only CPU
queryable where the platform supports async compute, and Quest does not. All
three now use GraphicsFenceType.CPUSynchronisation, which is the type meant
for CPU polling; the stage flag is unchanged so they still signal after all
preceding GPU work. The one genuine GPU-side wait, the fence handed to
WaitOnAsyncGraphicsFence, stays AsyncQueueSynchronisation.
AFTER THE FIX THE EXCEPTION COUNT IS ZERO over a 150 s capture, from every
frame before.

DEFECT B, DIAGNOSED, FIRST TWO ATTEMPTS MEASURED AND REJECTED. Every frame:

  Flower cull count reset requires an outside-render-pass command buffer.
  Flower indexed wrapper rejected: current-view cull count was not reset.
  Flower draw rejected: invalid registered indexed indirect invocation.

ResetFlowerCullCount refuses to record its transfer when CommandRecordingState
reports subPassIndex >= 0, so g_flowerCountResetBuffer stays null, the
registration finds no matching reset and every draw is dropped. The header
confirms the check is right: subPassIndex is "-1 if not inside a render pass".

  ATTEMPT 1, removing UseTexture(activeColorTexture, ReadWrite) from the unsafe
  cull pass: no change on device. Kept anyway, because the cull writes only the
  indirect argument buffer and declaring the active colour target was wrong.
  ATTEMPT 2, kUnityVulkanRenderPass_EnsureOutside on the reset event: no change
  on device, 8 328 rejections in one capture. REVERTED, because
  IUnityGraphicsVulkan.h states plainly that EnsureOutside "is undefined" in
  combination with the SRP RenderPass API, and URP's render graph uses it.

Being outside a render pass therefore has to be structural. The cull now runs
in its own MerkabaCullPass at RenderPassEvent.BeforeRendering, before URP opens
the render pass that the draw at BeforeRenderingTransparents renders into.
MEASURED ON DEVICE: all three rejection messages drop from 8 328 in one capture
to ZERO. The flower draw is no longer refused. Suite 364 total, 364 passed.

WHAT STILL BLOCKS THE SCANNER IS OPEN-1 ALONE:
  [RoomScan] Merkaba native startup FAILED: pipeline=FlowerCommit
  VkResult=-13; scanner disabled.

THE NEXT MEASUREMENT IS PREPARED AND ISOLATES THE CONFOUND. Halving the
groupshared owner arrays — M8_FLOWER_REDUCTION_OWNER_COUNT 512 to 256 and the
four FlowerCommit arrays to 256 — takes groupshared from 24 704 B to about
12 352 B while the module stays 944 896 B, byte for byte, at 49 139 versus
49 137 instructions. A build of that variant answers whether the driver
rejects the module for its size or for its groupshared. It is a throwaway
diagnostic: the arrays would be too small to be correct, so it must never be
committed.

---

### CURRENT TRUE STATE — OPEN-1 cause isolated, target quantified, 2026-09-08 13:55Z

TWO CANDIDATE CAUSES WERE CONFOUNDED IN EVERY EARLIER OBSERVATION: the three
kernels the driver rejects are also the only three with more than 16 KiB of
groupshared. A throwaway diagnostic build separated them. Halving the owner
arrays — M8_FLOWER_REDUCTION_OWNER_COUNT 512 to 256 and the four FlowerCommit
arrays to 256 — drops groupshared from 24 704 B to about 12 352 B while the
module stays 944 896 B BYTE FOR BYTE, 49 139 versus 49 137 instructions. On
device FlowerCommit still failed:

  index=11 name=FlowerCommit ms=6739.409 result=-13

GROUPSHARED IS NOT THE CAUSE. The diagnostic was reverted and never committed;
its arrays were deliberately too small to be correct.

That run had a cold pipeline cache, so it also gives the first real compile
times and the first hard bracket on the driver's limit:

  index=0  StereoFlowerRefine      671 652 B   7 574 ms   result=0
  index=10 UpdateObservationDual   494 360 B   3 324 ms   result=0
  index=11 FlowerCommit            944 896 B   6 739 ms   result=-13

IT IS NOT A TIMEOUT EITHER: the 671 652 B module compiled for LONGER than the
944 896 B one that failed, and succeeded. The driver's boundary lies strictly
between 671 652 B and 944 896 B.

SPIRV-OPT IS NOT A GLOBAL FIX, MEASURED ON ALL SHIPPED HEAVY KERNELS with -Os,
every output spirv-val clean:

  FlowerCommit                944 896 ->   853 940   -10 percent
  StereoFlowerRefine          671 652 ->   628 256   -6 percent
  DrainObservationRefinement 2 595 448 -> 2 431 120   -6 percent
  CompactDirtyFlowerSymbols  1 335 616 -> 2 084 192   +56 percent, GROWS
  UpdateObservationDual         494 360 ->   539 376   +9 percent, GROWS

It helps one kernel by ten percent and inflates two others, one of which
currently compiles. It cannot be adopted.

THE TARGET IS NOW A NUMBER, NOT A GUESS. Against the 671 652 B that is proven
to compile from a cold cache:

  FlowerCommit                944 896 B   needs about -29 percent
  CompactDirtyFlowerSymbols 1 335 616 B   needs about -50 percent
  DrainObservationRefinement 2 595 448 B   needs about -74 percent

OPEN-1 STATUS=cause isolated to module size, target quantified. The remaining
lever is unchanged and unstarted: move lattice-determined evaluation out of the
shader into the generated tables, per the closure. Local rewrites, kernel
splitting by stage and spirv-opt have all now been measured and none reaches
this.

---

### CURRENT TRUE STATE — correction: the draw fix does NOT work, 2026-09-08 14:05Z

I REPORTED THE DRAW REJECTIONS AS ZERO. THAT WAS WRONG, AND THE ERROR WAS MINE,
NOT THE DEVICE'S. The counting pipeline used

  grep -o "MerkabaNative([0-9]*): .*"

but logcat writes "MerkabaNative( 2623):" with a space before the pid, so the
pattern never matched, the pipeline produced nothing, and I read empty output
as a count of zero. Commit 2aada51 and the ledger entry above it both state
that all three rejection messages dropped to zero. They did not. Counted
correctly with a plain "Flower draw rejected" match:

  dev2  before any fix              fence 2 622   draw 2 060   (27 318 lines)
  dev4  fence fix                   fence     0   draw 9 072   (27 318 lines)
  dev5  cull in its own pass        fence     0   draw 6 185   (18 657 lines)
  dev7  same source, restored       fence     0   draw 5 441   (16 425 lines)

Normalized per captured line the rejection rate is identical in dev4, dev5 and
dev7. MOVING THE CULL TO ITS OWN BeforeRendering PASS CHANGED NOTHING.

THE FENCE FIX IS UNAFFECTED AND STILL PROVEN: 2 622 exceptions before, zero in
every capture after, across comparable rendering runs.

WHY THE NEXT STEP IS A MEASUREMENT AND NOT A FOURTH GUESS. ResetFlowerCullCount
emitted one message for three different preconditions — Unity not recording,
no current command buffer, or subPassIndex >= 0 — so the text
"requires an outside-render-pass command buffer" was an assumption about which
one failed, and every guess costs a full device build. Each precondition now
reports itself, and the render-pass case also prints subPassIndex and whether
renderPass and framebuffer are set.

OPEN DEFECT B STATUS=cause not yet identified; three attempts measured and
rejected (attachment dependency, EnsureOutside precondition, separate
BeforeRendering pass). The cull pass split is retained because it is correct
in its own right, not because it fixed this.

---

### CURRENT TRUE STATE — the flower draw was never accepted; two native defects, 2026-09-08 14:20Z

DEFECT B IS FIXED AND MEASURED. It was two separate native defects, and
neither was where the first three attempts looked. Counted with an unambiguous
match across four device captures, each fix removing exactly one layer:

  capture  lines   draw rejected  cull reset  wrapper rejected
  dev8     16 542          5 480       5 480             5 480
  dev9      5 577          5 475           0                 0
  dev10     5 544          5 442           0                 0
  dev11        102             0           0                 0

DEFECT B1, THE RENDER PASS TEST WAS ITSELF WRONG. ResetFlowerCullCount rejected
on recording.subPassIndex >= 0. IUnityGraphicsVulkan.h documents subPassIndex
as "-1 if not inside a render pass", but the instrumented build measured, every
frame:

  subPassIndex=0 renderPass=null framebuffer=null

Unity 6000.5 on Quest reports subPassIndex 0 while genuinely outside a render
pass. The condition was therefore true unconditionally and rejected every
frame no matter where the caller ran, which is exactly why removing the colour
attachment dependency, setting EnsureOutside and moving the cull to its own
BeforeRendering pass all measured as no change. They could not have worked.
The render pass handle is now the authority: a render pass cannot be in
progress without one.

DEFECT B2, A LEGAL ZERO STRIDE WAS TREATED AS INVALID. With B1 fixed the
wrapper still rejected every draw. The instrumented message named the term:

  registered=1 ready=1 countDraw=1 buffer=match offset=0 drawCount=1
  stride=0 expectedStride=20

Vulkan ignores stride when drawCount is one, so Unity is entitled to pass zero
and does. The guard now accepts zero or the exact command stride; the wrapper
substitutes kFlowerCommandStride itself for its own count draw.

THE INSTRUMENTATION IS KEPT, NOT REVERTED. One shared message across three
recording preconditions and seven draw terms is what made three consecutive
diagnoses guesses, at one device build each. Each condition now reports itself
with its value, and that is what found both defects.

THE FLOWER DRAW HAS THEREFORE NEVER BEEN ACCEPTED ON DEVICE since this code was
written. It is accepted now: the whole per-frame rejection stream is gone and a
100 s capture is 102 lines.

Tools/unity/run_merkaba_tests.sh: 364 total, 364 passed, 0 failed.

WHAT REMAINS IS OPEN-1 ALONE:
  [RoomScan] Merkaba native startup FAILED: pipeline=FlowerCommit
  VkResult=-13; scanner disabled.
The draw path runs; the scanner does not, because the native executor requires
all 23 pipelines and FlowerCommit is refused at 944 896 B. Target against the
671 652 B proven to compile: FlowerCommit -29 percent,
CompactDirtyFlowerSymbols -50 percent, DrainObservationRefinement -74 percent.

---

### CURRENT TRUE STATE — OPEN-1 cut: emit the algebra once, not per site, 2026-09-08 15:10Z

THE FRAMING WAS CORRECTED BEFORE THE CUT, AND THE CORRECTION MATTERS. Nothing
here changes the algebra. Every value is the same value; it is simply no longer
emitted several times per call site. Two measurements set the direction:

  1. THE LATTICE DATA IS ALREADY PRECOMPUTED. Every M8Flower...At(index) is a
     one-line read from _M8FlowerTables costing about 3.6 instructions. There
     is no table left to add. What remains is arithmetic over the MEASURED
     plane, whose alphabet is a 1024x1024 octahedral code plus 256 offset
     codes, so it cannot be tabulated.
  2. IT IS NOT A GLSLANG ARTEFACT EITHER. Unity's own DXC output for the same
     kernels, extracted from Library/ShaderCache, is the same order:
       MerkabaIntegration  2 462 692 / 871 696 / 550 660 B
       ours (glslang)      2 595 448 / 944 896 / 494 360 B
     Switching compilers would buy about eight percent, not a factor.

WHAT THE CUT ACTUALLY DOES, all in the codegen authority and regenerated:

  M8FlowerClassifyRoot computed q = IAdd(ISquare(y),ISquare(z)) and
  aa = ISquare(x), and its only caller M8FlowerRootInterval computed q AGAIN
  immediately afterwards. It now publishes both. Hoisting them above the
  exact-equality exit changes no result because they are pure functions of abc.

  Five sites emitted the same arithmetic with different operands and now emit
  one body inside a loop: M8FlowerICross's two multiplies, M8FlowerRootInSector's
  two crosses, M8FlowerRootInterval's two component multiplies and its two
  secant updates, M8FlowerRotatePhaseMetric's four rotation multiplies, and
  M8FlowerSealPhaseRelation's two independent endpoint sector proofs. The &&
  short circuit was dropped only where the callee is pure.

MEASURED, Tools/shaders/audit_merkaba_compute_spirv.sh:

  FlowerCommit                 944 896 B / 49 137  ->  825 256 B / 43 847
  CompactDirtyFlowerSymbols  1 335 616 B / 70 069  -> 1 217 516 B / 64 998
  DrainObservationRefinement 2 595 448 B /136 755  -> 2 351 204 B /126 167

Cumulative with the earlier interval-selection cut, FlowerCommit is down from
1 079 720 B to 825 256 B, a fall of 23.6 percent, and is now below the
871 696 B that Unity's own compiler produces for it.

THE STATIC MODEL OVERSTATES THIS AND THE MEASUREMENT IS THE AUTHORITY. Static
interval-operation call sites in Drain fell 14 007 -> 4 598, sixty-seven
percent, while the module fell 9.4 percent. Do not plan the next cut from the
call-site count. The remaining collapsible edges are now small, the largest
worth 231 sites, so this technique is mined out.

Tools/unity/run_merkaba_tests.sh: 364 total, 364 passed, 0 failed, 0 shader
errors. The first attempt failed 18 tests because M8FlowerClassifyRoot is also
called by MerkabaObservationBinsProbe and MerkabaSphereFlowerOracle, which my
search for callers had not covered; each now forwards through one local helper
rather than restating the extra outputs at five sites. The parity suite is what
caught it, and it is the same suite that would catch an algebra change.

OPEN-1 STATUS=FlowerCommit 825 256 B awaiting the device. Drain and
CompactDirtyFlowerSymbols still FAIL the gate; Drain needs a different lever
than call-site collapsing.

---

### CURRENT TRUE STATE — FlowerCommit compiles; the real failure mode is compiler memory, 2026-09-08 14:55Z

FLOWERCOMMIT IS CLOSED, MEASURED ON 340YC20G7X0QZ4:

  index=10 UpdateObservationDual    494 360 B   ms=4229.430   result=0
  index=11 FlowerCommit             825 256 B   ms=9988.440   result=0
  index=12 DrainObservationRefinement 2 351 204 B  killed while compiling

At 944 896 B this same pipeline failed with VkResult=-13. At 825 256 B it
compiles. The driver's practical boundary therefore lies between 825 256 and
944 896 bytes, a fourteen percent bracket, and that is the first hard number
we have for it.

THAT CORRECTS THE TARGETS I PUBLISHED EARLIER. They were computed against
671 652 B, the only size then known to compile, so they were too pessimistic:

  CompactDirtyFlowerSymbols 1 217 516 B   was -50 percent, is about -32
  DrainObservationRefinement 2 351 204 B  was -74 percent, is about -65

IT IS NOT A SIZE LIMIT. IT IS THE DRIVER'S SHADER COMPILER EXHAUSTING MEMORY,
and the kernel log names it:

  14:51:03 lowmemorykiller: Kill 'com.genesis.questmerkabascan' (9926),
  to free 3355860kb rss, and 1839952kB swap; reason: low watermark is breached

Compiling the 2 351 204 B Drain module ran the Adreno compiler for more than
270 seconds and grew the process to 3.3 GB resident plus 1.8 GB of swap until
Android killed it. Cost is strongly superlinear in module size: 494 360 B
takes 4.2 s, 825 256 B takes 10.0 s, 2 351 204 B does not finish. This also
explains why the 944 896 B module returned VkResult=-13 after only 6.7 s,
LESS than the 10.0 s a successful smaller compile takes: that reads as an
allocation failure inside the compiler, not a threshold test.

CONSEQUENCE FOR THE REMAINING WORK. Every shipped module must land under about
825 KB, and the two that do not are now the only thing between this build and
a running scanner. Call-site collapsing gave FlowerCommit 12.7 percent and is
mined out; CompactDirtyFlowerSymbols needs roughly a third and Drain roughly
two thirds, which for Drain means splitting the entry point rather than
another local rewrite.

APP STATE ON DEVICE: killed by the OS during pipeline creation, so it is not
running. Nothing about the draw path changed; that fix is unaffected.

---

### CURRENT TRUE STATE — what each part of the drain MEANS, 2026-09-08 16:10Z

THIS ENTRY EXISTS BECAUSE THE PRECEDING WORK WAS SYNTACTIC. Counting
instructions, collapsing call sites and flattening arrays reduced the modules
but produced two wrong conclusions and one broken semantic, because none of it
asked what the code is FOR. The reading below is by meaning; the byte figures
are attached to purposes, not to functions.

WHAT THE DRAIN IS. Closure section4.3: FlowerCommit closes the work whose
dependencies are tile-local and available inside its quantum, and THE DRAIN
GETS ONLY WHAT IS LEFT. It is the continuation-workset consumer, walking one
linear cursor. Stages advance only across the global Finalize barrier, because
a child may not be processed before its required ancestors are complete.

  cursor 0                      .. ROOT_PHASE_TASKS   R2/R3 root phase, stage 0
  .. + R2_L1_TASKS (144)                              level 1 children, stage 1
  .. + R2_L2_TASKS = PHASE_TASKS                      level 2 children, stage 2
  SKIN_CURSOR_BASE .. 128 carriers x 57 parent steps  skin, stage 3

THE THREE MODES, BY MEANING:

  mode 0, stage<3, 419 764 B
    Seals the shared phase root of a junction J = 2K+d. Both endpoints must
    evaluate the identical circle bit-identically. This is the geometric truth
    the whole lattice rests on.
  mode 1, once after the root phase, 109 748 B
    Post-root R3 junction check. IT PRODUCES NOTHING. M8FlowerReadR3Junction's
    only consumer here is M8_COUNTER_REFINEMENT_UNRESOLVED, and that counter is
    read by exactly one place in the repository, the telemetry string in
    MerkabaGpuTimestamps.cs:697. 99 112 B of exact interval algebra for a debug
    counter. It cannot be deleted from the codebase, because the same function
    IS productive in M8FlowerPrepareCompletionPetal in the readout, where its
    classification gates the completion petal. In the drain it is diagnostics.
  mode 2, stage==3, 1 348 288 B
    Skin: captured RGB radiance and the V microrelief on L3-L5.
  shared base, 469 628 B
    Guards, invalidation drain, cursor and task decode, and PrepareFinePacket,
    which fills the packet of original phase roots.

FOUR CONSEQUENCES THAT FOLLOW FROM MEANING, NOT FROM BYTE COUNTS:

  1. GEOMETRY AND SIGNAL ARE ALREADY SEPARATE. The frozen invariant says
     geometry terminates at L2 and L3/L4/L5 are signal only. Modes 0 and 1 run
     only at stage<3, mode 2 only at stage==3; they never coexist in one
     dispatch. Splitting them is not an invention, it only makes the module
     reflect a boundary that already governs execution.
  2. MODE 1 IS DIAGNOSTIC, not a producer. See above.
  3. RGB AND V SHARE A LOOP FOR PUBLICATION ORDER, NOT FOR COMPUTATION. The
     source says why: "A BUSY RGB writer cannot fall through into V
     publication." Their bodies are five percent similar by line. They are two
     independent captured authorities, 222 112 B and 429 264 B.
  4. THE COMPUTE-ONCE-THEN-READ MECHANISM ALREADY EXISTS, AND THAT CORRECTS
     MY EARLIER PLAN. M8FlowerReadOriginalShared switches on
     M8_FLOWER_GEOMETRY_PACKET_READ between recomputing the original shared
     root and reading it from the fine packet, and both
     MerkabaFlowerRefinement.hlsl and MerkabaReadout.compute already define it.
     The lever is pulled, and at the right level: a per-observation packet
     rather than a static table, because the values depend on the MEASURED
     plane, not on the lattice. My earlier "move it into the generated tables"
     framing was one level off; the design had already solved it correctly.

WHERE THE DECOMPOSITION ACTUALLY BINDS. The skin stage divides by meaning into
a chart, which is where on the surface the sample sits, and two signals, which
is what is measured there:

  chart   PrepareFineSites 310 656 + ClassifyL2Carrier 104 756
          + frame/chamber/pixel-cover about 281 500        = 696 912 B
  signal  RGB 222 112   |   V 429 264

Both signals need the chart. Base plus chart is 1 166 540 B, ALREADY ABOVE THE
825 KB THE DEVICE ACCEPTS BEFORE EITHER SIGNAL IS ADDED, so splitting RGB from
V does not by itself close anything. Closing it requires publishing the chart
so a second pass reads instead of recomputing it, and the chart currently lives
in groupshared inside one workgroup. That is a data-flow change, not a code
move, and it is NOT made here.

WHAT THIS COMMIT CONTAINS. The seven reduction banks were seven groupshared
arrays reached through a seven-way switch in M8FlowerPacketLoadWord and
M8FlowerPacketStoreWord, but the switch only re-derived an offset the address
already carried. They are now one flat bank. Storage is unchanged at 14 336
groupshared bytes.

  DrainObservationRefinement 2 351 204 B /126 167 -> 2 253 604 B /118 954
  CompactDirtyFlowerSymbols  1 217 516 B / 64 998 -> unchanged
  FlowerCommit                 825 256 B / 43 847 ->   826 324 B / 43 909

FlowerCommit rises by 1 068 bytes, one tenth of one percent, because it uses
the banks by name and now carries the bank offset; it stays far below the
944 896 B that failed and just above the 825 256 B proven to compile.

THE FIRST ATTEMPT AT THIS WAS WRONG AND THE SUITE CAUGHT IT. I masked the
address flat, but the switch had a default arm: banks at or above six aliased
onto the representative bank, and the fine packet really does use such
addresses, since M8_FINE_PACKET_READY is 3456 and the skin control region runs
past it. The exact mapping is min(address>>9,6)*512 + (address&511), which is
the identity below 3584 and folds onto bank six above it. That is the second
time in this session a model replaced a measurement and was wrong.

Tools/unity/run_merkaba_tests.sh: 364 total, 364 passed, 0 failed.

---

### CURRENT TRUE STATE — the drain's diagnostic mode is gone, 2026-09-08 16:35Z

STEP 1 OF THE GEOMETRY/SKIN BOUNDARY REPAIR. The drain had three modes; the
middle one produced no state at all. M8FlowerReadR3Junction ran once after the
root phase and its only consumer was M8_COUNTER_REFINEMENT_UNRESOLVED, which
the whole repository reads in exactly one place, the telemetry string in
MerkabaGpuTimestamps.cs:697. The branch ended in junctionCheck=false and
continue, advancing no cursor and admitting no record, exactly as its own
comment said.

Removed from the production drain: the mode selector's diagnostic arm, the
junctionCheck loop guard, the per-owner R3 read and its counter increment, and
the M8FlowerSupportCacheDualTile warm-up that hung in the same branch.
M8FlowerReadR3Junction ITSELF IS UNTOUCHED and stays where it is functionally
required, in M8FlowerPrepareCompletionPetal, where its classification gates the
completion petal.

  DrainObservationRefinement 2 253 604 B /118 954 -> 2 149 788 B /113 557

Tools/unity/run_merkaba_tests.sh: 364 total, 364 passed, 0 failed.

NEXT, AND STATED BEFORE IT IS ATTEMPTED SO IT CANNOT BE FUDGED: cutting
M8FlowerPrepareFineSites from fourteen alternatives to the seven selected ones
reduces RUNTIME work but NOT module size, because its 310 656 B is the body of
M8FlowerReadL2Knot, emitted once whatever the trip count. The 415 412 B of L2
rediscovery only leaves the skin module if the skin reads those roots from the
canonical phase records through M8_FLOWER_GEOMETRY_PACKET_READ instead of
re-deriving them.

---

### CURRENT TRUE STATE — the drain is two entry points on one chain, 2026-09-08 17:05Z

STEP 2 OF THE GEOMETRY/SKIN BOUNDARY REPAIR. Not two algorithms: one
serialized continuation chain entered at the boundary the contract already
freezes, geometry through L2 and L3..L5 as signal only.

  DrainFlowerGeometry   stages 0..2, the former mode 0    882 492 B / 47 076
  DrainFlowerSkin       stage 3, the former mode 2      1 638 848 B / 86 328
  before the split                                     2 149 788 B /113 557

THE SPLIT IS BY PREPROCESSOR, NOT BY A RUNTIME FLAG, AND THAT IS THE POINT.
The body moved to MerkabaFlowerDrainBody.hlsl with no include guard and is
included twice under M8_DRAIN_SKIN 0 and 1. A runtime flag would leave both
branches in both modules, which is the entire cost this split exists to
remove. Each entry returns immediately outside its own stage class, while the
invalidation stages still pass through both so epoch hygiene is never skipped.

Wired through: the two #pragma kernel entries, the native generator PIPELINES
in cursor order with the allocation barriers now following DrainFlowerSkin,
PipelineNames in MerkabaNativeVulkanExecutor, MerkabaIntegrator's single
kernel handle becoming two that are bound and dispatched back to back with no
barrier between them because they are mutually exclusive by stage, and both
tests that resolved the kernel by name. MerkabaObservationDrainGpuTests now
drives the whole chain through both entry points.

Tools/unity/run_merkaba_tests.sh: 364 total, 364 passed, 0 failed.

STEP 3 IS THE ONE THAT MATTERS AND IT IS NOT DONE. DrainFlowerSkin still
rediscovers the L2 carrier it is supposed to consume: M8FlowerPrepareFineSites
then M8FlowerClassifyL2Carrier run before either signal, 415 412 B of geometry
discovery inside a signal stage.

---

### CURRENT TRUE STATE — the split verified on device, ABI 17, 2026-09-08 16:15Z

THE SPLIT ENTRY POINTS WERE MEASURED ON 340YC20G7X0QZ4, and the first build
attempt failed before reaching the device, which is worth recording because I
briefly reported it as a success. The native executor pins its pipeline count:

  static_assert(kMerkabaExecutorPipelineCount == 23)  ->  '24 == 23'

Splitting the drain makes it 24, so the build stopped and the deploy never ran;
what I measured at that moment was the previous APK. Fixed as an ABI change
rather than a loosened assertion: kExecutorAbiVersion 16 -> 17 on both the
native and the C# side, since the pipeline table genuinely changed shape.

MEASURED, cold cache, after the real deploy:

  index=11 FlowerCommit          825 256 B   ms= 9 092   result=0
  index=12 DrainFlowerGeometry   882 492 B   ms=24 537   result=0
  index=13 DrainFlowerSkin     1 638 848 B   ms=83 643   result=-13
           and the compile evicted horizonos.openxr.runtimebroker,
           com.android.settings and com.android.providers.calendar

THE DRIVER BRACKET IS NOW 882 492 ACCEPTED, 944 896 REFUSED. That is the first
accepted size above 825 256 and it puts the estimated resolver, about 878 kB,
below a proven bound rather than inside an unknown one.

COMPILE COST IS STEEPLY SUPERLINEAR AND THAT MATTERS FOR THE TARGET:
825 256 B takes 9.1 s, 882 492 B takes 24.5 s, 1 638 848 B takes 83.6 s and
then fails. Just under the bracket is not a safe place to sit; the resolver
should be aimed nearer 800 kB than 880 kB.

Tools/unity/run_merkaba_tests.sh: 364 total, 364 passed, 0 failed.

NEXT, AND THE STORAGE SHAPE IS DECIDED BY A BOUND, NOT A PREFERENCE. A fixed
receipt table per (touched tile, owner) is impossible: _M8TouchedTileQueue has
PhysicalTileCapacity 32768 entries, so 32768 x 512 x 224 B is 3.7 GB. The
receipt therefore goes in the bounded transient arena the decision allows: one
block of 512 slots per touched tile, one atomic reservation per workgroup, the
block base in a small per-tile table, and BUSY with deferral through the
existing REFINEMENT_PENDING_TILES beyond capacity. Starting capacity is 128
blocks, 14.7 MB, to be raised only if the device shows deferral thrashing.

---

### CURRENT TRUE STATE — the drain is four stages on one chain, 2026-09-08 21:00Z

THE GEOMETRY/SKIN BOUNDARY IS REPAIRED. The skin stage no longer rediscovers
the L2 carrier it is supposed to consume. The chain is

  DrainFlowerGeometry -> ResolveFlowerCarriers -> DrainFlowerSkinRgb
                      -> DrainFlowerSkinV -> Finalize

and the resolver is the ONLY place that predicts a parent, applies the
persisted R2/R3 residual, classifies the carrier, selects the seven actual L2
sites and converts them to complete world intervals.

  DrainFlowerGeometry     882 708 B / 47 089   writable bindings 7
  ResolveFlowerCarriers   715 124 B / 37 293   writable bindings 6
  DrainFlowerSkinRgb      382 572 B / 19 812   writable bindings 6
  DrainFlowerSkinV        581 760 B / 29 791   writable bindings 6
  before, one skin entry 1 638 848 B / 86 328

Three of the four are far below the 882 492 B the driver has accepted, and
DrainFlowerSkinRgb is under even the 524 288 B soft threshold. 26 pipelines
embed with no FAIL.

THE ACCEPTANCE IS A MEASUREMENT, NOT A READING. Stubbing M8FlowerReadL2Knot,
M8FlowerApplyGeometryDetail, M8FlowerClassifyL2Carrier,
M8FlowerPredictGeometryNode and M8FlowerEvaluateOriginalShared changes
DrainFlowerSkinRgb and DrainFlowerSkinV by EXACTLY ZERO BYTES. No geometry
admission survives in either signal pass.

WHAT ACTUALLY BROKE THE SIZE WAS THE SHARED DRAIN BODY, exactly as the
decision predicted. The signal entries were dragging the geometry prologue:
the invalidation writer and M8FlowerPrepareFinePacket. Confining the
invalidation writer to geometry and the original shared-root packet to
geometry and the resolver took V from 911 048 to 581 760 and the resolver from
837 092 to 715 124.

THE RECEIPT IS NOT A NEW WORLD. Three storage attempts were rejected on
evidence before the fourth was written:
  a dense (touched tile, owner) table is 32768 x 512 x 224 B = 3.7 GB;
  a new arena is a parallel allocator beside the one the tile/owner ontology
    already implies;
  _M8FlowerPageDirectory is declared writable only under M8_FLOWER_PAGE_WRITE,
    which the readout defines and the observation deliberately does not, so
    writing receipts there would have made the observation a page writer.
What remains is a bounded transient region of _M8FlowerDetailPages, the buffer
the drain already writes, addressed as base + tileItem*block + local*192 with
no allocator and no directory. It sits after the persistent arena and
M8FlowerDetailRange refuses it, so no persistent path can reach it. Lifetime is
the observation token plus slot generation. 64 tiles in flight; a tile beyond
that defers through the existing REFINEMENT_PENDING_TILES rather than
allocating.

Identity in the receipt is integer and generated: FlowerKey, activeWedgeMask,
rootSigns and the site mask, plus token, generation, parent cursor and status.
The seven world intervals use the same packing M8FlowerFineStoreWorld already
uses, so complete intervals are preserved by construction. Identity is never
derived back from them.

THE RGB TO V TRANSACTION SURVIVES THE SPLIT. RGB publishes RGB_OK or RGB_BUSY
into the receipt and V consumes only RGB_OK, which is the same rule the shared
barrier used to enforce inside one dispatch. Only V advances the cursor; the
resolver and RGB store the cursor they entered with, so their retry is
idempotent under the same observation token and cannot skip work the
observation still owes.

Tools/unity/run_merkaba_tests.sh: 364 total, 364 passed, 0 failed.

TWO MISTAKES OF MINE COST ABOUT FORTY MINUTES AND BOTH WERE THE SAME KIND.
A wait keyed on `pgrep -f "Editor/Unity"` matched my own waiting shells, whose
command lines contain that string, so five of them span forever matching each
other. And the running log had already printed
"Property (_M8FlowerDetailPagesRead) at kernel index (3) is not set" twenty
minutes before I acted on it; I waited for a complete failure list that could
never arrive, because without that binding the drain never completes and the
proof burns its whole 5 344-attempt budget. The frozen-world harness now binds
the read-only view, as production always did through BindFlowerPages.

DEVICE ACCEPTANCE PENDING for the four new pipelines.

ONE DISPATCH TRIPLE OWNS EXACTLY ONE L2 CARRIER. The skin cursor advance could
cross into carrier+1 inside the loop while the resolver had written receipts
only for the carrier the triple entered with. The signal passes would then read
an unwritten receipt as an already finished parent and step the cursor straight
over that carrier's skin work, silently dropping it. The quantum is now bounded
to the entry carrier and the next triple picks the successor up. Sizes moved by
+152 B, which is the guard itself.

THE SIZE HYPOTHESIS WAS WRONG AND THE MEASUREMENT SAYS SO. ResolveFlowerCarriers
failed pipeline creation at 715 124 B while DrainFlowerGeometry succeeded at
882 708 B. I had spent a day on "the boundary lies between 882 492 and 944 896
bytes". It does not. Disassembling the five flower entry points and counting
what the driver actually has to allocate:

  entry                    instr   blocks  locVars  GS      maxDynArray  device
  FlowerCommit             45 419   4 606   259     24 704       4       creates
  DrainFlowerGeometry      48 605   4 946   284     20 616       4       creates
  ResolveFlowerCarriers    38 799   3 473   254     20 628      14       VK -13
  DrainFlowerSkinRgb       20 998   1 951    96     20 628      28       untested
  DrainFlowerSkinV         31 315   2 663   168     20 628      28       untested

The failing entry is the SMALLEST of the three tested by instructions, blocks
and local variables, and its groupshared is within twelve bytes of one that
creates. Every one of those metrics fails to separate creates from fails. One
metric separates them completely: the longest dynamically indexed function-scope
array. Both entries the driver accepts top out at four. In ResolveFlowerCarriers
the two longest were M8FlowerInterval3 support[14] and uint proofTags[14], and
all ten access chains into them are dynamic, zero static.

That is not a new rule. It is 4.5 measured: "Rozdelit nezavisle owner/carrier/
site polozky mezi lanes ... Neprenaset vsechny kandidaty v lokalnich polich
jednoho lane."

THE FIX IS WHERE THE CANDIDATES ARE ENUMERATED, NOT WHAT THEY ARE. A wedge's
eight triples reference only the six alternatives of its own three sites, so
the candidates are now enumerated inside the wedge that consumes them: same
reader, same arithmetic, same intervals, same order. The six per-wedge triple
masks are eight bits each, so certain/uncertain/directTriples travel packed in
two words instead of three six-entry arrays. glslang's HLSL front end does not
honour [unroll] here, so the seven published roots are named rather than looped.
ResolveFlowerCarriers now has no dynamically indexed function array longer than
three, below both entries the driver accepts.

WHAT IT COST. ResolveFlowerCarriers 715 276 -> 721 068 B, +0.8 percent, because
the wedge-local read costs one more inlined reader. CompactDirtyFlowerSymbols
1 217 516 -> 1 236 104 B; it shares the classifier and it was already over the
1 MiB gate, so I made a failing gate 1.5 percent worse and it stays OPEN-1.

WHAT IS STILL UNPROVEN. Only 4 (creates) and 14 (fails) are measured. Nothing
between five and thirteen is. The skin entries still carry uint words[28],
float2 intervals[21] and a row of seven-entry children arrays; 28 and 21 are
flattenings of the seven-child ontology, seven is the ontology itself.

Tools/shaders/audit_merkaba_compute_spirv.sh: all 70 kernels compile; 3 FAIL,
none new to this change. Tools/unity/run_merkaba_tests.sh: 364/364.
DEVICE ACCEPTANCE PENDING.

THE SKIN ENTRIES CARRIED TWO FLATTENINGS AND ONE ALIAS. Neither flattening was
the ontology. float2 intervals[21] is a seven-child group flattened over three
channels; the RGB split test is exactly the scalar split test applied per
channel and ORed, so it is now evaluated one channel at a time against
float2 intervals[7]. Identical existential, identical guard, identical short
circuit. The generated twenty-one entry form stays the parity authority and the
oracle still calls it, so no fixture was lost. uint words[28] is a seven-child
group flattened over four words; the commit path now carries one uint4 per
child and the consumer selects the component. The split algebra itself was NOT
touched: children read from the packet are not revalidated at that point, so
replacing the pairwise test with a max-lo/min-hi reduction would have changed
SPLIT for an inverted interval. Correct on valid input is not the same as
identical.

M8FlowerRestoreSkinCarrier read receipts through _M8FlowerDetailPagesRead while
the same kernels bind _M8FlowerDetailPages for writing, which is the RW/read
alias pair the audit forbids. It now reads through M8_FLOWER_DETAIL_SOURCE,
the convention this file already had. RO bindings fall 13 -> 12 and both skin
entries lose the FAIL.

  entry                  maxDynArray before -> after   bytes
  ResolveFlowerCarriers        14 -> 3        715 276 -> 721 068
  DrainFlowerSkinRgb           28 -> 7        382 724 -> 382 424
  DrainFlowerSkinV             28 -> 7        581 920 -> 581 684

Seven is where it stops, because seven is M8FlowerVInterval children[7] and
M8ThreadColorInterval children[7]: the seven-child group itself. Reducing that
would be a data model change, not a compile surface change. The device run now
asks exactly one question that four and fourteen do not already answer.

Tools/shaders/audit_merkaba_compute_spirv.sh: 70 kernels, 1 FAIL, and that FAIL
is CompactDirtyFlowerSymbols over the 1 MiB gate, which is OPEN-1 and predates
this change. Two alias FAILs are gone. Tools/unity/run_merkaba_tests.sh: 364/364.
DEVICE ACCEPTANCE PENDING.

### DEVICE ACCEPTANCE — all 26 pipelines create, 2026-09-08 22:05Z

Quest 340YC20G7X0QZ4, APK built 22:00:30 from f1c3b40, cold pipeline cache.

  init worker: state=ready family=0 requiredPipelines=26 elapsedMs=226164.148

No VkResult=-13 and no "scanner disabled". The three entries this work targeted:

  index=13 ResolveFlowerCarriers     ms= 18729.149  result=0
  index=14 DrainFlowerSkinRgb        ms=   460.384  result=0
  index=15 DrainFlowerSkinV          ms=  4649.229  result=0

ResolveFlowerCarriers creates at 721 068 B. The build that failed with -13 was
715 124 B. It is now 5 944 B LARGER and it creates. That settles the size
hypothesis by measurement rather than by argument: the discriminator was the
longest dynamically indexed function-scope array, 14 -> 3. Seven is also proven
acceptable now, which is what mattered for the skin entries, because seven is
M8ThreadColorInterval children[7] and M8FlowerVInterval children[7] and could
not have been reduced without changing the data model.

WHAT THIS DOES NOT PROVE. Pipeline creation and readout dispatch are proven;
scanning is not. The native queue runs FlowerReadout with real timestamps
(5 dispatches, total=0.052 ms, validBits=48, single-serialized-native), but
DepthCapture reports rawFrames=18881 preprocessed=0 ready=False depthAvail=False
isOcclusionOn=0 and observation=0. Depth frames arrive and are not consumed. No
observation, drain, FINE/ERASE, save/open or export has been exercised. OPEN-6
acceptance is untouched by this run.

OPEN-1 IS NOT CLOSED AND THIS RUN MADE THAT WORSE, NOT BETTER.
CompactDirtyFlowerSymbols compiles in 152 500 ms of the 226 164 ms cold startup
and its module is 1 236 104 B against the 1 MiB gate. During that compile the
Adreno compiler drove the device past its low watermark and lowmemorykiller
killed four system processes to make room: horizon.platform.providers,
com.android.settings, com.oculus.updater and com.oculus.horizon. Our process
survived at 2.39 GB RSS with 4.7 MB swap. A 3.8 minute cold start that evicts
the system shell is a defect with a device receipt now, not an audit number.

Warm cache is unmeasured; state=cold on this run.

### THE NEXT BUG THE WORKING PIPELINES EXPOSED — leased dual world, 22:06Z

With all 26 pipelines created, the first attempt to start a new session failed:

  [RoomScan] Could not create a new Merkaba session:
  System.InvalidOperationException: Cannot clear a leased dual GPU world.

This was never reachable before. ClearGpuWorldForNewScan refuses to clear while
MerkabaNativeVulkanExecutor.HasJobInFlight, and the FlowerReadout job is
submitted from OnContextRendered on every rendered frame once the native
scanner is ready. While the scanner was disabled no job existed and the guard
was always satisfied. Now it is essentially never satisfied, so NewClearAsync,
OpenSessionAsync and DeleteSessionAsync-on-the-active-session could not
complete: all four _integrator.Clear() sites reach the same guard, and those
three paths only called QuiesceScanningAsync, which stops scanning and leaves
the readout job running.

THE FIX IS THE ORDER THIS CODE ALREADY ESTABLISHED, NOT A NEW ONE.
DisableTeardownCoreAsync already solves exactly this: suspend the renderer,
quiesce scanning, then AWAIT FinishCurrentReadoutAsync and
FinishObservationDurableCutAsync. That is now QuiesceForWorldClearAsync and the
three world-replacing paths use it. Closure 4.7 is explicit that in-flight
Vulkan work is waited out rather than preempted by a CPU flag, which is why the
existing await primitives are reused instead of relaxing the guard.

The two _gpuSubmissionSuspended fields are NOT the same flag and that is what
makes this safe. MerkabaGrid's field drives GpuSubmissionAllowed, which
SwitchStorageRootAsync requires to be true when it clears. MerkabaGridRenderer's
field only gates OnContextRendered. Suspending the renderer stops new readout
jobs while leaving GpuSubmissionAllowed true.

SaveAsAsync deliberately keeps the GPU world, so it still uses the plain
scanning quiesce and must not stall the renderer; the regression test asserts
that too, along with the suspend/quiesce/readout/durable order.

Tools/unity/run_merkaba_tests.sh: 365 total, 365 passed, 0 failed.
DEVICE ACCEPTANCE PENDING for the session lifecycle itself: new session, open
session and delete-active are untested on the headset after this change.

### DEVICE ACCEPTANCE — warm cache and the session lifecycle, 2026-09-08 22:17Z

APK 22:16:49 from da37199, same Quest, second launch of the same shader ABI.

  pipeline cache: state=loaded bytes=3716444 shaderHash=1d19b93a115de546 result=0
  native init worker: state=ready family=0 requiredPipelines=26 elapsedMs=116.242

116 ms against 226 164 ms cold. The 152 s CompactDirtyFlowerSymbols compile is a
first-run cost per shader ABI, not a per-launch cost. That does not close OPEN-1
-- the module is still over the 1 MiB gate and the cold compile still evicted
four system processes -- but the severity is a one-time install cost, and the
ledger should not have implied otherwise.

  [RoomScan] Started a new empty anchored Merkaba session
             7f9b28f2-c21e-ed83-af16-cd44aca687cd

Zero occurrences of "leased dual GPU world" in this run. The new-session path
now completes under exactly the conditions that broke it: the native scanner was
ready 18 s earlier and the FlowerReadout job is submitted every rendered frame,
so the lease was held and was waited out rather than refused. OpenSessionAsync
and DeleteSessionAsync-on-active share QuiesceForWorldClearAsync but were not
exercised on the headset and stay PENDING.

DEPTH IS STILL NOT CONSUMED AND THAT IS UNTOUCHED BY TODAY'S WORK.
DepthCapture reports rawFrames climbing past 5644 with preprocessed=0
ready=False depthAvail=False isOcclusionOn=0, while occMgr.enabled=True
running=True shaderOcc=True hardKeyword=True globalDepth=True. observation=0
throughout. Frames arrive and no observation is formed, so no scan, drain,
FINE/ERASE, save/open or export has been proven. OPEN-6 remains open.

### THE SECOND CONSEQUENCE OF A BUSY NATIVE QUEUE — save counting, 22:2xZ

Reported from the headset: SAVE sits on "Counting dirty canonical tiles".

PumpStorage gates its whole GPU section on
  if (!GpuSubmissionAllowed || MerkabaNativeVulkanExecutor.HasJobInFlight) return;
and the dirty-page readout job measures lifetimeMs=42.6 (queueFenceMs=28.4,
acquireFenceMs=13.0) while being resubmitted from OnContextRendered every
rendered frame.

BE PRECISE ABOUT THE MECHANISM: THIS IS DEGRADATION, NOT DEADLOCK. The pump is
driven from MerkabaGrid.Gpu.cs and does get a window between a job completing in
LateUpdate's poll and the next frame's submission. What makes the flush crawl is
that the counter readback is an AsyncGPUReadback whose CALLBACK re-checks the
same gate: if a new job started while the readback was in flight the sampled
counters are discarded and the attempt is retried only after the 0.05 s
_nextStreamPoll throttle. With the queue occupied most of the time most attempts
are thrown away, so the flush advances at a small fraction of its rate. I first
wrote "starved" and "never"; the evidence says slow, which is what "trva" meant.

THE FIX IS THE PRIORITY THE CONTRACT ALREADY STATES, NOT A RELAXED GATE.
Closure 4.7: "Mezi bounded jobs/kvanty plati priorita FINE/ERASE -> observation
-> dirty page -> WARM." The dirty-page readout is the lowest bounded rank above
WARM, so it must yield to a pending canonical flush instead of competing with
it. MerkabaGridRenderer.OnContextRendered already yields to the integrator's
observation and fine-erase work; MerkabaGrid.HasPendingCanonicalFlush joins that
same condition, so no new page quantum is submitted while a flush is pending and
the in-flight one drains within about 42 ms. One authority, and it covers every
flush caller including autosave and export rather than each save entry point.

SAME CLASS, NOT YET ADDRESSED: BeginLoadAddressReadback and the writeback batch
sit behind the same gate and the same discard-on-callback pattern, so COLD tile
loading and writeback are slowed by a busy queue in exactly this way. They are
not covered by the flush priority and no device evidence has been collected for
them. Recorded as suspected, not fixed.

Tools/unity/run_merkaba_tests.sh: 366 total, 366 passed, 0 failed.
DEVICE ACCEPTANCE PENDING for save.

### EVERY ACTION AND WHAT IT MEANS FOR THE WORLD, 22:39Z

The user stated the ontology plainly and it is the right one: a canonical M8
tile is COMPLETE at every instant. A running scan does not leave tiles
half-built, it keeps refining finished ones. So SAVE does not mean "let the
pending work finish", it means "take this instant". Nothing drains.

Two producers have to stop for an operation to own that instant: the scan, and
the dirty-page readout job. Only the first was ever stopped. The readout is
resubmitted from OnContextRendered every rendered frame once the native scanner
is ready, which is why one root cause produced three different faults: a leased
dual world on NEW, a crawling flush on SAVE, and quiet competition everywhere
else. Walking every action:

  action        meaning for the world              held before          now
  scan on/off   the producer of evidence           scan                 scan
  SAVE          freeze this instant, write dirty   scan only            frozen
  SAVE AS       same instant, new destination      scan only            frozen
  LOAD          REPLACE the world from SSD         scan only            frozen
  OPEN          replace the world from SSD         frozen               frozen
  DELETE active replace the world with empty       frozen               frozen
  NEW           empty world on a new anchor        frozen               frozen
  EXPORT        derived artifact, never truth      scan only            frozen
  IMPORT        register a package, then OPEN      scan only            frozen
  CLEAR ALL     delegates to NEW                   inherits             inherits
  rename        catalog label only                 nothing              nothing

LOAD was heading for the same lease guard that broke NEW and had not been hit
yet only because it had not been tried. EXPORT covers GLB and 3D Tiles together
because ExportGlbCoreAsync and ExportViewerPackageCoreAsync share RunExportAsync.

WHAT MUST NOT BE FROZEN, WHICH MATTERS AS MUCH. The refine tab's FINE and
FINE/ERASE are producers, and closure 4.7 ranks them ABOVE observation and
dirty page, so freezing them would inverting the contract's own priority.
OnContextRendered already yields to HasAttemptInFlight,
HasFineEraseAttemptInFlight, HasPendingFineErase and ObservationHasBoundaryPriority;
that is correct and untouched. The view toggles mutate nothing. Design, paint,
objects and annotations are the document layer, not M8: closure keeps membrane,
readout, GLB and 3D Tiles "derived only", and the import path states it in code
as "No imported mesh enters M8."

I ALMOST SHIPPED A REGRESSION AND THE CONTRACT CAUGHT IT.
SuspendGpuSubmission also clears _active, and MerkabaGridRenderer.TryGetActive
gates the flower draw on it. Using it for a frozen-world operation would have
blanked the scanned geometry for the whole of a minutes-long EXPORT. Closure 4.5
says "Renderer nesmi vyhladovet kvuli jedne dlouho zijici observaci". The pause
is now PausePagePublication, which stops only the submission of new page quanta;
published pages keep drawing and the separate cull path is untouched.

A POSITIVE FIXTURE FAILED AND IT WAS RIGHT TO.
DepthPreprocessTests.SaveAndExport_AwaitSharedQuiesceBeforeExplicitOperation
guards "quiesce precedes the read"; renaming the entry point hid the quiesce
behind a helper. Per closure 10.1 it was adapted, not deleted, and while doing
so one of its asserts turned out to be vacuous after the rename: a bare IndexOf
returning -1 satisfies "less than" and would have let the quiesce disappear
unnoticed. It is now anchored at >= 0 and additionally requires the helper to
retire the observation and wait the readout out.

STILL SUSPECTED, NOT FIXED: BeginLoadAddressReadback and the writeback batch
share PumpStorage's gate and its discard-on-callback pattern, so COLD tile
loading and writeback degrade the same way a save did. No device evidence yet.

Tools/unity/run_merkaba_tests.sh: 366 total, 366 passed, 0 failed.
APK 22:39:43 deployed. DEVICE ACCEPTANCE PENDING for save, load, open,
delete-active, export and import.

### ONE DUPLICATE UNIFORM WEDGED THE WHOLE APP, 22:41Z

Depth finally reached the observation path (preprocessed=1, depthAvail=True,
held=True) and immediately:

  InvalidOperationException: Duplicate native uniform value: _DepthProjInv

BuildNativeObservationUniforms writes _DepthProjInv and then calls
_depthCapture.WriteDepthCertificateUniforms(values), which writes it again into
the same table. Both read the same array (DepthCapture.ProjInv => _projInv), so
it is a pure duplication with identical values. It has been latent since
c1d2cfd "wip(m8): checkpoint native scanner queue" and only became reachable now
because depth had never been consumed before. A scan of both writers finds
_DepthProjInv is the ONLY name written twice out of 45.

_DepthProjInv belongs to the held observation's frozen set, which is what
WriteDepthCertificateUniforms documents, so the observation builder's copy is
the one that goes.

THE REAL FRAGILITY IS THAT ONE THROW WEDGES EVERYTHING, PERMANENTLY.
BuildNativeObservationUniforms runs at MerkabaIntegrator.cs:907, BEFORE
BeginNativeDualMutation and outside the try/catch that guards TryCreateJob. When
it throws, the observation has already been prepared, its token allocated and
its bins begun, but nothing is submitted and the hold is never released. The next
frame retries the identical deterministic input and throws again, forever. The
frame stays held=True ready=False, so QuiesceScanningAsync never completes,
so ScanLifecycle stays Quiescing, so IsBusy stays true, so the UI disables SAVE,
LOAD, NEW and DELETE. That is the whole observed symptom set from one line:
"scan active but nothing appears" plus "cannot click save". A deterministic
exception is a programming error, not evidence ambiguity, and closure 4.3's
retry rules do not cover it; retrying it identically forever is wrong. NOT FIXED
YET, recorded as the next cut.

Tools/unity/run_merkaba_tests.sh: 367 total, 367 passed, 0 failed. The new
fixture asserts every native uniform is named exactly once across the
observation builder and the certificate writer it calls.
APK 22:50:33 deployed. DEVICE ACCEPTANCE PENDING for the scan itself.

### PREVIEW BUDGET AND THE VIEWER'S PRODUCERS, 23:0xZ

TWO ALLOCATIONS PRECEDED EVERY BUDGET CHECK. ParseGlbForPreview read the JSON
chunk with `new byte[jsonLength]` bounded only by int.MaxValue and the stream
length, then UTF8.GetString allocated a UTF-16 string up to twice that, all
before maximumDecodedBytes was consulted. A large foreign model is therefore an
out-of-memory kill rather than a rejection, and the default budget for a library
import is LargePackageBytes/2 = 512 MiB, so nothing else stood in the way. The
JSON chunk is glTF structure, not payload: this writer emits a few KB, and a
foreign file's JSON grows with accessor and material count rather than with
vertex data. It now has its own 32 MiB cap, also clamped by the caller's budget.

PAGE PUBLICATION NOW FOLLOWS THE READOUT DRAW. Opening the artifact viewer sets
ReadoutDrawEnabled=false and restores it on close, which stops the draw through
TryGetActive. OnContextRendered did not check it, so the FlowerReadout job kept
being submitted every frame while nothing consumed the result. Published pages
have exactly one consumer, MerkabaFlowerVertex.hlsl. Publishing them with the
draw off is work nobody reads and it competes with paint and with an export
started from that same viewer. MerkabaGridRenderer.cs already gated another path
on !readoutDrawEnabled, so this follows the file's own rule.

THE FOREIGN ACCESSOR REWRITE IS PARKED, DELIBERATELY. Optional NORMAL/COLOR,
index component types and interleaved accessors are OPEN-4 work for importing
foreign models into the design layer, which is not on the stated priority path
(scan, draw, export GLB and 3D Tiles, then paint into a loaded package). The
reader is forward-only because a zip entry stream cannot seek, so interleaving
needs each view buffered once and released after decode - one largest view of
peak, not their sum. Designed, not applied.

OPEN-4 IS PARTLY STALE AND THE LEDGER SHOULD SAY SO. Verified against the code:
the full tile matrix IS carried (CollectTiles decomposes it and CreateTileObject
applies localRotation/localScale, and DecomposeTilesetTransform rejects
non-finite, degenerate, sheared and mirrored transforms rather than substituting
identity); DiscardResumeReceipt exists with a test. Three of five listed items
are already closed. Only the accessor decoding and the Tiles leaf cursor/journal
remain unverified.

WHERE PAINT ACTUALLY WRITES, AND THE GAP THAT MATTERS.
MerkabaPaintEngine writes a MerkabaDesignDocument of strokes and instances, never
M8, so closure's "derived only" holds and paint cannot become a truth producer.
But OpenSessionDesign binds the engine to _scanner.ActiveDesignPath and refuses
to open unless the previewed package's anchor equals the ACTIVE session anchor:
  "Session design requires the preview package's actual anchor to match the
   active anchored session."
So loading an already-exported 3D Tiles package from another session, or with no
active session, leaves nothing to paint into, and what does open is stored under
the session rather than with the package. The requirement is an ARTIFACT-LOCAL
design: bound to the loaded package and saveable straight back into it. The right
frame is package space via _packageSpatialBinding.AnchorFromPackage, not the room
anchor, so the result survives reopening in another session or on a PC. NOT
IMPLEMENTED; this is the next cut.

Tools/unity/run_merkaba_tests.sh: 367 total, 367 passed, 0 failed.

### A LOADED PACKAGE OWNS ITS OWN PAINT, 23:1xZ

OpenSessionDesign refused to open unless the previewed package's anchor equalled
the ACTIVE session anchor, so a loaded 3D Tiles export from another session, or
with no session at all, had nothing to paint into - the one thing the viewer
exists for. BindAnnotations already had the right rule for notes: a matching
anchored package edits that session's document, a foreign preview keeps its own
beside the archive. The design now follows that same rule instead of a second
mechanism, with a ".design.json" sidecar next to the ".annotations.json" the same
package already keeps.

PER PACKAGE BY CONSTRUCTION. The sidecar path is derived from the archive path,
and OpenSessionDesign saves and closes the engine before reopening it, so loading
a different package cannot show the previous one's paint. The honest limit of a
sidecar: moving or renaming the .zip leaves the paint behind. Writing the design
INTO the zip is the transport-correct answer and is not done yet.

WHY THIS CANNOT REACH TRUTH. Paint and placed library objects are a
MerkabaDesignDocument of strokes and instances; MerkabaPaintEngine never writes
M8. Samples are stored in the engine root's LOCAL space, so a foreign package's
paint lives in that package's own model frame and survives reopening elsewhere
and a later align, because it never referenced the room. Closure keeps membrane,
readout, GLB and 3D Tiles derived-only, and 8.3 states it directly: "Cizi glTF
neni anchorovany canonical scan."

ALIGN IS THE ONLY THING AN ANCHORLESS PACKAGE LOSES. SetRoomAlignedAsync already
refused with "ALIGN 1:1 unavailable: package has no spatial binding", so the
runtime was right; the toggle just promised it anyway. CanAlignToRoom now gates
the toggle, and the test asserts painting does NOT depend on being alignable.

### THE PANELAK SCENARIO AND WHY 6.4 m IS NOT ARBITRARY

Scan seven floors of 200 m2, save on the seventh, reopen on the fourth, align
the whole building. Two halves, and only one of them works today.

DRAWING IT ALREADY WORKS, and better than a draw distance. The viewer computes
frustum planes, scores every tile by in-view plus squared distance from the
camera with priority for the tile under the pointer, sorts, and keeps tiles only
while the resident budget lasts, destroying the rest; the status line reports
"full-load" versus "spatial streaming".

ALIGNING IT DOES NOT. MerkabaSpatialBinding carries exactly ONE AnchorUuid and
one AnchorFromPackage. A single ground-floor anchor cannot be relocalized from
the fourth floor - the anchor is not in the current map. That is a platform
limit, not a bug.

THE FIX THE USER PROPOSED IS EXACTLY ONE M8 BLOCK. MerkabaSpatial.BlockKernelSpan
is 256 and the lattice step is 0.025 m, so a block is 256 * 0.025 = 6.4 m, and
GlobalCoord = BlockCoord * BlockKernelSpan + Local. Every MerkabaTileAddress
already carries BlockCoord, so grouping tiles by anchor volume costs no new
index. Anchors stay sparse in exactly the way the lattice is sparse: one per
OCCUPIED block, nothing for empty space. For 200 m2 floors that is about 9 blocks
per storey and 4 blocks of height over 20 m, so roughly 36 anchors for a whole
building - and standing on the fourth floor you are inside a block whose anchor
is in the current map.

CONTRACT CHECK: 8.3 fixes the transform as anchorNow * inverse(anchorAtSave) *
sceneGridToWorld. That is a per-anchor relation, not a claim that there is one
anchor; each block anchor satisfies it with its own AnchorFromPackage. 8.3 also
demands an explicit state, retry or manual ALIGN on failure and forbids an
assumed identity transform, which is already the behaviour. So this adds inputs
to one existing frame rather than a second spatial authority.

WHAT IT TOUCHES, NOT YET IMPLEMENTED: the binding becomes a set of
(BlockCoord, anchorUuid, anchorFromPackage) with a package version bump from 1;
the scan creates an anchor per newly occupied block; the viewer localizes
whichever it can and derives the package pose from that one; version 1 packages
keep working with their single anchor.

Tools/unity/run_merkaba_tests.sh: 368 total, 368 passed, 0 failed.

### DEVICE MEASUREMENT — 0.5 fps AND NOTHING DRAWN HAVE ONE CAUSE, 23:22Z

Scan running on the Quest, APK 23:17:35. Per-dispatch GPU timestamps from the
native queue, one ObservationRetry:

  dispatch=0  ResetObservationBins             0.007 ms
  dispatch=1  CountObservationBins             0.043 ms
  dispatch=5  ReserveObservationBins           0.342 ms
  dispatch=7  UpdateObservationDual         3932.243 ms   <-- everything
  dispatch=9  FlowerCommit                     0.003 ms
  dispatch=10 DrainFlowerGeometry              0.002 ms
  dispatch=11 ResolveFlowerCarriers            0.002 ms
  dispatch=12 DrainFlowerSkinRgb               0.002 ms
  dispatch=13 DrainFlowerSkinV                 0.002 ms
  dispatch=17 FinalizeObservation              0.009 ms

  observation=6 UpdateObservationDual dispatches=8 gpuSumMs=26237.036
  observation=7 UpdateObservationDual dispatches=9 gpuSumMs=30833.007
  BuildDepthCertificate 0.300 ms   ReduceDepthCertificate 0.023 ms
  observation attempt unresolved observation=8 attempt=53
  attemptResidencyEpoch=24 currentResidencyEpoch=24
  metrics-held-refinement completed=0 pendingTiles=1 quantumProgress=1 stage=1
  gpu-coverage registered=39 seen=6 missing=33

DO NOT MISREAD gpu-coverage: it is telemetry name coverage, 33 pipelines simply
have not run yet. It is a consequence, not the cause.

WHAT THE DUAL IS, ONTOLOGICALLY. A depth sample asserts two things: the surface
is OCCUPIED and the whole volume between the camera and it is FREE. The dual is
that free-space certification. It walks block 6.4 m -> chunk 0.8 m -> tile 0.2 m
and writes ALL_THROUGH at the COARSEST node it can prove entirely in front of
the surface, descending only into MIXED. That is what makes carving affordable,
because most of a room is air.

THE ALGORITHM IS RIGHT AND IS NOT THE BUG. M8_DEPTH_CERTIFICATE_SIDE 512 with
10 levels per eye is a full min/max pyramid; M8DepthCertificateThrough takes ONE
lookup at the envelope level and only then descends a bounded quadtree, accepting
fully contained nodes at their own level and pruning the rest. M8SupportThrough
encloses the owner-support union in exact interval arithmetic before projecting.
M8SupportThrough is consulted at block span 256 and chunk span 32 with
`through ? 255u : M8ScanChildMask(...)` pruning. That is 4.7 implemented as
written.

THE MISUNDERSTANDING IS THE CONTINUATION, NOT THE DUAL. The generator builds
    retry = observation[observation.index("ResetObservationBins"):]
so a RETRY of a FROZEN observation re-runs UpdateObservationDual. The dual is a
function of the frozen depth, the frozen pose and the resident block set, all
immutable inside one observation, and the log proves the inputs did not move:
attemptResidencyEpoch equals currentResidencyEpoch. So the same free space is
re-certified for 4 s on every attempt while the refinement advances
quantumProgress=1. Closure 4.3 states the rule in one line: "Drain dostane jen
zbylou praci."

AND THE QUANTUM MAKES IT A HUNDRED-FOLD. RefinementCandidatePassesPerQuantum is
64. One tile owes M8_FLOWER_PHASE_TASKS (1336 in the harness) plus the skin
cursor of 128 carriers x 57 = 7296, so about 8600 cursor positions, i.e. roughly
135 attempts per tile. 135 attempts x 4 s of unchangeable dual is about nine
minutes for ONE tile.

WHY NOTHING IS DRAWN IS THE SAME FACT. Completion needs stage 3 with
pendingTiles 0; the device sits at stage=1 pendingTiles=1, so
CompletedObservationToken is never stamped, nothing retires into the readout, and
the flower entries run 2 us because at stage 1 they are handed one quantum of
nothing. This is not a draw defect. FinalizeObservation owns the stage machine
(MerkabaIntegration.compute 600-630), NOT the dual, so a retry that omits the
dual still advances.

NEXT, IN THIS ORDER, NEITHER DONE:
  1. Omit UpdateObservationDual and its publication-only ReserveObservationBins
     from the retry schedule while the inputs are unchanged - same observation
     token, same residency epoch, empty new block/chunk/tile queues. Those are
     exactly the counters the dual already reads for its own `ready`, so no new
     state is introduced. It changes the native schedule, so it raises the
     executor ABI.
  2. The quantum. 135 attempts per tile is stepping, not continuation. Measure
     what one quantum actually costs before changing 64, rather than guessing.

The first dual pass itself is also slow - 125 blocks at 5 m radius, 31.5 ms per
6.4 m block - but that is a separate question worth its own measurement once the
50-to-135x repetition is gone.

### CORRECTION — THE ARRAY RULE I RECORDED IS TOO STRONG

Earlier today I wrote that no production entry may carry a dynamically indexed
function-scope array longer than four. UpdateObservationDual falsifies it from
the same build: it carries FOUR 64-entry and THREE 16-entry dynamically indexed
function arrays and it creates on the device in 3263 ms.

  entry                        bytes    body    maxDynArray  device
  UpdateObservationDual       494 360  24 925        64       creates
  ResolveFlowerCarriers (old) 715 124  ~37 300       14       VK -13
  DrainFlowerGeometry         882 708  47 089         4       creates
  FlowerCommit                826 324  43 907         4       creates

So neither metric predicts creation on its own: a small module tolerates 64-entry
indexed arrays, and a large one failed at 14. The honest statement is that the
driver's limit is the COMBINATION of module size and the register/scratch
pressure those arrays create. Today's fix is still consistent with that - removing
the arrays let a LARGER module create - but the rule as written was stronger than
the evidence, and anything relying on "<= 4" as a threshold should not.

### THE DUAL IS THE SECOND OF A PAIR, AND THAT CHANGES THE DIAGNOSIS

Corrected framing from the user, and it is right. The primary scan is M8 evidence:
occupied, free and unknown, with ON 512 / OFF 128 / SURFACE 640 / FREE 256 and
0.150 m clearance. The dual - M8DualBlockState, M8DualChunkState, M8DualLeaves -
is the SECOND, auxiliary structure: an aid to refinement and a defence against
artefacts. A guard has no business costing 3932 ms of a 3933 ms attempt.

WHAT THE DESCENT ACTUALLY DOES. Per block, 64 lanes cover the 8x8 (d4,d3) octant
pairs at spans 128 and 64 from M8ScanChildMask, which is a GEOMETRIC test of what
intersects the scan volume. That reaches up to 512 chunk slots per block, and
inside each chunk that is not certified through, two more geometric masks at
spans 32 and 16 reach up to 8x8 = 64 tile slots. Existence is consulted only at
the LEAF, via _M8ChunkTileRefsRead[chunk*64+tile] - after the visit has been paid
for. Worst case that is 32768 slot visits per 6.4 m block, and the measurement is
31.5 ms per block over 125 blocks.

Closure 4.7 says the opposite in one clause: "MIXED rozvijí pouze nutne potomky
V EXISTUJICIM block/chunk/tile indexu." The descent must be driven BY the sparse
index intersected with the scan volume, not by enumerating the volume and asking
at the leaf whether anything is there. For a room scanned at 25 mm the occupied
set is a thin shell, so the existing-index descent visits a small fraction of
those slots.

IT NEEDS NO NEW BINDING. UpdateObservationDual already binds
_M8BlockChunkRefsRead, which says which of a block's 512 chunks exist, and
_M8ChunkTileRefsRead, which says which of a chunk's 64 tiles exist - both
read-only, both already there. The coarse certificate keeps its job: uniform
volumes are certified at block or chunk level with no descent at all, and COLD
children are still requested from SSD. Nothing has to visit a tile that does not
exist.

SO THE ORDER OF WORK CHANGES. Driving the descent from the existing index is the
primary fix and needs no ABI change. Omitting the dual from an unchanged retry
stays worth doing and is additive, but it is the second-order one: it removes
repetition of a cost that should not be large in the first place.

Neither is implemented.

### THE MENTAL MODEL I WAS MISSING — M8 IS ADDRESSED, NOT TRAVERSED

Two more corrections from the user, and together they change the shape of the
dual fix rather than its size. I had written "drive the descent from the sparse
index". There is no descent to drive.

ADDRESSING, NOT WALKING. Two hashmaps:
  block     MerkabaPcg3d(blockCoord) -> two buckets of four slots, 2-choice
            cuckoo, at most eight probes, O(1). M8FindBlock is exactly that.
  residency 32768 HOT physical tiles in four banks, the same PCG3D 2-choice
            cuckoo, COLD on SSD.
And the frozen address is signed int3 -> block 256^3 / five octant digits /
tile 8^3 / 512 kernels, so even the levels between are pure INDEXING:
_M8BlockChunkRefs is 512 entries per block and _M8ChunkTileRefs is 64 per chunk,
reached as chunk*64+tile. Nothing about this structure is searched or walked.

THE TILE IS BUILT MORE CLEVERLY THAN IT IS USED. _M8TileBits is uint4 per word
with M8TileWordIndex(physicalSlot, word) over words 0..15, so a tile carries
16 x 32 = 512 bits per channel - one bit per kernel - across FOUR channels.
FlowerCommit reads it as _M8TileBits[M8TileWordIndex(slot,lane)].z with one lane
per word, so sixteen lanes cover an entire tile in one read each. Whether a tile
is empty, uniform or mixed, and which kernels are set, is therefore bit
arithmetic on sixteen words, addressed in O(1) from the physical slot. No
per-kernel visit, no geometric descent.

THE WORK SET IS ALREADY ENUMERATED. CountObservationBins and EmitObservationBins
produce _M8ObservationRecords and _M8ObservationTileBins in 0.043 + 0.048 ms.
That is the addressed set of owners the observation actually touched, binned per
tile. The dual re-derives from geometry what those passes already published for
almost nothing.

SO THE DUAL'S SHAPE IS WRONG TWICE OVER, not slow. It enumerates up to 512 chunk
slots per block and 64 tile slots per chunk from M8ScanChildMask, a geometric
intersection test, and only asks at the leaf whether anything exists. Against a
structure that is addressed in O(1) and a tile that answers uniformity from a
512-bit mask, that is the wrong operation, and 31.5 ms per 6.4 m block is what
the wrong operation costs. It is an auxiliary guard against artefacts, so it must
be cheap by construction, not merely optimised.

WHAT I HAD PROPOSED AND WHY IT WAS STILL WRONG. "Descend only into existing
children" keeps the traversal and merely prunes it. The correct shape starts from
the addressed work - the observation's binned tiles - resolves each by hash,
answers uniformity from the tile masks, and uses the coarse depth certificate
only to certify whole volumes that need no per-tile answer at all. Skipping the
dual on an unchanged retry remains additive and is now third in order.

Not implemented. Recorded so the next cut starts from the right operation.

### MEASURED: OPTIMISING THE PER-KERNEL LOOP IS THE WRONG TRADE

REV-C section 2 is the authority on the dual and settles several things I had
been inferring:

  00 ALL_FULL  01 ALL_THROUGH  10 MIXED  11 INVALID
  "A missing block means ALL_FULL: the excavation model starts as matter, not as
   empty space."
  DualLeaf { uint ThroughBits[16]; }  512 bits per mixed tile, 2 MiB for 32768.

So the world begins as MATTER and the scan excavates see-through space; a missing
block is solid, not unknown. And the per-kernel 512-bit leaf is SPECIFIED, not a
misuse - the NODE hierarchy ends at the tile, which is what "the smallest dual is
20 cm" means. The chunk carries NonFullMask and MixedMask and the leaf is reached
by a popcount prefix, so leaf selection is addressing too. Closure 5.2's 0/1/2 is
the QUERY; REV-C's four states are the STORED state. I had conflated them.

I IMPLEMENTED THE IN-SPEC SPEEDUP AND THEN REVERTED IT ON MEASUREMENT.
The certificate descent stops at the tile and then tests all 512 kernels one at a
time. M8SupportThrough encloses the union of a span's owner supports, so
certifying spans 4 and 2 first and marking a certified span whole is exact. It
passed 368/368, including
FrozenDepthObservation_NonzeroR2IsBitIdenticalAcrossQuantaAndBackpressure, which
drives a real frozen observation through the whole GPU chain and requires
bit-identical canonical records. So exactness was proven, not asserted.

It still went back, because of what it costs:
  UpdateObservationDual  494 360 -> 694 876 B (+40%), body 24 925 -> 34 924
M8SupportThrough is inlined three times instead of once. That entry carries FOUR
64-entry dynamically indexed function arrays, and today's device evidence is that
creation breaks on the COMBINATION of module size and that pressure -
ResolveFlowerCarriers failed at 715 124 B. Taking this one to 694 876 B risks the
whole scanner failing to initialise, and the reward is about 2.5x on an operation
that REV-C says should not be expensive at all. 3932 ms -> ~1570 ms does not make
the scan work either, because the observation still needs ~135 attempts.

WHY THE CERTIFICATE CANNOT DO BETTER AS BUILT. The node is uint2 holding the
MINIMUM depth and a validity flag - M8DepthCertificateNode(min(min(a.x,b.x),
min(c.x,d.x)), ...). It can prove THROUGH and nothing else, which is exactly
REV-B's closure line "certifies THROUGH over the full projected support". ALL_FULL
- the common case behind a surface - is therefore only reachable by excluding all
512 kernels. A cheap ALL_FULL needs a maximum-depth channel alongside the
minimum, which is a change to the certificate codegen, and that is where the real
remaining cost is.

WHAT I WILL NOT GUESS AT. The 135x factor is the retry re-running the dual, and
skipping it is NOT merely dropping a dispatch: the dual is a generation-keyed
transaction. M8DualBeginBlock takes _M8DualPublishingGeneration, every write
carries publishing and retired generations, and each attempt is issued a NEW
generation. Skipping the pass leaves the dual state on an older generation, and I
have not established what validates against that. I have been corrected four
times on this subsystem today and twice proposed the wrong operation, so this
stops here rather than shipping a fifth guess into the carver.

Suite 368/368 with the change; reverted afterwards, so the tree is unchanged.

### THE SPAN SHORTCUT, WITH ONE READER CALL SITE

Reinstated after the revert, now costing almost nothing. M8SupportThrough
encloses the union support [first-1, first+n], and a kernel's own cube
[owner-1, owner+1] lies inside its span's cube; the projected rect of a
contained box is contained and its upper depth bound is no larger, and
M8DepthCertificateThrough requires upper < minDepth over the rect, so a
certified span proves every kernel inside it. Parent true implies child true;
parent FALSE implies nothing, which is why an uncertified span still descends.

The span travels as a VARIABLE through one loop over levels 4, 2 and 1, so the
reader is inlined once:
  three constant-span calls   494 360 -> 694 876 B  (+40.6%)
  one variable-span call site 494 360 -> 496 460 B  (+0.4%), body +111
That matters because this entry carries four 64-entry dynamically indexed
function arrays and today's device evidence is that creation breaks on size
combined with that pressure; ResolveFlowerCarriers failed at 715 124 B.

Suite 368/368, including
FrozenDepthObservation_NonzeroR2IsBitIdenticalAcrossQuantaAndBackpressure, which
drives a real frozen observation through the whole GPU chain and requires
bit-identical canonical records. Exactness is proven, not asserted.

### CORRECTION — "ALL_FULL IS NEVER RECORDED" WAS WRONG

I claimed the cost never falls because FULL is unprovable and therefore never
stored. M8DualWriteTileWords disproves it:
  uint state = allZero == 0u ? M8_DUAL_FULL :
      allOne == 0xffffffffu ? M8_DUAL_THROUGH : M8_DUAL_MIXED;
An all-zero result records M8_DUAL_FULL, and M8DualReadPreparedTileWords hands
back zeros for it, so the tile is examined again next observation. That is
CORRECT, not a defect: matter may be excavated later, and REV-B says FULL is only
"the conservative complement of proven free space". The repetition is the model
working, so that idea is withdrawn.

### WHY THE RELIC ERASE IS REAL AND WHY IT NEVER RUNS

Verified end to end. MerkabaFlowerCommit.hlsl M8FlowerApplyDualVeto:
  if (M8DualReadKernelAt(block,child,tile,local,true,true) != M8_DUAL_THROUGH) return;
  uint4 next = M8FlowerContradictR1(...);
and M8FlowerContradictR1 subtracts M8_FLOWER_THROUGH_EVIDENCE (256) and zeroes
the kernel once evidence falls to OCCUPIED_OFF, with its own comment about not
keeping "a contradicted ghost". Two counters record it,
M8_COUNTER_THROUGH_EVIDENCE_DECREMENTS and M8_COUNTER_THROUGH_OCCUPIED_TO_FREE.
So certified see-through space does erase a person's wake and a moved object's
relic, exactly as described, and it needs the per-kernel leaf because
M8DualReadKernelAt asks per kernel.

It never runs on the device because FlowerCommit is gated on
M8_COUNTER_TOUCHED_TILE_COUNT and the observation never retires; FlowerCommit
measures 0.003 ms.

### NEXT SUSPECT, NOT YET CONFIRMED

M8DualReadPreparedTileWords returns M8_DUAL_AMBIGUOUS when the chunk payload is
not canonical or when a MIXED leaf is not resident (M8DualLeafResident), and the
caller then requests storage and marks the observation pending. If MIXED leaves
cannot stay resident, every mixed tile is AMBIGUOUS, the observation can never
retire, and the device picture follows exactly: pendingTiles=1, unresolved=0,
stage=1, attempt 53 and climbing, residency epoch static at 24. Counters exist -
M8_COUNTER_DUAL_STORAGE_INTENT_COUNT 51, M8_COUNTER_DUAL_TOUCH_PUBLICATION 52 -
so this is measurable rather than arguable. Not yet measured.

### THE DUAL BELONGS TO THE OBSERVATION TOKEN, NOT THE ATTEMPT TOKEN

The generator built the retry schedule as everything from ResetObservationBins
onward, so every refinement quantum replayed the whole acquisition side -
stereo, the depth certificate, the bins, and UpdateObservationDual. On device
that was 3932 ms of a 3933 ms attempt, 8 to 9 dual dispatches and 26 to 31 s of
GPU per observation, while refinement advanced quantumProgress=1 and
attemptResidencyEpoch stayed equal to currentResidencyEpoch. Closure 4.3 states
the rule: "Drain dostane jen zbylou praci."

A new schedule and job kind, ObservationContinue, carries exactly eight
dispatches:
  DrainFlowerGeometry, ResolveFlowerCarriers, DrainFlowerSkinRgb,
  DrainFlowerSkinV, ResolveMissingSpatialNodes, ResolveObservationTileRequests,
  InitializeNewTiles, FinalizeObservation
No stereo, no certificate, no count/emit, no dual, and no FlowerCommit - that one
belongs to the immutable observation. The allocation barriers stay because the
drain can still request tiles. Executor ABI 18 -> 19, and the kind is appended
last so existing kinds keep their index.

The decision needed no new state. CanRetryPreparedObservation already knew WHY it
was resuming and threw the reason away into one bool; it is now split:
  _resumeNeedsAcquisition = ResidencyEpoch moved || loadCursor moved ||
                            durableGeneration moved
Those three are the only reasons the acquisition side must run again. Refinement
progress alone takes the continuation, which is exactly REV-C's rule that FULL is
re-examined by a NEW observation and never because the same token entered another
quantum.

TWO THINGS THAT WOULD HAVE BROKEN IT SILENTLY. The native side validated
`kind > kJobFineErase` in two places and would have rejected the new kind. And
the dual lease is CPU-side: CompleteNativeDualMutation runs after the fence, not
inside the dual kernel, and M8DualChunkCanonical does NOT compare the payload
generation against the current publishing generation - it only range-checks. So
omitting the dual leaves no open transaction and no stale-invalid payload. That
was the thing I earlier refused to guess at.

### AMBIGUOUS WAS THREE DIFFERENT THINGS AND ONE OF THEM COULD NOT MAKE PROGRESS

M8DualLeafResident fails for three unrelated reasons: the ref is COLD, or the
slot is HOT but its generation, meta or back reference no longer describes this
tile, or the chunk payload is not canonical. All three collapsed into
M8_DUAL_AMBIGUOUS, and the caller then requested storage ONLY when the tile
reference was COLD_ON_SSD before pending the observation. So a HOT tile with a
stale leaf ref requested nothing and pended forever - a real liveness hole.

Now separated:
  M8_DUAL_READ_COLD    residency dependency: request the load, pend, and the
                       retry arrives with a moved residency epoch
  M8_DUAL_READ_STALE   the leaf ref no longer describes this tile, so the words
                       are zero, the volume is recomputed and the write rebinds
                       it - repair, never a wait
  M8_DUAL_AMBIGUOUS    the payload itself is not canonical, which BeginChunk
                       should have prevented, so it fails the transaction
                       instead of waiting for evidence that cannot arrive
REV-C's "AMBIGUOUS child is not pending compute; it needs new evidence" is why
these cannot share one scheduler state.

### A VERIFICATION HOLE OF MY OWN, WORTH MORE THAN EITHER CUT

I reported "SUITE 369/369" for code that does not compile. Two separate traps:
  - run_merkaba_tests.sh only does `test -s` on the results XML, so when Unity
    writes no new file the OLD results parse as Passed;
  - and the EditMode run can pass on a CACHED assembly, while the player build
    compiles fresh and caught error CS0103 immediately (a `kind` declared inside
    one try block and logged from another).
Worse, my own chain piped the build through `tail`, so the pipeline's exit code
was tail's and a FAILED build still ran the deploy, which reported
"DEPLOYED ... Success" while installing the 23:50:57 APK. Three cuts appeared to
be on the device and were not.
The chain now checks that the results XML mtime advanced, does not mask the
build's exit code, and refuses to deploy unless the APK mtime advanced. The suite
is not a compile gate; the player build is.

Suite 369/369 on a verified-fresh XML. Shader audit: 70 kernels, 1 FAIL, and that
one is CompactDirtyFlowerSymbols over the 1 MiB gate, which is OPEN-1 and
deferred by explicit instruction. UpdateObservationDual 497 068 B / body 25 063
after both cuts, against 494 360 / 24 925 before the span shortcut.
APK 00:20:57 deployed through both freshness gates.
DEVICE ACCEPTANCE PENDING: kind=ObservationContinue in the submit log,
dispatches=8 on those jobs, whether attempt stops climbing, whether
"observation complete" appears, and M8_COUNTER_THROUGH_EVIDENCE_DECREMENTS as
proof the relic erase actually runs.

Contracts untouched; all of this is ledger only.

### THE CONTRACTS CARRIED THE WRONG INVARIANT, AND THAT IS WHY THE CODE DOES

Corrected on explicit instruction, because no amount of reimplementation helps
while two normative documents mandate the wrong model.

REV-C section 18 said DrainObservationRefinement must "consume remaining work
from the SAME immutable observation" and that "The immutable observation may be
released only after all work that it can make CERTAIN has either committed or
been proven unnecessary". Closure section 3 said "immutable observation prezije
veskery pending compute", and closure 4.3 froze the bounded continuation
workset with eager-equivalence per quantum. Together they turn a realtime
scanner into a grinding buffer, and the implementation followed them faithfully.

Replaced by the snapshot transaction model:
  One snapshot is one bounded synchronous scan transaction. It is never retained
  to exhaust derived refinement work. It processes all directly addressed
  resident evidence and all finite tile-local consequences reachable in that
  transaction, commits them to canonical world state, marks derived readout
  pages dirty, and retires. COLD dependencies are requested and skipped;
  AMBIGUOUS evidence is skipped; neither retains the snapshot. Further geometry,
  excavation and skin refinement is driven by LATER observations against the
  persistent world. No per-observation cursor, pending tile, refinement quantum
  or continuation workset survives FinalizeObservation.
  The persistent M8 / dual / FlowerDetail / ThreadAtlas world IS the refinement
  memory. A camera observation is evidence, not a work queue.

root -> L1 -> L2 -> skin stay real dependency barriers, but all four belong to
ONE snapshot command graph rather than four further attempts at the same frame.
Determinism gets stronger, not weaker: with no slicing there is no ordering or
quantum left to change a result. Transient scratch, lists and receipts may exist
inside one GPU submit and must be gone after FinalizeObservation.

The excavation view binds to the same rule, which settles a question I got wrong
twice today: what a snapshot certified THROUGH it stores, what it did not
certify stays FULL, and FULL is re-examined by a NEW observation with a new
camera - never by re-running the same frozen certificate 135 times.

Measured consequence of the old model, for the record: observation=1 held from
00:34, stage=1 and pendingTiles=1 unchanged across 50 s of sampling while the
attempt counter passed 200, residencyEpoch static at 6, compiledBatchActiveTriangles=0.
The device was re-asking the same question about the same tile on a frame a
minute old while ~3000 newer depth frames went unlooked at. My own contribution
to that was the 64-tile receipt region: M8_FLOWER_SKIN_RECEIPT_TILES bounds a
per-(tile, owner) table at 512 x 192 B per tile, so touched tiles beyond queue
index 64 incremented REFINEMENT_PENDING_TILES and returned, every attempt,
forever. Indexed by group.x, which never changes between attempts, that is not a
bounded workset - it is permanent exclusion. It existed only to carry state
across the resolve/RGB/V split, which itself exists only because of an Adreno
module-size limit. Under the new model there is nothing to carry.

### 2026-09-10 LIVE_SCAN_RECOVERY — realtime admission, native evidence, wake

BASE=cf39413 + preserved Claude dual/codegen/metrics/UI changes.
SCOPE=restore actual four-stream realtime admission; preserve Flower algebra,
shared addresses, native single queue, scan/wake lifecycle and measured metrics.
USER_CLARIFICATION=external certified sensor errors were a mistaken interpretation.
The scanner uses SDK camera/depth calibration and realtime consistency, not a
laboratory profile. REV-C §5.1/§15.1/§19 and the immutable prefix agree.
PURSUIT=product goal remains the obsolete paused REV-B goal; this is the cursor.

DAG:
  RECOVERY_1 remove profile/build/start gates; freeze digital representation
    bounds + separate THROUGH clearance. Stereo retains an original depth
    supported by both depth and RGB eyes when correction remains ambiguous.
    Generated Flower roots replace the old census. IMPLEMENTED; GPU tests PASS.
  RECOVERY_2 native observation/attempt/depth-bound metrics. IMPLEMENTED.
    metricPriorAccepted and metricCorrectionAccepted distinguish the two cases.
  RECOVERY_3 anchor wake recovery and explicit new-live-scan restart; actual
    authority changes alone mark dirty. IMPLEMENTED in cf39413, preserved.
  RECOVERY_4 [1,2,3] full Unity suite -> fixes -> bounded Unity Quest APK build
    -> device measurement. TESTS + APK PASS; device measurement pending.

CURSOR=RECOVERY_4 fresh APK ready for device acceptance. Native StereoFlowerRefine
  compile PASS, metric gate REVIEW (no FAIL): 479132 B / 24544 body instructions /
  240 B groupshared / 8x8 / 3 writable. Unity DXC + generated-table parity PASS.
  Positive textureless-wall test now requires a real accepted endpoint with
  unmodified measured depth; missing opposite depth and disjoint RGB reject.
PREVIOUS_RECEIPT=380/380 in cf39413 included the wrong profile gate and an
  intentionally empty textureless fixture; it did NOT prove live admission.
TESTS=375/375 full Unity EditMode PASS, 2026-09-10 12:01:19Z–12:02:02Z.
  Removed 8 obsolete external-profile test cases, added 3 observation-policy
  tests, corrected the existing positive/negative GPU stereo fixtures.
  /mnt/kingston-unity/Builds/TestResults/merkaba-results.xml
BUILD=Tools/unity/build_merkaba_apk.sh PASS, 2026-09-10 12:07Z.
  Native 27 pipelines + Unity Android IL2CPP arm64 + release packaging.
  APK=/mnt/kingston-unity/Builds/QuestMerkabaScan/QuestMerkabaScan-release.apk
  APK_BYTES=76383070
  APK_SHA256=bf42fea6724f792d6f6a36730864af6e163b3b1ebada987c9b8ed283b4e03934
  MemoryHigh=12G, MemoryMax=16G, MemorySwapMax=2G, CPU affinity 0–1.
  Full 71-entry audit/device performance were not rerun or claimed PASS.
CHECKPOINT=includes preserved pre-existing Claude cooperative dual/codegen,
  residency metrics and UI build stamp, as tested and packaged in this APK.
  Untracked agent/session files are not included or deleted.
DEVICE_ACCEPTANCE=pending.
NEXT=install this APK and measure
  accepted endpoints -> requested/HOT/touched tiles -> occupancy -> draw,
  and verify pause/wake/explicit Start. No calibration asset is requested.

### 2026-09-10 LIVE_SCAN_RECOVERY — endpoint bins and mobile execution

BASE=153918d. Contracts/ontology unchanged. SimpleScan is reference only.
MEASURED_BASELINE=installed bf42fea6 APK: accepted stereo endpoints exist,
  but unresolved neighbours veto all HOT emission. StereoFlowerRefine takes
  2902–5817 ms; touched/occupied/triangles remain zero despite HOT tiles.
DAG:
  LIVE_1 local missing-owner admission; Count publishes its touched set before
    allocation/recount; claims publish owner records at the storage barrier.
    IMPLEMENTED, Count=8 writable bindings; no extra dispatch or persistent field.
  LIVE_2 shared native divide/sqrt hints with exact integer corrections;
    reuse five R1 results, immutable radius LUT and per-WG observed loop axes.
    IMPLEMENTED; four sensor streams and both depth/RGB support remain required.
  LIVE_3 PC SPIR-V redundancy/ID cleanup, exact embedded-payload audit;
    driver/UUID-valid pipeline cache survives unrelated shader changes.
    Parallel elementary DIRT faces replace the serial 32-face word evaluator.
    IMPLEMENTED, but Compact remains OPEN-1 (not a green closure claim).
  LIVE_4 full tests, APK, install/start, measure endpoint->occupancy->draw and
    actual stereo GPU time. TESTS + APK + INSTALL PASS; DEVICE MEASUREMENT PENDING.
TESTS=378/378 full Unity EditMode PASS, 2026-09-10 16:54:17Z–16:55:29Z.
  Includes missing-owner HOT emission, allocation Count/Reset without Reserve,
  post-Count install exclusion, exponent-wide division and exact sqrt parity.
AUDIT=72 entries compiled/validated, 71/72 engineering gates pass.
  ONLY FAIL: CompactDirtyFlowerSymbols=1143316 B / 60686 body / 30024 B GS.
  StereoFlowerRefine=423780 B / 21292 body / 864 B GS / 8x8 / 3 writable.
  Receipt=/mnt/kingston-unity/Builds/QuestMerkabaScan/recovery-full-audit.log
BUILD=Tools/unity/build_merkaba_apk.sh PASS, 2026-09-10 17:05Z.
  Native 27 pipelines + Unity Android IL2CPP arm64 + release packaging.
  APK=/mnt/kingston-unity/Builds/QuestMerkabaScan/QuestMerkabaScan-release.apk
  APK_BYTES=75506362
  APK_SHA256=9eb0ab065e4fda3869f6c85adb94da069d198c58f92cd0f6c8bfd9f251779c62
INSTALL=adb install -r SUCCESS, Quest 3S 340YC20G7X0QZ4; installed base.apk SHA256
  matches the build above. Activity launch PASS, process 9945; headset asleep.
  Device log=/mnt/kingston-unity/Builds/QuestMerkabaScan/evidence/recovery-live.log
DEVICE_FAILURE=9eb0ab06 APK startup failed at StereoFlowerRefine with VkResult=-13.
  Same exact module fails in an isolated Adreno pipeline-link probe without a
  cache; previous shader succeeds. Removing ONLY spirv-opt redundancy-elimination
  restores linking. Arithmetic, workgroups, admission and geometry are unchanged.
  Driver=Adreno 740 0x80345009; failure is not attributed to pipeline cache.
STEREO_FIX=remove redundancy-elimination globally. Intermediate stereo module:
  449720 B / 22617 body / 864 B GS, SHA256
  d47c614637e0ed238d31360481eec391018631673b8769411acabae7d6c935d0.
  Isolated device comparison: old module=-13 / 2300.908 ms;
  fixed module=VK_SUCCESS / 2934.392 ms. These are LINK times, not dispatch times.
  Receipt=/mnt/kingston-unity/Builds/QuestMerkabaScan/evidence/vk13-link-comparison.log
BUILD_UPDATE=full Unity Quest APK PASS, 2026-09-10 17:35Z; 75616334 bytes.
  APK_SHA256=96512f717e9cb6b36fead792fbfaf5f40a276991ebb9a6974ed6178133dcba21
  Native 27-pipeline validation/reflection/embedding PASS. Full EditMode suite
  was not rerun: only the native post-link pass changed; prior 378/378 receipt
  does not replace actual Adreno pipeline creation.
INTERMEDIATE_DEVICE=96512f71 APK installed/hash-verified and launched. Stereo
  linked in-app (3375.808 ms, result=0), as did the following 19 pipelines.
  CompactDirtyFlowerSymbols then failed -13 after 58837.775 ms.
COMPACT_ISOLATION=the same current shader without merge-blocks links on-device,
  cache=NONE, result=0, 73088.415 ms. Dead-code removal leaves that module's bytes
  identical, isolating merge-blocks as the additional failing pass.
  Raw+ID-compacted module=1226716 B / 64985 body / 30024 B GS, SHA256
  8f8b2b17275d7d3093ad067234b93d32c8616b043400fe0e6a48b18da456dd8d.
  Receipt=/mnt/kingston-unity/Builds/QuestMerkabaScan/evidence/vk13-compact-raw-link.log
FINAL_FIX=postprocess only --preserve-bindings --compact-ids. Preserve glslang's
  instruction/control-flow graph; no additional CSE or CFG transformations.
  No changes to shader arithmetic, workgroups, scan admission or geometry.
FINAL_BUILD=full native + Unity Quest APK PASS, 2026-09-10 17:48Z; 75619618 bytes.
  APK_SHA256=6d00af922a4211caa966d89e353beab89591143ebccacf55712035aa1edc613a
  All 27 exact embedded modules compiled/validated/reflected with ID-only cleanup.
FINAL_LINK=27/27 exact final modules link on Adreno 740 0x80345009, cache=NONE.
  First batch returned Stereo/Depth/UpdateDual/FlowerCommit success; its channel
  ended during DrainFlowerGeometry without a result, so Drain was rerun alone:
  VK_SUCCESS, 26645.526 ms. Remaining 22 modules: 22 success / 0 failures,
  including CompactDirtyFlowerSymbols=VK_SUCCESS / 73592.169 ms.
  Receipts=evidence/vk13-all-final-link.log + vk13-final-rest-link.log;
  isolated Drain result is recorded above; these are LINK, not dispatch times.
FINAL_INSTALL=adb install -r SUCCESS; installed base.apk SHA256 equals
  6d00af922a4211caa966d89e353beab89591143ebccacf55712035aa1edc613a.
  Activity launch PASS; PID=15252. Live log=evidence/vk13-final-live.log.
FINAL_APP_LINK=27/27 in-app pipelines VK_SUCCESS, 2026-09-10 18:00:56Z.
  StereoFlowerRefine=3290.838 ms; CompactDirtyFlowerSymbols=81646.855 ms.
  Native cache saved durably, 9869935 bytes. Both reported native -13 failures
  are fixed in the installed APK, not merely in the standalone probe.
  MRUK's separate PcaGpuProviderVulkan pipeline logs a link failure during
  startup; this is not one of the 27 native executor pipelines and is not
  counted as fixed here. Scan/render acceptance still requires live measurement.
CURSOR=LIVE_4, all native pipelines initialized; first scan observations starting.
  No scan-success claim; OPEN-1
  (compact shader size) remains separate from this link-failure repair.
  PC builds SPIR-V; the actual Adreno driver still compiles device machine code.
  Never describe a Vulkan pipeline cache as a portable PC-compiled Quest binary.

### 2026-09-10 SIMPLESCAN_SENSOR_RESTORE — explicit user sensor amendment

BASE=897c176. USER_DIRECTIVE=stereo depth projection + stereo RGB refinement
  must be the same realtime sensor operation as SimpleScan. L3-L5 are signal
  detail on L2, not another geometric solve in sensor preprocessing. This
  supersedes the earlier sensor-stage Flower-root/census-replacement rule,
  not M8/Sphere-Flower geometry, incidence, dual, fine state or draw ontology.
MEASURED=installed 6d00af92 APK links all 27 pipelines, but live stereo costs
  1494.943 of 1497.249 ms in observation 9. HOT tiles grow to 41; occupancy and
  drawn triangles remain zero. Do not infer a readback-only cause from this.
IMPLEMENTED=the SimpleScan four-stream sensor algorithm replaces the per-pixel
  interval/shell solve. Same SDK projections/crop, five distance hypotheses,
  bounded stereo plane/RGB census tests, original-depth fallback/confidence.
  Existing Flower consumers still require normal.w==1; low-confidence sensor
  bootstrap is not relabelled as strict Flower evidence. No legacy surface,
  carve, mesh or draw path is restored; no runtime algorithm switch exists.
  Camera projection/sampling is shared with the actual Flower RGB consumer.
  Sensor no longer needs Flower tables or world-grid matrices; metrics return
  to 8 values per radial bin, with matching producer/consumer labels.
PC_SENSOR=44208 B validated/reflected native SPIR-V, versus 451048 B installed.
  Receipt=/mnt/kingston-unity/Builds/QuestMerkabaScan/stereo-restore-module.log
READBACK_FINDING=SimpleScan also has storage/counter/attempt readbacks. Uniscan
  additionally samples native stereo metrics and flushes dual state. Removing
  readbacks has NOT been implemented or claimed; storage transport is distinct
  from geometry authority. No readback is added by this sensor replacement.
READOUT_MEASURED=installed APK CompactDirtyFlowerSymbols count+emit costs
  36487.371 ms at revision 10091 and 54609.059 ms at revision 10092 while
  occupancy remains zero. Receipt=evidence/scan-stutter-live.log. The growing
  GPU cost is not explained by asynchronous CPU telemetry readback.
READOUT_FIX=256 lanes select two owners each into a 16-word page-local bitmap.
  Only strict occupied owners enter cooperative evidence packets, in the same
  canonical order. Coverage selects the same bounded halo domain in 512-item
  batches and preserves unresolved receipts. No empty-owner packet barriers,
  no extra persistent topology, no change to Flower geometry/skin predicates.
PC_COMPACT=1239200 B / 65650 body / 30088 B GS / 7 writable bindings, validated
  and reflected using the exact native ID-only cleanup. OPEN-1 shader-size gate
  remains open; fewer empty-owner barriers is a runtime repair, not a size PASS.
  SHA256=9078315bdae421155f2586c05f431963f4c7e88ec6fb82d9a5ab23ab3cfda94d
  Receipt=/mnt/kingston-unity/Builds/QuestMerkabaScan/readout-select-module.log
TESTS=full EditMode 378/378 PASS after both production edits. The sole initial
  failure was the FINE source assertion for the replaced sensor output name;
  it now checks masking before selectedDepth publication. Behavioral FINE,
  flat-wall/shared-knot and textureless/conflicting stereo fixtures remain.
APK=full native + Unity Android/IL2CPP build PASS, 2026-09-10 18:30Z;
  75379862 bytes, SHA256
  03280202fc2270f38b1936f6b3fe6fdf67ce3ae82b73d6f9b775e91494940251.
  Receipt=/mnt/kingston-unity/Builds/QuestMerkabaScan/stereo-restore-apk.log
INSTALL=adb install -r SUCCESS on 340YC20G7X0QZ4; installed base.apk SHA256
  verified equal to the build. Activity launch status=ok, PID=17385 at 18:31Z.
  No app data or pipeline cache was cleared. USB/ADB disconnected immediately
  afterwards (adb devices empty), before new pipeline/runtime timings arrived.
CURSOR=SENSOR_3; installed and launched, DEVICE ACCEPTANCE PENDING. Reconnect
  the headset and measure stereo/Compact dispatch times, occupied kernels and
  emitted triangles. Neither runtime speedup nor a visible scan is yet proven
  for this APK. Do not confuse the 44 kB stereo module with measured GPU time.

### 2026-09-10 REALTIME_REWRITE — new user-authorized execution authority

BASE=9ab1f687691f91fbefb62e46cc308f8fb5327a8a
AUTHORITY=M8-REALTIME-MEASUREMENT-DRIVEN-CONTRACT.md
CURSOR=M8-REALTIME-DAG.md, RT1 OPEN. Historical RUN/CUT cursors are superseded.
GOAL=old REV-B goal no longer exists; the replacement 'stop' instruction was
  completed, then the new measurement-driven full rewrite goal was created.
SCOPE=replace persistent cursor/1336-task/quantum/receipt/exhaustive production
  decode model, preserving the specified M8/Flower geometry and fixed skin.
  Shared generated decode serves observation-reached integration, readout and
  frozen export; exhaustive enumeration is test-only. No local performance
  patch is presented as completion of this rewrite.
USER_DELIVERY_GATE=APK build, installation and Quest acceptance ONLY after the
  ENTIRE rewrite, in RT4. RT1–RT3: implementation, necessary compile/codegen
  checks, coherent intermediate commits. Baseline 378/378 is not RT acceptance.

### 2026-09-10 REALTIME_RT1 — generated R1 witness incidence checkpoint

CURSOR=RT1 OPEN; R1 admission replacement implemented, carrier decode next.
CODEGEN=54 unique exact child R1 loops + six reached-direction masks + peer
  incidence. The 48*4*3 enumeration runs only in the editor generator.
PRODUCTION=M8FlowerR1FlagWitness consumes reached loop bits and peer masks;
  no independent RootOwner mask intersection, changed metric predicate or
  persisted branch ordinal. CPU counterpart shares the frozen tables.
COMPILE=Unity GenerateForBatch PASS; native FlowerCommit ID-only SPIR-V compile
  PASS: 780768 B, 41358 body instructions, 24704 B shared, 8 writable bindings.
  /mnt/kingston-unity/Builds/QuestMerkabaScan/realtime-rt1-codegen.log
  /mnt/kingston-unity/Builds/QuestMerkabaScan/realtime-rt1-r1-compile.log
TESTS=not run; APK=not built; DEVICE=not run, per final-only delivery gate.
REMAINING=actual-carrier DecodeFlower, RT2 cursor/task/receipt removal, RT3
  readout/export rewire and V normal atlas, then integrated RT4 acceptance.

### 2026-09-10 REALTIME_RT1 — shared source reach and inverse sign closure

CURSOR=RT1 OPEN, necessary-source selection implemented; actual child/branch
  decode and replacement of the original/site metric packet remain next.
PRODUCTION=one generated M8FlowerReachedCarriers CPU/HLSL integer recipe.
  Proved absence of both signs excludes that node's incident constructions;
  unknown roots never exclude. Inverse petal/carrier tables replace blind
  carrier ranges in GPU direct/final batches and CPU export/halo coverage.
  Excluded children are still failures of whole-parent direct coverage; they
  cannot fabricate COMPLETED donors. Partial GPU batches clear unused slots.
  CPU seven-site sign closure now consumes the same generated 6*8 inverse
  masks as HLSL instead of evaluating every 128-bit assignment individually.
COMPILE=Unity codegen/current C# consumers PASS, receipt
  /mnt/kingston-unity/Builds/QuestMerkabaScan/realtime-rt1-reached-compile.log.
  Exact native Compact compile/spirv-val PASS, /tmp/m8-rt1-decode.3h1s13vy:
  1242872 B, 65864 body instructions, 30124 B shared, 7 writable bindings,
  SHA256=2655f0fad18f767b6dcf62a908a14901f74203b7ce4ebf5518e551e55a156247.
GATE=shader-size FAIL remains; source selection is not elimination of the
  remaining metric/coverage compiler. No runtime speedup is claimed.
TESTS=two independent finite-incidence/triple-inverse tests added, not run.
  Full suite/ABI/shader/GLB validation deferred to RT4 after complete rewire.
APK=not built; INSTALL=not run; DEVICE=not run. Active rewrite goal unchanged.

### 2026-09-10 REALTIME_RT1 — reached read reuse and GPU carrier fan-out

CURSOR=RT1 OPEN. This checkpoint is not completion of the decoder/rewrite.
CODEGEN=one frozen child-loop address/creation table for CPU/HLSL. Exact
  incidence derivation is editor/oracle-only; the player reads generated data.
CPU=offline frozen export/oracle only. Disposable FlowerDecode evaluates only
  requested original/child knots, reuses direct results and re-evaluates only
  completion-affected carriers. No CPU scan/live-readout fallback was added.
GPU=one wedge/sign predicate shared by consumers; readout maps actual batch
  source anchors and sign triples onto lanes. Metric site supports are reused
  from the disjoint owner-prefix scratch lifetime, not recomputed per triple.
  Endpoint receipts retain prefix dependency semantics; masked alternatives
  do not create unrelated COLD dependencies. No new persistent fields.
COMPILE=Unity GenerateForBatch/current C# PASS:
  /mnt/kingston-unity/Builds/QuestMerkabaScan/realtime-rt1-child-lookup-compile.log
  Exact native Compact/spirv-val PASS; artifact
  /tmp/m8-rt1-cooperative-final-5xye3nov/pipeline-0.spv
  1248772 B / 66194 body instructions / 31340 B shared / 7 RW / 256 lanes;
  SHA256=2b1d95d770b3831b272c4addb5135220989aa56e6f1e303f6bd7f77accfc5f39.
  ResolveFlowerCarriers/spirv-val PASS; artifact
  /tmp/m8-rt1-cooperative-5g0u8j2m/pipeline-1.spv
  707884 B / 36752 body / 20628 B shared / 6 RW / 128 lanes.
GATE=Compact size/instruction FAIL remains. Parallel distribution is not a
  measured speedup and does not remove the original/base acquisition packet.
NEXT=M8FlowerPacketAcquire reached L1/L2 dependency decode; then RT2 removes
  the persistent cursor/task/receipt model. RT3 export V/rewrite remains open.
TESTS=child lookup/oracle exhaustive test added, not run. Existing positive
  fixtures retained; only their decoder factory name changed.
APK=not built; INSTALL=not run; DEVICE=not run. Only RT4 may run those gates.

### 2026-09-10 REALTIME_RT1 — demanded GPU roots and present-phase ancestry

CURSOR=RT1 generated selection/transport implementation closed; RT2 OPEN.
  This is a compile checkpoint, not integrated/device acceptance. RT3 still
  owns replacement of the remaining presentation packet/count/emit structure.
PRODUCTION=generated carrier source-node masks drive progressive R1/R2/R3
  acquisition. Only reached roots/peers occupy lanes; carrier batches request
  inherited knots and present phase ancestry. CPU oracle/frozen export uses
  the same incidence and skips absent phase sources; never a live backend.
  Removed the separate pure-original array. Shared roots, complete pure bases,
  pending-invalidation raw outputs and endpoint receipts retain their meanings.
COMPILE=Unity codegen/C# PASS:
  /mnt/kingston-unity/Builds/QuestMerkabaScan/realtime-rt1-demanded-compile.log:676
  Exact native compile/spirv-val PASS:
  /tmp/m8-rt1-demanded-r35azpxu/pipeline-0.spv
  Compact: 1263272 B / 67046 body / 29464 B shared / 7 RW / 256 lanes;
  SHA256=d7d6f52149b2ad831063aa7ce0f28a3394df039002d0e12ce47bd31f899da205.
  /tmp/m8-rt1-demanded-r35azpxu/pipeline-1.spv
  ResolveFlowerCarriers: 708072 B / 36758 body / 20628 B shared / 6 RW;
  SHA256=5c25c4e189973a65447043498151f880b8c8c3d7d17705860e2815109963c93b.
GATE=Compact size/instructions still FAIL. Shared payload -1876 B versus
  previous checkpoint; no speedup/device claim. Source-demand selection does
  not substitute for RT2 execution replacement or RT3 presentation removal.
TESTS=source-node union and shell-reach/oracle comparison added, not run.
NEXT=RT2 observation-reached work replacing cursors/tasks/quantum/skin receipts
  with all shader/native consumers in the same cut. Do not restart RT1.
APK=not built; INSTALL=not run; DEVICE=not run. Active goal continues.

### 2026-09-10 REALTIME_RT2 — GPU snapshot-local signal handoff checkpoint

CURSOR=RT2 OPEN. Skin handoff replaced; geometry task/cursor replacement next.
PRODUCTION=removed fixed 64-tile/512-owner skin receipts from FlowerDetail,
  their RGB_OK gate, skin cursor/quantum branches and the old signal helper.
  Resolver visits measured owners and generated reached-carrier masks; only
  validated resolved carriers append observation-local SignalItems. The GPU
  owns count/owner manifests/indirect arguments. Owner manifests serialize that
  owner's sorted run writers; actual carrier links live only in this snapshot.
  RGB and V independently evaluate three fixed substitutions over their signal
  splits, with complete-support predicates unchanged. No CPU geometry, no
  scheduling readback, no world-coordinate persistent records were introduced.
ABI=21 / 44 resources. Separate 32 MiB scratch; 64 B header, 16 B owner manifest,
  224 B signal item. Source owner/generation/token/flags and epoch guard all
  reads. Overflow is explicit and does not publish a partial owner's list.
  Finalize's completed flag kills the handoff; next snapshot resets the header
  with bounded GPU transfer commands. No ninth writable binding in Finalize.
FIXED=old 192 B receipt wrote 208 B; world-site producer lacked its collective
  barrier; signal consumer attempted root validation from unrestored site
  scratch. Identity is now validated once by the resolver and transferred,
  not recomputed or trusted from another workgroup's uninitialized roots.
COMPILE=Unity codegen/C# resource consumers PASS:
  /mnt/kingston-unity/Builds/QuestMerkabaScan/realtime-rt2-signal-compile.log:706
  Targeted native shader compile/spirv-val PASS (not full native/APK build):
  /tmp/m8-rt2-signal64-x4jj35kz/pipeline-0.spv
  RGB 305536 B / 15696 body / 204 B shared / 4 RW / 64 lanes;
  SHA256=ef12e9a29fa5e7c7ddf99ecfae824996db01134c2fd21689e471845042a4d183.
  /tmp/m8-rt2-signal64-x4jj35kz/pipeline-1.spv
  V 500384 B / 25370 body / 204 B shared / 4 RW / 64 lanes;
  SHA256=a91491c3c9f80dbb311b931055a9a1fad6b233e57cc3724b0fb3b39ce9b8aae7.
  /tmp/m8-rt2-signal-final-sibepc7k/pipeline-0.spv
  Resolver 718424 B / 36967 body / 20692 B shared / 6 RW / 128 lanes;
  SHA256=23e1fa1e489f4b9c11e1399747d4561272dc3989012eabf70a8e60f866f0ae31.
  /tmp/m8-rt2-signal-final-sibepc7k/pipeline-1.spv
  Finalize 41744 B / 1938 body / 12 B shared / 8 RW / 128 lanes.
NEXT=replace GeometryNodeAt/1336 tasks/RefinementQuantum/TileRefinementCursor
  and deferred owner/peer invalidation in FlowerDrainBody/Refinement/Commit/
  Sidecar/native graph. Finish evidence-footprint child routing and remaining
  eager resolver root acquisition. These are OPEN, not hidden by the skin
  replacement. RT3 still removes the presentation compiler and adds export V.
TESTS=not run; APK=not built; INSTALL=not run; DEVICE=not run. No performance
  claim and no whole-rewrite acceptance. Integrated validation belongs to RT4.

## RT2 receipt — observation-reached phase addresses, 2026-09-10

BASE=fb98efa. RT2 remains OPEN; RT3/RT4 not started.
IMPLEMENTED=removed production GeometryNodeAt, PHASE_TASKS/1336 catalogue,
  RefinementQuantum and both TileRefinementCursor read/write helpers plus
  their commit/invalidation/managed consumers. No reconstruction cursor is
  written in the FlowerDetail tile directory. Its spare words are not state.
  Editor-only BuildObservedRelations derives 20 original, 24 L1 and 192 L2
  unsigned relation addresses from the existing Nodes/Strands/ChildLoop
  tables, retaining both signs and distinct L2 parent predictions. It also
  emits the inverse of the exact endpoint-support predicate over 64 L1 and
  512 L2 half-open cells, into the same generated RO blob and frozen hash.
  GPU source records mark cells once; separate lanes expand occupied cells,
  not every pixel's candidate list. Only reached relation bits enter metric
  classification/reduction. No CPU work-list builder, geometry backend,
  scan readback, sampled plane table or alternate surface authority added.
PRESERVED=prediction/observation residual vs peer intersection, quantized
  plane enclosures, endpoint orientation, RootOwner/incidence separation,
  canonical record keys and exact child transport. Cell addressing corrects
  the float division hint with the original ordered endpoint products; it
  neither clamps a measurement nor adds an epsilon.
COMPILE=Unity Editor codegen/C# PASS:
  /mnt/kingston-unity/Builds/QuestMerkabaScan/realtime-rt2-reached-relations.log:781
  Exact native glslang compile/spirv-val PASS (not APK/full audit):
  /tmp/m8-rt2-reached-relations-ayd5w1v3/pipeline-0.spv
  DrainFlowerGeometry 853000 B / 45212 body / 20704 B shared / 7 RW / 128 lanes;
  SHA256=9bf43e6f5a34efaec0f629b12bcdd322d5951de8820d0d73aa0878512988c971.
  pipeline-1.spv FlowerCommit 779156 B / 41266 body / 24704 B shared / 8 RW;
  SHA256=08748b2b1677549a2451753584a1ebc5441d2a9b6887bb053139aa0d206e95c1.
  pipeline-2.spv ResolveFlowerCarriers 718364 B / 36967 body / 20692 B shared / 6 RW;
  SHA256=297078b670d331c337e49c78152f34e9af556a37cc3c23b9dab05974b783042b.
NEXT=remove persistent owner/peer invalidation receipt/ACK and global
  INVALIDATION_OWED/AdvanceRefinementStage. Epoch must change with structural
  M8 mutation. Rewire every pending-invalidation reader, then fixed-level
  geometry work over actual owners, with local COLD/write outcomes. Current
  tile-wide reduction and failure gates are not final fan-out. Finish actual
  parent/footprint skin reach and resolver's eager original acquisition.
  RT3 presentation compiler/export V remain separate unfinished work.
TESTS=not run; APK=not built; INSTALL=not run; DEVICE=not run. No speedup or
  full-rewrite acceptance claim; full validation remains RT4.

## 2026-09-10T22:12Z — RT2 snapshot-local R1/epoch publication checkpoint

CURSOR=RT2 OPEN. RT1 implementation checkpoint retained; RT3/RT4 not started.
CHANGE=removed persistent owner invalidation receipts/ACK, INVALIDATION_OWED,
  stage/lease counters and AdvanceRefinementStage. R1 changes are prepared in
  observation scratch; structural owner epochs retain the existing exact
  invalidation/rebase rules. Unique receiver tiles pull changed-source bits
  and invalidate dependent peer phases before PublishFlowerR1 writes M8.
  Root/L1/L2 now have fixed GPU entrypoints with real dependency barriers.
  COLD receiver preflight skips only its source change. Unresolved surface
  counts no longer globally suppress R1 or the three geometry entrypoints.
  The 32 MiB SignalItems buffer holds the transient receiver queue and reuses
  prepared-R1 payload only after publication. No CPU solver, geometry readback,
  new persistent field or alternate geometry introduced. Native/managed ABI22,
  30 pipeline identities, 44 resources. Removed predecessor consumers directly.
COMPILE=Unity Editor codegen/C# completed successfully:
  /mnt/kingston-unity/Builds/QuestMerkabaScan/realtime-rt2-epoch-publish.log:752
  Native pipeline count assertions were then synchronized to 30/ABI22.
  Targeted exact native glslang/spirv-val only, not full audit or native/APK build:
  /tmp/m8-rt2-epoch-cut-d7h_ua82/pipeline-0.spv
  FlowerCommit 866380 B / 46261 body / 24704 B shared / 8 RW / 128 lanes;
  SHA256=a3da4d0bb08f10d1bbd468648858389737dd751c81ccbf4949c51f1e0add82ec.
  Root 529972 B / 27656 body / 20704 B shared / 5 RW;
  L1 730320 B / 38162 body / 20704 B shared / 5 RW;
  L2 730240 B / 38162 body / 20704 B shared / 5 RW.
  /tmp/m8-rt2-r1-transaction-final-4e7h_920 also compiled peer invalidation
  (55064 B / 2717 body / 120 B shared / 5 RW), R1 publication
  (10584 B / 293 body / 7 RW), resolver and Finalize. git diff --check PASS.
NEXT=actual measured-owner fan-out, local tile/owner COLD/write outcomes,
  footprint-directed skin evidence and resolver's eager original alternatives.
  ERASE pending invalidation and tile-wide dual-write guard remain to close.
  RT3 presentation packet/export V and RT4 integrated acceptance remain open.
TESTS=not run; APK=not built; INSTALL=not run; DEVICE=not run. No timing claim.

## RT2 — local dual/fine outcomes (after c4f358b)

CHANGE=removed the whole-tile dual-ready gate. Only the owner whose strict
  endpoint failed to restore FULL skips positive publication; unrelated R1
  owners and negative work continue. Geometry COLD/write status no longer
  breaks the remaining reached relations. Individual metric/epoch/capacity
  predicates still reject their own mutation; residency requests are emitted
  at the end of the finite stage. No deferred work survives the snapshot.
  Bin reset no longer retains a post-commit retry stamp: it is only acquisition
  or the same-snapshot sparse-allocation recount, before world mutation.
COMPILE=exact native glslang/spirv-val PASS in
  /tmp/m8-rt2-local-outcomes-40nr7jxx/pipeline-{0..5}.spv:
  FlowerCommit 865844 B / 46218 body / 24700 B shared / 8 RW;
  Root 528996 B / 27612 body / 20704 B shared / 5 RW;
  L1 729036 B / 38107 body / 20704 B shared / 5 RW;
  L2 728956 B / 38107 body / 20704 B shared / 5 RW;
  ResetObservationBins 6252 B / 164 body / 5 RW;
  ReduceDepthCertificate 7952 B / 287 body / 6 RW. git diff --check PASS.
NEXT=RT2 OPEN: actual-owner fan-out (no per-owner whole-bin rescan),
  footprint-directed skin and reached original-root acquisition, ERASE epoch
  path, conditional allocation and local malformed/capacity outcomes. RT3/4
  remain unfinished. CPU oracle/codegen is outside the GPU hotpath; no CPU
  geometry backend or new runtime readback was added.
TESTS/APK/INSTALL/DEVICE=not run, per RT4 delivery gate. No timing claim.

## RT2 — GPU measured-owner grouping (after 09ec72d)

CHANGE=PrepareFlowerOwners groups immutable record indices with a parallel
  tile histogram/exclusive prefix/scatter. Actual owner manifests and stamped
  mask/rank words replace repeated scans of whole tile/halo bins. Root/L1/L2
  and resolver dispatch one WG per measured owner, through GPU indirect args.
  Three 512-word owner banks, four-owner private arrays and owner batch loops
  were removed. Only that owner's reached cells/relations are evaluated.
  The fixed peer endpoint uses its grouped range and the current bin stamp;
  stale scratch after NEW/OPEN cannot supply peer evidence. The existing
  27-address halo is generated once per tile and loaded by every owner WG.
  Metric operations, root identity, shared closure and quantization unchanged.
ABI=23 / 31 pipelines / 44 resources. Native and Unity command paths rewired.
  Still one 32 MiB snapshot buffer: dense R1 payload is dead before grouping;
  grouped source indices/owner ranges remain read-only through the resolver;
  disjoint compact skin items feed RGB/V. R1 capacity=1,044,350 changes;
  measured owners/skin items capacity=79,343 each. No world-address or thread
  topology limit changed. Capacity failure is local/explicit, no partial tile
  owner range is published. CPU did not gain a solver, worklist or readback.
COMPILE=Unity codegen/C# PASS:
  /mnt/kingston-unity/Builds/QuestMerkabaScan/realtime-rt2-actual-owners.log:733
  Necessary native compile/spirv-val, not full audit/APK:
  /tmp/m8-rt2-grouped-lookup-ler08fhr: Prepare 26316 B / 1070 body /
  6232 B shared / 4 RW; resolver 704608 B / 36263 body / 14496 B shared / 6 RW.
  /tmp/m8-rt2-owner-scope-2whk_lvs: Root 513456 B / 26773 body;
  L1 704848 B / 36818 body; L2 704792 B / 36818 body;
  all three 14564 B shared / 5 RW / 128 lanes.
  /tmp/m8-rt2-actual-owners-jct_drnr: R1 preparation/publication and both
  skin consumers compile; R1 still 8 RW, RGB/V still 4 RW. diff --check PASS.
NEXT=RT2 OPEN: required-only original acquisition and footprint-directed skin,
  ERASE epoch path, conditional allocation and local allocator outcomes.
  RT3 readout/export rewrite and V atlas remain, RT4 acceptance has not begun.
TESTS/APK/INSTALL/DEVICE=not run. No timing or full-rewrite acceptance claim.

## RT2 — required original evidence and cooperative carrier closure (after 24fc51d)

CHANGE=removed the resolver's unconditional 52-original acquisition. R1 source
  incidence selects the required higher shells; surviving carriers request
  inherited knots and only the present phase families' original dependencies.
  One shared known/requested mask pair guards exact cached evidence. The cache
  is owner/snapshot-local and not a task list or persistent refinement state.
  Geometry requests a coarse original only when that innovation participates
  in the reached child's prediction; endpoint reduction banks retire first.
  Resolver source anchors (18) and wedge/sign predicates (48) now occupy
  independent lanes instead of the scalar carrier evaluator's nested loops.
  The existing deterministic combination and canonical symbol publication
  consume those same predicates. COLD dependency collection preserves each
  source wedge's ordered early-out. No geometry/RootOwner rule changed.
  Removed obsolete multi-owner/52-or-single packet modes. No CPU solver,
  scan readback, extra dispatch, buffer or ABI change.
COMPILE=necessary exact native glslang/spirv-val PASS, not a full audit:
  /tmp/m8-rt2-cooperative-carriers-878ozbso/pipeline-{0..3}.spv
  Resolver 710932 B / 36608 body / 14684 B shared / 6 RW;
  SHA256=5cf4bbc58cfda02b835fedada71d4a47b57e08b6dab58e4e6b6938f0cdd7ef5d.
  Root 511080 B / 26643 body / 14572 B shared / 5 RW;
  L1 708536 B and L2 708480 B / 37047 body / 14580 B shared / 5 RW.
  All four use 128 lanes. git diff --check PASS. No C# or codegen ABI changed.
NEXT=RT2 OPEN: measured-footprint skin reach; ERASE epoch path; conditional
  allocation; local allocator/contention outcomes. RT3 readout/export decode
  and V atlas remain; RT4 integrated acceptance has not begun.
TESTS/APK/INSTALL/DEVICE=not run. No measured speedup or completed-rewrite claim.

## RT2 — measured-footprint skin reach (after a8de0b9)

CHANGE=current measured-owner records project their whole bounded pixel cells
  onto the evaluated L2 wedge charts. The existing generated chamber transforms
  select reached L3/L4/L5 localities, conservatively retaining shared-boundary
  alternatives. Seven-value split groups are deduplicated in a transient 57-bit
  mask stored in the final unused 8 bytes of the existing 224-byte SignalItem.
  RGB and V consume that same reached-group mask; no 399-position sweep or
  persistent work state was added. Every stored split still requires the
  unchanged full seven-child support predicate. Metric projection failure does
  not veto RGB: generated possible wedge-support groups remain eligible for
  their independent signal predicates. Empty reached carriers allocate nothing.
  Six chart frames are shared by the owner's source-pixel lanes, not rebuilt
  per measurement. Frame arithmetic was factored from the existing metric
  frame without changing its ordered Gram/normal expressions. No CPU hotpath,
  readback, extra dispatch, allocation or external ABI change.
COMPILE=necessary exact native glslang/spirv-val PASS, not full audit/tests:
  /tmp/m8-rt2-reach-final-ifmx1p13/pipeline-0.spv
  ResolveFlowerCarriers 935964 B / 48376 body / 15464 B shared / 6 RW / 128;
  SHA256=6eeaddc4763f14ab782362809a2d4f599ded2a275c9b0bdcc77f9a3c4d2ed662.
  /tmp/m8-rt2-skin-reach-3hv4y2in/pipeline-{1,2}.spv
  RGB 307940 B / 15835 body; V 503004 B / 25509 body;
  both 204 B shared / 4 RW / 64 lanes. git diff --check PASS.
NEXT=RT2 OPEN: ERASE currently deletes M8 before its pending epoch finalizer;
  replace through the shared same-transaction preparation/peer/publication
  path and remove retry consumers. Then conditional allocation and local
  allocator/contention outcomes. RT3 readout/export and RT4 remain unfinished.
  RT4 must cover footprint selection against the exact chamber oracle, including
  boundary-spanning pixels, projection ambiguity and independent RGB/V novelty.
TESTS/APK/INSTALL/DEVICE=not run. No measured speedup or full-rewrite acceptance.

## RT2 — transactional ERASE and fence-only retirement (after 08c2617)

CHANGE=ERASE prepares zero M8 through the shared R1 epoch/peer/publication
  path. No M8-first destructive write or pending epoch finalizer remains.
  The five-stage graph reuses InvalidateFlowerPeers and PublishFlowerR1;
  all counters/indirect headers are reset before this job. Native pipeline
  membership follows the generated schedule, not its enclosing index range.
  A COLD peer uses the existing SSD request queue and skips only its dependent
  mutation. Explicit ERASE does not run parent-plane structural classification.
  Shared publication updates occupancy/active bits and clears its changed bit.
  Fine tokens use the common observation allocator, distinct from attempt IDs.
  Removed held-brush dependency retries and completion-result CPU readback for
  BOTH scan and ERASE. Retirement follows GPU fences; GPU counters still own
  work/results. Storage metadata records the retired token only, and its existing
  pressure maintenance recognizes completed one-shot observations. CPU has no
  new geometry backend. Dirty UI bookkeeping is conservative after a mutation;
  actual world dirty records remain GPU-authored. No buffers added/enlarged.
ABI=24; 31 pipelines, 44 resources; FineErase executes five existing pipelines.
COMPILE=Unity C#/Editor codegen PASS in
  /mnt/kingston-unity/Builds/QuestMerkabaScan/realtime-rt2-erase-retirement.log.
  Necessary exact glslang/spirv-val PASS, not a full audit:
  /tmp/m8-rt2-erase-bindings-z2mogf89:
    ERASE 67196 B / 3309 body / 124 B shared / 8 RW / 128 lanes;
    SHA256=47fd53436877904c4c20d3a9fc7e8f34c6d74976e99a8f745274759119ede950;
    FlowerCommit 865740 B / 46243 body / 24700 B shared / 8 RW / 128.
  /tmp/m8-rt2-erase-final-h8l_o2uo:
    query 25924 B / 1050 body / 52 B shared / 5 RW / 64;
    finalizer 23516 B / 948 body / 8 B shared / 6 RW / 128.
  /tmp/m8-rt2-erase-qjwrhbr1:
    peer 55064 B / 2717 body / 120 B shared / 5 RW;
    publisher 10916 B / 309 body / 0 B shared / 7 RW.
NEXT=RT2 conditional allocation/recount; owner-local allocator/contention;
  malformed-peer publication handling and remaining dual global gates. RT3
  readout/export decode and V atlas remain. Do not redo this ERASE/retirement cut.
TESTS/APK/INSTALL/DEVICE=not run. Native plugin/full embedded set not rebuilt;
  final native build and integrated acceptance belong to RT4, not this checkpoint.

## RT2 — GPU-conditional sparse publication and recount (after de79665)

CHANGE=first HOT endpoint counts survive to Reserve/Emit unchanged. New sparse
  addresses traverse the fixed Block -> Chunk -> Tile publication boundaries
  inside the same snapshot; allocation/reset and recount use GPU indirect
  arguments. The 64-byte existing argument buffer holds active recount at
  byte 48 and NEXT dimensions in the unused pre-Reserve tile-work packet.
  COLD-only requests keep resident counts and retire their allocation gate.
  Final claims are published before dual reuses the claim queue. No new
  entrypoint, buffer, CPU scan decision, readback or continuation state.
  Removed UpdateObservationDual's observation-wide unresolved-direct veto;
  local support/certificate/dual readers still enforce missing residency.
  Native dispatch mode is per scheduled command, permitting the same Count
  pipeline to be direct once and indirect only when missing addresses require
  recount. Generated maximum timing count is checked against managed capacity.
ABI=25; 31 pipelines, 44 resources; observation has 40 recorded commands:
  18 allocation/reset indirect + 3 recount indirect + 19 others. HOT skips
  the corresponding work through zero GPU arguments, not CPU filtering.
  Recorded commands/barriers are not claimed removed or device-measured.
COMPILE=Unity C#/Editor codegen PASS:
  /mnt/kingston-unity/Builds/QuestMerkabaScan/realtime-rt2-allocation.log.
  Exact target glslang/spirv-val PASS in /tmp/m8-rt2-allocation-i6kujr5d:
  Count 25068 B / 1127 body / 0 B shared / 8 RW;
  Reset 7560 B / 242 / 12 / 5; Reserve 7816 B / 277 / 2060 / 4;
  Emit 25012 B / 1122 / 0 / 4; Reduce 8180 B / 300 / 8196 / 6;
  UpdateObservationDual 609132 B / 30721 / 112 / 8, 64 lanes.
NEXT=RT2 OPEN: owner-local allocator contention, malformed-peer publication
  and complete THROUGH policy. RT3 readout/export and RT4 still remain.
TESTS/APK/INSTALL/DEVICE=not run. Native plugin/full embedded set not rebuilt;
  this is an implementation checkpoint, not full rewrite acceptance.

## RT2 — allocation-free updates stay owner-local (after e705f94)

CHANGE=existing owner lookup, phase writes within allocated capacity and phase
  removal no longer lease the global allocator. RGB/V replacement updates only
  its seven values in place (uint4/uint2), retaining allocation, other groups,
  split masks and optical program. Actual skin growth alone leases its payload
  pool; RGB does not lease the unrelated detail pool. All callers retain the
  exclusive measured-owner WG and raw-reader retirement requirements. No CPU
  geometry, new buffer, new dispatch, persistent cursor or spin lock added.
COMPILE=exact target glslang/spirv-val PASS in
  /tmp/m8-rt2-owner-updates-votomwa5:
  Root 511764 B / 26681 body / 14572 shared / 5 RW;
  L1 709220 B / 37085 / 14580 / 5; L2 709164 B / 37085 / 14580 / 5;
  RGB 309324 B / 15915 / 204 / 4; V 504556 B / 25603 / 204 / 4.
NEXT=RT2 actual allocation/growth and epoch-wrap contention remain, as does
  malformed-peer publication. Do not reinterpret BUSY as exhausted capacity.
  Retained REV-C14.1 explicitly uses R1 hysteresis under full THROUGH; no
  evidence-policy change was made while removing execution gates. RT3 and
  integrated RT4 remain. TESTS/APK/INSTALL/DEVICE=not run.

## RT2 — contention-free arena publication (after 18d3bd2)

CHANGE=global buddy leases replaced by free/allocated bitplanes and three
  availability summaries within the existing metadata reservation. Atomic
  claims own blocks; pair CAS coalesces retired buddies. Real-bitmap fallback
  prevents stale hints alone from reporting capacity. First index/owner uses
  initialized-then-CAS publication, reclaiming losing allocations. All phase,
  RGB/V growth, epoch-wrap, storage import and page consumers are rewired;
  optical refcounts are atomic. BUSY denotes an unretired reader only. No CPU
  geometry, dispatch, buffer, persistent workset or global spin lock added.
ABI=26, resident metadata only; persistent record formats/addressing unchanged.
COMPILE=Unity C#/Editor codegen PASS:
  /mnt/kingston-unity/Builds/QuestMerkabaScan/realtime-rt2-atomic-arena.log.
  Exact target glslang/spirv-val compiled nine entries in
  /tmp/m8-rt2-atomic-arena-f_l4ao4d: Root 541576 B / 28307 body;
  L1 739032 / 38711; L2 738976 / 38711; RGB 353136 / 18314; V 548368 / 28002;
  FlowerCommit 906224 / 48574 / 24700 B shared / 8 RW; ERASE 80840 / 4086;
  ReserveDirty 29264 / 1539; PublishDirty 37936 / 1991.
NEXT=RT2 malformed-peer publication; then RT3 presentation/export rewrite.
  RT4 must prove concurrent allocation/creation/free/coalesce and stale hints.
TESTS/APK/INSTALL/DEVICE=not run. Native plugin/full embedded set not rebuilt.
  Compile is not a concurrency proof, runtime measurement or full RT2 closure.

## RT2 — preflight before source/peer mutation (after fbdf6a8)

CHANGE=FlowerCommit and ERASE validate the source and actual peer fine spans,
  sorted keys and current phase intervals without changing epochs or rows.
  Missing fine owner and malformed directory are distinct. Invalid local
  writes increment failedWrites instead of masquerading as capacity/global
  admission failure. Only fully preflighted changes enter the prepared list.
  InvalidateFlowerSources cuts owner epochs/runs; the existing unique peer
  pass cuts receivers; PublishFlowerR1 writes M8 after both GPU barriers.
  Source pass is GPU-indirect over the same snapshot-local prepared records.
  No CPU geometry, completion readback, new buffer, future cursor or retry.
WHY=folding fine writes into the M8 publisher compiled with 9 writable bindings;
  Quest limit is 8. That version was replaced, not accepted. A separate source
  boundary also prevents peer preflight racing source epoch/count writes.
ABI=27, 32 pipelines, 44 resources; native and Unity consumers connected.
  Observation has 41 recorded commands, readout 7, ERASE 6. Timing capacity
  matches the complete generated graph, including zero-work indirect commands.
COMPILE=exact target glslang/spirv-val PASS in
  /tmp/m8-rt2-peer-boundary-eps8j_uh:
  FlowerCommit 835372 B / 44443 body / 24700 B shared / 8 RW;
  Sources 68200 / 3580 / 0 / 6; Peers 55844 / 2746 / 120 / 5;
  Publisher 12052 / 342 / 0 / 7; ERASE 74792 / 3732 / 124 / 8;
  Resolver 938184 / 48491 / 15464 / 6. All use 128 lanes.
  Unity C#/Editor codegen PASS:
  /mnt/kingston-unity/Builds/QuestMerkabaScan/realtime-rt2-peer-publication.log.
CURSOR=RT2 implementation checkpoint; integrated acceptance belongs to RT4.
  NEXT=RT3 GPU readout and offline frozen-export actual-carrier decode,
  bounded incident completion, removal of exhaustive packets and RGB/V atlas.
  Do not redo RT2 or resume historical numbered RUNs. CPU oracle/codegen stays
  offline; it never produces live pages or scan evidence.
TESTS/APK/INSTALL/DEVICE=not run. Native plugin/full embedded set awaits RT4.
  RT4 must prove malformed-peer rejection leaves source epoch/M8 unchanged,
  concurrent adjacent sources, full THROUGH, ERASE and allocator concurrency.

## RT3 — frozen RGB/V atlas consumer (after ce13930)

CHANGE=atlas allocation reads the compiled RGB/V split union, including V-only
  detail. One shared skin evaluation per texel produces captured RGB and the
  additive A3/A4/A5 gradient in the evaluated L2 wedge frame. The paired normal
  PNG contains linear tangent-space values, never sRGB-encoded normals. Atlas
  padding projects only to active chart support, not unproved inactive sites.
  GLB/3D Tiles share the same writer, seven positions and active wedge indices.
  Removed the arbitrary first-incident per-knot NORMAL stream; flat L2/UV
  normal generation remains presentation, not a knot identity or world field.
  Base-color pixels remain absolute RGB with white COLOR_0 for textured sites.
  normalTexture carries explicit derived/lit-fallback metadata; unlit does not
  display it. Exact V intervals remain in the unchanged native record payload.
  The foreign unlit preview accepts omitted NORMAL without CPU normal solving;
  native records still reopen through GPU page compilation, not this importer.
  Both atlas streams are flushed and hashed by the same 76-byte cell receipt;
  versioned export policy rejects old RGB-only resume instead of mixing formats.
COMPILE=Unity C#/Editor codegen PASS:
  /mnt/kingston-unity/Builds/QuestMerkabaScan/realtime-rt3-rgbv-atlas.log.
  No scan shader, runtime queue, canonical field or CPU hotpath was added.
CURSOR=RT3 OPEN. Next: GPU actual-carrier decode without the large original-root
  packet, bounded incident completion/coverage and corresponding CPU frozen
  decode. RT4 must check V-only/nested atlas, mirrored/wound tangent convention,
  gutters, paired receipts/resume and GLB/native package offsets. Old NORMAL
  stream/RGB-only/PBR fixture expectations remain to be reconciled in RT4.
TESTS/APK/INSTALL/DEVICE=not run. This is a compile checkpoint, not parity proof,
  a portable-viewer visual result or completion of the full rewrite.

## RT3 — reached DIRT coverage and lane fan-out (after b568f44)

CHANGE=coverage's independent (cell, face, half) work is partitioned across
  32 lanes per actual carrier, utilizing the existing 256-lane page WG. The
  exact triangle/union/paired-edge predicates and OR publication are unchanged.
  A remaining exposed face reaches only owners inside its integer corner box
  expanded by the existing +/-2a knot bound. Generated corner lookup supplies
  the CPU/HLSL bound; X-run bit unions build a 69-word neighbor-owner mask.
  Central owners are excluded because their coverage already ran. Unrelated
  halo endpoints are no longer queried merely because some face needs coverage.
  Frozen CPU export uses the same per-face bound, memoizes only reached owners,
  and tests cached footprints only against queries in their support domain.
  COLD and exact union/completion semantics remain, with no CPU hotpath work,
  new buffer, new dispatch or persistent geometry/work state.
COMPILE=Unity C#/Editor codegen PASS:
  /mnt/kingston-unity/Builds/QuestMerkabaScan/realtime-rt3-coverage.log.
  Exact glslang/spirv-val Compact compilation in /tmp/m8-rt3-coverage-vc4r4d_b:
  1,268,260 B / 67,329 body instructions / 29,740 B groupshared / 256 lanes /
  7 RW. SPIR-V hash 8765a5c0951642ad6f2d36a236babed0534c85d1b73f16089198f06d6519df40.
  This is a compile check; the entrypoint STILL FAILS the production size gate.
CURSOR=RT3 OPEN. Large original-root/site packet and bounded completion decode
  replacement remain. Geometry.hlsl and Support.hlsl were read completely at
  this cut; the main Readout packet/handlers/acquisition are understood, not an
  invitation to repeat the old RT2 audit. RT4 must compare reached-neighbor
  masks to complete coverage and scalar vs 32-lane OR, including boundary/COLD.
TESTS/APK/INSTALL/DEVICE=not run. No runtime speedup or full closure claimed.

## RT3 — compact reached source intervals (after 9e156d2)

CHANGE=removed the fixed 52 x 19-word full-original-root packet from readout.
  Generated node/sign addresses retain only status, receipt and value handles;
  reached nonempty Tag/four-endpoint payloads occupy compact scratch slots.
  Bit-identical raw/final payloads reuse a slot. Implicit J and the explicit
  empty-J marker preserve failed/ambiguous evidence as well as certain roots.
  Endpoint-local pure restrictions stay on their own query lane; no peer raw
  root is staged through the carrier-site buffer. Only present L1 ancestors
  occupy rank-addressed six-word metric entries; J comes from generated strand
  offsets. No canonical record, GPU resource, dispatch or CPU path was added.
COMPILE=exact native target glslang/spirv-val PASS:
  /tmp/m8-rt3-source-cache-1db28ywd
  Compact 1,287,540 B / 68,454 body instructions / 26,556 B shared / 256 lanes /
  7 RW. SHA256 1690a0ee6aa263b03247cb59545ef7090795abc8388422fd8c039c5b4dc9df86.
  Shared memory fell 3,184 B, but code grew; the size gate STILL FAILS.
CURSOR=RT3 OPEN. Next remove generic full-root ancestry transport arrays and
  repeated site/completion evaluation. Do not confuse a compact cache with
  completion of the actual-carrier decoder or a measured runtime speedup.
  RT4 must cover absent/provisional/ambiguous/boundary roots, exact raw-vs-final
  payload identity, empty J, signed limits, owner-cache reset and COLD receipts.
  Removed the obsolete RT3 NOT STARTED footer from DAG; its current cursor
  remains RT3, not RT4 or a historical numbered run.
TESTS/APK/INSTALL/DEVICE=not run; no CPU geometry entered the hotpath.

## RT3 — streamed generated ancestry transport (after 344d4eb)

CHANGE=removed full parent/ancestor root arrays and record/key arrays from
  production PredictGeometryNode. Each reached ancestor is consumed by the
  shared generated ReadTransportTerm predicate while its source is live;
  transport retains only two four-integer terms. Begin/Finish preserve the
  exact generated child address, endpoint orientation, complete interval and
  ordered rational rotations. Epoch/key/sector/source-incidence checks remain.
  The general oracle bridge uses the same predicates, not a second formula.
  Frozen CPU decode uses the identical term flow and no longer recomputes the
  already classified child base. No live CPU evaluator or fallback was added.
COMPILE=Unity C#/Editor codegen PASS:
  /mnt/kingston-unity/Builds/QuestMerkabaScan/realtime-rt3-transport-terms.log.
  Exact native glslang/spirv-val, /tmp/m8-rt3-transport-terms-3ei4d6yw:
    Compact 1,285,712 B / 68,316 body / 26,556 B shared / 7 RW / 256 lanes
      sha256 1f234adb4fd9e2fdcde1246f8058dddc0df3de45db163e2762d55c3482d5eedd
    Resolver 936,388 B / 48,353 body / 15,464 B shared / 6 RW / 128 lanes
    Root     525,704 B / 27,471 body / 14,572 B shared / 5 RW / 128 lanes
    L1       739,632 B / 38,702 body / 14,580 B shared / 5 RW / 128 lanes
    L2       739,576 B / 38,702 body / 14,580 B shared / 5 RW / 128 lanes
  Compact STILL FAILS the production size gate; compilation is not acceptance.
CURSOR=RT3 OPEN. Next address repeated actual-carrier site acquisition and
  completion work; do not repeat the source-cache or ancestry-array changes.
  RT4 must prove no/coarse/L1/both-ancestor transport against the oracle,
  signed/witness boundaries, incompatible/stale records and raw-base reuse.
TESTS/APK/INSTALL/DEVICE=not run. No performance or full-rewrite PASS claimed.

## RT3 — shared completion junction evidence (after 98cd3ff)

CHANGE=ordered donor preflight remains per actual candidate. Only a certain
  donor set requests the eight R3 metric/dual alternatives; eight GPU lanes
  compute those independently once per frozen owner, not per candidate.
  Their 40-word q/tag/cold/status cache occupies existing control scratch and
  survives candidate batches only, never a GPU job/observation or saved world.
  Each candidate still uses its own generated junction filter and required
  dual receipt. Precomputed orientation cannot override an unresolved junction;
  all sixteen child proofs remain mandatory. Removed the former serial
  ReadR3Junction and monolithic PrepareCompletionPetal shader helpers.
  Frozen CPU FlowerDecode lazily caches the same evidence. The uncached oracle
  and cached path share the collector; no CPU hotpath backend was introduced.
COMPILE=Unity C#/Editor codegen PASS:
  /mnt/kingston-unity/Builds/QuestMerkabaScan/realtime-rt3-junction-evidence.log.
  Exact native glslang/spirv-val, /tmp/m8-rt3-junction-lanes-rdg2sko_:
  Compact 1,291,628 B / 68,638 body / 26,556 B shared / 7 RW / 256 lanes /
  75 barriers; sha256 8d7106e257b0990ea6b9578e91dd0bf10b748a2799a1399b630ad2b8e5208dc8.
  Size gate STILL FAILS; this does not establish a device speedup.
CURSOR=RT3 OPEN. Next parallelize actual skin-sample materialization in the
  existing 256-lane FINAL emit phase: eight carriers x32 lanes, one region
  evaluator call site, existing reserved spans and the unused emission-phase
  footprint scratch. SkinReadout.hlsl was read completely. No new dispatch
  or CPU fallback is needed. Repeated carrier/site acquisition remains after it.
  RT4 must cover zero donor-backed candidates, multiple candidate batches,
  per-flag junction filtering, contradictory/ambiguous orientation precedence,
  COLD receipt union and cache retirement between owners.
TESTS/APK/INSTALL/DEVICE=not run; the goal and RT3 remain open.

## RT3 — cooperative actual-signal emission (after ee7ca25)

CHANGE=FINAL emission uses all 256 GPU lanes, thirty-two per actual carrier.
  Only its persisted union samples are visited. Compact group rank is inverted
  with five bounded integer selections and the existing generated thread map;
  root and child samples share one CompileSkinRegion call site. The resident
  RGB/metric layouts are fetched once, stored in twenty existing footprint
  scratch words and consumed by the cooperating lanes. No new scratch capacity,
  buffer, dispatch, CPU work or persistent program cursor. Root RGB replacement,
  additive A3/A4/A5 and certified optical fields retain their evaluator.
  All writes complete before the header. A malformed sample rejects the page,
  and M8-only uniform carriers still allocate zero skin bytes. Removed the
  scalar CompileSkinDrawProgram/CompileCarrierSkin production path.
COMPILE=exact native glslang/spirv-val PASS:
  /tmp/m8-rt3-skin-lanes-gy2skyge
  Compact 1,232,316 B / 65,201 body / 26,556 B shared / 7 RW / 256 lanes /
  76 barriers; sha256 c349d9b2df838d65aeaccb74d180b9e3619d2ace5d0d5909da06b559afc6fc0e.
  Size gate STILL FAILS; no runtime speedup or integrated parity claim.
CURSOR=RT3 OPEN. Next reuse already computed site intervals in dual wedge
  support, then remaining actual-carrier/site work. Do not repeat skin or R3
  changes. RT4 must cover sparse/dense split select, both split words, union-only
  RGB/V, optical-only root, stale/malformed runs, capacity checks, header/write
  ordering and exact shader/CPU sample bytes. Necessary shader compile only;
  TESTS/APK/INSTALL/DEVICE=not run.

## RT3 — reuse selected knot bounds for dual admission (after b3fc153)

CHANGE=SupportWedgeBounds reads the exact coordinate enclosure already computed
  by the cooperative site stage. It validates the batch owner/carrier/site and
  full root tag/J/interval identity before using the cache; no fit, midpoint
  enclosure, clamp or recomputation. The shared hub/ring interval is not rebuilt
  per incident wedge. Translation, floor/cell range, dual predicate and required
  COLD receipts remain unchanged. The scalar non-batch consumer retains the
  same RootRelativeBounds formula, not a production readout fallback.
COMPILE=exact native glslang/spirv-val PASS:
  /tmp/m8-rt3-shared-support-vlpg9b4x
  Compact 1,227,068 B / 64,938 body / 26,556 B shared / 7 RW / 256 lanes /
  76 barriers; sha256 a0c169d134dc7e4ccfe4b9c8675778892f7d824e90ce3e2d4170c29b3f1a1274.
  Size gate STILL FAILS. No new scratch, dispatch, CPU hotpath or device claim.
CURSOR=RT3 OPEN. Selected seven-site positions and six wedge dual queries still
  run on eight carrier lanes despite cooperative upstream evidence. Continue
  that bounded fan-out; do not repeat source/ancestry/junction/skin work. RT4
  must compare cached/noncached bounds and dual receipts bit-for-bit including
  half-open roots, shared hubs/ring knots, negative coordinates and COLD halo.
TESTS/APK/INSTALL/DEVICE=not run. The complete rewrite is not yet accepted.

## RT3 — selected geometry on lanes; RT4 handoff (after f778a51)

CHANGE=shared SelectL2Carrier/FinishL2Carrier predicates separate symbolic
  choice from materialization without changing admission or winding. The
  observation consumer retains their scalar application. Page compilation
  uses 56 independent selected-site lanes and 48 independent wedge-dual lanes.
  Metric results occupy the ancestor scratch after acquisition retires; shared
  allocation stays unchanged. Ordered receipt reduction preserves the first
  failed site's dependency prefix and partial root/position output. Dual still
  uses the same three knot intervals, ordered cell traversal and COLD rules.
  The page consumer copies results, not another geometry solver or fallback.
COMPILE=exact native glslang/spirv-val PASS:
  /tmp/m8-rt3-selected-batch-qm15wad2
  Compact 1,239,460 B / 65,745 body / 26,556 B shared / 7 RW / 256 lanes /
  80 barriers; sha256 82db3bc698e7c6b3b9cc067286bf6fc445791cf0c73648876dad69637df672bf.
  /tmp/m8-rt3-selected-resolver-xrc1ov1w
  resolver 938,760 B / 48,519 body / 15,464 B shared / 6 RW / 128 lanes;
  sha256 37811dbfea8c437624debda15df70abdad51363d0b7c0d4e5909cbb72d0ffb4e.
  The first resolver compile caught a reserved HLSL identifier in an adapter;
  it was corrected before the successful compile. No tests were substituted.
CURSOR=RT3 is an implemented compile checkpoint. All replacement presentation
  consumers are connected; no acceptance or speedup is claimed. RT4 is now
  active for complete-suite reconciliation/validation and shader/ABI repairs.
  Compact STILL FAILS the production size budget; that remains mandatory repair
  before the APK, not an exception. Preserve positive fixtures and validate
  scalar/cooperative selection, invalid-site dependency prefixes, root identity,
  direct/completed ownership, wedge dual cache and scratch phase retirement.
TESTS/APK/INSTALL/DEVICE=not yet run for the complete rewrite. Goal remains active.

## RT4 — full-suite reconciliation and RGB/V atlas proofs (after bc744c1)

CHANGE=tests now assert GPU-fence retirement without CPU geometry readback,
  local COLD handling and same-snapshot source/peer/M8 publication. Removed
  expected persistent cursor/global mutex and per-knot NORMAL stream; no
  production fallback or admission predicate was added. The GPU nonzero-R2
  fixture fills the actual buddy pool, observes local capacity failure, releases
  it and retains the original exact-R2/idempotence/parity requirements. HOT
  counts persist without NEXT; the gated recount still retires every old span.
  Added six cases for V-only L5 atlas, all four mirror/winding combinations,
  nested A3/A4/A5 and unchanged L2 positions; both RGB and V checksum corruption
  must reject resume. GLB/Tiles tests read the current shared-position ABI.
VALIDATION=first complete run 368/383 PASS (15 failures), followed by full
  389/389 PASS after reconciliation, no skipped/ignored tests:
  /mnt/kingston-unity/Builds/QuestMerkabaScan/realtime-rt4-reconcile/TestResults/
  merkaba-results.xml; merkaba-tests.log. Unity used two workers and a user
  scope with MemoryHigh=12G, MemoryMax=16G, MemorySwapMax=2G. No concurrent
  native compilation. Command-graph audit reports 41 observation dispatches
  (18 allocation indirect), 7 readout and 6 ERASE; no <=10 or speedup claim.
CURSOR=RT4 shader-gate repair. Compact remains 1,239,460 B / 65,745 body
  instructions, over the mandatory size gate. Full shader/GLB gates and
  APK/install/Quest acceptance remain outstanding. Goal is active, not done.

## RT4 — GPU shader closure and coherent resource views (after 96f6a14)

CHANGE=existing count/reserve/emit page schedule now compiles count and emit
  as separate entrypoints over one recipe. Seven readout commands remain;
  ABI28 has 33 pipelines, 44 resources and 41 timing slots. Eight reached
  carrier tasks use eight lanes with deterministic mask rank. Coverage reuses
  exact cell/halo receipts and proved wedge winding; its ordered segment,
  orientation and witness arithmetic share bodies. R2/R3 rotation and child
  ancestry retain their original operation/failure order. Generated outward
  rounding pairs predecessor/successor bit operations, not geometric inputs.
  CPU remains offline oracle/codegen/export only; no live CPU producer or
  scan-decision readback was added. No tolerance, geometry or admission change.
RESOURCE FIX=FlowerCommit and peer invalidation now use the same TileRecords
  view throughout; PublishFlowerR1 reads/writes the same four M8 bank views;
  ERASE halo validation uses its writable chunk-reference descriptor. Targeted
  native reflection validates all four entries with <=8 writable bindings.
VALIDATION=complete exact native audit 78/78 PASS (61 production, 33 native,
  17 oracle), including all size/body, shared-memory and RO/RW alias gates:
  /mnt/kingston-unity/Builds/QuestMerkabaScan/realtime-rt4-gates/spirv-unity-fixed.
  Compact 930,876 B / 49,915 body / 26,552 B shared / 6 RW / 256 lanes;
  Emit 647,240 B / 34,579 body / 26,272 B shared / 7 RW / 256 lanes.
  No hard budget exemptions; occupancy warnings remain, not device acceptance.
  First Unity run 378/391 caught three struct-valued ternary expressions DXC
  rejects. Explicit branches/numeric component selection fixed them; full
  rerun 391/391 PASS, zero skipped/ignored/inconclusive, 86.56 s:
  /mnt/kingston-unity/Builds/QuestMerkabaScan/realtime-rt4-unity-fixed/TestResults.
  Includes two direct GPU/scalar IEEE rounding/ordered interval comparisons.
  Codegen/C# PASS; GLB interoperability PASS (0 Khronos errors/warnings, NodeIO
  opens the untextured DIRT fixture). Paired RGB/V atlas is covered by the suite.
  Command graph still reports 41/7/6 scheduled observation/readout/ERASE calls;
  actual nonzero work requires device measurement, no <=10 or speedup claim.
CURSOR=RT4 host gates passed; coherent commit then Unity APK/push. No rewrite APK has
  been built or installed. ADB currently lists no connected device; runtime
  speed, visible scan and sleep/resume remain DEVICE ACCEPTANCE PENDING.
