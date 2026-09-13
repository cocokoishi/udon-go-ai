# Udon difficulty ladder vs fixed oracle on empty white-to-move position

Status: `VERIFIED` for the paired observations; `NOT STRENGTH CALIBRATED`.

Udon source log:
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-clientsim-ladder-2.log`

Oracle query:
`Benchmarks/KaTrain/queries/udon-ladder-empty-white-2026-08-31.jsonl`

The Udon ladder used the generated empty board with White to move and ran the
real public profile at each configured budget. The oracle used the authorized
KaTrain KataGo v1.18.1/OpenCL configuration with `maxVisits=1000`,
`nnRandomize=false`, one analysis/search thread, and
`searchRandSeed=20260831`; tuning/cache data stayed under the allowed Temp
directory.

| Udon preset | Udon visits | Udon move | Udon tree nodes | Udon tree edges | Udon root positive | Udon root visit sum | Udon root max |
|---|---:|---|---:|---:|---:|---:|---:|
| Beginner | 32 | D16 | 32 | 11524 | 2 | 31 | 28 |
| Advanced | 64 | D4 | 64 | 23046 | 4 | 63 | 37 |
| Master | 128 | D16 | 128 | 46087 | 5 | 127 | 52 |
| UltraHard | 514 | D16 | 514 | 185054 | 12 | 513 | 63 |

Udon terminal markers:

```text
PURE_UDON_GO_CLIENTSIM_LADDER_PASS samples=32,64,128,514
PURE_UDON_GO_CLIENTSIM_LADDER_RESULT pass=True
```

Fixed oracle result for the same empty white-to-move board:

```text
rootVisits=1000 rootWinrate=0.004919 rootScoreLead=-14.320735
top=C4:0:81,C16:1:81,R4:2:81,R16:3:81,Q17:4:81,D17:5:81,Q3:6:81,D3:7:81
D4 order=16 visits=56 winrate=0.004642 scoreLead=-14.618779 prior=0.011882
D16 order=17 visits=56 winrate=0.004642 scoreLead=-14.618779 prior=0.011882
```

The public visit budget is therefore directly confirmed in Udon, and the
external oracle provides a fixed reference distribution. The production Udon
model is `g170-b10c128-s1141046784-d204142634`, while the authorized KaTrain
oracle model is `b10c384h6nbttflrs`; they are not the same model. Consequently
the move orders above are not a model-equivalence, Elo, or difficulty-strength
claim. More positions and balanced complete games are required for calibration.
