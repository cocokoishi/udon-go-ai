# Pure Udon Go legal-move matrix differential — 2026-09-02

Status: `EQUIVALENCE VERIFIED` for the sampled production Tromp–Taylor rule
states below. This is a real serialized UdonSharp/ClientSim execution followed
by an independent Python replay; the Udon verifier does not compute expected
legal moves itself.

Environment:

- Unity `2022.3.22f1`
- Direct3D 11 (`-force-d3d11`)
- license entitlement resolved in the Unity log
- isolated project: `E:\UnityPRJS\ShaderGPT_neoversion\Temp\PureUdonGoClientSimPostRecovery`
- production scene: `Assets/PureUdonGo/Generated/Scenes/PureUdonGoProduction.unity`
- rules: positional superko, area scoring, komi `7.5`, multi-stone suicide,
  singleton suicide illegal

## Method

The compiled `GoRulesLegalMatrixProbe` advanced the real `GoGame` Udon
program through the same 160-action history used by the long-history trace.
At action counts `0, 1, 2, 4, 8, 16, 32, 64, 96, 128, 160`, it queried
`GoGame.IsRulesLegalMove()` for every one of the 361 board intersections and
for `PASS`. The export contained the complete board, legal mask, pass result,
side-to-move, game state, move count, and position-history count for each
checkpoint.

`Tools/Rules/compare_clientsim_legal_matrix.py` independently replayed every
action using `Tests/Reference/go_rules_reference.py`, cloned each expected
state for all 361 candidate moves, and compared every observed boolean and
state scalar.

## Result

```text
PURE_UDON_GO_CLIENTSIM_RULE_MATRIX_RESULT passed=True actions=160 positions=11 ... failure=
PURE_UDON_GO_CLIENTSIM_RULE_MATRIX_PASS actions=160 positions=11 legalQueries=3971 referenceComparison=not-performed
PURE_UDON_GO_CLIENTSIM_RULE_MATRIX_FINAL pass=True
PURE_UDON_GO_RULES_LEGAL_MATRIX_DIFFERENTIAL_PASS positions=11 queries=3971 accepted=160
```

All 11 rows passed. The legal counts were `361, 360, 359, 357, 353, 345,
329, 298, 266, 236, 207`; observed and reference counts were identical at
every checkpoint. The final state and position-history count also matched.

Evidence files:

- Unity log: `E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260902-rules-matrix-v1.log`
- Udon export: `E:\UnityPRJS\ShaderGPT_neoversion\Temp\PureUdonGoClientSimPostRecovery\Assets\udon-go-ai\Benchmarks\KaTrain\ClientSimExports\GoRulesMatrix\go-rules-legal-matrix.json`
- differential report: `Benchmarks/KaTrain/udon-clientsim-rules-legal-matrix-differential-2026-09-02.json`

This strengthens the fixed long-history evidence with full legal masks at
eleven histories. It is still not an exhaustive proof of every possible
19×19 position, randomized history, handicap count, or independent
multi-client transport behavior.
