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
CS_FRAME_OUTPUT = (ROOT / "Runtime" / "SigmaPrism" / "Generated" /
                   "SigmaGeneratedFrame.cs")
HLSL_FRAME_OUTPUT = (ROOT / "Runtime" / "Resources" / "SigmaPrism" /
                     "SigmaFrameAbi.hlsl")
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
        }, indent=2))
    valid = check_or_write(CS_OUTPUT, render_cs(descriptor), args.check)
    valid &= check_or_write(HLSL_LAYOUT_OUTPUT,
                            render_hlsl_layout(descriptor), args.check)
    valid &= check_or_write(HLSL_OUTPUT, render_hlsl(descriptor), args.check)
    valid &= check_or_write(CS_FRAME_OUTPUT, render_frame_cs(frame), args.check)
    valid &= check_or_write(HLSL_FRAME_OUTPUT,
                            render_frame_hlsl(frame), args.check)
    return 0 if valid else 1


if __name__ == "__main__":
    raise SystemExit(main())
