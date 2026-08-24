#!/usr/bin/env python3
"""Generate the one canonical Sigma-PRISM-16 algebra/operator descriptor bundle."""

from __future__ import annotations

import argparse
import hashlib
import itertools
import json
import sys
from functools import lru_cache
from pathlib import Path
from typing import Iterable


ROOT = Path(__file__).resolve().parents[2]
CS_OUTPUT = ROOT / "Runtime" / "SigmaPrism" / "Generated" / "SigmaGeneratedAlgebra.cs"
HLSL_OUTPUT = (ROOT / "Runtime" / "Resources" / "SigmaPrism" / "Generated" /
               "SigmaGeneratedTables.hlsl")
HLSL_LAYOUT_OUTPUT = (ROOT / "Runtime" / "Resources" / "SigmaPrism" /
                      "Generated" / "SigmaGeneratedLayout.hlsl")
CS_STREAMING_OUTPUT = (ROOT / "Runtime" / "SigmaPrism" / "Generated" /
                       "SigmaGeneratedStreaming.cs")
HLSL_STREAMING_OUTPUT = (ROOT / "Runtime" / "Resources" / "SigmaPrism" /
                         "SigmaStreamingAbi.hlsl")
CS_FRAME_OUTPUT = (ROOT / "Runtime" / "SigmaPrism" / "Generated" /
                   "SigmaGeneratedFrame.cs")
HLSL_FRAME_OUTPUT = (ROOT / "Runtime" / "Resources" / "SigmaPrism" /
                     "SigmaFrameAbi.hlsl")
KERNEL_MANIFEST = ROOT / "Tools" / "sigma" / "sigma_kernel_execution_manifest.json"
NUMERIC_ID = "num.fixed.q16_48.checked.nearest_even"
GENERATOR_VERSION = "CPQ4-S16-GEN-1"
FRAME_ABI_VERSION = "CPQ4-S16-FRAME-1"
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
    "Fault": 1 << 31,
}

BUDGET_CLASSES = {
    "NONE": 0,
    "INGRESS_ADMISSION": 1,
    "CANONICAL_PROGRESS": 2,
    "PROOF_TRANSITION_CLOSURE": 3,
    "DERIVED_READOUT": 4,
}

COST_FIELDS = (
    "xorPermutation",
    "signedAddSub",
    "maskSelect",
    "q48WideMul",
    "q48Div",
    "intervalMulDiv",
    "genericDenseS16Products",
)


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


def build_kernel_execution_descriptor(operator_costs: dict) -> dict:
    """Expand the checked-in execution manifest into fixed scheduler costs."""
    manifest = json.loads(KERNEL_MANIFEST.read_text(encoding="utf-8"))
    if manifest.get("schema") != "CPQ4-S16-KERNEL-COST-1":
        raise RuntimeError("unexpected Sigma kernel execution manifest schema")
    quantum = manifest.get("token_quantum")
    weights = manifest.get("weights")
    opcodes = manifest.get("opcodes")
    if not isinstance(quantum, int) or quantum <= 0:
        raise RuntimeError("kernel token quantum must be a positive integer")
    if not isinstance(weights, dict) or not isinstance(opcodes, list):
        raise RuntimeError("kernel execution manifest is incomplete")
    expected_weight_names = {
        "fixed_alu", "xor_permutation", "signed_add_sub", "mask_select",
        "q48_wide_mul", "q48_div", "interval_mul_div",
        "generic_dense_s16_product", "byte_transfer_divisor", "barrier",
        "annihilator_witness",
    }
    if set(weights) != expected_weight_names or any(
            not isinstance(value, int) or value <= 0 for value in weights.values()):
        raise RuntimeError("kernel cost weights are incomplete or non-positive")

    expanded = []
    names = set()
    for expected_id, entry in enumerate(opcodes):
        if entry.get("id") != expected_id:
            raise RuntimeError("kernel opcode ids must be contiguous from zero")
        name = entry.get("name")
        budget_name = entry.get("budget_class")
        if not isinstance(name, str) or not name or name in names:
            raise RuntimeError("kernel opcode names must be unique")
        if budget_name not in BUDGET_CLASSES:
            raise RuntimeError(f"unknown kernel budget class: {budget_name}")
        names.add(name)
        stages = entry.get("stages", [])
        if (not isinstance(stages, list) or
                any(not isinstance(stage, str) or not stage
                    for stage in stages) or
                len(stages) != len(set(stages))):
            raise RuntimeError(f"invalid kernel stage list for {name}")
        threads = entry.get("threads")
        if (not isinstance(threads, list) or len(threads) != 3 or
                any(not isinstance(value, int) or value <= 0 for value in threads) or
                threads[0] * threads[1] * threads[2] > 1024):
            raise RuntimeError(f"invalid workgroup for kernel opcode {name}")

        primitive = {field: 0 for field in COST_FIELDS}
        invocations = entry.get("operator_invocations", {})
        if not isinstance(invocations, dict):
            raise RuntimeError(f"invalid operator invocation map for {name}")
        for operator_name, count in invocations.items():
            if operator_name not in operator_costs:
                raise RuntimeError(
                    f"unknown generated operator {operator_name} in {name}")
            if not isinstance(count, int) or count < 0:
                raise RuntimeError(f"invalid operator invocation count in {name}")
            operator = operator_costs[operator_name]
            for field in COST_FIELDS:
                primitive[field] += count * int(operator.get(field, 0))

        scalar_names = (
            "fixed_alu", "bytes_read", "bytes_written", "scratch_bytes",
            "barriers", "annihilator_witnesses", "max_records",
        )
        scalar = {}
        for scalar_name in scalar_names:
            value = entry.get(scalar_name)
            if not isinstance(value, int) or value < 0:
                raise RuntimeError(f"invalid {scalar_name} for opcode {name}")
            scalar[scalar_name] = value

        byte_units = ((scalar["bytes_read"] + scalar["bytes_written"] +
                       scalar["scratch_bytes"] +
                       weights["byte_transfer_divisor"] - 1) //
                      weights["byte_transfer_divisor"])
        score = (
            scalar["fixed_alu"] * weights["fixed_alu"] +
            primitive["xorPermutation"] * weights["xor_permutation"] +
            primitive["signedAddSub"] * weights["signed_add_sub"] +
            primitive["maskSelect"] * weights["mask_select"] +
            primitive["q48WideMul"] * weights["q48_wide_mul"] +
            primitive["q48Div"] * weights["q48_div"] +
            primitive["intervalMulDiv"] * weights["interval_mul_div"] +
            primitive["genericDenseS16Products"] *
            weights["generic_dense_s16_product"] +
            byte_units + scalar["barriers"] * weights["barrier"] +
            scalar["annihilator_witnesses"] *
            weights["annihilator_witness"])
        token_cost = 0 if expected_id == 0 else max(1, (score + quantum - 1) // quantum)
        expanded.append({
            **entry,
            **scalar,
            **primitive,
            "budgetClassId": BUDGET_CLASSES[budget_name],
            "tokenCost": token_cost,
            "score": score,
        })

    return {
        "schema": manifest["schema"],
        "tokenQuantum": quantum,
        "weights": weights,
        "opcodes": expanded,
        "fingerprint": sha256(manifest),
    }


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


def uint_array(values: Iterable[int]) -> str:
    return ", ".join(f"{int(value)}u" for value in values)


def render_streaming_cs(execution: dict) -> str:
    opcodes = execution["opcodes"]
    enum_lines = "\n".join(
        f"        {entry['name']} = {entry['id']}," for entry in opcodes)
    budget_lines = "\n".join(
        f"        {name} = {value}," for name, value in BUDGET_CLASSES.items())

    def cs_values(field: str) -> str:
        return ", ".join(f"{int(entry[field])}u" for entry in opcodes)

    return f"""// <auto-generated by Tools/sigma/generate_sigma_operators.py>
// Canonical baseline: CPQ4-2026-08-22-S16-v6. Do not edit by hand.

using System.Runtime.InteropServices;

namespace Genesis.RoomScan.SigmaPrism
{{
    internal enum SigmaStreamOpcode : uint
    {{
{enum_lines}
    }}

    internal enum SigmaStreamBudgetClass : uint
    {{
{budget_lines}
    }}

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct SigmaStreamUInt4Gpu
    {{
        internal uint X;
        internal uint Y;
        internal uint Z;
        internal uint W;
    }}

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct SigmaStreamPageReferenceGpu
    {{
        internal SigmaStreamUInt4Gpu Coordinate;
        internal SigmaStreamUInt4Gpu State;
    }}

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct SigmaTransactionGpu
    {{
        internal SigmaStreamUInt4Gpu Identity;
        internal SigmaStreamUInt4Gpu Ticket;
        internal SigmaStreamUInt4Gpu DependencyTicket;
        internal SigmaStreamUInt4Gpu Source;
        internal SigmaStreamUInt4Gpu Publication;
        internal SigmaStreamPageReferenceGpu Page0;
        internal SigmaStreamPageReferenceGpu Page1;
        internal SigmaStreamPageReferenceGpu Page2;
        internal SigmaStreamPageReferenceGpu Page3;
        internal SigmaStreamUInt4Gpu AffectedMaskLo;
        internal SigmaStreamUInt4Gpu AffectedMaskHi;
        internal SigmaStreamUInt4Gpu CompletedMaskLo;
        internal SigmaStreamUInt4Gpu CompletedMaskHi;
        internal SigmaStreamUInt4Gpu Progress;
        internal SigmaStreamUInt4Gpu Execution;
        internal SigmaStreamUInt4Gpu Scratch;
        internal SigmaStreamUInt4Gpu Transition;
        internal SigmaStreamUInt4Gpu Dependency0;
        internal SigmaStreamUInt4Gpu Dependency1;
    }}

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct SigmaSealedSourceBundleGpu
    {{
        internal SigmaStreamUInt4Gpu Identity;
        internal SigmaStreamUInt4Gpu Provenance;
        internal SigmaStreamUInt4Gpu Keys;
        internal SigmaStreamUInt4Gpu Raw;
        internal SigmaStreamUInt4Gpu Calibration;
        internal SigmaStreamUInt4Gpu Probation;
        internal SigmaStreamUInt4Gpu Dependency;
    }}

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct SigmaProbationGpu
    {{
        internal SigmaStreamUInt4Gpu Identity;
        internal SigmaStreamUInt4Gpu BundleHandles;
        internal SigmaStreamUInt4Gpu Support;
        internal SigmaStreamUInt4Gpu Dependency;
    }}

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct SigmaSourceHandleSegmentGpu
    {{
        internal SigmaStreamUInt4Gpu Identity;
        internal SigmaStreamUInt4Gpu Link;
        internal SigmaStreamUInt4Gpu Handle01;
        internal SigmaStreamUInt4Gpu Handle23;
        internal SigmaStreamUInt4Gpu Handle45;
        internal SigmaStreamUInt4Gpu Handle67;
    }}

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct SigmaProofClosureGpu
    {{
        internal SigmaStreamUInt4Gpu Identity;
        internal SigmaStreamUInt4Gpu SourceCursor;
        internal SigmaStreamUInt4Gpu Journal;
        internal SigmaStreamUInt4Gpu Ordering;
        internal SigmaStreamUInt4Gpu Coalesce;
        internal SigmaStreamUInt4Gpu Redundancy;
        internal SigmaStreamUInt4Gpu Result;
        internal SigmaStreamUInt4Gpu Reserved;
    }}

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct SigmaProofCandidateGpu
    {{
        internal SigmaStreamUInt4Gpu Identity;
        internal SigmaStreamUInt4Gpu Provenance;
        internal SigmaStreamUInt4Gpu Mask;
        internal SigmaStreamUInt4Gpu Source;
    }}

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct SigmaProofPrefixGpu
    {{
        internal SigmaStreamUInt4Gpu CoordinateMask;
        internal SigmaStreamUInt4Gpu Independence;
        internal SigmaStreamUInt4Gpu Gate;
    }}

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct SigmaAssociationSampleGpu
    {{
        internal SigmaStreamUInt4Gpu LeftDepthPage;
        internal SigmaStreamUInt4Gpu LeftStateUv;
        internal SigmaStreamUInt4Gpu LeftGeneration;
        internal SigmaStreamUInt4Gpu RightDepthPage;
        internal SigmaStreamUInt4Gpu RightStateUv;
        internal SigmaStreamUInt4Gpu RightGeneration;
    }}

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct SigmaPublicationManifestGpu
    {{
        internal SigmaStreamUInt4Gpu Identity;
        internal SigmaStreamUInt4Gpu Closure;
        internal SigmaStreamUInt4Gpu Pages;
        internal SigmaStreamUInt4Gpu Reserved;
    }}

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct SigmaPageVisibilityGpu
    {{
        internal SigmaStreamUInt4Gpu BornRetired;
        internal SigmaStreamUInt4Gpu Pins;
    }}

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct SigmaStreamWorkItemGpu
    {{
        internal SigmaStreamUInt4Gpu Identity;
        internal SigmaStreamUInt4Gpu Cursor;
    }}

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct SigmaStreamDiagnosticGpu
    {{
        internal SigmaStreamUInt4Gpu Admission;
        internal SigmaStreamUInt4Gpu Transactions;
        internal SigmaStreamUInt4Gpu Proof;
        internal SigmaStreamUInt4Gpu Transition;
        internal SigmaStreamUInt4Gpu Publication;
        internal SigmaStreamUInt4Gpu Lifetime;
        internal SigmaStreamUInt4Gpu Memory;
        internal SigmaStreamUInt4Gpu Reserved;
    }}

    internal static class SigmaGeneratedStreaming
    {{
        internal const string KernelExecutionSchema = "{execution['schema']}";
        internal const string KernelExecutionFingerprint =
            "{execution['fingerprint']}";
        internal const int OpcodeCount = {len(opcodes)};
        internal const int TokenQuantum = {execution['tokenQuantum']};
        internal const int TransactionCapacity = 8;
        internal const int BundleCapacity = 64;
        internal const int MaximumPagesPerTransaction = 4;
        internal const int SourceHandleWindowCapacity = 8;
        internal const int ProofCandidateWindowCapacity = 12;
        internal const int ProofSourceClassCount = 4;
        internal const int ProofBlocksPerPage = 64;
        internal const int MicrotilesPerProofBlock = 4;
        internal const int CalibrationQ48ValuesPerBundle = 88;
        internal const int TransactionStride = 368;
        internal const int BundleStride = 112;
        internal const int ProbationStride = 64;
        internal const int SourceHandleSegmentStride = 96;
        internal const int ProofClosureStride = 128;
        internal const int ProofCandidateStride = 64;
        internal const int ProofPrefixStride = 48;
        internal const int AssociationSampleStride = 96;
        internal const int PublicationManifestStride = 64;
        internal const int PageVisibilityStride = 32;
        internal const int WorkItemStride = 32;
        internal const int DiagnosticStride = 128;
        internal const uint ExecutionPhasePrepared = 1u << 0;
        internal const uint ExecutionPhaseRgbLeft = 1u << 1;
        internal const uint ExecutionPhaseRgbRight = 1u << 2;
        internal const uint ExecutionPhaseMet = 1u << 3;
        internal const uint ExecutionPhaseFinal = 1u << 4;
        internal const uint ExecutionIssued = 1u << 5;
        internal const uint ExecutionPhaseAll = (1u << 6) - 1u;
        internal const uint ExecutionProposalMask = (1u << 10) - 1u;
        internal const int ExecutionOutcomeShift = 16;
        internal const uint ExecutionOutcomeMask = 0x1fu <<
            ExecutionOutcomeShift;
        internal const uint ExecutionFault = 1u << 31;

        internal static readonly uint[] KernelTokenCost = {{ {cs_values('tokenCost')} }};
        internal static readonly uint[] KernelBudgetClass = {{ {cs_values('budgetClassId')} }};
        internal static readonly uint[] KernelThreadCount = {{ {', '.join(str(entry['threads'][0] * entry['threads'][1] * entry['threads'][2]) + 'u' for entry in opcodes)} }};
        internal static readonly uint[] KernelBytesRead = {{ {cs_values('bytes_read')} }};
        internal static readonly uint[] KernelBytesWritten = {{ {cs_values('bytes_written')} }};
        internal static readonly uint[] KernelScratchBytes = {{ {cs_values('scratch_bytes')} }};
        internal static readonly uint[] KernelBarrierCount = {{ {cs_values('barriers')} }};
        internal static readonly uint[] KernelWitnessCount = {{ {cs_values('annihilator_witnesses')} }};
        internal static readonly uint[] KernelMaximumRecords = {{ {cs_values('max_records')} }};
    }}
}}
"""


def render_streaming_hlsl(execution: dict) -> str:
    opcodes = execution["opcodes"]
    opcode_lines = "\n".join(
        f"#define SIGMA_STREAM_OPCODE_{entry['name']} {entry['id']}u"
        for entry in opcodes)
    budget_lines = "\n".join(
        f"#define SIGMA_STREAM_BUDGET_{name} {value}u"
        for name, value in BUDGET_CLASSES.items())

    def values(field: str) -> str:
        return uint_array(entry[field] for entry in opcodes)

    thread_counts = [entry["threads"][0] * entry["threads"][1] *
                     entry["threads"][2] for entry in opcodes]
    return f"""// <auto-generated by Tools/sigma/generate_sigma_operators.py>
// Canonical baseline: CPQ4-2026-08-22-S16-v6. Do not edit by hand.
#ifndef SIGMA_STREAMING_ABI_INCLUDED
#define SIGMA_STREAMING_ABI_INCLUDED

#include "SigmaCarrierAbi.hlsl"

#define SIGMA_STREAM_TRANSACTION_CAPACITY 8u
#define SIGMA_STREAM_BUNDLE_CAPACITY 64u
#define SIGMA_STREAM_MAX_PAGES 4u
#define SIGMA_STREAM_SOURCE_HANDLE_WINDOW 8u
#define SIGMA_STREAM_PROOF_CANDIDATE_WINDOW 12u
#define SIGMA_STREAM_PROOF_SOURCE_CLASS_COUNT 4u
#define SIGMA_STREAM_PROOF_BLOCKS_PER_PAGE 64u
#define SIGMA_STREAM_MICROTILES_PER_BLOCK 4u
#define SIGMA_STREAM_CALIBRATION_Q48_PER_BUNDLE 88u
#define SIGMA_STREAM_OPCODE_COUNT {len(opcodes)}u
#define SIGMA_STREAM_TOKEN_QUANTUM {execution['tokenQuantum']}u
#define SIGMA_STREAM_INVALID 0xffffffffu

#define SIGMA_STREAM_BUNDLE_FREE 0u
#define SIGMA_STREAM_BUNDLE_EXTRACTING 1u
#define SIGMA_STREAM_BUNDLE_READY 2u
#define SIGMA_STREAM_BUNDLE_PROBATION 3u
#define SIGMA_STREAM_BUNDLE_ADMISSION_PENDING 4u
#define SIGMA_STREAM_BUNDLE_ACTIVE 5u
#define SIGMA_STREAM_BUNDLE_ABSORBED 6u
#define SIGMA_STREAM_BUNDLE_DORMANT 7u

#define SIGMA_STREAM_TRANSACTION_FREE 0u
#define SIGMA_STREAM_TRANSACTION_WAITING_DEPENDENCY 1u
#define SIGMA_STREAM_TRANSACTION_EVALUATING 2u
#define SIGMA_STREAM_TRANSACTION_PROOF_PENDING 3u
#define SIGMA_STREAM_TRANSACTION_TRANSITION_PENDING 4u
#define SIGMA_STREAM_TRANSACTION_REVALIDATE_PENDING 5u
#define SIGMA_STREAM_TRANSACTION_PUBLISHABLE 6u
#define SIGMA_STREAM_TRANSACTION_PUBLISHED 7u
#define SIGMA_STREAM_TRANSACTION_DORMANT 8u
#define SIGMA_STREAM_TRANSACTION_FAILED 9u

#define SIGMA_STREAM_MANIFEST_FREE 0u
#define SIGMA_STREAM_MANIFEST_PREPARING 1u
#define SIGMA_STREAM_MANIFEST_PUBLISHED 2u
#define SIGMA_STREAM_MANIFEST_RETIRED 3u

#define SIGMA_STREAM_OUTCOME_NO_EVIDENCE 0u
#define SIGMA_STREAM_OUTCOME_EXISTING_UPDATE 1u
#define SIGMA_STREAM_OUTCOME_NULL_PROMOTION 2u
#define SIGMA_STREAM_OUTCOME_EMPTY_EXACT 3u
#define SIGMA_STREAM_OUTCOME_TRANSITION_UNRESOLVED 4u

#define SIGMA_STREAM_SOURCE_SEGMENT_FREE 0u
#define SIGMA_STREAM_SOURCE_SEGMENT_OPEN 1u
#define SIGMA_STREAM_SOURCE_SEGMENT_SEALED 2u
#define SIGMA_STREAM_SOURCE_SEGMENT_SPILLED 3u

#define SIGMA_STREAM_PROOF_IDLE 0u
#define SIGMA_STREAM_PROOF_JOURNAL 1u
#define SIGMA_STREAM_PROOF_SORT_RUNS 2u
#define SIGMA_STREAM_PROOF_MERGE_RUNS 3u
#define SIGMA_STREAM_PROOF_COALESCE 4u
#define SIGMA_STREAM_PROOF_PREFIX 5u
#define SIGMA_STREAM_PROOF_REDUNDANCY 6u
#define SIGMA_STREAM_PROOF_EMIT_CERTIFICATES 7u
#define SIGMA_STREAM_PROOF_EMIT_RAW 8u
#define SIGMA_STREAM_PROOF_CLOSED 9u

// Transient execution ownership. These bits never become canonical state.
#define SIGMA_STREAM_PHASE_PREPARED (1u << 0u)
#define SIGMA_STREAM_PHASE_RGB_LEFT (1u << 1u)
#define SIGMA_STREAM_PHASE_RGB_RIGHT (1u << 2u)
#define SIGMA_STREAM_PHASE_MET (1u << 3u)
#define SIGMA_STREAM_PHASE_FINAL (1u << 4u)
#define SIGMA_STREAM_EXECUTION_ISSUED (1u << 5u)
#define SIGMA_STREAM_PHASE_ALL (SIGMA_STREAM_PHASE_PREPARED | \
    SIGMA_STREAM_PHASE_RGB_LEFT | SIGMA_STREAM_PHASE_RGB_RIGHT | \
    SIGMA_STREAM_PHASE_MET | SIGMA_STREAM_PHASE_FINAL | \
    SIGMA_STREAM_EXECUTION_ISSUED)
#define SIGMA_STREAM_EXECUTION_OUTCOME_SHIFT 16u
#define SIGMA_STREAM_EXECUTION_OUTCOME_MASK (0x1fu << \
    SIGMA_STREAM_EXECUTION_OUTCOME_SHIFT)
#define SIGMA_STREAM_EXECUTION_FAULT (1u << 31u)

uint SigmaStreamOutcomeBit(uint outcome)
{{
    return outcome <= SIGMA_STREAM_OUTCOME_TRANSITION_UNRESOLVED
        ? 1u << (SIGMA_STREAM_EXECUTION_OUTCOME_SHIFT + outcome) : 0u;
}}

{budget_lines}

{opcode_lines}

struct SigmaStreamPageReferenceGpu
{{
    uint4 coordinate;
    uint4 state;
}};

struct SigmaTransactionGpu
{{
    uint4 identity;
    uint4 ticket;
    uint4 dependencyTicket;
    uint4 source;
    uint4 publication;
    SigmaStreamPageReferenceGpu page0;
    SigmaStreamPageReferenceGpu page1;
    SigmaStreamPageReferenceGpu page2;
    SigmaStreamPageReferenceGpu page3;
    uint4 affectedMaskLo;
    uint4 affectedMaskHi;
    uint4 completedMaskLo;
    uint4 completedMaskHi;
    uint4 progress;
    uint4 execution;
    uint4 scratch;
    uint4 transition;
    uint4 dependency0;
    uint4 dependency1;
}};

struct SigmaSealedSourceBundleGpu
{{
    uint4 identity;
    uint4 provenance;
    uint4 keys;
    uint4 raw;
    uint4 calibration;
    uint4 probation;
    uint4 dependency;
}};

struct SigmaProbationGpu
{{
    uint4 identity;
    uint4 bundleHandles;
    uint4 support;
    uint4 dependency;
}};

struct SigmaSourceHandleSegmentGpu
{{
    uint4 identity;
    uint4 link;
    uint4 handle01;
    uint4 handle23;
    uint4 handle45;
    uint4 handle67;
}};

struct SigmaProofClosureGpu
{{
    uint4 identity;
    uint4 sourceCursor;
    uint4 journal;
    uint4 ordering;
    uint4 coalesce;
    uint4 redundancy;
    uint4 result;
    uint4 reserved;
}};

struct SigmaProofCandidateGpu
{{
    uint4 identity;
    uint4 provenance;
    uint4 mask;
    uint4 source;
}};

struct SigmaProofPrefixGpu
{{
    uint4 coordinateMask;
    uint4 independence;
    uint4 gate;
}};

struct SigmaAssociationSampleGpu
{{
    uint4 leftDepthPage;
    uint4 leftStateUv;
    uint4 leftGeneration;
    uint4 rightDepthPage;
    uint4 rightStateUv;
    uint4 rightGeneration;
}};

struct SigmaPublicationManifestGpu
{{
    uint4 identity;
    uint4 closure;
    uint4 pages;
    uint4 reserved;
}};

struct SigmaPageVisibilityGpu
{{
    uint4 bornRetired;
    uint4 pins;
}};

struct SigmaStreamWorkItemGpu
{{
    uint4 identity;
    uint4 cursor;
}};

struct SigmaStreamDiagnosticGpu
{{
    uint4 admission;
    uint4 transactions;
    uint4 proof;
    uint4 transition;
    uint4 publication;
    uint4 lifetime;
    uint4 memory;
    uint4 reserved;
}};

static const uint SIGMA_STREAM_KERNEL_TOKEN_COST[SIGMA_STREAM_OPCODE_COUNT] = {{ {values('tokenCost')} }};
static const uint SIGMA_STREAM_KERNEL_BUDGET_CLASS[SIGMA_STREAM_OPCODE_COUNT] = {{ {values('budgetClassId')} }};
static const uint SIGMA_STREAM_KERNEL_THREAD_COUNT[SIGMA_STREAM_OPCODE_COUNT] = {{ {uint_array(thread_counts)} }};
static const uint SIGMA_STREAM_KERNEL_BYTES_READ[SIGMA_STREAM_OPCODE_COUNT] = {{ {values('bytes_read')} }};
static const uint SIGMA_STREAM_KERNEL_BYTES_WRITTEN[SIGMA_STREAM_OPCODE_COUNT] = {{ {values('bytes_written')} }};
static const uint SIGMA_STREAM_KERNEL_SCRATCH_BYTES[SIGMA_STREAM_OPCODE_COUNT] = {{ {values('scratch_bytes')} }};
static const uint SIGMA_STREAM_KERNEL_BARRIER_COUNT[SIGMA_STREAM_OPCODE_COUNT] = {{ {values('barriers')} }};
static const uint SIGMA_STREAM_KERNEL_WITNESS_COUNT[SIGMA_STREAM_OPCODE_COUNT] = {{ {values('annihilator_witnesses')} }};
static const uint SIGMA_STREAM_KERNEL_MAX_RECORDS[SIGMA_STREAM_OPCODE_COUNT] = {{ {values('max_records')} }};

#endif
"""


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
    execution = build_kernel_execution_descriptor(
        descriptor["operatorCosts"])
    frame = frame_abi_descriptor()
    if args.summary:
        print(json.dumps({
            "generator": descriptor["generatorVersion"],
            "zeroDivisorPairs": len(descriptor["annihilator"]["catalog"]),
            "annihilatorActions": len(descriptor["annihilator"]["actions"]),
            "zNull": descriptor["annihilator"]["zNull"],
            "geometryRows": descriptor["readout"]["geometryRows"],
            "fingerprints": descriptor["fingerprints"],
            "kernelExecutionFingerprint": execution["fingerprint"],
            "kernelOpcodes": len(execution["opcodes"]),
            "frameAbiFingerprint": frame["fingerprint"],
        }, indent=2))
    valid = check_or_write(CS_OUTPUT, render_cs(descriptor), args.check)
    valid &= check_or_write(HLSL_LAYOUT_OUTPUT,
                            render_hlsl_layout(descriptor), args.check)
    valid &= check_or_write(HLSL_OUTPUT, render_hlsl(descriptor), args.check)
    valid &= check_or_write(CS_STREAMING_OUTPUT,
                            render_streaming_cs(execution), args.check)
    valid &= check_or_write(HLSL_STREAMING_OUTPUT,
                            render_streaming_hlsl(execution), args.check)
    valid &= check_or_write(CS_FRAME_OUTPUT, render_frame_cs(frame), args.check)
    valid &= check_or_write(HLSL_FRAME_OUTPUT,
                            render_frame_hlsl(frame), args.check)
    return 0 if valid else 1


if __name__ == "__main__":
    raise SystemExit(main())
