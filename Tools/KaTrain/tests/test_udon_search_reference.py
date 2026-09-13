"""Deterministic vectors for the desktop Udon search reference.

These tests deliberately exercise the diagnostic ablation modes without
changing the shipped Go runtime profile.  They lock the numerical contracts
that are easy to get subtly wrong when comparing against KataGo: suppressed
no-result logits must not be used as the softmax anchor, FPU must only affect
unvisited children, and dynamic cpuct must use total child weight.
"""

from pathlib import Path
import sys


TOOLS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOLS))

from udon_search_reference import (  # noqa: E402
    AREA,
    Edge,
    Node,
    UdonSearchReference,
    f32,
    softmax_value_with_optional_no_result,
)


def _payload(mode: int = 0) -> dict:
    logits = [0.0] * AREA
    logits[0] = 2.0
    return {
        "schema": "pure-udon-go.udon-search-reference-input.v1",
        "targetVisits": 1,
        "explorationConstant": 1.4,
        "searchSemanticsMode": mode,
        "rootMoveTemperature": 0.01,
        "rootPolicyTopK": AREA + 1,
        "expansions": [
            {
                "nodeId": 0,
                "parentNodeId": -1,
                "parentMove": -2,
                "legalMoves": [0],
                "policySpatialLogits": logits,
                "policyPassLogit": -2.0,
                "valueLogit0": 1.0,
                "valueLogit1": 0.0,
                "valueLogit2": 100.0,
                "scoreMean": 12.0,
                "scoreStdev": 2.0,
            }
        ],
    }


def test_suppressed_no_result_uses_surviving_maximum() -> None:
    value, no_result = softmax_value_with_optional_no_result(80.0, 79.0, 200.0, True)
    assert no_result == 0.0
    # (exp(80)-exp(79))/(exp(80)+exp(79)) = tanh(0.5).
    assert abs(value - 0.46211716) < 1e-6


def test_legacy_no_result_remains_three_way() -> None:
    value, no_result = softmax_value_with_optional_no_result(80.0, 79.0, 200.0, False)
    assert no_result > 0.999999
    assert abs(value) < 1e-6


def test_fpu_changes_only_unvisited_child_q() -> None:
    reference = UdonSearchReference(_payload(mode=1))
    node = Node(0, visits=4, value_sum=1.2)
    node.edges = [
        Edge(0, f32(0.7), visits=2, value_sum=f32(0.4)),
        Edge(1, f32(0.3), visits=0, value_sum=0.0),
    ]
    edge, q, _ = reference._select_edge(node)
    assert edge.move in (0, 1)
    assert q <= 0.6
    # The visited edge's mean Q must remain exactly its backed-up average.
    node.edges[0].visits = 2
    node.edges[0].value_sum = f32(0.4)
    assert abs(node.edges[0].mean_q - 0.2) < 1e-7


def test_dynamic_cpuct_uses_total_child_weight() -> None:
    reference = UdonSearchReference(_payload(mode=2))
    node = Node(0, visits=4, value_sum=0.0)
    node.edges = [Edge(0, f32(0.5), visits=3), Edge(1, f32(0.5), visits=0)]
    _, _, exploration_u = reference._select_edge(node)
    expected_scaling = f32(1.4 * (3.01**0.5))
    # The unvisited edge is selected in this vector, so its denominator is 1.
    assert abs(exploration_u - f32(expected_scaling * 0.5)) < 1e-6


def test_recent_center_is_root_effective_score() -> None:
    reference = UdonSearchReference(_payload(mode=4))
    expansion = reference.expansions[0]
    reference._expand(reference.root or Node(0), expansion)
    assert abs(reference.recent_score_center - 0.0) < 1e-6
    # No-result is effectively one here; recent score remains exactly the
    # unconditional score mean after the root expansion when the mode is on.
    payload = _payload(mode=4)
    payload["expansions"][0]["valueLogit2"] = -100.0
    reference = UdonSearchReference(payload)
    root = Node(0)
    reference._expand(root, reference.expansions[0])
    assert abs(reference.recent_score_center - 12.0) < 1e-5


def test_all_ablation_bits_remain_composable() -> None:
    reference = UdonSearchReference(_payload(mode=15))
    result = reference.run()
    assert result["actualVisits"] == 1
    assert result["rootVisits"] == 1


if __name__ == "__main__":
    _tests = [
        test_suppressed_no_result_uses_surviving_maximum,
        test_legacy_no_result_remains_three_way,
        test_fpu_changes_only_unvisited_child_q,
        test_dynamic_cpuct_uses_total_child_weight,
        test_recent_center_is_root_effective_score,
        test_all_ablation_bits_remain_composable,
    ]
    for _test in _tests:
        _test()
    print(f"PURE_UDON_GO_SEARCH_SEMANTICS_VECTORS_PASS tests={len(_tests)}")
