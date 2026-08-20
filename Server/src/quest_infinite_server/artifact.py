"""Fail-closed validation for server-produced DiffSoup artifact bundles."""

from __future__ import annotations

import hashlib
import json
import math
from pathlib import Path, PurePosixPath
import stat
import struct
from typing import Any
import zipfile

from .contracts import (
    MAX_ARTIFACT_BUNDLE_BYTES,
    MAX_ARTIFACT_FILES,
    ContractError,
    DiffSoupArtifactManifest,
    JobKey,
)


_MAX_MANIFEST_BYTES = 2 * 1024 * 1024
_MAX_JSON_BYTES = 4 * 1024 * 1024
_MAX_COMPRESSION_RATIO = 200
_PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"


class ArtifactValidationError(ValueError):
    pass


def validate_artifact_bundle(
    path: Path | str,
    *,
    expected_key: JobKey | None = None,
    expected_request_fingerprint: str | None = None,
) -> DiffSoupArtifactManifest:
    artifact_path = Path(path)
    try:
        if not artifact_path.is_file():
            raise ArtifactValidationError("artifact is not a regular file")
        size = artifact_path.stat().st_size
        if not 1 <= size <= MAX_ARTIFACT_BUNDLE_BYTES:
            raise ArtifactValidationError("artifact ZIP size is outside supported limits")
        with zipfile.ZipFile(artifact_path, "r") as archive:
            infos = archive.infolist()
            if not 2 <= len(infos) <= MAX_ARTIFACT_FILES + 1:
                raise ArtifactValidationError("artifact entry count is outside supported limits")
            names: set[str] = set()
            folded: set[str] = set()
            total = 0
            for info in infos:
                if info.is_dir() or not _safe_name(info.filename):
                    raise ArtifactValidationError("artifact contains an unsafe path")
                folded_name = info.filename.casefold()
                if info.filename in names or folded_name in folded:
                    raise ArtifactValidationError("artifact contains duplicate paths")
                names.add(info.filename)
                folded.add(folded_name)
                if info.flag_bits & 0x1:
                    raise ArtifactValidationError("encrypted artifact entries are unsupported")
                mode = info.external_attr >> 16
                if mode and stat.S_IFMT(mode) not in (0, stat.S_IFREG):
                    raise ArtifactValidationError("artifact contains a link or special entry")
                total += info.file_size
                if total > MAX_ARTIFACT_BUNDLE_BYTES:
                    raise ArtifactValidationError("artifact expands beyond the aggregate limit")
                if (
                    info.file_size > 1024 * 1024
                    and info.file_size > max(1, info.compress_size) * _MAX_COMPRESSION_RATIO
                ):
                    raise ArtifactValidationError("artifact compression ratio is unsafe")
            if "artifact.json" not in names:
                raise ArtifactValidationError("artifact.json is missing")
            info = archive.getinfo("artifact.json")
            if info.file_size > _MAX_MANIFEST_BYTES:
                raise ArtifactValidationError("artifact manifest exceeds the size limit")
            manifest_bytes = archive.read(info)
            try:
                manifest = DiffSoupArtifactManifest.from_wire(
                    json.loads(manifest_bytes.decode("utf-8"))
                )
            except (UnicodeDecodeError, json.JSONDecodeError, ContractError) as exception:
                raise ArtifactValidationError(
                    f"artifact manifest is invalid: {exception}"
                ) from exception
            if expected_key is not None and manifest.key != expected_key:
                raise ArtifactValidationError("artifact job key does not match the request")
            if (
                expected_request_fingerprint is not None
                and manifest.request_fingerprint != expected_request_fingerprint
            ):
                raise ArtifactValidationError(
                    "artifact request fingerprint does not match the request"
                )
            declared = {descriptor.path: descriptor for descriptor in manifest.files}
            if names != {"artifact.json", *declared}:
                raise ArtifactValidationError(
                    "artifact ZIP entries do not exactly match artifact.json"
                )
            payloads: dict[str, bytes] = {}
            for name, descriptor in declared.items():
                entry = archive.getinfo(name)
                if entry.file_size != descriptor.byte_length:
                    raise ArtifactValidationError(f"artifact size mismatch for {name}")
                digest = hashlib.sha256()
                retained_limit = (
                    33
                    if descriptor.role in {"lut0", "lut1"}
                    else _MAX_JSON_BYTES
                    if descriptor.role in {"mlp", "meta"}
                    else 0
                )
                if descriptor.role in {"mlp", "meta"} and entry.file_size > retained_limit:
                    raise ArtifactValidationError(
                        f"artifact JSON payload exceeds the limit for {name}"
                    )
                chunks: list[bytes] = []
                read = 0
                with archive.open(entry, "r") as stream:
                    while block := stream.read(1024 * 1024):
                        read += len(block)
                        if read > descriptor.byte_length:
                            raise ArtifactValidationError(
                                f"artifact payload exceeds its declaration for {name}"
                            )
                        digest.update(block)
                        if retained_limit and sum(map(len, chunks)) < retained_limit:
                            retained = retained_limit - sum(map(len, chunks))
                            chunks.append(block[:retained])
                if read != descriptor.byte_length or digest.hexdigest() != descriptor.sha256:
                    raise ArtifactValidationError(f"artifact hash mismatch for {name}")
                if retained_limit:
                    payloads[descriptor.role] = b"".join(chunks)

            mesh_descriptor = next(file for file in manifest.files if file.role == "mesh")
            with archive.open(mesh_descriptor.path, "r") as mesh_stream:
                _validate_ply_stream(
                    mesh_stream,
                    mesh_descriptor.byte_length,
                    manifest.num_vertices,
                    manifest.num_faces,
                )
            _validate_png(payloads["lut0"], manifest.lut_width, manifest.lut_height)
            _validate_png(payloads["lut1"], manifest.lut_width, manifest.lut_height)
            _validate_mlp(payloads["mlp"])
            _validate_meta(payloads["meta"], manifest)
            return manifest
    except zipfile.BadZipFile as exception:
        raise ArtifactValidationError("artifact is not a valid ZIP archive") from exception


def _safe_name(name: str) -> bool:
    if not name or name.startswith("/") or "\\" in name or "\x00" in name:
        return False
    path = PurePosixPath(name)
    return not path.is_absolute() and all(part not in ("", ".", "..") for part in path.parts)


def _validate_ply_stream(
    stream: Any,
    byte_length: int,
    expected_vertices: int,
    expected_faces: int,
) -> None:
    marker = b"end_header\n"
    header = bytearray()
    while not header.endswith(marker) and len(header) <= 16 * 1024:
        line = stream.readline(16 * 1024 + 1 - len(header))
        if not line:
            break
        header.extend(line)
    if not header.endswith(marker) or len(header) > 16 * 1024:
        raise ArtifactValidationError("DiffSoup mesh PLY header is invalid")
    payload_offset = len(header)
    try:
        lines = bytes(header).decode("ascii").splitlines()
    except UnicodeDecodeError as exception:
        raise ArtifactValidationError("DiffSoup mesh PLY header is not ASCII") from exception
    if not lines[:2] == ["ply", "format binary_little_endian 1.0"]:
        raise ArtifactValidationError("DiffSoup mesh must be binary little-endian PLY")
    vertex_lines = [line for line in lines if line.startswith("element vertex ")]
    face_lines = [line for line in lines if line.startswith("element face ")]
    if len(vertex_lines) != 1 or len(face_lines) != 1:
        raise ArtifactValidationError("DiffSoup mesh PLY element declarations are invalid")
    try:
        vertices = int(vertex_lines[0].split()[-1])
        faces = int(face_lines[0].split()[-1])
    except ValueError as exception:
        raise ArtifactValidationError("DiffSoup mesh PLY counts are invalid") from exception
    if vertices != expected_vertices or faces != expected_faces:
        raise ArtifactValidationError("DiffSoup mesh PLY counts disagree with artifact.json")
    expected_length = payload_offset + vertices * 12 + faces * 13
    if byte_length != expected_length:
        raise ArtifactValidationError("DiffSoup mesh PLY payload length is invalid")
    validated = 0
    while validated < vertices:
        count = min(65_536, vertices - validated)
        block = _read_exact(stream, count * 12, "DiffSoup mesh vertex payload")
        for relative, xyz in enumerate(struct.iter_unpack("<3f", block)):
            if not all(math.isfinite(value) and abs(value) <= 100_000.0 for value in xyz):
                raise ArtifactValidationError(
                    f"DiffSoup mesh vertex {validated + relative} is invalid"
                )
        validated += count
    validated = 0
    while validated < faces:
        count = min(65_536, faces - validated)
        block = _read_exact(stream, count * 13, "DiffSoup mesh face payload")
        for relative, (arity, a, b, c) in enumerate(struct.iter_unpack("<B3i", block)):
            if arity != 3 or min(a, b, c) < 0 or max(a, b, c) >= vertices:
                raise ArtifactValidationError(
                    f"DiffSoup mesh face {validated + relative} is invalid"
                )
        validated += count
    if stream.read(1):
        raise ArtifactValidationError("DiffSoup mesh PLY contains trailing bytes")


def _read_exact(stream: Any, count: int, label: str) -> bytes:
    blocks: list[bytes] = []
    remaining = count
    while remaining:
        block = stream.read(remaining)
        if not block:
            raise ArtifactValidationError(f"{label} is truncated")
        blocks.append(block)
        remaining -= len(block)
    return b"".join(blocks)


def _validate_png(data: bytes, width: int, height: int) -> None:
    if len(data) < 33 or data[:8] != _PNG_SIGNATURE:
        raise ArtifactValidationError("DiffSoup LUT is not a PNG")
    length, kind = struct.unpack_from(">I4s", data, 8)
    if length != 13 or kind != b"IHDR":
        raise ArtifactValidationError("DiffSoup LUT PNG has an invalid IHDR")
    values = struct.unpack_from(">IIBBBBB", data, 16)
    if values != (width, height, 8, 6, 0, 0, 0):
        raise ArtifactValidationError("DiffSoup LUT PNG format or dimensions are invalid")


def _validate_mlp(data: bytes) -> None:
    value = _json_object(data, "MLP")
    expected = {"W1": 256, "b1": 16, "W2": 256, "b2": 16, "W3": 48, "b3": 3}
    if set(value) != set(expected):
        raise ArtifactValidationError("DiffSoup MLP JSON has an unsupported field set")
    for key, length in expected.items():
        array = value[key]
        if not isinstance(array, list) or len(array) != length:
            raise ArtifactValidationError(f"DiffSoup MLP {key} has an invalid shape")
        if not all(
            not isinstance(item, bool)
            and isinstance(item, (int, float))
            and math.isfinite(float(item))
            and abs(float(item)) <= 1.0e6
            for item in array
        ):
            raise ArtifactValidationError(f"DiffSoup MLP {key} contains invalid values")


def _validate_meta(data: bytes, manifest: DiffSoupArtifactManifest) -> None:
    value = _json_object(data, "metadata")
    required = {"up", "level", "background", "num_faces", "num_verts"}
    if not required <= set(value):
        raise ArtifactValidationError("DiffSoup metadata is missing required fields")
    if (
        value["level"] != manifest.level
        or value["num_faces"] != manifest.num_faces
        or value["num_verts"] != manifest.num_vertices
    ):
        raise ArtifactValidationError("DiffSoup metadata disagrees with artifact.json")
    for field in ("up", "background"):
        vector = value[field]
        if not isinstance(vector, list) or len(vector) != 3 or not all(
            not isinstance(item, bool)
            and isinstance(item, (int, float))
            and math.isfinite(float(item))
            for item in vector
        ):
            raise ArtifactValidationError(f"DiffSoup metadata {field} is invalid")


def _json_object(data: bytes, label: str) -> dict[str, Any]:
    if len(data) > _MAX_JSON_BYTES:
        raise ArtifactValidationError(f"DiffSoup {label} JSON exceeds the size limit")
    try:
        value = json.loads(data.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exception:
        raise ArtifactValidationError(f"DiffSoup {label} JSON is invalid") from exception
    if not isinstance(value, dict):
        raise ArtifactValidationError(f"DiffSoup {label} JSON must contain an object")
    return value
