#!/usr/bin/env python3
"""Compare a real ClientSim/Udon Go transition trace with the reference rules.

The input trace is produced by GoRulesDifferentialProbe, which only executes
the serialized GoGame Udon program. This tool independently replays the same
fixed actions through Tests/Reference/go_rules_reference.py and compares every
board, both feature-history boards, move counter, captures, ko, turn, pass
counter, terminal state, and position-history count.
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))

from Tests.Reference.go_rules_reference import (  # noqa: E402
    AREA,
    BLACK,
    EMPTY,
    GoRules,
    PASS,
    WHITE,
)


def compare_vector(name: str, expected: list[int], observed: list[int]) -> dict:
    if len(expected) != len(observed):
        return {
            "name": name,
            "expectedCount": len(expected),
            "observedCount": len(observed),
            "mismatchCount": max(len(expected), len(observed)),
            "firstMismatch": 0,
            "status": "SHAPE_MISMATCH",
        }
    mismatch = [index for index, (left, right) in enumerate(zip(expected, observed)) if left != right]
    return {
        "name": name,
        "expectedCount": len(expected),
        "observedCount": len(observed),
        "mismatchCount": len(mismatch),
        "firstMismatch": mismatch[0] if mismatch else -1,
        "status": "PASS" if not mismatch else "FAIL",
    }


def compare_trace(actual: dict) -> tuple[dict, int]:
    if actual.get("schema") != "pure-udon-go.clientsim-rules-trace.v1":
        raise ValueError(f"unexpected trace schema: {actual.get('schema')}")
    if actual.get("status") != "CLIENTSIM_RULES_TRACE_PASS":
        raise ValueError(f"Udon trace did not pass: {actual.get('status')}")
    if actual.get("referenceComparison") != "NOT_PERFORMED_BY_THIS_VERIFIER":
        raise ValueError("trace was already or ambiguously reference-compared")

    moves = actual.get("moves", [])
    if len(moves) != 160:
        raise ValueError(f"expected 160 trace moves, got {len(moves)}")
    array_names = (
        "accepted",
        "moveCounts",
        "sideToMove",
        "consecutivePasses",
        "blackCaptures",
        "whiteCaptures",
        "koLocations",
        "gameStates",
        "positionHistoryCounts",
        "lastMoves",
    )
    for name in array_names:
        if len(actual.get(name, [])) != len(moves):
            raise ValueError(f"{name} length does not match moves")
    for name in ("boardTrace", "previousBoard1Trace", "previousBoard2Trace"):
        if len(actual.get(name, [])) != len(moves) * AREA:
            raise ValueError(f"{name} length does not match moves * board area")

    reference = GoRules(komi=7.5, suicide_legal=True, positional_superko=True)
    previous = [EMPTY] * AREA
    previous_previous = [EMPTY] * AREA
    rows = []
    failures = []

    for index, location in enumerate(moves):
        before = reference.board[:]
        before_previous = previous[:]
        expected_accepted = reference.play(location)
        observed_accepted = bool(actual["accepted"][index])
        row = {"index": index, "location": location, "accepted": expected_accepted == observed_accepted}
        scalar_mismatch = False
        if expected_accepted:
            expected_board = reference.board[:]
            expected_previous = before
            expected_previous_previous = before_previous
            previous_previous = previous
            previous = before
            expected_scalars = {
                "moveCounts": index + 1,
                "sideToMove": reference.to_move,
                "consecutivePasses": reference.consecutive_passes,
                "blackCaptures": reference.black_captures,
                "whiteCaptures": reference.white_captures,
                # GoGame uses NONE=-2, while the compact Python reference
                # uses -1 for "no ko" (the same value as PASS). Keep the
                # protocol mapping explicit rather than treating it as a
                # gameplay difference.
                "koLocations": -2 if reference.ko_loc == -1 else reference.ko_loc,
                "gameStates": 1 if reference.winner == BLACK and reference.finished else 2 if reference.winner == WHITE and reference.finished else 3 if reference.finished else 0,
                "positionHistoryCounts": index + 2,
                "lastMoves": reference.last_move,
            }
            for name, expected in expected_scalars.items():
                observed = actual[name][index]
                if observed != expected:
                    row[name] = {"expected": expected, "observed": observed}
                    scalar_mismatch = True
            offset = index * AREA
            vector_results = (
                compare_vector("board", expected_board, actual["boardTrace"][offset : offset + AREA]),
                compare_vector("previousBoard1", expected_previous, actual["previousBoard1Trace"][offset : offset + AREA]),
                compare_vector("previousBoard2", expected_previous_previous, actual["previousBoard2Trace"][offset : offset + AREA]),
            )
            row["vectors"] = vector_results
            if any(result["status"] != "PASS" for result in vector_results):
                row["vectorsPass"] = False
            else:
                row["vectorsPass"] = True
        else:
            row["referenceRejected"] = True
        row_pass = row["accepted"] and row.get("vectorsPass", True) and not scalar_mismatch
        row["status"] = "PASS" if row_pass else "FAIL"
        if row["status"] != "PASS":
            failures.append(row)
        rows.append(row)

    expected_final = {
        "finalMoveCount": len(reference.move_history),
        "finalSideToMove": reference.to_move,
        "finalConsecutivePasses": reference.consecutive_passes,
        "finalBlackCaptures": reference.black_captures,
        "finalWhiteCaptures": reference.white_captures,
        "finalKoLoc": -2 if reference.ko_loc == -1 else reference.ko_loc,
        "finalGameState": 1 if reference.winner == BLACK and reference.finished else 2 if reference.winner == WHITE and reference.finished else 3 if reference.finished else 0,
        "finalPositionHistoryCount": len(moves) + 1,
    }
    # The trace has no rejected action, so the Udon position history contains
    # the initial position plus one entry for every accepted action.
    final_results = {}
    for name, expected in expected_final.items():
        observed = actual.get(name)
        final_results[name] = {"expected": expected, "observed": observed, "status": "PASS" if observed == expected else "FAIL"}
        if observed != expected:
            failures.append({"final": name, "expected": expected, "observed": observed})

    report = {
        "schema": "pure-udon-go.clientsim-rules-differential.v1",
        "status": "PASS" if not failures else "FAIL",
        "traceStatus": actual["status"],
        "rules": actual.get("rules"),
        "moveCount": len(moves),
        "acceptedMoves": sum(bool(value) for value in actual["accepted"]),
        "rowCount": len(rows),
        "rowFailures": len(failures),
        "final": final_results,
        "rows": rows,
        "failures": failures,
        "scope": "full board and two-history-plane transition comparison against independent Python reference; no neural or strength claim",
    }
    return report, len(failures)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--actual", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    actual = json.loads(args.actual.read_text(encoding="utf-8"))
    report, failure_count = compare_trace(actual)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    if failure_count:
        print(
            f"PURE_UDON_GO_RULES_NUMERIC_DIFFERENTIAL_FAIL moves={report['moveCount']} "
            f"rows={report['rowCount']} failures={failure_count}"
        )
        return 1
    print(
        f"PURE_UDON_GO_RULES_NUMERIC_DIFFERENTIAL_PASS moves={report['moveCount']} "
        f"rows={report['rowCount']} accepted={report['acceptedMoves']}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
