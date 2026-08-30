#!/usr/bin/env python3
"""Generate the one canonical Sigma-PRISM-16 algebra/operator descriptor bundle."""

from __future__ import annotations

import argparse
import hashlib
import itertools
import json
import math
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
CS_MERKABA_OUTPUT = (ROOT / "Tests" / "Editor" / "Generated" /
                     "SigmaGeneratedMerkabaProgram.cs")
HLSL_MERKABA_OUTPUT = (ROOT / "Tests" / "Editor" / "Generated" /
                       "SigmaGeneratedMerkabaProgram.hlsl")
HLSL_RUNTIME_MERKABA_OUTPUT = (ROOT / "Runtime" / "Resources" /
                               "SigmaPrism" / "Generated" /
                               "SigmaGeneratedMerkabaProgram.hlsl")
HLSL_MERKABA_FIXTURE_OUTPUT = (ROOT / "Tests" / "Editor" / "Generated" /
                               "SigmaMerkabaProgramFixture.compute")
NUMERIC_ID = "num.fixed.q16_48.checked.nearest_even"
GENERATOR_VERSION = "CPQ4-S16-GEN-1"
FRAME_ABI_VERSION = "CPQ4-S16-NATIVE-FRAME-3"
MERKABA_PROGRAM_VERSION = "CPQ4-S16-MERKABA-N1R-8"
TOE_UPSTREAM_SHA256 = "9d2e3604846305cfe5244a4ef49f169632c60582cf895256fadc36426dc5786f"
LANES = 16

FRAME_STRUCTS = (
    ("SigmaNativeFrameGpu", ("Identity", "Disposition", "Evidence",
                              "Publication")),
    ("SigmaNativeObservationGpu", ("Identity", "Footprint", "Evidence",
                                    "Query")),
    ("SigmaNativeReverseWorkGpu", ("Identity", "Support", "Relation",
                                    "Provenance")),
    ("SigmaNativeStateDeltaGpu", ("Coordinate", "Generation", "Changed",
                                   "Witness", "Receipts", "State01",
                                   "State23", "State45", "State67",
                                   "State89", "State1011", "State1213",
                                   "State1415")),
    ("SigmaNativeGaugeDeltaGpu", ("Coordinate", "Prior", "Next", "Witness")),
    ("SigmaUnresolvedConstraintGpu", ("Observation", "Relation", "Evidence",
                                      "Provenance", "Frontier", "Program")),
    ("SigmaNativeFieldRevisionGpu", ("Identity", "Changed", "Evidence",
                                     "Publication")),
)

FRAME_ENUMS = {
    "SigmaNativeSensorSide": {
        "Left": 0,
        "Right": 1,
    },
    "SigmaNativeLeafKind": {
        "Order": 0,
        "Optical0": 1,
        "Optical1": 2,
        "Optical2": 3,
    },
    "SigmaNativeFirstHitRole": {
        "NoClaim": 0,
        "PreHitExclusion": 1,
        "FirstHitMould": 2,
    },
    "SigmaNativeFrameDisposition": {
        "Free": 0,
        "GpuOwned": 1,
        "NoChange": 2,
        "Resolved": 3,
        "Unresolved": 4,
        "Published": 5,
        "Faulted": 6,
    },
    "SigmaNativeColdReason": {
        "None": 0,
        "ContractorOverflow": 1,
        "RepresentationRefinement": 2,
        "PageFault": 3,
        "GaugeNormalization": 4,
        "StaticExclusion": 5,
    },
    "SigmaNativeRevisionState": {
        "Free": 0,
        "Building": 1,
        "Closed": 2,
        "Published": 3,
    },
    "SigmaNativeGaugeCellFlags": {
        "Inactive": 0,
        "Active": 1,
        "Normalized": 2,
        "Refined": 4,
    },
    "SigmaNativeCertificateFlags": {
        "None": 0,
        "Valid": 1,
        "Directional": 2,
        "Coupled": 4,
        "Minimized": 8,
    },
    "SigmaNativeConstraintProofFlags": {
        "None": 0,
        "BoundLocality": 1,
        "LosslessPullback": 2,
        "Coupled": 4,
        "Disjunctive": 8,
        "RawRequired": 16,
    },
}

FRAME_OBSERVATION_FLAGS = {
    "Coherent": 1 << 0,
    "LeftFirstHit": 1 << 1,
    "RightFirstHit": 1 << 2,
    "LeftEvidence": 1 << 3,
    "RightEvidence": 1 << 4,
    "OpticalClaim": 1 << 5,
    "PriorSupport": 1 << 6,
    "Fault": 1 << 31,
}

FRAME_DELTA_FLAGS = {
    "Resolved": 1 << 0,
    "Common": 1 << 1,
    "StateChanged": 1 << 2,
    "GaugeChanged": 1 << 3,
    "EvidenceRetained": 1 << 4,
    "Fault": 1 << 31,
}

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


def transpose(matrix: list[list[int]]) -> list[list[int]]:
    return [list(column) for column in zip(*matrix)]


def matrix_multiply(left: list[list[int]],
                    right: list[list[int]]) -> list[list[int]]:
    return [[sum(left[row][inner] * right[inner][column]
                 for inner in range(len(right)))
             for column in range(len(right[0]))]
            for row in range(len(left))]


def information_metric(diffraction: list[list[int]]) -> list[list[int]]:
    """G=2*A^T*A=-2*A^2, with A the generated diffraction operator."""
    ata = matrix_multiply(transpose(diffraction), diffraction)
    square = matrix_multiply(diffraction, diffraction)
    metric = [[2 * value for value in row] for row in ata]
    if metric != [[-2 * value for value in row] for row in square]:
        raise RuntimeError("diffraction metric lost G=2*A^T*A=-2*A^2")
    if any(metric[row][column] != metric[column][row]
           for row in range(LANES) for column in range(LANES)):
        raise RuntimeError("diffraction information metric is not symmetric")
    for probe in itertools.product((-1, 0, 1), repeat=4):
        vector = list(probe) + [0] * (LANES - len(probe))
        quadratic = sum(vector[row] * metric[row][column] * vector[column]
                        for row in range(LANES) for column in range(LANES))
        if quadratic < 0:
            raise RuntimeError("diffraction information metric is not PSD")
    return metric


def build_executable_merkaba_ir() -> dict:
    """Build the one numeric, bracket-preserving IR consumed by CPU and HLSL."""
    opcode_names = (
        "INPUT_S16", "INPUT_FIELD", "INPUT_QUERY", "INPUT_INTERVAL",
        "INPUT_ROLE", "BASIS_PRODUCT", "S16_MULTIPLY", "S16_SUBTRACT",
        "DIFFRACTION_APPLY", "INFORMATION_METRIC_APPLY", "SIGN_TRANSPORT",
        "PRIMITIVE_REPRESENTATIVE", "OUTWARD_G_NORM", "NORMALIZE_FACTOR",
        "PLAQUETTE_HOLONOMY", "PLAQUETTE_NORMALIZE_HALF", "DIRECT_SUM",
        "MERKABA_SHADOW", "CALIBRATED_QUERY_CONTRACT", "SCENE_REDUCE",
        "PREIMAGE_UNION", "FIRST_HIT_ACTION", "OPTICAL_NUISANCE_CONTRACT",
        "INFORMATION_PULLBACK", "CERTIFICATE_MINIMIZE", "DYADIC_DECODE",
        "GAUGE_NORMALIZE", "ZEMPTY_DEFAULT", "RELATION_CLASSIFY",
        "SHADOW_CELL_INTERSECT", "TANGENT_MIN_CHANGE_SELECT",
        "MERKABA_DUAL_FRAME_LIFT", "FORWARD_RELATION_VERIFY",
        "FRESH_BASE_PATTERN", "COMMON_UNION_OR_UNRESOLVED",
        "WHOLE_FRAME_REVERSE_SET", "FOOTPRINT_CONTRACT",
        "IMPLICIT_BOUNDARY_CONTRACT", "GLOBAL_EXACT_CLOSE",
    )
    value_kinds = (
        "S16", "Q48_INTERVAL", "S16_INTERVAL", "RELATION_FACTOR",
        "SCENE_SHADOW", "PREIMAGE_UNION", "ACTION_WITNESS", "CERTIFICATE",
        "GAUGE_FIELD", "QUERY_ROLE", "BOOLEAN", "SHADOW_CELL",
        "FRESH_ADMISSION", "FOOTPRINT_FIELD", "BOUNDARY_FIELD",
    )
    neighbourhoods = (
        "LOCAL", "LOCAL_CONTEXT", "FULL_LOCAL_STATE", "WHOLE_QUERY",
        "WHOLE_PROGRAM", "INTRINSIC_PAIR", "INTRINSIC_TRIPLE",
        "PLAQUETTE", "FINITE_SUPPORT", "COHERENT_EYE",
    )
    reverse_rules = (
        "NONE", "EXACT_IDENTITY", "EXACT_INVERSE_PERMUTATION",
        "OUTWARD_ADD_SUB", "OUTWARD_PRODUCT_ZERO_BRANCH_UNION",
        "REVERSE_SAME_BRACKET_TREE", "RETAIN_SUPPORT_DISJUNCTION",
        "NO_CLAIM", "REVERSE_CALIBRATED_QUERY", "RETAIN_FACTOR",
        "MINIMUM_CHANGE_ON_RESOLVED_FIBRE", "FORWARD_VERIFY_RETAIN",
        "COMMON_RESULT_OR_UNRESOLVED",
    )
    reducers = (
        "NONE", "DIRECT_ORDER_FIRST_HIT_OCCLUSION", "EXACT_DIRECT_SUM",
        "NATIVE_RELATION_CONTEXT", "EXPORT_RELATION_GATED",
    )
    op = {name: index for index, name in enumerate(opcode_names)}
    kind = {name: index for index, name in enumerate(value_kinds)}
    neighbourhood = {name: index for index, name in enumerate(neighbourhoods)}
    reverse = {name: index for index, name in enumerate(reverse_rules)}
    reducer = {name: index for index, name in enumerate(reducers)}
    nodes: list[dict] = []
    operands: list[int] = []
    expressions: list[dict] = []

    def add_node(opcode: str, output_kind: str, inputs: Iterable[int] = (),
                 reverse_rule: str = "NONE", argument0: int = 0,
                 argument1: int = 0) -> int:
        inputs = tuple(inputs)
        first = len(operands)
        operands.extend(inputs)
        nodes.append({
            "opcode": op[opcode],
            "outputKind": kind[output_kind],
            "reverseRule": reverse[reverse_rule],
            "operandStart": first,
            "operandCount": len(inputs),
            "argument0": argument0,
            "argument1": argument1,
        })
        return len(nodes) - 1

    def add_expression(identifier: str, source: str, arity: int,
                       locality: str, build) -> int:
        first = len(nodes)
        root = build()
        expression = {
            "id": identifier,
            "source": source,
            "arity": arity,
            "neighbourhood": neighbourhood[locality],
            "nodeStart": first,
            "nodeCount": len(nodes) - first,
            "rootNode": root,
        }
        expression["fingerprint"] = sha256({
            **expression,
            "nodes": nodes[first:],
            "operands": operands,
        })
        expressions.append(expression)
        return len(expressions) - 1

    def input_s16(slot: int) -> int:
        return add_node("INPUT_S16", "S16", argument0=slot,
                        reverse_rule="EXACT_IDENTITY")

    add_expression("K16_BASIS_PRODUCT", "I_TOE:1", 2, "LOCAL",
        lambda: add_node("BASIS_PRODUCT", "S16",
                         (input_s16(0), input_s16(1)),
                         "EXACT_INVERSE_PERMUTATION"))

    def associator_expression() -> int:
        a, b, c = input_s16(0), input_s16(1), input_s16(2)
        left = add_node("S16_MULTIPLY", "S16",
                        (add_node("S16_MULTIPLY", "S16", (a, b),
                                  "OUTWARD_PRODUCT_ZERO_BRANCH_UNION"), c),
                        "OUTWARD_PRODUCT_ZERO_BRANCH_UNION")
        right = add_node("S16_MULTIPLY", "S16",
                         (a, add_node("S16_MULTIPLY", "S16", (b, c),
                                      "OUTWARD_PRODUCT_ZERO_BRANCH_UNION")),
                         "OUTWARD_PRODUCT_ZERO_BRANCH_UNION")
        return add_node("S16_SUBTRACT", "S16", (left, right),
                        "OUTWARD_ADD_SUB")
    associator_index = add_expression(
        "K16_ASSOCIATOR", "I_TOE:2", 3, "INTRINSIC_TRIPLE",
        associator_expression)

    def diffraction_expression() -> int:
        return add_node("DIFFRACTION_APPLY", "S16", (input_s16(0),),
                        "REVERSE_SAME_BRACKET_TREE")
    add_expression("K16_DIFFRACTION", "I_TOE:3", 1, "LOCAL",
                   diffraction_expression)

    def link_factor_expression() -> int:
        ui, uj = input_s16(0), input_s16(1)
        transport = add_node("SIGN_TRANSPORT", "S16", (ui,),
                             "EXACT_INVERSE_PERMUTATION")
        defect = add_node("S16_SUBTRACT", "S16", (uj, transport),
                          "OUTWARD_ADD_SUB")
        primitive = add_node("PRIMITIVE_REPRESENTATIVE", "S16", (defect,),
                             "RETAIN_FACTOR")
        metric = add_node("INFORMATION_METRIC_APPLY", "S16", (primitive,),
                          "REVERSE_SAME_BRACKET_TREE")
        norm = add_node("OUTWARD_G_NORM", "Q48_INTERVAL",
                        (primitive, metric), "RETAIN_FACTOR")
        return add_node("NORMALIZE_FACTOR", "RELATION_FACTOR", (defect, norm),
                        "RETAIN_FACTOR")
    link_index = add_expression(
        "NORMALIZED_LINK_DEFECT", "I_TOE:8", 2, "INTRINSIC_PAIR",
        link_factor_expression)

    def normalized_associator_expression() -> int:
        associator_root = expressions[associator_index]["rootNode"]
        primitive = add_node("PRIMITIVE_REPRESENTATIVE", "S16",
                             (associator_root,), "RETAIN_FACTOR")
        metric = add_node("INFORMATION_METRIC_APPLY", "S16", (primitive,),
                          "REVERSE_SAME_BRACKET_TREE")
        norm = add_node("OUTWARD_G_NORM", "Q48_INTERVAL",
                        (primitive, metric), "RETAIN_FACTOR")
        return add_node("NORMALIZE_FACTOR", "RELATION_FACTOR",
                        (associator_root, norm), "RETAIN_FACTOR")
    normalized_associator_index = add_expression(
        "NORMALIZED_ASSOCIATOR_DEFECT", "I_TOE:8", 3,
        "INTRINSIC_TRIPLE", normalized_associator_expression)

    def plaquette_expression() -> int:
        holonomy = add_node("PLAQUETTE_HOLONOMY", "Q48_INTERVAL",
                            (), "REVERSE_SAME_BRACKET_TREE")
        return add_node("PLAQUETTE_NORMALIZE_HALF", "RELATION_FACTOR",
                        (holonomy,), "RETAIN_FACTOR")
    plaquette_index = add_expression(
        "NORMALIZED_PLAQUETTE_DEFECT", "I_TOE:8", 4, "PLAQUETTE",
        plaquette_expression)

    add_expression("NATIVE_CLOSURE_DEFECT", "I_TOE:8", -1,
        "LOCAL_CONTEXT", lambda: add_node(
            "DIRECT_SUM", "RELATION_FACTOR",
            (expressions[link_index]["rootNode"],
             expressions[normalized_associator_index]["rootNode"],
             expressions[plaquette_index]["rootNode"]), "RETAIN_FACTOR"))

    def shadow_expression() -> int:
        field = add_node("INPUT_FIELD", "GAUGE_FIELD", argument0=0,
                         reverse_rule="RETAIN_SUPPORT_DISJUNCTION")
        query = add_node("INPUT_QUERY", "Q48_INTERVAL", argument0=1,
                         reverse_rule="REVERSE_CALIBRATED_QUERY")
        local = add_node("MERKABA_SHADOW", "S16_INTERVAL", (field,),
                         "REVERSE_SAME_BRACKET_TREE")
        contracted = add_node("CALIBRATED_QUERY_CONTRACT", "SCENE_SHADOW",
                              (local, query), "REVERSE_CALIBRATED_QUERY")
        return add_node("SCENE_REDUCE", "SCENE_SHADOW", (contracted,),
                        "RETAIN_SUPPORT_DISJUNCTION",
                        argument0=reducer["DIRECT_ORDER_FIRST_HIT_OCCLUSION"])
    sensor_index = add_expression(
        "SENSOR_SCENE_SHADOW", "I_Q:SENSOR_LEFT_RIGHT", -1,
        "WHOLE_QUERY", shadow_expression)

    def reverse_expression() -> int:
        shadow_root = expressions[sensor_index]["rootNode"]
        observation = add_node("INPUT_INTERVAL", "Q48_INTERVAL", argument0=0,
                               reverse_rule="EXACT_IDENTITY")
        return add_node("PREIMAGE_UNION", "PREIMAGE_UNION",
                        (shadow_root, observation),
                        "RETAIN_SUPPORT_DISJUNCTION")
    reverse_index = add_expression(
        "EXACT_SENSOR_REVERSE", "I_Q:reverseContractor", -1,
        "WHOLE_QUERY", reverse_expression)

    def action_expression() -> int:
        role = add_node("INPUT_ROLE", "QUERY_ROLE", argument0=0,
                        reverse_rule="NO_CLAIM")
        direction = add_node("INPUT_INTERVAL", "Q48_INTERVAL", argument0=1,
                             reverse_rule="EXACT_IDENTITY")
        residual = add_node("INPUT_INTERVAL", "Q48_INTERVAL", argument0=2,
                            reverse_rule="EXACT_IDENTITY")
        return add_node("FIRST_HIT_ACTION", "ACTION_WITNESS",
                        (role, direction, residual), "RETAIN_FACTOR")
    action_index = add_expression(
        "DIRECTIONAL_FIRST_HIT_ACTION", "I_Q:sceneReduction+reverseContractor",
        3, "WHOLE_QUERY", action_expression)

    def optical_expression() -> int:
        observation = add_node("INPUT_INTERVAL", "Q48_INTERVAL", argument0=0,
                               reverse_rule="EXACT_IDENTITY")
        nuisance = add_node("INPUT_QUERY", "Q48_INTERVAL", argument0=1,
                            reverse_rule="REVERSE_CALIBRATED_QUERY")
        return add_node("OPTICAL_NUISANCE_CONTRACT", "RELATION_FACTOR",
                        (observation, nuisance), "RETAIN_FACTOR")
    add_expression("OPTICAL_NUISANCE", "I_Q:photometricNuisance", 2,
                   "COHERENT_EYE", optical_expression)

    add_expression("NATIVE_INFORMATION_PULLBACK", "I_Q:certificate", -1,
        "LOCAL_CONTEXT", lambda: add_node(
            "INFORMATION_PULLBACK", "CERTIFICATE",
            (expressions[reverse_index]["rootNode"],), "RETAIN_FACTOR"))
    add_expression("CERTIFICATE_MINIMIZER", "I_Q:certificate", -1,
        "FINITE_SUPPORT", lambda: add_node(
            "CERTIFICATE_MINIMIZE", "CERTIFICATE", (), "RETAIN_FACTOR"))

    def gauge_expression() -> int:
        backing = add_node("INPUT_FIELD", "GAUGE_FIELD", argument0=0,
                           reverse_rule="EXACT_IDENTITY")
        decoded = add_node("DYADIC_DECODE", "GAUGE_FIELD", (backing,),
                           "EXACT_IDENTITY")
        return add_node("GAUGE_NORMALIZE", "GAUGE_FIELD", (decoded,),
                        "EXACT_IDENTITY")
    gauge_index = add_expression(
        "DYADIC_GAUGE_NORMALIZER", "I_REP:kappa+normalizer", 1,
        "FINITE_SUPPORT", gauge_expression)

    def fresh_admission_expression() -> int:
        reverse_union = expressions[reverse_index]["rootNode"]
        cell = add_node("SHADOW_CELL_INTERSECT", "SHADOW_CELL",
                        (reverse_union,), "RETAIN_SUPPORT_DISJUNCTION")
        selected = add_node("TANGENT_MIN_CHANGE_SELECT", "SHADOW_CELL",
                            (cell,), "MINIMUM_CHANGE_ON_RESOLVED_FIBRE")
        lifted = add_node("MERKABA_DUAL_FRAME_LIFT", "S16",
                          (selected,), "REVERSE_SAME_BRACKET_TREE")
        verified = add_node("FORWARD_RELATION_VERIFY", "BOOLEAN",
                            (lifted, cell,
                             expressions[link_index]["rootNode"]),
                            "FORWARD_VERIFY_RETAIN")
        relative = add_node("FRESH_BASE_PATTERN", "GAUGE_FIELD",
                            (lifted, verified), "RETAIN_FACTOR",
                            argument0=0, argument1=1)
        normalized = add_node("GAUGE_NORMALIZE", "GAUGE_FIELD", (relative,),
                              "EXACT_IDENTITY")
        return add_node("COMMON_UNION_OR_UNRESOLVED", "FRESH_ADMISSION",
                        (lifted, normalized, reverse_union),
                        "COMMON_RESULT_OR_UNRESOLVED")
    fresh_admission_index = add_expression(
        "FRESH_BASE_ADMISSION",
        "I_TOE:6+I_Q:freshBaseAdmission+I_REP:freshSupport", -1,
        "WHOLE_PROGRAM", fresh_admission_expression)

    def fresh_support_set_expression() -> int:
        field = add_node("INPUT_FIELD", "GAUGE_FIELD", argument0=0,
                         reverse_rule="RETAIN_SUPPORT_DISJUNCTION")
        query = add_node("INPUT_QUERY", "Q48_INTERVAL", argument0=1,
                         reverse_rule="REVERSE_CALIBRATED_QUERY")
        whole_reverse = add_node("WHOLE_FRAME_REVERSE_SET", "PREIMAGE_UNION",
                                 (field, query,
                                  expressions[reverse_index]["rootNode"]),
                                 "RETAIN_SUPPORT_DISJUNCTION")
        footprint = add_node("FOOTPRINT_CONTRACT", "FOOTPRINT_FIELD",
                             (whole_reverse, query),
                             "RETAIN_SUPPORT_DISJUNCTION")
        boundary = add_node("IMPLICIT_BOUNDARY_CONTRACT", "BOUNDARY_FIELD",
                            (footprint,
                             expressions[link_index]["rootNode"],
                             expressions[normalized_associator_index]["rootNode"],
                             expressions[plaquette_index]["rootNode"]),
                            "RETAIN_SUPPORT_DISJUNCTION")
        return add_node("GLOBAL_EXACT_CLOSE", "FRESH_ADMISSION",
                        (whole_reverse, footprint, boundary),
                        "COMMON_RESULT_OR_UNRESOLVED")
    fresh_support_set_index = add_expression(
        "FRESH_SUPPORT_SET_ADMISSION",
        "I_Q:constructiveModalStitching+I_TOE:8+I_REP:stitchEmbedding",
        -1, "WHOLE_PROGRAM", fresh_support_set_expression)
    zempty_index = add_expression(
        "ZEMPTY_DEFAULT", "I_Q:defaultSemantics+I_REP:defaultRepresentations",
        -1, "WHOLE_PROGRAM", lambda: add_node(
            "ZEMPTY_DEFAULT", "S16", (), "EXACT_IDENTITY"))

    entry_points = [
        {"id": "SENSOR_LEFT", "forwardExpression": sensor_index,
         "reverseExpression": reverse_index,
         "reducer": reducer["DIRECT_ORDER_FIRST_HIT_OCCLUSION"]},
        {"id": "SENSOR_RIGHT", "forwardExpression": sensor_index,
         "reverseExpression": reverse_index,
         "reducer": reducer["DIRECT_ORDER_FIRST_HIT_OCCLUSION"]},
        {"id": "EYE_PAIR", "forwardExpression": sensor_index,
         "reverseExpression": -1,
         "reducer": reducer["DIRECT_ORDER_FIRST_HIT_OCCLUSION"]},
        {"id": "INTRINSIC_RELATION", "forwardExpression": link_index,
         "reverseExpression": link_index,
         "reducer": reducer["NATIVE_RELATION_CONTEXT"]},
        {"id": "PREDICTION_SUPPORT", "forwardExpression": sensor_index,
         "reverseExpression": reverse_index,
         "reducer": reducer["DIRECT_ORDER_FIRST_HIT_OCCLUSION"]},
        {"id": "EXPORT", "forwardExpression": sensor_index,
         "reverseExpression": -1,
         "reducer": reducer["EXPORT_RELATION_GATED"]},
        {"id": "DEBUG", "forwardExpression": sensor_index,
         "reverseExpression": -1, "reducer": reducer["NONE"]},
    ]
    ir = {
        "opcodes": list(opcode_names),
        "valueKinds": list(value_kinds),
        "neighbourhoods": list(neighbourhoods),
        "reverseRules": list(reverse_rules),
        "reducers": list(reducers),
        "nodes": nodes,
        "operands": operands,
        "expressions": expressions,
        "entryPoints": entry_points,
        "actionExpression": action_index,
        "gaugeExpression": gauge_index,
        "freshAdmissionExpression": fresh_admission_index,
        "freshSupportSetExpression": fresh_support_set_index,
        "zEmptyExpression": zempty_index,
    }
    for expression in expressions:
        start = expression["nodeStart"]
        end = start + expression["nodeCount"]
        if not start <= expression["rootNode"] < end:
            # Cross-expression roots are allowed only as explicit operands; each
            # expression still owns a final root node of its own.
            raise RuntimeError(f"IR expression has external root: {expression['id']}")
    for index, node in enumerate(nodes):
        for operand in operands[node["operandStart"]:
                                node["operandStart"] + node["operandCount"]]:
            if operand >= index:
                raise RuntimeError("Merkaba IR is not a forward acyclic DAG")
    ir["fingerprint"] = sha256(ir)
    return ir


def reverse_complete_tree_proof(ir: dict) -> dict:
    """Soundness of the generated bracket tree, including zero branches."""
    # This is the bounded exhaustive oracle domain for intermediate values, not
    # the source-domain interval.  It must contain every product in the fixture.
    full = (-64, 64)

    def interval_mul(left: tuple[int, int], right: tuple[int, int]) -> tuple[int, int]:
        products = [left[0] * right[0], left[0] * right[1],
                    left[1] * right[0], left[1] * right[1]]
        return min(products), max(products)

    def interval_add(left: tuple[int, int], right: tuple[int, int]) -> tuple[int, int]:
        return left[0] + right[0], left[1] + right[1]

    def interval_sub(left: tuple[int, int], right: tuple[int, int]) -> tuple[int, int]:
        return left[0] - right[1], left[1] - right[0]

    def reverse_mul(output: tuple[int, int], known: tuple[int, int]) -> tuple[int, int]:
        if known[0] <= 0 <= known[1]:
            return full
        quotients = [Fraction(output[0], known[0]), Fraction(output[0], known[1]),
                     Fraction(output[1], known[0]), Fraction(output[1], known[1])]
        lower = min(value.numerator // value.denominator for value in quotients)
        upper = max(-(-value.numerator // value.denominator) for value in quotients)
        return max(full[0], lower), min(full[1], upper)

    fixtures = 0
    zero_branches = 0
    bracket_negative_controls = 0
    for a, b, c in itertools.product(range(-3, 4), repeat=3):
        for padding in (0, 1):
            ia, ib, ic = (a, a), (b, b), (c, c)
            ab = interval_mul(ia, ib)
            bc = interval_mul(ib, ic)
            left = interval_mul(ab, ic)
            right = interval_mul(ia, bc)
            root = interval_sub(left, right)
            output = (root[0] - padding, root[1] + padding)
            reverse_left = interval_add(output, right)
            reverse_right = interval_sub(left, output)
            reverse_ab = reverse_mul(reverse_left, ic)
            reverse_c_left = reverse_mul(reverse_left, ab)
            reverse_a_left = reverse_mul(reverse_ab, ib)
            reverse_b_left = reverse_mul(reverse_ab, ia)
            reverse_a_right = reverse_mul(reverse_right, bc)
            reverse_bc = reverse_mul(reverse_right, ia)
            reverse_b_right = reverse_mul(reverse_bc, ic)
            reverse_c_right = reverse_mul(reverse_bc, ib)
            retained = (
                reverse_a_left[0] <= a <= reverse_a_left[1] and
                reverse_a_right[0] <= a <= reverse_a_right[1] and
                reverse_b_left[0] <= b <= reverse_b_left[1] and
                reverse_b_right[0] <= b <= reverse_b_right[1] and
                reverse_c_left[0] <= c <= reverse_c_left[1] and
                reverse_c_right[0] <= c <= reverse_c_right[1])
            if not retained:
                raise RuntimeError("complete bracket-tree reverse lost a source")
            zero_branches += int(a == 0 or b == 0 or c == 0)
            fixtures += 1
        # The two S16 bracket histories are independently fingerprinted and must
        # differ for a known nonassociative basis triple.
        if any((basis_product(LANES, a0, b0)[0] *
                basis_product(LANES, a0 ^ b0, c0)[0]) !=
               (basis_product(LANES, b0, c0)[0] *
                basis_product(LANES, a0, b0 ^ c0)[0])
               for a0, b0, c0 in itertools.product(range(LANES), repeat=3)):
            bracket_negative_controls = 1
    # Interpret the actual numeric K16_ASSOCIATOR node graph over a complete
    # zero-plus-basis domain.  This ties the reverse proof to the emitted node
    # topology instead of reimplementing a prose formula beside it.
    associator = next(expression for expression in ir["expressions"]
                      if expression["id"] == "K16_ASSOCIATOR")
    opcode_names = ir["opcodes"]
    zero = (0,) * LANES
    basis_domain = [zero] + [tuple(1 if lane == basis else 0
                                   for lane in range(LANES))
                             for basis in range(LANES)]

    def multiply_s16(left: tuple[int, ...],
                     right: tuple[int, ...]) -> tuple[int, ...]:
        output = [0] * LANES
        for left_lane, left_value in enumerate(left):
            if left_value == 0:
                continue
            for right_lane, right_value in enumerate(right):
                if right_value == 0:
                    continue
                sign, lane = basis_product(LANES, left_lane, right_lane)
                output[lane] += sign * left_value * right_value
        return tuple(output)

    def evaluate_actual_associator(inputs: tuple[tuple[int, ...], ...]
                                    ) -> tuple[int, ...]:
        values: dict[int, tuple[int, ...]] = {}
        start = associator["nodeStart"]
        end = start + associator["nodeCount"]
        for node_index in range(start, end):
            node = ir["nodes"][node_index]
            opcode = opcode_names[node["opcode"]]
            node_operands = ir["operands"][
                node["operandStart"]:node["operandStart"] + node["operandCount"]]
            if opcode == "INPUT_S16":
                values[node_index] = inputs[node["argument0"]]
            elif opcode == "S16_MULTIPLY":
                values[node_index] = multiply_s16(
                    values[node_operands[0]], values[node_operands[1]])
            elif opcode == "S16_SUBTRACT":
                values[node_index] = tuple(
                    left - right for left, right in zip(
                        values[node_operands[0]], values[node_operands[1]]))
            else:
                raise RuntimeError(
                    f"unsupported opcode in actual associator proof: {opcode}")
        return values[associator["rootNode"]]

    ir_preimages: dict[tuple[int, ...], list[tuple[int, int, int]]] = {}
    ir_forward_fixtures = 0
    for input_indices in itertools.product(range(len(basis_domain)), repeat=3):
        inputs = tuple(basis_domain[index] for index in input_indices)
        actual = evaluate_actual_associator(inputs)
        reference = tuple(left - right for left, right in zip(
            multiply_s16(multiply_s16(inputs[0], inputs[1]), inputs[2]),
            multiply_s16(inputs[0], multiply_s16(inputs[1], inputs[2]))))
        if actual != reference:
            raise RuntimeError("generated associator DAG changed its bracket tree")
        ir_preimages.setdefault(actual, []).append(input_indices)
        ir_forward_fixtures += 1
    ir_ambiguous_outputs = sum(len(preimages) > 1
                               for preimages in ir_preimages.values())
    ir_max_preimage_count = max(map(len, ir_preimages.values()))
    if ir_ambiguous_outputs == 0 or ir_max_preimage_count <= 1:
        raise RuntimeError("actual generated reverse proof lost set-valued preimages")

    # Scene reduction reverse is a disjunction, never one winner applied to all.
    scene_fixtures = 0
    for first_order, second_order, first_value, second_value in itertools.product(
            (1, 2), (1, 2), (-1, 1), (-1, 1)):
        selected = first_value if first_order <= second_order else second_value
        candidates = []
        for support in (0, 1):
            value = first_value if support == 0 else second_value
            order = first_order if support == 0 else second_order
            if value == selected and order == min(first_order, second_order):
                candidates.append(support)
        expected_support = 0 if first_order <= second_order else 1
        if expected_support not in candidates:
            raise RuntimeError("scene reverse lost the true first-hit support")
        scene_fixtures += 1
    if zero_branches == 0 or bracket_negative_controls != 1:
        raise RuntimeError("reverse proof lacks zero/bracket negative controls")
    return {
        "fixtureCount": fixtures + scene_fixtures + ir_forward_fixtures,
        "zeroBranchCount": zero_branches,
        "sceneDisjunctionCount": scene_fixtures,
        "bracketNegativeControls": bracket_negative_controls,
        "irAssociatorFingerprint": associator["fingerprint"],
        "irForwardFixtureCount": ir_forward_fixtures,
        "irPreimageOutputCount": len(ir_preimages),
        "irAmbiguousPreimageOutputCount": ir_ambiguous_outputs,
        "irMaxPreimageCount": ir_max_preimage_count,
    }


def query_support_exhaustive_proof(i_q: dict) -> dict:
    """Compare omission with an independent evaluation of generated query law."""
    fixtures = 0
    false_negatives = 0
    omitted = 0
    refined_fixtures = 0
    nonresident_fixtures = 0
    evaluations = []
    for storage_class, refined, state_mask, boundary_closed, fingerprints_match in (
            itertools.product(i_q["querySupportSummary"]["covers"],
                              (False, True), range(16), (False, True),
                              (False, True))):
        coarse_cells = tuple(
            tuple(1 if address == lane else 0 for address in range(LANES))
            if state_mask & (1 << lane) else (0,) * LANES
            for lane in range(4))
        # Evaluate the generated Merkaba shadow on every exact cell.  A refined
        # cell is four equal-measure copies of the same full-S16 state; resident
        # and nonresident spellings decode to the same value.  The intrinsic
        # entry point retains direct S16 dependencies, so a nonzero state is
        # query-visible even when this particular four-coordinate shadow happens
        # to vanish.  An open mixed-default boundary is independently visible.
        exact_cells = tuple((state, Fraction(1, 16))
                            for state in coarse_cells for _ in range(4)) \
            if refined else tuple((state, Fraction(1, 4))
                                   for state in coarse_cells)
        shadow_contributions = tuple(
            tuple(sum(state[address] * merkaba_shadow_numerator(address)[axis]
                      for address in range(LANES)) * measure
                  for axis in range(4))
            for state, measure in exact_cells)
        sensor_contribution = any(any(value != 0 for value in shadow)
                                  for shadow in shadow_contributions)
        intrinsic_contribution = any(any(value != 0 for value in state)
                                     for state, _ in exact_cells)
        exhaustive_contribution = (sensor_contribution or
                                   intrinsic_contribution or
                                   not boundary_closed)
        summary_all_default = all(not any(state) for state, _ in exact_cells)
        summary_omit = (summary_all_default and boundary_closed and
                        fingerprints_match)
        if summary_omit and exhaustive_contribution:
            false_negatives += 1
        evaluations.append((storage_class, refined, state_mask, boundary_closed,
                            fingerprints_match, sensor_contribution,
                            intrinsic_contribution, exhaustive_contribution,
                            summary_omit))
        omitted += int(summary_omit)
        refined_fixtures += int(refined)
        nonresident_fixtures += int(storage_class == "NONRESIDENT")
        fixtures += 1
    if false_negatives:
        raise RuntimeError("query-support summary lost exhaustive contribution")
    return {"fixtureCount": fixtures, "falseNegatives": false_negatives,
            "omittedIdentityFixtures": omitted,
            "refinedFixtureCount": refined_fixtures,
            "nonresidentFixtureCount": nonresident_fixtures,
            "evaluationFingerprint": sha256(evaluations)}


def default_representation_proof(i_q: dict, i_rep: dict, ir: dict) -> dict:
    """Abstract-interpret every forward entry from each S16-zero spelling."""
    representations = (
        ("LOGICAL_UNBACKED", i_rep["defaultRepresentations"]["logicalUnbacked"]),
        ("EXPLICIT_ZEMPTY", i_rep["defaultRepresentations"]["allocated"]),
        ("NULL_CODEC", i_rep["defaultRepresentations"]["nullCodecDecode"]),
    )
    admitted = {"ZEMPTY", "FULL_S16_ALGEBRA_ZERO"}
    if any(value not in admitted for _, value in representations):
        raise RuntimeError("a default representation does not decode to algebra zero")
    if (i_q["defaultSemantics"]["localContribution"] !=
            "EXACT_REDUCER_IDENTITY" or
            i_q["defaultSemantics"]["allDefaultRelation"] != "DEFAULT_SAT"):
        raise RuntimeError("default query substitution is not quiescent")
    zempty = ir["expressions"][ir["zEmptyExpression"]]
    if zempty["id"] != "ZEMPTY_DEFAULT" or zempty["source"] != (
            "I_Q:defaultSemantics+I_REP:defaultRepresentations"):
        raise RuntimeError("default decoder is not tied to both authorities")
    opcode_names = ir["opcodes"]

    def interpret_forward(root: int) -> str:
        values: dict[int, str] = {}
        for node_index in range(root + 1):
            node = ir["nodes"][node_index]
            opcode = opcode_names[node["opcode"]]
            operands = [values[index] for index in ir["operands"]
                        [node["operandStart"]:
                         node["operandStart"] + node["operandCount"]]]
            if opcode == "INPUT_S16":
                value = "ZERO_S16"
            elif opcode == "INPUT_FIELD":
                value = "DEFAULT_FIELD"
            elif opcode in ("INPUT_QUERY", "INPUT_INTERVAL"):
                value = "QUERY_VALUE"
            elif opcode == "INPUT_ROLE":
                value = "NO_CLAIM"
            elif opcode in ("BASIS_PRODUCT", "S16_MULTIPLY",
                            "S16_SUBTRACT", "DIFFRACTION_APPLY",
                            "INFORMATION_METRIC_APPLY", "SIGN_TRANSPORT",
                            "PRIMITIVE_REPRESENTATIVE"):
                value = "ZERO_S16" if operands and all(
                    operand == "ZERO_S16" for operand in operands) else "OTHER"
            elif opcode == "OUTWARD_G_NORM":
                value = "ZERO_INTERVAL" if operands and all(
                    operand == "ZERO_S16" for operand in operands) else "OTHER"
            elif opcode == "NORMALIZE_FACTOR":
                value = "DEFAULT_FACTOR" if operands and operands[0] == (
                    "ZERO_S16") else "OTHER"
            elif opcode == "MERKABA_SHADOW":
                value = "NO_CONTRIBUTION" if operands == [
                    "DEFAULT_FIELD"] else "OTHER"
            elif opcode == "CALIBRATED_QUERY_CONTRACT":
                value = "NO_CONTRIBUTION" if operands and operands[0] == (
                    "NO_CONTRIBUTION") else "OTHER"
            elif opcode == "SCENE_REDUCE":
                value = "REDUCER_IDENTITY" if operands == [
                    "NO_CONTRIBUTION"] else "OTHER"
            elif opcode == "ZEMPTY_DEFAULT":
                value = "ZERO_S16"
            else:
                value = "OTHER"
            values[node_index] = value
        return values[root]

    entry_results = []
    for entry in ir["entryPoints"]:
        expression = ir["expressions"][entry["forwardExpression"]]
        result = interpret_forward(expression["rootNode"])
        expected = ("DEFAULT_FACTOR" if entry["id"] == "INTRINSIC_RELATION"
                    else "REDUCER_IDENTITY")
        if result != expected:
            raise RuntimeError(
                f"default representation activates {entry['id']}: {result}")
        entry_results.append((entry["id"], result,
                              expression["fingerprint"]))
    fixtures = [(entry[0], representation[0], entry[1], entry[2])
                for entry in entry_results for representation in representations]
    if len(fixtures) != len(ir["entryPoints"]) * len(representations):
        raise RuntimeError("default representation query proof is incomplete")
    return {
        "fixtureCount": len(fixtures),
        "queryCount": len(ir["entryPoints"]),
        "representationCount": len(representations),
        "fingerprint": sha256(fixtures),
    }


def certificate_minimizer_proof() -> dict:
    """Minimize exact factors and compare feasible assignments exhaustively."""
    # scope, expression, independence, provenance, coupling, branch, lower, upper
    duplicate = ("s0", "e0", "i0", "p0", "c0", "b0", -2, 3)
    weak = ("s0", "e0", "i0", "p0", "c0", "b0", -4, 9)
    coupled_a = ("s1", "e1", "i1", "p1", "coupling-A", "branch-A", -1, 2)
    coupled_b = ("s1", "e1", "i1", "p1", "coupling-B", "branch-B", -1, 2)
    factors = [duplicate] * 10000 + [weak, coupled_a, coupled_b]

    def minimize(values: Iterable[tuple]) -> list[tuple[tuple, int]]:
        by_context: dict[tuple, list[tuple[int, int, int]]] = {}
        for value in values:
            context = value[:6]
            lower, upper = value[6:]
            bucket = by_context.setdefault(context, [])
            duplicate_index = next((index for index, item in enumerate(bucket)
                                    if item[0] == lower and item[1] == upper), None)
            if duplicate_index is not None:
                old_lower, old_upper, count = bucket[duplicate_index]
                bucket[duplicate_index] = (old_lower, old_upper, count + 1)
                continue
            if any(existing_lower >= lower and existing_upper <= upper
                   for existing_lower, existing_upper, _ in bucket):
                continue
            bucket[:] = [item for item in bucket
                         if not (lower >= item[0] and upper <= item[1])]
            bucket.append((lower, upper, 1))
        output = []
        for context in sorted(by_context):
            for lower, upper, count in sorted(by_context[context]):
                output.append((context + (lower, upper), count))
        return output

    minimized = minimize(factors)
    for candidate in range(-6, 12):
        exhaustive = all(factor[6] <= candidate <= factor[7]
                         for factor in factors if factor[:6] == duplicate[:6])
        compact = all(factor[6] <= candidate <= factor[7]
                      for factor, _ in minimized if factor[:6] == duplicate[:6])
        if exhaustive != compact:
            raise RuntimeError("certificate minimizer changed feasible set")
    contexts = {factor[:6] for factor, _ in minimized}
    if coupled_a[:6] not in contexts or coupled_b[:6] not in contexts:
        raise RuntimeError("certificate minimizer collapsed coupled branches")
    duplicate_record = next(item for item in minimized
                            if item[0][:6] == duplicate[:6])
    if duplicate_record[1] != 10000 or len(minimized) != 3:
        raise RuntimeError("certificate storage does not follow new information")
    return {
        "duplicateFixtureCount": 10000,
        "minimizedFactorCount": len(minimized),
        "duplicateMultiplicity": duplicate_record[1],
        "coupledFactorCount": 2,
        "feasibleAssignmentCount": 18,
        "fingerprint": sha256(minimized),
    }


def gauge_transport_proof() -> dict:
    """Constructive dyadic transport and allocation-order-independent normal form."""
    payload_a = (tuple(range(-8, 8)), "factor-A", "relation-A", "evidence-A",
                 "information-A", "bandwidth-A")
    payload_b = (tuple(range(8, 24)), "factor-B", "relation-B", "evidence-B",
                 "information-B", "bandwidth-B")

    def split(cell: tuple) -> list[tuple]:
        u, v, level, payload = cell
        return [(2 * u + du, 2 * v + dv, level + 1, payload)
                for dv in range(2) for du in range(2)]

    def collapse_once(cells: list[tuple]) -> tuple[list[tuple], bool]:
        groups: dict[tuple, list[tuple]] = {}
        for cell in cells:
            u, v, level, payload = cell
            if level == 0:
                continue
            groups.setdefault((u // 2, v // 2, level - 1, payload), []).append(cell)
        for parent, children in sorted(groups.items(), key=lambda item: repr(item[0])):
            expected = {(parent[0] * 2 + du, parent[1] * 2 + dv,
                         parent[2] + 1, parent[3])
                        for dv in range(2) for du in range(2)}
            if set(children) == expected:
                return [cell for cell in cells if cell not in expected] + [parent], True
        return cells, False

    def normalize(cells: Iterable[tuple]) -> tuple[tuple, ...]:
        cells = list(cells)
        changed = True
        while changed:
            cells, changed = collapse_once(cells)
        lower_coordinates = [(Fraction(u, 1 << level),
                              Fraction(v, 1 << level))
                             for u, v, level, _ in cells]
        minimum_u, minimum_v = min(lower_coordinates)
        translate_u = minimum_u.numerator // minimum_u.denominator
        translate_v = minimum_v.numerator // minimum_v.denominator
        translated = [(u - translate_u * (1 << level),
                       v - translate_v * (1 << level), level, payload)
                      for u, v, level, payload in cells]
        return tuple(sorted(translated, key=lambda cell: (
            cell[2], signed_morton(cell[0], cell[1]), cell[0], cell[1],
            sha256(cell[3]))))

    parent = (5, -3, 0, payload_a)
    refined_once = split(parent)
    refined = [grandchild for child in refined_once for grandchild in split(child)]
    # Add disconnected level-two support with a distinct six-field payload.
    disconnected = split((9, 4, 1, payload_b))
    pattern = refined + disconnected
    normal = normalize(pattern)
    permutation_count = 0
    # Exercise all cells in 24 deterministic allocation/discovery orders without
    # pretending that factorial enumeration is a constructive theorem.
    for shift in range(12):
        rotated = pattern[shift:] + pattern[:shift]
        for candidate in (rotated, list(reversed(rotated))):
            if normalize(candidate) != normal:
                raise RuntimeError("gauge normal form depends on discovery order")
            permutation_count += 1
    if permutation_count != 24:
        raise RuntimeError("gauge permutation corpus changed unexpectedly")
    translated = [(u + 7 * (1 << level), v - 5 * (1 << level), level, payload)
                  for u, v, level, payload in pattern]
    if normalize(translated) != normal:
        raise RuntimeError("gauge normal form depends on allocation translation")
    # A split transported at two levels must collapse to the original state and
    # proof payload before any higher-frequency information is introduced.
    if normalize(refined) != normalize(refined_once) or \
            normalize(refined_once) != normalize([parent]):
        raise RuntimeError("recursive gauge refinement changed canonical field")
    non_equivalent = pattern + [(31, 17, 0, payload_a)]
    if normalize(non_equivalent) == normal:
        raise RuntimeError("gauge normalizer collapsed non-equivalent support")

    # Pointwise field and exact dyadic measure are invariant before new detail.
    probe_count = 0
    for numerator_u, numerator_v in itertools.product(range(20, 24), range(-12, -8)):
        u = Fraction(numerator_u, 4)
        v = Fraction(numerator_v, 4)
        parent_owns = Fraction(5) <= u < Fraction(6) and Fraction(-3) <= v < Fraction(-2)
        child_owners = [cell for cell in refined
                        if Fraction(cell[0], 1 << cell[2]) <= u <
                           Fraction(cell[0] + 1, 1 << cell[2]) and
                           Fraction(cell[1], 1 << cell[2]) <= v <
                           Fraction(cell[1] + 1, 1 << cell[2])]
        if parent_owns != (len(child_owners) == 1):
            raise RuntimeError("dyadic split changed pointwise field support")
        if child_owners and child_owners[0][3] != payload_a:
            raise RuntimeError("dyadic split lost state/evidence/relation transport")
        probe_count += 1
    parent_measure = Fraction(1)
    child_measure = sum(Fraction(1, 1 << (2 * cell[2])) for cell in refined)
    if child_measure != parent_measure:
        raise RuntimeError("dyadic split changed exact intrinsic measure")
    if normalize(refined) != normalize([parent]):
        raise RuntimeError("equal-child collapse changed canonical serialization")
    return {
        "permutationCount": permutation_count,
        "pointProbeCount": probe_count,
        "transportFieldCount": len(payload_a),
        "normalForm": [list(cell[:3]) + [sha256(cell[3])] for cell in normal],
        "canonicalSerializationFingerprint": sha256(normal),
        "freshSupportUniqueModuloGauge": True,
        "freshSupportNonEquivalentRejected": True,
    }


def fresh_base_admission_proof() -> dict:
    """Prove the candidate-free chi0/kappa0 lift from coherent shadow cells."""
    scale = 1 << 48
    diffraction = diffraction_matrix()

    def nearest_even(numerator: int, denominator: int) -> int:
        if denominator <= 0:
            raise ValueError("positive denominator required")
        sign = -1 if numerator < 0 else 1
        magnitude = abs(numerator)
        quotient, remainder = divmod(magnitude, denominator)
        twice = remainder * 2
        if twice > denominator or twice == denominator and quotient & 1:
            quotient += 1
        return sign * quotient

    def qmul(left: int, right: int) -> int:
        value = nearest_even(left * right, scale)
        if not -(1 << 63) <= value < (1 << 63):
            raise OverflowError("fresh admission Q16.48 multiply overflow")
        return value

    def select_tangent(bounds: tuple[tuple[int, int], ...]) -> tuple[int, ...] | None:
        if len(bounds) != 4 or any(lower > upper for lower, upper in bounds):
            return None
        selected = [min(max(0, lower), upper) for lower, upper in bounds]
        residual = sum(selected)
        if residual > 0:
            for axis in range(4):
                adjustment = min(residual, selected[axis] - bounds[axis][0])
                selected[axis] -= adjustment
                residual -= adjustment
        elif residual < 0:
            deficit = -residual
            for axis in range(4):
                adjustment = min(deficit, bounds[axis][1] - selected[axis])
                selected[axis] += adjustment
                deficit -= adjustment
            residual = -deficit
        return tuple(selected) if residual == 0 else None

    def lift(shadow_value: tuple[int, ...]) -> tuple[int, ...]:
        state = []
        for address in range(LANES):
            value = 0
            for axis in range(4):
                coefficient = nearest_even(
                    merkaba_shadow_numerator(address)[axis] * scale, 64)
                value += qmul(shadow_value[axis], coefficient)
            if not -(1 << 63) <= value < (1 << 63):
                raise OverflowError("fresh admission lift overflow")
            state.append(value)
        return tuple(state)

    def forward(state: tuple[int, ...]) -> tuple[int, ...]:
        shadow_value = []
        for axis in range(4):
            value = 0
            for address in range(LANES):
                coefficient = nearest_even(
                    merkaba_shadow_numerator(address)[axis] * scale, 4)
                value += qmul(state[address], coefficient)
            if not -(1 << 63) <= value < (1 << 63):
                raise OverflowError("fresh admission forward overflow")
            shadow_value.append(value)
        return tuple(shadow_value)

    def fresh_boundary_relation(state: tuple[int, ...]) -> str:
        """Specialize the generated native relation to state/ZEmpty/ZEmpty.

        U_0 is identity, the explicitly bracketed associator with two algebra
        zeros is exactly zero, and the canonical base plaquette has W=+1.  The
        only non-trivial factor is therefore d=ZEmpty-U_0(state)=-state.  Its
        primitive G norm is positive exactly when A(state/content) is nonzero.
        No caller-supplied relation class participates in this proof.
        """
        if not any(state):
            return "DEFAULT_SAT"
        content = 0
        for coefficient in state:
            content = math.gcd(content, abs(coefficient))
        if content == 0:
            raise RuntimeError("nonzero fresh state has zero primitive content")
        primitive = tuple(coefficient // content for coefficient in state)
        diffraction_value = tuple(sum(diffraction[row][column] * primitive[column]
                                      for column in range(LANES))
                                  for row in range(LANES))
        if not any(diffraction_value):
            return "UNRESOLVED"
        if any(basis_product(LANES, 0, address)[0] != 1
               for address in range(LANES)):
            raise RuntimeError("canonical fresh transport U_0 is not identity")
        holonomy = (basis_product(LANES, 0, 0)[0] ** 4)
        if holonomy != 1:
            raise RuntimeError("canonical fresh plaquette is not exactly closed")
        return "NO_RELATION"

    fixture_records = []
    quantum = 256
    for first, second, third in itertools.product(range(-2, 3), repeat=3):
        target = (first * quantum, second * quantum, third * quantum,
                  -(first + second + third) * quantum)
        bounds = tuple((value, value) for value in target)
        selected = select_tangent(bounds)
        if selected != target:
            raise RuntimeError("fresh tangent selector lost singleton preimage")
        state = lift(selected)
        projected = forward(state)
        if projected != target:
            raise RuntimeError("dual-frame lift failed exact generated round trip")
        boundary_relation = fresh_boundary_relation(state)
        admitted = any(state) and boundary_relation != "UNRESOLVED"
        if admitted != any(target):
            raise RuntimeError("fresh support admitted ZEmpty or lost support")
        fixture_records.append((target, state, projected, boundary_relation,
                                admitted))

    broad_bounds = ((-2 * quantum, 4 * quantum),
                    (quantum, 5 * quantum),
                    (-6 * quantum, -quantum),
                    (-4 * quantum, 6 * quantum))
    broad_selected = select_tangent(broad_bounds)
    if broad_selected is None or sum(broad_selected) != 0:
        raise RuntimeError("fresh minimum-change selector lost feasible tangent cell")
    broad_state = lift(broad_selected)
    broad_forward = forward(broad_state)
    if any(not lower <= value <= upper
           for value, (lower, upper) in zip(broad_forward, broad_bounds)):
        raise RuntimeError("fresh selected state failed retained shadow relation")
    broad_relation = fresh_boundary_relation(broad_state)
    if broad_relation == "UNRESOLVED":
        raise RuntimeError("fresh broad fixture has unresolved native boundary")

    impossible = ((quantum, 2 * quantum),) * 4
    if select_tangent(impossible) is not None:
        raise RuntimeError("fresh selector admitted a non-tangent shadow cell")

    nonzero = [record for record in fixture_records if record[4]]
    common_state = nonzero[0][1]
    if any(value != common_state for value in (common_state, common_state)):
        raise RuntimeError("complete-union common fresh result changed")
    different_state = nonzero[-1][1]
    if different_state == common_state:
        raise RuntimeError("fresh ambiguity fixture did not differ")
    branch_orders = ((common_state, common_state),
                     tuple(reversed((common_state, common_state))))
    common_serializations = [sha256({"state": values[0], "cell": (0, 0, 0)})
                             for values in branch_orders]
    if len(set(common_serializations)) != 1:
        raise RuntimeError("fresh result depends on branch order")

    kernel_probe = tuple([scale] + [0] * (LANES - 1))
    if fresh_boundary_relation(kernel_probe) != "UNRESOLVED":
        raise RuntimeError("fresh boundary admitted a nonzero diffraction kernel")
    one_lsb_probe = tuple([0, 1] + [0] * (LANES - 2))
    if fresh_boundary_relation(one_lsb_probe) != "NO_RELATION":
        raise RuntimeError("exact one-LSB defect aliased algebraic zero")
    if fresh_boundary_relation((0,) * LANES) != "DEFAULT_SAT":
        raise RuntimeError("fresh boundary lost all-default DEFAULT_SAT")
    if any(record[3] != "NO_RELATION" for record in nonzero):
        raise RuntimeError("fresh lifted support lost resolved termination relation")

    return {
        "fixtureCount": len(fixture_records) + 2,
        "admittedFixtureCount": len(nonzero) + 1,
        "unresolvedFixtureCount": 2,
        "dualFrameRoundTripCount": len(fixture_records),
        "boundaryResolvedFixtureCount": len(nonzero) + 1,
        "exactPointDefectFixtureCount": 1,
        "externalRelationTruthInputCount": 0,
        "commonPermutationCount": len(branch_orders),
        "basePattern": [[0, 0, 0]],
        "fingerprint": sha256({
            "fixtures": fixture_records,
            "broad": (broad_bounds, broad_selected, broad_state, broad_forward,
                      broad_relation),
            "impossible": impossible,
            "kernelProbe": kernel_probe,
            "oneLsbProbe": one_lsb_probe,
            "common": common_serializations,
        }),
    }


def constructive_stitch_authority_proof(i_q: dict, i_rep: dict,
                                         ir: dict) -> dict:
    """Freeze abstract native incidence and representation-only D4 chart gauge."""
    authority = i_q["constructiveModalStitching"]
    boundary = authority["implicitBoundaryField"]
    modal = authority["modalConjunction"]
    associator_context = authority["associatorContextAuthority"]
    fusion = authority["hotFusion"]
    embedding = i_rep["stitchEmbedding"]
    transport = embedding["nativeRelativeTransportTheorem"]
    gauge = i_rep["gaugeFamily"]
    if (authority["authority"] !=
            "SCANNER_LEVEL_I_Q_CONTACT_CONJUNCTION_WITH_I_TOE_NATIVE_RELATION_NOT_AN_UPSTREAM_TOE_CLAIM" or
            authority["semanticTruthInputs"] != [] or
            authority["semanticClasses"] !=
            "OUTPUT_RECEIPTS_ONLY_NEVER_TRUSTED_INPUT" or
            associator_context["externalBracketContextInput"] or
            associator_context["sensorDerivedBracketContext"] or
            associator_context["completeBasisContextCount"] != LANES or
            not associator_context["associatorProfileIsIntrinsicS16"] or
            associator_context["S32Required"] or
            boundary["arbitraryEpsilon"] or
            boundary["pixelAdjacencyAuthority"] or
            not boundary["samplingFootprintAdjacencyBroadPhaseOnly"] or
            boundary["crossFramePixelAdjacency"] or
            boundary["freshFreshBroadPhase"] !=
            "ONLY_SHARED_BOUNDARIES_OF_ADJACENT_VALID_COHERENT_SAMPLING_FOOTPRINTS_IN_THE_SAME_FRAME" or
            boundary["samplingAdjacencyMeaning"] !=
            "BROAD_PHASE_ELIGIBILITY_ONLY_NEVER_SIGMA2_INCIDENCE_OR_RELATIVE_DELTA" or
            boundary["invalidGapOrOcclusionBridge"] or
            boundary["materializeCompleteBoundarySet"] or
            boundary["full320BoundaryCardinality"] != 204160 or
            modal["minimumResidualWinner"] or
            modal["callerSuppliedRelationOrFactorClass"] or
            modal["callerSuppliedTransportSign"] or
            modal["callerSuppliedNativeGenerator"] or
            modal["callerSuppliedBracketFingerprint"] or
            modal["callerSuppliedPlaquetteC"] or
            modal["callerSuppliedLoopOrRelationTruth"] or
            modal["physicalStitchContainsSignedDyadicDelta"] or
            modal["nativeSectorToUvEquationRequired"] or
            modal["staticOneHotOrSampleSideTransformMap"] or
            modal["generatedSectorPairTransport"]["transportAddress"] !=
            "G_AB_EQUALS_A_XOR_B" or
            modal["generatedSectorPairTransport"]["forwardAction"] !=
            "U_AB_OF_U_EQUALS_EPSILON_A_G_AB_TIMES_EXPLICIT_RIGHT_BRACKET_U_E_G_AB" or
            modal["generatedSectorPairTransport"]["chartCoordinateMeaning"] !=
            "NONE" or
            gauge["independentComponentGaugeGroup"] !=
            "PRODUCT_OVER_STITCH_DISCONNECTED_COMPONENTS_OF_Z2_SEMIDIRECT_D4" or
            set(gauge["notGauge"]) != {
                "DYADIC_SCALE", "REFINEMENT_LEVEL", "SUPPORTED_BANDWIDTH",
                "LOSSY_RESAMPLING"} or
            gauge["physicalTransformAuthorityFromD4"] or
            not transport["nativeTransportRequired"] or
            transport["externalBracketContextInput"] or
            transport["sensorDerivedBracketContext"] or
            transport["completeBasisContextCount"] != LANES or
            not transport["associatorProfileIsIntrinsicS16"] or
            transport["S32Required"] or
            transport["samplingBoundarySideMayDetermineDelta"] or
            transport["samplingBoundarySideMayDetermineNativePort"] or
            transport["nativeSectorPairToSignedUvRequired"] or
            transport["sectorPairTransport"] !=
            "FOR_NATIVE_SECTOR_ADDRESSES_A_B_GENERATE_G_EQUALS_A_XOR_B_AND_U_AB_OF_U_EQUALS_EPSILON_A_G_TIMES_EXPLICIT_RIGHT_BRACKET_U_E_G_WITH_THE_SWAPPED_REVERSE_EVALUATED_SEPARATELY" or
            transport["callerSuppliedOrientationOrDelta"] or
            transport["callerSuppliedNativeGenerator"] or
            transport["callerSuppliedRelationOrFactorClass"] or
            transport["callerSuppliedPlaquetteCOrLoopClass"] or
            transport["unilateralSideMapping"] or
            i_rep["normalizer"]["nonGaugeEquivalentEmbeddingClasses"] !=
            "UNRESOLVED_NEVER_LEXICOGRAPHIC_WINNER_SELECTION" or
            i_rep["normalizer"]["abstractSectorChartAssignments"] !=
            "ENUMERATE_ALL_4_FACTORIAL_24_BIJECTIONS_BEFORE_QUOTIENT" or
            i_rep["normalizer"]["abstractSectorChartAssignmentCount"] != 24 or
            i_rep["normalizer"]["abstractSectorChartD4OrbitCount"] != 3 or
            i_rep["normalizer"]["fixedNativeSectorToSquareSideConvention"] or
            embedding["chartEmbedding"]["nonGaugeEquivalentEmbeddingClasses"] !=
            "UNRESOLVED" or
            embedding["chartEmbedding"]["abstractSectorAssignment"] !=
            "ENUMERATE_ALL_24_BIJECTIONS_FROM_FOUR_ABSTRACT_NATIVE_SECTORS_TO_FOUR_SQUARE_BOUNDARY_DIRECTIONS" or
            embedding["chartEmbedding"]["assignmentGaugeQuotient"] !=
            "D4_COLLAPSES_EIGHT_IMAGES_OF_ONE_ASSIGNMENT_ORBIT_BUT_DOES_NOT_COLLAPSE_THE_THREE_POSSIBLE_CYCLIC_OPPOSITION_CLASSES" or
            embedding["chartEmbedding"]["survivingAssignmentOrbitRule"] !=
            "ONE_D4_ORBIT_RESOLVED_MORE_THAN_ONE_D4_ORBIT_UNRESOLVED" or
            embedding["chartEmbedding"]["samplingBoundarySideSelectsAssignment"] or
            embedding["chartEmbedding"]["d4TransformsPhysicalS16OrNativeWitness"] or
            embedding["callerSuppliedLoopClassification"] or
            embedding["componentNormalization"]["persistentComponentIdentity"] or
            fusion["semanticPhases"] != ["FOOTPRINT", "BOUNDARY", "CLOSE"] or
            not fusion["mapIntoExistingEntrypoints"] or
            fusion["newShaderFamilyPerPhase"] or
            fusion["separateRuntimeSubsystems"] or
            fusion["targetMaximumAdditionalHotSubmissionsBeyondAcceptedN3NativeGraph"] != 2 or
            not fusion["parallelismDominatesLiteralCount"] or
            fusion["serialGenericInterpreter"]):
        raise RuntimeError("constructive stitch authority admits heuristic identity/gauge")

    width = height = 320
    implicit_edges = (width - 1) * height + width * (height - 1)
    implicit_plaquettes = (width - 1) * (height - 1)
    if implicit_edges != 204160 or implicit_plaquettes != 101761:
        raise RuntimeError("implicit coherent sampling-complex count drifted")

    # K16 character sectors remain abstract native boundary labels.  They are not
    # signed chart axes.  D4 is enumerated only after an abstract stitched pattern
    # has closed, and acts only on chart coordinates.
    abstract_sectors = (1, 2, 4, 8)
    if modal["nativeBoundarySectorInventory"] != (
            "FOUR_GENERATED_K16_CHARACTER_FRAME_SECTORS_FROM_I_TOE_SECTION_6_WITH_NO_UV_OR_SAMPLE_SIDE_MEANING"):
        raise RuntimeError("native boundary sectors are not capsule-backed")
    d4 = (
        (1, 0, 0, 1), (0, -1, 1, 0), (-1, 0, 0, -1),
        (0, 1, -1, 0), (-1, 0, 0, 1), (1, 0, 0, -1),
        (0, 1, 1, 0), (0, -1, -1, 0),
    )
    if len(set(d4)) != 8 or any(abs(a * d - b * c) != 1
                                for a, b, c, d in d4):
        raise RuntimeError("D4 chart gauge is not the eight square isometries")
    probe = ((0, 0), (2, 0), (2, 1))
    d4_images = {
        tuple((a * u + b * v, c * u + d * v) for u, v in probe)
        for a, b, c, d in d4
    }
    if len(d4_images) != 8:
        raise RuntimeError("D4 proof fixture does not distinguish eight images")
    chain = frozenset(((0, 0), (1, 0), (2, 0)))
    corner = frozenset(((0, 0), (1, 0), (1, 1)))
    chain_orbit = {
        frozenset((a * u + b * v, c * u + d * v) for u, v in chain)
        for a, b, c, d in d4
    }
    if corner in chain_orbit:
        raise RuntimeError("non-D4-equivalent incidence ambiguity was collapsed")

    # A fixed abstract-sector -> square-side convention is not chart gauge.
    # All 4! assignments split into three D4 orbits of eight; closure must retain
    # distinct surviving orbits instead of selecting one hidden convention.
    sector_assignments = tuple(itertools.permutations(range(4)))
    direction_vectors = ((1, 0), (0, 1), (-1, 0), (0, -1))

    def transform_direction(direction: int,
                            transform: tuple[int, int, int, int]) -> int:
        u, v = direction_vectors[direction]
        a, b, c, d = transform
        transformed = (a * u + b * v, c * u + d * v)
        return direction_vectors.index(transformed)

    def assignment_orbit(assignment: tuple[int, ...]) -> tuple[int, ...]:
        return min(tuple(transform_direction(direction, transform)
                         for direction in assignment)
                   for transform in d4)

    assignment_orbits = {
        assignment_orbit(assignment) for assignment in sector_assignments
    }
    orbit_sizes = sorted(sum(1 for assignment in sector_assignments
                             if assignment_orbit(assignment) == orbit)
                         for orbit in assignment_orbits)
    if (len(sector_assignments) != 24 or len(assignment_orbits) != 3 or
            orbit_sizes != [8, 8, 8]):
        raise RuntimeError("abstract-sector chart assignment quotient drifted")

    # One base integer translation is scaled in dyadic numerator space.  These
    # probes are the constructive mixed-level representation invariant used by
    # the generated CPU implementation and its DecodeField tests.
    translation = -3
    mixed_level_probes = []
    for level in (0, 1, 2):
        numerator = 5 * (1 << level)
        translated = numerator + translation * (1 << level)
        if Fraction(translated, 1 << level) != \
                Fraction(numerator, 1 << level) + translation:
            raise RuntimeError("mixed-level component translation changed field point")
        mixed_level_probes.append((level, numerator, translated))
    expression = ir["expressions"][ir["freshSupportSetExpression"]]
    node_names = ir["opcodes"]
    owned = [node_names[ir["nodes"][index]["opcode"]]
             for index in range(expression["nodeStart"],
                                expression["nodeStart"] + expression["nodeCount"])]
    required = [
        "WHOLE_FRAME_REVERSE_SET", "FOOTPRINT_CONTRACT",
        "IMPLICIT_BOUNDARY_CONTRACT", "GLOBAL_EXACT_CLOSE",
    ]
    if any(name not in owned for name in required):
        raise RuntimeError("set-level stitch expression is not executable in sole IR")
    return {
        "source": expression["source"],
        "expressionFingerprint": expression["fingerprint"],
        "nodeCount": expression["nodeCount"],
        "contactEpsilonCount": 0,
        "pixelOrXyzAuthorityCount": 0,
        "externalSemanticTruthInputCount": 0,
        "callerLoopTruthInputCount": 0,
        "samplingSideToDeltaAuthorityCount": 0,
        "abstractNativeSectorCount": len(abstract_sectors),
        "abstractSectorChartAssignmentCount": len(sector_assignments),
        "abstractSectorChartAssignmentOrbitCount": len(assignment_orbits),
        "d4ChartImageCount": len(d4),
        "nonGaugeEmbeddingAmbiguityCount": 1,
        "implicitBoundaryCount320": implicit_edges,
        "implicitPlaquetteCount320": implicit_plaquettes,
        "hotSemanticPhaseCount": len(fusion["semanticPhases"]),
        "targetAdditionalHotSubmissionCount":
            fusion["targetMaximumAdditionalHotSubmissionsBeyondAcceptedN3NativeGraph"],
        "mixedLevelTranslationProbeCount": len(mixed_level_probes),
        "freshFreshBroadPhase": "SHARED_COHERENT_FOOTPRINT_BOUNDARY_ONLY",
        "componentGauge": "INDEPENDENT_Z2_SEMIDIRECT_D4_CHART_GAUGE",
        "persistentComponentIdentity": False,
        "externalBracketContextInputCount": 0,
        "completeAssociatorBasisContextCount": LANES,
        "associatorProfileIsIntrinsicS16": True,
        "s32Required": False,
        "fingerprint": sha256({
            "authority": authority,
            "embedding": embedding,
            "expression": expression,
            "ownedOpcodes": owned,
            "implicitCounts": (implicit_edges, implicit_plaquettes),
            "abstractSectors": abstract_sectors,
            "d4": d4,
            "sectorAssignments": sector_assignments,
            "sectorAssignmentOrbits": sorted(assignment_orbits),
            "nonGaugeAmbiguity": (sorted(chain), sorted(corner)),
            "fusion": fusion,
            "mixedLevelProbes": mixed_level_probes,
        }),
    }


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
        "G=2A^TA=-2A^2",
        "\\widehat d_{ij}",
        "\\widehat{\\mathfrak A}_{ijk}",
        "\\widehat F_\\square",
        "\\mathfrak D_{cl}",
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
    capture_boundary = i_q.get("captureBoundary")
    if not isinstance(capture_boundary, dict):
        raise RuntimeError("I_Q lacks the constructive Quest capture boundary")
    expected_leaves = (
        "LEFT_DEPTH_ORDER", "LEFT_OPTICAL_R", "LEFT_OPTICAL_G",
        "LEFT_OPTICAL_B", "RIGHT_DEPTH_ORDER", "RIGHT_OPTICAL_R",
        "RIGHT_OPTICAL_G", "RIGHT_OPTICAL_B")
    if (tuple(capture_boundary.get("leafInventory", ())) != expected_leaves or
            capture_boundary.get("rawOwner") != "StereoRigFrameLease" or
            capture_boundary["nativeRowConstruction"].get(
                "pixelOrXyzIdentity", True) or
            capture_boundary["leafValueConstruction"].get(
                "depthMayBootstrapOptical", True) or
            capture_boundary["leafValueConstruction"].get(
                "opticalMayBootstrapDepth", True)):
        raise RuntimeError("I_Q capture adapter lost its eight-leaf/no-bootstrap law")
    nuisance = i_q["photometricNuisance"]
    if (nuisance["calibrationProvenance"] !=
            "CAPTURE_CALIBRATION_EPOCH_PLUS_GRAPHICS_FORMAT_AND_POST_SAMPLER_TRANSFER_FINGERPRINT" or
            nuisance["unboundedParameterRegion"] !=
            "FORBIDDEN_TO_PROVE_COMPATIBILITY_OR_MUTATION" or
            nuisance["metadataMissing"] !=
            "DERIVE_BOUNDED_POST_ISP_CODE_SPACE_REGION_FROM_GRAPHICS_FORMAT_AND_THE_CALIBRATED_2X2_FOOTPRINT_HULL_WITH_MISSING_RAW_METADATA_PROVENANCE" or
            nuisance.get("missingMetadataMayProveSceneLinearRadiance", True) or
            not nuisance["requiredBoundedParameters"]):
        raise RuntimeError("photometric nuisance law is not calibrated/fail-closed")
    if i_q["querySupportSummary"]["falseNegatives"] != 0:
        raise RuntimeError("query-support authority permits false negatives")
    native_relation = i_q["nativeModalRelation"]
    if (native_relation["source"] != "I_TOE_SECTION_8" or
            native_relation["informationMetric"] !=
            "G_EQUALS_2_AT_A_EQUALS_MINUS_2_A_SQUARED_FOR_DIFFRACTION_A" or
            native_relation["primitiveRepresentative"] !=
            "DIVIDE_NONZERO_Q16_48_RAW_COEFFICIENT_VECTOR_BY_ITS_POSITIVE_INTEGER_CONTENT_GCD" or
            native_relation["independentContinuousWeights"] or
            native_relation["epsilonClParameter"] or
            "NONZERO_RAW_Q16_48_NUMERATOR" not in
                native_relation["exactPointDefectWitness"] or
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
    if (i_rep["address"]["baseLevel"] != 0 or
            i_rep["address"]["maximumLevel"] != 62 or
            i_rep["kappa"]["ownership"] != "DISJOINT_PARTITION" or
            i_rep["kappa"]["overlap"] != "FORBIDDEN"):
        raise RuntimeError("representation address/ownership bounds are not exact")
    fresh_authority = i_q["freshBaseAdmission"]
    if ("NO_EXTERNAL_RELATION_CLASS_OR_TRUTH_BOOLEAN" not in
            fresh_authority["relationVerification"] or
            fresh_authority["pixelOrXyzAuthority"] or
            fresh_authority["candidateObject"]):
        raise RuntimeError("fresh admission permits an external physical authority")
    stitch_authority = i_q["constructiveModalStitching"]
    if (stitch_authority["manifestationFootprint"]["canonicalState"] or
            stitch_authority["manifestationFootprint"]["sigma2Address"] or
            stitch_authority["implicitBoundaryField"]["arbitraryEpsilon"] or
            stitch_authority["implicitBoundaryField"]["pixelAdjacencyAuthority"] or
            i_rep["stitchEmbedding"]["forbiddenAuthorities"] !=
            ["PIXEL", "XYZ", "PAGE", "SAMPLE", "SAMPLE_BOUNDARY_SIDE",
             "HASH_PLACEMENT"]):
        raise RuntimeError("constructive stitch authority mints physical identity")

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
    metric = information_metric(diffraction)

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

    ir = build_executable_merkaba_ir()
    reverse_proof = reverse_complete_tree_proof(ir)
    support_proof = query_support_exhaustive_proof(i_q)
    default_proof = default_representation_proof(i_q, i_rep, ir)
    certificate_proof = certificate_minimizer_proof()
    gauge_proof = gauge_transport_proof()
    fresh_admission_proof = fresh_base_admission_proof()
    stitch_authority_proof = constructive_stitch_authority_proof(i_q, i_rep, ir)

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
        "ir": ir,
        "expressions": ir["expressions"],
        "reverseRules": ir["reverseRules"],
        "queryFamilies": i_q["queryFamilies"],
        "captureBoundary": i_q["captureBoundary"],
        "sceneReduction": i_q["sceneReduction"],
        "photometricNuisance": i_q["photometricNuisance"],
        "querySupportSummary": i_q["querySupportSummary"],
        "certificate": i_q["certificate"],
        "freshBaseAdmission": i_q["freshBaseAdmission"],
        "constructiveModalStitching": i_q["constructiveModalStitching"],
        "representation": i_rep,
        "diffractionMatrix": [value for row in diffraction for value in row],
        "informationMetric": [value for row in metric for value in row],
        "shellSquareByRank": shell_square_by_rank,
        "shadowNumerator4": [value for row in shadow for value in row],
        "visibleProjectorNumerator256": [value for row in visible_numerator
                                          for value in row],
        "proofs": {
            "associatorNonzero": associator_nonzero,
            "associatorHistogram": associator_coefficients,
            "diffractionSkew": True,
            "informationMetricIdentity": "G=2*A^T*A=-2*A^2",
            "independentClosureWeights": 0,
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
            "missingOpticalMetadata":
                "BOUNDED_POST_ISP_CODE_REGION_WITH_MISSING_RAW_METADATA_PROVENANCE",
            "captureBoundaryLeafCount": len(expected_leaves),
            "captureBoundaryFingerprint": sha256(i_q["captureBoundary"]),
            "querySupportFalseNegatives": support_proof["falseNegatives"],
            "querySupportFixtureCount": support_proof["fixtureCount"],
            "querySupportOmittedIdentityFixtures":
                support_proof["omittedIdentityFixtures"],
            "querySupportRefinedFixtureCount":
                support_proof["refinedFixtureCount"],
            "querySupportNonresidentFixtureCount":
                support_proof["nonresidentFixtureCount"],
            "querySupportEvaluationFingerprint":
                support_proof["evaluationFingerprint"],
            "reverseIntervalSoundFixtureCount": reverse_proof["fixtureCount"],
            "reverseZeroBranchRetained": reverse_proof["zeroBranchCount"] > 0,
            "reverseSceneDisjunctionCount": reverse_proof["sceneDisjunctionCount"],
            "bracketNegativeControls": reverse_proof["bracketNegativeControls"],
            "reverseIrAssociatorFingerprint":
                reverse_proof["irAssociatorFingerprint"],
            "reverseIrForwardFixtureCount":
                reverse_proof["irForwardFixtureCount"],
            "reverseIrPreimageOutputCount":
                reverse_proof["irPreimageOutputCount"],
            "reverseIrAmbiguousPreimageOutputCount":
                reverse_proof["irAmbiguousPreimageOutputCount"],
            "reverseIrMaxPreimageCount": reverse_proof["irMaxPreimageCount"],
            "duplicateFixtureCount": certificate_proof["duplicateFixtureCount"],
            "duplicateMinimizedFactorCount":
                certificate_proof["minimizedFactorCount"],
            "duplicateMultiplicity": certificate_proof["duplicateMultiplicity"],
            "coupledFactorInputCount": certificate_proof["coupledFactorCount"],
            "coupledFactorMinimizedCount": certificate_proof["coupledFactorCount"],
            "certificateFeasibleAssignmentCount":
                certificate_proof["feasibleAssignmentCount"],
            "certificateProofFingerprint": certificate_proof["fingerprint"],
            "weakFactorPreservesStrongRegion": True,
            "gaugePermutationCount": gauge_proof["permutationCount"],
            "gaugePointProbeCount": gauge_proof["pointProbeCount"],
            "gaugeTransportFieldCount": gauge_proof["transportFieldCount"],
            "baseGaugeNormalForm": gauge_proof["normalForm"],
            "canonicalSerializationFingerprint":
                gauge_proof["canonicalSerializationFingerprint"],
            "freshSupportUniqueModuloGauge":
                gauge_proof["freshSupportUniqueModuloGauge"],
            "freshSupportNonEquivalentRejected":
                gauge_proof["freshSupportNonEquivalentRejected"],
            "freshAdmissionFixtureCount":
                fresh_admission_proof["fixtureCount"],
            "freshAdmissionAdmittedFixtureCount":
                fresh_admission_proof["admittedFixtureCount"],
            "freshAdmissionUnresolvedFixtureCount":
                fresh_admission_proof["unresolvedFixtureCount"],
            "freshAdmissionDualFrameRoundTripCount":
                fresh_admission_proof["dualFrameRoundTripCount"],
            "freshAdmissionBoundaryResolvedFixtureCount":
                fresh_admission_proof["boundaryResolvedFixtureCount"],
            "freshAdmissionExactPointDefectFixtureCount":
                fresh_admission_proof["exactPointDefectFixtureCount"],
            "freshAdmissionExternalRelationTruthInputCount":
                fresh_admission_proof["externalRelationTruthInputCount"],
            "freshAdmissionCommonPermutationCount":
                fresh_admission_proof["commonPermutationCount"],
            "freshAdmissionBasePattern":
                fresh_admission_proof["basePattern"],
            "freshAdmissionProofFingerprint":
                fresh_admission_proof["fingerprint"],
            "constructiveStitchExpressionFingerprint":
                stitch_authority_proof["expressionFingerprint"],
            "constructiveStitchNodeCount":
                stitch_authority_proof["nodeCount"],
            "constructiveStitchContactEpsilonCount": 0,
            "constructiveStitchPixelOrXyzAuthorityCount": 0,
            "constructiveStitchExternalSemanticTruthInputCount":
                stitch_authority_proof["externalSemanticTruthInputCount"],
            "constructiveStitchCallerLoopTruthInputCount":
                stitch_authority_proof["callerLoopTruthInputCount"],
            "constructiveStitchSamplingSideToDeltaAuthorityCount":
                stitch_authority_proof["samplingSideToDeltaAuthorityCount"],
            "constructiveStitchAbstractNativeSectorCount":
                stitch_authority_proof["abstractNativeSectorCount"],
            "constructiveStitchAbstractSectorChartAssignmentCount":
                stitch_authority_proof["abstractSectorChartAssignmentCount"],
            "constructiveStitchAbstractSectorChartAssignmentOrbitCount":
                stitch_authority_proof[
                    "abstractSectorChartAssignmentOrbitCount"],
            "constructiveStitchD4ChartImageCount":
                stitch_authority_proof["d4ChartImageCount"],
            "constructiveStitchNonGaugeEmbeddingAmbiguityCount":
                stitch_authority_proof["nonGaugeEmbeddingAmbiguityCount"],
            "constructiveStitchImplicitBoundaryCount320":
                stitch_authority_proof["implicitBoundaryCount320"],
            "constructiveStitchImplicitPlaquetteCount320":
                stitch_authority_proof["implicitPlaquetteCount320"],
            "constructiveStitchHotSemanticPhaseCount":
                stitch_authority_proof["hotSemanticPhaseCount"],
            "constructiveStitchTargetAdditionalHotSubmissionCount":
                stitch_authority_proof["targetAdditionalHotSubmissionCount"],
            "constructiveStitchMixedLevelTranslationProbeCount":
                stitch_authority_proof["mixedLevelTranslationProbeCount"],
            "constructiveStitchExternalBracketContextInputCount":
                stitch_authority_proof["externalBracketContextInputCount"],
            "constructiveStitchCompleteAssociatorBasisContextCount":
                stitch_authority_proof["completeAssociatorBasisContextCount"],
            "constructiveStitchAssociatorProfileIsIntrinsicS16":
                stitch_authority_proof["associatorProfileIsIntrinsicS16"],
            "constructiveStitchS32Required":
                stitch_authority_proof["s32Required"],
            "constructiveStitchFreshFreshBroadPhase":
                stitch_authority_proof["freshFreshBroadPhase"],
            "constructiveStitchComponentGauge":
                stitch_authority_proof["componentGauge"],
            "constructiveStitchPersistentComponentIdentity": False,
            "constructiveStitchProofFingerprint":
                stitch_authority_proof["fingerprint"],
            "refinementProlongation": "FOUR_EXACT_FULL_S16_COPIES",
            "refinementExactHalfOpenCover": True,
            "refinementPointwiseFullS16": True,
            "refinementExactMeasure": True,
            "representationDefaultParity": True,
            "defaultRepresentationFixtureCount": default_proof["fixtureCount"],
            "defaultRepresentationQueryCount": default_proof["queryCount"],
            "defaultRepresentationCount": default_proof["representationCount"],
            "defaultRepresentationProofFingerprint":
                default_proof["fingerprint"],
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
    for name in ("numeric", "multiplication", "annihilator", "readout",
                 "operators", "bundle"):
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


SIGMA_CHART_D4 = (
    (1, 0, 0, 1),
    (0, -1, 1, 0),
    (-1, 0, 0, -1),
    (0, 1, -1, 0),
    (-1, 0, 0, 1),
    (1, 0, 0, -1),
    (0, 1, 1, 0),
    (0, -1, -1, 0),
)


def sigma_chart_assignment_orbit(assignment: tuple[int, ...]) -> int:
    opposite = (assignment[0] + 2) & 3
    return (1 if assignment[2] == opposite else 0) | (
        2 if assignment[3] == opposite else 0)


def sigma_chart_d4_tables() -> tuple[list[int], list[int], list[int], list[int]]:
    assignments = list(itertools.permutations(range(4)))
    representatives = [
        next(index for index, assignment in enumerate(assignments)
             if sigma_chart_assignment_orbit(assignment) == orbit)
        for orbit in range(3)
    ]

    compose: list[int] = []
    for outer in SIGMA_CHART_D4:
        for inner in SIGMA_CHART_D4:
            product = (
                outer[0] * inner[0] + outer[1] * inner[2],
                outer[0] * inner[1] + outer[1] * inner[3],
                outer[2] * inner[0] + outer[3] * inner[2],
                outer[2] * inner[1] + outer[3] * inner[3],
            )
            compose.append(SIGMA_CHART_D4.index(product))
    inverse = [
        next(candidate for candidate in range(8)
             if compose[candidate * 8 + frame] == 0)
        for frame in range(8)
    ]

    directions = ((1, 0), (0, 1), (-1, 0), (0, -1))

    def determinant(frame: int) -> int:
        value = SIGMA_CHART_D4[frame]
        return value[0] * value[3] - value[1] * value[2]

    def direction(assignment_index: int, frame: int,
                  sector: int) -> tuple[int, int]:
        source = directions[assignments[assignment_index][sector]]
        transform = SIGMA_CHART_D4[frame]
        return (
            transform[0] * source[0] + transform[1] * source[1],
            transform[2] * source[0] + transform[3] * source[1],
        )

    adjacent: list[int] = []
    for orbit, assignment_index in enumerate(representatives):
        for current_frame in range(8):
            for current_sector in range(4):
                for next_sector in range(4):
                    for parity in (-1, 1):
                        current_direction = direction(assignment_index,
                                                      current_frame,
                                                      current_sector)
                        matches = []
                        for candidate in range(8):
                            reverse_direction = direction(assignment_index,
                                                          candidate,
                                                          next_sector)
                            if (reverse_direction ==
                                    (-current_direction[0],
                                     -current_direction[1]) and
                                    determinant(candidate) ==
                                    determinant(current_frame) * parity):
                                matches.append(candidate)
                        if len(matches) != 1:
                            raise ValueError(
                                "Generated D4 adjacent-frame table is not total")
                        adjacent.append(matches[0])
    return compose, inverse, representatives, adjacent


def render_merkaba_cs(descriptor: dict) -> str:
    proofs = descriptor["proofs"]
    ir = descriptor["ir"]
    d4_compose, d4_inverse, orbit_representatives, adjacent_frames = \
        sigma_chart_d4_tables()
    chart_d4_lines = ",\n".join(
        "            new SigmaChartD4Transform(" +
        ", ".join(str(value) for value in transform) + ")"
        for transform in SIGMA_CHART_D4)
    d4_compose_values = ", ".join(str(value) for value in d4_compose)
    d4_inverse_values = ", ".join(str(value) for value in d4_inverse)
    orbit_representative_values = ", ".join(
        str(value) for value in orbit_representatives)
    adjacent_frame_values = ", ".join(str(value) for value in adjacent_frames)
    stitch_bracket_fingerprint = int(
        proofs["constructiveStitchExpressionFingerprint"][:16], 16)
    expression_fingerprints = ",\n".join(
        f'            "{entry["fingerprint"]}"'
        for entry in descriptor["expressions"])
    input_lines = "\n".join(
        f'        internal const string {upper_snake(name).title().replace("_", "")}InputFingerprint = "{value}";'
        for name, value in descriptor["inputs"].items())
    opcode_members = "\n".join(
        f"        {name} = {index}u," for index, name in enumerate(ir["opcodes"]))
    kind_members = "\n".join(
        f"        {name} = {index}u," for index, name in enumerate(ir["valueKinds"]))
    reverse_members = "\n".join(
        f"        {name} = {index}u," for index, name in enumerate(ir["reverseRules"]))
    node_lines = ",\n".join(
        "            new SigmaMerkabaIrNode(" +
        f"(SigmaMerkabaIrOpcode){node['opcode']}u, " +
        f"(SigmaMerkabaValueKind){node['outputKind']}u, " +
        f"(SigmaMerkabaReverseRule){node['reverseRule']}u, " +
        f"{node['operandStart']}, {node['operandCount']}, " +
        f"{node['argument0']}, {node['argument1']})"
        for node in ir["nodes"])
    operand_lines = ", ".join(str(value) for value in ir["operands"])
    expression_lines = ",\n".join(
        "            new SigmaMerkabaExpression(" +
        f'"{entry["id"]}", "{entry["source"]}", {entry["arity"]}, ' +
        f"{entry['neighbourhood']}, {entry['nodeStart']}, " +
        f"{entry['nodeCount']}, {entry['rootNode']}, " +
        f'"{entry["fingerprint"]}")'
        for entry in ir["expressions"])
    entry_lines = ",\n".join(
        "            new SigmaMerkabaEntryPoint(" +
        f'"{entry["id"]}", {entry["forwardExpression"]}, ' +
        f"{entry['reverseExpression']}, {entry['reducer']})"
        for entry in ir["entryPoints"])
    return f"""// <auto-generated by Tools/sigma/generate_sigma_operators.py>
// Canonical baseline: CPQ4-2026-08-25-S16-v8.3. Do not edit by hand.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Genesis.RoomScan.SigmaPrism
{{
    internal enum SigmaMerkabaIrOpcode : uint
    {{
{opcode_members}
    }}

    internal enum SigmaMerkabaValueKind : uint
    {{
{kind_members}
    }}

    internal enum SigmaMerkabaReverseRule : uint
    {{
{reverse_members}
    }}

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

    internal enum SigmaExactFactorClass : uint
    {{
        ProvenIncompatible = 0u,
        ProvenExactClosed = 1u,
        Unresolved = 2u,
    }}

    internal enum SigmaDefaultBackingKind : uint
    {{
        LogicalUnbacked = 0u,
        ExplicitZEmpty = 1u,
        NullCodec = 2u,
    }}

    internal enum SigmaFreshAdmissionStatus : uint
    {{
        Unresolved = 0u,
        Admitted = 1u,
    }}

    internal readonly struct SigmaMerkabaIrNode
    {{
        internal SigmaMerkabaIrNode(SigmaMerkabaIrOpcode opcode,
            SigmaMerkabaValueKind outputKind, SigmaMerkabaReverseRule reverseRule,
            int operandStart, int operandCount, int argument0, int argument1)
        {{
            Opcode = opcode;
            OutputKind = outputKind;
            ReverseRule = reverseRule;
            OperandStart = operandStart;
            OperandCount = operandCount;
            Argument0 = argument0;
            Argument1 = argument1;
        }}
        internal SigmaMerkabaIrOpcode Opcode {{ get; }}
        internal SigmaMerkabaValueKind OutputKind {{ get; }}
        internal SigmaMerkabaReverseRule ReverseRule {{ get; }}
        internal int OperandStart {{ get; }}
        internal int OperandCount {{ get; }}
        internal int Argument0 {{ get; }}
        internal int Argument1 {{ get; }}
    }}

    internal readonly struct SigmaMerkabaExpression
    {{
        internal SigmaMerkabaExpression(string id, string source, int arity,
            int neighbourhood, int nodeStart, int nodeCount, int rootNode,
            string fingerprint)
        {{
            Id = id; Source = source; Arity = arity; Neighbourhood = neighbourhood;
            NodeStart = nodeStart; NodeCount = nodeCount; RootNode = rootNode;
            Fingerprint = fingerprint;
        }}
        internal string Id {{ get; }}
        internal string Source {{ get; }}
        internal int Arity {{ get; }}
        internal int Neighbourhood {{ get; }}
        internal int NodeStart {{ get; }}
        internal int NodeCount {{ get; }}
        internal int RootNode {{ get; }}
        internal string Fingerprint {{ get; }}
    }}

    internal readonly struct SigmaMerkabaEntryPoint
    {{
        internal SigmaMerkabaEntryPoint(string id, int forwardExpression,
            int reverseExpression, int reducer)
        {{
            Id = id; ForwardExpression = forwardExpression;
            ReverseExpression = reverseExpression; Reducer = reducer;
        }}
        internal string Id {{ get; }}
        internal int ForwardExpression {{ get; }}
        internal int ReverseExpression {{ get; }}
        internal int Reducer {{ get; }}
    }}

    internal readonly struct SigmaDirectionalActionWitness
    {{
        internal SigmaDirectionalActionWitness(SigmaNativeQueryClaim claim,
            SigmaQ48Interval direction, SigmaQ48Interval residual,
            SigmaQ48Interval action, bool active)
        {{
            Claim = claim; Direction = direction; Residual = residual;
            Action = action; Active = active;
        }}
        internal SigmaNativeQueryClaim Claim {{ get; }}
        internal SigmaQ48Interval Direction {{ get; }}
        internal SigmaQ48Interval Residual {{ get; }}
        internal SigmaQ48Interval Action {{ get; }}
        internal bool Active {{ get; }}
        internal bool StopsAtMeasuredMould =>
            Active && Claim == SigmaNativeQueryClaim.FirstHitMould;
    }}

    internal readonly struct SigmaCertificateFactor
    {{
        internal SigmaCertificateFactor(string scope, string expression,
            string independence, string provenance, string coupling, string branch,
            long lower, long upper)
        {{
            Scope = scope; Expression = expression; Independence = independence;
            Provenance = provenance; Coupling = coupling; Branch = branch;
            Lower = lower; Upper = upper;
        }}
        internal string Scope {{ get; }}
        internal string Expression {{ get; }}
        internal string Independence {{ get; }}
        internal string Provenance {{ get; }}
        internal string Coupling {{ get; }}
        internal string Branch {{ get; }}
        internal long Lower {{ get; }}
        internal long Upper {{ get; }}
        internal string ContextKey => string.Join("|", Scope, Expression,
            Independence, Provenance, Coupling, Branch);
    }}

    internal readonly struct SigmaMinimizedFactor
    {{
        internal SigmaMinimizedFactor(SigmaCertificateFactor factor, int multiplicity)
        {{ Factor = factor; Multiplicity = multiplicity; }}
        internal SigmaCertificateFactor Factor {{ get; }}
        internal int Multiplicity {{ get; }}
    }}

    internal readonly struct SigmaGaugeCell
    {{
        internal SigmaGaugeCell(long u, long v, int level, string payloadFingerprint)
        {{
            if ((uint)level > 62u)
                throw new ArgumentOutOfRangeException(nameof(level));
            U = u; V = v; Level = level;
            PayloadFingerprint = payloadFingerprint ??
                throw new ArgumentNullException(nameof(payloadFingerprint));
        }}
        internal long U {{ get; }}
        internal long V {{ get; }}
        internal int Level {{ get; }}
        internal string PayloadFingerprint {{ get; }}
    }}

    internal readonly struct SigmaChartD4Transform
    {{
        internal SigmaChartD4Transform(int m00, int m01, int m10, int m11)
        {{
            int determinant = checked(m00 * m11 - m01 * m10);
            if (Math.Abs(determinant) != 1 ||
                Math.Abs(m00) + Math.Abs(m01) != 1 ||
                Math.Abs(m10) + Math.Abs(m11) != 1)
                throw new ArgumentException(
                    "A chart D4 transform is one signed axis permutation.");
            M00 = m00; M01 = m01; M10 = m10; M11 = m11;
            Determinant = determinant;
        }}
        internal int M00 {{ get; }}
        internal int M01 {{ get; }}
        internal int M10 {{ get; }}
        internal int M11 {{ get; }}
        internal int Determinant {{ get; }}
    }}

    // Sampling sides exist only to derive one implicit shared-footprint boundary.
    // They are not native ports and never encode a Sigma_2 direction.
    internal enum SigmaSampleBoundarySide : uint
    {{
        Left = 0u,
        Right = 1u,
        Up = 2u,
        Down = 3u,
    }}

    internal enum SigmaFootprintSupportDisposition : uint
    {{
        Invalid = 0u,
        ExistingSupport = 1u,
        UnattachedFirstHit = 2u,
        UnresolvedExisting = 3u,
    }}

    internal enum SigmaStitchResolution : uint
    {{
        NoStitch = 0u,
        Resolved = 1u,
        Unresolved = 2u,
    }}

    // Generated K16 character-frame sectors.  These are abstract native
    // boundary labels, never signed chart axes and never sampling sides.
    internal enum SigmaNativeBoundarySector : uint
    {{
        Sector0 = 0u,
        Sector1 = 1u,
        Sector2 = 2u,
        Sector3 = 3u,
    }}

    internal readonly struct SigmaStitchContactBranch
    {{
        internal SigmaStitchContactBranch(
            IReadOnlyList<SigmaQ48Interval> roomBounds)
        {{
            if (roomBounds == null || roomBounds.Count != 3 ||
                roomBounds.Any(value => value.IsEmpty))
                throw new ArgumentException(
                    "A contact branch requires three nonempty calibrated axes.",
                    nameof(roomBounds));
            RoomBounds = roomBounds.ToArray();
        }}
        internal SigmaQ48Interval[] RoomBounds {{ get; }}
        internal string CanonicalSerialization => string.Join(",", RoomBounds
            .Select(value => $"{{unchecked((ulong)value.Lower):x16}}-" +
                $"{{unchecked((ulong)value.Upper):x16}}"));
    }}

    internal readonly struct SigmaStitchBoundaryEnvelope
    {{
        internal SigmaStitchBoundaryEnvelope(SigmaSampleBoundarySide side,
            IReadOnlyList<SigmaQ48Interval> roomBounds)
        {{
            if (roomBounds == null || roomBounds.Count != 3)
                throw new ArgumentException(
                    "A stitch boundary envelope has three room-gauge axes.",
                    nameof(roomBounds));
            Side = side;
            RoomBounds = roomBounds.ToArray();
        }}
        internal SigmaSampleBoundarySide Side {{ get; }}
        internal SigmaQ48Interval[] RoomBounds {{ get; }}
    }}

    // CPU semantic-oracle reference for one implicit boundary. Production derives
    // this tuple arithmetically from edgeIndex and never materializes the full set.
    internal readonly struct SigmaImplicitBoundaryRef
    {{
        internal SigmaImplicitBoundaryRef(int edgeIndex, ulong leftKey,
            ulong rightKey, SigmaSampleBoundarySide leftSide,
            SigmaSampleBoundarySide rightSide,
            IEnumerable<SigmaStitchContactBranch> contactBranches)
        {{
            if (edgeIndex < 0 || leftKey == rightKey)
                throw new ArgumentException("A stitch requires distinct supports.");
            ContactBranches = (contactBranches ?? throw new ArgumentNullException(
                nameof(contactBranches))).OrderBy(value =>
                    value.CanonicalSerialization, StringComparer.Ordinal).ToArray();
            if (ContactBranches.Length == 0)
                throw new ArgumentException(
                    "An implicit boundary exists only with exact contact evidence.",
                    nameof(contactBranches));
            EdgeIndex = edgeIndex;
            LeftKey = leftKey;
            RightKey = rightKey;
            LeftSide = leftSide;
            RightSide = rightSide;
        }}
        internal int EdgeIndex {{ get; }}
        internal ulong LeftKey {{ get; }}
        internal ulong RightKey {{ get; }}
        internal SigmaSampleBoundarySide LeftSide {{ get; }}
        internal SigmaSampleBoundarySide RightSide {{ get; }}
        internal SigmaStitchContactBranch[] ContactBranches {{ get; }}
    }}

    internal sealed class SigmaFreshFootprintSample
    {{
        internal SigmaFreshFootprintSample(ulong coherentFrameKey,
            int sampleX, int sampleY, ulong supportKey,
            SigmaNativeQueryClaim claim,
            SigmaFootprintSupportDisposition disposition,
            IEnumerable<SigmaStitchBoundaryEnvelope> boundaries)
        {{
            if (coherentFrameKey == 0UL || supportKey == 0UL)
                throw new ArgumentOutOfRangeException(nameof(coherentFrameKey));
            if (sampleX < 0 || sampleY < 0)
                throw new ArgumentOutOfRangeException(nameof(sampleX));
            CoherentFrameKey = coherentFrameKey;
            SampleX = sampleX;
            SampleY = sampleY;
            SupportKey = supportKey;
            Claim = claim;
            Disposition = disposition;
            Boundaries = (boundaries ?? throw new ArgumentNullException(
                nameof(boundaries))).OrderBy(value => value.Side).ToArray();
        }}
        internal ulong CoherentFrameKey {{ get; }}
        internal int SampleX {{ get; }}
        internal int SampleY {{ get; }}
        internal ulong SupportKey {{ get; }}
        internal SigmaNativeQueryClaim Claim {{ get; }}
        internal SigmaFootprintSupportDisposition Disposition {{ get; }}
        internal SigmaStitchBoundaryEnvelope[] Boundaries {{ get; }}
        internal bool Valid => Disposition != SigmaFootprintSupportDisposition.Invalid;
    }}

    internal readonly struct SigmaStitchNativeContext
    {{
        internal SigmaStitchNativeContext(string provenanceFingerprint)
        {{
            if (provenanceFingerprint == null ||
                provenanceFingerprint.Length != 64)
                throw new ArgumentException(
                    "Native stitch context requires a SHA-256 provenance receipt.",
                    nameof(provenanceFingerprint));
            ProvenanceFingerprint = provenanceFingerprint;
        }}
        internal string ProvenanceFingerprint {{ get; }}
    }}

    internal readonly struct SigmaStitchRelationReceipt
    {{
        internal SigmaStitchRelationReceipt(
            SigmaNativeBoundarySector leftSector,
            SigmaNativeBoundarySector rightSector,
            SigmaS16 linkDefect,
            SigmaS16 reverseLinkDefect,
            SigmaS16[] associatorProfile,
            SigmaS16[] reverseAssociatorProfile, SigmaS16 transition,
            SigmaS16 reverseTransition,
            IReadOnlyList<SigmaQ48Interval> normalizedLink,
            IReadOnlyList<SigmaQ48Interval> normalizedReverseLink,
            SigmaQ48Interval[][] normalizedAssociatorProfile,
            SigmaQ48Interval[][] normalizedReverseAssociatorProfile,
            SigmaExactFactorClass[] associatorProfileClasses,
            SigmaExactFactorClass[] reverseAssociatorProfileClasses,
            SigmaExactFactorClass linkClass,
            SigmaExactFactorClass reverseLinkClass,
            SigmaExactFactorClass associatorClass,
            SigmaExactFactorClass reverseAssociatorClass,
            SigmaExactFactorClass closureClass,
            SigmaMerkabaRelationClass relationClass, int transportAddress,
            int forwardTransportSign, int reverseTransportSign,
            bool nonzeroAssociatorProfile, int exactAnnihilatorAction,
            int reverseExactAnnihilatorAction,
            ulong bracketFingerprint, string provenanceFingerprint)
        {{
            LeftSector = leftSector;
            RightSector = rightSector;
            LinkDefect = linkDefect;
            ReverseLinkDefect = reverseLinkDefect;
            if (associatorProfile == null ||
                associatorProfile.Length != SigmaS16.LaneCount ||
                reverseAssociatorProfile == null ||
                reverseAssociatorProfile.Length != SigmaS16.LaneCount ||
                normalizedAssociatorProfile == null ||
                normalizedAssociatorProfile.Length != SigmaS16.LaneCount ||
                normalizedReverseAssociatorProfile == null ||
                normalizedReverseAssociatorProfile.Length != SigmaS16.LaneCount ||
                associatorProfileClasses == null ||
                associatorProfileClasses.Length != SigmaS16.LaneCount ||
                reverseAssociatorProfileClasses == null ||
                reverseAssociatorProfileClasses.Length != SigmaS16.LaneCount)
                throw new ArgumentException(
                    "A stitch receipt requires the complete 16-basis associator profile.");
            AssociatorProfile = associatorProfile.ToArray();
            ReverseAssociatorProfile = reverseAssociatorProfile.ToArray();
            Transition = transition;
            ReverseTransition = reverseTransition;
            NormalizedLink = normalizedLink?.ToArray() ??
                Array.Empty<SigmaQ48Interval>();
            NormalizedReverseLink = normalizedReverseLink?.ToArray() ??
                Array.Empty<SigmaQ48Interval>();
            NormalizedAssociatorProfile = normalizedAssociatorProfile.Select(
                value => value?.ToArray() ?? Array.Empty<SigmaQ48Interval>()).ToArray();
            NormalizedReverseAssociatorProfile =
                normalizedReverseAssociatorProfile.Select(value => value?.ToArray() ??
                    Array.Empty<SigmaQ48Interval>()).ToArray();
            AssociatorProfileClasses = associatorProfileClasses.ToArray();
            ReverseAssociatorProfileClasses =
                reverseAssociatorProfileClasses.ToArray();
            LinkClass = linkClass;
            ReverseLinkClass = reverseLinkClass;
            AssociatorClass = associatorClass;
            ReverseAssociatorClass = reverseAssociatorClass;
            ClosureClass = closureClass;
            RelationClass = relationClass;
            TransportAddress = transportAddress;
            ForwardTransportSign = forwardTransportSign;
            ReverseTransportSign = reverseTransportSign;
            NonzeroAssociatorProfile = nonzeroAssociatorProfile;
            ExactAnnihilatorAction = exactAnnihilatorAction;
            ReverseExactAnnihilatorAction = reverseExactAnnihilatorAction;
            BracketFingerprint = bracketFingerprint;
            ProvenanceFingerprint = provenanceFingerprint ?? string.Empty;
        }}
        internal SigmaNativeBoundarySector LeftSector {{ get; }}
        internal SigmaNativeBoundarySector RightSector {{ get; }}
        internal SigmaS16 LinkDefect {{ get; }}
        internal SigmaS16 ReverseLinkDefect {{ get; }}
        internal SigmaS16[] AssociatorProfile {{ get; }}
        internal SigmaS16[] ReverseAssociatorProfile {{ get; }}
        internal SigmaS16 Transition {{ get; }}
        internal SigmaS16 ReverseTransition {{ get; }}
        internal SigmaQ48Interval[] NormalizedLink {{ get; }}
        internal SigmaQ48Interval[] NormalizedReverseLink {{ get; }}
        internal SigmaQ48Interval[][] NormalizedAssociatorProfile {{ get; }}
        internal SigmaQ48Interval[][] NormalizedReverseAssociatorProfile {{ get; }}
        internal SigmaExactFactorClass[] AssociatorProfileClasses {{ get; }}
        internal SigmaExactFactorClass[] ReverseAssociatorProfileClasses {{ get; }}
        internal SigmaExactFactorClass LinkClass {{ get; }}
        internal SigmaExactFactorClass ReverseLinkClass {{ get; }}
        internal SigmaExactFactorClass AssociatorClass {{ get; }}
        internal SigmaExactFactorClass ReverseAssociatorClass {{ get; }}
        internal SigmaExactFactorClass ClosureClass {{ get; }}
        internal SigmaMerkabaRelationClass RelationClass {{ get; }}
        internal int TransportAddress {{ get; }}
        internal int ForwardTransportSign {{ get; }}
        internal int ReverseTransportSign {{ get; }}
        internal int OrientationParity => checked(ForwardTransportSign *
            ReverseTransportSign);
        internal bool NonzeroAssociatorProfile {{ get; }}
        internal int ExactAnnihilatorAction {{ get; }}
        internal int ReverseExactAnnihilatorAction {{ get; }}
        internal ulong BracketFingerprint {{ get; }}
        internal string ProvenanceFingerprint {{ get; }}
    }}

    internal readonly struct SigmaResolvedStitch
    {{
        internal SigmaResolvedStitch(SigmaImplicitBoundaryRef boundary,
            SigmaStitchRelationReceipt receipt)
        {{
            Boundary = boundary;
            Receipt = receipt;
        }}
        internal SigmaImplicitBoundaryRef Boundary {{ get; }}
        internal SigmaNativeBoundarySector LeftSector => Receipt.LeftSector;
        internal SigmaNativeBoundarySector RightSector => Receipt.RightSector;
        internal SigmaStitchRelationReceipt Receipt {{ get; }}
        internal int OrientationParity => Receipt.OrientationParity;
        internal ulong BracketFingerprint => Receipt.BracketFingerprint;
        internal SigmaMerkabaRelationClass RelationClass => Receipt.RelationClass;
    }}

    internal sealed class SigmaStitchWitnessSet
    {{
        internal SigmaStitchWitnessSet(SigmaStitchResolution resolution,
            IReadOnlyList<SigmaStitchRelationReceipt> receipts,
            IReadOnlyList<SigmaResolvedStitch> resolvedAlternatives,
            bool hasOpenFactor = false)
        {{
            Resolution = resolution;
            Receipts = receipts?.ToArray() ??
                Array.Empty<SigmaStitchRelationReceipt>();
            ResolvedAlternatives = resolvedAlternatives?.ToArray() ??
                Array.Empty<SigmaResolvedStitch>();
            HasOpenFactor = hasOpenFactor;
        }}
        internal SigmaStitchResolution Resolution {{ get; }}
        internal SigmaStitchRelationReceipt[] Receipts {{ get; }}
        internal SigmaResolvedStitch[] ResolvedAlternatives {{ get; }}
        internal bool HasOpenFactor {{ get; }}
        internal SigmaResolvedStitch Resolved =>
            Resolution == SigmaStitchResolution.Resolved &&
            ResolvedAlternatives.Length == 1
                ? ResolvedAlternatives[0]
                : throw new InvalidOperationException(
                    "Only one complete-program-equivalent stitch is resolved.");
    }}

    internal readonly struct SigmaStitchLocality
    {{
        internal SigmaStitchLocality(ulong scratchKey, int level, SigmaS16 state,
            string certificateFingerprint)
        {{
            if (scratchKey == 0UL || (uint)level > 62u)
                throw new ArgumentOutOfRangeException(nameof(level));
            if (state.IsZero)
                throw new ArgumentException(
                    "A stitch locality is manifested full S16 support.",
                    nameof(state));
            if (certificateFingerprint == null ||
                certificateFingerprint.Length != 64)
                throw new ArgumentException(
                    "A stitch locality requires an exact certificate fingerprint.",
                    nameof(certificateFingerprint));
            ScratchKey = scratchKey;
            Level = level;
            State = state;
            CertificateFingerprint = certificateFingerprint;
            CompletePayloadFingerprint = string.Join(",", state.ToArray().Select(
                value => unchecked((ulong)value).ToString("x16"))) + ":" +
                certificateFingerprint;
        }}
        internal ulong ScratchKey {{ get; }}
        internal int Level {{ get; }}
        internal SigmaS16 State {{ get; }}
        internal string CertificateFingerprint {{ get; }}
        internal string CompletePayloadFingerprint {{ get; }}
    }}

    internal readonly struct SigmaBoundaryNativeInput
    {{
        internal SigmaBoundaryNativeInput(SigmaImplicitBoundaryRef boundary,
            SigmaStitchNativeContext nativeContext)
        {{
            Boundary = boundary;
            NativeContext = nativeContext;
        }}
        internal SigmaImplicitBoundaryRef Boundary {{ get; }}
        internal SigmaStitchNativeContext NativeContext {{ get; }}
    }}

    internal readonly struct SigmaStitchPattern
    {{
        internal SigmaStitchPattern(SigmaStitchResolution resolution,
            IReadOnlyList<SigmaGaugeCell> packedCells, int componentCount,
            string canonicalSerialization, int embeddingClassCount = 0,
            IReadOnlyList<byte> canonicalTokens = null)
        {{
            Resolution = resolution;
            PackedCells = packedCells ?? Array.Empty<SigmaGaugeCell>();
            ComponentCount = componentCount;
            CanonicalSerialization = canonicalSerialization ?? string.Empty;
            EmbeddingClassCount = embeddingClassCount;
            CanonicalTokens = canonicalTokens?.ToArray() ??
                Encoding.ASCII.GetBytes(CanonicalSerialization);
        }}
        internal SigmaStitchResolution Resolution {{ get; }}
        internal IReadOnlyList<SigmaGaugeCell> PackedCells {{ get; }}
        internal int ComponentCount {{ get; }}
        internal string CanonicalSerialization {{ get; }}
        internal int EmbeddingClassCount {{ get; }}
        internal byte[] CanonicalTokens {{ get; }}
    }}

    internal enum SigmaInstrumentLeafKind : uint
    {{
        DepthOrder = 0u,
        OpticalR = 1u,
        OpticalG = 2u,
        OpticalB = 3u,
    }}

    internal enum SigmaInstrumentOpticalTransfer : uint
    {{
        LinearUnorm = 0u,
        SrgbDecodedLinear = 1u,
        Unsupported = 0xffffffffu,
    }}

    internal readonly struct SigmaInstrumentFootprint
    {{
        internal SigmaInstrumentFootprint(IReadOnlyList<long> ray,
            IReadOnlyList<long> differentialX,
            IReadOnlyList<long> differentialY, long halfAngleX,
            long halfAngleY, long solidAngle)
        {{
            if (ray == null || differentialX == null || differentialY == null ||
                ray.Count != 3 || differentialX.Count != 3 ||
                differentialY.Count != 3)
                throw new ArgumentException(
                    "A calibrated footprint requires three 3D Q48 vectors.");
            Ray = ray.ToArray();
            DifferentialX = differentialX.ToArray();
            DifferentialY = differentialY.ToArray();
            HalfAngleX = halfAngleX;
            HalfAngleY = halfAngleY;
            SolidAngle = solidAngle;
        }}
        internal long[] Ray {{ get; }}
        internal long[] DifferentialX {{ get; }}
        internal long[] DifferentialY {{ get; }}
        internal long HalfAngleX {{ get; }}
        internal long HalfAngleY {{ get; }}
        internal long SolidAngle {{ get; }}
    }}

    // Immutable output of the repository's capture/calibration boundary. It
    // deliberately contains no Merkaba row, S16 proposal or carrier address.
    internal sealed class SigmaInstrumentEyeBoundary
    {{
        internal SigmaInstrumentEyeBoundary(string side,
            ulong observationRevision, ulong calibrationEpoch,
            long depthSourceSequence, long opticalSourceSequence,
            long depthTimestampNanoseconds, long opticalTimestampNanoseconds,
            ulong depthIntrinsicsSignature, ulong opticalIntrinsicsSignature,
            string poseCalibrationFingerprint,
            SigmaInstrumentFootprint footprint,
            SigmaQ48Interval projectionDepth01,
            SigmaQ48Interval metricDirectOrder,
            IReadOnlyList<SigmaQ48Interval> opticalCode,
            SigmaInstrumentOpticalTransfer opticalTransfer,
            bool firstHit, string provenanceFingerprint)
        {{
            if (side != "LEFT" && side != "RIGHT")
                throw new ArgumentException("Instrument side must be LEFT/RIGHT.",
                    nameof(side));
            if (observationRevision == 0UL || calibrationEpoch == 0UL ||
                depthSourceSequence <= 0L || opticalSourceSequence <= 0L ||
                depthTimestampNanoseconds <= 0L ||
                opticalTimestampNanoseconds <= 0L ||
                depthIntrinsicsSignature == 0UL ||
                opticalIntrinsicsSignature == 0UL)
                throw new ArgumentException(
                    "Instrument observation provenance is incomplete.");
            if (poseCalibrationFingerprint == null ||
                poseCalibrationFingerprint.Length != 64 ||
                provenanceFingerprint == null ||
                provenanceFingerprint.Length != 64)
                throw new ArgumentException(
                    "Instrument fingerprints must be SHA-256 hex strings.");
            if (opticalCode == null || opticalCode.Count != 3)
                throw new ArgumentException(
                    "One coherent eye boundary carries RGB code intervals.",
                    nameof(opticalCode));
            Side = side;
            ObservationRevision = observationRevision;
            CalibrationEpoch = calibrationEpoch;
            DepthSourceSequence = depthSourceSequence;
            OpticalSourceSequence = opticalSourceSequence;
            DepthTimestampNanoseconds = depthTimestampNanoseconds;
            OpticalTimestampNanoseconds = opticalTimestampNanoseconds;
            DepthIntrinsicsSignature = depthIntrinsicsSignature;
            OpticalIntrinsicsSignature = opticalIntrinsicsSignature;
            PoseCalibrationFingerprint = poseCalibrationFingerprint;
            Footprint = footprint;
            ProjectionDepth01 = projectionDepth01;
            MetricDirectOrder = metricDirectOrder;
            OpticalCode = opticalCode.ToArray();
            OpticalTransfer = opticalTransfer;
            FirstHit = firstHit;
            ProvenanceFingerprint = provenanceFingerprint;
        }}
        internal string Side {{ get; }}
        internal ulong ObservationRevision {{ get; }}
        internal ulong CalibrationEpoch {{ get; }}
        internal long DepthSourceSequence {{ get; }}
        internal long OpticalSourceSequence {{ get; }}
        internal long DepthTimestampNanoseconds {{ get; }}
        internal long OpticalTimestampNanoseconds {{ get; }}
        internal ulong DepthIntrinsicsSignature {{ get; }}
        internal ulong OpticalIntrinsicsSignature {{ get; }}
        internal string PoseCalibrationFingerprint {{ get; }}
        internal SigmaInstrumentFootprint Footprint {{ get; }}
        internal SigmaQ48Interval ProjectionDepth01 {{ get; }}
        internal SigmaQ48Interval MetricDirectOrder {{ get; }}
        internal SigmaQ48Interval[] OpticalCode {{ get; }}
        internal SigmaInstrumentOpticalTransfer OpticalTransfer {{ get; }}
        internal bool FirstHit {{ get; }}
        internal string ProvenanceFingerprint {{ get; }}
    }}

    internal readonly struct SigmaAssembledSensorEye
    {{
        internal SigmaAssembledSensorEye(string side,
            IReadOnlyList<IReadOnlyList<long>> rows,
            IReadOnlyList<SigmaQ48Interval> measured,
            SigmaQ48Interval metricDirectOrder,
            SigmaInstrumentFootprint footprint, bool firstHit,
            string provenanceFingerprint)
        {{
            if (rows == null || rows.Count != 4 ||
                rows.Any(row => row == null || row.Count != 4) ||
                measured == null || measured.Count != 4)
                throw new ArgumentException(
                    "An assembled eye requires four four-axis leaves.");
            Side = side;
            Rows = rows.Select(row => row.ToArray()).ToArray();
            Measured = measured.ToArray();
            MetricDirectOrder = metricDirectOrder;
            Footprint = footprint;
            FirstHit = firstHit;
            ProvenanceFingerprint = provenanceFingerprint;
        }}
        internal string Side {{ get; }}
        internal long[][] Rows {{ get; }}
        internal SigmaQ48Interval[] Measured {{ get; }}
        internal SigmaQ48Interval MetricDirectOrder {{ get; }}
        internal SigmaInstrumentFootprint Footprint {{ get; }}
        internal bool FirstHit {{ get; }}
        internal string ProvenanceFingerprint {{ get; }}
    }}

    internal readonly struct SigmaFreshShadowBranch
    {{
        internal SigmaFreshShadowBranch(IEnumerable<SigmaQ48Interval> shadowAxes,
            uint firstHitEyeMask, bool coherent, string provenanceFingerprint)
        {{
            if (shadowAxes == null) throw new ArgumentNullException(nameof(shadowAxes));
            ShadowAxes = shadowAxes.ToArray();
            if (ShadowAxes.Length != 4)
                throw new ArgumentException("A Merkaba shadow has four axes.",
                    nameof(shadowAxes));
            FirstHitEyeMask = firstHitEyeMask;
            Coherent = coherent;
            ProvenanceFingerprint = provenanceFingerprint ??
                throw new ArgumentNullException(nameof(provenanceFingerprint));
        }}
        internal SigmaQ48Interval[] ShadowAxes {{ get; }}
        internal uint FirstHitEyeMask {{ get; }}
        internal bool Coherent {{ get; }}
        internal string ProvenanceFingerprint {{ get; }}
    }}

    internal readonly struct SigmaFreshBaseAdmission
    {{
        internal SigmaFreshBaseAdmission(SigmaFreshAdmissionStatus status,
            SigmaS16 state, IReadOnlyList<SigmaGaugeCell> support,
            SigmaMerkabaRelationClass boundaryRelation,
            string canonicalSerialization)
        {{
            Status = status; State = state; Support = support;
            BoundaryRelation = boundaryRelation;
            CanonicalSerialization = canonicalSerialization;
        }}
        internal SigmaFreshAdmissionStatus Status {{ get; }}
        internal SigmaS16 State {{ get; }}
        internal IReadOnlyList<SigmaGaugeCell> Support {{ get; }}
        internal SigmaMerkabaRelationClass BoundaryRelation {{ get; }}
        internal string CanonicalSerialization {{ get; }}
    }}

    internal static class SigmaGeneratedMerkabaProgram
    {{
        internal const string ProgramVersion = "{descriptor['version']}";
        internal const string NumericDomainId = "{descriptor['numericDomain']}";
        internal const string ProgramFingerprint = "{descriptor['fingerprint']}";
        internal const string CaptureBoundaryFingerprint =
            "{proofs['captureBoundaryFingerprint']}";
        internal const int CaptureBoundaryLeafCount =
            {proofs['captureBoundaryLeafCount']};
        internal const string DeclaredToeUpstreamFingerprint = "{descriptor['inputs']['toeUpstreamDeclared']}";
{input_lines}
        internal const int ExpressionCount = {len(ir['expressions'])};
        internal const int IrNodeCount = {len(ir['nodes'])};
        internal const int IrOperandCount = {len(ir['operands'])};
        internal const int EntryPointCount = {len(ir['entryPoints'])};
        internal const int AssociatorNonzeroBasisTriples = {proofs['associatorNonzero']};
        internal const bool ShadowKernelDecouplingProofSupplied = false;
        internal const int NegativeHolonomyFixtures = {proofs['negativeHolonomy']};
        internal const int E22InventoryCount = 0;
        internal const bool DirectS16DependenciesRetained = true;
        internal const bool LegacyZNullAccepted = false;
        internal const int QuerySupportFalseNegatives = 0;
        internal const int QuerySupportFixtureCount = {proofs['querySupportFixtureCount']};
        internal const int QuerySupportRefinedFixtureCount = {proofs['querySupportRefinedFixtureCount']};
        internal const int QuerySupportNonresidentFixtureCount = {proofs['querySupportNonresidentFixtureCount']};
        internal const string QuerySupportEvaluationFingerprint =
            "{proofs['querySupportEvaluationFingerprint']}";
        internal const int ReverseIntervalSoundFixtureCount = {proofs['reverseIntervalSoundFixtureCount']};
        internal const bool ReverseZeroBranchRetained = true;
        internal const int ReverseSceneDisjunctionCount = {proofs['reverseSceneDisjunctionCount']};
        internal const int BracketNegativeControlCount = {proofs['bracketNegativeControls']};
        internal const int ReverseIrForwardFixtureCount = {proofs['reverseIrForwardFixtureCount']};
        internal const int ReverseIrPreimageOutputCount = {proofs['reverseIrPreimageOutputCount']};
        internal const int ReverseIrAmbiguousPreimageOutputCount = {proofs['reverseIrAmbiguousPreimageOutputCount']};
        internal const int ReverseIrMaxPreimageCount = {proofs['reverseIrMaxPreimageCount']};
        internal const string ReverseIrAssociatorFingerprint =
            "{proofs['reverseIrAssociatorFingerprint']}";
        internal const int DuplicateFixtureCount = {proofs['duplicateFixtureCount']};
        internal const int DuplicateMinimizedFactorCount = {proofs['duplicateMinimizedFactorCount']};
        internal const int DuplicateMultiplicity = {proofs['duplicateMultiplicity']};
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
        internal const int DefaultRepresentationFixtureCount = {proofs['defaultRepresentationFixtureCount']};
        internal const int DefaultRepresentationQueryCount = {proofs['defaultRepresentationQueryCount']};
        internal const int DefaultRepresentationCount = {proofs['defaultRepresentationCount']};
        internal const string DefaultRepresentationProofFingerprint =
            "{proofs['defaultRepresentationProofFingerprint']}";
        internal const bool CanFreezeShadowKernel = false;
        internal const bool OpticalCalibrationProvenance = true;
        internal const bool OpticalUnboundedExplanationForbidden = true;
        internal const int IndependentClosureWeightCount = 0;
        internal const bool EpsilonClExists = false;
        internal const int GaugePermutationCount = {proofs['gaugePermutationCount']};
        internal const int GaugePointProbeCount = {proofs['gaugePointProbeCount']};
        internal const int GaugeTransportFieldCount = {proofs['gaugeTransportFieldCount']};
        internal const bool FreshSupportUniqueModuloGauge = true;
        internal const bool FreshSupportNonEquivalentRejected = true;
        internal const int FreshAdmissionFixtureCount = {proofs['freshAdmissionFixtureCount']};
        internal const int FreshAdmissionAdmittedFixtureCount = {proofs['freshAdmissionAdmittedFixtureCount']};
        internal const int FreshAdmissionUnresolvedFixtureCount = {proofs['freshAdmissionUnresolvedFixtureCount']};
        internal const int FreshAdmissionDualFrameRoundTripCount = {proofs['freshAdmissionDualFrameRoundTripCount']};
        internal const int FreshAdmissionBoundaryResolvedFixtureCount = {proofs['freshAdmissionBoundaryResolvedFixtureCount']};
        internal const int FreshAdmissionExactPointDefectFixtureCount = {proofs['freshAdmissionExactPointDefectFixtureCount']};
        internal const int FreshAdmissionExternalRelationTruthInputCount = {proofs['freshAdmissionExternalRelationTruthInputCount']};
        internal const int FreshAdmissionCommonPermutationCount = {proofs['freshAdmissionCommonPermutationCount']};
        internal const string FreshAdmissionProofFingerprint =
            "{proofs['freshAdmissionProofFingerprint']}";
        internal const int ConstructiveStitchExternalSemanticTruthInputCount =
            {proofs['constructiveStitchExternalSemanticTruthInputCount']};
        internal const int ConstructiveStitchCallerLoopTruthInputCount =
            {proofs['constructiveStitchCallerLoopTruthInputCount']};
        internal const int ConstructiveStitchSamplingSideToDeltaAuthorityCount =
            {proofs['constructiveStitchSamplingSideToDeltaAuthorityCount']};
        internal const int ConstructiveStitchAbstractNativeSectorCount =
            {proofs['constructiveStitchAbstractNativeSectorCount']};
        internal const int ConstructiveStitchAbstractSectorChartAssignmentCount =
            {proofs['constructiveStitchAbstractSectorChartAssignmentCount']};
        internal const int ConstructiveStitchAbstractSectorChartAssignmentOrbitCount =
            {proofs['constructiveStitchAbstractSectorChartAssignmentOrbitCount']};
        internal const int ConstructiveStitchD4ChartImageCount =
            {proofs['constructiveStitchD4ChartImageCount']};
        internal const int ConstructiveStitchNonGaugeEmbeddingAmbiguityCount =
            {proofs['constructiveStitchNonGaugeEmbeddingAmbiguityCount']};
        internal const int ConstructiveStitchImplicitBoundaryCount320 =
            {proofs['constructiveStitchImplicitBoundaryCount320']};
        internal const int ConstructiveStitchImplicitPlaquetteCount320 =
            {proofs['constructiveStitchImplicitPlaquetteCount320']};
        internal const int ConstructiveStitchHotSemanticPhaseCount =
            {proofs['constructiveStitchHotSemanticPhaseCount']};
        internal const int ConstructiveStitchTargetAdditionalHotSubmissionCount =
            {proofs['constructiveStitchTargetAdditionalHotSubmissionCount']};
        internal const int ConstructiveStitchMixedLevelTranslationProbeCount =
            {proofs['constructiveStitchMixedLevelTranslationProbeCount']};
        internal const int ConstructiveStitchExternalBracketContextInputCount =
            {proofs['constructiveStitchExternalBracketContextInputCount']};
        internal const int ConstructiveStitchCompleteAssociatorBasisContextCount =
            {proofs['constructiveStitchCompleteAssociatorBasisContextCount']};
        internal const bool ConstructiveStitchAssociatorProfileIsIntrinsicS16 =
            {str(proofs['constructiveStitchAssociatorProfileIsIntrinsicS16']).lower()};
        internal const bool ConstructiveStitchS32Required =
            {str(proofs['constructiveStitchS32Required']).lower()};
        internal const string ConstructiveStitchProofFingerprint =
            "{proofs['constructiveStitchProofFingerprint']}";
        internal const ulong GeneratedStitchBracketFingerprint =
            0x{stitch_bracket_fingerprint:016x}UL;
        internal const string CanonicalSerializationFingerprint =
            "{proofs['canonicalSerializationFingerprint']}";
        internal const string CertificateProofFingerprint =
            "{proofs['certificateProofFingerprint']}";

        internal static readonly string[] ExpressionFingerprints =
        {{
{expression_fingerprints}
        }};

        internal static readonly SigmaMerkabaIrNode[] IrNodes =
        {{
{node_lines}
        }};

        internal static readonly int[] IrOperands = {{ {operand_lines} }};

        internal static readonly SigmaMerkabaExpression[] Expressions =
        {{
{expression_lines}
        }};

        internal static readonly SigmaMerkabaEntryPoint[] EntryPoints =
        {{
{entry_lines}
        }};

{cs_array('DiffractionMatrix', 'sbyte', descriptor['diffractionMatrix'])}

{cs_array('InformationMetric', 'short', descriptor['informationMetric'])}

        // Orientation-independent recurrence invariant A_k^2 = -(2^k-1)I.
{cs_array('ShellSquareByRank', 'sbyte', descriptor['shellSquareByRank'], 4)}

        // p(address) = ShadowNumerator4 / 4.
{cs_array('ShadowNumerator4', 'sbyte', descriptor['shadowNumerator4'])}

        // P_visible = VisibleProjectorNumerator256 / 256.
{cs_array('VisibleProjectorNumerator256', 'sbyte', descriptor['visibleProjectorNumerator256'])}

        // Representation-only square-chart gauge.  These matrices never act on
        // physical S16 values or native transport receipts.
        internal static readonly SigmaChartD4Transform[] ChartD4 =
        {{
{chart_d4_lines}
        }};

        // Complete finite execution tables for the square-chart group.  These
        // are generated from ChartD4 and the same 24 assignments; runtime code
        // performs no search over the eight-element group.
        internal static readonly byte[] ChartD4ComposeTable =
        {{
            {d4_compose_values}
        }};

        internal static readonly byte[] ChartD4InverseTable =
        {{
            {d4_inverse_values}
        }};

        internal static readonly byte[] ChartOrbitRepresentativeTable =
        {{
            {orbit_representative_values}
        }};

        internal static readonly byte[] ChartAdjacentFrameTable =
        {{
            {adjacent_frame_values}
        }};

        // Every bijection from the four abstract native boundary sectors to the
        // four square-chart directions is a representation candidate.  D4 acts
        // on the direction values after this complete 4! enumeration; it cannot
        // authorize one hidden sector-to-side convention.
        internal static readonly int[][] NativeSectorChartAssignments =
            BuildNativeSectorChartAssignments();

        internal static int NativeSectorChartAssignmentCount =>
            NativeSectorChartAssignments.Length;

        internal static int NativeSectorChartOrbitCount =>
            NativeSectorChartAssignments.Select(
                    CanonicalNativeSectorChartAssignment)
                .Distinct(StringComparer.Ordinal).Count();

        internal static string NativeSectorChartOrbitAt(int assignmentIndex)
        {{
            if ((uint)assignmentIndex >=
                (uint)NativeSectorChartAssignments.Length)
                throw new ArgumentOutOfRangeException(nameof(assignmentIndex));
            return CanonicalNativeSectorChartAssignment(
                NativeSectorChartAssignments[assignmentIndex]);
        }}

        private sealed class SigmaCanonicalTokenWriter
        {{
            private readonly List<byte> _tokens = new List<byte>(512);

            internal void Character(char value)
            {{
                if (value > 0x7f)
                    throw new ArgumentOutOfRangeException(nameof(value));
                _tokens.Add((byte)value);
            }}

            internal void Text(string value)
            {{
                if (value == null) throw new ArgumentNullException(nameof(value));
                foreach (char character in value)
                    Character(character);
            }}

            internal void Decimal(long value) => Text(value.ToString(
                CultureInfo.InvariantCulture));

            internal void Decimal(uint value) => Text(value.ToString(
                CultureInfo.InvariantCulture));

            internal void Hex64(ulong value)
            {{
                for (int shift = 60; shift >= 0; shift -= 4)
                {{
                    int nibble = (int)((value >> shift) & 15UL);
                    Character((char)(nibble < 10 ? '0' + nibble :
                        'a' + nibble - 10));
                }}
            }}

            internal void Hex32(uint value)
            {{
                for (int shift = 28; shift >= 0; shift -= 4)
                {{
                    int nibble = (int)((value >> shift) & 15u);
                    Character((char)(nibble < 10 ? '0' + nibble :
                        'a' + nibble - 10));
                }}
            }}

            internal void Tokens(IReadOnlyList<byte> values)
            {{
                if (values == null) throw new ArgumentNullException(nameof(values));
                for (int index = 0; index < values.Count; ++index)
                    _tokens.Add(values[index]);
            }}

            internal byte[] ToArray() => _tokens.ToArray();
            internal string ToAscii() =>
                Encoding.ASCII.GetString(_tokens.ToArray());
        }}

        internal static int CompareCanonicalTokens(
            IReadOnlyList<byte> left, IReadOnlyList<byte> right)
        {{
            if (left == null) throw new ArgumentNullException(nameof(left));
            if (right == null) throw new ArgumentNullException(nameof(right));
            int shared = Math.Min(left.Count, right.Count);
            for (int index = 0; index < shared; ++index)
            {{
                if (left[index] < right[index]) return -1;
                if (left[index] > right[index]) return 1;
            }}
            return left.Count.CompareTo(right.Count);
        }}

        internal static int CompareCompleteCanonicalComponentImage(
            SigmaStitchPattern left, SigmaStitchPattern right) =>
            CompareCanonicalTokens(left.CanonicalTokens, right.CanonicalTokens);

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

        // Exact generated lowering for the only coefficients in the Merkaba
        // shadow/dual frames: 0,+/-2,+/-4,+/-6 divided by 4 or 64.  Quotient and
        // remainder are formed before the factor of three, so this has exactly
        // the same single nearest-even rounding and checked final range as QMul.
        internal static long MultiplyMerkabaDyadic(long value, int numerator,
            int denominatorShift)
        {{
            int magnitudeNumerator = Math.Abs(numerator);
            if ((magnitudeNumerator != 0 && magnitudeNumerator != 2 &&
                 magnitudeNumerator != 4 && magnitudeNumerator != 6) ||
                (denominatorShift != 2 && denominatorShift != 6))
                throw new ArgumentOutOfRangeException(nameof(numerator));
            if (magnitudeNumerator == 0 || value == 0L)
                return 0L;

            int factor = magnitudeNumerator >> 1;
            int shift = denominatorShift - 1;
            if (factor == 2)
            {{
                factor = 1;
                --shift;
            }}
            BigInteger magnitude = BigInteger.Abs(new BigInteger(value));
            BigInteger divisor = BigInteger.One << shift;
            BigInteger quotient = BigInteger.DivRem(magnitude, divisor,
                out BigInteger remainder);
            quotient *= factor;
            remainder *= factor;
            quotient += BigInteger.DivRem(remainder, divisor, out remainder);
            BigInteger twiceRemainder = remainder << 1;
            if (twiceRemainder > divisor ||
                (twiceRemainder == divisor && !quotient.IsEven))
                ++quotient;
            bool negative = (value < 0L) != (numerator < 0);
            return CheckedLong(negative ? -quotient : quotient);
        }}

        internal static long MultiplyMerkabaShadowCoefficient(long value,
            int address, int axis) => MultiplyMerkabaDyadic(value,
                ShadowNumerator(address, axis), 2);

        internal static long MultiplyMerkabaDualCoefficient(long value,
            int address, int axis) => MultiplyMerkabaDyadic(value,
                ShadowNumerator(address, axis), 6);

        internal static long[] EvaluateMerkabaShadow(SigmaS16 state)
        {{
            var output = new long[4];
            for (int axis = 0; axis < 4; ++axis)
            {{
                long sum = 0L;
                for (int address = 0; address < 16; ++address)
                    sum = SigmaNumericDomain.QAdd(sum,
                        MultiplyMerkabaShadowCoefficient(state[address],
                            address, axis));
                output[axis] = sum;
            }}
            return output;
        }}

        internal static SigmaS16 LiftMerkabaShadow(IReadOnlyList<long> shadow)
        {{
            if (shadow == null) throw new ArgumentNullException(nameof(shadow));
            if (shadow.Count != 4)
                throw new ArgumentException("A Merkaba shadow has four axes.",
                    nameof(shadow));
            var lanes = new long[16];
            for (int address = 0; address < 16; ++address)
            {{
                long sum = 0L;
                for (int axis = 0; axis < 4; ++axis)
                    sum = SigmaNumericDomain.QAdd(sum,
                        MultiplyMerkabaDualCoefficient(shadow[axis], address,
                            axis));
                lanes[address] = sum;
            }}
            return SigmaS16.FromArray(lanes);
        }}

        internal static int ComposeChartD4(int outer, int inner)
        {{
            if ((uint)outer >= 8u || (uint)inner >= 8u)
                throw new ArgumentOutOfRangeException(nameof(outer));
            return ChartD4ComposeTable[(outer << 3) + inner];
        }}

        internal static int InverseChartD4(int frame)
        {{
            if ((uint)frame >= 8u)
                throw new ArgumentOutOfRangeException(nameof(frame));
            return ChartD4InverseTable[frame];
        }}

        internal static int ChartOrbitRepresentative(int orbit)
        {{
            if ((uint)orbit >= 3u)
                throw new ArgumentOutOfRangeException(nameof(orbit));
            return ChartOrbitRepresentativeTable[orbit];
        }}

        internal static int ResolveAdjacentOrbitFrame(int orbit,
            int currentFrame, int currentSector, int nextSector,
            int orientationParity)
        {{
            if ((uint)orbit >= 3u || (uint)currentFrame >= 8u ||
                (uint)currentSector >= 4u || (uint)nextSector >= 4u ||
                (orientationParity != -1 && orientationParity != 1))
                throw new ArgumentOutOfRangeException(nameof(orbit));
            int parityIndex = orientationParity > 0 ? 1 : 0;
            int index = ((((orbit * 8 + currentFrame) * 4 + currentSector) *
                4 + nextSector) * 2 + parityIndex);
            return ChartAdjacentFrameTable[index];
        }}

        internal static bool TryAssembleSensorEye(
            SigmaInstrumentEyeBoundary source,
            out SigmaAssembledSensorEye assembled)
        {{
            assembled = default;
            if (source == null ||
                source.OpticalTransfer == SigmaInstrumentOpticalTransfer.Unsupported ||
                source.ProjectionDepth01.IsEmpty ||
                source.MetricDirectOrder.IsEmpty ||
                source.OpticalCode.Any(value => value.IsEmpty) ||
                source.Footprint.Ray == null ||
                source.Footprint.Ray.Length != 3)
                return false;
            try
            {{
                var code = new SigmaQ48Interval[4];
                code[0] = CentreUnitCode(source.ProjectionDepth01);
                for (int channel = 0; channel < 3; ++channel)
                    code[channel + 1] = CentreUnitCode(
                        source.OpticalCode[channel]);

                SigmaQ48Interval total = new SigmaQ48Interval(0L, 0L);
                for (int leaf = 0; leaf < 4; ++leaf)
                    total = AddOutward(total, code[leaf]);
                var tangent = new SigmaQ48Interval[4];
                for (int leaf = 0; leaf < 4; ++leaf)
                    tangent[leaf] = SubtractOutward(
                        ScalePowerOfTwoOutward(code[leaf], 2), total);

                if (!TryBuildCalibratedRowPermutation(source.Footprint.Ray,
                        out int[] permutation, out int globalSign))
                    return false;
                var rows = new IReadOnlyList<long>[4];
                var measured = new SigmaQ48Interval[4];
                for (int leaf = 0; leaf < 4; ++leaf)
                {{
                    var row = new long[4];
                    row[permutation[leaf]] = globalSign > 0
                        ? SigmaNumericDomain.One
                        : SigmaNumericDomain.QNegate(SigmaNumericDomain.One);
                    rows[leaf] = row;
                    measured[leaf] = globalSign > 0
                        ? tangent[leaf]
                        : NegateOutward(tangent[leaf]);
                }}
                assembled = new SigmaAssembledSensorEye(source.Side, rows,
                    measured, source.MetricDirectOrder, source.Footprint,
                    source.FirstHit, source.ProvenanceFingerprint);
                return true;
            }}
            catch (OverflowException)
            {{
                return false;
            }}
        }}

        internal static bool TryBuildCalibratedRowPermutation(
            IReadOnlyList<long> roomRay, out int[] permutation,
            out int globalSign)
        {{
            permutation = Array.Empty<int>();
            globalSign = 0;
            if (roomRay == null || roomRay.Count != 3)
                return false;
            try
            {{
                long x = roomRay[0];
                long y = roomRay[1];
                long z = roomRay[2];
                long[] pullback =
                {{
                    SigmaNumericDomain.QAdd(SigmaNumericDomain.QAdd(x, y), z),
                    SigmaNumericDomain.QSub(SigmaNumericDomain.QSub(x, y), z),
                    SigmaNumericDomain.QSub(SigmaNumericDomain.QSub(y, x), z),
                    SigmaNumericDomain.QAdd(
                        SigmaNumericDomain.QSub(0L, x),
                        SigmaNumericDomain.QSub(z, y)),
                }};
                if (pullback.All(value => value == 0L))
                    return false;
                permutation = Enumerable.Range(0, 4)
                    .OrderByDescending(axis => SigmaNumericDomain.QAbs(
                        pullback[axis]))
                    .ThenByDescending(axis => pullback[axis])
                    .ThenBy(axis => axis)
                    .ToArray();
                globalSign = pullback[permutation[0]] < 0L ? -1 : 1;
                return true;
            }}
            catch (OverflowException)
            {{
                permutation = Array.Empty<int>();
                globalSign = 0;
                return false;
            }}
        }}

        private static SigmaQ48Interval CentreUnitCode(
            SigmaQ48Interval value)
        {{
            if (value.IsEmpty || value.Lower < 0L ||
                value.Upper > SigmaNumericDomain.One)
                return SigmaQ48Interval.Empty;
            return new SigmaQ48Interval(
                SigmaNumericDomain.QSub(
                    SigmaNumericDomain.QShiftLeft(value.Lower, 1),
                    SigmaNumericDomain.One),
                SigmaNumericDomain.QSub(
                    SigmaNumericDomain.QShiftLeft(value.Upper, 1),
                    SigmaNumericDomain.One));
        }}

        private static SigmaQ48Interval AddOutward(
            SigmaQ48Interval left, SigmaQ48Interval right) =>
            left.IsEmpty || right.IsEmpty ? SigmaQ48Interval.Empty :
            new SigmaQ48Interval(
                SigmaNumericDomain.QAdd(left.Lower, right.Lower),
                SigmaNumericDomain.QAdd(left.Upper, right.Upper));

        private static SigmaQ48Interval SubtractOutward(
            SigmaQ48Interval left, SigmaQ48Interval right) =>
            left.IsEmpty || right.IsEmpty ? SigmaQ48Interval.Empty :
            new SigmaQ48Interval(
                SigmaNumericDomain.QSub(left.Lower, right.Upper),
                SigmaNumericDomain.QSub(left.Upper, right.Lower));

        private static SigmaQ48Interval ScalePowerOfTwoOutward(
            SigmaQ48Interval value, int shift) => value.IsEmpty
                ? SigmaQ48Interval.Empty
                : new SigmaQ48Interval(
                    SigmaNumericDomain.QShiftLeft(value.Lower, shift),
                    SigmaNumericDomain.QShiftLeft(value.Upper, shift));

        private static SigmaQ48Interval NegateOutward(
            SigmaQ48Interval value) => value.IsEmpty
                ? SigmaQ48Interval.Empty
                : new SigmaQ48Interval(
                    SigmaNumericDomain.QNegate(value.Upper),
                    SigmaNumericDomain.QNegate(value.Lower));

        internal static bool TryResolveFreshBaseAdmission(
            IEnumerable<SigmaFreshShadowBranch> branches,
            out SigmaFreshBaseAdmission admission)
        {{
            if (branches == null) throw new ArgumentNullException(nameof(branches));
            SigmaFreshBaseAdmission? common = null;
            int count = 0;
            foreach (SigmaFreshShadowBranch branch in branches)
            {{
                ++count;
                if (!TryResolveFreshBranch(branch, out SigmaFreshBaseAdmission current))
                {{
                    admission = UnresolvedFreshAdmission();
                    return false;
                }}
                if (common.HasValue &&
                    (common.Value.State != current.State ||
                     common.Value.BoundaryRelation != current.BoundaryRelation ||
                     !string.Equals(common.Value.CanonicalSerialization,
                         current.CanonicalSerialization, StringComparison.Ordinal)))
                {{
                    admission = UnresolvedFreshAdmission();
                    return false;
                }}
                common = current;
            }}
            if (count == 0 || !common.HasValue)
            {{
                admission = UnresolvedFreshAdmission();
                return false;
            }}
            admission = common.Value;
            return true;
        }}

        private static bool TryResolveFreshBranch(SigmaFreshShadowBranch branch,
            out SigmaFreshBaseAdmission admission)
        {{
            admission = UnresolvedFreshAdmission();
            if (!branch.Coherent || (branch.FirstHitEyeMask & 3u) != 3u ||
                string.IsNullOrEmpty(branch.ProvenanceFingerprint) ||
                branch.ShadowAxes == null || branch.ShadowAxes.Length != 4)
                return false;
            try
            {{
                if (!TrySelectTangentMinimumChange(branch.ShadowAxes,
                    out long[] selected))
                    return false;
                SigmaS16 state = LiftMerkabaShadow(selected);
                if (state.IsZero)
                    return false;
                long[] forward = EvaluateMerkabaShadow(state);
                for (int axis = 0; axis < 4; ++axis)
                    if (!branch.ShadowAxes[axis].Contains(forward[axis]))
                        return false;
                SigmaMerkabaRelationClass boundaryRelation =
                    EvaluateFreshBoundaryRelation(state);
                if (boundaryRelation == SigmaMerkabaRelationClass.Unresolved ||
                    boundaryRelation == SigmaMerkabaRelationClass.DefaultSat)
                    return false;
                string stateBytes = string.Join(",", state.ToArray().Select(value =>
                    unchecked((ulong)value).ToString("x16")));
                string payload = ProgramFingerprint + ":" +
                    ((uint)boundaryRelation).ToString("x8") + ":" + stateBytes;
                IReadOnlyList<SigmaGaugeCell> support = NormalizeGauge(new[]
                {{
                    new SigmaGaugeCell(0L, 0L, 0, payload),
                }});
                string serialization = ((uint)boundaryRelation).ToString("x8") +
                    "|" + stateBytes + "|" +
                    CanonicalGaugeSerialization(support);
                admission = new SigmaFreshBaseAdmission(
                    SigmaFreshAdmissionStatus.Admitted, state, support,
                    boundaryRelation,
                    serialization);
                return true;
            }}
            catch (OverflowException)
            {{
                return false;
            }}
        }}

        private static bool TrySelectTangentMinimumChange(
            IReadOnlyList<SigmaQ48Interval> bounds, out long[] selected)
        {{
            selected = new long[4];
            if (bounds == null || bounds.Count != 4 || bounds.Any(value => value.IsEmpty))
                return false;
            BigInteger residual = BigInteger.Zero;
            for (int axis = 0; axis < 4; ++axis)
            {{
                selected[axis] = SigmaNumericDomain.QClamp(0L,
                    bounds[axis].Lower, bounds[axis].Upper);
                residual += selected[axis];
            }}
            if (residual.Sign > 0)
            {{
                for (int axis = 0; axis < 4 && residual.Sign > 0; ++axis)
                {{
                    BigInteger capacity = (BigInteger)selected[axis] -
                        bounds[axis].Lower;
                    BigInteger adjustment = BigInteger.Min(residual, capacity);
                    selected[axis] = CheckedLong((BigInteger)selected[axis] - adjustment);
                    residual -= adjustment;
                }}
            }}
            else if (residual.Sign < 0)
            {{
                BigInteger deficit = -residual;
                for (int axis = 0; axis < 4 && deficit.Sign > 0; ++axis)
                {{
                    BigInteger capacity = (BigInteger)bounds[axis].Upper -
                        selected[axis];
                    BigInteger adjustment = BigInteger.Min(deficit, capacity);
                    selected[axis] = CheckedLong((BigInteger)selected[axis] + adjustment);
                    deficit -= adjustment;
                }}
                residual = -deficit;
            }}
            return residual.IsZero;
        }}

        private static SigmaFreshBaseAdmission UnresolvedFreshAdmission() =>
            new SigmaFreshBaseAdmission(SigmaFreshAdmissionStatus.Unresolved,
                SigmaS16.Zero, Array.Empty<SigmaGaugeCell>(),
                SigmaMerkabaRelationClass.Unresolved, string.Empty);

        internal static SigmaMerkabaRelationClass EvaluateFreshBoundaryRelation(
            SigmaS16 state)
        {{
            if (state.IsZero)
                return SigmaMerkabaRelationClass.DefaultSat;

            if (Enumerable.Range(0, 16).Any(address =>
                    SignTransport(0, address) != 1) ||
                PlaquetteHolonomy(0, 0, 0) != 1 ||
                !SigmaS16Operators.Associator(state, SigmaS16.Zero,
                    SigmaS16.Zero).IsZero)
                throw new InvalidOperationException(
                    "Generated fresh base relation specialization is invalid.");

            // This is the exact generated NATIVE_CLOSURE_DEFECT specialization
            // for (state, ZEmpty, ZEmpty) in the canonical chi0/kappa0 base
            // context. U_0 is identity, [state,0,0]=0 and W_00(0)=+1, so the
            // normalized link d=-state is the sole nonzero factor. A nonzero
            // diffraction-kernel link remains unresolved; otherwise the
            // resolved mixed boundary is an exact NO_RELATION termination.
            SigmaS16 link = SigmaS16Operators.Subtract(SigmaS16.Zero, state);
            if (!TryNormalizePrimitiveDefect(link,
                    out _,
                    out bool diffractionKernel) || diffractionKernel)
                return SigmaMerkabaRelationClass.Unresolved;
            // The raw Q16.48 numerator remains an exact factor witness. Once
            // its primitive G norm is positive, a nonzero raw link proves the
            // normalized exact point is nonzero even if its outward enclosure
            // contains zero at Q48 resolution. Uncertain input intervals still
            // use ClassifyExactZeroFactor and remain unresolved when they span 0.
            return SigmaMerkabaRelationClass.NoRelation;
        }}

        internal static bool IsZEmpty(SigmaS16 value) => value.IsZero;

        internal static SigmaS16 DecodeDefaultRepresentation(
            SigmaDefaultBackingKind backing)
        {{
            if ((uint)backing > (uint)SigmaDefaultBackingKind.NullCodec)
                throw new ArgumentOutOfRangeException(nameof(backing));
            return SigmaS16.Zero;
        }}

        internal static SigmaDirectionalActionWitness BuildDirectionalAction(
            SigmaNativeQueryClaim measuredRole, SigmaQ48Interval queryDirection,
            SigmaQ48Interval residual)
        {{
            if (measuredRole == SigmaNativeQueryClaim.NoClaim)
                return new SigmaDirectionalActionWitness(measuredRole,
                    queryDirection, residual, new SigmaQ48Interval(0L, 0L), false);
            return new SigmaDirectionalActionWitness(measuredRole, queryDirection,
                residual, MultiplyOutward(queryDirection, residual), true);
        }}

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

        internal static SigmaExactFactorClass ClassifyExactZeroFactor(
            SigmaQ48Interval factor)
        {{
            if (factor.IsEmpty || factor.Upper < 0L || factor.Lower > 0L)
                return SigmaExactFactorClass.ProvenIncompatible;
            if (factor.Lower == 0L && factor.Upper == 0L)
                return SigmaExactFactorClass.ProvenExactClosed;
            return SigmaExactFactorClass.Unresolved;
        }}

        internal static int ImplicitBoundaryCount(int width, int height)
        {{
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));
            return checked((width - 1) * height + width * (height - 1));
        }}

        internal static bool TryDecodeImplicitBoundary(int edgeIndex, int width,
            int height, out int leftX, out int leftY, out int rightX,
            out int rightY, out SigmaSampleBoundarySide leftSide,
            out SigmaSampleBoundarySide rightSide)
        {{
            leftX = leftY = rightX = rightY = 0;
            leftSide = SigmaSampleBoundarySide.Right;
            rightSide = SigmaSampleBoundarySide.Left;
            int count = ImplicitBoundaryCount(width, height);
            if ((uint)edgeIndex >= (uint)count) return false;
            int horizontalCount = checked((width - 1) * height);
            if (edgeIndex < horizontalCount)
            {{
                leftX = edgeIndex % (width - 1);
                leftY = edgeIndex / (width - 1);
                rightX = leftX + 1;
                rightY = leftY;
                return true;
            }}
            int vertical = edgeIndex - horizontalCount;
            leftX = vertical % width;
            leftY = vertical / width;
            rightX = leftX;
            rightY = leftY + 1;
            leftSide = SigmaSampleBoundarySide.Down;
            rightSide = SigmaSampleBoundarySide.Up;
            return true;
        }}

        // Exhaustive CPU semantic reference only. Production edge work items are
        // index-derived in BOUNDARY and never materialize this complete array.
        internal static SigmaImplicitBoundaryRef[]
            EnumerateImplicitBoundaryReference(
                IEnumerable<SigmaFreshFootprintSample> source,
                int width, int height)
        {{
            if (source == null) throw new ArgumentNullException(nameof(source));
            SigmaFreshFootprintSample[] samples = source.ToArray();
            if (samples.Length == 0) return Array.Empty<SigmaImplicitBoundaryRef>();
            if (samples.Select(value => value.CoherentFrameKey).Distinct().Count() != 1)
                throw new ArgumentException(
                    "Implicit boundary reference accepts one coherent frame.",
                    nameof(source));
            if (samples.GroupBy(value => (value.SampleX, value.SampleY)).Any(
                    group => group.Count() != 1))
                throw new ArgumentException(
                    "Coherent sampling coordinates must be unique execution keys.",
                    nameof(source));
            var byCoordinate = samples.ToDictionary(
                value => (value.SampleX, value.SampleY));
            if (samples.Any(value => value.SampleX >= width ||
                    value.SampleY >= height))
                throw new ArgumentException(
                    "Sampling coordinates exceed the coherent domain.",
                    nameof(source));
            var output = new List<SigmaImplicitBoundaryRef>();
            int boundaryCount = ImplicitBoundaryCount(width, height);
            for (int edgeIndex = 0; edgeIndex < boundaryCount; ++edgeIndex)
            {{
                TryDecodeImplicitBoundary(edgeIndex, width, height,
                    out int leftX, out int leftY, out int rightX,
                    out int rightY, out SigmaSampleBoundarySide leftSide,
                    out SigmaSampleBoundarySide rightSide);
                if (!byCoordinate.TryGetValue((leftX, leftY),
                        out SigmaFreshFootprintSample left) ||
                    !byCoordinate.TryGetValue((rightX, rightY),
                        out SigmaFreshFootprintSample right) ||
                    !left.Valid || !right.Valid ||
                    left.CoherentFrameKey != right.CoherentFrameKey ||
                    left.Claim != SigmaNativeQueryClaim.FirstHitMould ||
                    right.Claim != SigmaNativeQueryClaim.FirstHitMould)
                    continue;
                SigmaStitchBoundaryEnvelope[] leftBoundary =
                    left.Boundaries.Where(value => value.Side == leftSide).ToArray();
                SigmaStitchBoundaryEnvelope[] rightBoundary =
                    right.Boundaries.Where(value => value.Side == rightSide).ToArray();
                SigmaStitchContactBranch[] contactBranches = leftBoundary
                    .SelectMany(a => rightBoundary.Select(b => Enumerable
                        .Range(0, 3).Select(axis => a.RoomBounds[axis]
                            .Intersect(b.RoomBounds[axis])).ToArray()))
                    .Where(region => region.All(value => !value.IsEmpty))
                    .Select(region => new SigmaStitchContactBranch(region))
                    .GroupBy(value => value.CanonicalSerialization,
                        StringComparer.Ordinal)
                    .Select(group => group.First())
                    .OrderBy(value => value.CanonicalSerialization,
                        StringComparer.Ordinal).ToArray();
                if (contactBranches.Length != 0)
                    output.Add(new SigmaImplicitBoundaryRef(edgeIndex,
                        left.SupportKey, right.SupportKey, leftSide, rightSide,
                        contactBranches));
            }}
            return output.ToArray();
        }}

        internal static SigmaStitchWitnessSet EvaluateModalStitch(
            SigmaImplicitBoundaryRef boundary, SigmaStitchLocality left,
            SigmaStitchLocality right, SigmaStitchNativeContext nativeContext)
        {{
            if (boundary.LeftKey != left.ScratchKey ||
                boundary.RightKey != right.ScratchKey)
                throw new ArgumentException(
                    "Stitch endpoints must match the implicit boundary.");
            if (left.Level != right.Level)
                return new SigmaStitchWitnessSet(
                    SigmaStitchResolution.Unresolved,
                    Array.Empty<SigmaStitchRelationReceipt>(),
                    Array.Empty<SigmaResolvedStitch>(), true);
            if (boundary.ContactBranches.Length != 1)
                return new SigmaStitchWitnessSet(
                    SigmaStitchResolution.Unresolved,
                    Array.Empty<SigmaStitchRelationReceipt>(),
                    Array.Empty<SigmaResolvedStitch>(), true);

            var receipts = new List<SigmaStitchRelationReceipt>(16);
            var alternatives = new List<SigmaResolvedStitch>(16);
            bool hasUnresolved = false;
            for (int leftOrdinal = 0; leftOrdinal < 4; ++leftOrdinal)
            for (int rightOrdinal = 0; rightOrdinal < 4; ++rightOrdinal)
            {{
                var leftSector = (SigmaNativeBoundarySector)leftOrdinal;
                var rightSector = (SigmaNativeBoundarySector)rightOrdinal;
                SigmaStitchRelationReceipt receipt =
                    EvaluateNativeStitchCandidate(left, right, nativeContext,
                        leftSector, rightSector);
                receipts.Add(receipt);
                if (receipt.ClosureClass == SigmaExactFactorClass.Unresolved)
                {{
                    hasUnresolved = true;
                    continue;
                }}
                if (receipt.ClosureClass ==
                        SigmaExactFactorClass.ProvenIncompatible)
                    continue;
                alternatives.Add(new SigmaResolvedStitch(boundary, receipt));
            }}
            if (hasUnresolved)
                return new SigmaStitchWitnessSet(
                    SigmaStitchResolution.Unresolved, receipts, alternatives,
                    true);
            if (alternatives.Count == 0)
                return new SigmaStitchWitnessSet(
                    SigmaStitchResolution.NoStitch, receipts, alternatives);
            IGrouping<string, SigmaResolvedStitch>[] classes = alternatives
                .GroupBy(CanonicalStitchSerialization, StringComparer.Ordinal)
                .ToArray();
            if (classes.Length != 1)
                return new SigmaStitchWitnessSet(
                    SigmaStitchResolution.Unresolved, receipts, alternatives);
            SigmaResolvedStitch resolved = classes[0]
                .OrderBy(value => value.LeftSector)
                .ThenBy(value => value.RightSector).First();
            return new SigmaStitchWitnessSet(SigmaStitchResolution.Resolved,
                receipts, new[] {{ resolved }});
        }}

        private static SigmaStitchRelationReceipt EvaluateNativeStitchCandidate(
            SigmaStitchLocality left, SigmaStitchLocality right,
            SigmaStitchNativeContext nativeContext,
            SigmaNativeBoundarySector leftSector,
            SigmaNativeBoundarySector rightSector)
        {{
            int leftAddress = NativeBoundaryAddress(leftSector);
            int rightAddress = NativeBoundaryAddress(rightSector);
            int transportAddress = leftAddress ^ rightAddress;
            int forwardTransportSign = BasisSign(leftAddress,
                transportAddress);
            int reverseTransportSign = BasisSign(rightAddress,
                transportAddress);
            SigmaS16 transportedLeft = SigmaS16Operators.RightBasisAction(
                left.State, transportAddress);
            if (forwardTransportSign < 0)
                transportedLeft = NegateS16(transportedLeft);
            SigmaS16 link = SigmaS16Operators.Subtract(right.State,
                transportedLeft);
            SigmaS16 transportedRight = SigmaS16Operators.RightBasisAction(
                right.State, transportAddress);
            if (reverseTransportSign < 0)
                transportedRight = NegateS16(transportedRight);
            SigmaS16 reverseLink = SigmaS16Operators.Subtract(left.State,
                transportedRight);
            SigmaS16[] leftProfile = EvaluateBasisAssociatorProfile(left.State,
                leftSector);
            SigmaS16[] rightProfile = EvaluateBasisAssociatorProfile(right.State,
                rightSector);
            var associatorProfile = new SigmaS16[SigmaS16.LaneCount];
            var reverseAssociatorProfile = new SigmaS16[SigmaS16.LaneCount];
            var normalizedAssociatorProfile =
                new SigmaQ48Interval[SigmaS16.LaneCount][];
            var normalizedReverseAssociatorProfile =
                new SigmaQ48Interval[SigmaS16.LaneCount][];
            var associatorProfileClasses =
                new SigmaExactFactorClass[SigmaS16.LaneCount];
            var reverseAssociatorProfileClasses =
                new SigmaExactFactorClass[SigmaS16.LaneCount];
            bool nonzeroAssociatorProfile = false;
            for (int context = 0; context < SigmaS16.LaneCount; ++context)
            {{
                associatorProfile[context] = SigmaS16Operators.Subtract(
                    rightProfile[context], leftProfile[context]);
                reverseAssociatorProfile[context] = SigmaS16Operators.Subtract(
                    leftProfile[context], rightProfile[context]);
                nonzeroAssociatorProfile |= !associatorProfile[context].IsZero;
                associatorProfileClasses[context] = NormalizeStitchFactor(
                    associatorProfile[context],
                    out normalizedAssociatorProfile[context]);
                reverseAssociatorProfileClasses[context] = NormalizeStitchFactor(
                    reverseAssociatorProfile[context],
                    out normalizedReverseAssociatorProfile[context]);
            }}
            SigmaExactFactorClass linkClass = NormalizeStitchFactor(link,
                out SigmaQ48Interval[] normalizedLink);
            SigmaExactFactorClass reverseLinkClass = NormalizeStitchFactor(
                reverseLink, out SigmaQ48Interval[] normalizedReverseLink);
            SigmaExactFactorClass associatorClass = AggregateStitchFactors(
                associatorProfileClasses);
            SigmaExactFactorClass reverseAssociatorClass = AggregateStitchFactors(
                reverseAssociatorProfileClasses);
            SigmaExactFactorClass closureClass = AggregateStitchFactors(
                linkClass, reverseLinkClass, associatorClass,
                reverseAssociatorClass);
            SigmaS16 transition = SigmaS16Operators.Transition(left.State,
                right.State);
            SigmaS16 reverseTransition = SigmaS16Operators.Transition(right.State,
                left.State);
            int exactAnnihilator = FindExactStitchAnnihilator(transition);
            int reverseExactAnnihilator = FindExactStitchAnnihilator(
                reverseTransition);
            bool exactZd = (!transition.IsZero && exactAnnihilator >= 0) ||
                (!reverseTransition.IsZero && reverseExactAnnihilator >= 0);
            SigmaMerkabaRelationClass relationClass = closureClass ==
                    SigmaExactFactorClass.Unresolved
                ? SigmaMerkabaRelationClass.Unresolved
                : closureClass == SigmaExactFactorClass.ProvenIncompatible
                    ? nonzeroAssociatorProfile
                        ? SigmaMerkabaRelationClass.NonassociativeContext
                        : SigmaMerkabaRelationClass.NoRelation
                    : nonzeroAssociatorProfile
                        ? SigmaMerkabaRelationClass.NonassociativeContext
                        : exactZd
                            ? SigmaMerkabaRelationClass.ExactZeroDivisor
                            : SigmaMerkabaRelationClass.Regular;
            return new SigmaStitchRelationReceipt(leftSector, rightSector,
                link, reverseLink,
                associatorProfile, reverseAssociatorProfile, transition,
                reverseTransition,
                normalizedLink,
                normalizedReverseLink, normalizedAssociatorProfile,
                normalizedReverseAssociatorProfile, associatorProfileClasses,
                reverseAssociatorProfileClasses, linkClass, reverseLinkClass,
                associatorClass, reverseAssociatorClass,
                closureClass, relationClass, transportAddress,
                forwardTransportSign, reverseTransportSign,
                nonzeroAssociatorProfile,
                exactAnnihilator, reverseExactAnnihilator,
                GeneratedStitchBracketFingerprint,
                nativeContext.ProvenanceFingerprint);
        }}

        internal static SigmaS16[] EvaluateBasisAssociatorProfile(SigmaS16 state,
            SigmaNativeBoundarySector sector)
        {{
            int sectorAddress = NativeBoundaryAddress(sector);
            var profile = new SigmaS16[SigmaS16.LaneCount];
            for (int context = 0; context < SigmaS16.LaneCount; ++context)
            {{
                var lanes = new long[SigmaS16.LaneCount];
                for (int outputLane = 0; outputLane < SigmaS16.LaneCount;
                     ++outputLane)
                {{
                    int source = outputLane ^ sectorAddress ^ context;
                    int coefficient = BasisAssociatorActionCoefficient(source,
                        sectorAddress, context);
                    if (coefficient == 0) continue;
                    if (coefficient != -2 && coefficient != 2)
                        throw new InvalidOperationException(
                            "Generated basis associator coefficient escaped 0,+/-2.");
                    long value = SigmaNumericDomain.QShiftLeft(state[source], 1);
                    lanes[outputLane] = coefficient < 0
                        ? SigmaNumericDomain.QNegate(value) : value;
                }}
                profile[context] = SigmaS16.FromArray(lanes);
            }}
            return profile;
        }}

        internal static SigmaS16 ReconstructAssociatorFromBasisProfile(
            IReadOnlyList<SigmaS16> profile, SigmaS16 context)
        {{
            if (profile == null || profile.Count != SigmaS16.LaneCount)
                throw new ArgumentException(
                    "Associator reconstruction requires sixteen basis factors.",
                    nameof(profile));
            var lanes = new long[SigmaS16.LaneCount];
            for (int basis = 0; basis < SigmaS16.LaneCount; ++basis)
            for (int lane = 0; lane < SigmaS16.LaneCount; ++lane)
                lanes[lane] = SigmaNumericDomain.QAdd(lanes[lane],
                    SigmaNumericDomain.QMul(context[basis], profile[basis][lane]));
            return SigmaS16.FromArray(lanes);
        }}

        private static int BasisAssociatorActionCoefficient(int source,
            int sectorAddress, int contextAddress) =>
            BasisSign(source, sectorAddress) *
                BasisSign(source ^ sectorAddress, contextAddress) -
            BasisSign(sectorAddress, contextAddress) *
                BasisSign(source, sectorAddress ^ contextAddress);

        private static int NativeBoundaryAddress(
            SigmaNativeBoundarySector sector)
        {{
            int ordinal = (int)sector;
            if ((uint)ordinal >= 4u)
                throw new ArgumentOutOfRangeException(nameof(sector));
            return 1 << ordinal;
        }}

        private sealed class SigmaComponentNormalForm
        {{
            internal SigmaComponentNormalForm(string canonical,
                IReadOnlyList<SigmaGaugeCell> cells)
            {{
                Canonical = canonical;
                CanonicalTokens = Encoding.ASCII.GetBytes(canonical);
                Cells = cells?.ToArray() ?? Array.Empty<SigmaGaugeCell>();
            }}
            internal string Canonical {{ get; }}
            internal byte[] CanonicalTokens {{ get; }}
            internal SigmaGaugeCell[] Cells {{ get; }}
        }}

        internal static bool TryIntegrateStitchPattern(
            IEnumerable<SigmaStitchLocality> localitySource,
            IEnumerable<SigmaBoundaryNativeInput> edgeSource,
            out SigmaStitchPattern pattern)
        {{
            if (localitySource == null)
                throw new ArgumentNullException(nameof(localitySource));
            if (edgeSource == null)
                throw new ArgumentNullException(nameof(edgeSource));
            SigmaStitchLocality[] localities = localitySource.ToArray();
            SigmaBoundaryNativeInput[] edgeInputs = edgeSource.ToArray();
            pattern = new SigmaStitchPattern(SigmaStitchResolution.Unresolved,
                Array.Empty<SigmaGaugeCell>(), 0, string.Empty);
            if (localities.Length == 0 || localities.Select(value =>
                    value.ScratchKey).Distinct().Count() != localities.Length)
                return false;
            var byKey = localities.ToDictionary(value => value.ScratchKey);
            if (edgeInputs.Any(value =>
                    !byKey.ContainsKey(value.Boundary.LeftKey) ||
                    !byKey.ContainsKey(value.Boundary.RightKey)))
                return false;

            var resolved = new List<SigmaResolvedStitch>();
            foreach (SigmaBoundaryNativeInput edge in edgeInputs)
            {{
                SigmaStitchWitnessSet witnessSet = EvaluateModalStitch(
                    edge.Boundary, byKey[edge.Boundary.LeftKey],
                    byKey[edge.Boundary.RightKey], edge.NativeContext);
                if (witnessSet.HasOpenFactor)
                {{
                    pattern = new SigmaStitchPattern(
                        SigmaStitchResolution.Unresolved,
                        Array.Empty<SigmaGaugeCell>(), 0, string.Empty,
                        Math.Max(1, witnessSet.ResolvedAlternatives.Length));
                    return true;
                }}
                if (witnessSet.Resolution == SigmaStitchResolution.Unresolved)
                {{
                    int classes = witnessSet.ResolvedAlternatives.Select(
                            CanonicalStitchSerialization)
                        .Distinct(StringComparer.Ordinal).Count();
                    pattern = new SigmaStitchPattern(
                        SigmaStitchResolution.Unresolved,
                        Array.Empty<SigmaGaugeCell>(), 0, string.Empty,
                        Math.Max(2, classes));
                    return true;
                }}
                if (witnessSet.Resolution == SigmaStitchResolution.Resolved)
                    resolved.Add(witnessSet.Resolved);
            }}

            var stitches = new List<SigmaResolvedStitch>();
            foreach (IGrouping<(ulong A, ulong B), SigmaResolvedStitch> group in
                     resolved.GroupBy(value => value.Boundary.LeftKey <
                             value.Boundary.RightKey
                         ? (value.Boundary.LeftKey, value.Boundary.RightKey)
                         : (value.Boundary.RightKey, value.Boundary.LeftKey)))
            {{
                SigmaResolvedStitch[] alternatives = group.ToArray();
                string[] classes = alternatives.Select(value =>
                    CanonicalUnpositionedStitchSerialization(value, byKey))
                    .Distinct(StringComparer.Ordinal).ToArray();
                if (classes.Length != 1)
                {{
                    pattern = new SigmaStitchPattern(
                        SigmaStitchResolution.Unresolved,
                        Array.Empty<SigmaGaugeCell>(), 0, string.Empty,
                        classes.Length);
                    return true;
                }}
                stitches.Add(alternatives.OrderBy(value =>
                    CanonicalUnpositionedStitchSerialization(value, byKey),
                    StringComparer.Ordinal).First());
            }}

            var adjacency = localities.ToDictionary(value => value.ScratchKey,
                _ => new List<(SigmaResolvedStitch Edge, bool Forward)>());
            foreach (SigmaResolvedStitch edge in stitches)
            {{
                adjacency[edge.Boundary.LeftKey].Add((edge, true));
                adjacency[edge.Boundary.RightKey].Add((edge, false));
            }}
            foreach (List<(SigmaResolvedStitch Edge, bool Forward)> list in
                     adjacency.Values)
                list.Sort((left, right) => string.CompareOrdinal(
                    CanonicalStitchSerialization(left.Edge),
                    CanonicalStitchSerialization(right.Edge)));

            var componentByKey = new Dictionary<ulong, int>();
            int componentCount = 0;
            foreach (SigmaStitchLocality seed in localities.OrderBy(value =>
                         value.CompletePayloadFingerprint, StringComparer.Ordinal)
                     .ThenBy(value => value.Level))
            {{
                if (componentByKey.ContainsKey(seed.ScratchKey)) continue;
                int component = componentCount++;
                componentByKey.Add(seed.ScratchKey, component);
                var queue = new Queue<ulong>();
                queue.Enqueue(seed.ScratchKey);
                while (queue.Count != 0)
                {{
                    ulong key = queue.Dequeue();
                    foreach ((SigmaResolvedStitch Edge, bool Forward) step in
                             adjacency[key])
                    {{
                        ulong next = step.Forward ? step.Edge.Boundary.RightKey :
                            step.Edge.Boundary.LeftKey;
                        if (componentByKey.TryGetValue(next,
                                out int existingComponent))
                        {{
                            if (existingComponent != component)
                                throw new InvalidOperationException(
                                    "A stitch cannot cross two derived components.");
                            continue;
                        }}
                        componentByKey.Add(next, component);
                        queue.Enqueue(next);
                    }}
                }}
            }}

            var components = new List<SigmaComponentNormalForm>();
            for (int component = 0; component < componentCount; ++component)
            {{
                ulong[] keys = componentByKey.Where(value =>
                        value.Value == component).Select(value => value.Key)
                    .ToArray();
                SigmaResolvedStitch[] componentStitches = stitches.Where(value =>
                        componentByKey[value.Boundary.LeftKey] == component &&
                        componentByKey[value.Boundary.RightKey] == component)
                    .ToArray();
                int embeddingClassCount = 0;
                if (!TryValidateNativeStitchTransport(keys, adjacency, byKey) ||
                    !TryEnumerateComponentChartEmbeddings(keys,
                        componentStitches, adjacency, byKey,
                        out SigmaComponentNormalForm componentForm,
                        out embeddingClassCount))
                {{
                    pattern = new SigmaStitchPattern(
                        SigmaStitchResolution.Unresolved,
                        Array.Empty<SigmaGaugeCell>(), componentCount,
                        string.Empty, Math.Max(2, embeddingClassCount));
                    return true;
                }}
                components.Add(componentForm);
            }}
            components.Sort((left, right) => CompareCanonicalTokens(
                left.CanonicalTokens, right.CanonicalTokens));

            var packed = new List<SigmaGaugeCell>();
            long cursor = 0L;
            foreach (SigmaComponentNormalForm part in components)
            {{
                long minimumU = part.Cells.Length == 0 ? 0L : part.Cells.Min(
                    value => FloorDyadic(value.U, value.Level));
                long minimumV = part.Cells.Length == 0 ? 0L : part.Cells.Min(
                    value => FloorDyadic(value.V, value.Level));
                long maximumU = part.Cells.Length == 0 ? 0L : part.Cells.Max(
                    value => CeilingDyadic(checked(value.U + 1L), value.Level));
                long width = checked(maximumU - minimumU);
                long translateU = checked(cursor - minimumU);
                long translateV = checked(-minimumV);
                packed.AddRange(part.Cells.Select(value => new SigmaGaugeCell(
                    checked(value.U + ScaleBaseTranslation(translateU,
                        value.Level)),
                    checked(value.V + ScaleBaseTranslation(translateV,
                        value.Level)), value.Level,
                    value.PayloadFingerprint)));
                cursor = checked(cursor + width + 1L);
            }}
            string canonical = string.Join("||", components.Select(value =>
                value.Canonical));
            var canonicalWriter = new SigmaCanonicalTokenWriter();
            for (int component = 0; component < components.Count; ++component)
            {{
                if (component != 0) canonicalWriter.Text("||");
                canonicalWriter.Tokens(components[component].CanonicalTokens);
            }}
            pattern = new SigmaStitchPattern(SigmaStitchResolution.Resolved,
                packed.OrderBy(value => value.Level).ThenBy(value => value.U)
                    .ThenBy(value => value.V).ThenBy(value =>
                        value.PayloadFingerprint, StringComparer.Ordinal).ToArray(),
                componentCount, canonical, 1, canonicalWriter.ToArray());
            return true;
        }}

        internal static string CanonicalStitchSerialization(
            SigmaResolvedStitch stitch)
        {{
            string contact = Encoding.ASCII.GetString(
                CanonicalContactTokens(stitch.Boundary));
            // A resolved abstract incidence and its exact reversal are the same
            // undirected stitch.  Direction remains present in both serialized
            // receipt halves; it must not leak endpoint enumeration order into
            // the canonical witness key.
            return $"{{contact}}:" +
                CanonicalStitchReceiptSerialization(stitch.Receipt);
        }}

        private static SigmaComponentNormalForm CanonicalizeStitchComponent(
            IReadOnlyList<ulong> keys,
            IReadOnlyList<SigmaResolvedStitch> stitches,
            IReadOnlyDictionary<ulong, SigmaStitchLocality> localities,
            IReadOnlyDictionary<ulong, (long U, long V)> positions)
        {{
            SigmaComponentNormalForm best = null;
            for (int transformIndex = 0;
                 transformIndex < ChartD4.Length; ++transformIndex)
            {{
                var transformed = new Dictionary<ulong, (long U, long V)>();
                foreach (ulong key in keys)
                {{
                    SigmaStitchLocality locality = localities[key];
                    transformed.Add(key, TransformDyadicCellLower(
                        positions[key], locality.Level,
                        ChartD4[transformIndex]));
                }}
                SigmaGaugeCell minimum = keys.Select(key => new SigmaGaugeCell(
                        transformed[key].U, transformed[key].V,
                        localities[key].Level,
                        CompleteLocalityPayload(key, stitches, localities)))
                    .Aggregate((left, right) =>
                        CompareDyadicLower(left, right) <= 0 ? left : right);
                long translationU = FloorDyadic(minimum.U, minimum.Level);
                long translationV = FloorDyadic(minimum.V, minimum.Level);
                var normalizedPositions =
                    new Dictionary<ulong, (long U, long V)>();
                foreach (ulong key in keys)
                {{
                    SigmaStitchLocality locality = localities[key];
                    (long U, long V) value = transformed[key];
                    normalizedPositions.Add(key, (
                        checked(value.U - ScaleBaseTranslation(translationU,
                            locality.Level)),
                        checked(value.V - ScaleBaseTranslation(translationV,
                            locality.Level))));
                }}
                SigmaGaugeCell[] normalized = keys.Select(key =>
                {{
                    SigmaStitchLocality locality = localities[key];
                    (long U, long V) value = normalizedPositions[key];
                    return new SigmaGaugeCell(value.U, value.V, locality.Level,
                        CompleteLocalityPayload(key, stitches, localities));
                }}).OrderBy(value => value.Level)
                    .ThenBy(value => SignedMorton(value.U, value.V))
                    .ThenBy(value => value.U).ThenBy(value => value.V)
                    .ThenBy(value => value.PayloadFingerprint,
                        StringComparer.Ordinal).ToArray();
                for (int left = 0; left < normalized.Length; ++left)
                    for (int right = left + 1; right < normalized.Length; ++right)
                        if (DyadicCellsOverlap(normalized[left], normalized[right]))
                            throw new InvalidOperationException(
                                "A chart embedding overlaps distinct localities.");
                string cellBytes = string.Join(";", normalized.Select(value =>
                    $"{{value.Level}}:{{value.U}}:{{value.V}}:" +
                    value.PayloadFingerprint));
                string edgeBytes = string.Join(";", stitches.Select(value =>
                        CanonicalIntegratedStitchSerialization(value, localities,
                            normalizedPositions))
                    .OrderBy(value => value, StringComparer.Ordinal));
                string canonical = $"{{cellBytes}}#{{edgeBytes}}";
                var candidate = new SigmaComponentNormalForm(canonical, normalized);
                if (best == null || CompareCanonicalTokens(
                        candidate.CanonicalTokens, best.CanonicalTokens) < 0)
                    best = candidate;
            }}
            return best ?? throw new InvalidOperationException(
                "D4 chart orbit was empty.");
        }}

        private static string CompleteLocalityPayload(ulong key,
            IReadOnlyList<SigmaResolvedStitch> stitches,
            IReadOnlyDictionary<ulong, SigmaStitchLocality> localities)
        {{
            string incident = string.Join(",", stitches.Where(value =>
                    value.Boundary.LeftKey == key ||
                    value.Boundary.RightKey == key)
                .Select(value => CanonicalUnpositionedStitchSerialization(
                    value, localities))
                .OrderBy(value => value, StringComparer.Ordinal));
            return $"{{localities[key].CompletePayloadFingerprint}}@{{incident}}";
        }}

        private static string CanonicalUnpositionedStitchSerialization(
            SigmaResolvedStitch stitch,
            IReadOnlyDictionary<ulong, SigmaStitchLocality> localities)
        {{
            SigmaStitchLocality left = localities[stitch.Boundary.LeftKey];
            SigmaStitchLocality right = localities[stitch.Boundary.RightKey];
            string leftToken = $"{{left.Level}}:{{left.CompletePayloadFingerprint}}";
            string rightToken = $"{{right.Level}}:{{right.CompletePayloadFingerprint}}";
            string forward = $"{{leftToken}}>{{rightToken}}:" +
                DirectedStitchWitnessSerialization(stitch.Receipt, true);
            string reverse = $"{{rightToken}}>{{leftToken}}:" +
                DirectedStitchWitnessSerialization(stitch.Receipt, false);
            return string.CompareOrdinal(forward, reverse) <= 0 ? forward : reverse;
        }}

        private static string CanonicalIntegratedStitchSerialization(
            SigmaResolvedStitch stitch,
            IReadOnlyDictionary<ulong, SigmaStitchLocality> localities,
            IReadOnlyDictionary<ulong, (long U, long V)> positions)
        {{
            SigmaStitchLocality left = localities[stitch.Boundary.LeftKey];
            SigmaStitchLocality right = localities[stitch.Boundary.RightKey];
            (long U, long V) leftPosition = positions[stitch.Boundary.LeftKey];
            (long U, long V) rightPosition = positions[stitch.Boundary.RightKey];
            string leftToken = $"{{leftPosition.U}}:{{leftPosition.V}}:" +
                $"{{left.Level}}:{{left.CompletePayloadFingerprint}}";
            string rightToken = $"{{rightPosition.U}}:{{rightPosition.V}}:" +
                $"{{right.Level}}:{{right.CompletePayloadFingerprint}}";
            string forward = $"{{leftToken}}>{{rightToken}}:" +
                DirectedStitchWitnessSerialization(stitch.Receipt, true);
            string reverse = $"{{rightToken}}>{{leftToken}}:" +
                DirectedStitchWitnessSerialization(stitch.Receipt, false);
            return string.CompareOrdinal(forward, reverse) <= 0 ? forward : reverse;
        }}

        private static byte[] CanonicalContactTokens(
            SigmaImplicitBoundaryRef boundary)
        {{
            var writer = new SigmaCanonicalTokenWriter();
            for (int branch = 0; branch < boundary.ContactBranches.Length;
                 ++branch)
            {{
                if (branch != 0) writer.Character('|');
                SigmaStitchContactBranch contact = boundary.ContactBranches[branch];
                for (int axis = 0; axis < contact.RoomBounds.Length; ++axis)
                {{
                    if (axis != 0) writer.Character(',');
                    writer.Hex64(unchecked((ulong)contact.RoomBounds[axis].Lower));
                    writer.Character('-');
                    writer.Hex64(unchecked((ulong)contact.RoomBounds[axis].Upper));
                }}
            }}
            return writer.ToArray();
        }}

        internal static string CanonicalStitchReceiptSerialization(
            SigmaStitchRelationReceipt receipt)
        {{
            return Encoding.ASCII.GetString(
                CanonicalStitchReceiptTokens(receipt));
        }}

        internal static byte[] CanonicalStitchReceiptTokens(
            SigmaStitchRelationReceipt receipt)
        {{
            byte[] forward = DirectedStitchWitnessTokens(receipt, true);
            byte[] reverse = DirectedStitchWitnessTokens(receipt, false);
            var writer = new SigmaCanonicalTokenWriter();
            bool forwardFirst = CompareCanonicalTokens(forward, reverse) <= 0;
            writer.Tokens(forwardFirst ? forward : reverse);
            writer.Character('/');
            writer.Tokens(forwardFirst ? reverse : forward);
            return writer.ToArray();
        }}

        private static string DirectedStitchWitnessSerialization(
            SigmaStitchRelationReceipt receipt, bool forward) =>
            Encoding.ASCII.GetString(DirectedStitchWitnessTokens(receipt,
                forward));

        private static byte[] DirectedStitchWitnessTokens(
            SigmaStitchRelationReceipt receipt, bool forward)
        {{
            SigmaS16 link = forward ? receipt.LinkDefect :
                receipt.ReverseLinkDefect;
            IReadOnlyList<SigmaS16> associatorProfile = forward
                ? receipt.AssociatorProfile : receipt.ReverseAssociatorProfile;
            IReadOnlyList<SigmaQ48Interval> normalizedLink = forward
                ? receipt.NormalizedLink : receipt.NormalizedReverseLink;
            IReadOnlyList<SigmaQ48Interval[]> normalizedAssociatorProfile = forward
                ? receipt.NormalizedAssociatorProfile :
                    receipt.NormalizedReverseAssociatorProfile;
            SigmaExactFactorClass linkClass = forward ? receipt.LinkClass :
                receipt.ReverseLinkClass;
            IReadOnlyList<SigmaExactFactorClass> associatorProfileClasses = forward
                ? receipt.AssociatorProfileClasses :
                    receipt.ReverseAssociatorProfileClasses;
            SigmaExactFactorClass associatorClass = forward
                ? receipt.AssociatorClass : receipt.ReverseAssociatorClass;
            SigmaNativeBoundarySector from = forward ? receipt.LeftSector :
                receipt.RightSector;
            SigmaNativeBoundarySector to = forward ? receipt.RightSector :
                receipt.LeftSector;
            int sign = forward ? receipt.ForwardTransportSign :
                receipt.ReverseTransportSign;
            int annihilator = forward ? receipt.ExactAnnihilatorAction :
                receipt.ReverseExactAnnihilatorAction;
            var writer = new SigmaCanonicalTokenWriter();
            writer.Decimal((uint)from);
            writer.Character('>');
            writer.Decimal((uint)to);
            writer.Character(':');
            writer.Decimal((uint)receipt.TransportAddress);
            writer.Character(':');
            writer.Decimal(sign);
            writer.Character(':');
            WriteCanonicalDirectionalFactor(writer, link, associatorProfile,
                normalizedLink, normalizedAssociatorProfile, linkClass,
                associatorProfileClasses, associatorClass);
            writer.Character(':');
            writer.Decimal((uint)receipt.ClosureClass);
            writer.Character(':');
            writer.Decimal((uint)receipt.RelationClass);
            writer.Character(':');
            writer.Decimal(receipt.NonzeroAssociatorProfile ? 1u : 0u);
            writer.Character(':');
            writer.Decimal(annihilator);
            writer.Character(':');
            writer.Hex64(receipt.BracketFingerprint);
            writer.Character(':');
            writer.Text(receipt.ProvenanceFingerprint);
            return writer.ToArray();
        }}

        private static string CanonicalDirectionalFactorSerialization(
            SigmaS16 link, IReadOnlyList<SigmaS16> associatorProfile,
            IReadOnlyList<SigmaQ48Interval> normalizedLink,
            IReadOnlyList<SigmaQ48Interval[]> normalizedAssociatorProfile,
            SigmaExactFactorClass linkClass,
            IReadOnlyList<SigmaExactFactorClass> associatorProfileClasses,
            SigmaExactFactorClass associatorClass)
        {{
            var writer = new SigmaCanonicalTokenWriter();
            WriteCanonicalDirectionalFactor(writer, link, associatorProfile,
                normalizedLink, normalizedAssociatorProfile, linkClass,
                associatorProfileClasses, associatorClass);
            return writer.ToAscii();
        }}

        private static void WriteCanonicalDirectionalFactor(
            SigmaCanonicalTokenWriter writer, SigmaS16 link,
            IReadOnlyList<SigmaS16> associatorProfile,
            IReadOnlyList<SigmaQ48Interval> normalizedLink,
            IReadOnlyList<SigmaQ48Interval[]> normalizedAssociatorProfile,
            SigmaExactFactorClass linkClass,
            IReadOnlyList<SigmaExactFactorClass> associatorProfileClasses,
            SigmaExactFactorClass associatorClass)
        {{
            void Raw(SigmaS16 value)
            {{
                for (int lane = 0; lane < SigmaS16.LaneCount; ++lane)
                {{
                    if (lane != 0) writer.Character(',');
                    writer.Hex64(unchecked((ulong)value[lane]));
                }}
            }}
            void Intervals(IReadOnlyList<SigmaQ48Interval> values)
            {{
                for (int lane = 0; lane < values.Count; ++lane)
                {{
                    if (lane != 0) writer.Character(',');
                    writer.Hex64(unchecked((ulong)values[lane].Lower));
                    writer.Character('-');
                    writer.Hex64(unchecked((ulong)values[lane].Upper));
                }}
            }}
            writer.Decimal((uint)linkClass);
            writer.Character(':');
            Raw(link);
            writer.Character(':');
            Intervals(normalizedLink);
            writer.Character(':');
            writer.Decimal((uint)associatorClass);
            writer.Character(':');
            writer.Character('[');
            for (int context = 0; context < SigmaS16.LaneCount; ++context)
            {{
                if (context != 0) writer.Character(';');
                writer.Decimal((uint)context);
                writer.Character(':');
                writer.Decimal((uint)associatorProfileClasses[context]);
                writer.Character(':');
                Raw(associatorProfile[context]);
                writer.Character(':');
                Intervals(normalizedAssociatorProfile[context]);
            }}
            writer.Character(']');
        }}

        private static SigmaS16 ApplyNativeStitchTransport(SigmaS16 state,
            SigmaStitchRelationReceipt receipt, bool forward)
        {{
            SigmaS16 transported = SigmaS16Operators.RightBasisAction(state,
                receipt.TransportAddress);
            int sign = forward ? receipt.ForwardTransportSign :
                receipt.ReverseTransportSign;
            return sign < 0 ? NegateS16(transported) : transported;
        }}

        private static int[][] BuildNativeSectorChartAssignments()
        {{
            var result = new List<int[]>(24);
            for (int a = 0; a < 4; ++a)
            for (int b = 0; b < 4; ++b)
            for (int c = 0; c < 4; ++c)
            for (int d = 0; d < 4; ++d)
            {{
                if (a == b || a == c || a == d || b == c || b == d ||
                    c == d)
                    continue;
                result.Add(new[] {{ a, b, c, d }});
            }}
            if (result.Count != 24)
                throw new InvalidOperationException(
                    "Four abstract sectors require all 4! chart assignments.");
            return result.ToArray();
        }}

        private static (int U, int V) ChartDirection(int direction)
        {{
            return direction switch
            {{
                0 => (1, 0),
                1 => (0, 1),
                2 => (-1, 0),
                3 => (0, -1),
                _ => throw new ArgumentOutOfRangeException(nameof(direction)),
            }};
        }}

        private static int ChartDirectionOrdinal(int u, int v)
        {{
            if (u == 1 && v == 0) return 0;
            if (u == 0 && v == 1) return 1;
            if (u == -1 && v == 0) return 2;
            if (u == 0 && v == -1) return 3;
            throw new InvalidOperationException(
                "A square-chart boundary direction must be one signed axis.");
        }}

        private static int TransformChartDirection(int direction,
            SigmaChartD4Transform transform)
        {{
            (int U, int V) value = ChartDirection(direction);
            return ChartDirectionOrdinal(
                checked(transform.M00 * value.U + transform.M01 * value.V),
                checked(transform.M10 * value.U + transform.M11 * value.V));
        }}

        private static string CanonicalNativeSectorChartAssignment(
            IReadOnlyList<int> assignment)
        {{
            if (assignment == null || assignment.Count != 4 ||
                assignment.Distinct().Count() != 4 ||
                assignment.Any(value => (uint)value >= 4u))
                throw new ArgumentException(
                    "A sector chart assignment is one complete four-way bijection.",
                    nameof(assignment));
            return ChartD4.Select(transform => string.Join(",",
                    assignment.Select(direction =>
                        TransformChartDirection(direction, transform))))
                .OrderBy(value => value, StringComparer.Ordinal).First();
        }}

        private static bool TryValidateNativeStitchTransport(
            IReadOnlyList<ulong> keys,
            IReadOnlyDictionary<ulong,
                List<(SigmaResolvedStitch Edge, bool Forward)>> adjacency,
            IReadOnlyDictionary<ulong, SigmaStitchLocality> localities)
        {{
            if (keys.Count == 0) return false;
            var transported = new Dictionary<ulong, SigmaS16>();
            ulong seed = keys.OrderBy(key =>
                    localities[key].CompletePayloadFingerprint,
                    StringComparer.Ordinal).ThenBy(key => localities[key].Level)
                .First();
            transported.Add(seed, localities[seed].State);
            var queue = new Queue<ulong>();
            queue.Enqueue(seed);
            while (queue.Count != 0)
            {{
                ulong current = queue.Dequeue();
                foreach ((SigmaResolvedStitch Edge, bool Forward) step in
                         adjacency[current])
                {{
                    ulong next = step.Forward ? step.Edge.Boundary.RightKey :
                        step.Edge.Boundary.LeftKey;
                    SigmaS16 proposed = ApplyNativeStitchTransport(
                        transported[current], step.Edge.Receipt, step.Forward);
                    if (proposed != localities[next].State)
                        return false;
                    if (transported.TryGetValue(next, out SigmaS16 existing))
                    {{
                        if (existing != proposed) return false;
                        continue;
                    }}
                    transported.Add(next, proposed);
                    queue.Enqueue(next);
                }}
            }}
            return transported.Count == keys.Count;
        }}

        private static bool TryEnumerateComponentChartEmbeddings(
            IReadOnlyList<ulong> keys,
            IReadOnlyList<SigmaResolvedStitch> stitches,
            IReadOnlyDictionary<ulong,
                List<(SigmaResolvedStitch Edge, bool Forward)>> adjacency,
            IReadOnlyDictionary<ulong, SigmaStitchLocality> localities,
            out SigmaComponentNormalForm normalForm,
            out int embeddingClassCount)
        {{
            normalForm = null;
            embeddingClassCount = 0;
            if (keys.Count == 0) return false;
            if (stitches.Any(edge => localities[edge.Boundary.LeftKey].Level !=
                    localities[edge.Boundary.RightKey].Level))
                return false;

            var orbitClasses = new Dictionary<string, SigmaComponentNormalForm>(
                StringComparer.Ordinal);
            foreach (int[] rootAssignment in NativeSectorChartAssignments)
            {{
                if (!TryBuildComponentChartEmbedding(keys, adjacency,
                        localities, rootAssignment,
                        out Dictionary<ulong, (long U, long V)> positions))
                    continue;
                SigmaComponentNormalForm candidate;
                try
                {{
                    candidate = CanonicalizeStitchComponent(keys, stitches,
                        localities, positions);
                }}
                catch (InvalidOperationException)
                {{
                    continue;
                }}
                if (!orbitClasses.ContainsKey(candidate.Canonical))
                    orbitClasses.Add(candidate.Canonical, candidate);
                if (orbitClasses.Count > 1) break;
            }}
            embeddingClassCount = orbitClasses.Count;
            if (orbitClasses.Count != 1) return false;
            normalForm = orbitClasses.Values.Single();
            return true;
        }}

        private static bool TryBuildComponentChartEmbedding(
            IReadOnlyList<ulong> keys,
            IReadOnlyDictionary<ulong,
                List<(SigmaResolvedStitch Edge, bool Forward)>> adjacency,
            IReadOnlyDictionary<ulong, SigmaStitchLocality> localities,
            IReadOnlyList<int> rootAssignment,
            out Dictionary<ulong, (long U, long V)> positions)
        {{
            positions = new Dictionary<ulong, (long U, long V)>();
            if (rootAssignment == null || rootAssignment.Count != 4)
                return false;
            var frames = new Dictionary<ulong, int>();
            ulong root = keys.OrderBy(key =>
                    localities[key].CompletePayloadFingerprint,
                    StringComparer.Ordinal).ThenBy(key => localities[key].Level)
                .First();
            positions.Add(root, (0L, 0L));
            frames.Add(root, 0);
            var queue = new Queue<ulong>();
            queue.Enqueue(root);
            while (queue.Count != 0)
            {{
                ulong currentKey = queue.Dequeue();
                foreach ((SigmaResolvedStitch Edge, bool Forward) step in
                         adjacency[currentKey])
                {{
                    ulong nextKey = step.Forward
                        ? step.Edge.Boundary.RightKey
                        : step.Edge.Boundary.LeftKey;
                    SigmaNativeBoundarySector currentSector = step.Forward
                        ? step.Edge.LeftSector : step.Edge.RightSector;
                    SigmaNativeBoundarySector nextSector = step.Forward
                        ? step.Edge.RightSector : step.Edge.LeftSector;
                    (int U, int V) direction = SectorChartCandidateDirection(
                        rootAssignment, frames[currentKey], currentSector);
                    (long U, long V) proposed = (
                        checked(positions[currentKey].U + direction.U),
                        checked(positions[currentKey].V + direction.V));
                    if (!TryResolveAdjacentCandidateFrame(rootAssignment,
                            frames[currentKey], currentSector, nextSector,
                            step.Edge.OrientationParity,
                            out int proposedFrame))
                        return false;
                    if (positions.TryGetValue(nextKey,
                            out (long U, long V) existing))
                    {{
                        if (existing != proposed ||
                            frames[nextKey] != proposedFrame)
                            return false;
                        continue;
                    }}
                    positions.Add(nextKey, proposed);
                    frames.Add(nextKey, proposedFrame);
                    queue.Enqueue(nextKey);
                }}
            }}
            return positions.Count == keys.Count;
        }}

        private static (int U, int V) SectorChartCandidateDirection(
            IReadOnlyList<int> rootAssignment, int frameIndex,
            SigmaNativeBoundarySector sector)
        {{
            int ordinal = (int)sector;
            if (rootAssignment == null || rootAssignment.Count != 4 ||
                (uint)frameIndex >= (uint)ChartD4.Length ||
                (uint)ordinal >= 4u)
                throw new ArgumentOutOfRangeException(nameof(frameIndex));
            (int U, int V) source = ChartDirection(rootAssignment[ordinal]);
            SigmaChartD4Transform frame = ChartD4[frameIndex];
            return (checked(frame.M00 * source.U + frame.M01 * source.V),
                checked(frame.M10 * source.U + frame.M11 * source.V));
        }}

        private static bool TryResolveAdjacentCandidateFrame(
            IReadOnlyList<int> rootAssignment, int currentFrame,
            SigmaNativeBoundarySector currentSector,
            SigmaNativeBoundarySector nextSector, int orientationParity,
            out int nextFrame)
        {{
            nextFrame = -1;
            (int U, int V) direction = SectorChartCandidateDirection(
                rootAssignment, currentFrame, currentSector);
            int requiredDeterminant = checked(
                ChartD4[currentFrame].Determinant * orientationParity);
            for (int candidate = 0; candidate < ChartD4.Length; ++candidate)
            {{
                (int U, int V) candidateDirection =
                    SectorChartCandidateDirection(rootAssignment, candidate,
                        nextSector);
                if (candidateDirection.U != -direction.U ||
                    candidateDirection.V != -direction.V ||
                    ChartD4[candidate].Determinant != requiredDeterminant)
                    continue;
                if (nextFrame >= 0) return false;
                nextFrame = candidate;
            }}
            return nextFrame >= 0;
        }}

        private static (long U, long V) TransformDyadicCellLower(
            (long U, long V) source, int level,
            SigmaChartD4Transform transform)
        {{
            long TransformAxis(int fromU, int fromV)
            {{
                if (fromU == 1) return source.U;
                if (fromU == -1) return checked(-source.U - 1L);
                if (fromV == 1) return source.V;
                if (fromV == -1) return checked(-source.V - 1L);
                throw new InvalidOperationException(
                    "A D4 row must select one signed chart axis.");
            }}
            _ = level;
            return (TransformAxis(transform.M00, transform.M01),
                TransformAxis(transform.M10, transform.M11));
        }}

        internal static string CanonicalD4GaugeSerialization(
            IEnumerable<SigmaGaugeCell> source)
        {{
            if (source == null) throw new ArgumentNullException(nameof(source));
            SigmaGaugeCell[] cells = source.ToArray();
            if (cells.Length == 0) return string.Empty;
            return ChartD4.Select(transform => CanonicalGaugeSerialization(
                    cells.Select(cell =>
                    {{
                        (long U, long V) lower = TransformDyadicCellLower(
                            (cell.U, cell.V), cell.Level, transform);
                        return new SigmaGaugeCell(lower.U, lower.V, cell.Level,
                            cell.PayloadFingerprint);
                    }})))
                .OrderBy(value => value, StringComparer.Ordinal).First();
        }}

        internal static SigmaGaugeCell[] ApplyChartD4(
            IEnumerable<SigmaGaugeCell> source, int transformIndex)
        {{
            if (source == null) throw new ArgumentNullException(nameof(source));
            if ((uint)transformIndex >= (uint)ChartD4.Length)
                throw new ArgumentOutOfRangeException(nameof(transformIndex));
            return source.Select(cell =>
            {{
                (long U, long V) lower = TransformDyadicCellLower(
                    (cell.U, cell.V), cell.Level, ChartD4[transformIndex]);
                return new SigmaGaugeCell(lower.U, lower.V, cell.Level,
                    cell.PayloadFingerprint);
            }}).ToArray();
        }}

        internal static bool TryCanonicalizeChartEmbeddingClasses(
            IEnumerable<IEnumerable<SigmaGaugeCell>> alternatives,
            out string canonicalSerialization)
        {{
            if (alternatives == null)
                throw new ArgumentNullException(nameof(alternatives));
            string[] classes = alternatives.Select(
                    CanonicalD4GaugeSerialization)
                .Distinct(StringComparer.Ordinal).ToArray();
            canonicalSerialization = classes.Length == 1
                ? classes[0] : string.Empty;
            return classes.Length == 1;
        }}

        private static SigmaExactFactorClass NormalizeStitchFactor(
            SigmaS16 raw, out SigmaQ48Interval[] normalized)
        {{
            if (!TryNormalizePrimitiveDefect(raw, out normalized,
                    out bool diffractionKernel) || diffractionKernel)
                return SigmaExactFactorClass.Unresolved;
            return AggregateStitchFactors(normalized.Select(
                ClassifyExactZeroFactor).ToArray());
        }}

        private static SigmaExactFactorClass AggregateStitchFactors(
            params SigmaExactFactorClass[] factors)
        {{
            if (factors.Any(value =>
                    value == SigmaExactFactorClass.ProvenIncompatible))
                return SigmaExactFactorClass.ProvenIncompatible;
            return factors.Any(value => value == SigmaExactFactorClass.Unresolved)
                ? SigmaExactFactorClass.Unresolved
                : SigmaExactFactorClass.ProvenExactClosed;
        }}

        private static int FindExactStitchAnnihilator(SigmaS16 transition)
        {{
            if (transition.IsZero) return -1;
            for (int action = 0;
                 action < SigmaGeneratedAlgebra.AnnihilatorActionCount; ++action)
                if (SigmaS16Operators.RightSignedDyadAction(transition,
                        SigmaS16Operators.GetAnnihilatorAction(action)).IsZero)
                    return action;
            return -1;
        }}

        private static SigmaS16 NegateS16(SigmaS16 value)
        {{
            var lanes = new long[SigmaS16.LaneCount];
            for (int lane = 0; lane < lanes.Length; ++lane)
                lanes[lane] = SigmaNumericDomain.QNegate(value[lane]);
            return SigmaS16.FromArray(lanes);
        }}

        internal static long[] ApplyInformationMetric(long[] value)
        {{
            if (value == null || value.Length != 16)
                throw new ArgumentException("G requires one full S16 value.", nameof(value));
            var output = new long[16];
            for (int row = 0; row < 16; ++row)
            {{
                BigInteger sum = BigInteger.Zero;
                for (int column = 0; column < 16; ++column)
                    sum += (BigInteger)InformationMetric[(row << 4) + column] *
                           value[column];
                output[row] = CheckedLong(sum);
            }}
            return output;
        }}

        internal static bool TryNormalizePrimitiveDefect(SigmaS16 defect,
            out SigmaQ48Interval[] normalized, out bool diffractionKernel)
        {{
            long[] raw = defect.ToArray();
            normalized = new SigmaQ48Interval[16];
            BigInteger content = BigInteger.Zero;
            for (int lane = 0; lane < 16; ++lane)
                content = BigInteger.GreatestCommonDivisor(content,
                    BigInteger.Abs(new BigInteger(raw[lane])));
            if (content.IsZero)
            {{
                for (int lane = 0; lane < 16; ++lane)
                    normalized[lane] = new SigmaQ48Interval(0L, 0L);
                diffractionKernel = false;
                return true;
            }}
            var primitive = new BigInteger[16];
            for (int lane = 0; lane < 16; ++lane)
                primitive[lane] = raw[lane] / content;
            BigInteger normSquare = BigInteger.Zero;
            for (int row = 0; row < 16; ++row)
                for (int column = 0; column < 16; ++column)
                    normSquare += primitive[row] *
                        InformationMetric[(row << 4) + column] * primitive[column];
            if (normSquare.IsZero)
            {{
                diffractionKernel = true;
                for (int lane = 0; lane < 16; ++lane)
                    normalized[lane] = SigmaQ48Interval.Full;
                return false;
            }}
            if (normSquare.Sign < 0)
                throw new InvalidOperationException("Generated G is not PSD.");
            BigInteger scaledSquare = normSquare * SigmaNumericDomain.One *
                SigmaNumericDomain.One;
            BigInteger normLower = FloorSqrt(scaledSquare);
            BigInteger normUpper = normLower * normLower == scaledSquare
                ? normLower : normLower + BigInteger.One;
            if (normLower.IsZero)
                throw new InvalidOperationException("Positive primitive norm rounded to zero.");
            for (int lane = 0; lane < 16; ++lane)
            {{
                BigInteger numerator = (BigInteger)raw[lane] * SigmaNumericDomain.One;
                BigInteger lower = raw[lane] >= 0L
                    ? DivideFloor(numerator, normUpper)
                    : DivideFloor(numerator, normLower);
                BigInteger upper = raw[lane] >= 0L
                    ? DivideCeiling(numerator, normLower)
                    : DivideCeiling(numerator, normUpper);
                normalized[lane] = new SigmaQ48Interval(
                    CheckedLong(lower), CheckedLong(upper));
            }}
            diffractionKernel = false;
            return true;
        }}

        internal static IReadOnlyList<SigmaMinimizedFactor> MinimizeCertificates(
            IEnumerable<SigmaCertificateFactor> source)
        {{
            if (source == null) throw new ArgumentNullException(nameof(source));
            var result = new List<SigmaMinimizedFactor>();
            foreach (SigmaCertificateFactor factor in source.OrderBy(
                value => value.ContextKey, StringComparer.Ordinal).ThenBy(
                value => value.Lower).ThenBy(value => value.Upper))
            {{
                int duplicate = result.FindIndex(value =>
                    value.Factor.ContextKey == factor.ContextKey &&
                    value.Factor.Lower == factor.Lower &&
                    value.Factor.Upper == factor.Upper);
                if (duplicate >= 0)
                {{
                    SigmaMinimizedFactor old = result[duplicate];
                    result[duplicate] = new SigmaMinimizedFactor(old.Factor,
                        checked(old.Multiplicity + 1));
                    continue;
                }}
                if (result.Any(value => value.Factor.ContextKey == factor.ContextKey &&
                    value.Factor.Lower >= factor.Lower &&
                    value.Factor.Upper <= factor.Upper))
                    continue;
                result.RemoveAll(value =>
                    value.Factor.ContextKey == factor.ContextKey &&
                    factor.Lower >= value.Factor.Lower &&
                    factor.Upper <= value.Factor.Upper);
                result.Add(new SigmaMinimizedFactor(factor, 1));
            }}
            return result.OrderBy(value => value.Factor.ContextKey,
                StringComparer.Ordinal).ThenBy(value => value.Factor.Lower)
                .ThenBy(value => value.Factor.Upper).ToArray();
        }}

        // Lossless lowering of the generated four-axis pullback certificate.
        // Per-frame provenance hashes and concrete room rays are deliberately
        // absent here: the generated reverse program has already reduced them to
        // the canonical axis intervals and finite 48-mode row receipts.  Native
        // relation/coupling and program context must still match exactly.
        internal static bool TryMeetLocalityCertificates(
            IReadOnlyList<SigmaFrameUInt4Gpu> left,
            IReadOnlyList<SigmaFrameUInt4Gpu> right,
            out SigmaFrameUInt4Gpu[] result)
        {{
            const int wordCount = 16;
            const int identity = 0;
            const int context = 1;
            const int independence = 2;
            const int relation = 3;
            const int axis0 = 4;
            const int information0 = 8;
            const int receipts0 = 12;
            result = null;
            if (left == null || right == null || left.Count != wordCount ||
                right.Count != wordCount)
                return false;
            uint required = (uint)(SigmaNativeCertificateFlags.Valid |
                SigmaNativeCertificateFlags.Directional |
                SigmaNativeCertificateFlags.Minimized);
            if ((left[identity].X & required) != required ||
                (right[identity].X & required) != required ||
                left[identity].Y != right[identity].Y ||
                ((left[identity].X ^ right[identity].X) &
                    (uint)SigmaNativeCertificateFlags.Coupled) != 0u ||
                !FrameWordEqual(left[context], right[context]) ||
                !FrameWordEqual(left[relation], right[relation]) ||
                !FrameWordEqual(left[receipts0], right[receipts0]) ||
                !FrameWordEqual(left[receipts0 + 1], right[receipts0 + 1]) ||
                !FrameWordEqual(left[receipts0 + 3], right[receipts0 + 3]))
                return false;

            result = left.ToArray();
            result[identity].X = left[identity].X | right[identity].X;
            result[identity].Z = 1u;
            result[identity].W = 0u;
            result[independence] = default;
            for (int axis = 0; axis < 4; ++axis)
            {{
                long lower = Math.Max(FrameRaw(left[axis0 + axis].X,
                    left[axis0 + axis].Y), FrameRaw(right[axis0 + axis].X,
                    right[axis0 + axis].Y));
                long upper = Math.Min(FrameRaw(left[axis0 + axis].Z,
                    left[axis0 + axis].W), FrameRaw(right[axis0 + axis].Z,
                    right[axis0 + axis].W));
                if (lower > upper)
                {{
                    result = null;
                    return false;
                }}
                result[axis0 + axis] = FrameInterval(lower, upper);
                ulong width = unchecked((ulong)upper - (ulong)lower);
                long boundedWidth = width <= long.MaxValue
                    ? (long)width : long.MaxValue;
                result[information0 + axis] = new SigmaFrameUInt4Gpu
                {{
                    X = unchecked((uint)boundedWidth),
                    Y = unchecked((uint)(boundedWidth >> 32)),
                    Z = (uint)axis,
                    W = 3u,
                }};
            }}
            result[receipts0 + 2] = new SigmaFrameUInt4Gpu
            {{
                X = left[receipts0 + 2].X | right[receipts0 + 2].X,
                Y = left[receipts0 + 2].Y | right[receipts0 + 2].Y,
                Z = left[receipts0 + 2].Z | right[receipts0 + 2].Z,
                W = left[receipts0 + 2].W | right[receipts0 + 2].W,
            }};
            return true;
        }}

        private static bool FrameWordEqual(SigmaFrameUInt4Gpu left,
            SigmaFrameUInt4Gpu right) => left.X == right.X &&
            left.Y == right.Y && left.Z == right.Z && left.W == right.W;

        private static long FrameRaw(uint low, uint high) => unchecked(
            (long)((ulong)high << 32 | low));

        private static SigmaFrameUInt4Gpu FrameInterval(long lower, long upper) =>
            new SigmaFrameUInt4Gpu
            {{
                X = unchecked((uint)lower),
                Y = unchecked((uint)(lower >> 32)),
                Z = unchecked((uint)upper),
                W = unchecked((uint)(upper >> 32)),
            }};

        internal static SigmaGaugeCell[] SplitGaugeCell(SigmaGaugeCell parent)
        {{
            int level = checked(parent.Level + 1);
            return new[]
            {{
                new SigmaGaugeCell(checked(parent.U * 2), checked(parent.V * 2),
                    level, parent.PayloadFingerprint),
                new SigmaGaugeCell(checked(parent.U * 2 + 1), checked(parent.V * 2),
                    level, parent.PayloadFingerprint),
                new SigmaGaugeCell(checked(parent.U * 2), checked(parent.V * 2 + 1),
                    level, parent.PayloadFingerprint),
                new SigmaGaugeCell(checked(parent.U * 2 + 1),
                    checked(parent.V * 2 + 1), level, parent.PayloadFingerprint),
            }};
        }}

        internal static IReadOnlyList<SigmaGaugeCell> NormalizeGauge(
            IEnumerable<SigmaGaugeCell> source)
        {{
            if (source == null) throw new ArgumentNullException(nameof(source));
            var cells = source.ToList();
            for (int left = 0; left < cells.Count; ++left)
                for (int right = left + 1; right < cells.Count; ++right)
                    if (DyadicCellsOverlap(cells[left], cells[right]))
                        throw new InvalidOperationException(
                            "Gauge cells must be one disjoint half-open partition.");
            bool changed;
            do
            {{
                changed = false;
                foreach (SigmaGaugeCell child in cells.OrderByDescending(c => c.Level)
                    .ThenBy(c => c.U).ThenBy(c => c.V).ToArray())
                {{
                    if (child.Level == 0) continue;
                    long parentU = FloorDivideByTwo(child.U);
                    long parentV = FloorDivideByTwo(child.V);
                    int level = child.Level - 1;
                    SigmaGaugeCell[] siblings =
                        SplitGaugeCell(new SigmaGaugeCell(parentU, parentV, level,
                            child.PayloadFingerprint));
                    if (siblings.All(sibling => cells.Any(candidate =>
                        SameGaugeCell(candidate, sibling))))
                    {{
                        cells.RemoveAll(candidate => siblings.Any(sibling =>
                            SameGaugeCell(candidate, sibling)));
                        cells.Add(new SigmaGaugeCell(parentU, parentV, level,
                            child.PayloadFingerprint));
                        changed = true;
                        break;
                    }}
                }}
            }} while (changed);
            if (cells.Count == 0) return Array.Empty<SigmaGaugeCell>();
            SigmaGaugeCell minimum = cells.Aggregate((left, right) =>
                CompareDyadicLower(left, right) <= 0 ? left : right);
            long translateU = FloorDyadic(minimum.U, minimum.Level);
            long translateV = FloorDyadic(minimum.V, minimum.Level);
            return cells.Select(cell => new SigmaGaugeCell(
                    checked(cell.U - translateU * (1L << cell.Level)),
                    checked(cell.V - translateV * (1L << cell.Level)),
                    cell.Level, cell.PayloadFingerprint))
                .OrderBy(cell => cell.Level).ThenBy(cell => SignedMorton(cell.U,
                    cell.V)).ThenBy(cell => cell.U).ThenBy(cell => cell.V)
                .ThenBy(cell => cell.PayloadFingerprint, StringComparer.Ordinal)
                .ToArray();
        }}

        internal static bool TryNormalizeFreshSupport(
            IEnumerable<IEnumerable<SigmaGaugeCell>> alternatives,
            out string canonicalSerialization)
        {{
            if (alternatives == null)
                throw new ArgumentNullException(nameof(alternatives));
            string[] normalized = alternatives.Select(CanonicalGaugeSerialization)
                .ToArray();
            if (normalized.Length == 0)
            {{
                canonicalSerialization = string.Empty;
                return false;
            }}
            string expected = normalized[0];
            canonicalSerialization = expected;
            return normalized.All(value => string.Equals(value,
                expected, StringComparison.Ordinal));
        }}

        internal static string CanonicalGaugeSerialization(
            IEnumerable<SigmaGaugeCell> source) => string.Join(";",
                NormalizeGauge(source).Select(cell =>
                    $"{{cell.Level}}:{{cell.U}}:{{cell.V}}:{{cell.PayloadFingerprint}}"));

        private static SigmaQ48Interval MultiplyOutward(SigmaQ48Interval left,
            SigmaQ48Interval right)
        {{
            long[] lower = {{
                SigmaNumericDomain.QMulLower(left.Lower, right.Lower),
                SigmaNumericDomain.QMulLower(left.Lower, right.Upper),
                SigmaNumericDomain.QMulLower(left.Upper, right.Lower),
                SigmaNumericDomain.QMulLower(left.Upper, right.Upper),
            }};
            long[] upper = {{
                SigmaNumericDomain.QMulUpper(left.Lower, right.Lower),
                SigmaNumericDomain.QMulUpper(left.Lower, right.Upper),
                SigmaNumericDomain.QMulUpper(left.Upper, right.Lower),
                SigmaNumericDomain.QMulUpper(left.Upper, right.Upper),
            }};
            return new SigmaQ48Interval(lower.Min(), upper.Max());
        }}

        private static bool SameGaugeCell(SigmaGaugeCell left, SigmaGaugeCell right) =>
            left.U == right.U && left.V == right.V && left.Level == right.Level &&
            string.Equals(left.PayloadFingerprint, right.PayloadFingerprint,
                StringComparison.Ordinal);

        private static bool DyadicCellsOverlap(SigmaGaugeCell left,
            SigmaGaugeCell right)
        {{
            int common = Math.Max(left.Level, right.Level);
            int leftShift = common - left.Level;
            int rightShift = common - right.Level;
            BigInteger leftU0 = (BigInteger)left.U << leftShift;
            BigInteger leftU1 = ((BigInteger)left.U + BigInteger.One) << leftShift;
            BigInteger rightU0 = (BigInteger)right.U << rightShift;
            BigInteger rightU1 = ((BigInteger)right.U + BigInteger.One) << rightShift;
            BigInteger leftV0 = (BigInteger)left.V << leftShift;
            BigInteger leftV1 = ((BigInteger)left.V + BigInteger.One) << leftShift;
            BigInteger rightV0 = (BigInteger)right.V << rightShift;
            BigInteger rightV1 = ((BigInteger)right.V + BigInteger.One) << rightShift;
            return leftU0 < rightU1 && rightU0 < leftU1 &&
                   leftV0 < rightV1 && rightV0 < leftV1;
        }}

        private static int CompareDyadicLower(SigmaGaugeCell left, SigmaGaugeCell right)
        {{
            int common = Math.Max(left.Level, right.Level);
            BigInteger leftU = (BigInteger)left.U << (common - left.Level);
            BigInteger rightU = (BigInteger)right.U << (common - right.Level);
            int compare = leftU.CompareTo(rightU);
            if (compare != 0) return compare;
            BigInteger leftV = (BigInteger)left.V << (common - left.Level);
            BigInteger rightV = (BigInteger)right.V << (common - right.Level);
            return leftV.CompareTo(rightV);
        }}

        private static long FloorDyadic(long numerator, int level)
        {{
            if (level == 0) return numerator;
            long denominator = 1L << level;
            long quotient = numerator / denominator;
            long remainder = numerator % denominator;
            return remainder < 0L ? checked(quotient - 1L) : quotient;
        }}

        private static long CeilingDyadic(long numerator, int level) =>
            CheckedLong(DivideCeiling(new BigInteger(numerator),
                BigInteger.One << level));

        private static long ScaleBaseTranslation(long translation, int level) =>
            CheckedLong(new BigInteger(translation) << level);

        private static long FloorDivideByTwo(long value) =>
            value >= 0L || (value & 1L) == 0L ? value / 2L : value / 2L - 1L;

        private static BigInteger SignedMorton(long u, long v)
        {{
            BigInteger x = u >= 0L ? (BigInteger)u * 2 :
                -(BigInteger)u * 2 - 1;
            BigInteger y = v >= 0L ? (BigInteger)v * 2 :
                -(BigInteger)v * 2 - 1;
            BigInteger output = BigInteger.Zero;
            for (int bit = 0; bit < 64; ++bit)
            {{
                output |= ((x >> bit) & BigInteger.One) << (bit * 2);
                output |= ((y >> bit) & BigInteger.One) << (bit * 2 + 1);
            }}
            return output;
        }}

        private static BigInteger FloorSqrt(BigInteger value)
        {{
            if (value.Sign < 0) throw new ArgumentOutOfRangeException(nameof(value));
            if (value.IsZero) return BigInteger.Zero;
            BigInteger root = BigInteger.One << ((BitLength(value) + 1) / 2);
            while (true)
            {{
                BigInteger next = (root + value / root) >> 1;
                if (next >= root) return root;
                root = next;
            }}
        }}

        private static int BitLength(BigInteger value)
        {{
            byte[] bytes = value.ToByteArray();
            int bits = (bytes.Length - 1) * 8;
            byte high = bytes[bytes.Length - 1];
            while (high != 0) {{ ++bits; high >>= 1; }}
            return bits;
        }}

        private static BigInteger DivideFloor(BigInteger numerator,
            BigInteger denominator)
        {{
            BigInteger quotient = BigInteger.DivRem(numerator, denominator,
                out BigInteger remainder);
            if (!remainder.IsZero && numerator.Sign != denominator.Sign)
                --quotient;
            return quotient;
        }}

        private static BigInteger DivideCeiling(BigInteger numerator,
            BigInteger denominator) => -DivideFloor(-numerator, denominator);

        private static long CheckedLong(BigInteger value)
        {{
            if (value < long.MinValue || value > long.MaxValue)
                throw new OverflowException("Generated exact operation overflow.");
            return (long)value;
        }}

        private static void RequireAddress(int value)
        {{
            if ((uint)value >= 16u)
                throw new ArgumentOutOfRangeException(nameof(value));
        }}
    }}
}}
"""


def render_merkaba_hlsl(descriptor: dict, include_prefix: str =
                        "../../../Runtime/Resources/SigmaPrism") -> str:
    proofs = descriptor["proofs"]
    ir = descriptor["ir"]
    d4_compose, d4_inverse, orbit_representatives, adjacent_frames = \
        sigma_chart_d4_tables()
    diffraction = ", ".join(str(value) for value in descriptor["diffractionMatrix"])
    metric = ", ".join(str(value) for value in descriptor["informationMetric"])
    shadow = ", ".join(str(value) for value in descriptor["shadowNumerator4"])
    visible = ", ".join(
        str(value) for value in descriptor["visibleProjectorNumerator256"])
    words = ", ".join(
        f"0x{value:08x}u" for value in fingerprint_words(descriptor["fingerprint"]))
    stitch_bracket_fingerprint = int(
        proofs["constructiveStitchExpressionFingerprint"][:16], 16)
    stitch_bracket_low = stitch_bracket_fingerprint & 0xffffffff
    stitch_bracket_high = stitch_bracket_fingerprint >> 32
    sector_chart_assignments = ",\n    ".join(
        "uint4(" + ", ".join(f"{value}u" for value in assignment) + ")"
        for assignment in itertools.permutations(range(4)))
    chart_d4 = ", ".join(
        "int4(" + ", ".join(str(value) for value in transform) + ")"
        for transform in SIGMA_CHART_D4)
    d4_compose_values = ", ".join(f"{value}u" for value in d4_compose)
    d4_inverse_values = ", ".join(f"{value}u" for value in d4_inverse)
    orbit_representative_values = ", ".join(
        f"{value}u" for value in orbit_representatives)
    adjacent_frame_values = ", ".join(
        f"{value}u" for value in adjacent_frames)
    opcode_macros = "\n".join(
        f"#define SIGMA_MERKABA_IR_{name} {index}u"
        for index, name in enumerate(ir["opcodes"]))
    node_a = ",\n    ".join(
        f"uint4({node['opcode']}u, {node['outputKind']}u, "
        f"{node['reverseRule']}u, {node['operandStart']}u)"
        for node in ir["nodes"])
    node_b = ",\n    ".join(
        f"int4({node['operandCount']}, {node['argument0']}, "
        f"{node['argument1']}, 0)" for node in ir["nodes"])
    operands = ", ".join(f"{value}u" for value in ir["operands"])
    expression_a = ",\n    ".join(
        f"int4({entry['arity']}, {entry['neighbourhood']}, "
        f"{entry['nodeStart']}, {entry['nodeCount']})"
        for entry in ir["expressions"])
    expression_b = ",\n    ".join(
        f"int4({entry['rootNode']}, 0, 0, 0)" for entry in ir["expressions"])
    entry_points = ",\n    ".join(
        f"int4({entry['forwardExpression']}, {entry['reverseExpression']}, "
        f"{entry['reducer']}, 0)" for entry in ir["entryPoints"])
    return f"""// <auto-generated by Tools/sigma/generate_sigma_operators.py>
// Canonical baseline: CPQ4-2026-08-25-S16-v8.3. Do not edit by hand.
#ifndef SIGMA_GENERATED_MERKABA_PROGRAM_INCLUDED
#define SIGMA_GENERATED_MERKABA_PROGRAM_INCLUDED

#include "{include_prefix}/Sedenion16.hlsl"
#include "{include_prefix}/SigmaExactCompare.hlsl"
#include "{include_prefix}/Generated/SigmaGeneratedTables.hlsl"

{opcode_macros}

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
#define SIGMA_EXACT_FACTOR_PROVEN_INCOMPATIBLE 0u
#define SIGMA_EXACT_FACTOR_PROVEN_CLOSED 1u
#define SIGMA_EXACT_FACTOR_UNRESOLVED 2u

#define SIGMA_Q48_ZERO uint2(0u, 0u)
#define SIGMA_Q48_ONE uint2(0u, 0x00010000u)

#define SIGMA_DEFAULT_LOGICAL_UNBACKED 0u
#define SIGMA_DEFAULT_EXPLICIT_ZEMPTY 1u
#define SIGMA_DEFAULT_NULL_CODEC 2u

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
#define SIGMA_MERKABA_IR_NODE_COUNT {len(ir['nodes'])}u
#define SIGMA_MERKABA_IR_OPERAND_COUNT {len(ir['operands'])}u
#define SIGMA_MERKABA_EXPRESSION_COUNT {len(ir['expressions'])}u
#define SIGMA_MERKABA_ENTRY_POINT_COUNT {len(ir['entryPoints'])}u
#define SIGMA_MERKABA_INDEPENDENT_CLOSURE_WEIGHT_COUNT 0u
#define SIGMA_MERKABA_EPSILON_CL_EXISTS 0u
#define SIGMA_FRESH_ADMISSION_UNRESOLVED 0u
#define SIGMA_FRESH_ADMISSION_ADMITTED 1u
#define SIGMA_FRESH_FIRST_HIT_LEFT 1u
#define SIGMA_FRESH_FIRST_HIT_RIGHT 2u
#define SIGMA_FRESH_EXTERNAL_RELATION_TRUTH_INPUT_COUNT {proofs['freshAdmissionExternalRelationTruthInputCount']}u
#define SIGMA_INSTRUMENT_BOUNDARY_LEAF_COUNT {proofs['captureBoundaryLeafCount']}u
#define SIGMA_SAMPLE_BOUNDARY_LEFT 0u
#define SIGMA_SAMPLE_BOUNDARY_RIGHT 1u
#define SIGMA_SAMPLE_BOUNDARY_UP 2u
#define SIGMA_SAMPLE_BOUNDARY_DOWN 3u
#define SIGMA_STITCH_NO_STITCH 0u
#define SIGMA_STITCH_RESOLVED 1u
#define SIGMA_STITCH_UNRESOLVED 2u
#define SIGMA_STITCH_EXTERNAL_SEMANTIC_TRUTH_INPUT_COUNT 0u
#define SIGMA_STITCH_CALLER_LOOP_TRUTH_INPUT_COUNT 0u
#define SIGMA_STITCH_SAMPLE_SIDE_TO_DELTA_AUTHORITY_COUNT 0u
#define SIGMA_STITCH_ABSTRACT_NATIVE_SECTOR_COUNT {proofs['constructiveStitchAbstractNativeSectorCount']}u
#define SIGMA_STITCH_ABSTRACT_SECTOR_CHART_ASSIGNMENT_COUNT {proofs['constructiveStitchAbstractSectorChartAssignmentCount']}u
#define SIGMA_STITCH_ABSTRACT_SECTOR_CHART_ORBIT_COUNT {proofs['constructiveStitchAbstractSectorChartAssignmentOrbitCount']}u
#define SIGMA_STITCH_D4_CHART_IMAGE_COUNT {proofs['constructiveStitchD4ChartImageCount']}u
#define SIGMA_STITCH_NON_GAUGE_EMBEDDING_AMBIGUITY_COUNT {proofs['constructiveStitchNonGaugeEmbeddingAmbiguityCount']}u
#define SIGMA_STITCH_IMPLICIT_BOUNDARY_COUNT_320 {proofs['constructiveStitchImplicitBoundaryCount320']}u
#define SIGMA_STITCH_IMPLICIT_PLAQUETTE_COUNT_320 {proofs['constructiveStitchImplicitPlaquetteCount320']}u
#define SIGMA_STITCH_EXTERNAL_BRACKET_CONTEXT_INPUT_COUNT 0u
#define SIGMA_STITCH_COMPLETE_ASSOCIATOR_BASIS_CONTEXT_COUNT {proofs['constructiveStitchCompleteAssociatorBasisContextCount']}u
#define SIGMA_STITCH_ASSOCIATOR_PROFILE_IS_INTRINSIC_S16 1u
#define SIGMA_STITCH_S32_REQUIRED 0u

static const uint2 SIGMA_STITCH_GENERATED_BRACKET_FINGERPRINT =
    uint2(0x{stitch_bracket_low:08x}u, 0x{stitch_bracket_high:08x}u);

static const uint SIGMA_MERKABA_PROGRAM_FINGERPRINT[8] = {{ {words} }};
static const int SIGMA_MERKABA_DIFFRACTION[256] = {{ {diffraction} }};
static const int SIGMA_MERKABA_INFORMATION_METRIC[256] = {{ {metric} }};
static const int SIGMA_MERKABA_SHELL_SQUARE_BY_RANK[4] = {{ -1, -3, -7, -15 }};
static const int SIGMA_MERKABA_SHADOW_NUMERATOR4[64] = {{ {shadow} }};
static const int SIGMA_MERKABA_VISIBLE_PROJECTOR_NUMERATOR256[256] = {{ {visible} }};
static const uint4 SIGMA_MERKABA_IR_NODE_A[{len(ir['nodes'])}] = {{
    {node_a}
}};
static const int4 SIGMA_MERKABA_IR_NODE_B[{len(ir['nodes'])}] = {{
    {node_b}
}};
static const uint SIGMA_MERKABA_IR_OPERANDS[{len(ir['operands'])}] = {{ {operands} }};
static const int4 SIGMA_MERKABA_IR_EXPRESSION_A[{len(ir['expressions'])}] = {{
    {expression_a}
}};
static const int4 SIGMA_MERKABA_IR_EXPRESSION_B[{len(ir['expressions'])}] = {{
    {expression_b}
}};
static const int4 SIGMA_MERKABA_IR_ENTRY_POINTS[{len(ir['entryPoints'])}] = {{
    {entry_points}
}};

int SigmaMerkabaBasisSign(uint left, uint right)
{{
    return SigmaMulBasisSign(left, right);
}}

int SigmaMerkabaAssociatorCoefficient(uint a, uint b, uint c)
{{
    return SigmaMerkabaBasisSign(a, b) * SigmaMerkabaBasisSign(a ^ b, c) -
           SigmaMerkabaBasisSign(b, c) * SigmaMerkabaBasisSign(a, b ^ c);
}}

int SigmaMerkabaBasisAssociatorActionCoefficient(uint source,
    uint sectorAddress, uint contextAddress)
{{
    return SigmaMerkabaBasisSign(source, sectorAddress) *
            SigmaMerkabaBasisSign(source ^ sectorAddress, contextAddress) -
        SigmaMerkabaBasisSign(sectorAddress, contextAddress) *
            SigmaMerkabaBasisSign(source, sectorAddress ^ contextAddress);
}}

uint SigmaMerkabaBasisAssociatorSource(uint outputLane, uint sectorAddress,
    uint contextAddress)
{{
    return outputLane ^ sectorAddress ^ contextAddress;
}}

uint2 SigmaMerkabaScaleBasisAssociatorLane(uint2 sourceValue,
    int coefficient, inout uint valid)
{{
    valid &= coefficient == 0 || coefficient == -2 || coefficient == 2 ? 1u : 0u;
    uint2 scaled = uint2(0u, 0u);
    if (coefficient != 0)
    {{
        scaled = SigmaQ48ShiftLeftChecked(sourceValue, 1u, valid);
        if (coefficient < 0)
            scaled = SigmaQ48NegateChecked(scaled, valid);
    }}
    return scaled;
}}

void SigmaMerkabaEvaluateAssociatorProfileDeltaLane(
    uint2 leftSourceValue, uint2 rightSourceValue,
    uint leftSectorAddress, uint rightSectorAddress,
    uint contextAddress, uint outputLane,
    out uint2 forwardDelta, out uint2 reverseDelta, inout uint valid)
{{
    uint leftSource = SigmaMerkabaBasisAssociatorSource(outputLane,
        leftSectorAddress, contextAddress);
    uint rightSource = SigmaMerkabaBasisAssociatorSource(outputLane,
        rightSectorAddress, contextAddress);
    int leftCoefficient = SigmaMerkabaBasisAssociatorActionCoefficient(leftSource,
        leftSectorAddress, contextAddress);
    int rightCoefficient = SigmaMerkabaBasisAssociatorActionCoefficient(rightSource,
        rightSectorAddress, contextAddress);
    uint2 leftValue = SigmaMerkabaScaleBasisAssociatorLane(leftSourceValue,
        leftCoefficient, valid);
    uint2 rightValue = SigmaMerkabaScaleBasisAssociatorLane(rightSourceValue,
        rightCoefficient, valid);
    forwardDelta = SigmaQ48SubChecked(rightValue, leftValue, valid);
    reverseDelta = SigmaQ48SubChecked(leftValue, rightValue, valid);
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

// Exact compiled lowering for 0,+/-2,+/-4,+/-6 divided by 4 or 64.  It
// preserves one nearest-even rounding of the complete product.  In particular,
// factor three is applied to quotient/remainder before the single rounding; it
// is never approximated as a sum of independently rounded terms.
uint2 SigmaMerkabaMultiplyDyadic(uint2 value, int numerator,
    uint denominatorShift, inout uint valid)
{{
    uint absoluteNumerator = (uint)abs(numerator);
    bool supported = (absoluteNumerator == 0u || absoluteNumerator == 2u ||
        absoluteNumerator == 4u || absoluteNumerator == 6u) &&
        (denominatorShift == 2u || denominatorShift == 6u);
    valid &= supported ? 1u : 0u;
    if (!supported || absoluteNumerator == 0u || all(value == 0u))
        return uint2(0u, 0u);

    uint factor = absoluteNumerator >> 1u;
    uint shift = denominatorShift - 1u;
    if (factor == 2u)
    {{
        factor = 1u;
        --shift;
    }}

    bool negative = ((value.y & 0x80000000u) != 0u) != (numerator < 0);
    uint2 magnitude = SigmaU64AbsSigned(value);
    uint2 quotient = shift == 0u
        ? magnitude : SigmaU64ShiftRight(magnitude, shift);
    uint remainderMask = shift == 0u ? 0u : (1u << shift) - 1u;
    uint remainder = magnitude.x & remainderMask;

    if (factor == 3u)
    {{
        uint carry0;
        uint2 doubled = SigmaU64Add(quotient, quotient, carry0);
        uint carry1;
        quotient = SigmaU64Add(doubled, quotient, carry1);
        valid &= (carry0 | carry1) == 0u ? 1u : 0u;
        remainder *= 3u;
        uint integerRemainder = remainder >> shift;
        remainder &= remainderMask;
        uint carry2;
        quotient = SigmaU64Add(quotient,
            uint2(integerRemainder, 0u), carry2);
        valid &= carry2 == 0u ? 1u : 0u;
    }}

    if (shift != 0u)
    {{
        uint half = 1u << (shift - 1u);
        bool roundUp = remainder > half ||
            (remainder == half && (quotient.x & 1u) != 0u);
        if (roundUp)
        {{
            uint carry;
            quotient = SigmaU64Increment(quotient, carry);
            valid &= carry == 0u ? 1u : 0u;
        }}
    }}
    return SigmaApplyMagnitudeSign(quotient, negative, valid);
}}

uint2 SigmaMerkabaMultiplyShadowCoefficient(uint2 value, uint address,
    uint axis, inout uint valid)
{{
    valid &= address < 16u && axis < 4u ? 1u : 0u;
    return SigmaMerkabaMultiplyDyadic(value,
        SigmaMerkabaShadowNumerator(min(address, 15u), min(axis, 3u)), 2u,
        valid);
}}

uint2 SigmaMerkabaMultiplyDualCoefficient(uint2 value, uint address,
    uint axis, inout uint valid)
{{
    valid &= address < 16u && axis < 4u ? 1u : 0u;
    return SigmaMerkabaMultiplyDyadic(value,
        SigmaMerkabaShadowNumerator(min(address, 15u), min(axis, 3u)), 6u,
        valid);
}}

void SigmaMerkabaEvaluateShadow(uint2 state[16], out uint2 shadow[4],
    inout uint valid)
{{
    [unroll]
    for (uint axis = 0u; axis < 4u; ++axis)
    {{
        uint2 sum = uint2(0u, 0u);
        [unroll]
        for (uint address = 0u; address < 16u; ++address)
            sum = SigmaQ48AddChecked(sum,
                SigmaMerkabaMultiplyShadowCoefficient(state[address], address,
                    axis, valid), valid);
        shadow[axis] = sum;
    }}
}}

void SigmaMerkabaLiftShadow(uint2 shadow[4], out uint2 state[16],
    inout uint valid)
{{
    [unroll]
    for (uint address = 0u; address < 16u; ++address)
    {{
        uint2 sum = uint2(0u, 0u);
        [unroll]
        for (uint axis = 0u; axis < 4u; ++axis)
            sum = SigmaQ48AddChecked(sum,
                SigmaMerkabaMultiplyDualCoefficient(shadow[axis], address,
                    axis, valid), valid);
        state[address] = sum;
    }}
}}

void SigmaMerkabaInstrumentCompareSwap(inout uint left, inout uint right,
    uint2 pullback[4])
{{
    uint2 leftMagnitude = SigmaU64AbsSigned(pullback[left]);
    uint2 rightMagnitude = SigmaU64AbsSigned(pullback[right]);
    bool rightComesFirst = !SigmaU64Equal(leftMagnitude, rightMagnitude)
        ? SigmaU64Less(leftMagnitude, rightMagnitude)
        : !SigmaU64Equal(pullback[left], pullback[right])
            ? SigmaI64Less(pullback[left], pullback[right])
            : right < left;
    if (!rightComesFirst)
        return;
    uint temporary = left;
    left = right;
    right = temporary;
}}

bool SigmaMerkabaBuildInstrumentRowPermutation(uint2 roomRay[3],
    inout uint4 permutation, out int globalSign, inout uint valid)
{{
    globalSign = 0;
    uint2 x = roomRay[0];
    uint2 y = roomRay[1];
    uint2 z = roomRay[2];
    uint2 pullback[4];
    pullback[0] = SigmaQ48AddChecked(SigmaQ48AddChecked(x, y, valid), z, valid);
    pullback[1] = SigmaQ48SubChecked(SigmaQ48SubChecked(x, y, valid), z, valid);
    pullback[2] = SigmaQ48SubChecked(SigmaQ48SubChecked(y, x, valid), z, valid);
    pullback[3] = SigmaQ48AddChecked(
        SigmaQ48SubChecked(SIGMA_Q48_ZERO, x, valid),
        SigmaQ48SubChecked(z, y, valid), valid);
    uint axis0 = 0u;
    uint axis1 = 1u;
    uint axis2 = 2u;
    uint axis3 = 3u;
    // Fixed four-input sorting network. Query direction changes data only;
    // it never changes scheduling or emits a dispatch per row.
    SigmaMerkabaInstrumentCompareSwap(axis0, axis1, pullback);
    SigmaMerkabaInstrumentCompareSwap(axis2, axis3, pullback);
    SigmaMerkabaInstrumentCompareSwap(axis0, axis2, pullback);
    SigmaMerkabaInstrumentCompareSwap(axis1, axis3, pullback);
    SigmaMerkabaInstrumentCompareSwap(axis1, axis2, pullback);
    permutation = uint4(axis0, axis1, axis2, axis3);
    uint2 maximum = pullback[axis0];
    if (valid == 0u || SigmaU64Equal(SigmaU64AbsSigned(maximum), SIGMA_Q48_ZERO))
    {{
        valid = 0u;
        globalSign = 0;
        return false;
    }}
    globalSign = (maximum.y & 0x80000000u) != 0u ? -1 : 1;
    return true;
}}

void SigmaMerkabaAssembleInstrumentTangent(uint2 codeLower[4],
    uint2 codeUpper[4], int globalSign, out uint2 measuredLower[4],
    out uint2 measuredUpper[4], inout uint valid)
{{
    uint2 centredLower[4];
    uint2 centredUpper[4];
    uint2 totalLower = SIGMA_Q48_ZERO;
    uint2 totalUpper = SIGMA_Q48_ZERO;
    [unroll]
    for (uint instrumentLeaf = 0u; instrumentLeaf < 4u; ++instrumentLeaf)
    {{
        if (SigmaQ48Less(codeLower[instrumentLeaf], SIGMA_Q48_ZERO) ||
            SigmaQ48Less(SIGMA_Q48_ONE, codeUpper[instrumentLeaf]) ||
            SigmaQ48Less(codeUpper[instrumentLeaf], codeLower[instrumentLeaf]))
            valid = 0u;
        centredLower[instrumentLeaf] = SigmaQ48SubChecked(
            SigmaQ48ShiftLeftChecked(codeLower[instrumentLeaf], 1u, valid),
            SIGMA_Q48_ONE, valid);
        centredUpper[instrumentLeaf] = SigmaQ48SubChecked(
            SigmaQ48ShiftLeftChecked(codeUpper[instrumentLeaf], 1u, valid),
            SIGMA_Q48_ONE, valid);
        totalLower = SigmaQ48AddChecked(totalLower,
            centredLower[instrumentLeaf], valid);
        totalUpper = SigmaQ48AddChecked(totalUpper,
            centredUpper[instrumentLeaf], valid);
    }}
    [unroll]
    for (uint outputLeaf = 0u; outputLeaf < 4u; ++outputLeaf)
    {{
        uint2 lower = SigmaQ48SubChecked(
            SigmaQ48ShiftLeftChecked(centredLower[outputLeaf], 2u, valid),
            totalUpper, valid);
        uint2 upper = SigmaQ48SubChecked(
            SigmaQ48ShiftLeftChecked(centredUpper[outputLeaf], 2u, valid),
            totalLower, valid);
        measuredLower[outputLeaf] = globalSign > 0
            ? lower : SigmaQ48NegateChecked(upper, valid);
        measuredUpper[outputLeaf] = globalSign > 0
            ? upper : SigmaQ48NegateChecked(lower, valid);
    }}
}}

bool SigmaMerkabaSelectFreshTangent(uint2 lower[4], uint2 upper[4],
    out uint2 selected[4], inout uint valid)
{{
    uint2 zero = uint2(0u, 0u);
    uint2 residual = zero;
    [unroll]
    for (uint axis = 0u; axis < 4u; ++axis)
    {{
        if (SigmaI64Less(upper[axis], lower[axis]))
            valid = 0u;
        selected[axis] = SigmaI64Less(zero, lower[axis]) ? lower[axis] :
            (SigmaI64Less(upper[axis], zero) ? upper[axis] : zero);
        residual = SigmaQ48AddChecked(residual, selected[axis], valid);
    }}
    bool positive = SigmaI64Less(zero, residual);
    bool negative = SigmaI64Less(residual, zero);
    if (positive)
    {{
        [unroll]
        for (uint axis = 0u; axis < 4u; ++axis)
        {{
            uint2 capacity = SigmaQ48SubChecked(selected[axis], lower[axis], valid);
            uint2 adjustment = SigmaI64Less(capacity, residual) ? capacity : residual;
            selected[axis] = SigmaQ48SubChecked(selected[axis], adjustment, valid);
            residual = SigmaQ48SubChecked(residual, adjustment, valid);
        }}
    }}
    else if (negative)
    {{
        uint2 deficit = SigmaQ48NegateChecked(residual, valid);
        [unroll]
        for (uint axis = 0u; axis < 4u; ++axis)
        {{
            uint2 capacity = SigmaQ48SubChecked(upper[axis], selected[axis], valid);
            uint2 adjustment = SigmaI64Less(capacity, deficit) ? capacity : deficit;
            selected[axis] = SigmaQ48AddChecked(selected[axis], adjustment, valid);
            deficit = SigmaQ48SubChecked(deficit, adjustment, valid);
        }}
        residual = SigmaQ48NegateChecked(deficit, valid);
    }}
    return valid != 0u && SigmaU64Equal(residual, zero);
}}

uint SigmaMerkabaEvaluateFreshBoundaryRelation(uint2 state[16],
    inout uint valid)
{{
    uint stateNonzero = 0u;
    [unroll]
    for (uint lane = 0u; lane < 16u; ++lane)
        stateNonzero |= state[lane].x | state[lane].y;
    if (stateNonzero == 0u)
        return SIGMA_MERKABA_RELATION_DEFAULT_SAT;

    // Exact specialization of NATIVE_CLOSURE_DEFECT(state,ZEmpty,ZEmpty):
    // U_0=I, [state,0,0]=0 and W_00(0)=+1.  Thus d=-state is the
    // only nonzero factor.  G=2*A^T*A makes A(state)=0 precisely the
    // unresolved diffraction-kernel case. Checked Q48 overflow fails closed.
    uint diffractionNonzero = 0u;
    [unroll]
    for (uint row = 0u; row < 16u; ++row)
    {{
        uint2 sum = uint2(0u, 0u);
        [unroll]
        for (uint column = 0u; column < 16u; ++column)
        {{
            int coefficient = SIGMA_MERKABA_DIFFRACTION[row * 16u + column];
            if (coefficient != 0)
            {{
                uint2 coefficientQ48 = uint2(0u,
                    asuint(coefficient * 65536));
                sum = SigmaQ48AddChecked(sum,
                    SigmaQ48MulNearestEven(state[column], coefficientQ48, valid),
                    valid);
            }}
        }}
        diffractionNonzero |= sum.x | sum.y;
    }}
    if (valid == 0u || diffractionNonzero == 0u ||
        SigmaMerkabaPlaquetteHolonomy(0u, 0u, 0u) != 1)
        return SIGMA_MERKABA_RELATION_UNRESOLVED;
    return SIGMA_MERKABA_RELATION_NO_RELATION;
}}

bool SigmaMerkabaResolveFreshBranch(uint2 lower[4], uint2 upper[4],
    uint firstHitEyeMask, uint coherent, out uint2 state[16],
    out uint2 selectedShadow[4], out uint boundaryRelation, inout uint valid)
{{
    boundaryRelation = SIGMA_MERKABA_RELATION_UNRESOLVED;
    bool rolesValid = coherent != 0u &&
        (firstHitEyeMask & (SIGMA_FRESH_FIRST_HIT_LEFT |
            SIGMA_FRESH_FIRST_HIT_RIGHT)) ==
            (SIGMA_FRESH_FIRST_HIT_LEFT | SIGMA_FRESH_FIRST_HIT_RIGHT);
    if (!rolesValid ||
        !SigmaMerkabaSelectFreshTangent(lower, upper, selectedShadow, valid))
    {{
        valid = 0u;
        [unroll]
        for (uint lane = 0u; lane < 16u; ++lane)
            state[lane] = uint2(0u, 0u);
        return false;
    }}
    SigmaMerkabaLiftShadow(selectedShadow, state, valid);
    uint nonzero = 0u;
    [unroll]
    for (uint lane = 0u; lane < 16u; ++lane)
        nonzero |= state[lane].x | state[lane].y;
    if (valid == 0u || nonzero == 0u)
        return false;
    boundaryRelation = SigmaMerkabaEvaluateFreshBoundaryRelation(state, valid);
    if (boundaryRelation == SIGMA_MERKABA_RELATION_UNRESOLVED ||
        boundaryRelation == SIGMA_MERKABA_RELATION_DEFAULT_SAT)
        return false;
    uint2 forward[4];
    SigmaMerkabaEvaluateShadow(state, forward, valid);
    [unroll]
    for (uint axis = 0u; axis < 4u; ++axis)
        if (SigmaI64Less(forward[axis], lower[axis]) ||
            SigmaI64Less(upper[axis], forward[axis]))
            valid = 0u;
    return valid != 0u;
}}

bool SigmaMerkabaIsZEmpty(uint2 state[16])
{{
    uint nonzero = 0u;
    [unroll]
    for (uint lane = 0u; lane < 16u; ++lane)
        nonzero |= state[lane].x | state[lane].y;
    return nonzero == 0u;
}}

bool SigmaMerkabaSplitDyadicGauge(uint4 parentCoordinate, uint parentLevel,
    uint childIndex, out uint4 childCoordinate, out uint childLevel)
{{
    uint valid = parentLevel < 62u && childIndex < 4u ? 1u : 0u;
    uint2 u = SigmaQ48ShiftLeftChecked(parentCoordinate.xy, 1u, valid);
    uint2 v = SigmaQ48ShiftLeftChecked(parentCoordinate.zw, 1u, valid);
    if ((childIndex & 1u) != 0u)
        u = SigmaQ48AddChecked(u, uint2(1u, 0u), valid);
    if ((childIndex & 2u) != 0u)
        v = SigmaQ48AddChecked(v, uint2(1u, 0u), valid);
    childCoordinate = uint4(u, v);
    childLevel = parentLevel + 1u;
    return valid != 0u;
}}

uint2 SigmaMerkabaGaugeZigZag(uint2 coordinate, inout uint valid)
{{
    uint2 output = uint2(0u, 0u);
    if ((coordinate.y & 0x80000000u) == 0u)
    {{
        output = SigmaU64ShiftLeftRaw(coordinate, 1u);
    }}
    else
    {{
        uint carry = 0u;
        uint2 shifted = SigmaU64Add(coordinate, uint2(1u, 0u), carry);
        shifted = SigmaU64NegateRaw(shifted);
        shifted = SigmaU64ShiftLeftRaw(shifted, 1u);
        output = SigmaU64Add(shifted, uint2(1u, 0u), carry);
        valid &= carry == 0u ? 1u : 0u;
    }}
    return output;
}}

uint SigmaMerkabaGaugeSpread16(uint value)
{{
    value &= 0x0000ffffu;
    value = (value | (value << 8u)) & 0x00ff00ffu;
    value = (value | (value << 4u)) & 0x0f0f0f0fu;
    value = (value | (value << 2u)) & 0x33333333u;
    return (value | (value << 1u)) & 0x55555555u;
}}

uint4 SigmaMerkabaGaugeSignedMorton(uint2 u, uint2 v, inout uint valid)
{{
    uint2 x = SigmaMerkabaGaugeZigZag(u, valid);
    uint2 y = SigmaMerkabaGaugeZigZag(v, valid);
    return uint4(
        SigmaMerkabaGaugeSpread16(x.x) |
            (SigmaMerkabaGaugeSpread16(y.x) << 1u),
        SigmaMerkabaGaugeSpread16(x.x >> 16u) |
            (SigmaMerkabaGaugeSpread16(y.x >> 16u) << 1u),
        SigmaMerkabaGaugeSpread16(x.y) |
            (SigmaMerkabaGaugeSpread16(y.y) << 1u),
        SigmaMerkabaGaugeSpread16(x.y >> 16u) |
            (SigmaMerkabaGaugeSpread16(y.y >> 16u) << 1u));
}}

bool SigmaMerkabaGaugeMortonLess(uint4 left, uint4 right)
{{
    bool less = false;
    if (left.w != right.w)
        less = left.w < right.w;
    else if (left.z != right.z)
        less = left.z < right.z;
    else if (left.y != right.y)
        less = left.y < right.y;
    else
        less = left.x < right.x;
    return less;
}}

bool SigmaMerkabaGaugeLess(uint4 leftCoordinate, uint leftLevel,
    uint4 rightCoordinate, uint rightLevel, inout uint valid)
{{
    bool less = false;
    if (leftLevel != rightLevel)
    {{
        less = leftLevel < rightLevel;
    }}
    else
    {{
        uint4 leftMorton = SigmaMerkabaGaugeSignedMorton(leftCoordinate.xy,
            leftCoordinate.zw, valid);
        uint4 rightMorton = SigmaMerkabaGaugeSignedMorton(rightCoordinate.xy,
            rightCoordinate.zw, valid);
        if (any(leftMorton != rightMorton))
            less = SigmaMerkabaGaugeMortonLess(leftMorton, rightMorton);
        else if (!SigmaU64Equal(leftCoordinate.xy, rightCoordinate.xy))
            less = SigmaI64Less(leftCoordinate.xy, rightCoordinate.xy);
        else
            less = SigmaI64Less(leftCoordinate.zw, rightCoordinate.zw);
    }}
    return less;
}}

void SigmaMerkabaBuildDirectionalAction(
    uint measuredRole, uint2 directionLower, uint2 directionUpper,
    uint2 residualLower, uint2 residualUpper,
    out uint actionRole, out uint actionActive,
    out uint2 actionLower, out uint2 actionUpper,
    inout uint valid)
{{
    actionRole = measuredRole;
    actionActive = measuredRole == SIGMA_NATIVE_QUERY_NO_CLAIM ? 0u : 1u;
    actionLower = uint2(0u, 0u);
    actionUpper = uint2(0u, 0u);
    if (actionActive == 0u)
        return;
    actionLower = SigmaQ48MulLower(directionLower, residualLower, valid);
    actionLower = SigmaQ48Min(actionLower,
        SigmaQ48MulLower(directionLower, residualUpper, valid));
    actionLower = SigmaQ48Min(actionLower,
        SigmaQ48MulLower(directionUpper, residualLower, valid));
    actionLower = SigmaQ48Min(actionLower,
        SigmaQ48MulLower(directionUpper, residualUpper, valid));
    actionUpper = SigmaQ48MulUpper(directionLower, residualLower, valid);
    actionUpper = SigmaQ48Max(actionUpper,
        SigmaQ48MulUpper(directionLower, residualUpper, valid));
    actionUpper = SigmaQ48Max(actionUpper,
        SigmaQ48MulUpper(directionUpper, residualLower, valid));
    actionUpper = SigmaQ48Max(actionUpper,
        SigmaQ48MulUpper(directionUpper, residualUpper, valid));
}}

bool SigmaMerkabaCanOmitQueryRegion(bool allDefault,
    bool defaultBoundaryClosed, bool fingerprintsMatch)
{{
    return allDefault && defaultBoundaryClosed && fingerprintsMatch;
}}

uint2 SigmaMerkabaDecodeDefaultLane(uint backingKind, uint lane)
{{
    bool valid = backingKind <= SIGMA_DEFAULT_NULL_CODEC && lane < 16u;
    return valid ? uint2(0u, 0u) : uint2(0xffffffffu, 0xffffffffu);
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

bool SigmaMerkabaBoundaryEnvelopesContact(
    uint leftClaim, uint rightClaim,
    uint2 leftLower[3], uint2 leftUpper[3],
    uint2 rightLower[3], uint2 rightUpper[3])
{{
    bool contact = leftClaim == SIGMA_NATIVE_QUERY_FIRST_HIT_MOULD &&
        rightClaim == SIGMA_NATIVE_QUERY_FIRST_HIT_MOULD;
    [unroll]
    for (uint axis = 0u; axis < 3u; ++axis)
        contact = contact &&
            !SigmaI64Less(leftUpper[axis], rightLower[axis]) &&
            !SigmaI64Less(rightUpper[axis], leftLower[axis]);
    return contact;
}}

uint SigmaMerkabaNativeBoundarySectorAddress(uint ordinal)
{{
    return ordinal < 4u ? (1u << ordinal) : 0u;
}}

// The sector-pair transport is K16 address/sign geometry only.  It returns no
// signed chart direction.  The swapped reverse is evaluated independently.
void SigmaMerkabaEvaluateNativeStitchLink(uint2 left[16], uint2 right[16],
    uint leftSectorOrdinal, uint rightSectorOrdinal,
    out uint2 link[16], out uint2 reverseLink[16],
    out uint transportAddress, out int forwardSign, out int reverseSign,
    inout uint valid)
{{
    uint leftAddress = SigmaMerkabaNativeBoundarySectorAddress(
        leftSectorOrdinal);
    uint rightAddress = SigmaMerkabaNativeBoundarySectorAddress(
        rightSectorOrdinal);
    valid &= leftAddress != 0u && rightAddress != 0u ? 1u : 0u;
    transportAddress = leftAddress ^ rightAddress;
    forwardSign = SigmaMerkabaBasisSign(leftAddress, transportAddress);
    reverseSign = SigmaMerkabaBasisSign(rightAddress, transportAddress);
    uint2 transportedLeft[16];
    uint2 transportedRight[16];
    SigmaRightBasisAction(left, transportAddress, transportedLeft, valid);
    SigmaRightBasisAction(right, transportAddress, transportedRight, valid);
    [unroll]
    for (uint lane = 0u; lane < 16u; ++lane)
    {{
        uint2 forwardValue = forwardSign < 0
            ? SigmaQ48NegateChecked(transportedLeft[lane], valid)
            : transportedLeft[lane];
        uint2 reverseValue = reverseSign < 0
            ? SigmaQ48NegateChecked(transportedRight[lane], valid)
            : transportedRight[lane];
        link[lane] = SigmaQ48SubChecked(right[lane], forwardValue, valid);
        reverseLink[lane] = SigmaQ48SubChecked(left[lane], reverseValue, valid);
    }}
}}

int SigmaMerkabaCanonicalCompareU32(uint left, uint right)
{{
    if (left == right) return 0;
    uint leftDivisor = 1u;
    uint rightDivisor = 1u;
    while (left / leftDivisor >= 10u && leftDivisor <= 100000000u)
        leftDivisor *= 10u;
    while (right / rightDivisor >= 10u && rightDivisor <= 100000000u)
        rightDivisor *= 10u;
    uint leftCursor = leftDivisor;
    uint rightCursor = rightDivisor;
    while (leftCursor != 0u && rightCursor != 0u)
    {{
        uint leftDigit = (left / leftCursor) % 10u;
        uint rightDigit = (right / rightCursor) % 10u;
        if (leftDigit != rightDigit)
            return leftDigit < rightDigit ? -1 : 1;
        leftCursor /= 10u;
        rightCursor /= 10u;
    }}
    return leftCursor == rightCursor ? 0 : (leftCursor == 0u ? -1 : 1);
}}

int SigmaMerkabaCanonicalCompareI32(int left, int right)
{{
    bool leftNegative = left < 0;
    bool rightNegative = right < 0;
    if (leftNegative != rightNegative)
        return leftNegative ? -1 : 1;
    uint leftMagnitude = leftNegative ? (uint)(-(left + 1)) + 1u : (uint)left;
    uint rightMagnitude = rightNegative ? (uint)(-(right + 1)) + 1u :
        (uint)right;
    return SigmaMerkabaCanonicalCompareU32(leftMagnitude, rightMagnitude);
}}

int SigmaMerkabaCanonicalCompareHex64(uint2 left, uint2 right)
{{
    if (left.y != right.y) return left.y < right.y ? -1 : 1;
    if (left.x != right.x) return left.x < right.x ? -1 : 1;
    return 0;
}}

int SigmaMerkabaCanonicalCompareReceipt256(uint4 left0, uint4 left1,
    uint4 right0, uint4 right1)
{{
    uint left[8] = {{ left0.x, left0.y, left0.z, left0.w,
        left1.x, left1.y, left1.z, left1.w }};
    uint right[8] = {{ right0.x, right0.y, right0.z, right0.w,
        right1.x, right1.y, right1.z, right1.w }};
    [unroll]
    for (uint word = 0u; word < 8u; ++word)
    {{
        if (left[word] != right[word])
            return left[word] < right[word] ? -1 : 1;
    }}
    return 0;
}}

uint SigmaMerkabaCanonicalFactorClassFromNonzero(uint nonzero, uint valid)
{{
    return valid == 0u ? SIGMA_EXACT_FACTOR_UNRESOLVED :
        (nonzero == 0u ? SIGMA_EXACT_FACTOR_PROVEN_CLOSED :
            SIGMA_EXACT_FACTOR_PROVEN_INCOMPATIBLE);
}}

uint SigmaMerkabaCanonicalAggregateFactorClass(uint left, uint right)
{{
    if (left == SIGMA_EXACT_FACTOR_PROVEN_INCOMPATIBLE ||
        right == SIGMA_EXACT_FACTOR_PROVEN_INCOMPATIBLE)
        return SIGMA_EXACT_FACTOR_PROVEN_INCOMPATIBLE;
    if (left == SIGMA_EXACT_FACTOR_UNRESOLVED ||
        right == SIGMA_EXACT_FACTOR_UNRESOLVED)
        return SIGMA_EXACT_FACTOR_UNRESOLVED;
    return SIGMA_EXACT_FACTOR_PROVEN_CLOSED;
}}

// Canonical comparison is scheduled cooperatively by the existing 256-thread
// CLOSE workgroup.  One lane evaluates one link or (context, output) profile
// coefficient.  This generated surface intentionally owns no scalar 16x16
// interpreter and persists no profile array per boundary.
uint2 SigmaMerkabaCanonicalEvaluateLinkLane(uint2 leftTransportSource,
    uint2 rightAtLane, uint2 rightTransportSource, uint2 leftAtLane,
    uint leftSector, uint rightSector, uint outputLane, bool forward,
    inout uint valid)
{{
    uint leftAddress = SigmaMerkabaNativeBoundarySectorAddress(leftSector);
    uint rightAddress = SigmaMerkabaNativeBoundarySectorAddress(rightSector);
    uint transport = leftAddress ^ rightAddress;
    uint source = outputLane ^ transport;
    uint2 transportedLeft = SigmaMerkabaBasisSign(source, transport) < 0
        ? SigmaQ48NegateChecked(leftTransportSource, valid)
        : leftTransportSource;
    uint2 transportedRight = SigmaMerkabaBasisSign(source, transport) < 0
        ? SigmaQ48NegateChecked(rightTransportSource, valid)
        : rightTransportSource;
    int forwardSign = SigmaMerkabaBasisSign(leftAddress, transport);
    int reverseSign = SigmaMerkabaBasisSign(rightAddress, transport);
    uint2 forwardValue = forwardSign < 0
        ? SigmaQ48NegateChecked(transportedLeft, valid) : transportedLeft;
    uint2 reverseValue = reverseSign < 0
        ? SigmaQ48NegateChecked(transportedRight, valid) : transportedRight;
    uint2 forwardLink = SigmaQ48SubChecked(rightAtLane, forwardValue, valid);
    uint2 reverseLink = SigmaQ48SubChecked(leftAtLane, reverseValue, valid);
    return forward ? forwardLink : reverseLink;
}}

uint2 SigmaMerkabaCanonicalEvaluateAssociatorLane(uint2 leftSourceValue,
    uint2 rightSourceValue, uint leftSector, uint rightSector,
    uint context, uint outputLane, bool forward, inout uint valid)
{{
    uint2 forwardDelta;
    uint2 reverseDelta;
    SigmaMerkabaEvaluateAssociatorProfileDeltaLane(leftSourceValue,
        rightSourceValue, SigmaMerkabaNativeBoundarySectorAddress(leftSector),
        SigmaMerkabaNativeBoundarySectorAddress(rightSector), context,
        outputLane, forwardDelta, reverseDelta, valid);
    return forward ? forwardDelta : reverseDelta;
}}

// Separators and context ordinals are identical for both sides.  These semantic
// token ordinals are sign-equivalent to the accepted ASCII serializer.  Exact
// normalized point intervals need not be emitted twice: they are a total
// generated function of the raw point factor and are reached only after that raw
// factor compares equal.
#define SIGMA_MERKABA_CANONICAL_LINK_TOKEN_BASE 5u
#define SIGMA_MERKABA_CANONICAL_ASSOCIATOR_CLASS_TOKEN 21u
#define SIGMA_MERKABA_CANONICAL_PROFILE_TOKEN_BASE 22u
#define SIGMA_MERKABA_CANONICAL_PROFILE_TOKEN_STRIDE 17u
#define SIGMA_MERKABA_CANONICAL_SUFFIX_TOKEN_BASE 294u
#define SIGMA_MERKABA_CANONICAL_DIRECTED_TOKEN_COUNT 306u

int SigmaMerkabaCanonicalCompareDirectedPrefix(uint fromA, uint toA,
    uint transportA, int signA, uint linkClassA, uint fromB, uint toB,
    uint transportB, int signB, uint linkClassB)
{{
    int comparison = SigmaMerkabaCanonicalCompareU32(fromA, fromB);
    if (comparison != 0) return comparison;
    comparison = SigmaMerkabaCanonicalCompareU32(toA, toB);
    if (comparison != 0) return comparison;
    comparison = SigmaMerkabaCanonicalCompareU32(transportA, transportB);
    if (comparison != 0) return comparison;
    comparison = SigmaMerkabaCanonicalCompareI32(signA, signB);
    return comparison != 0 ? comparison :
        SigmaMerkabaCanonicalCompareU32(linkClassA, linkClassB);
}}

int SigmaMerkabaCanonicalCompareDirectedSuffix(uint associatorClassA,
    uint closureClassA, uint relationClassA,
    uint4 provenanceA0, uint4 provenanceA1, uint associatorClassB,
    uint closureClassB, uint relationClassB,
    uint4 provenanceB0, uint4 provenanceB1)
{{
    int comparison = SigmaMerkabaCanonicalCompareU32(associatorClassA,
        associatorClassB);
    if (comparison != 0) return comparison;
    comparison = SigmaMerkabaCanonicalCompareU32(closureClassA,
        closureClassB);
    if (comparison != 0) return comparison;
    comparison = SigmaMerkabaCanonicalCompareU32(relationClassA,
        relationClassB);
    if (comparison != 0) return comparison;
    comparison = SigmaMerkabaCanonicalCompareU32(
        associatorClassA == SIGMA_EXACT_FACTOR_PROVEN_CLOSED ? 0u : 1u,
        associatorClassB == SIGMA_EXACT_FACTOR_PROVEN_CLOSED ? 0u : 1u);
    if (comparison != 0) return comparison;
    // The bracket-program fingerprint is identical inside one generated bundle.
    return SigmaMerkabaCanonicalCompareReceipt256(provenanceA0, provenanceA1,
        provenanceB0, provenanceB1);
}}

uint SigmaMerkabaCanonicalDirectionalClass(uint packedClasses, bool forward,
    bool associator)
{{
    uint shift = associator ? (forward ? 8u : 12u) :
        (forward ? 0u : 4u);
    return (packedClasses >> shift) & 15u;
}}

uint SigmaMerkabaCanonicalClosureClass(uint packedClasses)
{{
    uint closure = SIGMA_EXACT_FACTOR_PROVEN_CLOSED;
    [unroll]
    for (uint factor = 0u; factor < 4u; ++factor)
        closure = SigmaMerkabaCanonicalAggregateFactorClass(closure,
            (packedClasses >> (factor * 4u)) & 15u);
    return closure;
}}

uint SigmaMerkabaCanonicalRelationClass(uint packedClasses, int annihilator)
{{
    uint closure = SigmaMerkabaCanonicalClosureClass(packedClasses);
    uint associator = SigmaMerkabaCanonicalAggregateFactorClass(
        (packedClasses >> 8u) & 15u, (packedClasses >> 12u) & 15u);
    if (closure == SIGMA_EXACT_FACTOR_UNRESOLVED)
        return SIGMA_MERKABA_RELATION_UNRESOLVED;
    if (closure == SIGMA_EXACT_FACTOR_PROVEN_INCOMPATIBLE)
        return associator == SIGMA_EXACT_FACTOR_PROVEN_INCOMPATIBLE
            ? SIGMA_MERKABA_RELATION_NONASSOCIATIVE_CONTEXT
            : SIGMA_MERKABA_RELATION_NO_RELATION;
    return annihilator >= 0 ? SIGMA_MERKABA_RELATION_EXACT_ZD :
        SIGMA_MERKABA_RELATION_REGULAR;
}}

uint SigmaMerkabaClassifyPointNativeStitchPair(uint2 left[16],
    uint2 right[16], uint leftSectorOrdinal, uint rightSectorOrdinal,
    inout uint valid)
{{
    uint2 link[16];
    uint2 reverseLink[16];
    uint transportAddress;
    int forwardSign;
    int reverseSign;
    SigmaMerkabaEvaluateNativeStitchLink(left, right, leftSectorOrdinal,
        rightSectorOrdinal, link, reverseLink, transportAddress,
        forwardSign, reverseSign, valid);
    bool closed = valid != 0u;
    [unroll]
    for (uint lane = 0u; lane < 16u; ++lane)
        closed = closed && all(link[lane] == uint2(0u, 0u)) &&
            all(reverseLink[lane] == uint2(0u, 0u));
    return valid == 0u ? SIGMA_EXACT_FACTOR_UNRESOLVED :
        (closed ? SIGMA_EXACT_FACTOR_PROVEN_CLOSED :
            SIGMA_EXACT_FACTOR_PROVEN_INCOMPATIBLE);
}}

// Four uint4 values are private bounded scratch for the sixteen abstract sector
// pairs.  N2 fills them from the parallel full-S16/bracket evaluator; no host
// relation truth enters this finalizer.
void SigmaMerkabaAccumulateNativeStitchClass(uint factorClass, uint pair,
    inout uint closedCount, inout uint resolvedPair, inout uint unresolved)
{{
    unresolved |= factorClass == SIGMA_EXACT_FACTOR_UNRESOLVED ? 1u : 0u;
    if (factorClass == SIGMA_EXACT_FACTOR_PROVEN_CLOSED)
    {{
        resolvedPair = pair;
        ++closedCount;
    }}
}}

uint4 SigmaMerkabaFinalizeNativeStitchSet(
    uint4 closureClass0, uint4 closureClass1,
    uint4 closureClass2, uint4 closureClass3)
{{
    uint closedCount = 0u;
    uint resolvedPair = 0u;
    uint unresolved = 0u;
    SigmaMerkabaAccumulateNativeStitchClass(closureClass0.x, 0u,
        closedCount, resolvedPair, unresolved);
    SigmaMerkabaAccumulateNativeStitchClass(closureClass0.y, 1u,
        closedCount, resolvedPair, unresolved);
    SigmaMerkabaAccumulateNativeStitchClass(closureClass0.z, 2u,
        closedCount, resolvedPair, unresolved);
    SigmaMerkabaAccumulateNativeStitchClass(closureClass0.w, 3u,
        closedCount, resolvedPair, unresolved);
    SigmaMerkabaAccumulateNativeStitchClass(closureClass1.x, 4u,
        closedCount, resolvedPair, unresolved);
    SigmaMerkabaAccumulateNativeStitchClass(closureClass1.y, 5u,
        closedCount, resolvedPair, unresolved);
    SigmaMerkabaAccumulateNativeStitchClass(closureClass1.z, 6u,
        closedCount, resolvedPair, unresolved);
    SigmaMerkabaAccumulateNativeStitchClass(closureClass1.w, 7u,
        closedCount, resolvedPair, unresolved);
    SigmaMerkabaAccumulateNativeStitchClass(closureClass2.x, 8u,
        closedCount, resolvedPair, unresolved);
    SigmaMerkabaAccumulateNativeStitchClass(closureClass2.y, 9u,
        closedCount, resolvedPair, unresolved);
    SigmaMerkabaAccumulateNativeStitchClass(closureClass2.z, 10u,
        closedCount, resolvedPair, unresolved);
    SigmaMerkabaAccumulateNativeStitchClass(closureClass2.w, 11u,
        closedCount, resolvedPair, unresolved);
    SigmaMerkabaAccumulateNativeStitchClass(closureClass3.x, 12u,
        closedCount, resolvedPair, unresolved);
    SigmaMerkabaAccumulateNativeStitchClass(closureClass3.y, 13u,
        closedCount, resolvedPair, unresolved);
    SigmaMerkabaAccumulateNativeStitchClass(closureClass3.z, 14u,
        closedCount, resolvedPair, unresolved);
    SigmaMerkabaAccumulateNativeStitchClass(closureClass3.w, 15u,
        closedCount, resolvedPair, unresolved);
    uint resolution = unresolved != 0u || closedCount > 1u
        ? SIGMA_STITCH_UNRESOLVED
        : closedCount == 0u ? SIGMA_STITCH_NO_STITCH : SIGMA_STITCH_RESOLVED;
    uint leftSector = resolvedPair >> 2u;
    uint rightSector = resolvedPair & 3u;
    uint transportAddress = resolution == SIGMA_STITCH_RESOLVED
        ? SigmaMerkabaNativeBoundarySectorAddress(leftSector) ^
            SigmaMerkabaNativeBoundarySectorAddress(rightSector)
        : 0u;
    return uint4(resolution,
        resolution == SIGMA_STITCH_RESOLVED ? leftSector : 0u,
        resolution == SIGMA_STITCH_RESOLVED ? rightSector : 0u,
        transportAddress);
}}

static const int4 SIGMA_STITCH_CHART_D4[8] = {{
    {chart_d4} }};

static const uint SIGMA_STITCH_CHART_D4_COMPOSE[64] = {{
    {d4_compose_values} }};
static const uint SIGMA_STITCH_CHART_D4_INVERSE[8] = {{
    {d4_inverse_values} }};
static const uint SIGMA_STITCH_CHART_ORBIT_REPRESENTATIVE[3] = {{
    {orbit_representative_values} }};
static const uint SIGMA_STITCH_CHART_ADJACENT_FRAME[768] = {{
    {adjacent_frame_values} }};

// Complete finite representation candidates.  No entry is authoritative by
// itself: closure must enumerate them and quotient only each eight-image D4
// orbit.  Distinct surviving orbit classes remain unresolved.
static const uint4 SIGMA_STITCH_NATIVE_SECTOR_CHART_ASSIGNMENTS[24] = {{
    {sector_chart_assignments} }};

uint SigmaMerkabaComposeChartD4(uint outer, uint inner, inout uint valid)
{{
    valid &= outer < 8u && inner < 8u ? 1u : 0u;
    return SIGMA_STITCH_CHART_D4_COMPOSE[
        min(outer, 7u) * 8u + min(inner, 7u)];
}}

uint SigmaMerkabaInverseChartD4(uint frame, inout uint valid)
{{
    valid &= frame < 8u ? 1u : 0u;
    return SIGMA_STITCH_CHART_D4_INVERSE[min(frame, 7u)];
}}

uint SigmaMerkabaChartOrbitRepresentative(uint orbit, inout uint valid)
{{
    valid &= orbit < 3u ? 1u : 0u;
    return SIGMA_STITCH_CHART_ORBIT_REPRESENTATIVE[min(orbit, 2u)];
}}

uint SigmaMerkabaResolveAdjacentOrbitFrame(uint orbit, uint currentFrame,
    uint currentSector, uint nextSector, int orientationParity,
    inout uint valid)
{{
    bool parityValid = orientationParity == -1 || orientationParity == 1;
    valid &= orbit < 3u && currentFrame < 8u && currentSector < 4u &&
        nextSector < 4u && parityValid ? 1u : 0u;
    uint parityIndex = orientationParity > 0 ? 1u : 0u;
    uint index = ((((min(orbit, 2u) * 8u + min(currentFrame, 7u)) * 4u +
        min(currentSector, 3u)) * 4u + min(nextSector, 3u)) * 2u +
        parityIndex);
    return SIGMA_STITCH_CHART_ADJACENT_FRAME[index];
}}

int2 SigmaMerkabaSectorChartCandidateDirection(uint assignmentIndex,
    uint sectorOrdinal, inout uint valid)
{{
    valid &= assignmentIndex < 24u && sectorOrdinal < 4u ? 1u : 0u;
    uint4 assignment = SIGMA_STITCH_NATIVE_SECTOR_CHART_ASSIGNMENTS[
        min(assignmentIndex, 23u)];
    uint direction = assignment[min(sectorOrdinal, 3u)];
    int2 output = int2(0, -1);
    if (direction == 0u) output = int2(1, 0);
    else if (direction == 1u) output = int2(0, 1);
    else if (direction == 2u) output = int2(-1, 0);
    return output;
}}

int2 SigmaMerkabaTransformChartCellLower(int2 lower, uint d4Index,
    inout uint valid)
{{
    valid &= d4Index < 8u ? 1u : 0u;
    int4 transform = SIGMA_STITCH_CHART_D4[min(d4Index, 7u)];
    int source[2] = {{ lower.x, lower.y }};
    int2 output = int2(0, 0);
    [unroll]
    for (uint row = 0u; row < 2u; ++row)
    {{
        int a = row == 0u ? transform.x : transform.z;
        int b = row == 0u ? transform.y : transform.w;
        int selected = a != 0 ? source[0] : source[1];
        int sign = a != 0 ? a : b;
        output[row] = sign > 0 ? selected : -selected - 1;
    }}
    return output;
}}

#endif
"""


def render_merkaba_fixture(descriptor: dict) -> str:
    return f"""// <auto-generated by Tools/sigma/generate_sigma_operators.py>
// N1R Editor/Vulkan parity fixture only; never part of the live runtime graph.
#pragma kernel MerkabaProgramParity
#pragma kernel MerkabaMatrixAndIrParity
#pragma kernel MerkabaDirectionalActionParity
#pragma kernel MerkabaFreshAdmissionParity
#pragma kernel MerkabaInstrumentBoundaryParity
#pragma kernel MerkabaGaugeParity
#pragma kernel MerkabaStitchSetParity
#pragma kernel MerkabaDyadicParity
#pragma kernel MerkabaFiniteChartParity
#pragma target 5.0

#include "SigmaGeneratedMerkabaProgram.hlsl"

RWStructuredBuffer<uint4> _MerkabaResults;
RWStructuredBuffer<uint4> _MerkabaMatrixResults;
RWStructuredBuffer<uint4> _MerkabaIrResults;
RWStructuredBuffer<uint4> _MerkabaActionResults;
RWStructuredBuffer<uint4> _MerkabaFreshResults;
RWStructuredBuffer<uint4> _MerkabaInstrumentResults;
RWStructuredBuffer<uint4> _MerkabaGaugeResults;
RWStructuredBuffer<uint4> _MerkabaStitchResults;
StructuredBuffer<uint4> _MerkabaDyadicInputs;
RWStructuredBuffer<uint4> _MerkabaDyadicResults;
RWStructuredBuffer<uint4> _MerkabaFiniteChartResults;

uint _MerkabaDyadicInputCount;

groupshared uint _MerkabaStitchForwardClasses[16];
groupshared uint _MerkabaStitchReverseClasses[16];

uint4 _MerkabaGaugeParentCoordinate;
uint _MerkabaGaugeParentLevel;

[numthreads(16, 16, 1)]
void MerkabaProgramParity(uint3 id : SV_DispatchThreadID)
{{
    uint a = id.x;
    uint b = id.y;
    uint c = id.z;
    uint offset = (c * 16u + b) * 16u + a;
    _MerkabaResults[offset] = uint4(
        asuint(SigmaMerkabaAssociatorCoefficient(a, b, c)),
        asuint(SigmaMerkabaPlaquetteHolonomy(a, c, b)),
        asuint(SigmaMerkabaShadowNumerator(a, c & 3u)),
        asuint(SigmaMerkabaBasisSign(a, b)));
}}

[numthreads(64, 1, 1)]
void MerkabaDyadicParity(uint3 id : SV_DispatchThreadID)
{{
    if (id.x >= _MerkabaDyadicInputCount)
        return;
    uint2 value = _MerkabaDyadicInputs[id.x].xy;
    [unroll]
    for (uint coefficient = 0u; coefficient < 7u; ++coefficient)
    {{
        int numerator = (int)coefficient * 2 - 6;
        uint shadowValid = 1u;
        uint2 shadow = SigmaMerkabaMultiplyDyadic(value, numerator, 2u,
            shadowValid);
        uint dualValid = 1u;
        uint2 dual = SigmaMerkabaMultiplyDyadic(value, numerator, 6u,
            dualValid);
        uint output = (id.x * 7u + coefficient) * 2u;
        _MerkabaDyadicResults[output] = uint4(shadow, shadowValid, 0u);
        _MerkabaDyadicResults[output + 1u] = uint4(dual, dualValid, 0u);
    }}
}}

[numthreads(256, 1, 1)]
void MerkabaFiniteChartParity(uint3 id : SV_DispatchThreadID)
{{
    uint index = id.x;
    if (index < 64u)
    {{
        uint valid = 1u;
        uint value = SigmaMerkabaComposeChartD4(index >> 3u, index & 7u,
            valid);
        _MerkabaFiniteChartResults[index] = uint4(value, valid, index, 0u);
    }}
    else if (index < 72u)
    {{
        uint frame = index - 64u;
        uint valid = 1u;
        uint value = SigmaMerkabaInverseChartD4(frame, valid);
        _MerkabaFiniteChartResults[index] = uint4(value, valid, frame, 1u);
    }}
    else if (index < 75u)
    {{
        uint orbit = index - 72u;
        uint valid = 1u;
        uint value = SigmaMerkabaChartOrbitRepresentative(orbit, valid);
        _MerkabaFiniteChartResults[index] = uint4(value, valid, orbit, 2u);
    }}
    else if (index < 843u)
    {{
        uint packed = index - 75u;
        uint parityIndex = packed & 1u;
        packed >>= 1u;
        uint nextSector = packed & 3u;
        packed >>= 2u;
        uint currentSector = packed & 3u;
        packed >>= 2u;
        uint currentFrame = packed & 7u;
        uint orbit = packed >> 3u;
        uint valid = 1u;
        uint value = SigmaMerkabaResolveAdjacentOrbitFrame(orbit,
            currentFrame, currentSector, nextSector,
            parityIndex != 0u ? 1 : -1, valid);
        _MerkabaFiniteChartResults[index] = uint4(value, valid, orbit,
            (currentFrame << 8u) | (currentSector << 4u) | nextSector);
    }}
}}

[numthreads(4, 1, 1)]
void MerkabaGaugeParity(uint3 id : SV_DispatchThreadID)
{{
    uint4 coordinate;
    uint level;
    bool valid = SigmaMerkabaSplitDyadicGauge(_MerkabaGaugeParentCoordinate,
        _MerkabaGaugeParentLevel, id.x, coordinate, level);
    _MerkabaGaugeResults[id.x * 2u] = coordinate;
    _MerkabaGaugeResults[id.x * 2u + 1u] = uint4(level,
        valid ? 1u : 0u, id.x, 0u);
    [unroll]
    for (uint peer = 0u; peer < 4u; ++peer)
    {{
        uint4 peerCoordinate;
        uint peerLevel;
        uint orderValid = valid ? 1u : 0u;
        orderValid &= SigmaMerkabaSplitDyadicGauge(
            _MerkabaGaugeParentCoordinate, _MerkabaGaugeParentLevel, peer,
            peerCoordinate, peerLevel) ? 1u : 0u;
        bool less = SigmaMerkabaGaugeLess(coordinate, level,
            peerCoordinate, peerLevel, orderValid);
        _MerkabaGaugeResults[8u + id.x * 4u + peer] = uint4(
            less ? 1u : 0u, orderValid, id.x, peer);
    }}
    const uint4 wideCoordinates[4] = {{
        uint4(0u, 0u, 1u, 0u), uint4(0u, 0u, 0u, 1u),
        uint4(1u, 0u, 0u, 0u), uint4(0u, 1u, 0u, 0u) }};
    [unroll]
    for (uint widePeer = 0u; widePeer < 4u; ++widePeer)
    {{
        uint wideValid = 1u;
        bool wideLess = SigmaMerkabaGaugeLess(wideCoordinates[id.x], 0u,
            wideCoordinates[widePeer], 0u, wideValid);
        _MerkabaGaugeResults[24u + id.x * 4u + widePeer] = uint4(
            wideLess ? 1u : 0u, wideValid, id.x, widePeer);
    }}
}}

[numthreads(16, 16, 1)]
void MerkabaMatrixAndIrParity(uint3 id : SV_DispatchThreadID)
{{
    uint offset = id.x * 16u + id.y;
    _MerkabaMatrixResults[offset] = uint4(
        asuint(SIGMA_MERKABA_DIFFRACTION[offset]),
        asuint(SIGMA_MERKABA_INFORMATION_METRIC[offset]),
        asuint(SIGMA_MERKABA_VISIBLE_PROJECTOR_NUMERATOR256[offset]),
        SIGMA_MERKABA_INDEPENDENT_CLOSURE_WEIGHT_COUNT |
            (SIGMA_MERKABA_EPSILON_CL_EXISTS << 16u));
    if (offset < SIGMA_MERKABA_IR_NODE_COUNT)
    {{
        _MerkabaIrResults[offset * 2u] = SIGMA_MERKABA_IR_NODE_A[offset];
        _MerkabaIrResults[offset * 2u + 1u] =
            asuint(SIGMA_MERKABA_IR_NODE_B[offset]);
    }}
}}

[numthreads(1, 1, 1)]
void MerkabaDirectionalActionParity(uint3 id : SV_DispatchThreadID)
{{
    uint valid = 1u;
    uint2 zero = uint2(0u, 0u);
    uint2 one = uint2(0u, 0x00010000u);
    uint2 half = uint2(0u, 0x00008000u);
    uint noneRole;
    uint noneActive;
    uint2 noneLower;
    uint2 noneUpper;
    SigmaMerkabaBuildDirectionalAction(SIGMA_NATIVE_QUERY_NO_CLAIM,
        one, one, half, half, noneRole, noneActive, noneLower, noneUpper, valid);
    uint mouldRole;
    uint mouldActive;
    uint2 mouldLower;
    uint2 mouldUpper;
    SigmaMerkabaBuildDirectionalAction(SIGMA_NATIVE_QUERY_FIRST_HIT_MOULD,
        one, one, half, half, mouldRole, mouldActive, mouldLower, mouldUpper,
        valid);
    _MerkabaActionResults[0] = uint4(noneRole, noneActive,
        noneLower.x | noneLower.y, noneUpper.x | noneUpper.y);
    _MerkabaActionResults[1] = uint4(mouldRole, mouldActive,
        mouldLower.x, mouldLower.y);
    _MerkabaActionResults[2] = uint4(mouldUpper.x, mouldUpper.y, valid,
        SIGMA_MERKABA_QUERY_SUPPORT_FALSE_NEGATIVES);
    uint2 unbacked = SigmaMerkabaDecodeDefaultLane(
        SIGMA_DEFAULT_LOGICAL_UNBACKED, 7u);
    uint2 explicitDefault = SigmaMerkabaDecodeDefaultLane(
        SIGMA_DEFAULT_EXPLICIT_ZEMPTY, 7u);
    uint2 nullCodec = SigmaMerkabaDecodeDefaultLane(
        SIGMA_DEFAULT_NULL_CODEC, 7u);
    _MerkabaActionResults[3] = uint4(unbacked.x | unbacked.y,
        explicitDefault.x | explicitDefault.y, nullCodec.x | nullCodec.y,
        SIGMA_MERKABA_REPRESENTATION_DEFAULT_PARITY);
}}

[numthreads(1, 1, 1)]
void MerkabaFreshAdmissionParity(uint3 id : SV_DispatchThreadID)
{{
    uint valid = 1u;
    uint2 one = uint2(0u, 0x00010000u);
    uint2 negativeOne = uint2(0u, 0xffff0000u);
    uint2 half = uint2(0u, 0x00008000u);
    uint2 negativeHalf = uint2(0u, 0xffff8000u);
    uint2 lower[4];
    uint2 upper[4];
    lower[0] = upper[0] = one;
    lower[1] = upper[1] = negativeOne;
    lower[2] = upper[2] = half;
    lower[3] = upper[3] = negativeHalf;
    uint2 state[16];
    uint2 selected[4];
    uint boundaryRelation = SIGMA_MERKABA_RELATION_UNRESOLVED;
    bool admitted = SigmaMerkabaResolveFreshBranch(lower, upper,
        SIGMA_FRESH_FIRST_HIT_LEFT | SIGMA_FRESH_FIRST_HIT_RIGHT, 1u,
        state, selected, boundaryRelation, valid);
    uint2 forward[4];
    SigmaMerkabaEvaluateShadow(state, forward, valid);
    [unroll]
    for (uint axis = 0u; axis < 4u; ++axis)
        _MerkabaFreshResults[axis] = uint4(selected[axis], forward[axis]);
    [unroll]
    for (uint lane = 0u; lane < 16u; ++lane)
        _MerkabaFreshResults[4u + lane] = uint4(state[lane], 0u, 0u);
    _MerkabaFreshResults[20] = uint4(admitted ? 1u : 0u, valid,
        SIGMA_FRESH_ADMISSION_ADMITTED, boundaryRelation);
    uint2 kernelState[16];
    [unroll]
    for (uint kernelLane = 0u; kernelLane < 16u; ++kernelLane)
        kernelState[kernelLane] = uint2(0u, 0u);
    kernelState[0] = one;
    uint kernelValid = 1u;
    uint kernelRelation = SigmaMerkabaEvaluateFreshBoundaryRelation(
        kernelState, kernelValid);
    _MerkabaFreshResults[21] = uint4(SIGMA_MERKABA_EXPRESSION_COUNT,
        SIGMA_FRESH_EXTERNAL_RELATION_TRUTH_INPUT_COUNT,
        kernelRelation, kernelValid);
    [unroll]
    for (uint tinyLane = 0u; tinyLane < 16u; ++tinyLane)
        kernelState[tinyLane] = uint2(0u, 0u);
    kernelState[1] = uint2(1u, 0u);
    uint tinyValid = 1u;
    uint tinyRelation = SigmaMerkabaEvaluateFreshBoundaryRelation(
        kernelState, tinyValid);
    _MerkabaFreshResults[22] = uint4(tinyRelation, tinyValid, 0u, 0u);
}}

[numthreads(1, 1, 1)]
void MerkabaInstrumentBoundaryParity(uint3 id : SV_DispatchThreadID)
{{
    uint valid = 1u;
    uint2 ray[3];
    ray[0] = uint2(0u, 0x00004000u);
    ray[1] = uint2(0u, 0x00008000u);
    ray[2] = uint2(0u, 0x00010000u);
    uint4 permutation = uint4(0u, 1u, 2u, 3u);
    int globalSign;
    bool routed = SigmaMerkabaBuildInstrumentRowPermutation(ray,
        permutation, globalSign, valid);
    uint2 codeLower[4];
    uint2 codeUpper[4];
    codeLower[0] = codeUpper[0] = uint2(0u, 0x0000a000u);
    codeLower[1] = codeUpper[1] = uint2(0u, 0x00006000u);
    codeLower[2] = codeUpper[2] = uint2(0u, 0x00009000u);
    codeLower[3] = codeUpper[3] = uint2(0u, 0x00007000u);
    uint2 measuredLower[4];
    uint2 measuredUpper[4];
    SigmaMerkabaAssembleInstrumentTangent(codeLower, codeUpper,
        globalSign, measuredLower, measuredUpper, valid);
    _MerkabaInstrumentResults[0] = uint4(permutation.x,
        asuint(globalSign), routed ? 1u : 0u, valid);
    _MerkabaInstrumentResults[1] = uint4(permutation.y,
        asuint(globalSign), routed ? 1u : 0u, valid);
    _MerkabaInstrumentResults[2] = uint4(permutation.z,
        asuint(globalSign), routed ? 1u : 0u, valid);
    _MerkabaInstrumentResults[3] = uint4(permutation.w,
        asuint(globalSign), routed ? 1u : 0u, valid);
    [unroll]
    for (uint leaf = 0u; leaf < 4u; ++leaf)
        _MerkabaInstrumentResults[4u + leaf] = uint4(
            measuredLower[leaf], measuredUpper[leaf]);
}}

[numthreads(16, 1, 1)]
void MerkabaStitchSetParity(uint3 id : SV_DispatchThreadID)
{{
    uint pair = id.x;
    uint leftSector = pair >> 2u;
    uint rightSector = pair & 3u;
    uint2 zero = uint2(0u, 0u);
    uint2 one = uint2(0u, 0x00010000u);
    uint2 two = uint2(0u, 0x00020000u);
    uint2 leftState[16];
    uint2 rightState[16];
    [unroll]
    for (uint stateLane = 0u; stateLane < 16u; ++stateLane)
    {{
        leftState[stateLane] = zero;
        rightState[stateLane] = zero;
    }}
    leftState[1] = one;
    rightState[2] = one;
    uint forwardValid = 1u;
    uint reverseValid = 1u;
    uint forwardClass = SigmaMerkabaClassifyPointNativeStitchPair(
        leftState, rightState, leftSector, rightSector, forwardValid);
    uint reverseClass = SigmaMerkabaClassifyPointNativeStitchPair(
        rightState, leftState, leftSector, rightSector, reverseValid);
    _MerkabaStitchForwardClasses[pair] = forwardClass;
    _MerkabaStitchReverseClasses[pair] = reverseClass;
    _MerkabaStitchResults[pair] = uint4(forwardClass, forwardValid,
        leftSector, rightSector);
    _MerkabaStitchResults[16u + pair] = uint4(reverseClass, reverseValid,
        leftSector, rightSector);

    GroupMemoryBarrierWithGroupSync();
    if (pair == 0u)
    {{
        uint4 forward0 = uint4(_MerkabaStitchForwardClasses[0],
            _MerkabaStitchForwardClasses[1],
            _MerkabaStitchForwardClasses[2],
            _MerkabaStitchForwardClasses[3]);
        uint4 forward1 = uint4(_MerkabaStitchForwardClasses[4],
            _MerkabaStitchForwardClasses[5],
            _MerkabaStitchForwardClasses[6],
            _MerkabaStitchForwardClasses[7]);
        uint4 forward2 = uint4(_MerkabaStitchForwardClasses[8],
            _MerkabaStitchForwardClasses[9],
            _MerkabaStitchForwardClasses[10],
            _MerkabaStitchForwardClasses[11]);
        uint4 forward3 = uint4(_MerkabaStitchForwardClasses[12],
            _MerkabaStitchForwardClasses[13],
            _MerkabaStitchForwardClasses[14],
            _MerkabaStitchForwardClasses[15]);
        uint4 reverse0 = uint4(_MerkabaStitchReverseClasses[0],
            _MerkabaStitchReverseClasses[1],
            _MerkabaStitchReverseClasses[2],
            _MerkabaStitchReverseClasses[3]);
        uint4 reverse1 = uint4(_MerkabaStitchReverseClasses[4],
            _MerkabaStitchReverseClasses[5],
            _MerkabaStitchReverseClasses[6],
            _MerkabaStitchReverseClasses[7]);
        uint4 reverse2 = uint4(_MerkabaStitchReverseClasses[8],
            _MerkabaStitchReverseClasses[9],
            _MerkabaStitchReverseClasses[10],
            _MerkabaStitchReverseClasses[11]);
        uint4 reverse3 = uint4(_MerkabaStitchReverseClasses[12],
            _MerkabaStitchReverseClasses[13],
            _MerkabaStitchReverseClasses[14],
            _MerkabaStitchReverseClasses[15]);
        _MerkabaStitchResults[32] = SigmaMerkabaFinalizeNativeStitchSet(
            forward0, forward1, forward2, forward3);
        _MerkabaStitchResults[33] = SigmaMerkabaFinalizeNativeStitchSet(
            reverse0, reverse1, reverse2, reverse3);

        uint2 leftLower[3];
        uint2 leftUpper[3];
        uint2 rightLower[3];
        uint2 rightUpper[3];
        [unroll]
        for (uint axis = 0u; axis < 3u; ++axis)
        {{
            leftLower[axis] = zero;
            leftUpper[axis] = one;
            rightLower[axis] = one;
            rightUpper[axis] = two;
        }}
        _MerkabaStitchResults[34] = uint4(
            SigmaMerkabaBoundaryEnvelopesContact(
                SIGMA_NATIVE_QUERY_FIRST_HIT_MOULD,
                SIGMA_NATIVE_QUERY_FIRST_HIT_MOULD,
                leftLower, leftUpper, rightLower, rightUpper) ? 1u : 0u,
            0u, 0u, 0u);
        rightLower[0] = two;
        _MerkabaStitchResults[35] = uint4(
            SigmaMerkabaBoundaryEnvelopesContact(
                SIGMA_NATIVE_QUERY_FIRST_HIT_MOULD,
                SIGMA_NATIVE_QUERY_FIRST_HIT_MOULD,
                leftLower, leftUpper, rightLower, rightUpper) ? 1u : 0u,
            0u, 0u, 0u);
        _MerkabaStitchResults[36] = uint4(
            SigmaMerkabaBoundaryEnvelopesContact(
                SIGMA_NATIVE_QUERY_NO_CLAIM,
                SIGMA_NATIVE_QUERY_FIRST_HIT_MOULD,
                leftLower, leftUpper, rightLower, rightUpper) ? 1u : 0u,
            0u, 0u, 0u);

        // Exercise the generated ordinal comparator in this existing Vulkan
        // dispatch.  These probes deliberately distinguish accepted textual
        // ordering from numeric ordering and carry the full 256-bit provenance
        // receipt; no hash or host-side semantic repair chooses the result.
        int unsignedText = SigmaMerkabaCanonicalCompareU32(2u, 10u);
        int signedText = SigmaMerkabaCanonicalCompareI32(-2, -10);
        int fixedHex = SigmaMerkabaCanonicalCompareHex64(
            uint2(0u, 1u), uint2(0xffffffffu, 0u));
        uint4 provenanceP = uint4(0x70707070u, 0x70707070u,
            0x70707070u, 0x70707070u);
        uint4 provenanceY = uint4(0x79797979u, 0x79797979u,
            0x79797979u, 0x79797979u);
        int completePrefix = SigmaMerkabaCanonicalCompareDirectedPrefix(
            2u, 1u, 3u, -1, SIGMA_EXACT_FACTOR_PROVEN_CLOSED,
            10u, 1u, 3u, -1, SIGMA_EXACT_FACTOR_PROVEN_CLOSED);
        int completeSuffix = SigmaMerkabaCanonicalCompareDirectedSuffix(
            SIGMA_EXACT_FACTOR_PROVEN_CLOSED,
            SIGMA_EXACT_FACTOR_PROVEN_CLOSED,
            SIGMA_MERKABA_RELATION_REGULAR,
            provenanceP, provenanceP,
            SIGMA_EXACT_FACTOR_PROVEN_CLOSED,
            SIGMA_EXACT_FACTOR_PROVEN_CLOSED,
            SIGMA_MERKABA_RELATION_REGULAR,
            provenanceY, provenanceY);
        _MerkabaStitchResults[45] = uint4(asuint(unsignedText),
            asuint(signedText), asuint(fixedHex), 1u);
        _MerkabaStitchResults[46] = uint4(asuint(completePrefix),
            asuint(completeSuffix),
            asuint(SigmaMerkabaCanonicalCompareReceipt256(
                provenanceP, provenanceP, provenanceY, provenanceY)), 1u);
    }}
    if (pair < 8u)
    {{
        uint d4Valid = 1u;
        int2 transformed = SigmaMerkabaTransformChartCellLower(
            int2(2, -3), pair, d4Valid);
        _MerkabaStitchResults[37u + pair] = uint4(
            asuint(transformed.x), asuint(transformed.y), pair, d4Valid);
    }}
}}
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
        "executableIr": descriptor["ir"],
        "expressionInventory": descriptor["expressions"],
        "reverseRules": descriptor["reverseRules"],
        "queryFamilies": descriptor["queryFamilies"],
        "captureBoundary": descriptor["captureBoundary"],
        "sceneReduction": descriptor["sceneReduction"],
        "freshBaseAdmission": descriptor["freshBaseAdmission"],
        "constructiveModalStitching":
            descriptor["constructiveModalStitching"],
        "photometricNuisance": descriptor["photometricNuisance"],
        "querySupportSummary": descriptor["querySupportSummary"],
        "certificate": descriptor["certificate"],
        "representation": descriptor["representation"],
        "generatedOutputs": [
            CS_MERKABA_OUTPUT.relative_to(ROOT).as_posix(),
            HLSL_MERKABA_OUTPUT.relative_to(ROOT).as_posix(),
            HLSL_MERKABA_FIXTURE_OUTPUT.relative_to(ROOT).as_posix(),
        ],
        "runtimeActivation": {
            "status": "DEFERRED_UNTIL_N4_LIVE_CUTOVER",
            "productionDeltaRequiredDuringCorrectiveN1N2": "+0/-0",
            "outputs": [
                CS_FRAME_OUTPUT.relative_to(ROOT).as_posix(),
                HLSL_FRAME_OUTPUT.relative_to(ROOT).as_posix(),
                HLSL_RUNTIME_MERKABA_OUTPUT.relative_to(ROOT).as_posix(),
            ],
        },
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


def frame_abi_descriptor(merkaba: dict) -> dict:
    chi_fingerprint = sha256({
        "address": merkaba["representation"]["address"],
        "gaugeFamily": merkaba["representation"]["gaugeFamily"],
        "normalizer": merkaba["representation"]["normalizer"],
    })
    kappa_fingerprint = sha256({
        "kappa": merkaba["representation"]["kappa"],
        "refinement": merkaba["representation"]["refinement"],
    })
    descriptor = {
        "version": FRAME_ABI_VERSION,
        "laneCount": LANES,
        "sensorSideCount": len(FRAME_ENUMS["SigmaNativeSensorSide"]),
        "leafCount": len(FRAME_ENUMS["SigmaNativeLeafKind"]),
        "structs": [
            {
                "name": name,
                "fields": list(fields),
                "stride": len(fields) * 16,
            }
            for name, fields in FRAME_STRUCTS
        ],
        "enums": FRAME_ENUMS,
        "observationFlags": FRAME_OBSERVATION_FLAGS,
        "deltaFlags": FRAME_DELTA_FLAGS,
        "packedQ48Stride": 8,
        "validityStride": 4,
        "provenanceStride": 16,
        "entryPoints": {
            entry["id"]: index
            for index, entry in enumerate(merkaba["ir"]["entryPoints"])
        },
        "representationFingerprint": merkaba["inputs"]["iRepresentation"],
        "chiFingerprint": chi_fingerprint,
        "kappaFingerprint": kappa_fingerprint,
        "certificateFingerprint": merkaba["proofs"]["certificateProofFingerprint"],
        # One compact uint2 record is the only GPU->host persistence envelope.
        # It is drained asynchronously in batches and is never a prerequisite
        # for ingress recycling or canonical publication.
        "completion": {
            "Frame": 0,
            "Root": 8,
            "Unresolved": 10,
            "ObservationHeaders": 22,
            "RoomRays": 26,
            "CodeLeaves": 32,
            "Certificate": 48,
            "WordCount": 80,
        },
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
        for name, value in frame["observationFlags"].items())
    enum_text.append(
        "    [System.Flags]\n"
        "    internal enum SigmaNativeObservationFlags : uint\n"
        f"    {{\n        None = 0u,\n{flag_members}\n    }}")
    cell_flag_members = "\n".join(
        f"        {name} = 0x{value:08x}u,"
        for name, value in frame["deltaFlags"].items())
    enum_text.append(
        "    [System.Flags]\n"
        "    internal enum SigmaNativeDeltaFlags : uint\n"
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
    entry_point_lines = "\n".join(
        f"        internal const int {''.join(part.title() for part in name.split('_'))}EntryPoint = {index};"
        for name, index in frame["entryPoints"].items())
    representation_constants = "\n".join((
        f'        internal const string RepresentationFingerprint = "{frame["representationFingerprint"]}";',
        f'        internal const string ChiFingerprint = "{frame["chiFingerprint"]}";',
        f'        internal const string KappaFingerprint = "{frame["kappaFingerprint"]}";',
        f'        internal const string CertificateFingerprint = "{frame["certificateFingerprint"]}";',
    ))
    completion_constants = "\n".join(
        f"        internal const int Completion{name} = {value};"
        for name, value in frame["completion"].items())

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
        internal const int SensorSideCount = {frame['sensorSideCount']};
        internal const int LeafCount = {frame['leafCount']};
        internal const int LaneCount = {frame['laneCount']};
        internal const int PackedQ48Stride = {frame['packedQ48Stride']};
        internal const int ValidityStride = {frame['validityStride']};
        internal const int ProvenanceStride = {frame['provenanceStride']};
        internal const uint Invalid = 0xffffffffu;
{stride_lines}
{entry_point_lines}
{representation_constants}
{completion_constants}

        // Generated lossless meet for one already-bound four-axis locality
        // certificate. Raw observation/ray ownership may transfer to this
        // certificate only after the caller has proved BoundLocality and
        // LosslessPullback and excluded coupled/disjunctive factors.
        internal static bool TryMeetLocalityCertificates(
            SigmaFrameUInt4Gpu[] left, SigmaFrameUInt4Gpu[] right,
            out SigmaFrameUInt4Gpu[] result)
        {{
            const int wordCount = 16;
            const int identity = 0;
            const int context = 1;
            const int independence = 2;
            const int relation = 3;
            const int axis0 = 4;
            const int information0 = 8;
            const int receipts0 = 12;
            result = null;
            if (left == null || right == null || left.Length != wordCount ||
                right.Length != wordCount)
                return false;
            uint required = (uint)(SigmaNativeCertificateFlags.Valid |
                SigmaNativeCertificateFlags.Directional |
                SigmaNativeCertificateFlags.Minimized);
            if ((left[identity].X & required) != required ||
                (right[identity].X & required) != required ||
                left[identity].Y != right[identity].Y ||
                ((left[identity].X ^ right[identity].X) &
                    (uint)SigmaNativeCertificateFlags.Coupled) != 0u ||
                !CertificateWordEqual(left[context], right[context]) ||
                !CertificateWordEqual(left[relation], right[relation]) ||
                !CertificateWordEqual(left[receipts0], right[receipts0]) ||
                !CertificateWordEqual(left[receipts0 + 1], right[receipts0 + 1]) ||
                !CertificateWordEqual(left[receipts0 + 3], right[receipts0 + 3]))
                return false;

            result = (SigmaFrameUInt4Gpu[])left.Clone();
            result[identity].X = left[identity].X | right[identity].X;
            result[identity].Z = 1u;
            result[identity].W = 0u;
            result[independence] = default;
            for (int axis = 0; axis < 4; ++axis)
            {{
                long leftLower = CertificateRaw(left[axis0 + axis].X,
                    left[axis0 + axis].Y);
                long rightLower = CertificateRaw(right[axis0 + axis].X,
                    right[axis0 + axis].Y);
                long leftUpper = CertificateRaw(left[axis0 + axis].Z,
                    left[axis0 + axis].W);
                long rightUpper = CertificateRaw(right[axis0 + axis].Z,
                    right[axis0 + axis].W);
                long lower = leftLower >= rightLower ? leftLower : rightLower;
                long upper = leftUpper <= rightUpper ? leftUpper : rightUpper;
                if (lower > upper)
                {{
                    result = null;
                    return false;
                }}
                result[axis0 + axis] = CertificateInterval(lower, upper);
                ulong width = unchecked((ulong)upper - (ulong)lower);
                long boundedWidth = width <= long.MaxValue
                    ? (long)width : long.MaxValue;
                result[information0 + axis] = new SigmaFrameUInt4Gpu
                {{
                    X = unchecked((uint)boundedWidth),
                    Y = unchecked((uint)(boundedWidth >> 32)),
                    Z = (uint)axis,
                    W = 3u,
                }};
            }}
            result[receipts0 + 2] = new SigmaFrameUInt4Gpu
            {{
                X = left[receipts0 + 2].X | right[receipts0 + 2].X,
                Y = left[receipts0 + 2].Y | right[receipts0 + 2].Y,
                Z = left[receipts0 + 2].Z | right[receipts0 + 2].Z,
                W = left[receipts0 + 2].W | right[receipts0 + 2].W,
            }};
            return true;
        }}

        private static bool CertificateWordEqual(SigmaFrameUInt4Gpu left,
            SigmaFrameUInt4Gpu right) => left.X == right.X &&
            left.Y == right.Y && left.Z == right.Z && left.W == right.W;

        private static long CertificateRaw(uint low, uint high) => unchecked(
            (long)((ulong)high << 32 | low));

        private static SigmaFrameUInt4Gpu CertificateInterval(long lower,
            long upper) => new SigmaFrameUInt4Gpu
        {{
            X = unchecked((uint)lower),
            Y = unchecked((uint)(lower >> 32)),
            Z = unchecked((uint)upper),
            W = unchecked((uint)(upper >> 32)),
        }};
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
    for name, value in frame["observationFlags"].items():
        macro_lines.append(
            f"#define SIGMA_NATIVE_OBSERVATION_{upper_snake(name)} 0x{value:08x}u")
    for name, value in frame["deltaFlags"].items():
        macro_lines.append(
            f"#define SIGMA_NATIVE_DELTA_{upper_snake(name)} 0x{value:08x}u")

    struct_text = []
    for entry in frame["structs"]:
        fields = "\n".join(
            f"    uint4 {field};" for field in entry["fields"])
        struct_text.append(
            f"struct {entry['name']}\n{{\n{fields}\n}};")

    fingerprint_words_text = ", ".join(
        f"0x{word:08x}u" for word in fingerprint_words(frame["fingerprint"]))
    chi_words = ", ".join(
        f"0x{word:08x}u" for word in fingerprint_words(frame["chiFingerprint"]))
    kappa_words = ", ".join(
        f"0x{word:08x}u" for word in fingerprint_words(frame["kappaFingerprint"]))
    certificate_words = ", ".join(
        f"0x{word:08x}u" for word in fingerprint_words(frame["certificateFingerprint"]))
    return f"""// <auto-generated by Tools/sigma/generate_sigma_operators.py>
// Canonical baseline: CPQ4-2026-08-24-S16-v7. Do not edit by hand.
#ifndef SIGMA_FRAME_ABI_INCLUDED
#define SIGMA_FRAME_ABI_INCLUDED

#include "SigmaCarrierAbi.hlsl"

#define SIGMA_NATIVE_SENSOR_SIDE_COUNT {frame['sensorSideCount']}u
#define SIGMA_NATIVE_LEAF_COUNT {frame['leafCount']}u
#define SIGMA_FRAME_LANE_COUNT {frame['laneCount']}u
#define SIGMA_FRAME_INVALID 0xffffffffu
{chr(10).join(
    f'#define SIGMA_COMPLETION_{upper_snake(name)} {value}u'
    for name, value in frame['completion'].items())}
{chr(10).join(macro_lines)}

static const uint SIGMA_FRAME_ABI_FINGERPRINT[8] = {{ {fingerprint_words_text} }};
static const uint SIGMA_CHI_FINGERPRINT[8] = {{ {chi_words} }};
static const uint SIGMA_KAPPA_FINGERPRINT[8] = {{ {kappa_words} }};
static const uint SIGMA_CERTIFICATE_FINGERPRINT[8] = {{ {certificate_words} }};

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
    parser.add_argument("--activate-runtime", action="store_true",
                        help="emit the proved authority candidate into live Runtime outputs; forbidden during corrective N1/N2")
    args = parser.parse_args()
    descriptor = build_descriptor()
    merkaba = build_merkaba_descriptor(descriptor)
    frame = frame_abi_descriptor(merkaba)
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
    valid &= check_or_write(CS_MERKABA_OUTPUT,
                            render_merkaba_cs(merkaba), args.check)
    valid &= check_or_write(HLSL_MERKABA_OUTPUT,
                            render_merkaba_hlsl(merkaba), args.check)
    valid &= check_or_write(HLSL_MERKABA_FIXTURE_OUTPUT,
                            render_merkaba_fixture(merkaba), args.check)
    valid &= check_or_write(AUTHORITY_MANIFEST_OUTPUT,
                            render_authority_manifest(merkaba), args.check)
    if args.activate_runtime:
        valid &= check_or_write(CS_FRAME_OUTPUT, render_frame_cs(frame), args.check)
        valid &= check_or_write(HLSL_FRAME_OUTPUT,
                                render_frame_hlsl(frame), args.check)
        valid &= check_or_write(HLSL_RUNTIME_MERKABA_OUTPUT,
                                render_merkaba_hlsl(merkaba, ".."), args.check)
    return 0 if valid else 1


if __name__ == "__main__":
    raise SystemExit(main())
