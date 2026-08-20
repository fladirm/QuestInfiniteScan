"""Subprocess boundary between the durable API service and PyTorch/CUDA."""

from __future__ import annotations

import asyncio
from collections import deque
from dataclasses import dataclass
import json
import os
from pathlib import Path
import shutil
import time
from typing import Any
import uuid

from .artifact import ArtifactValidationError, validate_artifact_bundle
from .backend import (
    BackendContext,
    BackendJobError,
    BackendResult,
    JobCanceledError,
)
from .contracts import BlobDescriptor, ContractError, JobKey
from .storage import JobNotFoundError, JobStateError


@dataclass(frozen=True, slots=True)
class DiffSoupProcessConfig:
    python_executable: Path
    source_commit: str
    maximum_runtime_seconds: int = 24 * 60 * 60

    @classmethod
    def from_environment(cls) -> DiffSoupProcessConfig:
        configured = os.environ.get("QIS_DIFFSOUP_PYTHON")
        if not configured:
            raise RuntimeError(
                "QIS_DIFFSOUP_PYTHON must identify the dedicated CUDA worker interpreter"
            )
        # A venv's ``bin/python`` is normally a symlink to the system interpreter.
        # Resolving it changes sys.prefix and silently drops the venv site-packages
        # (including torch). Keep the launcher path itself while making it absolute.
        python = Path(configured).expanduser().absolute()
        if not python.is_file() or not os.access(python, os.X_OK):
            raise RuntimeError("QIS_DIFFSOUP_PYTHON is not an executable file")
        commit = os.environ.get(
            "QIS_DIFFSOUP_UPSTREAM_COMMIT",
            "c74e35de74ad0116977b23e7951f4cbc25ab0f6b",
        )
        if len(commit) != 40 or any(character not in "0123456789abcdef" for character in commit):
            raise RuntimeError("QIS_DIFFSOUP_UPSTREAM_COMMIT must be a full lowercase SHA-1")
        raw_timeout = os.environ.get("QIS_DIFFSOUP_MAX_RUNTIME_SECONDS", "86400")
        try:
            timeout = int(raw_timeout)
        except ValueError as exception:
            raise RuntimeError("QIS_DIFFSOUP_MAX_RUNTIME_SECONDS is invalid") from exception
        if not 60 <= timeout <= 7 * 24 * 60 * 60:
            raise RuntimeError("QIS_DIFFSOUP_MAX_RUNTIME_SECONDS is outside supported limits")
        return cls(python, commit, timeout)


class DiffSoupProcessBackend:
    name = "diffsoup"

    def __init__(self, config: DiffSoupProcessConfig) -> None:
        self.config = config

    async def run(self, context: BackendContext) -> BackendResult:
        context.report(0.01, "preparing isolated DiffSoup worker")
        work_directory = context.store.temp_root / (
            f"worker-{context.submission.job_id}-{uuid.uuid4().hex}"
        )
        work_directory.mkdir(mode=0o700)
        submission_path = work_directory / "submission.json"
        submission_path.write_text(
            json.dumps(
                context.submission.to_wire(),
                sort_keys=True,
                separators=(",", ":"),
                allow_nan=False,
            ),
            encoding="utf-8",
        )
        submission_path.chmod(0o600)
        output_path = context.store.temp_root / (
            f"artifact-{context.submission.job_id}-{uuid.uuid4().hex}.tmp"
        )
        warm_artifact = self._resolve_warm_artifact(context)
        command = [
            str(self.config.python_executable),
            "-m",
            "quest_infinite_server.diffsoup_worker",
            "run",
            "--submission",
            str(submission_path),
            "--input",
            str(context.upload.path),
            "--output",
            str(output_path),
            "--work-dir",
            str(work_directory),
        ]
        if warm_artifact is not None:
            command.extend(("--warm-artifact", str(warm_artifact)))
        environment = os.environ.copy()
        package_source = str(Path(__file__).resolve().parents[1])
        existing_python_path = environment.get("PYTHONPATH")
        environment["PYTHONPATH"] = (
            package_source
            if not existing_python_path
            else package_source + os.pathsep + existing_python_path
        )
        environment["QIS_DIFFSOUP_UPSTREAM_COMMIT"] = self.config.source_commit
        result_event: dict[str, Any] | None = None
        error_event: dict[str, Any] | None = None
        stderr_tail: deque[str] = deque(maxlen=80)
        process: asyncio.subprocess.Process | None = None
        started = time.monotonic()
        try:
            process = await asyncio.create_subprocess_exec(
                *command,
                stdin=asyncio.subprocess.DEVNULL,
                stdout=asyncio.subprocess.PIPE,
                stderr=asyncio.subprocess.PIPE,
                env=environment,
                cwd=work_directory,
            )

            async def read_stdout() -> None:
                nonlocal result_event, error_event
                assert process is not None and process.stdout is not None
                while line := await process.stdout.readline():
                    if len(line) > 64 * 1024:
                        error_event = {
                            "code": "invalid_worker_protocol",
                            "message": "worker emitted an oversized protocol line",
                        }
                        continue
                    try:
                        event = json.loads(line.decode("utf-8"))
                    except (UnicodeDecodeError, json.JSONDecodeError):
                        error_event = {
                            "code": "invalid_worker_protocol",
                            "message": "worker emitted non-JSON protocol output",
                        }
                        continue
                    if not isinstance(event, dict):
                        continue
                    kind = event.get("kind")
                    if kind == "progress":
                        progress = event.get("progress")
                        message = event.get("message")
                        if (
                            not isinstance(progress, bool)
                            and isinstance(progress, (int, float))
                            and isinstance(message, str)
                        ):
                            context.report(float(progress), message[:512])
                    elif kind == "result":
                        result_event = event
                    elif kind == "error":
                        error_event = event

            async def read_stderr() -> None:
                assert process is not None and process.stderr is not None
                while line := await process.stderr.readline():
                    stderr_tail.append(line.decode("utf-8", errors="replace")[:2_048])

            stdout_task = asyncio.create_task(read_stdout())
            stderr_task = asyncio.create_task(read_stderr())
            while process.returncode is None:
                if context.store.is_cancel_requested(context.submission.job_id):
                    await _stop_process(process)
                    raise JobCanceledError("job canceled by client")
                if time.monotonic() - started > self.config.maximum_runtime_seconds:
                    await _stop_process(process)
                    raise BackendJobError(
                        "worker_timeout", "DiffSoup worker exceeded its runtime limit"
                    )
                try:
                    await asyncio.wait_for(process.wait(), timeout=0.25)
                except TimeoutError:
                    continue
            await asyncio.gather(stdout_task, stderr_task)
            if process.returncode != 0:
                code = _safe_error_code(error_event, "diffsoup_worker_failed")
                message = _safe_error_message(error_event)
                if not message:
                    message = "DiffSoup worker exited unsuccessfully"
                    if stderr_tail:
                        message += ": " + "".join(stderr_tail)[-2_000:].strip()
                raise BackendJobError(code, message[:4_000])
            if error_event is not None:
                raise BackendJobError(
                    _safe_error_code(error_event, "invalid_worker_protocol"),
                    _safe_error_message(error_event) or "worker reported an error",
                )
            if result_event is None:
                raise BackendJobError(
                    "invalid_worker_protocol", "worker exited without a result event"
                )
            if Path(str(result_event.get("artifactPath", ""))).resolve() != output_path.resolve():
                raise BackendJobError(
                    "invalid_worker_protocol", "worker result references an unexpected path"
                )
            try:
                descriptor = BlobDescriptor.from_wire(
                    result_event.get("descriptor"),
                    "$.worker.descriptor",
                    maximum_bytes=4 * 1024**3,
                )
            except ContractError as exception:
                raise BackendJobError(
                    "invalid_worker_protocol", f"worker descriptor rejected: {exception}"
                ) from exception
            try:
                validate_artifact_bundle(
                    output_path,
                    expected_key=context.submission.key,
                    expected_request_fingerprint=context.submission.request_fingerprint,
                )
            except ArtifactValidationError as exception:
                raise BackendJobError(
                    "invalid_diffsoup_artifact", f"worker artifact rejected: {exception}"
                ) from exception
            if (
                output_path.stat().st_size != descriptor.byte_length
                or _file_sha256(output_path) != descriptor.sha256
            ):
                raise BackendJobError(
                    "invalid_worker_protocol", "worker artifact descriptor is incorrect"
                )
            final_path = context.store.artifact_root / f"{context.submission.job_id}.zip"
            os.replace(output_path, final_path)
            _fsync_directory(final_path.parent)
            context.report(0.99, "DiffSoup artifact validated and committed")
            return BackendResult(final_path, descriptor)
        except asyncio.CancelledError:
            if process is not None and process.returncode is None:
                await _stop_process(process)
            raise
        finally:
            output_path.unlink(missing_ok=True)
            # The exact directory was created above under JobStore.temp_root and contains
            # only this worker's regenerable intermediates.
            if work_directory.parent == context.store.temp_root and work_directory.name.startswith(
                f"worker-{context.submission.job_id}-"
            ):
                shutil.rmtree(work_directory, ignore_errors=True)

    def _resolve_warm_artifact(self, context: BackendContext) -> Path | None:
        warm = context.submission.warm_start
        if warm is None:
            return None
        source_key = JobKey(
            context.submission.key.world_id,
            context.submission.key.chunk_id,
            warm.source_revision,
        )
        try:
            path = context.store.artifact_path(source_key.job_id)
            manifest = validate_artifact_bundle(path, expected_key=source_key)
            checkpoint = next(
                (file for file in manifest.files if file.role == "checkpoint"), None
            )
            compatible = (
                manifest.compatibility_tag == warm.compatibility_tag
                and checkpoint is not None
                and checkpoint.media_type == warm.checkpoint.media_type
                and checkpoint.format_version == warm.checkpoint.format_version
                and checkpoint.byte_length == warm.checkpoint.byte_length
                and checkpoint.sha256 == warm.checkpoint.sha256
            )
            if compatible:
                context.report(0.015, f"verified warm-start revision {warm.source_revision}")
                return path
            reason = "source artifact checkpoint or compatibility tag differs from the request"
        except (JobNotFoundError, JobStateError, ArtifactValidationError) as exception:
            reason = f"source artifact is unavailable or invalid: {exception}"
        if context.submission.allow_fresh_fallback:
            context.report(0.015, "warm-start unavailable; worker will use fresh fallback")
            return None
        raise BackendJobError("warm_start_incompatible", reason)


async def _stop_process(process: asyncio.subprocess.Process) -> None:
    if process.returncode is not None:
        return
    process.terminate()
    try:
        await asyncio.wait_for(process.wait(), timeout=5.0)
    except TimeoutError:
        process.kill()
        await process.wait()


def _safe_error_code(event: dict[str, Any] | None, default: str) -> str:
    if event is None:
        return default
    value = event.get("code")
    if not isinstance(value, str) or not value or len(value) > 64:
        return default
    if any(not (character.islower() or character.isdigit() or character == "_") for character in value):
        return default
    return value


def _safe_error_message(event: dict[str, Any] | None) -> str:
    if event is None:
        return ""
    value = event.get("message")
    return value[:4_000] if isinstance(value, str) else ""


def _file_sha256(path: Path) -> str:
    import hashlib

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
