#!/usr/bin/env python3
"""CPU/shader-layout reference for the bundled model-version-8 network.

This is a developer/reference path, not an end-user inference dependency. It
keeps the raw tensor order used by the packed RGBA atlas and mirrors the
operations in the bundled ``eigenbackend.cpp``: zero-padded convolution,
merged BN+ReLU, residual/gpool blocks, policy head, value/score head and
ownership head.
"""
from __future__ import annotations

import gzip
import hashlib
import json
import sys
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "Tools" / "KataGoModelConverter"))
from katago_model_parser import parse_model  # noqa: E402
from verify_weights import read_rgba32f_exr  # noqa: E402

SIZE = 19
AREA = SIZE * SIZE


def _tensor_map(tensors: dict[str, np.ndarray], name: str, shape: tuple[int, ...] | None = None) -> np.ndarray:
    value = tensors[name]
    if shape is not None and value.shape != shape:
        raise ValueError(f"{name}: expected shape {shape}, got {value.shape}")
    return value


def load_baked_tensors(model_dir: Path) -> tuple[dict[str, np.ndarray], dict]:
    packing = json.loads((model_dir / "packing_manifest.json").read_text(encoding="utf-8"))
    width, height, atlas = read_rgba32f_exr(model_dir / packing["texture"]["file"])
    floats = np.frombuffer(atlas, dtype="<f4")
    tensors = {}
    for entry in packing["tensors"]:
        start = entry["atlas_float_offset"]
        end = start + entry["atlas_float_count"]
        tensors[entry["name"]] = floats[start:end].reshape(tuple(entry["shape"]))
    return tensors, {"width": width, "height": height, "packing": packing}


def load_raw_tensors(model_path: Path) -> tuple[dict[str, np.ndarray], dict]:
    parsed = parse_model(model_path)
    raw = gzip.decompress(model_path.read_bytes())
    tensors = {}
    for entry in parsed["tensors"]:
        start = entry["decompressed_offset"]
        end = start + entry["byte_count"]
        tensors[entry["name"]] = np.frombuffer(raw[start:end], dtype="<f4").reshape(tuple(entry["shape"]))
    return tensors, parsed


def compare_tensor_maps(left: dict[str, np.ndarray], right: dict[str, np.ndarray]) -> dict:
    if set(left) != set(right):
        raise ValueError("tensor name sets differ")
    max_abs = 0.0
    exact = True
    first_difference = None
    for name in left:
        if left[name].shape != right[name].shape:
            raise ValueError(f"{name}: tensor shape differs")
        if not np.array_equal(left[name], right[name]):
            exact = False
            diff = np.abs(left[name].astype(np.float64) - right[name].astype(np.float64))
            local = float(np.max(diff))
            if local > max_abs:
                max_abs = local
            if first_difference is None:
                indices = np.argwhere(left[name] != right[name])[0]
                first_difference = {"name": name, "index": indices.tolist()}
    return {"tensor_count": len(left), "exact": exact, "max_abs": max_abs, "first_difference": first_difference}


def _conv(x: np.ndarray, weights: np.ndarray) -> np.ndarray:
    ky, kx, in_channels, out_channels = weights.shape
    if x.shape[0] != in_channels:
        raise ValueError(f"convolution input channels {x.shape[0]} != {in_channels}")
    h, w = x.shape[1:]
    out = np.zeros((out_channels, h, w), dtype=np.float32)
    radius_y, radius_x = ky // 2, kx // 2
    for dy in range(ky):
        y0 = max(0, radius_y - dy)
        y1 = min(h, h + radius_y - dy)
        if y0 >= y1:
            continue
        for dx in range(kx):
            x0 = max(0, radius_x - dx)
            x1 = min(w, w + radius_x - dx)
            if x0 >= x1:
                continue
            patch = x[:, y0 + dy - radius_y : y1 + dy - radius_y, x0 + dx - radius_x : x1 + dx - radius_x]
            contribution = weights[dy, dx].T @ patch.reshape(in_channels, -1).astype(np.float32)
            out[:, y0:y1, x0:x1] += contribution.reshape(out_channels, y1 - y0, x1 - x0)
    return out


def _merged_bn(x: np.ndarray, tensors: dict[str, np.ndarray], prefix: str, epsilon: float = 0.001) -> np.ndarray:
    mean = _tensor_map(tensors, prefix + ".mean")
    variance = _tensor_map(tensors, prefix + ".variance")
    scale = tensors.get(prefix + ".scale")
    bias = tensors.get(prefix + ".bias")
    if scale is None:
        scale = np.ones_like(mean)
    if bias is None:
        bias = np.zeros_like(mean)
    merged_scale = scale / np.sqrt(variance + np.float32(epsilon))
    merged_bias = bias - merged_scale * mean
    return np.maximum(x * merged_scale[:, None, None] + merged_bias[:, None, None], 0.0).astype(np.float32)


def _matmul(values: np.ndarray, weights: np.ndarray) -> np.ndarray:
    # Source/model layout is input-channel × output-channel.
    return np.asarray(values @ weights, dtype=np.float32)


def _gpool(values: np.ndarray) -> np.ndarray:
    mean = np.mean(values, axis=(1, 2), dtype=np.float32)
    maximum = np.max(values, axis=(1, 2))
    scale = np.float32((np.sqrt(values.shape[1] * values.shape[2]) - 14.0) * 0.1)
    return np.concatenate((mean, mean * scale, maximum)).astype(np.float32)


def _value_pool(values: np.ndarray) -> np.ndarray:
    mean = np.mean(values, axis=(1, 2), dtype=np.float32)
    scale = np.float32((np.sqrt(values.shape[1] * values.shape[2]) - 14.0) * 0.1)
    return np.concatenate((mean, mean * scale, mean * (scale * scale - 0.1))).astype(np.float32)


def infer(spatial: np.ndarray, global_features: np.ndarray, tensors: dict[str, np.ndarray], capture: bool = False) -> dict:
    spatial = np.asarray(spatial, dtype=np.float32).reshape(22, SIZE, SIZE)
    global_features = np.asarray(global_features, dtype=np.float32).reshape(19)
    checkpoints: dict[str, dict] = {}
    checkpoint_arrays: dict[str, np.ndarray] = {}

    def checkpoint(name: str, value: np.ndarray) -> None:
        if capture:
            captured = np.ascontiguousarray(value, dtype=np.float32)
            raw = captured.tobytes()
            checkpoints[name] = {
                "shape": list(value.shape),
                "sha256": hashlib.sha256(raw).hexdigest(),
                "min": float(np.min(value)),
                "max": float(np.max(value)),
                "mean": float(np.mean(value, dtype=np.float64)),
            }
            checkpoint_arrays[name] = captured.copy()

    trunk = _conv(spatial, _tensor_map(tensors, "conv1"))
    trunk += _matmul(global_features, _tensor_map(tensors, "ginputw"))[:, None, None]
    checkpoint("initial_trunk", trunk)

    for block_index in range(1, 11):
        name = f"rconv{block_index}"
        pre = _merged_bn(trunk, tensors, name + "/norm1")
        if name in ("rconv5", "rconv8"):
            regular = _conv(pre, _tensor_map(tensors, name + "/w1a"))
            gpool = _conv(pre, _tensor_map(tensors, name + "/w1b"))
            gpool = _merged_bn(gpool, tensors, name + "/norm1b")
            regular += _matmul(_gpool(gpool), _tensor_map(tensors, name + "/w1r"))[:, None, None]
            residual = _conv(_merged_bn(regular, tensors, name + "/norm2"), _tensor_map(tensors, name + "/w2"))
        else:
            residual = _conv(pre, _tensor_map(tensors, name + "/w1"))
            residual = _conv(_merged_bn(residual, tensors, name + "/norm2"), _tensor_map(tensors, name + "/w2"))
        trunk = (trunk + residual).astype(np.float32)
        checkpoint(name, trunk)

    trunk = _merged_bn(trunk, tensors, "trunk/norm")
    checkpoint("trunk_tip", trunk)

    p1 = _conv(trunk, _tensor_map(tensors, "p1/intermediate_conv/w"))
    g1 = _merged_bn(_conv(trunk, _tensor_map(tensors, "g1/w")), tensors, "g1/norm")
    gconcat = _gpool(g1)
    p1 += _matmul(gconcat, _tensor_map(tensors, "matmulg2w"))[:, None, None]
    policy_spatial = _conv(_merged_bn(p1, tensors, "p1/norm"), _tensor_map(tensors, "p2/w"))[0]
    policy_pass = float(_matmul(gconcat, _tensor_map(tensors, "matmulpass"))[0])

    v1 = _merged_bn(_conv(trunk, _tensor_map(tensors, "v1/w")), tensors, "v1/norm")
    v2 = _matmul(_value_pool(v1), _tensor_map(tensors, "v2/w")) + _tensor_map(tensors, "v2/b")
    v2 = np.maximum(v2, 0.0).astype(np.float32)
    value = _matmul(v2, _tensor_map(tensors, "v3/w")) + _tensor_map(tensors, "v3/b")
    score = _matmul(v2, _tensor_map(tensors, "sv3/w")) + _tensor_map(tensors, "sv3/b")
    ownership = _conv(v1, _tensor_map(tensors, "vownership/w"))[0]
    checkpoint("policy_spatial", policy_spatial)
    checkpoint("ownership", ownership)

    return {
        "policy_spatial": policy_spatial.astype(np.float32),
        "policy_pass": np.float32(policy_pass),
        "value": np.asarray(value, dtype=np.float32),
        "score": np.asarray(score, dtype=np.float32),
        "ownership": ownership.astype(np.float32),
        "checkpoints": checkpoints,
        "checkpoint_arrays": checkpoint_arrays,
    }
