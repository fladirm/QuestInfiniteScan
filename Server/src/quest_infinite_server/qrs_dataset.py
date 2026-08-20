"""Strict adapter from a verified Quest chunk bundle to DiffSoup inputs.

The adapter deliberately keeps the mesh in its canonical Unity chunk-local frame.
Only the camera view matrix changes at training time (Unity +Z forward to OpenGL
-Z forward), so exported geometry can be consumed by Quest without another
coordinate conversion.
"""

from __future__ import annotations

from dataclasses import dataclass
import json
import math
from pathlib import Path
import struct
from typing import Any, Mapping
import zipfile

from .bundle import validate_chunk_bundle
from .contracts import ChunkBundleManifest, JobKey, MAX_FACES, MAX_VERTICES


_QISM_MAGIC = 0x4D534951
_QIRM_MAGIC = 0x4D524951
_QISM_VERSION = 1
_QIRM_VERSION = 1
_VERTEX_STRIDE = 32
_MAX_FRAME_LINE_BYTES = 64 * 1024
_MAX_IMAGE_DIMENSION = 8_192
_MAX_POSITION_ABS_METERS = 100_000.0


class QrsDatasetError(ValueError):
    """Stable, user-actionable rejection of a QRS dataset."""


@dataclass(frozen=True, slots=True)
class QrsMesh:
    source_format: str
    vertex_count: int
    index_count: int
    vertex_stride: int
    vertex_bytes: bytes
    index_bytes: bytes
    indices_signed: bool
    has_vertex_colors: bool

    @property
    def face_count(self) -> int:
        return self.index_count // 3


@dataclass(frozen=True, slots=True)
class QrsFrame:
    frame_id: int
    timestamp: float | None
    revision: int
    image_path: str
    position: tuple[float, float, float]
    rotation_xyzw: tuple[float, float, float, float]
    fx: float
    fy: float
    cx: float
    cy: float
    sensor_width: int
    sensor_height: int
    width: int
    height: int


@dataclass(frozen=True, slots=True)
class QrsDataset:
    bundle_path: Path
    manifest: ChunkBundleManifest
    mesh: QrsMesh
    frames: tuple[QrsFrame, ...]


def load_qrs_dataset(path: Path | str, expected_key: JobKey) -> QrsDataset:
    """Validate and parse one QRS bundle without extracting it to the filesystem."""

    bundle_path = Path(path).resolve()
    manifest = validate_chunk_bundle(bundle_path, expected_key)
    by_role: dict[str, list[Any]] = {}
    for descriptor in manifest.files:
        by_role.setdefault(descriptor.role, []).append(descriptor)

    mesh_descriptor = (
        by_role.get("refined_mesh", []) or by_role.get("live_mesh", [])
    )[0]
    frame_descriptor = by_role["keyframe_manifest"][0]
    image_descriptors = by_role["keyframe_image"]

    with zipfile.ZipFile(bundle_path, "r") as archive:
        mesh_bytes = archive.read(mesh_descriptor.path)
        mesh = (
            _parse_qirm(mesh_bytes)
            if mesh_descriptor.role == "refined_mesh"
            else _parse_qism(mesh_bytes)
        )
        frame_bytes = archive.read(frame_descriptor.path)
        image_dimensions: dict[str, tuple[int, int]] = {}
        for descriptor in image_descriptors:
            with archive.open(descriptor.path, "r") as stream:
                # JPEG headers are small. A 1 MiB cap avoids decoding or retaining image
                # payloads in the API process while still accepting large APP metadata.
                header = stream.read(min(descriptor.byte_length, 1024 * 1024))
            image_dimensions[descriptor.path] = _jpeg_dimensions(header)

    frames = _parse_frames(frame_bytes, expected_key, image_dimensions)
    return QrsDataset(bundle_path, manifest, mesh, frames)


def read_frame_image(dataset: QrsDataset, frame: QrsFrame) -> bytes:
    """Read one already-declared image; used lazily by the CUDA worker."""

    if frame not in dataset.frames:
        raise QrsDatasetError("frame does not belong to this dataset")
    with zipfile.ZipFile(dataset.bundle_path, "r") as archive:
        return archive.read(frame.image_path)


def _parse_qism(data: bytes) -> QrsMesh:
    header_format = "<IIiii6fii"
    header_size = struct.calcsize(header_format)
    if len(data) < header_size:
        raise QrsDatasetError("QISM live mesh header is truncated")
    values = struct.unpack_from(header_format, data)
    magic, version, stride, vertex_count, index_count = values[:5]
    bounds = values[5:11]
    vertex_length, index_length = values[11:13]
    if magic != _QISM_MAGIC or version != _QISM_VERSION:
        raise QrsDatasetError("QISM live mesh magic or version is unsupported")
    if stride != _VERTEX_STRIDE:
        raise QrsDatasetError("QISM live mesh vertex stride is unsupported")
    _validate_counts(vertex_count, index_count)
    _validate_bounds(bounds)
    if vertex_length != vertex_count * stride or index_length != index_count * 4:
        raise QrsDatasetError("QISM live mesh payload lengths do not match its counts")
    if len(data) != header_size + vertex_length + index_length:
        raise QrsDatasetError("QISM live mesh has a truncated or trailing payload")
    vertices = data[header_size : header_size + vertex_length]
    indices = data[header_size + vertex_length :]
    _validate_vertices(vertices, vertex_count, stride, uv_offset=None)
    _validate_indices(indices, index_count, vertex_count, signed=False)
    return QrsMesh(
        "qism-v1",
        vertex_count,
        index_count,
        stride,
        vertices,
        indices,
        False,
        True,
    )


def _parse_qirm(data: bytes) -> QrsMesh:
    header_format = "<IIiiii"
    header_size = struct.calcsize(header_format)
    if len(data) < header_size:
        raise QrsDatasetError("QIRM refined mesh header is truncated")
    magic, version, vertex_count, index_count, atlas_width, atlas_height = (
        struct.unpack_from(header_format, data)
    )
    if magic != _QIRM_MAGIC or version != _QIRM_VERSION:
        raise QrsDatasetError("QIRM refined mesh magic or version is unsupported")
    _validate_counts(vertex_count, index_count)
    if not (
        1 <= atlas_width <= _MAX_IMAGE_DIMENSION
        and 1 <= atlas_height <= _MAX_IMAGE_DIMENSION
    ):
        raise QrsDatasetError("QIRM atlas dimensions are outside supported limits")
    vertex_length = vertex_count * _VERTEX_STRIDE
    index_length = index_count * 4
    if len(data) != header_size + vertex_length + index_length:
        raise QrsDatasetError("QIRM refined mesh has a truncated or trailing payload")
    vertices = data[header_size : header_size + vertex_length]
    indices = data[header_size + vertex_length :]
    _validate_vertices(vertices, vertex_count, _VERTEX_STRIDE, uv_offset=24)
    _validate_indices(indices, index_count, vertex_count, signed=True)
    return QrsMesh(
        "qirm-v1",
        vertex_count,
        index_count,
        _VERTEX_STRIDE,
        vertices,
        indices,
        True,
        False,
    )


def _validate_counts(vertex_count: int, index_count: int) -> None:
    if not 3 <= vertex_count <= MAX_VERTICES:
        raise QrsDatasetError("mesh vertex count is outside supported limits")
    if not 3 <= index_count <= MAX_FACES * 3 or index_count % 3:
        raise QrsDatasetError("mesh index count is outside supported limits")


def _validate_bounds(values: tuple[float, ...]) -> None:
    if len(values) != 6 or not all(math.isfinite(value) for value in values):
        raise QrsDatasetError("QISM bounds contain non-finite values")
    if any(value < 0.0 or value > _MAX_POSITION_ABS_METERS for value in values[3:]):
        raise QrsDatasetError("QISM bounds extents are outside supported limits")


def _validate_vertices(
    data: bytes, vertex_count: int, stride: int, uv_offset: int | None
) -> None:
    for index in range(vertex_count):
        offset = index * stride
        position_normal = struct.unpack_from("<6f", data, offset)
        if not all(math.isfinite(value) for value in position_normal):
            raise QrsDatasetError(f"mesh vertex {index} contains a non-finite value")
        if any(abs(value) > _MAX_POSITION_ABS_METERS for value in position_normal[:3]):
            raise QrsDatasetError(f"mesh vertex {index} is outside the coordinate limit")
        if uv_offset is not None:
            uv = struct.unpack_from("<2f", data, offset + uv_offset)
            if not all(math.isfinite(value) and abs(value) <= 1_000.0 for value in uv):
                raise QrsDatasetError(f"mesh vertex {index} contains an invalid UV")


def _validate_indices(
    data: bytes, index_count: int, vertex_count: int, *, signed: bool
) -> None:
    code = "<i" if signed else "<I"
    for index in range(index_count):
        value = struct.unpack_from(code, data, index * 4)[0]
        if value < 0 or value >= vertex_count:
            raise QrsDatasetError(f"mesh index {index} is outside the vertex array")


def _parse_frames(
    data: bytes,
    key: JobKey,
    image_dimensions: Mapping[str, tuple[int, int]],
) -> tuple[QrsFrame, ...]:
    frames: list[QrsFrame] = []
    seen_ids: set[int] = set()
    seen_paths: set[str] = set()
    previous_id = -1
    for line_number, raw_line in enumerate(data.splitlines(), start=1):
        if not raw_line.strip():
            continue
        if len(raw_line) > _MAX_FRAME_LINE_BYTES:
            raise QrsDatasetError(f"keyframe line {line_number} exceeds the size limit")
        try:
            value = json.loads(raw_line.decode("utf-8"))
        except (UnicodeDecodeError, json.JSONDecodeError) as exception:
            raise QrsDatasetError(
                f"keyframe line {line_number} is not valid UTF-8 JSON"
            ) from exception
        frame = _parse_frame(value, key, image_dimensions, line_number)
        if frame.frame_id in seen_ids or frame.frame_id <= previous_id:
            raise QrsDatasetError("keyframe IDs must be unique and strictly increasing")
        if frame.image_path in seen_paths:
            raise QrsDatasetError("a keyframe image is referenced more than once")
        previous_id = frame.frame_id
        seen_ids.add(frame.frame_id)
        seen_paths.add(frame.image_path)
        frames.append(frame)
    if not frames:
        raise QrsDatasetError("keyframe manifest contains no usable frames")
    if seen_paths != set(image_dimensions):
        raise QrsDatasetError("keyframe manifest and declared JPEG images do not match")
    return tuple(frames)


def _parse_frame(
    value: Any,
    key: JobKey,
    image_dimensions: Mapping[str, tuple[int, int]],
    line_number: int,
) -> QrsFrame:
    if not isinstance(value, Mapping):
        raise QrsDatasetError(f"keyframe line {line_number} must contain an object")
    required = {
        "id", "space", "chunk", "revision", "px", "py", "pz", "qx", "qy",
        "qz", "qw", "fx", "fy", "cx", "cy", "w", "h",
    }
    optional = {"ts", "sw", "sh"}
    if set(value) - required - optional or required - set(value):
        raise QrsDatasetError(f"keyframe line {line_number} has an unsupported field set")
    frame_id = _as_int(value["id"], "id", 0, 9_999_999)
    revision = _as_int(value["revision"], "revision", 0, key.chunk_revision)
    if value["space"] != "chunk" or value["chunk"] != key.chunk_id:
        raise QrsDatasetError(f"keyframe line {line_number} is not in the target chunk")
    width = _as_int(value["w"], "w", 1, _MAX_IMAGE_DIMENSION)
    height = _as_int(value["h"], "h", 1, _MAX_IMAGE_DIMENSION)
    sensor_width = _as_int(value.get("sw", width), "sw", width, _MAX_IMAGE_DIMENSION)
    sensor_height = _as_int(value.get("sh", height), "sh", height, _MAX_IMAGE_DIMENSION)
    position = tuple(_as_float(value[field], field, -_MAX_POSITION_ABS_METERS,
                               _MAX_POSITION_ABS_METERS) for field in ("px", "py", "pz"))
    rotation = tuple(_as_float(value[field], field, -1.001, 1.001)
                     for field in ("qx", "qy", "qz", "qw"))
    norm = math.sqrt(sum(component * component for component in rotation))
    if abs(norm - 1.0) > 0.02:
        raise QrsDatasetError(f"keyframe line {line_number} quaternion is not normalized")
    fx = _as_float(value["fx"], "fx", 0.001, 1_000_000.0)
    fy = _as_float(value["fy"], "fy", 0.001, 1_000_000.0)
    cx = _as_float(value["cx"], "cx", -sensor_width, sensor_width * 2.0)
    cy = _as_float(value["cy"], "cy", -sensor_height, sensor_height * 2.0)
    timestamp = (
        None
        if "ts" not in value
        else _as_float(value["ts"], "ts", 0.0, 1.0e12)
    )
    image_path = f"keyframes/images/{frame_id:06d}.jpg"
    if image_path not in image_dimensions:
        jpeg_path = f"keyframes/images/{frame_id:06d}.jpeg"
        image_path = jpeg_path if jpeg_path in image_dimensions else image_path
    dimensions = image_dimensions.get(image_path)
    if dimensions is None:
        raise QrsDatasetError(f"keyframe line {line_number} has no declared JPEG")
    if dimensions != (width, height):
        raise QrsDatasetError(
            f"keyframe {frame_id} JPEG dimensions do not match frames.jsonl"
        )
    return QrsFrame(
        frame_id,
        timestamp,
        revision,
        image_path,
        position,  # type: ignore[arg-type]
        rotation,  # type: ignore[arg-type]
        fx,
        fy,
        cx,
        cy,
        sensor_width,
        sensor_height,
        width,
        height,
    )


def _as_int(value: Any, field: str, minimum: int, maximum: int) -> int:
    if isinstance(value, bool) or not isinstance(value, int):
        raise QrsDatasetError(f"keyframe {field} must be an integer")
    if value < minimum or value > maximum:
        raise QrsDatasetError(f"keyframe {field} is outside supported limits")
    return value


def _as_float(value: Any, field: str, minimum: float, maximum: float) -> float:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise QrsDatasetError(f"keyframe {field} must be numeric")
    result = float(value)
    if not math.isfinite(result) or result < minimum or result > maximum:
        raise QrsDatasetError(f"keyframe {field} is outside supported limits")
    return result


def _jpeg_dimensions(data: bytes) -> tuple[int, int]:
    if len(data) < 4 or data[:2] != b"\xff\xd8":
        raise QrsDatasetError("declared keyframe image is not a JPEG")
    offset = 2
    sof_markers = {
        0xC0, 0xC1, 0xC2, 0xC3, 0xC5, 0xC6, 0xC7,
        0xC9, 0xCA, 0xCB, 0xCD, 0xCE, 0xCF,
    }
    while offset + 1 < len(data):
        while offset < len(data) and data[offset] != 0xFF:
            offset += 1
        while offset < len(data) and data[offset] == 0xFF:
            offset += 1
        if offset >= len(data):
            break
        marker = data[offset]
        offset += 1
        if marker in (0xD8, 0xD9, 0x01) or 0xD0 <= marker <= 0xD7:
            continue
        if offset + 2 > len(data):
            break
        segment_length = struct.unpack_from(">H", data, offset)[0]
        if segment_length < 2 or offset + segment_length > len(data):
            break
        if marker in sof_markers:
            if segment_length < 7:
                break
            height, width = struct.unpack_from(">HH", data, offset + 3)
            if not (
                1 <= width <= _MAX_IMAGE_DIMENSION
                and 1 <= height <= _MAX_IMAGE_DIMENSION
            ):
                raise QrsDatasetError("JPEG dimensions are outside supported limits")
            return width, height
        if marker == 0xDA:
            break
        offset += segment_length
    raise QrsDatasetError("JPEG dimensions are missing from its bounded header")
