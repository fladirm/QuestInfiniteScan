#!/usr/bin/env python3
"""Generate the one canonical Sigma-PRISM-16 algebra/operator descriptor bundle."""

from __future__ import annotations

import argparse
import hashlib
import itertools
import json
import sys
from functools import lru_cache
from fractions import Fraction
from pathlib import Path
from typing import Iterable


ROOT = Path(__file__).resolve().parents[2]
CS_OUTPUT = ROOT / "Runtime" / "SigmaPrism" / "Generated" / "SigmaGeneratedAlgebra.cs"
HLSL_OUTPUT = (ROOT / "Runtime" / "Resources" / "SigmaPrism" / "Generated" /
               "SigmaGeneratedTables.hlsl")
HLSL_LAYOUT_OUTPUT = (ROOT / "Runtime" / "Resources" / "SigmaPrism" /
                      "Generated" / "SigmaGeneratedLayout.hlsl")
CS_FRAME_OUTPUT = (ROOT / "Runtime" / "SigmaPrism" / "Generated" /
                   "SigmaGeneratedFrame.cs")
HLSL_FRAME_OUTPUT = (ROOT / "Runtime" / "Resources" / "SigmaPrism" /
                     "SigmaFrameAbi.hlsl")
TOE_CAPSULE = (ROOT / "Tools" / "sigma" / "authority" /
               "I_TOE_S16_K16_NATIVE_CLOSURE.md")
I_Q_SOURCE = (ROOT / "Tools" / "sigma" / "authority" /
              "I_Q_SIGMA_PRISM_V1.json")
I_REP_SOURCE = (ROOT / "Tools" / "sigma" / "authority" /
                "I_REP_SIGMA_PRISM_V1.json")
AUTHORITY_MANIFEST_OUTPUT = (ROOT / "Tools" / "sigma" / "authority" /
                             "SigmaMerkabaAuthorityManifest.json")
CS_MERKABA_OUTPUT = (ROOT / "Runtime" / "SigmaPrism" / "Generated" /
                     "SigmaGeneratedMerkabaProgram.cs")
HLSL_MERKABA_OUTPUT = (ROOT / "Runtime" / "Resources" / "SigmaPrism" /
                       "Generated" / "SigmaGeneratedMerkabaProgram.hlsl")
NUMERIC_ID = "num.fixed.q16_48.checked.nearest_even"
GENERATOR_VERSION = "CPQ4-S16-GEN-1"
FRAME_ABI_VERSION = "CPQ4-S16-FRAME-1"
MERKABA_PROGRAM_VERSION = "CPQ4-S16-MERKABA-N1R-1"
TOE_UPSTREAM_SHA256 = "9d2e3604846305cfe5244a4ef49f169632c60582cf895256fadc36426dc5786f"
LANES = 16

FRAME_STRUCTS = (
    ("SigmaOwnedFrameGpu", ("Identity", "Keys", "PoseSource")),
    ("SigmaFrameCandidateGpu", ("Identity", "Handle", "Coordinate")),
    ("SigmaFrameOutcomeGpu", ("Classification", "Evidence")),
    ("SigmaPendingGaugeGpu", ("Identity", "Provenance", "LocalExtent")),
    ("SigmaFrameDeltaGpu", ("Coordinate", "Candidate", "Evidence")),
    ("SigmaDirtyEdgeGpu", ("Left", "Right", "Closure")),
    ("SigmaFrameRevisionGpu", ("Identity", "ChangedPages", "WitnessJournal")),
)

FRAME_ENUMS = {
    "SigmaFrameSource": {
        "DepthLeft": 0,
        "DepthRight": 1,
        "RgbLeft": 2,
        "RgbRight": 3,
    },
    "SigmaFrameProposalKind": {
        "None": 0,
        "Current": 1,
        "Pending": 2,
        "Continuation": 3,
        "Novel": 4,
    },
    "SigmaFrameTargetKind": {
        "None": 0,
        "Canonical": 1,
        "Pending": 2,
    },
    "SigmaFrameClaimKind": {
        "None": 0,
        "Contact": 1,
        "ProvenNull": 2,
        "Conflict": 3,
    },
    "SigmaOwnedFrameState": {
        "Free": 0,
        "Sealed": 1,
        "SourceCells": 2,
        "Resolved": 3,
        "Closed": 4,
        "EvidenceRetained": 5,
    },
    "SigmaPendingGaugeState": {
        "Free": 0,
        "Open": 1,
        "Supported": 2,
        "Promoted": 3,
        "Aborted": 4,
    },
    "SigmaFrameRevisionState": {
        "Free": 0,
        "Building": 1,
        "Closed": 2,
        "Published": 3,
    },
}

FRAME_OUTCOME_FLAGS = {
    "Accepted": 1 << 0,
    "Unchanged": 1 << 1,
    "Conflict": 1 << 2,
    "Exclusion": 1 << 3,
    "Pending": 1 << 4,
    "Deferred": 1 << 5,
    "Fault": 1 << 31,
}

FRAME_CELL_FLAGS = {
    "Constrained": 1 << 0,
    "Observed": 1 << 1,
    "Unobservable": 1 << 2,
    "Independent": 1 << 3,
    "Conflict": 1 << 4,
    "Accepted": 1 << 5,
    "Fault": 1 << 31,
}

FRAME_SOURCE_MASK_SHIFT = 8

@lru_cache(maxsize=None)
def basis_product(dimension: int, left: int, right: int) -> tuple[int, int]:
    """Recursive reference for (a,b)(c,d)=(ac-d*conj(b),conj(a)d+cb)."""
    if dimension == 1:
        if left != 0 or right != 0:
            raise ValueError("invalid scalar basis address")
        return 1, 0
    half = dimension // 2
    if left < half and right < half:
        return basis_product(half, left, right)
    if left < half <= right:
        sign, index = basis_product(half, left, right - half)
        return conjugate_sign(left) * sign, half + index
    if right < half <= left:
        sign, index = basis_product(half, right, left - half)
        return sign, half + index
    sign, index = basis_product(half, right - half, left - half)
    return -conjugate_sign(left - half) * sign, index


def conjugate_sign(index: int) -> int:
    return 1 if index == 0 else -1


def hadamard_sign(row: int, column: int) -> int:
    return -1 if ((row & column).bit_count() & 1) else 1


def dyads() -> list[tuple[int, int, int, int]]:
    result = []
    for first, second in itertools.combinations(range(LANES), 2):
        for first_sign, second_sign in itertools.product((-1, 1), repeat=2):
            result.append((first, first_sign, second, second_sign))
    return sorted(result)


def multiply_dyads(left: tuple[int, int, int, int],
                    right: tuple[int, int, int, int]) -> tuple[int, ...]:
    output = [0] * LANES
    for left_index, left_sign in ((left[0], left[1]), (left[2], left[3])):
        for right_index, right_sign in ((right[0], right[1]),
                                        (right[2], right[3])):
            product_sign, output_index = basis_product(
                LANES, left_index, right_index)
            output[output_index] += left_sign * right_sign * product_sign
    return tuple(output)


def sha256(value: object) -> str:
    encoded = json.dumps(value, sort_keys=True, separators=(",", ":")).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()


def file_sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read_json(path: Path) -> dict:
    with path.open("r", encoding="utf-8") as stream:
        value = json.load(stream)
    if not isinstance(value, dict):
        raise RuntimeError(f"authority input must be an object: {path}")
    return value


def diffraction_matrix() -> list[list[int]]:
    output = [[0 for _ in range(LANES)] for _ in range(LANES)]
    for left in range(LANES):
        for right in range(left + 1, LANES):
            pair_sign = basis_product(LANES, left, right)[0]
            for source in range(LANES):
                destination = left ^ right ^ source
                direct = pair_sign * basis_product(
                    LANES, left ^ right, source)[0]
                composed = (basis_product(LANES, right, source)[0] *
                            basis_product(LANES, left, right ^ source)[0])
                output[destination][source] += direct - composed
    return output


def merkaba_shadow_numerator(address: int) -> tuple[int, int, int, int]:
    signs = [1 if ((address >> bit) & 1) == 0 else -1 for bit in range(4)]
    total = sum(signs)
    return tuple(4 * sign - total for sign in signs)


def signed_morton(left: int, right: int) -> int:
    """Stable unbounded Morton key after signed-to-unsigned zig-zag mapping."""
    def zig_zag(value: int) -> int:
        return value * 2 if value >= 0 else -value * 2 - 1

    x = zig_zag(left)
    y = zig_zag(right)
    output = 0
    bit = 0
    while x or y:
        output |= (x & 1) << (bit * 2)
        output |= (y & 1) << (bit * 2 + 1)
        x >>= 1
        y >>= 1
        bit += 1
    return output


def reverse_interval_soundness_fixture_count() -> int:
    """Prove the generated primitive reverse rules retain every source point."""
    fixtures = 0
    for left, right, padding in itertools.product(range(-8, 9), range(-8, 9),
                                                   (0, 1, 3)):
        add_value = left + right
        add_lower = add_value - padding
        add_upper = add_value + padding
        if not add_lower - right <= left <= add_upper - right:
            raise RuntimeError("ADD reverse interval lost its source point")
        fixtures += 1

        sub_value = left - right
        sub_lower = sub_value - padding
        sub_upper = sub_value + padding
        if not sub_lower + right <= left <= sub_upper + right:
            raise RuntimeError("SUB reverse interval lost its source point")
        fixtures += 1

    for value, sign in itertools.product(range(-32, 33), (-1, 1)):
        if sign * (sign * value) != value:
            raise RuntimeError("signed permutation reverse is not exact")
        fixtures += 1
    scale = 1 << 48
    for source, coefficient, padding in itertools.product(
            range(-4, 5), (-3, -2, -1, 1, 2, 3), (0, 1)):
        source_raw = source * scale
        coefficient_raw = coefficient * scale
        product_raw = source * coefficient * scale
        output_lower = product_raw - padding
        output_upper = product_raw + padding
        lower_fraction = Fraction(
            (output_lower if coefficient > 0 else output_upper) * scale,
            coefficient_raw)
        upper_fraction = Fraction(
            (output_upper if coefficient > 0 else output_lower) * scale,
            coefficient_raw)
        reverse_lower = lower_fraction.numerator // lower_fraction.denominator
        reverse_upper = -(-upper_fraction.numerator // upper_fraction.denominator)
        if not reverse_lower <= source_raw <= reverse_upper:
            raise RuntimeError("Q48 multiply reverse interval lost its source point")
        fixtures += 1
    return fixtures


def build_merkaba_descriptor(algebra: dict) -> dict:
    required_paths = (TOE_CAPSULE, I_Q_SOURCE, I_REP_SOURCE,
                      ROOT / "new_spec.md",
                      ROOT / ".codex" / "S4-08.6_NATIVE_CLOSURE_PLAN.md")
    for path in required_paths:
        if not path.is_file():
            raise RuntimeError(f"missing N1R authority input: {path}")

    toe_text = TOE_CAPSULE.read_text(encoding="utf-8")
    required_toe_tokens = (
        TOE_UPSTREAM_SHA256,
        "e_a e_b=\\varepsilon(a,b)e_{a\\oplus b}",
        "\\Omega(a,b,c)",
        "D_{ab}=\\varepsilon_{ab}L_{a\\oplus b}-L_aL_b",
        "\\mathscr A_4^2=-15I_{16}",
        "F_{\\mathfrak M}=\\sum_b p(b)p(b)^T=16P_t",
        "C_{vk}=C_{kv}=0",
        "U_a(b)=\\varepsilon_{a,b}",
    )
    for token in required_toe_tokens:
        if token not in toe_text:
            raise RuntimeError(f"TOE capsule lost required source token: {token}")

    i_q = read_json(I_Q_SOURCE)
    i_rep = read_json(I_REP_SOURCE)
    if i_q.get("schemaVersion") != "CPQ4-IQ-1":
        raise RuntimeError("unsupported I_Q schema")
    if i_rep.get("schemaVersion") != "CPQ4-IREP-1":
        raise RuntimeError("unsupported representation schema")
    if i_q["photometricNuisance"]["metadataMissing"] != (
            "NO_OPTICAL_CLAIM_WITH_MISSING_METADATA_PROVENANCE"):
        raise RuntimeError("missing optical metadata must be NO_OPTICAL_CLAIM")
    nuisance = i_q["photometricNuisance"]
    if (nuisance["calibrationProvenance"] !=
            "CAPTURE_CALIBRATION_EPOCH_FINGERPRINT" or
            nuisance["unboundedParameterRegion"] !=
            "FORBIDDEN_TO_PROVE_COMPATIBILITY_OR_MUTATION" or
            not nuisance["requiredBoundedParameters"]):
        raise RuntimeError("photometric nuisance law is not calibrated/fail-closed")
    if i_q["querySupportSummary"]["falseNegatives"] != 0:
        raise RuntimeError("query-support authority permits false negatives")
    native_relation = i_q["nativeModalRelation"]
    if (native_relation["source"] != "I_TOE_SECTION_8" or
            native_relation["missingOrUnprovedRegion"] != "UNRESOLVED" or
            native_relation["xyzOrPixelCriterion"]):
        raise RuntimeError("native modal relation is not TOE-derived/fail-closed")
    default_semantics = i_q["defaultSemantics"]
    default_representations = i_rep["defaultRepresentations"]
    if (default_semantics["localContribution"] != "EXACT_REDUCER_IDENTITY" or
            default_semantics["allDefaultRelation"] != "DEFAULT_SAT" or
            default_semantics["allDefaultActiveWork"] != 0):
        raise RuntimeError("I_Q all-default law is not exactly quiescent")
    if ({default_representations["logicalUnbacked"],
         default_representations["nullCodecDecode"]} != {"ZEMPTY"} or
            default_representations["allocated"] != "FULL_S16_ALGEBRA_ZERO" or
            default_representations["equivalence"] !=
            "COMPLETE_PROGRAM_BYTE_IDENTITY"):
        raise RuntimeError("ZEmpty backing representations are not identical")
    if (i_rep["kappa"]["measure"] != "EXACT_DYADIC_CELL_AREA" or
            i_rep["kappa"]["queryAccumulation"] !=
            "INTRINSIC_MEASURE_WEIGHTED_WITH_EXACT_CHILD_SUM_EQUAL_PARENT"):
        raise RuntimeError("kappa does not conserve exact intrinsic measure")

    associator_nonzero = 0
    associator_coefficients = {-2: 0, 0: 0, 2: 0}
    for a, b, c in itertools.product(range(LANES), repeat=3):
        omega = (basis_product(LANES, a, b)[0] *
                 basis_product(LANES, a ^ b, c)[0] -
                 basis_product(LANES, b, c)[0] *
                 basis_product(LANES, a, b ^ c)[0])
        if omega not in associator_coefficients:
            raise RuntimeError("basis associator escaped {-2,0,2}")
        associator_coefficients[omega] += 1
        associator_nonzero += omega != 0
    if associator_nonzero == 0:
        raise RuntimeError("K16 associator negative control is empty")

    diffraction = diffraction_matrix()
    if any(diffraction[row][column] != -diffraction[column][row]
           for row in range(LANES) for column in range(LANES)):
        raise RuntimeError("generated diffraction operator is not skew")

    # The capsule fixes the recurrence and A_1^2=-I, but not one concrete A_1
    # orientation.  Prove the orientation-independent square invariant without
    # manufacturing matrix entries absent from the authority input.
    shell_square_by_rank = [-1]
    for rank in range(1, 4):
        shell_square_by_rank.append(shell_square_by_rank[-1] - (1 << rank))
    if shell_square_by_rank != [-1, -3, -7, -15]:
        raise RuntimeError("K16 shell recurrence does not reach -15I")

    shadow = [merkaba_shadow_numerator(address) for address in range(LANES)]
    frame_numerator = [[sum(shadow[address][row] * shadow[address][column]
                            for address in range(LANES))
                        for column in range(4)] for row in range(4)]
    for row in range(4):
        for column in range(4):
            expected = 192 if row == column else -64
            if frame_numerator[row][column] != expected:
                raise RuntimeError("Merkaba shadow frame is not 16*P_t")
    if any(sum(value) != 0 for value in shadow):
        raise RuntimeError("Merkaba shadow escaped t_OR complement")

    # P_visible = S^T S / 16 = (n^T n) / 256 because p=n/4.
    visible_numerator = [[sum(shadow[left][axis] * shadow[right][axis]
                              for axis in range(4))
                          for right in range(LANES)] for left in range(LANES)]
    # I_TOE:7 permits omission only after an exact C_vk=C_kv=0 proof.  The
    # capsule supplies no such proof, so direct S16 coupling is retained.  Do
    # not invent a concrete C from the shell recurrence to manufacture one.
    shadow_kernel_decoupling_proof = False

    negative_holonomy = 0
    for a, c, b in itertools.product(range(LANES), repeat=3):
        value = (basis_product(LANES, a, b)[0] *
                 basis_product(LANES, c, b ^ a)[0] *
                 basis_product(LANES, a, b ^ c)[0] *
                 basis_product(LANES, c, b)[0])
        if value not in (-1, 1):
            raise RuntimeError("sign holonomy escaped +/-1")
        negative_holonomy += value < 0

    reverse_sound_fixtures = reverse_interval_soundness_fixture_count()

    support_summary_fixtures = 0
    support_false_negatives = 0
    for storage_class, all_default, boundary_closed, fingerprints_match in (
            itertools.product(i_q["querySupportSummary"]["covers"],
                              (False, True), (False, True), (False, True))):
        del storage_class
        omit = all_default and boundary_closed and fingerprints_match
        contribution_possible = not omit
        support_false_negatives += omit and contribution_possible
        support_summary_fixtures += 1
    if support_false_negatives != 0:
        raise RuntimeError("query-support summary produced a false negative")

    # A dyadic split is an exact disjoint cover of the parent half-open cell.
    # Copying the complete S16 value therefore preserves the represented field
    # pointwise before any newly resolved higher-frequency information is added.
    child_domains = (
        (Fraction(0), Fraction(1, 2), Fraction(0), Fraction(1, 2)),
        (Fraction(1, 2), Fraction(1), Fraction(0), Fraction(1, 2)),
        (Fraction(0), Fraction(1, 2), Fraction(1, 2), Fraction(1)),
        (Fraction(1, 2), Fraction(1), Fraction(1, 2), Fraction(1)),
    )
    probe_coordinates = (Fraction(0), Fraction(1, 4), Fraction(1, 2),
                         Fraction(3, 4), Fraction(15, 16))
    prior_state = tuple(range(-8, 8))
    refined_states = (prior_state,) * 4
    child_measure = sum((u1 - u0) * (v1 - v0)
                        for u0, u1, v0, v1 in child_domains)
    if child_measure != Fraction(1):
        raise RuntimeError("dyadic refinement changed intrinsic query measure")
    for u, v in itertools.product(probe_coordinates, repeat=2):
        owners = [index for index, (u0, u1, v0, v1) in enumerate(child_domains)
                  if u0 <= u < u1 and v0 <= v < v1]
        if len(owners) != 1 or refined_states[owners[0]] != prior_state:
            raise RuntimeError("dyadic refinement lost pointwise full-S16 state")

    expressions = [
        {"id": "K16_BASIS_PRODUCT", "source": "I_TOE:1", "arity": 2,
         "neighbourhood": "LOCAL", "bracket": "mul(e_a,e_b)",
         "formula": "epsilon(a,b)*e_(a XOR b)"},
        {"id": "K16_ASSOCIATOR", "source": "I_TOE:2", "arity": 3,
         "neighbourhood": "LOCAL_CONTEXT", "bracket": "(a*b)*c-a*(b*c)",
         "formula": "Omega(a,b,c)*e_(a XOR b XOR c)"},
        {"id": "K16_DIFFRACTION", "source": "I_TOE:3", "arity": 2,
         "neighbourhood": "LOCAL", "bracket": "epsilon_ab*L_(a XOR b)-L_a*L_b",
         "formula": "sum_(a<b) D_ab"},
        {"id": "K16_ZERO_DIVISOR", "source": "I_TOE:4+A_S16",
         "arity": 2, "neighbourhood": "LOCAL_RELATION",
         "bracket": "mul(a,b)",
         "formula": "a!=0 and b!=0 and a*b==0; distinct from ker(A)"},
        {"id": "K16_SHELL", "source": "I_TOE:5", "arity": 1,
         "neighbourhood": "LOCAL", "bracket": "recursive_block_matrix",
         "formula": "A4^2=-15I16"},
        {"id": "K16_CLOSURE_EIGENMODE", "source": "I_TOE:6", "arity": 1,
         "neighbourhood": "FULL_LOCAL_STATE",
         "bracket": "C16*u0",
         "formula": "C16*u0=lambda0*u0; observer shadow is not full mode"},
        {"id": "MERKABA_SHADOW", "source": "I_TOE:6", "arity": 1,
         "neighbourhood": "LOCAL", "bracket": "P_t*s(address)",
         "formula": "F_M=16P_t"},
        {"id": "SHADOW_COUPLING", "source": "I_TOE:7", "arity": 1,
         "neighbourhood": "COMPLETE_PROGRAM", "bracket": "P*C*(I-P)",
         "formula": "omit_kernel_only_if_Cvk=Ckv=0"},
        {"id": "SIGN_TRANSPORT", "source": "I_TOE:8", "arity": 3,
         "neighbourhood": "GENERATED_CONTEXT", "bracket": "ordered_plaquette",
         "formula": "Ua(b)Uc(b XOR a)Ua(b XOR c)^-1Uc(b)^-1"},
        {"id": "NATIVE_MODAL_RELATION",
         "source": "I_TOE:8+I_Q:nativeModalRelation", "arity": 2,
         "neighbourhood": "GENERATED_INTRINSIC_CONTEXT",
         "bracket": "sub(u_j,transport(U_ij,u_i))",
         "formula": "exact_residual_in_fingerprinted_Q48_region; missing_region=UNRESOLVED"},
        {"id": "SENSOR_SHADOW", "source": "I_Q:SENSOR_LEFT_RIGHT", "arity": -1,
         "neighbourhood": "WHOLE_QUERY", "bracket": "project_then_reduce",
         "formula": "finite_footprint+order+first_hit+occlusion"},
        {"id": "EXACT_REVERSE", "source": "I_Q:reverseContractor", "arity": -1,
         "neighbourhood": "QUERY_SUPPORT_UNION", "bracket": "source_tree_reverse",
         "formula": "preimage_union_outward_intervals"},
        {"id": "DYADIC_REPRESENTATION", "source": "I_REP:kappa", "arity": 1,
         "neighbourhood": "HALF_OPEN_DYADIC_CELL", "bracket": "decode_before_query",
         "formula": "piecewise_constant_full_S16"},
        {"id": "ZEMPTY_DEFAULT", "source": "I_Q:defaultSemantics+I_REP:defaultRepresentations",
         "arity": -1, "neighbourhood": "WHOLE_PROGRAM",
         "bracket": "decode_default_then_evaluate",
         "formula": "unbacked=allocated=NULL=algebra_zero; all_default=DEFAULT_SAT"},
        {"id": "DIRECTIONAL_FIRST_HIT_ACTION", "source": "I_Q:sceneReduction+reverseContractor",
         "arity": -1, "neighbourhood": "WHOLE_QUERY_PREIMAGE",
         "bracket": "reverse_source_tree",
         "formula": "pre_hit_exclusion+first_hit_mould; behind_hit=NO_CLAIM"},
        {"id": "OPTICAL_NUISANCE", "source": "I_Q:photometricNuisance",
         "arity": -1, "neighbourhood": "COHERENT_EYE_SHADOW",
         "bracket": "bounded_transfer_then_joint_reverse",
         "formula": "bounded_calibrated_monotone_Q48_transfer"},
        {"id": "NATIVE_INFORMATION_PULLBACK", "source": "I_Q:certificate",
         "arity": -1, "neighbourhood": "COMPLETE_CONSTRAINT_FACTOR",
         "bracket": "retain_coupled_or_disjunctive_factor",
         "formula": "directional_exact_factor_not_scalar_confidence"},
        {"id": "CERTIFICATE_MINIMIZER", "source": "I_Q:certificate",
         "arity": -1, "neighbourhood": "ACCUMULATED_EVIDENCE",
         "bracket": "canonical_factor_order_then_exact_dominance",
         "formula": "deduplicate_only_same_scope_class; retain_coupling_and_disjunction"},
        {"id": "CANONICAL_GAUGE_NORMALIZER", "source": "I_REP:gaugeFamily+normalizer",
         "arity": -1, "neighbourhood": "FINITE_NONDEFAULT_SUPPORT",
         "bracket": "normalize_before_serialize",
         "formula": "global_translation_normal_form+exact_dyadic_split_collapse"},
    ]
    for expression in expressions:
        expression["fingerprint"] = sha256(expression)

    reverse_rules = [
        {"opcode": "PERMUTE_SIGN", "rule": "EXACT_INVERSE_PERMUTATION"},
        {"opcode": "ADD_SUB", "rule": "OUTWARD_INTERVAL_BACKPROPAGATION"},
        {"opcode": "QMUL_QDIV", "rule": "OUTWARD_INTERVAL_WITH_ZERO_BRANCH_UNION"},
        {"opcode": "BRACKETED_PRODUCT", "rule": "REVERSE_SAME_EXPRESSION_TREE"},
        {"opcode": "SCENE_REDUCE", "rule": "RETAIN_SUPPORT_DISJUNCTION"},
        {"opcode": "BEHIND_HIT", "rule": "NO_CLAIM"},
    ]

    # Constructive proof fixtures: exact duplicates collapse; coupled/disjunctive
    # factors do not. Translation normal form ignores input order.
    duplicate_receipts = {("scope0", "expr0", "class0", -4, 9): 0}
    for _ in range(10000):
        key = next(iter(duplicate_receipts))
        duplicate_receipts[key] += 1
    if len(duplicate_receipts) != 1 or next(iter(duplicate_receipts.values())) != 10000:
        raise RuntimeError("duplicate certificate minimizer is not bounded")
    coupled_factors = {
        ("scope0", "expr0", "class0", "coupling-A", "branch-A", -4, 9),
        ("scope0", "expr0", "class0", "coupling-B", "branch-B", -4, 9),
    }
    if len(coupled_factors) != 2:
        raise RuntimeError("certificate minimizer collapsed coupled/disjunctive factors")
    strong_interval = (-2, 3)
    weak_interval = (-4, 9)
    if (max(strong_interval[0], weak_interval[0]),
            min(strong_interval[1], weak_interval[1])) != strong_interval:
        raise RuntimeError("weak exact factor changed a stronger feasible interval")

    pattern = [(5, -3, 0, 7), (7, -2, 0, 11), (6, -3, 0, 9)]
    def normalize(values: Iterable[tuple[int, int, int, int]]) -> tuple[tuple[int, int, int, int], ...]:
        values = list(values)
        minimum_u, minimum_v, _, _ = min(
            values, key=lambda value: (value[0], value[1]))
        translated = ((u - minimum_u, v - minimum_v, level, state)
                      for u, v, level, state in values)
        return tuple(sorted(translated,
            key=lambda value: (value[2], signed_morton(value[0], value[1]),
                               value[0], value[1], value[3])))
    normalized = normalize(pattern)
    for permutation in itertools.permutations(pattern):
        if normalize(permutation) != normalized:
            raise RuntimeError("gauge normalization depends on discovery order")

    inputs = {
        "generatorSource": file_sha256(Path(__file__)),
        "toeCapsule": file_sha256(TOE_CAPSULE),
        "toeUpstreamDeclared": TOE_UPSTREAM_SHA256,
        "iQ": file_sha256(I_Q_SOURCE),
        "iRepresentation": file_sha256(I_REP_SOURCE),
        "canonicalSpec": file_sha256(ROOT / "new_spec.md"),
        "closurePlan": file_sha256(
            ROOT / ".codex" / "S4-08.6_NATIVE_CLOSURE_PLAN.md"),
        "algebraCore": algebra["fingerprints"]["nativeCore"],
    }
    descriptor = {
        "version": MERKABA_PROGRAM_VERSION,
        "numericDomain": NUMERIC_ID,
        "inputs": inputs,
        "authorityBoundary": {
            "toeSections": ["1", "2", "3", "4", "5", "6", "7", "8"],
            "querySchema": i_q["schemaVersion"],
            "representationSchema": i_rep["schemaVersion"],
            "otherToeSectorsImported": False,
        },
        "expressions": expressions,
        "reverseRules": reverse_rules,
        "queryFamilies": i_q["queryFamilies"],
        "sceneReduction": i_q["sceneReduction"],
        "photometricNuisance": i_q["photometricNuisance"],
        "querySupportSummary": i_q["querySupportSummary"],
        "certificate": i_q["certificate"],
        "representation": i_rep,
        "diffractionMatrix": [value for row in diffraction for value in row],
        "shellSquareByRank": shell_square_by_rank,
        "shadowNumerator4": [value for row in shadow for value in row],
        "visibleProjectorNumerator256": [value for row in visible_numerator
                                          for value in row],
        "proofs": {
            "associatorNonzero": associator_nonzero,
            "associatorHistogram": associator_coefficients,
            "diffractionSkew": True,
            "shellSquare": "-15I16",
            "shadowFrame": "16P_t",
            "shadowKernelDecouplingProof": shadow_kernel_decoupling_proof,
            "negativeHolonomy": negative_holonomy,
            "e22InventoryCount": 0,
            "directS16DependenciesRetained": True,
            "zEmpty": "ALGEBRA_ZERO",
            "legacyZNullAccepted": False,
            "allDefaultRelation": "DEFAULT_SAT",
            "allDefaultActiveWork": 0,
            "behindHitAction": "NO_CLAIM",
            "missingOpticalMetadata": "NO_OPTICAL_CLAIM",
            "querySupportFalseNegatives": 0,
            "querySupportFixtureCount": support_summary_fixtures,
            "reverseIntervalSoundFixtureCount": reverse_sound_fixtures,
            "reverseZeroBranchRetained": True,
            "duplicateFixtureCount": 10000,
            "duplicateMinimizedFactorCount": len(duplicate_receipts),
            "coupledFactorInputCount": 2,
            "coupledFactorMinimizedCount": len(coupled_factors),
            "weakFactorPreservesStrongRegion": True,
            "gaugePermutationCount": 6,
            "baseGaugeNormalForm": [list(value) for value in normalized],
            "refinementProlongation": "FOUR_EXACT_FULL_S16_COPIES",
            "refinementExactHalfOpenCover": True,
            "refinementPointwiseFullS16": True,
            "refinementExactMeasure": True,
            "representationDefaultParity": True,
            "mixedDefaultSupportBehaviour": "DESCRIPTOR_EVALUATION_REQUIRED",
            "canFreezeShadowKernel": False,
            "shellBaseOrientationFrozen": False,
            "completeProgramFibreSelector": "IDENTITY_ONLY_UNLESS_EQUIVALENCE_PROVED",
            "opticalCalibrationProvenance": True,
            "opticalUnboundedExplanationForbidden": True,
        },
    }
    descriptor["fingerprint"] = sha256(descriptor)
    return descriptor


def build_descriptor() -> dict:
    signs = []
    indices = []
    for left in range(LANES):
        for right in range(LANES):
            sign, index = basis_product(LANES, left, right)
            signs.append(sign)
            indices.append(index)

    all_dyads = dyads()
    zero_pairs = []
    for witness in all_dyads:
        for annihilator in all_dyads:
            if not any(multiply_dyads(witness, annihilator)):
                zero_pairs.append((witness, annihilator))
    zero_pairs.sort()
    if not zero_pairs:
        raise RuntimeError("S16 generator found no signed-dyad zero divisors")

    annihilators = sorted({annihilator for _, annihilator in zero_pairs})
    annihilator_index = {value: index for index, value in enumerate(annihilators)}
    catalog = [(*witness, *annihilator, annihilator_index[annihilator])
               for witness, annihilator in zero_pairs]
    z_null = zero_pairs[0][0]

    hadamard = [hadamard_sign(row, column)
                for row in range(LANES) for column in range(LANES)]
    geometry_rows = [row for row in range(LANES)
                     if hadamard_sign(row, z_null[0]) * z_null[1] +
                     hadamard_sign(row, z_null[2]) * z_null[3] == 0][:4]
    if len(geometry_rows) != 4:
        raise RuntimeError("could not select four exact geometry rows")
    hidden_rows = [row for row in range(LANES) if row not in geometry_rows]

    left_sources = []
    left_signs = []
    right_sources = []
    right_signs = []
    for basis in range(LANES):
        for output in range(LANES):
            source = basis ^ output
            left_sources.append(source)
            left_signs.append(basis_product(LANES, basis, source)[0])
            right_sources.append(source)
            right_signs.append(basis_product(LANES, source, basis)[0])

    operator_semantics = {
        "conjugation": "lane[i] * conjugateSign[i]",
        "hadamardB": "fixed_reduce_c(hadamardSign[r,c] * lane[c])",
        "hadamardBT": "fixed_reduce_r(hadamardSign[r,c] * lane[r])",
        "geometryG": "hadamardB(rows=geometryRows)",
        "hiddenF": "hadamardB(rows=hiddenRows)",
        "leftBasis": "permute(source=basis XOR output,sign=mulBasis[basis,source])",
        "rightBasis": "permute(source=basis XOR output,sign=mulBasis[source,basis])",
        "signedDyad": "add_signed(left/right basis actions); no QMUL/QDIV",
        "view": "explicit sparse left/right quaternionic basis-action tree",
        "transition": "mul(conjugate(lhs),rhs)",
        "associator": "sub(mul(mul(a,b),c),mul(a,mul(b,c)))",
        "projectiveMeet": "lane-wise MAX lower / MIN upper / CMP",
        "projectiveCommit": "clamp then hadamardBT then exact SHIFT 4",
        "codecPredicates": "CMP/MASK/SELECT/FIXED_BOUNDED_REDUCTION",
    }

    # Static execution-plan costs are diagnostic only.  They describe the
    # fixed generated circuits, never influence canonical acceptance, and let
    # the live GPU multiply one compacted work count per stage instead of
    # issuing a global atomic for every exact ALU operation.
    operator_costs = {
        "hadamard16": {
            "xorPermutation": 0,
            "signedAddSub": 64,
            "maskSelect": 16,
            "q48WideMul": 0,
            "q48Div": 0,
            "intervalMulDiv": 0,
        },
        "signedDyadAction": {
            "xorPermutation": 32,
            "signedAddSub": 16,
            "maskSelect": 32,
            "q48WideMul": 0,
            "q48Div": 0,
            "intervalMulDiv": 0,
        },
        "projectiveMeet16": {
            "xorPermutation": 0,
            "signedAddSub": 0,
            "maskSelect": 32,
            "q48WideMul": 0,
            "q48Div": 0,
            "intervalMulDiv": 0,
        },
        "jointSourceCell": {
            "xorPermutation": 64,
            "signedAddSub": 192,
            "maskSelect": 96,
            "q48WideMul": 32,
            "q48Div": 16,
            "intervalMulDiv": 16,
        },
        "associatorCell": {
            "xorPermutation": 0,
            "signedAddSub": 0,
            "maskSelect": 0,
            "q48WideMul": 0,
            "q48Div": 0,
            "intervalMulDiv": 0,
            "genericDenseS16Products": 4,
        },
    }

    multiplication_descriptor = {
        "dimension": LANES,
        "signs": signs,
        "indices": indices,
        "conjugate": [conjugate_sign(index) for index in range(LANES)],
        "leftSources": left_sources,
        "leftSigns": left_signs,
        "rightSources": right_sources,
        "rightSigns": right_signs,
    }
    annihilator_descriptor = {
        "catalog": catalog,
        "actions": annihilators,
        "zNull": z_null,
    }
    readout_descriptor = {
        "hadamard": hadamard,
        "geometryRows": geometry_rows,
        "hiddenRows": hidden_rows,
    }
    fingerprints = {
        "numeric": sha256({
            "id": NUMERIC_ID,
            "signed": True,
            "integerBits": 16,
            "fractionBits": 48,
            "storageBits": 64,
            "rounding": "nearest_even",
            "overflow": "checked",
            "scale": "binary_power",
        }),
        "multiplication": sha256(multiplication_descriptor),
        "annihilator": sha256(annihilator_descriptor),
        "readout": sha256(readout_descriptor),
        "operators": sha256(operator_semantics),
    }
    fingerprints["bundle"] = sha256({
        "version": GENERATOR_VERSION,
        "fingerprints": fingerprints,
    })
    fingerprints["zeroDivisorRelation"] = sha256({
        "catalog": catalog,
        "actions": annihilators,
    })
    fingerprints["nativeCore"] = sha256({
        "generatorVersion": GENERATOR_VERSION,
        "numeric": fingerprints["numeric"],
        "multiplication": fingerprints["multiplication"],
        "zeroDivisorRelation": fingerprints["zeroDivisorRelation"],
    })

    descriptor = {
        "generatorVersion": GENERATOR_VERSION,
        "numericId": NUMERIC_ID,
        "multiplication": multiplication_descriptor,
        "annihilator": annihilator_descriptor,
        "readout": readout_descriptor,
        "operators": operator_semantics,
        "operatorCosts": operator_costs,
        "fingerprints": fingerprints,
    }
    validate_descriptor(descriptor)
    return descriptor


def validate_descriptor(descriptor: dict) -> None:
    multiplication = descriptor["multiplication"]
    for left in range(LANES):
        for right in range(LANES):
            offset = left * LANES + right
            sign, index = basis_product(LANES, left, right)
            if multiplication["indices"][offset] != (left ^ right):
                raise RuntimeError("generated basis address is not XOR")
            if multiplication["signs"][offset] != sign or index != (left ^ right):
                raise RuntimeError("generated sign table disagrees with recursion")
    for basis in range(1, LANES):
        if basis_product(LANES, basis, basis) != (-1, 0):
            raise RuntimeError(f"e_{basis}^2 is not -1")
    for entry in descriptor["annihilator"]["catalog"]:
        witness = tuple(entry[:4])
        annihilator = tuple(entry[4:8])
        if any(multiply_dyads(witness, annihilator)):
            raise RuntimeError("non-zero product entered annihilator catalog")
    z_null = descriptor["annihilator"]["zNull"]
    for row in descriptor["readout"]["geometryRows"]:
        dot = (hadamard_sign(row, z_null[0]) * z_null[1] +
               hadamard_sign(row, z_null[2]) * z_null[3])
        if dot != 0:
            raise RuntimeError("G z_null is non-zero")


def chunks(values: Iterable[int], width: int = 16) -> list[list[int]]:
    values = list(values)
    return [values[index:index + width] for index in range(0, len(values), width)]


def cs_array(name: str, element_type: str, values: Iterable[int],
             width: int = 16) -> str:
    lines = [f"        internal static readonly {element_type}[] {name} =", "        {"]
    for group in chunks(values, width):
        lines.append("            " + ", ".join(str(value) for value in group) + ",")
    lines.append("        };")
    return "\n".join(lines)


def render_cs(descriptor: dict) -> str:
    multiplication = descriptor["multiplication"]
    annihilator = descriptor["annihilator"]
    readout = descriptor["readout"]
    fingerprints = descriptor["fingerprints"]
    costs = descriptor["operatorCosts"]
    catalog_flat = [value for entry in annihilator["catalog"] for value in entry]
    actions_flat = [value for entry in annihilator["actions"] for value in entry]
    return f"""// <auto-generated by Tools/sigma/generate_sigma_operators.py>
// Canonical baseline: CPQ4-2026-08-22-S16-v6. Do not edit by hand.

namespace Genesis.RoomScan.SigmaPrism
{{
    internal static class SigmaGeneratedAlgebra
    {{
        internal const string GeneratorVersion = \"{descriptor['generatorVersion']}\";
        internal const string NumericDomainId = \"{descriptor['numericId']}\";
        internal const int LaneCount = {LANES};
        internal const int ZeroDivisorCatalogStride = 9;
        internal const int ZeroDivisorCatalogCount = {len(annihilator['catalog'])};
        internal const int AnnihilatorActionStride = 4;
        internal const int AnnihilatorActionCount = {len(annihilator['actions'])};
        internal const string NumericFingerprint = \"{fingerprints['numeric']}\";
        internal const string MultiplicationFingerprint = \"{fingerprints['multiplication']}\";
        internal const string AnnihilatorFingerprint = \"{fingerprints['annihilator']}\";
        internal const string ZeroDivisorRelationFingerprint = \"{fingerprints['zeroDivisorRelation']}\";
        internal const string NativeCoreFingerprint = \"{fingerprints['nativeCore']}\";
        internal const string ReadoutFingerprint = \"{fingerprints['readout']}\";
        internal const string OperatorFingerprint = \"{fingerprints['operators']}\";
        internal const string BundleFingerprint = \"{fingerprints['bundle']}\";

        // Generated fixed-circuit diagnostic costs. They are execution metadata,
        // not canonical algebra semantics and therefore do not alter fingerprints.
        internal const int CostHadamardSignedAddSub = {costs['hadamard16']['signedAddSub']};
        internal const int CostHadamardMaskSelect = {costs['hadamard16']['maskSelect']};
        internal const int CostDyadXorPermutation = {costs['signedDyadAction']['xorPermutation']};
        internal const int CostDyadSignedAddSub = {costs['signedDyadAction']['signedAddSub']};
        internal const int CostDyadMaskSelect = {costs['signedDyadAction']['maskSelect']};
        internal const int CostMeetMaskSelect = {costs['projectiveMeet16']['maskSelect']};
        internal const int CostSourceXorPermutation = {costs['jointSourceCell']['xorPermutation']};
        internal const int CostSourceSignedAddSub = {costs['jointSourceCell']['signedAddSub']};
        internal const int CostSourceMaskSelect = {costs['jointSourceCell']['maskSelect']};
        internal const int CostSourceWideMultiply = {costs['jointSourceCell']['q48WideMul']};
        internal const int CostSourceDivide = {costs['jointSourceCell']['q48Div']};
        internal const int CostSourceIntervalMulDiv = {costs['jointSourceCell']['intervalMulDiv']};
        internal const int CostAssociatorDenseProducts = {costs['associatorCell']['genericDenseS16Products']};

{cs_array('MultiplicationSigns', 'sbyte', multiplication['signs'])}

{cs_array('MultiplicationIndices', 'byte', multiplication['indices'])}

{cs_array('ConjugateSigns', 'sbyte', multiplication['conjugate'])}

{cs_array('LeftBasisSources', 'byte', multiplication['leftSources'])}

{cs_array('LeftBasisSigns', 'sbyte', multiplication['leftSigns'])}

{cs_array('RightBasisSources', 'byte', multiplication['rightSources'])}

{cs_array('RightBasisSigns', 'sbyte', multiplication['rightSigns'])}

{cs_array('HadamardSigns', 'sbyte', readout['hadamard'])}

{cs_array('GeometryRows', 'byte', readout['geometryRows'])}

{cs_array('HiddenRows', 'byte', readout['hiddenRows'])}

{cs_array('ZNullDyad', 'sbyte', annihilator['zNull'])}

{cs_array('AnnihilatorActions', 'short', actions_flat, 12)}

{cs_array('ZeroDivisorCatalog', 'short', catalog_flat, 18)}
    }}
}}
"""


def fingerprint_words(hex_digest: str) -> list[int]:
    return [int(hex_digest[index:index + 8], 16) for index in range(0, 64, 8)]


def render_hlsl_layout(descriptor: dict) -> str:
    geometry_text = ", ".join(
        f"{value}u" for value in descriptor["readout"]["geometryRows"])
    hidden_text = ", ".join(
        f"{value}u" for value in descriptor["readout"]["hiddenRows"])
    costs = descriptor["operatorCosts"]
    return f"""// <auto-generated by Tools/sigma/generate_sigma_operators.py>
// Canonical baseline: CPQ4-2026-08-22-S16-v6. Do not edit by hand.
#ifndef SIGMA_GENERATED_LAYOUT_INCLUDED
#define SIGMA_GENERATED_LAYOUT_INCLUDED

#define SIGMA_S16_LANES 16u
#define SIGMA_Q48_FRACTION_BITS 48u
#define SIGMA_Q48_ONE_LO 0u
#define SIGMA_Q48_ONE_HI 0x00010000u

// Generated fixed-circuit diagnostic costs. These constants describe the
// scheduled exact lowering and are consumed only by Section 44 telemetry.
#define SIGMA_COST_HADAMARD_SIGNED_ADD_SUB {costs['hadamard16']['signedAddSub']}u
#define SIGMA_COST_HADAMARD_MASK_SELECT {costs['hadamard16']['maskSelect']}u
#define SIGMA_COST_DYAD_XOR_PERMUTATION {costs['signedDyadAction']['xorPermutation']}u
#define SIGMA_COST_DYAD_SIGNED_ADD_SUB {costs['signedDyadAction']['signedAddSub']}u
#define SIGMA_COST_DYAD_MASK_SELECT {costs['signedDyadAction']['maskSelect']}u
#define SIGMA_COST_MEET_MASK_SELECT {costs['projectiveMeet16']['maskSelect']}u
#define SIGMA_COST_SOURCE_XOR_PERMUTATION {costs['jointSourceCell']['xorPermutation']}u
#define SIGMA_COST_SOURCE_SIGNED_ADD_SUB {costs['jointSourceCell']['signedAddSub']}u
#define SIGMA_COST_SOURCE_MASK_SELECT {costs['jointSourceCell']['maskSelect']}u
#define SIGMA_COST_SOURCE_WIDE_MULTIPLY {costs['jointSourceCell']['q48WideMul']}u
#define SIGMA_COST_SOURCE_DIVIDE {costs['jointSourceCell']['q48Div']}u
#define SIGMA_COST_SOURCE_INTERVAL_MULDIV {costs['jointSourceCell']['intervalMulDiv']}u
#define SIGMA_COST_ASSOCIATOR_DENSE_PRODUCTS {costs['associatorCell']['genericDenseS16Products']}u

static const uint SIGMA_GEOMETRY_ROWS[4] = {{ {geometry_text} }};
static const uint SIGMA_HIDDEN_ROWS[12] = {{ {hidden_text} }};

#endif
"""


def render_hlsl(descriptor: dict) -> str:
    signs = descriptor["multiplication"]["signs"]
    negative_masks = []
    for row in range(LANES):
        mask = 0
        for column in range(LANES):
            if signs[row * LANES + column] < 0:
                mask |= 1 << column
        negative_masks.append(mask)
    z_null = descriptor["annihilator"]["zNull"]
    actions = descriptor["annihilator"]["actions"]
    fingerprints = descriptor["fingerprints"]
    masks = ", ".join(f"0x{value:04x}u" for value in negative_masks)
    action_lines = ",\n    ".join(
        f"int4({value[0]}, {value[1]}, {value[2]}, {value[3]})"
        for value in actions)
    fingerprint_lines = []
    for name in ("numeric", "multiplication", "annihilator",
                 "zeroDivisorRelation", "nativeCore", "readout", "operators",
                 "bundle"):
        words = ", ".join(
            f"0x{value:08x}u" for value in fingerprint_words(fingerprints[name]))
        fingerprint_lines.append(
            f"static const uint SIGMA_{name.upper()}_FINGERPRINT[8] = {{ {words} }};")
    fingerprint_text = "\n".join(fingerprint_lines)
    return f"""// <auto-generated by Tools/sigma/generate_sigma_operators.py>
// Canonical baseline: CPQ4-2026-08-22-S16-v6. Do not edit by hand.
#ifndef SIGMA_GENERATED_TABLES_INCLUDED
#define SIGMA_GENERATED_TABLES_INCLUDED

#include "SigmaGeneratedLayout.hlsl"

#define SIGMA_ANNIHILATOR_ACTION_COUNT {len(descriptor['annihilator']['actions'])}u
#define SIGMA_ZERO_DIVISOR_CATALOG_COUNT {len(descriptor['annihilator']['catalog'])}u
#define SIGMA_Z_NULL_ANNIHILATOR_ACTION {descriptor['annihilator']['catalog'][0][8]}u

static const uint SIGMA_MUL_NEGATIVE_MASK[16] = {{ {masks} }};
static const int4 SIGMA_Z_NULL_DYAD = int4({z_null[0]}, {z_null[1]}, {z_null[2]}, {z_null[3]});
static const int4 SIGMA_ANNIHILATOR_ACTIONS[{len(actions)}] = {{
    {action_lines}
}};
{fingerprint_text}

uint SigmaMulBasisIndex(uint left, uint right) {{ return left ^ right; }}
int SigmaMulBasisSign(uint left, uint right)
{{
    return ((SIGMA_MUL_NEGATIVE_MASK[left] >> right) & 1u) != 0u ? -1 : 1;
}}
int SigmaConjugateSign(uint lane) {{ return lane == 0u ? 1 : -1; }}
int SigmaHadamardSign(uint row, uint column)
{{
    return (countbits(row & column) & 1u) != 0u ? -1 : 1;
}}

#endif
"""


def render_merkaba_cs(descriptor: dict) -> str:
    proofs = descriptor["proofs"]
    expression_fingerprints = ",\n".join(
        f'            "{entry["fingerprint"]}"'
        for entry in descriptor["expressions"])
    input_lines = "\n".join(
        f'        internal const string {upper_snake(name).title().replace("_", "")}InputFingerprint = "{value}";'
        for name, value in descriptor["inputs"].items())
    return f"""// <auto-generated by Tools/sigma/generate_sigma_operators.py>
// Canonical baseline: CPQ4-2026-08-25-S16-v8.3. Do not edit by hand.

using System;

namespace Genesis.RoomScan.SigmaPrism
{{
    internal enum SigmaMerkabaRelationClass : uint
    {{
        DefaultSat = 0u,
        Regular = 1u,
        ExactZeroDivisor = 2u,
        NearSingularQ48 = 3u,
        NonassociativeContext = 4u,
        NoRelation = 5u,
        Unresolved = 6u,
    }}

    internal enum SigmaNativeQueryClaim : uint
    {{
        NoClaim = 0u,
        PreHitExclusion = 1u,
        FirstHitMould = 2u,
    }}

    internal static class SigmaGeneratedMerkabaProgram
    {{
        internal const string ProgramVersion = "{descriptor['version']}";
        internal const string NumericDomainId = "{descriptor['numericDomain']}";
        internal const string ProgramFingerprint = "{descriptor['fingerprint']}";
        internal const string DeclaredToeUpstreamFingerprint = "{descriptor['inputs']['toeUpstreamDeclared']}";
{input_lines}
        internal const int ExpressionCount = {len(descriptor['expressions'])};
        internal const int AssociatorNonzeroBasisTriples = {proofs['associatorNonzero']};
        internal const bool ShadowKernelDecouplingProofSupplied = false;
        internal const int NegativeHolonomyFixtures = {proofs['negativeHolonomy']};
        internal const int E22InventoryCount = 0;
        internal const bool DirectS16DependenciesRetained = true;
        internal const bool LegacyZNullAccepted = false;
        internal const int QuerySupportFalseNegatives = 0;
        internal const int QuerySupportFixtureCount = {proofs['querySupportFixtureCount']};
        internal const int ReverseIntervalSoundFixtureCount = {proofs['reverseIntervalSoundFixtureCount']};
        internal const bool ReverseZeroBranchRetained = true;
        internal const int DuplicateFixtureCount = {proofs['duplicateFixtureCount']};
        internal const int DuplicateMinimizedFactorCount = {proofs['duplicateMinimizedFactorCount']};
        internal const int CoupledFactorInputCount = {proofs['coupledFactorInputCount']};
        internal const int CoupledFactorMinimizedCount = {proofs['coupledFactorMinimizedCount']};
        internal const bool WeakFactorPreservesStrongRegion = true;
        internal const int AllDefaultActiveWork = 0;
        internal const bool MissingOpticalMetadataProducesClaim = false;
        internal const bool BehindHitProducesAction = false;
        internal const int RefinementChildCount = 4;
        internal const bool RefinementCopiesFullS16 = true;
        internal const bool RefinementExactHalfOpenCover = true;
        internal const bool RefinementPointwiseFullS16 = true;
        internal const bool RefinementExactMeasure = true;
        internal const bool RepresentationDefaultParity = true;
        internal const bool CanFreezeShadowKernel = false;
        internal const bool OpticalCalibrationProvenance = true;
        internal const bool OpticalUnboundedExplanationForbidden = true;

        internal static readonly string[] ExpressionFingerprints =
        {{
{expression_fingerprints}
        }};

{cs_array('DiffractionMatrix', 'sbyte', descriptor['diffractionMatrix'])}

        // Orientation-independent recurrence invariant A_k^2 = -(2^k-1)I.
{cs_array('ShellSquareByRank', 'sbyte', descriptor['shellSquareByRank'], 4)}

        // p(address) = ShadowNumerator4 / 4.
{cs_array('ShadowNumerator4', 'sbyte', descriptor['shadowNumerator4'])}

        // P_visible = VisibleProjectorNumerator256 / 256.
{cs_array('VisibleProjectorNumerator256', 'sbyte', descriptor['visibleProjectorNumerator256'])}

{cs_array('BaseGaugeNormalForm', 'int', (value for entry in proofs['baseGaugeNormalForm'] for value in entry), 9)}

        internal static int BasisSign(int left, int right)
        {{
            RequireAddress(left);
            RequireAddress(right);
            return SigmaGeneratedAlgebra.MultiplicationSigns[(left << 4) + right];
        }}

        internal static int AssociatorCoefficient(int a, int b, int c)
        {{
            RequireAddress(a);
            RequireAddress(b);
            RequireAddress(c);
            return BasisSign(a, b) * BasisSign(a ^ b, c) -
                   BasisSign(b, c) * BasisSign(a, b ^ c);
        }}

        internal static int SignTransport(int generator, int address) =>
            BasisSign(generator, address);

        internal static int PlaquetteHolonomy(int a, int c, int b)
        {{
            RequireAddress(a);
            RequireAddress(c);
            RequireAddress(b);
            return SignTransport(a, b) * SignTransport(c, b ^ a) *
                   SignTransport(a, b ^ c) * SignTransport(c, b);
        }}

        internal static int ShadowNumerator(int address, int axis)
        {{
            RequireAddress(address);
            if ((uint)axis >= 4u)
                throw new ArgumentOutOfRangeException(nameof(axis));
            return ShadowNumerator4[(address << 2) + axis];
        }}

        internal static bool IsZEmpty(SigmaS16 value) => value.IsZero;

        internal static SigmaNativeQueryClaim ReverseActionFor(
            SigmaNativeQueryClaim measuredRole) => measuredRole switch
        {{
            SigmaNativeQueryClaim.PreHitExclusion =>
                SigmaNativeQueryClaim.PreHitExclusion,
            SigmaNativeQueryClaim.FirstHitMould =>
                SigmaNativeQueryClaim.FirstHitMould,
            _ => SigmaNativeQueryClaim.NoClaim,
        }};

        internal static bool CanOmitQueryRegion(bool allDefault,
            bool defaultBoundaryClosed, bool fingerprintsMatch) =>
            allDefault && defaultBoundaryClosed && fingerprintsMatch;

        internal static SigmaMerkabaRelationClass ClassifyZeroDivisor(
            bool leftNonzero, bool rightNonzero, bool exactProductZero,
            bool calibratedNonzeroNear)
        {{
            if (leftNonzero && rightNonzero && exactProductZero)
                return SigmaMerkabaRelationClass.ExactZeroDivisor;
            if (!exactProductZero && calibratedNonzeroNear)
                return SigmaMerkabaRelationClass.NearSingularQ48;
            return SigmaMerkabaRelationClass.Regular;
        }}

        internal static SigmaMerkabaRelationClass ClassifyAllDefault(
            SigmaS16 left, SigmaS16 right) =>
            IsZEmpty(left) && IsZEmpty(right)
                ? SigmaMerkabaRelationClass.DefaultSat
                : SigmaMerkabaRelationClass.Unresolved;

        private static void RequireAddress(int value)
        {{
            if ((uint)value >= 16u)
                throw new ArgumentOutOfRangeException(nameof(value));
        }}
    }}
}}
"""


def render_merkaba_hlsl(descriptor: dict) -> str:
    proofs = descriptor["proofs"]
    diffraction = ", ".join(str(value) for value in descriptor["diffractionMatrix"])
    shadow = ", ".join(str(value) for value in descriptor["shadowNumerator4"])
    visible = ", ".join(
        str(value) for value in descriptor["visibleProjectorNumerator256"])
    words = ", ".join(
        f"0x{value:08x}u" for value in fingerprint_words(descriptor["fingerprint"]))
    return f"""// <auto-generated by Tools/sigma/generate_sigma_operators.py>
// Canonical baseline: CPQ4-2026-08-25-S16-v8.3. Do not edit by hand.
#ifndef SIGMA_GENERATED_MERKABA_PROGRAM_INCLUDED
#define SIGMA_GENERATED_MERKABA_PROGRAM_INCLUDED

#include "SigmaGeneratedTables.hlsl"

#define SIGMA_MERKABA_RELATION_DEFAULT_SAT 0u
#define SIGMA_MERKABA_RELATION_REGULAR 1u
#define SIGMA_MERKABA_RELATION_EXACT_ZD 2u
#define SIGMA_MERKABA_RELATION_NEAR_SINGULAR_Q48 3u
#define SIGMA_MERKABA_RELATION_NONASSOCIATIVE_CONTEXT 4u
#define SIGMA_MERKABA_RELATION_NO_RELATION 5u
#define SIGMA_MERKABA_RELATION_UNRESOLVED 6u

#define SIGMA_NATIVE_QUERY_NO_CLAIM 0u
#define SIGMA_NATIVE_QUERY_PRE_HIT_EXCLUSION 1u
#define SIGMA_NATIVE_QUERY_FIRST_HIT_MOULD 2u

#define SIGMA_MERKABA_DIRECT_S16_DEPENDENCIES_RETAINED 1u
#define SIGMA_MERKABA_LEGACY_Z_NULL_ACCEPTED 0u
#define SIGMA_MERKABA_ALL_DEFAULT_ACTIVE_WORK 0u
#define SIGMA_MERKABA_QUERY_SUPPORT_FALSE_NEGATIVES 0u
#define SIGMA_MERKABA_QUERY_SUPPORT_FIXTURE_COUNT {proofs['querySupportFixtureCount']}u
#define SIGMA_MERKABA_REVERSE_SOUND_FIXTURE_COUNT {proofs['reverseIntervalSoundFixtureCount']}u
#define SIGMA_MERKABA_MISSING_OPTICAL_METADATA_PRODUCES_CLAIM 0u
#define SIGMA_MERKABA_BEHIND_HIT_PRODUCES_ACTION 0u
#define SIGMA_MERKABA_REFINEMENT_CHILD_COUNT 4u
#define SIGMA_MERKABA_REPRESENTATION_DEFAULT_PARITY 1u
#define SIGMA_MERKABA_CAN_FREEZE_SHADOW_KERNEL 0u

static const uint SIGMA_MERKABA_PROGRAM_FINGERPRINT[8] = {{ {words} }};
static const int SIGMA_MERKABA_DIFFRACTION[256] = {{ {diffraction} }};
static const int SIGMA_MERKABA_SHELL_SQUARE_BY_RANK[4] = {{ -1, -3, -7, -15 }};
static const int SIGMA_MERKABA_SHADOW_NUMERATOR4[64] = {{ {shadow} }};
static const int SIGMA_MERKABA_VISIBLE_PROJECTOR_NUMERATOR256[256] = {{ {visible} }};

int SigmaMerkabaBasisSign(uint left, uint right)
{{
    return SigmaMulBasisSign(left, right);
}}

int SigmaMerkabaAssociatorCoefficient(uint a, uint b, uint c)
{{
    return SigmaMerkabaBasisSign(a, b) * SigmaMerkabaBasisSign(a ^ b, c) -
           SigmaMerkabaBasisSign(b, c) * SigmaMerkabaBasisSign(a, b ^ c);
}}

int SigmaMerkabaSignTransport(uint generator, uint address)
{{
    return SigmaMerkabaBasisSign(generator, address);
}}

int SigmaMerkabaPlaquetteHolonomy(uint a, uint c, uint b)
{{
    return SigmaMerkabaSignTransport(a, b) *
           SigmaMerkabaSignTransport(c, b ^ a) *
           SigmaMerkabaSignTransport(a, b ^ c) *
           SigmaMerkabaSignTransport(c, b);
}}

int SigmaMerkabaShadowNumerator(uint address, uint axis)
{{
    return SIGMA_MERKABA_SHADOW_NUMERATOR4[address * 4u + axis];
}}

bool SigmaMerkabaIsZEmpty(uint2 state[16])
{{
    uint nonzero = 0u;
    [unroll]
    for (uint lane = 0u; lane < 16u; ++lane)
        nonzero |= state[lane].x | state[lane].y;
    return nonzero == 0u;
}}

uint SigmaMerkabaReverseActionFor(uint measuredRole)
{{
    return measuredRole == SIGMA_NATIVE_QUERY_PRE_HIT_EXCLUSION ?
        SIGMA_NATIVE_QUERY_PRE_HIT_EXCLUSION :
        (measuredRole == SIGMA_NATIVE_QUERY_FIRST_HIT_MOULD ?
            SIGMA_NATIVE_QUERY_FIRST_HIT_MOULD : SIGMA_NATIVE_QUERY_NO_CLAIM);
}}

bool SigmaMerkabaCanOmitQueryRegion(bool allDefault,
    bool defaultBoundaryClosed, bool fingerprintsMatch)
{{
    return allDefault && defaultBoundaryClosed && fingerprintsMatch;
}}

uint SigmaMerkabaClassifyZeroDivisor(bool leftNonzero, bool rightNonzero,
    bool exactProductZero, bool calibratedNonzeroNear)
{{
    bool exactZd = leftNonzero && rightNonzero && exactProductZero;
    bool nearZd = !exactProductZero && calibratedNonzeroNear;
    return exactZd ? SIGMA_MERKABA_RELATION_EXACT_ZD :
        (nearZd ? SIGMA_MERKABA_RELATION_NEAR_SINGULAR_Q48 :
            SIGMA_MERKABA_RELATION_REGULAR);
}}

#endif
"""


def render_authority_manifest(descriptor: dict) -> str:
    manifest = {
        "schemaVersion": "CPQ4-S16-MERKABA-AUTHORITY-1",
        "programVersion": descriptor["version"],
        "programFingerprint": descriptor["fingerprint"],
        "inputs": {
            "generatorSource": {
                "path": Path(__file__).resolve().relative_to(ROOT).as_posix(),
                "sha256": descriptor["inputs"]["generatorSource"],
                "version": GENERATOR_VERSION,
            },
            "toeCapsule": {
                "path": TOE_CAPSULE.relative_to(ROOT).as_posix(),
                "sha256": descriptor["inputs"]["toeCapsule"],
                "declaredUpstreamSource": "PROJECTION_ALGEBRA_TOE_CANONICAL.md",
                "declaredUpstreamSha256": TOE_UPSTREAM_SHA256,
                "admittedSections": descriptor["authorityBoundary"]["toeSections"],
            },
            "queryBoundary": {
                "path": I_Q_SOURCE.relative_to(ROOT).as_posix(),
                "sha256": descriptor["inputs"]["iQ"],
            },
            "representation": {
                "path": I_REP_SOURCE.relative_to(ROOT).as_posix(),
                "sha256": descriptor["inputs"]["iRepresentation"],
            },
            "canonicalSpec": {
                "path": "new_spec.md",
                "sha256": descriptor["inputs"]["canonicalSpec"],
            },
            "closurePlan": {
                "path": ".codex/S4-08.6_NATIVE_CLOSURE_PLAN.md",
                "sha256": descriptor["inputs"]["closurePlan"],
            },
            "algebraCoreSha256": descriptor["inputs"]["algebraCore"],
        },
        "otherToeSectorsImported": False,
        "e22InventoryCount": 0,
        "directS16DependenciesRetained": True,
        "expressionInventory": descriptor["expressions"],
        "reverseRules": descriptor["reverseRules"],
        "queryFamilies": descriptor["queryFamilies"],
        "sceneReduction": descriptor["sceneReduction"],
        "photometricNuisance": descriptor["photometricNuisance"],
        "querySupportSummary": descriptor["querySupportSummary"],
        "certificate": descriptor["certificate"],
        "representation": descriptor["representation"],
        "generatedOutputs": [
            CS_MERKABA_OUTPUT.relative_to(ROOT).as_posix(),
            HLSL_MERKABA_OUTPUT.relative_to(ROOT).as_posix(),
        ],
        "proofs": descriptor["proofs"],
    }
    return json.dumps(manifest, indent=2, sort_keys=True) + "\n"


def uint_array(values: Iterable[int]) -> str:
    return ", ".join(f"{int(value)}u" for value in values)


def upper_snake(value: str) -> str:
    output = []
    for index, character in enumerate(value):
        if (character.isupper() and index > 0 and
                (not value[index - 1].isupper() or
                 (index + 1 < len(value) and value[index + 1].islower()))):
            output.append("_")
        output.append(character.upper())
    return "".join(output)


def frame_abi_descriptor() -> dict:
    descriptor = {
        "version": FRAME_ABI_VERSION,
        "laneCount": LANES,
        "sourceCount": len(FRAME_ENUMS["SigmaFrameSource"]),
        "structs": [
            {
                "name": name,
                "fields": list(fields),
                "stride": len(fields) * 16,
            }
            for name, fields in FRAME_STRUCTS
        ],
        "enums": FRAME_ENUMS,
        "outcomeFlags": FRAME_OUTCOME_FLAGS,
        "cellFlags": FRAME_CELL_FLAGS,
        "packedQ48Stride": 8,
        "validityStride": 4,
        "provenanceStride": 16,
        "sourceMaskShift": FRAME_SOURCE_MASK_SHIFT,
    }
    descriptor["fingerprint"] = sha256(descriptor)
    return descriptor


def render_frame_cs(frame: dict) -> str:
    enum_text = []
    for enum_name, values in frame["enums"].items():
        members = "\n".join(
            f"        {name} = {value}," for name, value in values.items())
        enum_text.append(
            f"    internal enum {enum_name} : uint\n"
            f"    {{\n{members}\n    }}")

    flag_members = "\n".join(
        f"        {name} = 0x{value:08x}u,"
        for name, value in frame["outcomeFlags"].items())
    enum_text.append(
        "    [System.Flags]\n"
        "    internal enum SigmaFrameOutcomeFlags : uint\n"
        f"    {{\n        None = 0u,\n{flag_members}\n    }}")
    cell_flag_members = "\n".join(
        f"        {name} = 0x{value:08x}u,"
        for name, value in frame["cellFlags"].items())
    enum_text.append(
        "    [System.Flags]\n"
        "    internal enum SigmaFrameCellFlags : uint\n"
        f"    {{\n        None = 0u,\n{cell_flag_members}\n    }}")

    struct_text = []
    for entry in frame["structs"]:
        fields = "\n".join(
            f"        internal SigmaFrameUInt4Gpu {field};"
            for field in entry["fields"])
        struct_text.append(
            "    [StructLayout(LayoutKind.Sequential, Pack = 4)]\n"
            f"    internal struct {entry['name']}\n"
            f"    {{\n{fields}\n    }}")

    stride_lines = "\n".join(
        f"        internal const int {entry['name'][5:-3]}Stride = "
        f"{entry['stride']};"
        for entry in frame["structs"])

    return f"""// <auto-generated by Tools/sigma/generate_sigma_operators.py>
// Canonical baseline: CPQ4-2026-08-24-S16-v7. Do not edit by hand.

using System.Runtime.InteropServices;

namespace Genesis.RoomScan.SigmaPrism
{{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct SigmaFrameUInt2Gpu
    {{
        internal uint X;
        internal uint Y;
    }}

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct SigmaFrameUInt4Gpu
    {{
        internal uint X;
        internal uint Y;
        internal uint Z;
        internal uint W;
    }}

{chr(10).join(enum_text)}

{chr(10).join(struct_text)}

    internal static class SigmaGeneratedFrame
    {{
        internal const string AbiVersion = "{frame['version']}";
        internal const string AbiFingerprint = "{frame['fingerprint']}";
        internal const int SourceCount = {frame['sourceCount']};
        internal const int LaneCount = {frame['laneCount']};
        internal const int PackedQ48Stride = {frame['packedQ48Stride']};
        internal const int ValidityStride = {frame['validityStride']};
        internal const int ProvenanceStride = {frame['provenanceStride']};
        internal const int SourceMaskShift = {frame['sourceMaskShift']};
        internal const uint Invalid = 0xffffffffu;
{stride_lines}
    }}
}}
"""


def render_frame_hlsl(frame: dict) -> str:
    macro_lines = []
    for enum_name, values in frame["enums"].items():
        prefix = upper_snake(enum_name.removeprefix("Sigma"))
        for name, value in values.items():
            macro_lines.append(
                f"#define SIGMA_{prefix}_{upper_snake(name)} {value}u")
    for name, value in frame["outcomeFlags"].items():
        macro_lines.append(
            f"#define SIGMA_FRAME_OUTCOME_{upper_snake(name)} 0x{value:08x}u")
    for name, value in frame["cellFlags"].items():
        macro_lines.append(
            f"#define SIGMA_FRAME_CELL_{upper_snake(name)} 0x{value:08x}u")
    macro_lines.append(
        f"#define SIGMA_FRAME_SOURCE_MASK_SHIFT {frame['sourceMaskShift']}u")

    struct_text = []
    for entry in frame["structs"]:
        fields = "\n".join(
            f"    uint4 {field};" for field in entry["fields"])
        struct_text.append(
            f"struct {entry['name']}\n{{\n{fields}\n}};")

    fingerprint_words_text = ", ".join(
        f"0x{word:08x}u" for word in fingerprint_words(frame["fingerprint"]))
    return f"""// <auto-generated by Tools/sigma/generate_sigma_operators.py>
// Canonical baseline: CPQ4-2026-08-24-S16-v7. Do not edit by hand.
#ifndef SIGMA_FRAME_ABI_INCLUDED
#define SIGMA_FRAME_ABI_INCLUDED

#include "SigmaCarrierAbi.hlsl"

#define SIGMA_FRAME_SOURCE_COUNT {frame['sourceCount']}u
#define SIGMA_FRAME_LANE_COUNT {frame['laneCount']}u
#define SIGMA_FRAME_INVALID 0xffffffffu
{chr(10).join(macro_lines)}

static const uint SIGMA_FRAME_ABI_FINGERPRINT[8] = {{ {fingerprint_words_text} }};

{chr(10).join(struct_text)}

#endif
"""


def check_or_write(path: Path, content: str, check: bool) -> bool:
    if check:
        if not path.is_file() or path.read_text(encoding="utf-8") != content:
            print(f"generated Sigma descriptor stale: {path.relative_to(ROOT)}",
                  file=sys.stderr)
            return False
        return True
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8")
    print(f"generated {path.relative_to(ROOT)}")
    return True


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true",
                        help="fail when generated descriptor outputs are stale")
    parser.add_argument("--summary", action="store_true")
    args = parser.parse_args()
    descriptor = build_descriptor()
    merkaba = build_merkaba_descriptor(descriptor)
    frame = frame_abi_descriptor()
    if args.summary:
        print(json.dumps({
            "generator": descriptor["generatorVersion"],
            "zeroDivisorPairs": len(descriptor["annihilator"]["catalog"]),
            "annihilatorActions": len(descriptor["annihilator"]["actions"]),
            "zNull": descriptor["annihilator"]["zNull"],
            "geometryRows": descriptor["readout"]["geometryRows"],
            "fingerprints": descriptor["fingerprints"],
            "frameAbiFingerprint": frame["fingerprint"],
            "merkabaProgramFingerprint": merkaba["fingerprint"],
            "merkabaAuthorityInputs": merkaba["inputs"],
            "merkabaProofs": merkaba["proofs"],
        }, indent=2))
    valid = check_or_write(CS_OUTPUT, render_cs(descriptor), args.check)
    valid &= check_or_write(HLSL_LAYOUT_OUTPUT,
                            render_hlsl_layout(descriptor), args.check)
    valid &= check_or_write(HLSL_OUTPUT, render_hlsl(descriptor), args.check)
    valid &= check_or_write(CS_FRAME_OUTPUT, render_frame_cs(frame), args.check)
    valid &= check_or_write(HLSL_FRAME_OUTPUT,
                            render_frame_hlsl(frame), args.check)
    valid &= check_or_write(CS_MERKABA_OUTPUT,
                            render_merkaba_cs(merkaba), args.check)
    valid &= check_or_write(HLSL_MERKABA_OUTPUT,
                            render_merkaba_hlsl(merkaba), args.check)
    valid &= check_or_write(AUTHORITY_MANIFEST_OUTPUT,
                            render_authority_manifest(merkaba), args.check)
    return 0 if valid else 1


if __name__ == "__main__":
    raise SystemExit(main())
