# Real Udon ClientSim search ladder — 2026-08-31

Status: `VERIFIED` for actual Udon search effort and move commit; `NOT
STRENGTH CALIBRATED`.

This is a real Unity 2022.3.22f1 + VRChat Worlds 3.7.6 + UdonSharp +
ClientSim run. The editor-side benchmark only sent public UI/game Udon events
and read serialized `VRC.Udon.UdonBehaviour` variables. Feature encoding,
shader dispatch, five-stage async GPU readback, PUCT transitions and move
commit ran in the compiled Udon programs.

Invocation:

```text
Unity.exe -batchmode -projectPath E:\UnityPRJS\ShaderGPT_neoversion\Temp\PureUdonGoUdonClientSimTest -executeMethod PureUdonGo.GoClientSimDifficultyBenchmarkVerifier.VerifyClientSimDifficultySearchLadder
```

Compile/generator gate:
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-udon-world-38.log`

Runtime log:
`E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-clientsim-ladder-2.log`

The benchmark used a fresh empty board for each sample, white AI in PvAI,
white-to-move, and reset between samples. `aiMoves` is cumulative in the
controller; each row was accepted only when its value increased from that
sample's baseline and `moveCount` increased from zero.

| Preset | maxVisits / maxNNQueries | transitions/frame | cpuct | temperature | actual visits | elapsed seconds | ClientSim frames | move | tree nodes | tree edges | positive root visits | root visit sum | root max |
|---|---:|---:|---:|---:|---:|---:|---:|---|---:|---:|---:|---:|---:|
| Beginner | 32 / 32 | 1 | 1.85 | 1.15 | 32 | 47.814 | 1094 | D16 | 32 | 11524 | 2 | 31 | 28 |
| Advanced | 64 / 64 | 2 | 1.60 | 0.85 | 64 | 89.231 | 2619 | D4 | 64 | 23046 | 4 | 63 | 37 |
| Master | 128 / 128 | 4 | 1.35 | 0.30 | 128 | 178.536 | 4809 | D16 | 128 | 46087 | 5 | 127 | 52 |
| UltraHard | 514 / 514 | 8 | 1.25 | 0.10 | 514 | 718.174 | 20060 | D16 | 514 | 185054 | 12 | 513 | 63 |

Terminal markers:

```text
PURE_UDON_GO_CLIENTSIM_LADDER_PASS samples=32,64,128,514
PURE_UDON_GO_CLIENTSIM_LADDER_RESULT pass=True
ERROR_MARKERS=0
```

The run demonstrates a real monotonic search-budget/telemetry gradient and
that all four public presets can complete a Udon GPU search on the current
host. It does not establish monotonic playing strength, Elo, oracle rank,
full-game quality, VRChat multi-client behavior, or device performance. The
exact authorized KaTrain oracle was not used in this ladder run; its separate
provenance remains in `oracle-provenance-2026-08-31.md`.
