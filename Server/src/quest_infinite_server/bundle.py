"""Bounded validation for untrusted Quest chunk ZIP uploads."""

from __future__ import annotations

import hashlib
import json
from pathlib import Path, PurePosixPath
import stat
import zipfile

from .contracts import (
    MAX_INPUT_FILES,
    MAX_UPLOAD_BYTES,
    ChunkBundleManifest,
    ContractError,
    JobKey,
)


MAX_INPUT_MANIFEST_BYTES = 8 * 1024 * 1024
MAX_COMPRESSION_RATIO = 200


class BundleValidationError(ValueError):
    pass


def _safe_name(name: str) -> bool:
    if not name or name.startswith("/") or "\\" in name or "\x00" in name:
        return False
    path = PurePosixPath(name)
    return not path.is_absolute() and all(part not in ("", ".", "..") for part in path.parts)


def validate_chunk_bundle(path: Path | str, expected_key: JobKey) -> ChunkBundleManifest:
    bundle_path = Path(path)
    try:
        if not bundle_path.is_file():
            raise BundleValidationError("upload is not a regular file")
        if bundle_path.stat().st_size <= 0 or bundle_path.stat().st_size > MAX_UPLOAD_BYTES:
            raise BundleValidationError("compressed upload size is outside the limit")
        with zipfile.ZipFile(bundle_path, "r") as archive:
            infos = archive.infolist()
            if not 1 <= len(infos) <= MAX_INPUT_FILES + 1:
                raise BundleValidationError("archive entry count exceeds the limit")
            names: set[str] = set()
            folded: set[str] = set()
            total_uncompressed = 0
            for info in infos:
                if info.is_dir():
                    raise BundleValidationError("directory entries are not allowed")
                if not _safe_name(info.filename):
                    raise BundleValidationError(f"unsafe archive path: {info.filename!r}")
                folded_name = info.filename.casefold()
                if info.filename in names or folded_name in folded:
                    raise BundleValidationError("duplicate archive path")
                names.add(info.filename)
                folded.add(folded_name)
                if info.flag_bits & 0x1:
                    raise BundleValidationError("encrypted archive entries are not allowed")
                mode = info.external_attr >> 16
                if mode and stat.S_IFMT(mode) not in (0, stat.S_IFREG):
                    raise BundleValidationError("links and special archive entries are not allowed")
                if info.file_size < 0 or info.file_size > MAX_UPLOAD_BYTES:
                    raise BundleValidationError("archive entry exceeds the size limit")
                total_uncompressed += info.file_size
                if total_uncompressed > MAX_UPLOAD_BYTES:
                    raise BundleValidationError("archive expands beyond the aggregate limit")
                if (
                    info.file_size > 1024 * 1024
                    and info.file_size > max(1, info.compress_size) * MAX_COMPRESSION_RATIO
                ):
                    raise BundleValidationError("archive entry compression ratio is unsafe")
            if "input.json" not in names:
                raise BundleValidationError("archive is missing input.json")
            info = archive.getinfo("input.json")
            if info.file_size > MAX_INPUT_MANIFEST_BYTES:
                raise BundleValidationError("input manifest exceeds the size limit")
            with archive.open(info, "r") as stream:
                manifest_bytes = stream.read(MAX_INPUT_MANIFEST_BYTES + 1)
            if len(manifest_bytes) != info.file_size:
                raise BundleValidationError("input manifest is truncated or oversized")
            try:
                manifest_value = json.loads(manifest_bytes.decode("utf-8"))
                manifest = ChunkBundleManifest.from_wire(manifest_value)
            except (UnicodeDecodeError, json.JSONDecodeError, ContractError) as exception:
                raise BundleValidationError(f"input manifest rejected: {exception}") from exception
            if manifest.key != expected_key:
                raise BundleValidationError("input manifest job key does not match the upload")
            declared = {file.path: file for file in manifest.files}
            if names != {"input.json", *declared}:
                raise BundleValidationError("archive entries do not exactly match input.json")
            for name, descriptor in declared.items():
                entry = archive.getinfo(name)
                if entry.file_size != descriptor.byte_length:
                    raise BundleValidationError(f"declared size mismatch for {name}")
                digest = hashlib.sha256()
                read = 0
                with archive.open(entry, "r") as stream:
                    while block := stream.read(1024 * 1024):
                        read += len(block)
                        if read > descriptor.byte_length:
                            raise BundleValidationError(f"expanded size exceeds declaration for {name}")
                        digest.update(block)
                if read != descriptor.byte_length or digest.hexdigest() != descriptor.sha256:
                    raise BundleValidationError(f"hash or size mismatch for {name}")
            return manifest
    except zipfile.BadZipFile as exception:
        raise BundleValidationError("upload is not a valid ZIP archive") from exception

