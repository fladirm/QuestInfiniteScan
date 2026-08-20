from __future__ import annotations

import hashlib
import io
import json
from pathlib import Path
import time
import zipfile

from fastapi.testclient import TestClient

from quest_infinite_server.api import ServerConfig, create_app
from quest_infinite_server.backend import BackendJobError, FakeDiffSoupBackend
from quest_infinite_server.contracts import (
    BlobDescriptor,
    DiffSoupArtifactManifest,
    JobKey,
    JobState,
)

from helpers import submission_for_bundle, valid_bundle_bytes


def test_worker_config_preserves_virtualenv_python_symlink(
    tmp_path: Path, monkeypatch
) -> None:
    from quest_infinite_server.process_backend import DiffSoupProcessConfig

    target = tmp_path / "python-target"
    target.write_text("#!/bin/sh\nexit 0\n", encoding="utf-8")
    target.chmod(0o755)
    launcher = tmp_path / "venv-python"
    launcher.symlink_to(target)
    monkeypatch.setenv("QIS_DIFFSOUP_PYTHON", str(launcher))
    monkeypatch.setenv(
        "QIS_DIFFSOUP_UPSTREAM_COMMIT",
        "c74e35de74ad0116977b23e7951f4cbc25ab0f6b",
    )

    config = DiffSoupProcessConfig.from_environment()

    assert config.python_executable == launcher.absolute()
    assert config.python_executable.is_symlink()


def wait_for_state(client: TestClient, job_id: str, expected: set[str]) -> dict:
    deadline = time.monotonic() + 5.0
    while time.monotonic() < deadline:
        response = client.get(f"/v2/jobs/{job_id}")
        assert response.status_code == 200
        status = response.json()
        if status["state"] in expected:
            return status
        time.sleep(0.01)
    raise AssertionError(f"job did not reach {expected}")


def test_full_fake_backend_lifecycle_is_idempotent_and_restart_durable(
    tmp_path: Path,
) -> None:
    key = JobKey("world-api", "chunk-000004", 5)
    bundle = valid_bundle_bytes(key)
    submission = submission_for_bundle(key, bundle)
    app = create_app(ServerConfig(tmp_path), FakeDiffSoupBackend(step_delay_seconds=0.0))
    with TestClient(app) as client:
        capabilities = client.get("/v2/capabilities")
        assert capabilities.status_code == 200
        assert capabilities.json()["backends"] == ["fake"]

        created = client.put(f"/v2/jobs/{submission.job_id}", json=submission.to_wire())
        assert created.status_code == 201
        replay = client.put(f"/v2/jobs/{submission.job_id}", json=submission.to_wire())
        assert replay.status_code == 200

        uploaded = client.put(
            f"/v2/jobs/{submission.job_id}/input",
            content=bundle,
            headers={"Content-Type": submission.input_bundle.media_type},
        )
        assert uploaded.status_code == 200, uploaded.text
        assert uploaded.json()["state"] == JobState.AWAITING_UPLOAD.value
        replayed_upload = client.put(
            f"/v2/jobs/{submission.job_id}/input",
            content=bundle,
            headers={"Content-Type": submission.input_bundle.media_type},
        )
        assert replayed_upload.status_code == 200

        enqueued = client.post(f"/v2/jobs/{submission.job_id}/enqueue")
        assert enqueued.status_code == 200
        done = wait_for_state(client, submission.job_id, {JobState.SUCCEEDED.value})
        descriptor = BlobDescriptor.from_wire(done["artifactBundle"], "$.artifactBundle")

        result = client.get(f"/v2/jobs/{submission.job_id}/artifact")
        assert result.status_code == 200
        assert len(result.content) == descriptor.byte_length
        assert hashlib.sha256(result.content).hexdigest() == descriptor.sha256
        assert result.headers["x-qis-sha256"] == descriptor.sha256
        with zipfile.ZipFile(io.BytesIO(result.content), "r") as archive:
            manifest = DiffSoupArtifactManifest.from_wire(
                json.loads(archive.read("artifact.json"))
            )
            assert manifest.key == key
            assert set(archive.namelist()) == {
                "artifact.json",
                *(file.path for file in manifest.files),
            }

    restarted = create_app(ServerConfig(tmp_path), FakeDiffSoupBackend())
    with TestClient(restarted) as client:
        status = client.get(f"/v2/jobs/{submission.job_id}")
        assert status.status_code == 200
        assert status.json()["state"] == JobState.SUCCEEDED.value
        assert client.get(f"/v2/jobs/{submission.job_id}/artifact").status_code == 200


def test_conflicting_submission_hash_and_oversized_body_fail_closed(tmp_path: Path) -> None:
    key = JobKey("world-api", "chunk", 1)
    bundle = valid_bundle_bytes(key)
    original = submission_for_bundle(key, bundle)
    conflict_bundle = valid_bundle_bytes(key, image_payload=b"different-jpeg")
    conflict = submission_for_bundle(key, conflict_bundle)
    app = create_app(ServerConfig(tmp_path), FakeDiffSoupBackend())
    with TestClient(app) as client:
        assert client.put(f"/v2/jobs/{original.job_id}", json=original.to_wire()).status_code == 201
        response = client.put(f"/v2/jobs/{conflict.job_id}", json=conflict.to_wire())
        assert response.status_code == 409
        assert response.json()["error"]["code"] == "idempotency_conflict"

        too_large = client.put(
            f"/v2/jobs/{original.job_id}/input",
            content=bundle + b"x",
            headers={"Content-Type": original.input_bundle.media_type},
        )
        assert too_large.status_code in (409, 413)
        assert client.get(f"/v2/jobs/{original.job_id}").json()["state"] == "awaiting_upload"


def test_cancel_is_idempotent_and_prevents_enqueue(tmp_path: Path) -> None:
    key = JobKey("world-api", "chunk", 3)
    bundle = valid_bundle_bytes(key)
    submission = submission_for_bundle(key, bundle)
    app = create_app(ServerConfig(tmp_path), FakeDiffSoupBackend())
    with TestClient(app) as client:
        client.put(f"/v2/jobs/{submission.job_id}", json=submission.to_wire())
        first = client.post(f"/v2/jobs/{submission.job_id}/cancel")
        second = client.post(f"/v2/jobs/{submission.job_id}/cancel")
        assert first.status_code == second.status_code == 200
        assert first.json()["state"] == second.json()["state"] == "canceled"
        assert client.post(f"/v2/jobs/{submission.job_id}/enqueue").status_code == 409


def test_running_cancel_is_cooperative_and_terminal(tmp_path: Path) -> None:
    key = JobKey("world-api", "chunk", 30)
    bundle = valid_bundle_bytes(key)
    submission = submission_for_bundle(key, bundle)
    app = create_app(ServerConfig(tmp_path), FakeDiffSoupBackend(step_delay_seconds=0.15))
    with TestClient(app) as client:
        client.put(f"/v2/jobs/{submission.job_id}", json=submission.to_wire())
        client.put(
            f"/v2/jobs/{submission.job_id}/input",
            content=bundle,
            headers={"Content-Type": submission.input_bundle.media_type},
        )
        client.post(f"/v2/jobs/{submission.job_id}/enqueue")
        wait_for_state(client, submission.job_id, {JobState.RUNNING.value})
        requested = client.post(f"/v2/jobs/{submission.job_id}/cancel")
        assert requested.status_code == 200
        canceled = wait_for_state(client, submission.job_id, {JobState.CANCELED.value})
        assert canceled["artifactBundle"] is None
        assert client.post(f"/v2/jobs/{submission.job_id}/enqueue").status_code == 409


def test_invalid_zip_upload_is_rejected_without_registering_input(tmp_path: Path) -> None:
    key = JobKey("world-api", "chunk", 4)
    buffer = io.BytesIO()
    with zipfile.ZipFile(buffer, "w") as archive:
        archive.writestr("input.json", "{}")
        archive.writestr("../escape", b"bad")
    malicious = buffer.getvalue()
    submission = submission_for_bundle(key, malicious)
    app = create_app(ServerConfig(tmp_path), FakeDiffSoupBackend())
    with TestClient(app) as client:
        client.put(f"/v2/jobs/{submission.job_id}", json=submission.to_wire())
        response = client.put(
            f"/v2/jobs/{submission.job_id}/input",
            content=malicious,
            headers={"Content-Type": submission.input_bundle.media_type},
        )
        assert response.status_code == 422
        assert response.json()["error"]["code"] == "invalid_chunk_bundle"
        assert client.post(f"/v2/jobs/{submission.job_id}/enqueue").status_code == 409


def test_structured_worker_rejection_is_preserved_in_durable_status(tmp_path: Path) -> None:
    class RejectingBackend:
        name = "diffsoup"

        async def run(self, _):
            raise BackendJobError("invalid_qrs_dataset", "mesh payload is not QISM/QIRM")

    key = JobKey("world-api", "chunk", 5)
    bundle = valid_bundle_bytes(key)
    submission = submission_for_bundle(key, bundle)
    app = create_app(ServerConfig(tmp_path), RejectingBackend())
    with TestClient(app) as client:
        client.put(f"/v2/jobs/{submission.job_id}", json=submission.to_wire())
        client.put(
            f"/v2/jobs/{submission.job_id}/input",
            content=bundle,
            headers={"Content-Type": submission.input_bundle.media_type},
        )
        client.post(f"/v2/jobs/{submission.job_id}/enqueue")
        failed = wait_for_state(client, submission.job_id, {JobState.FAILED.value})
        assert failed["errorCode"] == "invalid_qrs_dataset"
        assert failed["message"] == "mesh payload is not QISM/QIRM"
