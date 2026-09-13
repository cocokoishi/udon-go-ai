#!/usr/bin/env python3
"""Probe the immutable KataGo .bin.gz source without interpreting tensor payloads.

This is a structural preflight, not the production tensor parser. It establishes
file identity, gzip integrity, and the exact textual header leading into the first
KataGo @BIN@ float block. Later bake stages must use a full descriptor parser.
"""
from __future__ import annotations

import argparse
import gzip
import hashlib
import json
from pathlib import Path
from typing import Any

MARKER = b"@BIN@"
MAX_HEADER_BYTES = 1 << 20


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def probe_model(path: Path) -> dict[str, Any]:
    compressed = path.read_bytes()
    try:
        decompressed = gzip.decompress(compressed)
    except (OSError, EOFError) as exc:
        raise ValueError(f"{path} is not a valid complete gzip stream: {exc}") from exc

    marker_offset = decompressed.find(MARKER)
    if marker_offset < 0:
        raise ValueError("KataGo binary float marker @BIN@ was not found")
    if marker_offset > MAX_HEADER_BYTES:
        raise ValueError(
            f"First @BIN@ marker at {marker_offset} exceeds header safety limit {MAX_HEADER_BYTES}"
        )

    header_bytes = decompressed[:marker_offset]
    try:
        header_text = header_bytes.decode("ascii")
    except UnicodeDecodeError as exc:
        raise ValueError(
            "Bytes before the first @BIN@ marker are not ASCII text; model structure is unexpected"
        ) from exc

    tokens = header_text.split()
    if len(tokens) < 3:
        raise ValueError(f"Header contains only {len(tokens)} whitespace tokens")

    model_version = None
    try:
        model_version = int(tokens[1])
    except (IndexError, ValueError):
        pass

    return {
        "schema": "pure-udon-go.raw-model-header-probe.v1",
        "source_path": path.as_posix(),
        "compressed_size_bytes": len(compressed),
        "compressed_sha256": sha256_bytes(compressed),
        "decompressed_size_bytes": len(decompressed),
        "decompressed_sha256": sha256_bytes(decompressed),
        "first_binary_marker": MARKER.decode("ascii"),
        "first_binary_marker_offset": marker_offset,
        "header_text_sha256": sha256_bytes(header_bytes),
        "header_token_count": len(tokens),
        "header_tokens": tokens,
        "model_name_token": tokens[0],
        "model_version_token": model_version,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--model", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()

    result = probe_model(args.model)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2, sort_keys=True) + "\n", encoding="utf-8")

    print(f"source={args.model}")
    print(f"compressed_size_bytes={result['compressed_size_bytes']}")
    print(f"compressed_sha256={result['compressed_sha256']}")
    print(f"decompressed_size_bytes={result['decompressed_size_bytes']}")
    print(f"decompressed_sha256={result['decompressed_sha256']}")
    print(f"model_name_token={result['model_name_token']}")
    print(f"model_version_token={result['model_version_token']}")
    print(f"header_token_count={result['header_token_count']}")
    print(f"first_binary_marker_offset={result['first_binary_marker_offset']}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
