# Real Unity GPU Shader Multi-Position Corpus — 2026-08-31

Status: `EQUIVALENCE VERIFIED` for the tested seven-state corpus.

The real Udon ClientSim feature probe first produced the input arrays. The
Editor-only GPU verifier then loaded that fixture JSON and ran the generated
production scene's actual `GoGpuNeuralRuntime` and `PureUdonGo/NNLayer` Shader
for every case. It read back 17 records per case: initial trunk, ten residual
blocks, trunk tip, policy spatial/pass, value, score, and ownership.

## Runtime evidence

Unity D3D11 log:

```text
E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-gpu-shader-corpus.log
```

The log recorded:

```text
PURE_UDON_GO_GPU_SHADER_RESULT device=NVIDIA GeForce RTX 3060 Laptop GPU cases=7 checkpointsPerCase=17 outputFolder=...
PURE_UDON_GO_GPU_SHADER_PASS cases=7 checkpointsPerCase=17
```

The verifier ran without `-nographics` and with `-force-d3d11`. The GPU path
used the generated scene's bound serialized production weight texture and
material. No native inference service was involved.

## Reference and comparison

The CPU reference corpus was generated from the real Udon fixture with the
committed raw/baked tensor map. Its manifest reported
`NN_REFERENCE_CORPUS_PASS cases=7 tensor_exact=True`.

```text
Reference: E:\UnityPRJS\ShaderGPT_neoversion\Temp\NNReference\corpus
Unity GPU: E:\UnityPRJS\ShaderGPT_neoversion\Temp\PureUdonGoRulesCompile\Assets\PureUdonGo\Generated\NNCorpus
Reports:   E:\UnityPRJS\ShaderGPT_neoversion\Temp\NNShader\corpus
```

Each of these seven comparisons returned:

```text
PUGNN_DIFFERENTIAL_PASS records=17 atol=0.0001 rtol=1e-05
```

| Case | Max absolute error | Stage | Mismatches |
| --- | ---: | --- | ---: |
| `empty-tromp` | `1.9073486328125e-5` | `rconv10` | 0 |
| `opening-7-tromp` | `2.002716064453125e-5` | `rconv10` | 0 |
| `fight-15-tromp` | `2.1457672119140625e-5` | `trunk_tip` | 0 |
| `ladder-19-tromp` | `2.765655517578125e-5` | `rconv10` | 0 |
| `capture-9-tromp` | `3.5762786865234375e-5` | `rconv10` | 0 |
| `pass-history-tromp` | `2.193450927734375e-5` | `rconv10` | 0 |
| `double-pass-tromp` | `1.239776611328125e-5` | `rconv10` | 0 |

## Scope

This proves the full exposed graph and all four output heads over seven
non-identical 19×19 inputs, including capture, ladder, history, and terminal
pass states. It does not yet prove other graphics backends, Quest hardware,
all shader compiler variants, or VRChat/device runtime behavior.
