#!/usr/bin/env python3
"""Compare real Udon search samples with a same-model KataGo oracle.

The Udon JSON files are produced by GoSearchCalibrationProbe in ClientSim.
This tool does not run or emulate the Udon search. It checks exact public
visit counts and compares each selected move with the committed KataGo
analysis response for the same position and visit budget.

The result is a position-level search/oracle comparison. It deliberately does
not claim Elo, a rank, or a monotonic playing-strength ladder.
"""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path


SCENARIOS = ("opening", "capture", "fight", "mixed")
PROFILE_NAMES = ("Beginner", "Advanced", "Master", "Ultrahard")
# Keep this tool aligned with the current generated product contract. Older
# 32/64/128/514 and 4/32/128/384 exports remain historical evidence and are not
# silently treated as current settings.
TARGET_VISITS = (4, 32, 100, 200)
MODEL_NAME = "g170-b10c128-s1141046784-d204142634"
AREA = 19 * 19
COLUMNS = "ABCDEFGHJKLMNOPQRST"


def coordinate(location: int) -> str:
    if location == -1:
        return "pass"
    if location == -2:
        return "none"
    if location < 0 or location >= AREA:
        return f"invalid({location})"
    return COLUMNS[location % 19] + str(19 - location // 19)


def load_oracle(path: Path) -> tuple[dict[str, dict], str]:
    entries: dict[str, dict] = {}
    for line in path.read_text(encoding="utf-8").splitlines():
        if not line.strip():
            continue
        entry = json.loads(line)
        entry_id = entry.get("id")
        if not isinstance(entry_id, str):
            continue
        if entry_id in entries:
            raise ValueError(f"duplicate oracle id: {entry_id}")
        entries[entry_id] = entry
    if not entries:
        raise ValueError("oracle JSONL is empty")
    return entries, hashlib.sha256(path.read_bytes()).hexdigest()


def validate_array(data: dict, name: str, count: int) -> list:
    value = data.get(name)
    if not isinstance(value, list) or len(value) < count:
        raise ValueError(f"{name} must contain at least {count} entries")
    return value


def load_udon(path: Path) -> list[dict]:
    data = json.loads(path.read_text(encoding="utf-8"))
    if data.get("schema") != "pure-udon-go.clientsim-search-calibration.v1":
        raise ValueError(f"{path}: unexpected schema {data.get('schema')}")
    if data.get("status") != "CLIENTSIM_SEARCH_CALIBRATION_PASS":
        raise ValueError(f"{path}: Udon export did not pass")
    if data.get("oracleComparison") != "NOT_PERFORMED_BY_THIS_VERIFIER":
        raise ValueError(f"{path}: ambiguous oracle comparison status")
    if data.get("model") != MODEL_NAME:
        raise ValueError(f"{path}: unexpected model {data.get('model')}")

    scenario_limit = int(data.get("scenarioLimit", 0))
    profile_start = int(data.get("profileStart", 0))
    profile_limit = int(data.get("profileLimit", 0))
    sample_count = int(data.get("sampleCount", 0))
    if not (1 <= scenario_limit <= len(SCENARIOS)):
        raise ValueError(f"{path}: invalid scenarioLimit")
    if not (0 <= profile_start < profile_limit <= len(TARGET_VISITS)):
        raise ValueError(f"{path}: invalid profile range")
    expected_count = scenario_limit * (profile_limit - profile_start)
    if sample_count != expected_count:
        raise ValueError(f"{path}: sampleCount {sample_count} != {expected_count}")

    int_names = (
        "selectedMoves",
        "policyMoves",
        "actualVisits",
        "targetSearchVisits",
        "moveCounts",
        "treeNodes",
        "treeEdges",
        "selectedRootVisits",
        "readerStages",
        "ownershipReadbacks",
        "rootOutputRevisions",
        "rootOutputSearchTokens",
    )
    arrays = {name: validate_array(data, name, sample_count) for name in int_names}
    arrays["elapsedSeconds"] = validate_array(data, "elapsedSeconds", sample_count)

    rows = []
    for index in range(sample_count):
        scenario_index = index // (profile_limit - profile_start)
        profile_index = profile_start + index % (profile_limit - profile_start)
        target = TARGET_VISITS[profile_index]
        exact = (
            arrays["actualVisits"][index] == target
            and arrays["targetSearchVisits"][index] == target
            and arrays["treeNodes"][index] >= target
            and arrays["selectedRootVisits"][index] > 0
            # The production reader now performs one packed readback stage;
            # four/five-stage thresholds belonged to the retired decoder.
            and arrays["readerStages"][index] >= 1
            and arrays["ownershipReadbacks"][index] > 0
            and arrays["rootOutputRevisions"][index] > 0
            and arrays["rootOutputSearchTokens"][index] > 0
        )
        rows.append(
            {
                "scenario": SCENARIOS[scenario_index],
                "profile": PROFILE_NAMES[profile_index],
                "targetVisits": target,
                "selectedMove": int(arrays["selectedMoves"][index]),
                "selectedCoordinate": coordinate(int(arrays["selectedMoves"][index])),
                "policyMove": int(arrays["policyMoves"][index]),
                "policyCoordinate": coordinate(int(arrays["policyMoves"][index])),
                "actualVisits": int(arrays["actualVisits"][index]),
                "targetSearchVisits": int(arrays["targetSearchVisits"][index]),
                "treeNodes": int(arrays["treeNodes"][index]),
                "treeEdges": int(arrays["treeEdges"][index]),
                "selectedRootVisits": int(arrays["selectedRootVisits"][index]),
                "readerStages": int(arrays["readerStages"][index]),
                "ownershipReadbacks": int(arrays["ownershipReadbacks"][index]),
                "elapsedSeconds": float(arrays["elapsedSeconds"][index]),
                "exactUdonSearch": exact,
            }
        )
    return rows


def oracle_policy_top(entry: dict) -> str | None:
    policy = entry.get("policy")
    if not isinstance(policy, list) or len(policy) < AREA + 1:
        return None
    legal = [(float(value), index) for index, value in enumerate(policy) if float(value) >= 0.0]
    if not legal:
        return None
    return coordinate(max(legal, key=lambda item: (item[0], -item[1]))[1])


def compare(
    rows: list[dict],
    oracle: dict[str, dict],
    reference: dict[str, dict] | None = None,
    reference_visits: int = 1000,
) -> dict:
    seen = set()
    failures = []
    for row in rows:
        key = (row["scenario"], row["profile"])
        if key in seen:
            failures.append({"type": "duplicate_udon_sample", "key": key})
        seen.add(key)
        oracle_id = f"{row['scenario']}-v{row['targetVisits']}"
        entry = oracle.get(oracle_id)
        if entry is None:
            failures.append({"type": "missing_oracle", "id": oracle_id})
            row["oracleId"] = oracle_id
            continue
        row["oracleId"] = oracle_id
        row["oracleRootVisits"] = int(entry.get("rootInfo", {}).get("visits", 0))
        row["oraclePolicyTop"] = oracle_policy_top(entry)
        move_infos = entry.get("moveInfos") or []
        selected_info = next(
            (info for info in move_infos if info.get("move") == row["selectedCoordinate"]),
            None,
        )
        best_info = move_infos[0] if move_infos else None
        row["oracleSelectedInCandidateList"] = selected_info is not None
        row["oracleSelectedRankZeroBased"] = (
            int(selected_info["order"]) if selected_info is not None and "order" in selected_info else None
        )
        row["oracleBestMove"] = best_info.get("move") if best_info else None
        row["oracleTop1Match"] = bool(best_info and best_info.get("move") == row["selectedCoordinate"])
        row["oracleSelectedWinrate"] = selected_info.get("winrate") if selected_info else None
        row["oracleBestWinrate"] = best_info.get("winrate") if best_info else None
        row["oracleSelectedScoreMean"] = selected_info.get("scoreMean") if selected_info else None
        row["oracleBestScoreMean"] = best_info.get("scoreMean") if best_info else None
        if selected_info is None:
            failures.append({"type": "selected_move_missing_from_oracle_candidates", "id": oracle_id, "move": row["selectedCoordinate"]})
        if not row["exactUdonSearch"]:
            failures.append({"type": "udon_search_invariant", "id": oracle_id})

        # A high-visit same-model reference is a move-quality screen, not a
        # claim of KataGo search identity. It lets the report distinguish a
        # harmless low-budget tie from a move that is genuinely far from the
        # reference policy/value envelope.
        if reference is not None:
            reference_id = f"{row['scenario']}-ref{reference_visits}"
            reference_entry = reference.get(reference_id)
            row["referenceId"] = reference_id
            if reference_entry is None:
                row["referenceSelectedRankZeroBased"] = None
                row["referenceBestMove"] = None
                row["referenceSelectedWinrate"] = None
                row["referenceBestWinrate"] = None
                row["referenceAbsoluteWinrateRegret"] = None
                row["referenceSelectedScoreMean"] = None
                row["referenceBestScoreMean"] = None
                row["referenceAbsoluteScoreRegret"] = None
            else:
                reference_infos = reference_entry.get("moveInfos") or []
                reference_selected = next(
                    (info for info in reference_infos if info.get("move") == row["selectedCoordinate"]),
                    None,
                )
                reference_best = reference_infos[0] if reference_infos else None
                row["referenceSelectedRankZeroBased"] = (
                    int(reference_selected["order"])
                    if reference_selected is not None and "order" in reference_selected
                    else None
                )
                row["referenceBestMove"] = reference_best.get("move") if reference_best else None
                row["referenceSelectedWinrate"] = (
                    reference_selected.get("winrate") if reference_selected else None
                )
                row["referenceBestWinrate"] = (
                    reference_best.get("winrate") if reference_best else None
                )
                row["referenceAbsoluteWinrateRegret"] = (
                    abs(float(reference_selected["winrate"]) - float(reference_best["winrate"]))
                    if reference_selected is not None
                    and reference_best is not None
                    and "winrate" in reference_selected
                    and "winrate" in reference_best
                    else None
                )
                row["referenceSelectedScoreMean"] = (
                    reference_selected.get("scoreMean") if reference_selected else None
                )
                row["referenceBestScoreMean"] = (
                    reference_best.get("scoreMean") if reference_best else None
                )
                row["referenceAbsoluteScoreRegret"] = (
                    abs(float(reference_selected["scoreMean"]) - float(reference_best["scoreMean"]))
                    if reference_selected is not None
                    and reference_best is not None
                    and "scoreMean" in reference_selected
                    and "scoreMean" in reference_best
                    else None
                )

    expected_keys = {(scenario, profile) for scenario in SCENARIOS for profile in PROFILE_NAMES}
    missing = sorted(expected_keys - seen)
    for scenario, profile in missing:
        failures.append({"type": "missing_udon_sample", "scenario": scenario, "profile": profile})

    aggregates = {}
    for profile in PROFILE_NAMES:
        profile_rows = [row for row in rows if row["profile"] == profile]
        ranks = [row["oracleSelectedRankZeroBased"] for row in profile_rows if row.get("oracleSelectedRankZeroBased") is not None]
        winrate_deltas = [
            abs(float(row["oracleSelectedWinrate"]) - float(row["oracleBestWinrate"]))
            for row in profile_rows
            if row.get("oracleSelectedWinrate") is not None and row.get("oracleBestWinrate") is not None
        ]
        score_deltas = [
            abs(float(row["oracleSelectedScoreMean"]) - float(row["oracleBestScoreMean"]))
            for row in profile_rows
            if row.get("oracleSelectedScoreMean") is not None and row.get("oracleBestScoreMean") is not None
        ]
        reference_ranks = [
            row["referenceSelectedRankZeroBased"]
            for row in profile_rows
            if row.get("referenceSelectedRankZeroBased") is not None
        ]
        reference_winrate_regrets = [
            float(row["referenceAbsoluteWinrateRegret"])
            for row in profile_rows
            if row.get("referenceAbsoluteWinrateRegret") is not None
        ]
        reference_score_regrets = [
            float(row["referenceAbsoluteScoreRegret"])
            for row in profile_rows
            if row.get("referenceAbsoluteScoreRegret") is not None
        ]
        aggregates[profile] = {
            "targetVisits": TARGET_VISITS[PROFILE_NAMES.index(profile)],
            "samples": len(profile_rows),
            "exactUdonSearches": sum(row["exactUdonSearch"] for row in profile_rows),
            "oracleCandidateCoverage": len(ranks),
            "oracleTop1Matches": sum(row.get("oracleTop1Match", False) for row in profile_rows),
            "meanOracleRankZeroBased": sum(ranks) / len(ranks) if ranks else None,
            "meanAbsoluteWinrateDelta": sum(winrate_deltas) / len(winrate_deltas) if winrate_deltas else None,
            "meanAbsoluteScoreMeanDelta": sum(score_deltas) / len(score_deltas) if score_deltas else None,
            "referenceCandidateCoverage": len(reference_ranks),
            "referenceTop10Coverage": sum(rank < 10 for rank in reference_ranks),
            "meanReferenceRankZeroBased": (
                sum(reference_ranks) / len(reference_ranks) if reference_ranks else None
            ),
            "meanReferenceWinrateRegret": (
                sum(reference_winrate_regrets) / len(reference_winrate_regrets)
                if reference_winrate_regrets
                else None
            ),
            "maxReferenceWinrateRegret": max(reference_winrate_regrets)
            if reference_winrate_regrets
            else None,
            "meanReferenceScoreRegret": (
                sum(reference_score_regrets) / len(reference_score_regrets)
                if reference_score_regrets
                else None
            ),
            "elapsedSeconds": sum(row["elapsedSeconds"] for row in profile_rows),
        }

    reference_rows = [
        row
        for row in rows
        if row.get("referenceSelectedRankZeroBased") is not None
    ]
    reference_ranks_all = [row["referenceSelectedRankZeroBased"] for row in reference_rows]
    reference_regrets_all = [
        float(row["referenceAbsoluteWinrateRegret"])
        for row in rows
        if row.get("referenceAbsoluteWinrateRegret") is not None
    ]
    # Conservative, explicit screening thresholds. This is only a signal for
    # manual review; it is deliberately not named PASS/FAIL strength or Elo.
    catastrophe_screen = "NOT_RUN"
    if reference is not None:
        catastrophe_screen = (
            "NO_CATASTROPHIC_SIGNAL"
            if reference_rows
            and len(reference_rows) == len(rows)
            and sum(rank < 10 for rank in reference_ranks_all) >= len(rows) * 0.75
            and (not reference_regrets_all or max(reference_regrets_all) <= 0.15)
            else "REVIEW_REQUIRED"
        )

    return {
        "schema": "pure-udon-go.clientsim-search-oracle-comparison.v1",
        "status": "PASS" if not failures else "FAIL",
        "sameModel": MODEL_NAME,
        "sampleCount": len(rows),
        "exactVisitSamples": sum(row["exactUdonSearch"] for row in rows),
        "oracleCandidateCoverage": sum(row.get("oracleSelectedInCandidateList", False) for row in rows),
        "oracleTop1Matches": sum(row.get("oracleTop1Match", False) for row in rows),
        "strengthCalibration": "NOT_ESTABLISHED",
        "referenceVisits": reference_visits if reference is not None else None,
        "catastrophicRegressionScreen": catastrophe_screen,
        "failures": failures,
        "aggregates": aggregates,
        "rows": rows,
        "scope": "real Udon ClientSim search observations compared with committed same-model KataGo position responses; no Elo, rank, or complete-game strength claim",
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--actual", type=Path, action="append", required=True)
    parser.add_argument("--oracle", type=Path, required=True)
    parser.add_argument("--reference", type=Path)
    parser.add_argument("--reference-visits", type=int, default=1000)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()

    rows = []
    for path in args.actual:
        rows.extend(load_udon(path))
    oracle, oracle_sha256 = load_oracle(args.oracle)
    reference = None
    reference_sha256 = None
    if args.reference is not None:
        reference, reference_sha256 = load_oracle(args.reference)
    report = compare(rows, oracle, reference, args.reference_visits)
    report["oracle"] = {
        "path": str(args.oracle),
        "sha256": oracle_sha256,
        "expectedEntries": 16,
        "loadedEntries": len(oracle),
        "executable": "E:\\UnityPRJS\\ShaderGPT_neoversion\\KaTrain\\_internal\\katrain\\KataGo\\katago.exe",
        "executableSha256": "0e6cc918840e6ef67c0b4a21109585306df7aaa3772971574e58deb8e8fc0575",
        "model": ".KataGO/g170e-b10c128-s1141046784-d204142634.bin.gz",
        "modelSha256": "1a8e05a4ea3fca20dab79410cbb566c760767fcdd2fa0b701cfe259a84cc8b04",
        "configSha256": "faf458dd0bd7923db48c1043af88f6cdfb938ebaf5950b116a82ca2cd4dc80c5",
        "rules": "tromp-taylor",
    }
    if args.reference is not None:
        report["referenceOracle"] = {
            "path": str(args.reference),
            "sha256": reference_sha256,
            "referenceVisits": args.reference_visits,
            "loadedEntries": len(reference),
        }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    if report["status"] != "PASS":
        print(
            f"PURE_UDON_GO_SEARCH_ORACLE_COMPARISON_FAIL samples={report['sampleCount']} "
            f"exactVisits={report['exactVisitSamples']} oracleCoverage={report['oracleCandidateCoverage']} "
            f"failures={len(report['failures'])} strengthCalibration=not-established"
        )
        return 1
    print(
        f"PURE_UDON_GO_SEARCH_ORACLE_COMPARISON_PASS samples={report['sampleCount']} "
        f"exactVisits={report['exactVisitSamples']} oracleCoverage={report['oracleCandidateCoverage']} "
        f"strengthCalibration=not-established"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
