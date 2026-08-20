"""Pinned upstream DiffSoup CUDA worker.

This module is executed by the dedicated DiffSoup Python environment, never by the
FastAPI interpreter. It uses upstream differentiable rasterisation and optimisers,
keeps QRS geometry in Unity chunk-local coordinates, and emits the canonical V1
artifact plus an exact-resume checkpoint.
"""

from __future__ import annotations

import argparse
from dataclasses import dataclass
import hashlib
import io
import json
import math
import os
from pathlib import Path
import random
import struct
import sys
from typing import Any
import uuid
import zipfile

import numpy as np
from PIL import Image
import torch

import diffsoup as ds

from .contracts import (
    ARTIFACT_FORMAT_VERSION,
    ArtifactFile,
    BlobDescriptor,
    DiffSoupArtifactManifest,
    JobSubmission,
)
from .qrs_dataset import QrsDataset, QrsDatasetError, QrsFrame, load_qrs_dataset


PINNED_DIFFSOUP_COMMIT = "c74e35de74ad0116977b23e7951f4cbc25ab0f6b"
CHECKPOINT_FORMAT = "questinfinitescan-diffsoup-resume"
CHECKPOINT_VERSION = 1
WORKER_SCHEMA_VERSION = 1
FEATURE_DIMENSION = 7
MLP_HIDDEN_DIMENSION = 16
MLP_HIDDEN_LAYERS = 2


class WorkerFailure(RuntimeError):
    def __init__(self, code: str, message: str) -> None:
        self.code = code
        self.message = message
        super().__init__(message)


class WarmStartRejected(WorkerFailure):
    def __init__(self, message: str) -> None:
        super().__init__("warm_start_incompatible", message)


@dataclass(frozen=True, slots=True)
class TrainingProfile:
    name: str
    steps: int
    maximum_frames: int
    maximum_dimension: int
    maximum_faces: int
    level: int
    geometry_learning_rate: float
    feature_learning_rate: float
    shader_learning_rate: float


@dataclass(slots=True)
class TrainingState:
    vertices: torch.Tensor
    faces: torch.Tensor
    feature_source: torch.Tensor
    alpha_source: torch.Tensor
    color_mlp: Any
    soup_optimizer: Any
    vertex_optimizer: Any
    shader_optimizer: Any
    completed_steps: int
    warm_start_used: bool
    warm_source_revision: int | None
    fresh_fallback_reason: str | None


@dataclass(frozen=True, slots=True)
class TrainingViews:
    mvps: torch.Tensor
    inverse_mvps: torch.Tensor
    images: tuple[torch.Tensor, ...]
    height: int
    width: int
    source_frame_ids: tuple[int, ...]


def _profile(name: str) -> TrainingProfile:
    defaults = {
        "preview": TrainingProfile("preview", 200, 8, 256, 20_000, 0, 5e-4, 5e-2, 1e-2),
        "balanced": TrainingProfile("balanced", 3_000, 48, 512, 100_000, 1, 2e-4, 3e-2, 5e-3),
        "quality": TrainingProfile("quality", 10_000, 128, 768, 250_000, 2, 1e-4, 2e-2, 3e-3),
    }
    try:
        value = defaults[name]
    except KeyError as exception:
        raise WorkerFailure("unsupported_profile", f"unsupported profile {name!r}") from exception
    return TrainingProfile(
        value.name,
        _environment_integer("QIS_DIFFSOUP_STEPS", value.steps, 1, 100_000),
        _environment_integer("QIS_DIFFSOUP_MAX_FRAMES", value.maximum_frames, 1, 512),
        _environment_integer("QIS_DIFFSOUP_MAX_DIMENSION", value.maximum_dimension, 16, 2_048),
        _environment_integer("QIS_DIFFSOUP_MAX_FACES", value.maximum_faces, 1, 1_000_000),
        _environment_integer("QIS_DIFFSOUP_LEVEL", value.level, 0, 5),
        value.geometry_learning_rate,
        value.feature_learning_rate,
        value.shader_learning_rate,
    )


def _environment_integer(name: str, default: int, minimum: int, maximum: int) -> int:
    raw = os.environ.get(name)
    if raw is None:
        return default
    try:
        value = int(raw)
    except ValueError as exception:
        raise WorkerFailure("invalid_worker_configuration", f"{name} is not an integer") from exception
    if not minimum <= value <= maximum:
        raise WorkerFailure(
            "invalid_worker_configuration", f"{name} must be in [{minimum}, {maximum}]"
        )
    return value


def _emit(kind: str, **values: Any) -> None:
    print(
        json.dumps({"kind": kind, **values}, sort_keys=True, separators=(",", ":"),
                   allow_nan=False),
        flush=True,
    )


def _progress(progress: float, message: str) -> None:
    _emit("progress", progress=max(0.0, min(0.99, float(progress))), message=message)


def _source_commit() -> str:
    value = os.environ.get("QIS_DIFFSOUP_UPSTREAM_COMMIT", PINNED_DIFFSOUP_COMMIT)
    if len(value) != 40 or any(character not in "0123456789abcdef" for character in value):
        raise WorkerFailure("invalid_worker_configuration", "DiffSoup commit must be full lowercase SHA-1")
    if value != PINNED_DIFFSOUP_COMMIT:
        raise WorkerFailure(
            "unpinned_diffsoup_source",
            f"worker source {value} does not match pinned {PINNED_DIFFSOUP_COMMIT}",
        )
    return value


def compatibility_tag(profile: TrainingProfile, source_commit: str) -> str:
    value = {
        "checkpointFormat": CHECKPOINT_FORMAT,
        "checkpointVersion": CHECKPOINT_VERSION,
        "coordinateSystem": "unity-lh-y-up-z-forward",
        "featureDimension": FEATURE_DIMENSION,
        "featureEncoding": "diffsoup-sh2-mlp16-v1",
        "level": profile.level,
        "mlpHiddenDimension": MLP_HIDDEN_DIMENSION,
        "mlpHiddenLayers": MLP_HIDDEN_LAYERS,
        "optimizer": "adam+vector-adam-v1",
        "profile": profile.name,
        "sourceCommit": source_commit,
        "workerSchemaVersion": WORKER_SCHEMA_VERSION,
    }
    encoded = json.dumps(value, sort_keys=True, separators=(",", ":")).encode("ascii")
    return hashlib.sha256(encoded).hexdigest()


def _load_submission(path: Path) -> JobSubmission:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
        return JobSubmission.from_wire(value)
    except Exception as exception:
        raise WorkerFailure("invalid_submission", f"worker submission rejected: {exception}") from exception


def _mesh_arrays(dataset: QrsDataset, profile: TrainingProfile) -> tuple[np.ndarray, np.ndarray, np.ndarray | None]:
    mesh = dataset.mesh
    vertices = np.ndarray(
        (mesh.vertex_count, 3),
        dtype="<f4",
        buffer=mesh.vertex_bytes,
        offset=0,
        strides=(mesh.vertex_stride, 4),
    ).copy()
    index_dtype = "<i4" if mesh.indices_signed else "<u4"
    faces = np.frombuffer(mesh.index_bytes, dtype=index_dtype).reshape(-1, 3).astype(np.int64)
    colors: np.ndarray | None = None
    if mesh.has_vertex_colors:
        packed = np.ndarray(
            (mesh.vertex_count,),
            dtype="<u4",
            buffer=mesh.vertex_bytes,
            offset=24,
            strides=(mesh.vertex_stride,),
        )
        colors = np.stack(
            (
                packed & 0xFF,
                (packed >> 8) & 0xFF,
                (packed >> 16) & 0xFF,
            ),
            axis=1,
        ).astype(np.float32) / 255.0

    a = vertices[faces[:, 0]]
    b = vertices[faces[:, 1]]
    c = vertices[faces[:, 2]]
    valid = np.linalg.norm(np.cross(b - a, c - a), axis=1) > 1.0e-10
    if not np.any(valid):
        raise WorkerFailure("invalid_qrs_mesh", "QRS mesh contains no non-degenerate triangles")
    faces = faces[valid]
    if faces.shape[0] > profile.maximum_faces:
        # Surface Nets emits faces in spatially coherent grid order. Evenly retaining
        # indices gives a deterministic whole-volume preview instead of keeping only
        # the largest wall. Balanced/quality caps are high enough for normal QRS output.
        selected = np.linspace(0, faces.shape[0] - 1, profile.maximum_faces, dtype=np.int64)
        faces = faces[selected]
    soup_vertices = vertices[faces.reshape(-1)].astype(np.float32, copy=True)
    soup_faces = np.arange(soup_vertices.shape[0], dtype=np.int32).reshape(-1, 3)
    soup_colors = (
        None
        if colors is None
        else colors[faces.reshape(-1)].reshape(-1, 3, 3).astype(np.float32, copy=True)
    )
    return soup_vertices, soup_faces, soup_colors


def _selected_frames(frames: tuple[QrsFrame, ...], maximum: int) -> tuple[QrsFrame, ...]:
    if len(frames) <= maximum:
        return frames
    indices = np.linspace(0, len(frames) - 1, maximum, dtype=np.int64)
    return tuple(frames[int(index)] for index in indices)


def _load_views(dataset: QrsDataset, profile: TrainingProfile, device: torch.device) -> TrainingViews:
    frames = _selected_frames(dataset.frames, profile.maximum_frames)
    first = frames[0]
    aspect = first.width / first.height
    for frame in frames[1:]:
        if abs(frame.width / frame.height - aspect) > 0.01:
            raise WorkerFailure("incompatible_keyframes", "selected keyframes have mixed aspect ratios")
    scale = min(1.0, profile.maximum_dimension / max(first.width, first.height))
    width = max(1, int(round(first.width * scale)))
    height = max(1, int(round(first.height * scale)))
    images: list[torch.Tensor] = []
    mvps: list[np.ndarray] = []
    with zipfile.ZipFile(dataset.bundle_path, "r") as archive:
        for frame in frames:
            try:
                image_bytes = archive.read(frame.image_path)
                with Image.open(io.BytesIO(image_bytes)) as source:
                    image = source.convert("RGB")
                    if image.size != (width, height):
                        image = image.resize((width, height), Image.Resampling.BILINEAR)
                    pixels = np.asarray(image, dtype=np.float32) / 255.0
            except Exception as exception:
                raise WorkerFailure(
                    "invalid_keyframe_image",
                    f"keyframe {frame.frame_id} JPEG decode failed: {exception}",
                ) from exception
            # QRS Texture2D pixels and intrinsics use a bottom-left image origin. DiffSoup
            # follows its upstream OpenGL loaders and expects row zero at that origin.
            pixels = np.ascontiguousarray(np.flip(pixels, axis=0))
            images.append(torch.from_numpy(pixels))
            mvps.append(_frame_mvp(frame, width, height))
    mvp_tensor = torch.from_numpy(np.stack(mvps)).to(device=device, dtype=torch.float32)
    return TrainingViews(
        mvp_tensor,
        torch.inverse(mvp_tensor).contiguous(),
        tuple(images),
        height,
        width,
        tuple(frame.frame_id for frame in frames),
    )


def _frame_mvp(frame: QrsFrame, output_width: int, output_height: int) -> np.ndarray:
    crop_scale = np.array(
        (frame.width / frame.sensor_width, frame.height / frame.sensor_height),
        dtype=np.float64,
    )
    crop_scale /= max(crop_scale)
    crop_size = np.array((frame.sensor_width, frame.sensor_height), dtype=np.float64) * crop_scale
    crop_min = (
        np.array((frame.sensor_width, frame.sensor_height), dtype=np.float64) - crop_size
    ) * 0.5
    image_scale = np.array((output_width, output_height), dtype=np.float64) / crop_size
    fx = frame.fx * image_scale[0]
    fy = frame.fy * image_scale[1]
    cx = (frame.cx - crop_min[0]) * image_scale[0]
    cy = (frame.cy - crop_min[1]) * image_scale[1]

    rotation = _quaternion_matrix(frame.rotation_xyzw)
    camera_from_chunk_unity = np.eye(4, dtype=np.float64)
    camera_from_chunk_unity[:3, :3] = rotation.T
    camera_from_chunk_unity[:3, 3] = -rotation.T @ np.asarray(frame.position)
    unity_to_opengl_camera = np.diag((1.0, 1.0, -1.0, 1.0))
    view = unity_to_opengl_camera @ camera_from_chunk_unity

    near, far = 0.05, 100.0
    projection = np.zeros((4, 4), dtype=np.float64)
    projection[0, 0] = 2.0 * fx / output_width
    projection[1, 1] = 2.0 * fy / output_height
    projection[0, 2] = 1.0 - 2.0 * cx / output_width
    projection[1, 2] = 2.0 * cy / output_height - 1.0
    projection[2, 2] = (far + near) / (near - far)
    projection[2, 3] = 2.0 * far * near / (near - far)
    projection[3, 2] = -1.0
    return (projection @ view).astype(np.float32)


def _quaternion_matrix(rotation: tuple[float, float, float, float]) -> np.ndarray:
    x, y, z, w = rotation
    norm = math.sqrt(x * x + y * y + z * z + w * w)
    x, y, z, w = x / norm, y / norm, z / norm, w / norm
    return np.array(
        (
            (1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)),
            (2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)),
            (2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)),
        ),
        dtype=np.float64,
    )


def _project_vertices(vertices: torch.Tensor, mvp: torch.Tensor) -> torch.Tensor:
    homogeneous = torch.cat((vertices, torch.ones_like(vertices[:, :1])), dim=-1)
    return torch.einsum("bij,nj->bni", mvp, homogeneous).contiguous()


def _initialize_state(
    dataset: QrsDataset,
    submission: JobSubmission,
    profile: TrainingProfile,
    compatibility: str,
    warm_artifact: Path | None,
    work_directory: Path,
    device: torch.device,
) -> TrainingState:
    fallback_reason: str | None = None
    if submission.warm_start is not None and warm_artifact is not None:
        try:
            return _load_warm_state(
                submission,
                profile,
                compatibility,
                warm_artifact,
                work_directory,
                device,
            )
        except WarmStartRejected as exception:
            if not submission.allow_fresh_fallback:
                raise
            fallback_reason = exception.message
            _progress(0.16, "warm-start incompatible; using verified fresh fallback")
    elif submission.warm_start is not None:
        if not submission.allow_fresh_fallback:
            raise WarmStartRejected("warm-start artifact is unavailable")
        fallback_reason = "warm-start artifact is unavailable"

    vertices_np, faces_np, colors_np = _mesh_arrays(dataset, profile)
    vertices = torch.from_numpy(vertices_np).to(device=device).requires_grad_(True)
    faces = torch.from_numpy(faces_np).to(device=device, dtype=torch.int32).contiguous()
    samples = ds.feats_at_level(profile.level)
    feature_source = torch.zeros(
        (faces.shape[0], samples, FEATURE_DIMENSION), dtype=torch.float32, device=device
    )
    if colors_np is not None:
        face_color = torch.from_numpy(colors_np).to(device=device).mean(dim=1)
        face_color = face_color.clamp(1.0 / 255.0, 254.0 / 255.0)
        color_logits = torch.logit(face_color)
        feature_source[..., :3] = color_logits[:, None, :]
        feature_source[..., 3] = torch.logit(
            torch.full((faces.shape[0], samples), 0.1, device=device)
        )
    feature_source.requires_grad_(True)
    alpha_source = torch.full(
        (faces.shape[0], samples, 1), 4.0, dtype=torch.float32, device=device,
        requires_grad=True,
    )
    color_mlp = ds.ColorMLP(
        input_dim=FEATURE_DIMENSION + 9,
        hidden_dim=MLP_HIDDEN_DIMENSION,
        n_layers=MLP_HIDDEN_LAYERS,
        output_dim=3,
    ).to(device=device)
    soup_optimizer, vertex_optimizer, shader_optimizer = _make_optimizers(
        vertices, feature_source, alpha_source, color_mlp, profile
    )
    return TrainingState(
        vertices,
        faces,
        feature_source,
        alpha_source,
        color_mlp,
        soup_optimizer,
        vertex_optimizer,
        shader_optimizer,
        0,
        False,
        None,
        fallback_reason,
    )


def _make_optimizers(
    vertices: torch.Tensor,
    feature_source: torch.Tensor,
    alpha_source: torch.Tensor,
    color_mlp: Any,
    profile: TrainingProfile,
) -> tuple[Any, Any, Any]:
    soup = torch.optim.Adam(
        (
            {"params": [feature_source], "lr": profile.feature_learning_rate},
            {"params": [alpha_source], "lr": profile.feature_learning_rate},
        )
    )
    vertex = ds.optimize.VectorAdam(params=[vertices], lr=profile.geometry_learning_rate)
    shader = torch.optim.Adam(color_mlp.parameters(), lr=profile.shader_learning_rate)
    return soup, vertex, shader


def _load_warm_state(
    submission: JobSubmission,
    profile: TrainingProfile,
    compatibility: str,
    artifact_path: Path,
    work_directory: Path,
    device: torch.device,
) -> TrainingState:
    warm = submission.warm_start
    if warm is None:
        raise WarmStartRejected("warm-start metadata is absent")
    if warm.compatibility_tag != compatibility:
        raise WarmStartRejected("warm-start compatibility tag differs from this worker")
    checkpoint_path = work_directory / (
        f"warm-{submission.job_id}-{uuid.uuid4().hex}.pt"
    )
    try:
        digest = hashlib.sha256()
        read = 0
        with zipfile.ZipFile(artifact_path, "r") as archive:
            try:
                info = archive.getinfo("checkpoint/resume.pt")
            except KeyError as exception:
                raise WarmStartRejected("source artifact has no resume checkpoint") from exception
            if info.file_size != warm.checkpoint.byte_length:
                raise WarmStartRejected("source checkpoint length differs from the request")
            with archive.open(info, "r") as source, checkpoint_path.open("xb") as target:
                while block := source.read(1024 * 1024):
                    read += len(block)
                    if read > warm.checkpoint.byte_length:
                        raise WarmStartRejected("source checkpoint exceeds its declaration")
                    digest.update(block)
                    target.write(block)
        if read != warm.checkpoint.byte_length or digest.hexdigest() != warm.checkpoint.sha256:
            raise WarmStartRejected("source checkpoint hash differs from the request")
        try:
            checkpoint = torch.load(
                checkpoint_path, map_location="cpu", weights_only=True
            )
        except Exception as exception:
            raise WarmStartRejected(f"source checkpoint could not be loaded: {exception}") from exception
    finally:
        checkpoint_path.unlink(missing_ok=True)

    if not isinstance(checkpoint, dict):
        raise WarmStartRejected("source checkpoint root is not an object")
    expected_literals = {
        "format": CHECKPOINT_FORMAT,
        "version": CHECKPOINT_VERSION,
        "compatibility_tag": compatibility,
        "profile": profile.name,
        "source_commit": _source_commit(),
        "world_id": submission.key.world_id,
        "chunk_id": submission.key.chunk_id,
        "chunk_revision": warm.source_revision,
        "level": profile.level,
        "feature_dimension": FEATURE_DIMENSION,
    }
    for field, expected in expected_literals.items():
        if checkpoint.get(field) != expected:
            raise WarmStartRejected(f"source checkpoint {field} is incompatible")
    required = {
        "vertices", "faces", "feature_source", "alpha_source", "color_mlp",
        "soup_optimizer", "vertex_optimizer", "shader_optimizer", "completed_steps",
    }
    if not required <= set(checkpoint):
        raise WarmStartRejected("source checkpoint is incomplete")

    vertices = _checkpoint_tensor(checkpoint["vertices"], torch.float32, 2, "vertices")
    faces = _checkpoint_tensor(checkpoint["faces"], torch.int32, 2, "faces")
    feature_source = _checkpoint_tensor(
        checkpoint["feature_source"], torch.float32, 3, "feature_source"
    )
    alpha_source = _checkpoint_tensor(
        checkpoint["alpha_source"], torch.float32, 3, "alpha_source"
    )
    if vertices.shape[1:] != (3,) or faces.shape[1:] != (3,):
        raise WarmStartRejected("source checkpoint mesh tensor shapes are invalid")
    expected_samples = ds.feats_at_level(profile.level)
    if feature_source.shape != (faces.shape[0], expected_samples, FEATURE_DIMENSION):
        raise WarmStartRejected("source checkpoint feature tensor shape is invalid")
    if alpha_source.shape != (faces.shape[0], expected_samples, 1):
        raise WarmStartRejected("source checkpoint alpha tensor shape is invalid")
    if faces.numel() == 0 or int(faces.min()) < 0 or int(faces.max()) >= vertices.shape[0]:
        raise WarmStartRejected("source checkpoint indices are invalid")

    vertices = vertices.to(device=device).contiguous().requires_grad_(True)
    faces = faces.to(device=device).contiguous()
    feature_source = feature_source.to(device=device).contiguous().requires_grad_(True)
    alpha_source = alpha_source.to(device=device).contiguous().requires_grad_(True)
    color_mlp = ds.ColorMLP(
        input_dim=FEATURE_DIMENSION + 9,
        hidden_dim=MLP_HIDDEN_DIMENSION,
        n_layers=MLP_HIDDEN_LAYERS,
        output_dim=3,
    ).to(device=device)
    try:
        color_mlp.load_state_dict(checkpoint["color_mlp"], strict=True)
        optimizers = _make_optimizers(
            vertices, feature_source, alpha_source, color_mlp, profile
        )
        for optimizer, field in zip(
            optimizers,
            ("soup_optimizer", "vertex_optimizer", "shader_optimizer"),
            strict=True,
        ):
            optimizer.load_state_dict(checkpoint[field])
    except Exception as exception:
        raise WarmStartRejected(f"source optimizer state is incompatible: {exception}") from exception
    completed_steps = checkpoint["completed_steps"]
    if isinstance(completed_steps, bool) or not isinstance(completed_steps, int) or completed_steps < 0:
        raise WarmStartRejected("source checkpoint step count is invalid")
    return TrainingState(
        vertices,
        faces,
        feature_source,
        alpha_source,
        color_mlp,
        optimizers[0],
        optimizers[1],
        optimizers[2],
        completed_steps,
        True,
        warm.source_revision,
        None,
    )


def _checkpoint_tensor(value: Any, dtype: torch.dtype, dimensions: int, label: str) -> torch.Tensor:
    if not isinstance(value, torch.Tensor) or value.dtype != dtype or value.ndim != dimensions:
        raise WarmStartRejected(f"source checkpoint {label} tensor is invalid")
    if value.numel() == 0:
        raise WarmStartRejected(f"source checkpoint {label} tensor is empty")
    if dtype.is_floating_point and not bool(torch.isfinite(value).all()):
        raise WarmStartRejected(f"source checkpoint {label} tensor is empty or non-finite")
    return value


def _train(
    state: TrainingState,
    views: TrainingViews,
    profile: TrainingProfile,
    seed: int,
) -> list[float]:
    generator = torch.Generator(device="cpu")
    generator.manual_seed(seed)
    order = torch.randperm(len(views.images), generator=generator).tolist()
    pointer = 0
    losses: list[float] = []
    visible_steps = 0
    base_rates = (
        tuple(group["lr"] for group in state.soup_optimizer.param_groups),
        tuple(group["lr"] for group in state.vertex_optimizer.param_groups),
        tuple(group["lr"] for group in state.shader_optimizer.param_groups),
    )
    report_every = max(1, profile.steps // 20)
    for local_step in range(1, profile.steps + 1):
        if pointer >= len(order):
            order = torch.randperm(len(views.images), generator=generator).tolist()
            pointer = 0
        view_index = order[pointer]
        pointer += 1
        multiplier = 0.05 ** (local_step / profile.steps)
        for optimizer, rates in zip(
            (state.soup_optimizer, state.vertex_optimizer, state.shader_optimizer),
            base_rates,
            strict=True,
        ):
            for group, base_rate in zip(optimizer.param_groups, rates, strict=True):
                group["lr"] = base_rate * multiplier

        mvp = views.mvps[view_index : view_index + 1]
        inverse_mvp = views.inverse_mvps[view_index : view_index + 1]
        ground_truth = views.images[view_index].to(
            device=state.vertices.device, dtype=torch.float32
        ).unsqueeze(0)
        clip_vertices = _project_vertices(state.vertices, mvp)
        accumulated_alpha = ds.accumulate_to_level(
            profile.level, profile.level, state.alpha_source
        ).sigmoid()
        accumulated_features = ds.accumulate_to_level(
            profile.level, profile.level, state.feature_source
        ).sigmoid()
        raster = ds.rasterize_multires_triangle_alpha(
            (views.height, views.width),
            clip_vertices,
            state.faces,
            level=profile.level,
            alpha_src=accumulated_alpha,
        )
        mask = raster[..., -1] > 0
        mask_count = int(mask.sum().detach().cpu())
        if mask_count == 0:
            continue
        visible_steps += 1
        features = ds.multires_triangle_color(
            raster, level=profile.level, feat=accumulated_features
        ).view(1, views.height, views.width, FEATURE_DIMENSION)
        features = torch.cat(
            (features, ds.encode_view_dir_sh2(raster, inverse_mvp)), dim=-1
        )
        color = state.color_mlp.forward(features, mask=mask)
        auxiliary = ds.opacity_aux_loss(
            color.detach(),
            ground_truth,
            raster,
            clip_vertices,
            state.faces,
            level=profile.level,
            alpha_src=accumulated_alpha,
        )
        color = ds.edge_grad(color, raster, clip_vertices, state.faces)
        mask_float = mask[..., None].float()
        photometric = (
            (ground_truth - color).abs() * mask_float
        ).sum() / (mask_float.sum() * 3.0).clamp_min(1.0)
        opacity_regularizer = (1.0 - accumulated_alpha).mean() * 1.0e-4
        loss = photometric + auxiliary + opacity_regularizer

        state.soup_optimizer.zero_grad(set_to_none=True)
        state.vertex_optimizer.zero_grad(set_to_none=True)
        state.shader_optimizer.zero_grad(set_to_none=True)
        loss.backward()
        state.soup_optimizer.step()
        state.vertex_optimizer.step()
        state.shader_optimizer.step()
        loss_value = float(loss.detach().cpu())
        if not math.isfinite(loss_value):
            raise WorkerFailure("non_finite_optimization", "DiffSoup loss became non-finite")
        losses.append(loss_value)
        if local_step == 1 or local_step % report_every == 0 or local_step == profile.steps:
            _progress(
                0.25 + 0.6 * local_step / profile.steps,
                f"optimizing step {local_step}/{profile.steps}; loss={loss_value:.6f}",
            )
    if visible_steps == 0:
        raise WorkerFailure(
            "no_visible_geometry",
            "none of the selected QRS cameras sees the uploaded chunk mesh",
        )
    state.completed_steps += profile.steps
    return losses


def _write_artifact(
    output_path: Path,
    work_directory: Path,
    submission: JobSubmission,
    profile: TrainingProfile,
    state: TrainingState,
    source_commit: str,
    compatibility: str,
    views: TrainingViews,
    losses: list[float],
) -> BlobDescriptor:
    model_directory = work_directory / "model"
    checkpoint_directory = work_directory / "checkpoint"
    model_directory.mkdir(parents=True, exist_ok=True)
    checkpoint_directory.mkdir(parents=True, exist_ok=True)
    with torch.no_grad():
        feature_accumulated = ds.accumulate_to_level(
            profile.level, profile.level, state.feature_source
        ).sigmoid().detach().cpu().numpy().astype(np.float32)
        alpha_accumulated = ds.accumulate_to_level(
            profile.level, profile.level, state.alpha_source
        ).sigmoid().detach().cpu().numpy().astype(np.float32)
    vertices = state.vertices.detach().cpu().numpy().astype(np.float32)
    faces = state.faces.detach().cpu().numpy().astype(np.int32)

    ply_path = model_directory / "mesh.ply"
    _write_ply(ply_path, vertices, faces)
    lut = _pack_lut(feature_accumulated, alpha_accumulated, faces.shape[0], profile.level)
    lut0_path = model_directory / "lut0.png"
    lut1_path = model_directory / "lut1.png"
    Image.fromarray((lut[..., :4] * 255.0).clip(0, 255).astype(np.uint8), "RGBA").save(lut0_path)
    Image.fromarray((lut[..., 4:] * 255.0).clip(0, 255).astype(np.uint8), "RGBA").save(lut1_path)
    mlp_path = model_directory / "mlp_weights.json"
    _write_json(mlp_path, _mlp_wire(state.color_mlp.state_dict()))
    meta_path = model_directory / "meta.json"
    _write_json(
        meta_path,
        {
            "up": [0.0, 1.0, 0.0],
            "level": profile.level,
            "background": [0.0, 0.0, 0.0],
            "num_faces": int(faces.shape[0]),
            "num_verts": int(vertices.shape[0]),
            "coordinateSystem": "unity-lh-y-up-z-forward",
            "frontFace": "clockwise",
            "profile": profile.name,
            "sourceFrameIds": list(views.source_frame_ids),
            "warmStartUsed": state.warm_start_used,
            "warmSourceRevision": state.warm_source_revision,
            "freshFallbackReason": state.fresh_fallback_reason,
            "completedSteps": state.completed_steps,
            "finalLoss": losses[-1] if losses else None,
        },
    )
    checkpoint_path = checkpoint_directory / "resume.pt"
    torch.save(
        {
            "format": CHECKPOINT_FORMAT,
            "version": CHECKPOINT_VERSION,
            "compatibility_tag": compatibility,
            "profile": profile.name,
            "source_commit": source_commit,
            "world_id": submission.key.world_id,
            "chunk_id": submission.key.chunk_id,
            "chunk_revision": submission.key.chunk_revision,
            "level": profile.level,
            "feature_dimension": FEATURE_DIMENSION,
            "vertices": state.vertices.detach().cpu(),
            "faces": state.faces.detach().cpu(),
            "feature_source": state.feature_source.detach().cpu(),
            "alpha_source": state.alpha_source.detach().cpu(),
            "color_mlp": _cpu_state(state.color_mlp.state_dict()),
            "soup_optimizer": _cpu_state(state.soup_optimizer.state_dict()),
            "vertex_optimizer": _cpu_state(state.vertex_optimizer.state_dict()),
            "shader_optimizer": _cpu_state(state.shader_optimizer.state_dict()),
            "completed_steps": state.completed_steps,
            "torch_rng_state": torch.get_rng_state(),
            "cuda_rng_state": tuple(torch.cuda.get_rng_state_all()),
        },
        checkpoint_path,
    )

    role_paths = {
        "mesh": (ply_path, "model/mesh.ply", "application/vnd.questinfinitescan.diffsoup-mesh"),
        "lut0": (lut0_path, "model/lut0.png", "image/png"),
        "lut1": (lut1_path, "model/lut1.png", "image/png"),
        "mlp": (mlp_path, "model/mlp_weights.json", "application/json"),
        "meta": (meta_path, "model/meta.json", "application/json"),
        "checkpoint": (
            checkpoint_path,
            "checkpoint/resume.pt",
            "application/vnd.questinfinitescan.diffsoup-checkpoint",
        ),
    }
    artifact_files = tuple(
        ArtifactFile(
            role,
            archive_path,
            media_type,
            1,
            source_path.stat().st_size,
            _file_sha256(source_path),
        )
        for role, (source_path, archive_path, media_type) in role_paths.items()
    )
    manifest = DiffSoupArtifactManifest(
        submission.key,
        submission.request_fingerprint,
        source_commit,
        compatibility,
        profile.level,
        int(vertices.shape[0]),
        int(faces.shape[0]),
        int(lut.shape[1]),
        int(lut.shape[0]),
        artifact_files,
    )
    manifest_bytes = json.dumps(
        manifest.to_wire(), sort_keys=True, separators=(",", ":"), allow_nan=False
    ).encode("utf-8")
    temporary = work_directory / f"artifact-{uuid.uuid4().hex}.zip"
    try:
        with zipfile.ZipFile(temporary, "w", compression=zipfile.ZIP_STORED) as archive:
            _zip_bytes(archive, "artifact.json", manifest_bytes)
            for _, (source_path, archive_path, _) in sorted(role_paths.items()):
                info = zipfile.ZipInfo(archive_path, date_time=(1980, 1, 1, 0, 0, 0))
                info.compress_type = zipfile.ZIP_STORED
                info.external_attr = 0o100600 << 16
                with source_path.open("rb") as source, archive.open(info, "w") as target:
                    while block := source.read(1024 * 1024):
                        target.write(block)
        with temporary.open("rb") as stream:
            os.fsync(stream.fileno())
        output_path.parent.mkdir(parents=True, exist_ok=True)
        os.replace(temporary, output_path)
        _fsync_directory(output_path.parent)
    finally:
        temporary.unlink(missing_ok=True)
    return BlobDescriptor(
        "application/vnd.questinfinitescan.diffsoup+zip",
        ARTIFACT_FORMAT_VERSION,
        output_path.stat().st_size,
        _file_sha256(output_path),
    )


def _cpu_state(value: Any) -> Any:
    if isinstance(value, torch.Tensor):
        return value.detach().cpu()
    if isinstance(value, dict):
        return {key: _cpu_state(item) for key, item in value.items()}
    if isinstance(value, list):
        return [_cpu_state(item) for item in value]
    if isinstance(value, tuple):
        return tuple(_cpu_state(item) for item in value)
    return value


def _write_ply(path: Path, vertices: np.ndarray, faces: np.ndarray) -> None:
    header = (
        "ply\nformat binary_little_endian 1.0\n"
        f"element vertex {vertices.shape[0]}\n"
        "property float x\nproperty float y\nproperty float z\n"
        f"element face {faces.shape[0]}\n"
        "property list uchar int vertex_indices\nend_header\n"
    ).encode("ascii")
    with path.open("wb") as stream:
        stream.write(header)
        stream.write(vertices.astype("<f4", copy=False).tobytes())
        for face in faces:
            stream.write(struct.pack("<B3i", 3, int(face[0]), int(face[1]), int(face[2])))
        stream.flush()
        os.fsync(stream.fileno())


def _pack_lut(features: np.ndarray, alpha: np.ndarray, faces: int, level: int) -> np.ndarray:
    samples = ds.feats_at_level(level)
    count = faces * samples
    feature_flat = features.reshape(-1, FEATURE_DIMENSION)
    alpha_flat = alpha.reshape(-1, 1)
    if feature_flat.shape[0] != count or alpha_flat.shape[0] != count:
        raise WorkerFailure("artifact_export_failed", "DiffSoup feature tensor shape is inconsistent")
    width = min(4_096, count)
    height = math.ceil(count / width)
    if height > 8_192:
        raise WorkerFailure("artifact_export_failed", "DiffSoup LUT exceeds the V1 dimension limit")
    result = np.zeros((height * width, 8), dtype=np.float32)
    result[:count] = np.concatenate((feature_flat, alpha_flat), axis=1)
    return result.reshape(height, width, 8)


def _mlp_wire(state: dict[str, torch.Tensor]) -> dict[str, list[float]]:
    weights = [value.detach().cpu().numpy().astype(np.float32) for key, value in state.items()
               if key.endswith("weight")]
    biases = [value.detach().cpu().numpy().astype(np.float32) for key, value in state.items()
              if key.endswith("bias")]
    if [tuple(value.shape) for value in weights] != [(16, 16), (16, 16), (3, 16)]:
        raise WorkerFailure("artifact_export_failed", "DiffSoup MLP weight shapes are unsupported")
    if [tuple(value.shape) for value in biases] != [(16,), (16,), (3,)]:
        raise WorkerFailure("artifact_export_failed", "DiffSoup MLP bias shapes are unsupported")
    return {
        "W1": weights[0].ravel().tolist(),
        "b1": biases[0].ravel().tolist(),
        "W2": weights[1].ravel().tolist(),
        "b2": biases[1].ravel().tolist(),
        "W3": weights[2].ravel().tolist(),
        "b3": biases[2].ravel().tolist(),
    }


def _write_json(path: Path, value: Any) -> None:
    encoded = json.dumps(value, sort_keys=True, separators=(",", ":"), allow_nan=False).encode(
        "utf-8"
    )
    with path.open("wb") as stream:
        stream.write(encoded)
        stream.flush()
        os.fsync(stream.fileno())


def _zip_bytes(archive: zipfile.ZipFile, name: str, data: bytes) -> None:
    info = zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
    info.compress_type = zipfile.ZIP_STORED
    info.external_attr = 0o100600 << 16
    archive.writestr(info, data)


def _file_sha256(path: Path) -> str:
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


def run_job(arguments: argparse.Namespace) -> None:
    if not torch.cuda.is_available():
        raise WorkerFailure("cuda_unavailable", "PyTorch cannot access a CUDA device")
    submission = _load_submission(Path(arguments.submission))
    profile = _profile(submission.profile)
    source_commit = _source_commit()
    compatibility = compatibility_tag(profile, source_commit)
    seed = int(submission.job_id[:16], 16) & 0x7FFF_FFFF
    random.seed(seed)
    np.random.seed(seed)
    torch.manual_seed(seed)
    torch.cuda.manual_seed_all(seed)
    device = torch.device("cuda")
    _progress(0.03, "validating QRS chunk bundle")
    try:
        dataset = load_qrs_dataset(Path(arguments.input), submission.key)
    except QrsDatasetError as exception:
        raise WorkerFailure("invalid_qrs_dataset", str(exception)) from exception
    _progress(0.10, "converting Unity chunk-local mesh and cameras")
    views = _load_views(dataset, profile, device)
    state = _initialize_state(
        dataset,
        submission,
        profile,
        compatibility,
        Path(arguments.warm_artifact) if arguments.warm_artifact else None,
        Path(arguments.work_dir),
        device,
    )
    _progress(
        0.20,
        f"DiffSoup CUDA ready: {state.faces.shape[0]} triangles, {len(views.images)} views",
    )
    try:
        losses = _train(state, views, profile, seed)
        torch.cuda.synchronize()
    except torch.OutOfMemoryError as exception:
        torch.cuda.empty_cache()
        raise WorkerFailure("cuda_out_of_memory", "DiffSoup exhausted CUDA memory") from exception
    _progress(0.88, "exporting canonical triangle soup and resume checkpoint")
    descriptor = _write_artifact(
        Path(arguments.output),
        Path(arguments.work_dir),
        submission,
        profile,
        state,
        source_commit,
        compatibility,
        views,
        losses,
    )
    _emit(
        "result",
        artifactPath=str(Path(arguments.output).resolve()),
        descriptor=descriptor.to_wire(),
        compatibilityTag=compatibility,
        warmStartUsed=state.warm_start_used,
        freshFallbackReason=state.fresh_fallback_reason,
    )


def probe_cuda() -> None:
    if not torch.cuda.is_available():
        raise WorkerFailure("cuda_unavailable", "PyTorch cannot access a CUDA device")
    device = torch.device("cuda")
    positions = torch.tensor(
        [[[-0.6, -0.6, 0.2, 1.0], [0.6, -0.6, 0.2, 1.0], [0.0, 0.6, 0.2, 1.0]]],
        device=device,
        dtype=torch.float32,
    ).contiguous()
    faces = torch.tensor([[0, 1, 2]], device=device, dtype=torch.int32).contiguous()
    alpha = torch.ones(
        (1, ds.feats_at_level(0), 1), device=device, dtype=torch.float32
    ).contiguous()
    raster = ds.rasterize_multires_triangle_alpha(
        (64, 64), positions, faces, level=0, alpha_src=alpha, stochastic=False
    )
    torch.cuda.synchronize()
    covered = int((raster[..., -1] > 0).sum().cpu())
    if covered <= 0:
        raise WorkerFailure("cuda_probe_failed", "DiffSoup rasterizer returned no fragments")
    _emit(
        "probe",
        torchVersion=torch.__version__,
        cudaVersion=torch.version.cuda,
        device=torch.cuda.get_device_name(0),
        coveredPixels=covered,
        sourceCommit=_source_commit(),
    )


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="QuestInfiniteScan DiffSoup CUDA worker")
    subparsers = parser.add_subparsers(dest="command", required=True)
    subparsers.add_parser("probe")
    run = subparsers.add_parser("run")
    run.add_argument("--submission", required=True)
    run.add_argument("--input", required=True)
    run.add_argument("--output", required=True)
    run.add_argument("--work-dir", required=True)
    run.add_argument("--warm-artifact")
    return parser


def main() -> None:
    arguments = _parser().parse_args()
    try:
        if arguments.command == "probe":
            probe_cuda()
        else:
            run_job(arguments)
    except WorkerFailure as exception:
        _emit("error", code=exception.code, message=exception.message)
        raise SystemExit(2) from None
    except Exception as exception:
        _emit(
            "error",
            code="worker_internal_error",
            message=f"{type(exception).__name__}: {exception}",
        )
        raise SystemExit(3) from None


if __name__ == "__main__":
    main()
