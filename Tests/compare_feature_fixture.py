"""Compare a Unity GoFeatureEncoder fixture with the local V7 reference."""
from __future__ import annotations

import argparse
import json
import math
from pathlib import Path

from Reference.katago_features_v7 import AREA, BLACK, EMPTY, NONE, PASS, WHITE, encode


def cases():
    empty = [EMPTY] * AREA
    previous = [EMPTY] * AREA
    previous_previous = [EMPTY] * AREA
    no_history = [NONE] * 5
    no_players = [EMPTY] * 5
    no_bans = [False] * AREA
    yield "empty_black", empty, previous, previous_previous, no_history, no_players, BLACK, NONE, 15, True, True, True, 0, no_bans

    perspective = [EMPTY] * AREA
    perspective[9] = BLACK
    recent_locs = [180, PASS, NONE, NONE, NONE]
    recent_pla = [BLACK, WHITE, EMPTY, EMPTY, EMPTY]
    bans = [False] * AREA
    bans[10] = True
    yield "perspective_white", perspective, previous, previous_previous, recent_locs, recent_pla, WHITE, 11, 15, True, True, True, 1, bans
    yield "territory_white", perspective, previous, previous_previous, no_history, no_players, WHITE, NONE, 15, True, True, False, 0, no_bans


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--fixture", required=True, type=Path)
    args = parser.parse_args()
    actual = {item["name"]: item for item in json.loads(args.fixture.read_text(encoding="utf-8"))["fixtures"]}
    expected = {}
    for case in cases():
        name, *params = case
        spatial, global_features = encode(*params[:-1], game_state=0, superko_banned=params[-1])
        expected[name] = (spatial, global_features)

    if set(actual) != set(expected):
        raise AssertionError(f"fixture names differ: actual={sorted(actual)} expected={sorted(expected)}")
    for name, (expected_spatial, expected_global) in expected.items():
        got_spatial = actual[name]["spatial"]
        got_global = actual[name]["global"]
        if len(got_spatial) != len(expected_spatial) or len(got_global) != len(expected_global):
            raise AssertionError(f"{name}: output lengths differ")
        for kind, got, want in (("spatial", got_spatial, expected_spatial), ("global", got_global, expected_global)):
            for index, (actual_value, expected_value) in enumerate(zip(got, want)):
                if not math.isclose(actual_value, expected_value, rel_tol=0.0, abs_tol=1e-6):
                    raise AssertionError(f"{name}: first {kind} divergence at {index}: actual={actual_value} expected={expected_value}")
    print(f"FEATURE_FIXTURE_EQUIVALENCE_PASS cases={len(expected)} floats={22 * AREA + 19}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
