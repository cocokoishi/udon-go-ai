#!/usr/bin/env python3
"""Compare Unity GoFeatureEncoder output with KataGo's dumped V7 input NPZs."""
from __future__ import annotations

import argparse
import json
from pathlib import Path

import numpy as np


AREA = 19 * 19
SPATIAL_CHANNELS = 22
GLOBAL_CHANNELS = 19


def first_difference(actual: np.ndarray, expected: np.ndarray, tolerance: float):
    mask = np.abs(actual.astype(np.float64) - expected.astype(np.float64)) > tolerance
    indices = np.argwhere(mask)
    if len(indices) == 0:
        return None
    index = tuple(int(value) for value in indices[0])
    return {
        "index": list(index),
        "actual": float(actual[index]),
        "expected": float(expected[index]),
        "abs": float(abs(float(actual[index]) - float(expected[index]))),
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--fixture", type=Path, required=True)
    parser.add_argument("--oracle-root", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--tolerance", type=float, default=1e-6)
    args = parser.parse_args()

    fixture_set = json.loads(args.fixture.read_text(encoding="utf-8"))
    manifest = json.loads((args.oracle_root / "manifest.json").read_text(encoding="utf-8"))
    manifest_by_name = {row["name"]: row for row in manifest["rows"]}
    rows = []
    failures = 0
    for fixture in fixture_set["fixtures"]:
        name = fixture["name"]
        if name not in manifest_by_name:
            raise AssertionError(f"missing KataGo oracle row for {name}")
        npz_path = args.oracle_root / manifest_by_name[name]["npz"]
        with np.load(npz_path) as archive:
            if archive["binaryInputNCHW"].shape != (1, SPATIAL_CHANNELS, 19, 19):
                raise AssertionError(f"{name}: unexpected binary input shape")
            if archive["globalInputNC"].shape != (1, GLOBAL_CHANNELS):
                raise AssertionError(f"{name}: unexpected global input shape")
            expected_spatial = np.asarray(archive["binaryInputNCHW"][0], dtype=np.float32)
            expected_global = np.asarray(archive["globalInputNC"][0], dtype=np.float32)

        actual_spatial = np.asarray(fixture["spatial"], dtype=np.float32).reshape(
            SPATIAL_CHANNELS, 19, 19
        )
        actual_global = np.asarray(fixture["global"], dtype=np.float32).reshape(GLOBAL_CHANNELS)
        spatial_diff = np.abs(actual_spatial.astype(np.float64) - expected_spatial.astype(np.float64))
        global_diff = np.abs(actual_global.astype(np.float64) - expected_global.astype(np.float64))
        spatial_first = first_difference(actual_spatial, expected_spatial, args.tolerance)
        global_first = first_difference(actual_global, expected_global, args.tolerance)
        row_failures = int(np.count_nonzero(spatial_diff > args.tolerance)) + int(
            np.count_nonzero(global_diff > args.tolerance)
        )
        failures += row_failures
        rows.append(
            {
                "name": name,
                "moveCount": fixture["moveCount"],
                "rules": fixture["rules"],
                "spatialMaxAbs": float(np.max(spatial_diff)),
                "globalMaxAbs": float(np.max(global_diff)),
                "spatialDifferent": int(np.count_nonzero(spatial_diff > args.tolerance)),
                "globalDifferent": int(np.count_nonzero(global_diff > args.tolerance)),
                "firstSpatialDifference": spatial_first,
                "firstGlobalDifference": global_first,
            }
        )

    result = "PASS" if failures == 0 else "FAIL"
    report = {
        "schema": "pure-udon-go.katago-feature-differential.v1",
        "result": result,
        "status": "EQUIVALENCE_VERIFIED" if failures == 0 else "DIFFERENTIAL_MISMATCH",
        "tolerance": args.tolerance,
        "caseCount": len(rows),
        "spatialFloatsPerCase": SPATIAL_CHANNELS * AREA,
        "globalFloatsPerCase": GLOBAL_CHANNELS,
        "failureCount": failures,
        "fixture": str(args.fixture),
        "oracleManifest": str(args.oracle_root / "manifest.json"),
        "rows": rows,
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(
        f"FEATURE_KATAGO_DIFFERENTIAL_{result} cases={len(rows)} "
        f"failures={failures} tolerance={args.tolerance}"
    )
    return 0 if failures == 0 else 1


if __name__ == "__main__":
    raise SystemExit(main())
