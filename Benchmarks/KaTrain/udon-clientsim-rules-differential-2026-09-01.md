# Real Udon ClientSim rules differential — 2026-09-01

Status: `EQUIVALENCE VERIFIED` for a fixed 160-action 19×19
Tromp-Taylor production history.

## Udon trace

The real Unity 2022.3.22f1 project compiled
`GoRulesDifferentialProbe` as UdonSharp and ran it through ClientSim. The
probe called only the serialized production `GoGame` methods, one action per
Udon `Update` frame, and recorded after every accepted action:

- the full 361-point board;
- the full `previousBoard1` and `previousBoard2` history planes;
- side to move, move count, pass count, captures, ko location, game state,
  last move, and position-history count.

The sequence contains 160 accepted actions, including opening play, four
captures, and three non-consecutive passes. It ends in a non-terminal playing
state with 149 stones and 4 black / 4 white captures.

Evidence log:

```text
E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260901-rules-trace-v1.log
PURE_UDON_GO_CLIENTSIM_RULE_TRACE_PASS moves=160 accepted=160 referenceComparison=not-performed
PURE_UDON_GO_CLIENTSIM_RULE_TRACE_RESULT pass=True
```

The persistent trace consumed by the independent comparator was:

```text
E:\UnityPRJS\ShaderGPT_neoversion\Temp\PureUdonGoClientSim20260831\Assets\udon-go-ai\Benchmarks\KaTrain\ClientSimExports\GoRulesTrace\go-rules-trace.json
```

The Udon-only export intentionally records
`referenceComparison=NOT_PERFORMED_BY_THIS_VERIFIER`; its PASS only means the
fixed action sequence was executed and all trace arrays were produced.

## Independent replay

`Tools/Rules/compare_clientsim_rules_trace.py` independently replays the
exported action list through `Tests/Reference/go_rules_reference.py` and
compares every row. The comparator checks 160 current-board snapshots, 160
`previousBoard1` snapshots, 160 `previousBoard2` snapshots, all scalar state
arrays, and final state values.

```text
PURE_UDON_GO_RULES_NUMERIC_DIFFERENTIAL_PASS moves=160 rows=160 accepted=160
```

The final compared values were:

```text
moveCount=160
positionHistoryCount=161
sideToMove=BLACK
consecutivePasses=0
blackCaptures=4
whiteCaptures=4
gameState=STATE_PLAYING
koLoc=NONE
rowFailures=0
```

The complete machine-readable report is
`Benchmarks/KaTrain/udon-clientsim-rules-differential-2026-09-01.json`.

## Scope

This proves long-history transition and feature-history parity for this fixed
production rules corpus under real Udon execution. It does not replace the
separate triple-ko PSK test, exhaustive legal-move enumeration, randomized
long-history coverage, full Japanese/territory cleanup rules, or independent
multi-client network transport testing.
