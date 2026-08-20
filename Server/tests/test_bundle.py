from __future__ import annotations

import io
import json
from pathlib import Path
import zipfile

import pytest

from quest_infinite_server.bundle import BundleValidationError, validate_chunk_bundle
from quest_infinite_server.contracts import JobKey

from helpers import valid_bundle_bytes


def test_valid_chunk_bundle_verifies_manifest_entries_and_hashes(tmp_path: Path) -> None:
    key = JobKey("world-a", "chunk-000001", 3)
    path = tmp_path / "input.zip"
    path.write_bytes(valid_bundle_bytes(key))
    manifest = validate_chunk_bundle(path, key)
    assert manifest.key == key
    assert {file.role for file in manifest.files} == {
        "live_mesh",
        "keyframe_manifest",
        "keyframe_image",
    }


@pytest.mark.parametrize("name", ("../escape", "/absolute", "folder\\evil"))
def test_archive_traversal_and_non_posix_paths_are_rejected(
    tmp_path: Path, name: str
) -> None:
    path = tmp_path / "unsafe.zip"
    with zipfile.ZipFile(path, "w") as archive:
        archive.writestr("input.json", "{}")
        archive.writestr(name, b"bad")
    with pytest.raises(BundleValidationError, match="unsafe archive path"):
        validate_chunk_bundle(path, JobKey("world", "chunk", 0))


def test_high_ratio_zip_bomb_is_rejected_before_manifest_use(tmp_path: Path) -> None:
    path = tmp_path / "bomb.zip"
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as archive:
        archive.writestr("input.json", "{}")
        archive.writestr("bomb.bin", b"0" * (2 * 1024 * 1024))
    with pytest.raises(BundleValidationError, match="compression ratio"):
        validate_chunk_bundle(path, JobKey("world", "chunk", 0))


def test_undeclared_and_hash_mismatched_entries_are_rejected(tmp_path: Path) -> None:
    key = JobKey("world", "chunk", 0)
    original = valid_bundle_bytes(key)
    source = zipfile.ZipFile(io.BytesIO(original), "r")
    path = tmp_path / "extra.zip"
    with zipfile.ZipFile(path, "w") as output:
        for info in source.infolist():
            output.writestr(info.filename, source.read(info.filename))
        output.writestr("surprise.bin", b"undeclared")
    source.close()
    with pytest.raises(BundleValidationError, match="exactly match"):
        validate_chunk_bundle(path, key)

    source = zipfile.ZipFile(io.BytesIO(original), "r")
    path = tmp_path / "tampered.zip"
    with zipfile.ZipFile(path, "w") as output:
        for info in source.infolist():
            data = source.read(info.filename)
            if info.filename.endswith(".jpg"):
                data += b"tamper"
            output.writestr(info.filename, data)
    source.close()
    with pytest.raises(BundleValidationError, match="size mismatch"):
        validate_chunk_bundle(path, key)

