# Σ‑PRISM‑16

## Full-field native S16 world, Merkaba/eigenmode law, scene-shadow closure and pure readouts

**Canonical reconstruction baseline:** `CPQ4-2026-08-25-S16-v8.1`
**Target device:** Meta Quest 3
**Implementation target:** Unity / Android / Vulkan / GPU-first
**Status:** ontology-reset canonical replacement specification

Version 8.1 replaces both the v7 classical scanner graph and the germ-first v8
draft. It does not rename a conventional association/inverse/topology pipeline
with native terminology. The scanner is a sparse exact lowering of one full-field
native feasibility equation.

The physical direction is from the native S16 field through the authoritative
Merkaba/eigenmode law into a whole-scene lower-dimensional shadow. Scan follows
the inverse causal direction by constraining possible native fields. It never
promotes a 3D reconstruction into S16 and never applies one observation to every
candidate support.

---

# 0. Authority, scope and replacement rule

This document is the sole canonical reconstruction/product specification.

The live implementation must be self-contained. Exact TOE/Merkaba/eigenmode
expressions may enter as authoritative build-time source material, but they become
runtime authority only after generator ingestion, semantic validation and frozen
fingerprints. Scanner code may not invent missing relations, eigenmodes, brackets,
null strata or refinement laws.

Version 8.1 is a replacement, not an appendix or compatibility mode. The following
are explicitly noncanonical:

```text
four sensor-cell worlds
per-pixel/per-germ physical sensor shadow
CURRENT / PENDING / CONTINUATION / NOVEL physical kinds
candidate identity inside immutable observation evidence
one-winner association
mutation of every enumerated candidate support
LatentGerm as the first form of unresolved evidence
separate inverse then stitch/topology semantic authorities
image/XYZ proximity topology
pixel/global-bbox carrier allocation
ABSENT storage interpreted as a materialized native-null state
scalar confidence used as canonical physics
live mesh/XYZ/texture world
```

Each S4‑08.6 replacement commit deletes the branch it supersedes. No feature flag,
legacy fallback, renamed manager or parallel solver may remain.

Representation-neutral infrastructure may survive: synchronized capture,
immutable calibration/poses, XR lifecycle, permissions/anchors/input/UI, Vulkan
resource/fence/indirect helpers, asynchronous persistence plumbing and GLB
encoding utilities.

---

# 1. Canonical physical world

The only canonical reconstructed world is

\[
\boxed{\Psi:\Sigma_2\rightarrow\mathbb S_{16}}.
\]

`Σ₂` is a sparse, logically unbounded intrinsic two-dimensional **atlas
namespace**. It can contain multiple disconnected chart components, folds,
multi-sheet manifestations and two-sided surfaces. It is not one camera grid, one
connected sheet, a voxel lattice, a render mesh or a storage page layout.

For `ξ∈Σ₂`,

\[
s_\xi=\Psi(\xi)\in S16
\]

is the complete native local algebraic state. Its sixteen coefficients are not
independent `xyz/rgb/normal/confidence/...` channels.

The canonical world comprises:

```text
full allocated S16 carrier states
intrinsic atlas chart domains and incidence
descriptor/fingerprint interpretation metadata
minimal exact evidence/native-relation certificates
the selected immutable revision root
```

Chart incidence is canonical domain structure of `Σ₂`; it is not a separately
editable topology world.

No readout, candidate, hypothesis, observation record, certificate, mesh, texture,
topology graph, scene-history object or cache is canonical physical state.

## 1.1 ABSENT is not NATIVE_NULL

Two states are distinct:

```text
ABSENT
    no materialized carrier/chart state exists at that atlas address

NATIVE_NULL
    an allocated descriptor-defined S16 null-manifestation state or stratum
```

Sparse `ABSENT` consumes no page/sample storage and makes no native-state claim.
`NATIVE_NULL` may be nonzero and may use zero-divisor/eigenmode structure. Its
exact state/stratum and fingerprint come only from the authoritative native law.

No codec tag, missing page, zero-filled memory or allocator default may silently
equate these concepts.

---

# 2. Native-16D field viewpoint

A camera produces a lower-dimensional drawing of a native S16 field. It does not
observe an isolated germ, and it does not supply primitive 3D reality later encoded
into S16.

The authoritative native law may depend on a bounded intrinsic neighbourhood:

\[
\Psi|_{\mathcal N_r(\xi)}.
\]

For a query `q`, one carrier locality produces only a **local contribution**

\[
\phi_{q,\xi}[\Psi]
=
\Pi^{loc}_{q,\xi}
\left(
\mathcal M
\left(
\mathcal K_M,
\mathcal E_M,
\Psi|_{\mathcal N_r(\xi)}
\right)
\right).
\]

This is a lowering primitive. It is not the physical sensor observation.

The camera/eye observes the whole-scene shadow

\[
\boxed{
\mathscr S_q[\Psi]
=
\mathfrak R_q
\left(
\{\phi_{q,\xi}[\Psi]\}_{\xi\in\Sigma_2}
\right).
}
\]

The scene reducer owns the many-to-one effects that no single germ can decide:

```text
overlap of multiple native supports
direct projective order
first manifested stratum / first-hit
occlusion and behind-hit no-claim
fold/two-sheet collapse in a query
native-null manifestation
query-relevant ZD relation collapse
nonassociative context with its frozen bracket plan
finite-footprint integration
```

Scan therefore asks which possible native fields could have produced the measured
whole-scene shadow. It does not ask which one germ produced one pixel.

---

# 3. Non-negotiable invariants

1. **One full native field.** `Ψ:Σ₂→S16` is the only physical world.
2. **One authoritative native law.** Algebra, eigenmodes, relations,
   manifestation, local projection, scene reduction, ZD and brackets share one
   generated descriptor.
3. **No assumed unary E22 ontology.** Exactly 22 relations may be used only as an
   inventory supplied by TOE; completeness requires a faithfulness proof.
4. **Local contribution is not observation.** First-hit and occlusion are
   whole-scene reduction results.
5. **Two coherent sensor shadows.** Left and right each contain independent depth
   and optical leaves with shared eye pose/footprint provenance.
6. **Observation identity is not support identity.** Immutable evidence contains
   no native/chart/candidate key.
7. **Alternative supports are a disjunction.** `A OR B` is neither one winner nor
   simultaneous mutation of A and B.
8. **Ambiguity does not mutate.** Only a resolved hypothesis or a delta common to
   every surviving hypothesis may publish.
9. **One semantic closure.** Observation, Merkaba/eigenmode and intrinsic atlas
   relations share one feasible set before selection.
10. **Minimum change cannot choose a hypothesis.** It acts only inside an already
    resolved harmless-equivalence fibre.
11. **Hidden native modes survive by construction.** Linear updates use a proven
    right-lift/direct-sum decomposition; nonlinear updates preserve the prior
    representative on indistinguishable fibres.
12. **Exact ZD differs from near singular.** A nonzero calibrated residual is not
    an exact zero divisor.
13. **Direct order differs from ZD.** ZD is never fake depth.
14. **Nonassociativity is semantic.** Every multi-factor relation keeps the
    authoritative bracket tree.
15. **Atlas incidence is intrinsic.** Geometry proximity cannot create
    connectivity or identity.
16. **ABSENT differs from NATIVE_NULL.** The distinction survives codec,
    persistence, refinement and replay.
17. **Confidence is not canonical scalar physics.** Exact uncertainty is a region,
    disjunction, provenance and independence structure.
18. **Independent evidence never votes.** Repetition alone adds no information.
19. **Static correction is part of reconstruction.** Valid exclusion may remove a
    false same-scene manifestation; behind-hit still provides no evidence.
20. **Refinement is modal and intrinsic.** It adds full S16 states only after the
    authoritative capacity law proves current atlas/modal insufficiency.
21. **Readouts are pure.** Eye, prediction, export and debug cannot mutate or
    impoverish `Ψ`.
22. **Pages/segments are storage only.** Decomposition changes cost only, never
    physical cardinality or result.
23. **One immutable root.** Readers see the entire old or entire new revision; root
    exchange is last.
24. **No admitted evidence loss.** Pre-admission sampling may be deterministic;
    admitted observations remain owned until a terminal disposition.

---

# 4. Why S16 is the native local algebra

Within the Cayley-Dickson ladder

\[
\mathbb R\rightarrow\mathbb C\rightarrow\mathbb H
\rightarrow\mathbb O\rightarrow\mathbb S,
\]

the required native relation semantics include both explicit bracket sensitivity

\[
[a,b,c]=(ab)c-a(bc)\ne0
\]

and non-trivial exact zero-divisor/annihilator strata

\[
z\ne0,\qquad a\ne0,\qquad za=0.
\]

S16 is the first Cayley-Dickson stage possessing both. This is a native local
state-space minimality statement, not the dimensionality of manifested physical
position. Ordinary 3D is one query/readout.

---

# 5. Canonical NumericDomain

Canonical coefficients and every state-changing scalar decision use:

```text
NumericDomain = num.fixed.q16_48.checked.nearest_even
signed         = true
int_bits       = 16
frac_bits      = 48
storage_bits   = 64
point_rounding = nearest-even
interval       = outward-rounded
overflow       = checked, fail-closed
ONE            = 1 << 48
range          = [-32768,32768)
```

Required primitives include checked add/subtract/multiply/divide, dyadic shifts,
exact comparisons, outward interval arithmetic and descriptor-required bounded
integer roots.

FP16/FP32 may run only after a canonical decision for disposable output. Floating
point cannot decide feasible branches, first-hit, ZD class, chart allocation,
native state, evidence proof, publication or persistence.

Packed-32 and native-I64 are execution lowerings. Each is enabled only after exact
CPU/GPU parity and capability gates; unsupported arithmetic disables canonical
mutation.

---

# 6. Exact S16 algebra and generated semantic IR

Use basis `e0=1,e1,…,e15` and generated signed-XOR multiplication

\[
e_ie_j=\varepsilon_{ij}e_{i\oplus j},
\qquad\varepsilon_{ij}\in\{-1,+1\}.
\]

The generator owns:

```text
basis product index/sign
conjugation
left/right basis permutations
exact signed-dyad annihilator catalog
all TOE native operator/eigenmode relation expressions
all explicit bracket trees
query/local-contribution/scene-reducer plans
reverse contractors and reference evaluators
stable semantic/lowering fingerprints
```

Every product of more than two factors has an explicit bracket tree. `a*b*c` is
invalid semantic source. GPU fusion records exactly which frozen tree it lowers.

Generated IR includes:

```text
XOR_INDEX / PERMUTE / SIGN / NEGATE
ADD / SUB / SHIFT
CMP / MIN / MAX / MASK / SELECT
GATHER / SCATTER
FIXED_BOUNDED_REDUCTION
QMUL / QDIV only when semantically required
INTERVAL_MUL / INTERVAL_DIV for conservative reverse propagation
UNION_BRANCH / INTERSECT_CONSTRAINT / FORWARD_VERIFY
```

Common subexpressions are shared across the entire native law. Dense schoolbook
S16 multiplication remains a semantic reference or explicit fallback for a truly
dense generated expression, never the default vocabulary.

---

# 7. Authoritative Merkaba/eigenmode descriptor

## 7.1 Descriptor schema

Freeze one generated descriptor:

\[
\boxed{
\mathcal D_M=
(\mathcal A_{S16},\mathcal K_M,\mathcal E_M,\mathcal M,
\Pi^{loc}_q,\mathfrak R_q,\mathcal Z,\mathcal B,\Delta).
}
\]

It contains:

```text
A_S16      exact signed-XOR S16 algebra
K_M        authoritative TOE Merkaba/eigenmode operator/frame/kernel semantics
E_M        generated native relation family with explicit arity and brackets
M          native manifestation law
Pi_loc     local query contribution/contraction plans
R_q        whole-scene shadow reduction plans
Z          exact ZD/annihilator and calibrated near-singular strata
B          explicit nonassociative context/bracket plans
Delta      fibre-preserving selector inside a resolved harmless equivalence class
```

`E_M` may contain unary state relations, binary neighbour relations, k-ary
nonassociative relations and bounded-neighbourhood relations. The implementation
must not force the native law into unary `E_k(s)` form.

The descriptor also freezes:

```text
operator arities and domains
input roles and intrinsic neighbourhood radius
all constants, permutations and bracket trees
native eigenmode/eigenspace identifiers
query coupling and mode/kernel visibility
mode transport and refinement-capacity semantics
local contribution plans
whole-scene first-hit/order/occlusion reducers
reverse contractor schedules
exact consistency identities
harmless gauge/null equivalence
semantic and lowering fingerprints
```

The exact TOE artifact is mandatory. If it is absent or incomplete, N1R is
blocked; current scanner equations cannot substitute for it.

## 7.2 Optional E22 inventory and faithfulness

If the authoritative artifact supplies exactly 22 relations, the generator may
name their inventory `E22`. The automatically safe dimensional statement is only

\[
\dim E_{22}(S16)\le16.
\]

Before the full law may factor exclusively through `E22`, prove on the canonical
admissible domain:

\[
E_{22}(s_a)=E_{22}(s_b)
\Longrightarrow
s_a\equiv s_b,
\]

where `≡` is the frozen harmless gauge/null equivalence from the descriptor.

If this faithfulness gate fails or remains unproven:

- direct S16/eigenmode dependencies remain in the descriptor;
- local manifestation, inverse closure and export may not route solely through
  `E22`;
- no native direction may disappear because the 22-relation inventory does not
  separate it.

Relation values are derived/cached outputs. They are never independently editable
canonical lanes.

## 7.3 Generated authorities

One semantic descriptor generates:

```text
ForwardNativeLawReference
ProjectLocalNativeContribution
ReduceWholeSceneShadow
ContractNativeWorldPreimage
CloseNativeFieldReference
NativeRelationClassification
NativeModeTransport
NativeRefinementCapacity
readout-specific query plans
```

These are evaluators of one law, not separate physical solvers.

---

# 8. Forward manifestation and whole-scene shadows

For each materialized atlas locality `ξ`, query `q` and descriptor-defined bounded
neighbourhood, evaluate

\[
\phi_{q,\xi}[\Psi]
=
\Pi^{loc}_{q,\xi}
\left(
\mathcal M(
\mathcal K_M,
\mathcal E_M,
\Psi|_{\mathcal N_r(\xi)})
\right).
\]

`φ` may include:

```text
native support/chart key
direct-order interval
homogeneous query coordinate interval
optical/directional interval
native-null/support role
relation/ZD/bracket witness
finite-footprint coverage contribution
```

The physical query shadow is

\[
\boxed{
\mathscr S_q[\Psi]
=
\mathfrak R_q
(\{\phi_{q,\xi}[\Psi]\}_{\xi\in\Sigma_2}).
}
\]

`mathfrak R_q` performs exact or conservatively outward-bounded many-to-one
reduction:

1. collect all local contributions overlapping the query footprint;
2. preserve alternative support keys and relation witnesses;
3. resolve direct-order intervals where provable;
4. compute first manifested stratum and occlusion;
5. preserve ambiguous equal/overlapping order as a hypothesis group;
6. apply descriptor-owned ZD/nonassociative reduction only where query semantics
   require it;
7. integrate the calibrated finite footprint;
8. emit the shadow plus complete support-hypothesis provenance.

No local projector owns first-hit. No isolated state is declared to be what the
camera sees.

The reducer is query-specific but generated from the same native law. Eye, sensor,
export and debug queries may expose different lossy shadows without changing
`Ψ`.

“Whole-scene” is semantic coverage, not a requirement to traverse every persisted
page. A disposable conservative query-support index may cull a locality only when
its exact bound proves zero contribution to the query. If a potentially relevant
support is nonresident or the index is incomplete, the query/closure defers and
rehydrates; it may not omit the support or mint a replacement identity.

---

# 9. Canonical stereo observation

One admitted rig observation is

\[
\boxed{Y_t=(O_L,O_R,\Gamma_t)}
\]

with

\[
O_L=(D_L,RGB_L,F_L,H_L),
\qquad
O_R=(D_R,RGB_R,F_R,H_R),
\]

where:

```text
D       calibrated depth/direct-order region
RGB     calibrated optical relation region
F       finite footprint geometry
H       measured first-hit/support role
Gamma   timestamped rig pose, intrinsics, extrinsics and calibration epoch
```

The rig supplies **two coherent RGB-D shadows**, not four unrelated physical
worlds. Within each side, depth and RGB remain independent relation leaves:

\[
O_e=O_{D_e}\cap O_{RGB_e}
\]

only at the exact relation conjunction. Depth may not initialize or pre-contract
the RGB native domain, and RGB may not alter the depth uncertainty model.

Left and right remain independent support classes under the frozen rig
correlation model. Repeated frames share an independence class unless baseline,
pose, footprint phase or sensor statistics prove otherwise.

The immutable observation contains no native support key, germ key, chart address
or latent identity.

---

# 10. Exact inverse as a preimage of possible fields

## 10.1 World preimage

For query `q` and measured shadow region `O_q`, the exact preimage is

\[
\boxed{
\mathcal P_q(O_q)
=
\{\Phi:\mathscr S_q[\Phi]\in O_q\}.
}
\]

This is a set of possible native fields, not a per-germ inverse and not a floating
pseudoinverse.

Each possible field includes the affected atlas support/materialization and
incidence choices together with full S16 values; it is not merely a vector update
over an already chosen support.

For a measured footprint, scene reduction generally produces alternative support
hypotheses:

\[
\boxed{
\mathcal P_{q,p}(O)
=
\bigcup_{H\in\mathcal H_{q,p}}
\mathfrak A_{q,p,H}(O).
}
\]

The union is mandatory ontology. If supports `A`, `B` and `C` remain possible,
the observation means `A OR B OR C`.

It does **not** mean:

```text
choose one nearest/lowest handle
constrain A, B and C simultaneously
mutate every enumerated support
mint a new branch because enumeration was incomplete
```

## 10.2 Exact reverse propagation

For each support hypothesis, contractors reverse the exact descriptor expression
DAG while retaining original predicate records. They do not algebraically
reassociate nonassociative products.

A contractor may emit:

```text
conservative Q16.48 S16/field enclosure
exact relation predicates
branch/disjunction cursor
support/chart constraints
direct-order and first-hit role
source/provenance references
forward-verification obligations
```

Outward propagation may be broad. It may not remove a mathematically possible
field. Resource exhaustion produces `UNRESOLVED`, never false accept/reject.

## 10.3 Resolved/common delta rule

Canonical mutation is legal only if one of these is proven:

1. exactly one native support hypothesis remains and its canonical delta is
   resolved modulo frozen harmless gauge equivalence;
2. every surviving hypothesis induces the same byte-identical canonical delta on
   every affected field key;
3. the authoritative native law proves one common update valid over the complete
   disjunction.

Let `D(H)` be the set of legal sparse deltas under hypothesis `H`. A common delta
`d` is publishable only when

\[
\forall H\in\mathcal H_{survive}: d\in D(H)
\]

and its complete affected-key/value set is identical under every branch.

Otherwise the result is

```text
UNRESOLVED_SHADOW_BRANCH
canonical delta = empty
evidence retained
```

---

# 11. Scene-level first-hit and exclusion

First-hit is evaluated inside `mathfrak R_q`, after all relevant local contributions are
available.

For one measured footprint, each support hypothesis contains a direct-order
interval and one role:

```text
SUPPORTED_HIT
PRE_HIT_EXCLUSION
BEHIND_HIT_NO_CLAIM
ORDER_UNRESOLVED
```

## 11.1 Compatible first stratum

If measured and predicted first-stratum order regions overlap under calibrated
uncertainty, the observation may constrain that support hypothesis.

## 11.2 Measured stratum in front

An older support strictly behind the measured first stratum receives no evidence.
The measured shadow may be explained by a different supported or unresolved
branch.

## 11.3 Predicted manifestation in clear measured pre-hit path

If a predicted manifestation lies strictly in the independently observed clear
pre-hit path, emit exact exclusion evidence against that support branch.

Exclusion participates in the same static field closure. It may remove a false
same-scene manifestation when independent pass-through evidence and the native law
resolve the field. It is not automatically deferred to temporal scene evolution.

## 11.4 Behind-hit invariant

Anything behind the measured first hit receives:

\[
\boxed{\text{no inclusive constraint, no exclusion and no evidence strengthening}.}
\]

No ray carving or inferred free-space volume is canonical.

## 11.5 Direct order, ZD and nonassociativity are distinct

```text
direct projective order   decides front/back ordering
exact ZD                  identifies exact null/singular algebraic relation
nonassociative bracket    defines context-sensitive composition
```

All can affect scene reduction, but none may impersonate another.

---

# 12. Evidence and disposable support hypotheses

## 12.1 Immutable `ShadowObservation`

```text
observation revision
sensor side / independence class
rig pose/calibration epoch
finite footprint
measured depth/direct-order region
measured optical relation region
measured first-hit/support role
raw reference when exact replay/refinement requires it
```

It contains no native key, chart address, support identity or candidate identity.

## 12.2 Disposable `ShadowHypothesis`

```text
ShadowObservation id
hypothesis-group id
candidate native support key/range OR unbound support branch
predicted local-contribution witness
whole-scene reducer role
direct-order/support interval
native relation / ZD / bracket witness references
```

Many hypotheses may reference one observation. They are possibilities, not
physical objects. Sort/compaction handles are execution details and cannot be
persisted as identity.

## 12.3 Coverage and pruning

Candidate generation must conservatively include every resident or required
nonresident support whose shadow bounds can explain the measurement. Pruning needs
an exact conservative incompatibility proof.

If complete support coverage cannot be guaranteed because required carrier data is
nonresident, submission defers/backpressures. It cannot create a new support.

Right and left eyes use their actual reprojections. A one-winner depth/handle
projection is insufficient whenever multiple sheets/folds overlap.

An observation may also create an unbound support hypothesis that has no atlas
address. This remains `UnresolvedShadowBranch` evidence until §14 proof gates are
satisfied; it is not an implicit candidate state.

---

# 13. One semantic operation: `NativeCloseCommit`

The complete feasible field set is

\[
\boxed{
\mathcal C[\Psi_t,Y_t]
=
\mathcal C_{prior}
\cap
\bigcap_{q,p}
\left(
\bigcup_{H\in\mathcal H_{q,p}}
\mathfrak A_{q,p,H}
\right)
\cap
\mathcal C_{Merkaba/eigen}
\cap
\mathcal C_{intrinsic}.
}
\]

Observation preimages, support alternatives, native eigenmode/zero-divisor/
nonassociative relations and intrinsic atlas incidence participate in the **same
feasible set before selection**.

The sole canonical semantic operation is

\[
\boxed{
\operatorname{NativeCloseCommit}(\Psi_t,Y_t)
=
\operatorname{RootLastCommit}
\left[
\operatorname{NativeSelect}
(\mathcal C[\Psi_t,Y_t],\Psi_t)
\right].
}
\]

There is no semantic `inverse → stitch → latent solve → commit` sequence. Physical
GPU phases may use profiler labels such as projection, reduction, close, overflow
and commit, but none is an independent physical authority.

## 13.1 Native relation component

`C_Merkaba/eigen` is generated from the authoritative descriptor. It can include
unary, neighbour, k-ary and bounded-neighbourhood constraints. For intrinsic
neighbours one descriptor relation may use

\[
\tau_{ij}=\overline{s_i}s_j,
\]

but no separate stitch subsystem owns topology.

## 13.2 Exact ZD and near-singular classes

An exact zero-divisor witness satisfies

\[
\tau a_k=0
\]

in exact Q16.48/algebra semantics for an authoritative probe `a_k`.

`NEAR_SINGULAR_Q48` is a separate calibrated nonzero-residual class. It cannot be
called exact ZD and cannot acquire exact ZD proof by repetition.

## 13.3 Nonassociative context

For a chain, the descriptor may require both

\[
(\tau_{ij}\tau_{jk})a_r
\qquad\text{and}\qquad
\tau_{ij}(\tau_{jk}a_r).
\]

Their difference can change whether a branch collapses in a query or remains
distinct. Associator magnitude is not a generic image-edge detector.

## 13.4 Native relation taxonomy

Only descriptor-defined subtypes may be emitted. The minimum transport/cache
taxonomy is:

```text
REGULAR
ZD_EXACT
NEAR_SINGULAR
NONASSOC_CONTEXT
FOLD_OR_CREASE          only when descriptor derives it
NULL_CONTACT_RELATION   only when descriptor derives it
NO_RELATION
UNRESOLVED
```

A `NativeRelationCache` may cache outcomes for generation/evidence-keyed intrinsic
neighbourhoods. It is disposable and owns no topology.

---

# 14. Unresolved evidence lifecycle

A shadow not yet explained is not yet a germ.

## 14.1 F0 — `UnresolvedShadowBranch`

```text
branch id
one or more immutable observation references
bounded native relation/preimage representation
support-hypothesis ancestry
continuation cursor for exact bounded contraction
no canonical native identity
no Sigma_2 chart
no physical extent
```

This is pure unresolved evidence.

## 14.2 F1 — `BoundNativeBranch`

Created only after independent observations plus native relation closure prove a
persistent intrinsic support relation.

```text
stable noncanonical intrinsic local chart/gauge
native relation region
complete evidence references
native relation attachment references
generation and proof receipt
```

It still has no supported canonical carrier address.

## 14.3 F2 — supported carrier materialization

Canonical `Σ₂` allocation occurs only after the native law proves:

```text
support/contact manifestation
resolved support hypothesis or common delta
chart attachment/component semantics
complete independent evidence ownership
forward satisfaction of both whole-scene sensor shadows
```

Deterministic ordering may choose only among mathematically gauge-equivalent
placements. Observation revision, provenance, image position, XYZ position, page
or GPU order may never determine physical placement.

## 14.4 Branch transitions

New evidence may:

- refine the same unresolved branch;
- merge hypothesis ancestry without declaring support;
- prove and bind one native branch;
- attach a bound branch to existing atlas incidence;
- materialize a new supported chart component;
- prove the branch inconsistent and release it after evidence obligations end.

There is no first-stage `LatentGerm`, pending pixel chart or novel rectangle.

---

# 15. Multi-pass eigenmode refinement

N1R must import and freeze the authoritative Merkaba/eigenmode operator, frame and
kernel semantics `K_M`. The scanner cannot merely use the word “eigenmode”.

The descriptor exposes:

```text
native eigenmode/eigenspace identification required by TOE
query coupling of each relevant mode
mode/kernel visibility under each whole-scene shadow
native relation transport of modes
current chart/modal bandwidth or capacity semantics
refinement transport rules
```

Repeated views refine one full native field because distinct shadows constrain
different native-mode combinations. A stable duplicate from the same independence
class does not gain strength merely by repetition.

Refinement is demanded only when all are proven:

1. retained observations are jointly real/supportable under one static scene;
2. current chart/modal capacity cannot reproduce them without contradiction or
   destructive broadening;
3. a finer intrinsic chart increases representable native modal capacity;
4. the finer closure forward-verifies every retained whole-scene observation.

Then:

```text
exact intrinsic gauge split/remap
transport complete evidence and native relations
instantiate finer full S16 states
rerun the same NativeCloseCommit feasibility law
```

No 3D voxel resolution, depth-distance heuristic, image tile count or repeated
sample count is canonical refinement authority.

---

# 16. Evidence, uncertainty and certificates

## 16.1 Exact epistemic representation

Canonical epistemic knowledge is represented by:

```text
native relation/preimage regions
support-hypothesis disjunctions
provenance and independence classes
first-hit/order roles
unresolved branch structure
forward-verification receipts
```

No normative scalar precision/confidence formula is part of canonical physics
unless the authoritative TOE law explicitly introduces one.

A scalar confidence may be derived for telemetry or presentation. It cannot drive
state fusion, hypothesis choice, native relation classification, chart allocation,
refinement, export colour/detail or publication.

## 16.2 Complete evidence before visibility

Every visible revision owns complete immutable evidence sufficient to reproduce:

- the support-hypothesis groups;
- the selected/common delta or no-change result;
- exact contradictions/gaps;
- first-hit and exclusion roles;
- unresolved/bound/materialized branch transitions;
- native relation classes required by the revision;
- independence and forward-verification gates.

Evidence is stored once per observation. Page generations hold references and do
not duplicate it.

## 16.3 `NativeClosureCertificate`

After deterministic minimization, retain:

```text
descriptor and query fingerprints
observation/independence/provenance keys
hypothesis-group and resolved/common-delta proof
native relation predicates/bounds
first-hit/order/exclusion role
intrinsic relation subtype/signature
branch binding/materialization receipt
raw reference when irreducible
```

Certificates prove `Ψ`; they do not supply physical appearance or detail absent
from `Ψ`.

## 16.4 Deterministic minimization

1. stable-sort by observation, hypothesis group, role, relation and provenance;
2. preserve the explicit union/intersection structure;
3. coalesce only equivalent same-branch predicates;
4. retain exact conflicts and branch ancestry;
5. remove a record only when complete feasible-field set, selected/common delta,
   first-hit, native relation and independence gates remain bit-identical;
6. repeat reverse-lexicographic redundancy passes to fixed point using a
   generation-owned continuation cursor.

Scratch window size affects time only. Complete evidence remains owned until an
exact certificate/raw persistence handoff makes reclamation safe.

## 16.5 Repeated-pass semantics

- exact duplicates in one independence class do not become stronger by count;
- new baseline/pose/footprint phase may constrain different native modes;
- statistically independent remeasurement narrows only through the frozen sensor
  uncertainty law;
- strong→weak and weak→strong order is invariant;
- contradictory branches remain explicit until native closure resolves them.

---

# 17. Sparse canonical publication

The only canonical mutation record is `NativeStateDeltaGpu`:

```text
complete intrinsic carrier key
prior page/sample generation
full selected S16[16] Q16.48 state
changed mask
closure witness
evidence/certificate receipts
affected intrinsic relation receipts
```

Publication is:

```text
resolved/common NativeStateDelta stream
→ stable reduce by complete intrinsic carrier key
→ final full-field forward verification
→ unique touched logical pages
→ allocate/clone unpublished shadow generations
→ scatter exact full S16 states
→ attach complete evidence receipts
→ validate revision closure
→ mark immutable generations complete
→ one root exchange, last
```

`UNCHANGED` may strengthen a certificate without creating a new page generation.
`UNRESOLVED`, conflict, incomplete support coverage or failed native relation gate
cannot advance root.

Readers select only complete generations at or below the published root. A fault
before the final exchange leaves the previous world entirely visible.

---

# 18. Carrier storage, codec and atlas incidence

`Σ₂` uses signed-64 intrinsic chart/page coordinates. Page and block dimensions are
storage choices only.

Persist per allocated chart/page:

```text
logical atlas address and chart component/incidence metadata
immutable generation/revision
full S16 Q16.48 samples
certificate ranges
codec payload and checksum
```

The deterministic lossless block encodings remain:

```text
CONST / AFFINE / DELTA / RAW
```

The historical `NULL` codec name cannot mean sparse absence. Before reuse, the
descriptor must prove an exact allocated `NATIVE_NULL` state/stratum and the codec
must be named/typed accordingly. Until then an allocated native-null state is
encoded losslessly as ordinary state data.

`ABSENT` is represented only by absence from the allocated atlas/page map. Reading
an absent address yields “no materialized state”, not an S16 vector.

Atlas chart incidence is intrinsic domain metadata. Export connectivity uses
incidence gated by native relation class; it never infers adjacency from page or
XYZ proximity.

---

# 19. Residency, paging and logically unbounded scale

Logical world size is independent of decoded GPU residency.

Residency priorities may include:

```text
current eye-query localities
sensor-shadow contributors and support hypotheses
closure-affected intrinsic neighbourhoods
unresolved/bound branch evidence
dirty unpublished pages
explicit export region
```

Pressure may:

- evict clean disposable readouts;
- encode clean immutable pages;
- durably spill complete evidence;
- pause admission while required supports rehydrate;
- reduce noncanonical query quality where permitted.

Pressure may not lower sensor resolution, erase accepted native detail, truncate a
hypothesis union, equate ABSENT with NATIVE_NULL or mint identity because a support
is nonresident.

Each Vulkan binding obeys runtime limits through segmented pools. Segmentation
cannot repeat a whole logical domain or alter results.

---

# 20. Pose and calibration as query gauge

The immutable Meta rig pose, timestamps, intrinsics, extrinsics and calibration
epoch define query descriptor `Γ_t`.

A same-frame pose correction may be inferred from independently conditioned
whole-scene shadow overlaps. It is an observation/query gauge, not carrier state.

Rules:

- the corrected pose is bounded by the immutable tracking prior;
- correction uses exact native-shadow relations, not a CPU point cloud;
- the same retained frame is reprojected under the accepted correction;
- correction from frame `t` is never blindly transported to `t+1`;
- pose conflict retains the immutable Meta pose or defers the observation;
- no second SLAM, pose graph or geometry world is canonical.

---

# 21. Static reconstruction correction and temporal evolution

## 21.1 Static same-scene correction

If independent same-epoch observations prove that current `Ψ` contains a false
manifestation, valid scene-level exclusion participates in `NativeCloseCommit`.

Examples:

```text
approach artifact
later side view
independent clear pre-hit/pass-through evidence
same static scene remains jointly feasible only after removing false support
```

The resulting contraction may publish a corrected full S16 field. It is ordinary
epistemic reconstruction correction, not temporal object deletion.

## 21.2 Actual temporal evolution

Temporal transition semantics becomes relevant only when admitted observations
cannot be reconciled as one static scene under the frozen pose/calibration/epoch
model.

S4‑09 may then evaluate native identity-preserving transport, disappearance and new
manifestation. A nearer occluder never proves background removal. Behind-hit
remains no evidence.

Time is provenance/causality, not a weighted source or separate geometry history.

---

# 22. Readout family A — direct stereo XR eyes

For eye descriptors:

\[
\boxed{
I_L=\mathscr S_{eye,L}[\Psi],
\qquad
I_R=\mathscr S_{eye,R}[\Psi].
}
\]

The implementation reuses `ProjectNativeShadow` and `ReduceNativeShadow` with eye
query plans. It may directly produce two 2D RGB/depth/order maps.

No persistent XYZ, triangle, splat, meshlet or texture world is required. The eye
maps are lossy and disposable. Their kernel directions remain preserved in `Ψ`.

Eye reduction must preserve binocular disparity, direct order, folds, overlapping
sheets, native-null behaviour and descriptor-relevant ZD/nonassociative context.
Display quantization never feeds canonical closure.

---

# 23. Readout family B — scanner prediction

Sensor prediction is the generated scene-shadow computation for the timestamped
rig query. It emits:

```text
predicted whole-scene RGB-D/order shadow
support-hypothesis groups
candidate native support keys/ranges
local-contribution and reducer witnesses
generation/query/descriptor fingerprints
relation/ZD/bracket signatures required for reverse closure
```

Prediction is disposable acceleration. It has no allocation or identity authority.
It must retain multiple overlapping supports whenever reduction cannot prove a
single one.

Deleting/rebuilding prediction changes no canonical bytes or certificate.

---

# 24. Readout family C — rich textured 3D export

Export is an explicit rich query of the full latest published `Ψ` and
authoritative native law.

## 24.1 Geometry

Use the descriptor-authoritative 3D manifestation. Where the frozen operator
contains homogeneous geometry rows `G`, a supported state may yield

\[
h=Gs,
\qquad
X(s)=\left(\frac{h_1}{h_0},\frac{h_2}{h_0},\frac{h_3}{h_0}\right).
\]

This is one export/readout, never canonical storage.

## 24.2 Connectivity

Candidate connectivity is

```text
intrinsic atlas chart incidence
AND descriptor-defined native relation class
```

Regular intrinsic incidence may connect. A fold can remain intrinsically connected
while nonsmooth. `NO_RELATION` never welds. Exact ZD, near-singular,
nonassociative-context and unresolved subtypes follow descriptor-specific export
rules. XYZ-nearest-neighbour welding is forbidden.

## 24.3 Detail and appearance

Geometry/detail/appearance come from full S16/eigenmode state at requested
refinement. Certificates provide proof/uncertainty metadata only.

Certificates and observation journals may not synthesize physical RGB/detail
missing from `Ψ`. If retained raw optical evidence contains unresolved physical
detail, first refine/close `Ψ`; then export.

On-demand export may generate textures, material parameters, directional residuals
and GLB. These are disposable readouts and never lower the information ceiling of
the canonical field.

---

# 25. Readout family D — debug and analytics

Generated pure queries may expose:

```text
XYZ/depth/RGB/order
native support/chart key
observation and hypothesis groups
unresolved/bound branch state
exact gaps/conflicts
ZD_EXACT vs NEAR_SINGULAR
nonassociative bracket signature
mode visibility/refinement rank
residency/evidence age
```

Debug buffers and timestamps are read-only. Telemetry cannot select work or mutate
`Ψ`.

---

# 26. One semantic closure and low-code GPU lowering

The sole semantic mutation is `NativeCloseCommit` from §13.

Initial physical kernels:

```text
ProjectNativeShadow
    current Psi local contributions for sensor/eye query

ReduceNativeShadow
    whole-scene order/first-hit/occlusion/footprint reduction
    emits sparse support-hypothesis groups

CloseNativeField
    measured two-eye RGB-D shadows
    + support-hypothesis disjunctions
    + authoritative Merkaba/eigenmode relations
    + intrinsic atlas incidence
    → one feasible closure
    → resolved/common sparse NativeStateDelta
    → unresolved branches remain evidence

ResolveClosureOverflow          indirect/cold
    only cross-workgroup, coupled-hypothesis, nonresident or bounded-contractor
    continuation; re-enters the same semantic closure

PrepareChangedPages             indirect, touched pages only
ScatterChangedStates            indirect, resolved/common deltas only
CloseAndPublishRevision         bounded validation, root exchange last
```

Eye readout uses only `ProjectNativeShadow → ReduceNativeShadow` with eye query
descriptors.

Physical phases may fuse after exact parity/profiling. They may not split into
sensor-specific, topology, latent-object or proof-owner lifecycle subsystems.

No persistent transaction, token scheduler, candidate manager, topology solver,
latent solver, page-owned proof or CPU work selection is permitted.

If bounded work cannot finish in one physical window, a generation-owned cursor
continues the same logical closure. Changing window size/order cannot repeat a
whole field domain or alter output.

---

# 27. Runtime ABI

## 27.1 Generated `NativeLawDescriptorGpu`

Read-only descriptor tables for S16 algebra, `K_M`, relation expressions with
arity/brackets, manifestation, local query contributions, scene reducers, reverse
contractors, exact/near ZD, mode transport/refinement and fingerprints.

## 27.2 Immutable `ShadowObservationGpu`

```text
observationRevision
sensorSide
independenceClass
poseCalibrationEpoch
finiteFootprint
depthOrderRegion
opticalRelationRegion
measuredFirstHitRole
rawRef
```

No native/candidate/chart key is legal.

## 27.3 Disposable `ShadowHypothesisGpu`

```text
observationId
hypothesisGroupId
supportKeyOrUnboundBranch
predictedShadowWitness
sceneReducerRole
directOrderSupportInterval
nativeRelationWitnessRange
```

## 27.4 `UnresolvedShadowBranchGpu`

```text
branchId
observationRefRange
native preimage/relation representation
hypothesis ancestry
continuation cursor
no canonical chart or physical extent
```

## 27.5 `BoundNativeBranchGpu`

```text
stable noncanonical intrinsic local chart
native relation region
evidence refs
relation attachment refs
generation/proof receipt
```

## 27.6 `NativeStateDeltaGpu`

```text
complete intrinsic carrier key
prior generation
full S16[16] state
changed mask
closure witness
evidence receipts
```

No ABI field may overload observation identity, hypothesis identity, chart identity,
page identity or physical state.

---

# 28. Capture admission and host responsibilities

Distinguish:

```text
CAPTURED_CANDIDATE
    may be deterministically sampled before canonical admission

CANONICALLY_ADMITTED
    owns both coherent eye shadows until PUBLISHED, NO_CHANGE,
    RETAINED_UNRESOLVED or FAULT
```

An admitted observation cannot be overwritten by a newer latest frame. Sensor
capture itself never waits; bounded admission/backpressure is explicit.

C# owns:

```text
capture pairing/admission and immutable leases
descriptor/calibration/pose epoch selection
GPU buffer/page/evidence residency
command recording, dimensions and fences
revision/readout leases
asynchronous persistence/export orchestration
truthful completion/fault reporting
```

GPU owns local projection, scene reduction, hypothesis enumeration, exact field
closure, native relations, branch resolution, delta creation, scatter and readout.

C# does not inspect pixels, choose support hypotheses, classify native relations,
allocate physical charts from observations, construct meshes or repair topology.

---

# 29. Determinism and decomposition invariance

Persist fingerprints for:

```text
NumericDomain
signed-XOR algebra and conjugation
authoritative K_M and E_M expressions/arity/brackets
manifestation and local query plans
whole-scene reducers
exact/near ZD definitions
fibre-preserving selector/right-lifts/equivalence rules
refinement-capacity law
codec and persistence schema
```

For the same admitted observation sequence, all legal source orders, workgroup
shapes, dispatch/window partitions, segment/page layouts, cache hit/miss patterns
and proof scratch sizes produce byte-identical:

```text
support-hypothesis union structure
unresolved/bound branch identities and ancestry
resolved/common NativeStateDelta stream
Psi pages/generations/root sequence
atlas chart incidence and gauge allocation
native relation subtypes/signatures
evidence/certificates/provenance
readout results at the same exact query contract
```

Physical segmentation cannot change how many times an entire logical scene,
hypothesis group or intrinsic relation domain is evaluated.

Stable ordering is used only for deterministic execution/allocation among proven
gauge-equivalent choices. Observation revision/provenance can order such choices;
it cannot create their physical placement.

---

# 30. Performance and Release telemetry

Cost follows active query support and new information:

```text
projection        active materialized local contributions for the query
scene reduction   overlapping support contributions/hypothesis groups
field closure     affected hypotheses/native neighbourhoods
overflow          only unresolved cross-block/coupled continuations
publication       resolved changed states/touched pages
eye readout       current eye-visible contributions
export            explicit requested region/quality
```

Forbidden scaling:

```text
total persisted world per scan frame
image footprint × backing segments
all image edges as topology
revision count or pending-window count
candidate count followed by mutation of every candidate
page count as relation domain
proof minimization in the foreground visibility gate
```

S4‑08.6 acceptance on the frozen 320×320 Quest fixture:

```text
old sensor-cell/proposal/provider graph              absent
old optical-edge/label/topology graph                absent
old pending/LatentGerm/pixel-chart graph             absent
old duplicated target/page publication graph         absent

NativeCloseCommit measured compute                   <= 1500 ms
admission-to-completion wall                          <= 1800 ms
30-revision steady-state drift                        <= 1%
segment decomposition semantic work-count change     0
```

Profiler phase labels may report projection/reduction/close/overflow/commit, but
they do not define semantic authorities.

One-shot Release Vulkan telemetry reports per-kernel time/count/records plus:

```text
local contributions and scene-reducer overlap
hypothesis groups and alternatives/group
unique/resolved/common/unresolved branch outcomes
exact ZD / near-singular / nonassoc relation classes
unresolved→bound→materialized transitions
changed states/touched pages/root/fault
resident carrier/evidence/readout bytes
oldest admitted age
descriptor/operator operation counts
```

Telemetry never controls execution. Its sampled wall time is diagnostic-contaminated
and labeled accordingly.

---

# 31. Persistence schema

Persist:

```text
world/manifest
    schema = CPQ4-2026-08-25-S16-v8.1
    NumericDomain/algebra fingerprints
    K_M/E_M/manifestation/query/reducer fingerprints
    exact/near ZD and bracket-plan fingerprints
    ABSENT/native-null distinction and native-null fingerprint if defined
    calibration/query epochs
    selected root

world/atlas
    chart domains/components/incidence
    sorted sparse allocated page generations
    exact lossless S16 payloads
    NativeClosureCertificate ranges

world/unresolved
    UnresolvedShadowBranch records and evidence refs
    BoundNativeBranch charts/relations/evidence refs

world/observations
    unresolved raw tiles only where exact replay/refinement requires them

world/derived                 optional/deletable
    sensor/eye/debug/export caches
```

No ABSENT address is serialized as a native S16 state. An allocated NATIVE_NULL is
serialized exactly like any descriptor-defined native state.

Durable publication precedes eviction. Restart plus the same admitted observation
sequence reproduces byte-identical atlas state, branches, certificates and roots.

---

# 32. Repository architecture and hard deletion boundary

Active reconstruction remains under:

```text
Runtime/SigmaPrism
Runtime/Resources/SigmaPrism
```

Preserve where compatible:

```text
capture/sync/calibration/pose infrastructure
SigmaNumericDomain and exact backend gate
generated signed-XOR S16 primitives
SigmaCarrier and lossless codec/storage primitives
GPU completion/fence/indirect helpers
root-last immutable publication primitive
one-shot Release timing infrastructure
XR lifecycle/UI/anchors
GLB encoding plumbing
```

Target small native core:

```text
generated NativeLawDescriptor C#/HLSL tables
SigmaNativeShadow.compute        project/reduce query shadows
SigmaNativeClose.compute         joint field closure + overflow continuation
SigmaNativeCommit.compute        sparse root-last storage lowering
SigmaNativeReadout.shader        direct eye presentation where needed
SigmaNativeGraph.cs              fixed recorder, no semantic lifecycle objects
SigmaNativeResources.cs          observations/hypotheses/branches/deltas/evidence
```

Hard-delete after each cutover:

```text
SigmaFrameInverse.compute
SigmaFrameClosure.compute
SigmaFramePublish.compute
old SigmaFrameGraph/SigmaFrameResources
sensor-specific live inverse cell worlds
proposal-kind/candidate identity machinery
separate topology/stitch controller/math authority
pending/LatentGerm projection/label/link/retention lifecycle
global novel bbox/pixel continuation
global target/page sorting/mapping orchestration superseded by sparse owner close
page halo/live persistent XYZ/mesh authority
```

Final S4‑08.6 production gates:

```text
gross deletion versus cac9ab0       >= 10000 LOC
new production code                 <= 4000 LOC
net versus cac9ab0                  <= -6100 LOC
net versus d3b83e1                  <= -5500 LOC
retired live symbols/assets         0
legacy/fallback paths               0
```

Generated tables and tests are reported separately and cannot conceal
orchestration growth.

---

# 33. Forbidden architecture violations

The implementation is invalid if it introduces or retains:

- a canonical geometry/mesh/voxel/splat/texture/topology/object/history beside
  `Ψ`;
- forced unary `E22(s)` factorization without faithfulness modulo frozen harmless
  equivalence;
- independently editable relation/eigenmode channels;
- local `S_q,p(s)` presented as the complete physical camera shadow;
- first-hit inside one local projector;
- immutable observation packets containing candidate/native identity;
- one-winner support pruning without an exact coverage proof;
- mutation of every alternative support;
- minimum-change selection across unresolved support hypotheses;
- `LatentGerm`, PENDING or NOVEL as first unresolved identity;
- separate semantic inverse and stitch/topology operations;
- generic associator magnitude as image-edge detector;
- nonzero near-singular residual labeled exact ZD;
- ZD used as depth/order;
- image/XYZ/page/segment identity or connectivity;
- `ABSENT == NATIVE_NULL`;
- scalar confidence/precision driving canonical physics;
- depth-conditioned RGB or cross-source pre-contraction;
- vote/count fusion;
- deferred static artifact correction solely because exclusion is “temporal”;
- export appearance/detail reconstructed from certificates instead of `Ψ`;
- fixed journal/session caps, CPU pixel decisions or synchronous readback;
- legacy/fallback graph.

---

# 34. Exact unit and oracle gates

## 34.1 Descriptor gate H0

Before live mutation:

1. authoritative `K_M`, relation family, arities and brackets are present and
   fingerprinted;
2. semantic reference matches generated CPU/HLSL forward evaluation bit-for-bit;
3. common-subexpression fusion on/off is identical;
4. E22 faithfulness modulo frozen harmless equivalence is proven, or direct S16
   dependencies remain visibly active;
5. ABSENT/native-null semantics are explicit and distinct;
6. exact ZD and calibrated nonzero near-singular fixtures never alias;
7. all multi-factor CPU/GPU bracket plans match;
8. whole-scene reduction oracle covers overlap/order/first-hit/occlusion/folds;
9. linear right-lifts prove `R L_R = I` on the observable image and the frozen
   direct-sum decomposition;
10. nonlinear selector preserves prior representatives on indistinguishable
    fibres;
11. refinement-capacity and mode-transport laws are present, not inferred by
    scanner code;
12. no handwritten alternate physics is live.

## 34.2 Scene-shadow gates

- two candidate supports, only A scene-valid → A may resolve;
- two supports remain valid → no canonical mutation;
- two supports imply one identical delta → common delta is legal;
- one measurement cannot mutate A and B independently;
- fold/two sheets at one sensor pixel remain distinct alternatives;
- scene-level first-hit matches exhaustive CPU oracle;
- right-eye-only support and different left/right physical sheets are handled;
- finite-footprint/outward reduction never drops a possible support;
- candidate enumeration/window/segment permutations are identical.

## 34.3 Source and closure gates

- depth and RGB leaves within each eye remain independent;
- left/right source order and work partition are invariant;
- hidden linear modes are byte-preserved through generated right-lift;
- nonlinear indistinguishable fibres preserve prior representative;
- unresolved disjunction retains evidence and emits no delta;
- all surviving branches/common delta are forward-verified against both measured
  whole-scene shadows;
- direct order, exact ZD and nonassoc context remain distinct;
- current atlas incidence and all affected native relations participate before
  state selection;
- static clear-path exclusion can remove a false manifestation;
- behind-hit state remains byte/evidence identical.

## 34.4 Branch/refinement gates

- unexplained shadow first creates `UnresolvedShadowBranch`, not chart/state;
- repeated compatible evidence reuses branch ancestry;
- binding requires independent relation proof;
- materialization requires support/chart-attachment proof;
- resolution/image/page/workgroup changes do not change chart identity;
- stale generation cannot bind/materialize/reclaim a newer branch;
- modal refinement is demanded only by the authoritative capacity law;
- finer closure forward-verifies all retained evidence;
- thin/opposite/fold manifestations remain distinct when required.

## 34.5 Publication/evidence gates

- only resolved/common `NativeStateDelta` reaches scatter;
- multiple records for one key reduce exactly before one writer;
- no-change creates no page generation;
- fault/unresolved/incomplete coverage cannot advance root;
- readers see all-old or all-new across pages/segments;
- observation evidence survives frame-slot reuse;
- minimization window size and record permutation preserve certificate bytes;
- proof minimization changes no physical export geometry/appearance;
- capacity pressure backpressures/fails closed without false commit.

---

# 35. Physical acceptance corpus

## 35.1 Static scan and support ambiguity

```text
flat wall 1/5/20 passes
front + grazing wall
right-eye-only texture/depth support
two valid sheets at one sensor pixel
thin board with different sides
fold/crease and self-overlap
bucket inside/outside
alcove, stairs, multi-floor loop
subpixel relief and repeated footprint phases
approach artifact then independent clear pass-through
```

Expected:

- resolved modes improve or remain stable without vote-count strengthening;
- ambiguous alternatives do not mutate every support;
- common delta across alternatives is allowed only with exact proof;
- hidden modes survive partial shadows;
- no duplicate chart from image/view decomposition;
- valid static exclusion removes a false artifact;
- folds and close parallel sheets retain correct incidence/relation distinctions.

## 35.2 Native-law and refinement

```text
E22 alias-pair search over the canonical fixture domain
exact ZD vs one-LSB near-singular
nonassociative bracket swap negative control
same relation under cache hit/miss
mode visible only from grazing query
current modal capacity insufficient, refined capacity sufficient
```

Expected descriptor/oracle parity and no loss of direct S16 dependencies when E22
faithfulness is unproven.

## 35.3 Eye readout

```text
left/right disparity
head translation/rotation
overlapping sheets and fold crossing
native-null manifestation
sleep/wake/resume
delete/rebuild all eye caches
```

Expected stable world-locked low-latency retinal shadows and no change to `Ψ` or
export after cache deletion.

## 35.4 Export

Export after one pass, multiple passes, modal refinement, opposite-side scan and
restart/rehydrate.

Expected latest geometry, intrinsic incidence gated by native relations,
multi-view physical appearance from `Ψ`, no certificate-derived hidden texture and
no eye-readout information ceiling.

## 35.5 Scale/lifecycle

Long scan, building loop, residency pressure, interrupted persistence, restart,
revisit and deterministic replay. No capacity cliff, revision-dependent latency,
segment seam or admitted-frame loss.

---

# 36. S4‑08.6 deterministic ontology-reset closure sequence

S4‑08 remains open. Prior S4‑01…S4‑07 are retained only as primitive evidence
where they satisfy v8.1.

## N0R — ontology rebase

- replace v8 germ-first spec, architecture audit interpretation and closure plan;
- freeze field-level shadow, hypothesis disjunction, single closure,
  `UnresolvedShadowBranch`, `BoundNativeBranch`, ABSENT/native-null and pure-readout
  contracts;
- runtime unchanged;
- controls/code graph/math/link/diff gates;
- commit separately.

## N1R — authoritative native descriptor

- import/freeze S16 algebra plus actual TOE `K_M`, relation family arity/brackets,
  manifestation, local projection, scene reducers, ZD distinctions, bracket plans,
  fibre-preserving selectors/right-lifts and modal refinement law;
- prove E22 faithfulness or retain direct S16 dependencies;
- emit semantic oracle and CPU/HLSL generated plans;
- no live mutation;
- commit separately.

## N2R — non-mutating scene-shadow oracle

- implement `ProjectNativeShadow`, `ReduceNativeShadow`, immutable observations and
  disposable hypothesis groups;
- pass §34.2 including ambiguous/no-mutation/common-delta cases;
- no live canonical cutover;
- commit separately.

## N3R — joint field closure cutover

- implement `CloseNativeField` with observation hypothesis unions and native
  relation law in one feasible solve;
- hard-delete proposals, sensor cell worlds, provider×segment evaluation and old
  target sort/reduce in the same commit;
- no separate stitch semantic pass or fallback;
- production LOC becomes negative;
- commit separately.

## N4R — native relation/overflow cutover

- fold same-locality ZD/nonassoc/intrinsic constraints into `CloseNativeField`;
- use one sparse `ResolveClosureOverflow` only for physical partition/cross-block
  continuation of the same closure;
- delete optical edge universe, label propagation, XYZ qualifier and separate
  topology authority;
- commit separately.

## N5R — unresolved branch cutover

- replace pending/novel/LatentGerm identity with `UnresolvedShadowBranch` and
  proof-gated `BoundNativeBranch`;
- delete pending projection winner, pixel chart, global bbox and pending SoA
  lifecycle;
- pass branch reuse/binding/materialization/modal-refinement gates;
- commit separately.

## N6R — sparse root-last commit

- consume only resolved/common `NativeStateDelta`;
- close generation-safe complete evidence/certificate ownership;
- delete duplicate page mapping/sorting/publication graph and old frame graph;
- preserve one root-last immutable publication primitive;
- commit separately.

## N7R — pure readouts

- direct whole-field eye shadows;
- prediction emits scene-level support-hypothesis output;
- export reads full latest `Ψ` plus atlas/native law, certificates as proof only;
- remove page halo/live XYZ/mesh authority after parity;
- commit separately.

## N8R — hard deletion and physical closure

- pass LOC/retired-symbol/code-graph/full exact/Vulkan gates;
- archive the exact source commit;
- build/install the Release APK from that same commit;
- capture truthful per-kernel evidence;
- pass §30 and §35;
- only then mark S4‑08 done and stop before S4‑09.

At most one run is active. A failed gate is corrected inside the same native law or
its exact sparse lowering; it cannot introduce a new subsystem.

---

# 37. Subsequent implementation sequence

After accepted S4‑08:

```text
S4-09   temporal evolution only for observations not reconcilable as one static scene
S4-10   durable unbounded paging/eviction/rehydration/restart
S4-11   direct eye quality/culling completion if needed after N7R
S4-12   rich textured 3D/PBR/GLB export from full latest Psi
S4-13   complete physical Quest correctness/scale/quality corpus
```

Later nodes may extend query lowerings and persistence. They cannot change the
frozen native law without a schema/fingerprint migration and complete replay gate.

---

# 38. Definition of done

Complete means physically demonstrated on Quest 3:

```text
Psi : Sigma_2 -> S16 is the only canonical physical world
Sigma_2 is an intrinsic atlas with canonical chart incidence
ABSENT and NATIVE_NULL remain distinct everywhere
authoritative K_M/eigenmode semantics are present and fingerprinted
relation arities/brackets match the TOE artifact
E22 faithfulness is proven or direct S16 dependencies remain
local contribution and whole-scene shadow are distinct
first-hit/order/occlusion are scene-reducer results
rig input is two coherent RGB-D shadows with independent depth/RGB leaves
support alternatives remain an explicit disjunction
ambiguous alternatives cannot all mutate
minimum-change never chooses physical support identity
hidden S16 modes are preserved by right-lift/fibre construction
exact ZD differs from near-singular and direct order
nonassociative brackets are deterministic and never reassociated
one NativeCloseCommit owns observation/native/intrinsic feasibility
unexplained evidence has no germ/chart/extent
binding/materialization require exact proof
multi-pass refinement follows authoritative modal-capacity semantics
static exclusion can correct false same-scene manifestations
only resolved/common full-S16 deltas mutate immutable root
evidence/certificates prove but never supply missing physical appearance
eye/prediction/export/debug are pure whole-field readouts
pages/segments/residency never define physics
restart/replay is byte-identical
no retired v7/v8 germ-first graph or fallback remains
production LOC and measured performance pass §30/§32
same-commit physical corpus passes
```

A compile, synthetic fixture, visible point cloud or advancing root alone is not
acceptance.

---

# 39. Architectural summary

The canonical reality is

\[
\boxed{\Psi:\Sigma_2\rightarrow S16}
\]

plus one authoritative Merkaba/eigenmode native law.

The physical readout direction is

\[
\Psi
\xrightarrow{\mathcal K_M,\mathcal E_M,\mathcal M}
\{\phi_{q,\xi}[\Psi]\}
\xrightarrow{\mathfrak R_q}
\mathscr S_q[\Psi].
\]

The scanner receives two coherent measured RGB-D shadows and closes the set of
possible native fields:

\[
\boxed{
\mathcal C[\Psi_t,Y_t]
=
\mathcal C_{prior}
\cap
\bigcap_{q,p}
\left(
\bigcup_H\mathfrak A_{q,p,H}
\right)
\cap
\mathcal C_{Merkaba/eigen}
\cap
\mathcal C_{intrinsic}.
}
\]

Only a resolved or branch-common full-S16 delta may publish:

\[
\boxed{
\Psi_{t+1}
=
\operatorname{NativeCloseCommit}(\Psi_t,Y_t).
}
\]

Ambiguity remains evidence. It does not become a germ, chart, object or mutation.

Eyes consume a cheap pair of retinal shadows. Export consumes a rich textured 3D
shadow. Debug consumes arbitrary shadows. None can simplify or mutate the native
world.

The core rule is:

\[
\boxed{
\textbf{Do not optimize the old scanner ontology by renaming its objects.}
}
\]

The implementation is a sparse exact lowering of the full-field native equation,
not a pipeline of native-sounding managers.
