"""FastAPI surface for the versioned durable LAN service."""

from __future__ import annotations

import asyncio
from contextlib import asynccontextmanager
from dataclasses import dataclass
import hashlib
import os
from pathlib import Path
import re
from typing import Any, AsyncIterator
import uuid

from fastapi import FastAPI, Request
from fastapi.responses import FileResponse, JSONResponse

from .backend import ComputeBackend, FakeDiffSoupBackend
from .bundle import BundleValidationError, validate_chunk_bundle
from .contracts import (
    ARTIFACT_FORMAT_VERSION,
    CHUNK_BUNDLE_FORMAT_VERSION,
    MAX_ARTIFACT_BUNDLE_BYTES,
    MAX_UPLOAD_BYTES,
    PROTOCOL_VERSION,
    ContractError,
    JobSubmission,
)
from .scheduler import JobScheduler
from .storage import (
    JobConflictError,
    JobNotFoundError,
    JobStateError,
    JobStore,
)


_JOB_ID = re.compile(r"^[0-9a-f]{64}$")


@dataclass(frozen=True, slots=True)
class ServerConfig:
    data_root: Path
    maximum_upload_bytes: int = MAX_UPLOAD_BYTES

    @classmethod
    def from_environment(cls) -> ServerConfig:
        configured = os.environ.get("QIS_SERVER_DATA_ROOT")
        root = (
            Path(configured)
            if configured
            else Path.home() / ".local" / "share" / "quest-infinite-server"
        )
        return cls(root.resolve())


class ApiError(RuntimeError):
    def __init__(self, status: int, code: str, message: str, path: str | None = None) -> None:
        self.status = status
        self.code = code
        self.message = message
        self.path = path
        super().__init__(message)


class ServerService:
    def __init__(self, config: ServerConfig, backend: ComputeBackend) -> None:
        self.config = config
        self.store = JobStore(config.data_root)
        self.backend = backend
        self.scheduler = JobScheduler(self.store, backend)
        self._upload_locks: dict[str, asyncio.Lock] = {}
        self._upload_locks_gate = asyncio.Lock()

    async def start(self) -> int:
        self._remove_orphan_temporaries()
        return await self.scheduler.start()

    async def stop(self) -> None:
        await self.scheduler.stop()
        self.store.close()

    async def upload_lock(self, job_id: str) -> asyncio.Lock:
        async with self._upload_locks_gate:
            return self._upload_locks.setdefault(job_id, asyncio.Lock())

    def _remove_orphan_temporaries(self) -> None:
        # Only server-owned, specifically named temporary files are in scope.
        for candidate in self.store.temp_root.glob("upload-*.tmp"):
            if candidate.is_file():
                candidate.unlink(missing_ok=True)
        for candidate in self.store.temp_root.glob("artifact-*.tmp"):
            if candidate.is_file():
                candidate.unlink(missing_ok=True)


def create_app(
    config: ServerConfig | None = None,
    backend: ComputeBackend | None = None,
) -> FastAPI:
    resolved_config = config or ServerConfig.from_environment()
    resolved_backend = backend or _backend_from_environment()
    service = ServerService(resolved_config, resolved_backend)

    @asynccontextmanager
    async def lifespan(app: FastAPI) -> AsyncIterator[None]:
        recovered = await service.start()
        app.state.recovered_jobs = recovered
        try:
            yield
        finally:
            await service.stop()

    app = FastAPI(
        title="QuestInfiniteScan local compute server",
        version="2.0.0",
        lifespan=lifespan,
    )
    app.state.service = service

    @app.exception_handler(ApiError)
    async def api_error_handler(_: Request, exception: ApiError) -> JSONResponse:
        return JSONResponse(
            status_code=exception.status,
            content={
                "schemaVersion": PROTOCOL_VERSION,
                "error": {
                    "code": exception.code,
                    "message": exception.message,
                    "path": exception.path,
                },
            },
        )

    @app.get("/v2/capabilities")
    async def capabilities() -> dict[str, Any]:
        return {
            "schemaVersion": PROTOCOL_VERSION,
            "protocolVersions": [PROTOCOL_VERSION],
            "chunkBundleFormatVersions": [CHUNK_BUNDLE_FORMAT_VERSION],
            "diffSoupArtifactFormatVersions": [ARTIFACT_FORMAT_VERSION],
            "backends": [service.backend.name],
            "profiles": ["preview", "balanced", "quality"],
            "maximumUploadBytes": service.config.maximum_upload_bytes,
            "maximumArtifactBytes": MAX_ARTIFACT_BUNDLE_BYTES,
            "supportsCancel": True,
            "supportsRetry": True,
            "supportsWarmStart": service.backend.name == "diffsoup",
        }

    @app.put("/v2/jobs/{job_id}")
    async def create_job(job_id: str, request: Request) -> JSONResponse:
        _validate_job_id(job_id)
        try:
            value = await request.json()
            submission = JobSubmission.from_wire(value)
        except ContractError as exception:
            raise ApiError(422, "invalid_contract", exception.message, exception.path) from exception
        except Exception as exception:
            raise ApiError(400, "invalid_json", "request body is not valid JSON") from exception
        if submission.job_id != job_id:
            raise ApiError(409, "job_id_mismatch", "URL job ID does not match the body")
        try:
            status, created = service.store.create_or_replay(submission)
        except JobConflictError as exception:
            raise ApiError(409, "idempotency_conflict", str(exception)) from exception
        return JSONResponse(status_code=201 if created else 200, content=status.to_wire())

    @app.put("/v2/jobs/{job_id}/input")
    async def upload_input(job_id: str, request: Request) -> JSONResponse:
        _validate_job_id(job_id)
        try:
            submission = service.store.get_submission(job_id)
        except JobNotFoundError as exception:
            raise ApiError(404, "job_not_found", "job does not exist") from exception
        descriptor = submission.input_bundle
        if descriptor.byte_length > service.config.maximum_upload_bytes:
            raise ApiError(413, "upload_too_large", "declared upload exceeds server limit")
        content_type = request.headers.get("content-type", "").split(";", 1)[0].strip().lower()
        if content_type != descriptor.media_type:
            raise ApiError(
                415,
                "unsupported_media_type",
                "Content-Type does not match the immutable upload descriptor",
            )
        content_length = request.headers.get("content-length")
        if content_length is not None:
            try:
                stated_length = int(content_length)
            except ValueError as exception:
                raise ApiError(400, "invalid_content_length", "Content-Length is invalid") from exception
            if stated_length != descriptor.byte_length:
                raise ApiError(
                    409,
                    "content_length_mismatch",
                    "Content-Length does not match the immutable upload descriptor",
                )
        lock = await service.upload_lock(job_id)
        async with lock:
            existing = service.store.get_upload(job_id)
            if existing is not None:
                matches = await asyncio.to_thread(
                    _verified_file_matches,
                    existing.path,
                    existing.byte_length,
                    existing.sha256,
                )
                if matches:
                    return JSONResponse(
                        status_code=200,
                        content=service.store.get_status(job_id).to_wire(),
                    )
                if service.store.get_status(job_id).state.value != "awaiting_upload":
                    raise ApiError(
                        409,
                        "stored_upload_corrupt",
                        "registered upload is unavailable after the job was enqueued",
                    )
            temporary = service.store.temp_root / f"upload-{job_id}-{uuid.uuid4().hex}.tmp"
            final_path: Path | None = None
            try:
                digest = hashlib.sha256()
                received = 0
                with temporary.open("xb") as stream:
                    async for block in request.stream():
                        if not block:
                            continue
                        received += len(block)
                        if received > descriptor.byte_length:
                            raise ApiError(413, "upload_too_large", "upload exceeds declared size")
                        stream.write(block)
                        digest.update(block)
                    stream.flush()
                    os.fsync(stream.fileno())
                if received != descriptor.byte_length:
                    raise ApiError(422, "upload_truncated", "upload ended before declared size")
                actual_digest = digest.hexdigest()
                if actual_digest != descriptor.sha256:
                    raise ApiError(422, "upload_hash_mismatch", "upload SHA-256 does not match")
                try:
                    await asyncio.to_thread(validate_chunk_bundle, temporary, submission.key)
                except BundleValidationError as exception:
                    raise ApiError(422, "invalid_chunk_bundle", str(exception)) from exception
                final_path = service.store.upload_root / f"{job_id}.zip"
                os.replace(temporary, final_path)
                _fsync_directory(final_path.parent)
                try:
                    status = service.store.record_upload(
                        job_id, final_path, received, actual_digest
                    )
                except (JobConflictError, JobStateError) as exception:
                    if existing is None:
                        final_path.unlink(missing_ok=True)
                    raise ApiError(409, "upload_conflict", str(exception)) from exception
                return JSONResponse(status_code=200, content=status.to_wire())
            finally:
                temporary.unlink(missing_ok=True)

    @app.post("/v2/jobs/{job_id}/enqueue")
    async def enqueue(job_id: str) -> dict[str, Any]:
        _validate_job_id(job_id)
        try:
            status = service.store.enqueue(job_id)
        except JobNotFoundError as exception:
            raise ApiError(404, "job_not_found", "job does not exist") from exception
        except JobStateError as exception:
            raise ApiError(409, "invalid_job_state", str(exception)) from exception
        service.scheduler.notify()
        return status.to_wire()

    @app.get("/v2/jobs/{job_id}")
    async def status(job_id: str) -> dict[str, Any]:
        _validate_job_id(job_id)
        try:
            return service.store.get_status(job_id).to_wire()
        except JobNotFoundError as exception:
            raise ApiError(404, "job_not_found", "job does not exist") from exception

    @app.post("/v2/jobs/{job_id}/cancel")
    async def cancel(job_id: str) -> dict[str, Any]:
        _validate_job_id(job_id)
        try:
            result = service.store.cancel(job_id)
        except JobNotFoundError as exception:
            raise ApiError(404, "job_not_found", "job does not exist") from exception
        service.scheduler.notify()
        return result.to_wire()

    @app.get("/v2/jobs/{job_id}/artifact")
    async def artifact(job_id: str) -> FileResponse:
        _validate_job_id(job_id)
        try:
            status_value = service.store.get_status(job_id)
            path = service.store.artifact_path(job_id)
        except JobNotFoundError as exception:
            raise ApiError(404, "job_not_found", "job does not exist") from exception
        except JobStateError as exception:
            raise ApiError(409, "artifact_not_ready", str(exception)) from exception
        descriptor = status_value.artifact_bundle
        if descriptor is None or not path.is_file() or path.stat().st_size != descriptor.byte_length:
            raise ApiError(500, "artifact_missing", "durable artifact is unavailable")
        return FileResponse(
            path,
            media_type=descriptor.media_type,
            filename=f"{job_id}.diffsoup.zip",
            headers={
                "X-QIS-Format-Version": str(descriptor.format_version),
                "X-QIS-SHA256": descriptor.sha256,
                "ETag": f'"sha256:{descriptor.sha256}"',
            },
        )

    return app


def _backend_from_environment() -> ComputeBackend:
    name = os.environ.get("QIS_COMPUTE_BACKEND", "fake").strip().lower()
    if name == "fake":
        return FakeDiffSoupBackend()
    if name == "diffsoup":
        # Imported only when explicitly selected so API/contract tests and machines
        # without a CUDA worker environment remain lightweight.
        from .process_backend import DiffSoupProcessBackend, DiffSoupProcessConfig

        return DiffSoupProcessBackend(DiffSoupProcessConfig.from_environment())
    raise RuntimeError("QIS_COMPUTE_BACKEND must be 'fake' or 'diffsoup'")


def _validate_job_id(job_id: str) -> None:
    if not _JOB_ID.fullmatch(job_id):
        raise ApiError(400, "invalid_job_id", "job ID must be a lowercase SHA-256 digest")


def _fsync_directory(path: Path) -> None:
    descriptor = os.open(path, os.O_RDONLY | getattr(os, "O_DIRECTORY", 0))
    try:
        os.fsync(descriptor)
    finally:
        os.close(descriptor)


def _verified_file_matches(path: Path, byte_length: int, expected_sha256: str) -> bool:
    try:
        if not path.is_file() or path.stat().st_size != byte_length:
            return False
        digest = hashlib.sha256()
        with path.open("rb") as stream:
            while block := stream.read(1024 * 1024):
                digest.update(block)
        return digest.hexdigest() == expected_sha256
    except OSError:
        return False
