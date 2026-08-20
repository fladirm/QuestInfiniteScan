from __future__ import annotations

from pathlib import Path

import pytest

from quest_infinite_server.contracts import BlobDescriptor, JobKey, JobState
from quest_infinite_server.storage import JobConflictError, JobStore

from helpers import submission_for_bundle, valid_bundle_bytes


def register_upload(store: JobStore, key: JobKey) -> str:
    bundle = valid_bundle_bytes(key)
    submission = submission_for_bundle(key, bundle)
    store.create_or_replay(submission, now=1_000)
    path = store.upload_root / f"{submission.job_id}.zip"
    path.write_bytes(bundle)
    store.record_upload(
        submission.job_id,
        path,
        submission.input_bundle.byte_length,
        submission.input_bundle.sha256,
    )
    return submission.job_id


def test_durable_identity_conflict_and_interrupted_recovery(tmp_path: Path) -> None:
    store = JobStore(tmp_path)
    key = JobKey("world", "chunk", 1)
    job_id = register_upload(store, key)
    replay, created = store.create_or_replay(store.get_submission(job_id), now=1_100)
    assert not created
    assert replay.key == key
    conflict_bundle = valid_bundle_bytes(key, image_payload=b"different")
    with pytest.raises(JobConflictError):
        store.create_or_replay(submission_for_bundle(key, conflict_bundle), now=1_200)
    store.enqueue(job_id)
    claimed = store.claim_next()
    assert claimed is not None
    assert store.get_status(job_id).state == JobState.RUNNING
    store.close()

    reopened = JobStore(tmp_path)
    assert reopened.recover_interrupted() == 1
    recovered = reopened.get_status(job_id)
    assert recovered.state == JobState.QUEUED
    assert recovered.attempt == 1
    assert "restart" in recovered.message
    reopened.close()


def test_successful_terminal_job_survives_restart(tmp_path: Path) -> None:
    store = JobStore(tmp_path)
    key = JobKey("world", "chunk", 2)
    job_id = register_upload(store, key)
    store.enqueue(job_id)
    assert store.claim_next() is not None
    artifact_path = store.artifact_root / f"{job_id}.zip"
    artifact_path.write_bytes(b"artifact")
    descriptor = BlobDescriptor(
        "application/vnd.questinfinitescan.diffsoup+zip",
        1,
        len(b"artifact"),
        "c7c5c1d70c5dec44fe94ee0f2c0018084d232fc4b4849a60656e3c0133d4b659",
    )
    store.complete(job_id, artifact_path, descriptor)
    store.close()

    reopened = JobStore(tmp_path)
    status = reopened.get_status(job_id)
    assert status.state == JobState.SUCCEEDED
    assert status.artifact_bundle == descriptor
    assert reopened.artifact_path(job_id) == artifact_path
    reopened.close()

