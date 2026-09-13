#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "Tools" / "KataGoModelConverter"))
sys.path.insert(0, str(ROOT / "Tools" / "NNReference"))
from katago_nn_reference import compare_tensor_maps, infer, load_baked_tensors, load_raw_tensors  # noqa: E402
from run_production_reference import summarize_output, write_array_output  # noqa: E402


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--model-dir", type=Path, required=True)
    parser.add_argument("--raw-model", type=Path, required=True)
    parser.add_argument("--fixture", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()

    baked, baked_meta = load_baked_tensors(args.model_dir)
    raw, raw_meta = load_raw_tensors(args.raw_model)
    tensor_compare = compare_tensor_maps(raw, baked)
    if not tensor_compare["exact"]:
        raise ValueError("raw and baked tensor maps are not byte-exact")
    fixture_set = json.loads(args.fixture.read_text(encoding="utf-8"))
    fixtures = fixture_set.get("fixtures", [])
    if not fixtures:
        raise ValueError(f"fixture set is empty: {args.fixture}")

    args.output_dir.mkdir(parents=True, exist_ok=True)
    manifest_rows = []
    for fixture in fixtures:
        name = fixture["name"]
        spatial = np.asarray(fixture["spatial"], dtype=np.float32)
        global_features = np.asarray(fixture["global"], dtype=np.float32)
        if spatial.size != 22 * 19 * 19 or global_features.size != 19:
            raise ValueError(f"{name}: invalid feature array sizes")
        output = infer(spatial, global_features, baked, capture=True)
        array_path = args.output_dir / f"{name}.pugnn"
        write_array_output(array_path, output)
        manifest_rows.append({
            "name": name,
            "array": array_path.name,
            "tensor_count": tensor_compare["tensor_count"],
            "summary": summarize_output(output),
        })

    manifest = {
        "schema": "pure-udon-go.nn-reference-fixture-corpus.v1",
        "fixture": str(args.fixture),
        "model_name": raw_meta["architecture"]["model_name"],
        "model_version": raw_meta["architecture"]["model_version"],
        "baked_texture": {
            "width": baked_meta["width"],
            "height": baked_meta["height"],
            "sha256": baked_meta["packing"]["texture"]["sha256"],
        },
        "raw_vs_baked_tensor_bytes": tensor_compare,
        "cases": manifest_rows,
    }
    (args.output_dir / "manifest.json").write_text(
        json.dumps(manifest, indent=2, sort_keys=True) + "\n", encoding="utf-8"
    )
    print(f"NN_REFERENCE_CORPUS_PASS cases={len(manifest_rows)} tensor_exact={tensor_compare['exact']}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
