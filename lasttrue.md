# M8 DUAL SPHERE–FLOWER

## CLOSED PRODUCTION ALGORITHMIC CONTRACT

### Mandatory base

```text
c34d27f0ecb51500b12209ed5d2fe72b893726f5
```

This contract replaces every previous Sphere–Flower draft.

**REV-B closure:** this revision removes observation-dependent branch ranks, replaces the invented R2 half-phase split with exact child-loop residual synthesis, derives R3 chirality from generated incidence, makes parent fine-state invalidation explicit, certifies THROUGH over the full projected support, and permits resolved geometry through L5.

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

One fine metric record remains 16 bytes:

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
bits  0..2   level              3   // L0..L5
bits  3..12  childPath          10  // 2 bits × L1..L5
bits 13..18  petalClass         6   // 0..47
bits 19..22  channel            4   // 13 line classes / local channel
bits 23..25  kind               3
bit      26  rootSign           1
bits 27..31  sector             5   // generated fixed loop sector
```

Codegen MUST prove `maxSectorCount <= 32` for every line class. If this proof fails, the ABI build fails; sectors are never truncated or aliased.

Kinds:

```text
R2_PHASE
R3_PHASE
V_AMPLITUDE
KNOT_METRIC
TOMBSTONE
```

`Lower/Upper` are fixed-point interval endpoints:

- phase values use signed Q2.29 tangent-half-angle within one generated sector;
- metric V uses signed Q5.26 metres relative to its parent segment;
- no single best float is persisted.

Render value is the deterministic interval midpoint:

\[
x_{\rm draw}=x_-+\left\lfloor\frac{x_+-x_-}{2}\right\rfloor.
\]

A record is valid iff its `ParentEpoch` equals the current sparse owner epoch. Parent structural invalidation therefore removes every descendant logically without walking the descendant records.

## 1.4 ThreadAtlas

ThreadAtlas is persistent appearance truth under the same parent-owner epoch. It is not a cache and not another spatial world.

```c
struct ThreadRun
{
    uint FlowerKey;
    uint ProgramRef;
    uint ResidualBase;
    uint ParentEpoch;
}
```

```c
struct ThreadResidual
{
    uint  SegmentKey;
    uint  ParentEpoch;
    half4 LowerLinearRgba;
    half4 UpperLinearRgba;
    uint  Flags;
    uint  Reserved;
}
```

Thread programs contain only reusable:

```text
endpoint color rule
RGB residual basis
optional certified optical response
Fibonacci route origin
```

No Thread record contains topology or world XYZ. A Thread record whose parent epoch is stale is ignored identically to stale FlowerDetail.

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

The canonical geometry of a level is the evaluated nested Flower knots/strands. Presentation rasterization may connect evaluated child knots into triangles, but those triangles are disposable output and never define the scan truth.

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
    FlowerDetail/Thread update

FinalizeObservation
```

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

# 20. L1–L5 authority and Fibonacci routing

## 20.1 Levels

```text
L0 25.00000 mm
L1 12.50000 mm
L2  6.25000 mm
L3  3.12500 mm
L4  1.56250 mm
L5  0.78125 mm
```

Fine detail cannot exist without a valid parent R1 FlowerAddress.

## 20.2 Invalidation

```text
parent R1 structural anchor changes
    increment sparse FlowerOwnerEpoch
    stale ParentEpoch descendants disappear logically

compatible parent metric refinement inside same sector/rootSign
    keep OwnerEpoch; descendants are re-evaluated relative to refined parent

parent R1 deleted / explicit ERASE
    increment epoch and remove parent; all descendants become stale

R2 invalidated
    ignore only R2-dependent child metric

R3 invalidated
    ignore only cross-junction/chirality-dependent detail
```

Cleanup is deferred log compaction, never an immediate subtree traversal.

## 20.3 Exact Fibonacci word

Define:

```text
W0 = "0"
W1 = "01"
Wn = Wn-1 || Wn-2
```

Lengths are precomputed Fibonacci integers.

`FibBit(n)`:

1. choose the smallest stored \(k\) with \(|W_k|>n\);
2. while \(k>1\):

```text
n < |Wk-1|:
    k = k-1
else:
    n = n-|Wk-1|
    k = k-2
```

3. return the indexed bit from `W0` or `W1`.

Only integer compares/subtracts are used.

This runs during observation/page compaction, not per fragment.

## 20.4 Two permitted Fibonacci uses

Acquisition:

```text
geometry supplies finite equivalent child routes
FibBit(routeOrigin + observationOrdinal)
selects which additional valid route is measured
```

Appearance:

```text
geometry supplies finite equivalent strand continuations
FibBit(routeOrigin + segmentOrdinal)
selects continuation
```

Fibonacci never makes geometry valid and never chooses between incompatible petal/junction classes.

---

# 21. Closed-loop RGBV

Every fine loop is divided by generated Flower knots into canonical half-open segments.

For segment coordinate:

\[
s\in[0,1].
\]

Use the fixed endpoint-preserving basis:

\[
b(s)=16s^2(1-s)^2.
\]

Properties:

\[
b(0)=b(1)=0
\]

\[
b'(0)=b'(1)=0
\]

\[
b(1/2)=1.
\]

## 21.1 RGB

With shared endpoint colors \(C_0,C_1\):

\[
C(s)=(1-s)C_0+sC_1+b(s)\Delta C.
\]

`ΔC` is stored only when its interval excludes zero.

Because loop endpoint colors are shared KnotAddress values, a closed loop has no seam.

RGB cannot modify V.

## 21.2 Unique completion color

For a COMPLETED petal:

```text
unique ThreadAddress + phase continuation
    continue existing RGB residual

otherwise
    use canonical owner M8 PackedColor
    no fine RGB residual
```

No texture detail is invented.

---

# 22. Metric V

## 22.1 Terminology

Constant \(v\):

```text
ordinary conjugate sphere breathing
```

Variable \(v(\theta)\):

```text
conjugate deformed Flower loop
```

It is not claimed to be the intersection of two constant-radius spheres.

## 22.2 Basis

Every canonical segment stores one interval amplitude \(A_V\):

\[
v(s)=A_V\,b(s).
\]

Nested levels add detail only inside their own child segments.

At every parent knot:

\[
v=0,\qquad v'=0.
\]

Parent knot position and tangent therefore remain invariant.

## 22.3 Geometry

\[
e(\theta)=E_1\cos\theta+E_2\sin\theta
\]

\[
e_\perp(\theta)=-E_1\sin\theta+E_2\cos\theta.
\]

\[
\rho_v=
\sqrt{\frac34R^2-3v^2}.
\]

\[
X_v=
M+2v\hat r+\rho_v e.
\]

\[
\rho_v'=-\frac{3vv'}{\rho_v}.
\]

\[
T=
2v'\hat r+\rho_v'e+\rho_ve_\perp.
\]

## 22.4 Representability

Required interval invariant:

\[
|v|_{\max}<R/2.
\]

Then:

\[
\rho_v>0
\]

and:

\[
|T|^2=
4(v')^2+(\rho_v')^2+\rho_v^2>0.
\]

Because projection into the loop plane has positive polar radius \(\rho_v\), angular ordering is injective and cannot locally self-cross.

Root order is therefore preserved.

If the complete V interval violates the bound:

- do not clamp;
- reject the child record;
- represent the residual at the first coarser level whose bound is satisfied;
- otherwise return `UNRESOLVED`.

V is admitted only from metric stereo/multiview residual whose interval excludes zero.

---

# 23. Micro-normal and optical synthesis

## 23.1 Actual normal

At a petal knot, generated incidence supplies two independent analytic/nested strand tangents:

\[
T_a,T_b.
\]

\[
N_\mu=
\frac{T_a\times T_b}{|T_a\times T_b|}.
\]

If the lower interval bound of `|Ta × Tb|²` is zero:

```text
AMBIGUOUS
use the parent normal for presentation only
request refinement
```

No angular tolerance exists.

## 23.2 Resolved and unresolved microgeometry

L4/L5 are not intrinsically shading-only levels.

If their conservative projected geometric deviation is resolvable, the procedural draw emits their actual child geometry. If it is subpixel, the same generated child normals are reduced to moments instead of being discarded.

For the six generated local child-petal normal directions:

\[
N_0,\ldots,N_5
\]

with exact projected-area weights `w_i`:

\[
\bar N=\frac{\sum_iw_iN_i}{\sum_iw_i}
\]

\[
M_N=\frac{\sum_iw_iN_iN_i^T}{\sum_iw_i}
\]

\[
C_N=M_N-\bar N\bar N^T.
\]

The two tangent-plane eigenvalues of `C_N` are deterministic unresolved directional spread/anisotropy descriptors. No normal map or fitted Gaussian geometry is stored.

## 23.3 Captured-radiance invariant

The scanned RGB channel is treated as captured linear radiance, **not as known diffuse albedo**. Therefore the production renderer MUST NOT invent an ambient irradiance value or fully relight the captured diffuse component.

Base output is exactly:

\[
\boxed{C_{base}=C_{capture}}.
\]

Resolved V geometry still changes real silhouette, parallax, depth, self-occlusion and any actual presentation-light interaction through its evaluated geometry.

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

## 23.4 Specular / view-dependent appearance

A ThreadProgram may contain a view-dependent optical interval only when repeated multiview RGB observations make that interval `CERTAIN`. If not certified:

```text
OPTICAL_VALID = 0
specular correction = 0
```

When certified, the renderer evaluates the optical response over the **actual six Flower normals / tangent directions**, area-weighted by `w_i`. The unresolved normal moments may choose the directional width of the presentation lobe, but they never replace the six physical directions as geometry authority.

Required ThreadProgram optical fields when valid:

```text
F0 interval
roughness-floor interval
capture/view residual interval
OPTICAL_VALID
```

The presentation BRDF is isolated from reconstruction: changing its closed shader formula cannot move a knot, create a petal, alter V, change occupancy, or affect dual/hole logic.

---

# 24. Procedural readout ABI

Readout pages contain compact Flower symbols, never persistent vertices or indices.

## 24.1 Symbol record

One active petal instance remains 16 bytes:

```c
struct FlowerSymbolRecord
{
    uint OwnerAndPetal;
    uint TopologyAndSector;
    uint DetailRef;
    uint ThreadRef;
}
```

`OwnerAndPetal` packs:

```text
kernelLocal       9 bits
petalClass        6 bits
geometryDepth     3 bits  // L0..L5 may be emitted
validShellMask    3 bits
sector            5 bits
rootSign          1 bit
COMPLETED         1 bit
HINGE             1 bit
remaining         3 bits
```

No dynamic branch rank exists. Only active surface symbols receive records.

## 24.2 Page header

```c
struct FlowerPageHeader
{
    int3  LogicalTile;
    uint  Generation;
    uint  FirstByDepth[6];
    uint  CountByDepth[6];
}
```

Depth bins correspond exactly to emitted geometry depth `L0..L5`.

## 24.3 Geometry-depth selection

Geometry validity is independent of presentation depth. A valid L4/L5 detail is never discarded merely because it is fine.

For each page/symbol, evaluate a conservative screen-space deviation interval between level `g` and its certain child level `g+1`.

```text
child detail CERTAIN and projected deviation >= 0.5 pixel
    emit child geometry

child may change silhouette/depth ordering
    emit child geometry regardless of area

child detail CERTAIN but strictly subpixel and no silhouette/depth change
    keep child as analytic normal/optical moments

child AMBIGUOUS
    never invent geometry; draw deepest certain parent
```

`geometryDepth` may therefore be any `0..5`.

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

For geometry depth `g=0..5`, immutable vertex-ID tables contain repeated child substitution from section 8.5.

The table does **not** store world geometry. It tells the vertex shader which generated child knot/strand sample to evaluate.

Vertex shader input:

```text
FlowerSymbolRecord
SV_VertexID
generated incidence/child tables
M8 / FlowerDetail
```

Output:

```text
evaluated nested Flower position
linear captured RGBV/thread coordinate
actual normal/tangent inputs
```

Triangles emitted to the rasterizer are presentation tessellation of the evaluated Flower cell; they are never canonical topology.

## 24.7 Draw

For each of six emitted geometry depths:

```text
one vkCmdDrawIndirectCount
```

Each visible page contributes one 16-byte `VkDrawIndirectCommand` per nonempty depth bin.

Maximum command memory:

```text
32768 pages × 6 depths × 16 B = 3 MiB
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
(level, J, lineClass, sector, rootSign)
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
unique Thread continuation
    export ThreadAtlas result

otherwise
    export canonical owner M8 PackedColor
```

No RGB inpainting is performed.

## 27.6 Position encoding

The canonical evaluator performs exactly one round-to-nearest binary32 conversion in grid-local coordinates.

3D Tiles use a tile RTC origin. Shared world positions are evaluated before subtracting RTC origin, preserving identical canonical binary32 world values.

## 27.7 Presentation materialization

GLB/3D Tiles may materialize:

- vertices;
- indices;
- baked base color;
- baked normals/roughness.

These files remain presentation products and can never be loaded as canonical M8 truth.

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
hinge shared position with distinct normals
sparse FlowerOwnerEpoch invalidates descendants exactly
compatible parent metric refinement does not spuriously invalidate descendants
dual uniform-node collapse/expand round trip
common-prefix THROUGH fast path has zero false positives
dyadic-cover THROUGH slow path has zero false positives
completed petal count 0/1/>1
completed petals cannot recursively seed completion
parent deletion invalidates every descendant
L4/L5 resolved geometry can be emitted
L4/L5 subpixel collapse preserves normal moments
single-native-queue page publication
CPU/HLSL symbol parity
export/live knot parity
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
L0-L5 parent invariance
L4/L5 close-up resolved detail
L4/L5 far subpixel reduction
V representable limit
V promotion
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
dyadic\ L0-L5\ FlowerDetail\\
\downarrow\\
Fibonacci\ route\ ordering\\
\downarrow\\
closed-loop\ RGBV\\
\downarrow\\
conjugate\ deformed\ V\\
\downarrow\\
actual\ tangents/normals/optical\ moments\\
\downarrow\\
compact\ procedural\ sphere-resident\ draw
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
ThreadAtlas persists RGBV novelty.
Renderer and exporters synthesize the same authority.
```

Repository state was not changed. No commit or patch was created.

---

# REV-B IMPLEMENTATION CONTROL LEDGER

This ledger is mutable implementation state. Everything above this separator is the immutable REV-B contract and MUST remain byte-for-byte identical to `M8-DUAL-SPHERE-FLOWER-CLOSED-PRODUCTION-CONTRACT-REV-B.md`.

## Authority identity

```text
REPOSITORY=fladirm/QuestInfiniteScan
MANDATORY_BASE=c34d27f0ecb51500b12209ed5d2fe72b893726f5
CONTRACT_FILE=M8-DUAL-SPHERE-FLOWER-CLOSED-PRODUCTION-CONTRACT-REV-B.md
CONTRACT_BYTES=64680
CONTRACT_SHA256=75f67ad9080fcbd999ba9ee7f0e30312201cc6dc671112700f403ddd4a309012
WORKTREE=/mnt/aidisk/prace/uniscan
BRANCH=refactor/m8-dual-sphere-flower-rev-b
CURRENT_COMMIT=HEAD (resolve with git rev-parse; a commit cannot contain its own SHA)
CURRENT_CUT=CUT_00_PASS_NEXT_CUT_01
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
H05 J_L(K,d)=2*K_L+d=J_L(K+d,-d), including negative coordinates and L0..L5.
H06 R1=core, R2=native shape refinement, R3=branch/corner/chirality closure; shell is neither LOD nor confidence.
H07 No branch ranks, nearest-root matching, normal-angle ownership, generic fitting, or magic epsilon.
H08 ABC/root/SEAL/BEND/R2/R3 use outward-rounded CERTAIN/IMPOSSIBLE/AMBIGUOUS interval algebra.
H09 Runtime topology is the generated finite 26-node/72-strand/48-petal alphabet; no adjacency/intersection search.
H10 Child substitution preserves parent knot identity and boundary orientation exactly.
H11 Completion is finite candidate intersection with exactly one candidate; completed petals never recursively complete.
H12 THROUGH requires conservative full projected-support proof; AMBIGUOUS performs no destructive write.
H13 Seeds do not draw or own fine state; stable R1 requires finite incidence or a compatible later observation.
H14 Reduction is touched-only, deterministic, with exactly one commit workgroup per touched tile.
H15 L1..L5 are real metric hierarchy; resolved L4/L5 emit geometry and only proven subpixel detail becomes moments.
H16 RGB cannot create V; V is metric-only, endpoint/tangent preserving, bounded without clamp/self-cross.
H17 Captured RGB remains captured radiance; no invented albedo or ambient field.
H18 Readout is compact procedural Flower symbols plus immutable generated topology, never canonical/page mesh truth.
H19 Head rotation causes zero residency, SSD, topology, page, or Flower rebuild work.
H20 SAVE is dirty append plus atomic manifest; OPEN rejects tails/orphans and resumes SCAN independently of DRAW/WARM.
H21 Live, GLB and 3D Tiles use the same evaluator/KnotAddress; export mesh is presentation only.
H22 One serialized native Vulkan queue; scanner/FINE/ERASE outrank bounded page compaction.
H23 Superseded authority is removed in its replacement cut; no fallback, alias, flag, or second authority remains.
H24 Insufficient information yields UNRESOLVED plus a discrete refinement reason, never inferred conventional geometry.
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

The full dependency-ordered DAG and ownership audit are populated in PHASE 2 before any production behavior change. Until then no cut may pass CUT 0.

| Cut | Name | Status | Commit |
|---:|---|---|---|
| 0 | Authority/bootstrap | PASS | `HEAD: cut 00: authority bootstrap — REV-B closure control` |
| 1 | CPU exact oracle + codegen | PENDING | — |
| 2 | ABI/data model | PENDING | — |
| 3 | Deterministic observation reduction | PENDING | — |
| 4 | Stereo Flower support | PENDING | — |
| 5 | Direct R1 production commit | PENDING | — |
| 6 | Sparse SEE_THROUGH negative volume | PENDING | — |
| 7 | Exact R2 shape refinement | PENDING | — |
| 8 | Exact R3 closure | PENDING | — |
| 9 | Hole/ghost/refinement algebra | PENDING | — |
| 10 | L1-L5 FlowerDetail | PENDING | — |
| 11 | RGBV/V/photoreal synthesis | PENDING | — |
| 12 | Procedural readout | PENDING | — |
| 13 | Spherical residency | PENDING | — |
| 14 | Transactional persistence | PENDING | — |
| 15 | Unified GLB/3D Tiles export | PENDING | — |
| 16 | Legacy excision + ABI closure | PENDING | — |
| 17 | Full application closure | PENDING | — |

## Proof/test evidence

```text
PHASE_0 contract bytes/SHA-256: 64680 / 75f67ad9080fcbd999ba9ee7f0e30312201cc6dc671112700f403ddd4a309012
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

## CURRENT TRUE STATE

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

## PHASE 2 — dependency-ordered implementation DAG

The numeric labels preserve the requested contract partition. Execution is topological. Every temporary coexistence named below is bounded by an explicit deletion cut; none is a final-state feature flag or fallback.

### CUT 0 — authority/bootstrap

```text
ID=CUT_00
NAME=Authority bootstrap and immutable REV-B control
DEPENDS_ON=mandatory base only
FILES_TOUCHED=AGENTS.md; contr.md; REV-B contract; lasttrue.md; Runtime/Merkaba/MerkabaSphereFlowerAuthority.cs(.meta); Editor/MerkabaSphereFlowerCodegen.cs(.meta); Runtime/Shaders/MerkabaSphereFlower.generated.hlsl(.meta)
NEW_AUTHORITY=REV-B immutable contract prefix; ledger; compile-time Sphere-Flower namespace/constants/table schema with no production dispatch
LEGACY_REMOVED=contr.md as competing normative authority (replaced by an unambiguous pointer); no production behavior
INVARIANTS=H01,H05,H06,H23; contract prefix stays byte-identical; constants a=25mm,L0..L5,13/26/72/48 capacities frozen
CPU_PROOFS=contract byte count/SHA; exact HEAD ancestry; assembly compile of empty authority surface; table-schema uniqueness
GPU_TESTS=generated HLSL include parses but is not bound by production
SCENE_FIXTURES=none; behavior checksum of existing tests must be unchanged
PERF_CHECKS=no new buffer, dispatch, allocation or runtime call site
ACCEPTANCE=authority chain has one normative target; lasttrue contains full forensic map/DAG; source behavior unchanged
ROLLBACK_BOUNDARY=single documentation/skeleton commit
```

### CUT 1 — CPU exact oracle + codegen

```text
ID=CUT_01
NAME=Exact Sphere-Flower CPU oracle and generated finite alphabet
DEPENDS_ON=CUT_00
FILES_TOUCHED=MerkabaSphereFlowerAuthority.cs; MerkabaSphereFlowerCodegen.cs; MerkabaSphereFlower.generated.hlsl; new oracle/parity tests; shader audit tooling; generated .meta files
NEW_AUTHORITY=single exact CPU evaluator plus generated HLSL tables for integer incidence, interval ABC/root algebra, 26 nodes/72 strands/48 petals, R2/R3/V
LEGACY_REMOVED=none from production before gate; old geometry generator is explicitly deferred to CUT_12/CUT_16 because current scanner/readout still requires it
INVARIANTS=H05-H10,H15-H17,H24; no runtime adjacency/root search; no dynamic branch rank; no epsilon
CPU_PROOFS=J and negative coordinates; 13 lines; endpoint uniqueness; loop basis; symbolic sector partition; all root degeneracies; outward intervals; root transform; 26/72/48 counts/incidence/winding; child substitution; R2 predict/analyse/synthesise/closure; eight R3 determinant frames/chirality; hinge; parallel owners; V basis/bound/order/noncrossing
GPU_TESTS=bit-identical table hashes; CPU/HLSL evaluations across exhaustive finite tables and adversarial interval vectors; SPIR-V validation with precise math flags
SCENE_FIXTURES=analytic flat/translated/arbitrary quantized planes; bends/corners/hinges/parallel sheets; all negative/tile/chunk/block boundary coordinates; L0-L5 and V edges
PERF_CHECKS=generated immutable tables <64KiB target; no heap allocation in evaluator hot methods; fixed operation-count report per primitive
ACCEPTANCE=every CPU_ORACLE_GATE item relevant to pure algebra PASS; generated artifacts reproducible byte-for-byte; no heuristic fallback
ROLLBACK_BOUNDARY=oracle/codegen commit; production remains base behavior until later cutover
```

### CUT 2 — ABI/data model

```text
ID=CUT_02
NAME=Exact subordinate data models and persistence record ABI
DEPENDS_ON=CUT_01
FILES_TOUCHED=KernelState.cs; MerkabaConstants.cs; new MerkabaDualVisibility.cs; new MerkabaFlowerDetail.cs; new MerkabaThreadAtlas.cs; new persistence record/layout source; MerkabaGrid.Gpu.cs layout declarations; managed/native ABI headers and layout tests
NEW_AUTHORITY=sparse dual node/leaf types; sparse FlowerOwnerEpoch; 16B FlowerDetailRecord; ThreadRun/ThreadResidual; fixed record headers; typed transient Flower symbol and observation records
LEGACY_REMOVED=no semantic path yet; old ABI fields are marked with last legal consumer cuts, not aliased to new meanings; physical removal occurs when each consumer is replaced
INVARIANTS=H02-H04,H06,H08,H13,H15-H18,H20,H22; exact byte offsets/strides; no second coordinate hierarchy; no orphan fine state
CPU_PROOFS=Marshal/Unsafe size and offset checks; key round trips including max sector; epoch wrap/rebase; stale-descendant rejection; dual node encode/decode; endian/version fixtures; parent-owner ownership
GPU_TESTS=C#/HLSL/native struct reflection parity; buffer stride/resource range tests; no binding is exposed before a real consumer exists
SCENE_FIXTURES=parent compatible refinement, structural sector change, deletion with stale descendants, negative owner addresses
PERF_CHECKS=KernelState remains 16B; mixed leaf=64B; detail=16B; no per-kernel epoch allocation; allocation upper-bound report
ACCEPTANCE=all persistent/transient structures have one owner, exact packing and failure semantics; production output unchanged
ROLLBACK_BOUNDARY=data-model/ABI-schema commit
```

### CUT 3 — deterministic observation reduction

```text
ID=CUT_03
NAME=Touched-tile observation binning and deterministic one-WG reduction
DEPENDS_ON=CUT_02
FILES_TOUCHED=MerkabaIntegrator.cs; MerkabaGrid.Gpu.cs; MerkabaIntegration.compute; native executor/generator resource declarations; new reduction tests
NEW_AUTHORITY=CountObservationBins→bounded HOT prefix reserve→EmitObservationBins→one FlowerCommit WG per stamped tile; interval intersection precedes representative selection
LEGACY_REMOVED=world clearing is removed where no longer used; SurfaceCandidates capacity is retyped as ObservationRecords; SurfaceWinnerRanks/SurfaceQueue remain explicitly deferred only until CUT_05 switches canonical R1 commit
INVARIANTS=H05,H07,H08,H14,H22,H24; max 8 owners/pixel; max 2,097,152×16B; canonical source selection cannot resolve incompatible symbols
CPU_PROOFS=owner arithmetic/half-open supports; deterministic bin/reference output under every input permutation; prefix bounds; duplicate-compatible interval intersection; incompatible bucket -> UNRESOLVED
GPU_TESTS=CPU/GPU record parity; exactly one commit group/stamped tile; count/reserve/emit overflow fixtures; slot-generation/ABA halo validation; negative and hierarchy boundaries
SCENE_FIXTURES=flat wall under shuffled pixels; overlapping owners; thin parallel sheets; maximum 512² frame; missing/cold tile allocation retry
PERF_CHECKS=no O(world) clear; transient <=32MiB plus 512KiB tile metadata; zero per-record hash after root-tile resolution; dispatch/timing baseline
ACCEPTANCE=deterministic reducer proven and callable by CUT_05; no new geometry decision is hidden in allocation stages
ROLLBACK_BOUNDARY=reduction commit; bounded deferred winner deletion=CUT_05
```

### CUT 4 — stereo Flower support

```text
ID=CUT_04
NAME=Exact Flower-supported stereo interval evidence
DEPENDS_ON=CUT_01,CUT_03
FILES_TOUCHED=StereoRgbdRefine.compute; DepthCapture.cs; shared camera/depth includes; MerkabaObservation.cs; native shader generator; stereo/oracle tests
NEW_AUTHORITY=five bounded hypotheses validated on exact R1 loop roots, conditional R3 disambiguation and conditional R2 bend evidence
LEGACY_REMOVED=JointTangentBasis; eight square census offsets; arbitrary 12.5mm tangent/bitangent patch; PCA tangent geometry role
INVARIANTS=H05-H08,H12,H17,H24; PCA L/R remains sensor evidence only; invalid/ambiguous roots do not emit measurement
CPU_PROOFS=hypothesis-to-ABC intervals; sector/root agreement across eyes; two independent non-collinear R1 relation predicate; deterministic hypothesis tie/ambiguity
GPU_TESTS=CPU/HLSL hypothesis classification parity; immutable depth/PCA inputs; no camera-dependent branch numbering; source bounds
SCENE_FIXTURES=flat/oblique/translated planes; arbitrary quantized normals; depth discontinuity; FOV/invalid/occlusion/range edges; thin parallel sheets
PERF_CHECKS=five-hypothesis bound retained; report R1 fast path/R2/R3 activation; no per-pixel heap/readback/extra world lookup
ACCEPTANCE=all stereo fixtures either emit contract-valid interval evidence or UNRESOLVED; old square semantic absent
ROLLBACK_BOUNDARY=stereo support commit
```

### CUT 5 — direct R1 production commit

```text
ID=CUT_05
NAME=Canonical direct R1 seed/promotion/retirement and Flower commit cutover
DEPENDS_ON=CUT_03,CUT_04,CUT_14
FILES_TOUCHED=KernelState.cs; MerkabaConstants.cs; MerkabaIntegrator.cs; MerkabaIntegration.compute; MerkabaGrid.Gpu.cs; native executor/generator; evidence/GPU/lifecycle tests
NEW_AUTHORITY=strict endpoint→eight owners→typed symbols→attempt interval reduction→R1 seed or stable exact finite-incidence commit
LEGACY_REMOVED=DiscoverSurfaceCandidates; Initialize/Select/Queue winners; Prepare/IntegrateSurfaceCandidates; ClearTouchedSurfaceCandidates; SurfaceWinnerRanks0..3; SurfaceQueue; legacy candidate semantic struct; nearest/±normal owner routing; current-frame planar support; same-sheet normal classification; weighted plane fusion authority
INVARIANTS=H02,H05-H09,H13,H14,H23,H24; `NeedsCarve` meaning is gone and bit is `R1_SEED`; seed never draws/owns children; incompatible occupied owner is not overwritten
CPU_PROOFS=first-hit state truth table; same-attempt two-relation promotion; cross-generation compatible promotion; incompatible dormant replacement; stable evidence hysteresis; exact root/sector identity
GPU_TESTS=new observation path is sole positive commit; touched-only finalization; negative/boundary owners; retry at residency epoch; removed resources/pipelines unbound
SCENE_FIXTURES=isolated first hit; repeated real object; doorway/boundary; comet-tail depth; flat/oblique wall; thin/two parallel sheets
PERF_CHECKS=winner 72MiB removed; no world winner clear; per-observation timings/memory; no regression against base observation cadence
ACCEPTANCE=positive canonical surface comes only from direct R1 path; old positive authority physically absent; legacy negative CARVE is the sole explicitly deferred path and loses all `NeedsCarve` flag dependence before close
ROLLBACK_BOUNDARY=atomic positive-authority cutover commit; deferred negative path deletion=CUT_06
```

### CUT 6 — sparse SEE_THROUGH negative volume

```text
ID=CUT_06
NAME=Conservative full-support free certificate and sparse negative-volume cutover
DEPENDS_ON=CUT_04,CUT_05,CUT_14
FILES_TOUCHED=DepthCapture.cs; new DepthCertificate.compute; delete DepthDilation.compute; MerkabaDualVisibility.cs; MerkabaObservation.cs; MerkabaIntegrator.cs; MerkabaIntegration.compute; MerkabaGrid.Gpu.cs; native executor/generator; persistence replay hooks; certificate/dual tests
NEW_AUTHORITY=min-lower/all-valid depth pyramid; convex projected-support dyadic cover; sparse ALL_FULL/ALL_THROUGH/MIXED Block→Chunk→Tile leaves; endpoint precedence; endpoint/through dual projection
LEGACY_REMOVED=DepthDilation compute/resources/bindings; QueryCarveTiles; PrepareCarveArgs; IntegrateCarveTiles; CarveTiles/Args; 26-plane continuing-sheet CARVE; dilated-depth and image-morphology free authority
INVARIANTS=H03,H08,H11-H13,H20,H22-H24; missing/cold dual=AMBIGUOUS; dual never creates positive surface; complete-support proof has zero false positives
CPU_PROOFS=pyramid reduction; common-prefix superset proof; exact dyadic-cover query; projected eight-corner uncertainty enclosure; ALL states promotion/collapse; sparse record replay; endpoint exclusion/restore
GPU_TESTS=CPU/GPU certificate parity; 64-node exhaustion=>AMBIGUOUS; frustum sparse traversal; node collapse/expand; slot generation; no destructive write from invalid/FOV/range/occlusion ambiguity
SCENE_FIXTURES=FOV/invalid/occlusion/range boundaries; foreground occluder; comet tail; direct endpoint crossing; closed ghost support; negative/tile/chunk/block edges
PERF_CHECKS=certificate memory ~5.33MiB per stereo hierarchy as contracted; dual HOT <= stated bounds; report fast/slow cover nodes and R1 mutation time; no pixel→world raymarch/hash traversal
ACCEPTANCE=SEE_THROUGH persists across save/open; sole automatic free/ghost contradiction path is certificate-based; old CARVE/dilation physically absent
ROLLBACK_BOUNDARY=atomic negative-volume authority cutover commit
```

### CUT 7 — exact R2 shape refinement

```text
ID=CUT_07
NAME=Native child-loop R2 analysis/synthesis
DEPENDS_ON=CUT_05,CUT_06
FILES_TOUCHED=MerkabaSphereFlowerAuthority.cs/generated HLSL; MerkabaFlowerDetail.cs; MerkabaIntegrator.cs; MerkabaIntegration.compute; detail page GPU resources; R2 oracle/GPU tests
NEW_AUTHORITY=six face-diagonal channels; exact parent evaluator restricted to generated child loops; tangent-half-angle innovation; rational rotation synthesis; incident-child interval intersection
LEGACY_REMOVED=remaining normal-difference curvature/crease decisions; any provisional R2 placeholder from earlier cuts
INVARIANTS=H06-H10,H15,H23,H24; no Hessian/phi/Taylor/LS/PCA; inherited nodes never move; nonzero certain innovations only
CPU_PROOFS=all 48 parent classes × generated children; predicted loop identity; tau degeneracy; orientation signed permutations; analysis→synthesis containment; shared-child intersection; sector promotion to first coarser representable level
GPU_TESTS=CPU/HLSL R2 parity at every L; conditional activation masks; parent bit identity; conflict and ambiguous paths write nothing
SCENE_FIXTURES=flat zero innovation; smooth bend; mixed bends xy/xz/yz; hinge onset; parent boundary; representability promotion; negative/tile boundaries
PERF_CHECKS=R1 fast wall executes no R2 metric work; max six scalar interval innovations/parent; touched-only record allocation/timing
ACCEPTANCE=R2 modifies only subordinate FlowerDetail, produces exact certain child geometry, and cannot affect occupancy
ROLLBACK_BOUNDARY=R2 authority commit
```

### CUT 8 — exact R3 closure

```text
ID=CUT_08
NAME=Tetrahedral branch/corner/chirality closure
DEPENDS_ON=CUT_07
FILES_TOUCHED=SphereFlower authority/codegen/generated HLSL; FlowerDetail; integration shader/orchestration; R3 tests
NEW_AUTHORITY=four generated tetra frames for each of eight parity classes; q_i from oriented predicted/observed root tau; exact qs/qv transform and generated chirality candidate intersection
LEGACY_REMOVED=all remaining normal-angle branch/corner logic; any hard-coded level chirality toggle or Taylor predictor
INVARIANTS=H06-H10,H15,H23,H24; R3 never predicts R1/R2, owns occupancy, or gates unrelated L4/L5
CPU_PROOFS=eight determinant frames; signed permutations/eta; forward/inverse tetra transform; chiCell/product root signs; all candidate junction decisions; shared hinge position with branch normals
GPU_TESTS=CPU/HLSL q_i/qs/qv/chirality parity; conditional R3 only at branch/corner/hole/cross-junction; ambiguous class writes no R3 state
SCENE_FIXTURES=convex/concave/trihedral corners; chirality mirrors; doorway; two sheets; missing R3 with valid local fine sheet; junction ambiguity
PERF_CHECKS=R3 activation ratio and fixed four-channel instruction bound; zero work on ordinary R1-only wall
ACCEPTANCE=one finite generated class or UNRESOLVED; no threshold/score/predictor; R1 survives every R3 failure
ROLLBACK_BOUNDARY=R3 authority commit
```

### CUT 9 — hole/ghost/refinement algebra

```text
ID=CUT_09
NAME=Finite direct/dual completion, veto and discrete refinement reasons
DEPENDS_ON=CUT_06,CUT_07,CUT_08
FILES_TOUCHED=SphereFlower authority/generated incidence; DualVisibility; Integrator/integration shader; symbol page inputs; hole/ghost scene tests
NEW_AUTHORITY=fixed signed 72×48 incidence operations; candidate bit intersections; exact 0/1/>1 completion; full-interval dual veto; discrete refinement reason mask
LEGACY_REMOVED=all scan-time generic hole/ghost heuristics, confidence/refinement scores and any inferred patch logic in production scanner
INVARIANTS=H03,H08-H12,H15,H23,H24; dual only removes/narrows; completed cannot seed completion; no extrapolated knot; partial THROUGH overlap=AMBIGUOUS
CPU_PROOFS=B·x boundary signs; every one-petal removal/restoration; 0/1/>1 candidate enumeration; generation-local nonrecursion; direct precedence; complete support contradiction; reason-bit determinism
GPU_TESTS=CPU/HLSL mask parity; completed flag transient/page-only; immediate dual veto; no canonical M8 write from completion; bounded per-parent operations
SCENE_FIXTURES=unique one-petal hole; large ambiguous hole; real doorway/frontier; closed ghost in THROUGH; partial dual overlap; direct later confirmation; through later invalidation
PERF_CHECKS=fixed masks/add-sub/popcount only; no traversal/allocation/fit; completion/refinement activation profile
ACCEPTANCE=surface completion occurs only for exactly one certain generated symbol; every other case remains VETO/UNRESOLVED/REFINE
ROLLBACK_BOUNDARY=direct/dual algebra commit
```

### CUT 10 — L1–L5 FlowerDetail

```text
ID=CUT_10
NAME=Persistent dyadic metric detail and exact parent invalidation
DEPENDS_ON=CUT_07,CUT_08,CUT_09,CUT_14
FILES_TOUCHED=MerkabaFlowerDetail.cs; SphereFlower evaluator/generated HLSL; Integrator/shader; Grid GPU/storage; persistence records/replay; FINE integration; detail tests
NEW_AUTHORITY=sparse per-owner epochs; L1-L5 R2/R3/V/Knot interval records; exact child paths; generation invalidation; integer Fibonacci acquisition ordering
LEGACY_REMOVED=any fine geometry encoded as legacy carrier fusion/readout subdivision; FINE mutations that touch only coarse M8 without subordinate-detail semantics
INVARIANTS=H04,H06,H08-H10,H15,H16,H20,H22-H24; no orphan detail; compatible parent refinement retains epoch; structural change invalidates logically without walk
CPU_PROOFS=key packing; all child paths/levels; epoch wrap rebase; parent delete; R2/R3-dependent selective invalidation; exact FibWord bits/random access; save/open detail identity
GPU_TESTS=detail page allocation/update/tombstone; stale epoch rejection; CPU/HLSL child evaluator; FINE priority; resolved actual L4/L5 position emission inputs
SCENE_FIXTURES=L0-L5 hierarchy; close-up L4/L5; parent compatible refine; sector/root/free-side change; delete/stale descendants; FINE refine across hierarchy boundaries
PERF_CHECKS=sparse records only for excluding-zero innovations; no subtree walk; O(dirty) upload/log; O(1) Fibonacci route per selected measurement
ACCEPTANCE=persistent fine metric truth survives OPEN and is solely addressed beneath valid R1 owners; L4/L5 are genuine geometry-capable levels
ROLLBACK_BOUNDARY=fine metric authority commit
```

### CUT 11 — RGBV/V/photoreal synthesis

```text
ID=CUT_11
NAME=Closed-loop ThreadAtlas, metric V and unified optical evaluator
DEPENDS_ON=CUT_10,CUT_14
FILES_TOUCHED=MerkabaThreadAtlas.cs; new MerkabaFlowerCloth.hlsl; SphereFlower evaluator; render shader shared includes; integration observation updates; persistence; RGBV/optical tests
NEW_AUTHORITY=epoch-bound ThreadRun/residuals; deterministic ThreadAddress/Fibonacci continuations; endpoint-preserving RGB/V basis; deformed loop/tangent/micro-normal/moments and certified optical intervals
LEGACY_REMOVED=any new-path dense texture/UV/normal-map/roughness authority; provisional coarse-only color shortcut for resolvable fine data
INVARIANTS=H04,H10,H15-H17,H20,H21,H24; RGB never creates V; V only metric evidence; no ambient/albedo invention; completion continues detail only when unique
CPU_PROOFS=closed-loop endpoint/seam and derivative constraints; ThreadAddress continuation; Fib route equivalence; V bound/promotion/noncrossing/root order; tangent nonzero; normal/moment math; stale epoch
GPU_TESTS=CPU/HLSL RGBV and geometry parity; linear-light interval updates; actual tangent normals; subpixel moment reduction; optical invalid => zero specular correction
SCENE_FIXTURES=flat photograph V=0; plaster relief; wood/cloth repeat; unique text/photo residual; V limit/promotion; completed petal unique/ambiguous color; close/far L4/L5
PERF_CHECKS=novelty-proportional storage; no per-fragment Fibonacci walk; six-normal fixed optical work; no texture atlas allocation/readback
ACCEPTANCE=geometry and photoreal appearance are one address/evaluator; persisted RGBV survives OPEN; unresolved information is not invented
ROLLBACK_BOUNDARY=RGBV/photoreal authority commit
```

### CUT 12 — procedural readout

```text
ID=CUT_12
NAME=Compact symbol pages and procedural L0-L5 draw cutover
DEPENDS_ON=CUT_05,CUT_07,CUT_08,CUT_09,CUT_10,CUT_11
FILES_TOUCHED=MerkabaGridRenderer.cs; MerkabaGrid.Gpu.cs; MerkabaReadout.compute (full replacement); MerkabaGrid.shader; MerkabaRenderFeature.cs; native executor/generator; canonical geometry generator/files; procedural readout tests
NEW_AUTHORITY=dirty active-symbol page compaction; 64MiB generational arena; immutable child/topology tables; six depth-bin indirect draws; shared VS/FS evaluator; single-queue page generation publication
LEGACY_REMOVED=all Reset/Query/Prepare/Build/Finalize legacy readout and MeshReadout kernels; both full vertex streams/index buffers/Unity Mesh slots; meshReadoutEnabled/checker alternate; whole-world FRONT/BACK publication; direct octahedron/tip draw authority
INVARIANTS=H09,H10,H15-H19,H21-H24; page records only active symbols; no dynamic index/world authority; L4/L5 emitted when resolved; failed allocation leaves FRONT intact
CPU_PROOFS=symbol packing/page runs; canonical owner/petal evaluation; geometry-depth deviation/silhouette decisions; arena allocate/retire; generation and fence state machine; all VertexID tables
GPU_TESTS=CPU/HLSL vertex/RGBV parity; dirty-only compaction; six indirect bins; completed/hinge semantics; single native queue publication; old fence-safe page retention; environment depth/opaque passes
SCENE_FIXTURES=flat/curved/corners/hinges/parallel sheets; hole completion; L0-L5 close/far; head rotation; dirty single page; arena failure
PERF_CHECKS=remove ~480MiB reservation; arena<=64MiB; commands<=3MiB; page compaction fixed 32–64 page quantum; no whole-world rebuild/readback
ACCEPTANCE=only procedural Sphere-Flower synthesis is drawable; every legacy mesh resource/pipeline/call site physically absent
ROLLBACK_BOUNDARY=atomic readout authority cutover commit
```

### CUT 13 — spherical residency

```text
ID=CUT_13
NAME=SCAN/DRAW/WARM physical-slot spherical residency
DEPENDS_ON=CUT_12
FILES_TOUCHED=MerkabaGridRenderer.cs; MerkabaReadoutCoverage.cs; readout/cull compute; Grid storage residency hooks; native scheduler; residency tests
NEW_AUTHORITY=translation-triggered parallel AABB-distance classification of at most 32768 HOT slots; changed-slot classification; asynchronous WARM prefetch; SCAN priority
LEGACY_REMOVED=loaded-coverage dependency on scan start; canonicalDirty whole-surface build trigger; per-rotation query/rebuild; any precomputed giant logical sphere stencil
INVARIANTS=H19,H22,H23; 360° DRAW set independent of head orientation; culling may change commands only, never residency/topology/page membership
CPU_PROOFS=AABB distance and boundary states; translation-cell trigger; DRAW/WARM/SCAN nesting; negative coords; residency generation
GPU_TESTS=HOT-slot classification parity; changed-slot path; rotation produces no residency/page/storage job; culling only indirect commands
SCENE_FIXTURES=head rotation; subcell and cross-cell translation; cold/warm movement; multiroom/stairs/large space; eviction/install
PERF_CHECKS=classification<=32768 slots only on translation/slot change; zero head-rotation SSD/page work; WARM latency and memory budget
ACCEPTANCE=instant 360° rotation and independent SCAN readiness; no world traversal
ROLLBACK_BOUNDARY=spherical residency commit
```

### CUT 14 — transactional persistence

```text
ID=CUT_14
NAME=Generation-bound dirty append SAVE/OPEN foundation
DEPENDS_ON=CUT_02
FILES_TOUCHED=MerkabaSsdStore.cs; MerkabaGrid.Storage.cs; MerkabaPersistence.cs; MerkabaSessionCatalog.cs; record/layout sources; RoomScanner lifecycle hooks; persistence tests
NEW_AUTHORITY=separate M8/through/detail/thread append streams; commit generation and valid end offsets; manifest.tmp fsync+atomic rename; replayed sparse epochs; independent PumpStorageCpu/PumpStorageGpu
LEGACY_REMOVED=CaptureStoredSnapshotAsync; ReadCanonicalSnapshotAsync; whole-world PublishCheckpoint SAVE dependency; write-through+fsync per tiny batch; GPU-job gate around CPU completion/accounting
INVARIANTS=H02-H04,H20,H22-H24; no old-format compatibility authority; tails ignored; ancestor dual record supersedes descendants; no orphan detail; SCAN readiness independent of DRAW/WARM
CPU_PROOFS=every crash point before/after stream flush and manifest rename; truncated/corrupt tail; generation ordering; uniform dual supersession; epoch/detail replay; dirty-only byte counts; named-session and anchor preservation
GPU_TESTS=upload/readback phases serialize correctly while CPU file/index tasks progress; resume with queue occupied; no readback in scan hot path
SCENE_FIXTURES=save/open empty/coarse/fine worlds; parent tombstone; dual mixed/uniform; interrupted save; wake/resume; named session switch; anchor relocation
PERF_CHECKS=SAVE bytes/time proportional to dirty records; no full stored-index scan or checkpoint rewrite; CPU pump makes progress during long GPU job
ACCEPTANCE=all four authorities commit atomically by manifest generation and reopen without orphans; legacy snapshot path absent
ROLLBACK_BOUNDARY=storage-format cutover commit; new format begins cleanly with no compatibility alias
```

### CUT 15 — unified GLB/3D Tiles export

```text
ID=CUT_15
NAME=Presentation export from the shared CPU Flower evaluator
DEPENDS_ON=CUT_09,CUT_10,CUT_11,CUT_14
FILES_TOUCHED=MerkabaExporter.cs; MerkabaGlbWriter.cs; MerkabaTilesetWriter.cs; delete MerkabaExportShell.cs/MerkabaExportMembrane.cs/MerkabaOverlapShell.cs and generated overlap files when last live dependency is gone; export/oracle/validator tests
NEW_AUTHORITY=streamed confirmed+unique-completed petal enumeration; generated canonical owner; KnotAddress identity; deterministic winding; same L0-L5/RGBV evaluator; export-only presentation tessellation and baking
LEGACY_REMOVED=export closing offsets/synthetic kernels; donor inference; sparse max-flow/min-cut membrane; overlap-shell patch authority; dominant-axis quads; float VertexKey weld; export-only surface repair
INVARIANTS=H09-H11,H15-H17,H20-H24; materialized triangles cannot re-enter canonical world; completion metadata/fallback color exact; no float identity
CPU_PROOFS=live/export knot bit parity; tile-boundary owner; winding/hinge normals; completed policy; RTC origin and binary32 quantization; deterministic byte output; chunk streaming ownership
GPU_TESTS=none required for geometry authority; validate any shared HLSL/CPU table hash before export
SCENE_FIXTURES=all geometry fixtures plus confirmed/completed holes, parallel sheets, negative/hierarchy boundaries, fine V, save/open export, large multiroom/stairs
PERF_CHECKS=bounded streaming memory; no whole-world mesh list; GLB and 3D Tiles size/time; no repair pass or weld dictionary
ACCEPTANCE=GLB validator and 3D Tiles viewer pass; exporter invokes only shared Flower evaluator; all membrane/shell repair sources and stale tests absent
ROLLBACK_BOUNDARY=export authority cutover commit
```

### CUT 16 — legacy excision + ABI closure

```text
ID=CUT_16
NAME=Final physical excision and minimal native ABI
DEPENDS_ON=CUT_06,CUT_12,CUT_13,CUT_15
FILES_TOUCHED=all managed/native resource enums/bindings; shader generator; build sanitizer; deleted old generated/source/meta files; tests/tool manifests; all source roots searched
NEW_AUTHORITY=one final ABI containing only retained M8 storage plus TileHalo, sparse dual, certificate, attempt bins, detail/thread pages, symbol arena/directory and indirect commands
LEGACY_REMOVED=every deferred resource/pipeline/helper/property/serialized flag/compatibility alias; dead generated files; stale legacy tests; obsolete docs presented as authority
INVARIANTS=H01-H24; exact managed/native/HLSL resource and pipeline equality; no hidden CPU/shader/export alternate geometry
CPU_PROOFS=forbidden-symbol scan; reflection/layout manifest; source ownership graph; generated artifact reproducibility; clean import/meta references
GPU_TESTS=compile/validate every final SPIR-V pipeline; descriptor type/count audit; native ABI handshake; every job kind; no stale binding/property
SCENE_FIXTURES=smoke set covering scan, through, R2/R3, detail, draw, export
PERF_CHECKS=final resource memory inventory compared to mandatory base; dispatch inventory; no world-sized cleared buffer or mesh reservation
ACCEPTANCE=forbidden registry search returns zero semantic matches except immutable contract/test assertions; ABI has no unused slot; clean Unity import/build
ROLLBACK_BOUNDARY=legacy-excision commit; no compatibility fallback exists after it
```

### CUT 17 — full application closure

```text
ID=CUT_17
NAME=End-to-end Quest application and final contract closure
DEPENDS_ON=CUT_13,CUT_14,CUT_15,CUT_16
FILES_TOUCHED=RoomScanner/lifecycle; FINE/ERASE integration; anchor/ALIGN; controller/UI/design workspace only where regression fixes are required; full tests/build/profiling evidence; lasttrue ledger
NEW_AUTHORITY=none; proves the single authority is used coherently by every application workflow
LEGACY_REMOVED=any final stale lifecycle readiness gate, dead UI field, abandoned setting, alternate save/export/readout path found by full audit
INVARIANTS=H01-H24 and every REV-B MUST/MUST NOT statement
CPU_PROOFS=complete oracle suite, persistence crash suite, export/live parity and deterministic repeat run
GPU_TESTS=full compute parity/SPIR-V/native ABI suite; long-run queue priority/publication; no readback/per-frame allocation in hot paths
SCENE_FIXTURES=every mandatory fixture plus SCAN/STOP/RESUME/WAKE/SAVE/OPEN/ALIGN/FINE/ERASE/GLB/3D Tiles/controller/two-hand/design/session anchor/multiroom/stairs/large scan/cold-warm residency
PERF_CHECKS=Quest GPU stages stereo/certificate/binning/commit/page compact/cull/draw; CPU storage/publication/session/submission; complete memory inventory; rotation and translation assertions
ACCEPTANCE=FINAL_DAG_AUDIT, FINAL_CONTRACT_AUDIT, FINAL_LEGACY_AUDIT, FINAL_BUILD and FINAL_RUNTIME_FIXTURES all PASS; every node closed and committed
ROLLBACK_BOUNDARY=final closure commit
```

## DAG topological execution order

Persistence is deliberately established before any newly persistent production authority. This avoids a forbidden HOT-only dual phase and avoids committing FlowerDetail/ThreadAtlas into the old whole-snapshot store.

```text
CUT_00
  -> CUT_01
  -> CUT_02
  -> CUT_14
  -> CUT_03
  -> CUT_04
  -> CUT_05
  -> CUT_06
  -> CUT_07
  -> CUT_08
  -> CUT_09
  -> CUT_10
  -> CUT_11
  -> CUT_12
  -> CUT_13
  -> CUT_15
  -> CUT_16
  -> CUT_17
```

This ordering is acyclic. CUT 14 retains its required logical identity but executes early as a storage foundation.

## DAG ownership audit

### Hard-invariant ownership

| Invariant | Owning cut(s) | Final verifier |
|---|---|---|
| H01 | 0 | 16,17 |
| H02 | 2,5,14 | 16,17 |
| H03 | 2,6,9,14 | 17 |
| H04 | 2,10,11,14 | 17 |
| H05 | 1,3 | 16,17 |
| H06 | 1,5,7,8 | 17 |
| H07 | 1,3,5,7,8 | 16,17 |
| H08 | 1,3,4,5,7,8,9 | 17 |
| H09 | 1,8,9,12,15 | 17 |
| H10 | 1,7,10,11,12,15 | 17 |
| H11 | 9,12,15 | 17 |
| H12 | 6,9 | 17 |
| H13 | 5,6 | 17 |
| H14 | 3,5 | 17 |
| H15 | 7,8,10,11,12,15 | 17 |
| H16 | 1,10,11 | 17 |
| H17 | 11,12,15 | 17 |
| H18 | 12 | 16,17 |
| H19 | 12,13 | 17 |
| H20 | 14 | 17 |
| H21 | 11,12,15 | 17 |
| H22 | 3,5,6,10,12,14,16 | 17 |
| H23 | every replacement cut | 16,17 |
| H24 | 1,3-11 | 17 |

Every hard invariant has an implementation owner and an independent final verifier.

### Legacy replacement/deletion ownership

| Legacy authority | Replacement available | Physical deletion no later than |
|---|---:|---:|
| candidate/winner/queue positive integration | CUT 3 + CUT 5 | CUT 5 |
| nearest/normal-step/current-frame/weighted fusion | CUT 5 | CUT 5 |
| dilation + CARVE + 26-plane continuation | CUT 6 | CUT 6 |
| square tangent stereo support | CUT 4 | CUT 4 |
| mesh/canonical primitive readout and ~480MiB buffers | CUT 12 | CUT 12 |
| loaded-coverage/per-rotation rebuild lifecycle | CUT 13 | CUT 13 |
| whole-snapshot SAVE/checkpoint | CUT 14 | CUT 14 |
| overlap shell/export heal/membrane/min-cut/float weld | CUT 15 | CUT 15 |
| leftover ABI aliases/dead generated helpers/tests | all prior cuts | CUT 16 |

Every delete has a preceding or same-cut replacement. No delete is ownerless.

### Cross-authority audit

```text
Canonical positive surface owner: CUT_05 M8 R1 only.
Negative volume owner: CUT_06 sparse SEE_THROUGH only, persisted by CUT_14.
Fine metric owner: CUT_10 FlowerDetail only.
Fine appearance owner: CUT_11 ThreadAtlas only.
Live presentation owner: CUT_12 shared procedural evaluator.
Export presentation owner: CUT_15 same CPU evaluator/table hash.
Persistence owner: CUT_14 transaction contains all four authorities.
No cut creates a second coordinate hierarchy, surface solver, mesh truth or fallback.
R1/R2/R3 are shell semantics in CUT_05/07/08, never LOD/confidence.
L4/L5 true geometry is proved in CUT_10 and emitted in CUT_12.
Dual never emits a petal except after CUT_09 leaves exactly one direct-incidence candidate.
```

```text
DAG_ACYCLIC=PASS
CONTRACT_INVARIANT_OWNERSHIP=PASS
LEGACY_REPLACEMENT_OWNERSHIP=PASS
DELETE_DEPENDENCY_AUDIT=PASS
PARALLEL_AUTHORITY_AUDIT=PASS
PERSISTENCE_READOUT_EXPORT_UNITY=PASS
L4_L5_GEOMETRY_OWNERSHIP=PASS
R1_R2_R3_SEMANTICS=PASS
DUAL_NON_SURFACE_ORACLE=PASS
DAG_AUDIT=PASS
```

## CURRENT TRUE STATE — after complete DAG audit

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
