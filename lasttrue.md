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
