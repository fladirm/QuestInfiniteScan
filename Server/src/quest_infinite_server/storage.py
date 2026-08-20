"""Durable SQLite job state with explicit, transactional transitions."""

from __future__ import annotations

from dataclasses import dataclass
import json
from pathlib import Path
import sqlite3
import threading
import time

from .contracts import (
    MAX_ARTIFACT_BUNDLE_BYTES,
    BlobDescriptor,
    JobState,
    JobStatus,
    JobSubmission,
    transition_allowed,
)


class JobNotFoundError(LookupError):
    pass


class JobConflictError(RuntimeError):
    pass


class JobStateError(RuntimeError):
    pass


@dataclass(frozen=True, slots=True)
class UploadRecord:
    path: Path
    byte_length: int
    sha256: str


def unix_ms() -> int:
    return time.time_ns() // 1_000_000


class JobStore:
    """One process-safe SQLite authority for job metadata.

    Large uploads and artifacts live beside the database; SQLite stores only verified
    descriptors and paths. Every public mutation is one `BEGIN IMMEDIATE` transaction.
    """

    def __init__(self, root: Path | str) -> None:
        self.root = Path(root).resolve()
        self.root.mkdir(parents=True, exist_ok=True)
        self.upload_root = self.root / "uploads"
        self.artifact_root = self.root / "artifacts"
        self.temp_root = self.root / ".tmp"
        for directory in (self.upload_root, self.artifact_root, self.temp_root):
            directory.mkdir(parents=True, exist_ok=True)
        self._gate = threading.RLock()
        self._connection = sqlite3.connect(
            self.root / "jobs.sqlite3",
            isolation_level=None,
            check_same_thread=False,
        )
        self._connection.row_factory = sqlite3.Row
        self._connection.execute("PRAGMA journal_mode=WAL")
        self._connection.execute("PRAGMA synchronous=FULL")
        self._connection.execute("PRAGMA foreign_keys=ON")
        self._connection.execute("PRAGMA busy_timeout=5000")
        self._create_schema()

    def _create_schema(self) -> None:
        with self._gate:
            self._connection.executescript(
                """
                CREATE TABLE IF NOT EXISTS jobs (
                    job_id TEXT PRIMARY KEY,
                    world_id TEXT NOT NULL,
                    chunk_id TEXT NOT NULL,
                    chunk_revision INTEGER NOT NULL,
                    request_fingerprint TEXT NOT NULL,
                    submission_json TEXT NOT NULL,
                    state TEXT NOT NULL,
                    progress REAL NOT NULL,
                    attempt INTEGER NOT NULL,
                    created_unix_ms INTEGER NOT NULL,
                    updated_unix_ms INTEGER NOT NULL,
                    message TEXT NOT NULL,
                    retry_after_ms INTEGER,
                    error_code TEXT,
                    cancel_requested INTEGER NOT NULL DEFAULT 0,
                    upload_path TEXT,
                    upload_byte_length INTEGER,
                    upload_sha256 TEXT,
                    artifact_path TEXT,
                    artifact_json TEXT,
                    UNIQUE(world_id, chunk_id, chunk_revision)
                );
                CREATE INDEX IF NOT EXISTS jobs_state_order
                    ON jobs(state, updated_unix_ms, created_unix_ms);
                """
            )

    def close(self) -> None:
        with self._gate:
            self._connection.close()

    def create_or_replay(
        self, submission: JobSubmission, now: int | None = None
    ) -> tuple[JobStatus, bool]:
        timestamp = unix_ms() if now is None else now
        encoded = json.dumps(
            submission.to_wire(), sort_keys=True, separators=(",", ":"), allow_nan=False
        )
        with self._transaction():
            row = self._connection.execute(
                "SELECT * FROM jobs WHERE job_id = ?", (submission.job_id,)
            ).fetchone()
            if row is not None:
                if row["request_fingerprint"] != submission.request_fingerprint:
                    raise JobConflictError(
                        "job identity already exists with a different request fingerprint"
                    )
                return self._status(row), False
            self._connection.execute(
                """
                INSERT INTO jobs (
                    job_id, world_id, chunk_id, chunk_revision,
                    request_fingerprint, submission_json, state, progress, attempt,
                    created_unix_ms, updated_unix_ms, message
                ) VALUES (?, ?, ?, ?, ?, ?, ?, 0.0, 0, ?, ?, ?)
                """,
                (
                    submission.job_id,
                    submission.key.world_id,
                    submission.key.chunk_id,
                    submission.key.chunk_revision,
                    submission.request_fingerprint,
                    encoded,
                    JobState.AWAITING_UPLOAD.value,
                    timestamp,
                    timestamp,
                    "awaiting verified input upload",
                ),
            )
            row = self._required_row(submission.job_id)
            return self._status(row), True

    def get_submission(self, job_id: str) -> JobSubmission:
        with self._gate:
            row = self._connection.execute(
                "SELECT submission_json FROM jobs WHERE job_id = ?", (job_id,)
            ).fetchone()
        if row is None:
            raise JobNotFoundError(job_id)
        return JobSubmission.from_wire(json.loads(row["submission_json"]))

    def get_status(self, job_id: str) -> JobStatus:
        with self._gate:
            row = self._connection.execute(
                "SELECT * FROM jobs WHERE job_id = ?", (job_id,)
            ).fetchone()
        if row is None:
            raise JobNotFoundError(job_id)
        return self._status(row)

    def record_upload(
        self, job_id: str, path: Path, byte_length: int, sha256: str
    ) -> JobStatus:
        with self._transaction():
            row = self._required_row(job_id)
            submission = JobSubmission.from_wire(json.loads(row["submission_json"]))
            descriptor = submission.input_bundle
            if descriptor.byte_length != byte_length or descriptor.sha256 != sha256:
                raise JobConflictError("verified upload does not match its immutable descriptor")
            if row["upload_path"] is not None:
                if (
                    row["upload_path"] != str(path)
                    or row["upload_byte_length"] != byte_length
                    or row["upload_sha256"] != sha256
                ):
                    raise JobConflictError("a different upload is already registered")
                return self._status(row)
            if JobState(row["state"]) != JobState.AWAITING_UPLOAD:
                raise JobStateError("input can only be registered while awaiting upload")
            timestamp = max(unix_ms(), row["updated_unix_ms"])
            self._connection.execute(
                """
                UPDATE jobs SET upload_path = ?, upload_byte_length = ?, upload_sha256 = ?,
                    updated_unix_ms = ?, message = ? WHERE job_id = ?
                """,
                (str(path), byte_length, sha256, timestamp, "input verified", job_id),
            )
            return self._status(self._required_row(job_id))

    def get_upload(self, job_id: str) -> UploadRecord | None:
        with self._gate:
            row = self._connection.execute(
                """SELECT upload_path, upload_byte_length, upload_sha256
                   FROM jobs WHERE job_id = ?""",
                (job_id,),
            ).fetchone()
        if row is None:
            raise JobNotFoundError(job_id)
        if row["upload_path"] is None:
            return None
        return UploadRecord(
            Path(row["upload_path"]), row["upload_byte_length"], row["upload_sha256"]
        )

    def enqueue(self, job_id: str) -> JobStatus:
        with self._transaction():
            row = self._required_row(job_id)
            state = JobState(row["state"])
            if state in (JobState.QUEUED, JobState.RUNNING, JobState.SUCCEEDED):
                return self._status(row)
            if state == JobState.CANCELED:
                raise JobStateError("a canceled job is immutable")
            if row["upload_path"] is None:
                raise JobStateError("a verified upload is required before enqueue")
            if not transition_allowed(state, JobState.QUEUED):
                raise JobStateError(f"cannot enqueue job from {state.value}")
            timestamp = max(unix_ms(), row["updated_unix_ms"])
            self._connection.execute(
                """
                UPDATE jobs SET state = ?, progress = 0.0, attempt = attempt + 1,
                    updated_unix_ms = ?, message = ?, retry_after_ms = NULL,
                    error_code = NULL, cancel_requested = 0
                WHERE job_id = ?
                """,
                (JobState.QUEUED.value, timestamp, "queued", job_id),
            )
            return self._status(self._required_row(job_id))

    def claim_next(self) -> tuple[JobSubmission, UploadRecord] | None:
        with self._transaction():
            row = self._connection.execute(
                """
                SELECT * FROM jobs WHERE state = ? AND cancel_requested = 0
                ORDER BY updated_unix_ms, created_unix_ms, job_id LIMIT 1
                """,
                (JobState.QUEUED.value,),
            ).fetchone()
            if row is None:
                return None
            if row["upload_path"] is None:
                self._connection.execute(
                    """UPDATE jobs SET state = ?, error_code = ?, message = ?
                       WHERE job_id = ?""",
                    (
                        JobState.FAILED.value,
                        "missing_upload",
                        "durable upload record is missing",
                        row["job_id"],
                    ),
                )
                return None
            timestamp = max(unix_ms(), row["updated_unix_ms"])
            self._connection.execute(
                """UPDATE jobs SET state = ?, updated_unix_ms = ?, message = ?
                   WHERE job_id = ?""",
                (JobState.RUNNING.value, timestamp, "running", row["job_id"]),
            )
            submission = JobSubmission.from_wire(json.loads(row["submission_json"]))
            upload = UploadRecord(
                Path(row["upload_path"]),
                row["upload_byte_length"],
                row["upload_sha256"],
            )
            return submission, upload

    def update_progress(self, job_id: str, progress: float, message: str) -> None:
        with self._transaction():
            row = self._required_row(job_id)
            if JobState(row["state"]) != JobState.RUNNING:
                raise JobStateError("only a running job can report progress")
            timestamp = max(unix_ms(), row["updated_unix_ms"])
            self._connection.execute(
                """UPDATE jobs SET progress = ?, updated_unix_ms = ?, message = ?
                   WHERE job_id = ?""",
                (max(0.0, min(1.0, float(progress))), timestamp, message[:1024], job_id),
            )

    def complete(
        self, job_id: str, artifact_path: Path, descriptor: BlobDescriptor
    ) -> JobStatus:
        with self._transaction():
            row = self._required_row(job_id)
            if JobState(row["state"]) != JobState.RUNNING:
                raise JobStateError("only a running job can complete")
            timestamp = max(unix_ms(), row["updated_unix_ms"])
            artifact_json = json.dumps(
                descriptor.to_wire(), sort_keys=True, separators=(",", ":")
            )
            self._connection.execute(
                """
                UPDATE jobs SET state = ?, progress = 1.0, updated_unix_ms = ?,
                    message = ?, artifact_path = ?, artifact_json = ?, error_code = NULL,
                    cancel_requested = 0 WHERE job_id = ?
                """,
                (
                    JobState.SUCCEEDED.value,
                    timestamp,
                    "succeeded",
                    str(artifact_path),
                    artifact_json,
                    job_id,
                ),
            )
            return self._status(self._required_row(job_id))

    def fail(self, job_id: str, error_code: str, message: str) -> JobStatus:
        with self._transaction():
            row = self._required_row(job_id)
            state = JobState(row["state"])
            if state.terminal:
                return self._status(row)
            timestamp = max(unix_ms(), row["updated_unix_ms"])
            self._connection.execute(
                """
                UPDATE jobs SET state = ?, updated_unix_ms = ?, message = ?,
                    error_code = ?, cancel_requested = 0 WHERE job_id = ?
                """,
                (JobState.FAILED.value, timestamp, message[:1024], error_code, job_id),
            )
            return self._status(self._required_row(job_id))

    def cancel(self, job_id: str) -> JobStatus:
        with self._transaction():
            row = self._required_row(job_id)
            state = JobState(row["state"])
            if state in (JobState.SUCCEEDED, JobState.CANCELED, JobState.FAILED):
                return self._status(row)
            timestamp = max(unix_ms(), row["updated_unix_ms"])
            if state == JobState.RUNNING:
                self._connection.execute(
                    """UPDATE jobs SET cancel_requested = 1, updated_unix_ms = ?,
                       message = ? WHERE job_id = ?""",
                    (timestamp, "cancellation requested", job_id),
                )
            else:
                self._connection.execute(
                    """UPDATE jobs SET state = ?, cancel_requested = 0,
                       updated_unix_ms = ?, message = ? WHERE job_id = ?""",
                    (JobState.CANCELED.value, timestamp, "canceled", job_id),
                )
            return self._status(self._required_row(job_id))

    def is_cancel_requested(self, job_id: str) -> bool:
        with self._gate:
            row = self._connection.execute(
                "SELECT cancel_requested FROM jobs WHERE job_id = ?", (job_id,)
            ).fetchone()
        if row is None:
            raise JobNotFoundError(job_id)
        return bool(row["cancel_requested"])

    def acknowledge_running_cancel(self, job_id: str) -> JobStatus:
        with self._transaction():
            row = self._required_row(job_id)
            if JobState(row["state"]) != JobState.RUNNING or not row["cancel_requested"]:
                return self._status(row)
            timestamp = max(unix_ms(), row["updated_unix_ms"])
            self._connection.execute(
                """UPDATE jobs SET state = ?, cancel_requested = 0,
                   updated_unix_ms = ?, message = ? WHERE job_id = ?""",
                (JobState.CANCELED.value, timestamp, "canceled", job_id),
            )
            return self._status(self._required_row(job_id))

    def recover_interrupted(self) -> int:
        with self._transaction():
            count = self._connection.execute(
                "SELECT COUNT(*) FROM jobs WHERE state = ?",
                (JobState.RUNNING.value,),
            ).fetchone()[0]
            if count:
                timestamp = unix_ms()
                self._connection.execute(
                    """
                    UPDATE jobs SET state = ?, progress = 0.0, updated_unix_ms = ?,
                        message = ?, cancel_requested = 0 WHERE state = ?
                    """,
                    (
                        JobState.QUEUED.value,
                        timestamp,
                        "recovered after server restart",
                        JobState.RUNNING.value,
                    ),
                )
            return count

    def artifact_path(self, job_id: str) -> Path:
        with self._gate:
            row = self._connection.execute(
                "SELECT state, artifact_path FROM jobs WHERE job_id = ?", (job_id,)
            ).fetchone()
        if row is None:
            raise JobNotFoundError(job_id)
        if JobState(row["state"]) != JobState.SUCCEEDED or row["artifact_path"] is None:
            raise JobStateError("job has no successful artifact")
        return Path(row["artifact_path"])

    def _required_row(self, job_id: str) -> sqlite3.Row:
        row = self._connection.execute(
            "SELECT * FROM jobs WHERE job_id = ?", (job_id,)
        ).fetchone()
        if row is None:
            raise JobNotFoundError(job_id)
        return row

    def _status(self, row: sqlite3.Row) -> JobStatus:
        submission = JobSubmission.from_wire(json.loads(row["submission_json"]))
        artifact = (
            None
            if row["artifact_json"] is None
            else BlobDescriptor.from_wire(
                json.loads(row["artifact_json"]),
                "$.artifactBundle",
                maximum_bytes=MAX_ARTIFACT_BUNDLE_BYTES,
            )
        )
        return JobStatus(
            key=submission.key,
            request_fingerprint=row["request_fingerprint"],
            state=JobState(row["state"]),
            progress=row["progress"],
            attempt=row["attempt"],
            created_unix_ms=row["created_unix_ms"],
            updated_unix_ms=row["updated_unix_ms"],
            message=row["message"],
            retry_after_ms=row["retry_after_ms"],
            artifact_bundle=artifact,
            error_code=row["error_code"],
        )

    class _Transaction:
        def __init__(self, store: JobStore) -> None:
            self.store = store

        def __enter__(self) -> None:
            self.store._gate.acquire()
            try:
                self.store._connection.execute("BEGIN IMMEDIATE")
            except BaseException:
                self.store._gate.release()
                raise

        def __exit__(self, exception_type, exception, traceback) -> None:
            try:
                self.store._connection.execute(
                    "COMMIT" if exception_type is None else "ROLLBACK"
                )
            finally:
                self.store._gate.release()

    def _transaction(self) -> JobStore._Transaction:
        return self._Transaction(self)
