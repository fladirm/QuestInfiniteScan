"""Strict, dependency-free wire contracts shared by the LAN service and tests.

The wire format intentionally uses camelCase so Unity can deserialize the same JSON
without a naming adapter.  Every parser rejects unknown fields: adding or changing a
wire meaning therefore requires a protocol-version review rather than silently
changing an existing request.
"""

from __future__ import annotations

from dataclasses import dataclass
from enum import StrEnum
import hashlib
import json
import math
import re
from typing import Any, ClassVar, Iterable, Mapping


PROTOCOL_VERSION = 2
CHUNK_BUNDLE_FORMAT_VERSION = 1
ARTIFACT_FORMAT_VERSION = 1

MAX_UPLOAD_BYTES = 8 * 1024**3
MAX_ARTIFACT_BUNDLE_BYTES = 4 * 1024**3
MAX_ARTIFACT_FILE_BYTES = 2 * 1024**3
MAX_ARTIFACT_FILES = 16
MAX_INPUT_FILES = 4_096
MAX_VERTICES = 8_000_000
MAX_FACES = 8_000_000
MAX_LUT_DIMENSION = 8_192
MAX_SUBDIVISION_LEVEL = 8

_IDENTIFIER = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._-]*$")
_SHA256 = re.compile(r"^[0-9a-f]{64}$")
_GIT_COMMIT = re.compile(r"^(?:[0-9a-f]{40}|[0-9a-f]{64})$")
_MEDIA_TYPE = re.compile(
    r"^[a-z0-9][a-z0-9!#$&^_.+-]*/[a-z0-9][a-z0-9!#$&^_.+-]*$"
)
_SAFE_PATH_SEGMENT = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._-]*$")


class ContractError(ValueError):
    """A fail-closed wire validation error with a stable field path."""

    def __init__(self, path: str, message: str) -> None:
        self.path = path
        self.message = message
        super().__init__(f"{path}: {message}")


def _object(
    value: Any,
    path: str,
    *,
    required: Iterable[str],
    optional: Iterable[str] = (),
) -> Mapping[str, Any]:
    if not isinstance(value, Mapping):
        raise ContractError(path, "must be an object")
    required_set = set(required)
    allowed = required_set | set(optional)
    missing = sorted(required_set - set(value))
    if missing:
        raise ContractError(path, "missing fields: " + ", ".join(missing))
    unknown = sorted(set(value) - allowed)
    if unknown:
        raise ContractError(path, "unknown fields: " + ", ".join(unknown))
    return value


def _integer(value: Any, path: str, minimum: int, maximum: int) -> int:
    if isinstance(value, bool) or not isinstance(value, int):
        raise ContractError(path, "must be an integer")
    if value < minimum or value > maximum:
        raise ContractError(path, f"must be in [{minimum}, {maximum}]")
    return value


def _number(value: Any, path: str, minimum: float, maximum: float) -> float:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise ContractError(path, "must be a finite number")
    result = float(value)
    if not math.isfinite(result) or result < minimum or result > maximum:
        raise ContractError(path, f"must be finite and in [{minimum}, {maximum}]")
    return result


def _boolean(value: Any, path: str) -> bool:
    if not isinstance(value, bool):
        raise ContractError(path, "must be a boolean")
    return value


def _string(value: Any, path: str, minimum: int = 1, maximum: int = 256) -> str:
    if not isinstance(value, str) or not minimum <= len(value) <= maximum:
        raise ContractError(path, f"must be a string with length in [{minimum}, {maximum}]")
    return value


def _identifier(value: Any, path: str, maximum: int) -> str:
    result = _string(value, path, maximum=maximum)
    if not _IDENTIFIER.fullmatch(result):
        raise ContractError(path, "contains unsafe characters")
    return result


def _digest(value: Any, path: str) -> str:
    if not isinstance(value, str) or not _SHA256.fullmatch(value):
        raise ContractError(path, "must be a lowercase SHA-256 hex digest")
    return value


def _git_commit(value: Any, path: str) -> str:
    if not isinstance(value, str) or not _GIT_COMMIT.fullmatch(value):
        raise ContractError(path, "must be a full lowercase Git commit hash")
    return value


def _media_type(value: Any, path: str) -> str:
    result = _string(value, path, maximum=128)
    if not _MEDIA_TYPE.fullmatch(result):
        raise ContractError(path, "must be a normalized media type without parameters")
    return result


def _safe_relative_path(value: Any, path: str) -> str:
    result = _string(value, path, maximum=192)
    if result.startswith("/") or "\\" in result:
        raise ContractError(path, "must be a normalized relative POSIX path")
    segments = result.split("/")
    if any(segment in ("", ".", "..") or not _SAFE_PATH_SEGMENT.fullmatch(segment)
           for segment in segments):
        raise ContractError(path, "contains an unsafe path segment")
    return result


def _canonical_json(value: Mapping[str, Any]) -> bytes:
    return json.dumps(
        value,
        ensure_ascii=True,
        allow_nan=False,
        sort_keys=True,
        separators=(",", ":"),
    ).encode("ascii")


def _sha256(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


@dataclass(frozen=True, slots=True)
class JobKey:
    world_id: str
    chunk_id: str
    chunk_revision: int

    def __post_init__(self) -> None:
        _identifier(self.world_id, "$.key.worldId", 96)
        _identifier(self.chunk_id, "$.key.chunkId", 64)
        _integer(self.chunk_revision, "$.key.chunkRevision", 0, 2_147_483_647)

    @property
    def job_id(self) -> str:
        # Length-prefixing makes the identity unambiguous even if identifier rules are
        # relaxed by a future protocol.  The version prefix prevents cross-version reuse.
        identity = (
            f"qis-job-v{PROTOCOL_VERSION}\0"
            f"{len(self.world_id)}:{self.world_id}\0"
            f"{len(self.chunk_id)}:{self.chunk_id}\0"
            f"{self.chunk_revision}"
        )
        return _sha256(identity.encode("utf-8"))

    def to_wire(self) -> dict[str, Any]:
        return {
            "worldId": self.world_id,
            "chunkId": self.chunk_id,
            "chunkRevision": self.chunk_revision,
        }

    @classmethod
    def from_wire(cls, value: Any, path: str = "$.key") -> JobKey:
        obj = _object(
            value,
            path,
            required=("worldId", "chunkId", "chunkRevision"),
        )
        return cls(
            _identifier(obj["worldId"], f"{path}.worldId", 96),
            _identifier(obj["chunkId"], f"{path}.chunkId", 64),
            _integer(obj["chunkRevision"], f"{path}.chunkRevision", 0, 2_147_483_647),
        )


@dataclass(frozen=True, slots=True)
class BlobDescriptor:
    media_type: str
    format_version: int
    byte_length: int
    sha256: str

    def __post_init__(self) -> None:
        _media_type(self.media_type, "$.blob.mediaType")
        _integer(self.format_version, "$.blob.formatVersion", 1, 2_147_483_647)
        _integer(self.byte_length, "$.blob.byteLength", 1, MAX_UPLOAD_BYTES)
        _digest(self.sha256, "$.blob.sha256")

    def to_wire(self) -> dict[str, Any]:
        return {
            "mediaType": self.media_type,
            "formatVersion": self.format_version,
            "byteLength": self.byte_length,
            "sha256": self.sha256,
        }

    @classmethod
    def from_wire(
        cls,
        value: Any,
        path: str,
        *,
        maximum_bytes: int = MAX_UPLOAD_BYTES,
    ) -> BlobDescriptor:
        obj = _object(
            value,
            path,
            required=("mediaType", "formatVersion", "byteLength", "sha256"),
        )
        descriptor = cls(
            _media_type(obj["mediaType"], f"{path}.mediaType"),
            _integer(obj["formatVersion"], f"{path}.formatVersion", 1, 2_147_483_647),
            _integer(obj["byteLength"], f"{path}.byteLength", 1, maximum_bytes),
            _digest(obj["sha256"], f"{path}.sha256"),
        )
        return descriptor


@dataclass(frozen=True, slots=True)
class WarmStart:
    source_revision: int
    compatibility_tag: str
    checkpoint: BlobDescriptor

    def __post_init__(self) -> None:
        _integer(self.source_revision, "$.warmStart.sourceRevision", 0, 2_147_483_647)
        _digest(self.compatibility_tag, "$.warmStart.compatibilityTag")
        if (
            self.checkpoint.media_type
            != "application/vnd.questinfinitescan.diffsoup-checkpoint"
        ):
            raise ContractError(
                "$.warmStart.checkpoint.mediaType", "unsupported checkpoint media type"
            )
        if self.checkpoint.byte_length > MAX_ARTIFACT_BUNDLE_BYTES:
            raise ContractError(
                "$.warmStart.checkpoint.byteLength", "checkpoint exceeds the size limit"
            )

    def to_wire(self) -> dict[str, Any]:
        return {
            "sourceRevision": self.source_revision,
            "compatibilityTag": self.compatibility_tag,
            "checkpoint": self.checkpoint.to_wire(),
        }

    @classmethod
    def from_wire(cls, value: Any, target: JobKey, path: str = "$.warmStart") -> WarmStart:
        obj = _object(
            value,
            path,
            required=("sourceRevision", "compatibilityTag", "checkpoint"),
        )
        source_revision = _integer(
            obj["sourceRevision"], f"{path}.sourceRevision", 0, 2_147_483_647
        )
        if source_revision >= target.chunk_revision:
            raise ContractError(
                f"{path}.sourceRevision", "must be older than the requested chunk revision"
            )
        compatibility_tag = _digest(
            obj["compatibilityTag"], f"{path}.compatibilityTag"
        )
        checkpoint = BlobDescriptor.from_wire(
            obj["checkpoint"], f"{path}.checkpoint", maximum_bytes=MAX_ARTIFACT_BUNDLE_BYTES
        )
        return cls(source_revision, compatibility_tag, checkpoint)


@dataclass(frozen=True, slots=True)
class JobSubmission:
    key: JobKey
    input_bundle: BlobDescriptor
    profile: str = "balanced"
    allow_fresh_fallback: bool = True
    warm_start: WarmStart | None = None

    _PROFILES: ClassVar[frozenset[str]] = frozenset(("preview", "balanced", "quality"))

    def __post_init__(self) -> None:
        if self.input_bundle.media_type != "application/vnd.questinfinitescan.chunk+zip":
            raise ContractError(
                "$.inputBundle.mediaType", "unsupported chunk bundle media type"
            )
        if self.input_bundle.format_version != CHUNK_BUNDLE_FORMAT_VERSION:
            raise ContractError(
                "$.inputBundle.formatVersion", "unsupported chunk bundle format version"
            )
        if self.profile not in self._PROFILES:
            raise ContractError("$.profile", "unsupported optimization profile")
        _boolean(self.allow_fresh_fallback, "$.allowFreshFallback")
        if self.warm_start is not None and (
            self.warm_start.source_revision >= self.key.chunk_revision
        ):
            raise ContractError(
                "$.warmStart.sourceRevision", "must be older than the target revision"
            )

    @property
    def job_id(self) -> str:
        return self.key.job_id

    @property
    def request_fingerprint(self) -> str:
        return _sha256(_canonical_json(self._immutable_wire()))

    def _immutable_wire(self) -> dict[str, Any]:
        return {
            "schemaVersion": PROTOCOL_VERSION,
            "key": self.key.to_wire(),
            "inputBundle": self.input_bundle.to_wire(),
            "backend": "diffsoup",
            "profile": self.profile,
            "allowFreshFallback": self.allow_fresh_fallback,
            "warmStart": self.warm_start.to_wire() if self.warm_start else None,
        }

    def to_wire(self) -> dict[str, Any]:
        result = self._immutable_wire()
        result["jobId"] = self.job_id
        result["requestFingerprint"] = self.request_fingerprint
        return result

    @classmethod
    def from_wire(cls, value: Any, path: str = "$") -> JobSubmission:
        obj = _object(
            value,
            path,
            required=(
                "schemaVersion",
                "jobId",
                "requestFingerprint",
                "key",
                "inputBundle",
                "backend",
                "profile",
                "allowFreshFallback",
                "warmStart",
            ),
        )
        version = _integer(obj["schemaVersion"], "$.schemaVersion", 1, 2_147_483_647)
        if version != PROTOCOL_VERSION:
            raise ContractError("$.schemaVersion", "unsupported protocol version")
        if obj["backend"] != "diffsoup":
            raise ContractError("$.backend", "unsupported backend")
        key = JobKey.from_wire(obj["key"])
        input_bundle = BlobDescriptor.from_wire(obj["inputBundle"], "$.inputBundle")
        warm_start = (
            None
            if obj["warmStart"] is None
            else WarmStart.from_wire(obj["warmStart"], key)
        )
        submission = cls(
            key=key,
            input_bundle=input_bundle,
            profile=_string(obj["profile"], "$.profile", maximum=32),
            allow_fresh_fallback=_boolean(
                obj["allowFreshFallback"], "$.allowFreshFallback"
            ),
            warm_start=warm_start,
        )
        if _digest(obj["jobId"], "$.jobId") != submission.job_id:
            raise ContractError("$.jobId", "does not match the deterministic job key")
        if (
            _digest(obj["requestFingerprint"], "$.requestFingerprint")
            != submission.request_fingerprint
        ):
            raise ContractError(
                "$.requestFingerprint", "does not match the immutable request fields"
            )
        return submission


class JobState(StrEnum):
    AWAITING_UPLOAD = "awaiting_upload"
    QUEUED = "queued"
    RUNNING = "running"
    SUCCEEDED = "succeeded"
    FAILED = "failed"
    CANCELED = "canceled"

    @property
    def terminal(self) -> bool:
        return self in (self.SUCCEEDED, self.FAILED, self.CANCELED)


_ALLOWED_TRANSITIONS: dict[JobState, frozenset[JobState]] = {
    JobState.AWAITING_UPLOAD: frozenset((JobState.QUEUED, JobState.CANCELED)),
    JobState.QUEUED: frozenset((JobState.RUNNING, JobState.CANCELED)),
    JobState.RUNNING: frozenset(
        (JobState.SUCCEEDED, JobState.FAILED, JobState.CANCELED, JobState.QUEUED)
    ),
    JobState.SUCCEEDED: frozenset(),
    JobState.FAILED: frozenset((JobState.QUEUED,)),
    JobState.CANCELED: frozenset(),
}


def transition_allowed(source: JobState, target: JobState) -> bool:
    """True for a real state transition; replaying the same state is idempotent."""

    return source == target or target in _ALLOWED_TRANSITIONS[source]


@dataclass(frozen=True, slots=True)
class JobStatus:
    key: JobKey
    request_fingerprint: str
    state: JobState
    progress: float
    attempt: int
    created_unix_ms: int
    updated_unix_ms: int
    message: str = ""
    retry_after_ms: int | None = None
    artifact_bundle: BlobDescriptor | None = None
    error_code: str | None = None

    def __post_init__(self) -> None:
        _digest(self.request_fingerprint, "$.requestFingerprint")
        _number(self.progress, "$.progress", 0.0, 1.0)
        _integer(self.attempt, "$.attempt", 0, 1_000_000)
        _integer(self.created_unix_ms, "$.createdUnixMs", 0, 9_223_372_036_854_775_807)
        _integer(self.updated_unix_ms, "$.updatedUnixMs", 0, 9_223_372_036_854_775_807)
        if self.updated_unix_ms < self.created_unix_ms:
            raise ContractError("$.updatedUnixMs", "cannot precede creation")
        _string(self.message, "$.message", minimum=0, maximum=1024)
        if self.retry_after_ms is not None:
            _integer(self.retry_after_ms, "$.retryAfterMs", 0, 86_400_000)
        if self.state == JobState.SUCCEEDED and self.artifact_bundle is None:
            raise ContractError("$.artifactBundle", "is required for a succeeded job")
        if self.state != JobState.SUCCEEDED and self.artifact_bundle is not None:
            raise ContractError("$.artifactBundle", "is allowed only for a succeeded job")
        if (
            self.artifact_bundle is not None
            and self.artifact_bundle.byte_length > MAX_ARTIFACT_BUNDLE_BYTES
        ):
            raise ContractError("$.artifactBundle.byteLength", "artifact exceeds the size limit")
        if self.state == JobState.FAILED and self.error_code is None:
            raise ContractError("$.errorCode", "is required for a failed job")
        if self.state != JobState.FAILED and self.error_code is not None:
            raise ContractError("$.errorCode", "is allowed only for a failed job")
        if self.error_code is not None:
            _identifier(self.error_code, "$.errorCode", 64)

    @property
    def job_id(self) -> str:
        return self.key.job_id

    def to_wire(self) -> dict[str, Any]:
        return {
            "schemaVersion": PROTOCOL_VERSION,
            "jobId": self.job_id,
            "requestFingerprint": self.request_fingerprint,
            "key": self.key.to_wire(),
            "state": self.state.value,
            "progress": self.progress,
            "attempt": self.attempt,
            "createdUnixMs": self.created_unix_ms,
            "updatedUnixMs": self.updated_unix_ms,
            "message": self.message,
            "retryAfterMs": self.retry_after_ms,
            "artifactBundle": (
                self.artifact_bundle.to_wire() if self.artifact_bundle else None
            ),
            "errorCode": self.error_code,
        }

    @classmethod
    def from_wire(cls, value: Any, path: str = "$") -> JobStatus:
        obj = _object(
            value,
            path,
            required=(
                "schemaVersion",
                "jobId",
                "requestFingerprint",
                "key",
                "state",
                "progress",
                "attempt",
                "createdUnixMs",
                "updatedUnixMs",
                "message",
                "retryAfterMs",
                "artifactBundle",
                "errorCode",
            ),
        )
        if _integer(obj["schemaVersion"], "$.schemaVersion", 1, 2_147_483_647) != PROTOCOL_VERSION:
            raise ContractError("$.schemaVersion", "unsupported protocol version")
        key = JobKey.from_wire(obj["key"])
        if _digest(obj["jobId"], "$.jobId") != key.job_id:
            raise ContractError("$.jobId", "does not match the deterministic job key")
        try:
            state = JobState(obj["state"])
        except (TypeError, ValueError) as exception:
            raise ContractError("$.state", "unsupported job state") from exception
        artifact = (
            None
            if obj["artifactBundle"] is None
            else BlobDescriptor.from_wire(
                obj["artifactBundle"],
                "$.artifactBundle",
                maximum_bytes=MAX_ARTIFACT_BUNDLE_BYTES,
            )
        )
        retry = (
            None
            if obj["retryAfterMs"] is None
            else _integer(obj["retryAfterMs"], "$.retryAfterMs", 0, 86_400_000)
        )
        error_code = (
            None
            if obj["errorCode"] is None
            else _identifier(obj["errorCode"], "$.errorCode", 64)
        )
        return cls(
            key=key,
            request_fingerprint=_digest(
                obj["requestFingerprint"], "$.requestFingerprint"
            ),
            state=state,
            progress=_number(obj["progress"], "$.progress", 0.0, 1.0),
            attempt=_integer(obj["attempt"], "$.attempt", 0, 1_000_000),
            created_unix_ms=_integer(
                obj["createdUnixMs"], "$.createdUnixMs", 0, 9_223_372_036_854_775_807
            ),
            updated_unix_ms=_integer(
                obj["updatedUnixMs"], "$.updatedUnixMs", 0, 9_223_372_036_854_775_807
            ),
            message=_string(obj["message"], "$.message", minimum=0, maximum=1024),
            retry_after_ms=retry,
            artifact_bundle=artifact,
            error_code=error_code,
        )


@dataclass(frozen=True, slots=True)
class ArtifactFile:
    role: str
    path: str
    media_type: str
    format_version: int
    byte_length: int
    sha256: str

    _ROLES: ClassVar[frozenset[str]] = frozenset(
        ("mesh", "lut0", "lut1", "mlp", "meta", "checkpoint")
    )

    def __post_init__(self) -> None:
        if self.role not in self._ROLES:
            raise ContractError("$.files[].role", "unsupported artifact role")
        _safe_relative_path(self.path, "$.files[].path")
        _media_type(self.media_type, "$.files[].mediaType")
        _integer(self.format_version, "$.files[].formatVersion", 1, 2_147_483_647)
        _integer(self.byte_length, "$.files[].byteLength", 1, MAX_ARTIFACT_FILE_BYTES)
        _digest(self.sha256, "$.files[].sha256")

    def to_wire(self) -> dict[str, Any]:
        return {
            "role": self.role,
            "path": self.path,
            "mediaType": self.media_type,
            "formatVersion": self.format_version,
            "byteLength": self.byte_length,
            "sha256": self.sha256,
        }

    @classmethod
    def from_wire(cls, value: Any, path: str) -> ArtifactFile:
        obj = _object(
            value,
            path,
            required=(
                "role", "path", "mediaType", "formatVersion", "byteLength", "sha256"
            ),
        )
        return cls(
            role=_string(obj["role"], f"{path}.role", maximum=32),
            path=_safe_relative_path(obj["path"], f"{path}.path"),
            media_type=_media_type(obj["mediaType"], f"{path}.mediaType"),
            format_version=_integer(
                obj["formatVersion"], f"{path}.formatVersion", 1, 2_147_483_647
            ),
            byte_length=_integer(
                obj["byteLength"], f"{path}.byteLength", 1, MAX_ARTIFACT_FILE_BYTES
            ),
            sha256=_digest(obj["sha256"], f"{path}.sha256"),
        )


@dataclass(frozen=True, slots=True)
class ChunkBundleFile:
    role: str
    path: str
    media_type: str
    byte_length: int
    sha256: str

    _ROLES: ClassVar[frozenset[str]] = frozenset(
        ("refined_mesh", "live_mesh", "keyframe_manifest", "keyframe_image", "depth")
    )

    def __post_init__(self) -> None:
        if self.role not in self._ROLES:
            raise ContractError("$.files[].role", "unsupported chunk-bundle role")
        _safe_relative_path(self.path, "$.files[].path")
        _media_type(self.media_type, "$.files[].mediaType")
        _integer(self.byte_length, "$.files[].byteLength", 1, MAX_ARTIFACT_FILE_BYTES)
        _digest(self.sha256, "$.files[].sha256")
        if self.role == "refined_mesh" and (
            self.path != "mesh/refined_mesh.qirm"
            or self.media_type != "application/vnd.questinfinitescan.refined-mesh"
        ):
            raise ContractError("$.files[]", "refined mesh path or media type is non-canonical")
        if self.role == "live_mesh" and (
            self.path != "mesh/live_mesh.qism"
            or self.media_type != "application/vnd.questinfinitescan.live-mesh"
        ):
            raise ContractError("$.files[]", "live mesh path or media type is non-canonical")
        if self.role == "keyframe_manifest" and (
            self.path != "keyframes/frames.jsonl"
            or self.media_type != "application/x-ndjson"
        ):
            raise ContractError(
                "$.files[]", "keyframe manifest path or media type is non-canonical"
            )
        if self.role == "keyframe_image" and (
            not self.path.startswith("keyframes/images/")
            or not self.path.lower().endswith((".jpg", ".jpeg"))
            or self.media_type != "image/jpeg"
        ):
            raise ContractError("$.files[]", "keyframe image path or media type is invalid")
        if self.role == "depth" and (
            not self.path.startswith("keyframes/depth/")
            or not self.path.lower().endswith(".png")
            or self.media_type != "image/png"
        ):
            raise ContractError("$.files[]", "depth path or media type is invalid")

    def to_wire(self) -> dict[str, Any]:
        return {
            "role": self.role,
            "path": self.path,
            "mediaType": self.media_type,
            "byteLength": self.byte_length,
            "sha256": self.sha256,
        }

    @classmethod
    def from_wire(cls, value: Any, path: str) -> ChunkBundleFile:
        obj = _object(
            value,
            path,
            required=("role", "path", "mediaType", "byteLength", "sha256"),
        )
        return cls(
            role=_string(obj["role"], f"{path}.role", maximum=32),
            path=_safe_relative_path(obj["path"], f"{path}.path"),
            media_type=_media_type(obj["mediaType"], f"{path}.mediaType"),
            byte_length=_integer(
                obj["byteLength"], f"{path}.byteLength", 1, MAX_ARTIFACT_FILE_BYTES
            ),
            sha256=_digest(obj["sha256"], f"{path}.sha256"),
        )


@dataclass(frozen=True, slots=True)
class ChunkBundleManifest:
    key: JobKey
    files: tuple[ChunkBundleFile, ...]

    def __post_init__(self) -> None:
        if not 3 <= len(self.files) <= MAX_INPUT_FILES:
            raise ContractError("$.files", "contains an invalid number of entries")
        paths = [file.path for file in self.files]
        if len(paths) != len(set(paths)) or len(paths) != len(set(map(str.casefold, paths))):
            raise ContractError("$.files", "file paths must be unique, including case")
        roles = [file.role for file in self.files]
        mesh_count = roles.count("refined_mesh") + roles.count("live_mesh")
        if mesh_count != 1:
            raise ContractError("$.files", "must contain exactly one supported mesh")
        if roles.count("keyframe_manifest") != 1:
            raise ContractError("$.files", "must contain exactly one keyframe manifest")
        if roles.count("keyframe_image") < 1:
            raise ContractError("$.files", "must contain at least one keyframe image")
        if sum(file.byte_length for file in self.files) > MAX_UPLOAD_BYTES:
            raise ContractError("$.files", "aggregate input payload exceeds the limit")

    def to_wire(self) -> dict[str, Any]:
        return {
            "schemaVersion": PROTOCOL_VERSION,
            "bundleFormatVersion": CHUNK_BUNDLE_FORMAT_VERSION,
            "key": self.key.to_wire(),
            "meshSpace": "chunk-local",
            "coordinateSystem": "unity-lh-y-up-z-forward",
            "units": "meter",
            "frontFace": "clockwise",
            "files": [file.to_wire() for file in self.files],
        }

    @classmethod
    def from_wire(cls, value: Any, path: str = "$") -> ChunkBundleManifest:
        obj = _object(
            value,
            path,
            required=(
                "schemaVersion",
                "bundleFormatVersion",
                "key",
                "meshSpace",
                "coordinateSystem",
                "units",
                "frontFace",
                "files",
            ),
        )
        if _integer(obj["schemaVersion"], "$.schemaVersion", 1, 2_147_483_647) != PROTOCOL_VERSION:
            raise ContractError("$.schemaVersion", "unsupported protocol version")
        if (
            _integer(
                obj["bundleFormatVersion"], "$.bundleFormatVersion", 1, 2_147_483_647
            )
            != CHUNK_BUNDLE_FORMAT_VERSION
        ):
            raise ContractError("$.bundleFormatVersion", "unsupported chunk bundle version")
        literals = {
            "meshSpace": "chunk-local",
            "coordinateSystem": "unity-lh-y-up-z-forward",
            "units": "meter",
            "frontFace": "clockwise",
        }
        for field, expected in literals.items():
            if obj[field] != expected:
                raise ContractError(f"$.{field}", f"must be {expected!r}")
        if not isinstance(obj["files"], list):
            raise ContractError("$.files", "must be an array")
        files = tuple(
            ChunkBundleFile.from_wire(item, f"$.files[{index}]")
            for index, item in enumerate(obj["files"])
        )
        return cls(JobKey.from_wire(obj["key"]), files)


@dataclass(frozen=True, slots=True)
class DiffSoupArtifactManifest:
    key: JobKey
    request_fingerprint: str
    producer_commit: str
    compatibility_tag: str
    level: int
    num_vertices: int
    num_faces: int
    lut_width: int
    lut_height: int
    files: tuple[ArtifactFile, ...]

    REQUIRED_ROLES: ClassVar[frozenset[str]] = frozenset(
        ("mesh", "lut0", "lut1", "mlp", "meta")
    )

    def __post_init__(self) -> None:
        _digest(self.request_fingerprint, "$.requestFingerprint")
        _git_commit(self.producer_commit, "$.producerCommit")
        _digest(self.compatibility_tag, "$.compatibilityTag")
        _integer(self.level, "$.model.level", 0, MAX_SUBDIVISION_LEVEL)
        _integer(self.num_vertices, "$.model.numVertices", 3, MAX_VERTICES)
        _integer(self.num_faces, "$.model.numFaces", 1, MAX_FACES)
        _integer(self.lut_width, "$.model.lutWidth", 1, MAX_LUT_DIMENSION)
        _integer(self.lut_height, "$.model.lutHeight", 1, MAX_LUT_DIMENSION)
        if not 1 <= len(self.files) <= MAX_ARTIFACT_FILES:
            raise ContractError("$.files", "contains an invalid number of entries")
        roles = [file.role for file in self.files]
        paths = [file.path for file in self.files]
        if len(set(roles)) != len(roles):
            raise ContractError("$.files", "artifact roles must be unique")
        if len(set(paths)) != len(paths):
            raise ContractError("$.files", "artifact paths must be unique")
        missing = sorted(self.REQUIRED_ROLES - set(roles))
        if missing:
            raise ContractError("$.files", "missing required roles: " + ", ".join(missing))
        aggregate = sum(file.byte_length for file in self.files)
        if aggregate > MAX_ARTIFACT_BUNDLE_BYTES:
            raise ContractError("$.files", "aggregate artifact payload exceeds the limit")
        expected = {
            "mesh": ("model/mesh.ply", "application/vnd.questinfinitescan.diffsoup-mesh"),
            "lut0": ("model/lut0.png", "image/png"),
            "lut1": ("model/lut1.png", "image/png"),
            "mlp": ("model/mlp_weights.json", "application/json"),
            "meta": ("model/meta.json", "application/json"),
            "checkpoint": (
                "checkpoint/resume.pt", "application/vnd.questinfinitescan.diffsoup-checkpoint"
            ),
        }
        for file in self.files:
            expected_path, expected_media = expected[file.role]
            if file.path != expected_path or file.media_type != expected_media:
                raise ContractError(
                    "$.files", f"role {file.role} has a non-canonical path or media type"
                )

    @property
    def job_id(self) -> str:
        return self.key.job_id

    def to_wire(self) -> dict[str, Any]:
        return {
            "schemaVersion": PROTOCOL_VERSION,
            "artifactFormatVersion": ARTIFACT_FORMAT_VERSION,
            "jobId": self.job_id,
            "requestFingerprint": self.request_fingerprint,
            "key": self.key.to_wire(),
            "producer": {
                "name": "diffsoup",
                "sourceCommit": self.producer_commit,
                "compatibilityTag": self.compatibility_tag,
            },
            "model": {
                "meshSpace": "chunk-local",
                "coordinateSystem": "unity-lh-y-up-z-forward",
                "units": "meter",
                "frontFace": "clockwise",
                "featureEncoding": "diffsoup-sh2-mlp16-v1",
                "level": self.level,
                "numVertices": self.num_vertices,
                "numFaces": self.num_faces,
                "lutWidth": self.lut_width,
                "lutHeight": self.lut_height,
            },
            "files": [file.to_wire() for file in self.files],
        }

    @classmethod
    def from_wire(cls, value: Any, path: str = "$") -> DiffSoupArtifactManifest:
        obj = _object(
            value,
            path,
            required=(
                "schemaVersion",
                "artifactFormatVersion",
                "jobId",
                "requestFingerprint",
                "key",
                "producer",
                "model",
                "files",
            ),
        )
        if _integer(obj["schemaVersion"], "$.schemaVersion", 1, 2_147_483_647) != PROTOCOL_VERSION:
            raise ContractError("$.schemaVersion", "unsupported protocol version")
        if (
            _integer(
                obj["artifactFormatVersion"],
                "$.artifactFormatVersion",
                1,
                2_147_483_647,
            )
            != ARTIFACT_FORMAT_VERSION
        ):
            raise ContractError(
                "$.artifactFormatVersion", "unsupported DiffSoup artifact version"
            )
        key = JobKey.from_wire(obj["key"])
        if _digest(obj["jobId"], "$.jobId") != key.job_id:
            raise ContractError("$.jobId", "does not match the deterministic job key")
        producer = _object(
            obj["producer"],
            "$.producer",
            required=("name", "sourceCommit", "compatibilityTag"),
        )
        if producer["name"] != "diffsoup":
            raise ContractError("$.producer.name", "unsupported artifact producer")
        model = _object(
            obj["model"],
            "$.model",
            required=(
                "meshSpace",
                "coordinateSystem",
                "units",
                "frontFace",
                "featureEncoding",
                "level",
                "numVertices",
                "numFaces",
                "lutWidth",
                "lutHeight",
            ),
        )
        required_literals = {
            "meshSpace": "chunk-local",
            "coordinateSystem": "unity-lh-y-up-z-forward",
            "units": "meter",
            "frontFace": "clockwise",
            "featureEncoding": "diffsoup-sh2-mlp16-v1",
        }
        for field, expected in required_literals.items():
            if model[field] != expected:
                raise ContractError(f"$.model.{field}", f"must be {expected!r}")
        files_value = obj["files"]
        if not isinstance(files_value, list):
            raise ContractError("$.files", "must be an array")
        files = tuple(
            ArtifactFile.from_wire(file, f"$.files[{index}]")
            for index, file in enumerate(files_value)
        )
        return cls(
            key=key,
            request_fingerprint=_digest(
                obj["requestFingerprint"], "$.requestFingerprint"
            ),
            producer_commit=_git_commit(
                producer["sourceCommit"], "$.producer.sourceCommit"
            ),
            compatibility_tag=_digest(
                producer["compatibilityTag"], "$.producer.compatibilityTag"
            ),
            level=_integer(model["level"], "$.model.level", 0, MAX_SUBDIVISION_LEVEL),
            num_vertices=_integer(
                model["numVertices"], "$.model.numVertices", 3, MAX_VERTICES
            ),
            num_faces=_integer(model["numFaces"], "$.model.numFaces", 1, MAX_FACES),
            lut_width=_integer(
                model["lutWidth"], "$.model.lutWidth", 1, MAX_LUT_DIMENSION
            ),
            lut_height=_integer(
                model["lutHeight"], "$.model.lutHeight", 1, MAX_LUT_DIMENSION
            ),
            files=files,
        )
