# Real Udon ClientSim performance — 2026-09-01

Status: `VERIFIED` for the measured isolated Unity path. This is not a
VRChat device/runtime claim.

The probes run through:

```text
UdonSharp source -> Unity UdonSharp compiler -> serialized UdonBehaviour
-> VRChat ClientSim -> production GoAiController/GoMctsSearch/GPU graph
```

The editor probe only sends `RunCompleteGame` and reads serialized Udon
variables. It does not execute the Go search in native/editor C#.

Environment:

- Unity `2022.3.22f1`
- real D3D11 graphics device
- copied host license accepted by the isolated project
- public production profiles, not reduced custom settings
- `Beginner` = `32` visits, preset `0`
- `Advanced` = `64` visits, preset `1`

## Results

| profile | target / actual visits | elapsed | search | fresh feature encodes | readback requests | result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Beginner | 32 / 32 | 8.488 s | 7.430 s | 32 | 129 | PASS |
| Advanced | 64 / 64 | 15.334 s | 14.281 s | 64 | 257 | PASS |

The final non-root evaluations read policy/value/score in four stages. The
root evaluation reads the fifth ownership stage for telemetry, so the total
request counts are `5 + (visits - 1) * 4`.

The complete logs are retained in the allowed disposable Temp root:

- `E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260901-beginner32-probe-copy.log`
- `E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260901-advanced64-probe-copy.log`

Both logs contain `UdonSharp Compile of 41 scripts`, exact visit markers,
`freshFeatureEncoding=True`, and `PURE_UDON_GO_CLIENTSIM_PERF_RESULT pass=True`.
No `error CS`, Shader error, UdonSharp compiler error, unhandled exception, or
performance-probe failure was found by the full-log diagnostic scan.

The optimization removed redundant 361-cell board copies from the already
incremental `GoSearchState` legality/superko probe and omitted ownership
readback for non-root leaves. It did not change move legality, hash history,
PUCT visits, neural inputs, or the GPU graph outputs.
