# PureUdonGo vs same-model KataGo strength regression screen — 2026-09-09

Status: `NO_CATASTROPHIC_SIGNAL` (position-level screen; not an Elo claim).

## Experimental controls

- Udon path: real Unity 2022.3.22f1 + ClientSim, minimal one-table fixture
  containing the production `GoGame`, `GoAiController`, V7 encoder, GPU graph,
  packed async readback and PUCT search. The full production scene was not used
  for the search sample because its 16-table Udon preload can exhaust the Unity
  editor; production scene generation itself passed separately.
- Reference path: KataGo v1.18.1 executable from the authorized KaTrain install,
  same shipped `g170-b10c128-s1141046784-d204142634` model, Tromp-Taylor rules,
  komi 7.5, deterministic testing, one analysis/search thread.
- Fixed positions: the four integer move logs already used by
  `GoSearchCalibrationProbe` (`opening`, `capture`, `fight`, `mixed`). No random
  position was generated during measurement.
- Public budgets: exactly `4`, `32`, `100`, `200` visits. Udon and the matching
  KataGo oracle were queried with the same target; a separate 1000-visit
  same-model KataGo response was used only as a higher-strength teacher.

## Results

| preset | visits | samples | exact Udon visits | same-budget top-1 | teacher candidate coverage | teacher top-10 | mean teacher rank (0-based) | mean winrate regret | max winrate regret | mean score regret |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Beginner | 4 | 4 | 4/4 | 3/4 | 4/4 | 4/4 | 2.00 | 0.005853 | 0.010494 | 0.044992 |
| Advanced | 32 | 4 | 4/4 | 2/4 | 4/4 | 4/4 | 2.50 | 0.007362 | 0.014179 | 0.060098 |
| Master | 100 | 4 | 4/4 | 2/4 | 4/4 | 4/4 | 2.75 | 0.007405 | 0.014179 | 0.097672 |
| Ultrahard | 200 | 4 | 4/4 | 1/4 | 4/4 | 4/4 | 1.25 | 0.004811 | 0.010494 | 0.034040 |
| **Total** | — | **16** | **16/16** | **8/16** | **16/16** | **16/16** | **2.125** | **0.00636** | **0.01418** | **0.0592** |

The Udon selected move was present in the same-budget KataGo candidate list for
all 16 rows. Every selected move was also present in the 1000-visit teacher
candidate list and all 16 were within its top ten. The screen threshold is
deliberately conservative: full teacher coverage, at least 75% top-ten
coverage, and maximum winrate regret no greater than 0.15. This run met all
three conditions.

## Interpretation and limits

This rules out a catastrophic position-level regression in the tested fixed
opening/capture/fight/mixed strata at the current public budgets. It does **not**
mean 100% KataGo playing strength: the Udon PUCT, score utility, root selection
and no-result handling are intentionally not bit-for-bit KataGo, and 16 positions
cannot establish Elo, balanced-game win rate, or monotonic strength. A larger
paired self-play tournament remains a separate strength-calibration task.

## Reproduction artifacts

- Udon export: `../ClientSimExports/GoSearchCalibration/go-search-calibration.json`
- Same-budget oracle: `20260909-180930-same-budget.jsonl`
- 1000-visit teacher oracle: `20260909-180930-reference-1000.jsonl`
- Machine-readable comparison: `20260909-current-comparison.json`
- Oracle manifest/hashes: `20260909-180930-manifest.json`
- Reproduction scripts: `Tools/KaTrain/run_strength_regression_oracle.ps1` and
  `Tools/Rules/compare_clientsim_search_calibration.py`
