# Udon search optimization regression check — 2026-09-01

Status: `VERIFIED` for exact Udon visit accounting and the fixed position
quality smoke corpus; `EQUIVALENCE VERIFIED` for the previously recorded
KataGo feature differential; `NOT STRENGTH CALIBRATED`.

This check answers whether the performance optimization changed the model,
the number of PUCT visits, or the observed move choices. The runtime path was
the real Unity `2022.3.22f1` + UdonSharp + VRChat ClientSim path on the
NVIDIA GeForce RTX 3060 Laptop GPU. The editor verifier only dispatched Udon
events and read serialized Udon variables.

## Exact public visit accounting

| preset | target / actual visits | elapsed | search | feature steps | readback requests | result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Beginner | 32 / 32 | 8.488 s | 7.430 s | 224 | 129 | PASS |
| Advanced | 64 / 64 | 15.334 s | 14.281 s | 448 | 257 | PASS |
| Master | 128 / 128 | 27.892 s | 26.818 s | 896 | 513 | PASS |
| UltraHard | 514 / 514 | 106.394 s | 105.288 s | 3598 | 2057 | PASS |

Every row emitted `freshFeatureEncoding=True` and
`PURE_UDON_GO_CLIENTSIM_PERF_RESULT pass=True`. The four-stage non-root
readback is intentional: PUCT consumes policy/value/score, while the root
also reads the ownership head for telemetry. The request count is therefore
`5 + (visits - 1) * 4`.

The current logs are retained in the allowed disposable Temp root:

```text
E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260901-beginner32-probe-copy.log
E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260901-advanced64-probe-copy.log
E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260901-master128-optimized.log
E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260901-ultrahard514-optimized.log
```

The pre-optimization Udon ladder recorded `47.814 / 89.231 / 178.536 /
718.174` seconds for the same `32 / 64 / 128 / 514` budgets. The optimized
run is faster, but the configured and completed visit counts remain exactly
equal; speedup is not being used as a strength claim.

## Model and move-quality regression

The optimization commit changed only frame budgeting, legality bookkeeping,
state-copy/probe mechanics, and non-root ownership readback. No model asset or
NN shader was changed. The raw production model remains
`g170e-b10c128-s1141046784-d204142634.bin.gz`, SHA-256
`1a8e05a4ea3fca20dab79410cbb566c760767fcdd2fa0b701cfe259a84cc8b04`.

The fresh current Udon position-quality run completed four fixed positions:

| position | current Udon move | policy prior | PUCT visits | tree edges | ownership readbacks |
| --- | --- | --- | ---: | ---: | ---: |
| opening | Q16 | Q16 | 32 | 11458 | 1 |
| capture | Q4 | D4 | 32 | 11432 | 2 |
| fight | R16 | Q4 | 32 | 11294 | 3 |
| mixed | Q16 | Q16 | 32 | 11172 | 4 |

These four selected moves and tree-edge counts match the prior committed Udon
position-quality record. The current run also showed a real policy/PUCT
divergence in 2 of 4 positions, with positive root visit distributions, and
emitted:

```text
PURE_UDON_GO_CLIENTSIM_QUALITY_PASS scenarios=4 visits=32 policyPuctDivergence=true
PURE_UDON_GO_CLIENTSIM_QUALITY_RESULT pass=True
```

Against the authorized KaTrain/KataGo position oracle already recorded for
the identical fixed positions, the same Udon moves retain the same coarse
orders: opening `0`, capture `2`, fight `9`, mixed `2`. The recorded oracle
winrate deltas are `0.000000 / 0.001600 / 0.000065 / 0.000054`; score-lead
deltas are `0.000000 / 0.017912 / 0.025681 / 0.179266`.

The oracle uses KaTrain's `b10c384h6nbttflrs` model while production uses the
bundled `g170e-b10c128-s1141046784-d204142634` model. Consequently these rows
are a regression/quality smoke check, not numerical model equivalence, Elo,
or a complete-game strength calibration. A balanced multi-game strength
calibration remains required before claiming global “棋力不变”.

## Public UI-driven ladder

The public `Verify ClientSim Difficulty Search Ladder` path was also rerun
after updating its readback assertion. It changed the generated UI profiles
through Udon events (`PresetBeginner`, `PresetAdvanced`, `PresetMaster`, and
`PresetUltraHard`), reset the board between samples, and then observed the
normal Udon controller/search state.

| preset | configured / completed visits | elapsed | frames | tree nodes | tree edges | completed stages | ownership readbacks |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Beginner | 32 / 32 | 7.460 s | 995 | 32 | 11524 | 4 | 1 |
| Advanced | 64 / 64 | 13.184 s | 1849 | 64 | 23046 | 4 | 2 |
| Master | 128 / 128 | 26.338 s | 3816 | 128 | 46087 | 4 | 3 |
| UltraHard | 514 / 514 | 102.410 s | 14815 | 514 | 185054 | 4 | 4 |

Terminal markers:

```text
PURE_UDON_GO_CLIENTSIM_LADDER_PASS samples=32,64,128,514
PURE_UDON_GO_CLIENTSIM_LADDER_RESULT pass=True
```

Runtime log:
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260901-public-ladder-optimized.log`.

This proves the public UI/profile-to-search visit wiring and the monotonic
search-effort ladder. It still does not prove monotonic playing strength or a
global unchanged-strength claim.

## Independent numerical evidence

The current production feature path has a fresh authorized-KataGo differential
record in `udon-clientsim-feature-differential-2026-09-01.md`: 7 cases,
55,594 spatial values and 133 global values, zero failures at tolerance
`1e-6`. Existing GPU corpus evidence also remains applicable because the NN
shader and baked model were not modified by this optimization.
