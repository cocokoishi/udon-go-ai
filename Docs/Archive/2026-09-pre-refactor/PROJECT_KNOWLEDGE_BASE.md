# Pure Udon Go Project Knowledge Base

> Historical reference. The current executable contract is in
> [`README.md`](README.md), [`ARCHITECTURE.md`](ARCHITECTURE.md),
> [`NETWORKING.md`](NETWORKING.md), [`UI_AND_SETTINGS.md`](UI_AND_SETTINGS.md)
> and `AGENTS.md`; statements below may describe superseded revisions.

This document is compact long-term technical memory for development agents, especially smaller local coding models.

`AGENTS.md` is the hard policy. This file explains what the project is, what the important subsystems mean, what references are allowed, how local Unity validation works, and how to prove the AI is actually correct and performant.

Repository code/tests and measured Unity behavior are the final source of truth. If this file becomes stale, update it after obtaining real evidence.

---

## 1. Product goal

Pure Udon Go is a complete 19x19 Go world for VRChat with an embedded KataGo-derived neural-network AI.

Final normal-user workflow:

```text
Open Unity project
 -> Tools > Pure Udon Go > Generate Final Production Scene
 -> ready to Play/ClientSim/build
```

The end user must not run Python, KataGo, converters, GitHub Actions, external inference services, or manually wire shaders/materials/RenderTextures/Udon behaviours.

The complete product chain is:

```text
raw KataGo model
 -> deterministic baked model assets
 -> correct Go rules/history
 -> exact KataGo NN features
 -> shader NN inference
 -> real PUCT/MCTS
 -> owner-authoritative networking
 -> board/UI/world presentation
 -> one-click idempotent generator
 -> production validator
 -> real Unity/runtime evidence
```

A beautiful world with fake AI is incomplete. A correct AI without the one-click world is incomplete.

---

## 2. Small-model read/work order

At every substantial session:

1. read `AGENTS.md`;
2. read this file;
3. read `Docs/PROGRESS.md` but verify it against Git;
4. inspect `git status`, HEAD and recent commits;
5. inspect only the subsystem needed for the current milestone;
6. run existing tests before rewriting code.

Work on one dependency-complete milestone at a time:

```text
inspect -> implement -> test -> inspect output/log -> document -> commit
```

Do not simultaneously redesign model packing, Go rules, shaders, MCTS, networking, UI and Generator.

---

## 3. Exact local access model

### Writable production root

The local checkout of:

```text
udon-go-ai
```

is the only production source tree that may be modified.

### Writable disposable Temp root

The exact allowed disposable path is:

```text
E:\UnityPRJS\ShaderGPT_neoversion\Temp
```

Do not invent a generic `<sandbox>/temp` path.

Use this exact subtree for clean Unity test projects, logs, generated test scenes, benchmark data, temporary packages created from this repository, and sandbox-visible Unity license material.

Although this Temp directory is physically inside `ShaderGPT_neoversion`, the parent ShaderGPT project is not writable. Only its exact `Temp` subtree is writable.

### Read-only Udon Xiangqi reference

The user's local Udon Xiangqi / Pure Udon Xiangqi project is an explicitly authorized read-only reference.

It may be inspected for:

- `WorldGenerator` structure and idempotent asset generation;
- production validator architecture;
- UI hierarchy and same-family visual language;
- panel colors, spacing, typography and control proportions;
- board/table/world composition;
- Udon organisation;
- `VRCGraphics.Blit` GPU patterns;
- async GPU readback scheduling;
- authority/revision protection;
- telemetry/status/audio language;
- ClientSim/test patterns.

Do not modify, clean, migrate, reimport, rename, regenerate, delete, or use the Xiangqi project as a writable test project.

When useful, intentionally copy/adapt code or assets from the read-only Xiangqi reference into `udon-go-ai`; the source reference remains untouched.

Other unrelated Unity/VRChat projects remain out of scope.

---

## 4. Unity license in a sandboxed local agent

The host Windows machine may already be correctly activated in Unity, while the sandboxed local agent cannot see the normal host license storage automatically.

That means:

```text
host Unity is licensed
!=
sandboxed Unity CLI can see the license
```

The correct workflow is to expose/copy the host machine's existing valid license material into the allowed Temp area before invoking Unity CLI.

Relevant Windows host license locations depend on Unity/license type and may include:

```text
%PROGRAMDATA%\Unity\Unity_lic.ulf
%LOCALAPPDATA%\Unity\licenses\UnityEntitlementLicense.xml
%LOCALAPPDATA%\Unity\licenses\*.xml
%LOCALAPPDATA%\VirtualStore\ProgramData\Unity\Unity_lic.ulf
```

The agent has a narrow read-only exception for those Unity license locations solely to obtain the host's already-active license material.

Copy needed material to:

```text
E:\UnityPRJS\ShaderGPT_neoversion\Temp\UnityLicense
```

Do not request a new license merely because the sandbox cannot see the existing one. Do not return/deactivate the host license.

### `.ulf` licenses

For a serial-based `.ulf`, Unity supports explicit import with `-manualLicenseFile`, for example:

```text
Unity.exe -batchmode \
  -manualLicenseFile "E:\UnityPRJS\ShaderGPT_neoversion\Temp\UnityLicense\Unity_lic.ulf" \
  -quit \
  -logFile "E:\UnityPRJS\ShaderGPT_neoversion\Temp\Logs\unity-license.log"
```

Use the real installed Unity executable/version.

### Named User / entitlement licensing

Modern Named User licensing may use an entitlement XML rather than a `.ulf`.

Do not force `-manualLicenseFile` onto an entitlement XML. Instead expose/copy the existing entitlement material into the sandbox-visible location expected by that Unity/Hub licensing stack and prove success by actually launching Unity in batchmode.

When the exact mechanism is uncertain, inspect the installed Unity/Hub version and official Unity licensing documentation rather than inventing flags.

### Proof of license visibility

A copied file is not proof. Run minimal batchmode and inspect the log.

If Unity still reports a licensing error, classify it as a sandbox license exposure/import problem and fix that before claiming Unity runtime verification.

Never commit license contents or print license secrets/tokens in reports.

---

## 5. Repository map

Important areas:

```text
.KataGO/
  KataGo-master/                       bundled KataGo reference/oracle
  g170e-b10c128-s1141046784-d204142634.bin.gz
                                       production raw model

Tools/KataGoModelConverter/            model parser/bake/verification tools
Tests/                                 reference/equivalence/regression tests
Assets/PureUdonGo/                     production Unity/Udon/shader assets
Docs/                                  architecture/evidence/project memory
.github/workflows/                     this repository CI only
```

Prefer not to modify `.KataGO/KataGo-master/`; it defines KataGo semantics and acts as the local oracle.

Current committed development has already progressed beyond planning and includes model-conversion tooling, Go-rule reference/regression work, and an authoritative 19x19 `GoGame.cs`. Always re-check current HEAD because this knowledge base will age.

---

## 6. Go runtime fundamentals

Production board:

```text
19 x 19 = 361 intersections
```

A serious rules engine needs:

- chain/group construction;
- liberties;
- captures;
- suicide behavior matching selected rules;
- ko;
- required superko semantics;
- pass and double-pass termination;
- komi;
- handicap when exposed;
- legal move generation;
- history required by KataGo inputs;
- scoring/end-state handling;
- resignation;
- deterministic reset/network behavior.

Udon hot paths should favor fixed/preallocated arrays and iterative algorithms over LINQ, recursion, per-move object allocation, and large managed collections.

### Required rule tests

Use both crafted cases and randomized legal games:

```text
corner/edge capture
multi-stone capture
self-atari
suicide
simple ko
ko recapture after intervening move
superko cycle
snapback
seki-like structures
ladder-like fights
capturing races
almost-full board
passes/double pass
handicap
scoring positions
```

A placement/capture-only implementation is not complete Go.

---

## 7. Raw KataGo model and parser

The only production raw model is:

```text
.KataGO/g170e-b10c128-s1141046784-d204142634.bin.gz
```

The bundled `.KataGO/KataGo-master/` source is the oracle for model format, NN input semantics, rule semantics and relevant neural operations.

The project parser/bake tools must derive real values from the raw file rather than trust prose.

Important parse evidence includes:

- compressed/decompressed hashes;
- model version/name;
- spatial/global input counts;
- residual/gpool block sequence;
- channels;
- tensor names/shapes;
- tensor offsets/hashes;
- exact EOF consumption;
- rejection of malformed/non-finite tensors.

---

## 8. Baked neural weights

Development flow is conceptually:

```text
raw .bin.gz
 -> parser
 -> tensor metadata
 -> canonical/reference packing
 -> Unity-readable weight texture(s)
 -> independent decoder
 -> raw-to-baked comparison
```

The current FP32 RGBA EXR route is a correctness baseline. It is not automatically the optimal runtime layout.

Unity weight import must preserve numeric data. At minimum validate:

```text
sRGB = false
Mip Maps = false
Compression = None
FilterMode = Point
WrapMode = Clamp
```

and verify the actual imported texture precision/platform format in real Unity.

### Independent verification matters

Weak check:

```text
EXR decoded hash == expected hash copied from packer manifest
```

Strong check:

```text
raw .bin.gz parsed independently
 -> raw tensor bytes
vs
EXR/runtime texture decoded tensor bytes
```

Lossless paths should support exact/bitwise reconstruction.

If runtime packing reorders weights for GPU locality, the mapping must remain deterministic, reversible, bounds-checked, and covered by a CPU reference.

---

## 9. BatchNorm runtime optimization

At bake time BatchNorm may be reduced to:

```text
mergedScale = scale / sqrt(variance + epsilon)
mergedBias  = bias - mergedScale * mean
output      = input * mergedScale + mergedBias
```

This avoids repeated runtime square roots/divisions.

Keep original raw BN tensors in the reproducibility/equivalence chain.

Fuse BN/ReLU with other shader passes only when correctness is preserved and verified.

---

## 10. KataGo feature encoding

Correct weights with wrong input features produce a wrong AI.

Feature semantics must be derived from bundled KataGo source, including:

- spatial planes;
- global inputs;
- coordinate orientation;
- current player perspective;
- recent history;
- ko/history values;
- komi;
- rules;
- pass/history behavior.

Best validation:

```text
same board + history + rules
 -> bundled KataGo feature output
 -> Pure Udon Go feature output
 -> element-by-element comparison
```

If Udon-side expansion becomes expensive, production may upload compact board/history state and expand features in a shader, but CPU/Udon-equivalent reference semantics must remain available.

---

## 11. Shader NN architecture

Target runtime:

```text
Go state/history
 -> feature activation texture
 -> initial trunk
 -> residual/global-pooling blocks
 -> policy/value/score/ownership heads
 -> compact result texture
 -> async GPU readback
 -> search
```

Intermediate activations stay on GPU.

Avoid layer-by-layer GPU -> CPU -> GPU synchronization.

Prefer minimum practical readback count.

Use VRChat-compatible Unity rendering operations/materials/RenderTextures; do not introduce an external inference server or native runtime.

### Shader correctness

Compiling is not enough.

Maintain a CPU/shader-equivalent reference that matches actual deployed:

- weight texture addressing;
- RGBA lane layout;
- feature mapping;
- convolution order/padding;
- accumulation semantics;
- merged BN;
- ReLU;
- residual addition;
- global pooling;
- matmul;
- policy/pass outputs;
- value/score outputs;
- ownership outputs.

Validate progressively and locate the first divergence:

```text
features
initial layer
block 0
block 1
...
trunk tip
policy head
value/score head
ownership
full network
```

For lossy FP16 paths record max abs error, mean abs error, RMSE, percentiles and task-specific output differences. Do not assume FP16 is safe without data.

---

## 12. Static NN cost model

Before aggressive shader optimization, calculate each real layer's:

```text
kernel
Cin/Cout
spatial dimensions
MACs
weights
activation reads/writes
proposed blit count
```

Aggregate:

```text
MACs/inference
weight footprint
activation footprint
logical passes
texture traffic estimate
```

This is planning evidence, not runtime speed evidence.

Actual speed must be measured in Unity/D3D11 and eventually VRChat.

---

## 13. PUCT/MCTS

Strong modes require real search:

```text
root
 -> PUCT/FPU selection
 -> expansion
 -> neural evaluation
 -> backup
 -> repeat
 -> choose move
```

Policy argmax alone is not Master.

Use Udon-friendly fixed-capacity arrays rather than allocating objects per tree node.

Typical node data:

```text
parent / child structure
move
prior
visit count
value sum
pending/virtual loss
expanded/terminal flags
```

### Frame budgeting

Search must be a multi-frame state machine, e.g.:

```text
Idle
PrepareRoot
Select
PrepareFeatures
DispatchNN
WaitNN
Readback
Expand
Backup
NextVisit
ChooseMove
Commit
```

Do not freeze a VRChat frame with one giant visit loop.

### Batching and tree reuse

Investigate batching only if it measurably improves throughput.

Reuse a tree only when the new authoritative position is proven to be a valid searched child. Reset/rules/difficulty/ownership changes invalidate stale search state.

---

## 14. Difficulty design

Use Go-native presets such as:

```text
Beginner
Advanced
Master
UltraHard
Custom
```

Possible internal knobs:

```text
maxVisits
maxNNQueries
think/frame budget
cpuct
FPU
root/chosen temperature
root noise
policyTopK/maxChildren
resign threshold
pass behavior
tree reuse
```

Beginner should still be NN-driven; it may use tiny search and sampling/noise.

Master must perform genuine multiple-visit PUCT/MCTS.

The current public ladder is exact and intentionally inspectable:

```text
Beginner    4 visits
Advanced   32 visits
Master    128 visits
Ultrahard 384 visits
Custom    clamped to the fixed tree capacity
```

`GoMctsSearch` reserves `444174` policy edges (`362 * 1227`) and therefore
accepts exactly `1226` completed visits for Custom. The four public names are
not cosmetic; the active side profile supplies the visit/query budget and frame
transition rate to the PUCT state machine. Ultrahard is the strongest preset in
the public ladder at `384` visits; the remaining fixed capacity is not exposed
under that label.

Do not expose Xiangqi/Chess Depth/QDepth controls as Go difficulty.

---

## 15. Networking and stale async results

The game is owner-authoritative.

Only the authoritative client should perform expensive full AI search. Other clients synchronize game state/results rather than NN tensors or the whole MCTS tree.

The owner also publishes a compact synchronized final-analysis snapshot
(candidate visits, color-fixed win probabilities, `SCORE W-B`, and
white-positive ownership) so the same result remains visible after the move
and after a late join. AI Hint permission is a separate pre-match synchronized
opt-in and is frozen by `matchStarted`.

Protect asynchronous GPU/search results using identity such as:

```text
revision
settingsRevision
move number / position hash
owner lifecycle
```

Discard results returned after:

- reset/new game;
- human move;
- game end;
- owner transfer;
- difficulty change;
- rules change.

The match controller is also synchronized at the side level. Black and White
each have a `PLAYER`/`AI` flag, an optional exclusive player seat, an AI-AI
authority identity, a selectable `starter`, and a `matchStarted` gate. The UI
actions mirror the Xiangqi reference semantics: join Black/White, leave seat,
toggle either controller, swap a single human side, choose Black/White first,
start/reset, and edit/apply the selected side's AI profile. Legacy `mode` and
`aiColor` fields are retained as compatibility mirrors only. Controller, seat,
starter, and profile changes invalidate the active search before any new move.

An old owner must never be able to commit a move after ownership transfer.

---

## 16. Udon Xiangqi as product-family reference

Do not rely on remembered prose when the authorized local Xiangqi project can be inspected read-only.

Study it directly for:

- generator organization;
- generated asset naming;
- scene layout;
- dark modern environment;
- Background / Surface / SurfaceRaised hierarchy;
- blue-gray accent language;
- typography and bilingual/status treatment;
- controls/button proportions;
- table/board positioning;
- telemetry;
- audio feedback;
- GPU scheduling conventions;
- networking/revision safeguards;
- production validator philosophy.

Adapt those patterns to Go rather than cloning Xiangqi content.

The final Go centerpiece should be a polished 19x19 wooden board with star points and black/white stones. UI/search controls should be Go-native.

The acceptance target is that a player familiar with Pure Udon Xiangqi can recognize Pure Udon Go as the same author's product family.

The Xiangqi source project remains read-only at all times.

---

## 17. VR board interaction

A 19x19 board has 361 intersections. Avoid 361 expensive independent Udon behaviours if centralized hit mapping can solve interaction.

Preferred concept:

```text
board collider / interaction surface
 -> local hit XZ
 -> nearest grid intersection
 -> hover/legal feedback
 -> authoritative TryPlay(location)
```

Required player feedback includes:

- hover point;
- legal/illegal placement;
- last-move marker;
- ko marker;
- captures;
- thinking status;
- pass/resign/end state;
- optional policy/ownership overlays.

Board/table scale must be validated in real VR/ClientSim for pointing accuracy and readability.

---

## 18. One-click Generator

The fixed production path is:

```text
Tools > Pure Udon Go > Generate Final Production Scene
```

`GoWorldGenerator` must ultimately create/repair all owned production content, including:

- environment/world root;
- table/board/grid/star points;
- stone visuals/interactions;
- game controller;
- AI/search controller;
- GPU NN resources;
- materials/RenderTextures;
- weight bindings;
- networking;
- UI/telemetry;
- independent Black/White controllers, seats, starter controls and AI-AI mode;
- audio;
- final scene.

It must be idempotent. Running twice must not produce `(1)`, `(2)` duplicate roots/materials/controllers.

If baked model assets are missing/corrupt, fail as a developer/incomplete-checkout problem, not by asking the end user to run Python.

---

## 19. Production Validator

`GoProductionValidator` checks at least:

### Model

- manifest/source identity;
- expected architecture/packing;
- files/hashes/dimensions.

### Unity imports

- linear/non-sRGB;
- no mipmaps;
- no compression;
- point filter;
- clamp wrap;
- correct precision/platform settings.

### GPU pipeline

- shaders/materials exist;
- properties are bound;
- RenderTextures have correct dimensions/formats;
- pass/resources are consistent.

### Scene/runtime

- game/view/UI/AI/network references;
- no missing scripts;
- valid difficulty profiles;
- independent Black/White controller/profile bindings and 26-button control wall;
- seat/starter/match-start state and owner/revision wiring;
- revision/authority wiring;
- unique generated root.

Return actionable PASS/WARN/FAIL.

Validator PASS does not replace NN numerical equivalence.

---

## 20. Real Unity validation ladder

After sandbox license visibility is proven, progressively run:

1. Unity batchmode startup;
2. isolated test project under `E:\UnityPRJS\ShaderGPT_neoversion\Temp`;
3. C# compile;
4. UdonSharp compile;
5. shader import/compile;
6. production texture import verification;
7. EditMode/unit tests;
8. Generator execution;
9. generated asset/scene inspection;
10. second Generator run for idempotence;
11. Production Validator;
12. PlayMode smoke test;
13. ClientSim when available;
14. Human-vs-AI / AI-vs-AI end-to-end smoke test;
15. log inspection for compiler/shader/Udon/serialization/missing-script errors.

Unity exit code 0 alone is not enough; inspect the log and generated output.

---

## 21. Self-validation of AI correctness, speed and strength

The finished system should not rely on an agent saying "it looks strong".

### A. Numerical correctness

For the same position/history compare bundled KataGo/reference outputs against Pure Udon Go:

```text
features
layer checkpoints
policy logits/probabilities
pass output
value
score outputs
ownership
```

Report first divergence plus error metrics.

### B. Real GPU performance

In licensed local Unity/D3D11 measure actual:

```text
NN inference P50/P95/P99
blits per inference
async readback latency
NN evals/s
MCTS visits/s
move P50/P95/P99
frame impact
RenderTexture/weight memory footprint
```

Test multiple board phases and search budgets.

Unity Editor performance is useful evidence but is not automatically equal to VRChat client performance.

### C. Search-strength regression

Use fixed positions to compare:

```text
top-1/top-k move agreement
root value
score lead
policy distribution
ownership
root/child visits
chosen move
```

Keep regression positions for opening, fights, ladder/ko, seki, races, endgame, pass decisions and randomized legal positions.

### D. Self-play difficulty monotonicity

Run automated matches such as:

```text
UltraHard vs Master
Master vs Advanced
Advanced vs Beginner
```

Swap colors and use enough games to avoid drawing conclusions from tiny samples.

The stronger preset should demonstrate a statistically meaningful advantage over weaker presets. Do not hard-code a required win rate before measurement.

### E. Same-model reference matches

Where feasible, compile/use the bundled KataGo oracle from this repository and compare Pure Udon Go against it using the same b10c128 model and matched visit/NN-query budgets.

This is powerful because the model is controlled and differences mainly expose feature/search/runtime discrepancies.

Record:

```text
games
wins/losses
color split
average score/game length
illegal moves
NN evals
visits
search failures
```

### F. Elo/rank claims

Internal relative Elo differences may be estimated from large match sets, but do not claim absolute human Elo/kyu/dan/pro rank without an external calibrated benchmark.

### G. Runtime truth hierarchy

Keep these distinct:

```text
static cost calculation
Unity Editor benchmark
ClientSim benchmark
actual VRChat PC client benchmark
Quest benchmark
```

Never promote one layer to another without running it.

---

## 22. Evidence labels

Use precise labels:

`IMPLEMENTED` — code exists only.

`STATICALLY VERIFIED` — source/assets inspected.

`LOCAL / SYNTHETIC` — only generated/synthetic tests passed.

`VERIFIED` — exact claim directly executed/observed.

`EQUIVALENCE VERIFIED` — differential/numerical oracle comparison passed.

`NOT RUNTIME VERIFIED` — relevant runtime was not executed.

`UNITY LICENSE SANDBOX BLOCKED` — host is activated but sandboxed Unity still cannot consume the copied/exposed license.

`TARGET-REPOSITORY CI BLOCKED` — target CI did not execute meaningful steps.

Do not invent benchmark numbers, error values, VRAM, latency, Elo, Unity PASS, VRChat PASS or Quest PASS.

---

## 23. High-value invariants

### Go

- every point is EMPTY/BLACK/WHITE;
- rejected move leaves board/hash/captures/ko unchanged;
- history stays in capacity;
- reset invalidates prior search revision;
- double pass ends once;
- final selected move is legal.

### Model

- parser consumes exact EOF;
- tensor byte count matches shape;
- raw tensors contain no non-finite values;
- lossless baked recovery matches raw bytes;
- generated offsets stay in texture bounds.

### Shader

- deterministic position gives deterministic output when noise is disabled;
- all accesses stay in bounds;
- no NaN/Inf activation;
- first divergence is localizable by named layer;
- policy includes legal board logits plus pass;
- ownership dimensions match 19x19.

### Search/network

- visits only grow during a valid current search;
- backup reaches root;
- stale revision cannot commit;
- old owner cannot commit;
- readback failure cannot leave permanent infinite-thinking state.

### Generator

- second run produces no duplicate owned roots/materials/controllers;
- serialized references resolve;
- final scene saves;
- Validator agrees with actual scene state.

---

## 24. Recommended milestone order

Always re-check current HEAD, then generally follow dependencies:

```text
A reconcile current code/docs + local test baseline
B real production model parse + raw-to-baked verification
C committed model assets + Unity import verification
D finish/verify 19x19 Go rules
E KataGo feature encoder + differential tests
F NN cost model + shader-equivalent CPU reference
G shader trunk/residual implementation layer-by-layer
H policy/value/score/ownership + full NN equivalence
I frame-budgeted PUCT/MCTS + regression/self-play
J owner-authoritative networking/revision safety
K board interaction/view/UI/telemetry/audio
L one-click idempotent GoWorldGenerator
M GoProductionValidator
N PlayMode/ClientSim/end-to-end validation
O performance/strength tuning + final reports
```

Do not skip dependencies because a later visible feature is more exciting.

---

## 25. Final Definition of Done

A supported local Unity/VRChat environment, with the already-legitimate host Unity license successfully made visible to the sandbox, must be able to use the committed `udon-go-ai` checkout and execute:

```text
Tools > Pure Udon Go > Generate Final Production Scene
```

without manual Pure Udon Go wiring.

The generated result must be a polished complete 19x19 Go world with:

- correct rules/history;
- committed verified KataGo-derived weights;
- correct KataGo NN features;
- numerically verified shader NN;
- genuine strong-mode PUCT/MCTS;
- useful difficulty presets;
- PvP/PvAI/AIvP/AIvAI;
- owner/revision-safe networking;
- same-family presentation informed by the read-only Udon Xiangqi reference;
- one-click idempotent Generator;
- Production Validator;
- automated numerical/performance/strength evidence;
- strongest locally available Unity/ClientSim/VRChat evidence without overclaiming.
