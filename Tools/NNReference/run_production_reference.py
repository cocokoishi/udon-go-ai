#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import struct
import sys
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "Tests"))
from Reference.katago_features_v7 import AREA, BLACK, EMPTY, NONE, encode  # noqa: E402
from katago_nn_reference import compare_tensor_maps, infer, load_baked_tensors, load_raw_tensors  # noqa: E402


def summarize_output(output: dict) -> dict:
    policy = output["policy_spatial"].reshape(AREA)
    top = np.argsort(policy)[-10:][::-1]
    return {
        "policy_top10": [{"loc": int(loc), "logit": float(policy[loc])} for loc in top],
        "policy_pass_logit": float(output["policy_pass"]),
        "value_logits": [float(x) for x in output["value"]],
        "score_logits": [float(x) for x in output["score"]],
        "ownership": {
            "min": float(np.min(output["ownership"])),
            "max": float(np.max(output["ownership"])),
            "mean": float(np.mean(output["ownership"], dtype=np.float64)),
        },
        "checkpoints": output["checkpoints"],
    }


def write_array_output(path: Path, output: dict) -> None:
    arrays = output["checkpoint_arrays"]
    records = [
        ("initial_trunk", arrays["initial_trunk"]),
        ("rconv1", arrays["rconv1"]),
        ("rconv2", arrays["rconv2"]),
        ("rconv3", arrays["rconv3"]),
        ("rconv4", arrays["rconv4"]),
        ("rconv5", arrays["rconv5"]),
        ("rconv6", arrays["rconv6"]),
        ("rconv7", arrays["rconv7"]),
        ("rconv8", arrays["rconv8"]),
        ("rconv9", arrays["rconv9"]),
        ("rconv10", arrays["rconv10"]),
        ("trunk_tip", arrays["trunk_tip"]),
        ("policy_spatial", output["policy_spatial"]),
        ("policy_pass", np.asarray([output["policy_pass"]], dtype=np.float32)),
        ("value", output["value"]),
        ("score", output["score"]),
        ("ownership", output["ownership"]),
    ]
    with path.open("wb") as stream:
        stream.write(b"PUGNN01\0")
        stream.write(struct.pack("<I", len(records)))
        for name, value in records:
            encoded_name = name.encode("ascii")
            array = np.ascontiguousarray(value, dtype="<f4")
            stream.write(struct.pack("<I", len(encoded_name)))
            stream.write(encoded_name)
            stream.write(struct.pack("<I", array.size))
            stream.write(array.tobytes(order="C"))


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--model-dir", type=Path, required=True)
    parser.add_argument("--raw-model", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--array-output", type=Path)
    args = parser.parse_args()

    baked, baked_meta = load_baked_tensors(args.model_dir)
    raw, raw_meta = load_raw_tensors(args.raw_model)
    tensor_compare = compare_tensor_maps(raw, baked)

    board = [EMPTY] * AREA
    recent_locs = [NONE] * 5
    recent_pla = [EMPTY] * 5
    banned = [False] * AREA
    spatial, global_features = encode(
        board,
        board,
        board,
        recent_locs,
        recent_pla,
        BLACK,
        NONE,
        15,
        True,
        True,
        True,
        0,
        superko_banned=banned,
    )
    output = infer(np.asarray(spatial, dtype=np.float32), np.asarray(global_features, dtype=np.float32), baked, capture=True)
    result = {
        "schema": "pure-udon-go.nn-reference.v1",
        "source": {
            "model_name": raw_meta["architecture"]["model_name"],
            "model_version": raw_meta["architecture"]["model_version"],
            "architecture_sha256": raw_meta["architecture_sha256"],
        },
        "baked_texture": {
            "width": baked_meta["width"],
            "height": baked_meta["height"],
            "sha256": baked_meta["packing"]["texture"]["sha256"],
        },
        "raw_vs_baked_tensor_bytes": tensor_compare,
        "input": {
            "spatial_channels": 22,
            "global_channels": 19,
            "nonzero_spatial": int(np.count_nonzero(spatial)),
            "global": [float(x) for x in global_features],
        },
        "output": summarize_output(output),
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    if args.array_output is not None:
        args.array_output.parent.mkdir(parents=True, exist_ok=True)
        write_array_output(args.array_output, output)
    print("NN_REFERENCE_PASS")
    print(f"tensor_exact={tensor_compare['exact']}")
    print(f"tensor_count={tensor_compare['tensor_count']}")
    print(f"policy_pass_logit={result['output']['policy_pass_logit']:.8f}")
    print(f"value_logits={result['output']['value_logits']}")
    print(f"score_logits={result['output']['score_logits']}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
