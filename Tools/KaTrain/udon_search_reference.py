#!/usr/bin/env python3
"""Desktop reference for the current PureUdonGo MCTS semantics.

This is deliberately *not* a KataGo reimplementation.  It is a small,
allocation-friendly diagnostic oracle for the search semantics currently in
GoMctsSearch.cs.  A Udon debug tape supplies the exact legal moves and raw NN
heads observed at each node expansion; this program then reproduces the tree
selection/expansion/score/backup/root-ranking path without Unity or Udon.

The tape format is intentionally explicit so a mismatch can be localized:
each expansion records its node id, parent edge, legal moves, policy logits,
three value logits and score mean/stdev.  No production search path consumes
this file and no benchmark position is special-cased here.
"""

from __future__ import annotations

import argparse
import json
import math
import struct
from dataclasses import dataclass, field
from pathlib import Path
from typing import Dict, Iterable, List, Optional, Sequence, Tuple


AREA = 19 * 19
PASS = AREA
NONE = -2


def f32(value: float) -> float:
    """Round a scalar at the same storage boundaries as Unity float fields."""
    return struct.unpack("<f", struct.pack("<f", float(value)))[0]


def clamp(value: float, low: float, high: float) -> float:
    return low if value < low else high if value > high else value


def expf(value: float) -> float:
    return f32(math.exp(float(value)))


def atanf(value: float) -> float:
    return f32(math.atan(float(value)))


def sqrtf(value: float) -> float:
    return f32(math.sqrt(max(0.0, float(value))))


def softmax3_value(win: float, loss: float, no_result: float) -> Tuple[float, float]:
    maximum = f32(max(win, loss, no_result))
    w = expf(clamp(f32(win - maximum), -80.0, 80.0))
    l = expf(clamp(f32(loss - maximum), -80.0, 80.0))
    n = expf(clamp(f32(no_result - maximum), -80.0, 80.0))
    total = f32(f32(w + l) + n)
    if total <= 0.0:
        return f32(0.0), f32(1.0 / 3.0)
    return f32((w - l) / total), f32(n / total)


def softmax_value_with_optional_no_result(
    win: float, loss: float, no_result: float, suppress_no_result: bool
) -> Tuple[float, float]:
    """Match GoMctsSearch.ValueFromLogits/NoResultProbability.

    When the rule set makes no-result impossible, KataGo removes that logit
    before choosing the numerical max.  Using a suppressed logit as the max
    anchor would underflow the surviving win/loss terms and is a subtle but
    catastrophic diagnostic mismatch.
    """
    if not suppress_no_result:
        return softmax3_value(win, loss, no_result)
    maximum = f32(max(win, loss))
    w = expf(clamp(f32(win - maximum), -80.0, 80.0))
    l = expf(clamp(f32(loss - maximum), -80.0, 80.0))
    total = f32(w + l)
    if total <= 0.0:
        return f32(0.0), f32(0.0)
    return f32((w - l) / total), f32(0.0)


def score_utility_weights() -> List[float]:
    return [expf(-0.5 * f32((i - 50) * 0.1) ** 2) for i in range(101)]


WEIGHTS = score_utility_weights()


def expected_score_utility(
    score_mean: float, score_stdev: float, scale: float, center: float = 0.0
) -> float:
    denominator = f32(scale * 19.0)
    if denominator <= 0.0:
        return f32(0.0)
    total = f32(0.0)
    weighted = f32(0.0)
    for i in range(-50, 51):
        z = f32(i * 0.1)
        weight = WEIGHTS[i + 50]
        score = f32(score_mean + f32(score_stdev * z) - center)
        term = f32(weight * atanf(score / denominator) * 0.63661975)
        weighted = f32(weighted + term)
        total = f32(total + weight)
    return f32(weighted / total) if total > 0.0 else f32(0.0)


def effective_score_stdev(mean: float, stdev: float, no_result_probability: float) -> float:
    result_weight = f32(1.0 - clamp(no_result_probability, 0.0, 1.0))
    effective_mean = f32(mean * result_weight)
    second = f32(f32(mean * mean + stdev * stdev) * result_weight)
    variance = f32(second - effective_mean * effective_mean)
    return sqrtf(max(0.0, variance))


def score_utility(mean: float, stdev: float, dynamic_center: float = 0.0) -> float:
    static = expected_score_utility(mean, stdev, 2.0, 0.0)
    dynamic = expected_score_utility(mean, stdev, 0.75, dynamic_center)
    value = f32(static * 0.10 + dynamic * 0.30)
    return f32(clamp(value, -0.40, 0.40))


@dataclass
class Expansion:
    node_id: int
    parent_node_id: int
    parent_move: int
    legal_moves: List[int]
    policy_spatial: List[float]
    policy_pass: float
    value0: float
    value1: float
    value2: float
    score_mean: float
    score_stdev: float
    terminal: bool = False
    terminal_value: float = 0.0

    @staticmethod
    def from_json(value: dict) -> "Expansion":
        policy = [f32(x) for x in value.get("policySpatialLogits", [])]
        if len(policy) != AREA:
            raise ValueError("policySpatialLogits must contain exactly 361 floats")
        legal = []
        for loc in value.get("legalMoves", []):
            loc = int(loc)
            if loc < 0 or loc > PASS:
                raise ValueError(f"invalid legal move {loc}")
            if loc == PASS:
                # GoMctsSearch always appends the pass edge separately after
                # spatial legal moves; the tape's mask includes it for
                # completeness, but it must not be inserted twice here.
                continue
            if loc not in legal:
                legal.append(loc)
        return Expansion(
            node_id=int(value["nodeId"]),
            parent_node_id=int(value.get("parentNodeId", -1)),
            parent_move=int(value.get("parentMove", NONE)),
            legal_moves=legal,
            policy_spatial=policy,
            policy_pass=f32(value.get("policyPassLogit", 0.0)),
            value0=f32(value.get("valueLogit0", 0.0)),
            value1=f32(value.get("valueLogit1", 0.0)),
            value2=f32(value.get("valueLogit2", 0.0)),
            score_mean=f32(value.get("scoreMean", 0.0)),
            score_stdev=f32(value.get("scoreStdev", 0.0)),
            terminal=bool(value.get("terminal", False)),
            terminal_value=f32(value.get("terminalValue", 0.0)),
        )


@dataclass
class Edge:
    move: int
    prior: float
    visits: int = 0
    value_sum: float = 0.0
    child: Optional[int] = None

    @property
    def mean_q(self) -> float:
        return f32(self.value_sum / self.visits) if self.visits > 0 else f32(0.0)


@dataclass
class Node:
    node_id: int
    parent_node_id: int = -1
    parent_move: int = NONE
    visits: int = 0
    value_sum: float = 0.0
    expanded: bool = False
    terminal: bool = False
    terminal_value: float = 0.0
    edges: List[Edge] = field(default_factory=list)


class UdonSearchReference:
    """Exact current GoMctsSearch selection/expansion/backup semantics."""

    def __init__(self, payload: dict):
        schema = payload.get("schema", "")
        if schema not in (
            "pure-udon-go.udon-search-reference-input.v1",
            "pure-udon-go.udon-search-reference-input.v2",
        ):
            raise ValueError(f"unsupported tape schema: {schema}")
        self.target_visits = int(payload["targetVisits"])
        if self.target_visits < 1:
            raise ValueError("targetVisits must be positive")
        self.cpuct = f32(payload.get("explorationConstant", 1.4))
        self.semantics_mode = int(payload.get("searchSemanticsMode", 0))
        self.positional_superko = bool(payload.get("positionalSuperko", True))
        self.area_scoring = bool(payload.get("areaScoring", True))
        self.recent_score_center = f32(0.0)
        self.temperature = f32(payload.get("rootMoveTemperature", 0.01))
        self.top_k = int(payload.get("rootPolicyTopK", PASS + 1))
        self.seed = int(payload.get("rootSelectionSeed", 1831565813)) or 1831565813
        self.expansions = [Expansion.from_json(x) for x in payload.get("expansions", [])]
        if not self.expansions:
            raise ValueError("tape contains no expansions")
        self.by_node = {x.node_id: x for x in self.expansions}
        if len(self.by_node) != len(self.expansions):
            raise ValueError("duplicate expansion node id")
        self.by_parent_move = {
            (x.parent_node_id, x.parent_move): x for x in self.expansions if x.parent_node_id >= 0
        }
        self.nodes: Dict[int, Node] = {}
        self.root: Optional[Node] = None
        self.simulations: List[dict] = []
        self.visits_completed = 0
        self._next_tape_index = 0

    def _consume_root(self) -> Expansion:
        root = self.by_node.get(0)
        if root is None:
            raise ValueError("tape must contain nodeId=0 root expansion")
        self._next_tape_index += 1
        return root

    def _expand(self, node: Node, expansion: Expansion) -> float:
        if expansion.node_id != node.node_id:
            raise ValueError(
                f"expansion node mismatch: expected {node.node_id}, got {expansion.node_id}"
            )
        if node.expanded:
            raise ValueError(f"node {node.node_id} expanded twice")
        maximum = expansion.policy_pass
        for loc in expansion.legal_moves:
            maximum = max(maximum, expansion.policy_spatial[loc])
        weights: List[Tuple[int, float]] = []
        total = f32(0.0)
        for loc in expansion.legal_moves:
            weight = expf(clamp(f32(expansion.policy_spatial[loc] - maximum), -80.0, 80.0))
            weights.append((loc, weight))
            total = f32(total + weight)
        pass_weight = expf(clamp(f32(expansion.policy_pass - maximum), -80.0, 80.0))
        weights.append((PASS, pass_weight))
        total = f32(total + pass_weight)
        if total <= 0.0:
            raise ValueError(f"node {node.node_id} produced no positive prior")
        node.edges = [Edge(move, f32(weight / total)) for move, weight in weights]
        node.expanded = True
        node.terminal = expansion.terminal
        node.terminal_value = f32(expansion.terminal_value)
        suppress_no_result = bool(
            self.semantics_mode & 8
            and self.positional_superko
            and self.area_scoring
        )
        value, no_result = softmax_value_with_optional_no_result(
            expansion.value0,
            expansion.value1,
            expansion.value2,
            suppress_no_result,
        )
        if expansion.terminal:
            return node.terminal_value
        effective_mean = f32(expansion.score_mean * f32(1.0 - no_result))
        effective_stdev = effective_score_stdev(expansion.score_mean, expansion.score_stdev, no_result)
        if expansion.node_id == 0 and self.semantics_mode & 4:
            self.recent_score_center = effective_mean
        dynamic_center = self.recent_score_center if self.semantics_mode & 4 else 0.0
        return f32(value + score_utility(effective_mean, effective_stdev, dynamic_center))

    @staticmethod
    def _better(left: Edge, right: Edge) -> bool:
        if left.visits != right.visits:
            return left.visits > right.visits
        if left.prior != right.prior:
            return left.prior > right.prior
        return left.move < right.move

    def _select_edge(self, node: Node) -> Tuple[Edge, float, float]:
        parent_visits = sqrtf(f32(node.visits + 1))
        total_child_weight = f32(sum(edge.visits for edge in node.edges))
        policy_mass_visited = f32(sum(edge.prior for edge in node.edges if edge.visits > 0))
        fpu = f32(0.0)
        if self.semantics_mode & 1:
            parent_utility = f32(node.value_sum / node.visits) if node.visits > 0 else f32(0.0)
            fpu = f32(parent_utility - f32(0.20 * sqrtf(policy_mass_visited)))
        if self.semantics_mode & 2:
            # The bundled KataGo defaults use cpuctLog=0 and base=500. Keep
            # the expression explicit so a future parameterized mode can add
            # the logarithmic term without changing the current diagnostic.
            cpuct_log = f32(0.0)
            cpuct_base = f32(500.0)
            cpuct = f32(self.cpuct + f32(cpuct_log * math.log(
                (float(total_child_weight) + float(cpuct_base)) / float(cpuct_base)
            )))
            explore_scaling = f32(cpuct * sqrtf(f32(total_child_weight + 0.01)))
        else:
            explore_scaling = f32(self.cpuct * parent_visits)
        best: Optional[Edge] = None
        best_score = -3.4028235e38
        for edge in node.edges:
            q = edge.mean_q if edge.visits > 0 else fpu
            u = f32(explore_scaling * edge.prior / f32(1 + edge.visits))
            score = f32(q + u)
            if best is None or score > best_score or (
                score == best_score and self._better(edge, best)
            ):
                best = edge
                best_score = score
        if best is None:
            raise ValueError(f"node {node.node_id} has no selectable edges")
        q = best.mean_q if best.visits > 0 else fpu
        u = f32(explore_scaling * best.prior / f32(1 + best.visits))
        return best, q, u

    def _backup(self, path: Sequence[Tuple[Node, Edge]], value: float) -> None:
        value = f32(clamp(value, -1.0, 1.0))
        for depth in range(len(path), -1, -1):
            node = self.root if depth == 0 else path[depth - 1][0]
            if depth > 0:
                node = path[depth - 1][1].child_node  # type: ignore[attr-defined]
            node.visits += 1
            node.value_sum = f32(node.value_sum + value)
            if depth > 0:
                edge = path[depth - 1][1]
                edge.visits += 1
                edge.value_sum = f32(edge.value_sum - value)
            value = f32(-value)

    def _backup_nodes(self, nodes: Sequence[Node], edges: Sequence[Edge], value: float) -> None:
        value = f32(clamp(value, -1.0, 1.0))
        # nodes includes root and every descendant in path order.
        for index in range(len(nodes) - 1, -1, -1):
            node = nodes[index]
            node.visits += 1
            node.value_sum = f32(node.value_sum + value)
            if index > 0:
                edge = edges[index - 1]
                edge.visits += 1
                edge.value_sum = f32(edge.value_sum - value)
            value = f32(-value)

    def run(self) -> dict:
        root_expansion = self._consume_root()
        self.root = Node(0)
        self.nodes[0] = self.root
        value = self._expand(self.root, root_expansion)
        self._backup_nodes([self.root], [], value)
        self.visits_completed = 1
        self.simulations.append({"index": 0, "path": [], "leafNode": 0, "leafValue": value})

        while self.visits_completed < self.target_visits:
            node = self.root
            nodes = [node]
            edges: List[Edge] = []
            path_moves: List[int] = []
            while node.expanded and not node.terminal:
                edge, q, u = self._select_edge(node)
                # Keep the child pointer on the edge as a private diagnostic
                # attribute; the serialized result never depends on it.
                edges.append(edge)
                path_moves.append(edge.move)
                if edge.child is None:
                    expansion = self.by_parent_move.get((node.node_id, edge.move))
                    if expansion is None:
                        raise ValueError(
                            f"missing tape expansion for parent={node.node_id} move={edge.move}"
                        )
                    child = Node(expansion.node_id, node.node_id, edge.move)
                    edge.child = child.node_id
                    edge.child_node = child  # type: ignore[attr-defined]
                    self.nodes[child.node_id] = child
                    value = self._expand(child, expansion)
                    nodes.append(child)
                    self._backup_nodes(nodes, edges, value)
                    self.visits_completed += 1
                    self.simulations.append({
                        "index": self.visits_completed - 1,
                        "path": path_moves,
                        "leafNode": child.node_id,
                        "leafValue": value,
                    })
                    break
                child = self.nodes[edge.child]
                edge.child_node = child  # type: ignore[attr-defined]
                node = child
                nodes.append(node)
            else:
                # A terminal/fully expanded node is backed up directly. This
                # mirrors GoMctsSearch.Step's terminal branch.
                value = node.terminal_value if node.terminal else 0.0
                self._backup_nodes(nodes, edges, value)
                self.visits_completed += 1
                self.simulations.append({
                    "index": self.visits_completed - 1,
                    "path": path_moves,
                    "leafNode": node.node_id,
                    "leafValue": value,
                })

        return self.result()

    def _ranked_root_edges(self) -> List[Edge]:
        if self.root is None:
            return []
        return sorted((e for e in self.root.edges if e.visits > 0), key=lambda e: (-e.visits, -e.prior, e.move))

    def _next_random(self) -> float:
        self.seed = (self.seed * 1103515245 + 12345) & 0xFFFFFFFF
        positive = self.seed & 0x7FFFFFFF
        return f32(positive / 2147483648.0)

    def sampled_root_move(self, ranked: Sequence[Edge]) -> int:
        if not ranked:
            return NONE
        if self.temperature <= 0.0101:
            return ranked[0].move
        selected = list(ranked[: max(1, min(self.top_k, len(ranked)))])
        maximum = -3.4028235e38
        logs = []
        for edge in selected:
            value = f32(math.log(edge.visits) / self.temperature)
            logs.append(value)
            maximum = max(maximum, value)
        weights = [expf(clamp(f32(x - maximum), -80.0, 80.0)) for x in logs]
        total = f32(sum(weights))
        if total <= 0.0:
            return selected[0].move
        sample = f32(self._next_random() * total)
        for edge, weight in zip(selected, weights):
            sample = f32(sample - weight)
            if sample <= 0.0:
                return edge.move
        return selected[-1].move

    def result(self) -> dict:
        ranked = self._ranked_root_edges()
        edges = []
        parent_visits = sqrtf(f32((self.root.visits if self.root else 0) + 1))
        total_child_weight = f32(sum(edge.visits for edge in self.root.edges)) if self.root else 0.0
        if self.semantics_mode & 2:
            explore_scaling = f32(self.cpuct * math.sqrt(total_child_weight + 0.01))
        else:
            explore_scaling = f32(self.cpuct * parent_visits)
        for edge in ranked:
            q = edge.mean_q
            u = f32(explore_scaling * edge.prior / f32(1 + edge.visits))
            edges.append({
                "move": edge.move,
                "prior": edge.prior,
                "visits": edge.visits,
                "valueSum": edge.value_sum,
                "meanQ": q,
                "explorationU": u,
                "selectionScore": f32(q + u),
            })
        deterministic = ranked[0].move if ranked else NONE
        return {
            "schema": "pure-udon-go.udon-search-reference-output.v1",
            "targetVisits": self.target_visits,
            "actualVisits": self.visits_completed,
            "rootVisits": self.root.visits if self.root else 0,
            "deterministicRootMove": deterministic,
            "sampledSelectedMove": self.sampled_root_move(ranked),
            "rootEdges": edges,
            "simulations": self.simulations,
        }


def run_self_test() -> dict:
    # Root expands four actions; each subsequent simulation follows a
    # deterministic root edge and expands a child with a distinct value. This
    # locks visit accounting, signed edge backup, ranking and sampled output.
    logits = [0.0] * AREA
    for loc, score in ((0, 2.0), (1, 1.0), (2, 0.5)):
        logits[loc] = score
    payload = {
        "schema": "pure-udon-go.udon-search-reference-input.v1",
        "targetVisits": 4,
        "explorationConstant": 1.4,
        "rootMoveTemperature": 0.01,
        "rootPolicyTopK": AREA + 1,
        "rootSelectionSeed": 1831565813,
        "expansions": [
            {
                "nodeId": 0,
                "parentNodeId": -1,
                "parentMove": NONE,
                "legalMoves": [0, 1, 2],
                "policySpatialLogits": logits,
                "policyPassLogit": -2.0,
                "valueLogit0": 0.2,
                "valueLogit1": -0.1,
                "valueLogit2": -2.0,
                "scoreMean": 0.0,
                "scoreStdev": 1.0,
            },
            *[
                {
                    "nodeId": i + 1,
                    "parentNodeId": 0,
                    "parentMove": i,
                    "legalMoves": [0, 1, 2],
                    "policySpatialLogits": logits,
                    "policyPassLogit": -2.0,
                    "valueLogit0": 0.3 - i * 0.2,
                    "valueLogit1": -0.1,
                    "valueLogit2": -2.0,
                    "scoreMean": 0.0,
                    "scoreStdev": 1.0,
                    "terminal": True,
                    "terminalValue": 0.0,
                }
                for i in range(3)
            ],
        ],
    }
    result = UdonSearchReference(payload).run()
    if result["actualVisits"] != 4 or result["rootVisits"] != 4:
        raise AssertionError(result)
    if result["deterministicRootMove"] not in (0, 1, 2, PASS):
        raise AssertionError(result)
    if len(result["rootEdges"]) < 1:
        raise AssertionError(result)
    return result


def compare_expected(payload: dict, result: dict) -> dict:
    """Compare the desktop result with optional root evidence from Udon."""
    mismatches: List[str] = []
    expected_visits = int(payload.get("expectedRootVisits", 0))
    if expected_visits and result.get("rootVisits") != expected_visits:
        mismatches.append(
            f"rootVisits expected {expected_visits}, got {result.get('rootVisits')}"
        )
    expected_move = payload.get("expectedDeterministicRootMove", NONE)
    if expected_move != NONE and result.get("deterministicRootMove") != expected_move:
        mismatches.append(
            "deterministicRootMove expected "
            f"{expected_move}, got {result.get('deterministicRootMove')}"
        )
    expected_edges = {
        int(edge["move"]): edge
        for edge in payload.get("expectedRootEdges", [])
        if int(edge.get("visits", 0)) > 0
    }
    actual_edges = {
        int(edge["move"]): edge
        for edge in result.get("rootEdges", [])
        if int(edge.get("visits", 0)) > 0
    }
    if set(expected_edges) != set(actual_edges):
        mismatches.append(
            f"visited root edge set differs: expected {sorted(expected_edges)}, "
            f"got {sorted(actual_edges)}"
        )
    for move in sorted(set(expected_edges) & set(actual_edges)):
        expected = expected_edges[move]
        actual = actual_edges[move]
        if int(expected.get("visits", 0)) != int(actual.get("visits", 0)):
            mismatches.append(f"move {move} visits differ")
        for field_name in ("prior", "valueSum"):
            if abs(float(expected.get(field_name, 0.0)) - float(actual.get(field_name, 0.0))) > 1e-4:
                mismatches.append(f"move {move} {field_name} differs")
    return {"pass": not mismatches, "mismatches": mismatches}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    input_payload = None
    if args.self_test:
        result = run_self_test()
    elif args.input:
        input_payload = json.loads(args.input.read_text(encoding="utf-8"))
        result = UdonSearchReference(input_payload).run()
    else:
        parser.error("provide --input or --self-test")
    if input_payload is not None and input_payload.get("expectedRootEdges") is not None:
        result["referenceMatch"] = compare_expected(input_payload, result)
    text = json.dumps(result, indent=2, sort_keys=True)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(text + "\n", encoding="utf-8")
    else:
        print(text)
    reference_match = result.get("referenceMatch", {"pass": True})
    if not reference_match.get("pass", False):
        print(
            "PURE_UDON_GO_SEARCH_REFERENCE_FAIL "
            + "; ".join(reference_match.get("mismatches", []))
        )
        return 1
    print(
        "PURE_UDON_GO_SEARCH_REFERENCE_PASS "
        f"visits={result['actualVisits']} deterministic={result['deterministicRootMove']} "
        f"rootEdges={len(result['rootEdges'])} referenceMatch={reference_match.get('pass', True)}",
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
