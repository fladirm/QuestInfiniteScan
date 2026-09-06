# M8 DUAL SPHERE–FLOWER

## CLOSED PRODUCTION ALGORITHMIC CONTRACT

### Mandatory base

```text
c34d27f0ecb51500b12209ed5d2fe72b893726f5
```

This contract replaces every previous Sphere–Flower draft.

**REV-C closure:** this revision preserves every REV-B closure, removes acquisition throttling, and freezes the skin representation. Fibonacci is never a refinement gate and never ties detail convergence to new camera observations. One accepted immutable observation must drain every finite child test that its own bounded evidence can resolve; new viewpoints are required only for new information or ambiguity. Geometry ends at L2. Every valid L2 carrier owns the same immutable planar Flower-7-in-Flower L3/L4/L5 address space and the same recursively subtree-contiguous 399-position embroidery. Scan evidence refines only the piecewise RGB and additive metric-V signal written on that fixed thread. Readout always performs the same three exact barycentric descents and never selects a detail level.

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
Canonical coarse surface:
    M8 KernelState

Persistent negative volume:
    sparse SEE_THROUGH hierarchy

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

`SEE_THROUGH` means only:

> The complete represented support was certified as visible free volume.

It never means that its boundary is a physical surface.

`FULL = NOT SEE_THROUGH` is only the conservative complement of proven free space.

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

It remains the only coarse positive surface authority.

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

A missing block means `ALL_FULL`.

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

Each loop is cut only by radical planes belonging to incident `R1⊂R2⊂R3` cube flags.

For an incident direction \(q\), substitute the loop into:

\[
q\cdot(X-C_K)=\frac{a_L}{2}(q\cdot q).
\]

This produces a fixed equation:

\[
\alpha+\beta\cos\theta+\gamma\sin\theta=0.
\]

Its exact algebraic roots partition the loop into half-open sectors.

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

## 10.2 Parent prediction on the exact child loop

For every new R2 child node `c` generated by section 8.5, codegen already knows its exact:

```text
child J
lineClass
canonical E1/E2
sector
orientation sign
```

The current parent evaluator `E_L` consists only of:

```text
M8 R1 carrier
+ already committed ancestor R2_PHASE records
+ already committed metric V ancestors where applicable
```

`E_L` is restricted to the exact child loop using the same `ABC -> analytic root` equations as every other loop. This gives the predicted certain root interval:

\[
U_c^{pred}.
\]

A new direct observation gives:

\[
U_c^{obs}.
\]

No interpolation or fitted child plane is introduced.

## 10.3 Analysis

If both intervals resolve to the same generated sector/rootSign, the child R2 innovation is:

\[
\boxed{r_c=\tau(U_c^{pred},U_c^{obs})}.
\]

Interval result:

```text
r_c contains only zero
    no persistent detail

r_c is CERTAIN, sector-preserving and excludes zero
    persist one R2_PHASE interval

pred/obs disjoint in the same symbolic relation
    IMPOSSIBLE at this level

sector/root ambiguity or tau singularity
    AMBIGUOUS -> refine/re-observe
```

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

Dual never creates `A(Σ)`. It only eliminates candidates or narrows their allowed dyadic sectors.

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

For one unsplit RGB parent with seven generated skin-child footprints:

```text
all seven complete-footprint RGB measurements are CERTAIN
AND at least one child pair is RgbDistinct
    -> publish the parent RGB split and all seven actual child RGB intervals

any required child measurement is AMBIGUOUS
    -> do not publish; request genuinely new evidence

all seven are CERTAIN but no child pair is provably distinct
    -> retain the uniform parent RGB signal
```

V uses the identical finite decision over scalar metric-innovation intervals
relative to the already committed parent V signal:

```text
all seven complete-footprint V innovation intervals are CERTAIN
AND at least one child pair is Disjoint
    -> publish the parent V split and all seven additive child innovations

any required child interval is AMBIGUOUS
    -> do not publish; request genuinely new evidence

all seven are CERTAIN but no child pair is provably distinct
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
   canonical footprint of that Flower-7 child.

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

Readout pages contain compact active L2 carrier symbols and derived compact
skin samples, never persistent vertices, indices or L3/L4/L5 geometry.

## 24.1 Symbol record

One active L2 carrier wedge remains 16 bytes:

```c
struct FlowerSymbolRecord
{
    uint OwnerAndPetal;
    uint TopologyAndSector;
    uint DetailRef;          // FlowerSkinMetricRun or invalid
    uint ThreadRef;          // ThreadRun or invalid
}
```

`OwnerAndPetal` packs:

```text
kernelLocal       9 bits
petalClass        6 bits
validShellMask    3 bits
COMPLETED         1 bit
HINGE             1 bit
directFreeSide    1 bit
remaining        11 bits    // MUST be zero
```

`TopologyAndSector` packs:

```text
sector            5 bits
rootSign          1 bit
L2 wedge          3 bits
wedgeOrientation  1 bit
remaining        22 bits    // MUST be zero
```

There is no geometry-depth or valid-depth field and no dynamic branch rank.
Only active canonical or uniquely COMPLETED surface symbols receive records.

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

Every drawable symbol evaluates to L2 raster geometry. Missing direct L1/L2
innovation means the exact certain parent evaluator is repeatedly restricted
to L2; it does not authorize invented child geometry.

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

Immutable vertex-ID tables contain only the two geometric substitutions needed
to evaluate L2 carrier wedges from section 8.5. They do **not** store world
geometry. They tell the vertex shader which generated L2 knot/strand sample to
evaluate. L3/L4/L5 never appear in a vertex-ID table.

Vertex shader input:

```text
FlowerSymbolRecord
SV_VertexID
generated incidence/child tables
M8 / FlowerDetail
```

Output:

```text
evaluated L2 Flower position
canonical L2 wedge barycentrics
flat Thread/FlowerDetail references
runtime-evaluated L2 frame
```

Triangles emitted to the rasterizer are presentation tessellation of the
evaluated L2 Flower carrier; they are never canonical topology. The fragment
always performs the three generated ordered-barycentric descents, follows the
scan-authored union mask, loads one compact RGB/A3/A4/A5 sample and evaluates
the analytic micro-normal.

## 24.7 Draw

Visible page commands are submitted with:

```text
one vkCmdDrawIndirectCount
```

Each visible page contributes one 16-byte `VkDrawIndirectCommand`.

Maximum command memory:

```text
32768 pages × 16 B = 512 KiB
```

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

Both exporters consume the same CPU Sphere–Flower authority and generated tables as live readout.

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

## 27.4 Confirmed/completed policy

Export includes:

```text
CONFIRMED petals
unique COMPLETED petals
```

Completed petals carry a metadata flag.

They disappear on a later dual veto.

## 27.5 Color

```text
valid ThreadRun for the owner epoch
    evaluate the same fixed three-descents + scan-authored RGB signal

otherwise
    export canonical owner M8 PackedColor
```

No RGB inpainting is performed.

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

Materialized geometry is the L2 carrier only. These files remain presentation
products and can never be loaded as canonical M8, FlowerDetail or ThreadAtlas
truth.

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
rootSign stability under d -> -d
NO observation-dependent branch renumbering
parallel-sheet separation through distinct overlap-owner relations
R2 prediction uses exact generated child loop
R2 analysis -> synthesis reproduces the observed child root interval
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
union of chambers mapped to each child equals its exact canonical Flower-7 footprint at all three recursions
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
ambiguous large hole
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

No surface is emitted from an unresolved fixture.

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
unique\ direct+dual\ completion\ only\\
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
compact\ procedural\ L2\ sphere-resident\ draw
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
SEE_THROUGH only vetoes impossible matter.
A hole is completed only by one unique finite Flower symbol.
Ambiguity remains unresolved.
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
CONTRACT_BYTES=86895
CONTRACT_SHA256=a49c511126750cd46cde2fa06f12d27410c94a3f47c50c58114316261fae9c0b
WORKTREE=/mnt/aidisk/prace/uniscan
BRANCH=refactor/m8-dual-sphere-flower-rev-b
CURRENT_COMMIT=HEAD (CUT 3 commit parent: d5551bfba0012c2e72a7071f38d023909bcb23c9)
CURRENT_CUT=CUT_03_IMPLEMENTED_VALIDATION_DEFERRED
DAG_REVISION=COMPACT_FOUR_RUNS_2026_09_06
DAG_AUDIT=PASS
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
H02 KernelState remains 16 B and the only canonical coarse positive surface authority.
H03 SEE_THROUGH is sparse persistent negative-volume evidence only; implicit FULL is never a surface oracle.
H04 FlowerDetail and ThreadAtlas are subordinate persistent truth under an M8 FlowerAddress and sparse parent epoch.
H05 J_L(K,d)=2*K_L+d=J_L(K+d,-d), including negative coordinates and geometric L0..L2.
H06 R1=core, R2=native shape refinement, R3=branch/corner/chirality closure; shell is neither LOD nor confidence.
H07 No branch ranks, nearest-root matching, normal-angle ownership, generic fitting, or magic epsilon.
H08 ABC/root/SEAL/BEND/R2/R3 use outward-rounded CERTAIN/IMPOSSIBLE/AMBIGUOUS interval algebra.
H09 Runtime topology is the generated finite 26-node/72-strand/48-petal alphabet; no adjacency/intersection search.
H10 Geometric child substitution stops at L2 and preserves parent knot identity and boundary orientation exactly.
H11 Completion is finite candidate intersection with exactly one candidate; completed petals never recursively complete.
H12 THROUGH requires conservative full projected-support proof; AMBIGUOUS performs no destructive write.
H13 Seeds do not draw or own fine state; stable R1 requires finite incidence or a compatible later observation.
H14 Reduction is touched-only, deterministic, with exactly one commit workgroup per touched tile.
H15 L0..L2 are geometry; L3..L5 are always-existing planar Flower-7 skin addresses and can never emit geometry, silhouette or depth.
H16 RGB cannot create V; RGB is deepest explicit captured radiance while V is additive nested metric innovation with exact boundary value/derivative preservation.
H17 Captured RGB remains captured radiance; no invented albedo or ambient field.
H18 Readout is compact procedural L2 symbols plus immutable generated topology, always three skin descents and no validDepth/LOD authority.
H19 Head rotation causes zero residency, SSD, topology, page, or Flower rebuild work.
H20 SAVE is dirty append plus atomic manifest; OPEN rejects tails/orphans and resumes SCAN independently of DRAW/WARM.
H21 Live, GLB and 3D Tiles use the same L0-L2 evaluator, fixed-thread mapping and RGB/V signal; export mesh is presentation only.
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
INVARIANTS=H02-H17,H20,H22-H27; R1 jediný vlastní occupancy; R2/R3 jej nemažou; dual pouze veto; skin nemění L2 geometrii
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
NEW_AUTHORITY=derived L2 symbol pages a RGB/V union samples; 64 MiB generational arena; jedna serialized-queue publication; SCAN/DRAW/WARM
LEGACY_REMOVED=oba legacy readout pipelines; MeshReadout; vertex/index/Mesh FRONT/BACK rezervace; octahedron/tip output; camera-dependent rebuild; giant logical sphere stencil a loaded-DRAW gate pro SCAN
INVARIANTS=H09-H11,H15-H19,H21-H27; pouze L2 raster geometry; thread order; runtime L2 frame; vždy tři sestupy a jeden aligned hot sample
CPU_PROOFS=§29 page/arena/ownership/winding, exact chamber footprints, 399/343 bijections, thread ranks, RGB/V union, additive V/normal moments a publication
GPU_TESTS=CPU/HLSL vertex/signal parity; page failure retains FRONT; epoch validity; single queue; rotation mění jen cull commands
SCENE_FIXTURES=uniform/partial/full skin se stejnou geometrií; hinge; completed/veto; RGB/V union; head rotation, translation, COLD/WARM a arena capacity
PERF_CHECKS=odstranit ~480 MiB rezervaci; arena 64 MiB; commands <=512 KiB; jeden vkCmdDrawIndirectCount; žádná L3-L5 geometrie, runtime Fibonacci ani per-frame rebuild
ACCEPTANCE=renderer používá jen společný L2 evaluator a scan-authored signal; readout nemůže vytvářet detail, měnit subdivision nebo emitovat alternativní surface
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
CPU_PROOFS=§26 crash/generation/epoch replay a §27 live/export knot+RGBV parity, ownership, winding, RTC a deterministic output
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
