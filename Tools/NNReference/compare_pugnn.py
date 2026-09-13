#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import struct
from pathlib import Path

import numpy as np


MAGIC = b"PUGNN01\0"


def read_records(path: Path) -> dict[str, np.ndarray]:
    data = path.read_bytes()
    offset = 0
    if data[: len(MAGIC)] != MAGIC:
        raise ValueError(f"{path}: invalid PUGNN01 header")
    offset += len(MAGIC)
    record_count = struct.unpack_from("<I", data, offset)[0]
    offset += 4
    records: dict[str, np.ndarray] = {}
    for _ in range(record_count):
        name_length = struct.unpack_from("<I", data, offset)[0]
        offset += 4
        name = data[offset : offset + name_length].decode("ascii")
        offset += name_length
        value_count = struct.unpack_from("<I", data, offset)[0]
        offset += 4
        byte_count = value_count * 4
        end = offset + byte_count
        if end > len(data):
            raise ValueError(f"{path}: truncated record {name}")
        records[name] = np.frombuffer(data[offset:end], dtype="<f4").copy()
        offset = end
    if offset != len(data):
        raise ValueError(f"{path}: trailing bytes after records")
    return records


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--reference", type=Path, required=True)
    parser.add_argument("--actual", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--atol", type=float, default=1e-4)
    parser.add_argument("--rtol", type=float, default=1e-5)
    args = parser.parse_args()

    reference = read_records(args.reference)
    actual = read_records(args.actual)
    rows = []
    failures = []
    for name in reference:
        if name not in actual:
            failures.append(name + ":missing")
            continue
        expected = reference[name]
        observed = actual[name]
        if expected.shape != observed.shape:
            failures.append(name + ":shape")
            continue
        difference = np.abs(observed.astype(np.float64) - expected.astype(np.float64))
        finite = np.isfinite(expected) & np.isfinite(observed)
        close = np.isclose(observed, expected, atol=args.atol, rtol=args.rtol)
        close &= finite
        row = {
            "name": name,
            "count": int(expected.size),
            "max_abs": float(np.max(difference)),
            "mean_abs": float(np.mean(difference, dtype=np.float64)),
            "rmse": float(np.sqrt(np.mean(np.square(difference), dtype=np.float64))),
            "mismatch_count": int(np.count_nonzero(~close)),
            "expected_min": float(np.min(expected)),
            "expected_max": float(np.max(expected)),
            "actual_min": float(np.min(observed)),
            "actual_max": float(np.max(observed)),
        }
        rows.append(row)
        if row["mismatch_count"]:
            failures.append(name)
    for name in actual:
        if name not in reference:
            failures.append(name + ":unexpected")

    result = {
        "schema": "pure-udon-go.pugnn-differential.v1",
        "reference": str(args.reference),
        "actual": str(args.actual),
        "atol": args.atol,
        "rtol": args.rtol,
        "record_count": len(reference),
        "failures": sorted(set(failures)),
        "rows": rows,
        "status": "PASS" if not failures else "FAIL",
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    if failures:
        print(f"PUGNN_DIFFERENTIAL_FAIL records={len(reference)} failures={len(set(failures))}")
        for row in rows:
            if row["mismatch_count"]:
                print(f"{row['name']}: max_abs={row['max_abs']:.8g} mismatches={row['mismatch_count']}")
        return 1
    print(f"PUGNN_DIFFERENTIAL_PASS records={len(reference)} atol={args.atol} rtol={args.rtol}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
