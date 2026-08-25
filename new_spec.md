# Σ‑PRISM‑16

## Full-native S16 world, inverse Merkaba closure and multiple pure readouts

**Canonical reconstruction baseline:** `CPQ4-2026-08-25-S16-v8`
**Target device:** Meta Quest 3
**Implementation target:** Unity / Android / Vulkan / GPU-first
**Status:** canonical replacement specification

Version 8 replaces the v7 sensor-cell/proposal/topology execution ontology. It
keeps the exact NumericDomain, signed-XOR S16 algebra, sparse intrinsic carrier,
evidence ownership, first-hit causality, exact codec and root-last publication. It
defines one native relation descriptor from which forward manifestation, sensor
inverse pullback, intrinsic stitching and every readout are generated.

The architecture is not “a conventional 3D scanner encoded in S16”. S16 is the
native local reality. RGB-D, retinal images, geometry, topology and export are
lower-dimensional shadows or relations of that reality.

---

# 0. Authority, scope and replacement rule

This document is the sole canonical reconstruction/product specification.

The live implementation must be self-contained. Exact TOE/Merkaba expressions may
be imported as build-time source material, but they become authoritative only
after they are represented by the generated descriptor and frozen fingerprints
defined here. No runtime dependency on another repository or undocumented theorem
is permitted.

Version 8 is a replacement, not an additive compatibility layer. The following v7
runtime concepts are not alternate valid physics:

```text
four persistent/global sensor-cell worlds
CURRENT / PENDING / CONTINUATION / NOVEL as physical kinds
one-winner pending projection
provider × backing-segment candidate evaluation
image-edge topology universe
XYZ overlap as transition authority
pixel-shaped canonical gauge allocation
page halo as continuity
live mesh/XYZ world as reconstruction state
```

They may exist only until the corresponding S4‑08.6 replacement gate passes. The
same commit then deletes them. No fallback, feature flag or compatibility graph may
remain.

Representation-neutral donor infrastructure is limited to synchronized capture,
immutable calibration/poses, XR lifecycle, permissions/anchors/input/UI, Vulkan
resource/fence/indirect helpers, asynchronous persistence plumbing and GLB
encoding utilities.

---

# 1. Canonical product statement

The only durable physical world is

\[
\boxed{\Psi:\Sigma_2\rightarrow\mathbb S_{16}}.
\]

- `Σ₂` is one sparse, logically unbounded intrinsic two-dimensional carrier.
- `S16` is the real 16-dimensional sedenion algebra.
- `Ψ(ξ)` is a complete native local state, called a **germ**.
- unallocated carrier is implicit native null state and costs no storage;
- canonical coefficients use checked nearest-even Q16.48;
- fixed algebra/relation fingerprints and minimal exact certificates are
  interpretation/proof metadata, not another physical world.

The canonical world is never reduced to:

```text
XYZ points or voxels
eye pixels or depth maps
mesh/splat/meshlet vertices
texture texels or material maps
topology/boundary/object graphs
sensor consensus records
scene-history geometry
```

All such products are disposable readouts or evidence views. Deleting every
readout cache must leave `Ψ`, its proof and future inference unchanged.

The product supports one persistent whole-building scan, revisit refinement,
two-sided/thin surfaces, folds, directional appearance, current-scene evolution,
direct stereo XR readout, rich textured 3D export, restart and deterministic
continuation—all through this one native field.

---

# 2. Native-16D viewpoint

A physical camera produces a lower-dimensional drawing of the native world. It
does not provide a primitive 3D reality that is later encoded into S16.

For germ `ξ`:

\[
s_\xi=\Psi(\xi)\in S16.
\]

For a sensor/readout query `q`, the observed value is a shadow:

\[
o_q\in\mathcal S_q(s_\xi).
\]

Scan asks for the native preimage:

\[
\boxed{
\mathcal A_q
=
\mathcal S_q^{-1}(O_q)
=
\{s\in S16\mid\mathcal S_q(s)\in O_q\}.
}
\]

This is analogous to understanding a native 3D object from several 2D drawings,
except the native object here is S16. A view may discard information; the canonical
world does not.

Multiple observations refine the same native admissible region:

\[
\mathcal A_\xi^{n+1}
=
\mathcal A_\xi^n\cap\mathcal A_{q,t,\xi}.
\]

Time is provenance and causality, not a separate fusion algebra. A later pose is
another shadow operator. Scene evolution is admitted only by explicit first-hit
transition proof.

---

# 3. Non-negotiable invariants

1. **One full native state.** Sixteen coefficients are an algebraic state, not
   independent `xyz/rgb/normal/confidence` channels.
2. **One relation vocabulary.** Manifestation, sensor pullback, intrinsic stitch
   and readout are generated from the same descriptor.
3. **Overcomplete relations are not state channels.** A 22-relation atlas has one
   shared S16 preimage and cannot be independently updated.
4. **Readout is lossy and pure.** Eye/export/debug/prediction readouts never mutate
   or impoverish `Ψ`.
5. **Independent evidence stays independent.** RGB-L/R, depth-L/R and different
   poses remain distinct native relation constraints until conjunction.
6. **Confidence is admissible width.** It never becomes a sensor weight or vote.
7. **First-hit causality.** A measurement constrains its pre-hit sector and first
   supported hit. Behind-hit state receives exactly no evidence.
8. **UNKNOWN is not EMPTY.** The complement of one shadow is an unresolved native
   fibre, not null space evidence.
9. **Minimum change is native.** XYZ, RGB or pixel error cannot define state
   identity.
10. **Invisible directions survive.** New evidence cannot modify a native relation
    direction it does not constrain.
11. **Topology is stitchability.** Regular/fold/null/no-relation/unresolved are
    strata of the same native relation closure, not a second topology solver.
12. **3D proximity is never identity.** Arbitrarily close manifestations may be
    different carrier preimages.
13. **Gauge is intrinsic.** Sensor pixels, pages and 3D coordinates never allocate
    canonical carrier coordinates.
14. **Refinement adds native carrier capacity.** It never creates a detail mesh or
    texture world.
15. **Contradictions remain exact.** Gaps and provenance are retained, never
    averaged or cancelled.
16. **Pages/segments are storage only.** Changing them changes no physical work
    cardinality, state, topology, proof or readout.
17. **One revision root.** A reader sees the entire old or entire new immutable
    revision. Root exchange is the last visible write.
18. **No fixed evidence/session ceiling.** Scratch limits continue, spill or
    backpressure; they cannot truncate admitted evidence.
19. **GPU owns canonical work.** CPU owns lifecycle/resources/fences/persistence
    orchestration, never per-pixel/native decisions.
20. **Admitted observations are owned.** Pre-admission capture may be sampled by a
    deterministic policy; after admission no frame is overwritten or partially
    retained.

---

# 4. Why S16 is the native local algebra

Within the Cayley-Dickson ladder

\[
\mathbb R\rightarrow\mathbb C\rightarrow\mathbb H
\rightarrow\mathbb O\rightarrow\mathbb S,
\]

the required native relation semantics include both:

\[
[a,b,c]=(ab)c-a(bc)\ne0
\]

and a non-trivial zero-divisor/annihilator stratum

\[
z\ne0,\quad a\ne0,\quad za=0.
\]

S16 is the first stage in this ladder possessing both. This is a state-space
minimality statement, not the dimension of physical position. Ordinary 3D is one
manifestation/readout.

---

# 5. Canonical NumericDomain

Canonical coefficient and state-changing scalar semantics are:

```text
NumericDomain = num.fixed.q16_48.checked.nearest_even
signed         = true
int_bits       = 16
frac_bits      = 48
storage_bits   = 64
rounding       = nearest-even for point arithmetic
interval       = outward-rounded
overflow       = checked, fail-closed
ONE            = 1 << 48
range          = [-32768,32768)
```

Required primitives:

```text
qadd/qsub
qmul/qdiv
qabs/qmin/qmax/qclamp
checked dyadic shifts
outward interval mul/div
deterministic integer sqrt where required
exact signed comparisons
```

FP16/FP32 may run after a canonical decision for disposable readout. FP cannot
decide acceptance, identity, first-hit sector, stitch class, gauge allocation,
proof, publication or persistence.

Backend layout is a lowering. Packed-32 and native-I64 are legal only after
bit-parity/capability gates. Unsupported exact arithmetic disables canonical
mutation; it never silently falls back to floating point.

---

# 6. Exact S16 algebra and generated operator IR

Use basis `e0=1,e1,…,e15` and generated signed-XOR multiplication

\[
e_ie_j=\varepsilon_{ij}e_{i\oplus j},
\qquad\varepsilon_{ij}\in\{-1,+1\}.
\]

Reference equivalence uses the frozen Cayley-Dickson recursion. The generator owns:

```text
mulBasis index/sign
conjugation signs
left/right basis permutations
signed-dyad annihilator catalog
explicit bracket trees
Hadamard/readout rows that remain part of the native descriptor
native relation descriptor and fingerprints
```

Every product of more than two factors has an explicit bracket tree. `a*b*c` is
invalid semantic source. A fused lowering records and proves equality to one exact
tree.

The common generated IR vocabulary is:

```text
XOR_INDEX / PERMUTE / SIGN / NEGATE
ADD / SUB / SHIFT
CMP / MIN / MAX / MASK / SELECT
GATHER / SCATTER
FIXED_BOUNDED_REDUCTION
QMUL / QDIV only where the semantic expression requires them
INTERVAL_MUL / INTERVAL_DIV only for conservative contractor propagation
```

Rules:

- dense schoolbook S16 multiplication is a reference or explicitly selected
  generated fallback, never the default hot path;
- signed-XOR/permutation/dyadic operations bypass generic multiplication;
- common subexpressions are shared across all 22 relations and readouts;
- bounded control lowers to masks/selects or uniform fixed schedules;
- optimized and reference evaluators are bit-identical;
- semantic descriptor fingerprint, not instruction order, is authority.

---

# 7. Native Merkaba/eigenmode relation descriptor

## 7.1 Descriptor definition

One generated descriptor is frozen:

\[
\boxed{
\mathcal D_M=
(\mathcal A_{S16},E_{0..21},\mathcal M,\Pi_q,
\mathcal T,\mathcal Z,\mathcal B,\Delta).
}
\]

It contains:

```text
22 exact relation expression DAGs
every input lane/constant/signed-XOR permutation
every explicit product bracket
relation output domains and consistency identities
forward local-manifestation plan
sensor/eye/export/debug query plans
reverse contractor for every expression node
intrinsic neighbour transport/stitch plan
ZD/annihilator and associator strata
native minimum-change selector and tie order
common-subexpression schedule
semantic and generated-lowering fingerprints
```

The exact E22 expressions come from the authoritative TOE construction supplied to
the generator. Scanner developers may not infer or invent the missing equations.
Before that descriptor passes §34.1, live v8 mutation remains disabled.

## 7.2 Relation atlas

\[
E_{22}(s)=(E_0(s),\ldots,E_{21}(s)),
\qquad
\mathcal V_E=E_{22}(S16).
\]

`V_E` is the consistent image of S16. It is an overcomplete coordinate/relation
atlas of the same state, not a 22-dimensional canonical replacement. Relation
bounds are conjoined through one shared S16 preimage.

No runtime may persist independently editable `Edge[22]` state. Generated relation
values/caches are disposable and keyed by germ generation plus descriptor
fingerprint.

## 7.3 Four mandatory evaluators from one descriptor

The generator emits:

```text
ForwardNative22            S16 -> relation atlas / manifestation / shadow
PullbackNative22           observation relation set -> admissible S16 region
StitchNative22             neighbouring S16 germs -> native relation stratum
Native22ReferenceOracle    slow semantic authority for all three
```

Separate handwritten depth, RGB, topology or export physics are forbidden.

---

# 8. Forward manifestation and shadow family

For germ state `s`, sub-carrier offset `δ` and direction/query `ω`, define the
local manifestation

\[
v_M=\mathcal M(E_{22}(s);\delta,\omega).
\]

For query descriptor `q`:

\[
\boxed{
\mathcal S_{q,p}(s)
=
\Pi_{q,p}(v_M).
}
\]

`Πq,p` contains only query/calibration semantics:

- finite pixel or requested readout footprint;
- exact pose/calibration epoch;
- homogeneous projection rows;
- direct order/depth relation;
- optical/directional response;
- support/null predicates;
- first-hit visibility policy.

The forward evaluator may materialize a local 3D point, differential or colour as
an output when a query requests it. That materialization is never an intermediate
canonical world.

For a homogeneous row pair `Uq,Wq`, an observed image interval

\[
u_{min}\le\frac{U_qs}{W_qs}\le u_{max}
\]

is represented by exact inequalities

\[
(U_q-u_{max}W_q)s\le0,
\qquad
(u_{min}W_q-U_q)s\le0.
\]

The descriptor uses the analogous exact relation form for `v`, direct order/depth
and optical ratios. Where a row is not linear, the original bracketed expression
remains in the relation packet and is contracted through its generated DAG.

---

# 9. Scanner observation model

One coherent admitted rig observation is

\[
Y_t=(D_L,D_R,RGB_L,RGB_R,M_t,K_t,C_t),
\]

where `M/K/C` name exact timestamped pose, intrinsics/extrinsics and immutable
calibration epoch.

Each valid finite footprint produces an independent `ShadowRelationPacket`:

```text
observation revision
source/eye and independence key
calibration/pose epoch
finite footprint key
first-hit sector and direct-order interval
candidate native fibre key or latent seed key
relation mask
exact relation bounds/predicates
raw reference when later contraction needs original samples
```

The four sensor streams are not converted into four persistent 16D cell arrays.
They are independent constraints on the same relation atlas. The implementation
may materialize packets or construct them on demand, but source order and physical
partition cannot alter their conjunction.

Stereo is native observability, not only triangulation. If `Rq` denotes the active
generated shadow rows on a linear branch, left/right constrain different row
spaces:

\[
V_L=\operatorname{rowspan}R_L,
\quad
V_R=\operatorname{rowspan}R_R,
\quad
\operatorname{span}(V_L,V_R).
\]

Across viewpoints that span can grow and reveal native directions shadowed by a
previous observation. No sample-count strengthening is implied.

---

# 10. Exact inverse Merkaba pullback

## 10.1 Inverse image, not pseudoinverse

For observation region `Oq,p`:

\[
\boxed{
\mathcal A_{q,p}
=
(\Pi_{q,p}\circ\mathcal M\circ E_{22})^{-1}(O_{q,p}).
}
\]

The contractor reverses the same semantic DAG. It never computes a floating matrix
pseudoinverse and never re-associates a nonassociative product for convenience.

## 10.2 Native admissible region

A `NativeRegion` is an exact conjunction of descriptor relation predicates with a
conservative Q16.48 enclosure and provenance. It is not defined to be a 16D
axis-aligned box.

```text
NativeRegion
    descriptor fingerprint
    conservative S16 enclosure
    relation predicate/range records
    source/independence provenance
    first-hit/order records
    consistency mask
    unresolved branch cursor, if bounded contraction is incomplete
```

Outward interval propagation may widen an enclosure. It may not drop an original
predicate. A candidate state commits only after all original predicates are
forward-evaluated and satisfied bit-exactly. Contractor exhaustion is unresolved.

## 10.3 Independent conjunction

For one germ candidate `i`:

\[
\mathcal C_i
=
\mathcal C_{prior,i}
\cap
\bigcap_{q,p}\mathcal A_{q,p,i}
\cap
\bigcap_{j\in N(i)}\mathcal N_{ij}.
\]

Conjunction is commutative and source-order invariant. Empty intersection retains
the exact failed predicate/gap and both provenances. Nothing is averaged.

## 10.4 Native minimum-change selection

\[
\boxed{
s'_i
=
\operatorname*{argmin}_{s\in\mathcal C_i\cap Q48^{16}}
\Delta_{\mathcal D_M}(s,s_i).
}
\]

`ΔD` and its lexicographic tie break are generated/fingerprinted. It cannot be
Euclidean XYZ distance, RGB error or pixel reprojection error alone.

If the prior satisfies all new relations, its bytes remain unchanged. Evidence may
still strengthen the certificate.

## 10.5 Hidden native directions

For a linear active row operator `R`:

\[
P_{\ker R}(s'-s)=0.
\]

For general nonlinear/bracketed relations, define observation-fibre equivalence:

\[
s_a\sim_O s_b
\iff
\forall c\in O:\ c(s_a)=c(s_b).
\]

Minimum change retains the prior representative along the unconstrained
equivalence fibre. One-eye/depth-only evidence cannot erase directional
appearance, hidden relation context, topology state or fine detail learned from
another view.

## 10.6 Checked result

The candidate must pass:

```text
all source relation predicates
descriptor consistency identities
Q16.48 checked range
first-hit and behind-hit causality
required intrinsic stitches
promotion/disappearance gates when relevant
evidence ownership
codec round-trip
```

Failure leaves the prior byte-identical and retains the necessary unresolved
evidence.

---

# 11. First-hit and direct-order semantics

First-hit is part of the sensor shadow descriptor before association/closure.

For measured first-hit interval `Dm` and predicted fibre order interval `Dp`:

## 11.1 Compatible hit

If the intervals overlap under calibrated uncertainty, the observation contributes
inclusive native relation constraints to that fibre.

## 11.2 Measured hit in front

If `Dm` is strictly before `Dp`, the measured hit may constrain another supported
or latent fibre. The old predicted fibre is behind the new first hit and receives
no evidence.

## 11.3 Predicted contact in measured pre-hit path

If `Dp` is strictly before `Dm`, emit an exact `PRE_HIT_EXCLUSION` relation against
that fibre. It is not a negative correction and cannot alone mutate state.

## 11.4 Behind-hit invariant

Every state behind the measured first hit receives:

\[
\boxed{
\text{no inclusive relation, no exclusion, no confidence/certificate change}.
}
\]

No ray carving or implicit free space is permitted.

---

# 12. Native fibre association

Prediction is a disposable generator of candidate native fibres. It is never
allocation or identity authority.

For footprint `p`, candidate generation returns an ordered compact set

\[
F_p=\{f_0,\ldots,f_n\}
\]

containing every visible/resident fibre whose exact shadow bound can explain the
observation. Multiple preimages at one eye pixel are legal and expected.

Rules:

1. both eyes use their actual reprojected footprint;
2. no nearest-depth/lowest-handle winner may discard another untested fibre;
3. pruning is legal only when a conservative native-shadow bound proves
   incompatibility;
4. each candidate uses the same `PullbackNative22` and first-hit rules;
5. a latent solve is attempted only after all supplied compatible fibre classes
   fail or remain unresolved according to the descriptor;
6. inability to enumerate a required nonresident fibre backpressures/defers; it
   cannot mint a new identity;
7. execution ordering is stable by complete native key and provenance.

`CURRENT`, `PENDING`, `CONTINUATION` and `NOVEL` are removed as physical proposal
kinds. Runtime dispositions may describe whether evidence matched a supported
germ, refined a latent germ or remains unresolved, but those labels do not define
physics or canonical addressing.

---

# 13. Intrinsic Merkaba stitching and native topology

## 13.1 Transition

For intrinsic neighbours `i,j`:

\[
\tau_{ij}=\overline{s_i}s_j.
\]

The descriptor generates relation transport `Tk(τij)` and compatibility:

\[
\mathcal N_{ij}
=
\bigcap_{k=0}^{21}
\operatorname{Compat}_k
\left(E_k(s_j),\mathcal T_k(\tau_{ij})E_k(s_i),e_{ij}\right).
\]

`eij` is exact first-hit/native-relation evidence. Image adjacency or 3D proximity
may propose a candidate key but creates no claim.

## 13.2 Outcomes

```text
REGULAR
    required relation modes close under regular transport

SINGULAR
    required relation enters a stable supported ZD/nonassociative stratum

NO_RELATION
    first-hit/native constraints prohibit the stitch

UNRESOLVED
    evidence or bounded contractor is insufficient
```

A fold, boundary, null/contact transition or different sheet is a stitch outcome.
No separate topology graph or XYZ qualifier exists.

## 13.3 Zero divisors

The generated exact dyad catalog defines probes `ak`. A singular relation may
satisfy

\[
\tau_{ij}a_k=0
\]

or the calibrated exact relative Q16.48 stratum gate. Dyad action uses generated
signed-XOR permutations/add/sub, not generic dense multiplication.

## 13.4 Nonassociative context

For an intrinsic chain `i→j→k`, compare explicit brackets

\[
(\tau_{ij}\tau_{jk})a_r,
\qquad
\tau_{ij}(\tau_{jk}a_r).
\]

A nonzero associator is native context and may prevent flattening the relation
into an associative pairwise continuation. The generator owns the bracket plan.

## 13.5 Dirty domain and cache

The dirty stitch domain is

\[
D_E=
\{(i,j)\in E_{\Sigma}:
g_i\ne g_i^{cache}
\lor g_j\ne g_j^{cache}
\lor e_{ij}\ne e_{ij}^{cache}\}.
\]

It depends on endpoint/evidence generations, never proposal kinds. This includes
changed supported↔supported edges.

The cache key contains both endpoint generations, evidence generation and
descriptor fingerprint. Hit and forced-miss paths are bit-identical. The cache is
disposable and has no topology authority.

## 13.6 Independent-view stability

A measured singular class becomes stable only when its exact relation signature is
supported by the persisted minimum number of independent view keys. Repeat frames
from one class do not count as new support. Until then the claimed edge remains
unresolved/fail-closed.

---

# 14. Latent native relations

If no supported fibre can explain an observation, the system creates or refines a
noncanonical `LatentGerm`:

```text
stable local native chart/seed
admissible NativeRegion
complete source-relation evidence references
candidate intrinsic stitch references
generation and lifecycle receipt
```

It is not a pixel, 3D point, page, surface object or second world. Sensor/image
coordinates remain observation provenance only.

A later observation first tests all conservatively viable supported and latent
fibres. Exact native conjunction may:

- refine the same latent germ;
- absorb it into an existing carrier branch through a supported stitch;
- keep it unresolved;
- reject/expire its identity while retaining still-required evidence;
- promote it after independent support.

Promotion to canonical `Σ₂` occurs only when:

```text
the native relation region is non-empty and forward-verified
independent null→contact support is satisfied
no compatible existing/latent stitch explains the evidence
the local chart and evidence are complete
```

The deterministic promoted-chart allocator preserves the latent local chart and
allocates collision-free intrinsic carrier space ordered by observation revision,
complete provenance key and lexicographic native seed. It is independent of image
resolution, pixel layout, 3D position, page layout and GPU scheduling.

---

# 15. Multi-pass refinement and intrinsic gauge

The descriptor may expose local manifestation over sub-carrier offset/direction:

\[
\mathcal R_\xi(\delta,\omega)
=
\mathcal M(E_{22}(s_\xi);\delta,\omega).
\]

Different baselines, angles and footprint phases can constrain different native
modes of one germ. Stable revisits may leave S16 bytes unchanged while strengthening
certificates. Informative revisits contract the admissible state.

A germ is refined only when all retained observations cannot be represented by one
state and its descriptor-permitted local variation:

\[
\exists O_a,O_b:
\text{one-germ closure empty or falsely broad, while a finer intrinsic chart has
a non-empty verified closure}.
\]

Refinement performs:

```text
exact gauge demand proof
→ bijective intrinsic chart split/remap
→ transport complete evidence and stitch relations
→ instantiate finer S16 germs
→ rerun the same native closure
```

No 3D voxel, displacement field, texture world, mip geometry or decimated
canonical mesh is introduced.

---

# 16. Evidence, precision and certificates

## 16.1 Epistemic precision is not an arbitrary S16 channel

Native state support/amplitude may be a descriptor observable. Directional
epistemic precision is carried by exact relation regions and certificates. It may
not be collapsed into an ad-hoc dyadic scalar staircase that changes canonical S16
bytes.

For a linear constrained relation width `wr` with physical floor `wFloor,r`, the
normative precision contribution is

\[
\pi_r=\frac{wFloor,r}{\max(w_r,wFloor,r)}
\]

using exact Q16.48 division. For nonlinear relations the descriptor supplies the
corresponding monotone precision bound. No source-count term exists.

Any scalar support value stored in/derived from `s` is only the descriptor-defined
physical/projective observable. It is not a substitute for directional proof.

## 16.2 Complete journal before visibility

Every candidate visible revision owns a complete immutable journal of all source
relation packets required to reproduce:

- the selected state or unchanged result;
- validity and exact gaps;
- first-hit sectors;
- latent promotion/absorption;
- required stitch classes;
- independent support.

The joint region or selected state is a fast witness/cache, not a complete journal.
Source evidence is stored once per observation. Page/revision generations hold
references and never duplicate it per page.

## 16.3 Native relation certificate

After deterministic proof minimization, retain:

```text
NativeRelationCertificate
    descriptor fingerprint
    native germ/latent range
    relation mask and exact predicates/bounds
    source class and independence key
    calibration/pose epoch
    first-hit/order role
    support / appearance / stitch / transition / pose role mask
    raw reference when irreducible
```

Certificates are proof metadata, not physical state.

## 16.4 Deterministic minimization

1. sort records by germ/latent key, role, independence key, source, descriptor
   relation and exact bounds;
2. coalesce same-key compatible relations by exact conjunction;
3. preserve explicit conflicts;
4. perform reverse-lexicographic redundancy sweeps;
5. remove one certificate only when state, full admissible relation set, first-hit,
   support and stitch gates remain bit-identical;
6. repeat to a fixed point with a generation-owned continuation cursor.

Scratch window size may change cost only. Complete journal/raw references remain
owned until minimization/persistence handoff. Frame execution storage may recycle
as soon as evidence has transferred to generation-safe ownership.

## 16.5 Raw observation retention

Retain compressed raw tiles only while needed to contract an unresolved native
relation, resolve a conflict, exploit future subpixel/baseline information, prove a
scene transition or resolve pose/calibration ambiguity. Reclaim after an exact
certificate/durable handoff proves them redundant.

---

# 17. Sparse canonical publication

Only `GermDelta` may mutate `Ψ`:

```text
NativeGermKey
prior generation
new S16[16] Q16.48
changed mask/outcome
evidence journal/certificate receipt
required stitch receipts
```

One admitted observation produces at most one sparse revision boundary:

```text
CHANGED germs
→ unique touched logical pages
→ allocate/prepare immutable shadow generations
→ scatter exact S16 bytes, one owner per germ
→ attach evidence/stitch receipts
→ close all pages and revision manifest
→ validate fault/defer state
→ atomic published-root exchange as final visible instruction
```

`UNCHANGED` may strengthen evidence without allocating a page generation.
`UNRESOLVED`, conflict or fault cannot expose partial state. Readers resolve every
page through the selected immutable root. Old generations remain pinned until all
GPU readers and evidence owners retire.

Pages, blocks and segments are absent from observation, topology, gauge and proof
identity.

---

# 18. Exact carrier storage and codec

`Σ₂` uses signed 64-bit logical page coordinates. Unallocated space is implicit
native null.

Logical 64×64 pages contain 8×8 codec blocks. Each block chooses the smallest
exact encoding:

```text
NULL      implicit exact native null state
CONST     one S16 state repeated 8×8
AFFINE    exact s(u,v)=s0+u*su+v*sv
DELTA     exact predictive residual stream
RAW       explicit 8×8×16 Q16.48 states
```

DELTA predictor per coefficient in raster order:

```text
(0,0)        0
first row    left
first col    up
interior     left + up - upperLeft
residual     actual - predictor
```

Store the minimum signed bit width per coefficient and fixed
`(coefficient,v,u)` bit order. Mode tie order is
`NULL<CONST<AFFINE<DELTA<RAW`. Decode must reproduce exact samples;
encode→decode→encode is deterministic.

Codec mode, block, page and physical segment have zero physical meaning.

---

# 19. Residency, paging and unbounded world

The logical carrier and evidence namespaces are unbounded. GPU residency is a
bounded cache.

Resident locality includes:

- eye-visible/query-active germs;
- sensor inverse fibres;
- dirty stitch neighbours;
- unresolved latent work;
- publication shadows and pinned readers.

Pressure order:

```text
discard disposable eye/prediction/debug/export caches
stage clean immutable carrier generations and certificates
spill owned complete evidence journals losslessly
evict clean pages after durable publication
backpressure new canonical admission if no lossless destination exists
```

It never reduces sensor resolution, relation count, proof, S16 detail or accepted
work. A storage-buffer binding never exceeds the runtime Vulkan range. Segment
count cannot change semantic work count.

S4‑08 retains fail-closed bounded residency. S4‑10 closes encode/evict/rehydrate,
restart and whole-building scale. A fixed 1 GiB decoded pool is not world size.

---

# 20. Pose and calibration as query gauge

Pose and calibration parameterize `Πq`; they are not canonical geometry.

Conditioned overlaps construct independent exact Q16.48 twist admissible regions
against the immutable Meta pose prior. Missing covariance is never zero
uncertainty; use the deterministic conservative envelope from clock/skew,
translation/rotation rate, rig residual and calibration bounds.

Conjoin source pose regions. If the non-empty region excludes zero, choose the
generated minimum-magnitude twist. If it contains zero, retain Meta pose. If empty,
retain Meta pose and unresolved evidence. An accepted correction reruns the same
frame's shadow query; it is not carried blindly into the next frame.

Calibration epochs are immutable and fingerprinted. Existing evidence is never
silently reinterpreted under a different descriptor.

---

# 21. Scene evolution on the same native world

The durable root represents the current best-supported scene, not an average or
union of history. Evolution remains a native relation transition:

```text
LATENT -> SUPPORTED
SUPPORTED -> LATENT
SUPPORTED(old manifestation) -> SUPPORTED(new manifestation)
SUPPORTED -> SUPPORTED* native deformation
```

## 21.1 New manifestation

A measured first hit in front of a current fibre may support a latent native germ.
The old behind-hit germ receives no constraint. Promotion follows §14.

## 21.2 Exclusion and disappearance

A predicted contact lying in independently observed pre-hit paths accumulates
exact exclusion certificates keyed by independence class. It returns to native
null only when:

1. the persisted minimum independent-view/angular gate passes;
2. null makes every confirming shadow admissible;
3. no retained stronger inclusive relation still requires the contact;
4. no coherent native transport explains the change;
5. no behind-hit sample was used.

This is not ray carving.

## 21.3 Identity-preserving transport

Before retire/recreate, test whether one descriptor-generated native transport of
the same intrinsic carrier region explains both old exclusions and new inclusive
shadows while preserving hidden relations, stitch strata, detail and evidence
strength. Transport identity is intrinsic; no semantic object or XYZ-nearest match
is canonical.

## 21.4 Occlusion

A nearer first hit supplies no evidence to the hidden background. Temporary
occlusion cannot retire or weaken that background.

S4‑09 implements these transitions by reusing v8 relation packets, latent records,
stitch closure and certificates. It may not add a temporal/object solver.

---

# 22. Readout family A — direct stereo XR eyes

The Quest eyes require two retinal images, not a persistent mesh world.

Compile two query descriptors:

\[
Q_{L,eye},\qquad Q_{R,eye}.
\]

For each active germ/fibre, `ForwardNative22` emits only the rows required for:

```text
homogeneous retinal location
direct-order/depth reduction
directional optical response
support/null and native stitch predicates
```

The eye reducer performs the unavoidable many-to-one first-hit/order reduction in
each retina using the same native stitch/ZD/bracket semantics. It outputs
disposable 2D RGB/depth/order targets.

Forbidden:

- baking `Ψ` into eye maps;
- using eye pixels as native identity;
- using display quantization as inverse evidence;
- deleting native modes because the current eyes do not expose them;
- requiring a persistent XYZ vertex, triangle, splat or meshlet world.

The eye path is intentionally cheap and may be visually lossy relative to export.
It must preserve binocular disparity, world lock, first-hit occlusion and
fold/two-sided separation. It cannot cap scanner or export quality.

---

# 23. Readout family B — scanner prediction

Sensor prediction compiles the same shadow descriptor at the sensor poses. It may
output:

```text
native germ/fibre key and generation
direct-order/depth interval
support/null predicate
predicted optical tuple
native relation/stitch signature needed for conservative candidate pruning
```

Prediction is disposable acceleration. It returns zero, one or multiple fibres per
measured footprint as required by §12. A first-hit raster may accelerate ordering,
but it cannot erase other conservatively viable preimages or allocate identity.

Deleting/rebuilding prediction cannot alter replay or canonical state.

---

# 24. Readout family C — rich textured 3D export

Export explicitly asks the full latest `Ψ` for a rich interoperable 3D drawing.
It is on demand and may be expensive.

## 24.1 Geometry

The descriptor owns a homogeneous geometry manifestation. If its proven generated
form is `G_M`, then:

\[
h=G_M(s),
\qquad
X(s)=\left(\frac{h_1}{h_0},\frac{h_2}{h_0},\frac{h_3}{h_0}\right).
\]

`G_M` is a readout of E22/native semantics, not a separately maintained geometry
model. Unsupported/null germs emit no contact.

## 24.2 Connectivity

Connectivity comes only from intrinsic stitch outcomes:

```text
REGULAR       may connect/interpolate
SINGULAR      preserve fold/boundary sides
NO_RELATION   never weld
UNRESOLVED    remain open/conservative
```

XYZ nearest-neighbour welding is forbidden.

## 24.3 Detail

Export evaluates the finest supported intrinsic gauge and requested local
manifestation modes. It may tessellate adaptively but may not decimate below the
requested evidence-supported threshold.

## 24.4 Appearance and texture

Export consumes the full native directional/eigenmode state plus all current
optical certificates. It may derive:

```text
view-stable base colour
high-resolution texture atlas
directional residual representation
normal/roughness/material proxies only when identifiable
confidence/evidence metadata
```

These are export products, never canonical texture state. Repeated passes can
improve geometry and texture because they constrain more native relations. Export
always reads the selected latest root, never an eye/prediction cache.

---

# 25. Readout family D — debug and analysis

Generated opt-in queries may expose:

```text
XYZ / depth / optical response
native key and generation
relation support and certificate age
exact gaps/conflicts
ZD/annihilator stratum
associator/bracket signature
stitch outcome/cache status
latent support/refinement rank
```

Debug buffers are disposable and read-only with respect to canonical resources.
Telemetry/timestamps never schedule or mutate.

---

# 26. Two semantic solves and physical GPU lowering

The canonical core has two semantic operations.

## 26.1 Ω — `INVERSE_NATIVE_22`

\[
\boxed{
\Omega(Y_t,\Psi_t)
=
\operatorname{NativeMinChange}
\left(
\mathcal C_{prior}
\cap
\bigcap_{q,p}\mathcal S_{q,p}^{-1}(O_{q,p})
\right).
}
\]

It owns sensor shadows, fibre association, exact native conjunction and one
candidate per germ/latent seed.

## 26.2 Ξ — `STITCH_COMMIT_NATIVE_22`

\[
\boxed{
\Xi(\Omega,\Psi_t)
=
\operatorname{RootLastCommit}
\left(
\operatorname{NativeStitch}_{22}(\Omega,\Psi_t)
\right).
}
\]

It owns dirty intrinsic stitching, latent resolution, sparse S16 deltas, evidence
receipts and root-last publication.

Readout is pure forward evaluation and not another reconstruction solve.

## 26.3 Initial physical kernels

```text
ProjectSensorShadow
ReduceSensorShadow
ConstrainNativeGerms
ResolveStitchOverflow          indirect, dirty cache misses only
ResolveLatentRelations         indirect/cold
PrepareChangedPages            touched pages only
ScatterChangedGerms
CloseAndPublishRevision        one bounded close, root exchange last
```

Eye readout:

```text
ProjectEyeShadow
ReduceEyeShadow
```

These may fuse after bit-parity and profiler evidence. They may not split into
sensor-specific or topology-specific physical solvers.

## 26.4 Work ownership

Logical owners are:

- one `ShadowRelationPacket` during projection;
- one complete native germ/latent key during conjunction;
- one intrinsic stitch key on cache miss;
- one changed germ during scatter;
- one touched logical page during backing preparation.

There is no persistent transaction, token scheduler, page lifecycle, singleton
proof owner or CPU work selection.

If one physical dispatch/window is insufficient, a generation-owned compact stream
and cursor continue it. Changing window size cannot repeat a whole logical domain
or alter output.

---

# 27. Runtime ABI

## 27.1 `NativeRelationDescriptorGpu`

Generated read-only tables/descriptors containing expression nodes, brackets,
signed-XOR actions, reverse contractor schedule, stitch plan and fingerprints.

## 27.2 `ShadowRelationPacketGpu`

One bounded packet with source/provenance key, fibre/latent key, relation mask,
exact Q16.48 ranges/predicate handles, first-hit/order and raw reference.

## 27.3 `GermCandidateGpu`

One record per owner:

```text
complete NativeGermKey
prior generation
candidate S16 Q16.48 state
outcome/changed mask
native relation witness/gap range
evidence range
incident stitch range
latent receipt
```

## 27.4 `NativeStitchGpu`

```text
endpoint native keys/generations
evidence generation
descriptor fingerprint
REGULAR/SINGULAR/NO_RELATION/UNRESOLVED
ZD/associator signature and exact residual
independent-support receipt
```

It is a disposable cache/certificate reference, not a topology object.

## 27.5 `GermDeltaGpu`

The only canonical mutation record defined in §17.

No ABI field may overload pixel, pending slot, page and carrier identity.

---

# 28. Capture admission and host responsibilities

Capture pairing may produce coherent candidates faster than canonical closure can
consume them. The system distinguishes:

```text
CAPTURED_CANDIDATE
    may be deterministically sampled/decimated before admission

CANONICALLY_ADMITTED
    owns the complete coherent observation until PUBLISHED, NO_CHANGE,
    RETAINED_UNRESOLVED or FAULT
```

An admitted observation cannot be silently overwritten by a later latest frame.
Sensor ingress itself never waits for reconstruction; admission is bounded and
observable.

C# owns:

- lifecycle, capture admission and resource leases;
- calibration/descriptor epoch selection;
- GPU buffer/page/evidence residency;
- command recording and fences;
- immutable revision/readout leases;
- asynchronous persistence/export orchestration;
- truthful completion/fault reporting.

C# does not inspect pixels, select fibres, decide native closure/stitch/gauge,
construct meshes or repair topology.

---

# 29. Determinism and decomposition invariance

Persist fingerprints for:

```text
NumericDomain
signed-XOR multiplication/conjugation
annihilator catalog
NativeRelationDescriptor/E22 bracket DAG
forward/pullback/stitch generated plans
minimum-change selector/tie order
codec schema
```

For the same accepted observation sequence, the following are byte-identical under
every legal source order, workgroup shape, dispatch partition, storage segmentation,
page size/placement, cache hit/miss and proof scratch-window size:

```text
Psi pages and generations
native validity/gaps/conflicts
certificates/provenance
latent seed/chart/promotion order
stitch classes/signatures
gauge allocation
published root sequence
```

Physical segment count may change buffer bindings only. It may not change the
number of evaluations of a whole logical sensor/germ/stitch domain.

Stable ordering is required only where output identity/allocation depends on
order. The order key is complete native key plus complete provenance; pixels/pages
are not tie authorities.

Any optimized generated plan differing by one bit from its semantic reference is
disabled for canonical mutation.

---

# 30. Performance contract and Release telemetry

Cost follows new information and active locality:

```text
sensor projection            admitted active footprints/fibres
native inverse               unique candidate germ/latent owners
stitch                       dirty intrinsic cache misses
publication                  changed germs/touched pages
eye readout                  current visible/query-active fibres
export                       explicit requested region/quality only
```

Forbidden cost scaling:

```text
total persisted world per frame
image footprints × backing segments
all optical image edges
revision count / pending window count
page count as topology domain
proof minimization in foreground visibility gate
```

S4‑08.6 acceptance for the frozen 320×320 Quest fixture:

```text
old BuildDepthSourceCells / BuildRgbSourceCells      absent
old EvaluateCandidateMeets provider loop             absent
old ClosePendingEdges / label closure                absent
old two global target sorts                          absent
old 23-kernel publication graph                      absent

Ω + Ξ measured compute                              <= 1500 ms
admission-to-completion wall                         <= 1800 ms
30-revision steady-state drift                       <= 1%
segment-decomposition semantic work-count change     0
```

Direct eye readout is independently gated at target headset refresh without
requiring scan closure at display rate. Export has no live-frame budget.

Release diagnostics use one-shot actual Vulkan timestamp markers around production
dispatches with minimal asynchronous readback. Report:

```text
per-kernel dispatch count, records and GPU time
active shadow packets/fibres/native owners
changed/unchanged/conflict/unresolved/exclusion outcomes
stitch proposals/cache hits/misses/classes
latent projected/evaluated/reused/absorbed/promoted/aborted
changed germs/touched pages/root/fault
resident carrier/evidence/readout bytes
owned ingress and oldest admitted age
descriptor/operator operation counts
```

Telemetry never controls work. A diagnostic timestamp sample contaminates its own
wall time and is labeled accordingly.

---

# 31. Persistence schema

Persist:

```text
world/manifest
    schema = CPQ4-2026-08-25-S16-v8
    NumericDomain fingerprint
    signed-XOR/algebra fingerprint
    NativeRelationDescriptor/E22 fingerprint
    generated plan fingerprints
    native null state
    calibration/query descriptor epochs
    selected world revision/root

world/carrier
    sorted sparse logical page generations
    exact NULL/CONST/AFFINE/DELTA/RAW payloads
    minimal NativeRelationCertificates
    intrinsic stitch certificates required by the selected root

world/latent
    unresolved LatentGerm local charts/regions/evidence refs

world/observations
    unresolved compressed raw relation tiles only

world/derived       optional/deletable
    eye/prediction/debug/export caches
```

Durable publication precedes eviction. Restart plus the same accepted observation
sequence produces byte-identical pages, certificates and roots.

---

# 32. Repository architecture and hard deletion boundary

Active code remains only under:

```text
Runtime/SigmaPrism
Runtime/Resources/SigmaPrism
```

Preserve/reuse:

```text
capture/sync/calibration/pose infrastructure
SigmaNumericDomain and exact backend gate
generated signed-XOR S16 primitives
SigmaCarrier and exact codec/storage
GPU completion/fence/indirect helpers
root-last immutable publication primitive
one-shot Release timing infrastructure
XR lifecycle/UI/anchors
GLB encoding plumbing
```

Generate/replace with a small native core:

```text
SigmaNativeRelationDescriptor.*          generated C#/HLSL
SigmaNativeClosure.compute               Ω and native stitch/latent lowering
SigmaNativeCommit.compute                sparse root-last commit
SigmaNativeReadout.compute/shader         sensor and eye query lowering
SigmaNativeGraph.cs                       fixed recorder, lifecycle-free
SigmaNativeResources.cs                   packets/germs/stitches/journal scratch
```

Hard-delete after parity/cutover:

```text
SigmaFrameInverse.compute
SigmaFrameClosure.compute
SigmaFramePublish.compute
old SigmaFrameGraph/SigmaFrameResources implementation
sensor-specific inverse math/live APIs superseded by descriptor
separate topology math/controller superseded by stitch
pending projection/labels/links/retention physical ontology
global target sorting circuits superseded by native owner reduction
global NOVEL bbox/pixel continuation mapping
page halo continuity and live persistent XYZ/mesh reconstruction caches
```

Gross production deletion from `cac9ab0` must be at least 10,000 lines. New
production additions before device acceptance are capped at 4,000 lines. Final
`Runtime/Resources` diff is at most `-6,100` lines versus `cac9ab0` and at most
`-5,500` versus `d3b83e1`. Generated descriptor tables and tests are reported
separately and cannot hide orchestration growth.

---

# 33. Forbidden architecture violations

The implementation is invalid if it introduces or retains:

- canonical geometry/mesh/voxel/splat/texture/topology/object/history beside `Ψ`;
- independently editable 22-edge state;
- depth-conditioned RGB pullback or any cross-source pre-contraction;
- sensor weights/averaging;
- hardcoded HIT or behind-hit evidence;
- one-winner candidate pruning without a conservative proof;
- unconditional physical NOVEL allocation;
- pixel/image/XYZ/page/segment identity;
- optical full-frame edges as intrinsic topology;
- generic dense S16 loops where generated sparse plans exist;
- changed-state publication before evidence/stitch closure;
- page/segment flags bypassing the selected root;
- fixed journal/proof/session caps;
- CPU pixel/native decision loops or synchronous readback;
- runtime legacy/fallback graph;
- eye/export/readout quantization fed back into canonical closure.

---

# 34. Exact unit and oracle gates

## 34.1 Native descriptor gate H0

Before live v8 mutation:

1. all 22 TOE relation expressions and brackets are present and fingerprinted;
2. every relation forward evaluator matches the semantic oracle bit-for-bit;
3. shared-CSE on/off outputs are identical;
4. reverse contractor is sound: no mathematically admissible Q48 fixture state is
   excluded by its enclosure;
5. every accepted inverse state forward-satisfies every original predicate;
6. bounded incompleteness yields unresolved, not false accept/reject;
7. `StitchNative22` matches its reference for regular, exact ZD, near-singular,
   associator and no-relation fixtures;
8. CPU and Vulkan packed-32/native-I64 enabled paths are bit-identical;
9. descriptor/plan fingerprints are stable;
10. no handwritten duplicate physical equation is live.

## 34.2 Numeric/algebra gates

- exhaustive basis signed-XOR parity;
- conjugation identities;
- all canonical zero-divisor pairs and dyad actions;
- explicit associator bracket fixtures;
- checked nearest-even/outward arithmetic extremes;
- exact codec round trips;
- cache hit/forced-miss parity;
- mask/select and work partition parity.

## 34.3 Inverse gates

- left/right and source-order permutation invariance;
- actual right-eye reprojection;
- multiple candidate fibres at one pixel;
- RGB native pullback independence from depth/prior;
- HIT/PRE_HIT_EXCLUSION/NO_CLAIM;
- no behind-hit mutation;
- hidden-mode/fibre preservation;
- weak-after-strong and strong-after-weak;
- explicit conflict/gap preservation;
- one owner/no multiple writer;
- execution-window and segment invariance.

## 34.4 Stitch/latent gates

- changed supported↔supported incident edges included;
- optical/XYZ proximity produces no claim;
- stable regular/fold/thin/no-relation/unknown outcomes;
- independent-view singular signature stability;
- cache key invalidates only on endpoint/evidence generation change;
- repeated latent observation reuses one latent germ;
- side reveal absorbs into compatible intrinsic branch;
- 5 mm parallel sheets remain distinct;
- image resolution and backing decomposition do not change local chart/promotion;
- stale generation cannot absorb/promote a reused latent handle.

## 34.5 Publication/evidence gates

- CHANGED-only scatter;
- UNCHANGED evidence strengthening without page generation;
- one root exposes all-or-none multi-page/multi-segment revision;
- failure leaves root and current pages byte-identical;
- frame slot recycle cannot invalidate journal;
- deterministic certificate minimization across window sizes;
- no source duplication per page;
- genuine pressure backpressures/fails closed without false commit.

---

# 35. Physical acceptance corpus

## 35.1 Scan/refinement

```text
same wall: 1 / 5 / 20 passes
front + grazing views
left/right asymmetric information
5/10/20/50 mm sheets, both sides different colours
fold, door frame, recess, alcove
pipe, railing, stairs, multi-floor loop
subpixel relief and printed high-frequency texture
approach artifact then reveal valid empty/pass-through
```

Expected:

- native region tightens or stays stable;
- previously learned hidden modes survive;
- no false duplicate extents;
- detail improves before/through justified gauge refinement;
- near manifestations remain different fibres when not stitchable;
- latency does not grow with revision or backing segments.

## 35.2 XR eye readout

```text
left/right disparity
head translation/rotation
fold crossing
thin two-sided sheet
occlusion
sleep/wake/resume
cache deletion/rebuild
```

Expected: stable world-locked eyes, correct order/fold separation, no live mesh
authority and target refresh independent of scan cadence.

## 35.3 Export

Export after one pass, multiple passes, local refinement, opposite-side scan and
restart. Geometry, non-welded connectivity and texture/material quality must improve
with accumulated native evidence. Export may never be limited by eye-cache quality.

## 35.4 Scene evolution

Add object, temporary person occlusion, remove object, move same object, open/close
door and replace with a different object. No behind-hit damage, averaging or
semantic object identity is allowed.

---

# 36. S4‑08.6 deterministic closure sequence

S4‑08 remains open. Prior S4‑01…S4‑07 results are primitive evidence only where
they match v8; their v7 sensor/proposal/topology compositions have no grandfathered
authority.

## N0 — canonical rebase

- publish v8 spec, `analyza.md`, this plan and compact resume cursor;
- freeze descriptor schema and external TOE/E22 provenance requirement;
- runtime unchanged;
- controls/code graph/Markdown/diff gates;
- commit separately.

## N1 — descriptor and four evaluators

- generate `ForwardNative22`, `PullbackNative22`, `StitchNative22`, oracle;
- pass §34.1/34.2;
- no live mutation/cutover;
- commit separately.

## N2 — native shadow/pullback oracle

- implement sensor packets and non-mutating native owner output;
- pass inverse candidate/source-independence/first-hit/fibre fixtures;
- captured forward parity where semantics are shared;
- commit separately.

## N3 — carrier-pull owner cutover

- live Ω cutover;
- simultaneously delete proposals, four source worlds, provider×segment evaluation
  and replaced global target sort/reduction;
- no fallback;
- production LOC becomes negative;
- commit separately.

## N4 — native stitch cutover

- dirty intrinsic/evidence cache misses only;
- delete optical edge universe, label propagation, XYZ qualifier and separate
  topology subsystem in the same commit;
- pass stitch/physical corpus fixtures;
- commit separately.

## N5 — latent native relation cutover

- replace pending/continuation/novel ontology with `LatentGerm`;
- delete pending projection winner, pixel chart, global bbox and pending SoA
  lifecycle;
- pass reuse/absorption/thin/chart/decomposition gates;
- commit separately.

## N6 — sparse commit/evidence cutover

- touched-page root-last commit and generation-safe journal/certificate ownership;
- delete duplicate page map/sort/scan graph and old frame graph/resources;
- pass atomicity/recycle/minimization/pressure gates;
- commit separately.

## N7 — pure readout cutover

- direct sensor and eye shadows from descriptor;
- remove page halo/live persistent XYZ/mesh authority after parity;
- export consumes full `Ψ` on demand;
- commit separately.

## N8 — hard deletion and physical closure

- gross/net LOC gates in §32;
- zero retired symbols/assets/calls/fallbacks;
- generated code graph and control validation;
- exact full Vulkan tests and Release compile;
- source archive, APK build and install from the same commit;
- capture complete Release per-kernel evidence;
- pass §30 and §35 physical gates;
- only then mark S4‑08 done and stop before S4‑09.

At most one run is `in_progress`. Each run ends in one commit. A failed gate is
fixed inside the same native descriptor/lowering; it cannot introduce a subsystem
outside Ω/Ξ.

---

# 37. Subsequent implementation sequence

After accepted S4‑08:

```text
S4-09   scene evolution using native relation/exclusion/transport closure
S4-10   durable infinite paging, eviction, rehydrate and restart
S4-11   direct XR readout quality/culling completion if not closed in N7
S4-12   rich textured 3D/PBR/GLB export from full latest Psi
S4-13   complete Quest correctness/quality/scale/performance corpus
```

These nodes may refine lowerings and readouts. They may not add a second physical
world or change the frozen native descriptor without a schema/fingerprint revision
and complete replay migration.

---

# 38. Definition of done

Complete means physically demonstrated on Quest 3:

```text
Psi : Sigma_2 -> S16 is the only canonical world
all 22 native relations share one S16 preimage and one frozen descriptor
forward, inverse, stitch and oracle come from that descriptor
canonical arithmetic and decisions are exact Q16.48
independent RGB-D/views constrain one native state without summation
first-hit has exact pre-hit/hit/behind-hit semantics
hidden native modes survive partial observations
revisits tighten/refine state without coarse degradation
thin/parallel/fold manifestations preserve native separation
topology is native stitchability, never XYZ/image/page proximity
latent identity/chart is intrinsic and reused before promotion
only GermDelta mutates canonical state
evidence remains reproducible through journal/certificate handoff
publication is immutable and root-last
pages/segments/residency never affect physics or work cardinality
direct stereo eye maps are pure/disposable and frame-rate safe
rich textured export reads full latest Psi, never preview cache
restart reproduces identical pages/certificates/relations/readouts
no retired v7 graph or fallback remains
production LOC and measured performance pass §30/§32
the full physical corpus passes from the exact installed commit
```

A compile, synthetic fixture, visible point cloud or advancing root alone is not
acceptance.

---

# 39. Architectural summary

The physical direction is

\[
S16
\xrightarrow{E_{22}}
\text{Merkaba/eigenmode relation atlas}
\xrightarrow{\mathcal M}
\text{manifestation}
\xrightarrow{\Pi_q}
\text{sensor/readout shadow}.
\]

The scanner follows the inverse causal direction by exact constraint pullback:

\[
\boxed{
\mathcal A_{RGBD}
=
\bigcap_q
(\Pi_q\circ\mathcal M\circ E_{22})^{-1}(O_q),
\qquad
\Psi_{t+1}=\Xi(\Omega(Y_t,\Psi_t),\Psi_t).
}
\]

Neighbour relations use the same vocabulary through `StitchNative22`. Eye,
prediction, export and debug are pure forward queries of the same state.

The crucial invariant is:

\[
\boxed{
\textbf{a readout can be simple without making the world simple.}
}
\]

Elegance is achieved by deleting duplicated representations and solvers, not by
discarding native information.
