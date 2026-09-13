# KataGo Model Analysis

Status: bundled-source analysis plus an implemented project model-version-8
parser and a locally verified production FP32 bake. The previous feature and
runtime reports driven through Editor C# / temporary API stubs are archived in
`Benchmarks/DeprecatedEditorCSharp/` and are not UdonSharp, ClientSim, VRChat,
or final-world evidence. No external repository/mirror evidence is accepted.

## 1. Production source model

Only valid production input:

```text
.KataGO/g170e-b10c128-s1141046784-d204142634.bin.gz
```

Repository metadata currently establishes:

```text
Git blob SHA: 7dc3470c40f2d41b8bffdb5c772fe1698b080f79
Stored file size: 11,138,361 bytes
```

These values establish repository object identity/presence only. They do not establish decoded architecture or tensor values.

## 2. Reference parser

Project oracle:

```text
.KataGO/KataGo-master/cpp/neuralnet/desc.cpp
.KataGO/KataGo-master/cpp/neuralnet/desc.h
```

The project parser is:

```text
Tools/KataGoModelConverter/katago_model_parser.py
```

It is intentionally specialized to model version 8. An incompatible model fails loudly.

## 3. Binary tensor format

Bundled KataGo `readFloats` establishes that binary blocks are:

```text
@BIN@
<expected count of little-endian IEEE-754 float32 values>
```

KataGo validates exact expected count and finiteness. The project parser mirrors the relevant invariants and records each source tensor block with logical shape, element count, byte count, source decompressed offset, and SHA-256 of source tensor bytes.

## 4. Descriptor semantics implemented

For version 8 the project parser implements the bundled source order for:

- `ConvLayerDesc`;
- `MatMulLayerDesc`;
- `MatBiasLayerDesc`;
- `BatchNormLayerDesc`;
- implicit ReLU activation descriptors for pre-v11 models;
- ordinary residual blocks;
- global-pooling residual blocks;
- trunk initial conv/global matmul, residual stack, and tip BN;
- policy head;
- value/score/ownership head;
- top-level model name/version/spatial/global input metadata.

The parser also reproduces important cross-layer channel checks rather than merely consuming byte lengths.

## 5. Tensor ordering

Bundled KataGo documents convolution model-file order as:

```text
y, x, input-channel, output-channel
```

The production texture manifest must preserve this logical order explicitly even if shader packing uses a different physical order. Any reordering must be deterministic and reversible.

## 6. Batch normalization

Bundled source reads mean, variance, optional scale, and optional bias and derives:

```text
mergedScale = scale / sqrt(variance + epsilon)
mergedBias  = bias - mergedScale * mean
```

The production bake may store merged affine terms for runtime efficiency, but original raw BN tensors remain part of the reconstruction evidence chain.

## 7. Parser and production bake evidence

Local synthetic regression command (executed from `Tools/KataGoModelConverter`):

```text
python -m unittest discover -s Tools/KataGoModelConverter/tests -v
```

Observed:

```text
11 tests / 11 passed / exit code 0
```

This is `LOCAL / SYNTHETIC` evidence for malformed-input and packing invariants.

The same tools were then run against the repository's own raw model:

```text
compressed_size_bytes=11138361
compressed_sha256=1a8e05a4ea3fca20dab79410cbb566c760767fcdd2fa0b701cfe259a84cc8b04
decompressed_size_bytes=12003218
decompressed_sha256=49b75eacdfd8587fe153e6889070d509504750a45fda16105e67bf8c456148d4
model_name=g170-b10c128-s1141046784-d204142634
model_version=8
tensor_count=127
tensor_element_count=3000071
bytes_consumed=12003218/12003218
architecture_sha256=b4e93cc11da2539f79e7a17d8c3deee360873730d90f059853cb62a71360cda3
```

The production bake generated `Assets/PureUdonGo/Model/Generated/weights_rgba32f.exr`
at 1024×733, with SHA-256
`e00f701b6ef1498037e56a330f6243d2878ff9c74e6f6fddd5b0cf06a2f94309`.
It also emits the exact padded `weights_rgba32f.raw` payload with SHA-256
`10285a17fd7a47a3b6bdde08a26d13c34c9bbcde566dfbd66c5fa27911c65866`.
The committed `weights_rgba32f.asset` is the Unity-serialized RGBAFloat
runtime copy made from those raw bytes.
Independent direct raw-byte reconstruction reports 127 exact tensors and 0
failures. A repeat bake produced identical SHA-256 values for the EXR, raw
payload, and JSON manifests. This is `EQUIVALENCE VERIFIED` for raw-to-baked
tensor reconstruction.

## 8. Production execution path

The target-repository workflow `.github/workflows/model-inspection.yml` is configured to:

1. checkout the current `cocokoishi/udon-go-ai` repository;
2. enforce repository isolation;
3. run model-tool unit tests;
4. hash/probe the repository raw model;
5. parse every raw tensor and require exact EOF consumption;
6. write `Tests/ReferenceData/model_tensor_manifest.json`;
7. validate deterministic outputs in the workflow workspace without mutating
   the remote repository;
8. upload the evidence as a target-repository artifact.

Target workflow run `33296740663` failed before runner steps existed (`steps = null`) and remains `TARGET-REPOSITORY CI BLOCKED`. Local production parsing and baking are now directly verified; CI status must not be used as a substitute for that evidence.

## 9. Next model milestone

The model milestone is complete through Unity asset import and shader-level
GPU diagnostics:
the EXR audit texture is imported as linear, no mipmaps/compression, point
filtering, clamp wrap, and `RGBAFloat`, while the runtime binds the committed
serialized RGBAFloat asset because Unity's EXR path on this host does not
preserve the float lanes numerically. A V7 encoder base layer now compiles and
matches local source-derived fixtures. The earlier 16-case KataGo input
comparison was generated by the Unity Editor C# / temporary-stub path and is
retained only as `DEPRECATED_EDITOR_CSHARP` evidence under
`Benchmarks/DeprecatedEditorCSharp/`. It must be rerun from an actual UdonSharp
and ClientSim execution before it can be used as production feature equivalence.
Broader ko/handicap/long-history coverage remains open.
The bake tool consumes only the repository raw model through the committed
parser and refuses architecture/version mismatches.

No external mirror/release/model-hub copy is a valid production model input,
even if its hash would match. A user-authorized precompiled KataGo release was
used only as disposable local oracle tooling and is not part of the product
runtime or model bake.
