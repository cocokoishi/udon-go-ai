# Current verification matrix

Overall status: `TARGETED_SCORE_SEMANTICS_STATIC_AUDIT`. Existing generated-scene,
UdonSharp compile, Production Validator, fixed eleven-line UI/Hint gate, 4/32
search performance gates and single-ClientSim network recovery evidence remain
historical; the current effective-komi/root-readiness patch has not been run in
Unity because the local LicensingClient IPC failed before project compilation.
The full historical search/performance matrix is not rerun by this targeted
change and remains reported below without being promoted to a new PASS.
External transport and hardware checks remain explicitly non-blocking
`FUTURE_MANUAL` items.

Current executable evidence before the latest preset change (2026-09-11):
generation produced three visible tables; compile produced 97/97 serialized
programs; the validator reported `failures=0`; UI/Hint verified English and
Chinese fixed eleven-line output, final `4/4`, and localized final status. The
diagnostic Beginner 4 and Advanced 32 searches completed their exact visit
budgets, and network recovery/seat cleanup passed.

The score-equivalent row and the current root-score/terminal semantics patch were
statically checked in this session; no new runtime PASS is claimed for them.

The copied sandbox license is present at `Temp/UnityLicense/Unity_lic.ulf`, but
both compile attempts stopped before project import because the LicensingClient
IPC channel failed (`return code 199`). This is an environment result, not a
source-code PASS/FAIL.

The public preset mapping is Beginner/8, Advanced/20, Master/48 and
Ultrahard/120. Earlier 4/32/128/384 measurements are historical and are not
substituted for current evidence.

This file is the single release-gate matrix. Historical evidence is useful, but
current changed code/schema requires current execution before a runtime gate can
be called PASS.

## Status labels

- `PASS_CURRENT` — executed successfully on the current production code/schema.
- `PASS_HISTORICAL` — valid older evidence exists, but relevant code/schema has
  changed since that run.
- `IMPLEMENTED` — static code path exists; runtime behavior not proven here.
- `FAIL` — current executable gate ran and failed.
- `OPEN` — required evidence does not exist yet.
- `NOT_EXECUTED` — deliberately not run in the current work session.
- `BLOCKED` — execution is prevented by a specific environment/external blocker.
- `ORACLE_NOT_PERFORMED_CURRENT` — a historical external oracle artifact exists,
  but no fresh oracle/export pair was produced for the current commit.

Never translate `IMPLEMENTED`, `PASS_HISTORICAL`, `OPEN` or `NOT_EXECUTED` into
`PASS_CURRENT` in a summary.

## Release matrix

| gate | status now | required current evidence |
| --- | --- | --- |
| production scene regeneration | PASS_CURRENT | current generated scene: three visible tables, applied-profile/force-reset bindings, grounded marker heights |
| production validator | PASS_CURRENT | current `failures=0`, one oracle-location warning |
| UdonSharp compile/generated programs | PASS_CURRENT | current Unity reference run compiled the changed runtime/probe with no C# compiler errors |
| basic rules / 361 legal matrix | PASS_CURRENT | 160-action trace + 160-position legal matrix |
| randomized rule differential | PASS_CURRENT | current deterministic trace; external oracle comparison not performed |
| long-history rule stress | PASS_CURRENT | 0/32/64/128/256/520 plies, fixed diagnostic 32 visits each, unchanged 90 s/case timeout |
| cooperative superko | PASS_CURRENT | 361 points, mismatches=0, cancellation/revision gate pass |
| INPUTSVERSION 7 differential (internal current) | PASS_CURRENT | 16/16 current feature cases after ladder cache changes |
| KataGo V7 external feature oracle (current commit) | PASS_CURRENT | fresh current 16-case export compared with local KataGo V7 encoder, tolerance 1e-6, failures=0 |
| neural numerical equivalence | PASS_CURRENT | fresh current 4-case/32-visit Udon export versus baked CPU graph, 127 tensors exact, 5 heads/case, atol=1e-4/rtol=1e-5 |
| full KataGo search-distribution parity | OPEN | current Udon default intentionally omits KataGo dynamic cpuct/FPU/score-center terms; isolated ablation bits are diagnostic only |
| same-model strength regression screen | PARTIAL | current v4/v32 are complete; current 100 has two verified positions, not a 32-position release gate; not Elo |
| single packed readback shape/decoding | PASS_CURRENT | current neural export/readback path, stages=1 |
| GPU graph frame slicing | PASS_CURRENT | 66 passes, stageMask=1022, bounded graph frames |
| match-start GPU pipeline/readback warm-up | PASS_CURRENT | production scene: 66 dummy graph passes, 21 warm-up frames, one packed readback before the first real search |
| reader A/B/C quarantine/recovery | PASS_CURRENT | minimal fixture: 1 game/controller/runtime, 3 readers, no BoardPool/BoardCell/Canvas; late callback/reuse/fresh 4 visits |
| stale result / owner lifecycle | PASS_CURRENT | stale cancellation + simulated owner transfer/reader quarantine |
| GoAiSettings one-domain Apply | PASS_CURRENT | current generated UI/ClientSim run |
| active search survives Apply | PASS_CURRENT | current token/target/last completed target gate |
| NEXT real search consumes new config | PASS_CURRENT | next target=32 in lifecycle gate |
| remote config deserialization during search | NOT_EXECUTED | remapped Master/48 target requires a fresh lifecycle run |
| deterministic complete Custom profile | PASS_CURRENT | all complete profile fields and Custom visits gate |
| Custom late join equality | OPEN | compare complete resolved profiles on fresh client |
| Custom owner-transfer equality | OPEN | new owner resolves exact same complete profile |
| PvP seats/permissions | PASS_CURRENT | current generated UI/permission gate: remote spectator endpoints denied and all three controls disabled |
| Force Reset policy | IMPLEMENTED | UI double-click confirmation is four seconds; live permission is owner/current human/AI authority or synchronized 50-second stall timeout; finished/lobby tables are public; dedicated timer/double-click exercise remains static-only |
| current-turn Undo + opposite acceptance | PASS_CURRENT | transactional Undo/Draw gate |
| Draw lifecycle/corruption | PASS_CURRENT | current Undo/Draw gate |
| UI three-area structure/progress | NOT_EXECUTED | fixed eleven-line ABI plus score-equivalent row is statically checked; rerun the UI/Hint gate after scene regeneration |
| direct `Ultrahard` label | PASS_CURRENT | regenerated current UI/validator, 120 visits |
| hover delta-only presentation | PASS_CURRENT | 361 sweep full-board refresh delta=0; grounded hover/ko marker plane validated by generator/validator |
| hover legality cache | PASS_CURRENT | current hit/miss/quota profile |
| actual VRChat board `Use`/Interact behavior | OPEN | desktop + VR client check |
| fixed 3 visible tables | PASS_CURRENT | regenerated scene + runtime visibility |
| lazy heavy table allocation | PASS_CURRENT | current memory/runtime gate |
| real peak memory | OPEN | Unity/process and/or client measurement |
| 1/2/3-table scheduler fairness | PASS_CURRENT | grants/gaps captured for 1, 2 and 3 tables |
| 1/2/3-table p95/p99/max | PASS_CURRENT | ClientSim frame tails recorded; not clean VRChat timing |
| public 8/20/48/120 budgets | NOT_EXECUTED | public preset constants and generated labels were changed after the last runtime run; rerun the ladder before calling this current |
| A-H 4/32 performance corpus | PASS_CURRENT | 16 fixed samples, fixture hashes stable |
| complex-position 4-visit speedup | PASS_CURRENT | current dense/long/superko/ladder stage data recorded |
| search calibration | PASS_HISTORICAL | last minimal-fixture 16-sample run used the former public ladder 4/32/100/200; rerun after the 8/20/48/120 change |
| complete-game lifecycle | PASS_CURRENT | 372-move AI lifecycle smoke |
| two real VRChat clients | OPEN | seats/settings/AI/ownership/undo/draw/transfer |
| late join/reconnect | OPEN | real transport reconstruction |
| clean PC/PCVR frame time | OPEN | real client profiler, not ClientSim |
| Quest/Android | OPEN | actual platform/backend evidence |
| model redistribution authorization | OPEN | explicit model grant/source/license evidence |
| Elo/human-rank calibration | OPEN | reproducible strength tournament/calibration |

## P0 status — Custom deterministic resolution

The static resolver applies the complete shared preset before overriding only
`maxVisits/maxNNQueries`. The current lifecycle gate proves local Apply,
normal completion of the captured search, next-search capture, and compiled
Udon `_onDeserialization` behavior for the remote snapshot.

Required fix and test:

```text
base = complete parameters(sharedPreset)
Custom = base with maxVisits/maxNNQueries = sideCustomVisits
```

Then initialize two profile views with deliberately different local values,
apply the same synchronized settings, and assert equality of:

```text
maxVisits
maxNNQueries
maxTransitionsPerFrame
cpuct
moveTemperature
policyTopK
resignThreshold
```

Repeat across deserialization/late-join/owner-recovery paths.

## Settings lifecycle completion gate

The current lifecycle verifier proves:

1. a search begins with the old profile;
2. a new profile is applied while it is active;
3. token/target/current complete profile remain unchanged;
4. old search completes;
5. the **next real search** begins and captures the new complete profile;
6. the same holds for remote deserialization;
7. a real position change still invalidates the old root;
8. ownership transfer quarantines old work.

## Reader recovery gate

The production-scene fixture remains too large for this adversarial test, so the
gate uses the minimal generated real ClientSim/readback fixture. It does not
weaken assertions or replace the pending request path with synthetic state.

Required lifecycle:

```text
request A -> WAITING
cancel A -> QUARANTINED
start/use another safe reader where available
observe A late callback -> stale only
A -> RECOVERED
new request may reuse A
```

## Hover gate

Run a repeatable sweep across all 361 intersections. Record full-board refresh
delta, cache hit/miss, legal-query cost, pointer-presentation cost and frame
tails. `fullBoardRefreshCount` must not rise due to ordinary pointer motion.

Then verify actual VRChat desktop/VR interaction and `Use` highlighting because
361 `Interact()` board endpoints remain.

## Performance gate

Use the exact A-H corpus in `PERFORMANCE.md` at 4 and 32 visits. The current
single-ClientSim run reports, with the same frozen fixture hashes:

- wall-time and ms/visit before/after;
- p95/p99/max frame time;
- stage costs;
- 1/2/3-table behavior;
- semantic regression results.

A timeout is FAIL/OPEN. Increasing a timeout is not a performance fix.

## Match-start pipeline warm-up gate

The current production-scene ClientSim run used the generated scene and the
real Udon path. The verifier observed `resourceWarmupComplete=True` and
`pipelineWarmupReadbackComplete=True` before the first search phase, then the
Beginner search completed 4/4 visits and committed a legal move. The warm-up
used a private negative token and its packed output was discarded; no MCTS
state or game revision was changed.

```text
warmupFrames=21 warmupPasses=66 warmupMs=880.863 warmupReadback=True
searchFrames=500 totalMs=1537.551 gpuEvalMs=151.277
gpuPasses=65 readbackRequests=4 completedVisits=4
```

## Udon search reference and semantic ablation

The current real ClientSim tape (4 visits, position 0) is consumed by
`Tools/KaTrain/udon_search_reference.py`. Legacy mode and the isolated FPU,
dynamic-cpuct, recent-score-center and rule-aware-no-result bits each reproduce
the Udon root edge set, value sums and deterministic winner
(`referenceMatch=true`). These are runtime/reference-consistency gates, not a
claim that the compact Udon tree is the full KataGo search.

The deterministic-root benchmark now reports sampled and deterministic moves
separately. With the complete current 4/32 traces, deterministic same-budget
Top-1 is 20/32 (62.5%) and 25/32 (78.1%); sampled Top-1 is 18/32 (56.2%) and
13/32 (40.6%). The sampled metric remains available for product behaviour, but
it is not used as the default search-fidelity comparator.

## 100-visit professional endgame liveness

The former position-1 timeout was a verifier lifecycle bug: Master’s
`resignThreshold=-0.98` produced a legitimate `STATE_RESIGNED` action without
incrementing `moveCount`, while the probe waited only for a new move. The probe
now records `aiResigned=true` and the searched best move. Position
`go-seigen-1973-07-01` passes at 100/100 visits in 12.63 seconds after this
fix. The complete 32-position 100-visit corpus still requires a fresh bounded
run; no timeout is being hidden or lengthened.

## Release rule

`READY_SINGLE_CLIENTSIM` is satisfied by the current single-Unity/ClientSim
matrix above. The remaining non-blocking items are `FUTURE_MANUAL`: real
multi-client transport/late join, clean PC/PCVR frame timing, Quest/Android,
model redistribution authorization, and KataGo strength/numeric oracle
comparison when the external oracle is unavailable.

For the working-tree scheduler hardening, this release label remains a
pre-patch checkpoint until Unity regenerates the affected Udon program assets
and reruns the same matrix. It must not be copied into a final release report
as a post-patch PASS without that rebuild.
