# Udon ClientSim vs authorized KataGo position quality — 2026-08-31

Status: `VERIFIED` for four independently executed position comparisons;
`NOT STRENGTH CALIBRATED`.

## Udon side

Source log:
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-clientsim-quality-2.log`

The real UdonSharp/ClientSim probe reset the generated game for each row,
played the fixed sequence through `GoGame` Udon methods, then let the real
Beginner profile run 32 PUCT visits and commit one move. Every row required a
new AI move, `actualVisits >= 32`, and five completed policy/pass/value/score/
ownership readback stages.

| Position | Fixed sequence | Udon side | Udon move | Udon visits | Udon tree nodes | Udon tree edges | readback stages |
|---|---|---|---|---:|---:|---:|---:|
| udon-opening | D16, Q4 | Black | Q16 | 32 | 32 | 11458 | 5 |
| udon-capture | B19, A19, A18 | White | Q4 | 32 | 32 | 11432 | 5 |
| udon-fight | K10, L10, K11, K9, J10, M9, J11, M8 | Black | R16 | 32 | 32 | 11294 | 5 |
| udon-mixed | D16, Q4, C17, R3, E16, P4, C16, R4, F16, O4, B16 | White | Q16 | 32 | 32 | 11172 | 5 |

Terminal Udon markers:

```text
PURE_UDON_GO_CLIENTSIM_QUALITY_PASS scenarios=4 visits=32
PURE_UDON_GO_CLIENTSIM_QUALITY_RESULT pass=True
```

## Oracle side

The authorized KaTrain root was used read-only:

- KataGo v1.18.1, Git `92ee95c0a4b25fec214da00951ab69e97e207729`;
- OpenCL on the NVIDIA GeForce RTX 3060 Laptop GPU;
- configured model `b10c384h6nbttflrs.bin.gz`;
- `maxVisits=1000`, `nnRandomize=false`, `numAnalysisThreads=1`,
  `numSearchThreads=1`, `searchRandSeed=20260831`;
- `homeDataDir` redirected to `E:\UnityPRJS\ShaderGPT_neoversion\Temp\KaTrainOracle`.

Query file:
`Benchmarks/KaTrain/queries/udon-position-quality-2026-08-31.jsonl`

The oracle rows below use zero-based `order` from KataGo's `moveInfos`; score
and winrate deltas are absolute differences from that query's first ordered
move. Values came from separate one-query runs with the fixed configuration.

| Position | Udon move | Oracle best | Udon move order | Oracle visits | Oracle winrate | Oracle score lead | Winrate delta | Score-lead delta |
|---|---|---|---:|---:|---:|---:|---:|---:|
| udon-opening | Q16 | Q16 | 0 | 188 | 0.360592 | -0.860640 | 0.000000 | 0.000000 |
| udon-capture | Q4 | D4 | 2 | 204 | 0.107967 | -4.310093 | 0.001600 | 0.017912 |
| udon-fight | R16 | Q4 | 9 | 30 | 0.014570 | -9.384553 | 0.000065 | 0.025681 |
| udon-mixed | Q16 | Q17 | 2 | 59 | 0.007304 | -10.978023 | 0.000054 | 0.179266 |

Relevant fixed-oracle top groups:

```text
udon-opening: Q16:0:188, D4:1:188, R16:2:190, D3:3:190, R6:4:146, O3:5:146
udon-capture: D4:0:255, Q16:1:255, Q4:2:204, C4:3:186, Q17:4:186, D3:5:113
udon-fight: Q4:0:84, D17:1:73, C16:2:78, D16:3:49, R4:4:39, Q3:5:40, Q16:6:33, D4:7:29, D3:8:34, R16:9:30
udon-mixed: Q17:0:100, C4:1:61, Q16:2:59, D4:3:55, D3:4:43, R16:5:36
```

The production Udon model is `g170-b10c128-s1141046784-d204142634`, while the
authorized KaTrain oracle uses `b10c384h6nbttflrs`; therefore this is a coarse
move-quality smoke comparison, not numerical model equivalence or a strength
calibration. Four positions and one Udon preset are insufficient for Elo,
blunder rates, monotonic difficulty claims, or complete-game win rates.
