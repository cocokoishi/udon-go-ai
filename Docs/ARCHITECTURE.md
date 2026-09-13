# PureUdonGo current architecture

This file describes schema 14. The implementation history and gate order are in
`IMPLEMENTATION_PLAN.md`.

## 1. Runtime ownership

```text
GoUI local drafts / GoBoardInput board input
                 |
                 v
          explicit commands
                 |
       +---------+----------+
       |                    |
       v                    v
    GoGame             GoAiSettings
position/match         AI user choices
       |                    |
       +---------+----------+
                 v
          GoAiController
                 |
          immutable session
                 |
        +--------+--------+
        |        |        |
        v        v        v
      MCTS    Features    GPU
                         |
                         v
                  packed readback
```

Presentation (`GoBoardView`, `GoUI`, `GoTelemetry`) never defines authority.

`GoGame` also owns the synchronized `turnStartedServerSecond` recovery marker.
It is refreshed only when an authoritative turn changes and is used solely for
the 50-second stalled-turn Force Reset permission; it never changes move or AI
search semantics.

## 2. `GoGame`

Owns synchronized board/match/rule state, seats, controller mode, starter,
offers, hints, final score and position/lifecycle revision.

It also owns exact authoritative legality. Its group scratch uses the same
generation-stamp/liberty-count model as `GoSearchState`; the hot legality path
does not clear two `bool[361]` arrays per group probe.

Expose only a small query for current move-mask warm-up completion so
`GoBoardPool` can schedule it. The pool must not reach into private cache arrays.

`settingsRevision` remains a local mirror/diagnostic; it is not position
identity and does not cancel a valid search.

## 3. `GoAiSettings`

One synchronized settings object per table. User-choice state is exactly:

```text
sharedPreset
blackUseCustom
blackCustomVisits
whiteUseCustom
whiteCustomVisits
configRevision
```

It performs one Apply transaction and resolves both local profiles.

`ApplyResolvedProfiles` always uses synchronized `sharedPreset` as the complete
base preset for both sides. A Custom side overrides only visits/query count.

## 4. `GoDifficultyProfile`

Local resolved view only. It must not contain effective values that depend on
whatever happened locally before the last `GoAiSettings` resolution.

Every resolution begins by assigning all preset constants. Custom then replaces
only:

```text
maxVisits
maxNNQueries
```

Generated profiles are always settings mirrors. Unsynchronized custom cpuct,
temperature or top-K mutation is forbidden in the generated path.

## 5. `GoUI`

Target settings state is five draft choices plus dirty/source revision:

```text
sharedPresetDraft
blackUseCustomDraft
blackCustomVisitsDraft
whiteUseCustomDraft
whiteCustomVisitsDraft
settingsDraftDirty
draftSourceSettingsRevision
```

There is no selected side because Black and White cards are visible
simultaneously. Remove hidden single-slider and selected-side compatibility
state after migrating verifiers.

## 6. `GoBoardView` and board input

`GoBoardView` is delta presentation. Hover updates marker/material/one pooled
preview only. Full 361-stone reconciliation occurs on real board/lifecycle
refresh.

The production board uses one world-space UGUI `GoBoardInput` receiver with
361 transparent button targets and exactly one explicit `BoxCollider` on that
Canvas. The collider is `Is Trigger`, so it defines the VRChat UIShape hit
surface without creating a solid board obstacle. Stones, grid and table
pedestal have no solid colliders. The control/status deck retains one explicit
non-trigger `BoxCollider` for panel interaction. `GoBoardCell` remains
available only as a legacy/test type and is rejected by the production input
validator.

Generated board views mark move-mask warm-up as pool-managed.

## 7. `GoBoardPool`

Responsibilities:

- exactly three visible tables;
- hidden backing roots inactive;
- lazy resource accounting;
- fair AI scheduler;
- one room-wide move-mask warm-up budget;
- frame-tail sampling.

Move-mask warm-up is round-robin across visible tables with at most one
`StepMoveMaskCacheTimeSliced` slice per pool frame. The exact cache still has a
24-point safety cap, but a 0.25–0.50 ms deadline is the pacing authority and is
suppressed whenever any visible table has critical AI/search work.

GPU scheduling uses live/recent
`GoGpuNeuralRuntime.smoothedGpuSubmitMilliseconds`, not completed-search visit
statistics as the active cost estimate.

After current scene/verifier migration, obsolete append/remove table methods and
hidden action endpoints are removed.

## 8. `GoAiController`

Keep it as the orchestration state machine:

```text
ShouldSearch
BeginSearch
superko preparation
feature encoding
GPU graph
packed readback
MCTS expansion/backup
analysis publication
move commit
```

The session captures complete effective difficulty at start. Configuration
changes do not invalidate it; position/owner lifecycle changes do.

Reader A/B/C identity and quarantine remain controller-managed.

## 9. `GoSearchState`

Keep existing generation-stamp group/liberty implementation.

Exact stone-count history buckets make the positive stone-count superko path
walk only history entries with compatible count, still comparing all four hash
lanes.

Simulation-appended bucket entries are pushed and popped with the simulation
history; no full bucket copy per visit.

## 10. `GoMctsSearch`

Keep fixed-capacity PUCT and current lazy node/edge allocation.

Do not perform another major MCTS rewrite before the fixed performance corpus.
The catastrophic full history copy is already gone. Remaining root board copies
or child scans are only changed if the final measured thresholds in
`IMPLEMENTATION_PLAN.md` are met.

## 11. `GoFeatureEncoder`

Keep cooperative V7 encoding and exact ladder semantics.

Per-ladder-attempt restoration uses one working board per source plane plus a
fixed mutation journal. Every attempt mutation goes through one helper, and only
touched positions are restored.

The full V7 oracle differential is mandatory after this change.

## 12. GPU runtime / reader

Keep:

```text
persistent upload resources
cross-frame graph state machine
one packed final output
one AsyncGPUReadback per evaluation
A/B/C reader quarantine
```

When an AI match starts, the owning table performs a separate persistent
pipeline warm-up before `BeginSearch`: zero-valued dummy input traverses the
production fused graph and one packed async readback is completed. Its negative
token is never visible to MCTS and its output is discarded. This initializes
the shader/upload/readback backend once at match start; subsequent visits and
turns reuse the same resources. Ownership loss, explicit heavy-resource
release, or a graphics-context reset cancels the warm-up and requires the new
owner to repeat it.

Add finer input-upload timing before deciding whether further upload changes are
needed.

Reader recovery is tested in a minimal one-table fixture to prevent production
16-table serialization OOM. The production scene still gets normal binding
validation separately.

## 13. Generator/validator are part of architecture

`GoWorldGenerator` is not a demo helper; it defines the serialized production
schema. Every runtime field/control removal must be reflected there in the same
phase.

`GoProductionValidator` must validate the **target** architecture, not preserve
legacy fields because old verifiers referenced them. After migration it should
reject:

- legacy single custom slider;
- selected-side settings code;
- obsolete hidden controller duplicates;
- append/remove-table actions 46/47;
- legacy difficulty display strings.

## 14. Verification boundary

Current architecture completion is proven in one Unity/ClientSim VM using:

- generated serialized Udon programs;
- simulated remote players/ownership;
- source/replica generated tables;
- simulated incoming deserialization through the actual Udon path;
- real pending GPU readbacks plus the existing late-callback verifier hook;
- 1/2/3 simultaneous tables.

Real multiple VRChat processes, PCVR and Quest are future manual validation, not
architecture blockers for `READY_SINGLE_CLIENTSIM`.

## 15. Admitted-frame scheduling

`GoBoardPool` is the room-level admission authority. A controller that receives
the slot gets one deadline for that rendered frame; `PumpSearchUntilDeadline`
consumes the same deadline across CPU-only search phases. Helpers receive the
deadline/remaining budget and must not mint another full slice. The only normal
cross-frame boundary is an outstanding async readback (plus stale/error,
scheduler denial or deadline exhaustion).

The pool also suppresses authoritative `GoGame` move-mask warm-up while a
table's AI turn or hint is critical. SearchState remains the exact lazy source
for MCTS legality; GoGame remains exact for a human click/hover.

GPU runtime input upload and graph dispatch share the admitted remainder. The
runtime no longer applies an independent frame-fraction throttle. Persistent
graph-submit EMA is separate from input CPU setup and is the scheduler's live
calibration source.

During a live search, internal phase transitions remain local state. `GoUI` and
`GoTelemetry` refresh at the bounded presentation cadence (or immediately for
game/revision/error/ownership events) rather than rebuilding TMP text for every
PUCT transition.
