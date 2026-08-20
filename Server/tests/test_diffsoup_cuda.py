from __future__ import annotations

import asyncio
import hashlib
import json
import os
from pathlib import Path
import zipfile

import pytest

from quest_infinite_server.artifact import validate_artifact_bundle
from quest_infinite_server.backend import BackendContext
from quest_infinite_server.contracts import BlobDescriptor, JobKey, JobSubmission, WarmStart
from quest_infinite_server.process_backend import (
    DiffSoupProcessBackend,
    DiffSoupProcessConfig,
)
from quest_infinite_server.storage import JobStore

from helpers import valid_qrs_bundle_bytes


pytestmark = pytest.mark.skipif(
    os.environ.get("QIS_RUN_CUDA_TESTS") != "1",
    reason="set QIS_RUN_CUDA_TESTS=1 for the pinned DiffSoup/CUDA integration",
)


def test_real_worker_exports_and_exactly_warm_starts_revision(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    python = Path(
        os.environ.get(
            "QIS_DIFFSOUP_PYTHON",
            "/mnt/kingston-unity/DiffSoup/.venv/bin/python",
        )
    )
    assert python.is_file()
    monkeypatch.setenv("QIS_DIFFSOUP_STEPS", "2")
    monkeypatch.setenv("QIS_DIFFSOUP_MAX_FRAMES", "1")
    monkeypatch.setenv("QIS_DIFFSOUP_MAX_DIMENSION", "32")
    monkeypatch.setenv("QIS_DIFFSOUP_MAX_FACES", "1")
    monkeypatch.setenv("QIS_DIFFSOUP_LEVEL", "0")
    store = JobStore(tmp_path / "store")
    backend = DiffSoupProcessBackend(
        DiffSoupProcessConfig(
            python,
            "c74e35de74ad0116977b23e7951f4cbc25ab0f6b",
            120,
        )
    )
    try:
        key0 = JobKey("world-cuda", "chunk-000001", 0)
        context0 = _context(store, key0)
        result0 = asyncio.run(backend.run(context0))
        manifest0 = validate_artifact_bundle(
            result0.artifact_path,
            expected_key=key0,
            expected_request_fingerprint=context0.submission.request_fingerprint,
        )
        checkpoint_file = next(file for file in manifest0.files if file.role == "checkpoint")
        store.complete(key0.job_id, result0.artifact_path, result0.descriptor)

        key1 = JobKey("world-cuda", "chunk-000001", 1)
        warm = WarmStart(
            0,
            manifest0.compatibility_tag,
            BlobDescriptor(
                checkpoint_file.media_type,
                checkpoint_file.format_version,
                checkpoint_file.byte_length,
                checkpoint_file.sha256,
            ),
        )
        context1 = _context(store, key1, warm=warm)
        result1 = asyncio.run(backend.run(context1))
        validate_artifact_bundle(
            result1.artifact_path,
            expected_key=key1,
            expected_request_fingerprint=context1.submission.request_fingerprint,
        )
        with zipfile.ZipFile(result1.artifact_path, "r") as archive:
            metadata = json.loads(archive.read("model/meta.json"))
        assert metadata["warmStartUsed"] is True
        assert metadata["warmSourceRevision"] == 0
        assert metadata["completedSteps"] == 4
        manifest1 = validate_artifact_bundle(result1.artifact_path, expected_key=key1)
        checkpoint1 = next(file for file in manifest1.files if file.role == "checkpoint")
        store.complete(key1.job_id, result1.artifact_path, result1.descriptor)

        key2 = JobKey("world-cuda", "chunk-000001", 2)
        incompatible = WarmStart(
            1,
            "f" * 64,
            BlobDescriptor(
                checkpoint1.media_type,
                checkpoint1.format_version,
                checkpoint1.byte_length,
                checkpoint1.sha256,
            ),
        )
        context2 = _context(store, key2, warm=incompatible)
        result2 = asyncio.run(backend.run(context2))
        with zipfile.ZipFile(result2.artifact_path, "r") as archive:
            fallback_metadata = json.loads(archive.read("model/meta.json"))
        assert fallback_metadata["warmStartUsed"] is False
        assert fallback_metadata["freshFallbackReason"] is not None
        assert fallback_metadata["completedSteps"] == 2
    finally:
        store.close()


def _context(
    store: JobStore,
    key: JobKey,
    *,
    warm: WarmStart | None = None,
) -> BackendContext:
    bundle = valid_qrs_bundle_bytes(key)
    submission = JobSubmission(
        key,
        BlobDescriptor(
            "application/vnd.questinfinitescan.chunk+zip",
            1,
            len(bundle),
            hashlib.sha256(bundle).hexdigest(),
        ),
        profile="preview",
        allow_fresh_fallback=True,
        warm_start=warm,
    )
    store.create_or_replay(submission)
    upload_path = store.upload_root / f"{key.job_id}.zip"
    upload_path.write_bytes(bundle)
    store.record_upload(key.job_id, upload_path, len(bundle), hashlib.sha256(bundle).hexdigest())
    store.enqueue(key.job_id)
    claimed = store.claim_next()
    assert claimed is not None
    claimed_submission, upload = claimed
    return BackendContext(claimed_submission, upload, store)
