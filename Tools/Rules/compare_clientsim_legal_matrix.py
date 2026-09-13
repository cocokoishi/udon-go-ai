#!/usr/bin/env python3
"""Compare a real Udon legal-move matrix against an independent Go reference."""

from __future__ import annotations

import argparse
import copy
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

EXPECTED_CHECKPOINTS = [0, 1, 2, 4, 8, 16, 32, 64, 96, 128, 160]


def clone_rules(source: GoRules) -> GoRules:
    clone = GoRules(
        komi=source.komi,
        suicide_legal=source.suicide_legal,
        positional_superko=source.positional_superko,
    )
    clone.board = source.board[:]
    clone.to_move = source.to_move
    clone.history = set(source.history)
    clone.move_history = source.move_history[:]
    clone.consecutive_passes = source.consecutive_passes
    clone.black_captures = source.black_captures
    clone.white_captures = source.white_captures
    clone.finished = source.finished
    clone.winner = source.winner
    clone.last_move = source.last_move
    clone.ko_loc = source.ko_loc
    return clone


def legal_mask(reference: GoRules) -> list[bool]:
    return [clone_rules(reference).play(location) for location in range(AREA)]


def compare_vector(name: str, expected: list, observed: list) -> dict:
    if len(expected) != len(observed):
        return {
            "name": name,
            "expectedCount": len(expected),
            "observedCount": len(observed),
            "mismatchCount": max(len(expected), len(observed)),
            "firstMismatch": 0,
            "status": "SHAPE_MISMATCH",
        }
    mismatches = [
        index for index, (left, right) in enumerate(zip(expected, observed)) if left != right
    ]
    return {
        "name": name,
        "expectedCount": len(expected),
        "observedCount": len(observed),
        "mismatchCount": len(mismatches),
        "firstMismatch": mismatches[0] if mismatches else -1,
        "status": "PASS" if not mismatches else "FAIL",
    }


def game_state(reference: GoRules) -> int:
    if not reference.finished:
        return 0
    if reference.winner == BLACK:
        return 1
    if reference.winner == WHITE:
        return 2
    return 3


def compare_matrix(actual: dict) -> tuple[dict, int]:
    if actual.get("schema") != "pure-udon-go.clientsim-rules-legal-matrix.v1":
        raise ValueError(f"unexpected matrix schema: {actual.get('schema')}")
    if actual.get("status") != "CLIENTSIM_RULES_LEGAL_MATRIX_PASS":
        raise ValueError(f"Udon matrix did not pass: {actual.get('status')}")
    if actual.get("referenceComparison") != "NOT_PERFORMED_BY_THIS_VERIFIER":
        raise ValueError("matrix was already or ambiguously reference-compared")

    moves = actual.get("moves", [])
    checkpoints = actual.get("checkpoints", [])
    if len(moves) != 160:
        raise ValueError(f"expected 160 actions, got {len(moves)}")
    if checkpoints != EXPECTED_CHECKPOINTS:
        raise ValueError(f"unexpected checkpoints: {checkpoints}")
    positions = len(checkpoints)
    for name in ("accepted", "moveCounts", "sideToMove", "gameStates", "positionHistoryCounts"):
        if len(actual.get(name, [])) != len(moves):
            raise ValueError(f"{name} length does not match actions")
    for name in ("passLegalTrace", "legalCounts"):
        if len(actual.get(name, [])) != positions:
            raise ValueError(f"{name} length does not match checkpoints")
    for name in ("boardTrace", "legalTrace"):
        if len(actual.get(name, [])) != positions * AREA:
            raise ValueError(f"{name} length does not match checkpoints * board area")

    reference = GoRules(komi=7.5, suicide_legal=True, positional_superko=True)
    rows = []
    failures = []
    action_index = 0

    for checkpoint_index, target_action in enumerate(checkpoints):
        while action_index < target_action:
            expected_accepted = reference.play(moves[action_index])
            observed_accepted = bool(actual["accepted"][action_index])
            if expected_accepted != observed_accepted:
                failures.append(
                    {
                        "action": action_index,
                        "location": moves[action_index],
                        "expectedAccepted": expected_accepted,
                        "observedAccepted": observed_accepted,
                    }
                )
            action_index += 1

        expected_board = reference.board[:]
        expected_mask = legal_mask(reference)
        expected_pass = clone_rules(reference).play(PASS)
        offset = checkpoint_index * AREA
        observed_board = actual["boardTrace"][offset : offset + AREA]
        observed_mask = actual["legalTrace"][offset : offset + AREA]
        vectors = [
            compare_vector("board", expected_board, observed_board),
            compare_vector("legalMask", expected_mask, observed_mask),
        ]
        scalar_expected = {
            "moveCount": target_action,
            "sideToMove": reference.to_move,
            "gameState": game_state(reference),
            "positionHistoryCount": target_action + 1,
            "passLegal": expected_pass,
            "legalCount": sum(expected_mask),
        }
        scalar_observed = {
            "moveCount": actual["moveCounts"][target_action - 1] if target_action else 0,
            "sideToMove": actual["sideToMove"][target_action - 1] if target_action else BLACK,
            "gameState": actual["gameStates"][target_action - 1] if target_action else 0,
            "positionHistoryCount": actual["positionHistoryCounts"][target_action - 1] if target_action else 1,
            "passLegal": bool(actual["passLegalTrace"][checkpoint_index]),
            "legalCount": actual["legalCounts"][checkpoint_index],
        }
        scalar_mismatches = {
            name: {"expected": scalar_expected[name], "observed": scalar_observed[name]}
            for name in scalar_expected
            if scalar_expected[name] != scalar_observed[name]
        }
        row = {
            "checkpoint": checkpoint_index,
            "actionCount": target_action,
            "vectors": vectors,
            "scalarMismatches": scalar_mismatches,
            "expectedLegalCount": scalar_expected["legalCount"],
            "observedLegalCount": scalar_observed["legalCount"],
            "status": "PASS" if not scalar_mismatches and all(
                result["status"] == "PASS" for result in vectors
            ) else "FAIL",
        }
        if row["status"] != "PASS":
            failures.append(row)
        rows.append(row)

    final_expected = {
        "finalMoveCount": 160,
        "finalSideToMove": reference.to_move,
        "finalGameState": game_state(reference),
        "finalPositionHistoryCount": 161,
    }
    final_observed = {
        name: actual.get(name)
        for name in final_expected
    }
    final_results = {
        name: {
            "expected": expected,
            "observed": final_observed[name],
            "status": "PASS" if expected == final_observed[name] else "FAIL",
        }
        for name, expected in final_expected.items()
    }
    for name, result in final_results.items():
        if result["status"] != "PASS":
            failures.append({"final": name, **result})

    report = {
        "schema": "pure-udon-go.clientsim-rules-legal-matrix-differential.v1",
        "status": "PASS" if not failures else "FAIL",
        "traceStatus": actual["status"],
        "rules": actual.get("rules"),
        "actionCount": len(moves),
        "checkpointCount": positions,
        "legalQueries": positions * AREA,
        "acceptedActions": sum(bool(value) for value in actual["accepted"]),
        "rowFailures": len(failures),
        "final": final_results,
        "rows": rows,
        "failures": failures,
        "scope": "361-point legal-mask and state comparison at eleven histories against independent Python reference; no neural or strength claim",
    }
    return report, len(failures)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--actual", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    actual = json.loads(args.actual.read_text(encoding="utf-8"))
    report, failure_count = compare_matrix(actual)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    if failure_count:
        print(
            f"PURE_UDON_GO_RULES_LEGAL_MATRIX_DIFFERENTIAL_FAIL positions={report['checkpointCount']} "
            f"queries={report['legalQueries']} failures={failure_count}"
        )
        return 1
    print(
        f"PURE_UDON_GO_RULES_LEGAL_MATRIX_DIFFERENTIAL_PASS positions={report['checkpointCount']} "
        f"queries={report['legalQueries']} accepted={report['acceptedActions']}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
