# Pure Udon Go — indoor three-table configuration evidence (2026-09-04)

This report records the actual disposable Unity run after the fixed three-table
indoor-room and public-preset change. It is not a claim of clean VRChat-client
performance, Quest support, two-client transport, model redistribution rights,
or playing strength.

Environment: Unity `2022.3.22f1`, Windows D3D11, disposable project
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\PureUdonGoValidation`, and the
offline license copied into `Temp\UnityLicense`. The generated scene schema is
`6`; model is `g170e-b10c128-s1141046784-d204142634`.

## Configuration

| item | value |
| --- | --- |
| visible tables | fixed `3` |
| serialized backing capacity | `16` slots; hidden slots are inactive |
| room | compact indoor beige/aqua wallpaper, collidable walls/ceiling, walkable floor |
| Beginner / 入门 | `4` visits (default) |
| Advanced / 进阶 | `32` visits |
| Master / 大师 | `128` visits |
| Ultrahard | `384` visits |
| engine fixed capacity | `1226` accepted visits |

## PASS evidence

| gate | observed result | raw log |
| --- | --- | --- |
| generator | `cells=361 visibleTables=3 ultraHardPresetVisits=384` | `pure-udon-go-indoor-generator-current-v4.log` |
| production validator | `failures=0 warnings=1` | `pure-udon-go-indoor-validator-v2.log` |
| default ClientSim runtime | exact `searchVisits=4`, legal `D16` commit | `pure-udon-go-indoor-runtime-v2.log` |
| modes and profiles | all four modes; profile probes `32/128/384`; restore `4` | `pure-udon-go-indoor-modes-v1.log` |
| fixed BoardPool | `visible=3`, `capacity=16`, independent dual search | `pure-udon-go-indoor-boardpool-v1.log` |
| walkable floor | `grounded=True`, `crossedFloor=False` | `pure-udon-go-indoor-floor-v1.log` |

The default runtime sample measured `frames=291`, `totalMs=7555.481`, and
completed exactly four visits. The result is retained as a diagnostic sample;
the user explicitly relaxed the former 11/20-second latency target, and the
sample is not relabelled as a hard performance pass.

## OPEN evidence

- `GoClientSimReadbackRecoveryVerifier` hit `OutOfMemoryException` in Udon
  serialization before its adversarial fixture ran. The separate A/B/C reader
  pool gate remains evidence for the implemented state machine, but this
  verifier is open.
- High-budget calibration retained a real timeout at 278 visits; no timeout
  was extended to manufacture a pass.
- Long-history stress retained the 64-ply budget overrun, and complete-game
  lifecycle exceeded the 20-minute execution window.
- Two/three-table percentile profiling did not finish within its fixed
  ClientSim budget. ClientSim is not clean VRChat-client frame-time proof.
