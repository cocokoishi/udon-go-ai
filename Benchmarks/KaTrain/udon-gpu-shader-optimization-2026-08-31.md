# GPU float4 graph optimization A/B — 2026-08-31

Status: `VERIFIED` for numerical correctness and measured D3D11 graph timing;
not a device/VRChat latency claim.

The optimization changes `Assets/PureUdonGo/Shaders/PureUdonGoNNLayer.shader`
so convolution, aligned BatchNorm, and aligned vector matrix multiplication
reuse packed RGBA reads. A non-aligned weight base, such as the score head's
`2999715` offset, explicitly keeps the original scalar implementation.

## Numerical gate

The final real Unity log:

```text
E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-gpu-shader-corpus-optimized-final.log
```

It recorded a real NVIDIA GeForce RTX 3060 Laptop GPU run over seven Udon
feature states and 17 graph records per state:

```text
PURE_UDON_GO_GPU_SHADER_PASS cases=7 checkpointsPerCase=17
```

All seven per-case `PUGNN_DIFFERENTIAL_PASS` comparisons passed at
`atol=1e-4`, `rtol=1e-5`; the maximum absolute error remained
`3.5762786865234375e-5` in `capture-9-tromp/rconv10`. The separate runtime
smoke log also recorded a legal `D16` commit after 32 visits and
`readerCompletedStages=5`.

## Graph timing A/B

The same verifier, same fixture order, same Unity project, and same D3D11
device were used for both versions. The baseline was extracted from the local
HEAD shader into the disposable Temp project; the optimized version was then
restored from the working checkout.

| Case | Baseline ms | Optimized ms |
| --- | ---: | ---: |
| `empty-tromp` | 397.612 | 229.512 |
| `opening-7-tromp` | 147.111 | 140.404 |
| `fight-15-tromp` | 127.535 | 130.780 |
| `ladder-19-tromp` | 132.850 | 135.352 |
| `capture-9-tromp` | 160.717 | 175.765 |
| `pass-history-tromp` | 129.287 | 113.811 |
| `double-pass-tromp` | 129.450 | 107.430 |
| **Total** | **1224.562** | **1033.054** |
| **Excluding first case** | **826.950** | **803.542** |

The first case in each fresh Unity process includes shader/process warm-up, so
the total is not used as a clean steady-state speedup. The non-first-case
measurement is a modest `2.83%` improvement and has per-case variance; the
optimization is retained because it reduces the packed-read work without
changing outputs, but a larger end-to-end improvement must come from reducing
Udon scheduling and repeated graph/readback overhead.

This report does not claim Quest performance, VRChat runtime latency, or full
game strength. Those remain separate validation gates.
