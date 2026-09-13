# Real Udon ClientSim search versus same-model KataGo — 2026-09-01

Status: `VERIFIED` for the 16 position-level observations and exact visit
accounting; `NOT STRENGTH CALIBRATED`.

## Udon execution

The production scene was run through real Unity 2022.3.22f1 UdonSharp and
ClientSim. The probe used the production frame-scheduled feature encoder, GPU
Shader graph, asynchronous output readback, and PUCT controller. It evaluated
four fixed positions (`opening`, `capture`, `fight`, `mixed`) at each public
preset (`Beginner`, `Advanced`, `Master`, `UltraHard`).

The low and high profile runs were separated so the long 514-visit samples did
not exceed the harness timeout. Both runs completed without compiler, Shader,
UdonSharp, or runtime error markers:

```text
E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260901-search-calibration-32-64-v4.log
PURE_UDON_GO_CLIENTSIM_SEARCH_CALIBRATION_PASS samples=8 profiles=32,64,128,514 oracleComparison=not-performed

E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260901-search-calibration-128-514-v5.log
PURE_UDON_GO_CLIENTSIM_SEARCH_CALIBRATION_PASS samples=8 profiles=32,64,128,514 oracleComparison=not-performed
```

The eight low-profile samples recorded exact `32,64` visits and the eight
high-profile samples recorded exact `128,514` visits. Every sample had tree
nodes at least equal to its target, a positive root visit count for the
committed move, four completed non-root readback stages, ownership readback,
and non-zero output revision/search token.

## Same-model oracle comparison

The independent comparator
`Tools/Rules/compare_clientsim_search_calibration.py` merged the two Udon
exports and compared them with the committed 16-response KataGo JSONL:

```text
PURE_UDON_GO_SEARCH_ORACLE_COMPARISON_PASS samples=16 exactVisits=16 oracleCoverage=16 strengthCalibration=not-established
```

Oracle provenance:

```text
executable: E:\UnityPRJS\ShaderGPT_neoversion\KaTrain\_internal\katrain\KataGo\katago.exe
executable SHA-256: 0e6cc918840e6ef67c0b4a21109585306df7aaa3772971574e58deb8e8fc0575
model: .KataGO/g170e-b10c128-s1141046784-d204142634.bin.gz
model SHA-256: 1a8e05a4ea3fca20dab79410cbb566c760767fcdd2fa0b701cfe259a84cc8b04
oracle JSONL SHA-256: fd66dbc9136fb3ad041f4647fe538c59a237e46a9113586eb1557ee4219906e0
rules: tromp-taylor
```

| Preset | Visits | Samples | Exact Udon | Oracle candidate coverage | Oracle top-1 matches | Mean oracle rank (0-based) | Total elapsed (s) |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Beginner | 32 | 4 | 4 | 4 | 2 | 2.00 | 74.38 |
| Advanced | 64 | 4 | 4 | 4 | 1 | 2.50 | 135.02 |
| Master | 128 | 4 | 4 | 4 | 0 | 3.75 | 226.77 |
| UltraHard | 514 | 4 | 4 | 4 | 1 | 4.75 | 663.47 |

Selected Udon moves were present in the corresponding oracle candidate list
for all 16 cases. The overall top-1 agreement was `4/16`; the recorded mean
rank did not improve monotonically with the public visit preset. This is a
real result, not a failed assertion to be hidden: Udon's compact PUCT/search
policy, score utility, root-selection temperature, and candidate truncation
are not the full KataGo search algorithm, and a 4-position sample cannot
establish strength.

## Scope and next gate

This proves that the optimized Udon controller still consumes the exact public
budgets and produces inspectable PUCT observations at all four presets, and it
provides a reproducible same-model position-level comparison. It does not prove
whole-search numerical identity to KataGo, Elo, rank, monotonic strength, or
balanced complete-game win rate. Those require a larger tactical/ko/endgame
corpus and balanced self-play games with a predeclared statistical criterion.

The complete machine-readable report is
`Benchmarks/KaTrain/udon-clientsim-search-oracle-comparison-2026-09-01.json`.
