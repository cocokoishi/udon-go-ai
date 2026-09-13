# Real Udon difficulty-budget regression after the 16-state feature gate

Status: `VERIFIED` for exact public search budgets in the generated D3D11
ClientSim scene. This is a budget/wiring regression, not a playing-strength or
Elo calibration result.

Unity log:

```text
E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260902-difficulty-post-random16-v1.log
```

The real Udon search completed every public profile:

| Profile | Target | Actual | Elapsed | Frames | Tree edges | Selected move |
|---|---:|---:|---:|---:|---:|---|
| Beginner | 32 | 32 | 8.695 s | 1,025 | 11,524 | D16 |
| Advanced | 64 | 64 | 16.044 s | 2,038 | 23,046 | D4 |
| Master | 128 | 128 | 32.572 s | 3,994 | 46,087 | D16 |
| UltraHard | 514 | 514 | 128.138 s | 16,558 | 185,054 | D16 |

Relevant terminal markers:

```text
PURE_UDON_GO_CLIENTSIM_LADDER_SAMPLE preset=Beginner targetVisits=32 actualVisits=32 ...
PURE_UDON_GO_CLIENTSIM_LADDER_SAMPLE preset=Advanced targetVisits=64 actualVisits=64 ...
PURE_UDON_GO_CLIENTSIM_LADDER_SAMPLE preset=Master targetVisits=128 actualVisits=128 ...
PURE_UDON_GO_CLIENTSIM_LADDER_SAMPLE preset=UltraHard targetVisits=514 actualVisits=514 ...
PURE_UDON_GO_CLIENTSIM_LADDER_PASS samples=32,64,128,514
PURE_UDON_GO_CLIENTSIM_LADDER_RESULT pass=True
```

All samples used the actual frame-stepped Udon encoder, GPU graph, async
readback, and PUCT search. Root visit sums were `31`, `63`, `127`, and `513`
respectively, and root ownership readbacks advanced once per sample. The
recorded elapsed times are not the clean-host `<=11s / <=20s` performance gate;
they are retained to show current post-change behavior under this run's host
conditions.
