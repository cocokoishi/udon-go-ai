# KataGo Model Converter Tools

These tools are developer/CI inputs. They are never part of the normal Unity-world installation path.

## Production source

The only valid production input is the copy already committed in this repository:

```text
.KataGO/g170e-b10c128-s1141046784-d204142634.bin.gz
```

Do not download or substitute a mirror/release copy for this production
model. A precompiled KataGo executable may be used only as disposable local
oracle tooling when explicitly authorized; it is never a model/source/runtime
dependency of Pure Udon Go.

## Tools

- `inspect_model_header.py` — gzip/hash/header preflight.
- `katago_model_parser.py` — complete, strict model-version-8 descriptor/tensor parser specialized for the production architecture family.
- `tests/` — regression tests for complete consumption, malformed data, deterministic EXR packing, direct raw-byte reconstruction, and tamper rejection.

`katago_model_parser.py` follows the descriptor order in the bundled `.KataGO/KataGo-master/cpp/neuralnet/desc.cpp`. It records every binary tensor block with logical shape, source byte offset, element count, and SHA-256, and fails if any decompressed bytes remain unparsed.

## Production bake

The production command is:

```text
python Tools/KataGoModelConverter/convert_model.py \
  --model .KataGO/g170e-b10c128-s1141046784-d204142634.bin.gz \
  --output-dir Assets/PureUdonGo/Model/Generated \
  --width 1024
```

`verify_weights.py` reparses the supplied raw model and compares the decoded
EXR bytes and the emitted `weights_rgba32f.raw` bytes directly with each raw
tensor byte range. Manifest hashes are cross-checked metadata; they are not
the source of expected tensor bytes. `ModelManifest.json` records
source/decompressed/architecture identity, the RGBA32F layout, texture
dimensions, and hashes for every generated artifact. The committed
`weights_rgba32f.asset` is the Unity-serialized runtime copy of the raw
RGBAFloat payload; it avoids importer color/channel conversion while keeping
the EXR and raw artifacts available for independent audit.

## Evidence classes

A local synthetic test pass proves parser invariants only. Exact production architecture/tensor counts become project evidence only after the parser runs against the repository's own `.KataGO/...bin.gz` through a real checkout or this repository's own GitHub Actions.

No end user should run any of these tools.
