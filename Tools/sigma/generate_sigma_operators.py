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
NUMERIC_ID = "num.fixed.q16_48.checked.nearest_even"
GENERATOR_VERSION = "CPQ4-S16-GEN-1"
LANES = 16


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
    return f"""// <auto-generated by Tools/sigma/generate_sigma_operators.py>
// Canonical baseline: CPQ4-2026-08-22-S16-v6. Do not edit by hand.
#ifndef SIGMA_GENERATED_LAYOUT_INCLUDED
#define SIGMA_GENERATED_LAYOUT_INCLUDED

#define SIGMA_S16_LANES 16u
#define SIGMA_Q48_FRACTION_BITS 48u
#define SIGMA_Q48_ONE_LO 0u
#define SIGMA_Q48_ONE_HI 0x00010000u

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
    if args.summary:
        print(json.dumps({
            "generator": descriptor["generatorVersion"],
            "zeroDivisorPairs": len(descriptor["annihilator"]["catalog"]),
            "annihilatorActions": len(descriptor["annihilator"]["actions"]),
            "zNull": descriptor["annihilator"]["zNull"],
            "geometryRows": descriptor["readout"]["geometryRows"],
            "fingerprints": descriptor["fingerprints"],
        }, indent=2))
    valid = check_or_write(CS_OUTPUT, render_cs(descriptor), args.check)
    valid &= check_or_write(HLSL_LAYOUT_OUTPUT,
                            render_hlsl_layout(descriptor), args.check)
    valid &= check_or_write(HLSL_OUTPUT, render_hlsl(descriptor), args.check)
    return 0 if valid else 1


if __name__ == "__main__":
    raise SystemExit(main())
