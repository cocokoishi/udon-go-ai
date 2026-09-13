# Performance implementation and measurement contract

The original user-visible problem is concrete: complex positions made even very
small visit budgets feel too slow. The implemented path reduces real end-to-end
ClientSim work while preserving rules, V7 features, neural outputs and search
semantics. Exact current-run evidence is appended after the final gate run.

Use one Unity/ClientSim VM for all current profiling. Real VRChat/PCVR timing is
future manual validation, not current DoD.

## 1. Keep already-correct architecture

Do not revert:

- one packed async readback per neural evaluation;
- cooperative GPU graph state machine;
- lazy MCTS/feature/GPU heavy allocation;
- compact synchronized history with local rebuild;
- exact move-mask cache;
- delta-only hover presentation;
- MCTS root history copied once rather than full history arrays every visit;
- generation-stamped search-state group/liberty scratch;
- deferred/stamped feature scratch clearing.

## 2. Required optimization order

Follow this order so improvements are attributable.

### 2.1 Authoritative group/liberty scratch — implemented

Port `GoSearchState` generation stamps to `GoGame`. This directly reduces
`IsLegalMove`, cache warm-up and authoritative move cost.

### 2.2 Room-wide move-mask warm-up — implemented

Move-mask scheduling is centralized in one round-robin `GoBoardPool` budget.
Each admitted table advances the exact cache through
`StepMoveMaskCacheTimeSliced`: a small 0.25–0.50 ms CPU slice (0.35 ms by
default) with the existing 24-point safety cap. The cap is only a
resolution-independent guard; the
deadline is the pacing authority, so a post-AI revision cannot create a burst
of 24 expensive legality probes on the commit frame. The standalone
`GoBoardView` fallback uses the same time-sliced entry point.

### 2.3 Live BoardPool GPU scheduling — implemented

Use active runtimes' `smoothedGpuSubmitMilliseconds` plus a pool-local recent EMA
for dispatch-capacity cost. Do not use completed-search visit statistics as the
primary active cost estimate.

### 2.4 Exact history buckets — implemented

Add stone-count -> history-entry buckets in `GoSearchState`; measure
`historyEntriesExamined`. Keep four-lane exact comparison.

### 2.5 Ladder delta journal — implemented

Initialize one working board per encoded source plane and restore only points
mutated during each ladder attempt. Full V7 oracle parity is mandatory.

### 2.6 Reader fixture size — implemented

Keep reader runtime architecture. Move A/B/C recovery to a minimal one-table
ClientSim fixture so serialization OOM is no longer part of the test workload.
This is reliability/testability work, not an inference speed claim.

### 2.7 Input upload only if measured

Split input upload timing into:

```text
CPU float -> Color packing
spatial SetPixels + Apply
global SetPixels + Apply
input Blit submit
```

Optimize the upload path only if it is >=10% of 8-visit wall time or one of the
top three measured stages after the earlier phases. Preferred change: write the
persistent upload representation during feature finalization so the same values
are not copied into another intermediate representation.

### 2.8 Deeper MCTS only if measured

After the above:

- change root simulation reset only if >=10% of visit CPU time;
- optimize child scanning only if it is top-three CPU cost;
- do not approximate score utility.

## 3. Fixed A-H ClientSim corpus

`GoPerformanceCorpusProbe` and its ClientSim verifier use deterministic existing fixture material.
Do not invent new arbitrary boards when tested sequences already exist.

| ID | class | source fixture |
| --- | --- | --- |
| A | empty | reset/canonical empty board |
| B | ordinary opening | current `GoSearchCalibrationProbe` opening sequence |
| C | tactical midgame | current search-calibration fight sequence (`scenario2Moves`) |
| D | dense/high-group-count | frozen 128-ply legal move log (`fixtureDDense`) |
| E | long history | frozen 256-ply legal move log (`fixtureELong`) |
| F | ko/superko | current `GoSuperkoCooperativeProbe` history fixture |
| G | ladder-heavy | `GoFeatureProbe` ladder fixture |
| H | dense late/endgame-like | frozen 520-ply legal move log (`fixtureHEndgame`) |

Run every fixture with exactly:

```text
8 visits
48 visits
```

20/120 remain public-budget correctness gates; they do not need to be run for
every expensive corpus fixture.

## 4. Per-sample metrics

Reset counters before every sample and emit:

```text
fixture id
visit target/completed visits
wall seconds
frames
ms/visit
controller max tick ms
 move-mask points/frames/cache hits/cache misses/max frame ms,
 suppressed-critical frames (AI/search budget protection)
superko points/frames/ms/max frame ms
history entries examined
feature encode steps/ms
ladder transitions/nodes/ms
MCTS selection ms
MCTS expansion ms
MCTS backup ms
MCTS root-copy/reset ms
score utility ms
input packing ms
spatial SetPixels/Apply ms
global SetPixels/Apply ms
input Blit ms
GPU passes/eval
graph frames/eval
max/smoothed GPU submit ms
readback requests/eval
readback latency
UI refresh count/max ms
 frame p50/p95/p99/max
 first four visit completion frames, first4/steady-state ms-per-visit
 ```

The first production encode uses the encoder-owned `spatialOutput`, which is
zero-initialized by the runtime. It therefore enters the BASE stage with
`firstSpatialClearCount=0` and `firstSpatialClearSlices=0`; subsequent encodes
clear only the tracked writes. External caller-provided buffers retain the
conservative full-clear path. `GoClientSimFeatureVerifier` treats a non-zero
first clear as a regression while still checking the complete V7 fixture output.

The Udon probe copies the raw frame ring into a linear sample buffer. Percentile
sorting is performed by the Editor verifier, so the measurement probe itself
does not add an unmeasured quadratic sorting spike at sample completion. Each
fixture also emits a sequence hash; the 4-visit and 32-visit samples must match
before an A-H result is accepted. The current product-aligned pair is 8/48;
older 4/32 measurements below are retained as historical evidence only.

All listed metrics are exposed as local non-synchronized counters. CPU selection,
expansion, backup and score work are measured at their real cooperative calls;
frame percentiles come from the pool ring buffer while AI search is active.

## 4.1 LongHistory stage evidence

The dedicated case-64 profiler ran with the unchanged 90-second hard timeout.
Representative current output (r18) was:

```text
targetPlies=64 completedPlies=64
liveSearchPhase=RUNNING liveVisits=24/32
liveFeatureMs=53638.5 liveLadderMs=53150.87
liveSuperkoMs=244.42 liveGpuUploadMs=155.69
liveGpuDispatchMs=324.37 liveReadbackMs=2087.09 liveMctsMs=176.09
```

This is a historical pre-budget sample from the ladder refactor. It is retained
as evidence of the original hotspot, not as the current status. The current
64-ply/32-visit run completes on the real GPU path; the 90-second timeout remains
a hard failure if it is ever exceeded. Exact ladder marker/working caches and
mutation-journal paths preserve the V7 feature gate.

## 4.2 GPU graph budget measurement (2026-09-07)

The operation-level fusion gate now verifies the unchanged float graph at every
checkpoint before enabling production fusion. Reference/fused passes are 84/66
with ownership (21.4% fewer submissions); the maximum observed checkpoint error
across the 16 V7 fixtures is 2.10e-05 under atol=1e-4, rtol=1e-5. Search and A-H
results must still be measured with fusion enabled before claiming an end-to-end
speedup.

The graph scheduler was changed without changing graph operations, shader
weights, precision, features or visit budgets:

```text
maxGpuSubmitMillisecondsPerFrame: 1.50 -> 3.00
pass quota cap: 12 -> 128 safety bound (deadline is governing)
frame-fraction clamp: removed from the runtime; BoardPool owns room admission
and divides one room budget across admitted dispatches
```

Measured on the same Unity/ClientSim GPU setup with `-nographics` omitted:

| sample | before | after |
| --- | ---: | ---: |
| 4-visit mean wall time | 4.262 s | 4.201 s |
| 32-visit mean wall time | 19.026 s | 18.353 s |
| actual/tree visits | 4/32 | 4/32 |

The GPU graph gate after the change reported `84` passes across `33` graph
frames, `maxPassesOneFrame=7`, `maxSubmitMs=2.405`, and stage mask `1022`.
Neural output export and the 64-ply/32-visit long-history gate both passed.
These are ClientSim measurements, not clean standalone VRChat frame timing.

## 4.2.1 Match-start GPU pipeline warm-up (2026-09-09)

Allocation-only warm-up was insufficient because the first real `VRCGraphics.Blit`
and packed async readback still initialized the production shader/backend path.
The owner of an AI table now runs a bounded dummy evaluation after `RequestStartMatch`:
the same fused graph and ownership packing execute with zero inputs, followed by
one real packed readback. The negative warm-up token is never submitted to MCTS,
and the graph/readback result is discarded before the first real `BeginSearch`.
Resources remain allocated between visits and turns; ownership loss or explicit
table teardown cancels the warm-up and requires the new owner to repeat it.

Production-scene ClientSim evidence (same generated scene and 4-visit Beginner
path as the previous runtime gate):

```text
warmupFrames=21 warmupPasses=66 warmupMs=880.863 warmupReadback=True
resourceWarmupComplete=True pipelineWarmupReadbackComplete=True
searchFrames=500 searchTotalMs=1537.551 gpuEvalMs=151.277
gpuPasses=65 readbackRequests=4 completedVisits=4
```

The preceding allocation-only run on the same scene measured `gpuEvalMs=876.523`
and `totalMs=2471.100` for the same 4-visit search. This is a real ClientSim
before/after indication that cold graph/readback work moved out of the first
search. It is not a clean standalone VRChat frame-time claim, and driver-level
shader compilation may still vary by platform.

## 4.3 Fixed A-H corpus result (2026-09-07)

The corrected verifier dispatches `RunPerformanceCorpusProbe` once and runs the
same frozen move logs at both fixed diagnostic budgets (4 and 32 visits,
independent from the public presets). The real-GPU run passed all 16
samples with exact visits/tree nodes. Wall time and ladder-stage time were:

| fixture | 4 visits wall / ladder (ms) | 32 visits wall / ladder (ms) |
| --- | ---: | ---: |
| A empty | 5483 / 282 | 18212 / 1431 |
| B opening | 3076 / 301 | 18000 / 1332 |
| C tactical | 3036 / 300 | 17584 / 1416 |
| D dense | 3214 / 822 | 21348 / 4769 |
| E long | 3506 / 1074 | 25460 / 8021 |
| F superko | 3143 / 622 | 19584 / 3587 |
| G ladder | 2859 / 311 | 19774 / 1704 |
| H endgame-520 | 2840 / 376 | 19576 / 2237 |

Frame p95 remained bounded in this ClientSim run (4 visits: 24.19–57.43 ms;
32 visits: 21.77–29.91 ms). This corpus is evidence for correctness and stage
cost attribution; it is not a standalone VRChat-client frame-time claim.

## 5. 1/2/3-table concurrent profile

Extend `GoClientSimConcurrentPerformanceVerifier` with a 1-table entry in
addition to existing 2/3-table runs.

For every count record:

```text
localRunningSearches
localDispatchesPerFrame
schedulerStride/schedulerSlot for each table
runtime.smoothedGpuSubmitMilliseconds per active table
per-table wall time and completed visits
pool frame p50/p95/p99/max
allocated workers/edge capacity/estimated GPU bytes
```

The scheduler must remain fair: distinct slots when more than one table is
active, no permanent starvation, and no hidden table work.

## 6. Hover performance gate

The current code uses one generated `GoBoardInput` receiver and the same
`IsLegalMove`/`SetHoverLocation` path as the UGUI board receiver backed by one
non-blocking UIShape trigger collider.
The current single-ClientSim gate is deterministic:

- build all 361 exact move-mask results cooperatively;
- sweep 361 hover locations;
- assert `fullBoardRefreshCount` delta is zero;
- assert preview object identity is unchanged;
- assert legality parity;
- dirty three tables and prove the pool advances at most the configured
  room-wide warm-up points per frame.

Do not redesign physical VR input in this task.

The 2026-09-08 production-scene run passed after allowing the generated
two-/thirty-frame startup recovery callbacks to drain before taking the strict
baseline:

```text
PURE_UDON_GO_CLIENTSIM_HOVER_PASS
warmupPoints=361 warmupFrames=17 fullRefreshDelta=0 poolMaxPoints=24
```

The settle window is only a verifier baseline barrier; any full-board refresh
during the actual 361-event sweep remains a hard failure. Desktop/VR laser
raycast behavior is still `FUTURE_MANUAL`.

## 6.1 Semantic equivalence checkpoint (2026-09-08)

The current production scene was compared with the pre-fusion checkpoint
`3a6acad` rather than judged from elapsed time alone:

- UdonSharp: `programs=72 compiled=72 missingSource=0 missingSerialized=0
  compilerError=False`.
- Production Validator: `failures=0 warnings=1` (the warning is the known
  external raw-KataGo oracle availability warning).
- GPU reference versus fused graph: 16 fixtures × 17 checkpoints, `84 -> 66`
  passes, maximum absolute error `2.09808349609375e-05`, `atol=1e-4`,
  `rtol=1e-5`.
- SearchCalibration semantic arrays are unchanged: selected moves, policy
  moves, actual/target visits, tree nodes/edges, reader stages, ownership
  readbacks and root revision/search-token arrays all compare equal. Root value
  maximum absolute difference is `1.19209289550781e-07`.
- Neural output export semantic arrays are unchanged: selected moves, visits,
  revisions, tokens, reader stages, ownership readbacks and scenario move
  counts compare equal. Maximum absolute differences are policy `9.54e-6`,
  value `9.54e-7`, score `1.79e-7`, ownership `7.15e-7`.

These are internal reference/fused and pre/post ClientSim comparisons. The
neural export explicitly remains `numericComparison=NOT_PERFORMED_BY_THIS_VERIFIER`;
no bundled KataGo oracle differential is claimed by this checkpoint.

## 7. Before/after discipline

For each material phase:

1. run the exact relevant before fixture;
2. make only the coherent change for that phase;
3. run correctness/semantic gates;
4. rerun the exact same fixture;
5. record delta in wall time, frame tail, stage cost and memory/accounting;
6. revert or fix any semantic regression.

Do not stack multiple performance changes and then guess which one helped.

## 8. Success criterion

Section 2 is complete in the available environment when:

- A-H 8/48 corpus exists with before/after data;
- dense, long-history, superko and ladder-heavy 8-visit cases improve materially;
- 1/2/3-table frame tails and fairness are measured;
- rules/V7/neural/search calibration remain valid;
- no improvement comes from lowering semantic work.

The final report must name the top three measured bottlenecks before and after
the refactor. `ClientSim` results should be labelled ClientSim, not clean VRChat
client timing.

Network byte reports are likewise domain-specific: GoGame int-array lower bound,
GoAiSettings, GoTelemetry raw estimate/actual `OnPostSerialization.byteCount`,
and GoBoardPool are reported separately. `EstimatedGoGameIntArrayBytes` is not
presented as a complete table snapshot size.

## 9. Per-table AI cold-start preparation (2026-09-08)

The Go runtime now follows the useful part of the Xiangqi cold-start pattern
without changing the inference graph or search semantics. When a match with an
AI side is started, the authoritative owner of that table requests a four-step
local preparation sequence:

1. MCTS node/edge and score-utility backing storage;
2. feature encoder neighbour/ladder storage;
3. persistent neural input/output textures and fused-graph scratch textures;
4. the packed readback staging resource.

No shader dispatch, search token, root revision, feature value, visit budget or
reader request is created by this preparation. `GoAiController` gates the work
by the table's `GoGame` ownership and `HasAnyAI()`, so a PvP table and the two
other inactive tables do not allocate or execute this work. The first real AI
search reuses the prepared storage instead of paying the cold allocation path.

This change has a fresh UdonSharp compile pass, but no new frame-time or search
wall-time claim is made until a runtime corpus is run.

## 10. Admitted-frame pump checkpoint (2026-09-09)

The controller now receives one scheduler admission deadline and pumps all
CPU-only transitions against that deadline. BeginSearch, superko preparation,
feature encoding, graph upload/dispatch, expansion, backup and the next
selection are no longer separate mandatory rendered-frame boundaries. Encoder
and MCTS expansion expose deadline-aware entry points so a helper cannot mint a
fresh minimum slice after the controller deadline. A real
async readback wait, stale identity, error, scheduler denial or deadline is
still allowed to yield. Difficulty `maxTransitionsPerFrame` remains a
compatibility field but is no longer the primary throughput cap.

The authoritative GoGame move-mask warm-up is suppressed while the table is on
an AI-controlled turn or running a hint. The pending bit is retained so human
hover resumes lazily on the next human revision; `GetMoveMask` itself remains
exact and lazy. Pool diagnostics expose
`moveMaskWarmupMillisecondsThisFrame` and
`maxMoveMaskWarmupMillisecondsOneFrame` alongside the existing point/frame
counters.

GPU runtime scheduling now consumes the remaining admitted budget directly. It
does not recalculate the old 18% frame fraction, and the old 24-pass value is
only replaced by a large safety bound plus the real deadline. The quota follows
the estimate, with at most one atomic dispatch as a liveness escape when the
remainder is smaller than that estimate. Persistent graph-submit EMA is kept
across leaves, is exposed as the live scheduler signal, and does not include
input packing/SetPixels/Apply time; full slice elapsed time is reported
separately as `lastSubmitFrameMilliseconds`.

Fresh single-ClientSim evidence after this phase:

```text
UdonSharp compile: programs=73 compiled=73 missingSource=0 missingSerialized=0 compilerError=False
Modes: PASS, searchVisits=4, searchFrames=65, visitFrames=17,34,49,65,
       sameFrameTransitions=20, ownershipFrames=8, scoreMs=1.024246,
       featureMs=63.29727, gpuUploadMs=23.20004
Stale result: PASS, cancelledWithoutCommit=True, staleCallbacks=0
GPU graph: PASS, passes=66, stageMask=1022, graphFrames=3,
           maxPassesOneFrame=42, maxSubmitMs=4.593849
BoardPool: PASS, fixed visible=3, dual independent searches, fair slots 0/1
Production Validator: PASS failures=0 warnings=1 (external raw KataGo oracle)
```

The `maxPassesOneFrame=42` and the final commit-frame budget overshoot
(`usedMs=13.4182` against a granted 4 ms in the Modes run) are retained as
measured telemetry, not hidden. They are not a clean standalone VRChat frame
proof and remain follow-up profiling items.

The follow-up deadline hardening keeps the same search semantics while adding
deadline-aware `StepEncodeUntil`/`StepPendingExpansionUntil` calls, honoring a
sub-millisecond remainder instead of rounding it up, and preserving the live
graph-only submit EMA across evaluations. The generated scene must be
regenerated before these source-level changes can be measured; no new Unity
performance PASS is claimed by this checkpoint.

The formal LongHistory stress was then rerun with its fixed diagnostic
32-visit budget (using the Advanced parameter base) and the unchanged
90-second case timeout. All six fixed cases
passed: 0/32/64/128/256/520 plies, each with 32 visits. Representative dense
case telemetry was:

```text
case 64:  searchMs=5185.112  searchFrames=1752  featureMs=177.8202
          ladderActiveCpuMs=949.5774  gpuUploadMs=191.6084
          gpuDispatchMs=28.82957  readbackMs=1409.059
          selectionMs=157.2685  expansionMs=90.44266  neuralPasses=65
case 256: searchMs=9426.777  searchFrames=3148  ladderActiveCpuMs=2290.596
case 520: searchMs=8124.638  searchFrames=2861  ladderActiveCpuMs=1788.128
```

These are ClientSim measurements. They prove completion and expose the real
remaining cost (ladder/feature CPU, readback latency and scheduler frame count)
but do not claim clean VRChat-client FPS.

## 11. Final current single-ClientSim corpus (2026-09-09)

The regenerated schema-14 scene was rerun after the admitted-frame and
per-dispatch budget changes. The fixed A-H fixture hashes were unchanged. All
16 samples passed with exact visits `[4 x 8, 32 x 8]`.

Representative current wall times (milliseconds) were:

```text
fixture       A       B       C       D       E       F       G       H
4 visits    1986.9   403.1   389.5   697.8  1007.5   561.3   397.8   792.7
32 visits   3347.4  2941.1  2850.4  5208.9  7701.0  4096.9  2850.9  5256.7
```

The 4-visit stage telemetry identified the largest costs as readback latency,
feature/ladder CPU on dense and long fixtures, and superko/history work on the
late endgame. The 32-visit samples show the same ordering; GPU graph dispatch
itself remained approximately 30–42 ms aggregate per sample with 65 graph
passes per evaluation. The corpus reports p50/p95/p99/max frame tails and all
stage counters in `Benchmarks/KaTrain/ClientSimExports/`.

Concurrent current runs completed four visits per table:

```text
tables  frame p95/p99/max (ms)  max dispatches  max scheduler gap  grants/table
1       46.32 / 77.51 / 350.36       1                 1               208
2       51.45 /102.57 / 254.80       2                 3               130/131
3       16.80 / 40.79 / 105.40       2                 5               125/126
```

These are ClientSim measurements, not clean standalone VRChat frame timing.
No visit budget, feature plane, rule check, ladder step or model operation was
removed to obtain them. The remaining top costs are ladder/feature CPU,
asynchronous readback latency and ClientSim/editor frame-tail variance.

AI move commits now defer the optional authoritative hover-mask warm-up for a
short four-frame grace window and combine final-analysis telemetry with the
post-move publication. This removes the commit-frame telemetry double-submit
and avoids a 24-point legality batch landing immediately on the AI move frame;
`GetMoveMask` remains exact and lazy for a real hover/click.

Performance status: `PARTIAL_STUTTER_REMAINS`. The admitted-frame refactor
removed repeated mandatory CPU phase barriers and preserves bounded/fair
dispatch, but first visits on dense/long positions can still show visible
frame-tail spikes. This is recorded as residual work, not called fully smooth.

## 12. Professional endgame search-consistency diagnostics (2026-09-10)

The current Udon probe now exports per-sample search, history-bucket, ladder,
GPU and readback counters, plus a live watchdog snapshot on failure. A 100-visit
run of `go-seigen-1973-07-01` initially appeared to exceed the hard timeout,
but telemetry showed `100/100` visits and no full-history scans. The actual
failure was the verifier waiting for `moveCount` after a legitimate
`resignThreshold=-0.98` AI action. After the lifecycle fix the same sample
completed as:

```text
actual/target visits       100/100
elapsed                    12.6289 s
search                     12.4272 s (11042 frames)
feature / ladder           4.1266 / 3.6002 s
score utility              0.0304 s
GPU evaluation / dispatch  1.4104 / 0.0866 s
readback                   3.6142 s (100 requests)
history queries            1096 (bucket hits 1000, misses 96, full scans 0)
ladder transitions/nodes   21780 / 6883
graph passes/frames        65 / 6
terminal                   STATE_RESIGNED
```

This is a liveness/verifier correction, not a claim that all 32 100-visit
professional positions have passed. The complete current 100-visit corpus
remains a required bounded gate.
