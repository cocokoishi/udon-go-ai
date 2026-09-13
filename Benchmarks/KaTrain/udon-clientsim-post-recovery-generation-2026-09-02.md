# Real Udon generation/validator regression after reader quarantine — 2026-09-02

Status: `PASS` for the current one-click generation and structural validation
path in an isolated Unity project.

Environment:

- Unity `2022.3.22f1`
- Direct3D 11 (`-force-d3d11`)
- copied host license accepted; logs contain `Successfully resolved entitlements`
- isolated project: `E:\UnityPRJS\ShaderGPT_neoversion\Temp\PureUdonGoClientSimPostRecovery`

The current repository source was synchronized into the isolated project. The
first run executed the production generator and real UdonSharp compile. The
second run opened the generated scene again and rebuilt the owned root in
place. Both runs produced the same structural counts:

```text
PURE_UDON_GO_GENERATOR_PASS scene=Assets/PureUdonGo/Generated/Scenes/PureUdonGoProduction.unity root=PureUdonGo.ProductionRoot cells=361 panel=3-screen ultraHardPresetVisits=514 engineMaxVisits=1226
PURE_UDON_GO_UDON_WORLD_RESULT scene=Assets/PureUdonGo/Generated/Scenes/PureUdonGoProduction.unity generatedRoots=1 udonBehaviours=6498 programs=46 compiled=46 compilerError=False
PURE_UDON_GO_UDON_WORLD_PASS
```

The regenerated pool contains 16 independent executor/runtime stacks and two
readback channels per table (32 readers total). The second channel is the
stale-callback quarantine path; it shares only its table's immutable/runtime
resources and is not a cross-table mutable search state.

The validator then passed after being updated for this intentional A/B reader
pair:

```text
PURE_UDON_GO_VALIDATOR_PASS failures=0 warnings=1 scene=Assets/PureUdonGo/Generated/Scenes/PureUdonGoProduction.unity model=g170e-b10c128-s1141046784-d204142634 maxVisits=1226
```

Evidence logs:

- `E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260901-post-recovery-generate-new-v1.log`
- `E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260902-post-recovery-generate-new-v2.log`
- `E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\pure-udon-go-cs20260902-post-validator-v2.log`

The one warning is the isolated-project condition that the raw KataGo oracle
file is outside the Unity project root; committed baked weights and manifests
were still checked. This is generator/compile/structural evidence, not proof
of physical VRChat hardware behavior or multi-client transport.
