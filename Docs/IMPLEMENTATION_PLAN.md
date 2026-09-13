# PureUdonGo implementation plan

This is the concrete code-edit sequence for the current repository. It is not a
research backlog. Follow the phases in order and keep runtime, generator,
validator and ClientSim verifiers synchronized.

Current executable environment: one Unity Editor + one ClientSim VM. Simulated
remote players/ownership/deserialization are allowed. Real multi-client VRChat is
future manual validation and is not part of this plan's completion gate.

## Implementation checkpoint — schema 14 / search audit

Phases 1–9 have substantial Runtime/Generator/verifier implementations. Their
historical and current evidence is recorded in `VERIFICATION.md`; it must not
be promoted to a current PASS when the generated scene or public budgets have
changed. The 2026-09-10 search audit adds a desktop reference tape and isolated
semantic-ablation bits without changing the shipped legacy mode. Phase 10
input-upload/deeper-MCTS work remains measurement-gated, and full KataGo
search-distribution parity remains an explicit open gate.

## Phase 0 — establish the current baseline

Before changing production code:

1. generate the production scene with `GoWorldGenerator`;
2. run `GoProductionValidator`;
3. run `GoUdonSharpCompileVerifier`;
4. run the current 4-visit runtime smoke;
5. save the relevant stage/performance logs as the before baseline;
6. do not modify old benchmark evidence.

If current scene generation fails because the docs-only refactor removed no
runtime asset, fix the generator/schema problem first.

---

# Phase 1 — deterministic settings and lifecycle

## Files

- `Assets/PureUdonGo/Runtime/GoDifficultyProfile.cs`
- `Assets/PureUdonGo/Runtime/GoAiSettings.cs`
- `Assets/PureUdonGo/Runtime/GoUI.cs`
- `Assets/PureUdonGo/Editor/GoClientSimSettingsLifecycleVerifier.cs`
- `Assets/PureUdonGo/Editor/GoClientSimCustomVisitsVerifier.cs`
- difficulty/mode probes that still call profile mutation directly

## 1.1 Centralize complete preset constants

Add one local resolver in `GoDifficultyProfile` that assigns every effective
search field from a public preset. The full table is:

| preset | visits | NN | transitions/frame | cpuct | temp | topK | resign |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Beginner | 8 | 8 | 1 | 1.85 | 0.10 | 24 | -1 |
| Advanced | 20 | 20 | 2 | 1.60 | 0.10 | 64 | -1 |
| Master | 48 | 48 | 4 | 1.35 | 0.10 | 128 | -0.98 |
| Ultrahard | 120 | 120 | 8 | 1.25 | 0.10 | 362 | -0.95 |

`ApplyResolvedLocal` must first apply this complete base preset and only then, if
Custom is enabled, override `maxVisits/maxNNQueries`.

## 1.2 Use shared preset as the Custom base

In `GoAiSettings.ApplyResolvedProfiles`, always pass `sharedPreset` as the base
preset. `GetEffectivePreset(side)==CUSTOM` is presentation information and must
not be used as the base resolver.

Exact effective semantics:

```text
Black Follow: Parameters(sharedPreset)
Black Custom: Parameters(sharedPreset) with visits=blackCustomVisits
White Follow: Parameters(sharedPreset)
White Custom: Parameters(sharedPreset) with visits=whiteCustomVisits
```

## 1.3 Remove local custom-knob residue

A generated profile bound to `GoAiSettings` must never accept local-only
exploration/temperature/topK changes. Migrate/remove mirror-mode `SetCustom`
behavior that writes those fields after a synchronized Apply.

## 1.4 Extend lifecycle verifier

Use the two real Black/White numeric input fields only.

Required sequence:

1. Advanced shared, Black Custom 64, White Follow;
2. assert full Black profile = Advanced base with visits64;
3. switch shared to Master; Black remains visits64 but inherits Master
   transitions/cpuct/temp/topK/resign; White becomes full Master;
4. both custom with different visits;
5. poison local profile non-visit fields, simulate an incoming settings snapshot,
   invoke the real deserialization path and assert deterministic restoration;
6. start a Beginner/8 search;
7. while it is active, Apply Advanced/20;
8. assert old token and target remain 8;
9. wait for old search to complete/commit;
10. in AIvAI let the next real AI search begin;
11. assert new token and target20 plus full Advanced profile;
12. repeat the config change through simulated `OnDeserialization` instead of a
    local Apply.

Use backing Udon variables/events so the serialized Udon path is tested.

---

# Phase 2 — delete selected-side settings debt

## Runtime GoUI

After verifier migration, retain only:

```text
sharedPresetDraft
blackUseCustomDraft
blackCustomVisitsDraft
whiteUseCustomDraft
whiteCustomVisitsDraft
settingsDraftDirty
draftSourceSettingsRevision
blackCustomVisitsInput
whiteCustomVisitsInput
blackCustomVisitsValueText
whiteCustomVisitsValueText
```

Delete the hidden/superseded state:

```text
customVisitsSlider
customVisitsValueText
customVisitsDraft
draftSettingsDisplaySide
blackPresetDraft
whitePresetDraft
blackDraftDirty
whiteDraftDirty
selected-side draft getters/setters
single-slider fallback
```

Preset buttons set `sharedPresetDraft`. They do not select a side and do not
clear Custom flags.

## Generator

Delete generated hidden migration controls after tests are migrated:

- `Legacy Custom Visits Slider`;
- redundant hidden selected-side/controller endpoints;
- table append/remove endpoints/actions 46/47.

Regenerate the current scene in the same phase.

## BoardPool

After actions 46/47 are absent from the regenerated scene/tests, remove obsolete
append/remove compatibility methods instead of leaving permanent no-ops.

## Validator

Replace legacy-positive checks with legacy-negative checks:

- require `GoAiSettings`;
- require Black and White real numeric inputs;
- require direct `Ultrahard` text;
- require current visible mode/settings actions;
- reject old single-slider/selected-side fields and methods;
- reject actions 46/47 and hidden table controls;
- validate required action IDs individually instead of exact total button count.

---

# Phase 3 — authoritative group/liberty hot path

Port the exact scratch strategy already used by `GoSearchState` into `GoGame`.

Replace full `bool[361]` clearing for group/liberty probes with:

```text
int[] groupVisitedStamp
int[] libertyVisitedStamp
int groupGeneration
int collectedGroupLibertyCount
```

`CollectGroup` increments a generation, marks group points, counts each unique
liberty once, and keeps the existing group-members array. `CountLiberties`
returns `collectedGroupLibertyCount` directly.

On generation wrap, clear the two stamp arrays once and restart at 1.

Do not change neighbor order or capture/suicide/ko behavior.

Immediately run:

- Rules
- Legal Matrix
- Rules Differential
- Derived History Rebuild
- Undo/Draw
- Superko

before continuing.

---

# Phase 4 — one room-wide move-mask warm-up budget

## GoGame

Expose a read-only query for whether the current-revision move-mask cache is
complete. Do not expose private arrays.

## GoBoardView

Add a generated-table flag `moveMaskWarmupManagedByPool`. In generated worlds it
is true, so `GoBoardView.Update` does not independently call the room warm-up.
Standalone fixtures may leave it false and use the same
`StepMoveMaskCacheTimeSliced` entry point.

## GoBoardPool

Add round-robin cursor `nextMoveMaskWarmTable`.

On each Update:

1. inspect visible tables beginning at the cursor;
2. find the first incomplete current-position cache;
3. call `StepMoveMaskCacheTimeSliced` on that one game only;
4. advance cursor;
5. perform no second table warm-up that frame.

This makes the room-wide legality warm-up budget explicit: one table per frame,
with a 24-point safety cap and a small time slice (0.25–0.50 ms, 0.35 ms by
default). If any visible table has critical AI/search work, the room warm-up
sleeps and retains its pending bit.

## Hover verifier

Create/extend a ClientSim sweep that proves:

- cooperative cache reaches 361 exact points;
- hover over all 361 cells does not increment full board refresh count;
- preview object identity remains constant;
- cached/fallback legality equals authoritative legality;
- with all three table caches dirty, one pool frame advances no more than the
  room quota.

The current production topology intentionally removes the 361 physical
`GoBoardCell` endpoints. Keep one world-space UGUI receiver with 361 button
targets and one explicit `Is Trigger` BoxCollider required by `VRCUiShape`;
reject every other board/stone/pedestal collider. The control/status deck
retains its explicit solid panel collider. `GoBoardCell` remains only for
legacy fixtures.

---

# Phase 5 — live BoardPool scheduler cost

Replace the active scheduling cost source with
`GoGpuNeuralRuntime.smoothedGpuSubmitMilliseconds` from currently active
controllers.

If no active runtime has a valid live sample, use a pool-local EMA from the most
recent valid runtime sample. Do not use `lastSearchCompletedVisits` as the main
active-search cost source.

Keep the existing slot/stride fairness algorithm and frame-budget clamps.

Extend concurrent ClientSim profiling to run 1, 2 and 3 tables and emit:

```text
running tables
dispatches/frame
slot + stride per table
live smoothed GPU submit ms
per-table completion time
frame p50/p95/p99/max
```

---

# Phase 6 — exact stone-count history buckets

Implement first in `GoSearchState`.

Add:

```text
historyBucketHead[362]
historyBucketNext[MAX_POSITION_HISTORY]
historyEntriesExamined
```

Use index+1 sentinel heads. Build buckets from the root history once. Push each
simulation-appended history entry to its count bucket. On simulation reset, pop
only appended entries in reverse order until the root history count is restored.

`HashSeen` walks only the candidate count bucket and still compares all four hash
lanes exactly.

Keep existing stone-count frequency counters if needed by current probes.

Update Superko/LongHistory tests to compare exact mask parity and report history
entries examined. If authoritative `GoGame` remains a measured long-history hot
path after its group-stamp refactor, apply the same bucket layout there in a
separate verified change.

---

# Phase 7 — ladder mutation journal

In `GoFeatureEncoder`, keep the working ladder board initialized once per source
board plane. Replace per-attempt full restore with a fixed delta journal:

```text
ladderChangedStamp[361]
ladderChangedLocations[361]
ladderChangedOriginal[361]
ladderChangeGeneration
ladderChangedCount
```

All ladder attempt mutations go through `SetLadderPoint(loc,value)`.
First write in an attempt records original value; subsequent writes to the same
point do not duplicate the journal entry. Attempt completion restores only the
journaled points.

The only direct full working-board copy is initialization when switching source
plane/current-vs-history board, not every ladder candidate.

Before deleting the old restore logic, grep/audit the entire encoder and ensure
there is no direct ladder-board mutation outside initialization/helper.

Run the full 16-case ClientSim feature export and KataGo V7 comparison after this
phase. One mismatch rejects the change.

---

# Phase 8 — reader recovery minimal fixture

Keep `GoGpuNeuralOutputReader` one packed readback stage. Correct stale comments.

Do not change the A/B/C quarantine state machine.

Create a minimal test scene/fixture with:

- one GoGame/table state sufficient to launch one AI evaluation;
- one controller/search/encoder/runtime;
- reader A/B/C;
- required shader/model assets;
- no 16-table generated room/UI payload.

Run the existing recovery sequence from `GoClientSimReadbackRecoveryVerifier`.
The real request must reach WAITING before cancellation. If ClientSim suppresses
the late callback, use `ObserveDiscardedCallbackForVerifier` only after
QUARANTINED exists.

Required final state: recovered reader channels are actually selected again and
a fresh search commits without stale-result acceptance.

---

# Phase 9 — fixed performance corpus

Create `GoPerformanceCorpusProbe` and `GoClientSimPerformanceCorpusVerifier`.
Reuse existing deterministic fixture sequences:

- empty board: reset state;
- opening/tactical: copy current `GoSearchCalibrationProbe` fixture sequences;
- dense/long: copy deterministic prefixes from `GoLongHistoryStressProbe`;
- ko/superko: copy current cooperative superko history fixture;
- ladder-heavy: copy `GoFeatureProbe` ladder fixture;
- late/endgame-like: use the tested longer dense deterministic prefix.

Eight classes A-H, each at fixed diagnostic budgets of 4 and 32 visits.

For every sample reset stage counters and output:

```text
fixture
visits
wall seconds
frames
ms/visit
superko/legal ms + history entries examined
feature ms + ladder transitions/nodes
MCTS selection/expansion/backup/root-copy/score ms
input upload packing/Apply/Blit ms
GPU passes + graph frames + max/smoothed submit
readback count + latency
UI refresh ms
frame p50/p95/p99/max
```

The same fixture representation must be used before and after changes.

---

# Phase 10 — input upload and later MCTS thresholds

Split input-upload telemetry into packing, `SetPixels/Apply`, and Blit timing.

If input upload is at least 10% of 4-visit wall time or top-three stage cost,
remove duplicated CPU writes by making the feature finalization path populate the
persistent upload representation directly. Do not add another conversion layer.
If below threshold, leave the path unchanged.

After all previous phases, if `ResetSimulationToRoot` remains at least 10% of
visit CPU time, then implement a reversible simulation/root reset. Otherwise do
not rewrite it.

Only optimize linear PUCT child scanning if it is one of the top three remaining
CPU costs. Keep the exact PUCT formula and root move selection.

---

# Required single-Unity gate sequence

The final regression run should include, in practical dependency order:

1. generator;
2. production validator;
3. UdonSharp compile;
4. rules + matrix + differential;
5. derived history;
6. cooperative superko;
7. feature export + KataGo V7 compare;
8. neural/GPU numerical compare;
9. GPU graph slicing;
10. settings lifecycle + custom visits;
11. modes;
12. stale result;
13. ClientSim simulated ownership;
14. ClientSim remote-player recovery;
15. permission guard;
16. undo/draw;
17. UI/hint;
18. BoardPool;
19. readback/reader pool minimal fixture;
20. long-history stress;
21. search calibration;
22. complete-game smoke;
23. A-H 4/32 performance corpus;
24. 1/2/3-table concurrent performance.

No real second client is required by this list.

## 2026-09-09 scheduler checkpoint

The runtime now has the first threshold-driven scheduling slice from the plan:

- GoBoardPool suppresses authoritative move-mask warm-up on AI/hint-critical
  turns and retains the pending revision bit;
- GoAiController uses one admitted-frame deadline and a cross-phase pump;
- ownership telemetry consumes only remaining/readback-wait budget;
- GoGpuNeuralRuntime no longer applies an independent 18% frame fraction or
  24-pass governing cap, and persistent graph-submit EMA excludes input setup;
- concurrent dispatch budget is divided by admitted dispatch count.

The final current single-ClientSim run now also has fresh evidence for the
fixed A-H 4/32 corpus, all six long-history cases, settings lifecycle/custom
visits, rules/legal matrix/differential, V7 features, neural/GPU equivalence,
reader A/B/C recovery, ownership/network recovery, UI/Undo/Draw/permissions,
complete-game smoke, and concurrent 1/2/3-table scheduling. ClientSim frame
tails are reported as ClientSim-only measurements; they are not a clean
standalone VRChat performance claim.

Current executable status: `READY_SINGLE_CLIENTSIM` for the recorded checkpoint;
the subsequent deadline-hardening source patch is `PENDING_REVERIFY` until the
generated Udon programs are rebuilt. External KataGo numeric oracle/strength
calibration, real two-client transport, PC/PCVR timing and Quest/Android remain
`FUTURE_MANUAL`.

# Current completion label

Use `READY_SINGLE_CLIENTSIM` only after all required current-environment gates
pass. In the final report put real multi-client/PCVR/Quest under a separate
`FUTURE_MANUAL` section and do not use them to downgrade a successful current
single-ClientSim implementation.
