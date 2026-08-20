from __future__ import annotations

import copy
import json
from pathlib import Path
import unittest

from quest_infinite_server.contracts import (
    ARTIFACT_FORMAT_VERSION,
    MAX_UPLOAD_BYTES,
    ArtifactFile,
    BlobDescriptor,
    ContractError,
    DiffSoupArtifactManifest,
    JobKey,
    JobState,
    JobStatus,
    JobSubmission,
    transition_allowed,
)


DIGEST_A = "a" * 64
DIGEST_B = "b" * 64
DIGEST_C = "c" * 64
DIFFSOUP_COMMIT = "c74e35de74ad0116977b23e7951f4cbc25ab0f6b"
CONTRACT_ROOT = Path(__file__).parents[1] / "contracts" / "v2"


def input_blob(digest: str = DIGEST_A, size: int = 1024) -> BlobDescriptor:
    return BlobDescriptor(
        "application/vnd.questinfinitescan.chunk+zip", 1, size, digest
    )


def submission(digest: str = DIGEST_A) -> JobSubmission:
    return JobSubmission(JobKey("world-01", "chunk-000042", 7), input_blob(digest))


def artifact_file(role: str, digest: str) -> ArtifactFile:
    values = {
        "mesh": ("model/mesh.ply", "application/vnd.questinfinitescan.diffsoup-mesh"),
        "lut0": ("model/lut0.png", "image/png"),
        "lut1": ("model/lut1.png", "image/png"),
        "mlp": ("model/mlp_weights.json", "application/json"),
        "meta": ("model/meta.json", "application/json"),
    }
    path, media_type = values[role]
    return ArtifactFile(role, path, media_type, 1, 64, digest)


def artifact_manifest() -> DiffSoupArtifactManifest:
    request = submission()
    files = tuple(
        artifact_file(role, format(index + 1, "064x"))
        for index, role in enumerate(("mesh", "lut0", "lut1", "mlp", "meta"))
    )
    return DiffSoupArtifactManifest(
        key=request.key,
        request_fingerprint=request.request_fingerprint,
        producer_commit=DIFFSOUP_COMMIT,
        compatibility_tag=DIGEST_C,
        level=5,
        num_vertices=300,
        num_faces=100,
        lut_width=4096,
        lut_height=3,
        files=files,
    )


class JobContractTests(unittest.TestCase):
    def test_job_identity_is_deterministic_and_revision_scoped(self) -> None:
        first = JobKey("world-01", "chunk-000042", 7)
        replay = JobKey("world-01", "chunk-000042", 7)
        next_revision = JobKey("world-01", "chunk-000042", 8)
        self.assertEqual(first.job_id, replay.job_id)
        self.assertNotEqual(first.job_id, next_revision.job_id)
        self.assertEqual(len(first.job_id), 64)

    def test_golden_submission_freezes_cross_language_hashes(self) -> None:
        wire = json.loads(
            (CONTRACT_ROOT / "examples" / "job-submission.json").read_text("utf-8")
        )
        restored = JobSubmission.from_wire(wire)
        self.assertEqual(
            restored.job_id,
            "30b28e11e4d78ea8765a83b544a41c55ccc1397d0a16be9b3700792ee910993c",
        )
        self.assertEqual(
            restored.request_fingerprint,
            "90a6517f476541d6b68905c8b04dda0497fdf188d0630f64614c90e3f988b9ba",
        )

    def test_same_key_changed_payload_is_an_idempotency_conflict(self) -> None:
        first = submission(DIGEST_A)
        replay = submission(DIGEST_A)
        conflict = submission(DIGEST_B)
        self.assertEqual(first.job_id, replay.job_id)
        self.assertEqual(first.request_fingerprint, replay.request_fingerprint)
        self.assertEqual(first.job_id, conflict.job_id)
        self.assertNotEqual(first.request_fingerprint, conflict.request_fingerprint)

    def test_submission_round_trip_is_canonical_and_tamper_evident(self) -> None:
        original = submission()
        wire = original.to_wire()
        encoded = json.dumps(wire, sort_keys=True)
        restored = JobSubmission.from_wire(json.loads(encoded))
        self.assertEqual(restored, original)

        tampered = copy.deepcopy(wire)
        tampered["profile"] = "quality"
        with self.assertRaisesRegex(ContractError, "requestFingerprint"):
            JobSubmission.from_wire(tampered)

        unknown = copy.deepcopy(wire)
        unknown["surprise"] = True
        with self.assertRaisesRegex(ContractError, "unknown fields"):
            JobSubmission.from_wire(unknown)

    def test_blob_hash_size_and_version_fail_closed(self) -> None:
        wire = submission().to_wire()
        wire["inputBundle"]["sha256"] = "ABC"
        with self.assertRaisesRegex(ContractError, "SHA-256"):
            JobSubmission.from_wire(wire)

        wire = submission().to_wire()
        wire["inputBundle"]["byteLength"] = MAX_UPLOAD_BYTES + 1
        with self.assertRaisesRegex(ContractError, "byteLength"):
            JobSubmission.from_wire(wire)

        wire = submission().to_wire()
        wire["inputBundle"]["formatVersion"] = 2
        wire["requestFingerprint"] = DIGEST_A
        with self.assertRaisesRegex(ContractError, "formatVersion"):
            JobSubmission.from_wire(wire)

    def test_status_contract_supports_poll_retry_cancel_and_terminal_result(self) -> None:
        request = submission()
        queued = JobStatus(
            request.key,
            request.request_fingerprint,
            JobState.QUEUED,
            0.0,
            1,
            1_000,
            1_100,
            retry_after_ms=1_500,
        )
        self.assertEqual(JobStatus.from_wire(queued.to_wire()), queued)
        self.assertTrue(transition_allowed(JobState.QUEUED, JobState.RUNNING))
        self.assertTrue(transition_allowed(JobState.RUNNING, JobState.QUEUED))
        self.assertTrue(transition_allowed(JobState.RUNNING, JobState.CANCELED))
        self.assertFalse(transition_allowed(JobState.SUCCEEDED, JobState.RUNNING))

        artifact = BlobDescriptor(
            "application/vnd.questinfinitescan.diffsoup+zip", 1, 4096, DIGEST_B
        )
        done = JobStatus(
            request.key,
            request.request_fingerprint,
            JobState.SUCCEEDED,
            1.0,
            1,
            1_000,
            2_000,
            artifact_bundle=artifact,
        )
        self.assertEqual(JobStatus.from_wire(done.to_wire()), done)
        with self.assertRaisesRegex(ContractError, "artifactBundle"):
            JobStatus(
                request.key,
                request.request_fingerprint,
                JobState.SUCCEEDED,
                1.0,
                1,
                1_000,
                2_000,
            )


class ArtifactContractTests(unittest.TestCase):
    def test_diffsoup_manifest_round_trip_locks_runtime_conventions(self) -> None:
        original = artifact_manifest()
        wire = original.to_wire()
        self.assertEqual(wire["artifactFormatVersion"], ARTIFACT_FORMAT_VERSION)
        self.assertEqual(wire["model"]["meshSpace"], "chunk-local")
        self.assertEqual(wire["model"]["coordinateSystem"], "unity-lh-y-up-z-forward")
        self.assertEqual(wire["model"]["frontFace"], "clockwise")
        restored = DiffSoupArtifactManifest.from_wire(
            json.loads(json.dumps(wire, sort_keys=True))
        )
        self.assertEqual(restored, original)

    def test_missing_duplicate_and_unsafe_artifacts_are_rejected(self) -> None:
        wire = artifact_manifest().to_wire()
        wire["files"] = [file for file in wire["files"] if file["role"] != "lut1"]
        with self.assertRaisesRegex(ContractError, "missing required roles"):
            DiffSoupArtifactManifest.from_wire(wire)

        wire = artifact_manifest().to_wire()
        wire["files"][1]["role"] = "mesh"
        with self.assertRaisesRegex(ContractError, "roles must be unique"):
            DiffSoupArtifactManifest.from_wire(wire)

        wire = artifact_manifest().to_wire()
        wire["files"][0]["path"] = "../mesh.ply"
        with self.assertRaisesRegex(ContractError, "unsafe"):
            DiffSoupArtifactManifest.from_wire(wire)

    def test_model_limits_versions_and_file_hashes_are_rejected(self) -> None:
        wire = artifact_manifest().to_wire()
        wire["artifactFormatVersion"] = 99
        with self.assertRaisesRegex(ContractError, "artifact version"):
            DiffSoupArtifactManifest.from_wire(wire)

        wire = artifact_manifest().to_wire()
        wire["model"]["lutWidth"] = 100_000
        with self.assertRaisesRegex(ContractError, "lutWidth"):
            DiffSoupArtifactManifest.from_wire(wire)

        wire = artifact_manifest().to_wire()
        wire["files"][0]["sha256"] = "0" * 63
        with self.assertRaisesRegex(ContractError, "SHA-256"):
            DiffSoupArtifactManifest.from_wire(wire)


if __name__ == "__main__":
    unittest.main()
