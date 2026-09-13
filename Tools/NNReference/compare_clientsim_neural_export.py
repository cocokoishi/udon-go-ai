#!/usr/bin/env python3
"""Compare raw Udon ClientSim root outputs with the baked-model CPU graph.

The ClientSim export contains the exact feature arrays captured immediately
before each root search.  This tool runs the independent CPU implementation
over those arrays and compares the raw policy, value, score, and ownership
heads.  It does not compare searched move visits and it does not use KataGo's
post-search ``moveInfos`` as a neural-network oracle.
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
import sys

sys.path.insert(0, str(ROOT / "Tools" / "KataGoModelConverter"))
from katago_nn_reference import (  # noqa: E402
    compare_tensor_maps,
    infer,
    load_baked_tensors,
    load_raw_tensors,
)


HEADS = (
    ("policy_spatial", "rootPolicySpatial", 361),
    ("policy_pass", "rootPolicyPass", 1),
    ("value", "rootValue", 3),
    ("score", "rootScore", 4),
    ("ownership", "rootOwnership", 361),
)


def compare_array(expected: np.ndarray, observed: np.ndarray, atol: float, rtol: float) -> dict:
    expected = np.asarray(expected, dtype=np.float32).reshape(-1)
    observed = np.asarray(observed, dtype=np.float32).reshape(-1)
    if expected.shape != observed.shape:
        return {
            "count": int(expected.size),
            "observed_count": int(observed.size),
            "max_abs": None,
            "mean_abs": None,
            "rmse": None,
            "mismatch_count": int(max(expected.size, observed.size)),
            "status": "SHAPE_MISMATCH",
        }
    difference = np.abs(expected.astype(np.float64) - observed.astype(np.float64))
    finite = np.isfinite(expected) & np.isfinite(observed)
    close = np.isclose(observed, expected, atol=atol, rtol=rtol) & finite
    return {
        "count": int(expected.size),
        "max_abs": float(np.max(difference)) if expected.size else 0.0,
        "mean_abs": float(np.mean(difference, dtype=np.float64)) if expected.size else 0.0,
        "rmse": float(np.sqrt(np.mean(np.square(difference), dtype=np.float64)))
        if expected.size
        else 0.0,
        "mismatch_count": int(np.count_nonzero(~close)),
        "status": "PASS" if bool(np.all(close)) else "FAIL",
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--actual", type=Path, required=True)
    parser.add_argument("--model-dir", type=Path, required=True)
    parser.add_argument("--raw-model", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--atol", type=float, default=1e-4)
    parser.add_argument("--rtol", type=float, default=1e-5)
    args = parser.parse_args()

    actual = json.loads(args.actual.read_text(encoding="utf-8"))
    if actual.get("schema") != "pure-udon-go.clientsim-neural-output-export.v2":
        raise ValueError(f"unexpected ClientSim export schema: {actual.get('schema')}")
    if actual.get("numericComparison") != "NOT_PERFORMED_BY_THIS_VERIFIER":
        raise ValueError("the input export is not an un-compared neural output export")

    baked, baked_meta = load_baked_tensors(args.model_dir)
    raw, raw_meta = load_raw_tensors(args.raw_model)
    expected_model = raw_meta["architecture"]["model_name"]
    if actual.get("status") != "CLIENTSIM_OUTPUT_EXPORT_PASS":
        raise ValueError(f"ClientSim export did not pass: {actual.get('status')}")
    if actual.get("model") != expected_model:
        raise ValueError(
            f"ClientSim export model {actual.get('model')} != raw model {expected_model}"
        )
    tensor_compare = compare_tensor_maps(raw, baked)

    cases = actual.get("cases", [])
    if len(cases) < 4:
        raise ValueError(f"expected at least four ClientSim cases, got {len(cases)}")
    visits = actual.get("visits", [])
    if visits != [32] * len(cases):
        raise ValueError(f"expected exact 32-visit exports for every case, got {visits}")
    spatial = np.asarray(actual.get("rootInputSpatial", []), dtype=np.float32)
    global_features = np.asarray(actual.get("rootInputGlobal", []), dtype=np.float32)
    if spatial.size != len(cases) * 22 * 19 * 19:
        raise ValueError(f"rootInputSpatial size mismatch: {spatial.size}")
    if global_features.size != len(cases) * 19:
        raise ValueError(f"rootInputGlobal size mismatch: {global_features.size}")
    if not np.all(np.isfinite(spatial)) or not np.all(np.isfinite(global_features)):
        raise ValueError("ClientSim root input contains non-finite values")

    rows = []
    failures = []
    for case_index, case_name in enumerate(cases):
        expected = infer(
            spatial[case_index * 22 * 19 * 19 : (case_index + 1) * 22 * 19 * 19],
            global_features[case_index * 19 : (case_index + 1) * 19],
            baked,
        )
        case_result = {"case": case_name, "heads": {}}
        for head_name, actual_name, count in HEADS:
            if head_name == "policy_spatial":
                expected_values = expected[head_name]
                start = case_index * count
                observed_values = np.asarray(actual[actual_name][start : start + count])
            elif head_name == "policy_pass":
                expected_values = np.asarray([expected[head_name]], dtype=np.float32)
                observed_values = np.asarray([actual[actual_name][case_index]], dtype=np.float32)
            else:
                expected_values = expected[head_name]
                start = case_index * count
                observed_values = np.asarray(actual[actual_name][start : start + count])
            comparison = compare_array(expected_values, observed_values, args.atol, args.rtol)
            case_result["heads"][head_name] = comparison
            if comparison["status"] != "PASS":
                failures.append(f"{case_name}:{head_name}")
        rows.append(case_result)

    report = {
        "schema": "pure-udon-go.clientsim-neural-numeric-comparison.v1",
        "status": "PASS" if not failures and tensor_compare["exact"] else "FAIL",
        "actual": str(args.actual),
        "model_name": expected_model,
        "export_status": actual["status"],
        "export_numeric_comparison": actual["numericComparison"],
        "baked_texture": {
            "width": baked_meta["width"],
            "height": baked_meta["height"],
            "sha256": baked_meta["packing"]["texture"]["sha256"],
        },
        "raw_vs_baked_tensor_bytes": tensor_compare,
        "atol": args.atol,
        "rtol": args.rtol,
        "cases": rows,
        "failures": sorted(set(failures)),
        "scope": "raw Udon GPU/readback heads versus independent CPU graph over captured Udon features; no KataGo post-search comparison",
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")

    if report["status"] != "PASS":
        print(
            f"PURE_UDON_GO_NEURAL_NUMERIC_COMPARISON_FAIL cases={len(cases)} "
            f"failures={len(report['failures'])} tensorExact={tensor_compare['exact']}"
        )
        return 1
    print(
        f"PURE_UDON_GO_NEURAL_NUMERIC_COMPARISON_PASS cases={len(cases)} "
        f"heads={len(HEADS)} atol={args.atol} rtol={args.rtol} tensorExact=True"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
