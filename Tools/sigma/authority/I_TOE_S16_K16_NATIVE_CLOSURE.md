# I_TOE — S16 / K16 native closure algebra capsule

Status: authoritative scanner-facing algebra capsule for Sigma-PRISM N1R.

```text
Upstream canonical source: PROJECTION_ALGEBRA_TOE_CANONICAL.md
Upstream SHA-256: 9d2e3604846305cfe5244a4ef49f169632c60582cf895256fadc36426dc5786f
```

This capsule intentionally excludes cosmology, particle masses, Higgs, CKM/PMNS,
QCD spectroscopy, SI/metrology, continuum GR/QFT derivations and every other
non-scanner sector. It is the only TOE material N1R ingests.

It does not define Quest sensor transfer, first-hit optics, exposure law, XR eye
readout or GLB export. Those belong to `I_Q`. Exact Q16.48 storage, conjugation
and base S16 arithmetic belong to `A_S16` unless explicitly restated below for
TOE provenance.

## 1. Native K16 carrier grammar

The local algebraic carrier is `K16 ~= S16`, with basis addresses

\[
a,b,c\in\mathbb Z_2^4.
\]

Canonical Cayley-Dickson basis multiplication is

\[
\boxed{e_a e_b=\varepsilon(a,b)e_{a\oplus b}}.
\]

`a xor b` is the address geometry. `epsilon(a,b)` is the sign geometry. They are
not interchangeable. Address XOR is associative; the sign cocycle carries the
ordering, noncommutative and nonassociative information.

The K16 local closure grammar is therefore

\[
\boxed{\mathcal K_{16}=(\mathcal A_4,\mathcal E_4)},
\qquad
\mathcal A_4=(\mathbb Z_2^4,\oplus),
\qquad
\mathcal E_4=\{\varepsilon_{ab}\}.
\]

No XYZ, pixel, page, mesh or scanner object is part of this law.

## 2. Exact associator / bracket information

For basis elements, the associator coefficient is

\[
\Omega(a,b,c)
=\varepsilon(a,b)\varepsilon(a\oplus b,c)
-\varepsilon(b,c)\varepsilon(a,b\oplus c),
\]

and

\[
\boxed{
[e_a,e_b,e_c]
=\Omega(a,b,c)e_{a\oplus b\oplus c}
}.
\]

Equivalently,

\[
\mathfrak A(a,b,c)=(e_ae_b)e_c-e_a(e_be_c).
\]

A nonzero associator means the two bracket histories are not equivalent. Product
brackets are native information and remain explicit in every compiled relation
expression. Reassociation is illegal unless exact equality is proved for the
concrete operands and context.

## 3. Diffraction / closure-defect operator

Let left multiplication be

\[
L_a e_b=e_ae_b.
\]

The elementary difference between direct signed-XOR transport and composed
left-regular transport is

\[
\boxed{
D_{ab}=\varepsilon_{ab}L_{a\oplus b}-L_aL_b
}.
\]

The canonical diffraction operator is

\[
\boxed{
A_k=\sum_{a<b}D_{ab}
}.
\]

For the symmetric local state `psi_0`,

\[
\sigma_k=A_k\psi_0^{(k)}.
\]

The operator is skew:

\[
\boxed{A_k^T=-A_k}.
\]

The diffraction operator and the K16 shell/closure operator below are distinct
canonical objects. They may share Cayley-Dickson/signed-XOR provenance but are
never identified entry-wise without an explicit theorem.

## 4. Kernel and zero-divisor are different native relations

Diffraction kernel:

\[
\ker A=\{\psi:A\psi=0\}.
\]

Zero-divisor relation:

\[
\boxed{
a\ne0,\qquad b\ne0,\qquad ab=0
}.
\]

A zero divisor is a non-invertible composition channel. It is not synonymous
with diffraction kernel, empty support, no-manifestation, absence or time.

The scanner may not infer `ZEmpty` merely from `A s = 0` or from one zero-divisor
relation. `ZEmpty` requires the separate complete-program proof in the canonical
scanner specification.

Exact zero and calibrated nonzero/near-singular residual classes remain separate.

## 5. Canonical K16 shell / closure core

The unscaled shell operator is recursively defined by

\[
\mathscr A_{k+1}
=
\begin{pmatrix}
\mathscr A_k & 2^{k/2}I_{2^k}\\
-2^{k/2}I_{2^k} & -\mathscr A_k
\end{pmatrix},
\qquad
\mathscr A_1^2=-I_2.
\]

Hence

\[
\boxed{
\mathscr A_k^2=-(2^k-1)I_{2^k}
}.
\]

At K16 (`k=4`):

\[
\mathscr A_4
=
\begin{pmatrix}
\mathscr A_3 & \sqrt8\,I_8\\
-\sqrt8\,I_8 & -\mathscr A_3
\end{pmatrix},
\qquad
\mathscr A_3^2=-7I_8,
\]

so

\[
\boxed{\mathscr A_4^2=-15I_{16}}.
\]

After complexification,

\[
\boxed{H_{16}=i\mathscr A_4},
\qquad H_{16}=H_{16}^\dagger,
\]

with

\[
\boxed{
\operatorname{spec}(H_{16})=\{-\sqrt{15},+\sqrt{15}\}
}
\]

and multiplicity eight in each sign sector.

The exact shell projectors are

\[
\boxed{
P_{\rm sh}^{\pm}
=\frac12\left(I\pm\frac{H_{16}}{\sqrt{15}}\right)
}.
\]

This shell operator is a closure/gap carrier, not a substitute for the diffraction
operator and not a scanner confidence scalar. Algebraic irrational coefficients
remain symbolic source expressions until an outward-bounded query lowering is
required; a rounded Q16.48 literal is not an exact replacement.

## 6. K16 closure eigenmode / Merkaba shadow

The local K16 closure mode is the full local state

\[
\boxed{\mathcal C_{16}u_0=\lambda_0u_0}.
\]

"Merkaba eigenmode" names this K16 closure eigenmode. The observer/readout shadow
is a projection of the full mode, not the mode itself.

Use the four-bit address character

\[
s(b)=\big((-1)^{b_1},(-1)^{b_2},(-1)^{b_3},(-1)^{b_4}\big),
\qquad b\in\mathbb Z_2^4,
\]

with normalized symmetric tangent

\[
t_{\rm OR}=\frac12(1,1,1,1),
\qquad
P_t=I_4-t_{\rm OR}t_{\rm OR}^T,
\]

and shadow direction

\[
p(b)=P_t s(b).
\]

Character orthogonality gives the exact frame operator

\[
\boxed{
F_{\mathfrak M}=\sum_b p(b)p(b)^T=16P_t
},
\]

hence

\[
\boxed{
\operatorname{spec}F_{\mathfrak M}=\{16,16,16,0\},
\qquad
\ker F_{\mathfrak M}=\operatorname{span}(t_{\rm OR})
}.
\]

The nonzero spatial shadow decomposes as the dual-tetra/octa family
`T8 = T+ union T- plus O6`; this is a readout/frame structure, not a physical
polyhedron stored beside S16.

## 7. Shadow kernel must not be frozen away

Let the full K16 state space decompose under the Merkaba shadow map as

\[
V_{16}=V_v\oplus V_k,
\qquad
V_k=\ker\mathscr S_{\mathfrak M}.
\]

The closure operator in this decomposition is

\[
\mathcal C=
\begin{pmatrix}
C_{vv}&C_{vk}\\
C_{kv}&C_{kk}
\end{pmatrix}.
\]

The invisible sector may be omitted only after exact decoupling proof

\[
\boxed{C_{vk}=C_{kv}=0}.
\]

Otherwise the visible effective closure is

\[
\boxed{
C_v^{\rm eff}(E)
=C_{vv}+C_{vk}(E-C_{kk})^{-1}C_{kv}
}.
\]

Therefore

\[
\boxed{\text{shadow-invisible}\ne\text{dynamically absent}}.
\]

This is the authoritative reason N1R preserves complete-program coupling and may
not freeze a readout-transparent S16 direction merely because one query does not
currently see it.

## 8. Native sign transport / relation context

The sign cocycle itself defines the discrete native transport. For bit generator
`a` at K16 address `b`:

\[
\boxed{U_a(b)=\varepsilon_{a,b}}.
\]

For the elementary plaquette generated by `a,c`:

\[
W_{ac}(b)
=U_a(b)
 U_c(b\oplus a)
 U_a(b\oplus c)^{-1}
 U_c(b)^{-1},
\qquad
W_{ac}(b)\in\{+1,-1\}.
\]

The substrate information metric is the Hessian diffraction quadratic form

\[
\boxed{G=2A^TA=-2A^2},
\]

where `A` is the diffraction operator from Section 3, not the shell operator
`\mathscr A` from Section 5. The second equality follows from `A^T=-A`.

For neighbouring full closure modes `u_i,u_j`, the exact link defect is

\[
\boxed{d_{ij}=u_j-U_{ij}u_i}.
\]

Let `d_{ij}^{prim}` be the canonical primitive representative supplied by the
native closure construction. Its primitive-normalized factor is

\[
\boxed{
\widehat d_{ij}
=\frac{d_{ij}}{\lVert d_{ij}^{prim}\rVert_G}
}.
\]

The same primitive normalization is applied to the explicitly bracketed
associator defect `\mathfrak A_{ijk}`:

\[
\boxed{
\widehat{\mathfrak A}_{ijk}
=\frac{\mathfrak A_{ijk}}
       {\lVert\mathfrak A_{ijk}^{prim}\rVert_G}
}.
\]

For a plaquette, the exact normalized holonomy defect is

\[
\boxed{
\widehat F_\square=\frac{W_\square-I}{2}
}.
\]

The complete native closure defect is the direct sum

\[
\boxed{
\mathfrak D_{cl}
=
\bigoplus_{\langle ij\rangle}\widehat d_{ij}
\oplus
\bigoplus_{\langle ijk\rangle}\widehat{\mathfrak A}_{ijk}
\oplus
\bigoplus_{\square}\widehat F_\square
},
\]

with closure functional

\[
\boxed{S_C=\lVert\mathfrak D_{cl}\rVert^2}.
\]

This functional has no independent continuous closure weights. In particular,
`epsilon_cl` is not a native scanner admissibility parameter and no fitted Q48
tolerance may replace the exact factors above.

For Sigma-PRISM these expressions are relation-program input, not permission to
create a persistent topology graph, chart database or seam object. The generated
program evaluates the required context directly over full S16 values and retains
every supplied product bracket and holonomy context.

The finite exact lowering evaluates the same expression tree with checked Q16.48
point arithmetic and outward interval arithmetic. It retains the normalized defect
interval as a feasible-set factor; it does not collapse it to confidence or a
tolerance test. For an exact-zero closure branch:

- an interval excluding zero proves incompatibility;
- singleton zero proves exact closure;
- a non-singleton interval containing zero remains unresolved and is retained.

If the primitive `G`-norm is zero, normalization is not divided through or silently
discarded. The diffraction-kernel factor remains explicit and unresolved unless a
separate exact relation proves its disposition. No XYZ/contact criterion may be
invented at this boundary.

## 9. Authority boundary for N1R

This capsule authorizes only the native algebra above.

From `A_S16`, not from this capsule:

- exact Q16.48 coefficient storage and checked arithmetic;
- generated Cayley-Dickson sign table;
- conjugation implementation;
- dense/sparse multiplication parity;
- interval rounding mechanics.

From `I_Q`, not from this capsule:

- Quest camera intrinsics/extrinsics;
- RGB/depth sensor transfer and finite footprint;
- first-hit / occlusion observation schema;
- illumination/exposure nuisance law;
- XR eye query;
- export/debug query boundary;
- representation `chi/kappa` storage mechanics.

Forbidden imports from the full TOE monograph:

N1R must not import particle spectra, masses, couplings, CKM/PMNS, Higgs,
cosmology, Yang-Mills phenomenology, SI anchors, K64+ particle corrections or any
other sector merely because it occurs in the upstream monograph.

If N1R still requires a native algebraic relation not derivable from Sections 1–8
above, stop and report that exact missing relation. Do not reopen the whole TOE as
an unconstrained source of scanner rules.

## 10. Provenance map into upstream canonical source

This capsule was reduced from these canonical sections only:

- K16 address/sign geometry and closure structure (`B.1`);
- sign-cocycle connection, associator curvature and modal stitching
  (`B.12–B.16`);
- signed-XOR associator and diffraction operator (`C.1–C.2`);
- K16 shell operator / GAP-CORE-16 (`Definition 3.2`, `Lemma 3.3`,
  `Theorem 3.4`);
- Merkaba eigen/kernel/shadow theorem (`B.6a`).

No other upstream section is normative for this capsule.
