from __future__ import annotations

import hashlib
import base64
import json
from pathlib import Path
import struct
import zipfile

from quest_infinite_server.contracts import (
    BlobDescriptor,
    ChunkBundleFile,
    ChunkBundleManifest,
    JobKey,
    JobSubmission,
)


def valid_bundle_bytes(key: JobKey, *, image_payload: bytes = b"\xff\xd8test\xff\xd9") -> bytes:
    payloads = {
        "mesh/live_mesh.qism": b"QISM-test-mesh",
        "keyframes/frames.jsonl": (
            json.dumps(
                {
                    "id": 0,
                    "space": "chunk",
                    "chunk": key.chunk_id,
                    "revision": key.chunk_revision,
                    "px": 0.0,
                    "py": 0.0,
                    "pz": 0.0,
                    "qx": 0.0,
                    "qy": 0.0,
                    "qz": 0.0,
                    "qw": 1.0,
                    "fx": 1000.0,
                    "fy": 1000.0,
                    "cx": 512.0,
                    "cy": 512.0,
                    "w": 1024,
                    "h": 1024,
                },
                separators=(",", ":"),
            ).encode("utf-8")
            + b"\n"
        ),
        "keyframes/images/000000.jpg": image_payload,
    }
    roles = {
        "mesh/live_mesh.qism": (
            "live_mesh", "application/vnd.questinfinitescan.live-mesh"
        ),
        "keyframes/frames.jsonl": ("keyframe_manifest", "application/x-ndjson"),
        "keyframes/images/000000.jpg": ("keyframe_image", "image/jpeg"),
    }
    files = tuple(
        ChunkBundleFile(
            role=roles[path][0],
            path=path,
            media_type=roles[path][1],
            byte_length=len(payload),
            sha256=hashlib.sha256(payload).hexdigest(),
        )
        for path, payload in payloads.items()
    )
    manifest = ChunkBundleManifest(key, files)
    from io import BytesIO

    destination = BytesIO()
    with zipfile.ZipFile(destination, "w", zipfile.ZIP_DEFLATED) as archive:
        archive.writestr(
            "input.json",
            json.dumps(manifest.to_wire(), sort_keys=True, separators=(",", ":")),
        )
        for path, payload in payloads.items():
            archive.writestr(path, payload)
    return destination.getvalue()


_JPEG_32 = base64.b64decode(
    "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAMCAgMCAgMDAwMEAwMEBQgFBQQEBQoH"
    "BwYIDAoMDAsKCwsNDhIQDQ4RDgsLEBYQERMUFRUVDA8XGBYUGBIUFRT/2wBDAQME"
    "BAUEBQkFBQkUDQsNFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQU"
    "FBQUFBQUFBQUFBQUFBT/wAARCAAgACADASIAAhEBAxEB/8QAFQABAQAAAAAAAAAA"
    "AAAAAAAAAAX/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/8QAFQEBAQAAAAAAAAAAAAAAAA"
    "AAAAf/xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oADAMBAAIRAxEAPwCmAqqaAAAAAAP/"
    "2Q=="
)


def valid_qism_bytes() -> bytes:
    packed_color = 96 | (144 << 8) | (192 << 16) | (255 << 24)
    vertices = b"".join(
        struct.pack("<6fII", *position, 0.0, 0.0, -1.0, packed_color, index)
        for index, position in enumerate(
            ((-0.5, -0.5, 2.0), (0.0, 0.5, 2.0), (0.5, -0.5, 2.0))
        )
    )
    indices = struct.pack("<3I", 0, 1, 2)
    header = struct.pack(
        "<IIiii6fii",
        0x4D534951,
        1,
        32,
        3,
        3,
        0.0,
        0.0,
        2.0,
        1.0,
        1.0,
        1.0,
        len(vertices),
        len(indices),
    )
    return header + vertices + indices


def valid_qirm_bytes() -> bytes:
    vertices = b"".join(
        struct.pack("<8f", *position, 0.0, 0.0, -1.0, *uv)
        for position, uv in (
            ((-0.5, -0.5, 2.0), (0.0, 0.0)),
            ((0.0, 0.5, 2.0), (0.5, 1.0)),
            ((0.5, -0.5, 2.0), (1.0, 0.0)),
        )
    )
    indices = struct.pack("<3i", 0, 1, 2)
    return struct.pack("<IIiiii", 0x4D524951, 1, 3, 3, 64, 64) + vertices + indices


def valid_qrs_bundle_bytes(key: JobKey, *, refined: bool = False) -> bytes:
    frame = {
        "id": 0,
        "ts": 1.0,
        "space": "chunk",
        "chunk": key.chunk_id,
        "revision": key.chunk_revision,
        "px": 0.0,
        "py": 0.0,
        "pz": 0.0,
        "qx": 0.0,
        "qy": 0.0,
        "qz": 0.0,
        "qw": 1.0,
        "fx": 32.0,
        "fy": 32.0,
        "cx": 16.0,
        "cy": 16.0,
        "sw": 32,
        "sh": 32,
        "w": 32,
        "h": 32,
    }
    mesh_path = "mesh/refined_mesh.qirm" if refined else "mesh/live_mesh.qism"
    payloads = {
        mesh_path: valid_qirm_bytes() if refined else valid_qism_bytes(),
        "keyframes/frames.jsonl": (
            json.dumps(frame, separators=(",", ":")).encode("utf-8") + b"\n"
        ),
        "keyframes/images/000000.jpg": _JPEG_32,
    }
    roles = {
        "mesh/live_mesh.qism": (
            "live_mesh",
            "application/vnd.questinfinitescan.live-mesh",
        ),
        "mesh/refined_mesh.qirm": (
            "refined_mesh",
            "application/vnd.questinfinitescan.refined-mesh",
        ),
        "keyframes/frames.jsonl": ("keyframe_manifest", "application/x-ndjson"),
        "keyframes/images/000000.jpg": ("keyframe_image", "image/jpeg"),
    }
    files = tuple(
        ChunkBundleFile(
            role=roles[path][0],
            path=path,
            media_type=roles[path][1],
            byte_length=len(payload),
            sha256=hashlib.sha256(payload).hexdigest(),
        )
        for path, payload in payloads.items()
    )
    manifest = ChunkBundleManifest(key, files)
    from io import BytesIO

    destination = BytesIO()
    with zipfile.ZipFile(destination, "w", zipfile.ZIP_STORED) as archive:
        archive.writestr(
            "input.json",
            json.dumps(manifest.to_wire(), sort_keys=True, separators=(",", ":")),
        )
        for path, payload in payloads.items():
            archive.writestr(path, payload)
    return destination.getvalue()


def submission_for_bundle(key: JobKey, bundle: bytes) -> JobSubmission:
    return JobSubmission(
        key,
        BlobDescriptor(
            "application/vnd.questinfinitescan.chunk+zip",
            1,
            len(bundle),
            hashlib.sha256(bundle).hexdigest(),
        ),
    )


def write_bytes(path: Path, data: bytes) -> Path:
    path.write_bytes(data)
    return path
