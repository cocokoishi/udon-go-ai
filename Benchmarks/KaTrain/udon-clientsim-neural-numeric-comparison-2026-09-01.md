# Real Udon ClientSim neural numeric comparison — 2026-09-01

Status: `EQUIVALENCE VERIFIED` for the four captured root positions and the
five raw output heads. This is a numeric comparison, not a strength or search
calibration result.

## Udon capture

The real Unity 2022.3.22f1 project compiled the temporary probe as UdonSharp,
ran it through ClientSim, and used the production frame-scheduled
`GoFeatureEncoder`, GPU graph, asynchronous readback, and 32-visit PUCT path.
The probe captured the encoder's root `22 x 19 x 19` spatial features and 19
global features immediately before the root GPU evaluation. It exported the
raw policy spatial, policy pass, value, score, and ownership head arrays.

Evidence log:

```text
E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260901-neural-output-export-v4.log
PURE_UDON_GO_CLIENTSIM_NEURAL_OUTPUT_EXPORT_PASS cases=4 visits=32 rootStages=5 numericComparison=not-performed
PURE_UDON_GO_CLIENTSIM_NEURAL_EXPORT_RESULT pass=True
```

Persistent export consumed by the comparator:

```text
E:\UnityPRJS\ShaderGPT_neoversion\Temp\PureUdonGoClientSim20260831\Assets\udon-go-ai\Benchmarks\KaTrain\ClientSimExports\GoNeuralOutputExport\udon-neural-output-export.json
```

The export has schema
`pure-udon-go.clientsim-neural-output-export.v2`, model
`g170-b10c128-s1141046784-d204142634`, four cases (`opening`, `capture`,
`fight`, `mixed`), and exact 32 visits per case.

## Independent CPU graph comparison

Command/tool:

```text
Tools/NNReference/compare_clientsim_neural_export.py
```

The tool decodes the committed raw KataGo model, loads the committed baked
RGBA32F atlas, checks all 127 raw/baked tensor arrays byte-for-byte, then runs
the independent CPU graph over the captured Udon features. It compares the
raw Udon GPU/readback values directly; it does not compare KataGo's searched
`moveInfos` or use an external inference service.

```text
PURE_UDON_GO_NEURAL_NUMERIC_COMPARISON_PASS cases=4 heads=5 atol=0.0001 rtol=1e-05 tensorExact=True
```

The maximum absolute error across all five heads and all four positions was
`9.5367431640625e-06`, with zero mismatches under `atol=1e-4` and
`rtol=1e-5`. Per-head maximum absolute errors were:

| Head | Maximum absolute error | Mismatches |
| --- | ---: | ---: |
| Policy spatial (361) | `9.5367431640625e-06` | 0 |
| Policy pass | `2.86102294921875e-06` | 0 |
| Value (3) | `1.430511474609375e-06` | 0 |
| Score (4) | `5.885958671569824e-07` | 0 |
| Ownership (361) | `9.5367431640625e-07` | 0 |

The baked atlas is `1024 x 733`, SHA-256
`e00f701b6ef1498037e56a330f6243d2878ff9c74e6f6fddd5b0cf06a2f94309`.
The full JSON report is
`Benchmarks/KaTrain/udon-clientsim-neural-numeric-comparison-2026-09-01.json`.

## Scope

This establishes that, for these four real Udon-captured inputs, the
production GPU/readback heads agree with the independent CPU implementation
of the same committed KataGo-derived graph within the stated tolerances. It
also establishes exact raw-to-baked tensor reconstruction for the model.
It does not establish randomized full-input coverage, Quest/other GPU backend
equivalence, search-strength calibration, or whole-search equivalence to the
KataGo executable.
