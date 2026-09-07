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

Accepted observations provide fixed calibrated maximum bounds:

\[
\epsilon_{N,s},\qquad \epsilon_{\delta,s}.
\]

Observations exceeding them are invalid, not downweighted.

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

A root interval is `CERTAIN` in a sector only when the complete interval lies strictly inside that sector.

Crossing a sector boundary yields `AMBIGUOUS`.

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
2. all roots lie in the generated sectors of the same flag;
3. each shared-loop endpoint correspondence satisfies section 7.3;
4. the three boundary-strand tangent/orientation intervals are nondegenerate;
5. winding agrees with the direct free-side orientation;
6. no node/strand/petal support is dual-vetoed.

R2/R3 direct measurement is not required for basic R1 existence. When absent, the R1 carrier may predict the corresponding higher-shell anchor **only as provisional geometry inside the same unique flag**. Such a prediction does not create persistent R2/R3 detail and cannot by itself prove a hole completion.

### 8.4.2 Exact R1 face-sector flag admission

For a fixed directed R1 face carrier \(f=s_i e_i\), use the existing power
residual:

\[
\Phi_q(X)=q\cdot(X-C_K)-\frac{a_L}{2}(q\cdot q).
\]

Select the maximum `Phi` among the four R2 edge directions incident to
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
This face chart does not replace or flatten the R2/R3 world loops.

Codegen uses the existing `Petal[48]` incidence, exact loop boundaries and
exact sign algebra on the power differences. The sign at a certified
sector interior may be evaluated by its exact positive-angle boundary
limit; no finite angular step or sampled adjacency is permitted.
It emits one `Sector→PetalMask` for each directed R1 face and sector.
Each mask MUST contain exactly one incident flag; all eight flags of each
face MUST be covered. Zero or multiple incompatible flags is a codegen
failure, not a runtime choice.

The half-open equality convention defines exact ownership only.
Section 7.1 still requires a root interval strictly inside one sector:
touching or crossing an order boundary is `AMBIGUOUS`, not a tie-based
permission to emit an uncertain root. Direction, sector and rootSign
transport remain the generated shared-loop conventions.

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
root interval crosses a generated sector boundary,
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
3. all three knot/root intervals already exist uniquely from incident confirmed petals;
4. its generated petal/junction class, sector and rootSign are unique;
5. R1 is certain;
6. required R2/R3 closure for that particular flag is certain;
7. no portion of its metric interval is `THROUGH_CERTAIN`;
8. its generated anchor/tangent orientation determinant interval excludes zero.

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

The interval contains:

- sensor quantization;
- calibration reprojection error;
- stereo hypothesis width.

An invalid pixel has no interval.

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
    consume remaining work from the SAME immutable observation
    no new camera input required
    stop only at workset exhaustion or AMBIGUOUS evidence boundary

FinalizeObservation
```

`DrainObservationRefinement` is a semantic obligation, not a mandatory extra micro-dispatch. Production MAY fuse it into `FlowerCommit` or process the fixed L2-skin stages in one workgroup/dispatch. The implementation MUST prefer fused local work over a dispatch zoo. Its order is a scheduler implementation detail and MUST NOT use Fibonacci.

The immutable observation may be released only after all work that it can make CERTAIN has either committed or been proven unnecessary. AMBIGUOUS children are not pending compute; they require new evidence and therefore do not keep the observation alive.

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

A hypothesis with ambiguous projection or insufficient valid root support remains unresolved.

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
R1 same-shell power-order cuts are complete in the shared sector arrangement
R1 clipping is exactly 2 in xi=2*(X-C)/aL and preserves every R1 world loop
each directed R1 sector selects exactly one incident flag and covers all eight face flags
exact equality ownership follows frozen Node ordinal; boundary-crossing intervals remain AMBIGUOUS
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
