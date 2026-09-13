# PureUdonGo post-refactor audit

Audit purpose: record the current implementation after the large UI/settings/
hover/GPU/search refactor, distinguish implemented code from executable proof,
and identify the next production blockers.

Overall release status: `NOT READY`.

This is a post-refactor audit, not a chronological progress log. Historical run
records remain evidence-only in `PROGRESS.md` and `Archive/**`.

## 1. What the refactor genuinely achieved

The following are substantial implementation improvements and should not be
casually reverted:

- AI configuration is separated from board-position identity;
- local UI Draft -> explicit settings Apply transaction;
- Black and White have independent Follow Shared / Custom visit choices;
- configuration Apply/deserialization no longer intentionally cancels a valid
  current SearchSession;
- current search captures its settings at `BeginSearch`;
- stale position/owner results remain guarded by token/revision/hash/lifecycle;
- board hover presentation no longer performs a 361-stone refresh;
- one preview stone is pooled instead of created/destroyed per pointer event;
- exact move-mask legality is warmed cooperatively;
- production board input is a single world-space UGUI receiver with one
  non-blocking `Is Trigger` UIShape collider; board cells, stones and table
  pedestal contain no solid physics colliders while the control/status deck
  keeps one explicit solid panel collider;
- NN graph execution is cooperative across frames;
- final neural outputs are packed into one async readback request;
- cancelled reader identities are quarantined pending late callback;
- superko/history has an exact stone-count frequency reject prefilter;
- MCTS no longer copies the complete 2049-entry history arrays every visit;
- heavy search/ladder/GPU resources are increasingly lazy/persistent;
- synchronized history payload is substantially reduced by local derived-history
  rebuild;
- three tables are player-visible while hidden backing slots remain inactive.

These facts explain why the codebase is materially healthier than the original
prototype. They do not establish production readiness.

## 2. Deterministic Custom profile correction

### Resolved architecture

`GoAiSettings` synchronizes:

```text
sharedPreset
blackUseCustom
blackCustomVisits
whiteUseCustom
whiteCustomVisits
configRevision
```

`GoDifficultyProfile.ApplyPresetConstantsLocal` now writes the complete shared
preset on every resolution. Custom then overrides only:

```text
maxVisits
maxNNQueries
```

`GoAiSettings` always uses `sharedPreset` as the base for Black and White.
Generated profiles no longer expose the old local `ApplyPreset`/`SetCustom`
mutation surface.

### Implemented rule

Custom is custom **visits only**:

```text
base = complete preset(sharedPreset)
Follow Shared = base
Custom = base with maxVisits/maxNNQueries overridden by sideCustomVisits
```

If future product UI exposes other custom knobs, synchronize those knobs
explicitly. Never use hidden client-local residue as product configuration.

### Executable proof

`GoClientSimSettingsLifecycleVerifier` poisons the local resolved knobs, invokes
the backing Udon `OnDeserialization` path, and checks all seven effective fields.
It also verifies current-search preservation and both local-Apply and remote-
snapshot next-search capture. Current run status is recorded in `VERIFICATION.md`.

## 3. Settings lifecycle implementation vs proof

Static architecture now supports the desired behavior:

```text
old active SearchSession
 + new config Apply
 => old session retains captured profile
 => next BeginSearch resolves new settings
```

The lifecycle verifier now continues through the next real AIvAI search, checks
captured visits/cpuct/temperature/top-K/transitions/resign, repeats the update
through backing-variable deserialization, and waits for the following real
search to capture the received complete profile.

## 4. Board hover audit

### Structurally fixed

The old high-risk path:

```text
hover -> SetHoverLocation -> RefreshNow -> full board scan
```

has been replaced by pointer-only presentation updates. The pooled preview stone
is reused. Move-mask warm-up reduces repeated exact legality work on pointer
movement.

### Still unproven

The generator no longer emits 361 `GoBoardCell` collider/Udon `Interact()`
endpoints.  Board interaction is routed through the one UGUI receiver; the
production validator rejects any collider or legacy cell under a board.

The single UGUI board grid and the removed full-board hover refresh are the
intended low-overhead input path; exact user-visible laser/Use behavior still
belongs to a real VRChat manual check.

### Room-wide bound

Generated views delegate warm-up to `GoBoardPool`. The pool advances at most one
table and 24 points per frame in round-robin order. `GoClientSimHoverVerifier`
checks the 361-point sweep, zero full refreshes, stable preview identity and the
three-table room quota.

## 5. GPU/readback audit

### Implemented

The output path now packs required heads into one final target and performs one
AsyncGPUReadback per evaluation. Do not restore the old staged output round
trips.

The NN graph itself is a cross-frame state machine with bounded submit work.

### Recovery fixture

The prior full-world recovery fixture hit serialization OOM. The current
verifiers generate and open a dedicated saved fixture scene containing only one
Gameplay/NN graph, three readers and required assets; there is no BoardPool,
board cell, Canvas, UI or second table. Assertions remain:

```text
WAITING -> QUARANTINED -> late callback -> RECOVERED -> reuse
```

Current execution status is recorded in `VERIFICATION.md`.

## 6. Performance audit

The fixed `GoPerformanceCorpusProbe` now contains A-H at diagnostic 4/32
profiles (independent from the public preset values),
including deterministic 128/256/520-ply generated fixtures using the existing
long-history seeds. It reports rule, feature/ladder, MCTS, upload, GPU, readback,
UI and frame-tail telemetry.

### Current likely residual costs

- CPU feature array -> Color[] -> `SetPixels/Apply` -> Blit upload total work;
- positive-match stone-count bucket occupancy;
- dense-position legal/group rediscovery;
- exact ladder work;
- remaining score-utility quadrature;
- per-visit board/previous-board/root scratch copies;
- multi-table scheduler telemetry/fairness and aggregate background work.

These are hypotheses until stage data ranks them.

### Evidence boundary

The code and telemetry schema exist. Exact current measurements and any missing
historical before sample are stated explicitly in `PERFORMANCE.md` and
`VERIFICATION.md`; ClientSim data is not presented as clean VRChat-client timing.

## 7. Multi-table scheduler audit

The scheduler now reads each active runtime's live
`smoothedGpuSubmitMilliseconds` and maintains a pool-local EMA fallback. The
1/2/3-table verifier records slots, stride, dispatch capacity, live submit EMA,
per-table wall time/completed visits and pool frame percentiles.

## 8. Documentation audit

The previous documentation reset copied several historical files into Archive
but also left duplicate old files in the active Docs root. This created two
problems:

1. active Docs was not actually reduced to a small authoritative set;
2. at least one old knowledge document still stated that difficulty/profile
   changes invalidate active searches, directly contradicting the corrected
   architecture.

The active documentation set is now defined by `Docs/README.md`. Historical
knowledge/design/checklist files with preserved Archive copies should not remain
as active root contracts.

`PROGRESS.md` remains at root only as a large dated evidence ledger. It is not a
source of current architecture semantics.

## 9. Current verification interpretation

Historical gates exist for rules, V7 features, neural equivalence, cooperative
superko, GPU graph behavior, earlier UI/difficulty paths and other work. Because
current settings/hover/generated-scene code changed after some of those runs,
use `PASS_HISTORICAL` until the current relevant gate is rerun.

Particularly important OPEN/NOT_EXECUTED gates:

- regenerated current scene;
- current UdonSharp/ClientSim schema;
- deterministic Custom full profile;
- next-search settings consumption;
- remote settings change during search;
- reader recovery;
- current Undo/Draw corruption contract;
- hover sweep/current VRChat Use behavior;
- A-H 4/32 performance corpus;
- 1/2/3-table p95/p99/max;
- current search calibration;
- complete-game lifecycle;
- real two-client late join/owner transfer/reconnect;
- clean PCVR;
- Quest;
- model redistribution authorization.

See `VERIFICATION.md` for the single release matrix.

## 10. Current execution order

1. regenerate schema 14 and run validator/Udon compile;
2. run settings/custom/modes and rule/history/feature gates;
3. run minimal reader recovery and lifecycle/permission/UI gates;
4. run A-H 4/32, long-history/search calibration and 1/2/3-table profiles;
5. apply Phase 10 thresholds only from those measurements;
6. record unavailable real transport/PCVR/Quest work as `FUTURE_MANUAL`.

## 11. Search-consistency audit checkpoint (2026-09-10)

The professional-endgame benchmark now separates the temperature-sampled
product move from the deterministic root ranking. A desktop Udon Search
Reference replays a real ClientSim expansion tape and matches root visits,
edge values and deterministic ranking exactly. `GoMctsSearch` contains four
opt-in diagnostic semantic bits (FPU, dynamic cpuct, recent score center and
rule-aware no-result); the shipped mode remains zero until a same-budget
ablation justifies a product change.

The 100-visit position that previously looked like a 1200-second liveness
failure was traced to a verifier that ignored legitimate AI resignation. The
probe now records that terminal action and the exact searched best move. One
position has passed the corrected 100-visit gate; the complete corpus and fresh
external V7/NN oracle remain open rather than being inferred from historical
artifacts.
