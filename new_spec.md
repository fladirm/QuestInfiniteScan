# Σ-PRISM-16

## Pure-Quest 3 holistic sedenion carrier reconstruction

**Canonical replacement specification**  
**Canonical reconstruction baseline:** `CPQ4-2026-08-24-S16-v7`
**Target device:** Meta Quest 3  
**Implementation target:** Unity / Vulkan / GPU-first, no server reconstruction  
**Status:** canonical implementation specification; v7 retains the v6 exact
NumericDomain/operator contract and replaces image-tile/page transaction semantics
with one direct whole-observation inverse lowering. Execution partition, storage
pages, proof windows and scheduling never allocate physical identity or define a
canonical publication boundary.

---

# 0. Authority and scope

This document is self-contained and defines the complete reconstruction core.

The implementation target is one holistic sedenion carrier state. Existing repository code may be reused only when it is representation-neutral infrastructure: synchronized sensor acquisition, calibration, XR lifecycle, Vulkan resource plumbing, asynchronous storage, renderer plumbing and export utilities.

The live reconstruction core is rewritten to this specification. Historical reconstruction ontologies, compatibility wrappers and comparative baselines are not part of the implementation contract.

Temporary GPU queues, page tables, render targets, meshlets, compacted work lists and staging buffers are implementation caches only. They may always be deleted and regenerated without changing the canonical world.

# 1. Product invariant

Quest 3 supplies synchronized and calibrated observations:

```text
RGB_L(t)          RGB_R(t)
DEPTH_L(t)        DEPTH_R(t)
POSE_L(t)         POSE_R(t)
K_RGB_L/R         K_DEPTH_L/R
fixed sensor extrinsics within one calibration epoch
```

Walking with the headset continuously produces one persistent world state from which the implementation can read out:

```text
unbounded persistent 3D world
true two-sided / thin geometry
arbitrary visible topology
realtime prediction and mesh preview
revisit-driven geometric refinement
sub-depth RGB/stereo/temporal refinement
measured multi-view texture superresolution
directional appearance
confidence-bearing PBR
direct GLB export
restart / revisit continuation
```

All live reconstruction remains on Quest 3. The reconstruction state and update path are GPU-resident; persistence/export may use asynchronous staged copies.

---

# 2. Hard non-negotiable invariants

1. **One canonical object.** The reconstructed world is the single field `Ψ`. Geometry, topology, boundaries, layers, uncertainty, detail, appearance, motion state, mesh and PBR are readouts or transient inverse constraints, not parallel canonical worlds.
2. **First-hit causality.** A valid depth observation constrains only the pre-hit path and the first supported hit. It supplies exactly zero information behind that hit.
3. **UNKNOWN is not EMPTY.** Unobserved carrier remains in the latent/null sector. No observation manufactures empty 3D volume behind a first hit.
4. **No destructive contradiction fusion.** Incompatible observations retain incompatible admissible sets and provenance. They never cancel through arithmetic averaging.
5. **Confidence is admissibility width.** Sensor confidence narrows or widens the inverse set contributed by that source. It is never a sensor weight in a sum.
6. **One-sided and thin geometry is native.** Distinct carrier coordinates may read out arbitrarily close in 3D and remain distinct without a merge rule.
7. **Lower-information evidence cannot erase supported higher-information state.** A broad admissible set cannot move a state already contained by a narrower supported prior, and a broad finite footprint cannot impose variation it does not resolve.
8. **The canonical domain is the 2D carrier.** No fixed 3D reconstruction lattice or 3D occupancy state exists.
9. **Renderable geometry is derived.** Triangles, meshlets, normals, boundaries and component graphs are disposable readout products.
10. **Topology is intrinsic to `Ψ`.** Observable folds, terminations and branch changes are singular transitions of the same sedenion carrier; they are not independently allocated topology objects.
11. **Carrier topology is conserved.** Scene holes, disconnections and moving surfaces are changes of observable/null readout and carrier immersion, not deletion of carrier material.
12. **The durable world is the current supported scene.** A proven physical scene change may move a carrier region, return it to null, or pull latent carrier into contact. Old and new configurations are never averaged into a compromise geometry.
13. **Identity-preserving transport precedes retire/recreate.** When one coherent carrier transform explains disappearance at an old location and appearance at a new location while preserving intrinsic state, the same carrier region is transported and retains its accumulated detail.
14. **No semantic object ontology is required.** Temporal transport uses carrier coherence and inverse-readout constraints, not object labels such as door, chair or cabinet.
15. **One arithmetic state.** Canonical sedenion coefficients and every state-changing algebraic decision use deterministic Q16.48. Floating-point readout caches may propose work but may not decide canonical acceptance.
16. **No sensor summation.** `RGB_L`, `RGB_R`, `DEPTH_L`, `DEPTH_R` and retained temporal observations remain independent constraints until their exact common admissible set is formed.
17. **GPU-first locality.** No synchronous GPU readback, CPU pixel loop, CPU meshing or full-world live traversal. Active cost follows visible, constraint-active, temporally unresolved and dirty carrier locality.
18. **Paging is not physics.** Physical page/block boundaries have zero reconstruction meaning and may never create seams, frontiers or identities.

# 3. Canonical mathematical object

The world state is

\[
\boxed{\Psi:\Sigma\rightarrow\mathbb S_{16}}
\]

where:

- `Σ` is one logically unbounded two-dimensional carrier sheet;
- `S16` is the real 16-dimensional sedenion algebra;
- each carrier coordinate `ξ=(u,v)` has one sedenion state `Ψ(ξ)`;
- the entire scan is this one field, not a collection of independent surface objects.

The implementation uses the one-point compactification conceptually:

\[
\Sigma\simeq \mathbb R^2\cup\{\infty\}.
\]

All unallocated carrier area is implicitly in the same null state. The null state has no physical 3D contact readout. Therefore the logically infinite latent carrier costs zero memory until information reaches it.

The carrier is analogous to one enormous sparse bitmap whose pixels contain sedenion state rather than RGB. The bitmap may fold, self-overlap, reverse orientation, enter a null sector, or project multiple distant carrier regions into nearby 3D positions. Carrier adjacency is not equivalent to Euclidean 3D proximity.

---

# 4. Meaning of “whole scan is one 16D object”

Σ-PRISM does **not** mean that an entire building is represented by only sixteen scalar numbers.

It means that there is one field whose value space is sixteen-dimensional:

\[
\Psi\in C(\Sigma,\mathbb S_{16}).
\]

A million carrier samples are samples of one continuous object, just as a bitmap is one image rather than a million unrelated point objects.

No per-sample physical identity object is required for meaning; carrier coordinate plus state is sufficient.

The scanner does not reconstruct 3D first and then organize it. It reconstructs `Ψ`; 3D is one readout of `Ψ`.

---

# 5. Exact sedenion algebra and deterministic operator lowering

## 5.1 Canonical NumericDomain

Σ-PRISM does not define a new fixed-point arithmetic system. It adopts the already-established FLUID/T-language deterministic domain **verbatim in semantics**:

```text
NumericDomain = num.fixed.q16_48.checked.nearest_even
signed         = true
int_bits       = 16
frac_bits      = 48
storage_bits   = 64
rounding       = NearestEven
overflow       = Checked
scale          = BinaryPower
ONE            = 1 << 48
real(q)        = q / 2^48
range          = [-32768, 32768)
```

The canonical doctrine is:

```text
NumericDomain is semantic truth.
Storage and execution formats are lowerings.
```

Accordingly:

- `Ψ`, fixed-domain scalar thresholds and exact proof/certificate bounds use raw signed Q16.48 values; discrete counts, masks, indices and shift gates remain exact integers/bitfields;
- FP16/FP32 never become canonical state;
- the physical meaning of a Q16.48 value is independent of whether one backend executes it as native integer arithmetic, packed 32-bit limbs, or another proven exact lowering;
- backend representation bytes that are not persistence bytes have no physical meaning;
- changing the execution lowering without changing the NumericDomain must not change canonical output bytes.

Sensor/readout values are quantized exactly once when they enter a canonical inverse constraint. FP render/prediction caches may exist after the canonical decision boundary.

## 5.2 Exact primitive semantics and backend legality

The semantic primitive set is:

```text
qadd(a,b)       exact signed add; checked overflow
qsub(a,b)       exact signed subtract; checked overflow
qmul(a,b)       Q16.48 product, NearestEven, checked overflow
qdiv(a,b)       Q16.48 quotient, NearestEven, checked overflow
qabs(a)
qclamp(a,lo,hi)
qshl/qshr(a,n)  exact checked/dyadic shifts where legal
qmul_lo/qmul_hi(a,b)     outward interval product bounds
qdiv_lo/qdiv_hi(a,b)     outward interval quotient bounds
qisqrt(a)                deterministic non-negative integer square root helper
```

Point arithmetic uses round-to-nearest, ties-to-even. Interval arithmetic rounds outward, so a computed admissible cell never excludes a mathematically admissible state because of quantization. Overflow is a failed candidate/update, never saturation.

The CPU reference implementation and every Quest backend lowering must implement these same semantics. The backend first declares a capability/legality profile for every primitive/operator it emits. A canonical kernel may run only when its exact lowering is proven by fixture parity.

Execution lowering is selected per operator, not globally. The mandatory order is:

```text
1. generated specialized exact lowering when the operator reduces to
   dyadic/sign/XOR-permutation/mask/readout operations and therefore needs no generic qmul/qdiv
2. native exact signed-64 execution for remaining coefficient arithmetic when proven legal
3. exact packed-32 execution with explicit widened operations for those remaining primitives when native I64 is unavailable
```

These are execution choices, not different numerical models. No source file may encode physical logic that depends on which choice was selected.

The implementation must **not** begin by writing a new universal software-I128 arithmetic subsystem and then route every Σ operation through it. A widened CPU oracle or a packed-32 Quest helper is permitted where a true coefficient product/division requires it, but it is only a reference/lowering primitive. Exact sign changes, XOR-address permutations, Hadamard projections, dyadic scaling, comparisons, interval meet, masks and annihilator-dyad actions must bypass generic multiply/divide.

A backend that cannot prove an exact lowering for an operator fails closed for canonical mutation. It may continue non-authoritative visualization/readout work.

## 5.3 Basis and signed-XOR Cayley-Dickson multiplication

Use the real basis

\[
e_0=1,e_1,\ldots,e_{15}.
\]

Basis addresses are four-bit Cayley-Dickson addresses. The generated multiplication law is represented canonically as

\[
\boxed{e_i e_j=\varepsilon_{ij}e_{i\oplus j}},
\qquad \varepsilon_{ij}\in\{-1,+1\}.
\]

The address operation is bitwise XOR; the generated sign table carries the orientation/bracketing-sensitive sign geometry. This signed-XOR form is the runtime operator substrate.

For specification/reference equivalence, represent a sedenion as a pair of octonions

\[
s=(a,b),\qquad a,b\in\mathbb O
\]

with exactly

\[
(a,b)(c,d)=\left(ac-d\bar b,\;\bar a d+cb\right),
\]

and

\[
\overline{(a,b)}=(\bar a,-b).
\]

A build-time generator recursively emits:

```text
mulBasis[i][j] -> (sign, i XOR j)
conjugateSign[i]
left/right basis permutations
operator descriptors used by section 5.7
```

The recursive Cayley-Dickson definition and the generated signed-XOR table must agree exhaustively. No hand-maintained multiplication table is allowed.

A generic dense `S16Mul(a,b)` may exist as a correctness oracle and as a generated fallback for the few operators that genuinely require a dense coefficient-coefficient product. It is **not** the default implementation vocabulary of the scanner hot path.

## 5.4 Explicit bracketing

Sedenions are non-associative. Every product of more than two factors has explicit source-code bracketing.

Forbidden:

```text
a*b*c
```

Required semantic forms:

```text
mul(mul(a,b),c)
mul(a,mul(b,c))
```

A generated optimized operator may fuse either expression, but its descriptor records the original bracket tree and its exact fixture output must equal that tree. Optimization may never erase bracket identity.

The associator is intentionally the difference of the two bracketings; unspecified evaluation order never carries physical meaning.

## 5.5 Exact zero divisors and annihilator catalog

The generator exhaustively enumerates canonical sparse signed dyads

\[
z=\pm e_i\pm e_j,
\qquad
a=\pm e_k\pm e_l
\]

and records every exact pair satisfying

\[
z\ne0,\qquad a\ne0,\qquad za=0
\]

under the generated signed-XOR multiplication law.

All dyad coefficients are exactly `+ONE` or `-ONE`, so these identities are bit-exact in Q16.48.

The catalog is canonical, sorted lexicographically and fingerprinted. Each entry stores only basis indices/signs and the generated permutation descriptors needed to evaluate it.

For transition state `t`, annihilator evidence against witness `a_k` is

\[
E_k(t)=\sum_{j=0}^{15}|(t a_k)_j|.
\]

Because `a_k` is a signed basis dyad, `t a_k` is **not** lowered through generic `S16Mul`: it is exactly two signed/XOR-indexed permutations of the sixteen lanes followed by add/sub and L1 accumulation. No Q16.48 scalar multiplication or division is legal in this witness action.

Define

```text
annihilatorId(t)    = lexicographically first argmin_k E_k(t)
annihilatorError(t) = min_k E_k(t)
```

`E=0` is an exact zero-divisor relation. A measured near-singular transition uses only persisted integer relative gates and must remain stable across independent observations before it changes canonical topology readout.

## 5.6 Left/right operators and associator

Define

\[
L_a(x)=ax,\qquad R_a(x)=xa
\]

and

\[
[a,b,c]=(ab)c-a(bc).
\]

These symbols specify semantics. Runtime implementation uses the generated operator lowering of section 5.7.

Long physical transformations are composed as explicit left/right operators. Non-associativity is used only through requested bracketed operators/associators; it never leaks from implementation order.

The canonical associator score is integer:

\[
A(a,b,c)=\sum_j |[a,b,c]_j|.
\]

Normalization, when required for a gate, uses deterministic Q16.48 division by an L1 state scale. No floating norm is required for topology decisions.

## 5.7 Generated exact operator IR — mandatory hot-path form

The established hyperlinearization rule is adopted directly: **semantic operator truth is separated from execution lowering**. Scanner physics is first expressed as a finite exact operator/region graph; only then is it lowered to Quest compute kernels.

The Σ operator generator accepts the fixed algebra/readout descriptors and emits an exact DAG using only this vocabulary:

```text
XOR_INDEX / PERMUTE
SIGN / NEGATE
ADD / SUB
SHIFT
CMP / MIN / MAX
MASK / SELECT
GATHER / SCATTER
FIXED_BOUNDED_REDUCTION
QMUL / QDIV              only when the exact operator really requires them
INTERVAL_MUL / INTERVAL_DIV only while constructing conservative cells that require them
```

Mandatory generated operators include at least:

```text
conjugation
Hadamard B / B^T readout transforms
geometry rows G and hidden rows F
basis left/right actions
signed-dyad annihilator actions
view-operator specialization for the sparse quaternionic nu(omega)
transition tau operator
associator gate operator
projective-cell meet/commit transforms
codec predictors / exact mode predicates
```

Rules:

1. **No schoolbook-by-default.** A nested `for i in 0..16 / for j in 0..16` dense multiplication is a test/reference form, not an accepted default hot-path lowering.
2. **Signed-XOR first.** Any operator whose coefficients reduce to basis address XOR, sign, permutation, dyadic scaling or lane selection is emitted only in that form.
3. **One transition evaluation per generation pair.** For neighbouring carrier samples, the derived transition descriptor/cache is keyed by the two endpoint page/block generations. `tau`, annihilator signature and any reusable bracketed intermediates are recomputed only when an endpoint state generation changes. The cache is disposable and never canonical.
4. **Common-subexpression sharing.** A generated operator plan shares exact intermediate products across all outputs/witnesses in that invocation. Recomputing the same coefficient product independently for multiple observables is forbidden.
5. **Masked control.** Bounded conditionals become predicate masks/selects. Inactive lanes do not execute canonical mutation merely because they share a dispatch.
6. **Fixed bounded schedules.** Hot-path loops have compile-time or dispatch-bounded iteration counts. Dynamic object/graph traversal is not an algebra primitive.
7. **Reference equivalence.** Every optimized generated plan has a slow semantic reference evaluation. Exact outputs must be bit-identical for fixed-point classes; no tolerance is permitted.
8. **Fingerprinting.** NumericDomain ID, signed-XOR table, bracket descriptors and generated operator-plan fingerprint are persisted/tested. A changed plan is legal only if exact semantic equivalence is proven; a changed semantic descriptor requires a schema/operator revision.
9. **Backend non-authority.** Native-I64, packed-32 Vulkan/HLSL, subgroup scheduling and dispatch shape may differ without changing operator truth.

This is the required implementation model. Codex must not reinterpret Σ-PRISM as sixteen ordinary scalar channels executed by a generic linear-algebra library.

## 5.8 Inherited implementation provenance — no external dependency

The rules in sections 5.1, 5.2, 5.7 and 35.2 are not an invitation to redesign the numeric/runtime substrate. They restate the already-used project doctrine in Σ-PRISM terms:

```text
FLUID / T-language:
    q16_48 checked nearest-even deterministic NumericDomain
    semantic numeric truth separated from storage/execution lowering
    exact backend legality/parity gates

T-language hyperlinearization:
    exact typed/operator IR
    masks/selects instead of bounded branch authority
    fixed recurrence/reduction schedules
    dense/tiled/gather/scatter memory forms
    backend execution shape non-authoritative

Projection-Algebra / Omega-family algebra tooling:
    Cayley-Dickson basis address = XOR
    sign/orientation = generated epsilon table
    signed-XOR left/right action and sparse operator evaluation
```

This specification is self-contained: the implementation does not require those repositories at runtime and Codex must not depend on undocumented behavior from them. Their role is provenance for the rules restated here. If current source already contains a bit-exact implementation of one of these primitives, reuse/port it and prove equivalence; do not replace it merely to make the code look local to Σ-PRISM.

# 6. One state, several readouts

`Ψ` is not divided into semantic scalar slots such as `xyz + normal + rgb + confidence`.

Physical quantities are readout operators applied to the same state.

For implementation efficiency Σ-PRISM defines fixed orthogonal operator subspaces, but these are measurement/readout gauges, not separate canonical objects.

## 6.1 Exact generated readout basis

Generate the unnormalized signed Walsh-Hadamard table

\[
B_{r,c}=(-1)^{\operatorname{popcount}(r\land c)}\in\{-1,+1\}.
\]

No Gram-Schmidt or floating-point normalization is used.

Choose `z_null` as the lexicographically first zero-divisor witness `z` from section 5.5 whose catalog also supplies a non-zero annihilator.

Select geometry rows `g0..g3` as the first four Hadamard rows satisfying the exact integer condition

\[
g_r\cdot z_{null}=0.
\]

Hadamard rows are mutually orthogonal, so no orthogonalization step is required. The remaining twelve rows are the hidden/readout rows `F`.

The NumericDomain ID, generated row indices, multiplication-table fingerprint, annihilator-catalog fingerprint, operator-plan bundle fingerprint and `z_null` raw Q16.48 coefficients are persisted in the world header.

All basis projections are signed additions/subtractions. Multiplication by `1/4` is unnecessary canonically; optional normalized FP32 render values are derived only after projection.

## 6.2 Geometry readout

For Q16.48 state `s` compute homogeneous geometry natural coordinates by exact signed accumulation:

\[
h=Gs=(h_0,h_1,h_2,h_3)^T.
\]

A carrier point has supported geometry when

\[
h_0>\tau_{contact}.
\]

Its 3D readout is

\[
\boxed{
X(s)=\left(\frac{h_1}{h_0},\frac{h_2}{h_0},\frac{h_3}{h_0}\right)
}
\]

in world metres. Canonical division uses `qdiv`; renderer caches may convert the resulting ratio to FP32.

`h0` is simultaneously the homogeneous scale / geometric information mass. Multiplying all four homogeneous coordinates by the same positive factor leaves `X` unchanged while making later weak updates less able to move it.

The initial null state satisfies `G z_null = 0`, hence has no finite 3D contact readout.

## 6.3 Geometry differential readout

Normal is not stored.

For carrier derivatives

\[
\Psi_u=\partial_u\Psi,\qquad\Psi_v=\partial_v\Psi,
\]

compute

\[
X_u=D X[\Psi]\Psi_u,
\qquad
X_v=D X[\Psi]\Psi_v,
\]

and

\[
N=\frac{X_u\times X_v}{\|X_u\times X_v\|}.
\]

If the differential is singular or below support, no trusted normal is emitted.

Curvature, tangent metric and orientation are differential readouts, not persisted duplicate fields.

## 6.4 Appearance readout

Appearance is another projection of the same `s`.

Let the twelve hidden signed projections be `f = F s` in Q16.48.

In hidden coordinates `f=Fs`, define `a0,aR,aG,aB` as the first four coordinate functionals of that **generated hidden basis**. They are not raw sedenion coefficient slots: `F` already mixes the original sixteen algebra coordinates. The remaining eight hidden coordinates remain available to the view operator, directional residual and contradiction/singularity structure.

View direction `ω` in the local camera/world gauge is lifted into the associative quaternionic subspace

\[
\nu(\omega)=\omega_xe_1+\omega_ye_2+\omega_ze_3.
\]

Define the explicitly bracketed view operator

\[
T_\omega(s)=\nu(\omega)\,(s\,\overline{\nu(\omega)}).
\]

Project its hidden component and read colour projectively:

\[
q(\omega)=F T_\omega(s),
\]

\[
w_A=\max(|a_0^Tq|,\epsilon_A),
\]

\[
C_R=\operatorname{sat}\frac{a_R^Tq}{w_A},\quad
C_G=\operatorname{sat}\frac{a_G^Tq}{w_A},\quad
C_B=\operatorname{sat}\frac{a_B^Tq}{w_A}.
\]

`|a0^T q|` is appearance information mass for the queried direction. If it is below the calibrated appearance support floor, output low-confidence neutral appearance rather than inventing PBR.

This is one deterministic baseline. Σ-PRISM permits strengthening the readout operator after physical evidence, but a change requires an operator-fingerprint/schema revision. It may not create a separate canonical texture world.

---

# 7. The carrier is the topology substrate

The carrier is one continuous 2D domain. Logical adjacency is native and permanent.

A valid rendered connection exists only where the readout remains in a compatible regular state. The carrier can therefore be continuous while the 3D readout contains:

- holes;
- folds;
- two sides of a thin sheet;
- close parallel surfaces;
- occlusion discontinuities;
- disconnected visible components;
- surfaces that self-overlap in 3D.

No explicit graph is needed to preserve the underlying sheet.

3D proximity never changes carrier identity.

---

# 8. Latent / null sector and the closed-eye state

Before observation, every unmaterialized carrier coordinate is implicitly

\[
\Psi(\xi)=z_{null}.
\]

`z_null` is the optical-seed/null state. It has no valid geometry readout.

Observed contact does not allocate an independent closed patch. The inverse sensor operator deforms a region of the same carrier out of the null sector into a supported contact state.

Thus the original injection-moulding idea becomes:

```text
same carrier point
    null / unobservable sedenion state
        -> inverse sensor constraint
    supported contact sedenion state
```

No latent triangle, eye ray, frontier-to-seed membrane, or back wall is stored.

The entire infinite implicit null background is the latent continuation of the same carrier.

---

# 9. Boundary, fold and topology are sedenion singularities

## 9.1 Transition state

For neighbouring supported carrier states define semantically

\[
\tau_{ij}=\overline{\Psi_i}\,\Psi_j.
\]

Runtime evaluation uses the generated transition operator from section 5.7 and its generation-keyed disposable cache. It is computed once for a changed endpoint pair, not once per annihilator witness. A generic dense reference multiplication is used only for equivalence testing or when the generated exact plan explicitly selects that fallback.

No state normalization is required. The annihilator residual is tested relative to the integer L1 scale of `tau`, so multiplying either endpoint by a positive projective information factor does not change the singularity decision.

Let

\[
T(\tau)=\max(1,\|\tau\|_1).
\]

The default supported-singularity relative gate is the exact dyadic fraction `1/64`:

```text
annihilatorError << 6 <= T(tau)
```

The shift is persisted per calibration epoch if later calibration changes it; canonical code still uses an integer compare, never division or floating epsilon.

## 9.2 Exact annihilator observable

Use only the generated annihilator catalog from section 5.5.

For every constraint-active dirty transition that is supported by a readout discontinuity, contact/null change, unresolved conflict, or associator gate, evaluate the complete canonical annihilator witness catalog and the exact integer residual

\[
E_k(\tau)=\|\tau a_k\|_1.
\]

The selected signature is

```text
annihilatorId = lexicographically first argmin(Ek)
annihilatorError = min(Ek)
```

The complete catalog is used for a topology-changing decision. Implementations may vectorize, reorder independent witness evaluation, or apply a generated lower-bound prune only when that prune mathematically proves the skipped witness cannot beat the current minimum. Candidate-subset heuristics are not canonical.

The witness scan uses only generated signed-XOR permutations, sign changes, integer add/abs accumulation and comparisons over the already-evaluated transition state. Generic Q16.48 coefficient multiplication is forbidden inside the witness loop.

A transition is **exact singular** when `annihilatorError == 0`.

A noisy measured transition is **supported singular** when:

```text
annihilatorError << singularShift <= ||tau||1
same annihilatorId is observed in >= singularMinIndependentViews
first-hit/readout residual supports the discontinuity
```

Default `singularShift = 6` and `singularMinIndependentViews = 2`. Both are persisted integers.

## 9.3 Singularity semantics

A smooth surface has a regular, slowly varying transition field.

A supported crease/fold/termination occurs when the transition enters a stable near-zero-divisor stratum across independent observations.

A boundary is therefore

\[
\boxed{
\mathcal B=\Psi^{-1}(\text{stable singular transition stratum})
}
\]

and its visible 3D curve is the geometry readout of that locus.

No canonical boundary object exists.

## 9.4 Annihilator bundle

At a singular transition the canonical topology signature is the exact catalogued annihilator family selected by `annihilatorId`. Its evolution along carrier adjacency is the local topology observable. Different sides of a thin object or fold may be spatially close in 3D while carrying different annihilator signatures because carrier identity is independent of Euclidean proximity.

## 9.5 Associator observable

For topology decisions use the exact decoded logical-cell forward differences

\[
\Delta_u\Psi=\Psi(u+1,v)-\Psi(u,v),\qquad
\Delta_v\Psi=\Psi(u,v+1)-\Psi(u,v)
\]

and define

\[
A(u,v)=[\Psi(u,v),\Delta_u\Psi,\Delta_v\Psi].
\]

No derivative normalization is performed before the associator gate. Null/missing neighbours make that cell unresolved rather than fabricating a derivative.

Use only integer L1 magnitudes:

```text
assocError = ||A||1
assocScale = max(1, ||Psi||1 + ||DeltaU_Psi||1 + ||DeltaV_Psi||1)
```

A supported associator transition satisfies the dyadic integer gate

```text
assocError << assocShift >= assocScale
```

with default `assocShift = 5`; calibration may persist another integer shift.

The associator contributes to distinguishing regular continuation from a fold/crease, detecting temporally inconsistent state, weakening continuity coupling and requesting denser readout sampling. It never creates geometry without sensor residual support.

# 10. No explicit split / merge

There is no canonical chart to split or merge.

A second nearby surface is another carrier region whose 3D readout happens to be nearby.

A fold is a continuous carrier region whose 3D immersion changes through a singular transition.

A hole is a region whose state remains null/unobservable between supported regions.

A visible component appearing/disappearing is a change in the supported readout subset, not an allocator topology event.

Therefore no allocator-owned surface identity, adjacency, frontier or split/merge state exists in the reconstruction model.

---

# 11. Whole-frame forward readout

The complete synchronized sensor frame is one observation of `Ψ`.

Define

\[
Y_t=(RGB_L,RGB_R,D_L,D_R)_t.
\]

The forward sensor model is

\[
\boxed{
\hat Y_t=\mathcal R(M_t,K,\text{calibration};\Psi_t)
}
\]

where `M_t` contains the exact timestamped sensor poses.

The forward readout includes:

- carrier geometry readout;
- finite pixel footprint;
- view-dependent appearance readout;
- hardware visibility / first-hit rasterization;
- sensor calibration transforms;
- sensor validity masks.

The implementation uses the GPU rasterizer for the visibility portion. This is an efficient evaluation of the readout, not canonical geometry.

Per eye prediction targets are:

```text
PredDepth        R32F
PredCarrierUV    RG32F or equivalent packed fixed-point carrier coordinate
PredSupport      R16F
PredStateKey     page/block generation key
PredNormal       optional derived cache, never canonical
PredRGB          RGB16F or equivalent only when photometric inverse is active
```

Prediction carries only carrier coordinates/state keys needed to map the readout back into the same `Psi`.

---

# 12. Whole-frame inverse readout: exact admissibility meet

The reconstruction step is the inverse of the same forward model:

\[
\boxed{
\Psi_{t+1}=\mathcal R_t^{-1}(Y_t\mid\Psi_t)
}
\]

This notation denotes a set-valued pullback. It is not a numerical inverse of a global matrix and it never collapses independent sensors into a single residual or correction vector.

## 12.1 Projective state and canonical operator coordinates

For supported state `s`, define geometry/readout mass

\[
m(s)=g_0^Ts=h_0>0
\]

and projective state

\[
p(s)=s/m(s).
\]

Therefore `g0^T p = ONE`. Positive scaling of `s` changes supported information mass but leaves geometry readout unchanged.

Use the complete generated 16x16 signed Hadamard operator table `B` from section 6 as the transient inverse coordinate gauge:

\[
y=Bp.
\]

Because

\[
BB^T=16I,
\]

the inverse is exact and dyadic:

\[
p=\frac{1}{16}B^Ty.
\]

In Q16.48 the division by sixteen is an exact signed shift after the checked accumulation. `y[g0]=ONE`; the three geometry readout rows are the world-coordinate projective readout. The other rows remain coupled sedenion operator coordinates. This coordinate gauge does not create semantic sub-objects and is never persisted independently of `s`.

The null state has `m=0` and is handled by section 18 rather than projective division.

## 12.2 Per-source admissible cell

Every independent source observation `q` touching carrier coordinate `xi` produces a transient Q16.48 operator-coordinate cell

\[
\mathcal C_q(\xi)=\prod_{r=0}^{15}[L_{qr},H_{qr}].
\]

The record contains only:

```text
lo[16], hi[16]       Q16.48 bounds in generated y=Bp coordinates
sourceClass          DEPTH_L / DEPTH_R / RGB_L / RGB_R / retained temporal
independenceKey      eye + calibrated pose/baseline bin + calibration epoch
firstHitSector       HIT / PRE_HIT_EXCLUSION / NO_CONSTRAINT
carrierFootprint     logical carrier interval touched by this source
```

An unconstrained operator coordinate receives the full valid projective range and therefore has no effect on the meet. The cell is temporary and is discarded when absorbed or retained only as unresolved evidence under section 30.

## 12.3 Confidence is cell width

Calibrated uncertainty determines admissible width. It never multiplies a sensor value.

```text
more reliable observation  -> narrower supported bounds
less reliable observation  -> wider supported bounds
invalid/unobservable axis   -> unconstrained bounds
```

Depth range uncertainty, pose/calibration uncertainty, incidence, mixed-pixel risk and motion widen depth-derived bounds. RGB exposure, finite footprint, local readout sensitivity and measured image uncertainty widen RGB-derived bounds. A source whose uncertainty makes a direction uninformative contributes no bound in that direction.

Repeated samples from the same independence class do not become stronger by count alone.

## 12.4 Deterministic depth-cell construction

For a valid depth sample:

1. quantize the calibrated measured depth interval `[dLo,dHi]` once to Q16.48;
2. take the precomputed finite pixel footprint corner/differential rays for that depth sensor;
3. evaluate the near/far endpoints of the finite truncated cone at `dLo/dHi` in the current calibrated sensor gauge;
4. transform those endpoints through the quantized current pose/calibration gauge used by the canonical decision path;
5. form the conservative Q16.48 world-coordinate hull of those endpoints;
6. set `y[g0]=ONE` and constrain `y[g1]`, `y[g2]`, `y[g3]` to that hull for the carrier samples reached by the footprint;
7. leave operator directions not constrained by depth unbounded;
8. apply section 15 first-hit semantics before the cell can enter a joint meet.

No centre-ray point is promoted to canonical geometry. The finite footprint determines the complete depth admissibility region.

For an already predicted carrier footprint, the cell is attached to that same carrier preimage. For unmatched supported depth, the same cell seeds the null-carrier search of section 18.

## 12.5 Deterministic RGB-cell construction

RGB constrains the same `y`, not a separate texture state.

For quantized view direction `omega`, the generated sedenion view operator defines a fixed Q16.48 linear map from canonical projective coordinates to view coordinates:

\[
q_\omega=A_\omega y,
\]

where `A_omega` is generated from `B`, `T_omega` and the exact dyadic inverse of `B`. It is temporary operator metadata for the current calibrated view.

Each measured channel interval `[cLo,cHi]` constrains the corresponding projective colour ratio. With appearance denominator `wA` supported away from zero, rewrite the ratio bound as exact linear inequalities:

\[
c_{Lo}w_A\le q_C\le c_{Hi}w_A.
\]

The source-cell builder contracts the current full-range `y` box against these inequalities using deterministic Q16.48 interval propagation:

```text
constraint order: A-support, R-low, R-high, G-low, G-high, B-low, B-high
sweeps: exactly 2 forward + 2 reverse
rounding: outward, so the resulting box remains conservative
```

For one inequality `a^T y <= b`, each coordinate bound is contracted by placing every other coefficient at the extremum that maximizes the remaining admissible range; the mirrored rule handles `a^T y >= b`. Zero coefficients do nothing. All interval products/divisions use the explicit outward-rounded section 5 interval primitives.

If `wA` cannot be bounded away from zero, or interval propagation cannot narrow any direction, that RGB sample contributes no canonical constraint. It may still remain as a retained unresolved observation if later views can make it identifiable.

This construction lets RGB constrain geometry-relevant directions only when the same view operator couples them observably; no separate geometry correction is created.

## 12.6 Temporal source cells

A retained prior observation is replayed only through its original calibration epoch, pose gauge and carrier footprint. It produces the same kind of source cell as a current observation. Temporal evidence therefore enters the same exact meet and never receives a different fusion law.

## 12.7 Exact joint meet

For all inclusive source cells that refer to the same current carrier preimage:

\[
L_r=\max_q L_{qr},\qquad H_r=\min_q H_{qr}.
\]

Thus

\[
\boxed{\mathcal C_{joint}=\bigcap_q\mathcal C_q}
\]

is bit-deterministic, commutative and independent of source dispatch order.

If

\[
L_r\le H_r\quad\forall r,
\]

the sources have at least one common projective S16 state.

If any

\[
L_r>H_r,
\]

the intersection is empty and no direct canonical state update is made.

## 12.8 Minimum-change representative of a non-empty meet

Let the current projective operator coordinates be `y`. The deterministic accepted representative is

\[
y'_r=\operatorname{clamp}(y_r,L_r,H_r).
\]

A state already inside every accepted source cell does not move simply because another compatible observation arrived.

Convert by the exact dyadic inverse

\[
p'=\frac1{16}B^Ty'.
\]

and verify `g0^T p' = ONE` within the persisted one-LSB projective normalization allowance. A quantized candidate that cannot satisfy all source cells is rejected as incompatible; it is never rounded across a conflict.

## 12.9 Prior admissibility and structural non-degradation

A supported canonical state defines a prior cell `C_prior(s)` in the same `y` coordinates. Its widths are a deterministic monotone function of projective information mass and independent-support provenance.

The actual inclusive meet is

\[
\mathcal C_{update}=\mathcal C_{prior}(s)\cap\bigcap_q\mathcal C_q.
\]

A strong narrow prior contained by a later broad cell does not move. A stronger independent observation can narrow previously broad directions. Incompatibility with a strong prior stays explicit and is resolved by the scene/topology rules rather than by moving to an arithmetic compromise.

## 12.10 Empty intersection record

An empty intersection is preserved transiently as:

```text
conflict operator-coordinate mask
lo source provenance / hi source provenance
exact gap[r] = lo[r] - hi[r]
independence keys
first-hit sectors
carrier footprint
candidate annihilator / associator signature
scene-transition candidate key, if any
```

The conflict can expire as unsupported noise, reveal another latent preimage, support a stable singular transition, participate in a proven scene change, or remain unresolved. Opposing sensors cannot numerically cancel because their constraints are never added.

## 12.11 Whole-frame semantics

`RGB_L`, `RGB_R`, `DEPTH_L`, `DEPTH_R` and retained temporal observations are simultaneous readouts of one `Psi`. Separate GPU kernels may construct their cells, but there is exactly one canonical fusion primitive: admissible-set intersection followed by the checked projective commit of section 13.

No persistent sensor-consensus product, per-pixel geometry object or second reconstruction state is produced.

# 13. Deterministic Q16.48 state commit

There is one canonical state commit rule for supported contact and it consumes only a non-empty admissible intersection from section 12.

For supported state `s`:

1. compute `m(s)`, projective `p=s/m`, and `y=Bp`;
2. construct `C_prior(s)`;
3. meet the prior and all valid inclusive source cells with integer `max/min`;
4. if the meet is empty, leave `s` unchanged and emit/extend the exact transient conflict record;
5. if non-empty, clamp only excluded `y` coordinates into the meet;
6. invert with the exact `(B^T y)>>4` transform;
7. derive the justified information-mass target from joint width and independent support;
8. set canonical mass to the stronger justified value without changing projective direction merely because support count increased;
9. lift the accepted projective direction and mass back to one Q16.48 sedenion;
10. re-evaluate every inclusive cell, first-hit sector, range and topology gate before atomic commit.

There is no alternative texel-local mutation path.

## 13.1 Width-to-information mapping

Let joint operator-coordinate widths be

\[
w_r=H_r-L_r.
\]

Generated operator metadata persists a minimum physically meaningful width `wFloor[r]`, an information floor `mMin` and maximum justified mass `mMax`.

For each operator coordinate constrained by the current proof set:

\[
\pi_r=\frac{wFloor_r}{\max(w_r,wFloor_r)}.
\]

The sixteen-value `pi` tuple is a transient precision certificate. The scalar projective mass stored in `s` is only the conservative **common support hardness** of the currently constrained readout, not a claim that every S16 direction is equally known. It is the minimum `pi_r` over directions whose bounds are required to reproduce the current geometry/appearance readout; directions with no justified bound contribute zero and therefore cannot manufacture hardness.

Directional certainty needed for future non-degradation is carried by the minimal constraint certificates of section 30, not invented from the scalar mass. No sample-count term exists.

## 13.2 Independent-support strengthening

A source can narrow an observable direction, but raising durable information mass beyond the single-source floor requires the persisted minimum number of distinct `independenceKey` classes constraining that direction.

Replaying one eye/pose bin can demonstrate repeatability but cannot make the state arbitrarily hard. A genuinely different baseline, side, footprint phase or sensor can tighten the common set and raise justified mass.

## 13.3 Null/contact commit

`z_null` is not projectively divided. A null region becomes supported contact only through section 18 probation. Its initial committed sedenion is the deterministic inverse lift of the first non-empty multi-source admissible set satisfying that promotion rule.

A supported region returns to `z_null` only through the proven disappearance transition of section 29.4. Ordinary incompatibility can never directly zero a state.

## 13.4 Checked atomic commit

A candidate commit is legal only when:

```text
all constrained lo[r] <= yCandidate[r] <= hi[r]
projective normalization is valid
all Q16.48 operations remain in range
post-hit space contributed no cell
null/contact promotion or disappearance gate is satisfied when applicable
stable singularity semantics remain valid
codec decode/encode is bit-identical
```

Failure keeps the previous canonical state byte-for-byte and preserves the unresolved transient evidence needed to decide later.

# 14. Finite pixel/cone footprint without ConeEvent objects

A sensor pixel is a finite integration footprint in the forward readout. It is not an infinitesimal ray and it is not a canonical ConeEvent object.

For pixel `p`, the current sensor/readout geometry induces a compact carrier footprint

\[
K_{t,p}(\xi).
\]

The forward sensor value is the finite-footprint readout of `Psi` with hardware first-hit visibility.

The calibrated footprint maps sensor uncertainty directly into one or more projective `S16` admissible cells over the carrier samples actually covered by that footprint.

A wide/distant footprint therefore produces broad, strongly overlapping admissible cells. It cannot impose carrier variation narrower than its own inverse support. A close or subpixel-shifted footprint produces narrower/differently phased cells and can constrain finer variation of the same sheet.

The same footprint mapping supplies geometry, RGB, appearance and first-hit constraints.

Footprint integration may use fixed hardware interpolation and deterministic fixed-point coverage coefficients in temporary readout caches. Those coefficients only construct source constraint widths; they are never accumulated into canonical sensor votes.

# 15. First-hit causality as set-valued inverse semantics

The measured depth `d_m` and predicted first-hit depth `d_p` determine **which admissible set exists**.

## 15.1 Measured hit in front of prediction

When a supported measured first hit is clearly in front of the predicted readout, that measurement constrains a different visible preimage. It may pull a latent carrier region out of `z_null` through section 18.

The old predicted surface is behind the newly measured first hit and receives no constraint from this measurement.

## 15.2 Compatible measured hit

When measured and predicted first-hit readouts overlap within their calibrated confidence cells, the measurement contributes its admissible cell directly to the currently predicted carrier footprint. It participates in the exact meet of section 12.

## 15.3 Predicted contact lies in measured pre-hit path

When the current predicted contact lies strictly before the measured supported first hit, the observation does **not** add a negative correction and does not push the state by subtraction.

It emits a transient **pre-hit exclusion cell** `E_q` in projective S16 space: the current first-contact readout is not admissible for that calibrated view within the confidence-shaped exclusion width.

An exclusion record contains:

```text
projective excluded bounds
source / independence key
view direction / pose epoch
predicted carrier footprint
first-hit ordering
candidate singularity signature
```

One exclusion never mutates canonical state. Independent exclusions are confirmed by set overlap and provenance, not by adding contradiction magnitudes. When sufficiently independent exclusions consistently reject the same current branch and a different admissible state reduces all corresponding first-hit conflicts, the local state may move, fold, return toward null, or expose another carrier region according to sections 9, 18 and 29.

## 15.4 Exact post-hit null effect

For every sensor sample, carrier state geometrically behind the measured first hit receives **no inclusive cell, no exclusion cell and no confidence update**:

\[
\boxed{\Delta\Psi_{post}=0.}
\]

This is a hard causal rule, not a small weight.

# 16. No explicit stereo consensus and no sensor sum

Left/right depth, left/right RGB and retained temporal views remain separate inverse constraints until the exact meet.

For the same carrier footprint:

\[
\mathcal C_{joint}
=
\mathcal C_{D_L}
\cap\mathcal C_{D_R}
\cap\mathcal C_{RGB_L}
\cap\mathcal C_{RGB_R}
\cap\cdots
\]

Missing/uninformative sources contribute no bound. Confidence changes each source cell width. It never becomes a multiplier in a sum.

If the intersection is non-empty, all sources agree on at least one common S16 state and section 13 commits the minimum-change representative.

If the intersection is empty, the incompatibility survives exactly with source provenance. It may indicate calibration/noise, thin/second carrier preimage, fold, occlusion or dynamic evidence. Nothing is averaged and opposite constraints cannot cancel.

The meet is commutative and associative under integer `max/min`; changing L/R dispatch order cannot change the result.

The same rule applies to temporal observations. There is no `DEPTH_DISAGREEMENT` geometry object and no `stereo confidence vote`; disagreement is simply an empty or narrowed common admissible set.

# 17. No canonical normal estimator after bootstrap

The state readout itself supplies geometry derivatives and normals.

Raw depth neighborhood fitting is permitted only for:

- initial null-to-contact gauge seeding when no state prediction exists;
- calibration diagnostics;
- catastrophic tracking recovery.

Once a region has supported `Ψ`, revisit normal is derived from `D X[Ψ]`, not repeatedly rebuilt from raw depth.

This removes a large repeated preprocessing cost and makes depth/RGB evidence update the same geometry.

---

# 18. Novel contact and latent gauge placement

Carrier coordinates are gauge, not physical identity. An unmatched physical
observation therefore does not immediately receive a canonical `Sigma_2` address.
The whole synchronized rig frame is one observation; execution tiling cannot divide
it into physical identities.

## 18.1 Existing carrier compatibility

Forward readout supplies one or more candidate carrier preimages for a measured
first hit. Each candidate is tested by the same exact four-source inverse
admissibility rules used for state mutation. A candidate is accepted only when the
joint source cell and current prior `Psi` have a legal non-empty meet satisfying
first-hit semantics and every required exact gate.

Prediction is a candidate generator, never allocation or acceptance authority.

## 18.2 Pending latent gauge

If no compatible current carrier preimage exists, supported observation enters one
or more transient pending gauges. A pending gauge:

- has only a temporary local `(u,v)` parameterization;
- owns the complete exact source/provenance evidence required for promotion;
- may be proposed to later observations before canonical promotion;
- participates in the same exact compatibility tests as current carrier proposals;
- has no canonical `Sigma_2` coordinate;
- is not canonical prediction state or a second physical world.

Execution tiles, image blocks, pages, workgroups and scratch windows may partition
pending work but cannot alter pending identity, physical meaning, accepted support,
topology, proof or final carrier allocation.

## 18.3 Exact local connectivity

Optical-domain or disposable-readout adjacency may only propose connectivity inside
a pending gauge. A proposed edge is retained only when exact admissible-cell,
first-hit and S16 transition closure support a regular or stable singular relation.
Unobserved separation creates no physical transition claim. Consequently one whole
frame may produce multiple exact pending components, but no component boundary may
come from image tiling, a storage page or a GPU workgroup.

## 18.4 Continuation into existing latent carrier

Before creating an independent pending gauge, an unmatched observation adjacent in
the inverse readout to a compatible supported carrier region shall test whether it
can continue into that region's implicit-unobserved carrier neighbourhood.
Continuation is accepted only when the exact source cells, first-hit semantics and
required transition constraints admit the same carrier parameterization.

Image adjacency and Euclidean 3D proximity may propose work but never force
continuation or identity.

## 18.5 Pending-gauge reuse

A later observation having no compatible canonical carrier shall test existing
pending gauges whose disposable readout can explain the measured first hit. Reuse
is decided by exact admissible-cell overlap, independence/provenance and first-hit
compatibility. Repeated observations of one not-yet-promoted surface refine the same
pending gauge instead of allocating another carrier identity.

## 18.6 Null-to-contact promotion

A pending gauge may enter canonical `Psi` only after the persisted independent
support rule is satisfied:

```text
coherent independent left/right depth support
OR
depth plus independently informative calibrated RGB support
OR
sufficiently independent temporal pose/baseline observations
```

Promotion performs one deterministic exact lift of the complete accepted joint
admissible set into S16. Execution windows cannot truncate that set.

## 18.7 Canonical carrier-extent allocation

Only promotion lacking compatible existing-carrier continuation allocates canonical
`Sigma_2` address space. Promotion events are ordered by canonical source revision,
then by their complete provenance key, then by the lexicographically first accepted
local coordinate. The promoted-extent allocator appends one collision-free local
extent plus the persisted latent deformation guard while preserving the pending
local parameterization.

The guard is implicit-unobserved carrier and makes no contact/null claim. Allocation
is independent of image tiles, page boundaries, execution partition, backing-store
placement and 3D position. No Morton ordering, image-block ordering or storage-page
identity is part of physical or canonical semantics.

## 18.8 Execution-decomposition invariance

For the same accepted finite-footprint observation sequence, changing GPU workgroup
shape, execution tile dimensions, storage-page dimensions, proof scratch-window
size, dispatch partition or backing placement may alter cost but may not alter
`Psi`, validity, gaps, topology, information strength, provenance, proof or gauge
allocation.

Changing the actual calibrated sensor footprint or observation content is new
evidence and is not an execution-decomposition change.

## 18.9 Aborted probation

A pending gauge that fails promotion or is superseded by a compatible canonical
carrier loses its transient identity and disposable candidate state without a
canonical mutation. Exact conflict/provenance/raw evidence still required by
section 30 remains retained; discarding a pending identity never discards required
evidence.

---

# 19. Detail is literal structure of the same sheet

There is no separate displacement field.

Fine geometry is fine spatial variation of

\[
\Psi(u,v).
\]

A groove, screw head, embossed trim or texture feature is simply more rapidly varying state on the same carrier.

The finite sensor footprint determines what spatial variation is observable. A broad footprint has no high-frequency inverse sensitivity and therefore cannot erase detail it cannot see.

Repeated subpixel-shifted observations constrain the same carrier at different footprint phases and can recover finer geometry/appearance than one raw depth frame.

---

# 20. Logical bitmap and exact physical codec

The mathematical carrier is one continuous/logically high-resolution bitmap. Quest 3 stores only allocated carrier blocks and may compress them losslessly. Compression is a byte codec of `Psi`, never a geometry/detail model.

Use logical 8x8 carrier blocks. A block chooses the smallest deterministic exact encoding among:

```text
NULL      implicit z_null, no payload
CONST     one Q16.48 S16 state repeated 8x8
AFFINE    exact s(u,v)=s0 + u*su + v*sv
DELTA     exact predictive integer residual stream
RAW       explicit 8x8 Q16.48 S16 samples
```

## 20.1 Exact predictive DELTA codec

`DELTA` uses raster order independently for each of the sixteen Q16.48 coefficients. Predictor:

```text
(0,0)        pred = 0
first row    pred = left
first col    pred = up
interior     pred = left + up - upperLeft
residual     = actual - pred
```

The residual is exact signed 64-bit integer difference. For each coefficient, store the minimum signed bit width that represents every residual in the block, then bit-pack residuals in fixed `(coefficient, v, u)` order. Width zero means all residuals are zero. Decode uses the same predictor and reconstructs every Q16.48 sample bit-for-bit.

## 20.2 Codec selection

For the same decoded 8x8 state, independently construct every legal mode, compute exact payload bytes including headers, and choose:

```text
smallest byte count
then mode order NULL < CONST < AFFINE < DELTA < RAW
```

`CONST` and `AFFINE` are legal only if exact decoding reproduces all 64 samples. `DELTA` and `RAW` are always exact when in range.

## 20.3 One-state rule

All canonical decisions operate on decoded Q16.48 `Psi`. Encoding mode has zero physical meaning. Encode -> decode -> encode must be deterministic, and decode must reproduce the exact state before any state-changing readout/topology decision.

No lossy canonical codec is permitted.

# 21. Carrier sampling density and gauge deformation

`u,v` have no physical unit. The same 3D square metre may occupy little or much carrier area.

A flat region can occupy a compact carrier span; detailed trim may consume more gauge area. This is parameterization, not a second detail hierarchy.

Gauge deformation is requested only when **independent accepted inverse cells demand reproducible variation that the current carrier sampling cannot represent without making their joint meet empty or falsely broad**.

The trigger therefore uses quantities already produced by the inverse readout:

```text
finite carrier footprint size
joint admissible-cell width
variation of neighbouring accepted projective states
independent-view confirmation
readout reproduction error per source
```

No additional canonical detail estimator is used.

Gauge deformation is a 2D bijective remap

\[
\chi:\Sigma\rightarrow\Sigma,
\qquad
\Psi'(\xi)=\Psi(\chi^{-1}(\xi)).
\]

A pure gauge move is accepted only if every retained source constraint and every current forward readout remains admissible after the remap within persisted Q16.48 quantization bounds.

Gauge work is dirty-local and may stretch a detailed region into surrounding implicit-null carrier area. It never changes physical geometry, singularity topology or information strength by itself.

Compaction is optional. Stretching is mandatory only when additional measured information cannot otherwise be represented by the same carrier without violating accepted source constraints.

# 22. Carrier continuity from one shared inverse readout

There is no independent continuity solver. The carrier is already one continuous domain and its observable coherence is determined by the same `Psi` readout that measurements constrain.

Continuity follows from:

1. finite sensor footprints constraining overlapping carrier support;
2. one projective state field being interpolated by the forward readout;
3. stable singular transitions explicitly stopping regular readout continuation.

After any accepted state commit, evaluate dirty neighbour transitions

\[
\tau_{ij}=\overline{\Psi_i}\Psi_j.
\]

The transition is read as:

```text
regular
    ordinary carrier interpolation remains valid
stable singular
    interpolation terminates/folds at the singular locus
unresolved
    publication stays conservative and transient evidence remains pending
```

A neighbouring carrier sample never changes merely because another sample is nearby. Every canonical change must be justified by an inclusive inverse cell, a proven pre-hit exclusion transition, a null/contact scene transition or a pure gauge deformation.

This rule preserves fine state variation and prevents unobserved carrier adjacency from manufacturing 3D surface support.

# 23. Thin surfaces and close parallel geometry

A thin object is naturally represented by two different carrier regions:

```text
carrier region A -> 3D front surface
carrier region B -> 3D back surface
```

They may project 5 mm apart or less because 3D proximity does not alter carrier coordinates.

The two regions may be connected through latent/null carrier, a fold, or a regular continuation depending on actual observation history. No minimum thickness is imposed by representation.

The sensor observability limit is the only geometric limit.

---

# 24. Holes, doorways and occlusions

A visible hole does not require deleting the carrier.

The carrier can pass through a null/unobservable state between supported regions:

```text
supported wall state
    -> singular/null transition
latent carrier
    -> singular/contact transition
supported state elsewhere
```

The 3D readout therefore contains a hole while the canonical carrier remains one sheet.

An occlusion edge is a first-hit/readout singularity supported by multiple views; it is not a carved empty-space boundary.

---

# 25. Geometry and RGB refinement are one inverse problem

RGB is not attached after geometry and it does not create a separate photometric optimizer.

Depth and RGB are simultaneous readouts of the same `Psi`. Each calibrated RGB sample therefore generates another confidence-shaped projective S16 admissible cell over the same carrier footprint.

RGB may tighten geometry-relevant directions only when its inverse cell is actually informative there. This requires the readout itself to make those directions observable through image gradient, calibrated baseline/view change, finite footprint and first-hit/occlusion consistency.

If RGB and depth cells have a non-empty joint intersection, they refine the same state through section 12. If they are incompatible, the conflict is preserved.

Repeated subpixel views sharpen geometry because their differently phased finite-footprint cells intersect to a smaller admissible set. RGB/depth refinement remains one inverse-readout constraint system over the same state.

# 26. Measured texture superresolution without a separate texture map

Subpixel RGB views produce differently shifted finite footprint kernels on the same carrier.

The appearance inverse readout updates the hidden/state directions of the same `Ψ`.

A sharper/narrower observation has a narrower carrier pullback and can add spatial variation that a broad observation cannot see.

A later broad/blurred observation therefore cannot directly erase a stable fine carrier pattern because its forward footprint integrates that pattern and its adjoint correction is correspondingly broad.

Appearance confidence is encoded projectively in the same state and tightens only when accepted independent constraints justify it.

No triplanar texture, global unwrap, or destructive texture bake exists in the live canonical path.

---

# 27. Directional appearance and PBR

Directional appearance is read from the same state by the view-dependent operator `T_omega`.

No separate canonical BRDF object is required.

GLB/PBR is a derived interoperable approximation:

- `baseColor`: robust view-stable component of appearance readout;
- `normal`: differential geometry readout;
- `roughness`: derived only if angular observations make directional spread identifiable;
- `metallic`: zero unless evidence strongly supports a metallic directional response;
- confidence: exported as extension/custom metadata when useful.

The canonical state is never replaced by its PBR approximation.

---

# 28. Pose and calibration are readout gauge parameters

The Quest pose is not baked into canonical geometry objects.

For frame pose `M_t`, the sensor readout is `R(M_t; Psi)`.

Pose refinement uses the same set-valued principle as state reconstruction. Each sufficiently conditioned overlapping observation produces a bounded admissible interval for the local six-component twist correction

\[
\delta\xi=(\delta t_x,\delta t_y,\delta t_z,\delta r_x,\delta r_y,\delta r_z).
\]

The immutable Meta tracking pose supplies the centre of the prior twist cell. When
the active Quest capture API exposes a numeric tracking covariance, quantize and
conservatively project it into the local twist basis. When it does not, the prior
width is the deterministic conservative tracking-derived uncertainty envelope
formed from the coherent frame's clock-mapping uncertainty, RGB/depth and stereo
timestamp skew, observed tracking translation/rotation rate, fixed-rig calibration
residual and persisted calibration bounds. Missing covariance is never interpreted
as zero uncertainty. Sensor/readout constraints intersect this exact prior cell
componentwise after conservative projection into the local twist basis.

```text
poseCell = MetaPoseTrackingPrior
for every valid independent overlap constraint:
    poseCell = intersect(poseCell, sourcePoseCell)
```

If the meet is non-empty and excludes zero, apply the deterministic minimum-magnitude twist obtained by clamping zero into the joint cell. If it contains zero, keep the Meta pose. If it is empty, do not average conflicting pose evidence; retain the Meta pose and mark the overlap unresolved/transient.

Accepted twist bounds are quantized to Q16.48 before they can affect canonical inverse decisions. Applying the resulting SE(3) transform to render/readout caches may use FP32 because pose is a readout gauge, but the accept/reject decision is integer and source-order independent.

This does not remesh or rewrite the world. It changes the observation gauge.

Calibration-epoch changes create a new immutable sensor operator epoch. Existing state is never silently reinterpreted under a different operator fingerprint.

# 29. Temporal evidence and physical scene evolution

`Psi` is persistent, but the physical world is allowed to change. The durable carrier represents the **current best-supported scene configuration**, not an immortal union of every configuration ever observed.

A scene change is never solved by averaging old and new readouts. It is one of four state transitions of the same carrier:

```text
LATENT -> CONTACT                 persistent appearance of new surface
CONTACT -> LATENT                 proven disappearance / pass-through
CONTACT(D,old) -> CONTACT(D,new)  coherent transport of same carrier region D
CONTACT -> CONTACT*               supported deformation not explained by one transport
```

Temporary occlusion is none of these: it contributes no evidence to the hidden background behind the nearer first hit.

## 29.1 Transient probation

New, conflicting or changing evidence stays in bounded transient scratch until its independent source constraints justify a durable transition. Transient state is not a second world and has no independent render identity except optional debug visualization.

A null-to-contact candidate is durable only when the same supported preimage is constrained by:

```text
both depth eyes in one coherent frame
OR
one depth eye plus persistent calibrated RGB constraint from an independent readout direction
OR
two temporally separated calibrated pose/baseline bins
```

The exact minimum classes and baseline bins are persisted calibration integers.

Moving people, transient depth artifacts and momentary occluders normally fail this rule or fail temporal consistency before becoming durable.

## 29.2 Appearance of a new surface

When the measured first hit is in front of the current predicted hit, the old predicted state is behind the new first hit and receives no constraint.

The new hit searches latent carrier through section 18 and enters probation. After independent support, that latent region becomes contact.

Consequences:

- placing a new chair in front of a wall adds supported carrier contact for the chair;
- the wall state behind it remains unchanged and keeps all previous detail;
- the chair does not erase, weaken or average with the wall;
- removing the chair later can expose the unchanged wall immediately.

## 29.3 Pre-hit exclusion of an existing contact

If an old predicted contact lies inside the newly measured pre-hit path, the view proves that **the old current readout is not valid from this calibrated direction**. Section 15.3 emits an exclusion cell; it does not subtract from the old state.

For each affected carrier footprint retain a deterministic exclusion provenance set keyed by `independenceKey`. Repeated observations from the same key replace/tighten that key's exclusion; they do not add votes.

An old contact becomes a durable disappearance candidate only when:

```text
>= disappearanceMinIndependentKeys independent exclusions reject the same carrier readout
>= disappearanceMinAngularBins calibrated direction bins are represented
no accepted inclusive source cell still requires the old first-hit state in those tested directions
a farther measured first hit or valid pass-through readout explains every exclusion
```

Defaults:

```text
disappearanceMinIndependentKeys = 3
disappearanceMinAngularBins     = 2
```

These are persisted integers, not frame-count confidence.

## 29.4 Proven disappearance: CONTACT -> LATENT

Before nulling an old region, the implementation must prove all of the following on the dirty connected carrier support `D`:

1. the exclusion gate of 29.3 passes;
2. replacing `Psi|D` by `z_null` makes every confirming pre-hit observation admissible;
3. the replacement does not violate any retained stronger inclusive constraint that still observes `D` directly;
4. no verified coherent transport candidate under 29.5 explains the same disappearance/appearance evidence;
5. no post-hit observation was used as negative evidence.

If all conditions hold, commit

\[
\Psi(\xi)=z_{null},\qquad \xi\in D
\]

atomically at one carrier revision boundary.

This is not ray carving: the carrier remains present, only its current observable contact state returns to the latent sector.

Example: after a cabinet is removed, repeated calibrated views see through its old first-hit location to the wall behind it. The cabinet carrier region is retired to `z_null`; the newly exposed wall is either the already-known unchanged carrier state or newly observed latent carrier if it had never been scanned.

## 29.5 Identity-preserving coherent transport

Disappearance at one location and appearance at another may be the same physical carrier region moving. Transport must be tested **before** the old region is retired and before a new candidate is independently promoted as unrelated state.

No semantic object label is used. A transport candidate joins:

- an old connected carrier region `D_old` carrying coherent pre-hit exclusions at its previous readout;
- a probationary new-contact region `D_new`;
- matching intrinsic carrier evidence.

### Intrinsic matching evidence

For transport matching, derive a temporary intrinsic signature from the same state:

```text
hidden generated operator-coordinate intervals
local finite-difference carrier metric ratios
local annihilator IDs/errors on incident transitions
associator signature
view-stable appearance admissible intervals when available
```

The three world-position geometry rows are excluded from the match key. No descriptor is persisted as a second canonical representation.

Candidate pairs are created only when their intrinsic admissible intervals overlap and their local singularity pattern is compatible. Candidate generation uses deterministic hash/order rules over carrier coordinates and never world-space nearest-neighbour merging.

### Deterministic rigid transport proposal

A rigid transport proposal requires at least three matched, non-collinear carrier points. Use the lexicographically first admissible triplets after sorting by old carrier coordinate and new candidate coordinate.

From each triplet, build Q16.48 orthonormal frames with deterministic fixed-point vector operations and integer square root:

```text
e1 = normalize(P1-P0)
e2 = normalize((P2-P0) - dot(P2-P0,e1)*e1)
e3 = cross(e1,e2)

f1,f2,f3 identically from Q0,Q1,Q2
R = [f1 f2 f3] * transpose([e1 e2 e3])
t = Q0 - R*P0
```

Degenerate triplets are rejected. Candidate `R,t` are quantized Q16.48 before verification. Proposal generation may use temporary FP readout for speed, but only the quantized candidate verified below can change canonical state.

### Transport verification

A candidate transform `T=(R,t)` is accepted only when one common transform simultaneously:

1. maps all tested old geometry readout points into the new-contact admissible geometry cells;
2. makes the old-location pre-hit exclusions admissible;
3. preserves hidden/intrinsic source cells that are invariant under spatial transport;
4. preserves each internal regular/singular carrier transition class of `D_old` unless current sensor evidence explicitly supports a new deformation there;
5. reduces no prior information mass and discards no supported detail;
6. passes at least `transportMinIndependentKeys` independent current/retained observation classes.

Default:

```text
transportMinIndependentKeys = 3
```

No score is averaged. Every required verification is a set-membership test; failure of any hard constraint rejects that candidate.

### Transport commit

In generated operator coordinates, apply the verified rigid transform to the homogeneous geometry readout rows of every `xi in D_old`, keep transport-invariant hidden coordinates unchanged, invert the exact Hadamard gauge and restore the original justified information mass.

The new candidate gauge region `D_new` is then released back to implicit null rather than becoming a duplicate physical surface. `D_old` remains the same carrier identity and now reads out at the new physical location.

After commit, recompute local transition signatures from the transformed S16 states and require them to satisfy the verification envelope before publication.

This preserves previously measured sub-depth geometry and appearance when a door closes or a previously scanned chair/cabinet is moved.

## 29.6 Deformation not explainable by one rigid transport

If no single rigid transport satisfies a changing carrier region, do not force one. Current inclusive/exclusion constraints may support different coherent subregions or genuine local deformation of the same sheet.

The implementation may derive temporary connected subregion masks solely to bound verification work. Each accepted subregion transition must independently satisfy the same admissible-set rules; these masks are disposable and are not object topology.

If no coherent transport/deformation is proven, keep old durable state until disappearance is proven and treat new contact independently through probation.

## 29.7 Occlusion is not disappearance

A closer first hit never supplies exclusion evidence to carrier state behind that hit.

Therefore:

```text
person walks in front of cabinet  -> cabinet untouched
new chair blocks wall             -> wall untouched
closed door blocks next room      -> room behind door untouched
```

Disappearance requires direct calibrated pass-through/pre-hit exclusion of the old contact itself. This is the hard distinction between occlusion and physical removal.

While a disappearance/transport transition is unresolved, the renderer may use a disposable **provisional visibility mask** derived from independently confirmed exclusion cells to soften/suppress the contradicted old readout in preview. That mask never changes prediction for canonical acceptance, never becomes durable state, and disappears when the transition resolves.

## 29.8 Door-state example

For an already scanned open door that becomes closed:

1. old open-door carrier receives coherent pre-hit exclusions where views now pass through its former position;
2. a new first-hit pattern appears at the closed-door position;
3. intrinsic hidden/appearance/singularity signatures match the old carrier region;
4. one rigid transform around the hinge geometry satisfies both old-position exclusions and new-position contact cells;
5. the same carrier region is transported; its detailed geometry and appearance are retained;
6. the newly allocated probationary carrier at the closed location is discarded as duplicate gauge.

No `Door` object, hinge semantic or historical surface pair is canonical.

## 29.9 Current state and history

The active `Psi` is the current supported scene. Previous complete revisions may be retained by the versioned storage layer for optional historical playback, but old revisions do not participate in current first-hit prediction and are not parallel reconstruction state.

A restart loads exactly one selected current revision plus unresolved retained constraints. Scene history is storage history, not scan ontology.

# 30. Minimal constraint certificates and observation retention

`Psi` remains the only canonical world state. A small evidence ledger is retained only to prove the admissible set that justifies its current confidence, singularities and unresolved temporal transitions. The ledger has no geometry or appearance of its own.

## 30.1 Constraint certificate

After a source cell has been absorbed, raw RGB-D may be discarded when the part of that source still logically required can be reduced to:

```text
ConstraintCertificate
    carrier page/block range
    operator-coordinate mask
    lo/hi bounds for masked coordinates
    sourceClass
    independenceKey
    calibrationEpoch
    roleMask: SUPPORT / SINGULARITY / APPEARANCE / TRANSITION / POSE
```

Only constrained coordinates are stored. Bounds are Q16.48. A certificate is evidence/provenance, not another reconstruction state.

## 30.2 Deterministic minimal proof set

Every visible candidate revision must first own a complete immutable exact evidence
journal sufficient to reproduce its state meet, validity, gaps, promotion and
required transition gates. That complete journal is the foreground commit witness;
it has no fixed record cap. A workgroup-local certificate array is only a scratch
window with a persisted continuation cursor.

Live revision visibility may follow exact journal closure without waiting for proof
minimization. The complete journal and every required raw reference remain owned
until the following minimization finishes. Durable persistence or eviction of that
revision requires the deterministic minimal proof set below; interruption before
durable publication restores the preceding complete durable revision.

Before minimization, certificates with identical `(carrier block, roleMask, independenceKey, sourceClass, calibrationEpoch)` are coalesced by exact admissible-set intersection whenever that intersection is non-empty. An empty intersection remains explicit unresolved evidence rather than being coalesced away.

For each dirty carrier block, collect the resulting certificates in lexicographic order `(roleMask, independenceKey, sourceClass, bounds)`. Compute the admissible meet they justify. Then perform one deterministic redundancy sweep in reverse lexicographic order:

1. tentatively remove one certificate;
2. recompute the relevant meet/gate;
3. delete it only if the resulting admissible set is bit-identical and every required independent-support/singularity/transition condition remains satisfied.

Repeat sweeps until no certificate is removed. The result is the deterministic minimal proof set for the current revision.

This proof set preserves directional confidence that cannot be represented by one scalar projective mass, while keeping the physical world itself solely in `Psi`.

## 30.3 Raw observation retention

Keep a compressed raw observation tile only when a certificate is insufficient, specifically when it is still needed to:

- contract a currently unresolved inverse cell;
- resolve an empty intersection whose exact pullback depends on image/depth samples;
- refine subpixel geometry/appearance from a future baseline;
- prove or reject a pending appearance/disappearance/transport transition;
- resolve pose/calibration ambiguity.

When its useful constraints become representable by certificates or are superseded, delete the raw tile.

## 30.4 Temporal transition evidence

Pending scene changes retain exactly the certificates and raw tiles required by section 29. A committed transport consumes the duplicate new-contact probation evidence after it has verified the moved old carrier. A committed disappearance can release exclusion evidence once the new durable revision and any requested history snapshot are sealed.

## 30.5 Persistence semantics

Constraint certificates required to justify the current revision are persisted with that revision. Unresolved raw tiles are persisted separately. Deleting all disposable render/prediction caches must not change `Psi` or its proof set.

The evidence ledger belongs to the inference of one global `Psi`; it never receives surface/object identity.

# 31. Prediction and rendering

Prediction is the forward readout of the same state used for reconstruction.

The renderer never invents a second geometry representation.

## 31.1 Carrier block readout

For visible/resident carrier blocks:

1. decode block state on GPU;
2. evaluate supported geometry readout;
3. evaluate corner/edge singularity state;
4. tessellate only where contact readout is supported;
5. break/split render interpolation at stable singular transitions;
6. emit ordinary indexed meshlets;
7. cull by frustum, Hi-Z, screen error and page residency;
8. rasterize with hardware Z-buffer.

## 31.2 Mixed null/contact cells

A cell containing both supported and null state is clipped at the supported-state transition. No eye-to-surface triangle may be created across null carrier.

## 31.3 Singular cells

A cell whose transition crosses a stable zero-divisor locus receives independent interpolation on each regular side. The singular locus is refined along the carrier cell edge by deterministic bisection until projected edge motion is <= 0.25 pixel in both eyes or six bisection steps have completed, whichever occurs first. This affects only derived tessellation.

This gives a sharp derived edge without storing a BoundaryCurve.

## 31.4 Preview

Use an opaque/front-depth bring-up mode first. Optional scan transparency uses a depth prepass followed by colour pass with equal-depth testing so hidden sheets do not accumulate alpha soup.

---

# 32. World scale and residency

The carrier is logically unbounded. Physical state is paged.

Use signed 64-bit logical carrier page coordinates. Unallocated pages are implicit `z_null` and require no storage.

Quest GPU retains:

- pages visible in either eye;
- current inverse-readout neighborhood;
- dirty state/readout pages;
- small overlap/revisit halo;
- transient probation pages.

Other pages persist on flash and rehydrate asynchronously.

No reconstruction operation scans all persisted pages.

---

# 33. Quest 3 physical storage format

Use logical 8x8 blocks grouped into 64x64 logical pages.

```text
PageHeader
    logicalPageX : int64
    logicalPageY : int64
    generation   : uint32
    revision     : uint32
    blockMode[8x8]   # NULL / CONST / AFFINE / DELTA / RAW
    certificateOffset/count

Block payload
    NULL      : none; exact z_null
    CONST     : one S16 Q16.48 state
    AFFINE    : S16 Q16.48 s0, su, sv
    DELTA     : per-coefficient signed bit widths + exact packed residual stream
    RAW       : explicit 8x8 x 16 Q16.48 coefficients

Proof payload
    minimal ConstraintCertificate records from section 30
```

Carrier decode and certificate lookup are GPU-local for resident pages. Dirty inverse/state scratch may use any exact backend lowering permitted by section 5.2; only the checked Q16.48 semantic value reaches canonical commit.

No individual storage-buffer binding may exceed the runtime-reported Vulkan storage-buffer range. Page pools are segmented below that limit. Native-I64 and packed-32 execution are interchangeable exact lowerings when their capability/parity gates pass; neither representation is canonical physics.

# 34. GPU memory execution profiles

Memory capacity is an execution profile, never a reconstruction or evidence limit.
The Quest implementation deliberately materializes complete whole-observation
source cells when this avoids repeated exact ALU work. At 320x320, four full 16D
lo/hi Q16.48 streams require approximately 100 MiB before validity/provenance
metadata and are therefore not split into physical-looking tiles to satisfy an
arbitrary scratch cap.

Initial Quest 3 profiles are:

```text
conservative resident target       1024 MiB
high-throughput resident target    2048 MiB
audited resident ceiling           3072 MiB
```

The high-throughput profile is the default during S4-08 physical closure. The
ceiling is not a required reservation: allocation remains segmented below the
runtime Vulkan binding range and leaves device-validated process/system headroom.
Changing profile may alter residency, staging and cadence but may not alter source
resolution, accepted evidence, `Psi`, proof or allocation order.

Allocation pressure first releases disposable prediction/readout caches, stages
clean immutable carrier generations and spills owned evidence journals. It never
truncates a coherent frame or a canonical evidence set.

# 35. Quest 3 work graph

The conceptual reconstruction hot path has only six physics stages.

```text
01 CAPTURE / SYNC
   synchronized rig capture, timestamping, calibration leases

02 FORWARD READOUT
   Ψ -> predicted L/R depth + RGB + CarrierUV using raster hardware

03 JOINT INVERSE READOUT
   measured L/R RGB-D + prediction -> per-source S16 admissible cells
   exact confidence-shaped meet; empty intersections remain conflicts
   includes first-hit causal sets, pre-hit exclusions and null->contact pullback

04 SEDENION STATE / GAUGE / TEMPORAL
   dirty transition/annihilator/associator readout,
   transient promotion, temporal scene transitions, local gauge deformation, bounded pose-cell meet

05 READOUT / PUBLICATION
   dirty Ψ -> meshlets / prediction cache / appearance readout

06 STAGE
   immutable compressed dirty carrier pages + minimal constraint certificates
   + retained unresolved observations
```

Implementation may split a stage into clear/compact/dispatch/finalize kernels, but it must not create parallel canonical geometry/topology systems.

## 35.1 Direct whole-observation lowering

The foreground implementation records one fixed GPU dataflow for each owned
synchronized observation:

```text
SEAL WHOLE FRAME
  -> current/pending candidate proposal
  -> materialize independent D_L/D_R/RGB_L/R source cells once
  -> exact stable target grouping and four-source meet
  -> existing update or exact pending-gauge closure/promotion
  -> complete immutable evidence journal
  -> exact closure of only incident claimed intrinsic transitions
  -> shadow scatter into affected backing pages
  -> one atomic frame-revision root publication
  -> dirty prediction/XR readout
```

Storage pages are allocated only while scattering resolved carrier addresses. A
page, image block, source bundle, proof block or microtile cannot own observation
identity, gauge identity, proof closure, topology closure or publication.

GPU workgroups remain bounded. If one fixed dataflow stage must span command
buffers, one generation-owned linear cursor continues its compact record stream;
it never restarts a page workflow and never changes canonical ordering. No token
scheduler, persistent per-page transaction arena or singleton proof owner exists in
the foreground path.

## 35.2 Hyperlinearized Quest execution contract

The six stages are semantic regions, not a mandate for branch-heavy procedural kernels. Within each pure bounded GPU region, execution follows the established T-language lowering pattern:

```text
semantic region
  -> exact operator DAG
  -> predicate/activity masks
  -> dense/tiled/gather/scatter work form
  -> fused or split Vulkan compute dispatches
  -> exact commit witness
```

Canonical per-lane classifications such as source validity, `HIT/PRE_HIT_EXCLUSION/NO_CONSTRAINT`, contact/null support, conflict state, singular-candidate state, commit eligibility and codec predicates are represented as bitfields/predicate masks over compacted work. A bounded branch is lowered to mask/select when both sides are pure and local. A bounded repeated operator uses a fixed recurrence/reduction schedule. Dynamic recursion, pointer graphs, allocator-owned surface traversal and exception/unwind control are forbidden in the GPU reconstruction hot path.

Instruction order is not semantic authority. The exact operator descriptor and final checked commit are authority; kernel fusion, subgroup width, workgroup shape and dispatch decomposition are execution lowering.

The CPU/reference lane exists for exact verification and recovery tooling. It must not become a second live geometry implementation.

---

# 36. Required repository assets

The active Σ-PRISM runtime core uses this layout:

```text
Runtime/SigmaPrism/
    SigmaRigBridge.cs
    SigmaCarrier.cs
    SigmaNumericDomain.cs
    SigmaOperatorSet.cs
    SigmaOperatorPlan.cs
    SigmaInverseController.cs
    SigmaWorldStore.cs
    SigmaRenderer.cs

Runtime/Resources/SigmaPrism/
    Sedenion16.hlsl
    SigmaOperatorPlan.hlsl
    SigmaPredict.shader
    SigmaInverse.compute
    SigmaState.compute
    SigmaCarrierCodec.compute
    SigmaMeshletReadout.compute
    SigmaViewCull.compute
    SigmaStage.compute
```

Additional small kernels for prefix sums, compaction or indirect args are allowed.

Do not introduce any second canonical geometry, topology, boundary, detail or appearance world under different names.

---

# 37. C# responsibilities

C# owns:

- lifecycle;
- calibration epoch selection;
- GPU resources;
- page residency;
- persistent staging;
- fences;
- immutable publication generations;
- error reporting;
- export orchestration.

C# does **not**:

- classify pixels into surface object types;
- traverse live geometry per pixel;
- build meshes on CPU;
- repair topology;
- decide boundaries;
- perform stereo matching loops.

---

# 38. Determinism requirements

1. Canonical coefficient semantics are exactly `num.fixed.q16_48.checked.nearest_even`; execution representation is a lowering, not state meaning.
2. One generated signed-XOR Cayley-Dickson multiplication-table fingerprint.
3. One generated exact annihilator-catalog fingerprint.
4. One generated exact operator-plan fingerprint per semantic operator family.
5. CPU reference and Quest lowering implement the same NumericDomain, bracket trees and generated operator descriptors.
6. Exact fixed-point equivalence means bit identity; tolerance-based acceptance is forbidden for canonical arithmetic.
7. Explicit sedenion product bracketing is preserved in semantic descriptors even when execution fuses the operator.
8. Backend capability/legality is checked before exact lowering; unsupported exact primitives cannot silently fall back to FP.
9. Stable GPU compaction order is required wherever output identity changes canonical addressing.
10. Canonical gauge allocation follows the promoted-extent order of section 18.7;
    execution partition, source-lane order, storage-page order and GPU scheduling
    cannot affect it.
11. Codec mode selection is deterministic for identical decoded state.
12. Persistence writes logical pages sorted by `(pageY,pageX,generation)`.
13. Randomized canonical algorithms are forbidden.
14. Floating-point values may drive visualization/candidate generation only where explicitly allowed; they may not decide singularity, allocation, promotion, rejection, retirement or persistence state without exact revalidation.
15. Restart from a snapshot followed by the same observation sequence produces byte-identical canonical carrier pages and proof certificates.
16. Constraint-certificate minimization order/result are deterministic.
17. DELTA bit-packing is deterministic for identical decoded blocks.
18. Temporal-transition candidate ordering and accepted transport selection are lexicographically deterministic.
19. Derived transition/operator caches are non-authoritative and keyed by canonical generation/fingerprint; deleting them cannot change a replay result.
20. A generated optimized operator that disagrees with its semantic reference by one bit is disabled for canonical mutation.
21. The same accepted observation sequence produces byte-identical `Psi`, validity,
    gaps, provenance and proof certificates under every legal workgroup, execution
    tile, proof-window and backing-page decomposition.

# 39. Runtime calibration quantities

Calibration exists to construct **constraint widths and validity**, never sensor weights.

Persist per calibration epoch deterministic integer distributions/bounds for:

```text
depth inverse-cell width by range/incidence
RGB inverse-cell width by exposure/gradient/footprint
pose prior twist-cell widths
mixed-pixel / motion widening rules
annihilatorError smooth-surface distribution
associator L1 smooth-surface distribution
contact support floor
appearance support floor
minimum independent-support classes
disappearance independent-view/angular gates
transport independent-support gate
intrinsic transport-signature tolerances
```

Statistics are accumulated in fixed integer bins or deterministic quantiles. Their only canonical effects are:

- widen/narrow a source admissible cell;
- mark a source dimension unconstrained;
- set a persisted integer validity/singularity gate.

Calibration may only change admissible bounds, validity and persisted integer gates.

# 40. Algebra proof gate H0 — mandatory before rewrite completion

Before enabling live state mutation, run the H0 fixture harness over recorded synchronized Quest frames. It validates the exact lift/readout/update constants and annihilator signatures used by the implementation.

It must evaluate:

```text
flat wall
smooth curved surface
sharp door/frame corner
5 / 10 / 20 / 50 mm plate
pipe / railing
parallel close surfaces
recess / doorway
occlusion edge
depth mixed-pixel artifact
moving hand/person
close -> far -> close revisit
new chair in previously scanned room
remove previously scanned cabinet
move same chair/cabinet to a new position
open door -> close door -> reopen
```

Measure for each:

- per-source forward admissibility;
- joint-cell width / empty-intersection rate;
- joint-meet consistency and tightening across independent views;
- annihilatorError / annihilatorId stability;
- annihilator stability across views;
- integer associator stability;
- false singularity rate on smooth surfaces;
- missed singularity rate at supported edges;
- thin-side separation;
- geometry readout error;
- retained state bytes;
- GPU work estimate;
- false disappearance under temporary occlusion;
- disappearance commit after real removal;
- transport identity retention and readout error;
- detail preservation across verified transport.

H0 passes when the persisted integer gates produce stable signatures on real folds/thin surfaces and remain regular on smooth static surfaces across the recorded fixture sequence.

If H0 fails, revise the lift/readout/operator construction or fixed integer thresholds. Do not add a parallel geometry/topology representation.

---

# 41. Algebra / NumericDomain / lowering unit tests

Mandatory tests:

1. `num.fixed.q16_48.checked.nearest_even` registry constants match the inherited FLUID/T-language contract exactly: signed 16.48, 64-bit storage, nearest-even, checked overflow, binary-power scale.
2. Q16.48 semantic fixtures for add/sub/mul/div/compare/shifts match the CPU reference bit-for-bit.
3. Every enabled Quest execution lowering for those primitives matches the same fixtures bit-for-bit; backend capability refusal is also tested.
4. Generated Cayley-Dickson table is identical in CPU and shader fixture output.
5. For every basis pair, generated output index is `i XOR j` and generated sign agrees with the recursive Cayley-Dickson reference.
6. `e_i^2=-1` for `i=1..15`.
7. Conjugation identities used by implementation hold bit-exactly on generated fixtures.
8. Every generated zero-divisor witness multiplies with its annihilator to sixteen exact zero Q16.48 coefficients.
9. Non-zero witness annihilators are non-zero.
10. Signed-dyad annihilator runtime action contains no generic Q16.48 coefficient multiply/divide and matches the reference `S16Mul(t,a_k)` exactly.
11. Selected non-zero associator fixture is reproduced bit-exactly by both explicit bracketings and by the generated fused associator plan.
12. The generic dense S16 reference product and the generated signed-XOR transition plan agree bit-for-bit on deterministic edge/random fixture vectors.
13. Generated operator plans for `B`, `B^T`, `G`, `F`, conjugation, view readout, transition, annihilator and commit transforms match their semantic references bit-for-bit.
14. Operator common-subexpression sharing does not change result bytes when optimization is disabled/enabled.
15. Transition-cache hit and forced-cache-miss paths produce identical signatures; changing either endpoint generation invalidates the cache deterministically.
16. `G z_null = 0`.
17. Algebra/numeric/operator fingerprints are stable for identical generators.
18. Geometry inverse-lift/readout round trip stays within the persisted Q16.48 sensor-quantization bound.
19. NULL/CONST/AFFINE/DELTA/RAW decode reproduces exact 8x8 samples and deterministic codec selection.
20. Certificate redundancy elimination preserves the exact justified meet and independent-support gates.
21. Mask/select lowering of every canonical bounded branch is equivalent to the scalar reference for all predicate combinations in its fixture domain.

# 42. Reconstruction invariance tests

## 42.1 Source-order invariance

For an existing supported carrier footprint, generate the same set of source cells in multiple dispatch orders. Because fusion is integer `max/min` meet, the committed canonical page bytes must be identical.

## 42.2 Weak-after-strong

Close, sharp, frontal evidence establishes a narrow prior cell. Many later far/grazing observations with broader cells may confirm validity but cannot move the state outside the narrow prior or reduce its projective information mass.

## 42.3 Strong-after-weak

A weak broad prior followed by stronger independent narrow cells must shrink the admissible set and improve the readout without averaging the old and new centres.

## 42.4 Stereo symmetry

Swap L/R dispatch order for the same synchronized observations. The joint cell and final canonical carrier bytes must be identical, not merely numerically close.

## 42.5 Explicit conflict preservation

Construct two high-confidence source cells with disjoint projective intervals. Canonical state must remain unchanged and the transient conflict must retain both source provenances and the exact gap. No cancellation is permitted.

## 42.6 No behind-hit mutation

Inject synthetic carrier state only behind a measured first hit. That state must receive no inclusive cell, exclusion cell or confidence-strength update from the measurement.

## 42.7 Carrier gauge invariance

Apply a bijective carrier reparameterization and transform `Psi` accordingly. All source admissible cells and sensor/3D readouts must remain equivalent within the persisted Q16.48 sensor-quantization bound.

## 42.8 Repeated-identical-view confidence

Replay the same observation from the same independence bin many times. Projective information mass may not grow beyond the precision justified by that source cell. Independent tighter views may raise it.

## 42.9 New-object occlusion safety

Add a supported foreground object in front of a previously stable background. The new object may promote after independent support; the background receives no exclusion and its canonical bytes remain unchanged.

## 42.10 Proven removal

Remove a previously supported object and provide independent views that pass through its old first-hit location. One/two insufficient exclusions do not mutate it. After the exact section 29.4 gate, the affected carrier region becomes `z_null` and the newly visible background remains or becomes supported without averaging with the removed state.

## 42.11 Coherent transport

Move a previously detailed rigid target. A verified section 29.5 transform must move the same carrier region, preserve its hidden/intrinsic state and information mass, release the duplicate probationary gauge region, and reproduce the new observations. The old physical location must no longer render contact.

## 42.12 Occlusion versus removal

Replay identical old-object state under two sequences: (A) a nearer temporary occluder, (B) direct pass-through after physical removal. Sequence A must not produce disappearance evidence for the hidden object; sequence B must.

## 42.13 Transport fallback correctness

Present a genuinely different new object where an old object disappeared. If intrinsic constraints or one common transform fail, transport must be rejected. Old state may retire only through the disappearance gate; new state must earn independent null-to-contact promotion.

# 43. Critical physical acceptance corpus

## Thin surfaces

```text
5 mm, 10 mm, 20 mm, 50 mm
front -> back -> front revisit
near -> far -> near
```

Both sides must remain distinct and improve independently.

## Boundary / fold

```text
door frame
open door
closed door
wall corner
recess
square and round pipe
railing
trim / skirting
stair edges
oblique edge
narrow gap
```

No foreground/background bridge may cross a supported silhouette.

## Latent / unknown

Scan only the front/side of a plate, pole, recess and doorway. Hidden backs remain unobserved, not fabricated, not deleted, and not marked empty.

## Detail

```text
flat wall
textured plaster
embossed trim
printed high-frequency texture
small screw/edge detail
```

Close/subpixel revisits must improve supported detail. Later distant passes must not erase it.

## Persistent scene change

Run from one durable baseline scan:

```text
add a new chair
walk/person occludes old furniture temporarily
remove a previously scanned cabinet
move the same chair to another position
open door -> closed door -> open door
replace one object with a visually different object in the same region
```

Required behaviour:

- added foreground state must not degrade hidden background;
- temporary occlusion must not retire hidden durable state;
- real removal must commit only after independent pre-hit/pass-through proof;
- a verified moved rigid surface must preserve the same carrier detail/information through coherent transport;
- a different replacement object must not steal the retired carrier identity when transport constraints fail;
- current prediction after each committed change must contain only the currently supported configuration;
- optional historical revisions must not participate in current prediction.

## Scale

```text
room -> corridor -> stairwell -> next floor -> whole building -> return
```

Active GPU cost follows visible/dirty carrier pages and all durable state rehydrates stably.

---

# 44. Quest 3 performance contract

The implementation is bounded by active locality and by generated-operator work, not by total world size.

```text
sensor ingress                 never waits for reconstruction
forward readout                visible/resident carrier only
inverse update                 constraint-active pixels/carrier only
state/gauge work               dirty local carrier neighbourhood only
transition algebra             only transitions whose endpoint generation or active evidence changed
mesh publication               <= 15 Hz default; independent of sensor ingress
full-world per-frame pass      forbidden
synchronous GPU readback       forbidden
CPU geometry hot path          forbidden
```

Every physical Quest profiling build exposes at least these counters per frame/stage:

```text
activeCarrierSamples
activeTransitions
transitionCacheHit / transitionCacheMiss
xorPermutationOps
signedAddSubOps
maskSelectOps
q48WideMulOps
q48DivOps
intervalMulDivOps
annihilatorWitnessEvals
genericDenseS16Products
operatorPlanInvocations
sourceCellsBuilt / sourceCellsMet
emptyMeets
bytesRead / bytesWritten
GPU time per stage
GPU time per production kernel
dispatches / records / coordinates processed per kernel
owned-frame backlog and oldest-frame age
resident carrier / source-cell / evidence / raw / readout bytes
```

The counters are diagnostic/readout state only and never influence canonical physics.

`genericDenseS16Products` is forbidden in per-source admissible-cell fusion, annihilator-dyad action, Hadamard/readout transforms and mask/control lowering. A dense S16 product in transition/associator/view work is permitted only through the generated operator plan and is counted explicitly. The implementation may replace such a product with a cheaper exact generated circuit at any time without changing semantics.

Performance acceptance compares whole-frame/stage work against the contract; multiplying the theoretical cost of a slow reference primitive by all pixels/edges is not an implementation model. Conversely, a slow generated lowering may not hide behind architectural claims: profiler evidence on Quest is required before final acceptance.

Release profiling uses actual GPU timestamp markers around every production
compute/raster stage. An unavailable platform recorder is reported explicitly and
cannot be represented as a zero-duration sample. Timestamp results and asynchronous
diagnostic readback never control canonical work selection or mutation.

The active-memory profiles and pressure rules are section 34. No per-category
scratch capacity is a correctness limit. When pressure approaches the selected
profile, reclaim disposable caches, stage clean carrier generations or spill the
complete owned evidence stream. Never alter accepted Q16.48 state, sensor
resolution, proof contents or source order.

# 45. Persistence

Persist exactly the information required to recreate the same carrier state:

```text
world/
    manifest
        schema = CPQ4-S16-v7
        numericDomainId = num.fixed.q16_48.checked.nearest_even
        multiplicationTableFingerprint
        annihilatorCatalogFingerprint
        operatorPlanFingerprint
        operatorRowIndices
        zNullRaw[16]
        QFormat = Q16.48
        calibration epochs
        world revision

    carrier/
        sparse logical page records
        page generations
        NULL / CONST / AFFINE / DELTA / RAW exact Q16.48 payloads
        minimal constraint certificates required by the selected revision

    observations/
        unresolved raw RGB-D constraint tiles only
        pose + calibration epoch + carrier footprint + transition provenance

    derived/             optional, disposable
        meshlet/readout caches
```

Canonical durable world state is the sparse Q16.48 carrier. Fixed operator/algebra metadata and minimal constraint certificates are interpretation/proof metadata required to continue deterministic inference; they do not form a second physical world.

Stop pauses ingress; it does not destroy the live carrier. Restart restores the same bytes and continues inverse refinement.

# 46. Direct GLB/PBR export

Export is a readout only:

```text
persistent Ψ
    -> geometry / singularity / appearance readout
    -> adaptive tessellation
    -> indexed mesh + confidence-bearing PBR
    -> GLB
```

Support selected spatial region and paged whole-world export. Export never mutates canonical carrier state.

# 47. Repository transition

Keep only representation-neutral infrastructure from the existing source tree:

```text
sensor acquisition and timestamp synchronization
rig calibration and projection metadata
XR lifecycle
GPU resource/fence/indirect-dispatch utilities
renderer/view-cull plumbing that accepts new readout buffers
asynchronous world storage shell
GLB/export plumbing
```

Archive the previous reconstruction core before the rewrite. Do not keep compatibility adapters in the live hot path. Old geometry/topology types may exist only in archived source or one-way migration tooling.

The active reconstruction namespace is `Runtime/SigmaPrism` and `Runtime/Resources/SigmaPrism`.

# 48. Forbidden architecture violations

The implementation is invalid if it introduces any durable physical state whose geometry, topology, detail or appearance can disagree with `Psi`.

In particular:

- independent sensors may not be collapsed before the admissible meet;
- evidence behind the measured first hit may not change state;
- 3D proximity may not define carrier identity;
- derived meshes/caches may not become authoritative reconstruction state;
- paging or codec boundaries may not acquire physical meaning;
- scene history may not participate in current prediction unless that historical revision is explicitly selected as the current world;
- transient masks, transport candidates and constraint certificates may not render as independent physical surfaces.
- the canonical Q16.48 domain may not be redefined as a Quest-specific two-limb ontology; packed limbs are only a backend lowering;
- generic schoolbook S16 multiplication may not replace generated signed-XOR/sparse/dyadic operators in hot paths where those exact specialized forms exist;
- backend execution layout, subgroup order or shader dispatch shape may not become semantic authority.

Any cache must be deletable and reproducible from the selected durable `Psi` revision plus its fixed interpretation/proof metadata.

# 49. Deterministic Codex rewrite sequence

Execute in this order. Each step leaves the repository buildable and has an explicit acceptance gate.

## S4-00 — Archive and activate

- archive/tag the current reconstruction source;
- create the Sigma implementation branch/worktree;
- make this specification canonical;
- reduce README/ALGORITHM to this architecture and current run state.

## S4-01 — Inherited NumericDomain + generated S16 operator core

Implement first, with no scanner dependencies. **Do not design a new fixed-point model.** Port/reuse the established FLUID/T-language semantic contract and build the Quest lowering around it:

```text
NumericDomain registry entry: num.fixed.q16_48.checked.nearest_even
CPU semantic/reference fixtures
Quest exact backend capability table
native-I64 lowering when proven legal
packed-32/widened fallback only for primitives that require it
signed-XOR Cayley-Dickson basis/sign generator
conjugation + left/right basis permutation descriptors
generic dense S16 reference multiply (tests/fallback, not default hot path)
exact signed-dyad annihilator generator
Hadamard/readout row generator
generated exact operator IR + optimizer
CPU evaluator and HLSL/Vulkan lowering from the same operator descriptors
transition generation cache
numeric/algebra/operator fingerprints
```

The generated operator IR must already lower sign/XOR/permutation/Hadamard/dyad/mask operations without generic coefficient multiply/divide. It must preserve bracket trees and share common subexpressions. Scanner code in later runs consumes these generated operators; it does not hand-code alternate S16 arithmetic.

Gate:

```text
all NumericDomain fixtures exact
recursive CD reference == generated signed-XOR table
CPU operator reference == Quest operator lowering bit-for-bit
all zero-divisor/annihilator fixtures exact
mask/select fixtures exact
cache hit/miss equivalence exact
no generic qmul/qdiv in signed-dyad annihilator action
no unapproved schoolbook S16 loop in the live operator plan
```

## S4-02 — Carrier

Implement one sparse logical 2D carrier:

```text
signed page coordinates
implicit z_null pages
8x8 logical blocks
NULL / CONST / AFFINE / DELTA / RAW exact Q16.48 codec
immutable page generations
certificate ranges per page
dirty compaction
```

Gate: encode/decode/restart are byte-identical.

## S4-03 — Forward geometry readout

- reuse synchronized rig/calibration inputs;
- read supported carrier into depth + CarrierUV + support;
- render with existing raster hardware;
- null state emits no contact.

Gate: synthetic folded/null carrier fixtures produce deterministic expected depth/UV.

## S4-04 — Joint inverse depth readout

- consume both depth eyes directly against one predicted `Ψ`;
- construct independent Q16.48 S16 admissible cells;
- confidence controls cell width only;
- fuse only by the exact section 12 meet;
- preserve empty intersections with source provenance;
- enforce exact behind-hit no-constraint rule;
- unmatched supported measurements enter exact pending gauges and allocate
  canonical carrier extent only at promotion under section 18.

Gate: static room fixture converges without a second geometry representation.

## S4-05 — Exact singular topology

- implement transition state;
- exact annihilator catalog lookup/residual;
- persistent annihilator signature gate;
- integer associator gate;
- singular render cuts/folds;
- no boundary objects.

Gate: wall, crease, doorway and thin-surface fixtures have the expected stable signatures.

## S4-06 — Joint RGB inverse readout

- add RGB_L/R as independent admissible cells in the same inverse meet;
- same carrier state produces geometry and appearance readouts;
- RGB may refine geometry only by narrowing the shared admissible set;
- incompatible RGB/depth cells remain explicit conflicts;
- reduce absorbed evidence to deterministic minimal constraint certificates; retain raw tiles only while future inverse evaluation still needs them.

Gate: repeated subpixel views sharpen the same state without creating a texture-world side channel.

## S4-07 — Gauge refinement and detail

- detect insufficient carrier sampling from repeated joint-cell width, footprint and readout reproducibility;
- allocate/deform carrier gauge locally and bijectively;
- preserve physical readout across pure gauge moves;
- no multiresolution geometry ontology.

Gate: gauge transform followed by inverse transform reproduces the same Q16.48 readout within the persisted Q16.48 sensor-quantization bound.

## S4-08 — Pose gauge

- construct bounded per-source pose-twist cells from the same readout constraints;
- intersect them with the Meta prior;
- pose correction changes readout gauge, not canonical topology;
- pose acceptance uses only the bounded source-cell intersection defined in section 28.

Gate: revisit improves complete forward-readout agreement while canonical carrier connectivity remains unchanged.

## S4-09 — Persistent scene evolution

- preserve independent pre-hit exclusion provenance;
- implement null->contact, contact->null and coherent-transport transitions from section 29;
- resolve transport before disappearance retirement;
- preserve carrier detail/information through verified transport;
- distinguish nearer occlusion from direct pass-through removal evidence;
- keep transition masks/candidates transient and disposable.

Gate: new-chair, cabinet-removal, moved-target and open/closed-door fixtures pass sections 42.9-42.13 with byte-deterministic current-state results.

## S4-10 — Infinite paging

- page/stage carrier by locality;
- background durable write of immutable generations;
- restart and revisit;
- no chunk-induced physical seams.

Gate: leave area, evict, reload and revisit with byte-stable untouched pages.

## S4-11 — Readout renderer

- dirty carrier -> meshlets;
- singular transitions determine cuts/folds;
- GPU cull/LOD/indirect draw;
- meshlets remain disposable.

Gate: deleting all derived meshlets and regenerating them produces the same geometry readout.

## S4-12 — PBR / GLB

- directional appearance readout;
- confidence-bearing PBR;
- direct GLB export from current readout.

## S4-13 — Physical Quest acceptance

Run the corpus in section 43 and record only absolute correctness/performance metrics required by this spec. Fix the canonical operator/lift/readout if a capability fails; do not add a parallel reconstruction ontology.

# 50. Definition of done

Complete means all of the following are physically demonstrated on Quest 3:

```text
one durable Ψ : Σ -> S16 is the only canonical reconstruction state
canonical S16 arithmetic/topology is Q16.48 and deterministic
both RGB and both depth streams constrain the same inverse readout without sensor summation
first-hit causality leaves behind-hit state untouched
thin front/back surfaces remain independent
real folds/edges produce stable annihilator/associator singular signatures
no explicit surface/boundary/topology graph is required
revisit improves supported geometry and appearance without coarse degradation
subpixel RGB views improve observable detail
unobserved carrier remains null rather than fabricated
world paging changes residency only, never physical state
restart continues from byte-identical canonical pages and proof certificates
lossless codec round-trips every canonical carrier sample exactly
mesh and GLB are reproducible disposable readouts
new persistent surfaces can appear without degrading hidden background
proven removed surfaces return to latent carrier without carving behind-hit space
verified moved rigid regions preserve carrier identity/detail through coherent transport
temporary occlusion cannot retire hidden durable state
sensor ingress remains asynchronous and never waits for reconstruction
active allocation stays inside the Quest 3 budget in section 44
Q16.48 semantics are inherited as one NumericDomain and backend lowering is exact/replaceable
generated signed-XOR/operator plans are bit-identical to semantic references
annihilator/readout/mask hot paths contain no unnecessary generic dense S16 or generic fixed-point multiply/divide
Quest profiler exposes and validates the section 44 exact-operator counters
```

A compile or synthetic-only test is insufficient; section 43 physical fixtures are mandatory.

# 51. Architectural summary

The scanner reconstructs exactly one higher-dimensional object:

\[
\boxed{\Psi:\Sigma_2\rightarrow\mathbb S_{16}}.
\]

The carrier is one logically unbounded 2D sheet. Its canonical coefficients and all topology-changing algebra are exact Q16.48.

Quest observations are readouts:

\[
Y_t=\mathcal R_t[\Psi].
\]

Scanning applies the joint inverse readout to that same state. Every sensor produces an independent confidence-shaped admissible S16 cell; their common state is the exact intersection:

\[
\mathcal C_t=\bigcap_q\mathcal C_{t,q},
\qquad
\Psi_{t+1}=\mathcal U(\Psi_t,\mathcal C_t).
\]

Left/right RGB-D are simultaneous constraints, not separately reconstructed worlds. Finite pixel extent is the readout footprint. First-hit causality is intrinsic to the inverse operator and has exactly zero post-hit effect. Fine detail is literal local variation of the same carrier. Geometry, colour, directional appearance, normals and PBR are operators/readouts of the same state.

The latent world is the same carrier in `z_null`. Observable folds, branch changes and boundaries are singular transitions of the sedenion state. Exact signed-dyad annihilators make those transitions cheap and deterministic: the hot path uses generated signed permutations/additions and integer residuals, not floating singular-value machinery.

Persistent scene changes are transitions of that same state: latent-to-contact for appearance, contact-to-latent for proven disappearance, and verified coherent transport for movement. No historical surface union or semantic object graph is required.

`Psi` is the only durable physical world state. Fixed algebra/readout metadata and the minimal proof certificates required by the selected revision are durable inference metadata; all other implementation state is disposable.
