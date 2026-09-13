# Real Unity GPU Shader Numerical Checkpoint — 2026-08-31

Status: `EQUIVALENCE VERIFIED` for the tested empty-board production state.

This is a Unity graphics validation artifact, not a strength claim. The
verifier loaded the generated Pure Udon Go scene and ran the actual
`PureUdonGo/NNLayer` Shader through the production `GoGpuNeuralRuntime`. It
then read back every exposed RenderTexture checkpoint and wrote the values in
the repository's `PUGNN01` format.

## Runtime evidence

Unity log:

```text
E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-gpu-shader-verify-2.log
```

The log recorded:

```text
PURE_UDON_GO_GPU_SHADER_RESULT device=NVIDIA GeForce RTX 3060 Laptop GPU passes=83 output=...
PURE_UDON_GO_GPU_SHADER_PASS checkpoints=17
```

The Unity run was performed without `-nographics`, with `-force-d3d11`, and
used the generated scene's bound production weight `Texture2D`, material, and
shader. No native inference service or external runtime was involved.

## Comparison

CPU reference export:

```text
E:\UnityPRJS\ShaderGPT_neoversion\Temp\NNReference\production-reference.pugnn
```

Unity GPU export:

```text
E:\UnityPRJS\ShaderGPT_neoversion\Temp\PureUdonGoRulesCompile\Assets\PureUdonGo\Generated\shader-actual.pugnn
```

Differential report:

```text
E:\UnityPRJS\ShaderGPT_neoversion\Temp\NNShader\differential-2.json
```

Command result:

```text
PUGNN_DIFFERENTIAL_PASS records=17 atol=0.0001 rtol=1e-05
```

The 17 records are initial trunk, ten residual blocks, trunk tip, policy
spatial/pass, value, score, and ownership. There were zero mismatches. The
maximum absolute errors by the deepest stages were:

```text
rconv10       1.9073486328125e-05
trunk_tip     1.33514404296875e-05
policy        5.0067901611328125e-06
value         4.76837158203125e-07
score         2.384185791015625e-07
ownership     3.1478703022003174e-07
```

The CPU reference first verified the raw model against the committed baked
atlas: 127 tensors, `failure_count=0`, exact bytes. The complete graph test
therefore checks the actual Unity texture/shader path against the intended
KataGo-derived tensor data for this input.

## Scope

This checkpoint proves one real D3D11 GPU graph input and all exposed outputs.
It does not yet prove numerical equivalence over a multi-position corpus,
other graphics backends, Quest hardware, or all VRChat shader restrictions.
