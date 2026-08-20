"""Compute backend boundary and a deterministic CUDA-free acceptance backend."""

from __future__ import annotations

import asyncio
from dataclasses import dataclass
import hashlib
import json
import os
from pathlib import Path
import struct
from typing import Protocol
import uuid
import zipfile
import zlib

from .contracts import (
    ArtifactFile,
    BlobDescriptor,
    DiffSoupArtifactManifest,
    JobSubmission,
)
from .storage import JobStore, UploadRecord


FAKE_COMPATIBILITY_TAG = hashlib.sha256(b"quest-infinite-fake-backend-v1").hexdigest()


class JobCanceledError(RuntimeError):
    pass


class BackendJobError(RuntimeError):
    """A structured worker failure safe to expose in durable job status."""

    def __init__(self, code: str, message: str) -> None:
        self.code = code
        self.message = message
        super().__init__(message)


@dataclass(frozen=True, slots=True)
class BackendResult:
    artifact_path: Path
    descriptor: BlobDescriptor


@dataclass(slots=True)
class BackendContext:
    submission: JobSubmission
    upload: UploadRecord
    store: JobStore

    def report(self, progress: float, message: str) -> None:
        if self.store.is_cancel_requested(self.submission.job_id):
            raise JobCanceledError("job canceled by client")
        self.store.update_progress(self.submission.job_id, progress, message)

    def check_canceled(self) -> None:
        if self.store.is_cancel_requested(self.submission.job_id):
            raise JobCanceledError("job canceled by client")


class ComputeBackend(Protocol):
    name: str

    async def run(self, context: BackendContext) -> BackendResult: ...


class FakeDiffSoupBackend:
    """Produces a tiny but contract-valid DiffSoup artifact without CUDA."""

    name = "fake"

    def __init__(self, step_delay_seconds: float = 0.01) -> None:
        self.step_delay_seconds = max(0.0, step_delay_seconds)

    async def run(self, context: BackendContext) -> BackendResult:
        for progress, message in ((0.2, "validating"), (0.55, "optimizing"), (0.85, "exporting")):
            context.report(progress, message)
            if self.step_delay_seconds:
                await asyncio.sleep(self.step_delay_seconds)
        context.check_canceled()
        result = await asyncio.to_thread(self._build_artifact, context)
        context.check_canceled()
        return result

    @staticmethod
    def _build_artifact(context: BackendContext) -> BackendResult:
        submission = context.submission
        payloads = {
            "model/mesh.ply": _triangle_ply(),
            "model/lut0.png": _rgba_png(3, 1, bytes((128, 128, 128, 128)) * 3),
            "model/lut1.png": _rgba_png(3, 1, bytes((128, 128, 128, 255)) * 3),
            "model/mlp_weights.json": _json_bytes(
                {
                    "W1": [0.0] * 256,
                    "b1": [0.0] * 16,
                    "W2": [0.0] * 256,
                    "b2": [0.0] * 16,
                    "W3": [0.0] * 48,
                    "b3": [0.0] * 3,
                }
            ),
            "model/meta.json": _json_bytes(
                {
                    "up": [0.0, 1.0, 0.0],
                    "level": 0,
                    "background": [0.0, 0.0, 0.0],
                    "num_faces": 1,
                    "num_verts": 3,
                    "backend": "fake",
                }
            ),
        }
        role_by_path = {
            "model/mesh.ply": (
                "mesh", "application/vnd.questinfinitescan.diffsoup-mesh"
            ),
            "model/lut0.png": ("lut0", "image/png"),
            "model/lut1.png": ("lut1", "image/png"),
            "model/mlp_weights.json": ("mlp", "application/json"),
            "model/meta.json": ("meta", "application/json"),
        }
        files = tuple(
            ArtifactFile(
                role=role_by_path[path][0],
                path=path,
                media_type=role_by_path[path][1],
                format_version=1,
                byte_length=len(payload),
                sha256=hashlib.sha256(payload).hexdigest(),
            )
            for path, payload in payloads.items()
        )
        manifest = DiffSoupArtifactManifest(
            key=submission.key,
            request_fingerprint=submission.request_fingerprint,
            producer_commit="0" * 40,
            compatibility_tag=FAKE_COMPATIBILITY_TAG,
            level=0,
            num_vertices=3,
            num_faces=1,
            lut_width=3,
            lut_height=1,
            files=files,
        )
        payloads = {"artifact.json": _json_bytes(manifest.to_wire()), **payloads}
        final_path = context.store.artifact_root / f"{submission.job_id}.zip"
        temporary = context.store.temp_root / (
            f"artifact-{submission.job_id}-{uuid.uuid4().hex}.tmp"
        )
        try:
            with zipfile.ZipFile(temporary, "w", compression=zipfile.ZIP_STORED) as archive:
                for path in sorted(payloads):
                    info = zipfile.ZipInfo(path, date_time=(1980, 1, 1, 0, 0, 0))
                    info.compress_type = zipfile.ZIP_STORED
                    info.external_attr = 0o100600 << 16
                    archive.writestr(info, payloads[path])
            with temporary.open("rb") as stream:
                os.fsync(stream.fileno())
            digest = _file_sha256(temporary)
            byte_length = temporary.stat().st_size
            os.replace(temporary, final_path)
            _fsync_directory(final_path.parent)
            return BackendResult(
                final_path,
                BlobDescriptor(
                    "application/vnd.questinfinitescan.diffsoup+zip",
                    1,
                    byte_length,
                    digest,
                ),
            )
        finally:
            temporary.unlink(missing_ok=True)


def _triangle_ply() -> bytes:
    header = (
        "ply\n"
        "format binary_little_endian 1.0\n"
        "element vertex 3\n"
        "property float x\nproperty float y\nproperty float z\n"
        "element face 1\n"
        "property list uchar int vertex_indices\n"
        "end_header\n"
    ).encode("ascii")
    vertices = struct.pack("<9f", 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 1.0, 0.0)
    face = struct.pack("<B3i", 3, 0, 1, 2)
    return header + vertices + face


def _rgba_png(width: int, height: int, pixels: bytes) -> bytes:
    if len(pixels) != width * height * 4:
        raise ValueError("RGBA payload dimensions mismatch")

    def chunk(kind: bytes, data: bytes) -> bytes:
        body = kind + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body))

    rows = b"".join(
        b"\x00" + pixels[row * width * 4 : (row + 1) * width * 4]
        for row in range(height)
    )
    return (
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
        + chunk(b"IDAT", zlib.compress(rows, level=9))
        + chunk(b"IEND", b"")
    )


def _json_bytes(value: object) -> bytes:
    return json.dumps(
        value, sort_keys=True, separators=(",", ":"), allow_nan=False
    ).encode("utf-8")


def _file_sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        while block := stream.read(1024 * 1024):
            digest.update(block)
    return digest.hexdigest()


def _fsync_directory(path: Path) -> None:
    descriptor = os.open(path, os.O_RDONLY | getattr(os, "O_DIRECTORY", 0))
    try:
        os.fsync(descriptor)
    finally:
        os.close(descriptor)
