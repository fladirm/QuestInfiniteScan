from __future__ import annotations

import io
import json
from pathlib import Path
import struct
import zipfile

import pytest

from quest_infinite_server.qrs_dataset import QrsDatasetError, load_qrs_dataset
from quest_infinite_server.contracts import ChunkBundleFile, ChunkBundleManifest, JobKey

from helpers import valid_qrs_bundle_bytes


def test_qism_and_chunk_local_keyframe_are_strictly_converted(tmp_path: Path) -> None:
    key = JobKey("world-qrs", "chunk-000042", 7)
    path = tmp_path / "chunk.zip"
    path.write_bytes(valid_qrs_bundle_bytes(key))

    dataset = load_qrs_dataset(path, key)

    assert dataset.mesh.source_format == "qism-v1"
    assert dataset.mesh.vertex_count == 3
    assert dataset.mesh.face_count == 1
    assert dataset.mesh.has_vertex_colors
    assert len(dataset.frames) == 1
    assert dataset.frames[0].position == (0.0, 0.0, 0.0)
    assert dataset.frames[0].rotation_xyzw == (0.0, 0.0, 0.0, 1.0)
    assert (dataset.frames[0].width, dataset.frames[0].height) == (32, 32)


def test_qirm_refined_mesh_is_converted_without_inventing_vertex_color(tmp_path: Path) -> None:
    key = JobKey("world-qrs", "chunk-000043", 2)
    path = tmp_path / "refined.zip"
    path.write_bytes(valid_qrs_bundle_bytes(key, refined=True))

    dataset = load_qrs_dataset(path, key)

    assert dataset.mesh.source_format == "qirm-v1"
    assert dataset.mesh.vertex_count == 3
    assert dataset.mesh.face_count == 1
    assert dataset.mesh.has_vertex_colors is False


@pytest.mark.parametrize(
    ("mutation", "message"),
    (
        (lambda value: value.__setitem__(slice(0, 4), b"NOPE"), "magic or version"),
        (
            lambda value: struct.pack_into("<I", value, len(value) - 4, 99),
            "outside the vertex array",
        ),
        (
            lambda value: struct.pack_into("<f", value, 52, float("nan")),
            "non-finite",
        ),
    ),
)
def test_corrupt_qism_payload_is_rejected_after_bundle_rehash(
    tmp_path: Path, mutation, message: str
) -> None:
    key = JobKey("world-qrs", "chunk", 0)
    original = valid_qrs_bundle_bytes(key)
    source = zipfile.ZipFile(io.BytesIO(original), "r")
    payloads = {
        info.filename: source.read(info.filename)
        for info in source.infolist()
        if info.filename != "input.json"
    }
    source.close()
    mesh = bytearray(payloads["mesh/live_mesh.qism"])
    mutation(mesh)
    payloads["mesh/live_mesh.qism"] = bytes(mesh)
    path = _rebuild(path=tmp_path / "bad.zip", key=key, payloads=payloads)

    with pytest.raises(QrsDatasetError, match=message):
        load_qrs_dataset(path, key)


def test_frame_and_jpeg_dimensions_must_match(tmp_path: Path) -> None:
    key = JobKey("world-qrs", "chunk", 0)
    source = zipfile.ZipFile(io.BytesIO(valid_qrs_bundle_bytes(key)), "r")
    payloads = {
        info.filename: source.read(info.filename)
        for info in source.infolist()
        if info.filename != "input.json"
    }
    source.close()
    frame = json.loads(payloads["keyframes/frames.jsonl"])
    frame["w"] = 31
    payloads["keyframes/frames.jsonl"] = (
        json.dumps(frame, separators=(",", ":")).encode() + b"\n"
    )
    path = _rebuild(path=tmp_path / "bad-frame.zip", key=key, payloads=payloads)

    with pytest.raises(QrsDatasetError, match="JPEG dimensions"):
        load_qrs_dataset(path, key)


def _rebuild(path: Path, key: JobKey, payloads: dict[str, bytes]) -> Path:
    import hashlib

    roles = {
        "mesh/live_mesh.qism": (
            "live_mesh",
            "application/vnd.questinfinitescan.live-mesh",
        ),
        "keyframes/frames.jsonl": ("keyframe_manifest", "application/x-ndjson"),
        "keyframes/images/000000.jpg": ("keyframe_image", "image/jpeg"),
    }
    files = tuple(
        ChunkBundleFile(
            roles[name][0],
            name,
            roles[name][1],
            len(payload),
            hashlib.sha256(payload).hexdigest(),
        )
        for name, payload in payloads.items()
    )
    manifest = ChunkBundleManifest(key, files)
    with zipfile.ZipFile(path, "w", zipfile.ZIP_STORED) as archive:
        archive.writestr(
            "input.json",
            json.dumps(manifest.to_wire(), sort_keys=True, separators=(",", ":")),
        )
        for name, payload in payloads.items():
            archive.writestr(name, payload)
    return path
