# Udon Go AI

<a href="https://vrchat.com/home/world/wrld_52b8fc72-0ad9-4c27-ac77-5b15137d0afd"> <img width="1920" height="1080" alt="VRChat_2026-09-13_14-16-07 646_1920x1080" src="https://github.com/user-attachments/assets/b7996877-8231-456f-a81e-6eeede29f9e9" /> </a>

## [▶ **PLAY UDON GO AI NOW IN VRCHAT**](https://vrchat.com/home/world/wrld_52b8fc72-0ad9-4c27-ac77-5b15137d0afd)

Udon Go AI is an open-source 19×19 Go world for VRChat, built with Unity, UdonSharp, and the VRChat Worlds SDK. It combines exact Go rules, KataGo-style `INPUTSVERSION 7` features, GPU-shader neural inference, and cooperative PUCT/MCTS search in a world that runs without Python, KataGo, or a web service at runtime.

License: **GNU Affero General Public License v3.0 only** (`AGPL-3.0-only`). Copyright © 2026 cocokoishi for original Udon Go AI material. See [`LICENSE`](LICENSE) and [`Docs/THIRD_PARTY_NOTICES.md`](Docs/THIRD_PARTY_NOTICES.md).

> This repository is a Unity package/source distribution, not a complete Unity project. A compatible VRChat Worlds project is required; Just clone this repo into Assets/.

## Getting started

1. Create or open a VRChat Worlds Unity project using Unity `2022.3.22f1`.
2. Install the VRChat Worlds SDK, UdonSharp, and TextMeshPro Essential Resources.
3. Clone this repo into Assets/
4. Run the generator via:

`Tools > Pure Udon Go > Generate Final Production Scene`

5. Open the generated scene, add it to the host project's build scenes, and test/build it through the normal VRChat SDK workflow.

The generator creates:

```text
Assets/PureUdonGo/Generated/Scenes/PureUdonGoProduction.unity
Assets/PureUdonGo/Generated/Materials/
Assets/PureUdonGo/Generated/Textures/
Assets/PureUdonGo/Generated/Audio/
Assets/PureUdonGo/Generated/GoGenerationReport.txt
```

Generation is idempotent. Regenerate after changing the serialized runtime or generator schema instead of manually patching an older generated scene.

The default generated room has three visible tables and starts in PvAI mode with White controlled by the AI. The production board uses one world-space UGUI receiver with 361 transparent targets and one UIShape trigger collider; stones and the board pedestal do not create solid interaction colliders.


## Features

* 19×19 Go board with capture, ko, positional superko/history, pass, double-pass, handicap, komi, scoring, resignation, move limits, undo, and draw handling.
* PvP, PvAI, AIvP, and AIvAI match modes.
* Three visible Go tables, each with isolated game, AI, GPU, search, and readback state.
* Generated indoor world with board, 361-point UGUI input, control wall, analysis panel, markers, pooled stones, audio, camera, spawn, and VRChat scene descriptor.
* Synchronized shared AI settings with independent Black/White custom visit controls.
* 22 spatial and 19 global KataGo V7-style input planes, including history, ladder, ko, and legality information.
* Policy/pass, value, score, and ownership neural heads.
* Cooperative GPU graph execution and one packed asynchronous readback per neural evaluation.
* Stale-result protection across position revision, search token, node/hash identity, and ownership lifecycle.
* Editor generation and validation tools, ClientSim verifiers, independent Go rule/feature references, model conversion tools, and benchmark comparators.

## Architecture

```text
GoBoardInput / GoUI
        |
        v
     commands
        |
  +-----+------+
  |            |
  v            v
GoGame     GoAiSettings
rules      synchronized choices
  |            |
  +-----+------+
        v
  GoAiController
        |
  SearchSession
        |
 +------+---------+----------------+
 |                |                |
 v                v                v
GoMctsSearch  GoFeatureEncoder  GoGpuNeuralRuntime
                                      |
                                      v
                          GoGpuNeuralOutputReader
```

`GoGame` owns the authoritative board, match, seats, rules, offers, hints, and position lifecycle. `GoAiSettings` owns synchronized AI choices. Search trees, feature buffers, GPU resources, asynchronous readers, UI drafts, and telemetry remain local to each table.

## Repository layout

| Path                                 | Description                                                                       |
| ------------------------------------ | --------------------------------------------------------------------------------- |
| `Assets/PureUdonGo/Runtime/`         | Runtime UdonSharp behaviours and serialized program assets                        |
| `Assets/PureUdonGo/Editor/`          | World generator, validator, model importer, compile gate, and ClientSim verifiers |
| `Assets/PureUdonGo/Shaders/`         | `PureUdonGo/NNLayer` neural-network shader                                        |
| `Assets/PureUdonGo/Model/Generated/` | Serialized runtime weights, audit data, and model manifests                       |
| `Assets/PureUdonGo/Fonts/`           | UI font and its license notice                                                    |
| `Assets/PureUdonGo/Tests/`           | Unity/UdonSharp probes and test fixtures                                          |
| `Tests/`                             | Independent Python rule/feature references and test sources                       |
| `Tools/`                             | Model conversion, neural/rules/feature comparisons, and search references         |
| `Docs/`                              | Architecture, networking, search, performance, verification, and notices          |
| `.github/workflows/`                 | Model inspection and baking CI workflow                                           |
| `Benchmarks/`                        | ClientSim, oracle, and historical benchmark evidence                              |

Keep Unity `.meta` files beside the corresponding `Assets/` files when moving the package into another Unity project.

The detailed engineering notes are indexed in [`Docs/README.md`](Docs/README.md). Start with the root README for installation, then use the active documentation when changing runtime behavior or running the verification matrix.

## Requirements

### Unity and VRChat

* Unity `2022.3.22f1` on Windows.
* VRChat Worlds SDK and UdonSharp.
* TextMeshPro and TextMeshPro Essential Resources.
* A graphics backend/GPU supporting the generated neural shader path. The development evidence is primarily Windows/D3D11 and does not prove Quest or Android compatibility.
* ClientSim for editor-side runtime verification.

### Optional developer tools

* Python 3.12 for the independent tests and conversion/reference tools.
* NumPy for the CPU neural reference and selected comparison tools.
* An authorized local KataGo/KaTrain installation for external oracle or strength comparisons. It is not a runtime dependency of the world.

## Built-in AI profiles

The current source revision defines these built-in profiles:

| Profile   | Visits | NN queries | Transitions/frame | PUCT `cpuct` | Temperature | Policy top-K | Resign threshold |
| --------- | -----: | ---------: | ----------------: | -----------: | ----------: | -----------: | ---------------: |
| Beginner  |      8 |          8 |                 1 |         1.85 |        0.10 |           24 |            -1.00 |
| Advanced  |     20 |         20 |                 2 |         1.60 |        0.10 |           64 |            -1.00 |
| Master    |     48 |         48 |                 4 |         1.35 |        0.10 |          128 |            -0.98 |
| Ultrahard |    120 |        120 |                 8 |         1.25 |        0.10 |          362 |            -0.95 |

The player-facing label is spelled exactly `Ultrahard`. Black and White can follow the shared profile or use their own synchronized Custom visit value. Searches capture their resolved profile when they start, so an applied settings change is used by the next search rather than mutating an active search.

## Model pipeline

The runtime loads the serialized Unity asset:

```text
Assets/PureUdonGo/Model/Generated/weights_rgba32f.asset
```

The checked-in manifests describe the current model identity, tensor layout, RGBA32F atlas, and reconstruction hashes. The `.exr` and `.raw` files are kept for audit and independent reconstruction; the normal VRChat runtime does not start KataGo or read the compressed source model.

For a development checkout with permission to use the raw model, rebuild the generated assets with:

```powershell
python Tools/KataGoModelConverter/convert_model.py `
  --model path/to/g170e-b10c128-s1141046784-d204142634.bin.gz `
  --output-dir Assets/PureUdonGo/Model/Generated `
  --width 1024
```

Do not substitute another model without updating the manifests, runtime layout, numerical evidence, and licensing record. Model weights and model-derived assets are not automatically covered by the KataGo source license.

## Testing and verification

Python tests can be run from the repository root with Python 3.12:

```powershell
python -m unittest discover -s Tests -v
python -m unittest discover -s Tools/KataGoModelConverter/tests -v
python -m unittest discover -s Tools/KaTrain/tests -v
python Tools/RepositoryBoundary/check_repository_boundary.py
```

The Unity editor menus include gates for:

* rules, legal-move matrices, derived history, and superko;
* V7 feature and neural/GPU numerical equivalence;
* GPU graph slicing and packed readback recovery;
* settings lifecycle and Custom visits;
* stale results, ownership, permissions, undo/draw, and remote-player recovery;
* UI/hints, BoardPool scheduling, long-history stress, search calibration, complete-game smoke, A–H performance, and one/two/three-table concurrency.

See [`Docs/VERIFICATION.md`](Docs/VERIFICATION.md) for the current gate matrix and evidence status. ClientSim is an editor diagnostic environment; real two-client VRChat transport, PC/PCVR profiling, and Quest/Android execution must be validated separately.

## Third-party notices

The root `LICENSE` applies to original Udon Go AI material that the copyright holder is authorized to license. It does not relicense third-party material.

* KataGo source code is licensed under the **MIT License**. See [`Docs/THIRD_PARTY_NOTICES.md`](Docs/THIRD_PARTY_NOTICES.md) for attribution and licensing information.
* The production neural model, its baked weights, and benchmark data may have separate redistribution terms. Confirm those terms before publishing them.
* The bundled CJK font is accompanied by `Assets/PureUdonGo/Fonts/Noto-CJK-LICENSE.txt` and remains under its own font license.
* Unity, TextMeshPro, VRChat SDK/UdonSharp, ClientSim, and any bundled Unity package remain subject to their respective supplier terms.

Full provenance is maintained in [`Docs/THIRD_PARTY_NOTICES.md`](Docs/THIRD_PARTY_NOTICES.md).

## Prefab Usage

When the Udon Go AI Go-table Prefab is used **intact and unmodified** in a VRChat world, it is sufficient to retain the built-in author and project attribution shown in the lower-left corner of the Prefab. No additional attribution is required, and the rest of the VRChat world does not need to be open-sourced solely because it includes this Prefab.

If the Prefab or any of its code, shaders, assets, or runtime logic is modified, this exception no longer applies, and the resulting use must comply with the normal terms of AGPL-3.0-only.

In that case, the modified version of Udon Go AI must be made available in source form under AGPL-3.0-only. Any other part of the VRChat world that, together with the modified Udon Go AI, constitutes a single combined work or otherwise falls within the scope of AGPL-3.0-only must also be made available under the terms required by AGPL-3.0-only.

This exception applies only to original Udon Go AI material that cocokoishi is authorized to license.

## License

Original Udon Go AI material that the copyright holder is authorized to license is licensed under the **GNU Affero General Public License v3.0 only**.

See [`LICENSE`](LICENSE) for the complete license text. Third-party components and data—including model weights, fonts, SDKs, Unity packages, and benchmark corpora—are excluded from this blanket licensing statement and remain under their own terms.
